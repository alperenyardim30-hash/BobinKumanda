using System.Globalization;
using System.Text;
using KumandaLab.Core;
using KumandaLab.Core.Catalog;
using KumandaLab.Core.Drawing;
using KumandaLab.Core.Model;

namespace KumandaLab.Core.Tests.Rendering;

/// <summary>Sembolleri SVG'ye çizer (önizleme ve görsel kontrol için).</summary>
internal sealed class SvgPainter : ISymbolPainter
{
    private readonly StringBuilder sb;
    private readonly Func<Vec, Vec> map;
    private readonly double scale;

    public SvgPainter(StringBuilder sb, ComponentDef def, ComponentInstance c, Vec offset, double scale)
    {
        this.sb = sb;
        this.scale = scale;
        map = p => (Placement.ToWorld(p, def.Size, c.Rotation, c.Mirror, c.Position) - c.Position) * scale + offset;
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Color(Paint p) => p.Ink switch
    {
        Ink.Custom => "#" + p.Rgb.ToString("X6"),
        Ink.Muted => "#8a8f98",
        Ink.Energized => "#d9480f",
        Ink.Fault => "#e03131",
        Ink.Accent => "#1c7ed6",
        Ink.Active => "#e8590c",
        Ink.Background => "#ffffff",
        _ => "#1f2430"
    };

    private static string Opacity(Paint p) => p.Alpha < 1 ? $" opacity=\"{F(p.Alpha)}\"" : "";

    private string Fill(Paint? fill) => fill is { } f ? $"fill=\"{Color(f)}\" fill-opacity=\"{F(f.Alpha)}\"" : "fill=\"none\"";

    private string Stroke(Paint ink, double width, bool dashed) =>
        width <= 0 ? "stroke=\"none\"" :
        $"stroke=\"{Color(ink)}\" stroke-opacity=\"{F(ink.Alpha)}\" stroke-width=\"{F(width * scale * 0.9)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"" +
        (dashed ? $" stroke-dasharray=\"{F(2 * scale)} {F(1.6 * scale)}\"" : "");

    public void Line(Vec a, Vec b, Paint ink, double width = 1, bool dashed = false)
    {
        Vec p = map(a), q = map(b);
        sb.Append($"<line x1=\"{F(p.X)}\" y1=\"{F(p.Y)}\" x2=\"{F(q.X)}\" y2=\"{F(q.Y)}\" {Stroke(ink, width, dashed)}/>");
    }

    public void Polyline(IReadOnlyList<Vec> points, Paint ink, double width = 1, bool closed = false, Paint? fill = null, bool dashed = false)
    {
        string pts = string.Join(" ", points.Select(v => { Vec m = map(v); return F(m.X) + "," + F(m.Y); }));
        string tag = closed ? "polygon" : "polyline";
        sb.Append($"<{tag} points=\"{pts}\" {Stroke(ink, width, dashed)} {Fill(fill)}/>");
    }

    public void Rect(Vec topLeft, Vec size, Paint ink, double width = 1, Paint? fill = null, bool dashed = false)
    {
        Vec a = map(topLeft), b = map(topLeft + size);
        sb.Append($"<rect x=\"{F(Math.Min(a.X, b.X))}\" y=\"{F(Math.Min(a.Y, b.Y))}\" width=\"{F(Math.Abs(a.X - b.X))}\" height=\"{F(Math.Abs(a.Y - b.Y))}\" {Stroke(ink, width, dashed)} {Fill(fill)}/>");
    }

    public void Circle(Vec center, double radius, Paint ink, double width = 1, Paint? fill = null)
    {
        Vec c = map(center);
        sb.Append($"<circle cx=\"{F(c.X)}\" cy=\"{F(c.Y)}\" r=\"{F(radius * scale)}\" {Stroke(ink, width, false)} {Fill(fill)}/>");
    }

    public void Text(Vec anchor, string text, double size, Paint ink, TextAlign align = TextAlign.Center, bool bold = false)
    {
        Vec a = map(anchor);
        string anchorAttr = align switch { TextAlign.Left => "start", TextAlign.Right => "end", _ => "middle" };
        sb.Append($"<text x=\"{F(a.X)}\" y=\"{F(a.Y + size * scale * 0.35)}\" font-size=\"{F(size * scale)}\" text-anchor=\"{anchorAttr}\" fill=\"{Color(ink)}\"{Opacity(ink)} font-family=\"Segoe UI, Arial, sans-serif\"{(bold ? " font-weight=\"700\"" : "")}>{System.Security.SecurityElement.Escape(text)}</text>");
    }
}
