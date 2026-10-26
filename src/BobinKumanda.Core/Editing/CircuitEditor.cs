using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Editing;

/// <summary>
/// Devre üzerinde düzenleme işlemleri: eleman ekleme, kablo bağlama, silme, döndürme, kopyalama/yapıştırma.
/// Arayüzden bağımsızdır; geri alma için işlemden önce <see cref="EditHistory.Push"/> çağrılır.
/// </summary>
public static class CircuitEditor
{
    public const double Grid = 10;
    private const double Eps = 0.01;

    public static Vec Snap(Vec p) => p.Snap(Grid);

    // ---- Eleman ----

    /// <summary>
    /// Yeni eleman ekler: varsayılan parametreler, otomatik etiket ve konumdaki boştaki kablo uçlarının bağlanması.
    /// Kontaklar için <paramref name="label"/> bağlanacağı bobinin etiketidir.
    /// </summary>
    public static ComponentInstance AddComponent(Circuit circuit, string type, Vec position, string sheet,
        int rotation = 0, bool mirror = false, string? label = null)
    {
        ComponentDef def = ComponentCatalog.Get(type);
        var c = new ComponentInstance
        {
            Id = circuit.NewId("c"),
            Type = type,
            Sheet = sheet,
            Position = position,
            Rotation = rotation & 3,
            Mirror = mirror,
            Label = label ?? (def.Role == DeviceRole.Follower ? DefaultFollowerLabel(circuit, def) : Labeler.Next(circuit, def))
        };
        def.ApplyDefaults(c);
        circuit.Components.Add(c);
        ConnectFreeEnds(circuit, c);
        return c;
    }

    /// <summary>
    /// Yeni kontak için etiket: devredeki son rölenin etiketi, hiç röle yoksa K1. Termik kontağı gibi kendi öneki olan
    /// kontaklar o önekli son cihazı (F2) izler.
    /// </summary>
    public static string DefaultFollowerLabel(Circuit circuit, ComponentDef def)
    {
        bool Prefers(ComponentDef d) =>
            def.LabelPrefix == "K" ? d.Category == ComponentCategory.Relays : d.LabelPrefix == def.LabelPrefix;
        for (int i = circuit.Components.Count - 1; i >= 0; i--)
        {
            ComponentInstance c = circuit.Components[i];
            if (ComponentCatalog.Find(c.Type) is { Role: DeviceRole.Master } d && Prefers(d) && c.Label.Trim().Length > 0)
            {
                return c.Label.Trim();
            }
        }
        return def.LabelPrefix + "1";
    }

    /// <summary>Elemanın klemenslerinin tam üstünde duran boştaki kablo uçlarını o klemense bağlar.</summary>
    public static int ConnectFreeEnds(Circuit circuit, ComponentInstance c)
    {
        if (ComponentCatalog.Find(c.Type) is not { } def)
        {
            return 0;
        }
        int n = 0;
        foreach (TerminalDef t in def.Terminals)
        {
            Vec p = def.TerminalWorld(c, t);
            foreach (Wire w in circuit.Wires)
            {
                if (w.Sheet != c.Sheet || w.Points.Count < 2)
                {
                    continue;
                }
                if (w.A.IsFree && w.Points[0].NearlyEquals(p, Eps))
                {
                    w.A = WireEnd.ToTerminal(c.Id, t.Id);
                    n++;
                }
                if (w.B.IsFree && w.Points[^1].NearlyEquals(p, Eps))
                {
                    w.B = WireEnd.ToTerminal(c.Id, t.Id);
                    n++;
                }
            }
        }
        return n;
    }

    // ---- Kablo ----

