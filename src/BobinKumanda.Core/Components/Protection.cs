using System.Globalization;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Components;

/// <summary>Akım × zaman ısınması (bimetal, sargı): dθ/dt = ((I/In)² − θ)/τ. θ = 1 anma akımında kararlı sıcaklık.</summary>
internal struct Thermal
{
    public double Theta;

    public void Advance(double ratio, double dt, double tauHeat, double tauCool)
    {
        double target = ratio * ratio;
        double tau = target >= Theta ? tauHeat : tauCool;
        Theta += (target - Theta) * Math.Min(1, dt / tau);
    }
}

/// <summary>Neden açtığı: kısa devre (manyetik), aşırı akım (termik), kaçak akım, test düğmesi, elle.</summary>
internal enum TripCause { None, Magnetic, Thermal, Leakage, Test }

/// <summary>
/// Kutuplarından geçen akımı ölçerek açan koruma elemanlarının ortak modeli: W otomatı, sigorta, motor koruma şalteri.
/// Kısa devrede çözüm sırasında anında açar; böylece korumalı yoldaki kısa devre simülasyonu durdurmaz.
/// </summary>
internal class BreakerSim : SimComponent, IDeviceMaster
{
    private readonly int poles;
    private readonly double magneticFactor;
    private readonly double tauHeat;
    private readonly double tripTheta;
    private readonly bool melts;
    private readonly int[] branches;
    private Thermal heat;
    private bool on;
    private double amps;
    private double tripAmps;
    private string[] tripWires = [];

    public BreakerSim(int poles, double ratedAmps, double magneticFactor, double tauHeat, double tripTheta, bool melts, bool startOn)
    {
        this.poles = poles;
        RatedAmps = Math.Max(0.05, ratedAmps);
        this.magneticFactor = magneticFactor;
        this.tauHeat = tauHeat;
        this.tripTheta = tripTheta;
        this.melts = melts;
        on = startOn;
        branches = new int[poles];
    }

    public double RatedAmps { get; }
    public TripCause Cause { get; private set; }
    public bool Tripped => Cause != TripCause.None;
    public bool Closed => on && !Tripped;

    SimComponent IDeviceMaster.Component => this;
    public string DefaultChannel => "main";
    public bool ChannelActive(string channel) => channel == "tripped" ? Tripped : Closed;

    public override bool IsInteractive => true;
    public override bool NeedsTicks => Closed && amps > 1.05 * RatedAmps || heat.Theta > 0.02;

    public override IReadOnlyList<SimAction> Actions =>
        melts ? [new SimAction(InteractionKind.Reset, "action.replaceFuse")] : [new SimAction(InteractionKind.Toggle, "action.lever")];

    public override void Stamp(Stamper s)
    {
        for (int i = 0; i < poles; i++)
        {
            branches[i] = s.Switch(T(2 * i), T(2 * i + 1), Closed);
        }
    }

    public override bool Evaluate(Solution sol)
    {
        amps = 0;
        for (int i = 0; i < poles; i++)
        {
            amps = Math.Max(amps, Math.Max(sol.CurrentAc(branches[i]).Magnitude, Math.Abs(sol.CurrentDc(branches[i]))));
        }
        if (Closed && amps > magneticFactor * RatedAmps)
        {
            // Kısa devre yolunu sakla: kablolar sorun panelinde yanıp sönsün
            double limit = 0.5 * magneticFactor * RatedAmps;
            tripWires = WireCurrents.Compute(Sim.Netlist, sol, Sim.Components)
                .Where(kv => kv.Value.Amps > limit).Select(kv => kv.Key).ToArray();
            Trip(TripCause.Magnetic, amps);
            return true;
        }
        return false;
    }

    public override bool Advance(double dt, Solution sol)
    {
        heat.Advance(Closed ? amps / RatedAmps : 0, dt, tauHeat, melts ? tauHeat : tauHeat / 3);
        if (Closed && heat.Theta > tripTheta)
        {
            tripWires = [];
            Trip(TripCause.Thermal, amps);
            return true;
        }
        return false;
    }

    private void Trip(TripCause cause, double atAmps)
    {
        Cause = cause;
        tripAmps = atAmps;
        string key = melts ? "log.fuseBlown" : cause == TripCause.Magnetic ? "log.tripShort" : "log.tripOverload";
        Sim.AddLog(L(key, Label, Simulator.FormatAmps(atAmps)), Id, ProblemSeverity.Warning);
    }

