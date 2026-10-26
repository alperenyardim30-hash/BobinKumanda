using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Editing;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Samples;

/// <summary>Örnek devreleri kodla kurmak için: eleman ekleme, klemens konumu ve dik açılı kablo.</summary>
internal sealed class SampleBuilder
{
    public Circuit Circuit { get; } = new();

    /// <summary>Bir kablo ucu: klemens ya da ek noktası.</summary>
    public readonly record struct End(ComponentInstance? C, string? T, Junction? J)
    {
        public static implicit operator End(Junction j) => new(null, null, j);
        public static implicit operator End((ComponentInstance C, string T) t) => new(t.C, t.T, null);
    }

    public ComponentInstance Add(string type, string label, double x, double y, int rotation = 0, bool mirror = false,
        params (string Key, string Value)[] parameters)
    {
        ComponentDef def = ComponentCatalog.Get(type);
        var c = new ComponentInstance
        {
            Id = Circuit.NewId("c"),
            Type = type,
            Label = label,
            Position = new Vec(x, y),
            Rotation = rotation,
            Mirror = mirror
        };
        def.ApplyDefaults(c);
        foreach ((string k, string v) in parameters)
        {
            c.Params[k] = v;
        }
        Circuit.Components.Add(c);
        return c;
    }

    /// <summary>Elemanı, verilen klemensi (x, y) noktasına gelecek şekilde ekler.</summary>
    public ComponentInstance AddAt(string type, string label, string terminal, double x, double y, int rotation = 0, bool mirror = false,
        params (string Key, string Value)[] parameters)
    {
        ComponentInstance c = Add(type, label, 0, 0, rotation, mirror, parameters);
        Vec t = Pos(c, terminal);
        c.Position = new Vec(x - t.X, y - t.Y);
        return c;
    }

    public Junction J(double x, double y)
    {
        var j = new Junction { Id = Circuit.NewId("j"), Position = new Vec(x, y) };
        Circuit.Junctions.Add(j);
        return j;
    }

    public static Vec Pos(ComponentInstance c, string terminal)
    {
        ComponentDef def = ComponentCatalog.Get(c.Type);
        return def.TerminalWorld(c, def.FindTerminal(terminal) ?? throw new ArgumentException($"{c.Type}: {terminal} yok"));
    }

    public static Vec Pos(End e) => e.J?.Position ?? Pos(e.C!, e.T!);

    private static Side SideOf(End e)
    {
        if (e.J != null)
        {
            return Side.None;
        }
        ComponentDef def = ComponentCatalog.Get(e.C!.Type);
        return def.TerminalSide(e.C, def.FindTerminal(e.T!)!);
    }

    private static WireEnd WireEndOf(End e) => e.J != null ? WireEnd.ToJunction(e.J.Id) : WireEnd.ToTerminal(e.C!.Id, e.T!);

    /// <summary>
    /// Kablo: köşeler verilirse onlardan geçer; verilmezse aynı hizadaki uçlar düz, yatay çıkan uçlar ortadan kırılan
    /// Z biçiminde, diğerleri otomatik yönlendirilir.
    /// </summary>
    public Wire W(End a, End b, params Vec[] via)
    {
        Vec pa = Pos(a), pb = Pos(b);
        var pts = new List<Vec> { pa };
        if (via.Length > 0)
        {
            pts.AddRange(via);
        }
        else if (Math.Abs(pa.X - pb.X) > 0.01 && Math.Abs(pa.Y - pb.Y) > 0.01)
        {
            Side sa = SideOf(a), sb = SideOf(b);
            bool horizontal = sa is Side.Left or Side.Right or Side.None && sb is Side.Left or Side.Right or Side.None;
            if (horizontal)
            {
                double mx = Math.Round((pa.X + pb.X) / 20) * 10;
                pts.Add(new Vec(mx, pa.Y));
                pts.Add(new Vec(mx, pb.Y));
            }
            else
            {
                List<Vec> route = CircuitEditor.RouteBetween(Circuit, "s1", pa, sa, pb, sb);
                pts = route;
            }
        }
        if (pts[^1] != pb)
        {
            pts.Add(pb);
        }
        return CircuitEditor.AddWire(Circuit, "s1", WireEndOf(a), WireEndOf(b), pts);
    }

