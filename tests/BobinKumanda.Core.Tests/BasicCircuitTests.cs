using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Tests;

public class BasicCircuitTests
{
    [Fact]
    public void Lamp_between_phase_and_neutral_lights()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var h1 = b.Add("lamp", "H1");
        b.Wire(l1, "L1", h1, "X1").Wire(h1, "X2", n, "N");
        Simulator sim = b.Start();

        var lamp = sim.Of(h1);
        Assert.True(lamp.Visual.Active);
        Assert.InRange(lamp.Visual.Level, 0.95, 1.01);
        // 5 W / 230 V ≈ 21.7 mA
        double amps = sim.WireFlows.Values.Max(f => f.Amps);
        Assert.InRange(amps, 0.020, 0.023);
        Assert.Equal(SimStatus.Running, sim.Status);
    }

    [Fact]
    public void Wire_current_flows_from_phase_to_neutral()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var h1 = b.Add("lamp", "H1");
        b.Wire(l1, "L1", h1, "X1").Wire(h1, "X2", n, "N");
        Simulator sim = b.Start();

        // Kablolar A→B yönünde çizildi: L1→lamba ve lamba→N, ikisinde de akış A'dan B'ye
        foreach (var wire in b.Circuit.Wires)
        {
            Assert.Equal(1, sim.WireFlows[wire.Id].Direction);
        }
    }

    [Fact]
    public void Direct_phase_to_neutral_is_a_short_circuit()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        b.Wire(l1, "L1", n, "N");
        Simulator sim = b.Start();

        Assert.Equal(SimStatus.Faulted, sim.Status);
        Assert.True(sim.Has("short-circuit"));
        Assert.Single(sim.Problems.Single(p => p.Code == "short-circuit").WireIds);
    }

    [Fact]
    public void Latching_circuit_holds_after_start_released_and_stops_on_stop()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var s0 = b.Add("button-nc", "S0");
        var s1 = b.Add("button-no", "S1");
        var k1 = b.Add("relay-coil", "K1");
        var k1a = b.Add("contact-no", "K1");
        b.Wire(l1, "L1", s0, "1")
         .Wire(s0, "2", s1, "1")
         .Wire(s1, "2", k1, "A1")
         .Wire(k1, "A2", n, "N")
         .Wire(s0, "2", k1a, "1")
         .Wire(k1a, "2", k1, "A1");
        Simulator sim = b.Start();

        Assert.False(sim.Of(k1).Visual.Active);
        sim.Click(s1);
        sim.Run(0.1);
        Assert.True(sim.Of(k1).Visual.Active);   // mühürlendi

        sim.Run(5);
        Assert.True(sim.Of(k1).Visual.Active);

        sim.Click(s0);
        sim.Run(0.1);
        Assert.False(sim.Of(k1).Visual.Active);
        Assert.Contains(sim.Log, e => e.Text.Contains("K1"));
    }

    [Fact]
    public void Coil_on_two_phases_burns_out()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var l2 = b.Add("source-l2");
        var k1 = b.Add("relay-coil", "K1");
        b.Wire(l1, "L1", k1, "A1").Wire(k1, "A2", l2, "L2");
        Simulator sim = b.Start();

        sim.Run(3);
        Assert.Equal(SimStatus.Faulted, sim.Status);
        Assert.True(sim.Has("coil-burned"));
    }

    [Fact]
    public void Contact_without_coil_reports_warning()
    {
        var b = new CircuitBuilder();
        b.Add("contact-no", "K9");
        Simulator sim = b.Start();
        Assert.True(sim.Has("no-master"));
    }

    [Fact]
    public void On_delay_timer_switches_after_set_time()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var s1 = b.Add("switch-3p", "Q1");
        var kt = b.Add("timer-on", "KT1", c => c.SetParam("time", 3));
        var kta = b.Add("contact-no", "KT1");
        var h1 = b.Add("lamp", "H1");
        b.Wire(l1, "L1", s1, "1").Wire(s1, "2", kt, "A1").Wire(kt, "A2", n, "N")
         .Wire(s1, "2", kta, "1").Wire(kta, "2", h1, "X1").Wire(h1, "X2", n, "N");
        Simulator sim = b.Start();

        sim.Press(s1);   // şalter: her basış konum değiştirir
        sim.Release(s1);
        sim.Run(2.9);
        Assert.False(sim.Of(h1).Visual.Active);
        sim.Run(0.3);
        Assert.True(sim.Of(h1).Visual.Active);
    }

    [Fact]
    public void Ohmmeter_reads_coil_resistance_when_disconnected()
    {
        var b = new CircuitBuilder();
        var k1 = b.Add("relay-coil", "K1");
        Simulator sim = b.Start();
        int a = sim.Netlist.NodeOf(k1.Id, "A1"), c = sim.Netlist.NodeOf(k1.Id, "A2");
        double? r = sim.MeasureResistance(a, c);
        Assert.NotNull(r);
        Assert.InRange(r!.Value, 1000, 5000);
    }
}
