using System.Globalization;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Components;

/// <summary>
/// Ölçtüğü büyüklük (seviye, basınç, sıcaklık, ışık) eşiği geçince değiştirici kontağını çeviren sensörler.
/// Değer simülasyonda sağ paneldeki kaydırıcıyla ayarlanır; sembole tıklamak değeri iki uç arasında değiştirir.
/// Kontak: ortak 11, NC 12, NO 14. Aynı etiketli kontaklar da sensörü izler.
/// </summary>
public abstract class SensorDefBase : ComponentDef
{
    public override ComponentCategory Category => ComponentCategory.Sensors;
    public override DeviceRole Role => DeviceRole.Master;
    public override string LabelPrefix => "B";
    public override Vec Size => new(30, 20);
    public override Vec DefaultLabelOffset => new(4, -9);

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("C", new Vec(0, 15), Side.Left, "11"),
        new TerminalDef("NC", new Vec(30, 7), Side.Right, "12"),
        new TerminalDef("NO", new Vec(30, 15), Side.Right, "14")
    ];

    /// <summary>Kaydırıcı: ad anahtarı, birim, alt/üst sınır, başlangıç değeri.</summary>
    protected abstract (string NameKey, string Unit, double Min, double Max, double Start) Range { get; }

    /// <summary>Eşik parametreleri (çalışma ve geri dönme değerleri) ve hangi yönde tetiklediği.</summary>
    protected abstract (double On, double Off) Thresholds(ComponentInstance c);

    /// <summary>true: değer eşiğin altına inince tetikler (fotosel karanlıkta, termostat soğukta ısıtıcıyı açar...).</summary>
    protected virtual bool ActsBelow(ComponentInstance c) => false;

    public override SimComponent CreateSim(ComponentInstance instance)
    {
        var r = Range;
        (double on, double off) = Thresholds(instance);
        return new SensorSim(r.NameKey, r.Unit, r.Min, r.Max, instance.GetDouble("startValue", r.Start), on, off, ActsBelow(instance));
    }

    /// <summary>Değiştirici kontak (sağdaki kutuya doğru) + sensöre özgü simge.</summary>
    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        bool actuated = ctx.State?.Active == true;
        DrawChangeover(p, ctx, ink, actuated);
        DrawSensorIcon(p, ctx, ink, new Vec(7, 6), actuated);
    }

    internal static void DrawChangeover(ISymbolPainter p, DrawContext ctx, Paint ink, bool actuated)
    {
        // Ortak (0,15) → pivot (16,15); NC (30,7), NO (30,15)
        var pivot = new Vec(16, 15);
        p.Line(new Vec(0, 15), pivot, ink);
        p.Line(new Vec(24, 15), new Vec(30, 15), ink);
        p.Line(new Vec(24, 7), new Vec(30, 7), ink);
        p.Line(new Vec(24, 7), new Vec(24, 9), ink);
        Vec tip = actuated && ctx.Simulating ? new Vec(24.5, 15) : new Vec(24.5, 8.5);
        p.Line(pivot, tip, SymbolKit.ActiveInk(ctx, ctx.Simulating), ctx.Simulating ? 1.3 : 1);
        Vec mid = (pivot + tip) / 2;
        p.Line(mid, new Vec(mid.X, 3), ink, 0.7, dashed: true);
        p.Line(new Vec(mid.X, 3), new Vec(13, 3), ink, 0.7, dashed: true);
    }

    protected abstract void DrawSensorIcon(ISymbolPainter p, DrawContext ctx, Paint ink, Vec center, bool actuated);
}

