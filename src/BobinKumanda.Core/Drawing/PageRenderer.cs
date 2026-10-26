using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Drawing;

/// <summary>Kâğıt boyutu (yatay), milimetre.</summary>
public sealed record PageFormat(string Name, double WidthMm, double HeightMm)
{
    public static readonly PageFormat A4 = new("A4", 297, 210);
    public static readonly PageFormat A3 = new("A3", 420, 297);
}

/// <summary>
/// Yazdırma ve resim çıktısı için bir sayfa: çerçeve, ölçeklenmiş devre ve sağ altta öğrenci/çizim bilgilerinin
/// yazıldığı antet. Çizim milimetre biriminde yapılır; çağıran taraf milimetreyi piksele çevirir.
/// </summary>
public static class PageRenderer
{
    public const double Margin = 10;
    public const double TitleHeight = 24;

    /// <summary>Devre birimi başına en büyük ölçek (mm): küçük devreler kâğıtta dev görünmesin.</summary>
    public const double MaxScale = 0.9;

    public static void Draw(Circuit circuit, string sheet, ISymbolPainter page, PageFormat format, CircuitRenderOptions options,
        string? footer = null)
    {
        double w = format.WidthMm, h = format.HeightMm;
        Paint ink = Paint.Of(Ink.Normal);
        page.Rect(new Vec(0, 0), new Vec(w, h), Paint.Of(Ink.Background), 0, Paint.Of(Ink.Background));
        page.Rect(new Vec(Margin, Margin), new Vec(w - 2 * Margin, h - 2 * Margin), ink, 0.5);

        // Çizim alanı: çerçevenin içi, antetin üstü
        var area = new RectD(Margin + 4, Margin + 4, w - 2 * Margin - 8, h - 2 * Margin - 8 - TitleHeight - 2);
        RectD box = CircuitRenderer.Bounds(circuit, sheet).Inflate(4);
        double scale = Math.Min(MaxScale, Math.Min(area.Width / Math.Max(1, box.Width), area.Height / Math.Max(1, box.Height)));
        var offset = new Vec(area.X + (area.Width - box.Width * scale) / 2 - box.X * scale,
            area.Y + (area.Height - box.Height * scale) / 2 - box.Y * scale);
        CircuitRenderer.Draw(circuit, new ScaledPainter(page, scale, offset), options);

        int index = circuit.Sheets.FindIndex(s => s.Id == sheet);
        DrawTitleBlock(page, circuit, new RectD(w - Margin - 190, h - Margin - TitleHeight, 190, TitleHeight),
            index + 1, circuit.Sheets.Count, index >= 0 ? circuit.Sheets[index].Name : "", footer);
    }

    /// <summary>Antet: devre adı ve sayfa; öğrenci, numara, sınıf; okul, öğretmen, tarih.</summary>
    public static void DrawTitleBlock(ISymbolPainter p, Circuit circuit, RectD r, int sheetNo, int sheetCount, string sheetName, string? footer)
    {
        TitleBlock t = circuit.Title;
        Paint ink = Paint.Of(Ink.Normal), muted = Paint.Of(Ink.Muted);
        p.Rect(r.TopLeft, new Vec(r.Width, r.Height), ink, 0.5, Paint.Of(Ink.Background));
        double row = r.Height / 3;
        for (int i = 1; i < 3; i++)
        {
            p.Line(new Vec(r.X, r.Y + row * i), new Vec(r.Right, r.Y + row * i), ink, 0.3);
        }
        double c1 = r.X + 80, c2 = r.X + 135, wide = r.X + 150;
        p.Line(new Vec(wide, r.Y), new Vec(wide, r.Y + row), ink, 0.3);
        p.Line(new Vec(c1, r.Y + row), new Vec(c1, r.Bottom), ink, 0.3);
        p.Line(new Vec(c2, r.Y + row), new Vec(c2, r.Bottom), ink, 0.3);

        void Cell(double x, double y, string caption, string value)
        {
            p.Text(new Vec(x + 1.5, y + 2.2), caption, 2.1, muted, TextAlign.Left);
            p.Text(new Vec(x + 1.5, y + 6), value, 3.4, ink, TextAlign.Left, bold: true);
        }
        string name = t.CircuitName.Length > 0 ? t.CircuitName : sheetName;
        if (sheetName.Length > 0 && t.CircuitName.Length > 0)
        {
            name += " — " + sheetName;
        }
        Cell(r.X, r.Y, Loc.T("title.circuit"), name);
        Cell(wide, r.Y, Loc.T("title.sheet"), $"{sheetNo} / {sheetCount}");
        Cell(r.X, r.Y + row, Loc.T("title.student"), t.StudentName);
        Cell(c1, r.Y + row, Loc.T("title.number"), t.StudentNumber);
        Cell(c2, r.Y + row, Loc.T("title.class"), t.ClassName);
        Cell(r.X, r.Y + 2 * row, Loc.T("title.school"), t.School);
        Cell(c1, r.Y + 2 * row, Loc.T("title.teacher"), t.Teacher);
        Cell(c2, r.Y + 2 * row, Loc.T("title.date"), t.Date);
        if (footer != null)
        {
            p.Text(new Vec(r.Right, r.Bottom + 4), footer, 2, muted, TextAlign.Right);
        }
    }
}

/// <summary>Dünya koordinatlarını ölçekleyip kaydırarak başka bir çiziciye aktarır (sayfaya yerleştirme).</summary>
public sealed class ScaledPainter(ISymbolPainter inner, double scale, Vec offset) : ISymbolPainter
{
    private Vec M(Vec v) => offset + v * scale;

    public void Line(Vec a, Vec b, Paint ink, double width = 1, bool dashed = false) =>
        inner.Line(M(a), M(b), ink, width * scale, dashed);

    public void Polyline(IReadOnlyList<Vec> points, Paint ink, double width = 1, bool closed = false, Paint? fill = null, bool dashed = false)
    {
        var pts = new Vec[points.Count];
        for (int i = 0; i < pts.Length; i++)
        {
            pts[i] = M(points[i]);
        }
        inner.Polyline(pts, ink, width * scale, closed, fill, dashed);
    }

    public void Rect(Vec topLeft, Vec size, Paint ink, double width = 1, Paint? fill = null, bool dashed = false) =>
        inner.Rect(M(topLeft), size * scale, ink, width * scale, fill, dashed);

    public void Circle(Vec center, double radius, Paint ink, double width = 1, Paint? fill = null) =>
        inner.Circle(M(center), radius * scale, ink, width * scale, fill);

    public void Text(Vec anchor, string text, double size, Paint ink, TextAlign align = TextAlign.Center, bool bold = false) =>
        inner.Text(M(anchor), text, size * scale, ink, align, bold);
}
