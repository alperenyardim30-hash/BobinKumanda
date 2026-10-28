using System.Globalization;
using System.Numerics;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Components;

/// <summary>
/// Üç fazlı asenkron motor modeli. Üç sargının gerilim fazörlerinden pozitif/negatif bileşen bulunur:
/// dönüş yönü, kalkış momenti, faz kaybı ve yıldız/üçgen bunlardan çıkar. Kalkışta akım ~6×In'den nominale iner.
/// </summary>
internal sealed class InductionMotorModel
{
    private static readonly Complex A = Complex.FromPolarCoordinates(1, 2 * Math.PI / 3);
    private const double RunAngle = 0.61;   // cosφ ≈ 0.82
    private const double StartAngle = 1.21; // cosφ ≈ 0.35
    private const double NoLoadAngle = 1.37; // cosφ ≈ 0.2
    // Sargı ısınma zaman sabiti: termik röle (τ = 240 s, 7,2×Ir'de ~7 s) motor yanmadan önce açar
    private const double ThermalTau = 300;

    private readonly SimComponent owner;
    private readonly int[] branches = new int[3];
    private readonly Complex[] windingVoltage = new Complex[3];
    private readonly Complex[] windingCurrent = new Complex[3];

    public double WindingVoltage { get; }
    public double RatedPowerKw { get; }
    public double StartTime { get; }
    public double WindingRatedCurrent { get; }

    /// <summary>İşaretli hız, −1..1 (1 = anma hızı, pozitif = sağa).</summary>
    public double Speed { get; private set; }

    /// <summary>Sargı sıcaklığı (1 = anma akımında kararlı).</summary>
    public double Heat { get; private set; }

    public bool Burned { get; private set; }
    public bool BrakeEngaged { get; set; }

    /// <summary>Mil yükü: 0 boşta, 1 anma yükü, 2 iki kat aşırı yük. Akımı ve kaymayı belirler.</summary>
    public double Load { get; set; } = 1;

    public double PositiveSeq { get; private set; }
    public double NegativeSeq { get; private set; }
    public double Torque { get; private set; }

    public InductionMotorModel WithLoad(double load)
    {
        Load = Math.Clamp(load, 0, 3);
        return this;
    }

    public InductionMotorModel(SimComponent owner, double windingVoltage, double powerKw, double startTime)
    {
        this.owner = owner;
        WindingVoltage = windingVoltage;
        RatedPowerKw = Math.Max(0.05, powerKw);
        StartTime = Math.Max(0.2, startTime);
        WindingRatedCurrent = RatedPowerKw * 1000 / (3 * windingVoltage * 0.85 * 0.82);
    }

    public bool Energized => PositiveSeq + NegativeSeq > 0.15 * WindingVoltage;
    public bool Running => Math.Abs(Speed) > 0.05;
    public bool NeedsTicks => Energized || Running || Heat > 0.05;

    private double Slip
    {
        get
        {
            int dir = Torque > 0 ? 1 : Torque < 0 ? -1 : Math.Sign(Speed);
            if (dir == 0)
            {
                dir = 1;
            }
            return Math.Clamp(1 - Speed * dir, 0, 2);
        }
    }

    /// <summary>Sargı empedansı: kalkışta düşük (yüksek akım), anma hızında yüke göre (boşta ~%35 akım).</summary>
    public Complex WindingImpedance()
    {
        double slip = Slip;
        double s = Math.Clamp(slip / 0.9, 0, 1);
        double load = Math.Clamp(Load, 0, 3);
        double k = 5 * Math.Pow(s, 1.5) + 0.35 + 0.65 * load;
        double zRun = WindingVoltage / WindingRatedCurrent;
        double runAngle = NoLoadAngle + (RunAngle - NoLoadAngle) * Math.Min(1, load);
        double angle = runAngle + (StartAngle - runAngle) * s;
        if (BrakeEngaged && Energized)
        {
            k = 6;
            angle = StartAngle;
        }
        return Complex.FromPolarCoordinates(zRun / k, angle);
    }

    public void Stamp(Stamper s, (Pin A, Pin B)[] windings)
    {
        Complex z = WindingImpedance();
        for (int i = 0; i < 3; i++)
        {
            branches[i] = Burned ? s.Switch(windings[i].A, windings[i].B, false) : s.Impedance(windings[i].A, windings[i].B, z, z.Real);
        }
    }

