using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KumandaLab.Core.Catalog;
using KumandaLab.Core.IO;
using KumandaLab.Core.Localization;
using KumandaLab.Core.Model;
using KumandaLab.Core.Simulation;
using Microsoft.Win32;

namespace KumandaLab.App.ViewModels;

/// <summary>
/// Ana pencerenin durumu: açık devre, dosya işlemleri ve simülasyonun gerçek zamanlı yürütülmesi.
/// Çizim alanı bu sınıfı <see cref="Frame"/> olayıyla izler.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch clock = new();
    private TimeSpan lastTick;
    private int logCount;
    private string problemSignature = "";
    private int tickCount;

    public MainViewModel(AppSettings settings)
    {
        Settings = settings;
        Speed = settings.Speed;
        timer.Tick += (_, _) => Tick();
        Loc.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(WindowTitle));
            UpdateStatus();
            RebuildSelection();
        };
        RefreshRecent();
        UpdateStatus();
    }

    public AppSettings Settings { get; }

    /// <summary>Çizim alanının yeniden çizilmesi gerektiğinde.</summary>
    public event Action? Frame;

    /// <summary>Yeni devre açıldı (çizim alanı sayfaya sığdırır).</summary>
    public event Action? CircuitLoaded;

    public ObservableCollection<LogItem> Log { get; } = [];
    public ObservableCollection<ProblemItem> Problems { get; } = [];
    public ObservableCollection<ReadoutItem> Readouts { get; } = [];
    public ObservableCollection<ParamItem> Params { get; } = [];
    public ObservableCollection<string> RecentFiles { get; } = [];

    public double[] Speeds { get; } = [0.25, 0.5, 1, 2, 5, 10];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCircuit), nameof(WindowTitle))]
    public partial Circuit? Circuit { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    public partial string? FilePath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSimulating), nameof(IsEditing))]
    public partial Simulator? Simulator { get; private set; }

    [ObservableProperty]
    public partial bool IsPaused { get; private set; }

    [ObservableProperty]
    public partial double Speed { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "";

    [ObservableProperty]
    public partial string HintText { get; private set; } = "";

    [ObservableProperty]
    public partial string SimTimeText { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial ComponentInstance? Selected { get; private set; }

    [ObservableProperty]
    public partial string SelectedTypeName { get; private set; } = "";

    [ObservableProperty]
    public partial string SelectedInfo { get; private set; } = "";

    [ObservableProperty]
    public partial int ProblemCount { get; private set; }

    /// <summary>Animasyon saati; simülasyon duraklatılınca durur.</summary>
    public double AnimTime { get; private set; }

    public bool HasCircuit => Circuit != null;
    public bool HasSelection => Selected != null;
    public bool IsSimulating => Simulator != null;
    public bool IsEditing => Simulator == null;
    public bool HasRecent => RecentFiles.Count > 0;

    public string DocumentName => FilePath != null ? Path.GetFileName(FilePath) :
        Circuit is { Title.CircuitName.Length: > 0 } c ? c.Title.CircuitName : Loc.T("ui.untitled");

    public string WindowTitle => Circuit == null ? "KumandaLab" : $"{DocumentName}{(IsDirty ? " *" : "")} — KumandaLab";

    public string SelectedLabel
    {
        get => Selected?.Label ?? "";
        set
        {
            if (Selected != null && IsEditing && Selected.Label != value)
            {
                Selected.Label = value;
                MarkChanged();
            }
        }
    }

    partial void OnSpeedChanged(double value)
    {
        Settings.Speed = value;
    }

    // ---- Dosya ----

    [RelayCommand]
    private void New()
    {
        if (!ConfirmDiscard())
        {
            return;
        }
        StopSimulation();
        SetDocument(new Circuit(), null);
    }

    [RelayCommand]
    private void Open()
    {
        if (!ConfirmDiscard())
        {
            return;
        }
        var dlg = new OpenFileDialog
        {
            Filter = $"{Loc.T("ui.filterAll")}|*.kmlab;*.esc|{Loc.T("ui.filterKmlab")} (*.kmlab)|*.kmlab|{Loc.T("ui.filterEsc")} (*.esc)|*.esc",
            InitialDirectory = FilePath != null ? Path.GetDirectoryName(FilePath) : null
        };
        if (dlg.ShowDialog() == true)
        {
            OpenFile(dlg.FileName, askToDiscard: false);
        }
    }

    [RelayCommand]
    private void OpenRecent(string? path)
    {
        if (path != null)
        {
            OpenFile(path);
        }
    }

    /// <summary>Komut satırından, son dosyalardan ya da Aç penceresinden.</summary>
    public bool OpenFile(string path, bool askToDiscard = true)
    {
        if (askToDiscard && !ConfirmDiscard())
        {
            return false;
        }
        StopSimulation();
        try
        {
            if (string.Equals(Path.GetExtension(path), ".esc", StringComparison.OrdinalIgnoreCase))
            {
                EscImportResult r = EscImporter.Read(path);
                // Eski dosya .esc olarak üzerine yazılmasın diye adsız (yalnızca ad taşıyan) belge olarak açılır
                SetDocument(r.Circuit, null);
                if (r.Warnings.Count > 0)
                {
                    MessageBox.Show(Loc.T("ui.importWarnings") + "\n\n• " + string.Join("\n• ", r.Warnings), "KumandaLab",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                SetDocument(KmlabFile.Load(path), path);
            }
            Settings.AddRecent(Path.GetFullPath(path));
            Settings.Save();
            RefreshRecent();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CircuitFileException or NotSupportedException)
        {
            MessageBox.Show($"{Loc.T("ui.openFailed")}:\n{path}\n\n{e.Message}", "KumandaLab", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    [RelayCommand]
    private void Save() => SaveDocument(FilePath);

    [RelayCommand]
    private void SaveAs() => SaveDocument(null);

    private bool SaveDocument(string? path)
    {
        if (Circuit == null)
        {
            return false;
        }
        if (path == null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = $"{Loc.T("ui.filterKmlab")} (*.kmlab)|*.kmlab",
                FileName = Path.GetFileNameWithoutExtension(DocumentName) + KmlabFile.Extension,
                DefaultExt = KmlabFile.Extension
            };
            if (dlg.ShowDialog() != true)
            {
                return false;
            }
            path = dlg.FileName;
        }
        try
        {
            KmlabFile.Save(Circuit, path);
            FilePath = path;
            IsDirty = false;
            Settings.AddRecent(Path.GetFullPath(path));
            Settings.Save();
            RefreshRecent();
            OnPropertyChanged(nameof(DocumentName));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"{Loc.T("ui.saveFailed")}:\n{path}\n\n{e.Message}", "KumandaLab", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>Kaydedilmemiş değişiklik varsa sorar. false: kullanıcı vazgeçti.</summary>
    public bool ConfirmDiscard()
    {
        if (!IsDirty || Circuit == null)
        {
            return true;
        }
        MessageBoxResult r = MessageBox.Show(Loc.T("ui.unsaved"), "KumandaLab", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r switch
        {
            MessageBoxResult.Yes => SaveDocument(FilePath),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void SetDocument(Circuit circuit, string? path)
    {
        Circuit = circuit;
        FilePath = path;
        IsDirty = false;
        Selected = null;
        RebuildSelection();
        OnPropertyChanged(nameof(DocumentName));
        CircuitLoaded?.Invoke();
        UpdateStatus();
    }

    private void RefreshRecent()
    {
        RecentFiles.Clear();
        foreach (string p in Settings.RecentFiles)
        {
            RecentFiles.Add(p);
        }
        OnPropertyChanged(nameof(HasRecent));
    }

    private void MarkChanged()
    {
        IsDirty = true;
        Frame?.Invoke();
    }

    // ---- Seçim ve özellikler ----

    public void SetSelection(ComponentInstance? c)
    {
        Selected = c;
        RebuildSelection();
    }

    private void RebuildSelection()
    {
        Params.Clear();
        Readouts.Clear();
        OnPropertyChanged(nameof(SelectedLabel));
        if (Selected == null || ComponentCatalog.Find(Selected.Type) is not { } def)
        {
            SelectedTypeName = "";
            SelectedInfo = "";
            return;
        }
        SelectedTypeName = def.Name;
        SelectedInfo = def.Info;
        foreach (ParamDef p in def.Params)
        {
            Params.Add(new ParamItem(Selected, p, MarkChanged));
        }
        UpdateReadouts();
    }

    private void UpdateReadouts()
    {
        Readouts.Clear();
        if (Simulator?.Solution is { } sol && Selected != null && Simulator.Find(Selected.Id) is { } sc)
        {
            foreach (Readout r in sc.Readouts(sol))
            {
                Readouts.Add(new ReadoutItem(r.Name, r.Value));
            }
        }
    }

    // ---- Simülasyon ----

    [RelayCommand]
    private void ToggleSimulation()
    {
        if (Simulator != null)
        {
            StopSimulation();
        }
        else
        {
            StartSimulation();
        }
    }

    public void StartSimulation()
    {
        if (Circuit == null || Simulator != null)
        {
            return;
        }
        Log.Clear();
        Problems.Clear();
        logCount = 0;
        problemSignature = "";
        AnimTime = 0;
        Simulator = new Simulator(Circuit);
        IsPaused = false;
        clock.Restart();
        lastTick = TimeSpan.Zero;
        timer.Start();
        AfterSimChange();
    }

    public void StopSimulation()
    {
        if (Simulator == null)
        {
            return;
        }
        timer.Stop();
        Simulator = null;
        IsPaused = false;
        SimTimeText = "";
        Problems.Clear();
        ProblemCount = 0;
        RebuildSelection();
        UpdateStatus();
        Frame?.Invoke();
    }

    [RelayCommand]
    private void Pause()
    {
        if (Simulator == null)
        {
            StartSimulation();
            return;
        }
        if (Simulator.Status == SimStatus.Paused)
        {
            Simulator.Resume();
        }
        else
        {
            Simulator.Pause();
        }
        AfterSimChange();
    }

    [RelayCommand]
    private void Step()
    {
        if (Simulator == null)
        {
            StartSimulation();
        }
        if (Simulator == null)
        {
            return;
        }
        Simulator.Pause();
        Simulator.StepNextEvent();
        AfterSimChange();
    }

    public void Press(ComponentInstance c) => Interact(c, InteractionKind.Press);

    public void Release(ComponentInstance c) => Interact(c, InteractionKind.Release);

    private void Interact(ComponentInstance c, InteractionKind kind)
    {
        if (Simulator == null)
        {
            return;
        }
        Simulator.Interact(c.Id, new Interaction(kind));
        AfterSimChange();
    }

    /// <summary>Simülasyon saatini anında ilerletir (betik ve testler için).</summary>
    public void RunFor(double seconds)
    {
        if (Simulator == null)
        {
            return;
        }
        Simulator.AdvanceTo(Simulator.Time + seconds);
        AnimTime += seconds;
        AfterSimChange();
    }

    private void Tick()
    {
        if (Simulator == null)
        {
            return;
        }
        TimeSpan now = clock.Elapsed;
        double dt = Math.Min((now - lastTick).TotalSeconds, 0.1);
        lastTick = now;
        if (Simulator.Status == SimStatus.Running)
        {
            Simulator.AdvanceTo(Simulator.Time + dt * Speed);
            AnimTime += dt;
        }
        else if (Simulator.Status == SimStatus.Faulted)
        {
            // Hatalı yolun yanıp sönmesi için
            AnimTime += dt;
        }
        tickCount++;
        AfterSimChange(readouts: tickCount % 6 == 0);
    }

    private void AfterSimChange(bool readouts = true)
    {
        Simulator? sim = Simulator;
        if (sim == null)
        {
            return;
        }
        IsPaused = sim.Status == SimStatus.Paused;
        SimTimeText = string.Format(Loc.Culture, "{0}: {1:0.00} s", Loc.T("ui.simTime"), sim.Time);
        for (; logCount < sim.Log.Count; logCount++)
        {
            SimLogEntry e = sim.Log[logCount];
            Log.Add(new LogItem(e.Time.ToString("0.00", Loc.Culture) + " s", e.Text, e.Severity));
        }
        string sig = string.Join("|", sim.Problems.Select(p => p.Code + p.Message));
        if (sig != problemSignature)
        {
            problemSignature = sig;
            Problems.Clear();
            foreach (SimProblem p in sim.Problems.OrderByDescending(p => p.Severity))
            {
                Problems.Add(new ProblemItem(p.Message, p.Explanation, p.Severity));
            }
            ProblemCount = Problems.Count;
        }
        if (readouts)
        {
            UpdateReadouts();
        }
        UpdateStatus();
        Frame?.Invoke();
    }

    private void UpdateStatus()
    {
        if (Simulator == null)
        {
            StatusText = Loc.T("ui.status.edit");
            HintText = Circuit == null ? "" : Loc.T("ui.hint.edit");
            return;
        }
        StatusText = Loc.T(Simulator.Status switch
        {
            SimStatus.Paused => "ui.status.paused",
            SimStatus.Faulted => "ui.status.faulted",
            _ => "ui.status.running"
        });
        HintText = Loc.T("ui.hint.sim");
    }

    public string AboutText => Loc.T("ui.aboutText",
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");
}
