using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Editing;

/// <summary>
/// Seçimi sürükleme. Başlangıçtaki durum saklanır ve her fare hareketinde toplam kaydırma baştan uygulanır; böylece
/// kablolara eklenen kırılmalar sürükleme boyunca birikmez. İki ucu da taşınan kablolar olduğu gibi kayar, tek ucu
/// taşınanlarda yalnızca o uca yakın köşe kaydırılarak dik açı korunur.
/// </summary>
public sealed class MoveSession
{
    private readonly Circuit circuit;
    private readonly Selection selection;
    private readonly Dictionary<string, Vec> components = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vec> junctions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vec> texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Vec>> wires = new(StringComparer.Ordinal);
    private readonly HashSet<string> rigid = new(StringComparer.Ordinal);

    public MoveSession(Circuit circuit, Selection selection)
    {
        this.circuit = circuit;
        this.selection = selection.Clone();
        foreach (ComponentInstance c in circuit.Components)
        {
            if (selection.Components.Contains(c.Id))
            {
                components[c.Id] = c.Position;
            }
        }
        foreach (Junction j in circuit.Junctions)
        {
            if (selection.Junctions.Contains(j.Id))
            {
                junctions[j.Id] = j.Position;
            }
        }
        foreach (TextNote t in circuit.Texts)
        {
            if (selection.Texts.Contains(t.Id))
            {
                texts[t.Id] = t.Position;
            }
        }
        foreach (Wire w in circuit.Wires)
        {
            bool aIn = Moves(w.A), bIn = Moves(w.B);
            bool selected = selection.Wires.Contains(w.Id);
            if (selected || aIn || bIn)
            {
                wires[w.Id] = [.. w.Points];
                // Seçili kablo, ucu sabit bir klemense bağlı olsa bile kopmasın diye yalnızca iki ucu da hareket ediyorsa kayar
                if ((aIn || (selected && !Fixed(w.A))) && (bIn || (selected && !Fixed(w.B))))
                {
                    rigid.Add(w.Id);
                }
            }
        }
    }

    public Vec Delta { get; private set; }

    private bool Moves(WireEnd e) =>
        (e.IsTerminal && selection.Components.Contains(e.Component!)) || (e.IsJunction && selection.Junctions.Contains(e.Junction!));

    private bool Fixed(WireEnd e) => !e.IsFree && !Moves(e);

    public void Update(Vec delta)
    {
        Delta = delta;
        foreach (ComponentInstance c in circuit.Components)
        {
            if (components.TryGetValue(c.Id, out Vec p))
            {
                c.Position = p + delta;
            }
        }
        foreach (Junction j in circuit.Junctions)
        {
            if (junctions.TryGetValue(j.Id, out Vec p))
            {
                j.Position = p + delta;
            }
        }
        foreach (TextNote t in circuit.Texts)
        {
            if (texts.TryGetValue(t.Id, out Vec p))
            {
                t.Position = p + delta;
            }
        }
        foreach (Wire w in circuit.Wires)
        {
            if (!wires.TryGetValue(w.Id, out List<Vec>? orig) || orig.Count < 2)
            {
                continue;
            }
            if (rigid.Contains(w.Id))
            {
                w.Points = WirePath.Translate(orig, delta);
                continue;
            }
            List<Vec> pts = orig;
            if (Moves(w.A) || (selection.Wires.Contains(w.Id) && w.A.IsFree))
            {
                pts = WirePath.MoveEnd(pts, true, w.A.IsFree ? orig[0] + delta : CircuitEditor.EndPosition(circuit, w, true));
            }
            if (Moves(w.B) || (selection.Wires.Contains(w.Id) && w.B.IsFree))
            {
                pts = WirePath.MoveEnd(pts, false, w.B.IsFree ? orig[^1] + delta : CircuitEditor.EndPosition(circuit, w, false));
            }
            w.Points = pts;
        }
    }

    /// <summary>Sürükleme bitti: bırakılan elemanların klemenslerine denk gelen boş kablo uçları bağlanır.</summary>
    public void Commit()
    {
        foreach (ComponentInstance c in circuit.Components)
        {
            if (components.ContainsKey(c.Id))
            {
                CircuitEditor.ConnectFreeEnds(circuit, c);
            }
        }
        foreach (Wire w in circuit.Wires)
        {
            if (wires.ContainsKey(w.Id))
            {
                w.Points = WirePath.Simplify(w.Points);
            }
        }
    }

    public void Cancel()
    {
        Update(Vec.Zero);
        foreach (Wire w in circuit.Wires)
        {
            if (wires.TryGetValue(w.Id, out List<Vec>? orig))
            {
                w.Points = [.. orig];
            }
        }
    }

    public bool HasComponents => components.Count > 0;

    /// <summary>Sürüklenen ilk elemanın klemensi ızgaraya otursun diye kaydırmayı düzelten miktar.</summary>
    public static Vec SnapDelta(Circuit circuit, Selection s, Vec rawDelta)
    {
        if (circuit.Components.FirstOrDefault(c => s.Components.Contains(c.Id)) is { } first &&
            ComponentCatalog.Find(first.Type) is not null)
        {
            Vec target = CircuitEditor.Snap(first.Position + rawDelta);
            return target - first.Position;
        }
        if (circuit.Junctions.FirstOrDefault(j => s.Junctions.Contains(j.Id)) is { } fj)
        {
            return CircuitEditor.Snap(fj.Position + rawDelta) - fj.Position;
        }
        return CircuitEditor.Snap(rawDelta);
    }
}
