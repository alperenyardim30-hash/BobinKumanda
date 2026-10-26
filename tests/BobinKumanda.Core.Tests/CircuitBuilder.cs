using BobinKumanda.Core;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Tests;

/// <summary>Testlerde kodla devre kurmak için küçük yardımcı.</summary>
internal sealed class CircuitBuilder
{
    public Circuit Circuit { get; } = new();
    private double x;

    public ComponentInstance Add(string type, string label = "", Action<ComponentInstance>? configure = null)
    {
        ComponentDef def = ComponentCatalog.Get(type);
        var c = new ComponentInstance
        {
            Id = Circuit.NewId("c"),
            Type = type,
            Label = label.Length > 0 ? label : def.DefaultLabel ?? "",
            Position = new Vec(x, 0)
        };
        x += 100;
        def.ApplyDefaults(c);
        configure?.Invoke(c);
        Circuit.Components.Add(c);
        return c;
    }

    /// <summary>İki klemens arasına düz bir kablo çeker.</summary>
    public CircuitBuilder Wire(ComponentInstance a, string ta, ComponentInstance b, string tb)
    {
        var defA = ComponentCatalog.Get(a.Type);
        var defB = ComponentCatalog.Get(b.Type);
        if (defA.FindTerminal(ta) == null || defB.FindTerminal(tb) == null)
        {
            throw new ArgumentException($"Klemens yok: {a.Type}.{ta} veya {b.Type}.{tb}");
        }
        Circuit.Wires.Add(new Wire
        {
            Id = Circuit.NewId("w"),
            A = WireEnd.ToTerminal(a.Id, ta),
            B = WireEnd.ToTerminal(b.Id, tb),
            Points = [defA.TerminalWorld(a, defA.FindTerminal(ta)!), defB.TerminalWorld(b, defB.FindTerminal(tb)!)]
        });
        return this;
    }

    public Simulator Start() => new(Circuit);
}

internal static class SimTestExtensions
{
    public static void Press(this Simulator sim, ComponentInstance c) => sim.Interact(c.Id, new Interaction(InteractionKind.Press));
    public static void Release(this Simulator sim, ComponentInstance c) => sim.Interact(c.Id, new Interaction(InteractionKind.Release));

    public static void Click(this Simulator sim, ComponentInstance c)
    {
        sim.Press(c);
        sim.Run(0.05);
        sim.Release(c);
    }

    public static void Run(this Simulator sim, double seconds) => sim.AdvanceTo(sim.Time + seconds);

    public static SimComponent Of(this Simulator sim, ComponentInstance c) => sim.Find(c.Id)!;

    public static bool Has(this Simulator sim, string code) => sim.Problems.Any(p => p.Code == code);
}
