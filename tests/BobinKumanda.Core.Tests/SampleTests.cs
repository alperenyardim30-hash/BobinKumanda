using BobinKumanda.Core.Model;
using BobinKumanda.Core.Samples;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Tests;

/// <summary>Örnek devreler: hepsi sorunsuz kurulur ve anlatıldığı gibi çalışır.</summary>
public class SampleTests
{
    public static TheoryData<string> Ids => new(SampleLibrary.All.Select(s => s.Id));

    private static (Circuit C, Simulator Sim) Start(string id)
    {
        Circuit c = SampleLibrary.Find(id)!.Build();
        return (c, new Simulator(c));
    }

    private static ComponentInstance By(Circuit c, string label, string? type = null) =>
        c.Components.First(x => x.Label == label && (type == null || x.Type == type));

    private static void Act(Simulator sim, ComponentInstance c, InteractionKind kind, double value = 0) =>
        sim.Interact(c.Id, new Interaction(kind, value));

    [Theory]
    [MemberData(nameof(Ids))]
    public void Sample_builds_without_loose_wires_or_problems(string id)
    {
        var (c, sim) = Start(id);
        Assert.NotEmpty(c.Title.CircuitName);
        Assert.All(c.Wires, w => Assert.False(w.A.IsFree || w.B.IsFree, $"{id}: {w.Id} boşta"));
        Assert.All(c.Wires, w => Assert.True(Editing.WirePath.IsOrthogonal(w.Points), $"{id}: {w.Id} dik değil"));
        sim.Run(1);
        Assert.Equal(SimStatus.Running, sim.Status);
        Assert.DoesNotContain(sim.Problems, p => p.Severity != ProblemSeverity.Info);
        // çakışan eleman yok
        var boxes = c.Components.Select(x => (x, Catalog.ComponentCatalog.Get(x.Type).Bounds(x).Inflate(-1))).ToList();
        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
            {
                Assert.False(boxes[i].Item2.Intersects(boxes[j].Item2), $"{id}: {boxes[i].x.Label} ile {boxes[j].x.Label} üst üste");
            }
        }
    }

    [Fact]
    public void Direct_on_line_starts_the_motor_and_overload_lights_the_fault_lamp()
    {
        var (c, sim) = Start("dol");
        sim.Click(By(c, "S1"));
        sim.Run(3);
        Assert.True(sim.Of(By(c, "M1")).Visual.Active);
        Assert.True(sim.Of(By(c, "H1")).Visual.Active);
        Act(sim, By(c, "M1"), InteractionKind.SetValue, 200);
        sim.Run(400);
        Assert.True(sim.Of(By(c, "F2", "overload")).Visual.Tripped);
        Assert.False(sim.Of(By(c, "M1")).Visual.Active);
        Assert.True(sim.Of(By(c, "H2")).Visual.Active);
    }

    [Fact]
    public void Reversing_runs_both_ways_and_interlocks()
    {
        var (c, sim) = Start("reversing");
        sim.Click(By(c, "S1"));
        sim.Run(3);
        Assert.Equal(1, sim.Of(By(c, "M1")).Visual.Direction);
        sim.Click(By(c, "S2")); // kilit: K1 çekiliyken K2 çekemez
        sim.Run(0.5);
        Assert.False(sim.Of(By(c, "K2", "relay-coil")).Visual.Active);
        sim.Click(By(c, "S0"));
        sim.Run(3);
        sim.Click(By(c, "S2"));
        sim.Run(6);
        Assert.Equal(-1, sim.Of(By(c, "M1")).Visual.Direction);
        Assert.Equal(SimStatus.Running, sim.Status);
    }

    [Fact]
    public void Star_delta_sample_goes_from_star_to_delta()
    {
        var (c, sim) = Start("star-delta");
        sim.Click(By(c, "S1"));
        sim.Run(2);
        Assert.True(sim.Of(By(c, "KY", "relay-coil")).Visual.Active);
        Assert.False(sim.Of(By(c, "KD", "relay-coil")).Visual.Active);
        sim.Run(4);
        Assert.False(sim.Of(By(c, "KY", "relay-coil")).Visual.Active);
        Assert.True(sim.Of(By(c, "KD", "relay-coil")).Visual.Active);
        Assert.InRange(sim.Of(By(c, "M1")).Visual.Level, 0.9, 1.0);
        Assert.Equal(SimStatus.Running, sim.Status);
    }

    [Fact]
    public void Sequence_starts_second_motor_only_after_the_first()
    {
        var (c, sim) = Start("sequence");
        sim.Click(By(c, "S2"));
        sim.Run(1);
        Assert.False(sim.Of(By(c, "K2", "relay-coil")).Visual.Active);
        sim.Click(By(c, "S1"));
        sim.Click(By(c, "S2"));
        sim.Run(3);
        Assert.True(sim.Of(By(c, "M1")).Visual.Active);
        Assert.True(sim.Of(By(c, "M2")).Visual.Active);
        sim.Click(By(c, "S0"));
        sim.Run(0.5);
        Assert.False(sim.Of(By(c, "K2", "relay-coil")).Visual.Active);
    }

    [Fact]
    public void Pump_follows_the_float_switch()
    {
        var (c, sim) = Start("pump");
        var b1 = By(c, "B1");
        Act(sim, b1, InteractionKind.SetValue, 90);
        sim.Run(3);
        Assert.True(sim.Of(By(c, "M1")).Visual.Active);
        Act(sim, b1, InteractionKind.SetValue, 10);
        sim.Run(3);
        Assert.False(sim.Of(By(c, "K1", "relay-coil")).Visual.Active);
    }

    [Fact]
    public void Rcd_sample_trips_on_body_fault()
    {
        var (c, sim) = Start("rcd");
        sim.Run(2);
        Assert.True(sim.Of(By(c, "M1")).Visual.Active);
        Act(sim, By(c, "M1"), InteractionKind.Fault);
        Assert.True(sim.Of(By(c, "F1")).Visual.Tripped);
    }

    [Fact]
    public void Control_24v_sample_pulls_in_with_24V()
    {
        var (c, sim) = Start("control-24v");
        sim.Click(By(c, "S1"));
        sim.Run(0.5);
        Assert.True(sim.Of(By(c, "K1", "relay-coil")).Visual.Active);
        Assert.True(sim.Of(By(c, "H1")).Visual.Active);
    }

    [Fact]
    public void Thermostat_sample_heats_until_set_point()
    {
        var (c, sim) = Start("thermostat");
        sim.Run(2);
        Assert.True(sim.Of(By(c, "E1")).Visual.Active);
        Act(sim, By(c, "B1"), InteractionKind.SetValue, 50);
        sim.Run(0.5);
        Assert.False(sim.Of(By(c, "E1")).Visual.Active);
    }

    [Fact]
    public void Counter_sample_counts_five()
    {
        var (c, sim) = Start("counter");
        for (int i = 0; i < 5; i++)
        {
            Assert.False(sim.Of(By(c, "H1")).Visual.Active);
            sim.Click(By(c, "S1"));
            sim.Run(0.1);
        }
        Assert.True(sim.Of(By(c, "H1")).Visual.Active);
        sim.Click(By(c, "S2"));
        sim.Run(0.1);
        Assert.False(sim.Of(By(c, "H1")).Visual.Active);
    }

    [Fact]
    public void Alarm_sample_flashes_and_sounds()
    {
        var (c, sim) = Start("alarm");
        sim.Click(By(c, "S1"));
        var seen = new HashSet<bool>();
        for (int i = 0; i < 10; i++)
        {
            sim.Run(0.2);
            seen.Add(sim.Of(By(c, "H1")).Visual.Active);
        }
        Assert.Equal(2, seen.Count);
        Assert.True(sim.AnySoundActive);
    }

    [Fact]
    public void Sensor_sample_switches_the_relay()
    {
        var (c, sim) = Start("sensor");
        sim.Run(0.2);
        Assert.False(sim.Of(By(c, "H1")).Visual.Active);
        sim.Press(By(c, "B1"));
        sim.Run(0.2);
        Assert.True(sim.Of(By(c, "K1", "relay-coil")).Visual.Active);
        Assert.True(sim.Of(By(c, "H1")).Visual.Active);
    }

    [Fact]
    public void Jog_sample_runs_only_while_held()
    {
        var (c, sim) = Start("jog");
        var s2 = c.Components.First(x => x.Label == "S2" && x.Type == "button-no");
        sim.Press(s2);
        sim.Run(0.2);
        Assert.True(sim.Of(By(c, "K1", "relay-coil")).Visual.Active);
        sim.Release(s2);
        sim.Run(0.2);
        Assert.False(sim.Of(By(c, "K1", "relay-coil")).Visual.Active);
        sim.Click(By(c, "S1"));
        sim.Run(0.2);
        Assert.True(sim.Of(By(c, "K1", "relay-coil")).Visual.Active);
    }
}
