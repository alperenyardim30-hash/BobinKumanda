using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BobinKumanda.Core;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Editing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.App.Rendering;

public enum EditTool { Select, Wire, Text }

/// <summary>Çizim alanının düzenleme sırasında haber verdiği taraf (ana pencerenin görünüm modeli).</summary>
public interface IEditorHost
{
    bool IsEditing { get; }
    EditTool Tool { get; set; }

    /// <summary>Devre değişmeden hemen önce (geri alma noktası).</summary>
    void BeginEdit(string? coalesceKey = null);

    /// <summary>Değişiklik bitti.</summary>
    void CommitEdit();

    /// <summary>Başlanan işlem hiçbir şeyi değiştirmeden bitti; geri alma noktası atılır.</summary>
    void CancelEdit();

    void SelectionChanged();

    /// <summary>Durum çubuğundaki ipucu; null varsayılan ipucuna döner.</summary>
    void SetHint(string? key);
}

/// <summary>
/// Devrenin çizildiği ve düzenlendiği alan. Bütün sayfa tek bir OnRender içinde çizilir (elemanlar ayrı WPF kontrolleri
/// değildir), böylece yüzlerce eleman zayıf bilgisayarda da akıcı kalır.
/// Düzenlemede: tıkla seç, sürükle taşı, boş yerden sürükle çoklu seç, klemensten başlayıp tıklayarak kablo çiz,
/// paletten sürükleyip bırak. Tekerlek yakınlaştırır; orta tuş ya da Boşluk + sürükleme kaydırır.
/// Simülasyonda butona basılı tutmak butona basmaktır, boş yeri sürüklemek kaydırır.
/// </summary>
public sealed class CircuitCanvas : FrameworkElement
{
    public const double MinZoom = 0.2, MaxZoom = 12;

    /// <summary>Paletten sürüklenen eleman tipinin veri biçimi.</summary>
    public const string DragFormat = "BobinKumanda.ComponentType";

    private const double DragThreshold = 4;

    private enum Gesture { None, Pending, Pan, Rubber, Move, Segment, Label, Press }

    private readonly WpfPainter painter = new();
    private Circuit? circuit;
    private Simulator? simulator;
    private double zoom = 2;
    private Vector pan = new(20, 20);
    private bool fitPending;
    private IReadOnlyDictionary<string, uint>? wireColors;

    private Gesture gesture;
    private Point downScreen;
    private Vec downWorld;
    private Hit downHit = Hit.None;
    private bool downCtrl;
    private Vector panStart;
    private MoveSession? move;
    private Vec moveAnchor;
    private string? segWire;
    private int segIndex;
    private List<Vec>? segOrig;
    private ComponentInstance? labelOf;
    private Vec? labelOrig;
    private ComponentInstance? pressed;
    private Vec mouseWorld;
    private bool mouseInside;
    private Hit hover = Hit.None;

    private WireDraft? draft;

    private string? placeType;
    private int placeRotation;
    private bool placeMirror;
    private bool placeByDrag;

    /// <summary>Çizilmekte olan kablo: başlangıcı ve şimdiye kadar sabitlenmiş köşeleri.</summary>
    private sealed class WireDraft
    {
        public required Hit Start { get; init; }
        public required Side StartSide { get; init; }
        public List<Vec> Points { get; } = [];
    }

    public CircuitCanvas()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        AllowDrop = true;
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
    public event Action? ViewChanged;

    /// <summary>Sağ tıklama; seçim önceden güncellenmiştir. Argüman tıklanan öğe (boşsa Hit.None).</summary>
    public event Action<Hit>? ContextMenuRequested;

    public IEditorHost? Host { get; set; }

    /// <summary>Görünüm modeliyle paylaşılan seçim.</summary>
    public Selection Selection { get; set; } = new();

    public Circuit? Circuit
    {
        get => circuit;
        set
        {
            ResetGestures();
            circuit = value;
            wireColors = null;
            ZoomToFit();
        }
    }

    /// <summary>Geri alma gibi durumlarda devreyi görünümü koruyarak değiştirir.</summary>
    public void ReplaceCircuit(Circuit c)
    {
        ResetGestures();
        circuit = c;
        wireColors = null;
        InvalidateVisual();
    }

    public string Sheet { get; set; } = "s1";

    public Simulator? Simulator
    {
        get => simulator;
        set
        {
            simulator = value;
            pressed = null;
            ResetGestures();
            InvalidateVisual();
        }
    }

    public CanvasTheme Theme { get; set; } = CanvasTheme.Light;
    public bool ShowTerminalNumbers { get; set; }
    public bool ShowGrid { get; set; } = true;
    public bool ShowFlow { get; set; } = true;
    public SymbolStyle SymbolStyle { get; set; } = SymbolStyle.School;