    public TextNote Text(double x, double y, string text)
    {
        var t = new TextNote { Id = Circuit.NewId("t"), Position = new Vec(x, y), Text = text, Size = new Vec(200, 12) };
        Circuit.Texts.Add(t);
        return t;
    }
}

/// <summary>
/// Kumanda devresi merdiven düzeni: solda L1 (ya da başka besleme) dikey hattı, sağda N hattı, aralarında yatay
/// basamaklar. Her basamak soldan sağa seri elemanlardan oluşur, son eleman (bobin, lamba) N hattının yanına oturur.
/// Basamağın iki düğümü arasına paralel kol (mühürleme kontağı gibi) eklenebilir.
/// </summary>
internal sealed class Ladder
{
    private readonly SampleBuilder b;
    private readonly List<Rung> rungs = [];
    private readonly List<(double Y, SampleBuilder.End End)> leftTaps = [], rightTaps = [];

    private readonly SampleBuilder.End phaseTop, neutralTop;

    public double Left { get; }
    public double Right { get; }

    public Ladder(SampleBuilder b, double left, double right, double top, string phase = "source-l1", string neutral = "source-n")
    {
        this.b = b;
        Left = left;
        Right = right;
        ComponentDef pd = ComponentCatalog.Get(phase), nd = ComponentCatalog.Get(neutral);
        phaseTop = (b.AddAt(phase, pd.DefaultLabel ?? "", pd.Terminals[0].Id, left, top + 30), pd.Terminals[0].Id);
        neutralTop = (b.AddAt(neutral, nd.DefaultLabel ?? "", nd.Terminals[0].Id, right, top + 30), nd.Terminals[0].Id);
    }

    /// <summary>Hatlar var olan uçlardan başlar (trafo sekonderi, DC güç kaynağı çıkışı); uçlar hattın üstünde olmalı.</summary>
    public Ladder(SampleBuilder b, double left, double right, SampleBuilder.End phase, SampleBuilder.End neutral)
    {
        this.b = b;
        Left = left;
        Right = right;
        phaseTop = phase;
        neutralTop = neutral;
    }

    /// <summary>Basamak dışında bir ucu sol (L) ya da sağ (N) hatta bağlar (hat o yükseklikte ek noktası alır).</summary>
    public void Tap(bool left, double y, SampleBuilder.End end) => (left ? leftTaps : rightTaps).Add((y, end));

    public Rung Rung(double y)
    {
        var r = new Rung(b, this, y);
        rungs.Add(r);
        return r;
    }

    /// <summary>Basamakları ve iki dikey hattı kablolar.</summary>
    public void Build()
    {
        foreach (Rung r in rungs)
        {
            r.Build();
        }
        Rail(Left, phaseTop, rungs.Where(r => r.FromRail).Select(r => (r.Y, r.First)).Concat(leftTaps).ToList());
        Rail(Right, neutralTop, rungs.Where(r => r.ToRail).Select(r => (r.Y, r.Last)).Concat(rightTaps).ToList());
    }

    /// <summary>
    /// Dikey hat: her basamak yüksekliğinde ek noktası, en uzaktaki basamakta köşe. Besleme ucu basamakların altındaysa
    /// hat aşağıdan yukarı kurulur.
    /// </summary>
    private void Rail(double x, SampleBuilder.End source, List<(double Y, SampleBuilder.End End)> taps)
    {
        Vec top = SampleBuilder.Pos(source);
        bool upward = taps.Count > 0 && top.Y > taps.Max(t => t.Y);
        taps.Sort((p, q) => upward ? q.Y.CompareTo(p.Y) : p.Y.CompareTo(q.Y));
        SampleBuilder.End previous = source;
        // hattın ucu hat çizgisinde değilse önce hatta gir
        if (Math.Abs(top.X - x) > 0.01 && taps.Count > 0)
        {
            Junction start = b.J(x, top.Y);
            b.W(source, start);
            previous = start;
        }
        for (int i = 0; i < taps.Count; i++)
        {
            (double y, SampleBuilder.End end) = taps[i];
            if (end.J is { } onRail && Math.Abs(onRail.Position.X - x) < 0.01 && Math.Abs(onRail.Position.Y - y) < 0.01)
            {
                // uç zaten hat üzerindeki bir ek noktası
                b.W(previous, onRail);
                previous = onRail;
                continue;
            }
            if (i == taps.Count - 1)
            {
                b.W(previous, end, new Vec(x, y));
            }
            else
            {
                Junction j = b.J(x, y);
                b.W(previous, j);
                b.W(j, end);
                previous = j;
            }
        }
    }
}

