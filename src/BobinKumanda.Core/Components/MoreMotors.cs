using System.Globalization;
using System.Numerics;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Components;

/// <summary>Frenli motor: fren bobini (B1-B2) enerjilenince fren açılır; enerji kesilince motor hemen durur.</summary>
public sealed class BrakeMotorDef : ComponentDef
{
    public override string TypeId => "motor-brake";
    public override ComponentCategory Category => ComponentCategory.Motors;
    public override string LabelPrefix => "M";
    public override string Tags => "frenli motor fren bobini elektromanyetik fren vinç brake motor";
    public override Vec Size => new(60, 60);
    public override Vec DefaultLabelOffset => new(44, 4);

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("U", new Vec(0, 0), Side.Top, "U"),
        new TerminalDef("V", new Vec(20, 0), Side.Top, "V"),
        new TerminalDef("W", new Vec(40, 0), Side.Top, "W"),
        new TerminalDef("PE", new Vec(40, 60), Side.Bottom, "PE"),
        new TerminalDef("B1", new Vec(60, 30), Side.Right, "B1"),
        new TerminalDef("B2", new Vec(60, 46), Side.Right, "B2")
    ];

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Number("powerKw", 1.5, "kW", 0.06, 200),
        ParamDef.Choice("connection", "star", "star", "delta"),
        ParamDef.Number("startTime", 1.0, "s", 0.2, 30),
        ParamDef.Choice("brakeVoltage", "230AC", CoilModel.VoltageChoices),
        ParamDef.Number("load", 100, "%", 0, 200),
        ParamDef.Bool("bodyFault", false)
    ];

    public override string? SubLabel(ComponentInstance c) =>
        string.Format(Loc.Culture, "{0:0.##} kW", c.GetDouble("powerKw", 1.5));

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        p.Polyline([new Vec(0, 0), new Vec(0, 14), new Vec(10, 26)], ink);
        p.Line(new Vec(20, 0), new Vec(20, 22), ink);
        p.Polyline([new Vec(40, 0), new Vec(40, 14), new Vec(30, 26)], ink);
        var c = new Vec(20, 38);
        SymbolKit.Motor(p, ctx, c, 15, "M", "3~", (ctx.State?.Level ?? 0) * (ctx.State?.Direction ?? 0), ctx.State?.Direction ?? 0, ctx.AnimTime);
        SymbolKit.MotorEarth(p, ink, new Vec(40, 60), c + new Vec(10.6, 10.6));
        // fren: bobin kutusu ve mile giden kesikli bağlantı; fren açıkken (bobin çekili) vurgulu
        bool released = ctx.State?.Position == 1;
        p.Line(new Vec(60, 30), new Vec(52, 30), ink);
        p.Line(new Vec(60, 46), new Vec(52, 46), ink);
        p.Rect(new Vec(44, 27), new Vec(8, 22), released ? Paint.Of(Ink.Active) : ink, 1, released ? Paint.Of(Ink.Active, 0.3) : null);
        p.Text(new Vec(48, 40), "Br", 4, ink, bold: true);
        p.Line(new Vec(35, 38), new Vec(44, 38), ink, 0.8, dashed: true);
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new BrakeMotorSim(instance.GetParam("connection", "star") == "delta", instance.GetDouble("powerKw", 1.5),
            instance.GetDouble("startTime", 1.0), instance.GetParam("brakeVoltage", "230AC"),
            instance.GetDouble("load", 100) / 100, instance.GetBool("bodyFault", false));
}