    /// <summary>Boşluk tuşu basılıyken sol tuşla sürükleme kaydırır.</summary>
    public bool SpacePan { get; set; }

    /// <summary>Animasyon saati (saniye); motor dönüşü, akım noktaları ve yanıp sönme bunu izler.</summary>
    public double AnimTime { get; set; }

    public double Zoom => zoom;

    private bool Editing => Host?.IsEditing ?? simulator == null;

    /// <summary>Kablo çizimi, yerleştirme ya da sürükleme sürüyor mu (Esc'nin ne yapacağını belirler).</summary>
    public bool IsBusy => draft != null || placeType != null || gesture != Gesture.None;

    public string? PlacingType => placeType;

    /// <summary>Farenin dünya koordinatı; fare alanın dışındaysa null (yapıştırma yeri için).</summary>
    public Vec? MouseWorld => mouseInside ? mouseWorld : null;

    /// <summary>Kablo renkleri gibi devreden türeyen önbellekleri atar ve yeniden çizer.</summary>
    public void InvalidateCircuit()
    {
        wireColors = null;
        InvalidateVisual();
    }

    public void Select(ComponentInstance? c)
    {
        Selection.Clear();
        if (c != null)
        {
            Selection.Components.Add(c.Id);
        }
        Host?.SelectionChanged();
        InvalidateVisual();
    }

    public Vec ToWorld(Point screen) => new((screen.X - pan.X) / zoom, (screen.Y - pan.Y) / zoom);

    public Point ToScreen(Vec world) => new(world.X * zoom + pan.X, world.Y * zoom + pan.Y);

    /// <summary>Görünen alanın ortası (dünya koordinatı); yapıştırma ve klavyeyle ekleme için.</summary>
    public Vec ViewCenter => ToWorld(new Point(ActualWidth / 2, ActualHeight / 2));