    /// <summary>Bir kablo ucunun dünyadaki yeri (boşta ise kablonun kendi noktası).</summary>
    public static Vec EndPosition(Circuit circuit, Wire w, bool a)
    {
        WireEnd end = a ? w.A : w.B;
        if (end.IsTerminal && circuit.FindComponent(end.Component) is { } c && ComponentCatalog.Find(c.Type) is { } def &&
            def.FindTerminal(end.Terminal!) is { } t)
        {
            return def.TerminalWorld(c, t);
        }
        if (end.IsJunction && circuit.FindJunction(end.Junction) is { } j)
        {
            return j.Position;
        }
        return w.Points.Count == 0 ? Vec.Zero : a ? w.Points[0] : w.Points[^1];
    }

    /// <summary>Uç klemense bağlıysa kablonun çıkması gereken yön.</summary>
    public static Side EndSide(Circuit circuit, WireEnd end)
    {
        if (end.IsTerminal && circuit.FindComponent(end.Component) is { } c && ComponentCatalog.Find(c.Type) is { } def &&
            def.FindTerminal(end.Terminal!) is { } t)
        {
            return def.TerminalSide(c, t);
        }
        return Side.None;
    }

    /// <summary>
    /// Vuruşu kablo ucuna çevirir. Kablonun ortasına bağlanılıyorsa kablo bölünür ve araya ek noktası konur.
    /// </summary>
    public static WireEnd ResolveEnd(Circuit circuit, string sheet, Hit hit)
    {
        switch (hit.Kind)
        {
            case HitKind.Terminal:
                return WireEnd.ToTerminal(hit.Id, hit.Terminal!);
            case HitKind.Junction:
                return WireEnd.ToJunction(hit.Id);
            case HitKind.Wire when circuit.FindWire(hit.Id) is { } w:
                return WireEnd.ToJunction(SplitWire(circuit, w, hit.Segment, hit.Point).Id);
            default:
                return WireEnd.Free();
        }
    }

    /// <summary>
    /// Kabloyu verilen noktada ek noktasıyla ikiye böler. Nokta kablonun ucundaysa bölmez: uç zaten ek noktasıysa onu,
    /// boştaysa ucu yeni ek noktasına bağlayıp onu döndürür.
    /// </summary>
    public static Junction SplitWire(Circuit circuit, Wire w, int segment, Vec at)
    {
        foreach (bool a in (ReadOnlySpan<bool>)[true, false])
        {
            Vec endPos = a ? w.Points[0] : w.Points[^1];
            if (!endPos.NearlyEquals(at, Eps))
            {
                continue;
            }
            WireEnd end = a ? w.A : w.B;
            if (end.IsJunction && circuit.FindJunction(end.Junction) is { } existing)
            {
                return existing;
            }
            if (end.IsFree)
            {
                var jEnd = new Junction { Id = circuit.NewId("j"), Sheet = w.Sheet, Position = endPos };
                circuit.Junctions.Add(jEnd);
                if (a)
                {
                    w.A = WireEnd.ToJunction(jEnd.Id);
                }
                else
                {
                    w.B = WireEnd.ToJunction(jEnd.Id);
                }
                return jEnd;
            }
        }
        var j = new Junction { Id = circuit.NewId("j"), Sheet = w.Sheet, Position = at };
        circuit.Junctions.Add(j);
        (List<Vec> first, List<Vec> second) = WirePath.SplitAt(w.Points, Math.Clamp(segment, 0, w.Points.Count - 2), at);
        var tail = new Wire { Id = circuit.NewId("w"), Sheet = w.Sheet, A = WireEnd.ToJunction(j.Id), B = w.B, Points = second };
        w.B = WireEnd.ToJunction(j.Id);
        w.Points = first;
        circuit.Wires.Insert(circuit.Wires.IndexOf(w) + 1, tail);
        return j;
    }

    public static Wire AddWire(Circuit circuit, string sheet, WireEnd a, WireEnd b, IReadOnlyList<Vec> points)
    {
        var w = new Wire { Id = circuit.NewId("w"), Sheet = sheet, A = a, B = b, Points = WirePath.Simplify(points) };
        circuit.Wires.Add(w);
        return w;
    }

