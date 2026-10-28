using System.Globalization;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Components;

/// <summary>Yatay bobin sembolü olan, kontakları etiketle bağlanan cihazlar için taban.</summary>
public abstract class CoilDefBase : ComponentDef
{
    public override ComponentCategory Category => ComponentCategory.Relays;
    public override DeviceRole Role => DeviceRole.Master;
    public override string LabelPrefix => "K";
    public override Vec Size => new(20, 20);
    // Okul sembolünde bobin kutusu 0..20 yüksekliğinin tamamını kaplar; etiket kutunun üstünde kalsın
    public override Vec DefaultLabelOffset => new(4, -10);

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("A1", new Vec(0, 10), Side.Left, "A1"),
        new TerminalDef("A2", new Vec(20, 10), Side.Right, "A2")
    ];

    public override IReadOnlyList<ParamDef> Params =>
        [ParamDef.Choice("coilVoltage", "230AC", CoilModel.VoltageChoices)];

    protected virtual SymbolKit.CoilStyle Style => SymbolKit.CoilStyle.Normal;

    public override void Draw(ISymbolPainter p, DrawContext ctx) =>
        SymbolKit.CoilH(p, ctx, 10, ctx.State?.Active == true, Style);
}

public sealed class RelayCoilDef : CoilDefBase
{
    public override string TypeId => "relay-coil";
    public override int? LegacyType => 0;
    public override char? Shortcut => 'K';
    public override string Tags => "röle kontaktör bobin contactor relay coil A1 A2";

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new RelayCoilSim(instance.GetParam("coilVoltage", "230AC"));
}

internal sealed class RelayCoilSim(string voltage) : SimComponent, IDeviceMaster
{
    private CoilModel? coil;

    public CoilModel Coil => coil ??= new CoilModel(this, voltage);

    SimComponent IDeviceMaster.Component => this;
    public string DefaultChannel => "main";
    public bool ChannelActive(string channel) => Coil.Pulled;

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

    public override IEnumerable<Readout> Readouts(Solution sol) => Coil.Readouts();
}

/// <summary>Zaman röleleri için ortak: süre parametresi ve etiket altında süre gösterimi.</summary>
public abstract class TimerDefBase : CoilDefBase
{
    public override string LabelPrefix => "K";

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Choice("coilVoltage", "230AC", CoilModel.VoltageChoices),
        ParamDef.Number("time", 3, "s", 0.1, 3600)
    ];

    public override string? SubLabel(ComponentInstance c) =>
        string.Format(Loc.Culture, "{0:0.##} s", c.GetDouble("time", 3));
}

public sealed class TimerOnDef : TimerDefBase
{
    public override string TypeId => "timer-on";
    public override int? LegacyType => 1;
    public override string Tags => "zaman rölesi çekmede gecikmeli on-delay timer ZR";
    protected override SymbolKit.CoilStyle Style => SymbolKit.CoilStyle.OnDelay;

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new TimerSim(instance.GetParam("coilVoltage", "230AC"), instance.GetDouble("time", 3), offDelay: false);
}

public sealed class TimerOffDef : TimerDefBase
{
    public override string TypeId => "timer-off";
    public override int? LegacyType => 2;
    public override string Tags => "zaman rölesi düşmede gecikmeli ters off-delay timer";
    protected override SymbolKit.CoilStyle Style => SymbolKit.CoilStyle.OffDelay;

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new TimerSim(instance.GetParam("coilVoltage", "230AC"), instance.GetDouble("time", 3), offDelay: true);
}

/// <summary>Çekmede veya düşmede gecikmeli zaman rölesi. Kanallar: "instant" (ani kontaklar), "timed".</summary>
internal sealed class TimerSim : SimComponent, IDeviceMaster
{
    private readonly string voltage;
    private readonly double seconds;
    private readonly bool offDelay;
    private CoilModel? coil;
    private bool timed;
    private EventHandle? timer;
    private double startTime;

    public TimerSim(string voltage, double seconds, bool offDelay)
    {
        this.voltage = voltage;
        this.seconds = Math.Max(0.01, seconds);
        this.offDelay = offDelay;
    }

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
    public string DefaultChannel => "timed";

    public bool ChannelActive(string channel) => channel is "instant" or "main" ? Coil.Pulled : timed;

    public override bool NeedsTicks => timer is { Pending: true };

    private void OnCoil(bool pulled)
    {
        Simulator.Cancel(timer);
        timer = null;
        if (pulled == !offDelay)
        {
            if (offDelay)
            {
                // düşmede gecikmeli: bobin bırakınca süre başlar
                StartTimer(false);
            }
            else
            {
                StartTimer(true);
            }
        }
        else
        {
            if (timed != pulled)
            {
                timed = pulled;
            }
        }
    }

    private void StartTimer(bool value)
    {
        startTime = Sim.Time;
        timer = Sim.Schedule(seconds, () =>
        {
            timer = null;
            timed = value;
            Sim.AddLog(L("log.timerElapsed", Label, seconds.ToString("0.##", Loc.Culture)), Id);
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
        Visual.Progress = timer is { Pending: true } ? Math.Clamp((Sim.Time - startTime) / seconds, 0, 1) : -1;
        Visual.Text = timer is { Pending: true }
            ? string.Format(Loc.Culture, "{0:0.0} s", Math.Max(0, seconds - (Sim.Time - startTime)))
            : null;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        foreach (Readout r in Coil.Readouts())
        {
            yield return r;
        }
        yield return new Readout(L("readout.timedContacts"), L(timed ? "state.switched" : "state.rest"));
        if (timer is { Pending: true })
        {
            yield return new Readout(L("readout.remaining"),
                string.Format(Loc.Culture, "{0:0.0} s", Math.Max(0, seconds - (Sim.Time - startTime))));
        }
    }

    internal static string FormatSeconds(double s) => s.ToString("0.##", CultureInfo.InvariantCulture);
}
