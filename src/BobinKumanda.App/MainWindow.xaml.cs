using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BobinKumanda.App.Rendering;
using BobinKumanda.App.ViewModels;
using BobinKumanda.Core;
using BobinKumanda.Core.Editing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;

namespace BobinKumanda.App;

public partial class MainWindow
{
    private readonly MainViewModel vm;
    private readonly AppSettings settings;
    private bool loading = true;
    private bool forceClose;
    private bool scrollPending;
    private Point paletteDown;
    private string? paletteDownType;

    public MainWindow(MainViewModel vm)
    {
        this.vm = vm;
        settings = vm.Settings;
        InitializeComponent();
        DataContext = vm;

        numbersCheck.IsChecked = settings.TerminalNumbers;
        gridCheck.IsChecked = settings.Grid;
        flowCheck.IsChecked = settings.Flow;
        paletteCheck.IsChecked = settings.ShowPalette;
        darkToggle.IsChecked = settings.DarkTheme;
        languageBox.SelectedIndex = settings.Language == "en" ? 1 : 0;
        loading = false;
        ApplyViewOptions();
        ApplyTheme(settings.DarkTheme);
        BuildInsertTab();

        canvas.Host = vm;
        canvas.Selection = vm.Selection;
        vm.PasteTarget = () => canvas.MouseWorld;
        vm.CircuitLoaded += () =>
        {
            canvas.Sheet = vm.CurrentSheet;
            canvas.Circuit = vm.Circuit;
        };
        vm.CircuitReplaced += () => canvas.ReplaceCircuit(vm.Circuit!);
        vm.CircuitEdited += canvas.InvalidateCircuit;
        vm.CancelCanvasGesture += () => canvas.CancelCurrent();
        vm.SheetChanged += () =>
        {
            if (canvas.Sheet != vm.CurrentSheet)
            {
                canvas.Sheet = vm.CurrentSheet;
                canvas.ZoomToFit();
            }
            canvas.InvalidateVisual();
        };
        vm.Frame += () =>
        {
            canvas.AnimTime = vm.AnimTime;
            canvas.InvalidateVisual();
        };
        vm.PropertyChanged += OnViewModelChanged;
        vm.Log.CollectionChanged += (_, e) =>
        {
            // Liste güncellenirken kaydırmak WPF'i bozar; güncelleme bitince bir kez kaydır
            if (e.Action == NotifyCollectionChangedAction.Add && !scrollPending)
            {
                scrollPending = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    scrollPending = false;
                    if (vm.Log.Count > 0)
                    {
                        logList.ScrollIntoView(vm.Log[^1]);
                    }
                });
            }
        };
        canvas.ComponentPressed += vm.Press;
        canvas.ComponentReleased += vm.Release;
        canvas.ViewChanged += UpdateZoomText;
        canvas.ContextMenuRequested += ShowCanvasMenu;
        PreviewKeyDown += OnPreviewKey;
        PreviewKeyUp += OnPreviewKeyUp;
        Deactivated += (_, _) => canvas.SpacePan = false;
        Closing += OnClosing;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Simulator):
                canvas.Simulator = vm.Simulator;
                break;
            case nameof(MainViewModel.ProblemCount):
                // Hata çıkınca sorunlar sekmesini öne getir
                if (vm.Problems.Any(p => p.IsError))
                {
                    bottomTabs.SelectedItem = problemsTab;
                }
                break;
        }
    }

    private void UpdateZoomText() =>
        zoomText.Text = string.Format(CultureInfo.InvariantCulture, "%{0:0}", canvas.Zoom * 100);

    // ---- Klavye ----

    private static bool TypingInText() =>
        Keyboard.FocusedElement is TextBoxBase or PasswordBox || Keyboard.FocusedElement is ComboBox { IsEditable: true };

    private void OnPreviewKey(object sender, KeyEventArgs e)
    {
        // F10 Windows'ta menü tuşudur; KeyBinding'e ulaşmaz
        if (e.Key == Key.System && e.SystemKey == Key.F10)
        {
            vm.StepCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FocusPaletteSearch();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FocusPaletteSearch();
            e.Handled = true;
            return;
        }
        if (TypingInText() || vm.Circuit == null)
        {
            return;
        }
        ModifierKeys mods = Keyboard.Modifiers;
        if (e.Key == Key.Space && mods == ModifierKeys.None && (canvas.IsMouseOver || canvas.IsKeyboardFocused))
        {
            canvas.SpacePan = true;
            e.Handled = true;
            return;
        }
        if (mods is not (ModifierKeys.None or ModifierKeys.Shift))
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Escape:
                if (!canvas.CancelCurrent())
                {
                    if (vm.Tool != EditTool.Select)
                    {
                        vm.Tool = EditTool.Select;
                    }
                    else if (!vm.Selection.IsEmpty)
                    {
                        vm.Selection.Clear();
                        vm.SelectionChanged();
                        canvas.InvalidateVisual();
                    }
                }
                e.Handled = true;
                return;
            case Key.Delete or Key.Back:
                vm.DeleteCommand.Execute(null);
                e.Handled = true;
                return;
            case Key.Add or Key.OemPlus:
                canvas.ZoomBy(1.25);
                e.Handled = true;
                return;
            case Key.Subtract or Key.OemMinus:
                canvas.ZoomBy(0.8);
                e.Handled = true;
                return;
            case Key.Home:
                canvas.ZoomToFit();
                e.Handled = true;
                return;
            case Key.Left or Key.Right or Key.Up or Key.Down when !vm.Selection.IsEmpty && vm.IsEditing:
                double step = CircuitEditor.Grid;
                vm.Nudge(e.Key switch
                {
                    Key.Left => new Vec(-step, 0),
                    Key.Right => new Vec(step, 0),
                    Key.Up => new Vec(0, -step),
                    _ => new Vec(0, step)
                });
                e.Handled = true;
                return;
        }
        if (mods != ModifierKeys.None || !vm.IsEditing)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.R:
                if (!canvas.RotateGhost())
                {
                    vm.RotateCommand.Execute(null);
                }
                e.Handled = true;
                return;
            case Key.F:
                if (!canvas.MirrorGhost())
                {
                    vm.MirrorCommand.Execute(null);
                }
                e.Handled = true;
                return;
            case Key.W:
                vm.Tool = vm.Tool == EditTool.Wire ? EditTool.Select : EditTool.Wire;
                e.Handled = true;
                return;
            case Key.T:
                vm.Tool = vm.Tool == EditTool.Text ? EditTool.Select : EditTool.Text;
                e.Handled = true;
                return;
        }
        if (e.Key is >= Key.A and <= Key.Z && vm.Palette.ByShortcut((char)('A' + (e.Key - Key.A))) is { } item)
        {
            vm.Tool = EditTool.Select;
            canvas.StartPlacing(item.TypeId);
            e.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && canvas.SpacePan)
        {
            canvas.SpacePan = false;
            e.Handled = true;
        }
    }

    private void FocusPaletteSearch()
    {
        if (!settings.ShowPalette)
        {
            paletteCheck.IsChecked = true;
        }
        paletteSearch.Focus();
        paletteSearch.SelectAll();
    }

    private void OnPaletteSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && vm.Palette.FirstMatch is { } first)
        {
            canvas.StartPlacing(first.TypeId);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.Palette.SearchText = "";
            canvas.Focus();
            e.Handled = true;
        }
    }

    // ---- Palet ----

    private void OnPaletteItemDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string type })
        {
            paletteDown = e.GetPosition(this);
            paletteDownType = type;
            e.Handled = true;
        }
    }

    private void OnPaletteItemMove(object sender, MouseEventArgs e)
    {
        if (paletteDownType == null || e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement fe)
        {
            return;
        }
        if ((e.GetPosition(this) - paletteDown).Length < 5)
        {
            return;
        }
        string type = paletteDownType;
        paletteDownType = null;
        DragDrop.DoDragDrop(fe, new DataObject(CircuitCanvas.DragFormat, type), DragDropEffects.Copy);
    }

    private void OnPaletteItemUp(object sender, MouseButtonEventArgs e)
    {
        // Sürüklemeden tıklandı: eleman fareyle birlikte gezer, çizim alanına tıklayınca yerleşir
        if (paletteDownType != null && sender is FrameworkElement { Tag: string type } && type == paletteDownType)
        {
            vm.Tool = EditTool.Select;
            canvas.StartPlacing(type);
        }
        paletteDownType = null;
    }

    /// <summary>Ekle sekmesi: her kategori için bir açılır liste.</summary>
    private void BuildInsertTab()
    {
        insertGroup.Items.Clear();
        foreach (PaletteGroup g in vm.Palette.Groups)
        {
            var drop = new Fluent.DropDownButton { Size = Fluent.RibbonControlSize.Large };
            drop.SetBinding(Fluent.DropDownButton.HeaderProperty, new Binding(nameof(PaletteGroup.Title)) { Source = g });
            if (g.Items.FirstOrDefault() is { } firstItem)
            {
                var icon = new Image { Width = 32, Height = 32, Stretch = Stretch.Uniform };
                icon.SetBinding(Image.SourceProperty, new Binding(nameof(PaletteItem.Icon)) { Source = firstItem });
                drop.LargeIcon = icon;
            }
            foreach (PaletteItem item in g.Items)
            {
                var image = new Image { Width = 28, Height = 20, Stretch = Stretch.Uniform };
                image.SetBinding(Image.SourceProperty, new Binding(nameof(PaletteItem.Icon)) { Source = item });
                var mi = new Fluent.MenuItem { Icon = image };
                mi.SetBinding(HeaderedItemsControl.HeaderProperty, new Binding(nameof(PaletteItem.Name)) { Source = item });
                if (item.HasShortcut)
                {
                    mi.InputGestureText = item.Shortcut;
                }
                string type = item.TypeId;
                mi.Click += (_, _) =>
                {
                    vm.Tool = EditTool.Select;
                    canvas.StartPlacing(type);
                };
                drop.Items.Add(mi);
            }
            insertGroup.Items.Add(drop);
        }
    }

    // ---- Sağ tık menüsü ----

    private void ShowCanvasMenu(Hit hit)
    {
        var menu = new ContextMenu();
        MenuItem Item(string key, ICommand command, string? gesture = null, string? glyph = null)
        {
            var mi = new MenuItem { Header = Loc.T(key), Command = command, InputGestureText = gesture ?? "" };
            if (glyph != null)
            {
                mi.Icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14 };
            }
            return mi;
        }
        if (vm.IsSimulating)
        {
            menu.Items.Add(Item("ui.stop", vm.ToggleSimulationCommand, "F5", ""));
        }
        else if (!hit.IsNone)
        {
            menu.Items.Add(Item("ui.rotate", vm.RotateCommand, "R", ""));
            menu.Items.Add(Item("ui.mirror", vm.MirrorCommand, "F", ""));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("ui.cut", vm.CutCommand, "Ctrl+X", ""));
            menu.Items.Add(Item("ui.copy", vm.CopyCommand, "Ctrl+C", ""));
            menu.Items.Add(Item("ui.duplicate", vm.DuplicateCommand, "Ctrl+D"));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("ui.delete", vm.DeleteCommand, "Del", ""));
        }
        else
        {
            menu.Items.Add(Item("ui.paste", vm.PasteCommand, "Ctrl+V", ""));
            menu.Items.Add(Item("ui.selectAll", vm.SelectAllCommand, "Ctrl+A", ""));
            menu.Items.Add(new Separator());
            // Ekle ▸ kategori ▸ eleman
            var insert = new MenuItem { Header = Loc.T("ui.insert") };
            Vec? at = canvas.MouseWorld;
            foreach (PaletteGroup g in vm.Palette.Groups)
            {
                var cat = new MenuItem { Header = g.Title };
                foreach (PaletteItem item in g.Items)
                {
                    string type = item.TypeId;
                    var mi = new MenuItem
                    {
                        Header = item.Name,
                        Icon = new Image { Source = item.Icon, Width = 26, Height = 18, Stretch = Stretch.Uniform },
                        InputGestureText = item.Shortcut
                    };
                    mi.Click += (_, _) =>
                    {
                        if (at is { } p)
                        {
                            canvas.PlaceAt(type, p);
                        }
                        else
                        {
                            canvas.StartPlacing(type);
                        }
                    };
                    cat.Items.Add(mi);
                }
                insert.Items.Add(cat);
            }
            menu.Items.Add(insert);
            MenuItem text = Item("ui.toolText", vm.SetToolCommand, "T", "");
            text.CommandParameter = "Text";
            menu.Items.Add(text);
        }
        menu.PlacementTarget = canvas;
        menu.IsOpen = true;
    }

    // ---- Sayfa sekmeleri ----

    private void OnSheetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            vm.ShowSheet(id);
        }
    }

    private void OnSheetDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            RenameSheet(id);
            e.Handled = true;
        }
    }

    private void OnSheetRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } fe)
        {
            return;
        }
        var menu = new ContextMenu { PlacementTarget = fe };
        var rename = new MenuItem { Header = Loc.T("ui.renameSheet") };
        rename.Click += (_, _) => RenameSheet(id);
        var delete = new MenuItem { Header = Loc.T("ui.deleteSheet"), IsEnabled = vm.Sheets.Count > 1 };
        delete.Click += (_, _) => vm.DeleteSheet(id);
        menu.Items.Add(rename);
        menu.Items.Add(delete);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void RenameSheet(string id)
    {
        if (!vm.IsEditing || vm.Circuit?.FindSheet(id) is not { } sheet)
        {
            return;
        }
        string? name = InputDialog.Ask(this, Loc.T("ui.renameSheet"), Loc.T("ui.sheetName"),
            vm.Sheets.FirstOrDefault(s => s.Id == id)?.Title ?? sheet.Name);
        if (name != null)
        {
            vm.RenameSheet(id, name);
        }
    }

    // ---- Kapatma ve kurtarma ----

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!forceClose && !vm.ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }
        vm.Shutdown();
        settings.Save();
    }

    public void ForceClose()
    {
        forceClose = true;
        Close();
    }

    /// <summary>Önceki oturum beklenmedik şekilde kapandıysa kaydedilmemiş devreyi geri getirmeyi önerir.</summary>
    public void OfferRecovery()
    {
        List<Autosave.Orphan> orphans = Autosave.FindOrphans();
        if (orphans.Count == 0)
        {
            return;
        }
        Autosave.Orphan latest = orphans[0];
        string when = latest.SavedAt.ToString("g", Loc.Culture);
        string name = latest.OriginalPath != null ? Path.GetFileName(latest.OriginalPath) : latest.Name;
        MessageBoxResult r = MessageBox.Show(this, Loc.T("ui.recover", name, when), App.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes)
        {
            vm.Recover(latest);
        }
        else
        {
            Autosave.Discard(latest);
        }
        // Daha eskiler (aynı anda açık başka pencerelerden kalanlar) sessizce atılmaz; bir sonraki açılışta yine sorulur
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnRecentClick(object sender, RoutedEventArgs e) => backstage.IsOpen = false;

    private void OnFitClick(object sender, RoutedEventArgs e) => canvas.ZoomToFit();

    private void OnZoomInClick(object sender, RoutedEventArgs e) => canvas.ZoomBy(1.25);

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => canvas.ZoomBy(0.8);

    // ---- Görünüm ----

    private void OnViewOptionChanged(object sender, RoutedEventArgs e)
    {
        if (!loading)
        {
            ApplyViewOptions();
        }
    }

    private void ApplyViewOptions()
    {
        settings.TerminalNumbers = numbersCheck.IsChecked == true;
        settings.Grid = gridCheck.IsChecked == true;
        settings.Flow = flowCheck.IsChecked == true;
        settings.ShowPalette = paletteCheck.IsChecked == true;
        canvas.ShowTerminalNumbers = settings.TerminalNumbers;
        canvas.ShowGrid = settings.Grid;
        canvas.ShowFlow = settings.Flow;
        palettePanel.Visibility = settings.ShowPalette ? Visibility.Visible : Visibility.Collapsed;
        paletteSplitter.Visibility = palettePanel.Visibility;
        paletteColumn.MinWidth = settings.ShowPalette ? 160 : 0;
        paletteColumn.Width = settings.ShowPalette ? new GridLength(230) : new GridLength(0);
        paletteSplitColumn.Width = settings.ShowPalette ? new GridLength(5) : new GridLength(0);
        canvas.InvalidateVisual();
    }

    private void OnThemeChanged(object sender, RoutedEventArgs e)
    {
        if (!loading)
        {
            ApplyTheme(darkToggle.IsChecked == true);
        }
    }

    public void ApplyTheme(bool dark)
    {
        settings.DarkTheme = dark;
        if (darkToggle.IsChecked != dark)
        {
            darkToggle.IsChecked = dark;
        }
        App.ApplyTheme(dark);
        canvas.Theme = dark ? CanvasTheme.Dark : CanvasTheme.Light;
        vm.Palette.SetTheme(canvas.Theme);
        canvas.InvalidateVisual();
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loading && languageBox.SelectedItem is ComboBoxItem { Tag: string lang })
        {
            SetLanguage(lang);
        }
    }

    public void SetLanguage(string lang)
    {
        settings.Language = lang;
        Loc.Language = lang;
        int index = lang == "en" ? 1 : 0;
        if (languageBox.SelectedIndex != index)
        {
            languageBox.SelectedIndex = index;
        }
        canvas.InvalidateVisual();
    }

    // ---- Betik desteği ----

    public CircuitCanvas Canvas => canvas;

    public void SetTerminalNumbers(bool on) => numbersCheck.IsChecked = on;

    public void SelectByLabel(string label)
    {
        ComponentInstance? c = vm.Circuit?.Components.FirstOrDefault(x => string.Equals(x.Label, label, StringComparison.OrdinalIgnoreCase));
        vm.SelectComponent(c);
    }

    public void ShowBottomTab(int index) => bottomTabs.SelectedIndex = index;

    public void ShowRibbonTab(int index) => ribbon.SelectedTabIndex = index;

    /// <summary>Pencerenin görüntüsünü PNG olarak kaydeder (deneme betikleri için).</summary>
    public void SaveScreenshot(string path)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        var root = (FrameworkElement)Content;
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Background ?? Brushes.White, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        rtb.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using FileStream fs = File.Create(path);
        encoder.Save(fs);
    }
}