    /// <summary>İki uç arasında otomatik dik açılı yol.</summary>
    public static List<Vec> RouteBetween(Circuit circuit, string sheet, Vec from, Side fromSide, Vec to, Side toSide)
    {
        List<RectD> obstacles = WireRouter.Obstacles(circuit, sheet);
        return WireRouter.Route(new WireRouter.End(from, fromSide), new WireRouter.End(to, toSide), obstacles);
    }

    // ---- Silme ----

    public static void Delete(Circuit circuit, Selection s)
    {
        var touched = new HashSet<string>(StringComparer.Ordinal);
        circuit.Texts.RemoveAll(t => s.Texts.Contains(t.Id));
        foreach (Wire w in circuit.Wires)
        {
            if (s.Wires.Contains(w.Id))
            {
                foreach (WireEnd e in (ReadOnlySpan<WireEnd>)[w.A, w.B])
                {
                    if (e.IsJunction)
                    {
                        touched.Add(e.Junction!);
                    }
                }
            }
        }
        circuit.Wires.RemoveAll(w => s.Wires.Contains(w.Id));
        // Silinen elemanın kabloları yerinde kalır, uçları boşa çıkar; yerine yeni eleman konunca yeniden bağlanır
        foreach (Wire w in circuit.Wires)
        {
            if (w.A.IsTerminal && s.Components.Contains(w.A.Component!))
            {
                w.A = WireEnd.Free();
            }
            if (w.B.IsTerminal && s.Components.Contains(w.B.Component!))
            {
                w.B = WireEnd.Free();
            }
            if (w.A.IsJunction && s.Junctions.Contains(w.A.Junction!))
            {
                w.A = WireEnd.Free();
            }
            if (w.B.IsJunction && s.Junctions.Contains(w.B.Junction!))
            {
                w.B = WireEnd.Free();
            }
        }
        circuit.Components.RemoveAll(c => s.Components.Contains(c.Id));
        circuit.Junctions.RemoveAll(j => s.Junctions.Contains(j.Id));
        touched.ExceptWith(s.Junctions);
        CleanupJunctions(circuit, touched);
    }

    /// <summary>
    /// Kablo silindikten sonra ek noktalarını toparlar: hiç kablosu kalmayan silinir, tek kablolu olan boş uca döner,
    /// iki kablolu olan iki kabloyu tek kabloya birleştirir.
    /// </summary>
    public static void CleanupJunctions(Circuit circuit, IEnumerable<string> junctionIds)
    {
        foreach (string id in junctionIds.ToList())
        {
            if (circuit.FindJunction(id) is not { } j)
            {
                continue;
            }
            var ends = new List<(Wire Wire, bool A)>();
            foreach (Wire w in circuit.Wires)
            {
                if (w.A.IsJunction && w.A.Junction == id)
                {
                    ends.Add((w, true));
                }
                if (w.B.IsJunction && w.B.Junction == id)
                {
                    ends.Add((w, false));
                }
            }
            if (ends.Count >= 3)
            {
                continue;
            }
            if (ends.Count == 2 && ends[0].Wire != ends[1].Wire)
            {
                Merge(circuit, ends[0], ends[1]);
            }
            else
            {
                foreach ((Wire w, bool a) in ends)
                {
                    if (a)
                    {
                        w.A = WireEnd.Free();
                    }
                    else
                    {
                        w.B = WireEnd.Free();
                    }
                }
            }
            circuit.Junctions.Remove(j);
        }
    }

    /// <summary>Aynı ek noktasında buluşan iki kabloyu tek kablo yapar (ilki kalır, ikincisi silinir).</summary>
    private static void Merge(Circuit circuit, (Wire Wire, bool A) x, (Wire Wire, bool A) y)
    {
        // x'i ek noktasında biten, y'yi ek noktasında başlayan hale getir
        List<Vec> px = [.. x.Wire.Points];
        WireEnd farX = x.A ? x.Wire.B : x.Wire.A;
        if (x.A)
        {
            px.Reverse();
        }
        List<Vec> py = [.. y.Wire.Points];
        WireEnd farY = y.A ? y.Wire.B : y.Wire.A;
        if (!y.A)
        {
            py.Reverse();
        }
        px.AddRange(py);
        x.Wire.A = farX;
        x.Wire.B = farY;
        x.Wire.Points = WirePath.Simplify(px);
        circuit.Wires.Remove(y.Wire);
    }

