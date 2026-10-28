using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Components;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Drawing;

public sealed class CircuitRenderOptions
{
    public string Sheet { get; init; } = "s1";
    public SymbolStyle Style { get; init; } = SymbolStyle.School;
    public bool ShowTerminalNumbers { get; init; } = true;

    /// <summary>Kabloları bağlı oldukları besleme fazının rengiyle çiz (L1 kahverengi, L2 siyah, L3 gri, N mavi, PE yeşil).</summary>
    public bool ColorWires { get; init; } = true;

    /// <summary>Önceden hesaplanmış kablo renkleri (her karede netlist kurulmasın diye); null ise burada hesaplanır.</summary>
    public IReadOnlyDictionary<string, uint>? WireColorCache { get; init; }

    /// <summary>Bobinlerin altında kontak listesi (çapraz referans).</summary>
    public bool ShowCrossReference { get; init; } = true;

    /// <summary>Önceden hesaplanmış kontak numaraları ve çapraz referans; null ise burada hesaplanır.</summary>
    public (IReadOnlyDictionary<string, ContactNumber> Numbers, IReadOnlyDictionary<string, List<CrossRefEntry>> Mirror)? CrossRefCache { get; init; }

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
        IReadOnlyDictionary<string, uint> colors = !options.ColorWires ? new Dictionary<string, uint>() :
            options.WireColorCache ?? WireColors(circuit);
        var junctionInk = new Dictionary<string, Paint>(StringComparer.Ordinal);
        var xref = options.CrossRefCache ?? Compute(circuit);
        HashSet<(string, string)> crowded = options.ShowTerminalNumbers ? CrowdedTerminals(circuit, sheet) : [];

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
            DrawLabels(world, def, c, state, circuit);
            if (options.ShowTerminalNumbers)
            {
                DrawTerminalNumbers(world, def, c, xref.Numbers.TryGetValue(c.Id, out ContactNumber n) ? n : new ContactNumber(1, false), crowded);
            }
            if (options.ShowCrossReference && xref.Mirror.TryGetValue(c.Id, out List<CrossRefEntry>? entries))
            {
                DrawCrossReference(world, def, c, entries, state?.Text ?? def.SubLabel(c), circuit);
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
        c.Label.Length > 0 && !def.DrawsOwnLabel && !(def.DefaultLabel != null && c.Label == def.DefaultLabel);

    private static void DrawLabels(ISymbolPainter world, ComponentDef def, ComponentInstance c, SymbolState? state, Circuit circuit)
    {
        Paint ink = state is { Fault: true } ? Paint.Of(Ink.Fault) : Paint.Of(Ink.Normal);
        if (ShowsLabel(def, c))
        {
            // LabelWorld etiketin sol üst köşesidir; yazı dikey ortasından hizalanır
            world.Text(def.LabelWorld(c, circuit) + new Vec(0, LabelSize * 0.8), c.Label, LabelSize, ink, TextAlign.Left, bold: true);
        }
        string? sub = state?.Text ?? def.SubLabel(c);
        if (!string.IsNullOrEmpty(sub))
        {
            (Vec at, TextAlign align) = def.SubLabelPlace(c, circuit);
            world.Text(at, sub, 6, Paint.Of(Ink.Muted), align);
        }
    }

    public static (IReadOnlyDictionary<string, ContactNumber> Numbers, IReadOnlyDictionary<string, List<CrossRefEntry>> Mirror) Compute(Circuit circuit)
    {
        Dictionary<string, ContactNumber> numbers = CrossReference.Numbers(circuit);
        return (numbers, CrossReference.Mirror(circuit, numbers));
    }

    /// <summary>Bobinin altında, kontaklarının numaraları (üç tanesi bir satırda).</summary>
    private static void DrawCrossReference(ISymbolPainter world, ComponentDef def, ComponentInstance c, List<CrossRefEntry> entries, string? sub,
        Circuit circuit)
    {
        (Vec at, TextAlign align) = def.SubLabelPlace(c, circuit);
        double y = at.Y + (string.IsNullOrEmpty(sub) ? -0.5 : 6);
        for (int i = 0; i < entries.Count; i += 3)
        {
            string line = string.Join("  ", entries.Skip(i).Take(3).Select(e => e.Text));
            world.Text(new Vec(at.X, y), line, 4.5, Paint.Of(Ink.Accent), align);
            y += 6;
        }
    }

    /// <summary>Bu kadar yakın karşılıklı iki klemensin numaraları dışarı yazılırsa birbirine karışır ("14" + "11" → "1411").</summary>
    private const double CrowdedGap = 14;

    /// <summary>
    /// Başka bir elemanın klemensine bitişik ya da çok yakın duran klemensler (eleman, klemens). Bunların numarası
    /// dışarı değil, kendi elemanının tarafına yazılır.
    /// </summary>
    private static HashSet<(string, string)> CrowdedTerminals(Circuit circuit, string sheet)
    {
        var ends = new List<(string C, string T, Vec P, Side S)>();
        foreach (ComponentInstance c in circuit.Components)
        {
            if (c.Sheet == sheet && ComponentCatalog.Find(c.Type) is { ShowsTerminalNumbers: true } def)
            {
                foreach (TerminalDef t in def.Terminals)
                {
                    ends.Add((c.Id, t.Id, def.TerminalWorld(c, t), def.TerminalSide(c, t)));
                }
            }
        }
        var result = new HashSet<(string, string)>();
        foreach (var a in ends)
        {
            if (a.S is not (Side.Right or Side.Bottom))
            {
                continue;
            }
            Side facing = a.S == Side.Right ? Side.Left : Side.Top;
            foreach (var b in ends)
            {
                if (b.S != facing || b.C == a.C)
                {
                    continue;
                }
                // sağ uç → sağındaki elemanın sol ucu; alt uç → altındaki elemanın üst ucu
                (double along, double across) = a.S == Side.Right ? (b.P.X - a.P.X, b.P.Y - a.P.Y) : (b.P.Y - a.P.Y, b.P.X - a.P.X);
                if (Math.Abs(across) < 1 && along >= 0 && along < CrowdedGap)
                {
                    result.Add((a.C, a.T));
                    result.Add((b.C, b.T));
                }
            }
        }
        return result;
    }

    private static void DrawTerminalNumbers(ISymbolPainter world, ComponentDef def, ComponentInstance c, ContactNumber number,
        HashSet<(string, string)> crowded)
    {
        if (!def.ShowsTerminalNumbers)
        {
            return;
        }
        foreach (TerminalDef t in def.Terminals)
        {
            string? text = def.NumberFor(t, number);
            if (text == null)
            {
                continue;
            }
            Vec p = def.TerminalWorld(c, t);
            Side side = def.TerminalSide(c, t);
            // Numara sembolün dışına yazılır: yatay uçlarda gelen kablonun üstüne, dikey uçlarda kablonun yanına.
            // Komşu elemanın klemensi hemen yanındaysa numaralar karışmasın diye kendi tarafına (içeri) yazılır.
            bool inward = crowded.Contains((c.Id, t.Id));
            (Vec offset, TextAlign align) = (side, inward) switch
            {
                (Side.Left, false) => (new Vec(-1, -3.5), TextAlign.Right),
                (Side.Left, true) => (new Vec(1, -3.5), TextAlign.Left),
                (Side.Right, false) => (new Vec(1, -3.5), TextAlign.Left),
                (Side.Right, true) => (new Vec(-1, -3.5), TextAlign.Right),
                (Side.Top, false) => (new Vec(1.5, -4), TextAlign.Left),
                (Side.Top, true) => (new Vec(1.5, 4), TextAlign.Left),
                (Side.Bottom, false) => (new Vec(1.5, 4), TextAlign.Left),
                (Side.Bottom, true) => (new Vec(1.5, -4), TextAlign.Left),
                _ => (new Vec(1.5, -3.5), TextAlign.Left)
            };
            world.Text(p + offset, text, 4.5, Paint.Of(Ink.Muted), align);
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
                    Vec l = def.LabelWorld(c, circuit);
                    Add(new RectD(l.X, l.Y, c.Label.Length * LabelSize * 0.62, LabelSize * 1.4));
                }
                if (def.SubLabel(c) != null)
                {
                    (Vec at, TextAlign align) = def.SubLabelPlace(c, circuit);
                    Add(align == TextAlign.Left ? new RectD(at.X, at.Y - 4, 30, 10) : new RectD(r.X, r.Bottom, r.Width, 10));
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
                // yazının gerçek genişliği (8 birimlik yazıda harf başına ~4,4): sayfaya sığdırırken taşmasın
                string[] lines = t.Text.Replace("\r", "").Split('\n');
                double w = Math.Max(t.Size.X, lines.Max(l => l.Length) * 4.4), h = Math.Max(t.Size.Y, 11 * lines.Length + 2);
                Add(new RectD(t.Position.X, t.Position.Y, w, h));
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