    public override void Interact(Interaction action)
    {
        switch (action.Kind)
        {
            case InteractionKind.Press or InteractionKind.Toggle:
                if (Tripped)
                {
                    Rearm();
                }
                else
                {
                    on = !on;
                    Sim.AddLog(L(on ? "log.switchOn" : "log.switchOff", Label), Id);
                }
                break;
            case InteractionKind.Reset:
                Rearm();
                break;
            case InteractionKind.SetValue:
                if (action.Value > 0.5 != on)
                {
                    on = action.Value > 0.5;
                    Sim.AddLog(L(on ? "log.switchOn" : "log.switchOff", Label), Id);
                }
                break;
        }
    }

    private void Rearm()
    {
        if (!Tripped)
        {
            return;
        }
        if (melts)
        {
            heat.Theta = 0;
            Sim.AddLog(L("log.fuseReplaced", Label), Id);
        }
        else
        {
            Sim.AddLog(L("log.rearmed", Label), Id);
        }
        Cause = TripCause.None;
        on = true;
        tripWires = [];
    }

    public override void Check(Solution sol)
    {
        if (!Tripped)
        {
            return;
        }
        bool shortCircuit = Cause == TripCause.Magnetic;
        Sim.ReportLive(new SimProblem(melts ? "fuse-blown" : "breaker-tripped", ProblemSeverity.Warning,
            L(shortCircuit ? "problem.tripShort" : "problem.tripOverload", Label, Simulator.FormatAmps(tripAmps)),
            L(shortCircuit ? "explain.tripShort" : "explain.tripOverload"), [Id], tripWires));
    }

    public override void UpdateVisual(Solution sol)
    {
        Visual.Closed = Enumerable.Repeat(Closed, poles).ToArray();
        Visual.Tripped = Tripped;
        Visual.Active = Closed;
        Visual.Text = Tripped ? L(melts ? "state.blown" : "state.tripped") : null;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L("readout.current"), Simulator.FormatAmps(Closed ? amps : 0));
        yield return new Readout(L("readout.rated"), Simulator.FormatAmps(RatedAmps));
        yield return new Readout(L("readout.heat"), string.Format(Loc.Culture, "%{0:0}", 100 * heat.Theta / tripTheta));
        yield return new Readout(L("readout.state"),
            L(Tripped ? (melts ? "state.blown" : "state.tripped") : on ? "state.on" : "state.off"));
    }
}