    // ---- Döndürme ve ayna ----

    /// <summary>
    /// Seçimi saat yönünde 90° döndürür. Tek eleman kendi merkezinde, birden çok öğe ortak merkez etrafında döner.
    /// Seçim dışındaki uçlara giden kablolar yeniden yönlendirilir.
    /// </summary>
    public static void Rotate(Circuit circuit, Selection s) => Transform(circuit, s, mirror: false);

    /// <summary>Seçimi yatayda aynalar (sol-sağ).</summary>
    public static void Mirror(Circuit circuit, Selection s) => Transform(circuit, s, mirror: true);

    private static void Transform(Circuit circuit, Selection s, bool mirror)
    {
        if (HitTest.Bounds(circuit, s) is not { } box)
        {
            return;
        }
        Vec center = box.Center;
        Vec Map(Vec p)
        {
            Vec v = p - center;
            return mirror ? new Vec(center.X - v.X, p.Y) : new Vec(center.X - v.Y, center.Y + v.X);
        }

        var moved = new Dictionary<string, Vec>(StringComparer.Ordinal);
        foreach (ComponentInstance c in circuit.Components)
        {
            if (!s.Components.Contains(c.Id) || ComponentCatalog.Find(c.Type) is not { } def)
            {
                continue;
            }
            Vec mid = def.Bounds(c).Center;
            if (mirror)
            {
                // Dönmüş elemanda yerel ayna dünyada dikey çevirmeye denk gelir; dünyada yatay ayna için yarım tur eklenir
                c.Mirror = !c.Mirror;
                if ((c.Rotation & 1) == 1)
                {
                    c.Rotation = (c.Rotation + 2) & 3;
                }
            }
            else
            {
                c.Rotation = (c.Rotation + 1) & 3;
            }
            Vec size = def.WorldSize(c);
            Vec newMid = s.Count == 1 ? mid : Map(mid);
            c.Position = newMid - size / 2;
        }
        foreach (Junction j in circuit.Junctions)
        {
            if (s.Junctions.Contains(j.Id))
            {
                j.Position = Map(j.Position);
            }
        }
        foreach (TextNote t in circuit.Texts)
        {
            if (s.Texts.Contains(t.Id) && s.Count > 1)
            {
                RectD r = HitTest.TextBounds(t);
                Vec m = Map(r.Center);
                t.Position = m - new Vec(r.Width / 2, r.Height / 2);
            }
        }

        // Döndürmeden sonra her şeyi aynı miktarda kaydırıp ilk elemanı ızgaraya oturt
        Vec shift = Vec.Zero;
        if (circuit.Components.FirstOrDefault(c => s.Components.Contains(c.Id)) is { } first)
        {
            shift = Snap(first.Position) - first.Position;
        }
        else if (circuit.Junctions.FirstOrDefault(j => s.Junctions.Contains(j.Id)) is { } fj)
        {
            shift = Snap(fj.Position) - fj.Position;
        }
        foreach (ComponentInstance c in circuit.Components)
        {
            if (s.Components.Contains(c.Id))
            {
                c.Position += shift;
                moved[c.Id] = c.Position;
            }
        }
        foreach (Junction j in circuit.Junctions)
        {
            if (s.Junctions.Contains(j.Id))
            {
                j.Position += shift;
            }
        }

        foreach (Wire w in circuit.Wires)
        {
            bool aIn = EndInside(w.A, s), bIn = EndInside(w.B, s);
            if (s.Wires.Contains(w.Id) || (aIn && bIn))
            {
                var pts = new List<Vec>(w.Points.Count);
                foreach (Vec p in w.Points)
                {
                    pts.Add(Map(p) + shift);
                }
                // Uçlar klemenslere tam otursun (yarım ızgaralı klemenslerde yuvarlama farkı olabilir)
                if (pts.Count >= 2)
                {
                    pts = WirePath.MoveEnd(pts, true, w.A.IsFree ? pts[0] : EndPosition(circuit, w, true));
                    pts = WirePath.MoveEnd(pts, false, w.B.IsFree ? pts[^1] : EndPosition(circuit, w, false));
                }
                w.Points = WirePath.Simplify(pts);
            }
            else if (aIn || bIn)
            {
                Reroute(circuit, w);
            }
        }
    }

