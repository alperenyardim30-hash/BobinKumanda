using System.Globalization;
using System.Numerics;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Components;

/// <summary>Sayıcı: sayma bobinine (A1-A2) gelen her darbeyi sayar, ayar değerine ulaşınca kontaklarını değiştirir. R1-R2 sıfırlar.</summary>
public sealed class CounterDef : ComponentDef
{
    public override string TypeId => "counter";
    public override ComponentCategory Category => ComponentCategory.Relays;
    public override DeviceRole Role => DeviceRole.Master;
    public override string LabelPrefix => "K";
    public override Vec Size => new(20, 30);
    public override Vec DefaultLabelOffset => new(4, -10);
    public override string Tags => "sayıcı sayaç adet sayma counter";

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("A1", new Vec(0, 10), Side.Left, "A1"), new TerminalDef("A2", new Vec(20, 10), Side.Right, "A2"),
        new TerminalDef("R1", new Vec(0, 26), Side.Left, "R1"), new TerminalDef("R2", new Vec(20, 26), Side.Right, "R2")
    ];

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Choice("coilVoltage", "230AC", CoilModel.VoltageChoices),
        ParamDef.Integer("preset", 5, 1, 9999)
    ];

    public override string? SubLabel(ComponentInstance c) => "n = " + c.GetInt("preset", 5).ToString(CultureInfo.InvariantCulture);

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        DrawContext iec = ctx.WithStyle(SymbolStyle.Iec);
        SymbolKit.CoilH(p, iec, 10, ctx.State?.Active == true, SymbolKit.CoilStyle.Counter, boxW: 10, boxH: 9);
        Paint ink = SymbolKit.InkFor(ctx);
        p.Line(new Vec(0, 26), new Vec(6, 26), ink);
        p.Line(new Vec(14, 26), new Vec(20, 26), ink);
        p.Rect(new Vec(6, 22.5), new Vec(8, 7), ink, 0.9, ctx.State?.Position == 1 ? Paint.Of(Ink.Active, 0.35) : null);
        p.Text(new Vec(10, 28), "R", 4.5, ink, bold: true);
        p.Line(new Vec(10, 22.5), new Vec(10, 14.5), ink, 0.6, dashed: true);
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new CounterSim(instance.GetParam("coilVoltage", "230AC"), instance.GetInt("preset", 5));
}

internal sealed class CounterSim(string voltage, int preset) : SimComponent, IDeviceMaster
{
    private CoilModel? countCoil, resetCoil;
    private int count;
    private bool done;

    SimComponent IDeviceMaster.Component => this;
    public string DefaultChannel => "main";
    public bool ChannelActive(string channel) => done;

    public override bool IsInteractive => true;

    public override IReadOnlyList<SimAction> Actions => [new SimAction(InteractionKind.Reset, "action.resetCounter")];

    private void Ensure()
    {
        if (countCoil != null)
        {
            return;
        }
        countCoil = new CoilModel(this, voltage);
        resetCoil = new CoilModel(this, voltage);
        countCoil.Changed += pulled =>
        {
            if (pulled && !resetCoil.Pulled && count < 99999)
            {
                count++;
                Sim.AddLog(L("log.count", Label, count, preset), Id);
                if (!done && count >= preset)
                {
                    done = true;
                    Sim.AddLog(L("log.countDone", Label, preset), Id);
                }
            }
        };
        resetCoil.Changed += pulled =>
        {
            if (pulled)
            {
                Reset();
            }
        };
    }

    private void Reset()
    {
        if (count != 0 || done)
        {
            Sim.AddLog(L("log.countReset", Label), Id);
        }
        count = 0;
        done = false;
    }

    public override void Stamp(Stamper s)
    {
        Ensure();
        countCoil!.Stamp(s, T(0), T(1));
        resetCoil!.Stamp(s, T(2), T(3));
    }

    public override bool Evaluate(Solution sol)
    {
        countCoil!.Evaluate(sol);
        resetCoil!.Evaluate(sol);
        return false;
    }

    public override void Check(Solution sol)
    {
        countCoil!.Check(sol);
        resetCoil!.Check(sol);
    }

    public override void Interact(Interaction action)
    {
        if (action.Kind is InteractionKind.Reset or InteractionKind.Press)
        {
            Reset();
        }
    }

