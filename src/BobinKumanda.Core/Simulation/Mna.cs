using System.Numerics;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Simulation;

/// <summary>Bir dal ucunun bağlandığı düğüm. Term ≥ 0 ise sahibinin klemens indeksi (kablo akımları için).</summary>
public readonly record struct Pin(int Node, int Term)
{
    public static readonly Pin Ground = new(-1, -1);
}

/// <summary>
/// Dal: a'dan b'ye, seri EMF + admitans. Dal içinden a→b akan akım I = Y·(Va − Vb − E).
/// AC fazörleri ve DC değerleri aynı dalda ayrı tutulur (süperpozisyon).
/// </summary>
internal sealed class Branch
{
    public Pin A;
    public Pin B;
    public Complex Yac;
    public double Gdc;
    public Complex Eac;
    public double Edc;
    public int AcSlot = -1;
    public int DcSlot = -1;
    public bool IsSource;
    public SimComponent? Owner;
}

/// <summary>Elemanların her çözümden önce elektriksel modellerini eklediği yapı.</summary>
public sealed class Stamper
{
    /// <summary>Kapalı kontak direnci.</summary>
    public const double ClosedResistance = 1e-3;

    internal readonly List<Branch> Branches = [];
    internal readonly List<string> AcSlots = [];
    internal readonly List<string> DcSlots = [];
    internal SimComponent? CurrentOwner;

    internal void Reset()
    {
        Branches.Clear();
    }

    private int Add(Branch b)
    {
        b.Owner = CurrentOwner;
        Branches.Add(b);
        return Branches.Count - 1;
    }

    /// <summary>Pasif empedans. zAc: AC empedansı (∞ için null), rDc: DC direnci (açık devre için ∞).</summary>
    public int Impedance(Pin a, Pin b, Complex? zAc, double rDc) =>
        Add(new Branch
        {
            A = a,
            B = b,
            Yac = zAc is { } z && z != Complex.Zero ? 1 / z : Complex.Zero,
            Gdc = double.IsFinite(rDc) && rDc > 0 ? 1 / rDc : 0
        });

    public int Resistor(Pin a, Pin b, double ohms) => Impedance(a, b, new Complex(ohms, 0), ohms);

    public int Switch(Pin a, Pin b, bool closed) =>
        closed ? Resistor(a, b, ClosedResistance) : Add(new Branch { A = a, B = b });

    /// <summary>
    /// Gerilim kaynağı (Norton eşdeğeri). Açık devrede V(pos) − V(neg) = emf. Her kaynak adı (slot) için
    /// ayrı çözüm yapılır; akımın yönünü göstermek için hangi kaynağın beslediği bilinir.
    /// </summary>
    public int Source(Pin pos, Pin neg, Complex emfAc, Complex zAc, double emfDc, double rDc, string slot)
    {
        var b = new Branch
        {
            A = neg,
            B = pos,
            Yac = 1 / zAc,
            Gdc = 1 / rDc,
            // a=neg, b=pos için Va − Vb = −emf
            Eac = -emfAc,
            Edc = -emfDc,
            IsSource = true
        };
        if (emfAc != Complex.Zero)
        {
            b.AcSlot = SlotIndex(AcSlots, slot);
        }
        if (emfDc != 0)
        {
            b.DcSlot = SlotIndex(DcSlots, slot);
        }
        return Add(b);
    }

    private static int SlotIndex(List<string> slots, string slot)
    {
        int i = slots.IndexOf(slot);
        if (i < 0)
        {
            slots.Add(slot);
            i = slots.Count - 1;
        }
        return i;
    }
}

/// <summary>Bir çözümün sonucu: düğüm gerilimleri, dal akımları, kablo akımları.</summary>
public sealed class Solution
{
    public const double Gmin = 1e-9;

    private readonly Branch[] branches;
    private readonly Complex[] vac;
    private readonly double[] vdc;
    private readonly Complex[][] vacSlot;
    private readonly double[][] vdcSlot;
    private readonly string[] acSlots;
    private readonly string[] dcSlots;