    private static bool EndInside(WireEnd e, Selection s) =>
        (e.IsTerminal && s.Components.Contains(e.Component!)) || (e.IsJunction && s.Junctions.Contains(e.Junction!));

    /// <summary>Kabloyu iki ucu arasında baştan dik açılı yönlendirir.</summary>
    public static void Reroute(Circuit circuit, Wire w)
    {
        Vec a = EndPosition(circuit, w, true), b = EndPosition(circuit, w, false);
        w.Points = RouteBetween(circuit, w.Sheet, a, EndSide(circuit, w.A), b, EndSide(circuit, w.B));
    }

    // ---- Kopyalama ve yapıştırma ----

    /// <summary>
    /// Seçimi bağımsız bir parça devre olarak kopyalar. Seçili elemanlar arasındaki kablolar da gelir;
    /// seçim dışına giden uçlar parçada boşta kalır.
    /// </summary>
    public static Circuit Copy(Circuit circuit, Selection s)
    {
        var frag = new Circuit { Sheets = [new Sheet { Id = "s1" }] };
        foreach (ComponentInstance c in circuit.Components)
        {
            if (s.Components.Contains(c.Id))
            {
                ComponentInstance x = c.Clone();
                x.Sheet = "s1";
                frag.Components.Add(x);
            }
        }
        foreach (Junction j in circuit.Junctions)
        {
            if (s.Junctions.Contains(j.Id))
            {
                frag.Junctions.Add(new Junction { Id = j.Id, Sheet = "s1", Position = j.Position });
            }
        }
        foreach (Wire w in circuit.Wires)
        {
            bool aIn = EndInside(w.A, s), bIn = EndInside(w.B, s);
            if (!s.Wires.Contains(w.Id) && !(aIn && bIn))
            {
                continue;
            }
            Wire x = w.Clone();
            x.Sheet = "s1";
            if (!aIn)
            {
                x.A = WireEnd.Free();
            }
            if (!bIn)
            {
                x.B = WireEnd.Free();
            }
            frag.Wires.Add(x);
        }
        foreach (TextNote t in circuit.Texts)
        {
            if (s.Texts.Contains(t.Id))
            {
                frag.Texts.Add(new TextNote { Id = t.Id, Sheet = "s1", Position = t.Position, Size = t.Size, Text = t.Text });
            }
        }
        return frag;
    }

