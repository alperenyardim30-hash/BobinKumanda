using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Components;

/// <summary>
/// Hazır sistemler (eski EKTS'deki kapı ve asansör): içlerinde 3 fazlı bir motor ve konuma bağlı sınır anahtarları vardır.
/// Her sınır anahtarı 4 klemenslidir: (a,d) dinlenmede kapalı, (b,c) tetiklenince kapalı.
/// </summary>
public abstract class SystemDefBase : ComponentDef
{
    public override ComponentCategory Category => ComponentCategory.Systems;
    public override string LabelPrefix => "SYS";

    /// <summary>Klemens 0-2 motor (U,V,W); ardından her sınır anahtarı için 4 klemens.</summary>
    protected abstract (Vec Pos, Side Side)[] TerminalLayout { get; }

    /// <summary>Sınır anahtarının tetiklendiği konum (0..1 arası).</summary>
    protected abstract double[] SwitchPositions { get; }

    protected virtual double TravelTime => 6;

    public override IReadOnlyList<TerminalDef> Terminals
    {
        get
        {
            var layout = TerminalLayout;
            var list = new List<TerminalDef>();
            string[] motor = ["U", "V", "W"];
            for (int i = 0; i < layout.Length; i++)
            {
                string id = i < 3 ? motor[i] : $"S{(i - 3) / 4 + 1}{"abcd"[(i - 3) % 4]}";
                list.Add(new TerminalDef(id, layout[i].Pos, layout[i].Side, id));
            }
            return list;
        }
    }

    public override SimComponent CreateSim(ComponentInstance instance) => new SystemSim(SwitchPositions, TravelTime);

    protected static double Position(DrawContext ctx) => ctx.State?.Level ?? 0;
}

internal sealed class SystemSim(double[] switchPositions, double travelTime) : SimComponent
{
    private InductionMotorModel? model;
    private InductionMotorModel Model => model ??= new InductionMotorModel(this, 230, 0.37, 0.4);
    private double position;
    private readonly bool[] actuated = new bool[switchPositions.Length];

    public override int InternalNodeCount => 1;
    public override bool NeedsTicks => Model.NeedsTicks;
    public override bool IsInteractive => true;

    public override void Stamp(Stamper s)
    {
        Model.Stamp(s, [(T(0), Internal(0)), (T(1), Internal(0)), (T(2), Internal(0))]);
        for (int k = 0; k < switchPositions.Length; k++)
        {
            int b = 3 + 4 * k;
            s.Switch(T(b), T(b + 3), !actuated[k]);
            s.Switch(T(b + 1), T(b + 2), actuated[k]);
        }
    }

    public override bool Evaluate(Solution sol)
    {
        Model.Read(sol);
        return UpdateSwitches();
    }

    private bool UpdateSwitches()
    {
        bool changed = false;
        for (int k = 0; k < switchPositions.Length; k++)
        {
            bool on = Math.Abs(position - switchPositions[k]) < 0.015;
            if (on != actuated[k])
            {
                actuated[k] = on;
                changed = true;
                Sim.AddLog(L(on ? "log.systemSwitchOn" : "log.systemSwitchOff", Label, k + 1), Id);
            }
        }
        return changed;
    }

    public override bool Advance(double dt, Solution sol)
    {
        bool changed = Model.Advance(dt);
        position = Math.Clamp(position + Model.Speed * dt / travelTime, 0, 1);
        if (Math.Abs(Model.Speed) > 0.05 && (position <= 0 || position >= 1))
        {
            // uç noktada mekanik dayanma: motor zorlanır
            Sim.ReportLive(new SimProblem("system-end", ProblemSeverity.Warning,
                L("problem.systemEnd", Label), L("explain.systemEnd"), [Id], []));
        }
        return UpdateSwitches() | changed;
    }

    public override void Interact(Interaction action)
    {
        if (action.Kind == InteractionKind.SetValue)
        {
            position = Math.Clamp(action.Value, 0, 1);
            UpdateSwitches();
        }
    }

    public override void Check(Solution sol) => Model.Check(this);

    public override void UpdateVisual(Solution sol)
    {
        Model.UpdateVisual(Visual);
        Visual.Level = position;
        Visual.Closed = actuated.ToArray();
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L("readout.position"), string.Format(Loc.Culture, "%{0:0}", position * 100));
        foreach (Readout r in Model.Readouts())
        {
            yield return r;
        }
    }
}

/// <summary>Sürgülü kapı (geniş): motor altta ortada, sol ve sağda sınır anahtarları.</summary>
public sealed class Door1Def : SystemDefBase
{
    public override string TypeId => "system-door1";
    public override int? LegacyType => 24;
    public override string Tags => "kapı sistemi sürgülü kapı garaj door";
    public override Vec Size => new(280, 100);
    public override Vec DefaultLabelOffset => new(0, -12);

    protected override (Vec, Side)[] TerminalLayout =>
    [
        (new Vec(130, 100), Side.Bottom), (new Vec(140, 100), Side.Bottom), (new Vec(150, 100), Side.Bottom),
        (new Vec(0, 60), Side.Left), (new Vec(0, 65), Side.Left), (new Vec(0, 85), Side.Left), (new Vec(0, 90), Side.Left),
        (new Vec(280, 60), Side.Right), (new Vec(280, 65), Side.Right), (new Vec(280, 85), Side.Right), (new Vec(280, 90), Side.Right)
    ];

