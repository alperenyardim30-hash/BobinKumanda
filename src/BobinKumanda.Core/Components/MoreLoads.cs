using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Components;

/// <summary>İki klemensli (X1-X2) yükler için ortak yerleşim.</summary>
public abstract class TwoTerminalLoadDef : ComponentDef
{
    public override ComponentCategory Category => ComponentCategory.Loads;
    public override Vec Size => new(20, 20);
    public override Vec DefaultLabelOffset => new(4, -9);

    public override IReadOnlyList<TerminalDef> Terminals =>
        [new TerminalDef("X1", new Vec(0, 10), Side.Left, "X1"), new TerminalDef("X2", new Vec(20, 10), Side.Right, "X2")];

    protected static void Leads(ISymbolPainter p, Paint ink, double inset)
    {
        p.Line(new Vec(0, 10), new Vec(inset, 10), ink);
        p.Line(new Vec(20 - inset, 10), new Vec(20, 10), ink);
    }
}

/// <summary>Sesli ikaz: korna, buzzer, siren ya da zil. Çalarken bilgisayardan ses gelir (ayarlardan kapatılabilir).</summary>
public sealed class HornDef : TwoTerminalLoadDef
{
    public override string TypeId => "horn";
    public override string LabelPrefix => "P";
    public override string Tags => "korna buzzer siren zil sesli ikaz alarm horn bell";

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Choice("soundKind", "horn", "horn", "buzzer", "siren", "bell"),
        ParamDef.Choice("ratedVoltage", "230", "230", "24", "12"),
        ParamDef.Number("power", 15, "W", 0.5, 500)
    ];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        bool on = ctx.State?.Active == true;
        Paint fill = on ? Paint.Of(Ink.Active, 0.35) : Paint.Of(Ink.Background);
        switch (ctx.Instance.GetParam("soundKind", "horn"))
        {
            case "buzzer":
                Leads(p, ink, 5);
                // yarım daire kubbe
                var dome = new List<Vec>();
                for (int i = 0; i <= 12; i++)
                {
                    double a = Math.PI + Math.PI * i / 12;
                    dome.Add(new Vec(10 + 5 * Math.Cos(a), 12 + 6 * Math.Sin(a)));
                }
                p.Polyline(dome, ink, 1, closed: true, fill: fill);
                p.Line(new Vec(5, 12), new Vec(5, 10), ink);
                p.Line(new Vec(15, 12), new Vec(15, 10), ink);
                break;
            case "bell":
                Leads(p, ink, 5);
                SymbolKit.Arc(p, ink, new Vec(10, 11), 5, 180, 180);
                p.Line(new Vec(4, 11), new Vec(16, 11), ink);
                p.Circle(new Vec(10, 13.5), 1.4, ink, 0.8, on ? Paint.Of(Ink.Active) : ink);
                break;
            default:
                Leads(p, ink, 4);
                p.Rect(new Vec(4, 7), new Vec(4, 6), ink, 1, fill);
                p.Polyline([new Vec(8, 7), new Vec(15, 2.5), new Vec(15, 17.5), new Vec(8, 13)], ink, 1, closed: true, fill: fill);
                p.Line(new Vec(15, 10), new Vec(16, 10), ink);
                if (ctx.Instance.GetParam("soundKind", "horn") == "siren")
                {
                    SymbolKit.Arc(p, ink, new Vec(15, 10), 3, -40, 80, 0.7);
                }
                break;
        }
        if (on)
        {
            // ses dalgaları
            double t = ctx.AnimTime * 3 % 1;
            SymbolKit.Arc(p, Paint.Of(Ink.Active, 1 - t), new Vec(16, 10), 2 + 3 * t, -45, 90, 0.8);
        }
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new SoundLoadSim(instance.GetDouble("ratedVoltage", 230), instance.GetDouble("power", 15), instance.GetParam("soundKind", "horn"));
}

internal sealed class SoundLoadSim(double ratedVoltage, double power, string sound)
    : ResistiveLoadSim(ratedVoltage, power, "sound", 0.7), ISoundSource
{
    public bool SoundActive => On;
    public string SoundKind => sound;
}

/// <summary>Isıtıcı (rezistans): saf dirençli yük, güç parametresiyle.</summary>
public sealed class HeaterDef : TwoTerminalLoadDef
{
    public override string TypeId => "heater";
    public override string LabelPrefix => "E";
    public override string Tags => "ısıtıcı rezistans fırın kazan heater resistor";

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Choice("ratedVoltage", "230", "230", "400", "24"),
        ParamDef.Number("power", 2000, "W", 1, 50000)
    ];

    public override string? SubLabel(ComponentInstance c)
    {
        double w = c.GetDouble("power", 2000);
        return w >= 1000 ? string.Format(Loc.Culture, "{0:0.##} kW", w / 1000) : string.Format(Loc.Culture, "{0:0} W", w);
    }

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        double glow = Math.Clamp(ctx.State?.Level ?? 0, 0, 1);
        Leads(p, ink, 3);
        p.Rect(new Vec(3, 6), new Vec(14, 8), ink, 1, ctx.Simulating && glow > 0.05 ? Paint.Color(0xE53935, 0.15 + 0.6 * glow) : Paint.Of(Ink.Background));
        // ısıtma işareti: zikzak
        p.Polyline([new Vec(5, 10), new Vec(7, 7.5), new Vec(9, 12.5), new Vec(11, 7.5), new Vec(13, 12.5), new Vec(15, 10)], ink, 0.8);
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new ResistiveLoadSim(instance.GetDouble("ratedVoltage", 230), instance.GetDouble("power", 2000), "heater");
}