internal sealed class SensorSim(string nameKey, string unit, double min, double max, double start, double on, double off, bool below)
    : SimComponent, IDeviceMaster
{
    private double value = Math.Clamp(start, min, max);
    private bool actuated;
    private static readonly ContactSpec[] Contacts = [new(0, 1, true), new(0, 2, false)];

    SimComponent IDeviceMaster.Component => this;
    public string DefaultChannel => "main";
    public bool ChannelActive(string channel) => actuated;

    public override bool IsInteractive => true;

    public override SimSlider Slider => new(nameKey, min, max, value, unit, (max - min) / 100);

    public override void Bind(DeviceRegistry devices) => Update(log: false);

    public override void Stamp(Stamper s)
    {
        foreach (ContactSpec c in Contacts)
        {
            s.Switch(T(c.A), T(c.B), actuated ^ c.NormallyClosed);
        }
    }

    private void Update(bool log)
    {
        bool want = below
            ? (actuated ? value < off : value <= on)
            : (actuated ? value > off : value >= on);
        if (want != actuated)
        {
            actuated = want;
            if (log)
            {
                Sim.AddLog(L(actuated ? "log.sensorOn" : "log.sensorOff", Label, Format(value)), Id);
            }
        }
    }

    private string Format(double v) => string.Format(Loc.Culture, "{0:0.#} {1}", v, unit);

    public override void Interact(Interaction action)
    {
        switch (action.Kind)
        {
            case InteractionKind.SetValue:
                value = Math.Clamp(action.Value, min, max);
                break;
            case InteractionKind.Press or InteractionKind.Toggle:
                // Tıklama: değeri eşiğin öbür tarafına taşı
                double lo = Math.Min(on, off), hi = Math.Max(on, off);
                bool high = value >= (lo + hi) / 2;
                value = high ? Math.Max(min, lo - 0.1 * (max - min)) : Math.Min(max, hi + 0.1 * (max - min));
                break;
            default:
                return;
        }
        Update(log: true);
    }

    public override void UpdateVisual(Solution sol)
    {
        Visual.Active = actuated;
        Visual.Level = (value - min) / (max - min);
        Visual.Closed = [!actuated, actuated];
        Visual.Text = Format(value);
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L(nameKey), Format(value));
        yield return new Readout(L("readout.switchPoint"), Format(on) + " / " + Format(off));
        yield return new Readout(L("readout.state"), L(actuated ? "state.actuated" : "state.rest"));
    }
}

/// <summary>Şamandıra (seviye şalteri): seviye üst değere çıkınca tetikler, alt değere inince bırakır.</summary>
public sealed class FloatSwitchDef : SensorDefBase
{
    public override string TypeId => "float-switch";
    public override string Tags => "şamandıra seviye şalteri depo su pompa float level switch";

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Number("levelHigh", 80, "%", 1, 100),
        ParamDef.Number("levelLow", 20, "%", 0, 99),
        ParamDef.Number("startValue", 50, "%", 0, 100)
    ];

    protected override (string, string, double, double, double) Range => ("slider.level", "%", 0, 100, 50);

    protected override (double On, double Off) Thresholds(ComponentInstance c) =>
        (c.GetDouble("levelHigh", 80), Math.Min(c.GetDouble("levelLow", 20), c.GetDouble("levelHigh", 80) - 1));

    protected override void DrawSensorIcon(ISymbolPainter p, DrawContext ctx, Paint ink, Vec c, bool actuated)
    {
        // depo ve içindeki su seviyesi
        double level = ctx.State?.Level ?? 0.5;
        p.Polyline([c + new Vec(-5, -5), c + new Vec(-5, 6), c + new Vec(5, 6), c + new Vec(5, -5)], ink, 0.8);
        double y = c.Y + 6 - 11 * Math.Clamp(level, 0, 1);
        p.Rect(new Vec(c.X - 4.6, y), new Vec(9.2, c.Y + 5.6 - y), Paint.Color(0x1E88E5, 0.35), 0, Paint.Color(0x1E88E5, 0.35));
        p.Circle(new Vec(c.X, y - 1.2), 1.6, ink, 0.7, Paint.Of(Ink.Background));
    }
}