    public void Read(Solution sol)
    {
        for (int i = 0; i < 3; i++)
        {
            windingVoltage[i] = sol.BranchVoltageAc(branches[i]);
            windingCurrent[i] = sol.CurrentAc(branches[i]);
        }
        Complex v1 = (windingVoltage[0] + A * windingVoltage[1] + A * A * windingVoltage[2]) / 3;
        Complex v2 = (windingVoltage[0] + A * A * windingVoltage[1] + A * windingVoltage[2]) / 3;
        PositiveSeq = v1.Magnitude;
        NegativeSeq = v2.Magnitude;
        Torque = (PositiveSeq * PositiveSeq - NegativeSeq * NegativeSeq) / (WindingVoltage * WindingVoltage);
    }

    public double MaxWindingVoltage() => windingVoltage.Max(v => v.Magnitude);
    public double MinWindingVoltage() => windingVoltage.Min(v => v.Magnitude);
    public double AverageWindingVoltage() => windingVoltage.Average(v => v.Magnitude);
    public double WindingCurrentMag(int i) => windingCurrent[i].Magnitude;

    /// <summary>Hız ve ısınmayı ilerletir; empedansı değiştirecek kadar hız değiştiyse true.</summary>
    public bool Advance(double dt)
    {
        if (Burned)
        {
            return false;
        }
        double before = Speed;
        double absTorque = Math.Abs(Torque);
        if (BrakeEngaged)
        {
            Speed = MoveTowards(Speed, 0, dt / 0.3);
        }
        else if (absTorque > 0.12 && Load < 3.2 * absTorque)
        {
            int dir = Math.Sign(Torque);
            // anma yükünde ~%4 kayma; yük arttıkça hız biraz düşer
            double limit = Math.Min(1, 0.79 + 0.25 * Math.Min(1, absTorque) - 0.04 * Math.Clamp(Load, 0, 2.5)) * dir;
            double rate = Math.Min(1.5, absTorque) / StartTime;
            Speed = MoveTowards(Speed, limit, rate * dt);
        }
        else if (Energized && Math.Abs(Speed) > 0.3)
        {
            // tek fazda kalan motor dönmeye devam eder ama yavaşlar
            Speed = MoveTowards(Speed, Math.Sign(Speed) * 0.85, dt / StartTime);
        }
        else
        {
            Speed = MoveTowards(Speed, 0, dt / 2.0);
        }

        double load = 0;
        for (int i = 0; i < 3; i++)
        {
            double r = windingCurrent[i].Magnitude / WindingRatedCurrent;
            load += r * r;
        }
        load /= 3;
        Heat += (load - Heat) * Math.Min(1, dt / ThermalTau);
        if (Heat > 2.2)
        {
            Burned = true;
            Speed = 0;
            owner.Visual.Fault = true;
            owner.Sim.ReportFault("motor-burned", Loc.T("problem.motorBurned", owner.Label),
                Loc.T("explain.motorBurned"), [owner.Id], []);
            return true;
        }
        return Math.Abs(Speed - before) > 0.002;
    }

    private static double MoveTowards(double v, double target, double step) =>
        v < target ? Math.Min(target, v + step) : Math.Max(target, v - step);

    private int lastLoggedState;

    /// <summary>Çalıştı/durdu/yön değiştirdi olaylarını günlüğe yazar.</summary>
    public void LogTransitions()
    {
        int state = Math.Abs(Speed) > 0.6 ? Math.Sign(Speed) : 0;
        if (state != lastLoggedState)
        {
            if (state == 0)
            {
                owner.Sim.AddLog(Loc.T("log.motorStopped", owner.Label), owner.Id);
            }
            else
            {
                owner.Sim.AddLog(Loc.T("log.motorRunning", owner.Label,
                    Loc.T(state > 0 ? "dir.right" : "dir.left"), ConnectionText()), owner.Id);
            }
            lastLoggedState = state;
        }
    }

    public string ConnectionText()
    {
        double avg = AverageWindingVoltage();
        if (avg < 10)
        {
            return "";
        }
        string conn = Math.Abs(avg - 230) < Math.Abs(avg - 400) ? Loc.T("conn.star") : Loc.T("conn.delta");
        return string.Format(Loc.Culture, "{0}, {1:0} V/{2}", conn, avg, Loc.T("unit.winding"));
    }