    public override void UpdateVisual(Solution sol)
    {
        Visual.Active = countCoil!.Pulled;
        Visual.Position = resetCoil!.Pulled ? 1 : 0;
        Visual.Fault = countCoil.Burned || resetCoil.Burned;
        Visual.Text = string.Format(CultureInfo.InvariantCulture, "{0} / {1}", count, preset);
        Visual.Warning = done;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L("readout.count"), string.Format(CultureInfo.InvariantCulture, "{0} / {1}", count, preset));
        yield return new Readout(L("readout.timedContacts"), L(done ? "state.switched" : "state.rest"));
        foreach (Readout r in countCoil!.Readouts())
        {
            yield return r;
        }
    }
}

/// <summary>Flaşör rölesi: bobin enerjili kaldıkça kontakları ayarlanan sürelerle açıp kapatır.</summary>
public sealed class FlasherDef : CoilDefBase
{
    public override string TypeId => "flasher";
    public override string Tags => "flaşör flaşör rölesi yanıp sönme ritim rölesi flasher blinker";
    protected override SymbolKit.CoilStyle Style => SymbolKit.CoilStyle.Flasher;

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Choice("coilVoltage", "230AC", CoilModel.VoltageChoices),
        ParamDef.Number("onTime", 0.5, "s", 0.05, 600),
        ParamDef.Number("offTime", 0.5, "s", 0.05, 600)
    ];

    public override string? SubLabel(ComponentInstance c) =>
        string.Format(Loc.Culture, "{0:0.##}/{1:0.##} s", c.GetDouble("onTime", 0.5), c.GetDouble("offTime", 0.5));

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new FlasherSim(instance.GetParam("coilVoltage", "230AC"), instance.GetDouble("onTime", 0.5), instance.GetDouble("offTime", 0.5));
}

internal sealed class FlasherSim(string voltage, double onTime, double offTime) : SimComponent, IDeviceMaster
{
    private CoilModel? coil;
    private bool output;
    private EventHandle? next;

    private CoilModel Coil
    {
        get
        {
            if (coil == null)
            {
                coil = new CoilModel(this, voltage);
                coil.Changed += pulled =>
                {
                    Simulator.Cancel(next);
                    next = null;
                    output = pulled;
                    if (pulled)
                    {
                        ScheduleToggle();
                    }
                };
            }
            return coil;
        }
    }

    SimComponent IDeviceMaster.Component => this;
    public string DefaultChannel => "main";
    public bool ChannelActive(string channel) => channel == "instant" ? Coil.Pulled : output;

    private void ScheduleToggle()
    {
        next = Sim.Schedule(Math.Max(0.02, output ? onTime : offTime), () =>
        {
            next = null;
            if (Coil.Pulled)
            {
                output = !output;
                ScheduleToggle();
            }
        });
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
        Visual.Warning = output;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        foreach (Readout r in Coil.Readouts())
        {
            yield return r;
        }
        yield return new Readout(L("readout.timedContacts"), L(output ? "state.switched" : "state.rest"));
    }
}

/// <summary>
/// Yıldız-üçgen zaman rölesi: bobin enerjilenince 17-18 (yıldız) hemen kapanır; süre dolunca açılır ve kısa bir
/// bekleme (50 ms) sonrası 17-28 (üçgen) kapanır. Bekleme, iki kontaktörün aynı anda kapalı kalıp kısa devre yapmasını önler.
/// </summary>
public sealed class StarDeltaTimerDef : ComponentDef
{
    public override string TypeId => "timer-stardelta";
    public override ComponentCategory Category => ComponentCategory.Relays;
    public override DeviceRole Role => DeviceRole.Master;
    public override string LabelPrefix => "KT";
    public override Vec Size => new(30, 30);
    public override Vec DefaultLabelOffset => new(4, -10);
    public override string Tags => "yıldız üçgen zaman rölesi yıldız üçgen rölesi star delta timer 17 18 28";

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("A1", new Vec(0, 8), Side.Left, "A1"), new TerminalDef("A2", new Vec(30, 8), Side.Right, "A2"),
        new TerminalDef("17", new Vec(0, 22), Side.Left, "17"),
        new TerminalDef("18", new Vec(30, 18), Side.Right, "18"), new TerminalDef("28", new Vec(30, 26), Side.Right, "28")
    ];

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Choice("coilVoltage", "230AC", CoilModel.VoltageChoices),
        ParamDef.Number("time", 5, "s", 0.1, 600)
    ];

    public override string? SubLabel(ComponentInstance c) =>
        string.Format(Loc.Culture, "Y→Δ {0:0.##} s", c.GetDouble("time", 5));

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        DrawContext iec = ctx.WithStyle(SymbolStyle.Iec);
        Paint ink = SymbolKit.InkFor(ctx);
        SymbolKit.CoilH(p, iec, 8, ctx.State?.Active == true, SymbolKit.CoilStyle.OnDelay, x0: 0, x1: 30, boxW: 10, boxH: 8);
        p.Line(new Vec(0, 22), new Vec(8, 22), ink);
        p.Line(new Vec(8, 18), new Vec(8, 26), ink);
        SymbolKit.ContactH(p, iec, 18, false, iec.IsClosed(0, false), x0: 8, x1: 30, timed: TimedMark.OffDelay);
        SymbolKit.ContactH(p, iec, 26, false, iec.IsClosed(1, false), x0: 8, x1: 30);
        p.Text(new Vec(27, 16.5), "Y", 3.6, ink);
        p.Text(new Vec(27, 24.5), "Δ", 3.6, ink);
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new StarDeltaTimerSim(instance.GetParam("coilVoltage", "230AC"), instance.GetDouble("time", 5));
}