/// <summary>Basınç şalteri (presostat): basınç ayar değerine çıkınca tetikler, fark kadar düşünce bırakır.</summary>
public sealed class PressureSwitchDef : SensorDefBase
{
    public override string TypeId => "pressure-switch";
    public override string Tags => "basınç şalteri presostat kompresör hidrofor pressure switch";

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Number("setPoint", 6, "bar", 0.1, 16),
        ParamDef.Number("hysteresis", 2, "bar", 0.1, 10),
        ParamDef.Number("startValue", 0, "bar", 0, 16)
    ];

    protected override (string, string, double, double, double) Range => ("slider.pressure", "bar", 0, 10, 0);

    protected override (double On, double Off) Thresholds(ComponentInstance c)
    {
        double set = c.GetDouble("setPoint", 6);
        return (set, Math.Max(0, set - c.GetDouble("hysteresis", 2)));
    }

    protected override void DrawSensorIcon(ISymbolPainter p, DrawContext ctx, Paint ink, Vec c, bool actuated)
    {
        // manometre: daire ve ibre
        p.Circle(c, 5, ink, 0.8, Paint.Of(Ink.Background));
        double level = ctx.State?.Level ?? 0;
        double a = Math.PI * (0.75 + 1.5 * Math.Clamp(level, 0, 1));
        p.Line(c, c + new Vec(Math.Cos(a), Math.Sin(a)) * 4, ctx.Simulating ? Paint.Of(Ink.Active) : ink, 0.9);
        p.Text(c + new Vec(0, 3.6), "P", 3.2, ink, bold: true);
    }
}

/// <summary>Termostat: sıcaklık ayar değerinin altına inince (ısıtma) ya da üstüne çıkınca (soğutma) tetikler.</summary>
public sealed class ThermostatDef : SensorDefBase
{
    public override string TypeId => "thermostat";
    public override string Tags => "termostat sıcaklık şalteri ısıtıcı kontrol thermostat temperature";

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Number("setPoint", 60, "°C", -20, 150),
        ParamDef.Number("hysteresis", 4, "°C", 0.5, 30),
        ParamDef.Choice("mode", "heating", "heating", "cooling"),
        ParamDef.Number("startValue", 20, "°C", -20, 150)
    ];

    protected override (string, string, double, double, double) Range => ("slider.temperature", "°C", -10, 120, 20);

    protected override bool ActsBelow(ComponentInstance c) => c.GetParam("mode", "heating") == "heating";

    protected override (double On, double Off) Thresholds(ComponentInstance c)
    {
        double set = c.GetDouble("setPoint", 60), h = c.GetDouble("hysteresis", 4) / 2;
        // ısıtma: ayarın altında tetikli (ısıtıcı çalışsın), üstünde bırakır; soğutma: tersi
        return ActsBelow(c) ? (set - h, set + h) : (set + h, set - h);
    }

    protected override void DrawSensorIcon(ISymbolPainter p, DrawContext ctx, Paint ink, Vec c, bool actuated)
    {
        // termometre
        p.Rect(new Vec(c.X - 1.3, c.Y - 5.5), new Vec(2.6, 8), ink, 0.7, Paint.Of(Ink.Background));
        p.Circle(new Vec(c.X, c.Y + 4), 2.2, ink, 0.7, Paint.Color(0xE53935, 0.8));
        double level = Math.Clamp(ctx.State?.Level ?? 0.25, 0, 1);
        p.Rect(new Vec(c.X - 0.6, c.Y + 2.5 - 7.5 * level), new Vec(1.2, 7.5 * level), Paint.Color(0xE53935, 0.8), 0, Paint.Color(0xE53935, 0.8));
    }
}

