using System.Numerics;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Simulation;

public enum SimStatus { Running, Paused, Faulted }

public enum ProblemSeverity { Info, Warning, Error }

/// <summary>Kullanıcıya gösterilen sorun: ne oldu, neden, nasıl düzeltilir.</summary>
public sealed record SimProblem(
    string Code,
    ProblemSeverity Severity,
    string Message,
    string Explanation,
    IReadOnlyList<string> ComponentIds,
    IReadOnlyList<string> WireIds);

public sealed record SimLogEntry(double Time, string Text, string? ComponentId, ProblemSeverity Severity);

public sealed class EventHandle
{
    internal bool Cancelled;
    public double Time { get; internal set; }
    public bool Pending { get; internal set; } = true;
}

/// <summary>
/// Devre simülatörü. Her durum değişikliğinden sonra devre fazörlerle çözülür (MNA), elemanlar sonuca göre
/// durumlarını günceller; zaman rölesi, motor hızlanması gibi süreçler olay kuyruğu ve zaman adımlarıyla ilerler.
/// </summary>
public sealed class Simulator
{
    public const double ShortCircuitAmps = 300;

    private readonly PriorityQueue<(EventHandle Handle, Action Action, string? Description), (double, long)> queue = new();
    private long eventSeq;
    private readonly List<SimLogEntry> log = [];
    private readonly List<SimProblem> latched = [];
    private List<SimProblem> live = [];
    private readonly Dictionary<string, SimComponent> byId = new(StringComparer.Ordinal);
    private int settleDepth;

    public Circuit Circuit { get; }
    public Netlist Netlist { get; }
    public IReadOnlyList<SimComponent> Components { get; }
    public DeviceRegistry Devices { get; } = new();
    public double Time { get; private set; }
    public SimStatus Status { get; private set; } = SimStatus.Running;
    public Solution? Solution { get; private set; }
    public IReadOnlyDictionary<string, WireFlow> WireFlows { get; private set; } = new Dictionary<string, WireFlow>();
    public IReadOnlyList<SimLogEntry> Log => log;
    public IEnumerable<SimProblem> Problems => latched.Concat(live);

    /// <summary>Kontaktör/röle kontaklarının mekanik gecikmesi (saniye).</summary>
    public double ContactDelay { get; set; } = 0.012;

    /// <summary>Sürekli süreçler için en büyük zaman adımı (saniye).</summary>
    public double TickDt { get; set; } = 0.02;

    /// <summary>Sesli elemanlar (korna, buzzer) için; uygulama sesi buna göre çalar.</summary>
    public bool AnySoundActive => Components.OfType<ISoundSource>().Any(s => s.SoundActive);

    /// <summary>Her yerleşim sonrası (durum değişince) tetiklenir.</summary>
    public event Action? Changed;

    /// <param name="powered">false: şebeke ve kaynaklar bağlı değilmiş gibi kurulur (ohmmetreyle enerjisiz ölçüm).</param>
    public Simulator(Circuit circuit, bool powered = true)
    {
        Circuit = circuit;
        Powered = powered;
        stamper.Powered = powered;
        Netlist = Netlist.Build(circuit);
        var comps = new List<SimComponent>();
        int internalBase = Netlist.NodeCount;
        foreach (ComponentInstance inst in circuit.Components)
        {
            ComponentDef? def = ComponentCatalog.Find(inst.Type);
            SimComponent? sim = def?.CreateSim(inst);
            if (def == null || sim == null)
            {
                continue;
            }
            sim.Instance = inst;
            sim.Def = def;
            sim.Sim = this;
            sim.TerminalNodes = def.Terminals.Select(t => Netlist.NodeOf(inst.Id, t.Id)).ToArray();
            sim.InternalBase = internalBase;
            internalBase += sim.InternalNodeCount;
            comps.Add(sim);
            byId[inst.Id] = sim;
        }
        NodeCount = internalBase;
        Components = comps;

        foreach (SimComponent c in comps)
        {
            if (c is IDeviceMaster m)
            {
                Devices.Register(m);
            }
        }
        foreach (var (label, list) in Devices.Duplicates)
        {
            ReportLatched(new SimProblem("duplicate-label", ProblemSeverity.Warning,
                Loc.T("problem.duplicateLabel", label),
                Loc.T("explain.duplicateLabel"),
                list.Select(m => m.Component.Id).ToList(), []));
        }
        foreach (SimComponent c in comps)
        {
            c.Bind(Devices);
        }
        AddLog(Loc.T("log.started"), null);
        Settle();
    }

    /// <summary>Terminal düğümleri + elemanların iç düğümleri.</summary>
    public int NodeCount { get; }

    /// <summary>false: devre enerjisiz (yalnızca direnç ölçümü için kurulmuş).</summary>
    public bool Powered { get; }

    public SimComponent? Find(string componentId) => byId.GetValueOrDefault(componentId);

    // ---- Zaman ve olaylar ----