    public void Check(SimComponent c)
    {
        if (Burned)
        {
            return;
        }
        if (Energized && NegativeSeq > 0.3 * PositiveSeq && PositiveSeq > 0.3 * NegativeSeq)
        {
            c.Sim.ReportLive(new SimProblem("motor-phase-loss", ProblemSeverity.Warning,
                Loc.T("problem.motorPhaseLoss", c.Label), Loc.T("explain.motorPhaseLoss"), [c.Id], []));
        }
        if (Energized && MaxWindingVoltage() > 1.2 * WindingVoltage)
        {
            c.Sim.ReportLive(new SimProblem("motor-overvoltage", ProblemSeverity.Warning,
                Loc.T("problem.motorOverVoltage", c.Label, Simulator.FormatVolts(MaxWindingVoltage()),
                    WindingVoltage.ToString("0", CultureInfo.InvariantCulture)),
                Loc.T("explain.motorOverVoltage"), [c.Id], []));
        }
        if (BrakeEngaged && Energized)
        {
            c.Sim.ReportLive(new SimProblem("motor-brake", ProblemSeverity.Warning,
                Loc.T("problem.motorBrake", c.Label), Loc.T("explain.motorBrake"), [c.Id], []));
        }
        if (Heat > 1.3)
        {
            c.Sim.ReportLive(new SimProblem("motor-hot", ProblemSeverity.Warning,
                Loc.T("problem.motorHot", c.Label, (Heat * 100).ToString("0", CultureInfo.InvariantCulture)),
                Loc.T("explain.motorHot"), [c.Id], []));
        }
    }

    public void UpdateVisual(SymbolState v)
    {
        v.Level = Math.Abs(Speed);
        v.Direction = Math.Sign(Speed);
        v.Active = Running;
        v.Fault = Burned;
        v.Warning = Heat > 1.3;
    }

    public IEnumerable<Readout> Readouts()
    {
        yield return new Readout(Loc.T("readout.speed"),
            string.Format(Loc.Culture, "{0:0} {1}", Math.Abs(Speed) * 1450, Loc.T("unit.rpm")));
        yield return new Readout(Loc.T("readout.direction"),
            Running ? Loc.T(Speed > 0 ? "dir.right" : "dir.left") : "—");
        yield return new Readout(Loc.T("readout.windingVoltage"), Simulator.FormatVolts(AverageWindingVoltage()));
        yield return new Readout(Loc.T("readout.connection"), ConnectionText());
        for (int i = 0; i < 3; i++)
        {
            yield return new Readout(Loc.T("readout.windingCurrent", i + 1), Simulator.FormatAmps(windingCurrent[i].Magnitude));
        }
        yield return new Readout(Loc.T("readout.ratedCurrent"), Simulator.FormatAmps(WindingRatedCurrent));
        yield return new Readout(Loc.T("readout.load"), string.Format(Loc.Culture, "%{0:0}", Load * 100));
        yield return new Readout(Loc.T("readout.heat"), string.Format(Loc.Culture, "%{0:0}", Heat * 100));
    }

    public SimSlider LoadSlider => new("slider.load", 0, 200, Load * 100, "%", 5);
}

/// <summary>
/// Motor gövde kaçağı: sargıdan gövdeye (PE klemensine) düşük dirençli yalıtım hatası. PE bağlıysa kaçak akım toprağa
/// akar ve kaçak akım rölesi (ya da büyük akımda sigorta) açar; bağlı değilse gövdede dokunma gerilimi oluşur.
/// </summary>
internal sealed class BodyFault(SimComponent owner, bool initially)
{
    public const double FaultOhms = 20;

    private Complex bodyVoltage;

    public bool Active { get; private set; } = initially;

    public static readonly SimAction Action = new(InteractionKind.Fault, "action.bodyFault");

    public void Stamp(Stamper s, Pin winding, Pin body)
    {
        if (Active)
        {
            s.Resistor(winding, body, FaultOhms);
        }
    }

    public void Read(Solution sol, int bodyNode) => bodyVoltage = sol.NodeAc(bodyNode);

    public void Toggle()
    {
        Active = !Active;
        owner.Sim.AddLog(Loc.T(Active ? "log.bodyFaultOn" : "log.bodyFaultOff", owner.Label), owner.Id,
            Active ? ProblemSeverity.Warning : ProblemSeverity.Info);
    }

    public void Check(int bodyTerminal)
    {
        if (!Active)
        {
            return;
        }
        double touch = bodyVoltage.Magnitude;
        if (touch > 50)
        {
            owner.Sim.ReportLive(new SimProblem("touch-voltage", ProblemSeverity.Error,
                Loc.T("problem.touchVoltage", owner.Label, Simulator.FormatVolts(touch)),
                Loc.T(owner.IsConnected(bodyTerminal) ? "explain.touchVoltageEarthed" : "explain.touchVoltage"), [owner.Id], []));
        }
        else
        {
            owner.Sim.ReportLive(new SimProblem("body-fault", ProblemSeverity.Warning,
                Loc.T("problem.bodyFault", owner.Label), Loc.T("explain.bodyFault"), [owner.Id], []));
        }
    }

    public Readout Readout() => new(Loc.T("readout.touchVoltage"), Active ? Simulator.FormatVolts(bodyVoltage.Magnitude) : "—");
}