    protected override double[] SwitchPositions => [0, 1];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        p.Rect(new Vec(0, 0), new Vec(280, 100), ink, 1, Paint.Of(Ink.Background));
        p.Line(new Vec(20, 40), new Vec(260, 40), ink, 1.5);
        double x = 20 + 120 * Position(ctx);
        p.Rect(new Vec(x, 10), new Vec(120, 30), ink, 1, Paint.Of(Ink.Accent, 0.25));
        p.Text(new Vec(x + 60, 28), Loc.T("sys.door"), 8, ink);
        DoorSwitchTerminals(p, ctx, 0, 60, true);
        DoorSwitchTerminals(p, ctx, 280, 60, false);
        p.Circle(new Vec(140, 78), 9, ink, 1, ctx.State?.Active == true ? Paint.Of(Ink.Active, 0.25) : null);
        p.Text(new Vec(140, 80), "M", 7, ink, bold: true);
        p.Line(new Vec(130, 100), new Vec(134, 85), ink);
        p.Line(new Vec(140, 100), new Vec(140, 87), ink);
        p.Line(new Vec(150, 100), new Vec(146, 85), ink);
    }

    internal static void DoorSwitchTerminals(ISymbolPainter p, DrawContext ctx, double x, double y, bool left)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        double d = left ? 1 : -1;
        p.Line(new Vec(x, y), new Vec(x + 10 * d, y), ink);
        p.Line(new Vec(x, y + 30), new Vec(x + 10 * d, y + 30), ink);
        p.Line(new Vec(x + 10 * d, y), new Vec(x + 10 * d, y + 30), ink, 0.8, dashed: true);
        p.Line(new Vec(x, y + 5), new Vec(x + 6 * d, y + 5), ink);
        p.Line(new Vec(x, y + 25), new Vec(x + 6 * d, y + 25), ink);
        p.Text(new Vec(x + 16 * d, y + 18), left ? "S1" : "S2", 6, ink);
    }
}

/// <summary>Kapı sistemi 2 (kompakt).</summary>
public sealed class Door2Def : SystemDefBase
{
    public override string TypeId => "system-door2";
    public override int? LegacyType => 25;
    public override string Tags => "kapı sistemi kompakt door";
    public override Vec Size => new(100, 100);
    public override Vec DefaultLabelOffset => new(102, 40);

    protected override (Vec, Side)[] TerminalLayout =>
    [
        (new Vec(10, 0), Side.Top), (new Vec(20, 0), Side.Top), (new Vec(30, 0), Side.Top),
        (new Vec(0, 70), Side.Left), (new Vec(0, 75), Side.Left), (new Vec(0, 95), Side.Left), (new Vec(0, 100), Side.Left),
        (new Vec(70, 0), Side.Top), (new Vec(75, 0), Side.Top), (new Vec(95, 0), Side.Top), (new Vec(100, 0), Side.Top)
    ];

    protected override double[] SwitchPositions => [0, 1];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        p.Rect(new Vec(0, 0), new Vec(100, 100), ink, 1, Paint.Of(Ink.Background));
        double h = 50 * Position(ctx);
        p.Rect(new Vec(30, 20 + h), new Vec(40, 50 - h), ink, 1, Paint.Of(Ink.Accent, 0.25));
        p.Text(new Vec(50, 90), Loc.T("sys.door"), 7, ink);
        p.Circle(new Vec(20, 14), 7, ink, 1, ctx.State?.Active == true ? Paint.Of(Ink.Active, 0.25) : null);
        p.Text(new Vec(20, 16), "M", 6, ink, bold: true);
        p.Text(new Vec(10, 85), "S1", 6, ink);
        p.Text(new Vec(85, 12), "S2", 6, ink);
    }
}

/// <summary>Beş duraklı asansör: her katta bir sınır anahtarı.</summary>
public sealed class ElevatorDef : SystemDefBase
{
    public override string TypeId => "system-elevator";
    public override int? LegacyType => 26;
    public override string Tags => "asansör kat elevator lift";
    public override Vec Size => new(60, 240);
    public override Vec DefaultLabelOffset => new(0, -12);
    protected override double TravelTime => 16;

    protected override (Vec, Side)[] TerminalLayout
    {
        get
        {
            var list = new List<(Vec, Side)> { (new Vec(10, 0), Side.Top), (new Vec(20, 0), Side.Top), (new Vec(30, 0), Side.Top) };
            for (int k = 0; k < 5; k++)
            {
                double y = 45 + 40 * k;
                list.Add((new Vec(60, y), Side.Right));
                list.Add((new Vec(60, y + 5), Side.Right));
                list.Add((new Vec(60, y + 25), Side.Right));
                list.Add((new Vec(60, y + 30), Side.Right));
            }
            return [.. list];
        }
    }

    // Kat 1 (en üst sınır anahtarı, klemens 3-6) en üstte; konum 0 = en alt kat
    protected override double[] SwitchPositions => [1, 0.75, 0.5, 0.25, 0];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        p.Rect(new Vec(0, 0), new Vec(60, 240), ink, 1, Paint.Of(Ink.Background));
        p.Circle(new Vec(20, 18), 8, ink, 1, ctx.State?.Active == true ? Paint.Of(Ink.Active, 0.25) : null);
        p.Text(new Vec(20, 20), "M", 7, ink, bold: true);
        p.Rect(new Vec(8, 40), new Vec(30, 195), ink, 0.8, dashed: true);
        double floorTop = 45, step = 40;
        for (int k = 0; k < 5; k++)
        {
            double y = floorTop + step * k + 15;
            p.Line(new Vec(38, y + 15), new Vec(52, y + 15), ink, 0.8);
            p.Text(new Vec(47, y + 2), (5 - k).ToString(), 6, ink);
        }
        double pos = Position(ctx);
        double cabY = floorTop + step * 4 * (1 - pos) + 2;
        p.Rect(new Vec(10, cabY), new Vec(26, 34), ink, 1, Paint.Of(Ink.Accent, 0.3));
        p.Line(new Vec(23, 26), new Vec(23, cabY), ink, 0.6);
    }
}