    public int NodeCount { get; }

    internal Solution(int nodeCount, Stamper st)
    {
        NodeCount = nodeCount;
        branches = [.. st.Branches];
        acSlots = [.. st.AcSlots];
        dcSlots = [.. st.DcSlots];
        int n = nodeCount;

        // AC sistemi
        bool anyAc = branches.Any(b => b.AcSlot >= 0);
        vacSlot = new Complex[acSlots.Length][];
        vac = new Complex[n];
        if (anyAc && n > 0)
        {
            var m = ComplexLu.NewMatrix(n);
            for (int i = 0; i < n; i++)
            {
                m[i * n + i] += Gmin;
            }
            foreach (Branch b in branches)
            {
                if (b.Yac != Complex.Zero)
                {
                    StampY(m, n, b.A.Node, b.B.Node, b.Yac);
                }
            }
            var lu = new ComplexLu(n, m);
            for (int s = 0; s < acSlots.Length; s++)
            {
                var rhs = new Complex[n];
                foreach (Branch b in branches)
                {
                    if (b.AcSlot == s)
                    {
                        Complex j = b.Yac * b.Eac;
                        if (b.A.Node >= 0) rhs[b.A.Node] += j;
                        if (b.B.Node >= 0) rhs[b.B.Node] -= j;
                    }
                }
                vacSlot[s] = lu.Solve(rhs);
                for (int i = 0; i < n; i++)
                {
                    vac[i] += vacSlot[s][i];
                }
            }
        }
        else
        {
            for (int s = 0; s < acSlots.Length; s++)
            {
                vacSlot[s] = new Complex[n];
            }
        }

        // DC sistemi
        bool anyDc = branches.Any(b => b.DcSlot >= 0);
        vdcSlot = new double[dcSlots.Length][];
        vdc = new double[n];
        if (anyDc && n > 0)
        {
            var m = new double[n * n];
            for (int i = 0; i < n; i++)
            {
                m[i * n + i] += Gmin;
            }
            foreach (Branch b in branches)
            {
                if (b.Gdc != 0)
                {
                    StampG(m, n, b.A.Node, b.B.Node, b.Gdc);
                }
            }
            var lu = new RealLu(n, m);
            for (int s = 0; s < dcSlots.Length; s++)
            {
                var rhs = new double[n];
                foreach (Branch b in branches)
                {
                    if (b.DcSlot == s)
                    {
                        double j = b.Gdc * b.Edc;
                        if (b.A.Node >= 0) rhs[b.A.Node] += j;
                        if (b.B.Node >= 0) rhs[b.B.Node] -= j;
                    }
                }
                vdcSlot[s] = lu.Solve(rhs);
                for (int i = 0; i < n; i++)
                {
                    vdc[i] += vdcSlot[s][i];
                }
            }
        }
        else
        {
            for (int s = 0; s < dcSlots.Length; s++)
            {
                vdcSlot[s] = new double[n];
            }
        }
    }

    private static void StampY(Complex[] m, int n, int a, int b, Complex y)
    {
        if (a >= 0) m[a * n + a] += y;
        if (b >= 0) m[b * n + b] += y;
        if (a >= 0 && b >= 0)
        {
            m[a * n + b] -= y;
            m[b * n + a] -= y;
        }
    }

    private static void StampG(double[] m, int n, int a, int b, double g)
    {
        if (a >= 0) m[a * n + a] += g;
        if (b >= 0) m[b * n + b] += g;
        if (a >= 0 && b >= 0)
        {
            m[a * n + b] -= g;
            m[b * n + a] -= g;
        }
    }

    public Complex NodeAc(int node) => node >= 0 ? vac[node] : Complex.Zero;
    public double NodeDc(int node) => node >= 0 ? vdc[node] : 0;