/// <summary>3 uçlu üç fazlı motor (iç bağlantı yıldız veya üçgen).</summary>
public sealed class Motor3Def : ComponentDef
{
    public override string TypeId => "motor-3ph";
    public override int? LegacyType => 10;
    public override ComponentCategory Category => ComponentCategory.Motors;
    public override string LabelPrefix => "M";
    public override char? Shortcut => 'M';
    public override string Tags => "üç fazlı motor asenkron 3~ three phase induction motor";
    public override Vec Size => new(40, 60);
    public override Vec DefaultLabelOffset => new(42, 20);

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("U", new Vec(0, 0), Side.Top, "U"),
        new TerminalDef("V", new Vec(20, 0), Side.Top, "V"),
        new TerminalDef("W", new Vec(40, 0), Side.Top, "W"),
        new TerminalDef("PE", new Vec(40, 60), Side.Bottom, "PE")
    ];

    public override int LegacyTerminalCount => 3;

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Number("powerKw", 1.5, "kW", 0.06, 200),
        ParamDef.Choice("connection", "star", "star", "delta"),
        ParamDef.Number("startTime", 1.0, "s", 0.2, 30),
        ParamDef.Number("load", 100, "%", 0, 200),
        ParamDef.Bool("bodyFault", false)
    ];

    public override string? SubLabel(ComponentInstance c) =>
        string.Format(Loc.Culture, "{0:0.##} kW", c.GetDouble("powerKw", 1.5));

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        if (ctx.School)
        {
            // eski EKTS: daire (20,40) r=19, orta uç düz, yan uçlar dairenin üst köşelerine
            var o = new Vec(20, 40);
            const double r = 19;
            p.Line(new Vec(20, 0), new Vec(20, o.Y - r), ink);
            p.Polyline([new Vec(0, 0), new Vec(0, 20), o + new Vec(-r * 0.707, -r * 0.707)], ink);
            p.Polyline([new Vec(40, 0), new Vec(40, 20), o + new Vec(r * 0.707, -r * 0.707)], ink);
            SymbolKit.SchoolMotor(p, ctx, o, r, "M", "3~", ctx.State?.Level ?? 0, ctx.State?.Direction ?? 0, ctx.AnimTime);
            SymbolKit.MotorEarth(p, ink, new Vec(40, 60), o + new Vec(r * 0.707, r * 0.707));
            return;
        }
        p.Polyline([new Vec(0, 0), new Vec(0, 14), new Vec(10, 26)], ink);
        p.Line(new Vec(20, 0), new Vec(20, 22), ink);
        p.Polyline([new Vec(40, 0), new Vec(40, 14), new Vec(30, 26)], ink);
        SymbolKit.Motor(p, ctx, new Vec(20, 38), 15, "M", "3~", (ctx.State?.Level ?? 0) * (ctx.State?.Direction ?? 0), ctx.State?.Direction ?? 0, ctx.AnimTime);
        SymbolKit.MotorEarth(p, ink, new Vec(40, 60), new Vec(20, 38) + new Vec(10.6, 10.6));
    }

    public override SimComponent CreateSim(ComponentInstance instance)
    {
        bool delta = instance.GetParam("connection", "star") == "delta";
        return new Motor3Sim(delta, instance.GetDouble("powerKw", 1.5), instance.GetDouble("startTime", 1.0),
            instance.GetDouble("load", 100) / 100, instance.GetBool("bodyFault", false));
    }
}

/// <summary>Üç fazlı motor simülasyonlarının ortak kısmı: mil yükü kaydırıcısı ve gövde kaçağı.</summary>
internal abstract class InductionMotorSimBase(double load, bool bodyFault) : SimComponent
{
    private InductionMotorModel? model;
    private BodyFault? fault;

    protected abstract InductionMotorModel CreateModel();

    /// <summary>PE klemensinin sırası.</summary>
    protected abstract int PeTerminal { get; }

    protected InductionMotorModel Model => model ??= CreateModel().WithLoad(load);
    private BodyFault Fault => fault ??= new BodyFault(this, bodyFault);

    public override bool NeedsTicks => Model.NeedsTicks;
    public override SimSlider Slider => Model.LoadSlider;
    public override IReadOnlyList<SimAction> Actions => [BodyFault.Action];

    protected abstract void StampWindings(Stamper s);

    public override void Stamp(Stamper s)
    {
        StampWindings(s);
        Fault.Stamp(s, T(0), T(PeTerminal));
    }

    public override bool Evaluate(Solution sol)
    {
        Model.Read(sol);
        Fault.Read(sol, TerminalNodes[PeTerminal]);
        return false;
    }

    public override bool Advance(double dt, Solution sol)
    {
        bool changed = Model.Advance(dt);
        Model.LogTransitions();
        return changed;
    }

    public override void Interact(Interaction action)
    {
        if (action.Kind == InteractionKind.SetValue)
        {
            Model.Load = Math.Clamp(action.Value / 100, 0, 3);
        }
        else if (action.Kind == InteractionKind.Fault)
        {
            Fault.Toggle();
        }
    }

