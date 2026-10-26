using System.Numerics;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Components;

/// <summary>Acil stop butonu: mantar başlı, basınca kilitlenir; çevirerek (yeniden tıklayarak) bırakılır.</summary>
public sealed class EmergencyStopDef : ManualDefBase
{
    public override string TypeId => "estop";
    public override string Tags => "acil stop acil durdurma mantar buton emergency stop e-stop kırmızı";
    public override Vec Size => new(20, 20);
    protected override bool Momentary => false;
    protected override string PressKey => "log.estopPressed";
    protected override string ReleaseKey => "log.estopReleased";

    public override IReadOnlyList<TerminalDef> Terminals =>
        [new TerminalDef("1", new Vec(0, 12), Side.Left, "11"), new TerminalDef("2", new Vec(20, 12), Side.Right, "12")];

    protected override ContactSpec[] Specs => [new(0, 1, true)];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        bool pressed = Pressed(ctx);
        Vec top;
        if (ctx.School)
        {
            top = SymbolKit.SchoolPushContact(p, ctx, 12, true, pressed, stem: 7);
        }
        else
        {
            Vec mid = SymbolKit.ContactH(p, ctx, 12, true, ctx.IsClosed(0, true));
            top = new Vec(mid.X, 2.5 + (pressed && ctx.Simulating ? 1.5 : 0));
            p.Line(mid, top, SymbolKit.InkFor(ctx), 0.8, dashed: true);
        }
        Mushroom(p, ctx, top);
    }

    /// <summary>Kırmızı mantar başlık.</summary>
    internal static void Mushroom(ISymbolPainter p, DrawContext ctx, Vec stemTop)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        var pts = new List<Vec>();
        for (int i = 0; i <= 10; i++)
        {
            double a = Math.PI + Math.PI * i / 10;
            pts.Add(stemTop + new Vec(Math.Cos(a) * 4.5, Math.Sin(a) * 3.2));
        }
        p.Polyline(pts, ink, 0.9, closed: true, fill: Paint.Color(0xE53935));
    }
}

/// <summary>Işıklı buton: NO kontak (13-14) ve buton içindeki sinyal lambası (X1-X2).</summary>
public sealed class IlluminatedButtonDef : ManualDefBase
{
    public override string TypeId => "button-lit";
    public override string Tags => "ışıklı buton lambalı buton start illuminated push button";
    public override Vec Size => new(20, 30);

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("1", new Vec(0, 10), Side.Left, "13"), new TerminalDef("2", new Vec(20, 10), Side.Right, "14"),
        new TerminalDef("X1", new Vec(0, 25), Side.Left, "X1"), new TerminalDef("X2", new Vec(20, 25), Side.Right, "X2")
    ];

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Choice("lampColor", "green", LampDef.Colors),
        ParamDef.Choice("ratedVoltage", "230", "230", "24", "12")
    ];

    protected override ContactSpec[] Specs => [new(0, 1, false)];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        Vec mid;
        if (ctx.School)
        {
            mid = SymbolKit.SchoolPushContact(p, ctx, 10, false, Pressed(ctx), stem: 8);
        }
        else
        {
            mid = SymbolKit.ContactH(p, ctx, 10, false, ctx.IsClosed(0, false));
            SymbolKit.Actuator(p, ctx, ActuatorKind.Push, mid, -0.5, Pressed(ctx));
        }
        p.Line(new Vec(0, 25), new Vec(6, 25), ink);
        p.Line(new Vec(14, 25), new Vec(20, 25), ink);
        SymbolKit.Lamp(p, ctx, new Vec(10, 25), 4, ctx.State?.Level ?? 0, SymbolKit.ColorOf(ctx.Instance.GetParam("lampColor", "green")));
        p.Line(new Vec(10, 21), new Vec(10, 13), ink, 0.6, dashed: true);
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new IlluminatedButtonSim(Specs, instance.GetDouble("ratedVoltage", 230));
}