    public Complex VoltageAc(Pin a, Pin b) => NodeAc(a.Node) - NodeAc(b.Node);
    public double VoltageDc(Pin a, Pin b) => NodeDc(a.Node) - NodeDc(b.Node);

    public Complex VoltageAc(int nodeA, int nodeB) => NodeAc(nodeA) - NodeAc(nodeB);
    public double VoltageDc(int nodeA, int nodeB) => NodeDc(nodeA) - NodeDc(nodeB);

    /// <summary>Dal üzerindeki gerilim (Va − Vb).</summary>
    public Complex BranchVoltageAc(int branch) => VoltageAc(branches[branch].A, branches[branch].B);
    public double BranchVoltageDc(int branch) => VoltageDc(branches[branch].A, branches[branch].B);

    /// <summary>Dal içinden a→b akan AC akım fazörü (efektif değer).</summary>
    public Complex CurrentAc(int branch)
    {
        Branch b = branches[branch];
        return b.Yac * (NodeAc(b.A.Node) - NodeAc(b.B.Node) - (b.AcSlot >= 0 ? b.Eac : Complex.Zero));
    }

    public double CurrentDc(int branch)
    {
        Branch b = branches[branch];
        return b.Gdc * (NodeDc(b.A.Node) - NodeDc(b.B.Node) - (b.DcSlot >= 0 ? b.Edc : 0));
    }

    internal Complex CurrentAcSlot(int branch, int slot)
    {
        Branch b = branches[branch];
        Complex[] v = vacSlot[slot];
        Complex va = b.A.Node >= 0 ? v[b.A.Node] : Complex.Zero;
        Complex vb = b.B.Node >= 0 ? v[b.B.Node] : Complex.Zero;
        return b.Yac * (va - vb - (b.AcSlot == slot ? b.Eac : Complex.Zero));
    }

    internal double CurrentDcSlot(int branch, int slot)
    {
        Branch b = branches[branch];
        double[] v = vdcSlot[slot];
        double va = b.A.Node >= 0 ? v[b.A.Node] : 0;
        double vb = b.B.Node >= 0 ? v[b.B.Node] : 0;
        return b.Gdc * (va - vb - (b.DcSlot == slot ? b.Edc : 0));
    }

    /// <summary>Herhangi bir dalda akan en büyük akım (kısa devre tespiti için).</summary>
    internal IEnumerable<(int Branch, double Amps)> LargeCurrents(double threshold)
    {
        for (int i = 0; i < branches.Length; i++)
        {
            double amps = Math.Max(CurrentAc(i).Magnitude, Math.Abs(CurrentDc(i)));
            if (amps > threshold)
            {
                yield return (i, amps);
            }
        }
    }

    internal SimComponent? BranchOwner(int branch) => branches[branch].Owner;

    internal int BranchCount => branches.Length;
    internal Branch BranchAt(int i) => branches[i];
    internal string[] AcSlotNames => acSlots;
    internal string[] DcSlotNames => dcSlots;

    /// <summary>Kaynak adına göre ideal EMF fazörü (akış yönü hesabı için).</summary>
    internal Complex SlotEmfAc(int slot)
    {
        foreach (Branch b in branches)
        {
            if (b.AcSlot == slot)
            {
                return -b.Eac;
            }
        }
        return Complex.One;
    }

    internal double SlotEmfDc(int slot)
    {
        foreach (Branch b in branches)
        {
            if (b.DcSlot == slot)
            {
                return -b.Edc;
            }
        }
        return 1;
    }
}

/// <summary>Kablodaki akım ve animasyon için akış yönü.</summary>
public readonly record struct WireFlow(Complex Ac, double Dc, int Direction, double Amps, string? Source);