/// <summary>Koruma elemanlarının ortak çizim parçaları.</summary>
internal static class ProtectionSymbols
{
    /// <summary>Şalter kutbu: (0,y)-(20,y), bıçak, sabit kontakta × (kesici işareti). Döner: bıçağın ortası.</summary>
    public static Vec BreakerPole(ISymbolPainter p, DrawContext ctx, double y, bool closed, bool cross = true)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        var pivot = new Vec(6, y);
        var fixedEnd = new Vec(14, y);
        p.Line(new Vec(0, y), pivot, ink);
        p.Line(fixedEnd, new Vec(20, y), ink);
        Vec tip = closed ? new Vec(14.5, y) : new Vec(14, y - 5.5);
        Paint blade = ctx.State is { Tripped: true } ? Paint.Of(Ink.Fault) : SymbolKit.ActiveInk(ctx, closed);
        p.Line(pivot, tip, blade, closed && ctx.Simulating ? 1.4 : 1);
        if (cross)
        {
            p.Line(fixedEnd + new Vec(-1.6, -1.6), fixedEnd + new Vec(1.6, 1.6), ink, 0.8);
            p.Line(fixedEnd + new Vec(-1.6, 1.6), fixedEnd + new Vec(1.6, -1.6), ink, 0.8);
        }
        return (pivot + tip) / 2;
    }

    /// <summary>Kutbun üstünde açtırma işaretleri: termik (⊓) ve manyetik (I&gt;) küçük kutular.</summary>
    public static void TripMarks(ISymbolPainter p, Paint ink, Vec bladeMid, double topY, bool thermal, bool magnetic)
    {
        p.Line(bladeMid, new Vec(bladeMid.X, topY + 3), ink, 0.8, dashed: true);
        double x = bladeMid.X;
        if (thermal && magnetic)
        {
            p.Rect(new Vec(x - 7, topY - 1), new Vec(6, 4), ink, 0.7);
            p.Polyline([new Vec(x - 6, topY + 2), new Vec(x - 6, topY), new Vec(x - 2, topY), new Vec(x - 2, topY + 2)], ink, 0.6);
            p.Rect(new Vec(x + 1, topY - 1), new Vec(6, 4), ink, 0.7);
            p.Text(new Vec(x + 4, topY + 2.2), "I>", 3, ink);
        }
        else if (magnetic)
        {
            p.Rect(new Vec(x - 3, topY - 1), new Vec(6, 4), ink, 0.7);
            p.Text(new Vec(x, topY + 2.2), "I>", 3, ink);
        }
        else
        {
            p.Rect(new Vec(x - 3, topY - 1), new Vec(6, 4), ink, 0.7);
            p.Polyline([new Vec(x - 2, topY + 2), new Vec(x - 2, topY), new Vec(x + 2, topY), new Vec(x + 2, topY + 2)], ink, 0.6);
        }
    }

    /// <summary>Termik eleman (bimetal ısıtıcı): kablo üstünde ⊓ biçimi.</summary>
    public static Vec Heater(ISymbolPainter p, Paint ink, double y)
    {
        p.Polyline([new Vec(0, y), new Vec(6, y), new Vec(6, y - 5), new Vec(14, y - 5), new Vec(14, y), new Vec(20, y)], ink);
        return new Vec(10, y - 5);
    }

    /// <summary>Sigorta: dikdörtgen gövde içinden geçen iletken; atmışsa iletken kopuk.</summary>
    public static void Fuse(ISymbolPainter p, DrawContext ctx, double y, bool blown)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        Paint body = blown && ctx.Simulating ? Paint.Of(Ink.Fault) : ink;
        p.Line(new Vec(0, y), new Vec(4, y), ink);
        p.Line(new Vec(16, y), new Vec(20, y), ink);
        p.Rect(new Vec(4, y - 3), new Vec(12, 6), body, 1, blown && ctx.Simulating ? Paint.Of(Ink.Fault, 0.12) : null);
        if (blown && ctx.Simulating)
        {
            p.Line(new Vec(4, y), new Vec(8.5, y), body);
            p.Line(new Vec(11.5, y), new Vec(16, y), body);
        }
        else
        {
            p.Line(new Vec(4, y), new Vec(16, y), SymbolKit.ActiveInk(ctx, ctx.Simulating));
        }
    }
}

/// <summary>Koruma elemanları için ortak: F öneki, Koruma kategorisi, akım parametresi ve etiket altında değer.</summary>
public abstract class ProtectionDefBase : ComponentDef
{
    public override ComponentCategory Category => ComponentCategory.Protection;
    public override string LabelPrefix => "F";
    public override DeviceRole Role => DeviceRole.Master;
    public override Vec DefaultLabelOffset => Poles == 1 ? new Vec(4, -10) : new Vec(22, 30);

    protected abstract int Poles { get; }

    public override Vec Size => Poles == 1 ? new Vec(20, 20) : new Vec(20, 25 * Poles - 5);

    /// <summary>Kutup i'nin y konumu: tek kutupta 10, çok kutupta 10, 35, 60, 85.</summary>
    protected static double PoleY(int i) => 10 + 25 * i;

    /// <summary>Kutup numaraları: 1-2, 3-4, 5-6, N-N (son kutup nötr olan kaçak akım rölesinde).</summary>
    protected virtual string PoleNumber(int pole, bool output) => (2 * pole + (output ? 2 : 1)).ToString(CultureInfo.InvariantCulture);

    public override IReadOnlyList<TerminalDef> Terminals
    {
        get
        {
            var list = new TerminalDef[2 * Poles];
            for (int i = 0; i < Poles; i++)
            {
                list[2 * i] = new TerminalDef((2 * i + 1).ToString(CultureInfo.InvariantCulture), new Vec(0, PoleY(i)), Side.Left, PoleNumber(i, false));
                list[2 * i + 1] = new TerminalDef((2 * i + 2).ToString(CultureInfo.InvariantCulture), new Vec(20, PoleY(i)), Side.Right, PoleNumber(i, true));
            }
            return list;
        }
    }
}