internal sealed class IlluminatedButtonSim(ContactSpec[] specs, double ratedVoltage)
    : ManualSim(specs, true, "log.pressed", "log.releasedButton")
{
    private int lampBranch = -1;
    private double ratio;

    private Complex LampZ => new(ratedVoltage * ratedVoltage / 2, 0);

    public override void Stamp(Stamper s)
    {
        base.Stamp(s);
        lampBranch = s.Impedance(T(2), T(3), LampZ, LampZ.Real);
    }

    public override bool Evaluate(Solution sol)
    {
        double vac = sol.BranchVoltageAc(lampBranch).Magnitude, vdc = Math.Abs(sol.BranchVoltageDc(lampBranch));
        ratio = Math.Sqrt(vac * vac + vdc * vdc) / ratedVoltage;
        return false;
    }

    public override void UpdateVisual(Solution sol)
    {
        base.UpdateVisual(sol);
        Visual.Level = Math.Clamp(ratio * ratio, 0, 1.2);
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        foreach (Readout r in base.Readouts(sol))
        {
            yield return r;
        }
        yield return new Readout(L("readout.lampVoltage"), Simulator.FormatVolts(ratio * ratedVoltage));
    }
}

/// <summary>
/// Konumlu şalterler (seçici, pako): her konumda hangi klemens çiftlerinin birleştiği bir tabloyla tanımlanır.
/// Tıklamak sıradaki konuma geçirir; sağ paneldeki kaydırıcıyla doğrudan konum seçilir.
/// </summary>
public abstract class PositionSwitchDefBase : ComponentDef
{
    public override ComponentCategory Category => ComponentCategory.Control;
    public override DeviceRole Role => DeviceRole.Master;
    public override string LabelPrefix => "S";

    /// <summary>Konum adları; ilki dinlenme (0) konumu.</summary>
    protected abstract string[] PositionNames { get; }

    /// <summary>Her konumda kapalı olan klemens çiftleri (klemens sırası).</summary>
    protected abstract (int A, int B)[][] ClosedPairs { get; }

    public override string? SubLabel(ComponentInstance c) => string.Join("-", PositionNames);

    public override SimComponent CreateSim(ComponentInstance instance) => new PositionSwitchSim(PositionNames, ClosedPairs);

    protected static int Position(DrawContext ctx) => ctx.State?.Position ?? 0;

    protected string PositionName(DrawContext ctx) => PositionNames[Math.Clamp(Position(ctx), 0, PositionNames.Length - 1)];
}

internal sealed class PositionSwitchSim : SimComponent, IDeviceMaster
{
    private readonly string[] names;
    private readonly (int A, int B)[][] closed;
    private readonly (int A, int B)[] allPairs;
    private int position;

    public PositionSwitchSim(string[] names, (int A, int B)[][] closed)
    {
        this.names = names;
        this.closed = closed;
        allPairs = closed.SelectMany(c => c).Distinct().ToArray();
    }

    SimComponent IDeviceMaster.Component => this;
    public string DefaultChannel => "main";

    public bool ChannelActive(string channel) =>
        channel.StartsWith('p') && int.TryParse(channel.AsSpan(1), out int k) ? position == k : position != 0;

    public override bool IsInteractive => true;

    public override SimSlider Slider => new("slider.position", 0, names.Length - 1, position, "", 1);

    public override void Stamp(Stamper s)
    {
        (int A, int B)[] now = closed[position];
        foreach ((int a, int b) in allPairs)
        {
            s.Switch(T(a), T(b), Array.IndexOf(now, (a, b)) >= 0);
        }
    }

    public override void Interact(Interaction action)
    {
        int before = position;
        switch (action.Kind)
        {
            case InteractionKind.Press or InteractionKind.Toggle:
                position = (position + 1) % names.Length;
                break;
            case InteractionKind.SetValue:
                position = Math.Clamp((int)Math.Round(action.Value), 0, names.Length - 1);
                break;
        }
        if (position != before)
        {
            Sim.AddLog(L("log.position", Label, names[position]), Id);
        }
    }

    public override void UpdateVisual(Solution sol)
    {
        Visual.Position = position;
        Visual.Active = position != 0;
        Visual.Closed = allPairs.Select(p => Array.IndexOf(closed[position], p) >= 0).ToArray();
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L("readout.position"), names[position]);
        yield return new Readout(L("readout.hint"), L("hint.position"));
    }
}

