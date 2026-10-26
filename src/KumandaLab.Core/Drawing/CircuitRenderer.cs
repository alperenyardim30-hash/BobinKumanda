using KumandaLab.Core.Catalog;
using KumandaLab.Core.Components;
using KumandaLab.Core.Model;
using KumandaLab.Core.Simulation;

namespace KumandaLab.Core.Drawing;

public sealed class CircuitRenderOptions
{
    public string Sheet { get; init; } = "s1";
    public SymbolStyle Style { get; init; } = SymbolStyle.School;
    public bool ShowTerminalNumbers { get; init; } = true;

    /// <summary>Kabloları bağlı oldukları besleme fazının rengiyle çiz (L1 kahverengi, L2 siyah, L3 gri, N mavi, PE yeşil).</summary>
    public bool ColorWires { get; init; } = true;

    /// <summary>Simülasyondaki görünüm; düzenleme modunda null.</summary>
    public Func<ComponentInstance, SymbolState?>? StateOf { get; init; }

    /// <summary>Kablo rengini ezmek için (seçim, hatalı yol vurgusu). null dönerse normal renk kullanılır.</summary>
    public Func<Wire, Paint?>? WireInk { get; init; }

    public double AnimTime { get; init; }
}

/// <summary>
/// Bir sayfayı herhangi bir çiziciye (ekran, PNG, SVG, yazıcı) çizer. Çizici dünya koordinatlarında çalışır;
/// elemanlar <see cref="PlacedPainter"/> ile kendi yerel koordinatlarında çizilir.
/// </summary>
public static class CircuitRenderer
{
    public const double LabelSize = 7;
    public const double WireWidth = 1.2;
    public const double JunctionRadius = 2.2;