/// <summary>W otomatı (minyatür devre kesici), B/C/D eğrisi.</summary>
public abstract class McbDefBase : ProtectionDefBase
{
    public static readonly string[] Ratings = ["2", "4", "6", "10", "16", "20", "25", "32", "40", "63"];

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Choice("ratedCurrent", "16", Ratings),
        ParamDef.Choice("curve", "C", "B", "C", "D")
    ];

    public override string? SubLabel(ComponentInstance c) =>
        c.GetParam("curve", "C") + c.GetParam("ratedCurrent", "16");

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        Vec first = default, last = default;
        for (int i = 0; i < Poles; i++)
        {
            Vec mid = ProtectionSymbols.BreakerPole(p, ctx, PoleY(i), ctx.IsClosed(i, false));
            if (i == 0)
            {
                first = mid;
            }
            last = mid;
        }
        if (Poles > 1)
        {
            SymbolKit.MechanicalLink(p, ctx, first, last);
        }
        ProtectionSymbols.TripMarks(p, ink, first, 1, thermal: true, magnetic: true);
    }

    public override SimComponent CreateSim(ComponentInstance instance)
    {
        double factor = instance.GetParam("curve", "C") switch { "B" => 4, "D" => 15, _ => 8 };
        return new BreakerSim(Poles, instance.GetDouble("ratedCurrent", 16), factor, tauHeat: 120, tripTheta: 1.69, melts: false, startOn: true);
    }
}

public sealed class Mcb1Def : McbDefBase
{
    public override string TypeId => "mcb-1p";
    public override string Tags => "w otomatı sigorta otomatı minyatür devre kesici mcb circuit breaker B16 C16";
    protected override int Poles => 1;
}

public sealed class Mcb3Def : McbDefBase
{
    public override string TypeId => "mcb-3p";
    public override string Tags => "üç fazlı w otomatı 3 kutuplu devre kesici mcb circuit breaker";
    protected override int Poles => 3;
}

/// <summary>Buşonlu / bıçaklı sigorta (gG). Atınca eriyen telin değiştirilmesi gerekir.</summary>
public abstract class FuseDefBase : ProtectionDefBase
{
    public static readonly string[] Ratings = ["1", "2", "4", "6", "10", "16", "20", "25", "32", "63"];

    public override IReadOnlyList<ParamDef> Params => [ParamDef.Choice("ratedCurrent", "10", Ratings)];

    public override string? SubLabel(ComponentInstance c) => c.GetParam("ratedCurrent", "10") + " A";

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        for (int i = 0; i < Poles; i++)
        {
            ProtectionSymbols.Fuse(p, ctx, PoleY(i), ctx.State is { Tripped: true });
        }
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new BreakerSim(Poles, instance.GetDouble("ratedCurrent", 10), 10, tauHeat: 10, tripTheta: 2.56, melts: true, startOn: true);
}

public sealed class Fuse1Def : FuseDefBase
{
    public override string TypeId => "fuse-1p";
    public override string Tags => "sigorta buşon eriyen telli fuse gG";
    protected override int Poles => 1;
}

public sealed class Fuse3Def : FuseDefBase
{
    public override string TypeId => "fuse-3p";
    public override string Tags => "üç fazlı sigorta bıçaklı sigorta fuse 3 pole";
    protected override int Poles => 3;
}

/// <summary>Motor koruma şalteri: termik + manyetik açtırmalı, elle açılıp kapanan 3 kutuplu şalter.</summary>
public sealed class MotorProtectionSwitchDef : ProtectionDefBase
{
    public override string TypeId => "mpcb";
    public override string LabelPrefix => "Q";
    public override string Tags => "motor koruma şalteri motor koruma şalter mpcb motor protection circuit breaker";
    protected override int Poles => 3;

    public override IReadOnlyList<ParamDef> Params => [ParamDef.Number("setCurrent", 3.2, "A", 0.1, 100)];

    public override string? SubLabel(ComponentInstance c) =>
        string.Format(Loc.Culture, "{0:0.##} A", c.GetDouble("setCurrent", 3.2));

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        Vec m1 = ProtectionSymbols.BreakerPole(p, ctx, PoleY(0), ctx.IsClosed(0, false));
        ProtectionSymbols.BreakerPole(p, ctx, PoleY(1), ctx.IsClosed(1, false));
        Vec m3 = ProtectionSymbols.BreakerPole(p, ctx, PoleY(2), ctx.IsClosed(2, false));
        SymbolKit.MechanicalLink(p, ctx, m1, m3);
        ProtectionSymbols.TripMarks(p, ink, m1, 1, thermal: true, magnetic: true);
        SymbolKit.Actuator(p, ctx, ActuatorKind.Rotary, m3, 64, false);
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new BreakerSim(3, instance.GetDouble("setCurrent", 3.2), 13, tauHeat: 240, tripTheta: 1.44, melts: false, startOn: true);
}

