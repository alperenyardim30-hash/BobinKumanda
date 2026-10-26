using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using BobinKumanda.App.Rendering;
using BobinKumanda.App.ViewModels;
using BobinKumanda.Core;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Editing;
using BobinKumanda.Core.Model;

namespace BobinKumanda.App;

/// <summary>
/// Arayüzü elle tıklamadan denemek için küçük komut dili (noktalı virgülle ayrılır). Yalnızca geliştirme içindir.
/// Dosya: open:yol; new; save:yol
/// Simülasyon: start; stop; press:ETİKET; release:ETİKET; click:ETİKET; run:saniye (anında); wait:saniye (gerçek)
/// Düzenleme: place:tip@x,y; wire:A.uç-B.uç (A/B etiket ya da kimlik); select:ETİKET; selectall; move:dx,dy;
///   rotate; mirror; delete; copy; paste; duplicate; undo; redo; tool:select|wire|text; ghost:tip; esc; sheet:add|N
/// Fare (dünya koordinatı, gerçek fareye dokunmaz): mclick:x,y; mdbl:x,y; mright:x,y; mdown/mmove/mup:x,y;
///   mdrag:x1,y1>x2,y2 — sonuna " ctrl" ya da " shift" eklenebilir
/// Denetim: expect:components=3 (components, wires, junctions, texts, sheets, selected, free, dirty, problems),
///   expect:meter=230.0 (multimetre göstergesi), expect:clamp=3.12, expect:active:K1=1 — uymazsa hata yazılır
/// Ölçüm: meter:multimeter|clamp|off; fn:ac|dc|ohm; slider:değer; action:sıra (sağ paneldeki düğme)
/// Görünüm: theme:dark|light; lang:tr|en; numbers:on|off; tab:0|1; ribbon:0..4; fit; search:yazı; shot:yol.png; quit
/// Hata olursa (bulunamayan eleman gibi) %LOCALAPPDATA%\BobinKumanda\logs\script.txt dosyasına yazılır.
/// </summary>
internal static class ScriptRunner
{
    public static async Task RunAsync(MainWindow w, MainViewModel vm, string script)
    {
        await Idle();
        var errors = new List<string>();
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
            try
            {
                if (!await RunOne(w, vm, cmd, arg))
                {
                    break;
                }
            }
            catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException or KeyNotFoundException or IOException)
            {
                errors.Add($"{cmd}:{arg} → {e.Message}");
            }
            catch (ExpectationFailed e)
            {
                errors.Add($"{cmd}:{arg} → {e.Message}");
            }
            await Idle();
        }
        errors.AddRange(Dialogs.Suppressed.Select(m => "uyarı kutusu: " + m));
        string log = Path.Combine(AppSettings.Folder, "logs", "script.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        File.WriteAllLines(log, errors.Count == 0 ? ["ok"] : errors);
    }

    /// <summary>false: betik bitti (quit).</summary>
    private static async Task<bool> RunOne(MainWindow w, MainViewModel vm, string cmd, string arg)
    {
        CircuitCanvas canvas = w.Canvas;
        switch (cmd)
        {
            case "open":
                vm.OpenFile(arg, askToDiscard: false);
                break;
            case "new":
                vm.NewCommand.Execute(null);
                break;
            case "save":
                vm.SaveDocument(Path.GetFullPath(arg));
                break;
            case "start":
                vm.StartSimulation();
                break;
            case "stop":
                vm.StopSimulation();
                break;
            case "press":
                vm.Press(Find(vm, arg));
                break;
            case "release":
                vm.Release(Find(vm, arg));
                break;
            case "click":
                ComponentInstance c = Find(vm, arg);
                vm.Press(c);
                vm.RunFor(0.05);
                vm.Release(c);
                break;
            case "run":
                vm.RunFor(Number(arg));
                break;
            case "wait":
                await Task.Delay(TimeSpan.FromSeconds(Number(arg)));
                break;
            case "place":
                string[] pt = arg.Split('@');
                _ = canvas.PlaceAt(pt[0].Trim(), Point(pt[1])) ?? throw new ArgumentException("yerleştirilemedi: " + arg);
                break;
            case "ghost":
                canvas.StartPlacing(arg);
                break;
            case "wire":
                Wire(vm, arg);
                break;
            case "select":
                w.SelectByLabel(arg);
                break;
            case "selectall":
                vm.SelectAllCommand.Execute(null);
                break;
            case "move":
                vm.Nudge(Point(arg));
                break;
            case "rotate":
                vm.RotateCommand.Execute(null);
                break;
            case "mirror":
                vm.MirrorCommand.Execute(null);
                break;
            case "delete":
                vm.DeleteCommand.Execute(null);
                break;
            case "copy":
                vm.CopyCommand.Execute(null);
                break;
            case "paste":
                vm.PasteCommand.Execute(null);
                break;
            case "duplicate":
                vm.DuplicateCommand.Execute(null);
                break;
            case "undo":
                vm.UndoCommand.Execute(null);
                break;
            case "redo":
                vm.RedoCommand.Execute(null);
                break;
            case "tool":
                vm.Tool = Enum.Parse<EditTool>(arg, ignoreCase: true);
                break;
            case "esc":
                canvas.CancelCurrent();
                break;
            case "sheet" when arg == "add":
                vm.AddSheetCommand.Execute(null);
                break;
            case "sheet":
                vm.ShowSheet(vm.Sheets[int.Parse(arg, CultureInfo.InvariantCulture) - 1].Id);
                break;
            case "search":
                vm.Palette.SearchText = arg;
                break;
            case "fit":
                canvas.ZoomToFit();
                break;
            case "mclick" or "mdbl" or "mright" or "mdown" or "mmove" or "mup":
                (Vec at, System.Windows.Input.ModifierKeys mods) = MouseArg(arg);
                switch (cmd)
                {
                    case "mclick":
                        canvas.SimulateMouse("click", at, mods);
                        break;
                    case "mdbl":
                        canvas.SimulateMouse("click", at, mods);
                        canvas.SimulateMouse("click", at, mods, clicks: 2);
                        break;
                    case "mright":
                        canvas.SimulateMouse("click", at, mods, System.Windows.Input.MouseButton.Right);
                        break;
                    default:
                        canvas.SimulateMouse(cmd[1..], at, mods);
                        break;
                }
                break;
            case "mdrag":
                string[] fromTo = arg.Split('>');
                (Vec from, System.Windows.Input.ModifierKeys dmods) = MouseArg(fromTo[0]);
                (Vec to, _) = MouseArg(fromTo[1]);
                canvas.SimulateMouse("move", from, dmods);
                canvas.SimulateMouse("down", from, dmods);
                for (int i = 1; i <= 6; i++)
                {
                    canvas.SimulateMouse("move", from + (to - from) * (i / 6.0), dmods);
                }
                canvas.SimulateMouse("up", to, dmods);
                break;
            case "expect":
                Expect(vm, arg);
                break;
            case "meter":
                vm.Meter = arg switch { "multimeter" => MeterTool.Multimeter, "clamp" => MeterTool.Clamp, _ => MeterTool.None };
                break;
            case "fn":
                vm.MeterFunction = arg switch { "dc" => Core.Simulation.MeterFunction.VoltsDc, "ohm" => Core.Simulation.MeterFunction.Ohms, _ => Core.Simulation.MeterFunction.VoltsAc };
                break;
            case "slider":
                (vm.Slider ?? throw new InvalidOperationException("kaydırıcı yok")).Value = Number(arg);
                break;
            case "action":
                vm.Actions[int.Parse(arg, CultureInfo.InvariantCulture)].Command.Execute(null);
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
            case "ribbon":
                w.ShowRibbonTab(int.Parse(arg, CultureInfo.InvariantCulture));
                break;
            case "shot":
                await Task.Delay(250);
                await Idle();
                w.SaveScreenshot(arg);
                break;
            case "quit":
                w.ForceClose();
                return false;
            default:
                throw new ArgumentException("bilinmeyen komut");
        }
        return true;
    }

    /// <summary>"K1.A1-H1.X2": iki klemens arasına otomatik yönlendirilmiş kablo (çizim alanındaki gibi).</summary>
    private static void Wire(MainViewModel vm, string arg)
    {
        string[] ends = arg.Split('-', 2);
        (ComponentInstance a, string ta) = Terminal(vm, ends[0]);
        (ComponentInstance b, string tb) = Terminal(vm, ends[1]);
        Circuit circuit = vm.Circuit!;
        ComponentDef da = ComponentCatalog.Get(a.Type), db = ComponentCatalog.Get(b.Type);
        TerminalDef t1 = da.FindTerminal(ta) ?? throw new ArgumentException("klemens yok: " + ends[0]);
        TerminalDef t2 = db.FindTerminal(tb) ?? throw new ArgumentException("klemens yok: " + ends[1]);
        List<Vec> pts = CircuitEditor.RouteBetween(circuit, vm.CurrentSheet, da.TerminalWorld(a, t1), da.TerminalSide(a, t1),
            db.TerminalWorld(b, t2), db.TerminalSide(b, t2));
        vm.BeginEdit();
        CircuitEditor.AddWire(circuit, vm.CurrentSheet, WireEnd.ToTerminal(a.Id, ta), WireEnd.ToTerminal(b.Id, tb), pts);
        vm.CommitEdit();
    }

    private static (ComponentInstance, string) Terminal(MainViewModel vm, string spec)
    {
        int dot = spec.LastIndexOf('.');
        return (Find(vm, spec[..dot].Trim()), spec[(dot + 1)..].Trim());
    }

    /// <summary>Önce kimliğe (c12), sonra etikete göre; aynı etiketli birden çok eleman varsa ilki.</summary>
    private static ComponentInstance Find(MainViewModel vm, string key) =>
        vm.Circuit?.FindComponent(key) ??
        vm.Circuit?.Components.FirstOrDefault(c => string.Equals(c.Label, key, StringComparison.OrdinalIgnoreCase)) ??
        throw new KeyNotFoundException("eleman yok: " + key);

    private sealed class ExpectationFailed(string message) : Exception(message);

    private static void Expect(MainViewModel vm, string arg)
    {
        string[] kv = arg.Split('=');
        Circuit c = vm.Circuit ?? throw new ExpectationFailed("devre yok");
        string key = kv[0].Trim();
        if (key is "meter" or "clamp")
        {
            string shown = key == "meter" ? vm.MeterValue : vm.ClampValue;
            if (shown != kv[1].Trim())
            {
                throw new ExpectationFailed($"gösterge {shown}, beklenen {kv[1].Trim()}");
            }
            return;
        }
        if (key.StartsWith("active:", StringComparison.Ordinal))
        {
            bool on = vm.Simulator?.Find(Find(vm, key[7..]).Id)?.Visual.Active == true;
            if (on != (kv[1].Trim() == "1"))
            {
                throw new ExpectationFailed($"{key[7..]} {(on ? "etkin" : "etkin değil")}");
            }
            return;
        }
        string sheet = vm.CurrentSheet;
        int actual = kv[0].Trim() switch
        {
            "components" => c.Components.Count(x => x.Sheet == sheet),
            "wires" => c.Wires.Count(x => x.Sheet == sheet),
            "junctions" => c.Junctions.Count(x => x.Sheet == sheet),
            "texts" => c.Texts.Count(x => x.Sheet == sheet),
            "sheets" => c.Sheets.Count,
            "selected" => vm.Selection.Count,
            "free" => c.Wires.Where(x => x.Sheet == sheet).Sum(x => (x.A.IsFree ? 1 : 0) + (x.B.IsFree ? 1 : 0)),
            "dirty" => vm.IsDirty ? 1 : 0,
            "problems" => vm.ProblemCount,
            _ => throw new ArgumentException("bilinmeyen ölçü")
        };
        int expected = int.Parse(kv[1].Trim(), CultureInfo.InvariantCulture);
        if (actual != expected)
        {
            throw new ExpectationFailed($"beklenen {expected}, bulunan {actual}");
        }
    }

    /// <summary>"120,40" ya da "120,40 ctrl".</summary>
    private static (Vec, System.Windows.Input.ModifierKeys) MouseArg(string s)
    {
        string[] parts = s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var mods = System.Windows.Input.ModifierKeys.None;
        foreach (string m in parts.Skip(1))
        {
            mods |= m.ToLowerInvariant() switch
            {
                "ctrl" => System.Windows.Input.ModifierKeys.Control,
                "shift" => System.Windows.Input.ModifierKeys.Shift,
                _ => throw new ArgumentException("bilinmeyen tuş: " + m)
            };
        }
        return (Point(parts[0]), mods);
    }

    private static Vec Point(string s)
    {
        string[] p = s.Split(',');
        return new Vec(Number(p[0]), Number(p[1]));
    }

    private static double Number(string s) => double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static async Task Idle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
}
