using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Editing;

/// <summary>Seçili elemanlar, kablolar, ek noktaları ve metinler (kimlikleriyle).</summary>
public sealed class Selection
{
    public HashSet<string> Components { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Wires { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Junctions { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Texts { get; } = new(StringComparer.Ordinal);

    public int Count => Components.Count + Wires.Count + Junctions.Count + Texts.Count;
    public bool IsEmpty => Count == 0;

    public bool Contains(string id) =>
        Components.Contains(id) || Wires.Contains(id) || Junctions.Contains(id) || Texts.Contains(id);

    public void Clear()
    {
        Components.Clear();
        Wires.Clear();
        Junctions.Clear();
        Texts.Clear();
    }

    public Selection Clone()
    {
        var s = new Selection();
        s.Components.UnionWith(Components);
        s.Wires.UnionWith(Wires);
        s.Junctions.UnionWith(Junctions);
        s.Texts.UnionWith(Texts);
        return s;
    }

    public void UnionWith(Selection other)
    {
        Components.UnionWith(other.Components);
        Wires.UnionWith(other.Wires);
        Junctions.UnionWith(other.Junctions);
        Texts.UnionWith(other.Texts);
    }

    /// <summary>Devrede artık olmayan kimlikleri atar (geri almadan sonra).</summary>
    public void Prune(Circuit c)
    {
        Components.RemoveWhere(id => c.FindComponent(id) == null);
        Wires.RemoveWhere(id => c.FindWire(id) == null);
        Junctions.RemoveWhere(id => c.FindJunction(id) == null);
        Texts.RemoveWhere(id => c.Texts.Find(t => t.Id == id) == null);
    }

    /// <summary>Bir öğeyi ekler ya da çıkarır (Ctrl ile tıklama).</summary>
    public void Toggle(HashSet<string> set, string id)
    {
        if (!set.Remove(id))
        {
            set.Add(id);
        }
    }

    public static Selection Of(ComponentInstance c)
    {
        var s = new Selection();
        s.Components.Add(c.Id);
        return s;
    }

    public bool SetEquals(Selection other) =>
        Components.SetEquals(other.Components) && Wires.SetEquals(other.Wires) &&
        Junctions.SetEquals(other.Junctions) && Texts.SetEquals(other.Texts);
}