    public void ZoomToFit()
    {
        if (circuit == null || ActualWidth < 10 || ActualHeight < 10)
        {
            fitPending = true;
            InvalidateVisual();
            return;
        }
        fitPending = false;
        RectD box = CircuitRenderer.Bounds(circuit, Sheet).Inflate(15);
        if (!circuit.Components.Any(c => c.Sheet == Sheet) && !circuit.Wires.Any(w => w.Sheet == Sheet))
        {
            // Boş sayfa: makul bir başlangıç yakınlığı
            box = new RectD(0, 0, 400, 260);
        }
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

    // ---- Araçlar ----

    /// <summary>Paletten ya da kısayoldan eleman yerleştirmeye başlar; eleman fareyle birlikte gezer, tıklayınca yerleşir.</summary>
    public void StartPlacing(string type)
    {
        if (!Editing || circuit == null || ComponentCatalog.Find(type) == null)
        {
            return;
        }
        CancelWire();
        placeType = type;
        placeRotation = 0;
        placeMirror = false;
        placeByDrag = false;
        if (!mouseInside)
        {
            // Klavyeden ya da şeritten başlatıldı: eleman ekranın ortasında görünsün
            mouseWorld = ViewCenter;
        }
        Host?.SetHint("ui.hint.place");
        Focus();
        InvalidateVisual();
    }

    /// <summary>Yerleştirilen elemanı döndürür; yerleştirme yoksa false.</summary>
    public bool RotateGhost()
    {
        if (placeType == null)
        {
            return false;
        }
        placeRotation = (placeRotation + 1) & 3;
        InvalidateVisual();
        return true;
    }

    public bool MirrorGhost()
    {
        if (placeType == null)
        {
            return false;
        }
        placeMirror = !placeMirror;
        InvalidateVisual();
        return true;
    }

    /// <summary>Esc: süren işi bırakır. Bir şey bırakıldıysa true.</summary>
    public bool CancelCurrent()
    {
        bool busy = IsBusy;
        if (gesture == Gesture.Move)
        {
            move?.Cancel();
            Host?.CancelEdit();
        }
        else if (gesture == Gesture.Segment && segOrig != null && circuit?.FindWire(segWire) is { } w)
        {
            w.Points = segOrig;
            Host?.CancelEdit();
        }
        else if (gesture == Gesture.Label && labelOf != null)
        {
            labelOf.LabelOffset = labelOrig;
            labelOf = null;
            Host?.CancelEdit();
        }
        gesture = Gesture.None;
        move = null;
        segOrig = null;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
        CancelWire();
        placeType = null;
        Host?.SetHint(null);
        InvalidateVisual();
        return busy;
    }

    private void CancelWire()
    {
        if (draft != null)
        {
            draft = null;
            Host?.SetHint(null);
        }
    }

    private void ResetGestures()
    {
        if (gesture == Gesture.Move)
        {
            move?.Cancel();
        }
        gesture = Gesture.None;
        move = null;
        segOrig = null;
        draft = null;
        placeType = null;
        hover = Hit.None;
    }

    // ---- Vuruş testi ----

    private double TerminalTolerance => Math.Min(7 / zoom, 4);
    private double WireTolerance => Math.Min(5 / zoom, 3.5);

    /// <summary>Seç aracında fare altındaki öğe: klemens, ek noktası, metin, eleman, kablo sırasıyla.</summary>
    private Hit HitAt(Vec p, bool terminals = true)
    {
        if (circuit == null)
        {
            return Hit.None;
        }
        if (terminals && Editing)
        {
            Hit t = HitTest.Terminal(circuit, Sheet, p, TerminalTolerance);
            if (!t.IsNone)
            {
                return t;
            }
        }
        Hit h = HitTest.Junction(circuit, Sheet, p, TerminalTolerance);
        if (h.IsNone)
        {
            h = HitTest.Text(circuit, Sheet, p, 1 / zoom);
        }
        if (h.IsNone && Editing)
        {
            h = HitTest.Label(circuit, Sheet, p, 0.5 / zoom);
        }
        if (h.IsNone)
        {
            h = HitTest.Component(circuit, Sheet, p, 1.5 / zoom);
        }
        if (h.IsNone)
        {
            h = HitTest.Wire(circuit, Sheet, p, WireTolerance);
        }
        return h;
    }

    /// <summary>Kablonun bağlanabileceği hedef: klemens, ek noktası ya da başka bir kablo.</summary>
    private Hit WireTarget(Vec p)
    {
        if (circuit == null)
        {
            return Hit.None;
        }
        Hit h = HitTest.Terminal(circuit, Sheet, p, TerminalTolerance);
        if (h.IsNone)
        {
            h = HitTest.Junction(circuit, Sheet, p, TerminalTolerance);
        }
        if (h.IsNone)
        {
            h = HitTest.Wire(circuit, Sheet, p, WireTolerance);
        }
        return h;
    }

    public ComponentInstance? HitComponent(Point screen) =>
        circuit != null && HitTest.Component(circuit, Sheet, ToWorld(screen), 3 / zoom) is { IsNone: false } h ? circuit.FindComponent(h.Id) : null;

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
        wireColors ??= CircuitRenderer.WireColors(circuit);
        CircuitRenderer.Draw(circuit, painter, new CircuitRenderOptions
        {
            Sheet = Sheet,
            Style = SymbolStyle,
            ShowTerminalNumbers = ShowTerminalNumbers,
            StateOf = sim == null ? null : c => sim.Find(c.Id)?.Visual,
            WireInk = badWires.Count == 0 ? null : w => blinkOn && badWires.Contains(w.Id) ? Paint.Of(Ink.Fault) : null,
            WireColorCache = wireColors,
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
                if (circuit.FindComponent(id) is { } c && c.Sheet == Sheet && ComponentCatalog.Find(c.Type) is { } def)
                {
                    RectD r = def.Bounds(c).Inflate(4);
                    painter.Rect(r.TopLeft, new Vec(r.Width, r.Height), Paint.Of(Ink.Fault), 1.4, Paint.Of(Ink.Fault, 0.12));
                }
            }
        }
        DrawSelection();
        if (Editing)
        {
            DrawEditOverlay(dc);
        }
        dc.Pop();
    }

    private void DrawSelection()
    {
        // Seçili kablonun kendi rengi (faz rengi) görünmeye devam etsin diye vurgu yarı saydam ve kalın
        foreach (Wire w in circuit!.Wires)
        {
            if (w.Sheet == Sheet && Selection.Wires.Contains(w.Id) && w.Points.Count >= 2)
            {
                painter.Polyline(w.Points, Paint.Of(Ink.Accent, 0.35), Math.Max(4, 7 / zoom));
            }
        }
        foreach (ComponentInstance c in circuit.Components)
        {
            if (c.Sheet == Sheet && Selection.Components.Contains(c.Id) && ComponentCatalog.Find(c.Type) is { } def)
            {
                RectD r = def.Bounds(c).Inflate(3);
                painter.Rect(r.TopLeft, new Vec(r.Width, r.Height), Paint.Of(Ink.Accent), 0.8, Paint.Of(Ink.Accent, 0.08), dashed: true);
            }
        }
        foreach (Junction j in circuit.Junctions)
        {
            if (j.Sheet == Sheet && Selection.Junctions.Contains(j.Id))
            {
                painter.Circle(j.Position, 4, Paint.Of(Ink.Accent), 1);
            }
        }
        foreach (TextNote t in circuit.Texts)
        {
            if (t.Sheet == Sheet && Selection.Texts.Contains(t.Id))
            {
                RectD r = HitTest.TextBounds(t).Inflate(2);
                painter.Rect(r.TopLeft, new Vec(r.Width, r.Height), Paint.Of(Ink.Accent), 0.8, Paint.Of(Ink.Accent, 0.08), dashed: true);
            }
        }
    }

