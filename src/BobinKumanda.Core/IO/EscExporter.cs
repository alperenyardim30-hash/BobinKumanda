using System.Globalization;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Components;
using BobinKumanda.Core.Editing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.IO;

public sealed class EscExportResult
{
    public required EscDocument Document { get; init; }

    /// <summary>Eski biçime sığmayan şeyler (yeni elemanlar, ikinci sayfa, aynalama...).</summary>
    public List<string> Warnings { get; } = [];
}

/// <summary>
/// Devreyi eski EKTS biçimine çevirir (içe aktarmanın tersi). Eski programda her bağlantı noktası tek bir noktaya
/// bağlanabildiği için, birden çok kablonun bağlandığı klemens ve ek noktalarına ek noktası (dot) eklenir.
/// Eski programda olmayan elemanlar kaydedilmez ve uyarı listesinde bildirilir. Yalnızca bir sayfa kaydedilir.
/// </summary>
public static class EscExporter
{
    public static EscExportResult Save(Circuit circuit, string path, string? sheet = null)
    {
        EscExportResult r = Export(circuit, sheet);
        EscWriter.Write(r.Document, path);
        return r;
    }

    public static EscExportResult Export(Circuit circuit, string? sheet = null)
    {
        sheet ??= circuit.Sheets.Count > 0 ? circuit.Sheets[0].Id : "s1";
        var doc = new EscDocument();
        var result = new EscExportResult { Document = doc };
        var cps = doc.ConnPoints;

        int NewCp(EscSide side)
        {
            cps.Add(new EscConnPoint { Side = side, ConnectedTo = -1 });
            return cps.Count - 1;
        }
        void Link(int a, int b)
        {
            if (a >= 0 && b >= 0)
            {
                cps[a].ConnectedTo = b;
                cps[b].ConnectedTo = a;
            }
        }

        if (circuit.Sheets.Count > 1)
        {
            result.Warnings.Add(Loc.T("escSave.oneSheet", SheetTitle(circuit, sheet)));
        }

        // ---- Elemanlar ----
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var terminalCp = new Dictionary<(string, string), int>();
        var skipped = new SortedSet<string>(StringComparer.CurrentCulture);
        bool mirrored = false;
        foreach (ComponentInstance c in circuit.Components)
        {
            if (c.Sheet != sheet || ComponentCatalog.Find(c.Type) is not { } def)
            {
                continue;
            }
            if (def.LegacyType is not int type)
            {
                skipped.Add(def.Name);
                continue;
            }
            mirrored |= c.Mirror;
            var e = new EscElement
            {
                Type = type,
                Location = Round(c.Position),
                Rotation = c.Rotation & 3,
                // eski programda besleme gibi sabit adlı elemanlar adsızdır ("...")
                Label1 = c.Label.Trim().Length > 0 && c.Label.Trim() != def.DefaultLabel ? c.Label.Trim() : "...",
                Label1Location = Round(def.LabelWorld(c, circuit)),
                Label2 = "0"
            };
            Vec size = def.WorldSize(c);
            e.Label2Location = Round(c.Position + new Vec(size.X / 2, size.Y + 4));
            if (def is TimerOnDef or TimerOffDef)
            {
                e.Label2 = Math.Max(1, (int)Math.Round(c.GetDouble("time", 3))).ToString(CultureInfo.InvariantCulture);
                e.Label2Visible = true;
            }
            if (def is LampDef)
            {
                e.ColorName = c.GetParam("lampColor", "yellow") switch
                {
                    "red" => "Red",
                    "blue" => "Blue",
                    "green" => "Green",
                    "magenta" => "Magenta",
                    "white" => "White",
                    "orange" => "Orange",
                    _ => "Yellow"
                };
            }
            for (int k = 0; k < def.LegacyTerminalCount && k < def.Terminals.Count; k++)
            {
                TerminalDef t = def.Terminals[k];
                int cp = NewCp((EscSide)def.TerminalSide(c, t));
                e.ConnPoints.Add(cp);
                terminalCp[(c.Id, t.Id)] = cp;
            }
            index[c.Id] = doc.Elements.Count;
            doc.Elements.Add(e);
        }
        foreach (string name in skipped)
        {
            result.Warnings.Add(Loc.T("escSave.skipped", name));
        }
        if (mirrored)
        {
            result.Warnings.Add(Loc.T("escSave.mirror"));
        }

        // Kontak ↔ bobin bağları: eski programda kontaklar yalnızca röle ve zaman rölelerine bağlanır
        var masters = new Dictionary<string, ComponentInstance>(StringComparer.OrdinalIgnoreCase);
        foreach (ComponentInstance c in circuit.Components)
        {
            if (index.ContainsKey(c.Id) && ComponentCatalog.Find(c.Type) is { Role: DeviceRole.Master, LegacyType: 0 or 1 or 2 } &&
                c.Label.Trim().Length > 0)
            {
                masters.TryAdd(c.Label.Trim(), c);
            }
        }
        foreach (ComponentInstance c in circuit.Components)
        {
            if (!index.TryGetValue(c.Id, out int ci) || ComponentCatalog.Find(c.Type)?.Role != DeviceRole.Follower)
            {
                continue;
            }
            if (masters.TryGetValue(c.Label.Trim(), out ComponentInstance? m))
            {
                int mi = index[m.Id];
                doc.Elements[ci].Links.Add(mi);
                doc.Elements[mi].Links.Add(ci);
            }
            else if (c.Label.Trim().Length > 0)
            {
                result.Warnings.Add(Loc.T("escSave.noCoil", c.Label.Trim()));
            }
        }

        // ---- Bağlantı uçları: klemens ve ek noktalarında birden çok kablo için ek noktası zinciri ----
        var wires = circuit.Wires.Where(w => w.Sheet == sheet && w.Points.Count >= 2).ToList();
        var ends = new Dictionary<string, List<(Wire W, bool A)>>(StringComparer.Ordinal);
        string? KeyOf(WireEnd end) =>
            end.IsTerminal && terminalCp.ContainsKey((end.Component!, end.Terminal!)) ? "T:" + end.Component + "\u0001" + end.Terminal
            : end.IsJunction && circuit.FindJunction(end.Junction) is { } j && j.Sheet == sheet ? "J:" + end.Junction
            : null;
        int droppedEnds = 0;
        foreach (Wire w in wires)
        {
            foreach (bool a in new[] { true, false })
            {
                WireEnd end = a ? w.A : w.B;
                if (KeyOf(end) is { } key)
                {
                    if (!ends.TryGetValue(key, out var list))
                    {
                        list = [];
                        ends[key] = list;
                    }
                    list.Add((w, a));
                }
                else if (!end.IsFree)
                {
                    droppedEnds++;
                }
            }
        }
        if (droppedEnds > 0)
        {
            result.Warnings.Add(Loc.T("escSave.droppedEnds", droppedEnds));
        }

        // Her kablo ucunun bağlanacağı bağlantı noktası
        var endCp = new Dictionary<(Wire, bool), int>();
        foreach (var (key, list) in ends)
        {
            Vec at;
            int? terminal = null;
            EscSide towardElement = EscSide.Null;
            if (key[0] == 'T')
            {
                string[] parts = key[2..].Split('');
                terminal = terminalCp[(parts[0], parts[1])];
                if (list.Count == 1)
                {
                    endCp[(list[0].W, list[0].A)] = terminal.Value;
                    continue;
                }
                ComponentInstance c = circuit.FindComponent(parts[0])!;
                ComponentDef def = ComponentCatalog.Get(c.Type);
                TerminalDef t = def.FindTerminal(parts[1])!;
                at = def.TerminalWorld(c, t);
                towardElement = Opposite((EscSide)def.TerminalSide(c, t));
            }
            else
            {
                at = circuit.FindJunction(key[2..])!.Position;
            }
            // Ek noktası 4 uçludur (alt, sol, sağ, üst); daha fazla bağlantı için noktalar birbirine zincirlenir.
            // k nokta zinciri 2k + 2 uç verir.
            int needed = list.Count + (terminal != null ? 1 : 0);
            int count = Math.Max(1, (needed - 1) / 2);
            var free = new List<(int Cp, EscSide Side)>();
            for (int i = 0; i < count; i++)
            {
                var dot = new EscDot { Origin = Round(at) };
                var mine = new List<(int Cp, EscSide Side)>();
                foreach (EscSide side in new[] { EscSide.Bottom, EscSide.Left, EscSide.Right, EscSide.Top })
                {
                    int cp = NewCp(side);
                    dot.ConnPoints.Add(cp);
                    mine.Add((cp, side));
                }
                doc.Dots.Add(dot);
                if (i > 0)
                {
                    // önceki noktanın boş bir ucuyla doğrudan (çizgisiz) bağlantı
                    Link(Take(free, EscSide.Bottom), Take(mine, EscSide.Top));
                }
                free.AddRange(mine);
            }
            if (terminal is int tcp)
            {
                Link(tcp, Take(free, towardElement));
            }
            foreach ((Wire w, bool a) in list)
            {
                endCp[(w, a)] = Take(free, Direction(w, a));
            }
        }

        // ---- Kablolar: dik parçalar halinde çizgiler ----
        foreach (Wire w in wires)
        {
            List<Vec> pts = Orthogonal(WirePath.Simplify(w.Points).Select(p => Round(p).ToVec()).ToList());
            int startCp = endCp.GetValueOrDefault((w, true), -1);
            int finalCp = endCp.GetValueOrDefault((w, false), -1);
            if (pts.Count < 2)
            {
                Link(startCp, finalCp);
                continue;
            }
            int prev = startCp;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vec p = pts[i], q = pts[i + 1];
                int ca = NewCp(EscSide.Null), cb = NewCp(EscSide.Null);
                doc.Lines.Add(new EscLine
                {
                    Kind = Math.Abs(p.Y - q.Y) < 0.5 ? "h" : "v",
                    A = new EscPoint((int)p.X, (int)p.Y),
                    B = new EscPoint((int)q.X, (int)q.Y),
                    ConnA = ca,
                    ConnB = cb
                });
                Link(prev, ca);
                prev = cb;
            }
            Link(prev, finalCp);
        }

