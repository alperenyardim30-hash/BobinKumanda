using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KumandaLab.App.Rendering;
using KumandaLab.App.ViewModels;
using KumandaLab.Core.Localization;
using KumandaLab.Core.Model;

namespace KumandaLab.App;

public partial class MainWindow
{
    private readonly MainViewModel vm;
    private readonly AppSettings settings;
    private bool loading = true;
    private bool forceClose;
    private bool scrollPending;

    public MainWindow(MainViewModel vm)
    {
        this.vm = vm;
        settings = vm.Settings;
        InitializeComponent();
        DataContext = vm;

        numbersCheck.IsChecked = settings.TerminalNumbers;
        gridCheck.IsChecked = settings.Grid;
        flowCheck.IsChecked = settings.Flow;
        darkToggle.IsChecked = settings.DarkTheme;
        languageBox.SelectedIndex = settings.Language == "en" ? 1 : 0;
        loading = false;
        ApplyViewOptions();
        ApplyTheme(settings.DarkTheme);

        vm.CircuitLoaded += () => canvas.Circuit = vm.Circuit;
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
        canvas.SelectionChanged += vm.SetSelection;
        canvas.ViewChanged += UpdateZoomText;
        PreviewKeyDown += OnPreviewKey;
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

    private void OnPreviewKey(object sender, KeyEventArgs e)
    {
        // F10 Windows'ta menü tuşudur; KeyBinding'e ulaşmaz
        if (e.Key == Key.System && e.SystemKey == Key.F10)
        {
            vm.StepCommand.Execute(null);
            e.Handled = true;
        }
        else if (Keyboard.FocusedElement is not TextBox && Keyboard.Modifiers == ModifierKeys.None)
        {
            switch (e.Key)
            {
                case Key.Add or Key.OemPlus:
                    canvas.ZoomBy(1.25);
                    e.Handled = true;
                    break;
                case Key.Subtract or Key.OemMinus:
                    canvas.ZoomBy(0.8);
                    e.Handled = true;
                    break;
                case Key.Home:
                    canvas.ZoomToFit();
                    e.Handled = true;
                    break;
            }
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!forceClose && !vm.ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }
        vm.StopSimulation();
        settings.Save();
    }

    public void ForceClose()
    {
        forceClose = true;
        Close();
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnRecentClick(object sender, RoutedEventArgs e) => backstage.IsOpen = false;

    private void OnFitClick(object sender, RoutedEventArgs e) => canvas.ZoomToFit();

    private void OnZoomInClick(object sender, RoutedEventArgs e) => canvas.ZoomBy(1.25);

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => canvas.ZoomBy(0.8);

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
        canvas.ShowTerminalNumbers = settings.TerminalNumbers;
        canvas.ShowGrid = settings.Grid;
        canvas.ShowFlow = settings.Flow;
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

    public void SetTerminalNumbers(bool on) => numbersCheck.IsChecked = on;

    public void SelectByLabel(string label)
    {
        ComponentInstance? c = vm.Circuit?.Components.FirstOrDefault(x => string.Equals(x.Label, label, StringComparison.OrdinalIgnoreCase));
        canvas.Select(c);
    }

    public void ShowBottomTab(int index) => bottomTabs.SelectedIndex = index;

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