/// <summary>Kaçak akım rölesi: kutuplardan giren ve çıkan akımların toplamı (kaçak) eşiği aşınca açar.</summary>
public abstract class RcdDefBase : ProtectionDefBase
{
    public override IReadOnlyList<ParamDef> Params => [ParamDef.Choice("sensitivity", "30", "10", "30", "100", "300")];

    public override string? SubLabel(ComponentInstance c) => c.GetParam("sensitivity", "30") + " mA";

    // Son kutup nötr
    protected override string PoleNumber(int pole, bool output) =>
        pole == Poles - 1 ? (output ? "N'" : "N") : base.PoleNumber(pole, output);

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        Vec first = default, last = default;
        for (int i = 0; i < Poles; i++)
        {
            Vec mid = ProtectionSymbols.BreakerPole(p, ctx, PoleY(i), ctx.IsClosed(i, false), cross: false);
            if (i == 0)
            {
                first = mid;
            }
            last = mid;
        }
        SymbolKit.MechanicalLink(p, ctx, first, last);
        // Toroid akım trafosu: kutupları saran kesikli halka ve IΔ kutusu
        double top = PoleY(0) - 3, bottom = PoleY(Poles - 1) + 3;
        p.Rect(new Vec(16, top), new Vec(3, bottom - top), ink, 0.7, dashed: true);
        p.Text(new Vec(10, 2.5), "IΔ", 4, ink, bold: true);
        p.Line(new Vec(17.5, top), new Vec(17.5, 4), ink, 0.6, dashed: true);
        p.Line(new Vec(13, 4), new Vec(17.5, 4), ink, 0.6, dashed: true);
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new RcdSim(Poles, instance.GetDouble("sensitivity", 30) / 1000);
}

public sealed class Rcd2Def : RcdDefBase
{
    public override string TypeId => "rcd-2p";
    public override string Tags => "kaçak akım rölesi hayat koruma rcd rccb 30 mA faz nötr";
    protected override int Poles => 2;
}

public sealed class Rcd4Def : RcdDefBase
{
    public override string TypeId => "rcd-4p";
    public override string Tags => "üç fazlı kaçak akım rölesi hayat koruma rcd 4 kutuplu";
    protected override int Poles => 4;
}

internal sealed class RcdSim(int poles, double sensitivity) : SimComponent, IDeviceMaster
{
    private readonly int[] branches = new int[poles];
    private bool on = true;
    private TripCause cause;
    private double leakage;
    private double tripLeakage;

    private bool Closed => on && cause == TripCause.None;

    SimComponent IDeviceMaster.Component => this;
    public string DefaultChannel => "main";
    public bool ChannelActive(string channel) => channel == "tripped" ? cause != TripCause.None : Closed;

    public override bool IsInteractive => true;

    public override IReadOnlyList<SimAction> Actions =>
        [new SimAction(InteractionKind.Test, "action.test"), new SimAction(InteractionKind.Toggle, "action.lever")];

    public override void Stamp(Stamper s)
    {
        for (int i = 0; i < poles; i++)
        {
            branches[i] = s.Switch(T(2 * i), T(2 * i + 1), Closed);
        }
    }

    public override bool Evaluate(Solution sol)
    {
        System.Numerics.Complex sum = 0;
        double dc = 0;
        for (int i = 0; i < poles; i++)
        {
            sum += sol.CurrentAc(branches[i]);
            dc += sol.CurrentDc(branches[i]);
        }
        leakage = Math.Max(sum.Magnitude, Math.Abs(dc));
        if (Closed && leakage > sensitivity)
        {
            Trip(TripCause.Leakage);
            return true;
        }
        return false;
    }

    private void Trip(TripCause c)
    {
        cause = c;
        tripLeakage = leakage;
        Sim.AddLog(c == TripCause.Test ? L("log.rcdTest", Label) : L("log.rcdTrip", Label, Simulator.FormatAmps(leakage)), Id,
            ProblemSeverity.Warning);
    }