internal sealed class BrakeMotorSim(bool delta, double powerKw, double startTime, string brakeVoltage, double load, bool bodyFault)
    : InductionMotorSimBase(load, bodyFault)
{
    private CoilModel? brake;
    private CoilModel Brake => brake ??= new CoilModel(this, brakeVoltage, logKeys: ("log.brakeReleased", "log.brakeApplied"));

    protected override InductionMotorModel CreateModel() => new(this, delta ? 400 : 230, powerKw, startTime) { BrakeEngaged = true };
    protected override int PeTerminal => 3;

    public override int InternalNodeCount => delta ? 0 : 1;

    protected override void StampWindings(Stamper s)
    {
        (Pin, Pin)[] w = delta
            ? [(T(0), T(1)), (T(1), T(2)), (T(2), T(0))]
            : [(T(0), Internal(0)), (T(1), Internal(0)), (T(2), Internal(0))];
        Model.BrakeEngaged = !Brake.Pulled;
        Model.Stamp(s, w);
        Brake.Stamp(s, T(4), T(5));
    }

    public override bool Evaluate(Solution sol)
    {
        Brake.Evaluate(sol);
        return base.Evaluate(sol);
    }

    public override void Check(Solution sol)
    {
        base.Check(sol);
        Brake.Check(sol);
    }

    public override void UpdateVisual(Solution sol)
    {
        base.UpdateVisual(sol);
        Visual.Position = Brake.Pulled ? 1 : 0;
        Visual.Fault |= Brake.Burned;
    }

    public override IEnumerable<Readout> Readouts(Solution sol) =>
        base.Readouts(sol).Append(new Readout(L("readout.brake"), L(Brake.Pulled ? "state.brakeOpen" : "state.brakeOn")));
}

/// <summary>
/// İki devirli motorlar. Dahlander: tek sargı, düşük devirde üçgen (1U-1V-1W beslenir), yüksek devirde çift yıldız
/// (2U-2V-2W beslenir, 1U-1V-1W köprülenir). Ayrı sargılı: iki bağımsız yıldız sargı, 1U.. düşük, 2U.. yüksek devir.
/// </summary>
public abstract class TwoSpeedMotorDefBase : ComponentDef
{
    public override ComponentCategory Category => ComponentCategory.Motors;
    public override string LabelPrefix => "M";
    public override Vec Size => new(70, 40);
    public override Vec DefaultLabelOffset => new(40, -10);

    protected abstract bool Dahlander { get; }

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("1U", new Vec(5, 0), Side.Top, "1U"),
        new TerminalDef("1V", new Vec(15, 0), Side.Top, "1V"),
        new TerminalDef("1W", new Vec(25, 0), Side.Top, "1W"),
        new TerminalDef("2U", new Vec(5, 40), Side.Bottom, "2U"),
        new TerminalDef("2V", new Vec(15, 40), Side.Bottom, "2V"),
        new TerminalDef("2W", new Vec(25, 40), Side.Bottom, "2W"),
        new TerminalDef("PE", new Vec(65, 40), Side.Bottom, "PE")
    ];

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Number("powerKw", 2.2, "kW", 0.06, 200),
        ParamDef.Number("startTime", 1.5, "s", 0.2, 30),
        ParamDef.Number("load", 80, "%", 0, 200),
        ParamDef.Bool("bodyFault", false)
    ];

    public override string? SubLabel(ComponentInstance c) =>
        string.Format(Loc.Culture, "{0:0.##} kW {1}", c.GetDouble("powerKw", 2.2), Dahlander ? "Δ/YY" : "Y/Y");

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        p.Rect(new Vec(0, 5), new Vec(30, 30), ink, 1, Paint.Of(Ink.Background));
        string[] top = ["1U", "1V", "1W"], bottom = ["2U", "2V", "2W"];
        for (int i = 0; i < 3; i++)
        {
            double x = 5 + 10 * i;
            p.Line(new Vec(x, 0), new Vec(x, 8), ink);
            p.Line(new Vec(x, 32), new Vec(x, 40), ink);
            SymbolKit.Terminal(p, ctx, new Vec(x, 9.5));
            SymbolKit.Terminal(p, ctx, new Vec(x, 30.5));
            p.Text(new Vec(x, 16), top[i], 3.4, ink);
            p.Text(new Vec(x, 26.5), bottom[i], 3.4, ink);
        }
        p.Text(new Vec(15, 21.5), Dahlander ? "Δ/YY" : "Y/Y", 3.2, Paint.Of(Ink.Muted));
        p.Line(new Vec(30, 20), new Vec(38, 20), ink);
        var c = new Vec(53, 20);
        SymbolKit.Motor(p, ctx, c, 14, "M", "3~", (ctx.State?.Level ?? 0) * (ctx.State?.Direction ?? 0), ctx.State?.Direction ?? 0, ctx.AnimTime);
        SymbolKit.MotorEarth(p, ink, new Vec(65, 40), new Vec(62.9, 29.9));
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new TwoSpeedMotorSim(Dahlander, instance.GetDouble("powerKw", 2.2), instance.GetDouble("startTime", 1.5),
            instance.GetDouble("load", 80) / 100, instance.GetBool("bodyFault", false));
}

