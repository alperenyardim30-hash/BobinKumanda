using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using KumandaLab.Core;
using KumandaLab.Core.Catalog;
using KumandaLab.Core.Drawing;
using KumandaLab.Core.Model;
using KumandaLab.Core.Simulation;

namespace KumandaLab.App.Rendering;

/// <summary>
/// Devrenin çizildiği alan. Bütün sayfa tek bir OnRender içinde çizilir (elemanlar ayrı WPF kontrolleri değildir),
/// böylece yüzlerce eleman zayıf bilgisayarda da akıcı kalır.
/// Fare: tekerlek yakınlaştırır, orta tuş ya da boş yere sol tıklayıp sürükleme kaydırır.
/// Simülasyonda butona basılı tutmak butona basmaktır.
/// </summary>
public sealed class CircuitCanvas : FrameworkElement
{
    public const double MinZoom = 0.2, MaxZoom = 12;

    private readonly WpfPainter painter = new();
    private Circuit? circuit;
    private Simulator? simulator;
    private double zoom = 2;
    private Vector pan = new(20, 20);
    private Point dragStart;
    private Vector panStart;
    private bool panning;
    private ComponentInstance? pressed;
    private string? selectedId;
    private bool fitPending;

    public CircuitCanvas()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        SizeChanged += (_, _) =>
        {
            if (fitPending)
            {
                ZoomToFit();
            }
        };
    }

    public event Action<ComponentInstance>? ComponentPressed;
    public event Action<ComponentInstance>? ComponentReleased;
    public event Action<ComponentInstance?>? SelectionChanged;
    public event Action? ViewChanged;

    public Circuit? Circuit
    {
        get => circuit;
        set
        {
            circuit = value;
            selectedId = null;
            ZoomToFit();
        }
    }

    public Simulator? Simulator
    {
        get => simulator;
        set
        {
            simulator = value;
            pressed = null;
            InvalidateVisual();
        }
    }

    public CanvasTheme Theme { get; set; } = CanvasTheme.Light;
    public bool ShowTerminalNumbers { get; set; }
    public bool ShowGrid { get; set; } = true;
    public bool ShowFlow { get; set; } = true;
    public SymbolStyle SymbolStyle { get; set; } = SymbolStyle.School;

    /// <summary>Animasyon saati (saniye); motor dönüşü, akım noktaları ve yanıp sönme bunu izler.</summary>
    public double AnimTime { get; set; }

    public double Zoom => zoom;

    public ComponentInstance? Selected => circuit?.FindComponent(selectedId);

    public void Select(ComponentInstance? c)
    {
        selectedId = c?.Id;
        SelectionChanged?.Invoke(c);
        InvalidateVisual();
    }

    public Vec ToWorld(Point screen) => new((screen.X - pan.X) / zoom, (screen.Y - pan.Y) / zoom);

    public Point ToScreen(Vec world) => new(world.X * zoom + pan.X, world.Y * zoom + pan.Y);

    public void ZoomToFit()
    {
        if (circuit == null || ActualWidth < 10 || ActualHeight < 10)
        {
            fitPending = true;
            InvalidateVisual();
            return;
        }
        fitPending = false;
        RectD box = CircuitRenderer.Bounds(circuit).Inflate(15);
        zoom = Math.Clamp(Math.Min(ActualWidth / box.Width, ActualHeight / box.Height), MinZoom, 4);
        pan = new Vector((ActualWidth - box.Width * zoom) / 2 - box.X * zoom, (ActualHeight - box.Height * zoom) / 2 - box.Y * zoom);
        ViewChanged?.Invoke();
        InvalidateVisual();
    }

    public void ZoomBy(double factor) => ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), factor);

    private void ZoomAt(Point screen, double factor)
    {
        double z = Math.Clamp(zoom * factor, MinZoom, MaxZoom);
        Vec w = ToWorld(screen);
        zoom = z;
        pan = new Vector(screen.X - w.X * zoom, screen.Y - w.Y * zoom);
        ViewChanged?.Invoke();
        InvalidateVisual();
    }

    public ComponentInstance? HitComponent(Point screen)
    {
        if (circuit == null)
        {
            return null;
        }
        Vec p = ToWorld(screen);
        double slack = 3 / zoom;
        for (int i = circuit.Components.Count - 1; i >= 0; i--)
        {
            ComponentInstance c = circuit.Components[i];
            if (c.Sheet == "s1" && ComponentCatalog.Find(c.Type) is { } def && def.Bounds(c).Inflate(slack).Contains(p))
            {
                return c;
            }
        }
        return null;
    }

    private bool IsInteractive(ComponentInstance? c) =>
        c != null && simulator?.Find(c.Id) is { IsInteractive: true };

    // ---- Çizim ----

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(painter.Brush(Theme.BackgroundColor), null, new Rect(RenderSize));
        if (circuit == null)
        {
            return;
        }
        if (ShowGrid)
        {
            DrawGrid(dc);
        }
        painter.PixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        painter.Begin(dc, Theme, zoom);
        dc.PushTransform(new MatrixTransform(zoom, 0, 0, zoom, pan.X, pan.Y));

        Simulator? sim = simulator;
        bool blinkOn = ((int)(AnimTime * 2.5) & 1) == 0;
        HashSet<string> badWires = [], badComps = [];
        if (sim != null)
        {
            foreach (SimProblem p in sim.Problems)
            {
                if (p.Severity == ProblemSeverity.Error)
                {
                    badWires.UnionWith(p.WireIds);
                    badComps.UnionWith(p.ComponentIds);
                }
            }
        }
        CircuitRenderer.Draw(circuit, painter, new CircuitRenderOptions
        {
            Style = SymbolStyle,
            ShowTerminalNumbers = ShowTerminalNumbers,
            StateOf = sim == null ? null : c => sim.Find(c.Id)?.Visual,
            WireInk = badWires.Count == 0 ? null : w => blinkOn && badWires.Contains(w.Id) ? Paint.Of(Ink.Fault) : null,
            AnimTime = AnimTime
        });

        if (sim != null && ShowFlow)
        {
            DrawFlow(dc, sim);
        }
        if (blinkOn)
        {
            foreach (string id in badComps)
            {
                if (circuit.FindComponent(id) is { } c && ComponentCatalog.Find(c.Type) is { } def)
                {
                    RectD r = def.Bounds(c).Inflate(4);
                    painter.Rect(r.TopLeft, new Vec(r.Width, r.Height), Paint.Of(Ink.Fault), 1.4, Paint.Of(Ink.Fault, 0.12));
                }
            }
        }
        if (Selected is { } s && ComponentCatalog.Find(s.Type) is { } sd)
        {
            RectD r = sd.Bounds(s).Inflate(3);
            painter.Rect(r.TopLeft, new Vec(r.Width, r.Height), Paint.Of(Ink.Accent), 0.8, Paint.Of(Ink.Accent, 0.08), dashed: true);
        }
        dc.Pop();
    }

    private void DrawGrid(DrawingContext dc)
    {
        double step = 10 * zoom;
        while (step < 9)
        {
            step *= 5;
        }
        double r = Math.Clamp(zoom * 0.45, 0.6, 1.3);
        var dot = new GeometryDrawing(painter.Brush(Theme.GridColor), null, new EllipseGeometry(new Point(0, 0), r, r));
        var brush = new DrawingBrush(new DrawingGroup { Children = { new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, step, step))), dot } })
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, step, step),
            Viewport = new Rect(Mod(pan.X, step), Mod(pan.Y, step), step, step),
            Stretch = Stretch.None
        };
        brush.Freeze();
        dc.DrawRectangle(brush, null, new Rect(RenderSize));
    }

    private static double Mod(double a, double m) => ((a % m) + m) % m;

    /// <summary>Akım geçen kablolarda akış yönünde ilerleyen noktalar.</summary>
    private void DrawFlow(DrawingContext dc, Simulator sim)
    {
        const double spacing = 14, speed = 24;
        SolidColorBrush brush = painter.Brush(Theme.FlowColor);
        double radius = Math.Max(1.5, 1.6 / zoom);
        foreach (Wire w in circuit!.Wires)
        {
            if (w.Sheet != "s1" || !sim.WireFlows.TryGetValue(w.Id, out WireFlow f) || f.Amps < 0.005 || f.Direction == 0)
            {
                continue;
            }
            double len = GeometryUtil.PolylineLength(w.Points);
            if (len < 4)
            {
                continue;
            }
            double phase = Mod(AnimTime * speed, spacing);
            for (double d = phase; d < len; d += spacing)
            {
                Vec p = GeometryUtil.PointAlong(w.Points, f.Direction > 0 ? d : len - d);
                dc.DrawEllipse(brush, null, new Point(p.X, p.Y), radius, radius);
            }
        }
    }

    // ---- Fare ----

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        Point pos = e.GetPosition(this);
        if (e.ChangedButton == MouseButton.Middle)
        {
            StartPan(pos);
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }
        ComponentInstance? hit = HitComponent(pos);
        if (hit?.Id != selectedId)
        {
            Select(hit);
        }
        if (IsInteractive(hit))
        {
            pressed = hit;
            CaptureMouse();
            ComponentPressed?.Invoke(hit!);
        }
        else
        {
            StartPan(pos);
        }
        e.Handled = true;
    }

    private void StartPan(Point pos)
    {
        panning = true;
        dragStart = pos;
        panStart = pan;
        CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Point pos = e.GetPosition(this);
        if (panning)
        {
            pan = panStart + (pos - dragStart);
            ViewChanged?.Invoke();
            InvalidateVisual();
            return;
        }
        Cursor = IsInteractive(HitComponent(pos)) ? Cursors.Hand : null;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (pressed != null && e.ChangedButton == MouseButton.Left)
        {
            ComponentInstance c = pressed;
            pressed = null;
            ComponentReleased?.Invoke(c);
        }
        if (panning && e.ChangedButton is MouseButton.Left or MouseButton.Middle)
        {
            panning = false;
            Cursor = null;
        }
        if (pressed == null && !panning)
        {
            ReleaseMouseCapture();
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        // Pencere odağı kaybolursa basılı buton bırakılmış sayılır
        if (pressed != null)
        {
            ComponentInstance c = pressed;
            pressed = null;
            ComponentReleased?.Invoke(c);
        }
        panning = false;
        Cursor = null;
    }
}