/// <summary>Fotosel (alacakaranlık şalteri): ışık şiddeti eşiğin altına inince (hava kararınca) tetikler.</summary>
public sealed class PhotocellDef : SensorDefBase
{
    public override string TypeId => "photocell";
    public override string Tags => "fotosel ışık sensörü alacakaranlık şalteri sokak lambası LDR photocell light sensor";

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Number("setPoint", 30, "%", 1, 99),
        ParamDef.Number("startValue", 80, "%", 0, 100)
    ];

    protected override (string, string, double, double, double) Range => ("slider.light", "%", 0, 100, 80);

    protected override bool ActsBelow(ComponentInstance c) => true;

    protected override (double On, double Off) Thresholds(ComponentInstance c)
    {
        double set = c.GetDouble("setPoint", 30);
        return (set, set + 10);
    }

    protected override void DrawSensorIcon(ISymbolPainter p, DrawContext ctx, Paint ink, Vec c, bool actuated)
    {
        // güneş ve oklar
        double level = Math.Clamp(ctx.State?.Level ?? 0.8, 0, 1);
        p.Circle(c, 3, ink, 0.8, Paint.Color(0xFDD835, 0.2 + 0.8 * level));
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            var d = new Vec(Math.Cos(a), Math.Sin(a));
            p.Line(c + d * 4, c + d * 5.6, ink, 0.6);
        }
    }
}

/// <summary>
/// Endüktif / kapasitif yaklaşım sensörü: 3 kablolu, 24 V DC. BN (+), BU (−), BK (çıkış).
/// PNP: cisim algılanınca çıkış +24 V'a bağlanır; NPN: çıkış 0 V'a bağlanır. Yük çıkış ile karşı uç arasına bağlanır.
/// </summary>
public abstract class ProximityDefBase : ComponentDef
{
    public override ComponentCategory Category => ComponentCategory.Sensors;
    public override DeviceRole Role => DeviceRole.Master;
    public override string LabelPrefix => "B";
    public override Vec Size => new(30, 30);
    public override Vec DefaultLabelOffset => new(8, -10);

    protected abstract bool Capacitive { get; }

    public override IReadOnlyList<TerminalDef> Terminals =>
    [
        new TerminalDef("BN", new Vec(0, 5), Side.Left, "BN +"),
        new TerminalDef("BK", new Vec(0, 15), Side.Left, "BK"),
        new TerminalDef("BU", new Vec(0, 25), Side.Left, "BU −")
    ];

    public override IReadOnlyList<ParamDef> Params =>
    [
        ParamDef.Choice("output", "pnp", "pnp", "npn"),
        ParamDef.Choice("function", "no", "no", "nc")
    ];

    public override string? SubLabel(ComponentInstance c) =>
        c.GetParam("output", "pnp").ToUpperInvariant() + " " + c.GetParam("function", "no").ToUpperInvariant();

    public override void Draw(ISymbolPainter p, DrawContext ctx)
    {
        Paint ink = SymbolKit.InkFor(ctx);
        // kablo renkleri: kahverengi, siyah, mavi
        p.Line(new Vec(0, 5), new Vec(8, 5), Paint.Color(0x8D5524));
        p.Line(new Vec(0, 15), new Vec(8, 15), Paint.Color(0x202020));
        p.Line(new Vec(0, 25), new Vec(8, 25), Paint.Color(0x1E64C8));
        p.Rect(new Vec(8, 1), new Vec(16, 28), ink, 1, Paint.Of(Ink.Background));
        // algılama yüzü
        p.Rect(new Vec(24, 5), new Vec(4, 20), ink, 0.9, Capacitive ? Paint.Color(0x8C8C8C, 0.4) : Paint.Color(0x8D5524, 0.35));
        // eşkenar dörtgen (sensör simgesi) ve tip
        var c = new Vec(16, 11);
        p.Polyline([c + new Vec(0, -5), c + new Vec(5, 0), c + new Vec(0, 5), c + new Vec(-5, 0)], ink, 0.8, closed: true);
        p.Text(c + new Vec(0, 1.6), Capacitive ? "C" : "L", 4, ink, bold: true);
        bool led = ctx.State?.Active == true;
        p.Circle(new Vec(16, 23), 2, ink, 0.6, led ? Paint.Color(0xFDD835) : Paint.Color(0xFDD835, 0.15));
        if (ctx.State is { Position: 1 })
        {
            // algılanan cisim
            p.Rect(new Vec(29, 9), new Vec(1, 12), Paint.Of(Ink.Accent), 0, Paint.Of(Ink.Accent, 0.8));
        }
    }

