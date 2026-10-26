using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KumandaLab.Core;
using KumandaLab.Core.Drawing;

namespace KumandaLab.App.Rendering;

/// <summary>
/// Çekirdekteki sembol çizimlerini WPF <see cref="DrawingContext"/>'e aktarır. Dünya koordinatlarında çizer; ölçek ve
/// kaydırmayı çağıran taraf dönüşüm olarak ekler. Kalem, fırça ve yazılar önbelleğe alınır (zayıf PC'ler için).
/// </summary>
public sealed class WpfPainter : ISymbolPainter
{
    private static readonly Typeface Regular = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Bold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private readonly Dictionary<(Color, double, bool), Pen> pens = [];
    private readonly Dictionary<Color, SolidColorBrush> brushes = [];
    private readonly Dictionary<(string, double, bool, Color), FormattedText> texts = [];

    private DrawingContext dc = null!;
    private double minWidth = 0.5;

    public CanvasTheme Theme { get; private set; } = CanvasTheme.Light;
    public double PixelsPerDip { get; set; } = 1;

    /// <summary>Her karede çağrılır. <paramref name="zoom"/>: ekran pikseli / dünya birimi; çizgiler en az ~1 piksel kalır.</summary>
    public void Begin(DrawingContext context, CanvasTheme theme, double zoom)
    {
        dc = context;
        if (!ReferenceEquals(theme, Theme))
        {
            Theme = theme;
            pens.Clear();
            brushes.Clear();
            texts.Clear();
        }
        double w = 0.9 / Math.Max(zoom, 0.01);
        if (Math.Abs(w - minWidth) > 1e-9)
        {
            minWidth = w;
            pens.Clear();
        }
        if (texts.Count > 4000)
        {
            texts.Clear();
        }
    }

    public SolidColorBrush Brush(Color c)
    {
        if (!brushes.TryGetValue(c, out SolidColorBrush? b))
        {
            b = new SolidColorBrush(c);
            b.Freeze();
            brushes[c] = b;
        }
        return b;
    }

    public SolidColorBrush Brush(Paint p) => Brush(Theme.Resolve(p));

    private Pen? PenFor(Paint ink, double width, bool dashed)
    {
        if (width <= 0)
        {
            return null;
        }
        Color c = Theme.Resolve(ink);
        double w = Math.Max(width * 0.9, minWidth);
        var key = (c, w, dashed);
        if (!pens.TryGetValue(key, out Pen? pen))
        {
            pen = new Pen(Brush(c), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (dashed)
            {
                pen.DashStyle = new DashStyle([2.2 / w, 1.8 / w], 0);
                pen.DashCap = PenLineCap.Flat;
            }
            pen.Freeze();
            pens[key] = pen;
        }
        return pen;
    }

    private static Point P(Vec v) => new(v.X, v.Y);

    public void Line(Vec a, Vec b, Paint ink, double width = 1, bool dashed = false)
    {
        if (PenFor(ink, width, dashed) is { } pen)
        {
            dc.DrawLine(pen, P(a), P(b));
        }
    }

    public void Polyline(IReadOnlyList<Vec> points, Paint ink, double width = 1, bool closed = false, Paint? fill = null, bool dashed = false)
    {
        if (points.Count < 2)
        {
            return;
        }
        var geo = new StreamGeometry();
        using (StreamGeometryContext g = geo.Open())
        {
            g.BeginFigure(P(points[0]), fill != null, closed);
            for (int i = 1; i < points.Count; i++)
            {
                g.LineTo(P(points[i]), true, true);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(fill is { } f ? Brush(f) : null, PenFor(ink, width, dashed), geo);
    }

    public void Rect(Vec topLeft, Vec size, Paint ink, double width = 1, Paint? fill = null, bool dashed = false) =>
        dc.DrawRectangle(fill is { } f ? Brush(f) : null, PenFor(ink, width, dashed), new Rect(topLeft.X, topLeft.Y, size.X, size.Y));

    public void Circle(Vec center, double radius, Paint ink, double width = 1, Paint? fill = null) =>
        dc.DrawEllipse(fill is { } f ? Brush(f) : null, PenFor(ink, width, false), P(center), radius, radius);

    public void Text(Vec anchor, string text, double size, Paint ink, TextAlign align = TextAlign.Center, bool bold = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        Color c = Theme.Resolve(ink);
        var key = (text, size, bold, c);
        if (!texts.TryGetValue(key, out FormattedText? ft))
        {
            ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? Bold : Regular,
                size, Brush(c), PixelsPerDip);
            texts[key] = ft;
        }
        double x = align switch
        {
            TextAlign.Left => anchor.X,
            TextAlign.Right => anchor.X - ft.WidthIncludingTrailingWhitespace,
            _ => anchor.X - ft.WidthIncludingTrailingWhitespace / 2
        };
        // Çekirdekteki çizimler yazının taban çizgisini anchor.Y + 0,35·boyut kabul eder
        dc.DrawText(ft, new Point(x, anchor.Y + size * 0.35 - ft.Baseline));
    }
}
