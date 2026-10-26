using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Editing;

/// <summary>
/// İki nokta arasında dik açılı kablo yolu önerir. Birkaç aday (L, Z, klemensten kısa çıkışlı) denenir ve en ucuzu seçilir:
/// uzunluk + kırılma başına ceza + başka bir elemanın üstünden geçme cezası. Okul devreleri (merdiven şema) için
/// bu basit yöntem yeterince düzgün sonuç verir; kullanıcı gerekirse köşeleri sürükleyerek düzeltir.
/// </summary>
public static class WireRouter
{
    public const double Stub = 10;
    private const double BendCost = 12;
    private const double CrossCost = 400;
    private const double BackCost = 60;

    /// <summary>Bir uç: konum ve (klemensse) kablonun çıkması gereken yön.</summary>
    public readonly record struct End(Vec Point, Side Side = Side.None);

    /// <summary>Yolu engelleyen dikdörtgenler (eleman gövdeleri).</summary>
    public static List<RectD> Obstacles(Circuit circuit, string sheet, IReadOnlySet<string>? ignore = null)
    {
        var list = new List<RectD>();
        foreach (ComponentInstance c in circuit.Components)
        {
            if (c.Sheet == sheet && ComponentCatalog.Find(c.Type) is { } def && (ignore == null || !ignore.Contains(c.Id)))
            {
                // Klemensler kutunun kenarında durur; kenardan geçen kablolar ceza almasın diye kutu biraz daraltılır
                RectD r = def.Bounds(c);
                if (r.Width > 4 && r.Height > 4)
                {
                    list.Add(new RectD(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4));
                }
            }
        }
        return list;
    }

    public static List<Vec> Route(End from, End to, IReadOnlyList<RectD>? obstacles = null)
    {
        Vec a = from.Point, b = to.Point;
        if (a.NearlyEquals(b))
        {
            return [a, b];
        }
        Vec da = Placement.SideDirection(from.Side), db = Placement.SideDirection(to.Side);
        var starts = new List<Vec> { a };
        var ends = new List<Vec> { b };
        if (from.Side != Side.None)
        {
            starts.Add(a + da * Stub);
        }
        if (to.Side != Side.None)
        {
            ends.Add(b + db * Stub);
        }

        List<Vec>? best = null;
        double bestCost = double.MaxValue;
        foreach (Vec s in starts)
        {
            foreach (Vec e in ends)
            {
                foreach (List<Vec> mid in Middles(s, e, obstacles))
                {
                    var pts = new List<Vec>(mid.Count + 2);
                    if (!s.NearlyEquals(a))
                    {
                        pts.Add(a);
                    }
                    pts.AddRange(mid);
                    if (!e.NearlyEquals(b))
                    {
                        pts.Add(b);
                    }
                    List<Vec> path = WirePath.Simplify(pts);
                    double cost = Cost(path, da, db, obstacles);
                    if (cost < bestCost - 1e-9)
                    {
                        bestCost = cost;
                        best = path;
                    }
                }
            }
        }
        return best ?? [a, b];
    }

    /// <summary>
    /// s ile e arasındaki aday orta yollar: doğrudan, iki L, iki Z ve aradaki elemanların üstünden, altından ya da
    /// yanından dolaşan U yollar.
    /// </summary>
    private static IEnumerable<List<Vec>> Middles(Vec s, Vec e, IReadOnlyList<RectD>? obstacles)
    {
        if (WirePath.IsHorizontal(s, e) || WirePath.IsVertical(s, e))
        {
            yield return [s, e];
        }
        yield return [s, new Vec(e.X, s.Y), e];
        yield return [s, new Vec(s.X, e.Y), e];
        yield return WirePath.ZHorizontal(s, e);
        yield return WirePath.ZVertical(s, e);
        if (obstacles == null)
        {
            yield break;
        }
        RectD area = RectD.FromPoints(s, e).Inflate(Stub);
        int used = 0;
        foreach (RectD r in obstacles)
        {
            if (!r.Intersects(area))
            {
                continue;
            }
            // Çok elemanlı sayfalarda aday sayısı patlamasın
            if (++used > 12)
            {
                yield break;
            }
            foreach (double y in new[] { Channel(r.Y - Stub), Channel(r.Bottom + Stub) })
            {
                yield return [s, new Vec(s.X, y), new Vec(e.X, y), e];
            }
            foreach (double x in new[] { Channel(r.X - Stub), Channel(r.Right + Stub) })
            {
                yield return [s, new Vec(x, s.Y), new Vec(x, e.Y), e];
            }
        }
    }

    /// <summary>Dolaşma hattını yarım ızgaraya yuvarlar, engelden uzaklaşacak yönde.</summary>
    private static double Channel(double v) => v < 0 ? Math.Floor(v / 5) * 5 : Math.Ceiling(v / 5) * 5;

    private static double Cost(List<Vec> path, Vec da, Vec db, IReadOnlyList<RectD>? obstacles)
    {
        double cost = GeometryUtil.PolylineLength(path) + BendCost * Math.Max(0, path.Count - 2);
        if (path.Count >= 2)
        {
            // Klemensin içine doğru (sembolün üstünden) çıkan yollar istenmez
            cost += BackwardPenalty(path[1] - path[0], da);
            cost += BackwardPenalty(path[^2] - path[^1], db);
        }
        if (obstacles != null)
        {
            for (int i = 1; i < path.Count; i++)
            {
                foreach (RectD r in obstacles)
                {
                    if (SegmentHitsRect(path[i - 1], path[i], r))
                    {
                        cost += CrossCost;
                    }
                }
            }
        }
        return cost;
    }

    private static double BackwardPenalty(Vec first, Vec dir)
    {
        if (dir == Vec.Zero || first.Length < 1e-9)
        {
            return 0;
        }
        double dot = (first.X * dir.X + first.Y * dir.Y) / first.Length;
        return dot < -0.5 ? BackCost * 3 : dot < 0.5 ? BackCost : 0;
    }

    /// <summary>Eksene paralel bir parça dikdörtgenin içinden geçiyor mu (kenara değmek sayılmaz).</summary>
    internal static bool SegmentHitsRect(Vec a, Vec b, RectD r)
    {
        double minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X), minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
        return maxX > r.X && minX < r.Right && maxY > r.Y && minY < r.Bottom;
    }
}
