using System.Windows.Media;
using BobinKumanda.Core.Drawing;

namespace BobinKumanda.App.Rendering;

/// <summary>Çizim alanının renkleri. Koyu temada çok koyu renkler (siyah L2 kablosu) açık griye çekilir.</summary>
public sealed class CanvasTheme
{
    public static readonly CanvasTheme Light = new(
        background: 0xFFFFFF, grid: 0xD5D9E0, normal: 0x1F2430, muted: 0x8A8F98, energized: 0xD9480F,
        fault: 0xE03131, accent: 0x1C7ED6, active: 0xE8590C, flow: 0xF08C00, dark: false);

    public static readonly CanvasTheme Dark = new(
        background: 0x1E2127, grid: 0x3A3F48, normal: 0xE6E8EE, muted: 0x9AA0AA, energized: 0xFF8A4C,
        fault: 0xFF6B6B, accent: 0x4DABF7, active: 0xFF922B, flow: 0xFFD43B, dark: true);

    private readonly uint background, grid, normal, muted, energized, fault, accent, active, flow;

    private CanvasTheme(uint background, uint grid, uint normal, uint muted, uint energized, uint fault, uint accent,
        uint active, uint flow, bool dark)
    {
        this.background = background;
        this.grid = grid;
        this.normal = normal;
        this.muted = muted;
        this.energized = energized;
        this.fault = fault;
        this.accent = accent;
        this.active = active;
        this.flow = flow;
        IsDark = dark;
    }

    public bool IsDark { get; }

    public Color BackgroundColor => ToColor(background);
    public Color GridColor => ToColor(grid);
    public Color FlowColor => ToColor(flow);
    public Color AccentColor => ToColor(accent);

    public Color Resolve(Paint p)
    {
        uint rgb = p.Ink switch
        {
            Ink.Muted => muted,
            Ink.Energized => energized,
            Ink.Fault => fault,
            Ink.Accent => accent,
            Ink.Active => active,
            Ink.Background => background,
            Ink.Custom => Adapt(p.Rgb),
            _ => normal
        };
        Color c = ToColor(rgb);
        c.A = (byte)Math.Clamp(Math.Round(p.Alpha * 255), 0, 255);
        return c;
    }

    private uint Adapt(uint rgb)
    {
        if (!IsDark)
        {
            return rgb;
        }
        double r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
        double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
        return lum < 0.2 ? 0xC8CCD4 : rgb;
    }

    private static Color ToColor(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
