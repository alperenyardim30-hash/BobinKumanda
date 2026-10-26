using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.App.ViewModels;

public sealed record LogItem(string Time, string Text, ProblemSeverity Severity)
{
    public bool IsWarning => Severity == ProblemSeverity.Warning;
    public bool IsError => Severity == ProblemSeverity.Error;
}

public sealed record ProblemItem(string Message, string Explanation, ProblemSeverity Severity, IReadOnlyList<string> ComponentIds)
{
    public bool HasComponent => ComponentIds.Count > 0;

    public bool IsError => Severity == ProblemSeverity.Error;
    public string Glyph => Severity == ProblemSeverity.Error ? "" : Severity == ProblemSeverity.Warning ? "" : "";
}

public sealed record ReadoutItem(string Name, string Value);

public sealed record ChoiceItem(string Value, string Text)
{
    public override string ToString() => Text;
}

/// <summary>
/// Özellikler panelindeki bir parametre satırı. Değer değişince elemana yazar; yazmadan önce <c>before</c> çağrılır
/// (geri alma noktası, anahtarıyla: aynı kutuya art arda yazılanlar tek adım olur).
/// </summary>
public sealed partial class ParamItem : ObservableObject
{
    private readonly ComponentInstance instance;
    private readonly Action<string> before;
    private readonly Action changed;

    public ParamItem(ComponentInstance instance, ParamDef def, Action<string> before, Action changed)
    {
        this.instance = instance;
        this.before = before;
        this.changed = changed;
        Def = def;
        Name = Loc.TryT(def.NameKey) ?? def.Key;
        Unit = def.Unit ?? "";
        Choices = def.Choices?.Select(c => new ChoiceItem(c, Loc.TryT(def.ChoiceNameKey(c)) ?? c)).ToList() ?? [];
        string raw = instance.GetParam(def.Key, def.Default);
        text = def.Kind == ParamKind.Number && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? d.ToString("0.###", Loc.Culture)
            : raw;
        selectedChoice = Choices.FirstOrDefault(c => c.Value == raw);
        isChecked = bool.TryParse(raw, out bool b) && b;
    }

    public ParamDef Def { get; }

    /// <summary>Geri almada birleştirme anahtarı: hangi elemanın hangi değeri.</summary>
    private string Key => "param:" + instance.Id + ":" + Def.Key;
    public string Name { get; }
    public string Unit { get; }
    public IReadOnlyList<ChoiceItem> Choices { get; }

    public bool IsChoice => Def.Kind == ParamKind.Choice;
    public bool IsBool => Def.Kind == ParamKind.Bool;
    public bool IsText => Def.Kind is ParamKind.Number or ParamKind.Integer or ParamKind.Text;

    private string text;
    private ChoiceItem? selectedChoice;
    private bool isChecked;

    [ObservableProperty]
    public partial bool IsInvalid { get; private set; }

    public string Text
    {
        get => text;
        set
        {
            if (!SetProperty(ref text, value))
            {
                return;
            }
            string s = value.Trim().Replace(',', '.');
            switch (Def.Kind)
            {
                case ParamKind.Number when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d):
                    before(Key);
                    instance.SetParam(Def.Key, Math.Clamp(d, Def.Min, Def.Max > Def.Min ? Def.Max : double.MaxValue));
                    IsInvalid = d < Def.Min || (Def.Max > Def.Min && d > Def.Max);
                    break;
                case ParamKind.Integer when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i):
                    before(Key);
                    instance.SetParam(Def.Key, Math.Clamp(i, (int)Def.Min, Def.Max > Def.Min ? (int)Def.Max : int.MaxValue).ToString(CultureInfo.InvariantCulture));
                    IsInvalid = i < Def.Min || (Def.Max > Def.Min && i > Def.Max);
                    break;
                case ParamKind.Text:
                    before(Key);
                    instance.SetParam(Def.Key, value);
                    IsInvalid = false;
                    break;
                default:
                    IsInvalid = true;
                    return;
            }
            changed();
        }
    }

    public ChoiceItem? SelectedChoice
    {
        get => selectedChoice;
        set
        {
            if (SetProperty(ref selectedChoice, value) && value != null)
            {
                before(Key);
                instance.SetParam(Def.Key, value.Value);
                changed();
            }
        }
    }

    public bool IsChecked
    {
        get => isChecked;
        set
        {
            if (SetProperty(ref isChecked, value))
            {
                before(Key);
                instance.SetParam(Def.Key, value ? "True" : "False");
                changed();
            }
        }
    }
}

/// <summary>Alttaki sayfa sekmelerinden biri.</summary>
public sealed partial class SheetItem(Sheet sheet, int index) : ObservableObject
{
    public Sheet Sheet { get; } = sheet;
    public string Id => Sheet.Id;
    public string Title => Sheet.Name.Length > 0 ? Sheet.Name : Loc.T("ui.sheetN", index + 1);

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}
