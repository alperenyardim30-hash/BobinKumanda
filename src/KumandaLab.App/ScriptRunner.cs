using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Threading;
using KumandaLab.App.ViewModels;
using KumandaLab.Core.Model;

namespace KumandaLab.App;

/// <summary>
/// Arayüzü elle tıklamadan denemek için küçük komut dili (noktalı virgülle ayrılır):
/// open:yol; start; stop; press:ETİKET; release:ETİKET; click:ETİKET; run:saniye (simülasyon saati, anında);
/// wait:saniye (gerçek bekleme); select:ETİKET; theme:dark|light; lang:tr|en; numbers:on|off; tab:0|1; shot:yol.png; quit
/// </summary>
internal static class ScriptRunner
{
    public static async Task RunAsync(MainWindow w, MainViewModel vm, string script)
    {
        await Idle();
        foreach (string raw in script.Split(';'))
        {
            string cmd = raw.Trim();
            if (cmd.Length == 0)
            {
                continue;
            }
            string arg = "";
            int colon = cmd.IndexOf(':');
            if (colon > 0)
            {
                arg = cmd[(colon + 1)..].Trim();
                cmd = cmd[..colon].Trim();
            }
            switch (cmd)
            {
                case "open":
                    vm.OpenFile(arg, askToDiscard: false);
                    break;
                case "start":
                    vm.StartSimulation();
                    break;
                case "stop":
                    vm.StopSimulation();
                    break;
                case "press" when Find(vm, arg) is { } c:
                    vm.Press(c);
                    break;
                case "release" when Find(vm, arg) is { } c:
                    vm.Release(c);
                    break;
                case "click" when Find(vm, arg) is { } c:
                    vm.Press(c);
                    vm.RunFor(0.05);
                    vm.Release(c);
                    break;
                case "run":
                    vm.RunFor(Seconds(arg));
                    break;
                case "wait":
                    await Task.Delay(TimeSpan.FromSeconds(Seconds(arg)));
                    break;
                case "select":
                    w.SelectByLabel(arg);
                    break;
                case "theme":
                    w.ApplyTheme(arg == "dark");
                    break;
                case "lang":
                    w.SetLanguage(arg);
                    break;
                case "numbers":
                    w.SetTerminalNumbers(arg == "on");
                    break;
                case "tab":
                    w.ShowBottomTab(int.Parse(arg, CultureInfo.InvariantCulture));
                    break;
                case "shot":
                    await Task.Delay(250);
                    await Idle();
                    w.SaveScreenshot(arg);
                    break;
                case "quit":
                    w.ForceClose();
                    return;
            }
            await Idle();
        }
    }

    private static ComponentInstance? Find(MainViewModel vm, string label) =>
        vm.Circuit?.Components.FirstOrDefault(c => string.Equals(c.Label, label, StringComparison.OrdinalIgnoreCase));

    private static double Seconds(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static async Task Idle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
}
