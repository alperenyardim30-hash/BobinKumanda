using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BobinKumanda.App.Rendering;
using BobinKumanda.Core;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Editing;
using BobinKumanda.Core.IO;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Samples;
using BobinKumanda.Core.Simulation;
using Microsoft.Win32;

namespace BobinKumanda.App.ViewModels;

/// <summary>
/// Ana pencerenin durumu: açık devre, dosya işlemleri, düzenleme komutları (geri al, pano, döndür...), sayfalar,
/// otomatik kayıt ve simülasyonun gerçek zamanlı yürütülmesi. Çizim alanı bu sınıfı <see cref="IEditorHost"/> olarak
/// kullanır ve <see cref="Frame"/> olayıyla yeniden çizilir.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IEditorHost
{
    /// <summary>Panodaki devre parçasının biçimi (başka bir Bobin Kumanda penceresine de yapıştırılabilir).</summary>
    public const string ClipboardFormat = "BobinKumanda.Fragment";

    private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer autosaveTimer = new(DispatcherPriority.Background) { Interval = Autosave.Interval };
    private readonly Stopwatch clock = new();
    private readonly EditHistory history = new();
    private readonly Autosave autosave = new();
    private TimeSpan lastTick;
    private int logCount;
    private string problemSignature = "";
    private int tickCount;
    private byte[]? savedState;
    private bool editPushed;
    private byte[]? internalClipboard;
    private int pasteCount;
    private string? hintKey;

    public MainViewModel(AppSettings settings)
    {
        Settings = settings;
        Speed = settings.Speed;
        timer.Tick += (_, _) => Tick();
        autosaveTimer.Tick += (_, _) => AutosaveNow();
        history.Changed += RefreshCommands;
        Loc.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(DocumentName));
            OnPropertyChanged(nameof(SampleItems));
            UpdateStatus();
            RebuildSelection();
            RebuildSheets();
        };
        RefreshRecent();
        UpdateStatus();
    }

    public AppSettings Settings { get; }

    public PaletteViewModel Palette { get; } = new();

    /// <summary>Çizim alanıyla paylaşılan seçim.</summary>
    public Selection Selection { get; } = new();

    /// <summary>Çizim alanının yeniden çizilmesi gerektiğinde.</summary>
    public event Action? Frame;

    /// <summary>Yeni devre açıldı (çizim alanı sayfaya sığdırır).</summary>
    public event Action? CircuitLoaded;

    /// <summary>Geri alma/yineleme devreyi değiştirdi; görünüm korunur.</summary>
    public event Action? CircuitReplaced;

    /// <summary>Devre düzenlendi (kablo renkleri gibi türetilmiş bilgiler yeniden hesaplanmalı).</summary>
    public event Action? CircuitEdited;

    /// <summary>Gösterilen sayfa değişti.</summary>
    public event Action? SheetChanged;

    /// <summary>Çizim alanındaki işlemi (kablo çizimi, sürükleme) bırakması için.</summary>
    public event Action? CancelCanvasGesture;

    /// <summary>Yapıştırma yeri: farenin altı, fare çizim alanında değilse null.</summary>
    public Func<Vec?>? PasteTarget { get; set; }

    public ObservableCollection<LogItem> Log { get; } = [];
    public ObservableCollection<ProblemItem> Problems { get; } = [];
    public ObservableCollection<ReadoutItem> Readouts { get; } = [];
    public ObservableCollection<ParamItem> Params { get; } = [];
    public ObservableCollection<string> RecentFiles { get; } = [];
    public ObservableCollection<SheetItem> Sheets { get; } = [];
    public ObservableCollection<string> LabelChoices { get; } = [];

    public double[] Speeds { get; } = [0.25, 0.5, 1, 2, 5, 10];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCircuit), nameof(WindowTitle), nameof(IsEditing))]
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
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(IsFollower))]
    public partial ComponentInstance? Selected { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTextSelection))]
    public partial TextNote? SelectedNote { get; private set; }

    [ObservableProperty]
    public partial string SelectedTypeName { get; private set; } = "";

    [ObservableProperty]
    public partial string SelectedInfo { get; private set; } = "";

    [ObservableProperty]
    public partial string SelectionSummary { get; private set; } = "";

    [ObservableProperty]
    public partial int ProblemCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectTool), nameof(IsWireTool), nameof(IsTextTool))]
    public partial EditTool Tool { get; set; }

    [ObservableProperty]
    public partial string CurrentSheet { get; private set; } = "s1";

    /// <summary>Animasyon saati; simülasyon duraklatılınca durur.</summary>
    public double AnimTime { get; private set; }

    public bool HasCircuit => Circuit != null;
    public bool HasSelection => Selected != null;
    public bool HasTextSelection => SelectedNote != null;
    public bool HasAnySelection => !Selection.IsEmpty;
    public bool IsFollower => Selected != null && ComponentCatalog.Find(Selected.Type)?.Role == DeviceRole.Follower;
    public bool IsSimulating => Simulator != null;
    public bool IsEditing => Simulator == null && Circuit != null;
    public bool HasRecent => RecentFiles.Count > 0;
    public bool HasManySheets => Sheets.Count > 1;

    public bool IsSelectTool
    {
        get => Tool == EditTool.Select;
        set
        {
            if (value)
            {
                Tool = EditTool.Select;
            }
        }
    }

    public bool IsWireTool
    {
        get => Tool == EditTool.Wire;
        set => Tool = value ? EditTool.Wire : EditTool.Select;
    }

    public bool IsTextTool
    {
        get => Tool == EditTool.Text;
        set => Tool = value ? EditTool.Text : EditTool.Select;
    }

    partial void OnToolChanged(EditTool value)
    {
        CancelCanvasGesture?.Invoke();
        UpdateStatus();
    }

    public string DocumentName => FilePath != null ? Path.GetFileName(FilePath) :
        Circuit is { Title.CircuitName.Length: > 0 } c ? c.Title.CircuitName : Loc.T("ui.untitled");

    public string WindowTitle => Circuit == null ? App.ProductName : $"{DocumentName}{(IsDirty ? " *" : "")} — {App.ProductName}";

    public string SelectedLabel
    {
        get => Selected?.Label ?? "";
        set
        {
            if (Selected != null && IsEditing && Selected.Label != value)
            {
                BeginEdit("label:" + Selected.Id);
                Selected.Label = value;
                CommitEdit();
            }
        }
    }

    public string SelectedText
    {
        get => SelectedNote?.Text ?? "";
        set
        {
            if (SelectedNote != null && IsEditing && SelectedNote.Text != value)
            {
                BeginEdit("text:" + SelectedNote.Id);
                SelectedNote.Text = value;
                CommitEdit();
            }
        }
    }

    partial void OnSpeedChanged(double value)
    {
        Settings.Speed = value;
    }

    partial void OnSimulatorChanged(Simulator? value)
    {
        RefreshCommands();
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
            Filter = $"{Loc.T("ui.filterAll")}|*.bobin;*.kmlab;*.esc|{Loc.T("ui.filterCircuit")} (*.bobin)|*.bobin;*.kmlab|{Loc.T("ui.filterEsc")} (*.esc)|*.esc",
            InitialDirectory = FilePath != null ? Path.GetDirectoryName(FilePath) : null
        };
        if (dlg.ShowDialog() == true)
        {
            OpenFile(dlg.FileName, askToDiscard: false);
        }
    }

    /// <summary>Örnekler listesi (dile göre adlarıyla).</summary>
    public IReadOnlyList<SampleItem> SampleItems => SampleLibrary.All.Select(s => new SampleItem(s.Id, s.Title, s.Description)).ToList();

    /// <summary>Örneği yeni, adsız ve kaydedilmemiş sayılmayan bir belge olarak açar.</summary>
    [RelayCommand]
    public void OpenSample(string? id)
    {
        if (id == null || SampleLibrary.Find(id) is not { } sample || !ConfirmDiscard())
        {
            return;
        }
        StopSimulation();
        SetDocument(sample.Build(), null);
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
                    Dialogs.Show(Loc.T("ui.importWarnings") + "\n\n• " + string.Join("\n• ", r.Warnings),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                // İlk deneme sürümünün .kmlab dosyaları açılır, kaydedince .bobin'e geçer
                bool legacy = string.Equals(Path.GetExtension(path), CircuitFile.LegacyExtension, StringComparison.OrdinalIgnoreCase);
                SetDocument(CircuitFile.Load(path), legacy ? null : path);
            }
            Settings.AddRecent(Path.GetFullPath(path));
            Settings.Save();
            RefreshRecent();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CircuitFileException or NotSupportedException)
        {
            Dialogs.Show($"{Loc.T("ui.openFailed")}:\n{path}\n\n{e.Message}", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    [RelayCommand]
    private void Save() => SaveDocument(FilePath);

    [RelayCommand]
    private void SaveAs() => SaveDocument(null);

    public bool SaveDocument(string? path)
    {
        if (Circuit == null)
        {
            return false;
        }
        if (path == null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = $"{Loc.T("ui.filterCircuit")} (*.bobin)|*.bobin|{Loc.T("ui.filterEsc")} (*.esc)|*.esc",
                FileName = Path.GetFileNameWithoutExtension(DocumentName) + CircuitFile.Extension,
                DefaultExt = CircuitFile.Extension
            };
            if (dlg.ShowDialog() != true)
            {
                return false;
            }
            path = dlg.FileName;
        }
        if (string.Equals(Path.GetExtension(path), ".esc", StringComparison.OrdinalIgnoreCase))
        {
            // Eski biçim kayıplıdır (yeni elemanlar, sayfalar): belge .bobin olarak açık kalır, yalnızca dışa aktarılır
            return ExportEsc(path);
        }
        try
        {
            CircuitFile.Save(Circuit, path);
            FilePath = path;
            savedState = CircuitFile.ToBytes(Circuit);
            IsDirty = false;
            autosave.Clear();
            Settings.AddRecent(Path.GetFullPath(path));
            Settings.Save();
            RefreshRecent();
            OnPropertyChanged(nameof(DocumentName));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Dialogs.Show($"{Loc.T("ui.saveFailed")}:\n{path}\n\n{e.Message}", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private bool ExportEsc(string path)
    {
        try
        {
            EscExportResult r = EscExporter.Save(Circuit!, path, CurrentSheet);
            string message = Loc.T("ui.escSaved") + "\n" + path;
            if (r.Warnings.Count > 0)
            {
                message += "\n\n" + Loc.T("ui.escSavedNotes") + "\n• " + string.Join("\n• ", r.Warnings);
            }
            Dialogs.Show(message, MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Dialogs.Show($"{Loc.T("ui.saveFailed")}:\n{path}\n\n{e.Message}", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        MessageBoxResult r = Dialogs.Show(Loc.T("ui.unsaved"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question, quietAnswer: MessageBoxResult.No);
        return r switch
        {
            MessageBoxResult.Yes => SaveDocument(FilePath),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    /// <summary>Program kapanırken: otomatik kayıt dosyası silinir.</summary>
    public void Shutdown()
    {
        StopSimulation();
        autosaveTimer.Stop();
        autosave.Clear();
    }

    private void SetDocument(Circuit circuit, string? path, bool dirty = false)
    {
        CancelCanvasGesture?.Invoke();
        Circuit = circuit;
        FilePath = path;
        redProbe = blackProbe = clamp = null;
        // önceki devrenin olay günlüğü ve sorunları yeni belgede kalmasın
        Log.Clear();
        Problems.Clear();
        ProblemCount = 0;
        InvalidateInstruments();
        savedState = dirty ? null : CircuitFile.ToBytes(circuit);
        IsDirty = dirty;
        history.Clear();
        Selection.Clear();
        CurrentSheet = circuit.Sheets.Count > 0 ? circuit.Sheets[0].Id : "s1";
        RebuildSheets();
        RebuildSelection();
        OnPropertyChanged(nameof(DocumentName));
        OnPropertyChanged(nameof(IsEditing));
        CircuitLoaded?.Invoke();
        SheetChanged?.Invoke();
        UpdateStatus();
        RefreshCommands();
        autosave.Clear();
        autosaveTimer.Start();
    }

    /// <summary>Çökme sonrası kalan otomatik kaydı adsız, kaydedilmemiş belge olarak açar.</summary>
    public void Recover(Autosave.Orphan o)
    {
        try
        {
            Circuit c = CircuitFile.Load(o.Path);
            if (o.Name.Length > 0 && c.Title.CircuitName.Length == 0 && o.OriginalPath == null)
            {
                c.Title.CircuitName = o.Name;
            }
            SetDocument(c, null, dirty: true);
            Autosave.Discard(o);
            // Hemen yeni oturumun dosyasına yaz; ikinci bir çökmede yine kurtarılabilsin
            AutosaveNow();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CircuitFileException)
        {
            Dialogs.Show($"{Loc.T("ui.openFailed")}:\n{o.Path}\n\n{e.Message}", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AutosaveNow()
    {
        if (Circuit != null && IsDirty)
        {
            autosave.Save(Circuit, FilePath, DocumentName);
        }
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

    // ---- Düzenleme altyapısı (IEditorHost) ----

    public void BeginEdit(string? coalesceKey = null)
    {
        if (Circuit == null)
        {
            return;
        }
        editPushed = history.Push(Circuit, CurrentSheet, coalesceKey);
    }

    public void CommitEdit()
    {
        IsDirty = true;
        InvalidateInstruments();
        CircuitEdited?.Invoke();
        Frame?.Invoke();
        RefreshCommands();
        UpdateStatus();
    }

    public void CancelEdit()
    {
        if (editPushed)
        {
            history.Discard();
            editPushed = false;
        }
    }

    public void SelectionChanged()
    {
        history.Seal();
        RebuildSelection();
        RefreshCommands();
    }

    public void SetHint(string? key)
    {
        hintKey = key;
        UpdateStatus();
    }

    private void RefreshCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        RotateCommand.NotifyCanExecuteChanged();
        MirrorCommand.NotifyCanExecuteChanged();
        CutCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasAnySelection));
    }

    /// <summary>Düzenleme olmadan değiştirilemeyen bir şey istendiğinde (simülasyon çalışırken Sil gibi).</summary>
    private bool EnsureEditing()
    {
        if (IsEditing)
        {
            return true;
        }
        if (IsSimulating)
        {
            SetHint("ui.hint.stopToEdit");
        }
        return false;
    }

    // ---- Geri al / yinele ----

    private bool CanUndo() => IsEditing && history.CanUndo;
    private bool CanRedo() => IsEditing && history.CanRedo;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Restore(history.Undo(Circuit!, CurrentSheet));

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => Restore(history.Redo(Circuit!, CurrentSheet));

    private void Restore((Circuit Circuit, string? Sheet)? state)
    {
        if (state is not { } s)
        {
            return;
        }
        CancelCanvasGesture?.Invoke();
        Circuit = s.Circuit;
        Selection.Prune(s.Circuit);
        if (s.Sheet != null && s.Circuit.FindSheet(s.Sheet) != null)
        {
            CurrentSheet = s.Sheet;
        }
        else if (s.Circuit.FindSheet(CurrentSheet) == null)
        {
            CurrentSheet = s.Circuit.Sheets[0].Id;
        }
        IsDirty = savedState == null || !CircuitFile.ToBytes(s.Circuit).AsSpan().SequenceEqual(savedState);
        InvalidateInstruments();
        RebuildSheets();
        CircuitReplaced?.Invoke();
        SheetChanged?.Invoke();
        RebuildSelection();
        RefreshCommands();
        Frame?.Invoke();
    }

    // ---- Seçim komutları ----

    private bool CanEditSelection() => IsEditing && !Selection.IsEmpty;
    private bool CanPaste() => IsEditing;

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Delete()
    {
        if (!EnsureEditing() || Selection.IsEmpty)
        {
            return;
        }
        BeginEdit();
        CircuitEditor.Delete(Circuit!, Selection);
        Selection.Clear();
        CommitEdit();
        SelectionChanged();
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Rotate()
    {
        if (!EnsureEditing() || Selection.IsEmpty)
        {
            return;
        }
        BeginEdit();
        CircuitEditor.Rotate(Circuit!, Selection);
        CommitEdit();
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Mirror()
    {
        if (!EnsureEditing() || Selection.IsEmpty)
        {
            return;
        }
        BeginEdit();
        CircuitEditor.Mirror(Circuit!, Selection);
        CommitEdit();
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void SelectAll()
    {
        if (Circuit == null)
        {
            return;
        }
        Selection.Clear();
        Selection.UnionWith(CircuitEditor.All(Circuit, CurrentSheet));
        SelectionChanged();
        Frame?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Copy()
    {
        if (Circuit == null || Selection.IsEmpty)
        {
            return;
        }
        byte[] data = CircuitFile.ToBytes(CircuitEditor.Copy(Circuit, Selection));
        internalClipboard = data;
        pasteCount = 0;
        try
        {
            var obj = new DataObject();
            obj.SetData(ClipboardFormat, Encoding.UTF8.GetString(data));
            Clipboard.SetDataObject(obj, copy: true);
        }
        catch (Exception e) when (e is COMException or ExternalException)
        {
            // Pano başka bir programda kilitliyse aynı pencere içinde yine yapıştırılabilir
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Cut()
    {
        if (!EnsureEditing() || Selection.IsEmpty)
        {
            return;
        }
        Copy();
        Delete();
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste()
    {
        if (!EnsureEditing())
        {
            return;
        }
        byte[]? data = internalClipboard;
        try
        {
            if (Clipboard.GetData(ClipboardFormat) is string text)
            {
                byte[] fromClipboard = Encoding.UTF8.GetBytes(text);
                if (internalClipboard == null || !fromClipboard.AsSpan().SequenceEqual(internalClipboard))
                {
                    pasteCount = 0;
                    internalClipboard = fromClipboard;
                }
                data = fromClipboard;
            }
        }
        catch (Exception e) when (e is COMException or ExternalException)
        {
        }
        if (data == null)
        {
            return;
        }
        Circuit fragment;
        try
        {
            fragment = CircuitFile.FromBytes(data);
        }
        catch (CircuitFileException)
        {
            return;
        }
        PasteFragment(fragment);
    }

    private void PasteFragment(Circuit fragment)
    {
        Selection all = CircuitEditor.All(fragment, "s1");
        if (HitTest.Bounds(fragment, all) is not { } box)
        {
            return;
        }
        Vec offset;
        if (PasteTarget?.Invoke() is { } target)
        {
            offset = CircuitEditor.Snap(target - box.Center);
        }
        else
        {
            // Fare dışarıdaysa her yapıştırma bir öncekinin biraz sağına-altına
            pasteCount++;
            offset = new Vec(20, 20) * pasteCount;
        }
        BeginEdit();
        Selection pasted = CircuitEditor.Paste(Circuit!, fragment, offset, CurrentSheet);
        Selection.Clear();
        Selection.UnionWith(pasted);
        CommitEdit();
        SelectionChanged();
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Duplicate()
    {
        if (!EnsureEditing() || Selection.IsEmpty)
        {
            return;
        }
        Circuit fragment = CircuitEditor.Copy(Circuit!, Selection);
        BeginEdit();
        Selection pasted = CircuitEditor.Paste(Circuit!, fragment, new Vec(20, 20), CurrentSheet);
        Selection.Clear();
        Selection.UnionWith(pasted);
        CommitEdit();
        SelectionChanged();
    }

    /// <summary>Ok tuşlarıyla seçimi bir ızgara adımı kaydırır.</summary>
    public void Nudge(Vec delta)
    {
        if (!EnsureEditing() || Selection.IsEmpty)
        {
            return;
        }
        BeginEdit("nudge");
        var move = new MoveSession(Circuit!, Selection);
        move.Update(delta);
        move.Commit();
        CommitEdit();
    }

    [RelayCommand]
    private void SetTool(string? tool)
    {
        if (EnsureEditing())
        {
            Tool = Enum.TryParse(tool, out EditTool t) ? t : EditTool.Select;
        }
    }

    /// <summary>Antet bilgilerini değiştirir (geri alınabilir).</summary>
    public void SetTitleBlock(TitleBlock t)
    {
        if (!EnsureEditing())
        {
            return;
        }
        BeginEdit();
        Circuit!.Title = t;
        CommitEdit();
        OnPropertyChanged(nameof(DocumentName));
        OnPropertyChanged(nameof(WindowTitle));
    }

    // ---- Sayfalar ----

    private void RebuildSheets()
    {
        Sheets.Clear();
        if (Circuit != null)
        {
            for (int i = 0; i < Circuit.Sheets.Count; i++)
            {
                Sheets.Add(new SheetItem(Circuit.Sheets[i], i) { IsCurrent = Circuit.Sheets[i].Id == CurrentSheet });
            }
        }
        OnPropertyChanged(nameof(HasManySheets));
    }

    public void ShowSheet(string id)
    {
        if (Circuit?.FindSheet(id) == null || id == CurrentSheet)
        {
            return;
        }
        CancelCanvasGesture?.Invoke();
        CurrentSheet = id;
        Selection.Clear();
        foreach (SheetItem s in Sheets)
        {
            s.IsCurrent = s.Id == id;
        }
        SheetChanged?.Invoke();
        SelectionChanged();
    }

    [RelayCommand]
    private void AddSheet()
    {
        if (!EnsureEditing())
        {
            return;
        }
        BeginEdit();
        Sheet s = CircuitEditor.AddSheet(Circuit!, "");
        CommitEdit();
        RebuildSheets();
        ShowSheet(s.Id);
    }

    public void RenameSheet(string id, string name)
    {
        if (!EnsureEditing() || Circuit!.FindSheet(id) is not { } s || s.Name == name)
        {
            return;
        }
        BeginEdit();
        s.Name = name.Trim();
        CommitEdit();
        RebuildSheets();
    }

    public void DeleteSheet(string id)
    {
        if (!EnsureEditing() || Circuit!.Sheets.Count <= 1)
        {
            return;
        }
        bool empty = !Circuit.Components.Any(c => c.Sheet == id) && !Circuit.Wires.Any(w => w.Sheet == id) && !Circuit.Texts.Any(t => t.Sheet == id);
        if (!empty && Dialogs.Show(Loc.T("ui.deleteSheetAsk"), MessageBoxButton.YesNo, MessageBoxImage.Question, quietAnswer: MessageBoxResult.Yes) != MessageBoxResult.Yes)
        {
            return;
        }
        int index = Circuit.Sheets.FindIndex(s => s.Id == id);
        BeginEdit();
        CircuitEditor.DeleteSheet(Circuit, id);
        CommitEdit();
        if (CurrentSheet == id)
        {
            CurrentSheet = Circuit.Sheets[Math.Clamp(index, 0, Circuit.Sheets.Count - 1)].Id;
            SheetChanged?.Invoke();
        }
        RebuildSheets();
        SelectionChanged();
    }

    // ---- Seçim ve özellikler ----

    private void RebuildSelection()
    {
        Params.Clear();
        Readouts.Clear();
        LabelChoices.Clear();
        Selected = Selection.Count == 1 && Selection.Components.Count == 1 ? Circuit?.FindComponent(Selection.Components.First()) : null;
        SelectedNote = Selection.Count == 1 && Selection.Texts.Count == 1 ? Circuit?.Texts.Find(t => t.Id == Selection.Texts.First()) : null;
        OnPropertyChanged(nameof(SelectedLabel));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(IsFollower));
        SelectionSummary = Selection.Count switch
        {
            0 => "",
            1 when Selection.Wires.Count == 1 => Loc.T("ui.sel.wire"),
            1 when Selection.Junctions.Count == 1 => Loc.T("ui.sel.junction"),
            _ when Selected == null && SelectedNote == null => Loc.T("ui.sel.many", Selection.Count),
            _ => ""
        };
        if (Selected == null || ComponentCatalog.Find(Selected.Type) is not { } def)
        {
            SelectedTypeName = "";
            SelectedInfo = "";
            return;
        }
        SelectedTypeName = def.Name;
        SelectedInfo = def.Info;
        if (def.Role == DeviceRole.Follower && Circuit != null)
        {
            foreach (string l in Labeler.MasterLabels(Circuit))
            {
                LabelChoices.Add(l);
            }
        }
        foreach (ParamDef p in def.Params)
        {
            Params.Add(new ParamItem(Selected, p, key => BeginEdit(key), CommitEdit));
        }
        UpdateReadouts();
    }

    /// <summary>Betiklerden ve aramadan: etikete göre seçer.</summary>
    public void SelectComponent(ComponentInstance? c)
    {
        Selection.Clear();
        if (c != null)
        {
            Selection.Components.Add(c.Id);
            if (c.Sheet != CurrentSheet)
            {
                ShowSheet(c.Sheet);
                Selection.Components.Add(c.Id);
            }
        }
        SelectionChanged();
        Frame?.Invoke();
    }

    private void UpdateReadouts()
    {
        var list = new List<ReadoutItem>();
        if (Simulator?.Solution is { } sol && Selected != null && Simulator.Find(Selected.Id) is { } sc)
        {
            foreach (Readout r in sc.Readouts(sol))
            {
                list.Add(new ReadoutItem(r.Name, r.Value));
            }
        }
        // Aynı satırlar yeniden kurulmasın: liste her 200 ms'de güncellenir, titreşmesin
        if (!list.SequenceEqual(Readouts))
        {
            Readouts.Clear();
            foreach (ReadoutItem r in list)
            {
                Readouts.Add(r);
            }
        }
        UpdateControls();
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
        CancelCanvasGesture?.Invoke();
        Log.Clear();
        Problems.Clear();
        logCount = 0;
        problemSignature = "";
        AnimTime = 0;
        hintKey = null;
        Simulator = new Simulator(Circuit);
        clampMax = 0;
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
        sound.Stop();
        IsPaused = false;
        SimTimeText = "";
        hintKey = null;
        Problems.Clear();
        ProblemCount = 0;
        RebuildSelection();
        UpdateMeters();
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
                Problems.Add(new ProblemItem(p.Message, p.Explanation, p.Severity, p.ComponentIds));
            }
            ProblemCount = Problems.Count;
        }
        if (readouts)
        {
            UpdateReadouts();
            UpdateMeters();
        }
        UpdateSound(sim);
        UpdateStatus();
        Frame?.Invoke();
    }

    private readonly SoundService sound = new();

    /// <summary>Korna, buzzer gibi elemanlar çalarken ses; ayarlardan kapatılabilir, betikle denemede çalmaz.</summary>
    private void UpdateSound(Simulator sim)
    {
        string? kind = null;
        if (Settings.Sound && !Settings.ReadOnly && sim.Status != SimStatus.Faulted)
        {
            kind = sim.Components.OfType<ISoundSource>().FirstOrDefault(s => s.SoundActive)?.SoundKind;
        }
        sound.Play(kind);
    }

    public void SetSound(bool on)
    {
        Settings.Sound = on;
        if (!on)
        {
            sound.Stop();
        }
        else if (Simulator != null)
        {
            UpdateSound(Simulator);
        }
    }

    private void UpdateStatus()
    {
        string? meterHint = Meter switch
        {
            MeterTool.Multimeter => "ui.hint.multimeter",
            MeterTool.Clamp => "ui.hint.clamp",
            _ => null
        };
        if (Simulator == null)
        {
            StatusText = Loc.T("ui.status.edit");
            HintText = Circuit == null ? "" : Loc.T(hintKey ?? meterHint ?? Tool switch
            {
                EditTool.Wire => "ui.hint.wireTool",
                EditTool.Text => "ui.hint.textTool",
                _ => "ui.hint.edit"
            });
            return;
        }
        StatusText = Loc.T(Simulator.Status switch
        {
            SimStatus.Paused => "ui.status.paused",
            SimStatus.Faulted => "ui.status.faulted",
            _ => "ui.status.running"
        });
        HintText = Loc.T(hintKey ?? meterHint ?? "ui.hint.sim");
    }

    public string AboutText => Loc.T("ui.aboutText",
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");
}