public sealed class DahlanderMotorDef : TwoSpeedMotorDefBase
{
    public override string TypeId => "motor-dahlander";
    public override string Tags => "dahlander motor iki devirli kutup değiştirme çift yıldız two speed pole changing";
    protected override bool Dahlander => true;
}

public sealed class TwoWindingMotorDef : TwoSpeedMotorDefBase
{
    public override string TypeId => "motor-2speed";
    public override string Tags => "ayrı sargılı iki devirli motor iki sargılı two speed separate winding";
    protected override bool Dahlander => false;
}

internal sealed class TwoSpeedMotorSim : SimComponent
{
    private static readonly Complex A = Complex.FromPolarCoordinates(1, 2 * Math.PI / 3);
    private const double RunAngle = 0.61, StartAngle = 1.21, NoLoadAngle = 1.37;

    private readonly bool dahlander;
    private readonly double startTime;
    private readonly double zLow, zHigh;          // anma çalışmasında dal empedansı (Dahlander: yarım sargı)
    private readonly double iRated;               // ısınma için dal anma akımı
    private readonly int[] br = new int[6];
    private readonly Complex[] bv = new Complex[6], bi = new Complex[6];
    private readonly BodyFault fault;
    private double speed, heat, load;
    private bool burned;
    private double posLow, negLow, posHigh, negHigh;
    private int lastLogged;

    public TwoSpeedMotorSim(bool dahlander, double powerKw, double startTime, double load, bool bodyFault)
    {
        this.dahlander = dahlander;
        this.startTime = Math.Max(0.2, startTime);
        this.load = Math.Clamp(load, 0, 3);
        double p = Math.Max(0.05, powerKw) * 1000;
        const double eff = 0.85 * 0.82;
        if (dahlander)
        {
            // düşük devir (üçgen, yarım sargılar seri): 0,75 P; yüksek devirde (çift yıldız) yaklaşık P
            double edge = 0.75 * p / (3 * 400 * eff);
            zLow = zHigh = 200 / edge;
            iRated = 1.15 * edge;
        }
        else
        {
            double low = 0.6 * p / (3 * 230 * eff), high = p / (3 * 230 * eff);
            zLow = 230 / low;
            zHigh = 230 / high;
            iRated = high;
        }
        fault = new BodyFault(this, bodyFault);
    }

    public override int InternalNodeCount => dahlander ? 0 : 2;
    public override bool NeedsTicks => Energized || Math.Abs(speed) > 0.001 || heat > 0.05;
    public override SimSlider Slider => new("slider.load", 0, 200, load * 100, "%", 5);
    public override IReadOnlyList<SimAction> Actions => [BodyFault.Action];

    private double TorqueLow => (posLow * posLow - negLow * negLow) / (dahlander ? 400.0 * 400 : 230.0 * 230);
    private double TorqueHigh => (posHigh * posHigh - negHigh * negHigh) / (230.0 * 230);
    private bool Energized => posLow + negLow > 30 || posHigh + negHigh > 20;

    /// <summary>Etkin devir: 1 düşük, 2 yüksek, 0 enerjisiz.</summary>
    private int Mode => !Energized ? 0 : Math.Abs(TorqueHigh) >= Math.Abs(TorqueLow) ? 2 : 1;

    private static double Sync(int mode) => mode == 1 ? 0.5 : 1;

    private Complex Impedance(int mode, double zRun, double torque)
    {
        double sync = Sync(mode);
        int dir = torque > 0 ? 1 : torque < 0 ? -1 : Math.Sign(speed);
        if (dir == 0)
        {
            dir = 1;
        }
        double slip = Math.Clamp(1 - speed * dir / sync, 0, 2);
        double s = Math.Clamp(slip / 0.9, 0, 1);
        double k = 5 * Math.Pow(s, 1.5) + 0.35 + 0.65 * Math.Min(load, 3);
        double runAngle = NoLoadAngle + (RunAngle - NoLoadAngle) * Math.Min(1, load);
        return Complex.FromPolarCoordinates(zRun / k, runAngle + (StartAngle - runAngle) * s);
    }

