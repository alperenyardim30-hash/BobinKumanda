using System.Globalization;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Components;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.IO;

public sealed class EscImportResult
{
    public required Circuit Circuit { get; init; }

    /// <summary>Kullanıcıya gösterilecek uyarılar (bilinmeyen eleman, yerine oturmayan kablo...).</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>Ucu, bağlı olduğu klemensin hesaplanan yerine denk gelmeyen kablo sayısı (eski dosyayla uyum ölçüsü).</summary>
    public int MisplacedWireEnds { get; set; }
}

/// <summary>
/// Eski .esc içeriğini BobinKumanda devresine çevirir. Elemanların konumu ve döndürmesi aynen alınır (yerleşim kuralları
/// eskisiyle aynıdır). Uç uca eklenmiş çizgi parçaları tek bir kabloya, eski ek noktaları ek noktasına dönüşür.
/// </summary>
public static class EscImporter
{
    public static EscImportResult Read(string path) =>
        Import(EscReader.Read(path), Path.GetFileNameWithoutExtension(path));

    public static EscImportResult Import(EscDocument doc, string? circuitName = null)
    {
        var circuit = new Circuit();
        circuit.Title.CircuitName = circuitName ?? "";
        var result = new EscImportResult { Circuit = circuit };
        var cps = doc.ConnPoints;

        // Her bağlantı noktasının sahibi: eleman klemensi, çizgi ucu ya da ek noktası
        var owner = new Owner[cps.Count];
        bool badIndex = false;
        void Claim(int cp, Owner o)
        {
            if (cp < 0 || cp >= cps.Count || owner[cp].Kind != OwnerKind.None)
            {
                badIndex = true;
                return;
            }
            owner[cp] = o;
        }

        var components = new ComponentInstance?[doc.Elements.Count];
        var unknownTypes = new SortedSet<int>();
        for (int i = 0; i < doc.Elements.Count; i++)
        {
            EscElement e = doc.Elements[i];
            ComponentDef? def = ComponentCatalog.FindLegacy(e.Type);
            if (def == null)
            {
                unknownTypes.Add(e.Type);
                continue;
            }
            var c = new ComponentInstance
            {
                Id = circuit.NewId("c"),
                Type = def.TypeId,
                Label = LegacyLabel(e.Label1) ?? def.DefaultLabel ?? "",
                Position = e.Location.ToVec(),
                Rotation = e.Rotation & 3
            };
            if (LegacyLabel(e.Label1) != null)
            {
                c.LabelOffset = e.Label1Location.ToVec() - c.Position;
            }
            def.ApplyDefaults(c);
            ApplyLegacyParams(def, c, e);
            components[i] = c;
            circuit.Components.Add(c);

            if (e.ConnPoints.Count != def.LegacyTerminalCount)
            {
                result.Warnings.Add(Loc.T("esc.terminalCount", c.Label.Length > 0 ? c.Label : def.Name, e.ConnPoints.Count, def.Terminals.Count));
            }
            for (int k = 0; k < e.ConnPoints.Count; k++)
            {
                Claim(e.ConnPoints[k], k < def.Terminals.Count ? new Owner(OwnerKind.Terminal, i, k) : new Owner(OwnerKind.Lost, i, k));
            }
        }
        foreach (int t in unknownTypes)
        {
            result.Warnings.Add(Loc.T("esc.unknownType", t));
        }

        // Kontaklar bağlı oldukları bobinin adını alır (eski programda bağ ad ile değil listeyle kuruluyordu)
        for (int i = 0; i < doc.Elements.Count; i++)
        {
            ComponentInstance? c = components[i];
            if (c == null || ComponentCatalog.Find(c.Type)?.Role != DeviceRole.Follower)
            {
                continue;
            }
            foreach (int m in doc.Elements[i].Links)
            {
                ComponentInstance? master = m >= 0 && m < components.Length ? components[m] : null;
                if (master != null && ComponentCatalog.Find(master.Type)?.Role != DeviceRole.Follower && master.Label.Length > 0)
                {
                    c.Label = master.Label;
                    break;
                }
            }
        }

        var junctions = new Junction[doc.Dots.Count];
        for (int i = 0; i < doc.Dots.Count; i++)
        {
            junctions[i] = new Junction { Id = circuit.NewId("j"), Position = doc.Dots[i].Origin.ToVec() };
            circuit.Junctions.Add(junctions[i]);
            for (int k = 0; k < doc.Dots[i].ConnPoints.Count; k++)
            {
                Claim(doc.Dots[i].ConnPoints[k], new Owner(OwnerKind.Dot, i, k));
            }
        }
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            Claim(doc.Lines[i].ConnA, new Owner(OwnerKind.Line, i, 0));
            Claim(doc.Lines[i].ConnB, new Owner(OwnerKind.Line, i, 1));
        }
        if (badIndex)
        {
            result.Warnings.Add(Loc.T("esc.badLinks"));
        }

