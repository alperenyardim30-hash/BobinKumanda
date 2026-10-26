using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ControlzEx.Theming;
using BobinKumanda.App.ViewModels;
using BobinKumanda.Core.Localization;

namespace BobinKumanda.App;

/// <summary>
/// Başlangıç: ayarlar, dil, tema ve ana pencere. Komut satırı: <c>BobinKumanda.exe [dosya.bobin|dosya.esc] [--script "komutlar"]</c>.
/// Betik yalnızca geliştirme sırasında arayüzü denemek içindir (bkz. <see cref="ScriptRunner"/>).
/// </summary>
public partial class App : Application
{
    public const string ProductName = "Bobin Kumanda";

    private bool scripted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            ReportCrash(ex.Exception);
            ex.Handled = !scripted;
            if (scripted)
            {
                Shutdown(3);
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => ReportCrash(ex.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            ReportCrash(ex.Exception);
            ex.SetObserved();
        };

        string? file = null, script = null;
        for (int i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--script" && i + 1 < e.Args.Length)
            {
                script = e.Args[++i];
            }
            else if (!e.Args[i].StartsWith("--", StringComparison.Ordinal))
            {
                file = e.Args[i];
            }
        }
        scripted = script != null;

        AppSettings settings = AppSettings.Load();
        settings.ReadOnly = scripted;
        Loc.Language = settings.Language;
        var vm = new MainViewModel(settings);
        var window = new MainWindow(vm);
        MainWindow = window;
        window.Show();
        if (file != null && File.Exists(file))
        {
            vm.OpenFile(file, askToDiscard: false);
        }
        else if (script == null)
        {
            window.Dispatcher.BeginInvoke(window.OfferRecovery, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        if (script != null)
        {
            _ = ScriptRunner.RunAsync(window, vm, script);
        }
    }

    /// <summary>Fluent şerit temasını ve panellerin renklerini değiştirir.</summary>
    public static void ApplyTheme(bool dark)
    {
        Application app = Current;
        ThemeManager.Current.ChangeTheme(app, dark ? "Dark.Blue" : "Light.Blue");
        void Set(string key, int rgb) => app.Resources[key] = Frozen(rgb);
        Set("App.Background", dark ? 0x17191E : 0xEEF0F3);
        Set("App.Panel", dark ? 0x23262D : 0xFFFFFF);
        Set("App.Border", dark ? 0x353A43 : 0xD8DCE2);
        Set("App.Input", dark ? 0x1B1E24 : 0xFFFFFF);
        Set("App.Hover", dark ? 0x2E333C : 0xE8ECF2);
        Set("App.Text", dark ? 0xE6E8EE : 0x1F2430);
        Set("App.TextMuted", dark ? 0x9AA0AA : 0x5C6370);
        Set("App.Accent", dark ? 0x4DABF7 : 0x1C7ED6);
        Set("App.Error", dark ? 0xFF6B6B : 0xD9363E);
        Set("App.Warning", dark ? 0xFFC078 : 0xB35C00);
    }

    private static SolidColorBrush Frozen(int rgb)
    {
        var b = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        b.Freeze();
        return b;
    }

    private bool reporting;

    private void ReportCrash(Exception? ex)
    {
        if (ex == null || reporting)
        {
            return;
        }
        reporting = true;
        string path = Path.Combine(AppSettings.Folder, "logs", $"hata-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"{ProductName} {typeof(App).Assembly.GetName().Version}\n{DateTime.Now:O}\n{Environment.OSVersion}\n\n{ex}", Encoding.UTF8);
        }
        catch (IOException)
        {
            path = "";
        }
        catch (UnauthorizedAccessException)
        {
            path = "";
        }
        if (!scripted)
        {
            MessageBox.Show($"{Loc.T("ui.crash")}\n{path}\n\n{ex.Message}", ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        reporting = false;
    }
}