    private void DrawEditOverlay(DrawingContext dc)
    {
        Circuit c = circuit!;
        double mark = Math.Max(1.6, 2.6 / zoom);

        // Boştaki kablo uçları: öğrenci bağlanmamış ucu görsün
        foreach (Wire w in c.Wires)
        {
            if (w.Sheet != Sheet || w.Points.Count < 2)
            {
                continue;
            }
            if (w.A.IsFree)
            {
                painter.Circle(w.Points[0], mark, Paint.Of(Ink.Fault), 0.7, Paint.Of(Ink.Background));
            }
            if (w.B.IsFree)
            {
                painter.Circle(w.Points[^1], mark, Paint.Of(Ink.Fault), 0.7, Paint.Of(Ink.Background));
            }
        }

        // Fare altındaki elemanın klemensleri
        Hit target = draft != null ? WireTarget(mouseWorld) : Hit.None;
        string? hoverComp = hover.Kind is HitKind.Component or HitKind.Terminal ? hover.Id : null;
        if (target.Kind == HitKind.Terminal)
        {
            hoverComp = target.Id;
        }
        if (hoverComp != null && c.FindComponent(hoverComp) is { } hc && ComponentCatalog.Find(hc.Type) is { } hdef)
        {
            foreach (TerminalDef t in hdef.Terminals)
            {
                painter.Circle(hdef.TerminalWorld(hc, t), mark * 0.75, Paint.Of(Ink.Accent), 0.6, Paint.Of(Ink.Background));
            }
        }
        if (draft == null && hover.Kind == HitKind.Terminal)
        {
            painter.Circle(hover.Point, mark * 1.5, Paint.Of(Ink.Accent), 1, Paint.Of(Ink.Accent, 0.25));
        }

        if (draft != null)
        {
            List<Vec> preview = PreviewLeg(target);
            var all = new List<Vec>(draft.Points);
            all.AddRange(preview.Skip(1));
            painter.Polyline(all, Paint.Of(Ink.Accent), CircuitRenderer.WireWidth);
            painter.Circle(draft.Points[0], mark, Paint.Of(Ink.Accent), 0.8, Paint.Of(Ink.Accent));
            if (!target.IsNone)
            {
                painter.Circle(target.Point, mark * 1.5, Paint.Of(Ink.Accent), 1, Paint.Of(Ink.Accent, 0.25));
            }
        }

        if (placeType != null && (mouseInside || !placeByDrag) && GhostAt(mouseWorld) is { } ghost)
        {
            var ghostCircuit = new Circuit { Sheets = [new Sheet { Id = Sheet }] };
            ghostCircuit.Components.Add(ghost);
            dc.PushOpacity(0.55);
            CircuitRenderer.Draw(ghostCircuit, painter, new CircuitRenderOptions
            {
                Sheet = Sheet,
                Style = SymbolStyle,
                ShowTerminalNumbers = ShowTerminalNumbers,
                ColorWires = false
            });
            dc.Pop();
        }

        if (gesture == Gesture.Rubber)
        {
            RectD r = RectD.FromPoints(downWorld, mouseWorld);
            painter.Rect(r.TopLeft, new Vec(r.Width, r.Height), Paint.Of(Ink.Accent), 0.8, Paint.Of(Ink.Accent, 0.07), dashed: true);
        }
    }