    public override void Stamp(Stamper s)
    {
        if (burned)
        {
            for (int k = 0; k < 6; k++)
            {
                br[k] = s.Switch(T(k % 6), T((k + 1) % 6), false);
            }
            fault.Stamp(s, T(0), T(6));
            return;
        }
        if (dahlander)
        {
            int mode = Mode;
            Complex z = mode == 2 ? Impedance(2, zHigh, TorqueHigh) : Impedance(1, zLow, TorqueLow);
            // yarım sargılar: 1U-2U, 2U-1V, 1V-2V, 2V-1W, 1W-2W, 2W-1U
            int[] a = [0, 3, 1, 4, 2, 5], b = [3, 1, 4, 2, 5, 0];
            for (int k = 0; k < 6; k++)
            {
                br[k] = s.Impedance(T(a[k]), T(b[k]), z, z.Real);
            }
        }
        else
        {
            Complex zl = Impedance(1, zLow, TorqueLow), zh = Impedance(2, zHigh, TorqueHigh);
            for (int k = 0; k < 3; k++)
            {
                br[k] = s.Impedance(T(k), Internal(0), zl, zl.Real);
                br[3 + k] = s.Impedance(T(3 + k), Internal(1), zh, zh.Real);
            }
        }
        fault.Stamp(s, T(0), T(6));
    }

    private static (double Pos, double Neg) Sequence(Complex a, Complex b, Complex c) =>
        (((a + A * b + A * A * c) / 3).Magnitude, ((a + A * A * b + A * c) / 3).Magnitude);

    public override bool Evaluate(Solution sol)
    {
        for (int k = 0; k < 6; k++)
        {
            bv[k] = sol.BranchVoltageAc(br[k]);
            bi[k] = sol.CurrentAc(br[k]);
        }
        if (dahlander)
        {
            // Düşük devir alanı: kenar gerilimleri (yarımların toplamı); yüksek devir: yarımların farkı / 2
            (posLow, negLow) = Sequence(bv[0] + bv[1], bv[2] + bv[3], bv[4] + bv[5]);
            (posHigh, negHigh) = Sequence((bv[1] - bv[0]) / 2, (bv[3] - bv[2]) / 2, (bv[5] - bv[4]) / 2);
        }
        else
        {
            (posLow, negLow) = Sequence(bv[0], bv[1], bv[2]);
            (posHigh, negHigh) = Sequence(bv[3], bv[4], bv[5]);
        }
        fault.Read(sol, TerminalNodes[6]);
        return false;
    }

    public override bool Advance(double dt, Solution sol)
    {
        if (burned)
        {
            return false;
        }
        double before = speed;
        int mode = Mode;
        double torque = mode == 2 ? TorqueHigh : mode == 1 ? TorqueLow : 0;
        double abs = Math.Abs(torque);
        if (abs > 0.12 && load < 3.2 * abs)
        {
            double sync = Sync(mode);
            double limit = sync * Math.Min(1, 0.79 + 0.25 * Math.Min(1, abs) - 0.04 * Math.Clamp(load, 0, 2.5)) * Math.Sign(torque);
            // yüksekten düşük devire geçişte rejeneratif frenleme: hız hızla senkron hıza iner
            double rate = (Math.Abs(speed) > sync ? 3 : Math.Min(1.5, abs)) / startTime;
            speed = Move(speed, limit, rate * dt);
        }
        else
        {
            speed = Move(speed, 0, dt / 2.0);
        }
        double sum = 0;
        for (int k = 0; k < 6; k++)
        {
            double r = bi[k].Magnitude / iRated;
            sum += r * r;
        }
        heat += (sum / 3 - heat) * Math.Min(1, dt / 300);
        if (heat > 2.2)
        {
            burned = true;
            speed = 0;
            Visual.Fault = true;
            Sim.ReportFault("motor-burned", L("problem.motorBurned", Label), L("explain.motorBurned"), [Id], []);
            return true;
        }
        int state = Math.Abs(speed) > 0.3 ? Math.Sign(speed) * (Math.Abs(speed) > 0.75 ? 2 : 1) : 0;
        if (state != lastLogged && (state == 0 || Math.Abs(Math.Abs(speed) - Sync(Math.Abs(state))) < 0.12))
        {
            Sim.AddLog(state == 0 ? L("log.motorStopped", Label)
                : L("log.motorRunning", Label, L(state > 0 ? "dir.right" : "dir.left"), L(Math.Abs(state) == 2 ? "speed.high" : "speed.low")), Id);
            lastLogged = state;
        }
        return Math.Abs(speed - before) > 0.002;
    }