/// <summary>Merdivenin bir basamağı.</summary>
internal sealed class Rung
{
    private readonly SampleBuilder b;
    private readonly Ladder ladder;
    private readonly List<Item> items = [];
    private readonly List<(int From, int To, double Y, List<Item> Items)> branches = [];
    private readonly Dictionary<int, Junction> nodes = [];
    private readonly Dictionary<int, List<Rung>> dependents = [];
    private bool fromParent;
    private double nextX;

    public sealed record Item(ComponentInstance C, string In, string Out);

    internal Rung(SampleBuilder b, Ladder ladder, double y)
    {
        this.b = b;
        this.ladder = ladder;
        Y = y;
        nextX = ladder.Left + 30;
    }

    public double Y { get; }
    public bool FromRail => !fromParent;
    public bool ToRail { get; private set; } = true;

    /// <summary>Basamağın sol hatta (ya da ana basamağa) bağlandığı uç: ilk düğümde ek noktası varsa o.</summary>
    public SampleBuilder.End First => nodes.TryGetValue(0, out Junction? j) ? j : (items[0].C, items[0].In);

    /// <summary>Basamağın N hattına bağlandığı uç.</summary>
    public SampleBuilder.End Last => nodes.TryGetValue(items.Count, out Junction? j) ? j : (items[^1].C, items[^1].Out);

    public IReadOnlyList<Item> Items => items;

    /// <summary>Seri eleman ekler; giriş klemensi basamak çizgisine oturur.</summary>
    public Rung Add(string type, string label, string? input = null, string? output = null, params (string, string)[] parameters)
    {
        ComponentDef def = ComponentCatalog.Get(type);
        string i = input ?? def.Terminals[0].Id, o = output ?? def.Terminals[1].Id;
        ComponentInstance c = b.AddAt(type, label, i, nextX, Y, parameters: parameters);
        nextX = SampleBuilder.Pos(c, o).X + 40;
        items.Add(new Item(c, i, o));
        return this;
    }

    /// <summary>Basamağın sonundaki yük (bobin, lamba): N hattının hemen soluna yerleşir.</summary>
    public Rung End(string type, string label, string? input = null, string? output = null, params (string, string)[] parameters)
    {
        ComponentDef def = ComponentCatalog.Get(type);
        string i = input ?? def.Terminals[0].Id, o = output ?? def.Terminals[1].Id;
        ComponentInstance c = b.AddAt(type, label, i, 0, Y, parameters: parameters);
        double width = SampleBuilder.Pos(c, o).X - SampleBuilder.Pos(c, i).X;
        c.Position = new Vec(c.Position.X + (ladder.Right - 30 - width), c.Position.Y);
        items.Add(new Item(c, i, o));
        return this;
    }

    /// <summary>
    /// Basamak sol hattan değil, başka bir basamağın düğümünden başlar (ortak stop butonu gibi). Aynı düğümden inen
    /// basamaklar dikey bir ara hatla birbirine bağlanır. Elemanlardan önce çağrılmalıdır.
    /// </summary>
    public Rung From(Rung parent, int node)
    {
        fromParent = true;
        parent.Node(node);
        if (!parent.dependents.TryGetValue(node, out var list))
        {
            list = [];
            parent.dependents[node] = list;
        }
        list.Add(this);
        nextX = parent.NodeX(node) + 30;
        return this;
    }

