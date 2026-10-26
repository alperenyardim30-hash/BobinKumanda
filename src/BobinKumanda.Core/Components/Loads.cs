using System.Globalization;
using System.Numerics;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Components;

/// <summary>Sinyal lambası: dirençli yük, parlaklık gerilimin karesiyle.</summary>
public sealed class LampDef : ComponentDef
{
    public static readonly string[] Colors = ["yellow", "red", "green", "blue", "white", "orange", "magenta"];

    public override string TypeId => "lamp";
    public override int? LegacyType => 9;
    public override ComponentCategory Category => ComponentCategory.Loads;
    public override string LabelPrefix => "H";
    public override char? Shortcut => 'H';
    public override string Tags => "lamba sinyal lambası ampul ışık lamp light indicator";
    public override Vec Size => new(20, 20);
    public override Vec DefaultLabelOffset => new(4, -8);

    public override IReadOnlyList<TerminalDef> Terminals =>
        [new TerminalDef("X1", new Vec(0, 10), Side.Left, "X1"), new TerminalDef("X2", new Vec(20, 10), Side.Right, "X2")];

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Choice("lampColor", "yellow", Colors),
        ParamDef.Choice("ratedVoltage", "230", "230", "24", "400", "12"),
        ParamDef.Number("power", 5, "W", 0.5, 2000)
    ];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        p.Line(new Vec(0, 10), new Vec(4, 10), ink);
        p.Line(new Vec(16, 10), new Vec(20, 10), ink);
        SymbolKit.Lamp(p, ctx, new Vec(10, 10), 6, ctx.State?.Level ?? 0,
            SymbolKit.ColorOf(ctx.Instance.GetParam("lampColor", "yellow")));
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new ResistiveLoadSim(
            instance.GetDouble("ratedVoltage", 230),
            instance.GetDouble("power", 5),
            "lamp");
}

/// <summary>Direnç/empedans olarak davranan yük: lamba, ısıtıcı, korna, fan vb.</summary>
internal class ResistiveLoadSim : SimComponent
{
    private readonly double ratedVoltage;
    private readonly double power;
    private readonly string kind;
    private readonly double cosPhi;
    private int branch = -1;
    private bool burned;
    private EventHandle? burnTimer;

    public ResistiveLoadSim(double ratedVoltage, double power, string kind, double cosPhi = 1)
    {
        this.ratedVoltage = ratedVoltage > 0 ? ratedVoltage : 230;
        this.power = Math.Max(0.1, power);
        this.kind = kind;
        this.cosPhi = Math.Clamp(cosPhi, 0.05, 1);
    }

    public double Ratio { get; private set; }
    public double Volts { get; private set; }
    public double Amps { get; private set; }
    public bool Burned => burned;
    public bool On => !burned && Ratio > 0.5;

    protected Complex Impedance()
    {
        double z = ratedVoltage * ratedVoltage / power;
        return Complex.FromPolarCoordinates(z, Math.Acos(cosPhi));
    }

    public override void Stamp(Stamper s)
    {
        if (burned)
        {
            branch = s.Switch(T(0), T(1), false);
            return;
        }
        Complex z = Impedance();
        branch = s.Impedance(T(0), T(1), z, z.Real);
    }

    public override bool Evaluate(Solution sol)
    {
        double vac = sol.BranchVoltageAc(branch).Magnitude;
        double vdc = Math.Abs(sol.BranchVoltageDc(branch));
        Volts = Math.Sqrt(vac * vac + vdc * vdc);
        Amps = Math.Max(sol.CurrentAc(branch).Magnitude, Math.Abs(sol.CurrentDc(branch)));
        Ratio = Volts / ratedVoltage;
        bool over = !burned && Ratio > 1.3;
        if (over && burnTimer == null)
        {
            burnTimer = Sim.Schedule(1.0, () =>
            {
                burnTimer = null;
                burned = true;
                Visual.Fault = true;
                Sim.ReportLatched(new SimProblem("load-burned", ProblemSeverity.Warning,
                    L("problem.loadBurned", Label, Simulator.FormatVolts(Volts), ratedVoltage.ToString("0", CultureInfo.InvariantCulture)),
                    L("explain.loadOverVoltage"), [Id], []));
            });
        }
        else if (!over && burnTimer != null)
        {
            Simulator.Cancel(burnTimer);
            burnTimer = null;
        }
        return false;
    }

    public override void UpdateVisual(Solution sol)
    {
        Visual.Level = burned ? 0 : Math.Clamp(Ratio * Ratio, 0, 1.2);
        Visual.Active = On;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L("readout.voltage"), Simulator.FormatVolts(Volts));
        yield return new Readout(L("readout.current"), Simulator.FormatAmps(Amps));
        yield return new Readout(L("readout.power"), string.Format(Loc.Culture, "{0:0.#} W", Volts * Amps * cosPhi));
        yield return new Readout(L("readout.state"), L(burned ? "state.burned" : On ? "state.on" : "state.off"));
    }
}