    private static double Move(double v, double target, double step) =>
        v < target ? Math.Min(target, v + step) : Math.Max(target, v - step);

    public override void Interact(Interaction action)
    {
        if (action.Kind == InteractionKind.SetValue)
        {
            load = Math.Clamp(action.Value / 100, 0, 3);
        }
        else if (action.Kind == InteractionKind.Fault)
        {
            fault.Toggle();
        }
    }

    public override void Check(Solution sol)
    {
        fault.Check(6);
        if (burned)
        {
            return;
        }
        if (!dahlander && posLow > 100 && posHigh > 100)
        {
            Sim.ReportLive(new SimProblem("motor-both-speeds", ProblemSeverity.Warning,
                L("problem.bothSpeeds", Label), L("explain.bothSpeeds"), [Id], []));
        }
        double pos = Mode == 2 ? posHigh : posLow, neg = Mode == 2 ? negHigh : negLow;
        if (Energized && neg > 0.3 * pos && pos > 0.3 * neg)
        {
            Sim.ReportLive(new SimProblem("motor-phase-loss", ProblemSeverity.Warning,
                L("problem.motorPhaseLoss", Label), L("explain.motorPhaseLoss"), [Id], []));
        }
        if (heat > 1.3)
        {
            Sim.ReportLive(new SimProblem("motor-hot", ProblemSeverity.Warning,
                L("problem.motorHot", Label, (heat * 100).ToString("0", CultureInfo.InvariantCulture)), L("explain.motorHot"), [Id], []));
        }
    }

    public override void UpdateVisual(Solution sol)
    {
        Visual.Level = Math.Abs(speed);
        Visual.Direction = Math.Sign(speed);
        Visual.Active = Math.Abs(speed) > 0.05;
        Visual.Fault = burned;
        Visual.Warning = heat > 1.3;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L("readout.speed"), string.Format(Loc.Culture, "{0:0} {1}", Math.Abs(speed) * 2900, L("unit.rpm")));
        yield return new Readout(L("readout.direction"), Math.Abs(speed) > 0.05 ? L(speed > 0 ? "dir.right" : "dir.left") : "—");
        yield return new Readout(L("readout.stage"), Mode switch
        {
            1 => L("speed.low") + (dahlander ? " (Δ)" : ""),
            2 => L("speed.high") + (dahlander ? " (YY)" : ""),
            _ => "—"
        });
        double max = bi.Max(c => c.Magnitude);
        yield return new Readout(L("readout.current"), Simulator.FormatAmps(max));
        yield return new Readout(L("readout.load"), string.Format(Loc.Culture, "%{0:0}", load * 100));
        yield return new Readout(L("readout.heat"), string.Format(Loc.Culture, "%{0:0}", heat * 100));
        yield return fault.Readout();
    }
}

/// <summary>Doğru akım motoru (sabit mıknatıslı): hız gerilimle, dönüş yönü kutuplarla değişir.</summary>
public sealed class DcMotorDef : ComponentDef
{
    public override string TypeId => "motor-dc";
    public override ComponentCategory Category => ComponentCategory.Motors;
    public override string LabelPrefix => "M";
    public override string Tags => "dc motor doğru akım motoru sabit mıknatıslı 24V dc motor";
    public override Vec Size => new(40, 40);
    public override Vec DefaultLabelOffset => new(36, -6);

    public override IReadOnlyList<TerminalDef> Terminals =>
        [new TerminalDef("A1", new Vec(0, 20), Side.Left, "A1 +"), new TerminalDef("A2", new Vec(40, 20), Side.Right, "A2 −")];

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Choice("ratedVoltage", "24", "12", "24", "48", "110", "220"),
        ParamDef.Number("power", 60, "W", 1, 20000)
    ];

    public override string? SubLabel(ComponentInstance c) => c.GetParam("ratedVoltage", "24") + " V =";

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        p.Line(new Vec(0, 20), new Vec(7, 20), ink);
        p.Line(new Vec(33, 20), new Vec(40, 20), ink);
        SymbolKit.Motor(p, ctx, new Vec(20, 20), 13, "M", "=", (ctx.State?.Level ?? 0) * (ctx.State?.Direction ?? 0), ctx.State?.Direction ?? 0, ctx.AnimTime);
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new DcMotorSim(instance.GetDouble("ratedVoltage", 24), instance.GetDouble("power", 60));
}