    /// <summary>Yerleştirilecek elemanın fareye göre konumu: eleman imlecin ortasında, ızgaraya oturmuş.</summary>
    private ComponentInstance? GhostAt(Vec p)
    {
        if (placeType == null || circuit == null || ComponentCatalog.Find(placeType) is not { } def)
        {
            return null;
        }
        var g = new ComponentInstance
        {
            Id = "ghost",
            Type = placeType,
            Sheet = Sheet,
            Rotation = placeRotation,
            Mirror = placeMirror,
            Label = def.Role == DeviceRole.Follower ? CircuitEditor.DefaultFollowerLabel(circuit, def) : Labeler.Next(circuit, def)
        };
        def.ApplyDefaults(g);
        g.Position = CircuitEditor.Snap(p - def.WorldSize(g) / 2);
        return g;
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
            if (w.Sheet != Sheet || !sim.WireFlows.TryGetValue(w.Id, out WireFlow f) || f.Amps < 0.005 || f.Direction == 0)
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

    // ---- Kablo çizimi ----

    private void BeginWire(Hit start)
    {
        if (circuit == null)
        {
            return;
        }
        Side side = start.Kind == HitKind.Terminal && circuit.FindComponent(start.Id) is { } c && ComponentCatalog.Find(c.Type) is { } def &&
                    def.FindTerminal(start.Terminal!) is { } t ? def.TerminalSide(c, t) : Side.None;
        draft = new WireDraft { Start = start, StartSide = side };
        draft.Points.Add(start.Point);
        if (!Selection.IsEmpty)
        {
            Selection.Clear();
            Host?.SelectionChanged();
        }
        Host?.SetHint("ui.hint.wire");
    }

    /// <summary>Son sabit köşeden fareye (ya da hedefe) önerilen dik açılı parça.</summary>
    private List<Vec> PreviewLeg(Hit target)
    {
        WireDraft d = draft!;
        Vec from = d.Points[^1];
        // Ara köşe kullanıcının dönmek istediği yerdir: oradan sonra yön dayatılmaz
        Side fromSide = d.Points.Count == 1 ? d.StartSide : Side.None;
        Vec to = target.IsNone ? mouseWorld.Snap(5) : target.Point;
        Side toSide = Side.None;
        if (target.Kind == HitKind.Terminal && circuit!.FindComponent(target.Id) is { } c && ComponentCatalog.Find(c.Type) is { } def &&
            def.FindTerminal(target.Terminal!) is { } t)
        {
            toSide = def.TerminalSide(c, t);
        }
        return CircuitEditor.RouteBetween(circuit!, Sheet, from, fromSide, to, toSide);
    }

    private void WireClick(bool finishHere)
    {
        if (draft == null || circuit == null)
        {
            return;
        }
        Hit target = WireTarget(mouseWorld);
        if (target.Kind == HitKind.Terminal && draft.Start.Kind == HitKind.Terminal &&
            target.Id == draft.Start.Id && target.Terminal == draft.Start.Terminal && draft.Points.Count == 1)
        {
            return;
        }
        List<Vec> leg = PreviewLeg(target);
        draft.Points.AddRange(leg.Skip(1));
        if (target.IsNone && !finishHere)
        {
            // Ara köşe: çizim sürer
            List<Vec> simplified = WirePath.Simplify(draft.Points);
            draft.Points.Clear();
            draft.Points.AddRange(simplified);
            InvalidateVisual();
            return;
        }
        FinishWire(target);
    }

    private void FinishWire(Hit target)
    {
        WireDraft d = draft!;
        Circuit c = circuit!;
        draft = null;
        List<Vec> pts = WirePath.Simplify(d.Points);
        if (pts.Count == 2 && pts[0].NearlyEquals(pts[1]))
        {
            Host?.SetHint(null);
            InvalidateVisual();
            return;
        }
        Host?.BeginEdit();
        WireEnd a = CircuitEditor.ResolveEnd(c, Sheet, d.Start);
        // Başlangıç bir kabloyu böldüyse hedefin parça numarası eskimiş olabilir; hedef yeniden bulunur
        if (target.Kind == HitKind.Wire)
        {
            target = HitTest.Wire(c, Sheet, target.Point, 0.01);
        }
        WireEnd b = target.IsNone ? WireEnd.Free() : CircuitEditor.ResolveEnd(c, Sheet, target);
        Wire w = CircuitEditor.AddWire(c, Sheet, a, b, pts);
        Selection.Clear();
        Selection.Wires.Add(w.Id);
        wireColors = null;
        Host?.CommitEdit();
        Host?.SelectionChanged();
        Host?.SetHint(null);
        InvalidateVisual();
    }

    // ---- Fare ----

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }

    protected override void OnMouseEnter(MouseEventArgs e) => mouseInside = true;

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        mouseInside = false;
        if (hover.Kind != HitKind.None || placeType != null)
        {
            hover = Hit.None;
            InvalidateVisual();
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        e.Handled = HandleDown(e.GetPosition(this), e.ChangedButton, e.ClickCount);
    }

    /// <summary>Betikle deneme sırasında klavye yerine bu tuşlar geçerlidir.</summary>
    private ModifierKeys? simulatedModifiers;

    private ModifierKeys Modifiers => simulatedModifiers ?? Keyboard.Modifiers;

    /// <summary>
    /// Fare olayını dünya koordinatında taklit eder (betikle arayüz denemesi): "down", "move", "up" ya da "click".
    /// Gerçek fare olaylarıyla aynı yoldan geçer.
    /// </summary>
    public void SimulateMouse(string kind, Vec world, ModifierKeys modifiers = ModifierKeys.None, MouseButton button = MouseButton.Left, int clicks = 1)
    {
        simulatedModifiers = modifiers;
        try
        {
            Point pos = ToScreen(world);
            switch (kind)
            {
                case "down":
                    HandleDown(pos, button, clicks);
                    break;
                case "move":
                    HandleMove(pos);
                    break;
                case "up":
                    HandleUp(pos, button);
                    break;
                case "click":
                    HandleMove(pos);
                    HandleDown(pos, button, clicks);
                    HandleUp(pos, button);
                    break;
            }
        }
        finally
        {
            simulatedModifiers = null;
        }
    }