    public override void Check(Solution sol)
    {
        Model.Check(this);
        Fault.Check(PeTerminal);
    }

    public override void UpdateVisual(Solution sol) => Model.UpdateVisual(Visual);

    public override IEnumerable<Readout> Readouts(Solution sol) => Model.Readouts().Append(Fault.Readout());
}

internal sealed class Motor3Sim(bool delta, double powerKw, double startTime, double load, bool bodyFault)
    : InductionMotorSimBase(load, bodyFault)
{
    protected override InductionMotorModel CreateModel() => new(this, delta ? 400 : 230, powerKw, startTime);
    protected override int PeTerminal => 3;

    public override int InternalNodeCount => delta ? 0 : 1;

    protected override void StampWindings(Stamper s)
    {
        (Pin, Pin)[] w = delta
            ? [(T(0), T(1)), (T(1), T(2)), (T(2), T(0))]
            : [(T(0), Internal(0)), (T(1), Internal(0)), (T(2), Internal(0))];
        Model.Stamp(s, w);
    }
}

/// <summary>6 uçlu, yıldız-üçgen bağlanabilen motor. Klemens sırası eski EKTS ile aynı: üstte U1 V1 W1, altta W2 U2 V2.</summary>
public sealed class Motor6Def : ComponentDef
{
    public override string TypeId => "motor-3ph-6t";
    public override int? LegacyType => 19;
    public override ComponentCategory Category => ComponentCategory.Motors;
    public override string LabelPrefix => "M";
    public override string Tags => "yıldız üçgen motor 6 uçlu klemens kutusu star delta motor U1 V1 W1 U V W X Y Z";
    public override Vec Size => new(70, 40);
    public override Vec DefaultLabelOffset => new(73, 0);

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("U1", new Vec(5, 0), Side.Top, "U1"),
        new TerminalDef("V1", new Vec(15, 0), Side.Top, "V1"),
        new TerminalDef("W1", new Vec(25, 0), Side.Top, "W1"),
        new TerminalDef("W2", new Vec(5, 40), Side.Bottom, "W2"),
        new TerminalDef("U2", new Vec(15, 40), Side.Bottom, "U2"),
        new TerminalDef("V2", new Vec(25, 40), Side.Bottom, "V2"),
        new TerminalDef("PE", new Vec(65, 40), Side.Bottom, "PE")
    ];

    public override int LegacyTerminalCount => 6;

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Number("powerKw", 4, "kW", 0.06, 200),
        ParamDef.Choice("windingVoltage", "400", "400", "230"),
        ParamDef.Number("startTime", 2.0, "s", 0.2, 30),
        ParamDef.Number("load", 100, "%", 0, 200),
        ParamDef.Bool("bodyFault", false)
    ];

    public override string? SubLabel(ComponentInstance c) =>
        string.Format(Loc.Culture, "{0:0.##} kW", c.GetDouble("powerKw", 4));

    // Dikey çizilince kablolar klemens kutusunun ve dairenin yanlarından geçer; etiket dairenin altına yazılır
    protected override Vec DefaultLabelOffsetFor(ComponentInstance c, Circuit? circuit) =>
        (c.Rotation & 1) == 1 ? new Vec(2, WorldSize(c).Y + 3) : base.DefaultLabelOffsetFor(c, circuit);

    // Yatay çizilince alttaki W2 U2 V2 kablolarının arasına değil dairenin altına
    public override (Vec Anchor, TextAlign Align) SubLabelPlace(ComponentInstance c, Circuit? circuit = null)
    {
        if ((c.Rotation & 1) == 1)
        {
            return base.SubLabelPlace(c, circuit);
        }
        Vec center = Placement.ToWorld(new Vec(50, 20), Size, c.Rotation, c.Mirror, c.Position);
        return (new Vec(center.X, c.Position.Y + WorldSize(c).Y + 6), TextAlign.Center);
    }

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        if (ctx.School)
        {
            // eski EKTS gibi klemens kutusu daireye bitişik; yazılar IEC 60034-8: üstte U1 V1 W1, altta W2 U2 V2
            var o = new Vec(50, 20);
            const double r = 20;
            double edge = o.X - Math.Sqrt(r * r - 100);
            for (int i = 0; i < 3; i++)
            {
                double x = 5 + 10 * i;
                p.Line(new Vec(x, 0), new Vec(x, 10), ink);
                p.Line(new Vec(x, 30), new Vec(x, 40), ink);
            }
            p.Polyline([new Vec(edge, 10), new Vec(0, 10), new Vec(0, 30), new Vec(edge, 30)], ink);
            string[] up = ["U1", "V1", "W1"], down = ["W2", "U2", "V2"];
            for (int i = 0; i < 3; i++)
            {
                p.Text(new Vec(5 + 10 * i, 14.5), up[i], 4, ink);
                p.Text(new Vec(5 + 10 * i, 25.5), down[i], 4, ink);
            }
            SymbolKit.SchoolMotor(p, ctx, o, r, "M", "3~", ctx.State?.Level ?? 0, ctx.State?.Direction ?? 0, ctx.AnimTime);
            SymbolKit.MotorEarth(p, ink, new Vec(65, 40), new Vec(65, 33.2));
            return;
        }
        p.Rect(new Vec(0, 5), new Vec(30, 30), ink, 1, Paint.Of(Ink.Background));
        string[] top = ["U1", "V1", "W1"];
        string[] bottom = ["W2", "U2", "V2"];
        for (int i = 0; i < 3; i++)
        {
            double x = 5 + 10 * i;
            p.Line(new Vec(x, 0), new Vec(x, 8), ink);
            p.Line(new Vec(x, 32), new Vec(x, 40), ink);
            SymbolKit.Terminal(p, ctx, new Vec(x, 9.5));
            SymbolKit.Terminal(p, ctx, new Vec(x, 30.5));
            p.Text(new Vec(x, 16), top[i], 3.6, ink);
            p.Text(new Vec(x, 26.5), bottom[i], 3.6, ink);
        }
        p.Line(new Vec(30, 20), new Vec(38, 20), ink);
        SymbolKit.Motor(p, ctx, new Vec(53, 20), 14, "M", "3~", (ctx.State?.Level ?? 0) * (ctx.State?.Direction ?? 0), ctx.State?.Direction ?? 0, ctx.AnimTime);
        SymbolKit.MotorEarth(p, ink, new Vec(65, 40), new Vec(62.9, 29.9));
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new Motor6Sim(instance.GetDouble("windingVoltage", 400), instance.GetDouble("powerKw", 4), instance.GetDouble("startTime", 2),
            instance.GetDouble("load", 100) / 100, instance.GetBool("bodyFault", false));
}