/// <summary>İki konumlu seçici şalter (0-1): 1'de 13-14 kapanır, 21-22 açılır.</summary>
public sealed class Selector2Def : PositionSwitchDefBase
{
    public override string TypeId => "selector-2";
    public override string Tags => "seçici şalter iki konumlu anahtar 0 1 selector switch";
    public override Vec Size => new(20, 30);
    public override Vec DefaultLabelOffset => new(4, -8);
    protected override string[] PositionNames => ["0", "1"];
    protected override (int A, int B)[][] ClosedPairs => [[(2, 3)], [(0, 1)]];

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("1", new Vec(0, 12), Side.Left, "13"), new TerminalDef("2", new Vec(20, 12), Side.Right, "14"),
        new TerminalDef("3", new Vec(0, 26), Side.Left, "21"), new TerminalDef("4", new Vec(20, 26), Side.Right, "22")
    ];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        ctx = ctx.WithStyle(SymbolStyle.Iec);
        bool on = Position(ctx) == 1;
        Vec m1 = SymbolKit.ContactH(p, ctx, 12, false, on);
        SymbolKit.ContactH(p, ctx, 26, true, !on);
        SymbolKit.Actuator(p, ctx, ActuatorKind.Rotary, m1, 1.5, false);
        SymbolKit.MechanicalLink(p, ctx, m1, new Vec(m1.X, 24));
    }
}

/// <summary>Üç konumlu seçici şalter (1-0-2): 1'de 13-14, 2'de 23-24 kapanır.</summary>
public sealed class Selector3Def : PositionSwitchDefBase
{
    public override string TypeId => "selector-3";
    public override string Tags => "seçici şalter üç konumlu 1 0 2 el otomatik selector switch three position";
    public override Vec Size => new(20, 30);
    public override Vec DefaultLabelOffset => new(4, -8);
    protected override string[] PositionNames => ["0", "1", "2"];
    protected override (int A, int B)[][] ClosedPairs => [[], [(0, 1)], [(2, 3)]];

    public override string? SubLabel(ComponentInstance c) => "1-0-2";

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("1", new Vec(0, 12), Side.Left, "13"), new TerminalDef("2", new Vec(20, 12), Side.Right, "14"),
        new TerminalDef("3", new Vec(0, 26), Side.Left, "23"), new TerminalDef("4", new Vec(20, 26), Side.Right, "24")
    ];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        ctx = ctx.WithStyle(SymbolStyle.Iec);
        int pos = Position(ctx);
        Vec m1 = SymbolKit.ContactH(p, ctx, 12, false, pos == 1);
        SymbolKit.ContactH(p, ctx, 26, false, pos == 2);
        SymbolKit.Actuator(p, ctx, ActuatorKind.Rotary, m1, 1.5, false);
        SymbolKit.MechanicalLink(p, ctx, m1, new Vec(m1.X, 23));
    }
}