    private bool HandleDown(Point pos, MouseButton button, int clickCount)
    {
        mouseWorld = ToWorld(pos);
        mouseInside = true;
        if (circuit == null)
        {
            return false;
        }
        if (button == MouseButton.Middle || (button == MouseButton.Left && SpacePan))
        {
            StartPan(pos);
            return true;
        }
        if (button != MouseButton.Left)
        {
            return button == MouseButton.Right;
        }
        if (!Editing)
        {
            SimulationDown(pos);
            return true;
        }
        if (placeType != null)
        {
            PlaceGhost(keepPlacing: Modifiers.HasFlag(ModifierKeys.Shift) || Modifiers.HasFlag(ModifierKeys.Control));
            return true;
        }
        if (draft != null)
        {
            WireClick(finishHere: clickCount >= 2);
            return true;
        }
        EditTool tool = Host?.Tool ?? EditTool.Select;
        if (tool == EditTool.Text && Host != null)
        {
            Host.BeginEdit();
            TextNote t = CircuitEditor.AddText(circuit, Sheet, CircuitEditor.Snap(mouseWorld), Loc.T("ui.newText"));
            Selection.Clear();
            Selection.Texts.Add(t.Id);
            Host.CommitEdit();
            Host.Tool = EditTool.Select;
            Host.SelectionChanged();
            InvalidateVisual();
            return true;
        }
        if (tool == EditTool.Wire)
        {
            Hit start = WireTarget(mouseWorld);
            BeginWire(start.IsNone ? new Hit(HitKind.None, Point: mouseWorld.Snap(5)) : start);
            InvalidateVisual();
            return true;
        }

        Hit hit = HitAt(mouseWorld);
        if (hit.Kind == HitKind.Terminal)
        {
            BeginWire(hit);
            InvalidateVisual();
            return true;
        }
        downScreen = pos;
        downWorld = mouseWorld;
        downHit = hit;
        downCtrl = Modifiers.HasFlag(ModifierKeys.Control);
        gesture = Gesture.Pending;
        CaptureMouse();
        if (!hit.IsNone && !IsSelected(hit))
        {
            if (!downCtrl)
            {
                Selection.Clear();
            }
            AddToSelection(hit);
            Host?.SelectionChanged();
        }
        else if (hit.IsNone && !downCtrl && !Selection.IsEmpty)
        {
            Selection.Clear();
            Host?.SelectionChanged();
        }
        InvalidateVisual();
        return true;
    }

    private void SimulationDown(Point pos)
    {
        ComponentInstance? hit = HitComponent(pos);
        Selection.Clear();
        if (hit != null)
        {
            Selection.Components.Add(hit.Id);
        }
        Host?.SelectionChanged();
        if (IsInteractive(hit))
        {
            pressed = hit;
            gesture = Gesture.Press;
            CaptureMouse();
            ComponentPressed?.Invoke(hit!);
        }
        else
        {
            StartPan(pos);
        }
        InvalidateVisual();
    }

    private bool IsSelected(Hit h) => h.Kind switch
    {
        HitKind.Component or HitKind.Label => Selection.Components.Contains(h.Id),
        HitKind.Wire => Selection.Wires.Contains(h.Id),
        HitKind.Junction => Selection.Junctions.Contains(h.Id),
        HitKind.Text => Selection.Texts.Contains(h.Id),
        _ => false
    };

    private HashSet<string>? SetOf(Hit h) => h.Kind switch
    {
        HitKind.Component or HitKind.Label => Selection.Components,
        HitKind.Wire => Selection.Wires,
        HitKind.Junction => Selection.Junctions,
        HitKind.Text => Selection.Texts,
        _ => null
    };

    private void AddToSelection(Hit h) => SetOf(h)?.Add(h.Id);

    private void RemoveFromSelection(Hit h) => SetOf(h)?.Remove(h.Id);

    private void StartPan(Point pos)
    {
        gesture = Gesture.Pan;
        downScreen = pos;
        panStart = pan;
        CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e) => HandleMove(e.GetPosition(this));

