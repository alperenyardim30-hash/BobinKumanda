using BobinKumanda.Core.Drawing;

namespace BobinKumanda.Core.Components;

/// <summary>
/// Okul kitabı (MEB atölye) ve eski EKTS sembol tarzı:
/// kontak ─┤ ├─ (NC'de çapraz çizgi), bobin çapraz çizgili dik dikdörtgen, butonlar iki nokta üzerinde köprü ve sap.
/// Simülasyonda kapalı kontak çapraz çizgiyle ve vurgu rengiyle gösterilir.
/// </summary>
public static partial class SymbolKit
{
    private const double DotR = 1.6;

    /// <summary>─┤ ├─ kontak. Plakalar x0+W/3 ve x0+2W/3'te, y±10 yüksekliğinde.</summary>
    private static Vec SchoolContact(ISymbolPainter p, DrawContext ctx, double y, bool nc, bool closed,
        double x0, double x1, TimedMark timed)
    {
        Paint ink = InkFor(ctx);
        double w = x1 - x0;
        double a = x0 + w / 3, b = x0 + 2 * w / 3;
        const double h = 10;
        bool sim = ctx.Simulating;
        Paint plate = sim && closed ? Paint.Of(Ink.Active) : ink;
        p.Line(new Vec(x0, y), new Vec(a, y), ink);
        p.Line(new Vec(b, y), new Vec(x1, y), ink);
        p.Line(new Vec(a, y - h), new Vec(a, y + h), plate, sim && closed ? 1.4 : 1);
        p.Line(new Vec(b, y - h), new Vec(b, y + h), plate, sim && closed ? 1.4 : 1);
        var slashFrom = new Vec(x0 + 6 * w / 7, y - h);
        var slashTo = new Vec(x0 + w / 7, y + h);
        if (nc)
        {
            // NC: çapraz çizgi; açıldığında (simülasyonda) kesikli ve soluk
            if (sim && !closed)
            {
                p.Line(slashFrom, slashTo, Paint.Of(Ink.Muted), 0.8, dashed: true);
            }
            else
            {
                p.Line(slashFrom, slashTo, plate, sim ? 1.4 : 1);
            }
        }
        else if (sim && closed)
        {
            p.Line(slashFrom, slashTo, Paint.Of(Ink.Active), 1.4);
        }
        if (timed != TimedMark.None)
        {
            Hourglass(p, ink, new Vec((a + b) / 2, y - h - 3.5), 3.2, timed == TimedMark.OffDelay);
        }
        return new Vec((a + b) / 2, y - h);
    }

    /// <summary>Kum saati (zaman) işareti; dolu ise düşmede gecikmeli.</summary>
    private static void Hourglass(ISymbolPainter p, Paint ink, Vec center, double s, bool filled)
    {
        Vec[] pts =
        [
            center + new Vec(-s, -s), center + new Vec(s, s), center + new Vec(-s, s), center + new Vec(s, -s)
        ];
        if (filled)
        {
            p.Polyline([pts[0], pts[3], center], ink, 0.8, closed: true, fill: ink);
            p.Polyline([pts[2], pts[1], center], ink, 0.8, closed: true, fill: ink);
        }
        else
        {
            p.Polyline([.. pts, pts[0]], ink, 0.8);
        }
    }

    /// <summary>Bobin: dik dikdörtgen (W/4..3W/4, y±10). Röle: çapraz çizgi; çekmede gecikmeli: kum saati; düşmede: üstü dolu.</summary>
    private static void SchoolCoil(ISymbolPainter p, DrawContext ctx, double y, bool energized, CoilStyle style, double x0, double x1)
    {
        Paint ink = InkFor(ctx);
        double w = x1 - x0;
        double l = x0 + w / 4, r = x0 + 3 * w / 4;
        const double h = 10;
        bool on = ctx.Simulating && energized;
        Paint line = on ? Paint.Of(Ink.Active) : ink;
        p.Line(new Vec(x0, y), new Vec(l, y), ink);
        p.Line(new Vec(r, y), new Vec(x1, y), ink);
        p.Rect(new Vec(l, y - h), new Vec(r - l, 2 * h), line, on ? 1.3 : 1, on ? Paint.Of(Ink.Active, 0.18) : null);
        switch (style)
        {
            case CoilStyle.Normal:
                p.Line(new Vec(r, y - h / 2), new Vec(l, y + h / 2), line);
                break;
            case CoilStyle.OnDelay:
                p.Polyline([new Vec(l, y - h), new Vec(r, y - h / 3), new Vec(l, y - h / 3), new Vec(r, y - h)], line, 0.9);
                break;
            case CoilStyle.OffDelay:
                p.Rect(new Vec(l, y - h), new Vec(r - l, 2 * h / 3), line, 0.9, line);
                break;
            case CoilStyle.Flasher:
                p.Polyline([new Vec(l + 1, y + 3), new Vec(l + 2.5, y + 3), new Vec(l + 2.5, y - 3), new Vec(r - 2.5, y - 3),
                    new Vec(r - 2.5, y + 3), new Vec(r - 1, y + 3)], line, 0.8);
                break;
            case CoilStyle.Counter:
                p.Text(new Vec((l + r) / 2, y + 2.5), "C", 6, line, bold: true);
                break;
        }
    }