internal sealed class DcMotorSim : SimComponent
{
    private readonly double ratedVoltage;
    private readonly double rRun;
    private readonly double ratedCurrent;
    private int branch = -1;
    private double speed, heat;
    private double vdc, vac, amps;
    private bool burned;
    private int lastLogged;

    public DcMotorSim(double ratedVoltage, double power)
    {
        this.ratedVoltage = ratedVoltage > 0 ? ratedVoltage : 24;
        ratedCurrent = Math.Max(0.01, power / (this.ratedVoltage * 0.8));
        rRun = this.ratedVoltage / ratedCurrent;
    }

    private double Target => Math.Clamp(vdc / ratedVoltage, -1.3, 1.3);
    public override bool NeedsTicks => Math.Abs(vdc) > 0.05 * ratedVoltage || Math.Abs(speed) > 0.001 || heat > 0.05;

    public override void Stamp(Stamper s)
    {
        if (burned)
        {
            branch = s.Switch(T(0), T(1), false);
            return;
        }
        double target = Math.Abs(Target);
        double slip = target < 0.05 ? 1 : Math.Clamp(1 - Math.Abs(speed) / target, 0, 1);
        double k = 1 + 9 * Math.Pow(slip, 1.5);
        double r = rRun / k;
        branch = s.Impedance(T(0), T(1), new Complex(r, 0.8 * r), r);
    }

    public override bool Evaluate(Solution sol)
    {
        vdc = sol.BranchVoltageDc(branch);
        vac = sol.BranchVoltageAc(branch).Magnitude;
        amps = Math.Max(Math.Abs(sol.CurrentDc(branch)), sol.CurrentAc(branch).Magnitude);
        return false;
    }

    public override bool Advance(double dt, Solution sol)
    {
        if (burned)
        {
            return false;
        }
        double before = speed;
        speed += (Target - speed) * Math.Min(1, dt / 0.6);
        double r = amps / ratedCurrent;
        heat += (r * r - heat) * Math.Min(1, dt / 120);
        if (heat > 2.5 || Math.Abs(vdc) > 1.5 * ratedVoltage && heat > 1.5)
        {
            burned = true;
            speed = 0;
            Visual.Fault = true;
            Sim.ReportFault("motor-burned", L("problem.motorBurned", Label), L("explain.dcMotorBurned"), [Id], []);
            return true;
        }
        int state = Math.Abs(speed) > 0.3 ? Math.Sign(speed) : 0;
        if (state != lastLogged)
        {
            Sim.AddLog(state == 0 ? L("log.motorStopped", Label) : L("log.motorRunning", Label, L(state > 0 ? "dir.right" : "dir.left"), "DC"), Id);
            lastLogged = state;
        }
        return Math.Abs(speed - before) > 0.002;
    }

    public override void Check(Solution sol)
    {
        if (!burned && vac > 0.3 * ratedVoltage)
        {
            Sim.ReportLive(new SimProblem("dc-motor-ac", ProblemSeverity.Warning,
                L("problem.dcMotorAc", Label, Simulator.FormatVolts(vac)), L("explain.dcMotorAc"), [Id], []));
        }
    }

    public override void UpdateVisual(Solution sol)
    {
        Visual.Level = Math.Min(1, Math.Abs(speed));
        Visual.Direction = Math.Sign(speed);
        Visual.Active = Math.Abs(speed) > 0.05;
        Visual.Fault = burned;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L("readout.voltage"), Simulator.FormatVolts(vdc) + " DC");
        yield return new Readout(L("readout.current"), Simulator.FormatAmps(amps));
        yield return new Readout(L("readout.speed"), string.Format(Loc.Culture, "{0:0} {1}", Math.Abs(speed) * 3000, L("unit.rpm")));
        yield return new Readout(L("readout.direction"), Math.Abs(speed) > 0.05 ? L(speed > 0 ? "dir.right" : "dir.left") : "—");
    }
}
