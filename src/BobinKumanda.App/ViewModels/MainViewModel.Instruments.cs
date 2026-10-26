using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BobinKumanda.App.Rendering;
using BobinKumanda.Core;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.App.ViewModels;

/// <summary>Simülasyondaki elle ayar: sensör değeri, motor mil yükü, şalter konumu. Değişince elemana bildirilir.</summary>
public sealed partial class SliderItem : ObservableObject
{
    private readonly Action<double> changed;
    private bool silent;

    public SliderItem(string componentId, SimSlider s, Action<double> changed)
    {
        ComponentId = componentId;
        NameKey = s.NameKey;
        Name = Loc.T(s.NameKey);
        Min = s.Min;
        Max = s.Max;
        Step = s.Step;
        Unit = s.Unit;
        this.changed = changed;
        value = s.Value;
    }

    public string ComponentId { get; }
    public string NameKey { get; }
    public string Name { get; }
    public double Min { get; }
    public double Max { get; }
    public double Step { get; }
    public string Unit { get; }

    private double value;

    public double Value
    {
        get => value;
        set
        {
            double v = Step >= 1 ? Math.Round(value) : value;
            if (SetProperty(ref this.value, v))
            {
                OnPropertyChanged(nameof(ValueText));
                if (!silent)
                {
                    changed(v);
                }
            }
        }
    }

    public string ValueText => string.Format(Loc.Culture, Max - Min > 20 ? "{0:0} {1}" : "{0:0.#} {1}", value, Unit).Trim();

    /// <summary>Simülasyondaki değer başka yoldan değişince (tıklama) kaydırıcıyı geri bildirim yapmadan günceller.</summary>
    public void Sync(double v)
    {
        if (Math.Abs(v - value) < Math.Max(1e-9, Step / 2))
        {
            return;
        }
        silent = true;
        try
        {
            Value = v;
        }
        finally
        {
            silent = false;
        }
    }
}

/// <summary>Sağ paneldeki işlem düğmesi (Reset, Test, Sigortayı değiştir...).</summary>
public sealed record ActionItem(string Name, IRelayCommand Command);

/// <summary>Çizim alanında gösterilen ölçü ucu: hedef ve kablo üstündeyse tıklanan nokta.</summary>
public readonly record struct ProbeMark(ProbeTarget Target, Vec At, ProbeKind Kind);

/// <summary>
/// Ölçü aletleri (multimetre, pens ampermetre) ve simülasyondaki elle ayarlar. Multimetre düzenlemede de çalışır:
/// devre enerjisizken direnç (Ω) ölçülür; gerilim için simülasyon başlatılmalıdır.
/// </summary>
public sealed partial class MainViewModel
{
    private Simulator? offline;
    private ProbeMark? redProbe, blackProbe, clamp;
    private double clampMax;

    public ObservableCollection<ActionItem> Actions { get; } = [];

