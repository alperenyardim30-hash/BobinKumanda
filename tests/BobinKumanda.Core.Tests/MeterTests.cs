using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Tests;

/// <summary>Multimetre, pens ampermetre ve kontak numaralandırma / çapraz referans.</summary>
public class MeterTests
{
    private static (CircuitBuilder B, ComponentInstance S1, ComponentInstance H1) LampCircuit()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var s1 = b.Add("switch-3p", "Q1");
        var h1 = b.Add("lamp", "H1", c => c.SetParam("power", 100));
        b.Wire(l1, "L1", s1, "1").Wire(s1, "2", h1, "X1").Wire(h1, "X2", n, "N");
        return (b, s1, h1);
    }

    [Fact]
    public void Multimeter_reads_lamp_voltage_and_refuses_ohms_on_live_circuit()
    {
        var (b, s1, h1) = LampCircuit();
        Simulator sim = b.Start();
        sim.Press(s1);
        var red = ProbeTarget.Terminal(h1.Id, "X1");
        var black = ProbeTarget.Terminal(h1.Id, "X2");
        MeterReading v = Meter.Multimeter(MeterFunction.VoltsAc, sim, () => null, red, black);
        Assert.Equal("V~", v.Unit);
        Assert.InRange(double.Parse(v.Value, System.Globalization.CultureInfo.InvariantCulture), 228, 231);

        MeterReading ohms = Meter.Multimeter(MeterFunction.Ohms, sim, () => null, red, black);
        Assert.True(ohms.Warning);
    }

    [Fact]
    public void Ohmmeter_on_unpowered_circuit_shows_lamp_resistance_or_open()
    {
        var (b, s1, h1) = LampCircuit();
        var offline = new Simulator(b.Circuit, powered: false);
        MeterReading lamp = Meter.Multimeter(MeterFunction.Ohms, null, () => offline,
            ProbeTarget.Terminal(h1.Id, "X1"), ProbeTarget.Terminal(h1.Id, "X2"));
        Assert.Equal("Ω", lamp.Unit);
        Assert.Equal("529.0", lamp.Value); // 230² / 100 W
        MeterReading open = Meter.Multimeter(MeterFunction.Ohms, null, () => offline,
            ProbeTarget.Terminal(s1.Id, "1"), ProbeTarget.Terminal(s1.Id, "2"));
        Assert.Equal("OL", open.Value);
        // gerilim kademesi enerjisiz devrede 0 ve not gösterir
        MeterReading v = Meter.Multimeter(MeterFunction.VoltsAc, null, () => offline,
            ProbeTarget.Terminal(h1.Id, "X1"), ProbeTarget.Terminal(h1.Id, "X2"));
        Assert.NotNull(v.Note);
    }

    [Fact]
    public void Probe_on_wire_uses_the_wire_conductor()
    {
        var (b, s1, h1) = LampCircuit();
        Simulator sim = b.Start();
        sim.Press(s1);
        Wire toLamp = b.Circuit.Wires.Single(w => w.B.Component == h1.Id && w.B.Terminal == "X1");
        Wire fromLamp = b.Circuit.Wires.Single(w => w.A.Component == h1.Id);
        MeterReading v = Meter.Multimeter(MeterFunction.VoltsAc, sim, () => null, ProbeTarget.Wire(toLamp.Id), ProbeTarget.Wire(fromLamp.Id));
        Assert.StartsWith("2", v.Value);
        MeterReading a = Meter.Clamp(sim, toLamp.Id);
        Assert.Equal("A~", a.Unit);
        Assert.InRange(double.Parse(a.Value, System.Globalization.CultureInfo.InvariantCulture), 0.42, 0.45); // 100 W / 230 V
    }

    [Fact]
    public void Auxiliary_contacts_are_numbered_in_reading_order()
    {
        var b = new CircuitBuilder();
        var k1 = b.Add("relay-coil", "K1");
        var a = b.Add("contact-no", "K1");
        var c = b.Add("contact-nc", "K1");
        var d = b.Add("contact-no", "K1");
        var main = b.Add("contact-3no", "K1");
        a.Position = new Vec(0, 100);
        c.Position = new Vec(0, 200);
        d.Position = new Vec(0, 300);
        var numbers = CrossReference.Numbers(b.Circuit);
        ComponentDef no = ComponentCatalog.Get("contact-no"), nc = ComponentCatalog.Get("contact-nc");
        Assert.Equal("13-14", CrossReference.ContactText(no, numbers[a.Id]));
        Assert.Equal("21-22", CrossReference.ContactText(nc, numbers[c.Id]));
        Assert.Equal("33-34", CrossReference.ContactText(no, numbers[d.Id]));
        Assert.False(numbers.ContainsKey(main.Id));

        var mirror = CrossReference.Mirror(b.Circuit, numbers);
        Assert.Equal(["1-2,3-4,5-6", "13-14", "21-22", "33-34"], mirror[k1.Id].Select(e => e.Text).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Timed_contacts_get_timer_function_digits_and_other_sheets_are_marked()
    {
        var b = new CircuitBuilder();
        b.Circuit.Sheets.Add(new Sheet { Id = "s2", Name = "" });
        var kt = b.Add("timer-on", "KT1");
        var no = b.Add("contact-no", "KT1");
        var nc = b.Add("contact-nc", "KT1");
        nc.Sheet = "s2";
        var no2 = b.Add("contact-no", "KT1");
        no2.Position = new Vec(0, 500);
        var numbers = CrossReference.Numbers(b.Circuit);
        // gerçek zaman rölesindeki gibi: ilk NC 15-16, ilk NO 17-18, ikinci NO 27-28
        Assert.Equal("17-18", CrossReference.ContactText(ComponentCatalog.Get("contact-no"), numbers[no.Id]));
        Assert.Equal("27-28", CrossReference.ContactText(ComponentCatalog.Get("contact-no"), numbers[no2.Id]));
        Assert.Equal("15-16", CrossReference.ContactText(ComponentCatalog.Get("contact-nc"), numbers[nc.Id]));
        var mirror = CrossReference.Mirror(b.Circuit, numbers);
        Assert.Contains(mirror[kt.Id], e => e.Text == "15-16/2");
    }

    [Fact]
    public void Timed_contacts_of_a_contactor_keep_running_sequence()
    {
        // kontaktöre takılan zaman bloğu (channel=timed) IEC sırasıyla numaralanır: 13-14, 25-26, 37-38
        var b = new CircuitBuilder();
        b.Add("relay-coil", "K1");
        var a = b.Add("contact-no", "K1");
        var t1 = b.Add("contact-nc", "K1", c => c.SetParam("channel", "timed"));
        var t2 = b.Add("contact-no", "K1", c => c.SetParam("channel", "timed"));
        (a.Position, t1.Position, t2.Position) = (new Vec(0, 100), new Vec(0, 200), new Vec(0, 300));
        var numbers = CrossReference.Numbers(b.Circuit);
        Assert.Equal("13-14", CrossReference.ContactText(ComponentCatalog.Get("contact-no"), numbers[a.Id]));
        Assert.Equal("25-26", CrossReference.ContactText(ComponentCatalog.Get("contact-nc"), numbers[t1.Id]));
        Assert.Equal("37-38", CrossReference.ContactText(ComponentCatalog.Get("contact-no"), numbers[t2.Id]));
    }
}