internal sealed class StarDeltaTimerSim(string voltage, double seconds) : SimComponent, IDeviceMaster
{
    private const double Pause = 0.05;
    private CoilModel? coil;
    private bool star, delta;
    private EventHandle? timer;
    private double startTime;

    private CoilModel Coil
    {
        get
        {
            if (coil == null)
            {
                coil = new CoilModel(this, voltage);
                coil.Changed += OnCoil;
            }
            return coil;
        }
    }

    SimComponent IDeviceMaster.Component => this;
    public string DefaultChannel => "star";

    public bool ChannelActive(string channel) => channel switch
    {
        "delta" or "timed" => delta,
        "main" or "instant" => Coil.Pulled,
        _ => star
    };

    public override bool NeedsTicks => timer is { Pending: true };

    private void OnCoil(bool pulled)
    {
        Simulator.Cancel(timer);
        timer = null;
        star = pulled;
        delta = false;
        if (!pulled)
        {
            return;
        }
        startTime = Sim.Time;
        timer = Sim.Schedule(Math.Max(0.01, seconds), () =>
        {
            star = false;
            Sim.AddLog(L("log.starEnd", Label), Id);
            timer = Sim.Schedule(Pause, () =>
            {
                timer = null;
                delta = true;
                Sim.AddLog(L("log.deltaOn", Label), Id);
            });
        });
    }

    public override void Stamp(Stamper s)
    {
        Coil.Stamp(s, T(0), T(1));
        s.Switch(T(2), T(3), star);
        s.Switch(T(2), T(4), delta);
    }

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
        Visual.Closed = [star, delta];
        bool timing = star && timer is { Pending: true };
        Visual.Progress = timing ? Math.Clamp((Sim.Time - startTime) / seconds, 0, 1) : -1;
        Visual.Text = timing ? string.Format(Loc.Culture, "Y {0:0.0} s", Math.Max(0, seconds - (Sim.Time - startTime))) : null;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        foreach (Readout r in Coil.Readouts())
        {
            yield return r;
        }
        yield return new Readout(L("readout.stage"), star ? "Y" : delta ? "Δ" : "—");
    }
}

/// <summary>
/// Faz koruma (faz sırası) rölesi: üç faz da varsa ve sıra doğruysa (L1-L2-L3) çıkış rölesi çeker: 11-14 kapanır.
/// Faz eksik ya da sıra ters ise 11-12 kapalı kalır; motor kontaktörünün bobin devresine 11-14 seri bağlanır.
/// </summary>
public sealed class PhaseMonitorDef : ComponentDef
{
    public override string TypeId => "phase-monitor";
    public override ComponentCategory Category => ComponentCategory.Protection;
    public override DeviceRole Role => DeviceRole.Master;
    public override string LabelPrefix => "F";
    public override Vec Size => new(30, 30);
    public override Vec DefaultLabelOffset => new(4, -10);
    public override string Tags => "faz koruma rölesi faz sırası rölesi faz kontrol phase monitoring sequence relay";

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("L1", new Vec(0, 5), Side.Left, "L1"),
        new TerminalDef("L2", new Vec(0, 15), Side.Left, "L2"),
        new TerminalDef("L3", new Vec(0, 25), Side.Left, "L3"),
        new TerminalDef("11", new Vec(30, 25), Side.Right, "11"),
        new TerminalDef("12", new Vec(30, 9), Side.Right, "12"),
        new TerminalDef("14", new Vec(30, 17), Side.Right, "14")
    ];

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        bool ok = ctx.State?.Active == true;
        for (int i = 0; i < 3; i++)
        {
            p.Line(new Vec(0, 5 + 10 * i), new Vec(5, 5 + 10 * i), ink);
        }
        p.Rect(new Vec(5, 1), new Vec(13, 28), ink, 1, Paint.Of(Ink.Background));
        p.Text(new Vec(11.5, 10), "L1", 3, ink);
        p.Text(new Vec(11.5, 14.5), "L2", 3, ink);
        p.Text(new Vec(11.5, 19), "L3", 3, ink);
        p.Circle(new Vec(11.5, 24.5), 1.8, ink, 0.6, ok ? Paint.Color(0x43A047) : ctx.Simulating ? Paint.Color(0xE53935) : Paint.Of(Ink.Background));
        // çıkış: değiştirici kontak, ortak 11 (30,25), NC 12 (30,9), NO 14 (30,17)
        var pivot = new Vec(22, 25);
        p.Line(pivot, new Vec(30, 25), ink);
        p.Line(new Vec(26, 9), new Vec(30, 9), ink);
        p.Line(new Vec(26, 17), new Vec(30, 17), ink);
        Vec tip = ok ? new Vec(26.5, 17.5) : new Vec(26.5, 10);
        p.Line(pivot, tip, SymbolKit.ActiveInk(ctx, ctx.Simulating), ctx.Simulating ? 1.3 : 1);
        Vec mid = (pivot + tip) / 2;
        p.Line(new Vec(18, mid.Y), mid, ink, 0.6, dashed: true);
    }

    public override SimComponent CreateSim(ComponentInstance instance) => new PhaseMonitorSim();
}