internal sealed class Motor6Sim(double windingVoltage, double powerKw, double startTime, double load, bool bodyFault)
    : InductionMotorSimBase(load, bodyFault)
{
    protected override InductionMotorModel CreateModel() => new(this, windingVoltage, powerKw, startTime);
    protected override int PeTerminal => 6;

    // Sargılar: U1-U2, V1-V2, W1-W2
    protected override void StampWindings(Stamper s) => Model.Stamp(s, [(T(0), T(4)), (T(1), T(5)), (T(2), T(3))]);
}

/// <summary>
/// Bir fazlı kondansatörlü motor: ana sargı (U1-U2) ve kondansatörlü yardımcı sargı (Z1-Z2).
/// Yön, yardımcı sargı akımının ana sargıya göre ileride/geride olmasından bulunur.
/// </summary>
public class Motor1Def : ComponentDef
{
    public override string TypeId => "motor-1ph";
    public override int? LegacyType => 18;
    public override ComponentCategory Category => ComponentCategory.Motors;
    public override string LabelPrefix => "M";
    public override string Tags => "bir fazlı motor monofaze yardımcı sargı kondansatörlü single phase motor";
    public override Vec Size => new(80, 100);
    public override Vec DefaultLabelOffset => new(62, 34);

    protected virtual bool CentrifugalSwitch => false;

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("U1", new Vec(0, 20), Side.Left, "U1"),
        new TerminalDef("U2", new Vec(80, 20), Side.Right, "U2"),
        new TerminalDef("Z1", new Vec(0, CentrifugalSwitch ? 30 : 80), Side.Left, "Z1"),
        new TerminalDef("Z2", new Vec(80, 80), Side.Right, "Z2")
    ];

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Number("powerKw", 0.37, "kW", 0.05, 5),
        ParamDef.Number("startTime", 0.8, "s", 0.2, 20)
    ];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        if (ctx.School)
        {
            DrawSchool(p, ctx, ink);
            return;
        }
        var c = new Vec(40, 50);
        // ana sargı
        p.Polyline([new Vec(0, 20), new Vec(22, 20), new Vec(29, 36)], ink);
        p.Polyline([new Vec(80, 20), new Vec(58, 20), new Vec(51, 36)], ink);
        // yardımcı sargı (kondansatörlü)
        double z1y = CentrifugalSwitch ? 30 : 80;
        p.Polyline([new Vec(0, z1y), new Vec(10, z1y), new Vec(10, 80), new Vec(22, 80), new Vec(29, 64)], ink);
        p.Polyline([new Vec(80, 80), new Vec(58, 80), new Vec(51, 64)], ink);
        p.Line(new Vec(14, 86), new Vec(14, 74), ink);
        p.Line(new Vec(17, 86), new Vec(17, 74), ink);
        if (CentrifugalSwitch)
        {
            bool closed = ctx.State == null || ctx.State.Closed.Length == 0 || ctx.State.Closed[0];
            p.Line(new Vec(10, 52), new Vec(10, 58), ink);
            p.Line(new Vec(10, 58), closed ? new Vec(10, 66) : new Vec(5, 65), SymbolKit.ActiveInk(ctx, closed));
            p.Line(new Vec(10, 66), new Vec(10, 72), ink);
            p.Text(new Vec(4, 62), "n", 4, ink);
        }
        p.Text(new Vec(6, 15), "U1", 4, ink);
        p.Text(new Vec(74, 15), "U2", 4, ink);
        p.Text(new Vec(6, z1y - 5), "Z1", 4, ink);
        p.Text(new Vec(74, 75), "Z2", 4, ink);
        SymbolKit.Motor(p, ctx, c, 16, "M", "1~", (ctx.State?.Level ?? 0), ctx.State?.Direction ?? 0, ctx.AnimTime);
    }

    /// <summary>Eski EKTS çizimi: yay biçimli ana (A.S.) ve yardımcı (Y.S.) sargı, kondansatör C1, kafes rotor.</summary>
    private void DrawSchool(ISymbolPainter p, DrawContext ctx, Paint ink)
    {
        bool sim = ctx.Simulating;
        Paint winding = sim && ctx.State!.Active ? Paint.Of(Ink.Active) : ink;
        p.Line(new Vec(0, 20), new Vec(30, 20), ink);
        SymbolKit.Winding(p, winding, new Vec(30, 20), 40);
        p.Line(new Vec(70, 20), new Vec(80, 20), ink);
        p.Text(new Vec(50, 9), "A.S.", 6, ink);
        double z1y = CentrifugalSwitch ? 30 : 80;
        if (CentrifugalSwitch)
        {
            bool closed = ctx.State == null || ctx.State.Closed.Length == 0 || ctx.State.Closed[0];
            p.Line(new Vec(0, 30), new Vec(5, 30), ink);
            p.Line(new Vec(5, 30), new Vec(5, 46), ink);
            p.Line(new Vec(5, 46), closed ? new Vec(5, 56) : new Vec(11, 54), SymbolKit.ActiveInk(ctx, closed));
            p.Line(new Vec(5, 56), new Vec(5, 80), ink);
            p.Text(new Vec(13, 50), "n", 5, ink);
        }
        else
        {
            p.Line(new Vec(0, z1y), new Vec(5, z1y), ink);
        }
        SymbolKit.Capacitor(p, ink, new Vec(5, 80), new Vec(30, 80));
        SymbolKit.Winding(p, winding, new Vec(30, 80), 40);
        p.Line(new Vec(70, 80), new Vec(80, 80), ink);
        p.Text(new Vec(50, 93), "Y.S.", 6, ink);
        p.Text(new Vec(17, 92), "C1", 5.5, ink);
        SymbolKit.CageRotor(p, ctx, new Vec(50, 50), 19, ctx.State?.Level ?? 0, ctx.State?.Direction ?? 0, ctx.AnimTime);
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new Motor1Sim(instance.GetDouble("powerKw", 0.37), instance.GetDouble("startTime", 0.8), CentrifugalSwitch);
}