        int Other(int cp) => cp >= 0 && cp < cps.Count && cps[cp].ConnectedTo >= 0 && cps[cp].ConnectedTo < cps.Count
            ? cps[cp].ConnectedTo : -1;
        int LineCp(int line, int slot) => slot == 0 ? doc.Lines[line].ConnA : doc.Lines[line].ConnB;
        Vec LinePt(int line, int slot) => (slot == 0 ? doc.Lines[line].A : doc.Lines[line].B).ToVec();

        WireEnd EndFor(int otherCp)
        {
            if (otherCp < 0)
            {
                return WireEnd.Free();
            }
            Owner o = owner[otherCp];
            if (o.Kind == OwnerKind.Terminal && components[o.Index] is { } c)
            {
                return WireEnd.ToTerminal(c.Id, ComponentCatalog.Get(c.Type).Terminals[o.Slot].Id);
            }
            if (o.Kind == OwnerKind.Dot)
            {
                return WireEnd.ToJunction(junctions[o.Index].Id);
            }
            return WireEnd.Free();
        }

        // Uç uca eklenmiş çizgileri izleyerek kabloları kur. Başlangıç: çizgi olmayan bir şeye bağlı (ya da boşta) uçlar.
        var visited = new bool[doc.Lines.Count];
        void Trace(int line, int slot)
        {
            var points = new List<Vec> { LinePt(line, slot) };
            WireEnd a = EndFor(Other(LineCp(line, slot)));
            WireEnd b = WireEnd.Free();
            while (true)
            {
                visited[line] = true;
                int far = 1 - slot;
                points.Add(LinePt(line, far));
                int next = Other(LineCp(line, far));
                if (next >= 0 && owner[next].Kind == OwnerKind.Line && !visited[owner[next].Index])
                {
                    line = owner[next].Index;
                    slot = owner[next].Slot;
                    points.Add(LinePt(line, slot));
                    continue;
                }
                if (next < 0 || owner[next].Kind != OwnerKind.Line)
                {
                    b = EndFor(next);
                }
                break;
            }
            AddWire(circuit, result, a, b, points);
        }
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            for (int slot = 0; slot < 2 && !visited[i]; slot++)
            {
                int other = Other(LineCp(i, slot));
                if (other < 0 || owner[other].Kind != OwnerKind.Line)
                {
                    Trace(i, slot);
                }
            }
        }
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            // Yalnızca çizgilerden oluşan kapalı halka: hiçbir şeye bağlı değil, yine de çizimde kalsın
            if (!visited[i])
            {
                Trace(i, 0);
            }
        }

        // Çizgisiz doğrudan bağlantılar (klemens-klemens, klemens-ek noktası)
        for (int i = 0; i < cps.Count; i++)
        {
            int j = Other(i);
            if (j <= i || owner[i].Kind is OwnerKind.Line or OwnerKind.None or OwnerKind.Lost ||
                owner[j].Kind is OwnerKind.Line or OwnerKind.None or OwnerKind.Lost)
            {
                continue;
            }
            WireEnd a = EndFor(i), b = EndFor(j);
            AddWire(circuit, result, a, b, [EndPosition(circuit, a) ?? Vec.Zero, EndPosition(circuit, b) ?? Vec.Zero]);
        }

        foreach (EscText t in doc.Texts)
        {
            circuit.Texts.Add(new TextNote
            {
                Id = circuit.NewId("t"),
                Position = t.Location.ToVec(),
                Size = t.Size.ToVec(),
                Text = t.Text
            });
        }

        if (result.MisplacedWireEnds > 0)
        {
            result.Warnings.Add(Loc.T("esc.misplaced", result.MisplacedWireEnds));
        }
        circuit.RecalculateNextId();
        return result;
    }

    /// <summary>Eski programda "..." adsız eleman demekti.</summary>
    private static string? LegacyLabel(string text)
    {
        string t = text.Trim();
        return t.Length == 0 || t == "..." ? null : t;
    }

    private static void ApplyLegacyParams(ComponentDef def, ComponentInstance c, EscElement e)
    {
        if (def is TimerOnDef or TimerOffDef &&
            int.TryParse(e.Label2.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) && seconds > 0)
        {
            c.SetParam("time", seconds);
        }
        if (def is LampDef && e.ColorName is { } color)
        {
            string? lamp = color switch
            {
                "Red" => "red",
                "Blue" => "blue",
                "Green" => "green",
                "Magenta" => "magenta",
                "Yellow" => "yellow",
                "White" => "white",
                "Orange" => "orange",
                _ => null
            };
            if (lamp != null)
            {
                c.SetParam("lampColor", lamp);
            }
        }
    }

    internal static Vec? EndPosition(Circuit circuit, WireEnd end)
    {
        if (end.IsTerminal && circuit.FindComponent(end.Component) is { } c && ComponentCatalog.Find(c.Type) is { } def &&
            def.FindTerminal(end.Terminal!) is { } t)
        {
            return def.TerminalWorld(c, t);
        }
        if (end.IsJunction && circuit.FindJunction(end.Junction) is { } j)
        {
            return j.Position;
        }
        return null;
    }

    private static void AddWire(Circuit circuit, EscImportResult result, WireEnd a, WireEnd b, List<Vec> points)
    {
        // Uç, klemensin hesaplanan yerine oturmuyorsa kabloyu o noktaya kadar uzat; bağlantı bozulmaz
        if (EndPosition(circuit, a) is { } pa && !pa.NearlyEquals(points[0], 0.5))
        {
            result.MisplacedWireEnds++;
            points.Insert(0, pa);
        }
        if (EndPosition(circuit, b) is { } pb && !pb.NearlyEquals(points[^1], 0.5))
        {
            result.MisplacedWireEnds++;
            points.Add(pb);
        }
        circuit.Wires.Add(new Wire { Id = circuit.NewId("w"), A = a, B = b, Points = Simplify(points) });
    }

    /// <summary>Tekrarlanan ve aynı doğru üzerindeki ara noktaları atar.</summary>
    internal static List<Vec> Simplify(List<Vec> points)
    {
        var list = new List<Vec>(points.Count);
        foreach (Vec p in points)
        {
            if (list.Count > 0 && list[^1].NearlyEquals(p))
            {
                continue;
            }
            if (list.Count >= 2)
            {
                Vec a = list[^2], m = list[^1];
                bool collinear = (Math.Abs(a.X - m.X) < 1e-6 && Math.Abs(m.X - p.X) < 1e-6) ||
                                 (Math.Abs(a.Y - m.Y) < 1e-6 && Math.Abs(m.Y - p.Y) < 1e-6);
                if (collinear)
                {
                    list[^1] = p;
                    continue;
                }
            }
            list.Add(p);
        }
        if (list.Count == 1)
        {
            list.Add(list[0]);
        }
        return list;
    }

    private enum OwnerKind { None, Terminal, Line, Dot, Lost }

    private readonly record struct Owner(OwnerKind Kind, int Index, int Slot);
}
