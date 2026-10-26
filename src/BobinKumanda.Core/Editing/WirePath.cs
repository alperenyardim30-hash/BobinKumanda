namespace BobinKumanda.Core.Editing;

/// <summary>
/// Dik açılı kablo yollarının geometrisi. Bütün işlemler yolu "her parça yatay ya da dikey, ardışık parçalar dik" halinde
/// tutar; böylece bir uç taşındığında yalnızca komşu köşeyi kaydırmak yeterli olur.
/// </summary>
public static class WirePath
{
    private const double Eps = 1e-6;

    public static bool IsHorizontal(Vec a, Vec b) => Math.Abs(a.Y - b.Y) < Eps;

    public static bool IsVertical(Vec a, Vec b) => Math.Abs(a.X - b.X) < Eps;

    /// <summary>Tekrarlanan noktaları ve aynı doğru üzerindeki ara köşeleri atar. Sonuç en az iki noktadır.</summary>
    public static List<Vec> Simplify(IReadOnlyList<Vec> points)
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
                if ((IsVertical(a, m) && IsVertical(m, p)) || (IsHorizontal(a, m) && IsHorizontal(m, p)))
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
        else if (list.Count == 0)
        {
            list.Add(Vec.Zero);
            list.Add(Vec.Zero);
        }
        return list;
    }

    /// <summary>Bütün parçalar yatay ya da dikey mi.</summary>
    public static bool IsOrthogonal(IReadOnlyList<Vec> points)
    {
        for (int i = 1; i < points.Count; i++)
        {
            if (!IsHorizontal(points[i - 1], points[i]) && !IsVertical(points[i - 1], points[i]))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Kablonun bir ucunu yeni yere taşır, diğer ucu ve köşelerin çoğunu yerinde bırakır. Uca bağlı ilk parçanın yönü
    /// (klemensten çıkış yönü) korunur; gerekirse araya bir kırılma eklenir.
    /// </summary>
    public static List<Vec> MoveEnd(IReadOnlyList<Vec> points, bool start, Vec to)
    {
        List<Vec> pts = Simplify(points);
        if (!start)
        {
            pts.Reverse();
        }
        Vec from = pts[0];
        if (from.NearlyEquals(to))
        {
            if (!start)
            {
                pts.Reverse();
            }
            return pts;
        }
        if (pts.Count == 2)
        {
            Vec other = pts[1];
            bool horizontal = from.NearlyEquals(other) ? Math.Abs(to.X - other.X) >= Math.Abs(to.Y - other.Y) : IsHorizontal(from, other);
            pts = horizontal ? ZHorizontal(to, other) : ZVertical(to, other);
        }
        else
        {
            Vec next = pts[1];
            bool horizontal = IsHorizontal(from, next);
            pts[0] = to;
            pts[1] = horizontal ? new Vec(next.X, to.Y) : new Vec(to.X, next.Y);
        }
        List<Vec> result = Simplify(pts);
        if (!start)
        {
            result.Reverse();
        }
        return result;
    }

    /// <summary>Yatay başlayıp yatay biten Z yol: önce yatay, ortada dikey, sonra yine yatay.</summary>
    public static List<Vec> ZHorizontal(Vec a, Vec b)
    {
        if (IsHorizontal(a, b))
        {
            return [a, b];
        }
        double mx = SnapHalf((a.X + b.X) / 2);
        return Simplify([a, new Vec(mx, a.Y), new Vec(mx, b.Y), b]);
    }

    public static List<Vec> ZVertical(Vec a, Vec b)
    {
        if (IsVertical(a, b))
        {
            return [a, b];
        }
        double my = SnapHalf((a.Y + b.Y) / 2);
        return Simplify([a, new Vec(a.X, my), new Vec(b.X, my), b]);
    }

    /// <summary>Orta noktaları yarım ızgaraya (5 birim) yuvarlar ki kırılmalar düzgün yerlere düşsün.</summary>
    private static double SnapHalf(double v) => Math.Round(v / 5) * 5;

    public static List<Vec> Translate(IReadOnlyList<Vec> points, Vec delta)
    {
        var list = new List<Vec>(points.Count);
        foreach (Vec p in points)
        {
            list.Add(p + delta);
        }
        return list;
    }

    /// <summary>Noktaya en yakın parça; uzaklık ve parça üzerindeki en yakın nokta ile.</summary>
    public static (int Segment, double Distance, Vec Closest) Nearest(IReadOnlyList<Vec> points, Vec p)
    {
        int best = -1;
        double bestD = double.MaxValue;
        Vec bestC = p;
        for (int i = 1; i < points.Count; i++)
        {
            double d = GeometryUtil.DistanceToSegment(p, points[i - 1], points[i], out Vec c);
            if (d < bestD)
            {
                best = i - 1;
                bestD = d;
                bestC = c;
            }
        }
        return (best, bestD, bestC);
    }

    /// <summary>Yolu verilen parçadaki bir noktadan ikiye böler; nokta iki parçada da bulunur.</summary>
    public static (List<Vec> First, List<Vec> Second) SplitAt(IReadOnlyList<Vec> points, int segment, Vec at)
    {
        var first = new List<Vec>();
        for (int i = 0; i <= segment; i++)
        {
            first.Add(points[i]);
        }
        first.Add(at);
        var second = new List<Vec> { at };
        for (int i = segment + 1; i < points.Count; i++)
        {
            second.Add(points[i]);
        }
        return (Simplify(first), Simplify(second));
    }

    /// <summary>
    /// Bir parçayı kendine dik yönde kaydırır (köşe düzeltme). Uçtaki parçalar kaydırılırken klemensten kopmasın diye
    /// uca yeni bir dik parça eklenir. Dönen liste sadeleştirilmemiştir; sürükleme bitince <see cref="Simplify"/> çağrılır.
    /// </summary>
    public static List<Vec> DragSegment(IReadOnlyList<Vec> points, int segment, Vec delta)
    {
        var pts = new List<Vec>(points);
        if (segment < 0 || segment >= pts.Count - 1)
        {
            return pts;
        }
        if (segment == 0)
        {
            pts.Insert(0, pts[0]);
            segment++;
        }
        if (segment == pts.Count - 2)
        {
            pts.Add(pts[^1]);
        }
        Vec a = pts[segment], b = pts[segment + 1];
        if (IsHorizontal(a, b))
        {
            pts[segment] = new Vec(a.X, a.Y + delta.Y);
            pts[segment + 1] = new Vec(b.X, b.Y + delta.Y);
        }
        else
        {
            pts[segment] = new Vec(a.X + delta.X, a.Y);
            pts[segment + 1] = new Vec(b.X + delta.X, b.Y);
        }
        return pts;
    }
}
