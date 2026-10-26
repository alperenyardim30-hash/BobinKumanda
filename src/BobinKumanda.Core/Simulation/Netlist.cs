using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Simulation;

/// <summary>Kablo grafındaki bir köşe: klemens, ek noktası ya da boştaki kablo ucu.</summary>
public readonly record struct NetPoint(char Kind, string Id, string Sub)
{
    public static NetPoint Terminal(string component, string terminal) => new('T', component, terminal);
    public static NetPoint Junction(string id) => new('J', id, "");
    public static NetPoint FreeEnd(string wire, bool a) => new('F', wire, a ? "A" : "B");

    public bool IsTerminal => Kind == 'T';

    public static NetPoint Of(Wire w, bool a)
    {
        WireEnd end = a ? w.A : w.B;
        if (end.IsTerminal)
        {
            return Terminal(end.Component!, end.Terminal!);
        }
        if (end.IsJunction)
        {
            return Junction(end.Junction!);
        }
        return FreeEnd(w.Id, a);
    }
}

/// <summary>Bir iletkenin (eş potansiyelli kablolar grubu) içeriği.</summary>
public sealed class Net
{
    public int Node { get; init; }
    public List<NetPoint> Points { get; } = [];
    public List<Wire> Wires { get; } = [];
}

/// <summary>Kablolardan ve kalıcı bağlantılardan iletkenleri (düğümleri) çıkarır.</summary>
public sealed class Netlist
{
    private readonly Dictionary<NetPoint, int> pointNode = [];
    private readonly Dictionary<string, int> wireNode = new(StringComparer.Ordinal);

    public List<Net> Nets { get; } = [];
    public int NodeCount => Nets.Count;

    public int NodeOf(string component, string terminal) =>
        pointNode.TryGetValue(NetPoint.Terminal(component, terminal), out int n) ? n : -1;

    public int NodeOf(NetPoint p) => pointNode.TryGetValue(p, out int n) ? n : -1;

    public int NodeOfWire(string wireId) => wireNode.TryGetValue(wireId, out int n) ? n : -1;

    public static Netlist Build(Circuit circuit)
    {
        var uf = new UnionFind<NetPoint>();

        foreach (ComponentInstance c in circuit.Components)
        {
            ComponentDef? def = ComponentCatalog.Find(c.Type);
            if (def == null)
            {
                continue;
            }
            foreach (TerminalDef t in def.Terminals)
            {
                uf.Add(NetPoint.Terminal(c.Id, t.Id));
            }
            foreach ((string a, string b) in def.NetLinks)
            {
                uf.Union(NetPoint.Terminal(c.Id, a), NetPoint.Terminal(c.Id, b));
            }
        }

        // Aynı adlı sayfa bağlantı okları aynı iletkendir
        var byLabel = new Dictionary<string, NetPoint>(StringComparer.OrdinalIgnoreCase);
        foreach (ComponentInstance c in circuit.Components)
        {
            ComponentDef? def = ComponentCatalog.Find(c.Type);
            if (def is not { LinksByLabel: true } || def.Terminals.Count == 0 || c.Label.Length == 0)
            {
                continue;
            }
            var p = NetPoint.Terminal(c.Id, def.Terminals[0].Id);
            if (byLabel.TryGetValue(c.Label.Trim(), out NetPoint first))
            {
                uf.Union(first, p);
            }
            else
            {
                byLabel[c.Label.Trim()] = p;
            }
        }

        foreach (Junction j in circuit.Junctions)
        {
            uf.Add(NetPoint.Junction(j.Id));
        }

        foreach (Wire w in circuit.Wires)
        {
            NetPoint a = NetPoint.Of(w, true), b = NetPoint.Of(w, false);
            uf.Union(a, b);
        }

        var netlist = new Netlist();
        var rootNode = new Dictionary<NetPoint, int>();
        foreach (NetPoint p in uf.Items)
        {
            NetPoint root = uf.Find(p);
            if (!rootNode.TryGetValue(root, out int node))
            {
                node = netlist.Nets.Count;
                rootNode[root] = node;
                netlist.Nets.Add(new Net { Node = node });
            }
            netlist.pointNode[p] = node;
            netlist.Nets[node].Points.Add(p);
        }
        foreach (Wire w in circuit.Wires)
        {
            int node = netlist.pointNode[NetPoint.Of(w, true)];
            netlist.wireNode[w.Id] = node;
            netlist.Nets[node].Wires.Add(w);
        }
        return netlist;
    }
}

internal sealed class UnionFind<T> where T : notnull
{
    private readonly Dictionary<T, T> parent = [];
    private readonly List<T> items = [];

    public IReadOnlyList<T> Items => items;

    public void Add(T x)
    {
        if (!parent.ContainsKey(x))
        {
            parent[x] = x;
            items.Add(x);
        }
    }

    public T Find(T x)
    {
        Add(x);
        T root = x;
        while (!EqualityComparer<T>.Default.Equals(parent[root], root))
        {
            root = parent[root];
        }
        while (!EqualityComparer<T>.Default.Equals(parent[x], root))
        {
            T next = parent[x];
            parent[x] = root;
            x = next;
        }
        return root;
    }

    public void Union(T a, T b)
    {
        T ra = Find(a), rb = Find(b);
        if (!EqualityComparer<T>.Default.Equals(ra, rb))
        {
            parent[ra] = rb;
        }
    }
}