    private void HandleMove(Point pos)
    {
        mouseWorld = ToWorld(pos);
        mouseInside = true;
        switch (gesture)
        {
            case Gesture.Pan:
                pan = panStart + (pos - downScreen);
                ViewChanged?.Invoke();
                InvalidateVisual();
                return;
            case Gesture.Pending when (pos - downScreen).Length >= DragThreshold:
                StartDrag();
                break;
            case Gesture.Press:
                return;
        }
        switch (gesture)
        {
            case Gesture.Move when move != null:
                move.Update(CircuitEditor.Snap(moveAnchor + (mouseWorld - downWorld)) - moveAnchor);
                InvalidateVisual();
                return;
            case Gesture.Segment when segOrig != null && circuit?.FindWire(segWire) is { } w:
                w.Points = WirePath.DragSegment(segOrig, segIndex, (mouseWorld - downWorld).Snap(5));
                InvalidateVisual();
                return;
            case Gesture.Label when labelOf != null:
                labelOf.LabelOffset = (moveAnchor + (mouseWorld - downWorld)).Snap(1);
                InvalidateVisual();
                return;
            case Gesture.Rubber:
            case Gesture.Pending:
                InvalidateVisual();
                return;
        }

        if (draft != null || placeType != null)
        {
            InvalidateVisual();
            Cursor = Cursors.Cross;
            return;
        }
        Hit h = Editing && circuit != null ? HitAt(mouseWorld) : Hit.None;
        if (h != hover)
        {
            hover = h;
            InvalidateVisual();
        }
        Cursor = !Editing ? (IsInteractive(HitComponent(pos)) ? Cursors.Hand : null) :
            h.Kind == HitKind.Terminal || Host?.Tool == EditTool.Wire ? Cursors.Cross :
            Host?.Tool == EditTool.Text ? Cursors.IBeam :
            h.IsNone ? null : Cursors.SizeAll;
    }

    private void StartDrag()
    {
        if (circuit == null)
        {
            return;
        }
        if (downHit.IsNone)
        {
            gesture = Gesture.Rubber;
            return;
        }
        // Etiket tutulup sürüklenirse yalnızca etiket kayar (üst üste binen yazıları ayırmak için)
        if (downHit.Kind == HitKind.Label && circuit.FindComponent(downHit.Id) is { } lc && ComponentCatalog.Find(lc.Type) is { } ldef)
        {
            Host?.BeginEdit();
            gesture = Gesture.Label;
            labelOf = lc;
            labelOrig = lc.LabelOffset;
            moveAnchor = ldef.LabelWorld(lc) - lc.Position;
            return;
        }
        // Tek başına seçili bir kablonun parçası sürüklenirse köşe düzeltilir
        if (downHit.Kind == HitKind.Wire && Selection.Count == 1 && Selection.Wires.Contains(downHit.Id) &&
            circuit.FindWire(downHit.Id) is { } w)
        {
            Host?.BeginEdit();
            gesture = Gesture.Segment;
            segWire = w.Id;
            segOrig = WirePath.Simplify(w.Points);
            w.Points = [.. segOrig];
            segIndex = Math.Max(0, WirePath.Nearest(segOrig, downWorld).Segment);
            return;
        }
        Host?.BeginEdit();
        gesture = Gesture.Move;
        move = new MoveSession(circuit, Selection);
        moveAnchor = AnchorOf(Selection);
    }

    /// <summary>Taşınan seçimin ızgaraya oturtulacak noktası: ilk elemanın konumu.</summary>
    private Vec AnchorOf(Selection s)
    {
        if (circuit!.Components.FirstOrDefault(c => s.Components.Contains(c.Id)) is { } c)
        {
            return c.Position;
        }
        if (circuit.Junctions.FirstOrDefault(j => s.Junctions.Contains(j.Id)) is { } j)
        {
            return j.Position;
        }
        if (circuit.Texts.FirstOrDefault(t => s.Texts.Contains(t.Id)) is { } t)
        {
            return t.Position;
        }
        return downWorld.Snap(CircuitEditor.Grid);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e) => e.Handled = HandleUp(e.GetPosition(this), e.ChangedButton);