/// <summary>Selenoid valf: bobin enerjilenince açılır (sıvı/hava geçer).</summary>
public sealed class SolenoidValveDef : ComponentDef
{
    public override string TypeId => "solenoid-valve";
    public override ComponentCategory Category => ComponentCategory.Loads;
    public override string LabelPrefix => "Y";
    public override string Tags => "selenoid valf solenoid vana elektrovalf bobin solenoid valve";
    public override Vec Size => new(20, 30);
    public override Vec DefaultLabelOffset => new(4, -9);

    public override IReadOnlyList<TerminalDef> Terminals =>
        [new TerminalDef("A1", new Vec(0, 10), Side.Left, "A1"), new TerminalDef("A2", new Vec(20, 10), Side.Right, "A2")];

    public override IReadOnlyList<ParamDef> Params =>
        [ParamDef.Choice("coilVoltage", "230AC", CoilModel.VoltageChoices)];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        bool open = ctx.State?.Active == true;
        p.Line(new Vec(0, 10), new Vec(5, 10), ink);
        p.Line(new Vec(15, 10), new Vec(20, 10), ink);
        p.Rect(new Vec(5, 5), new Vec(10, 10), open ? Paint.Of(Ink.Active) : ink, 1, open ? Paint.Of(Ink.Active, 0.3) : null);
        p.Line(new Vec(5, 15), new Vec(15, 5), ink, 0.8);
        p.Line(new Vec(10, 15), new Vec(10, 19), ink, 0.8, dashed: true);
        // vana (papyon) ve akış
        Paint body = open ? Paint.Color(0x1E88E5, 0.55) : Paint.Of(Ink.Background);
        p.Polyline([new Vec(3, 20), new Vec(10, 24), new Vec(3, 28)], ink, 0.9, closed: true, fill: body);
        p.Polyline([new Vec(17, 20), new Vec(10, 24), new Vec(17, 28)], ink, 0.9, closed: true, fill: body);
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new SolenoidSim(instance.GetParam("coilVoltage", "230AC"));
}

internal sealed class SolenoidSim(string voltage) : SimComponent
{
    private CoilModel? coil;
    private CoilModel Coil
    {
        get
        {
            if (coil == null)
            {
                coil = new CoilModel(this, voltage, holdingVa: 10, logKeys: ("log.valveOpen", "log.valveClosed"));
            }
            return coil;
        }
    }

    public override void Stamp(Stamper s) => Coil.Stamp(s, T(0), T(1));

    public override bool Evaluate(Solution sol)
    {
        Coil.Evaluate(sol);
        return false;
    }

    public override void Check(Solution sol) => Coil.Check(sol);

    public override void UpdateVisual(Solution sol)
    {
        Visual.Active = Coil.Pulled;
        Visual.Fault = Coil.Burned;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        foreach (Readout r in Coil.Readouts())
        {
            yield return r.Name == L("readout.state")
                ? new Readout(r.Name, L(Coil.Burned ? "state.burned" : Coil.Pulled ? "state.open" : "state.closed"))
                : r;
        }
    }
}

/// <summary>Vantilatör (fan): bir fazlı küçük motor; gerilim gelince hızlanarak döner.</summary>
public sealed class FanDef : TwoTerminalLoadDef
{
    public override string TypeId => "fan";
    public override string LabelPrefix => "M";
    public override string Tags => "fan vantilatör aspiratör havalandırma soğutma fanı fan ventilator";
    public override Vec Size => new(20, 20);

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Choice("ratedVoltage", "230", "230", "24", "12"),
        ParamDef.Number("power", 60, "W", 1, 5000)
    ];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        Leads(p, ink, 3);
        double speed = ctx.State?.Level ?? 0;
        bool running = ctx.Simulating && speed > 0.02;
        var c = new Vec(10, 10);
        p.Circle(c, 7, ink, 1, running ? Paint.Of(Ink.Active, 0.12) : Paint.Of(Ink.Background));
        double turn = running ? ctx.AnimTime * speed * 2 * Math.PI * 1.5 : 0;
        for (int i = 0; i < 3; i++)
        {
            double a = turn + i * 2 * Math.PI / 3;
            var d = new Vec(Math.Cos(a), Math.Sin(a));
            var n = new Vec(-d.Y, d.X);
            p.Polyline([c, c + d * 5.5 + n * 1.6, c + d * 5.5 - n * 0.6], running ? Paint.Of(Ink.Active) : ink, 0.8, closed: true,
                fill: running ? Paint.Of(Ink.Active, 0.5) : ink);
        }
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new FanSim(instance.GetDouble("ratedVoltage", 230), instance.GetDouble("power", 60));
}

internal sealed class FanSim(double ratedVoltage, double power) : ResistiveLoadSim(ratedVoltage, power, "fan", 0.8)
{
    private double speed;

    public override bool NeedsTicks => Ratio > 0.05 || speed > 0.001;

    public override bool Advance(double dt, Solution sol)
    {
        double target = Burned || Ratio < 0.5 ? 0 : Math.Min(1, Ratio);
        speed += (target - speed) * Math.Min(1, dt / 1.2);
        return false;
    }

    public override void UpdateVisual(Solution sol)
    {
        base.UpdateVisual(sol);
        Visual.Level = speed;
        Visual.Direction = 1;
        Visual.Active = speed > 0.05;
    }
}