    public override SimComponent CreateSim(ComponentInstance instance) =>
        new ProximitySim(instance.GetParam("output", "pnp") == "pnp", instance.GetParam("function", "no") == "nc");
}

public sealed class InductiveSensorDef : ProximityDefBase
{
    public override string TypeId => "prox-inductive";
    public override string Tags => "endüktif sensör yaklaşım sensörü metal algılayıcı pnp npn inductive proximity sensor";
    protected override bool Capacitive => false;
}

public sealed class CapacitiveSensorDef : ProximityDefBase
{
    public override string TypeId => "prox-capacitive";
    public override string Tags => "kapasitif sensör yaklaşım sensörü seviye algılayıcı pnp npn capacitive proximity sensor";
    protected override bool Capacitive => true;
}

internal sealed class ProximitySim(bool pnp, bool normallyClosed) : SimComponent, IDeviceMaster
{
    private int supplyBranch = -1;
    private bool target;
    private bool powered;
    private bool burned;
    private double vdc, vac;

    private bool Output => powered && !burned && (target ^ normallyClosed);

    SimComponent IDeviceMaster.Component => this;
    public string DefaultChannel => "main";
    public bool ChannelActive(string channel) => Output;

    public override bool IsInteractive => true;

    public override void Stamp(Stamper s)
    {
        // iç tüketim ~10 mA
        supplyBranch = burned ? s.Switch(T(0), T(2), false) : s.Resistor(T(0), T(2), 2400);
        if (pnp)
        {
            s.Switch(T(0), T(1), Output);
        }
        else
        {
            s.Switch(T(1), T(2), Output);
        }
    }

    public override bool Evaluate(Solution sol)
    {
        vdc = sol.BranchVoltageDc(supplyBranch);
        vac = sol.BranchVoltageAc(supplyBranch).Magnitude;
        bool now = vdc > 10 && vdc < 36;
        if (!burned && (vac > 50 || vdc > 40 || vdc < -10))
        {
            burned = true;
            Visual.Fault = true;
            Sim.ReportLatched(new SimProblem("sensor-burned", ProblemSeverity.Warning,
                L("problem.sensorBurned", Label, Simulator.FormatVolts(Math.Max(vac, Math.Abs(vdc))) + (vac > 50 ? " AC" : " DC")),
                L("explain.sensorSupply"), [Id], []));
            return true;
        }
        if (now != powered)
        {
            powered = now;
            return true;
        }
        return false;
    }

    public override void Check(Solution sol)
    {
        if (!burned && !powered && (Math.Abs(vdc) > 1 || vac > 1))
        {
            Sim.ReportLive(new SimProblem("sensor-supply", ProblemSeverity.Warning,
                L("problem.sensorSupply", Label, Simulator.FormatVolts(vdc)), L("explain.sensorSupply"), [Id], []));
        }
    }

    public override void Interact(Interaction action)
    {
        bool before = target;
        target = action.Kind switch
        {
            InteractionKind.Press or InteractionKind.Toggle => !target,
            InteractionKind.SetValue => action.Value > 0.5,
            _ => target
        };
        if (before != target)
        {
            Sim.AddLog(L(target ? "log.targetIn" : "log.targetOut", Label), Id);
        }
    }

    public override void UpdateVisual(Solution sol)
    {
        Visual.Active = Output;
        Visual.Position = target ? 1 : 0;
        Visual.Fault = burned;
    }

    public override IEnumerable<Readout> Readouts(Solution sol)
    {
        yield return new Readout(L("readout.supply"), Simulator.FormatVolts(vdc) + " DC");
        yield return new Readout(L("readout.target"), L(target ? "state.detected" : "state.none"));
        yield return new Readout(L("readout.output"), L(Output ? "state.on" : "state.off"));
        yield return new Readout(L("readout.hint"), L("hint.proximity"));
    }
}