    private bool HandleUp(Point pos, MouseButton button)
    {
        mouseWorld = ToWorld(pos);
        if (button == MouseButton.Right)
        {
            RightClick();
            return true;
        }
        if (gesture == Gesture.Press && pressed != null && button == MouseButton.Left)
        {
            ComponentInstance c = pressed;
            pressed = null;
            gesture = Gesture.None;
            ReleaseMouseCapture();
            ComponentReleased?.Invoke(c);
            return true;
        }
        if (gesture == Gesture.Pan && button is MouseButton.Left or MouseButton.Middle)
        {
            gesture = Gesture.None;
            Cursor = null;
            ReleaseMouseCapture();
            return true;
        }
        if (button != MouseButton.Left)
        {
            return false;
        }
        Gesture ended = gesture;
        gesture = Gesture.None;
        switch (ended)
        {
            case Gesture.Pending:
                // Sürüklemeden bırakılan tık: çoklu seçimde tıklanan öğe tek başına seçilir, Ctrl ile çıkarılır
                if (!downHit.IsNone)
                {
                    if (downCtrl && IsSelected(downHit) && Selection.Count > 1)
                    {
                        RemoveFromSelection(downHit);
                        Host?.SelectionChanged();
                    }
                    else if (!downCtrl && Selection.Count > 1)
                    {
                        Selection.Clear();
                        AddToSelection(downHit);
                        Host?.SelectionChanged();
                    }
                }
                break;
            case Gesture.Rubber:
                Selection picked = HitTest.InRect(circuit!, Sheet, RectD.FromPoints(downWorld, mouseWorld));
                if (!downCtrl)
                {
                    Selection.Clear();
                }
                Selection.UnionWith(picked);
                Host?.SelectionChanged();
                break;
            case Gesture.Move when move != null:
                if (move.Delta == Vec.Zero)
                {
                    move.Cancel();
                    Host?.CancelEdit();
                }
                else
                {
                    move.Commit();
                    wireColors = null;
                    Host?.CommitEdit();
                }
                move = null;
                break;
            case Gesture.Segment when circuit?.FindWire(segWire) is { } w:
                w.Points = WirePath.Simplify(w.Points);
                if (segOrig != null && w.Points.SequenceEqual(segOrig))
                {
                    Host?.CancelEdit();
                }
                else
                {
                    Host?.CommitEdit();
                }
                segOrig = null;
                break;
            case Gesture.Label when labelOf != null:
                if (labelOf.LabelOffset == labelOrig)
                {
                    Host?.CancelEdit();
                }
                else
                {
                    Host?.CommitEdit();
                }
                labelOf = null;
                break;
        }
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
        InvalidateVisual();
        return true;
    }

    private void RightClick()
    {
        if (draft != null || placeType != null)
        {
            CancelCurrent();
            return;
        }
        if (circuit == null || gesture != Gesture.None)
        {
            return;
        }
        Hit hit = Editing ? HitAt(mouseWorld, terminals: false) : Hit.None;
        if (!hit.IsNone && !IsSelected(hit))
        {
            Selection.Clear();
            AddToSelection(hit);
            Host?.SelectionChanged();
            InvalidateVisual();
        }
        ContextMenuRequested?.Invoke(hit);
    }

    private void PlaceGhost(bool keepPlacing)
    {
        if (circuit == null || GhostAt(mouseWorld) is not { } g)
        {
            return;
        }
        Host?.BeginEdit();
        ComponentInstance c = CircuitEditor.AddComponent(circuit, g.Type, g.Position, Sheet, g.Rotation, g.Mirror, g.Label);
        Selection.Clear();
        Selection.Components.Add(c.Id);
        wireColors = null;
        Host?.CommitEdit();
        Host?.SelectionChanged();
        if (!keepPlacing)
        {
            placeType = null;
            Host?.SetHint(null);
        }
        InvalidateVisual();
    }

    /// <summary>Betiklerden: verilen dünya noktasına eleman bırakır.</summary>
    public ComponentInstance? PlaceAt(string type, Vec world)
    {
        StartPlacing(type);
        if (placeType == null)
        {
            return null;
        }
        mouseWorld = world;
        PlaceGhost(keepPlacing: false);
        return circuit?.FindComponent(Selection.Components.FirstOrDefault());
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        // Pencere odağı kaybolursa basılı buton bırakılmış sayılır, yarım kalan sürükleme geri alınır
        if (pressed != null)
        {
            ComponentInstance c = pressed;
            pressed = null;
            ComponentReleased?.Invoke(c);
        }
        if (gesture is Gesture.Move or Gesture.Segment or Gesture.Label)
        {
            CancelCurrent();
        }
        else if (gesture != Gesture.None)
        {
            gesture = Gesture.None;
            InvalidateVisual();
        }
        Cursor = null;
    }

    // ---- Paletten sürükle-bırak ----

    protected override void OnDragEnter(DragEventArgs e) => OnDragOver(e);

    protected override void OnDragOver(DragEventArgs e)
    {
        if (!Editing || circuit == null || e.Data.GetData(DragFormat) is not string type)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (placeType != type)
        {
            placeType = type;
            placeRotation = 0;
            placeMirror = false;
        }
        placeByDrag = true;
        mouseInside = true;
        mouseWorld = ToWorld(e.GetPosition(this));
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        if (placeByDrag)
        {
            placeType = null;
            placeByDrag = false;
            mouseInside = false;
            InvalidateVisual();
        }
    }

    protected override void OnDrop(DragEventArgs e)
    {
        if (!Editing || e.Data.GetData(DragFormat) is not string type)
        {
            return;
        }
        placeType = type;
        mouseWorld = ToWorld(e.GetPosition(this));
        PlaceGhost(keepPlacing: false);
        placeByDrag = false;
        Focus();
        e.Handled = true;
    }
}