    private static void Dot(ISymbolPainter p, Paint ink, Vec at) => p.Circle(at, DotR, ink, 0.6, ink);

    /// <summary>
    /// Butonlu kontak: kontak noktaları W/4 ve 3W/4'te, köprü çubuğu NO'da üstte açık, NC'de altta değer; sap çubuğun ortasından yukarı çıkar.
    /// Döner: sapın üst ucu.
    /// </summary>
    public static Vec SchoolPushContact(ISymbolPainter p, DrawContext ctx, double y, bool nc, bool pressed,
        double x0 = 0, double x1 = 20, double stem = 10)
    {
        Paint ink = InkFor(ctx);
        double w = x1 - x0;
        double l = x0 + w / 4, r = x0 + 3 * w / 4, m = x0 + w / 2;
        bool sim = ctx.Simulating;
        bool closed = nc ? !pressed : pressed;
        Paint bar = sim && closed ? Paint.Of(Ink.Active) : ink;
        p.Line(new Vec(x0, y), new Vec(l, y), ink);
        p.Line(new Vec(r, y), new Vec(x1, y), ink);
        Dot(p, ink, new Vec(l, y));
        Dot(p, ink, new Vec(r, y));
        double barY = nc
            ? y + DotR + (pressed && sim ? 3 : 0)
            : y - DotR - (pressed && sim ? 0 : 3);
        p.Line(new Vec(l, barY), new Vec(r, barY), bar, sim && closed ? 1.4 : 1);
        double top = nc ? barY - stem : Math.Min(barY - stem * 0.6, y - stem);
        p.Line(new Vec(m, top), new Vec(m, barY), ink);
        return new Vec(m, top);
    }

    /// <summary>Jumelli buton / iki kontaklı sınır anahtarı: üstte NC, altta NO; çubuk ikisinin arasında.</summary>
    public static void SchoolDualContact(ISymbolPainter p, DrawContext ctx, double yTop, double yBottom, bool pressed, bool roller)
    {
        Paint ink = InkFor(ctx);
        bool sim = ctx.Simulating;
        foreach (double y in new[] { yTop, yBottom })
        {
            p.Line(new Vec(0, y), new Vec(5, y), ink);
            p.Line(new Vec(15, y), new Vec(20, y), ink);
            Dot(p, ink, new Vec(5, y));
            Dot(p, ink, new Vec(15, y));
        }
        bool act = sim && pressed;
        double barY = act ? yBottom - 2 * DotR + 0.4 : yTop + DotR;
        p.Line(new Vec(5, barY), new Vec(15, barY), sim ? Paint.Of(Ink.Active) : ink, sim ? 1.4 : 1);
        double top = barY - 9;
        p.Line(new Vec(10, top), new Vec(10, barY), ink);
        if (roller)
        {
            p.Circle(new Vec(10, top - 2.5), 2.5, ink, 0.9, Paint.Of(Ink.Background));
        }
    }

    /// <summary>Değiştirici sınır anahtarı: ortak (5,yC), NO (15,yC), NC (15,yNc); bıçak ve makaralı sap.</summary>
    public static void SchoolChangeover(ISymbolPainter p, DrawContext ctx, double yCommon, double yNc, bool actuated, bool roller)
    {
        Paint ink = InkFor(ctx);
        bool sim = ctx.Simulating;
        p.Line(new Vec(0, yCommon), new Vec(5, yCommon), ink);
        p.Line(new Vec(15, yCommon), new Vec(20, yCommon), ink);
        p.Line(new Vec(15, yNc), new Vec(20, yNc), ink);
        Dot(p, ink, new Vec(5, yCommon));
        Dot(p, ink, new Vec(15, yCommon));
        Dot(p, ink, new Vec(15, yNc));
        double tipY = actuated && sim ? yCommon : yNc + DotR + 0.4;
        if (!sim && actuated)
        {
            tipY = yCommon;
        }
        p.Line(new Vec(5, yCommon), new Vec(15, tipY), sim ? Paint.Of(Ink.Active) : ink, sim ? 1.4 : 1);
        double midY = (yCommon + tipY) / 2;
        double top = midY - 9;
        p.Line(new Vec(10, top), new Vec(10, midY), ink);
        if (roller)
        {
            p.Circle(new Vec(10, top - 2.5), 2.5, ink, 0.9, Paint.Of(Ink.Background));
        }
    }