    [ObservableProperty]
    public partial SliderItem? Slider { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMultimeter), nameof(IsClamp), nameof(HasMeter))]
    public partial MeterTool Meter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVoltsAc), nameof(IsVoltsDc), nameof(IsOhms))]
    public partial MeterFunction MeterFunction { get; set; }

    [ObservableProperty]
    public partial string MeterValue { get; private set; } = "- - - -";

    [ObservableProperty]
    public partial string MeterUnit { get; private set; } = "V~";

    [ObservableProperty]
    public partial string MeterNote { get; private set; } = "";

    [ObservableProperty]
    public partial bool MeterWarning { get; private set; }

    [ObservableProperty]
    public partial string ClampValue { get; private set; } = "- - - -";

    [ObservableProperty]
    public partial string ClampUnit { get; private set; } = "A~";

    [ObservableProperty]
    public partial string ClampNote { get; private set; } = "";

    [ObservableProperty]
    public partial string ClampMaxText { get; private set; } = "";

    public bool IsMultimeter
    {
        get => Meter == MeterTool.Multimeter;
        set => Meter = value ? MeterTool.Multimeter : Meter == MeterTool.Multimeter ? MeterTool.None : Meter;
    }

    public bool IsClamp
    {
        get => Meter == MeterTool.Clamp;
        set => Meter = value ? MeterTool.Clamp : Meter == MeterTool.Clamp ? MeterTool.None : Meter;
    }

    public bool HasMeter => Meter != MeterTool.None;

    public bool IsVoltsAc
    {
        get => MeterFunction == MeterFunction.VoltsAc;
        set { if (value) MeterFunction = MeterFunction.VoltsAc; }
    }

    public bool IsVoltsDc
    {
        get => MeterFunction == MeterFunction.VoltsDc;
        set { if (value) MeterFunction = MeterFunction.VoltsDc; }
    }

    public bool IsOhms
    {
        get => MeterFunction == MeterFunction.Ohms;
        set { if (value) MeterFunction = MeterFunction.Ohms; }
    }

    /// <summary>Çizim alanında gösterilecek uçlar (yalnızca açık alete ait olanlar).</summary>
    public IReadOnlyList<ProbeMark> ProbeMarks
    {
        get
        {
            var list = new List<ProbeMark>(3);
            if (Meter == MeterTool.Multimeter)
            {
                if (redProbe is { } r) list.Add(r);
                if (blackProbe is { } b) list.Add(b);
            }
            else if (Meter == MeterTool.Clamp && clamp is { } c)
            {
                list.Add(c);
            }
            return list;
        }
    }

    partial void OnMeterChanged(MeterTool value)
    {
        if (value == MeterTool.Multimeter && redProbe == null)
        {
            // Yeni açılan alet: enerjisiz devrede direnç ölçmek daha olası
            MeterFunction = IsSimulating ? MeterFunction.VoltsAc : MeterFunction.Ohms;
        }
        UpdateMeters();
        UpdateStatus();
        Frame?.Invoke();
    }

    partial void OnMeterFunctionChanged(MeterFunction value) => UpdateMeters();

    /// <summary>Çizim alanında bir klemense, ek noktasına ya da kabloya tıklandı.</summary>
    public void PlaceProbe(ProbeTarget target, Vec at)
    {
        if (Circuit == null)
        {
            return;
        }
        if (Meter == MeterTool.Clamp)
        {
            if (ClampWireFor(target) is not { } wire)
            {
                return;
            }
            clamp = new ProbeMark(ProbeTarget.Wire(wire.Id), target.Kind == 'W' ? at : WireMid(wire), ProbeKind.Clamp);
            clampMax = 0;
        }
        else if (Meter == MeterTool.Multimeter)
        {
            // Sırayla kırmızı, siyah; iki uç da yerindeyse yakın olanı taşınır
            var mark = new ProbeMark(target, at, ProbeKind.Red);
            if (redProbe == null || (blackProbe != null && Distance(redProbe.Value, at) <= Distance(blackProbe.Value, at)))
            {
                redProbe = mark;
            }
            else
            {
                blackProbe = mark with { Kind = ProbeKind.Black };
            }
        }
        UpdateMeters();
        UpdateStatus();
        Frame?.Invoke();
    }

    private double Distance(ProbeMark m, Vec at) => PositionOf(m).DistanceTo(at);

    /// <summary>Ucun çizimdeki yeri: klemens ve ek noktası taşınınca uç da onunla gider.</summary>
    public Vec PositionOf(ProbeMark m)
    {
        if (Circuit != null && m.Target.Kind == 'T' && Circuit.FindComponent(m.Target.Id) is { } c &&
            Core.Catalog.ComponentCatalog.Find(c.Type) is { } def && def.FindTerminal(m.Target.Sub) is { } t)
        {
            return def.TerminalWorld(c, t);
        }
        if (Circuit != null && m.Target.Kind == 'J' && Circuit.FindJunction(m.Target.Id) is { } j)
        {
            return j.Position;
        }
        return m.At;
    }

    /// <summary>Uçların hangi sayfada olduğu (başka sayfadaki uç çizilmez).</summary>
    public string? SheetOf(ProbeMark m) => m.Target.Kind switch
    {
        'T' => Circuit?.FindComponent(m.Target.Id)?.Sheet,
        'J' => Circuit?.FindJunction(m.Target.Id)?.Sheet,
        _ => Circuit?.FindWire(m.Target.Id)?.Sheet
    };

    private Wire? ClampWireFor(ProbeTarget t)
    {
        if (Circuit == null)
        {
            return null;
        }
        return t.Kind switch
        {
            'W' => Circuit.FindWire(t.Id),
            'T' => Circuit.Wires.FirstOrDefault(w => (w.A.Component == t.Id && w.A.Terminal == t.Sub) || (w.B.Component == t.Id && w.B.Terminal == t.Sub)),
            'J' => Circuit.Wires.FirstOrDefault(w => w.A.Junction == t.Id || w.B.Junction == t.Id),
            _ => null
        };
    }

    private static Vec WireMid(Wire w) => GeometryUtil.PointAlong(w.Points, GeometryUtil.PolylineLength(w.Points) / 2);

    [RelayCommand]
    private void ResetClampMax()
    {
        clampMax = 0;
        UpdateMeters();
    }

    [RelayCommand]
    private void CloseMeter() => Meter = MeterTool.None;

    [RelayCommand]
    private void ClearProbes()
    {
        redProbe = blackProbe = null;
        UpdateMeters();
        Frame?.Invoke();
    }

    /// <summary>Devre değişti: enerjisiz modeli yeniden kur, silinen yerlere takılı uçları kaldır.</summary>
    private void InvalidateInstruments()
    {
        offline = null;
        if (Circuit != null)
        {
            if (redProbe is { } r && !r.Target.Exists(Circuit)) redProbe = null;
            if (blackProbe is { } b && !b.Target.Exists(Circuit)) blackProbe = null;
            if (clamp is { } c && !c.Target.Exists(Circuit)) clamp = null;
        }
        else
        {
            redProbe = blackProbe = clamp = null;
        }
        UpdateMeters();
    }

    private Simulator? Offline()
    {
        if (offline == null && Circuit != null)
        {
            try
            {
                offline = new Simulator(Circuit, powered: false);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
        return offline;
    }

    private void UpdateMeters()
    {
        if (Meter == MeterTool.Multimeter)
        {
            MeterReading r = ReadMultimeter();
            MeterValue = r.Value;
            MeterUnit = r.Unit;
            MeterNote = r.Note ?? "";
            MeterWarning = r.Warning;
        }
        else if (Meter == MeterTool.Clamp)
        {
            MeterReading r = Core.Simulation.Meter.Clamp(Simulator, clamp?.Target.Id);
            clampMax = Math.Max(clampMax, Core.Simulation.Meter.ClampAmps(Simulator, clamp?.Target.Id));
            ClampValue = r.Value;
            ClampUnit = r.Unit;
            ClampNote = r.Note ?? "";
            ClampMaxText = clamp == null ? "" : "MAX " + Core.Simulation.Meter.Number(clampMax) + " " + r.Unit;
        }
    }

    private MeterReading ReadMultimeter() =>
        Core.Simulation.Meter.Multimeter(MeterFunction, Simulator, Offline, redProbe?.Target, blackProbe?.Target);

    // ---- Kaydırıcı ve işlem düğmeleri ----

    private void UpdateControls()
    {
        SimComponent? sc = Simulator != null && Selected != null ? Simulator.Find(Selected.Id) : null;
        SimSlider? s = sc?.Slider;
        if (s == null)
        {
            Slider = null;
        }
        else if (Slider == null || Slider.ComponentId != sc!.Id || Slider.NameKey != s.NameKey)
        {
            string id = sc!.Id;
            Slider = new SliderItem(id, s, v =>
            {
                Simulator?.Interact(id, new Interaction(InteractionKind.SetValue, v));
                AfterSimChange();
            });
        }
        else
        {
            Slider.Sync(s.Value);
        }

        IReadOnlyList<SimAction> acts = sc?.Actions ?? [];
        if (Actions.Count != acts.Count || Actions.Select(a => a.Name).Where((n, i) => n != Loc.T(acts[i].NameKey)).Any())
        {
            Actions.Clear();
            foreach (SimAction a in acts)
            {
                string id = sc!.Id;
                InteractionKind kind = a.Kind;
                Actions.Add(new ActionItem(Loc.T(a.NameKey), new RelayCommand(() =>
                {
                    Simulator?.Interact(id, new Interaction(kind));
                    AfterSimChange();
                })));
            }
        }
        OnPropertyChanged(nameof(HasActions));
    }

    public bool HasActions => Actions.Count > 0;

    /// <summary>Sorunlar listesinde tıklanan sorunun elemanını seçer ve gösterir.</summary>
    public void FocusProblem(ProblemItem p)
    {
        if (Circuit?.FindComponent(p.ComponentIds.FirstOrDefault()) is { } c)
        {
            SelectComponent(c);
            ComponentFocused?.Invoke(c);
        }
    }

    /// <summary>Seçilen eleman görünür alana getirilsin (sorunlar listesinden).</summary>
    public event Action<ComponentInstance>? ComponentFocused;
}
