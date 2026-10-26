using BobinKumanda.Core.Drawing;

namespace BobinKumanda.Core.Components;

public enum TimedMark { None, OnDelay, OffDelay }

public enum ActuatorKind { None, Push, Mushroom, Manual, Roller, Rotary, Pull }

/// <summary>Sembol parçaları (IEC 60617 bıçaklı tarz). Okul kitabı tarzı SchoolSymbols.cs'tedir. Koordinatlar yereldir.</summary>
public static partial class SymbolKit
{
    public static Paint InkFor(DrawContext ctx)
    {
        if (ctx.State is { Fault: true })
        {
            return Paint.Of(Ink.Fault);
        }
        if (ctx.Instance.Params.TryGetValue("drawColor", out string? hex) &&
            uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
        {
            return Paint.Color(rgb);
        }
        return Paint.Of(Ink.Normal);
    }

    public static Paint ActiveInk(DrawContext ctx, bool active) =>
        ctx.Simulating && active ? Paint.Of(Ink.Active) : InkFor(ctx);

    /// <summary>
    /// Yatay kontak: uçlar (x0,y) ve (x1,y). Kapalıyken bıçak sabit kontağa değer.
    /// Döner: bıçağın orta noktası (mekanik bağlantı çizgisi buradan geçer).
    /// </summary>
    public static Vec ContactH(ISymbolPainter p, DrawContext ctx, double y, bool normallyClosed, bool closed,
        double x0 = 0, double x1 = 20, TimedMark timed = TimedMark.None)
    {
        if (ctx.School)
        {
            return SchoolContact(p, ctx, y, normallyClosed, closed, x0, x1, timed);
        }
        Paint ink = InkFor(ctx);
        Paint blade = ActiveInk(ctx, closed);
        var pivot = new Vec(x0 + 6, y);
        var fixedEnd = new Vec(x1 - 6, y);
        p.Line(new Vec(x0, y), pivot, ink);
        p.Line(fixedEnd, new Vec(x1, y), ink);
        Vec tip;
        if (normallyClosed)
        {
            // NC: sabit kontakta dik çıkıntı; bıçak çıkıntının ucuna yaslanır
            p.Line(fixedEnd, new Vec(fixedEnd.X, y - 5), ink);
            tip = closed ? new Vec(fixedEnd.X + 1.5, y - 5.5) : new Vec(fixedEnd.X - 1, y - 9);
        }
        else
        {
            tip = closed ? new Vec(fixedEnd.X + 0.5, y) : new Vec(fixedEnd.X + 1, y - 5.5);
        }
        p.Line(pivot, tip, blade, closed && ctx.Simulating ? 1.4 : 1);
        Vec mid = (pivot + tip) / 2;
        if (timed != TimedMark.None)
        {
            DelayMark(p, ink, mid, timed);
        }
        return mid;
    }

    /// <summary>Zaman gecikmeli kontak işareti (bıçağa bağlı yarım daire).</summary>
    public static void DelayMark(ISymbolPainter p, Paint ink, Vec mid, TimedMark timed)
    {
        var top = new Vec(mid.X, mid.Y - 5);
        p.Line(new Vec(mid.X - 1, mid.Y - 0.5), new Vec(top.X - 1, top.Y), ink, 0.8);
        p.Line(new Vec(mid.X + 1, mid.Y - 0.5), new Vec(top.X + 1, top.Y), ink, 0.8);
        // Çekmede gecikmeli: yay yukarı açık; düşmede gecikmeli: yay aşağı açık
        double start = timed == TimedMark.OnDelay ? 180 : 0;
        Arc(p, ink, new Vec(top.X, top.Y - (timed == TimedMark.OnDelay ? 0 : 2.5)), 2.8, start, 180, 0.8);
    }

    /// <summary>Kontak bıçaklarını birbirine ve çalıştırıcıya bağlayan kesikli mekanik bağlantı.</summary>
    public static void MechanicalLink(ISymbolPainter p, DrawContext ctx, Vec from, Vec to) =>
        p.Line(from, to, InkFor(ctx), 0.8, dashed: true);

    public static void Actuator(ISymbolPainter p, DrawContext ctx, ActuatorKind kind, Vec bladeMid, double topY, bool pressed)
    {
        Paint ink = InkFor(ctx);
        double shift = pressed && ctx.Simulating ? 1.5 : 0;
        var head = new Vec(bladeMid.X, topY + shift);
        if (kind != ActuatorKind.None)
        {
            p.Line(bladeMid, new Vec(head.X, head.Y + 2), ink, 0.8, dashed: true);
        }
        switch (kind)
        {
            case ActuatorKind.Push:
                p.Line(new Vec(head.X - 3.5, head.Y), new Vec(head.X + 3.5, head.Y), ink);
                p.Line(new Vec(head.X - 3.5, head.Y), new Vec(head.X - 3.5, head.Y + 2), ink);
                p.Line(new Vec(head.X + 3.5, head.Y), new Vec(head.X + 3.5, head.Y + 2), ink);
                p.Line(new Vec(head.X, head.Y), new Vec(head.X, head.Y + 2), ink);
                break;
            case ActuatorKind.Pull:
                p.Line(new Vec(head.X - 3.5, head.Y + 2), new Vec(head.X + 3.5, head.Y + 2), ink);
                p.Line(new Vec(head.X - 3.5, head.Y + 2), new Vec(head.X - 3.5, head.Y), ink);
                p.Line(new Vec(head.X + 3.5, head.Y + 2), new Vec(head.X + 3.5, head.Y), ink);
                break;
            case ActuatorKind.Mushroom:
                Arc(p, ink, new Vec(head.X, head.Y + 2), 4, 180, 180, 1);
                p.Line(new Vec(head.X - 4, head.Y + 2), new Vec(head.X + 4, head.Y + 2), ink);
                break;
            case ActuatorKind.Manual:
                p.Line(new Vec(head.X - 3, head.Y + 2), new Vec(head.X + 3, head.Y + 2), ink);
                break;
            case ActuatorKind.Rotary:
                p.Line(new Vec(head.X - 3, head.Y + 3), new Vec(head.X - 3, head.Y + 0.5), ink);
                p.Line(new Vec(head.X - 3, head.Y + 0.5), new Vec(head.X + 3, head.Y + 0.5), ink);
                p.Line(new Vec(head.X + 3, head.Y + 0.5), new Vec(head.X + 3, head.Y - 1.5), ink);
                break;
            case ActuatorKind.Roller:
                p.Circle(new Vec(head.X, head.Y + 0.5), 1.8, ink);
                break;
        }
    }

    public enum CoilStyle { Normal, OnDelay, OffDelay, Flasher, Counter }

    /// <summary>Yatay bobin: uçlar (x0,y)-(x1,y), ortada dikdörtgen.</summary>
    public static void CoilH(ISymbolPainter p, DrawContext ctx, double y, bool energized, CoilStyle style = CoilStyle.Normal,
        double x0 = 0, double x1 = 20, double boxW = 10, double boxH = 10)
    {
        if (ctx.School)
        {
            SchoolCoil(p, ctx, y, energized, style, x0, x1);
            return;
        }
        Paint ink = InkFor(ctx);
        double cx = (x0 + x1) / 2;
        var tl = new Vec(cx - boxW / 2, y - boxH / 2);
        p.Line(new Vec(x0, y), new Vec(tl.X, y), ink);
        p.Line(new Vec(tl.X + boxW, y), new Vec(x1, y), ink);
        Paint? fill = ctx.Simulating && energized ? Paint.Of(Ink.Active, 0.45) : null;
        if (style == CoilStyle.OffDelay)
        {
            fill = ctx.Simulating && energized ? Paint.Of(Ink.Active, 0.9) : ink;
        }
        p.Rect(tl, new Vec(boxW, boxH), ink, 1, fill);
        switch (style)
        {
            case CoilStyle.OnDelay:
                p.Line(tl, tl + new Vec(boxW, boxH), ink, 0.8);
                p.Line(tl + new Vec(0, boxH), tl + new Vec(boxW, 0), ink, 0.8);
                break;
            case CoilStyle.Flasher:
                p.Polyline([tl + new Vec(1.5, boxH - 2.5), tl + new Vec(3.5, boxH - 2.5), tl + new Vec(3.5, 2.5),
                    tl + new Vec(6.5, 2.5), tl + new Vec(6.5, boxH - 2.5), tl + new Vec(8.5, boxH - 2.5)], ink, 0.8);
                break;
            case CoilStyle.Counter:
                p.Text(new Vec(cx, y + 2.2), "C", 6, ink);
                break;
        }
    }

    public static void Lamp(ISymbolPainter p, DrawContext ctx, Vec center, double r, double brightness, uint rgb)
    {
        Paint ink = InkFor(ctx);
        if (ctx.Simulating && brightness > 0.05)
        {
            double a = Math.Clamp(brightness, 0, 1);
            p.Circle(center, r * 1.6, Paint.Color(rgb, 0.25 * a), 0, Paint.Color(rgb, 0.25 * a));
            p.Circle(center, r, ink, 1, Paint.Color(rgb, 0.35 + 0.6 * a));
        }
        else
        {
            p.Circle(center, r, ink, 1, Paint.Color(rgb, 0.12));
        }
        double d = r * 0.707;
        p.Line(center + new Vec(-d, -d), center + new Vec(d, d), ink, 0.9);
        p.Line(center + new Vec(-d, d), center + new Vec(d, -d), ink, 0.9);
    }

    /// <summary>Motor dairesi; dönerken çevresinde yön oku gezinir.</summary>
    public static void Motor(ISymbolPainter p, DrawContext ctx, Vec center, double r, string top, string bottom,
        double speed, int direction, double animTime)
    {
        Paint ink = InkFor(ctx);
        bool running = ctx.Simulating && Math.Abs(speed) > 0.02;
        p.Circle(center, r, ink, 1.1, running ? Paint.Of(Ink.Active, 0.18) : null);
        p.Text(center + new Vec(0, -r * 0.12), top, r * 0.62, ink, bold: true);
        p.Text(center + new Vec(0, r * 0.55), bottom, r * 0.42, ink);
        if (running)
        {
            double angle = animTime * Math.Abs(speed) * 360 * 0.8 * (direction < 0 ? -1 : 1);
            Paint act = Paint.Of(Ink.Active);
            Arc(p, act, center, r + 2.5, angle, 70 * (direction < 0 ? -1 : 1), 1.2);
            double endA = (angle + 70 * (direction < 0 ? -1 : 1)) * Math.PI / 180;
            var end = center + new Vec(Math.Cos(endA), Math.Sin(endA)) * (r + 2.5);
            var tangent = new Vec(-Math.Sin(endA), Math.Cos(endA)) * (direction < 0 ? -1 : 1);
            var normal = new Vec(Math.Cos(endA), Math.Sin(endA));
            p.Polyline([end + normal * 1.6 - tangent * 2, end + tangent * 1.2, end - normal * 1.6 - tangent * 2], act, 1, closed: true, fill: act);
        }
    }

    /// <summary>Çokgenle yaklaşık yay (açılar derece, 0° = +X, artış saat yönünde).</summary>
    public static void Arc(ISymbolPainter p, Paint ink, Vec center, double r, double startDeg, double sweepDeg, double width = 1)
    {
        int segments = Math.Max(6, (int)(Math.Abs(sweepDeg) / 12));
        var pts = new Vec[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            double a = (startDeg + sweepDeg * i / segments) * Math.PI / 180;
            pts[i] = center + new Vec(Math.Cos(a), Math.Sin(a)) * r;
        }
        p.Polyline(pts, ink, width);
    }

    public static void Terminal(ISymbolPainter p, DrawContext ctx, Vec at, double r = 1.6) =>
        p.Circle(at, r, InkFor(ctx), 0.9, Paint.Of(Ink.Background));

    public static uint ColorOf(string name) => name switch
    {
        "red" => 0xE53935,
        "green" => 0x43A047,
        "yellow" => 0xFDD835,
        "blue" => 0x1E88E5,
        "white" => 0xF5F5F5,
        "orange" => 0xFB8C00,
        "magenta" => 0xC2189B,
        _ => 0xFFC107
    };

    /// <summary>TS EN 60445 iletken renkleri.</summary>
    public static uint PhaseColor(string phase) => phase switch
    {
        "L1" => 0x8D5524,
        "L2" => 0x202020,
        "L3" => 0x8C8C8C,
        "N" => 0x1E64C8,
        "PE" => 0x3C9A3C,
        _ => 0xC62828
    };
}