/// <summary>Pako (kam) şalterlerinin kutu sembolü: solda girişler, sağda çıkışlar, üstte konum; simülasyonda iç bağlantılar.</summary>
public abstract class CamSwitchDefBase : PositionSwitchDefBase
{
    public override string LabelPrefix => "Q";
    public override Vec Size => new(40, 60);
    public override Vec DefaultLabelOffset => new(42, 22);

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        IReadOnlyList<TerminalDef> terms = Terminals;
        p.Rect(new Vec(6, 0), new Vec(28, 60), ink, 1, Paint.Of(Ink.Background));
        foreach (TerminalDef t in terms)
        {
            double x0 = t.Side == Side.Left ? 0 : 34;
            p.Line(new Vec(x0, t.Position.Y), new Vec(x0 + 6, t.Position.Y), ink);
            p.Text(new Vec(t.Side == Side.Left ? 8 : 32, t.Position.Y + 1.4), t.Number ?? t.Id, 3.4, Paint.Of(Ink.Muted),
                t.Side == Side.Left ? TextAlign.Left : TextAlign.Right);
        }
        // konum düğmesi
        var knob = new Vec(20, 8);
        p.Circle(knob, 5, ink, 0.9, ctx.Simulating && Position(ctx) != 0 ? Paint.Of(Ink.Active, 0.25) : null);
        p.Text(knob + new Vec(0, 2), PositionName(ctx), 5, ink, bold: true);
        if (!ctx.Simulating)
        {
            return;
        }
        Paint act = Paint.Of(Ink.Active);
        foreach ((int a, int b) in ClosedPairs[Math.Clamp(Position(ctx), 0, ClosedPairs.Length - 1)])
        {
            Vec pa = Inner(terms[a]), pb = Inner(terms[b]);
            if (terms[a].Side == terms[b].Side)
            {
                // aynı taraftaki iki uç (yıldız köprüsü): kısa dik çizgi
                double x = terms[a].Side == Side.Left ? 11 : 29;
                p.Polyline([pa, new Vec(x, pa.Y), new Vec(x, pb.Y), pb], act, 1.3);
            }
            else
            {
                p.Line(pa, pb, act, 1.3);
            }
        }
    }

    private static Vec Inner(TerminalDef t) => new(t.Side == Side.Left ? 6 : 34, t.Position.Y);

    protected static TerminalDef In(string id, double y) => new(id, new Vec(0, y), Side.Left, id);

    protected static TerminalDef Out(string id, double y) => new(id, new Vec(40, y), Side.Right, id);
}

/// <summary>İleri-geri pako şalter (1-0-2): 2. konumda iki faz yer değiştirir, motor ters döner.</summary>
public sealed class CamReverseDef : CamSwitchDefBase
{
    public override string TypeId => "cam-reverse";
    public override string Tags => "ileri geri pako şalter kam şalter yön değiştirme reversing cam switch";
    protected override string[] PositionNames => ["0", "1", "2"];
    protected override (int A, int B)[][] ClosedPairs => [[], [(0, 3), (1, 4), (2, 5)], [(0, 4), (1, 3), (2, 5)]];

    public override string? SubLabel(ComponentInstance c) => "1-0-2";

    public override IReadOnlyList<TerminalDef> Terminals =>
        [In("1", 20), In("3", 35), In("5", 50), Out("2", 20), Out("4", 35), Out("6", 50)];
}

/// <summary>Yıldız-üçgen pako şalter (0-Y-Δ): Y'de U2-V2-W2 köprülenir, Δ'da U1-W2, V1-U2, W1-V2 bağlanır.</summary>
public sealed class CamStarDeltaDef : CamSwitchDefBase
{
    public override string TypeId => "cam-stardelta";
    public override string Tags => "yıldız üçgen pako şalter kam şalter star delta cam switch";
    protected override string[] PositionNames => ["0", "Y", "Δ"];

    // 0 L1, 1 L2, 2 L3, 3 U1, 4 V1, 5 W1, 6 U2, 7 V2, 8 W2
    protected override (int A, int B)[][] ClosedPairs =>
    [
        [],
        [(0, 3), (1, 4), (2, 5), (6, 7), (7, 8)],
        [(0, 3), (1, 4), (2, 5), (0, 8), (1, 6), (2, 7)]
    ];

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        In("L1", 20), In("L2", 35), In("L3", 50),
        Out("U1", 17), Out("V1", 23), Out("W1", 29), Out("U2", 41), Out("V2", 47), Out("W2", 53)
    ];
}

/// <summary>0-1-2 pako şalter (3 kutuplu, iki çıkış grubu): ayrı sargılı iki devirli motor ya da iki devre seçimi için.</summary>
public sealed class Cam012Def : CamSwitchDefBase
{
    public override string TypeId => "cam-012";
    public override string Tags => "0 1 2 pako şalter kam şalter iki devir devre seçici cam switch";
    protected override string[] PositionNames => ["0", "1", "2"];
    protected override (int A, int B)[][] ClosedPairs => [[], [(0, 3), (1, 4), (2, 5)], [(0, 6), (1, 7), (2, 8)]];

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        In("1", 20), In("3", 35), In("5", 50),
        Out("2", 17), Out("4", 23), Out("6", 29), Out("8", 41), Out("10", 47), Out("12", 53)
    ];
}