/// <summary>Klemens akımlarından kablo akımlarını çıkarır (her iletkenin kablo ağacı üzerinde).</summary>
internal static class WireCurrents
{
    public static Dictionary<string, WireFlow> Compute(Netlist netlist, Solution sol, IReadOnlyList<SimComponent> comps)
    {
        int acSlots = sol.AcSlotNames.Length, dcSlots = sol.DcSlotNames.Length;
        int slotCount = acSlots + dcSlots;

        // Klemens başına, elemana giren akım (toplam ve kaynak başına)
        var inject = new Dictionary<NetPoint, (Complex Ac, double Dc, Complex[] AcS, double[] DcS)>();
        for (int i = 0; i < sol.BranchCount; i++)
        {
            Branch b = sol.BranchAt(i);
            if (b.Owner == null || (b.A.Term < 0 && b.B.Term < 0))
            {
                continue;
            }
            Complex iac = sol.CurrentAc(i);
            double idc = sol.CurrentDc(i);
            Complex[] acs = new Complex[acSlots];
            double[] dcs = new double[dcSlots];
            for (int s = 0; s < acSlots; s++) acs[s] = sol.CurrentAcSlot(i, s);
            for (int s = 0; s < dcSlots; s++) dcs[s] = sol.CurrentDcSlot(i, s);
            if (b.A.Term >= 0)
            {
                Accumulate(inject, b.Owner.TerminalPoint(b.A.Term), iac, idc, acs, dcs, 1);
            }
            if (b.B.Term >= 0)
            {
                Accumulate(inject, b.Owner.TerminalPoint(b.B.Term), iac, idc, acs, dcs, -1);
            }
        }

        var result = new Dictionary<string, WireFlow>(StringComparer.Ordinal);
        foreach (Net net in netlist.Nets)
        {
            if (net.Wires.Count == 0)
            {
                continue;
            }
            // Kablo grafı: komşuluk listesi
            var adj = new Dictionary<NetPoint, List<(Wire W, bool FromA)>>();
            foreach (Wire w in net.Wires)
            {
                NetPoint a = NetPoint.Of(w, true), b = NetPoint.Of(w, false);
                Adj(adj, a).Add((w, true));
                Adj(adj, b).Add((w, false));
            }
            var visited = new HashSet<NetPoint>();
            foreach (NetPoint start in adj.Keys)
            {
                if (visited.Contains(start))
                {
                    continue;
                }
                // Yinelemeli DFS ile kapsayan ağaç; alt ağaç toplamları kablo akımlarını verir
                var order = new List<(NetPoint Node, Wire? Via, bool ViaFromA)>();
                var stack = new Stack<(NetPoint, Wire?, bool)>();
                stack.Push((start, null, false));
                while (stack.Count > 0)
                {
                    var (node, via, fromA) = stack.Pop();
                    if (!visited.Add(node))
                    {
                        continue;
                    }
                    order.Add((node, via, fromA));
                    foreach (var (w, isA) in adj[node])
                    {
                        NetPoint other = NetPoint.Of(w, !isA);
                        if (!visited.Contains(other))
                        {
                            // w kablosu node'dan other'a ağaç kenarı olacak; node tarafı isA
                            stack.Push((other, w, isA));
                        }
                    }
                }
                var subAc = new Dictionary<NetPoint, Complex>();
                var subDc = new Dictionary<NetPoint, double>();
                var subS = new Dictionary<NetPoint, double[]>();
                for (int k = order.Count - 1; k >= 0; k--)
                {
                    var (node, via, viaFromA) = order[k];
                    inject.TryGetValue(node, out var inj);
                    Complex sa = (subAc.GetValueOrDefault(node)) + inj.Ac;
                    double sd = subDc.GetValueOrDefault(node) + inj.Dc;
                    double[] ss = subS.GetValueOrDefault(node) ?? new double[slotCount * 2];
                    if (inj.AcS != null)
                    {
                        for (int s = 0; s < acSlots; s++)
                        {
                            ss[2 * s] += inj.AcS[s].Real;
                            ss[2 * s + 1] += inj.AcS[s].Imaginary;
                        }
                        for (int s = 0; s < dcSlots; s++)
                        {
                            ss[2 * (acSlots + s)] += inj.DcS[s];
                        }
                    }
                    if (via == null)
                    {
                        continue;
                    }
                    // Ebeveyn: via kablosunun diğer ucu. Kablo akımı ebeveynden bu düğüme doğru alt ağaç toplamı kadardır.
                    NetPoint parent = NetPoint.Of(via, viaFromA);
                    // viaFromA: ebeveyn A ucunda mı
                    int sign = viaFromA ? 1 : -1; // A→B yönünde akım
                    Complex wAc = sa * sign;
                    double wDc = sd * sign;
                    result[via.Id] = MakeFlow(sol, wAc, wDc, ss, sign, acSlots, dcSlots);
                    subAc[parent] = subAc.GetValueOrDefault(parent) + sa;
                    subDc[parent] = subDc.GetValueOrDefault(parent) + sd;
                    double[] ps = subS.GetValueOrDefault(parent) ?? new double[slotCount * 2];
                    for (int s = 0; s < ss.Length; s++)
                    {
                        ps[s] += ss[s];
                    }
                    subS[parent] = ps;
                }
            }
            foreach (Wire w in net.Wires)
            {
                result.TryAdd(w.Id, new WireFlow(Complex.Zero, 0, 0, 0, null));
            }
        }
        return result;
    }