public sealed class Motor1CsDef : Motor1Def
{
    public override string TypeId => "motor-1ph-cs";
    public override int? LegacyType => 21;
    public override string Tags => "bir fazlı motor santrifüj anahtarlı otomatik anahtar single phase centrifugal switch";
    protected override bool CentrifugalSwitch => true;
}

internal sealed class Motor1Sim : SimComponent
{
    private readonly double ratedCurrent;
    private readonly double startTime;
    private readonly bool centrifugal;
    private int mainBranch = -1, auxBranch = -1;
    private Complex mainI, auxI;
    private double speed;
    private double heat;
    private bool burned;
    private bool auxClosed = true;
    private int lastLogged;

    public Motor1Sim(double powerKw, double startTime, bool centrifugal)
    {
        ratedCurrent = Math.Max(0.2, powerKw * 1000 / (230 * 0.7 * 0.75));
        this.startTime = Math.Max(0.2, startTime);
        this.centrifugal = centrifugal;
    }

    public override bool NeedsTicks => mainI.Magnitude > 0.01 || Math.Abs(speed) > 0.001 || heat > 0.05;

    private double K()
    {
        double slip = Math.Clamp(1 - Math.Abs(speed), 0, 1);
        return 1 + 5 * Math.Pow(Math.Clamp(slip / 0.9, 0, 1), 1.5);
    }