    public static void Draw(Circuit circuit, ISymbolPainter world, CircuitRenderOptions? options = null)
    {
        options ??= new CircuitRenderOptions();
        string sheet = options.Sheet;
        IReadOnlyDictionary<string, uint> colors = options.ColorWires ? WireColors(circuit) : new Dictionary<string, uint>();
        var junctionInk = new Dictionary<string, Paint>(StringComparer.Ordinal);

        foreach (Wire w in circuit.Wires)
        {
            if (w.Sheet != sheet || w.Points.Count < 2)
            {
                continue;
            }
            Paint ink = options.WireInk?.Invoke(w) ??
                        (colors.TryGetValue(w.Id, out uint rgb) ? Paint.Color(rgb) : Paint.Of(Ink.Normal));
            world.Polyline(w.Points, ink, WireWidth);
            foreach (WireEnd end in (ReadOnlySpan<WireEnd>)[w.A, w.B])
            {
                if (end.IsJunction)
                {
                    junctionInk[end.Junction!] = ink;
                }
            }
        }
        foreach (Junction j in circuit.Junctions)
        {
            if (j.Sheet == sheet)
            {
                Paint ink = junctionInk.GetValueOrDefault(j.Id, Paint.Of(Ink.Normal));
                world.Circle(j.Position, JunctionRadius, ink, 0.5, ink);
            }
        }

        foreach (ComponentInstance c in circuit.Components)
        {
            if (c.Sheet != sheet || ComponentCatalog.Find(c.Type) is not { } def)
            {
                continue;
            }
            SymbolState? state = options.StateOf?.Invoke(c);
            var ctx = new DrawContext
            {
                Instance = c,
                Def = def,
                State = state,
                Style = options.Style,
                ShowTerminalNumbers = options.ShowTerminalNumbers,
                AnimTime = options.AnimTime,
                Circuit = circuit
            };
            def.Draw(new PlacedPainter(world, def, c), ctx);
            DrawLabels(world, def, c, state);
            if (options.ShowTerminalNumbers)
            {
                DrawTerminalNumbers(world, def, c);
            }
        }

        foreach (TextNote t in circuit.Texts)
        {
            if (t.Sheet != sheet)
            {
                continue;
            }
            string[] lines = t.Text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                world.Text(t.Position + new Vec(0, 6 + 11 * i), lines[i], 8, Paint.Of(Ink.Normal), TextAlign.Left);
            }
        }
    }

    /// <summary>Etiket, elemanın sabit adını sembolün kendisi yazıyorsa (L1, N) gösterilmez.</summary>
    public static bool ShowsLabel(ComponentDef def, ComponentInstance c) =>
        c.Label.Length > 0 && !(def.DefaultLabel != null && c.Label == def.DefaultLabel);

    private static void DrawLabels(ISymbolPainter world, ComponentDef def, ComponentInstance c, SymbolState? state)
    {
        Paint ink = state is { Fault: true } ? Paint.Of(Ink.Fault) : Paint.Of(Ink.Normal);
        if (ShowsLabel(def, c))
        {
            // LabelWorld etiketin sol üst köşesidir; yazı dikey ortasından hizalanır
            world.Text(def.LabelWorld(c) + new Vec(0, LabelSize * 0.8), c.Label, LabelSize, ink, TextAlign.Left, bold: true);
        }
        string? sub = state?.Text ?? def.SubLabel(c);
        if (!string.IsNullOrEmpty(sub))
        {
            Vec size = def.WorldSize(c);
            world.Text(c.Position + new Vec(size.X / 2, size.Y + 6), sub, 6, Paint.Of(Ink.Muted));
        }
    }

    private static void DrawTerminalNumbers(ISymbolPainter world, ComponentDef def, ComponentInstance c)
    {
        if (def.Category is ComponentCategory.Supply or ComponentCategory.Systems or ComponentCategory.Motors)
        {
            return;
        }
        foreach (TerminalDef t in def.Terminals)
        {
            if (t.Number == null)
            {
                continue;
            }
            Vec p = def.TerminalWorld(c, t);
            Side side = def.TerminalSide(c, t);
            // Numara sembolün dışına yazılır: yatay uçlarda gelen kablonun üstüne, dikey uçlarda kablonun yanına
            (Vec offset, TextAlign align) = side switch
            {
                Side.Left => (new Vec(-1, -3.5), TextAlign.Right),
                Side.Right => (new Vec(1, -3.5), TextAlign.Left),
                Side.Top => (new Vec(1.5, -4), TextAlign.Left),
                Side.Bottom => (new Vec(1.5, 4), TextAlign.Left),
                _ => (new Vec(1.5, -3.5), TextAlign.Left)
            };
            world.Text(p + offset, t.Number, 4.5, Paint.Of(Ink.Muted), align);
        }
    }

    /// <summary>Sayfadaki her şeyi (etiketler dahil, yaklaşık) içine alan dikdörtgen.</summary>
    public static RectD Bounds(Circuit circuit, string sheet = "s1")
    {
        RectD? box = null;
        void Add(RectD r) => box = box is { } b ? b.Union(r) : r;
        foreach (ComponentInstance c in circuit.Components)
        {
            if (c.Sheet == sheet && ComponentCatalog.Find(c.Type) is { } def)
            {
                RectD r = def.Bounds(c);
                Add(r.Inflate(1));
                if (ShowsLabel(def, c))
                {
                    Vec l = def.LabelWorld(c);
                    Add(new RectD(l.X, l.Y, c.Label.Length * LabelSize * 0.62, LabelSize * 1.4));
                }
                if (def.SubLabel(c) != null)
                {
                    Add(new RectD(r.X, r.Bottom, r.Width, 10));
                }
            }
        }
        foreach (Wire w in circuit.Wires)
        {
            if (w.Sheet == sheet)
            {
                foreach (Vec p in w.Points)
                {
                    Add(new RectD(p.X - 1, p.Y - 1, 2, 2));
                }
            }
        }
        foreach (Junction j in circuit.Junctions)
        {
            if (j.Sheet == sheet)
            {
                Add(new RectD(j.Position.X - 3, j.Position.Y - 3, 6, 6));
            }
        }
        foreach (TextNote t in circuit.Texts)
        {
            if (t.Sheet == sheet)
            {
                Add(new RectD(t.Position.X, t.Position.Y, t.Size.X, t.Size.Y));
            }
        }
        return box ?? new RectD(0, 0, 100, 100);
    }

    private static readonly HashSet<string> PhaseNames = ["L1", "L2", "L3", "N", "PE"];

    /// <summary>Tek bir besleme fazına bağlı iletkenlerdeki kabloların standart renkleri. Kumanda kabloları listede yoktur.</summary>
    public static IReadOnlyDictionary<string, uint> WireColors(Circuit circuit)
    {
        var result = new Dictionary<string, uint>(StringComparer.Ordinal);
        Netlist netlist = Netlist.Build(circuit);
        var compById = circuit.Components.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (Net net in netlist.Nets)
        {
            if (net.Wires.Count == 0)
            {
                continue;
            }
            string? phase = null;
            bool mixed = false;
            foreach (NetPoint p in net.Points)
            {
                if (!p.IsTerminal || !compById.TryGetValue(p.Id, out ComponentInstance? c) ||
                    ComponentCatalog.Find(c.Type) is not { Category: ComponentCategory.Supply } || !PhaseNames.Contains(p.Sub))
                {
                    continue;
                }
                if (phase != null && phase != p.Sub)
                {
                    mixed = true;
                }
                phase = p.Sub;
            }
            if (phase == null || mixed)
            {
                continue;
            }
            uint rgb = SymbolKit.PhaseColor(phase);
            foreach (Wire w in net.Wires)
            {
                result[w.Id] = rgb;
            }
        }
        return result;
    }
}

/// <summary>Elemanın yerel koordinatlarını dünya koordinatına çevirerek çizer; yazılar dik kalır.</summary>
public sealed class PlacedPainter(ISymbolPainter world, ComponentDef def, ComponentInstance c) : ISymbolPainter
{
    private Vec Map(Vec p) => Placement.ToWorld(p, def.Size, c.Rotation, c.Mirror, c.Position);

    public void Line(Vec a, Vec b, Paint ink, double width = 1, bool dashed = false) =>
        world.Line(Map(a), Map(b), ink, width, dashed);

    public void Polyline(IReadOnlyList<Vec> points, Paint ink, double width = 1, bool closed = false, Paint? fill = null, bool dashed = false)
    {
        var mapped = new Vec[points.Count];
        for (int i = 0; i < mapped.Length; i++)
        {
            mapped[i] = Map(points[i]);
        }
        world.Polyline(mapped, ink, width, closed, fill, dashed);
    }

    public void Rect(Vec topLeft, Vec size, Paint ink, double width = 1, Paint? fill = null, bool dashed = false)
    {
        // 90°'lik döndürmelerde dikdörtgen yine eksenlere paraleldir
        Vec a = Map(topLeft), b = Map(topLeft + size);
        world.Rect(new Vec(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Vec(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)), ink, width, fill, dashed);
    }

    public void Circle(Vec center, double radius, Paint ink, double width = 1, Paint? fill = null) =>
        world.Circle(Map(center), radius, ink, width, fill);

    public void Text(Vec anchor, string text, double size, Paint ink, TextAlign align = TextAlign.Center, bool bold = false) =>
        world.Text(Map(anchor), text, size, ink, align, bold);
}