    public override void Interact(Interaction action)
    {
        switch (action.Kind)
        {
            case InteractionKind.Test when Closed:
                Trip(TripCause.Test);
                break;
            case InteractionKind.Press or InteractionKind.Toggle or InteractionKind.Reset:
                if (cause != TripCause.None)
                {
                    cause = TripCause.None;
                    on = true;
                    Sim.AddLog(L("log.rearmed", Label), Id);
                }
                else if (action.Kind != InteractionKind.Reset)
                {
                    on = !on;
                    Sim.AddLog(L(on ? "log.switchOn" : "log.switchOff", Label), Id);
                }
                break;
        }
    }

    public override void Check(Solution sol)
    {
        if (cause == TripCause.Leakage)
        {
            Sim.ReportLive(new SimProblem("rcd-tripped", ProblemSeverity.Warning,
                L("problem.rcdTrip", Label, Simulator.FormatAmps(tripLeakage)), L("explain.rcdTrip"), [Id], []));
        }
    }

    public override void UpdateVisual(Solution sol)
    {
        Visual.Closed = Enumerable.Repeat(Closed, poles).ToArray();
        Visual.Tripped = cause != TripCause.None;
        Visual.Active = Closed;
        Visual.Text = cause != TripCause.None ? L("state.tripped") : null;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L("readout.leakage"), Simulator.FormatAmps(leakage));
        yield return new Readout(L("readout.sensitivity"), Simulator.FormatAmps(sensitivity));
        yield return new Readout(L("readout.state"), L(cause != TripCause.None ? "state.tripped" : on ? "state.on" : "state.off"));
    }
}

/// <summary>Termik aşırı akım rölesi (güç devresindeki 3 ısıtıcı). Kontakları (95-96, 97-98) aynı etiketle ayrı çizilir.</summary>
public sealed class OverloadRelayDef : ProtectionDefBase
{
    public override string TypeId => "overload";
    public override string Tags => "termik röle termik aşırı akım rölesi bimetal overload relay 95 96";
    protected override int Poles => 3;

    public override IReadOnlyList<ParamDef> Params => [ParamDef.Number("setCurrent", 3.2, "A", 0.1, 100)];

    public override string? SubLabel(ComponentInstance c) =>
        string.Format(Loc.Culture, "{0:0.##} A", c.GetDouble("setCurrent", 3.2));

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = ctx.State is { Tripped: true } ? Paint.Of(Ink.Fault) : SymbolKit.InkFor(ctx);
        Vec h1 = ProtectionSymbols.Heater(p, ink, PoleY(0));
        ProtectionSymbols.Heater(p, ink, PoleY(1));
        Vec h3 = ProtectionSymbols.Heater(p, ink, PoleY(2));
        p.Line(h1, h3, SymbolKit.InkFor(ctx), 0.8, dashed: true);
    }

    public override SimComponent CreateSim(ComponentInstance instance) => new OverloadSim(instance.GetDouble("setCurrent", 3.2));
}

internal sealed class OverloadSim(double setAmps) : SimComponent, IDeviceMaster
{
    private const double TripTheta = 1.44;
    private const double ResetTheta = 1.0;
    private readonly int[] branches = new int[3];
    private Thermal heat;
    private double amps;
    private bool tripped;
    private double tripAmps;

    private double Rated => Math.Max(0.05, setAmps);

    SimComponent IDeviceMaster.Component => this;
    public string DefaultChannel => "main";
    public bool ChannelActive(string channel) => tripped;

    public override bool IsInteractive => true;
    public override bool NeedsTicks => amps > 0.01 || heat.Theta > 0.02;

    public override IReadOnlyList<SimAction> Actions =>
        [new SimAction(InteractionKind.Reset, "action.reset"), new SimAction(InteractionKind.Test, "action.test")];

    public override void Stamp(Stamper s)
    {
        for (int i = 0; i < 3; i++)
        {
            branches[i] = s.Switch(T(2 * i), T(2 * i + 1), true);
        }
    }

    public override bool Evaluate(Solution sol)
    {
        amps = 0;
        for (int i = 0; i < 3; i++)
        {
            amps = Math.Max(amps, sol.CurrentAc(branches[i]).Magnitude);
        }
        return false;
    }