    /// <summary>
    /// Parçayı devreye yeni kimliklerle ekler ve <paramref name="offset"/> kadar kaydırır. Röleler ve diğer elemanlar yeni
    /// numara alır; birlikte gelen kontaklar yeni bobinin etiketini izler, bobini gelmeyen kontaklar eski etiketini korur.
    /// </summary>
    public static Selection Paste(Circuit circuit, Circuit fragment, Vec offset, string sheet)
    {
        var result = new Selection();
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var newLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var taken = new List<string>();

        foreach (ComponentInstance src in fragment.Components)
        {
            if (ComponentCatalog.Find(src.Type) is not { } def)
            {
                continue;
            }
            ComponentInstance c = src.Clone();
            c.Id = circuit.NewId("c");
            c.Sheet = sheet;
            c.Position = src.Position + offset;
            if (def.Role != DeviceRole.Follower && def.DefaultLabel == null)
            {
                string fresh = Labeler.Next(circuit, def, taken);
                taken.Add(fresh);
                if (src.Label.Trim().Length > 0)
                {
                    newLabels.TryAdd(src.Label.Trim(), fresh);
                }
                c.Label = fresh;
            }
            ids[src.Id] = c.Id;
            circuit.Components.Add(c);
            result.Components.Add(c.Id);
        }
        foreach (ComponentInstance c in circuit.Components)
        {
            if (result.Components.Contains(c.Id) && ComponentCatalog.Find(c.Type) is { Role: DeviceRole.Follower } &&
                newLabels.TryGetValue(c.Label.Trim(), out string? label))
            {
                c.Label = label;
            }
        }
        foreach (Junction src in fragment.Junctions)
        {
            var j = new Junction { Id = circuit.NewId("j"), Sheet = sheet, Position = src.Position + offset };
            ids[src.Id] = j.Id;
            circuit.Junctions.Add(j);
            result.Junctions.Add(j.Id);
        }
        WireEnd Map(WireEnd e)
        {
            if (e.IsTerminal && ids.TryGetValue(e.Component!, out string? c))
            {
                return WireEnd.ToTerminal(c, e.Terminal!);
            }
            if (e.IsJunction && ids.TryGetValue(e.Junction!, out string? j))
            {
                return WireEnd.ToJunction(j);
            }
            return WireEnd.Free();
        }
        foreach (Wire src in fragment.Wires)
        {
            var w = new Wire
            {
                Id = circuit.NewId("w"),
                Sheet = sheet,
                A = Map(src.A),
                B = Map(src.B),
                Points = WirePath.Translate(src.Points, offset)
            };
            circuit.Wires.Add(w);
            result.Wires.Add(w.Id);
        }
        foreach (TextNote src in fragment.Texts)
        {
            var t = new TextNote { Id = circuit.NewId("t"), Sheet = sheet, Position = src.Position + offset, Size = src.Size, Text = src.Text };
            circuit.Texts.Add(t);
            result.Texts.Add(t.Id);
        }
        return result;
    }

    // ---- Metin ----

    public static TextNote AddText(Circuit circuit, string sheet, Vec position, string text)
    {
        var t = new TextNote { Id = circuit.NewId("t"), Sheet = sheet, Position = position, Text = text };
        circuit.Texts.Add(t);
        return t;
    }

    // ---- Sayfalar ----

    public static Sheet AddSheet(Circuit circuit, string name)
    {
        var s = new Sheet { Id = circuit.NewId("s"), Name = name };
        circuit.Sheets.Add(s);
        return s;
    }

    /// <summary>Sayfayı ve üzerindeki her şeyi siler. Son sayfa silinmez.</summary>
    public static bool DeleteSheet(Circuit circuit, string sheetId)
    {
        if (circuit.Sheets.Count <= 1 || circuit.FindSheet(sheetId) is not { } sheet)
        {
            return false;
        }
        var all = new Selection();
        all.Components.UnionWith(circuit.Components.Where(c => c.Sheet == sheetId).Select(c => c.Id));
        all.Wires.UnionWith(circuit.Wires.Where(w => w.Sheet == sheetId).Select(w => w.Id));
        all.Junctions.UnionWith(circuit.Junctions.Where(j => j.Sheet == sheetId).Select(j => j.Id));
        all.Texts.UnionWith(circuit.Texts.Where(t => t.Sheet == sheetId).Select(t => t.Id));
        Delete(circuit, all);
        circuit.Sheets.Remove(sheet);
        return true;
    }

    public static Selection All(Circuit circuit, string sheet)
    {
        var s = new Selection();
        s.Components.UnionWith(circuit.Components.Where(c => c.Sheet == sheet).Select(c => c.Id));
        s.Wires.UnionWith(circuit.Wires.Where(w => w.Sheet == sheet).Select(w => w.Id));
        s.Junctions.UnionWith(circuit.Junctions.Where(j => j.Sheet == sheet).Select(j => j.Id));
        s.Texts.UnionWith(circuit.Texts.Where(t => t.Sheet == sheet).Select(t => t.Id));
        return s;
    }
}