    public override void Stamp(Stamper s)
    {
        if (burned)
        {
            mainBranch = s.Switch(T(0), T(1), false);
            auxBranch = s.Switch(T(2), T(3), false);
            return;
        }
        double zRun = 230 / ratedCurrent;
        Complex zm = Complex.FromPolarCoordinates(zRun / K(), 0.72 + 0.4 * (K() - 1) / 5);
        mainBranch = s.Impedance(T(0), T(1), zm, zm.Real);
        // Yardımcı sargı + seri kondansatör: akım ana sargıdan ~100° ileride
        var za = new Complex(zRun * 1.05, zRun * 0.5) + new Complex(0, -zRun * 2.6);
        auxBranch = auxClosed ? s.Impedance(T(2), T(3), za, double.PositiveInfinity) : s.Switch(T(2), T(3), false);
    }

    public override bool Evaluate(Solution sol)
    {
        mainI = sol.CurrentAc(mainBranch);
        auxI = sol.CurrentAc(auxBranch);
        return false;
    }

    private double TorqueNorm()
    {
        if (mainI.Magnitude < 1e-3 || auxI.Magnitude < 1e-3)
        {
            return 0;
        }
        double dphi = auxI.Phase - mainI.Phase;
        return mainI.Magnitude * auxI.Magnitude * Math.Sin(dphi) / (ratedCurrent * ratedCurrent * 0.35);
    }

    public override bool Advance(double dt, Solution sol)
    {
        if (burned)
        {
            return false;
        }
        double before = speed;
        double tq = TorqueNorm();
        bool mainOn = mainI.Magnitude > 0.2 * ratedCurrent;
        if (Math.Abs(tq) > 0.3 && mainOn)
        {
            speed = Move(speed, Math.Sign(tq), Math.Min(1.5, Math.Abs(tq)) * dt / startTime);
        }
        else if (mainOn && Math.Abs(speed) > 0.3)
        {
            speed = Move(speed, Math.Sign(speed), dt / startTime);
        }
        else
        {
            speed = Move(speed, 0, dt / 2.0);
        }
        if (centrifugal)
        {
            bool closedNow = auxClosed ? Math.Abs(speed) < 0.75 : Math.Abs(speed) < 0.6;
            if (closedNow != auxClosed)
            {
                auxClosed = closedNow;
                before = double.NaN;
            }
        }
        double r = mainI.Magnitude / ratedCurrent;
        heat += (r * r - heat) * Math.Min(1, dt / 60);
        if (heat > 2.2)
        {
            burned = true;
            speed = 0;
            Visual.Fault = true;
            Sim.ReportFault("motor-burned", L("problem.motorBurned", Label), L("explain.motor1Stalled"), [Id], []);
            return true;
        }
        int state = Math.Abs(speed) > 0.6 ? Math.Sign(speed) : 0;
        if (state != lastLogged)
        {
            Sim.AddLog(state == 0 ? L("log.motorStopped", Label) : L("log.motorRunning", Label, L(state > 0 ? "dir.right" : "dir.left"), "1~"), Id);
            lastLogged = state;
        }
        return double.IsNaN(before) || Math.Abs(speed - before) > 0.002;
    }

    private static double Move(double v, double target, double step) =>
        v < target ? Math.Min(target, v + step) : Math.Max(target, v - step);

    public override void Check(Solution sol)
    {
        if (!burned && mainI.Magnitude > 0.2 * ratedCurrent && Math.Abs(speed) < 0.1 && Math.Abs(TorqueNorm()) <= 0.3)
        {
            Sim.ReportLive(new SimProblem("motor1-no-start", ProblemSeverity.Warning,
                L("problem.motor1NoStart", Label), L("explain.motor1Stalled"), [Id], []));
        }
    }

    public override void UpdateVisual(Solution sol)
    {
        Visual.Level = Math.Abs(speed);
        Visual.Direction = Math.Sign(speed);
        Visual.Active = Math.Abs(speed) > 0.05;
        Visual.Fault = burned;
        Visual.Closed = [auxClosed];
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L("readout.speed"), string.Format(Loc.Culture, "{0:0} {1}", Math.Abs(speed) * 1400, L("unit.rpm")));
        yield return new Readout(L("readout.direction"), Math.Abs(speed) > 0.05 ? L(speed > 0 ? "dir.right" : "dir.left") : "—");
        yield return new Readout(L("readout.mainCurrent"), Simulator.FormatAmps(mainI.Magnitude));
        yield return new Readout(L("readout.auxCurrent"), Simulator.FormatAmps(auxI.Magnitude));
        yield return new Readout(L("readout.heat"), string.Format(Loc.Culture, "%{0:0}", heat * 100));
    }
}