    public override bool Advance(double dt, Solution sol)
    {
        heat.Advance(amps / Rated, dt, 240, 60);
        if (!tripped && heat.Theta > TripTheta)
        {
            tripped = true;
            tripAmps = amps;
            Sim.AddLog(L("log.overloadTrip", Label, Simulator.FormatAmps(amps), Simulator.FormatAmps(Rated)), Id, ProblemSeverity.Warning);
            return true;
        }
        return false;
    }

    public override void Interact(Interaction action)
    {
        switch (action.Kind)
        {
            case InteractionKind.Test when !tripped:
                tripped = true;
                tripAmps = 0;
                Sim.AddLog(L("log.overloadTest", Label), Id);
                break;
            case InteractionKind.Press or InteractionKind.Reset when tripped:
                if (heat.Theta > ResetTheta)
                {
                    Sim.AddLog(L("log.overloadHot", Label, CoolingSeconds().ToString("0", Loc.Culture)), Id);
                    break;
                }
                tripped = false;
                Sim.AddLog(L("log.overloadReset", Label), Id);
                break;
        }
    }

    /// <summary>Akım yokken θ'nın reset eşiğine düşmesi için gereken süre.</summary>
    private double CoolingSeconds() => heat.Theta <= ResetTheta ? 0 : 60 * Math.Log(heat.Theta / ResetTheta);

    public override void Check(Solution sol)
    {
        if (tripped && tripAmps > 0)
        {
            Sim.ReportLive(new SimProblem("overload-tripped", ProblemSeverity.Warning,
                L("problem.overloadTrip", Label, Simulator.FormatAmps(tripAmps)), L("explain.overloadTrip"), [Id], []));
        }
    }

    public override void UpdateVisual(Solution sol)
    {
        Visual.Tripped = tripped;
        Visual.Active = tripped;
        Visual.Warning = !tripped && heat.Theta > 1;
        Visual.Text = tripped ? L("state.tripped") : null;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L("readout.current"), Simulator.FormatAmps(amps));
        yield return new Readout(L("readout.setCurrent"), Simulator.FormatAmps(Rated));
        yield return new Readout(L("readout.heat"), string.Format(Loc.Culture, "%{0:0}", 100 * heat.Theta / TripTheta));
        yield return new Readout(L("readout.state"), L(tripped ? "state.tripped" : "state.rest"));
        if (tripped && CoolingSeconds() > 0)
        {
            yield return new Readout(L("readout.coolDown"), string.Format(Loc.Culture, "{0:0} s", CoolingSeconds()));
        }
    }
}

/// <summary>Termik rölenin yardımcı kontakları: 95-96 (NC, kumanda devresine seri) ve 97-98 (NO, arıza lambası).</summary>
public sealed class OverloadContactsDef : FollowerContactDef
{
    public override string TypeId => "overload-contacts";
    public override string LabelPrefix => "F";
    public override string Tags => "termik kontağı 95 96 97 98 termik röle kontak overload contact";
    public override Vec Size => new(20, 30);

    public override IReadOnlyList<ParamDef> Params => [];

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("95", new Vec(0, 12), Side.Left, "95"), new TerminalDef("96", new Vec(20, 12), Side.Right, "96"),
        new TerminalDef("97", new Vec(0, 26), Side.Left, "97"), new TerminalDef("98", new Vec(20, 26), Side.Right, "98")
    ];

    protected override ContactSpec[] Specs => [new(0, 1, true), new(2, 3, false)];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        // Okul tarzının uzun plakaları iki kontağı üst üste bindirir; bu sembol her tarzda bıçaklı çizilir
        ctx = ctx.WithStyle(SymbolStyle.Iec);
        Paint ink = SymbolKit.InkFor(ctx);
        Vec m1 = SymbolKit.ContactH(p, ctx, 12, true, ctx.IsClosed(0, true));
        Vec m2 = SymbolKit.ContactH(p, ctx, 26, false, ctx.IsClosed(1, false));
        var top = new Vec(m1.X, 1);
        SymbolKit.MechanicalLink(p, ctx, m2, new Vec(top.X, top.Y + 1.5));
        // termik çalıştırıcı işareti
        p.Polyline([new Vec(top.X - 3, top.Y + 1.5), new Vec(top.X - 3, top.Y - 0.5), new Vec(top.X + 3, top.Y - 0.5), new Vec(top.X + 3, top.Y + 1.5)], ink, 0.8);
    }

    public override SimComponent CreateSim(ComponentInstance instance) => new FollowerContactSim(Specs, "main");
}
