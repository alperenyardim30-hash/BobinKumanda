using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Editing;

public enum HitKind { None, Terminal, Junction, Wire, Component, Text }

/// <summary>Fare altındaki şey. Kablo vuruşunda parça numarası ve parça üzerindeki en yakın nokta da verilir.</summary>
public readonly record struct Hit(HitKind Kind, string Id = "", string? Terminal = null, int Segment = -1, Vec Point = default)
{
    public static readonly Hit None = new(HitKind.None);
    public bool IsNone => Kind == HitKind.None;
}

/// <summary>Dünya koordinatında vuruş testleri. Toleranslar dünya birimindedir (ekrandaki piksel / zoom).</summary>
public static class HitTest
{
    public static Hit Terminal(Circuit circuit, string sheet, Vec p, double tolerance, IReadOnlySet<string>? ignore = null)
    {
        Hit best = Hit.None;
        double bestD = tolerance;
        foreach (ComponentInstance c in circuit.Components)
        {
            if (c.Sheet != sheet || ComponentCatalog.Find(c.Type) is not { } def || (ignore != null && ignore.Contains(c.Id)))
            {
                continue;
            }
            if (!def.Bounds(c).Inflate(tolerance).Contains(p))
            {
                continue;
            }
            foreach (TerminalDef t in def.Terminals)
            {
                Vec w = def.TerminalWorld(c, t);
                double d = w.DistanceTo(p);
                if (d <= bestD)
                {
                    bestD = d;
                    best = new Hit(HitKind.Terminal, c.Id, t.Id, Point: w);
                }
            }
        }
        return best;
    }

    public static Hit Junction(Circuit circuit, string sheet, Vec p, double tolerance)
    {
        Hit best = Hit.None;
        double bestD = tolerance;
        foreach (Junction j in circuit.Junctions)
        {
            double d = j.Position.DistanceTo(p);
            if (j.Sheet == sheet && d <= bestD)
            {
                bestD = d;
                best = new Hit(HitKind.Junction, j.Id, Point: j.Position);
            }
        }
        return best;
    }

    public static Hit Wire(Circuit circuit, string sheet, Vec p, double tolerance, IReadOnlySet<string>? ignore = null)
    {
        Hit best = Hit.None;
        double bestD = tolerance;
        foreach (Wire w in circuit.Wires)
        {
            if (w.Sheet != sheet || w.Points.Count < 2 || (ignore != null && ignore.Contains(w.Id)))
            {
                continue;
            }
            (int seg, double d, Vec closest) = WirePath.Nearest(w.Points, p);
            if (seg >= 0 && d <= bestD)
            {
                bestD = d;
                best = new Hit(HitKind.Wire, w.Id, Segment: seg, Point: closest);
            }
        }
        return best;
    }

    /// <summary>En üstteki (son çizilen) eleman.</summary>
    public static Hit Component(Circuit circuit, string sheet, Vec p, double slack)
    {
        for (int i = circuit.Components.Count - 1; i >= 0; i--)
        {
            ComponentInstance c = circuit.Components[i];
            if (c.Sheet == sheet && ComponentCatalog.Find(c.Type) is { } def && def.Bounds(c).Inflate(slack).Contains(p))
            {
                return new Hit(HitKind.Component, c.Id, Point: p);
            }
        }
        return Hit.None;
    }

    public static Hit Text(Circuit circuit, string sheet, Vec p, double slack)
    {
        for (int i = circuit.Texts.Count - 1; i >= 0; i--)
        {
            TextNote t = circuit.Texts[i];
            if (t.Sheet == sheet && TextBounds(t).Inflate(slack).Contains(p))
            {
                return new Hit(HitKind.Text, t.Id, Point: p);
            }
        }
        return Hit.None;
    }

    /// <summary>Metnin yaklaşık kutusu (8 birimlik yazı, satır başına 11 birim).</summary>
    public static RectD TextBounds(TextNote t)
    {
        string[] lines = t.Text.Replace("\r", "").Split('\n');
        int longest = Math.Max(1, lines.Max(l => l.Length));
        return new RectD(t.Position.X, t.Position.Y, Math.Max(12, longest * 8 * 0.56), Math.Max(11, lines.Length * 11));
    }

    /// <summary>Dikdörtgenin tamamen içine giren öğeler (lastik bant seçimi).</summary>
    public static Selection InRect(Circuit circuit, string sheet, RectD r)
    {
        var s = new Selection();
        foreach (ComponentInstance c in circuit.Components)
        {
            if (c.Sheet == sheet && ComponentCatalog.Find(c.Type) is { } def && r.Contains(def.Bounds(c)))
            {
                s.Components.Add(c.Id);
            }
        }
        foreach (Wire w in circuit.Wires)
        {
            if (w.Sheet == sheet && w.Points.Count > 0 && w.Points.All(r.Contains))
            {
                s.Wires.Add(w.Id);
            }
        }
        foreach (Junction j in circuit.Junctions)
        {
            if (j.Sheet == sheet && r.Contains(j.Position))
            {
                s.Junctions.Add(j.Id);
            }
        }
        foreach (TextNote t in circuit.Texts)
        {
            if (t.Sheet == sheet && r.Contains(TextBounds(t)))
            {
                s.Texts.Add(t.Id);
            }
        }
        return s;
    }

    /// <summary>Seçimin dünya koordinatındaki sınırı (etiketler hariç).</summary>
    public static RectD? Bounds(Circuit circuit, Selection s)
    {
        RectD? box = null;
        void Add(RectD r) => box = box is { } b ? b.Union(r) : r;
        foreach (ComponentInstance c in circuit.Components)
        {
            if (s.Components.Contains(c.Id) && ComponentCatalog.Find(c.Type) is { } def)
            {
                Add(def.Bounds(c));
            }
        }
        foreach (Wire w in circuit.Wires)
        {
            if (s.Wires.Contains(w.Id))
            {
                foreach (Vec p in w.Points)
                {
                    Add(new RectD(p.X, p.Y, 0, 0));
                }
            }
        }
        foreach (Junction j in circuit.Junctions)
        {
            if (s.Junctions.Contains(j.Id))
            {
                Add(new RectD(j.Position.X, j.Position.Y, 0, 0));
            }
        }
        foreach (TextNote t in circuit.Texts)
        {
            if (s.Texts.Contains(t.Id))
            {
                Add(TextBounds(t));
            }
        }
        return box;
    }

    /// <summary>Etiketin kabaca kapladığı alan (etiketi sürüklemek için).</summary>
    public static RectD? LabelBounds(ComponentDef def, ComponentInstance c)
    {
        if (!CircuitRenderer.ShowsLabel(def, c))
        {
            return null;
        }
        Vec l = def.LabelWorld(c);
        return new RectD(l.X, l.Y, Math.Max(6, c.Label.Length * CircuitRenderer.LabelSize * 0.62), CircuitRenderer.LabelSize * 1.2);
    }
}
