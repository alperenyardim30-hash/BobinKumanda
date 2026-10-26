using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Tests;

public class MotorTests
{
    /// <summary>Direkt yol verme: Q1 ile K1 çeker, K1 ana kontakları motoru besler.</summary>
    private static (CircuitBuilder B, ComponentInstance Q1, ComponentInstance M1, ComponentInstance Supply) DirectOnLine(bool swapPhases = false)
    {
        var b = new CircuitBuilder();
        var supply = b.Add("source-3ph");
        var n = b.Add("source-n");
        var q1 = b.Add("switch-3p", "Q1");
        var k1 = b.Add("relay-coil", "K1");
        var k1m = b.Add("contact-3no", "K1");
        var m1 = b.Add("motor-3ph", "M1");
        // kumanda: L1 - Q1 - K1 - N
        b.Wire(supply, "L1", q1, "1").Wire(q1, "2", k1, "A1").Wire(k1, "A2", n, "N");
        // güç
        b.Wire(supply, "L1", k1m, "1").Wire(supply, "L2", k1m, "3").Wire(supply, "L3", k1m, "5");
        b.Wire(k1m, "2", m1, "U");
        b.Wire(k1m, "4", m1, swapPhases ? "W" : "V");
        b.Wire(k1m, "6", m1, swapPhases ? "V" : "W");
        return (b, q1, m1, supply);
    }

    [Fact]
    public void Motor_starts_and_runs_clockwise()
    {
        var (b, q1, m1, _) = DirectOnLine();
        Simulator sim = b.Start();
        sim.Press(q1);
        sim.Run(3);
        var v = sim.Of(m1).Visual;
        Assert.True(v.Active);
        Assert.Equal(1, v.Direction);
        Assert.InRange(v.Level, 0.95, 1.0);
        Assert.Equal(SimStatus.Running, sim.Status);
    }

    [Fact]
    public void Swapping_two_phases_reverses_the_motor()
    {
        var (b, q1, m1, _) = DirectOnLine(swapPhases: true);
        Simulator sim = b.Start();
        sim.Press(q1);
        sim.Run(3);
        Assert.Equal(-1, sim.Of(m1).Visual.Direction);
    }

    [Fact]
    public void Starting_current_is_higher_than_running_current()
    {
        var (b, q1, m1, _) = DirectOnLine();
        Simulator sim = b.Start();
        sim.Press(q1);
        sim.Run(0.05);
        double start = MaxMotorWireCurrent(sim, b, m1);
        sim.Run(3);
        double run = MaxMotorWireCurrent(sim, b, m1);
        Assert.True(start > 4 * run, $"kalkış {start:0.0} A, çalışma {run:0.0} A");
        Assert.InRange(run, 2.5, 4.0); // 1.5 kW → ~3.1 A
    }

    private static double MaxMotorWireCurrent(Simulator sim, CircuitBuilder b, ComponentInstance m1) =>
        b.Circuit.Wires.Where(w => w.B.Component == m1.Id).Max(w => sim.WireFlows[w.Id].Amps);

    [Fact]
    public void Missing_phase_prevents_start_and_warns()
    {
        var (b, q1, m1, _) = DirectOnLine();
        b.Circuit.Wires.RemoveAll(w => w.B.Component == m1.Id && w.B.Terminal == "W");
        Simulator sim = b.Start();
        sim.Press(q1);
        sim.Run(2);
        Assert.False(sim.Of(m1).Visual.Active);
        Assert.True(sim.Has("motor-phase-loss"));
    }

    /// <summary>İleri-geri devresinde kilitleme yoksa iki kontaktör birlikte çekince kısa devre olur.</summary>
    [Fact]
    public void Forward_reverse_without_interlock_shorts()
    {
        var b = new CircuitBuilder();
        var supply = b.Add("source-3ph");
        var n = b.Add("source-n");
        var q1 = b.Add("switch-3p", "Q1");
        var q2 = b.Add("switch-3p", "Q2");
        var k1 = b.Add("relay-coil", "K1");
        var k2 = b.Add("relay-coil", "K2");
        var k1m = b.Add("contact-3no", "K1");
        var k2m = b.Add("contact-3no", "K2");
        var m1 = b.Add("motor-3ph", "M1");
        b.Wire(supply, "L1", q1, "1").Wire(q1, "2", k1, "A1").Wire(k1, "A2", n, "N");
        b.Wire(supply, "L1", q2, "1").Wire(q2, "2", k2, "A1").Wire(k2, "A2", n, "N");
        b.Wire(supply, "L1", k1m, "1").Wire(supply, "L2", k1m, "3").Wire(supply, "L3", k1m, "5");
        b.Wire(supply, "L1", k2m, "1").Wire(supply, "L2", k2m, "3").Wire(supply, "L3", k2m, "5");
        b.Wire(k1m, "2", m1, "U").Wire(k1m, "4", m1, "V").Wire(k1m, "6", m1, "W");
        // K2 L2 ile L3'ü değiştirir
        b.Wire(k2m, "2", m1, "U").Wire(k2m, "4", m1, "W").Wire(k2m, "6", m1, "V");
        Simulator sim = b.Start();

        sim.Press(q1);
        sim.Run(2);
        Assert.Equal(1, sim.Of(m1).Visual.Direction);
        sim.Press(q2);
        sim.Run(0.1);
        Assert.Equal(SimStatus.Faulted, sim.Status);
        Assert.True(sim.Has("short-circuit"));
    }

