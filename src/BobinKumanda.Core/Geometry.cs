namespace BobinKumanda.Core;

/// <summary>Dünya/yerel koordinatlarda 2B nokta veya vektör. Birim: eski EKTS pikseli (ızgara 10 birim).</summary>
public readonly record struct Vec(double X, double Y)
{
    public static readonly Vec Zero = new(0, 0);

    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k);
    public static Vec operator /(Vec a, double k) => new(a.X / k, a.Y / k);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public double DistanceTo(Vec other) => (this - other).Length;

    public Vec Snap(double grid) => new(Math.Round(X / grid) * grid, Math.Round(Y / grid) * grid);

    public bool NearlyEquals(Vec other, double eps = 1e-6) =>
        Math.Abs(X - other.X) < eps && Math.Abs(Y - other.Y) < eps;

    public override string ToString() => FormattableString.Invariant($"({X:0.##}, {Y:0.##})");
}

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public Vec TopLeft => new(X, Y);
    public Vec Center => new(X + Width / 2, Y + Height / 2);

    public static RectD FromPoints(Vec a, Vec b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public bool Contains(Vec p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    public bool Contains(RectD r) => r.X >= X && r.Right <= Right && r.Y >= Y && r.Bottom <= Bottom;

    public bool Intersects(RectD r) => r.X <= Right && r.Right >= X && r.Y <= Bottom && r.Bottom >= Y;

    public RectD Inflate(double d) => new(X - d, Y - d, Width + 2 * d, Height + 2 * d);

    public RectD Union(RectD r)
    {
        double x = Math.Min(X, r.X), y = Math.Min(Y, r.Y);
        return new RectD(x, y, Math.Max(Right, r.Right) - x, Math.Max(Bottom, r.Bottom) - y);
    }
}

public enum Side { None, Left, Top, Right, Bottom }

/// <summary>
/// Eleman yerleşimi. Döndürme eski EKTS ile birebir aynıdır: eleman, döndürülmüş sınır kutusunun sol üst
/// köşesine (Position) göre saat yönünde 90°'lik adımlarla döner. Böylece .esc dosyalarındaki konumlar aynen kullanılır.
/// Ayna (mirror) döndürmeden önce yatay çevirmedir.
/// </summary>
public static class Placement
{
    public static Vec ToWorld(Vec local, Vec size, int rotation, bool mirror, Vec position) =>
        position + ToRotated(local, size, rotation, mirror);

    public static Vec ToRotated(Vec p, Vec size, int rotation, bool mirror)
    {
        if (mirror)
        {
            p = new Vec(size.X - p.X, p.Y);
        }
        return (rotation & 3) switch
        {
            1 => new Vec(size.Y - p.Y, p.X),
            2 => new Vec(size.X - p.X, size.Y - p.Y),
            3 => new Vec(p.Y, size.X - p.X),
            _ => p
        };
    }

    public static Vec ToLocal(Vec world, Vec size, int rotation, bool mirror, Vec position)
    {
        Vec p = world - position;
        Vec local = (rotation & 3) switch
        {
            1 => new Vec(p.Y, size.Y - p.X),
            2 => new Vec(size.X - p.X, size.Y - p.Y),
            3 => new Vec(size.X - p.Y, p.X),
            _ => p
        };
        if (mirror)
        {
            local = new Vec(size.X - local.X, local.Y);
        }
        return local;
    }

    public static Vec RotatedSize(Vec size, int rotation) =>
        (rotation & 1) == 1 ? new Vec(size.Y, size.X) : size;

    public static Side RotateSide(Side side, int rotation, bool mirror)
    {
        if (mirror)
        {
            side = side switch { Side.Left => Side.Right, Side.Right => Side.Left, _ => side };
        }
        for (int i = 0; i < (rotation & 3); i++)
        {
            side = side switch
            {
                Side.Right => Side.Bottom,
                Side.Bottom => Side.Left,
                Side.Left => Side.Top,
                Side.Top => Side.Right,
                _ => side
            };
        }
        return side;
    }

    public static Vec SideDirection(Side side) => side switch
    {
        Side.Left => new Vec(-1, 0),
        Side.Right => new Vec(1, 0),
        Side.Top => new Vec(0, -1),
        Side.Bottom => new Vec(0, 1),
        _ => Vec.Zero
    };
}

public static class GeometryUtil
{
    /// <summary>Noktanın doğru parçasına uzaklığı.</summary>
    public static double DistanceToSegment(Vec p, Vec a, Vec b, out Vec closest)
    {
        Vec ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        double t = len2 < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0, 1);
        closest = a + ab * t;
        return p.DistanceTo(closest);
    }

    public static double PolylineLength(IReadOnlyList<Vec> pts)
    {
        double len = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            len += pts[i - 1].DistanceTo(pts[i]);
        }
        return len;
    }

    /// <summary>Çoklu çizgi üzerinde baştan <paramref name="distance"/> kadar ilerideki nokta.</summary>
    public static Vec PointAlong(IReadOnlyList<Vec> pts, double distance)
    {
        for (int i = 1; i < pts.Count; i++)
        {
            double seg = pts[i - 1].DistanceTo(pts[i]);
            if (distance <= seg || i == pts.Count - 1)
            {
                double t = seg < 1e-9 ? 0 : Math.Clamp(distance / seg, 0, 1);
                return pts[i - 1] + (pts[i] - pts[i - 1]) * t;
            }
            distance -= seg;
        }
        return pts.Count > 0 ? pts[0] : Vec.Zero;
    }
}