        foreach (TextNote t in circuit.Texts.Where(t => t.Sheet == sheet))
        {
            doc.Texts.Add(new EscText { Location = Round(t.Position), Size = Round(t.Size), Text = t.Text });
        }
        return result;
    }

    private static EscSide Opposite(EscSide s) => s switch
    {
        EscSide.Left => EscSide.Right,
        EscSide.Right => EscSide.Left,
        EscSide.Top => EscSide.Bottom,
        EscSide.Bottom => EscSide.Top,
        _ => EscSide.Null
    };

    /// <summary>Boş uçlardan istenen yöndekini, yoksa ilk boşu alır.</summary>
    private static int Take(List<(int Cp, EscSide Side)> free, EscSide want)
    {
        int i = free.FindIndex(f => f.Side == want);
        if (i < 0)
        {
            i = 0;
        }
        int cp = free[i].Cp;
        free.RemoveAt(i);
        return cp;
    }

    /// <summary>Kablonun ek noktasından hangi yöne çıktığı (ilk parçanın yönü).</summary>
    private static EscSide Direction(Wire w, bool a)
    {
        IReadOnlyList<Vec> p = w.Points;
        Vec from = a ? p[0] : p[^1];
        Vec to = from;
        for (int i = 1; i < p.Count && to.NearlyEquals(from, 0.5); i++)
        {
            to = a ? p[i] : p[^(i + 1)];
        }
        Vec d = to - from;
        if (Math.Abs(d.X) >= Math.Abs(d.Y))
        {
            return d.X > 0 ? EscSide.Right : EscSide.Left;
        }
        return d.Y > 0 ? EscSide.Bottom : EscSide.Top;
    }

    /// <summary>Eğik parçaları (eski dosyadan gelmiş olabilir) L biçiminde iki dik parçaya böler; sıfır uzunlukları atar.</summary>
    private static List<Vec> Orthogonal(List<Vec> pts)
    {
        var list = new List<Vec>();
        foreach (Vec p in pts)
        {
            if (list.Count > 0)
            {
                Vec last = list[^1];
                if (last.NearlyEquals(p, 0.5))
                {
                    continue;
                }
                if (Math.Abs(last.X - p.X) > 0.5 && Math.Abs(last.Y - p.Y) > 0.5)
                {
                    list.Add(new Vec(p.X, last.Y));
                }
            }
            list.Add(p);
        }
        return list;
    }

    private static EscPoint Round(Vec v) =>
        new((int)Math.Round(v.X, MidpointRounding.AwayFromZero), (int)Math.Round(v.Y, MidpointRounding.AwayFromZero));

    private static string SheetTitle(Circuit c, string sheet)
    {
        int i = c.Sheets.FindIndex(s => s.Id == sheet);
        Sheet? s = i >= 0 ? c.Sheets[i] : null;
        return s is { Name.Length: > 0 } ? s.Name : Loc.T("ui.sheetN", i + 1);
    }
}
