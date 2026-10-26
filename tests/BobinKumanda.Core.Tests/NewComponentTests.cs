using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Tests;

/// <summary>Aşama 5 elemanları: koruma, şalterler, sensörler, yükler, röleler, motorlar ve besleme.</summary>
public class NewComponentTests
{
    private static void Act(Simulator sim, ComponentInstance c, InteractionKind kind, double value = 0) =>
        sim.Interact(c.Id, new Interaction(kind, value));

    // ---- Koruma ----

    [Fact]
    public void Breaker_trips_on_short_circuit_and_simulation_keeps_running()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var f1 = b.Add("mcb-1p", "F1");
        b.Wire(l1, "L1", f1, "1").Wire(f1, "2", n, "N");
        Simulator sim = b.Start();

        Assert.Equal(SimStatus.Running, sim.Status);
        Assert.False(sim.Has("short-circuit"));
        Assert.True(sim.Of(f1).Visual.Tripped);
        SimProblem p = sim.Problems.Single(x => x.Code == "breaker-tripped");
        Assert.NotEmpty(p.WireIds);

        // kısa devre sürerken kolu kaldırmak yine attırır
        Act(sim, f1, InteractionKind.Toggle);
        Assert.True(sim.Of(f1).Visual.Tripped);
    }

    [Fact]
    public void Breaker_trips_slowly_on_overload()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var f1 = b.Add("mcb-1p", "F1", c => c.SetParam("ratedCurrent", "6"));
        var e1 = b.Add("heater", "E1", c => c.SetParam("power", 2000)); // ~8,7 A
        b.Wire(l1, "L1", f1, "1").Wire(f1, "2", e1, "X1").Wire(e1, "X2", n, "N");
        Simulator sim = b.Start();

        sim.Run(10);
        Assert.False(sim.Of(f1).Visual.Tripped);
        sim.Run(400);
        Assert.True(sim.Of(f1).Visual.Tripped);
        Assert.True(sim.Has("breaker-tripped"));
        Assert.False(sim.Of(e1).Visual.Active);
    }

    [Fact]
    public void Fuse_blows_and_can_be_replaced()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var f1 = b.Add("fuse-1p", "F1");
        var h1 = b.Add("lamp", "H1");
        var s1 = b.Add("switch-3p", "Q1");
        b.Wire(l1, "L1", f1, "1").Wire(f1, "2", h1, "X1").Wire(h1, "X2", n, "N");
        // Q1 kapanınca lambanın uçları kısa devre olur
        b.Wire(f1, "2", s1, "1").Wire(s1, "2", n, "N");
        Simulator sim = b.Start();
        Assert.True(sim.Of(h1).Visual.Active);

        sim.Press(s1);
        Assert.True(sim.Of(f1).Visual.Tripped);
        Assert.True(sim.Has("fuse-blown"));
        Assert.False(sim.Of(h1).Visual.Active);

        sim.Press(s1); // kısa devreyi kaldır
        Act(sim, f1, InteractionKind.Reset);
        Assert.False(sim.Of(f1).Visual.Tripped);
        Assert.True(sim.Of(h1).Visual.Active);
    }

    /// <summary>Gövde kaçağı olan motor: PE bağlıyken kaçak akım rölesi açar.</summary>
    [Fact]
    public void Residual_current_device_trips_on_body_leakage()
    {
        var b = new CircuitBuilder();
        var supply = b.Add("source-3phnpe");
        var rcd = b.Add("rcd-4p", "F1");
        var m1 = b.Add("motor-3ph", "M1", c => c.SetParam("bodyFault", "True"));
        b.Wire(supply, "L1", rcd, "1").Wire(supply, "L2", rcd, "3").Wire(supply, "L3", rcd, "5").Wire(supply, "N", rcd, "7");
        b.Wire(rcd, "2", m1, "U").Wire(rcd, "4", m1, "V").Wire(rcd, "6", m1, "W");
        b.Wire(m1, "PE", supply, "PE");
        Simulator sim = b.Start();

        Assert.True(sim.Of(rcd).Visual.Tripped);
        Assert.True(sim.Has("rcd-tripped"));
        Assert.False(sim.Has("touch-voltage"));
        Assert.Equal(SimStatus.Running, sim.Status);
    }

    [Fact]
    public void Residual_current_device_holds_without_leakage_and_test_button_trips_it()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var rcd = b.Add("rcd-2p", "F1");
        var h1 = b.Add("lamp", "H1");
        b.Wire(l1, "L1", rcd, "1").Wire(n, "N", rcd, "3").Wire(rcd, "2", h1, "X1").Wire(h1, "X2", rcd, "4");
        Simulator sim = b.Start();
        Assert.False(sim.Of(rcd).Visual.Tripped);
        Assert.True(sim.Of(h1).Visual.Active);

        Act(sim, rcd, InteractionKind.Test);
        Assert.True(sim.Of(rcd).Visual.Tripped);
        Assert.False(sim.Of(h1).Visual.Active);
        Act(sim, rcd, InteractionKind.Toggle);
        Assert.True(sim.Of(h1).Visual.Active);
    }

    [Fact]
    public void Body_leakage_without_earth_gives_touch_voltage()
    {
        var b = new CircuitBuilder();
        var supply = b.Add("source-3ph");
        var m1 = b.Add("motor-3ph", "M1", c => c.SetParam("bodyFault", "True"));
        b.Wire(supply, "L1", m1, "U").Wire(supply, "L2", m1, "V").Wire(supply, "L3", m1, "W");
        Simulator sim = b.Start();
        Assert.True(sim.Has("touch-voltage"));
    }

    /// <summary>Faz eksik motor kalkamaz; termik röle açar, 95-96 kontaktörü bıraktırır.</summary>
    [Fact]
    public void Overload_relay_trips_and_drops_the_contactor()
    {
        var b = new CircuitBuilder();
        var supply = b.Add("source-3ph");
        var n = b.Add("source-n");
        var q1 = b.Add("switch-3p", "Q1");
        var f2c = b.Add("overload-contacts", "F2");
        var k1 = b.Add("relay-coil", "K1");
        var k1m = b.Add("contact-3no", "K1");
        var f2 = b.Add("overload", "F2", c => c.SetParam("setCurrent", 3.2));
        var m1 = b.Add("motor-3ph", "M1");
        b.Wire(supply, "L1", q1, "1").Wire(q1, "2", f2c, "95").Wire(f2c, "96", k1, "A1").Wire(k1, "A2", n, "N");
        b.Wire(supply, "L1", k1m, "1").Wire(supply, "L2", k1m, "3"); // L3 eksik
        b.Wire(k1m, "2", f2, "1").Wire(k1m, "4", f2, "3").Wire(k1m, "6", f2, "5");
        b.Wire(f2, "2", m1, "U").Wire(f2, "4", m1, "V").Wire(f2, "6", m1, "W");
        Simulator sim = b.Start();

        sim.Press(q1);
        sim.Run(1);
        Assert.True(sim.Of(k1).Visual.Active);
        sim.Run(30);
        Assert.True(sim.Of(f2).Visual.Tripped);
        Assert.False(sim.Of(k1).Visual.Active);
        Assert.Equal(SimStatus.Running, sim.Status); // motor yanmadan önce açtı

        // sıcakken resetlenmez, soğuyunca resetlenir
        Act(sim, f2, InteractionKind.Reset);
        Assert.True(sim.Of(f2).Visual.Tripped);
        sim.Run(120);
        Act(sim, f2, InteractionKind.Reset);
        Assert.False(sim.Of(f2).Visual.Tripped);
    }

    [Fact]
    public void Motor_load_slider_raises_current_and_overload_trips()
    {
        var b = new CircuitBuilder();
        var supply = b.Add("source-3ph");
        var f2 = b.Add("overload", "F2", c => c.SetParam("setCurrent", 3.2));
        var m1 = b.Add("motor-3ph", "M1");
        b.Wire(supply, "L1", f2, "1").Wire(supply, "L2", f2, "3").Wire(supply, "L3", f2, "5");
        b.Wire(f2, "2", m1, "U").Wire(f2, "4", m1, "V").Wire(f2, "6", m1, "W");
        Simulator sim = b.Start();
        sim.Run(3);
        double rated = MaxCurrent(sim, b, m1);
        Act(sim, m1, InteractionKind.SetValue, 200);
        sim.Run(1);
        double overloaded = MaxCurrent(sim, b, m1);
        Assert.True(overloaded > 1.4 * rated, $"{rated:0.00} A → {overloaded:0.00} A");
        Assert.False(sim.Of(f2).Visual.Tripped);
        sim.Run(400);
        Assert.True(sim.Of(f2).Visual.Tripped);
    }

    private static double MaxCurrent(Simulator sim, CircuitBuilder b, ComponentInstance m) =>
        b.Circuit.Wires.Where(w => w.B.Component == m.Id).Max(w => sim.WireFlows[w.Id].Amps);

    [Fact]
    public void Motor_protection_switch_trips_on_stalled_motor()
    {
        var b = new CircuitBuilder();
        var supply = b.Add("source-3ph");
        var q1 = b.Add("mpcb", "Q1", c => c.SetParam("setCurrent", 3.2));
        var m1 = b.Add("motor-3ph", "M1");
        b.Wire(supply, "L1", q1, "1").Wire(supply, "L2", q1, "3");
        b.Wire(q1, "2", m1, "U").Wire(q1, "4", m1, "V").Wire(q1, "6", m1, "W");
        Simulator sim = b.Start();
        sim.Run(30);
        Assert.True(sim.Of(q1).Visual.Tripped);
        Assert.Equal(SimStatus.Running, sim.Status);
    }

    // ---- Şalterler ----

    [Fact]
    public void Emergency_stop_latches_until_released()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var s0 = b.Add("estop", "S0");
        var h1 = b.Add("lamp", "H1");
        b.Wire(l1, "L1", s0, "1").Wire(s0, "2", h1, "X1").Wire(h1, "X2", n, "N");
        Simulator sim = b.Start();
        Assert.True(sim.Of(h1).Visual.Active);
        sim.Click(s0);
        Assert.False(sim.Of(h1).Visual.Active);
        sim.Run(1);
        Assert.False(sim.Of(h1).Visual.Active);
        sim.Click(s0);
        Assert.True(sim.Of(h1).Visual.Active);
    }

    [Fact]
    public void Illuminated_button_lamp_lights_with_its_own_supply()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var s1 = b.Add("button-lit", "S1");
        var h1 = b.Add("lamp", "H1");
        b.Wire(l1, "L1", s1, "1").Wire(s1, "2", h1, "X1").Wire(h1, "X2", n, "N");
        b.Wire(s1, "2", s1, "X1").Wire(s1, "X2", n, "N");
        Simulator sim = b.Start();
        Assert.True(sim.Of(s1).Visual.Level < 0.1);
        sim.Press(s1);
        Assert.True(sim.Of(h1).Visual.Active);
        Assert.True(sim.Of(s1).Visual.Level > 0.9);
    }

    [Fact]
    public void Reversing_cam_switch_changes_motor_direction()
    {
        var b = new CircuitBuilder();
        var supply = b.Add("source-3ph");
        var q1 = b.Add("cam-reverse", "Q1");
        var m1 = b.Add("motor-3ph", "M1");
        b.Wire(supply, "L1", q1, "1").Wire(supply, "L2", q1, "3").Wire(supply, "L3", q1, "5");
        b.Wire(q1, "2", m1, "U").Wire(q1, "4", m1, "V").Wire(q1, "6", m1, "W");
        Simulator sim = b.Start();
        sim.Run(1);
        Assert.False(sim.Of(m1).Visual.Active);

        Act(sim, q1, InteractionKind.SetValue, 1);
        sim.Run(3);
        Assert.Equal(1, sim.Of(m1).Visual.Direction);
        Act(sim, q1, InteractionKind.SetValue, 2);
        sim.Run(6);
        Assert.Equal(-1, sim.Of(m1).Visual.Direction);
        Assert.Equal(SimStatus.Running, sim.Status);
    }

    [Fact]
    public void Star_delta_cam_switch_runs_motor_in_star_then_delta()
    {
        var b = new CircuitBuilder();
        var supply = b.Add("source-3ph");
        var q1 = b.Add("cam-stardelta", "Q1");
        var m1 = b.Add("motor-3ph-6t", "M1");
        b.Wire(supply, "L1", q1, "L1").Wire(supply, "L2", q1, "L2").Wire(supply, "L3", q1, "L3");
        foreach (string t in new[] { "U1", "V1", "W1", "U2", "V2", "W2" })
        {
            b.Wire(q1, t, m1, t);
        }
        Simulator sim = b.Start();
        Act(sim, q1, InteractionKind.SetValue, 1);
        sim.Run(3);
        Assert.True(sim.Of(m1).Visual.Active);
        Assert.StartsWith("yıldız", Connection(sim, m1));
        Act(sim, q1, InteractionKind.SetValue, 2);
        sim.Run(2);
        Assert.StartsWith("üçgen", Connection(sim, m1));
        Assert.InRange(sim.Of(m1).Visual.Level, 0.95, 1.0);
    }

    private static string Connection(Simulator sim, ComponentInstance m) =>
        sim.Of(m).Readouts(sim.Solution!).First(r => r.Value.Contains("V/")).Value;

    [Fact]
    public void Selector_switch_positions_close_their_contacts()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var s1 = b.Add("selector-3", "S1");
        var h1 = b.Add("lamp", "H1");
        var h2 = b.Add("lamp", "H2");
        b.Wire(l1, "L1", s1, "1").Wire(s1, "2", h1, "X1").Wire(h1, "X2", n, "N");
        b.Wire(l1, "L1", s1, "3").Wire(s1, "4", h2, "X1").Wire(h2, "X2", n, "N");
        Simulator sim = b.Start();
        Assert.False(sim.Of(h1).Visual.Active || sim.Of(h2).Visual.Active);
        sim.Press(s1);
        Assert.True(sim.Of(h1).Visual.Active);
        Assert.False(sim.Of(h2).Visual.Active);
        sim.Press(s1);
        Assert.False(sim.Of(h1).Visual.Active);
        Assert.True(sim.Of(h2).Visual.Active);
    }

    // ---- Sensörler ----

    [Fact]
    public void Float_switch_follows_level_with_hysteresis()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var b1 = b.Add("float-switch", "B1");
        var h1 = b.Add("lamp", "H1");
        b.Wire(l1, "L1", b1, "C").Wire(b1, "NO", h1, "X1").Wire(h1, "X2", n, "N");
        Simulator sim = b.Start();
        Assert.False(sim.Of(h1).Visual.Active);
        Act(sim, b1, InteractionKind.SetValue, 85);
        Assert.True(sim.Of(h1).Visual.Active);
        Act(sim, b1, InteractionKind.SetValue, 50); // alt seviyeye inmedi
        Assert.True(sim.Of(h1).Visual.Active);
        Act(sim, b1, InteractionKind.SetValue, 15);
        Assert.False(sim.Of(h1).Visual.Active);
    }

    [Fact]
    public void Thermostat_in_heating_mode_switches_on_when_cold()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var b1 = b.Add("thermostat", "B1", c => c.SetParam("setPoint", 60));
        var e1 = b.Add("heater", "E1");
        b.Wire(l1, "L1", b1, "C").Wire(b1, "NO", e1, "X1").Wire(e1, "X2", n, "N");
        Simulator sim = b.Start(); // 20 °C
        Assert.True(sim.Of(e1).Visual.Active);
        Act(sim, b1, InteractionKind.SetValue, 70);
        Assert.False(sim.Of(e1).Visual.Active);
    }

    [Fact]
    public void Pnp_sensor_on_dc_supply_drives_a_dc_coil()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var g1 = b.Add("psu-dc", "G1");
        var b1 = b.Add("prox-inductive", "B1");
        var k1 = b.Add("relay-coil", "K1", c => c.SetParam("coilVoltage", "24DC"));
        b.Wire(l1, "L1", g1, "L").Wire(g1, "N", n, "N");
        b.Wire(g1, "+", b1, "BN").Wire(b1, "BU", g1, "-");
        b.Wire(b1, "BK", k1, "A1").Wire(k1, "A2", g1, "-");
        Simulator sim = b.Start();
        sim.Run(0.1);
        Assert.False(sim.Of(k1).Visual.Active);
        sim.Press(b1);
        sim.Run(0.1);
        Assert.True(sim.Of(k1).Visual.Active);
        Assert.True(sim.Of(b1).Visual.Active);
        Assert.DoesNotContain(sim.Problems, p => p.Severity != ProblemSeverity.Info);
    }

    [Fact]
    public void Proximity_sensor_on_mains_burns()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var b1 = b.Add("prox-inductive", "B1");
        b.Wire(l1, "L1", b1, "BN").Wire(b1, "BU", n, "N");
        Simulator sim = b.Start();
        Assert.True(sim.Has("sensor-burned"));
    }

    // ---- Yükler ----

    [Fact]
    public void Horn_reports_sound_and_valve_opens()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var p1 = b.Add("horn", "P1");
        var y1 = b.Add("solenoid-valve", "Y1");
        var m1 = b.Add("fan", "M1");
        b.Wire(l1, "L1", p1, "X1").Wire(p1, "X2", n, "N");
        b.Wire(l1, "L1", y1, "A1").Wire(y1, "A2", n, "N");
        b.Wire(l1, "L1", m1, "X1").Wire(m1, "X2", n, "N");
        Simulator sim = b.Start();
        sim.Run(3);
        Assert.True(sim.AnySoundActive);
        Assert.True(sim.Of(y1).Visual.Active);
        Assert.True(sim.Of(m1).Visual.Level > 0.9);
    }

    // ---- Röleler ----

    [Fact]
    public void Counter_switches_after_preset_pulses_and_resets()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var s1 = b.Add("button-no", "S1");
        var s2 = b.Add("button-no", "S2");
        var k1 = b.Add("counter", "K1", c => c.SetParam("preset", "3"));
        var k1a = b.Add("contact-no", "K1");
        var h1 = b.Add("lamp", "H1");
        b.Wire(l1, "L1", s1, "1").Wire(s1, "2", k1, "A1").Wire(k1, "A2", n, "N");
        b.Wire(l1, "L1", s2, "1").Wire(s2, "2", k1, "R1").Wire(k1, "R2", n, "N");
        b.Wire(l1, "L1", k1a, "1").Wire(k1a, "2", h1, "X1").Wire(h1, "X2", n, "N");
        Simulator sim = b.Start();
        for (int i = 0; i < 2; i++)
        {
            sim.Click(s1);
            sim.Run(0.1);
        }
        Assert.False(sim.Of(h1).Visual.Active);
        sim.Click(s1);
        sim.Run(0.1);
        Assert.True(sim.Of(h1).Visual.Active);
        sim.Click(s2);
        sim.Run(0.1);
        Assert.False(sim.Of(h1).Visual.Active);
    }

    [Fact]
    public void Flasher_blinks_its_lamp()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var k1 = b.Add("flasher", "K1", c => { c.SetParam("onTime", 0.5); c.SetParam("offTime", 0.5); });
        var k1a = b.Add("contact-no", "K1");
        var h1 = b.Add("lamp", "H1");
        b.Wire(l1, "L1", k1, "A1").Wire(k1, "A2", n, "N");
        b.Wire(l1, "L1", k1a, "1").Wire(k1a, "2", h1, "X1").Wire(h1, "X2", n, "N");
        Simulator sim = b.Start();
        var states = new List<bool>();
        for (int i = 0; i < 8; i++)
        {
            sim.Run(0.25);
            states.Add(sim.Of(h1).Visual.Active);
        }
        Assert.Contains(true, states);
        Assert.Contains(false, states.Skip(2));
        Assert.True(states.Zip(states.Skip(1)).Count(p => p.First != p.Second) >= 2);
    }

    [Fact]
    public void Star_delta_timer_relay_switches_with_a_pause()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var kt = b.Add("timer-stardelta", "KT1", c => c.SetParam("time", 2));
        var hy = b.Add("lamp", "H1");
        var hd = b.Add("lamp", "H2");
        b.Wire(l1, "L1", kt, "A1").Wire(kt, "A2", n, "N");
        b.Wire(l1, "L1", kt, "17").Wire(kt, "18", hy, "X1").Wire(hy, "X2", n, "N").Wire(kt, "28", hd, "X1").Wire(hd, "X2", n, "N");
        Simulator sim = b.Start();
        sim.Run(0.5);
        Assert.True(sim.Of(hy).Visual.Active);
        Assert.False(sim.Of(hd).Visual.Active);
        sim.Run(1.52); // süre doldu, bekleme anında ikisi de kapalı
        Assert.False(sim.Of(hy).Visual.Active);
        Assert.False(sim.Of(hd).Visual.Active);
        sim.Run(0.1);
        Assert.True(sim.Of(hd).Visual.Active);
    }

    [Fact]
    public void Phase_monitor_detects_reversed_sequence()
    {
        foreach (bool swap in new[] { false, true })
        {
            var b = new CircuitBuilder();
            var supply = b.Add("source-3ph");
            var n = b.Add("source-n");
            var f1 = b.Add("phase-monitor", "F1");
            var h1 = b.Add("lamp", "H1");
            b.Wire(supply, "L1", f1, "L1").Wire(supply, swap ? "L3" : "L2", f1, "L2").Wire(supply, swap ? "L2" : "L3", f1, "L3");
            b.Wire(supply, "L1", f1, "11").Wire(f1, "14", h1, "X1").Wire(h1, "X2", n, "N");
            Simulator sim = b.Start();
            sim.Run(1);
            Assert.Equal(!swap, sim.Of(h1).Visual.Active);
            Assert.Equal(swap, sim.Has("phase-sequence"));
        }
    }

    // ---- Motorlar ----

    [Fact]
    public void Dahlander_motor_runs_at_two_speeds()
    {
        var b = new CircuitBuilder();
        var supply = b.Add("source-3ph");
        var low = b.Add("cam-012", "Q1");
        var m1 = b.Add("motor-dahlander", "M1");
        // 1: düşük devir (1U 1V 1W), 2: yüksek devir (2U 2V 2W) + yıldız köprüsü ayrı bir şalterle
        var bridge = b.Add("switch-3p", "Q2");
        b.Wire(supply, "L1", low, "1").Wire(supply, "L2", low, "3").Wire(supply, "L3", low, "5");
        b.Wire(low, "2", m1, "1U").Wire(low, "4", m1, "1V").Wire(low, "6", m1, "1W");
        b.Wire(low, "8", m1, "2U").Wire(low, "10", m1, "2V").Wire(low, "12", m1, "2W");
        b.Wire(m1, "1U", bridge, "1").Wire(bridge, "2", bridge, "4").Wire(m1, "1V", bridge, "3")
         .Wire(bridge, "4", bridge, "6").Wire(m1, "1W", bridge, "5");
        Simulator sim = b.Start();
        Act(sim, low, InteractionKind.SetValue, 1);
        sim.Run(4);
        Assert.InRange(sim.Of(m1).Visual.Level, 0.4, 0.5);
        Act(sim, low, InteractionKind.SetValue, 2);
        sim.Press(bridge);
        sim.Run(4);
        Assert.InRange(sim.Of(m1).Visual.Level, 0.9, 1.0);
        Assert.Equal(SimStatus.Running, sim.Status);
    }

    [Fact]
    public void Two_winding_motor_uses_the_supplied_winding()
    {
        var b = new CircuitBuilder();
        var supply = b.Add("source-3ph");
        var m1 = b.Add("motor-2speed", "M1");
        b.Wire(supply, "L1", m1, "2U").Wire(supply, "L2", m1, "2V").Wire(supply, "L3", m1, "2W");
        Simulator sim = b.Start();
        sim.Run(4);
        Assert.InRange(sim.Of(m1).Visual.Level, 0.9, 1.0);
    }

    [Fact]
    public void Brake_motor_needs_its_brake_coil()
    {
        foreach (bool brakeSupplied in new[] { false, true })
        {
            var b = new CircuitBuilder();
            var supply = b.Add("source-3ph");
            var n = b.Add("source-n");
            var m1 = b.Add("motor-brake", "M1");
            b.Wire(supply, "L1", m1, "U").Wire(supply, "L2", m1, "V").Wire(supply, "L3", m1, "W");
            if (brakeSupplied)
            {
                b.Wire(supply, "L1", m1, "B1").Wire(m1, "B2", n, "N");
            }
            Simulator sim = b.Start();
            sim.Run(3);
            Assert.Equal(brakeSupplied, sim.Of(m1).Visual.Active);
            Assert.Equal(!brakeSupplied, sim.Has("motor-brake"));
        }
    }

    [Fact]
    public void Dc_motor_direction_follows_polarity()
    {
        foreach (bool swap in new[] { false, true })
        {
            var b = new CircuitBuilder();
            var l1 = b.Add("source-l1");
            var n = b.Add("source-n");
            var g1 = b.Add("psu-dc", "G1");
            var m1 = b.Add("motor-dc", "M1", c => c.SetParam("power", 30));
            b.Wire(l1, "L1", g1, "L").Wire(g1, "N", n, "N");
            b.Wire(g1, swap ? "-" : "+", m1, "A1").Wire(m1, "A2", g1, swap ? "+" : "-");
            Simulator sim = b.Start();
            sim.Run(3);
            Assert.True(sim.Of(m1).Visual.Active);
            Assert.Equal(swap ? -1 : 1, sim.Of(m1).Visual.Direction);
        }
    }

    // ---- Besleme ----

    [Fact]
    public void Control_transformer_supplies_a_24V_coil()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var t1 = b.Add("transformer", "T1");
        var k1 = b.Add("relay-coil", "K1", c => c.SetParam("coilVoltage", "24AC"));
        b.Wire(l1, "L1", t1, "1").Wire(t1, "2", n, "N");
        b.Wire(t1, "3", k1, "A1").Wire(k1, "A2", t1, "4");
        Simulator sim = b.Start();
        sim.Run(0.1);
        Assert.True(sim.Of(k1).Visual.Active);
        string sec = sim.Of(t1).Readouts(sim.Solution!).Single(r => r.Name == Localization.Loc.T("readout.secondaryVoltage")).Value;
        Assert.StartsWith("2", sec);
        Assert.False(sim.Has("coil-undervoltage"));
    }

    [Fact]
    public void Shorted_transformer_secondary_burns_it_unless_fused()
    {
        foreach (bool fused in new[] { false, true })
        {
            var b = new CircuitBuilder();
            var l1 = b.Add("source-l1");
            var n = b.Add("source-n");
            var t1 = b.Add("transformer", "T1");
            b.Wire(l1, "L1", t1, "1").Wire(t1, "2", n, "N");
            if (fused)
            {
                var f1 = b.Add("fuse-1p", "F1", c => c.SetParam("ratedCurrent", "4"));
                b.Wire(t1, "3", f1, "1").Wire(f1, "2", t1, "4");
            }
            else
            {
                b.Wire(t1, "3", t1, "4");
            }
            Simulator sim = b.Start();
            sim.Run(10);
            Assert.Equal(!fused, sim.Has("transformer-burned"));
            Assert.Equal(fused, sim.Has("fuse-blown"));
        }
    }

    [Fact]
    public void Dc_supply_limits_current_on_short_without_stopping_the_simulation()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var g1 = b.Add("psu-dc", "G1");
        b.Wire(l1, "L1", g1, "L").Wire(g1, "N", n, "N").Wire(g1, "+", g1, "-");
        Simulator sim = b.Start();
        sim.Run(3);
        Assert.Equal(SimStatus.Running, sim.Status);
        Assert.True(sim.Has("psu-overload"));
        Assert.Single(sim.Log, e => e.Text.Contains("G1") && e.Severity == ProblemSeverity.Warning);
        Assert.InRange(sim.WireFlows.Values.Max(f => f.Amps), 2.9, 3.1); // 1,2 × 2,5 A
    }

    [Fact]
    public void Sheet_links_and_terminals_carry_the_circuit()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var a1 = b.Add("sheet-link", "A");
        var a2 = b.Add("sheet-link", "A");
        a2.Sheet = "s2";
        var x1 = b.Add("terminal", "X1");
        var h1 = b.Add("lamp", "H1");
        h1.Sheet = "s2";
        b.Wire(l1, "L1", x1, "1").Wire(x1, "2", a1, "1").Wire(a2, "1", h1, "X1").Wire(h1, "X2", n, "N");
        Simulator sim = b.Start();
        Assert.True(sim.Of(h1).Visual.Active);
    }

    // ---- Ölçüm ----

    [Fact]
    public void Unpowered_circuit_measures_resistance_through_loads()
    {
        var b = new CircuitBuilder();
        var l1 = b.Add("source-l1");
        var n = b.Add("source-n");
        var s1 = b.Add("button-no", "S1");
        var h1 = b.Add("lamp", "H1", c => c.SetParam("power", 100));
        b.Wire(l1, "L1", s1, "1").Wire(s1, "2", h1, "X1").Wire(h1, "X2", n, "N");
        var sim = new Simulator(b.Circuit, powered: false);
        Assert.False(sim.Of(h1).Visual.Active);
        int a = sim.Netlist.NodeOf(s1.Id, "2"), c = sim.Netlist.NodeOf(h1.Id, "X2");
        Assert.InRange(sim.MeasureResistance(a, c) ?? 0, 520, 540); // 230² / 100 W
        int open = sim.Netlist.NodeOf(s1.Id, "1");
        Assert.Null(sim.MeasureResistance(open, a)); // açık buton
    }
}