    private static WireFlow MakeFlow(Solution sol, Complex ac, double dc, double[] slots, int sign, int acSlots, int dcSlots)
    {
        double amps = Math.Max(ac.Magnitude, Math.Abs(dc));
        if (amps < 1e-4)
        {
            return new WireFlow(ac, dc, 0, amps, null);
        }
        // Yön: en büyük katkıyı yapan kaynağın, o kaynağa göre etkin güç akışı yönü
        int best = -1;
        double bestMag = 0;
        for (int s = 0; s < acSlots + dcSlots; s++)
        {
            double mag = s < acSlots
                ? Math.Sqrt(slots[2 * s] * slots[2 * s] + slots[2 * s + 1] * slots[2 * s + 1])
                : Math.Abs(slots[2 * s]);
            if (mag > bestMag)
            {
                bestMag = mag;
                best = s;
            }
        }
        int dir = 0;
        string? source = null;
        if (best >= 0)
        {
            if (best < acSlots)
            {
                var i = new Complex(slots[2 * best], slots[2 * best + 1]) * sign;
                Complex e = sol.SlotEmfAc(best);
                double p = (i * Complex.Conjugate(e)).Real;
                dir = p > 0 ? 1 : p < 0 ? -1 : 0;
                source = sol.AcSlotNames[best];
            }
            else
            {
                int ds = best - acSlots;
                double i = slots[2 * best] * sign * Math.Sign(sol.SlotEmfDc(ds));
                dir = i > 0 ? 1 : i < 0 ? -1 : 0;
                source = sol.DcSlotNames[ds];
            }
        }
        return new WireFlow(ac, dc, dir, amps, source);
    }

    private static List<(Wire, bool)> Adj(Dictionary<NetPoint, List<(Wire, bool)>> adj, NetPoint p)
    {
        if (!adj.TryGetValue(p, out var list))
        {
            list = [];
            adj[p] = list;
        }
        return list;
    }

    private static void Accumulate(
        Dictionary<NetPoint, (Complex Ac, double Dc, Complex[] AcS, double[] DcS)> inject,
        NetPoint p, Complex iac, double idc, Complex[] acs, double[] dcs, int sign)
    {
        if (!inject.TryGetValue(p, out var cur))
        {
            cur = (Complex.Zero, 0, new Complex[acs.Length], new double[dcs.Length]);
        }
        cur.Ac += iac * sign;
        cur.Dc += idc * sign;
        for (int s = 0; s < acs.Length; s++) cur.AcS[s] += acs[s] * sign;
        for (int s = 0; s < dcs.Length; s++) cur.DcS[s] += dcs[s] * sign;
        inject[p] = cur;
    }
}