internal sealed class PhaseMonitorSim : SimComponent, IDeviceMaster
{
    private static readonly Complex A = Complex.FromPolarCoordinates(1, 2 * Math.PI / 3);
    private readonly int[] branches = new int[3];
    private bool ok;
    private bool wantOk;
    private EventHandle? pending;
    private string reason = "";

    public override int InternalNodeCount => 1;

    SimComponent IDeviceMaster.Component => this;
    public string DefaultChannel => "main";
    public bool ChannelActive(string channel) => ok;

    public override void Stamp(Stamper s)
    {
        for (int i = 0; i < 3; i++)
        {
            branches[i] = s.Resistor(T(i), Internal(0), 50_000);
        }
        // çıkış: 11-12 dinlenmede kapalı, 11-14 çekince kapalı
        s.Switch(T(3), T(4), !ok);
        s.Switch(T(3), T(5), ok);
    }

    public override bool Evaluate(Solution sol)
    {
        Complex v1 = sol.BranchVoltageAc(branches[0]), v2 = sol.BranchVoltageAc(branches[1]), v3 = sol.BranchVoltageAc(branches[2]);
        double l12 = (v1 - v2).Magnitude, l23 = (v2 - v3).Magnitude, l31 = (v3 - v1).Magnitude;
        Complex pos = (v1 + A * v2 + A * A * v3) / 3, neg = (v1 + A * A * v2 + A * v3) / 3;
        bool present = Math.Min(l12, Math.Min(l23, l31)) > 0.8 * 400;
        bool order = pos.Magnitude > 3 * neg.Magnitude;
        bool want = present && order;
        reason = !present ? (Math.Max(l12, Math.Max(l23, l31)) < 50 ? "" : "phaseLoss") : !order ? "sequence" : "";
        if (want != wantOk)
        {
            wantOk = want;
            Simulator.Cancel(pending);
            pending = null;
            if (want != ok)
            {
                // çekme gecikmesi 0,3 s, bırakma hızlı
                pending = Sim.Schedule(want ? 0.3 : Sim.ContactDelay, () =>
                {
                    pending = null;
                    ok = wantOk;
                    Sim.AddLog(L(ok ? "log.phaseOk" : "log.phaseFault", Label), Id, ok ? ProblemSeverity.Info : ProblemSeverity.Warning);
                });
            }
        }
        return false;
    }

    public override void Check(Solution sol)
    {
        if (reason.Length > 0)
        {
            Sim.ReportLive(new SimProblem("phase-" + reason, ProblemSeverity.Warning,
                L(reason == "sequence" ? "problem.phaseSequence" : "problem.phaseLoss", Label),
                L(reason == "sequence" ? "explain.phaseSequence" : "explain.phaseLossRelay"), [Id], []));
        }
    }

    public override void UpdateVisual(Solution sol)
    {
        Visual.Active = ok;
        Visual.Closed = [!ok, ok];
        Visual.Text = reason == "sequence" ? L("state.wrongSequence") : reason == "phaseLoss" ? L("state.phaseMissing") : null;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L("readout.state"), L(ok ? "state.phaseOk" : reason == "sequence" ? "state.wrongSequence" : "state.phaseMissing"));
    }
}
