using System.Numerics;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Simulation;

public enum MeterFunction { VoltsAc, VoltsDc, Ohms }

/// <summary>Ölçü aleti ucunun değdiği yer: klemens ('T', eleman, klemens), ek noktası ('J', kimlik) ya da kablo ('W', kimlik).</summary>
public readonly record struct ProbeTarget(char Kind, string Id, string Sub = "")
{
    public static ProbeTarget Terminal(string component, string terminal) => new('T', component, terminal);
    public static ProbeTarget Junction(string id) => new('J', id);
    public static ProbeTarget Wire(string id) => new('W', id);

    /// <summary>Devrede hâlâ var mı (eleman ya da kablo silinmiş olabilir).</summary>
    public bool Exists(Circuit c) => Kind switch
    {
        'T' => c.FindComponent(Id) is { } comp && ComponentCatalog.Find(comp.Type)?.FindTerminal(Sub) != null,
        'J' => c.FindJunction(Id) != null,
        'W' => c.FindWire(Id) != null,
        _ => false
    };

    public int NodeIn(Netlist n) => Kind switch
    {
        'T' => n.NodeOf(Id, Sub),
        'J' => n.NodeOf(NetPoint.Junction(Id)),
        'W' => n.NodeOfWire(Id),
        _ => -1
    };
}

/// <summary>Ölçü aletinin göstergesi: sayı, birim ve varsa uyarı (enerjili devrede Ω ölçümü gibi).</summary>
public sealed record MeterReading(string Value, string Unit, string? Note = null, bool Warning = false);

/// <summary>Multimetre ve pens ampermetre hesapları. Gösterge metinleri gerçek aletlerdeki gibidir: açık devrede "OL".</summary>
public static class Meter
{
    /// <summary>Ohmmetre bu gerilimin üstünde ölçmez (gerçek alet zarar görür ya da yanlış gösterir).</summary>
    public const double LiveThreshold = 1.0;

    /// <param name="live">Çalışan simülasyon; yoksa devre enerjisiz kabul edilir.</param>
    /// <param name="offline">Enerjisiz devrenin modeli (yalnızca Ω için, düzenleme sırasında).</param>
    public static MeterReading Multimeter(MeterFunction f, Simulator? live, Func<Simulator?> offline, ProbeTarget? red, ProbeTarget? black)
    {
        string unit = f switch { MeterFunction.VoltsAc => "V~", MeterFunction.VoltsDc => "V=", _ => "Ω" };
        if (red is not { } r || black is not { } b)
        {
            return new MeterReading("- - - -", unit, Loc.T(red == null ? "meter.placeRed" : "meter.placeBlack"));
        }
        Simulator? sim = live ?? (f == MeterFunction.Ohms ? offline() : null);
        if (sim == null)
        {
            return new MeterReading(f == MeterFunction.Ohms ? "- - - -" : "0.0", unit, Loc.T("meter.unpowered"));
        }
        int a = r.NodeIn(sim.Netlist), c = b.NodeIn(sim.Netlist);
        (Complex ac, double dc) = sim.MeasureVoltage(a, c);
        switch (f)
        {
            case MeterFunction.VoltsAc:
                return new MeterReading(Number(ac.Magnitude), unit, live == null ? Loc.T("meter.unpowered") : null);
            case MeterFunction.VoltsDc:
                return new MeterReading(Number(dc), unit, live == null ? Loc.T("meter.unpowered") : null);
        }
        if (live != null && (ac.Magnitude > LiveThreshold || Math.Abs(dc) > LiveThreshold))
        {
            return new MeterReading("- - - -", unit, Loc.T("meter.liveOhms"), Warning: true);
        }
        double? ohms = sim.MeasureResistance(a, c);
        return ohms is { } o ? Ohms(o) : new MeterReading("OL", "Ω", Loc.T("meter.open"));
    }

    /// <summary>Pens ampermetre: kelepçenin taktığı kablodaki akım.</summary>
    public static MeterReading Clamp(Simulator? live, string? wireId)
    {
        if (wireId == null)
        {
            return new MeterReading("- - - -", "A~", Loc.T("meter.placeClamp"));
        }
        if (live == null || !live.WireFlows.TryGetValue(wireId, out WireFlow flow))
        {
            return new MeterReading("0.00", "A~", Loc.T("meter.unpowered"));
        }
        // DC devrede (24 V DC) DC akım gösterilir; çoğu pens ampermetre AC/DC ölçer
        bool dc = Math.Abs(flow.Dc) > flow.Ac.Magnitude;
        double amps = dc ? Math.Abs(flow.Dc) : flow.Ac.Magnitude;
        return new MeterReading(Number(amps), dc ? "A=" : "A~");
    }

    public static double ClampAmps(Simulator? live, string? wireId) =>
        live != null && wireId != null && live.WireFlows.TryGetValue(wireId, out WireFlow f) ? Math.Max(f.Ac.Magnitude, Math.Abs(f.Dc)) : 0;

    /// <summary>Gösterge biçimi: en çok 4 anlamlı hane, ondalık nokta (LCD gibi).</summary>
    public static string Number(double v)
    {
        double a = Math.Abs(v);
        string s = a >= 1000 ? v.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : a >= 100 ? v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            : a >= 10 ? v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            : v.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        return s == "-0.000" ? "0.000" : s;
    }

    private static MeterReading Ohms(double o) =>
        o >= 1e6 ? new MeterReading(Number(o / 1e6), "MΩ")
        : o >= 1e3 ? new MeterReading(Number(o / 1e3), "kΩ")
        : new MeterReading(o < 0.05 ? "0.00" : Number(o), "Ω", o < 1 ? Loc.T("meter.continuity") : null);
}