    /// <summary>Bıçaklı şalter kutbu (eski EKTS "Startx3"): noktalar W/4, 3W/4; açıkken bıçak yukarı kalkık. upperThrow: iki konumlu.</summary>
    public static Vec SchoolKnife(ISymbolPainter p, DrawContext ctx, double y, bool closed, bool upperThrow)
    {
        Paint ink = InkFor(ctx);
        bool sim = ctx.Simulating;
        p.Line(new Vec(0, y), new Vec(5, y), ink);
        p.Line(new Vec(15, y), new Vec(20, y), ink);
        Dot(p, ink, new Vec(5, y));
        Dot(p, ink, new Vec(15, y));
        double lift;
        if (upperThrow)
        {
            double uy = y - 8;
            p.Line(new Vec(15, uy), new Vec(20, uy), ink);
            Dot(p, ink, new Vec(15, uy));
            lift = closed ? 0 : 8 - DotR;
        }
        else
        {
            lift = closed ? 0 : DotR + 3;
        }
        var tip = new Vec(15, y - lift);
        p.Line(new Vec(5, y), tip, sim ? Paint.Of(Ink.Active) : ink, sim ? 1.4 : 1);
        return (new Vec(5, y) + tip) / 2;
    }

    /// <summary>Okul kitabındaki motor dairesi; dönerken çevresinde bir nokta gezinir.</summary>
    public static void SchoolMotor(ISymbolPainter p, DrawContext ctx, Vec center, double r, string top, string bottom,
        double speed, int direction, double animTime)
    {
        Paint ink = InkFor(ctx);
        bool running = ctx.Simulating && Math.Abs(speed) > 0.02;
        p.Circle(center, r, running ? Paint.Of(Ink.Active) : ink, running ? 1.3 : 1.1, running ? Paint.Of(Ink.Active, 0.12) : null);
        p.Text(center + new Vec(0, -r * 0.18), top, r * 0.55, ink, bold: true);
        p.Text(center + new Vec(0, r * 0.45), bottom, r * 0.38, ink);
        if (running)
        {
            double angle = animTime * Math.Abs(speed) * 2 * Math.PI * 0.8 * (direction < 0 ? -1 : 1);
            var dot = center + new Vec(Math.Cos(angle), Math.Sin(angle)) * r;
            p.Circle(dot, 2.4, Paint.Of(Ink.Active), 0.8, Paint.Of(Ink.Active));
        }
    }

    /// <summary>Yay biçimli sargı (eski EKTS bir fazlı motor çizimi).</summary>
    public static void Winding(ISymbolPainter p, Paint ink, Vec from, double length, int loops = 4)
    {
        var pts = new List<Vec>();
        int steps = loops * 12;
        double loopW = length / loops;
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps * loops;
            double phase = t * 2 * Math.PI;
            // ilerleyen sarmal: x ileri gider, halka için geri döner
            double x = from.X + t * loopW - Math.Sin(phase) * loopW * 0.45;
            double y = from.Y - (1 - Math.Cos(phase)) * 2.2;
            pts.Add(new Vec(x, y));
        }
        p.Polyline(pts, ink, 1);
    }

    /// <summary>Kafes rotorlu motor: dış daire, mil ve 7 çubuk.</summary>
    public static void CageRotor(ISymbolPainter p, DrawContext ctx, Vec center, double r, double speed, int direction, double animTime)
    {
        Paint ink = InkFor(ctx);
        bool running = ctx.Simulating && Math.Abs(speed) > 0.02;
        Paint line = running ? Paint.Of(Ink.Active) : ink;
        p.Circle(center, r, line, running ? 1.3 : 1.1, running ? Paint.Of(Ink.Active, 0.12) : null);
        p.Circle(center, r * 0.18, line, 0.9);
        double turn = running ? animTime * Math.Abs(speed) * 2 * Math.PI * 0.6 * (direction < 0 ? -1 : 1) : 0;
        for (int i = 0; i < 7; i++)
        {
            double a = 2 * Math.PI * i / 7 + turn;
            p.Circle(center + new Vec(Math.Cos(a), Math.Sin(a)) * r * 0.66, 1.6, line, 0.8);
        }
    }

    /// <summary>Kondansatör (iki paralel plaka), yatay bağlantı için.</summary>
    public static void Capacitor(ISymbolPainter p, Paint ink, Vec left, Vec right)
    {
        double cx = (left.X + right.X) / 2;
        p.Line(left, new Vec(cx - 1.5, left.Y), ink);
        p.Line(new Vec(cx + 1.5, right.Y), right, ink);
        p.Line(new Vec(cx - 1.5, left.Y - 4), new Vec(cx - 1.5, left.Y + 4), ink, 1.2);
        p.Line(new Vec(cx + 1.5, right.Y - 4), new Vec(cx + 1.5, right.Y + 4), ink, 1.2);
    }
}