    /// <summary>Yıldız-üçgen: önce yıldızda (230 V/sargı) kalkar, zaman rölesi sonra üçgene geçirir.</summary>
    [Fact]
    public void Star_delta_starter_switches_from_star_to_delta()
    {
        var b = new CircuitBuilder();
        var supply = b.Add("source-3ph");
        var n = b.Add("source-n");
        var q1 = b.Add("switch-3p", "Q1");
        var km = b.Add("relay-coil", "KM");
        var ky = b.Add("relay-coil", "KY");
        var kd = b.Add("relay-coil", "KD");
        var kt = b.Add("timer-on", "KT", c => c.SetParam("time", 3));
        var ktNc = b.Add("contact-nc", "KT");
        var ktNo = b.Add("contact-no", "KT");
        var kyNc = b.Add("contact-nc", "KY", c => c.SetParam("channel", "instant"));
        var kdNc = b.Add("contact-nc", "KD");
        var kmMain = b.Add("contact-3no", "KM");
        var kyMain = b.Add("contact-3no", "KY");
        var kdMain = b.Add("contact-3no", "KD");
        var m1 = b.Add("motor-3ph-6t", "M1");

        // Kumanda: Q1 sonrası hat "x"
        b.Wire(supply, "L1", q1, "1");
        b.Wire(q1, "2", km, "A1").Wire(km, "A2", n, "N");
        b.Wire(q1, "2", kt, "A1").Wire(kt, "A2", n, "N");
        b.Wire(q1, "2", ktNc, "1").Wire(ktNc, "2", kdNc, "1").Wire(kdNc, "2", ky, "A1").Wire(ky, "A2", n, "N");
        b.Wire(q1, "2", ktNo, "1").Wire(ktNo, "2", kyNc, "1").Wire(kyNc, "2", kd, "A1").Wire(kd, "A2", n, "N");

        // Güç: KM → U1 V1 W1; KY yıldız köprüsü; KD üçgen (U1-W2, V1-U2, W1-V2)
        b.Wire(supply, "L1", kmMain, "1").Wire(supply, "L2", kmMain, "3").Wire(supply, "L3", kmMain, "5");
        b.Wire(kmMain, "2", m1, "U1").Wire(kmMain, "4", m1, "V1").Wire(kmMain, "6", m1, "W1");
        b.Wire(m1, "W2", kyMain, "1").Wire(m1, "U2", kyMain, "3").Wire(m1, "V2", kyMain, "5");
        b.Wire(kyMain, "2", kyMain, "4").Wire(kyMain, "4", kyMain, "6");
        b.Wire(kmMain, "2", kdMain, "1").Wire(kdMain, "2", m1, "W2");
        b.Wire(kmMain, "4", kdMain, "3").Wire(kdMain, "4", m1, "U2");
        b.Wire(kmMain, "6", kdMain, "5").Wire(kdMain, "6", m1, "V2");

        Simulator sim = b.Start();
        sim.Press(q1);
        sim.Run(2);
        var readouts = sim.Of(m1).Readouts(sim.Solution!).ToDictionary(r => r.Name, r => r.Value);
        Assert.True(sim.Of(ky).Visual.Active);
        Assert.False(sim.Of(kd).Visual.Active);
        Assert.StartsWith("yıldız", readouts.Values.First(v => v.Contains("V/")));

        sim.Run(3);
        Assert.False(sim.Of(ky).Visual.Active);
        Assert.True(sim.Of(kd).Visual.Active);
        Assert.Equal(SimStatus.Running, sim.Status);
        Assert.InRange(sim.Of(m1).Visual.Level, 0.95, 1.0);
        readouts = sim.Of(m1).Readouts(sim.Solution!).ToDictionary(r => r.Name, r => r.Value);
        Assert.StartsWith("üçgen", readouts.Values.First(v => v.Contains("V/")));
    }

    [Fact]
    public void Single_phase_motor_direction_follows_auxiliary_winding()
    {
        foreach (bool swap in new[] { false, true })
        {
            var b = new CircuitBuilder();
            var l1 = b.Add("source-l1");
            var n = b.Add("source-n");
            var m = b.Add("motor-1ph", "M1");
            b.Wire(l1, "L1", m, "U1").Wire(m, "U2", n, "N");
            b.Wire(l1, "L1", m, swap ? "Z2" : "Z1").Wire(m, swap ? "Z1" : "Z2", n, "N");
            Simulator sim = b.Start();
            sim.Run(3);
            Assert.True(sim.Of(m).Visual.Active);
            Assert.Equal(swap ? -1 : 1, sim.Of(m).Visual.Direction);
        }
    }
}