    /// <summary>Basamak N hattına bağlanmaz (son eleman başka yere kablolanacak).</summary>
    public Rung Open()
    {
        ToRail = false;
        return this;
    }

    /// <summary>
    /// Düğüm <paramref name="from"/> ile <paramref name="to"/> arasına alt satırda paralel kol. Düğüm k, k. elemanın
    /// girişinin hemen solundadır (k = eleman sayısı: son elemanın çıkışının sağı).
    /// </summary>
    public Rung Parallel(int from, int to, double dy, params (string Type, string Label)[] parts)
    {
        Node(from);
        Node(to);
        double x = NodeX(from) + 20;
        var list = new List<Item>();
        foreach ((string type, string label) in parts)
        {
            ComponentDef def = ComponentCatalog.Get(type);
            ComponentInstance c = b.AddAt(type, label, def.Terminals[0].Id, x, Y + dy);
            x = SampleBuilder.Pos(c, def.Terminals[1].Id).X + 30;
            list.Add(new Item(c, def.Terminals[0].Id, def.Terminals[1].Id));
        }
        branches.Add((from, to, Y + dy, list));
        return this;
    }

    public double NodeX(int k)
    {
        if (nodes.TryGetValue(k, out Junction? existing))
        {
            return existing.Position.X;
        }
        if (k <= 0)
        {
            return SampleBuilder.Pos(items[0].C, items[0].In).X - 20;
        }
        if (k >= items.Count)
        {
            return SampleBuilder.Pos(items[^1].C, items[^1].Out).X + 10;
        }
        double a = SampleBuilder.Pos(items[k - 1].C, items[k - 1].Out).X, c = SampleBuilder.Pos(items[k].C, items[k].In).X;
        return Math.Round((a + c) / 20) * 10;
    }

    /// <summary>Düğümdeki ek noktası (yoksa oluşturur); paralel kollar ve başka basamaklar buna bağlanır.</summary>
    public Junction Node(int k)
    {
        if (!nodes.TryGetValue(k, out Junction? j))
        {
            j = b.J(NodeX(k), Y);
            nodes[k] = j;
        }
        return j;
    }

    internal void Build()
    {
        if (nodes.TryGetValue(0, out Junction? j0))
        {
            b.W(j0, (items[0].C, items[0].In));
        }
        if (nodes.TryGetValue(items.Count, out Junction? jn))
        {
            b.W((items[^1].C, items[^1].Out), jn);
        }
        for (int k = 1; k < items.Count; k++)
        {
            SampleBuilder.End from = (items[k - 1].C, items[k - 1].Out), to = (items[k].C, items[k].In);
            if (nodes.TryGetValue(k, out Junction? j))
            {
                b.W(from, j);
                b.W(j, to);
            }
            else
            {
                b.W(from, to);
            }
        }
        foreach (var (from, to, y, list) in branches)
        {
            Junction ja = nodes[from], jb = nodes[to];
            b.W(ja, (list[0].C, list[0].In), new Vec(ja.Position.X, y));
            for (int k = 1; k < list.Count; k++)
            {
                b.W((list[k - 1].C, list[k - 1].Out), (list[k].C, list[k].In));
            }
            b.W((list[^1].C, list[^1].Out), jb, new Vec(jb.Position.X, y));
        }
        // Bu basamağın düğümlerinden inen basamaklar: dikey ara hat
        foreach (var (node, deps) in dependents)
        {
            Junction top = nodes[node];
            double x = top.Position.X;
            deps.Sort((p, q) => p.Y.CompareTo(q.Y));
            SampleBuilder.End previous = top;
            for (int i = 0; i < deps.Count; i++)
            {
                Rung d = deps[i];
                if (i == deps.Count - 1)
                {
                    b.W(previous, d.First, new Vec(x, d.Y));
                }
                else
                {
                    Junction j = b.J(x, d.Y);
                    b.W(previous, j);
                    b.W(j, d.First);
                    previous = j;
                }
            }
        }
    }
}