    public EventHandle Schedule(double delay, Action action, string? description = null)
    {
        var h = new EventHandle { Time = Time + Math.Max(0, delay) };
        queue.Enqueue((h, action, description), (h.Time, eventSeq++));
        return h;
    }

    public static void Cancel(EventHandle? handle)
    {
        if (handle != null)
        {
            handle.Cancelled = true;
            handle.Pending = false;
        }
    }

    public double? NextEventTime
    {
        get
        {
            DropCancelled();
            return queue.TryPeek(out var e, out _) ? e.Handle.Time : null;
        }
    }

    private void DropCancelled()
    {
        while (queue.TryPeek(out var e, out _) && e.Handle.Cancelled)
        {
            queue.Dequeue();
        }
    }

    private bool AnyTicking()
    {
        foreach (SimComponent c in Components)
        {
            if (c.NeedsTicks)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Simülasyon saatini hedefe kadar ilerletir.</summary>
    public void AdvanceTo(double target)
    {
        int guard = 0;
        while (Status == SimStatus.Running && Time < target - 1e-9 && guard++ < 20000)
        {
            DropCancelled();
            double next = target;
            if (queue.TryPeek(out var e, out _))
            {
                next = Math.Min(next, e.Handle.Time);
            }
            bool ticking = AnyTicking();
            if (ticking)
            {
                next = Math.Min(next, Time + TickDt);
            }
            AdvanceContinuous(next - Time, ticking, out bool resolve);
            Time = next;
            bool any = RunDueEvents(int.MaxValue);
            if (any || resolve)
            {
                Settle();
            }
        }
    }

    private void AdvanceContinuous(double dt, bool ticking, out bool resolve)
    {
        resolve = false;
        if (!ticking || dt <= 0 || Solution == null)
        {
            return;
        }
        foreach (SimComponent c in Components)
        {
            if (!c.NeedsTicks)
            {
                continue;
            }
            if (c.Advance(dt, Solution))
            {
                resolve = true;
            }
            else
            {
                // Yeniden çözüm gerekmese de görünüm (fan hızı, kalan süre) güncel kalsın
                c.UpdateVisual(Solution);
            }
        }
    }

    private bool RunDueEvents(int max)
    {
        bool any = false;
        int count = 0;
        while (count < max && queue.TryPeek(out var e, out _) && e.Handle.Time <= Time + 1e-9)
        {
            queue.Dequeue();
            if (e.Handle.Cancelled)
            {
                continue;
            }
            e.Handle.Pending = false;
            e.Action();
            any = true;
            count++;
        }
        return any;
    }

    /// <summary>Adım adım mod: sıradaki tek olaya kadar ilerler. Olay yoksa false.</summary>
    public bool StepNextEvent()
    {
        if (Status == SimStatus.Faulted)
        {
            return false;
        }
        DropCancelled();
        if (!queue.TryPeek(out var e, out _))
        {
            return false;
        }
        double target = e.Handle.Time;
        // Aradaki sürekli süreçleri de işlet
        while (Time < target - 1e-9)
        {
            bool ticking = AnyTicking();
            double next = ticking ? Math.Min(target, Time + TickDt) : target;
            AdvanceContinuous(next - Time, ticking, out bool resolve);
            Time = next;
            if (resolve && Time < target - 1e-9)
            {
                Settle();
            }
        }
        Time = target;
        RunDueEvents(1);
        Settle();
        return true;
    }

    public void Pause() => Status = Status == SimStatus.Faulted ? Status : SimStatus.Paused;

    public void Resume()
    {
        if (Status == SimStatus.Paused)
        {
            Status = SimStatus.Running;
        }
    }

    // ---- Etkileşim ----

    public void Interact(string componentId, Interaction action)
    {
        if (Status == SimStatus.Faulted || !byId.TryGetValue(componentId, out SimComponent? c))
        {
            return;
        }
        c.Interact(action);
        Settle();
    }

    // ---- Çözüm ----

    private readonly Stamper stamper = new();

    private Solution Solve()
    {
        stamper.Reset();
        foreach (SimComponent c in Components)
        {
            stamper.CurrentOwner = c;
            c.Stamp(stamper);
        }
        stamper.CurrentOwner = null;
        return new Solution(NodeCount, stamper);
    }

    /// <summary>Devreyi çözer ve elemanlar kararlı hale gelene kadar tekrarlar.</summary>
    public void Settle()
    {
        if (settleDepth > 0)
        {
            return;
        }
        settleDepth++;
        try
        {
            const int maxIterations = 60;
            int iteration = 0;
            Solution sol;
            while (true)
            {
                sol = Solve();
                bool changed = false;
                foreach (SimComponent c in Components)
                {
                    changed |= c.Evaluate(sol);
                }
                if (!changed)
                {
                    break;
                }
                if (++iteration >= maxIterations)
                {
                    ReportLatched(new SimProblem("oscillation", ProblemSeverity.Warning,
                        Loc.T("problem.oscillation"), Loc.T("explain.oscillation"), [], []));
                    Status = SimStatus.Paused;
                    break;
                }
            }
            Solution = sol;
            live = [];
            foreach (SimComponent c in Components)
            {
                c.Check(sol);
            }
            WireFlows = WireCurrents.Compute(Netlist, sol, Components);
            CheckShortCircuit(sol);
            foreach (SimComponent c in Components)
            {
                c.UpdateVisual(sol);
            }
        }
        finally
        {
            settleDepth--;
        }
        Changed?.Invoke();
    }

    private void CheckShortCircuit(Solution sol)
    {
        var large = sol.LargeCurrents(ShortCircuitAmps).ToList();
        if (large.Count == 0)
        {
            return;
        }
        double amps = large.Max(l => l.Amps);
        var comps = large.Select(l => sol.BranchOwner(l.Branch)?.Id).OfType<string>().Distinct().ToList();
        var wires = WireFlows
            .Where(kv => kv.Value.Amps > ShortCircuitAmps)
            .Select(kv => kv.Key)
            .ToList();
        ReportFault("short-circuit", Loc.T("problem.shortCircuit", FormatAmps(amps)),
            Loc.T("explain.shortCircuit"), comps, wires);
    }

    // ---- Sorunlar ve günlük ----

    public void ReportLive(SimProblem p) => live.Add(p);

    public void ReportLatched(SimProblem p)
    {
        if (!latched.Any(x => x.Code == p.Code && x.ComponentIds.SequenceEqual(p.ComponentIds)))
        {
            latched.Add(p);
            AddLog(p.Message, p.ComponentIds.FirstOrDefault(), p.Severity);
        }
    }

    /// <summary>Simülasyonu durduran hata (korumasız kısa devre, yanan bobin/motor).</summary>
    public void ReportFault(string code, string message, string explanation, IReadOnlyList<string> comps, IReadOnlyList<string> wires)
    {
        ReportLatched(new SimProblem(code, ProblemSeverity.Error, message, explanation, comps, wires));
        Status = SimStatus.Faulted;
    }

    public void AddLog(string text, string? componentId, ProblemSeverity severity = ProblemSeverity.Info)
    {
        log.Add(new SimLogEntry(Time, text, componentId, severity));
        if (log.Count > 2000)
        {
            log.RemoveRange(0, 500);
        }
    }

    // ---- Ölçüm ----

    /// <summary>Kablo veya klemens noktasının düğümü.</summary>
    public int NodeOf(NetPoint p) => Netlist.NodeOf(p);

    public (Complex Ac, double Dc) MeasureVoltage(int nodeA, int nodeB)
    {
        if (Solution == null || nodeA < 0 || nodeB < 0)
        {
            return (Complex.Zero, 0);
        }
        return (Solution.VoltageAc(nodeA, nodeB), Solution.VoltageDc(nodeA, nodeB));
    }

    /// <summary>
    /// Ohmmetre: kaynaklar devreden çıkarılmış gibi, iki nokta arasına 1 mA DC uygulanarak direnç bulunur.
    /// Sonsuz (açık devre) için null.
    /// </summary>
    public double? MeasureResistance(int nodeA, int nodeB)
    {
        if (nodeA < 0 || nodeB < 0)
        {
            return null;
        }
        if (nodeA == nodeB)
        {
            return 0;
        }
        var st = new Stamper { Powered = false };
        foreach (SimComponent c in Components)
        {
            st.CurrentOwner = c;
            c.Stamp(st);
        }
        int n = NodeCount;
        var m = new double[n * n];
        for (int i = 0; i < n; i++)
        {
            m[i * n + i] += 1e-12;
        }
        foreach (Branch b in st.Branches)
        {
            if (b.IsSource || b.Gdc == 0)
            {
                continue;
            }
            int a = b.A.Node, bb = b.B.Node;
            if (a >= 0) m[a * n + a] += b.Gdc;
            if (bb >= 0) m[bb * n + bb] += b.Gdc;
            if (a >= 0 && bb >= 0)
            {
                m[a * n + bb] -= b.Gdc;
                m[bb * n + a] -= b.Gdc;
            }
        }
        var rhs = new double[n];
        rhs[nodeA] = 1e-3;
        rhs[nodeB] = -1e-3;
        double[] v = new RealLu(n, m).Solve(rhs);
        double r = (v[nodeA] - v[nodeB]) / 1e-3;
        return r > 1e8 ? null : Math.Max(0, r);
    }

    public static string FormatAmps(double a) =>
        a >= 1000 ? string.Format(Loc.Culture, "{0:0.0} kA", a / 1000)
        : a >= 1 ? string.Format(Loc.Culture, "{0:0.00} A", a)
        : string.Format(Loc.Culture, "{0:0} mA", a * 1000);

    public static string FormatVolts(double v) =>
        string.Format(Loc.Culture, Math.Abs(v) >= 100 ? "{0:0} V" : "{0:0.0} V", v);
}

/// <summary>Ses çıkaran elemanlar (korna, buzzer, siren).</summary>
public interface ISoundSource
{
    bool SoundActive { get; }
    string SoundKind { get; }
}
