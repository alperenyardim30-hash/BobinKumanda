using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using KumandaLab.Core.Catalog;
using KumandaLab.Core.Localization;
using KumandaLab.Core.Model;
using KumandaLab.Core.Simulation;

namespace KumandaLab.App.ViewModels;

public sealed record LogItem(string Time, string Text, ProblemSeverity Severity)
{
    public bool IsWarning => Severity == ProblemSeverity.Warning;
    public bool IsError => Severity == ProblemSeverity.Error;
}

public sealed record ProblemItem(string Message, string Explanation, ProblemSeverity Severity)
{
    public bool IsError => Severity == ProblemSeverity.Error;
    public string Glyph => Severity == ProblemSeverity.Error ? "" : Severity == ProblemSeverity.Warning ? "" : "";
}

public sealed record ReadoutItem(string Name, string Value);

public sealed record ChoiceItem(string Value, string Text)
{
    public override string ToString() => Text;
}

/// <summary>Özellikler panelindeki bir parametre satırı. Değer değişince elemana yazar.</summary>
public sealed partial class ParamItem : ObservableObject
{
    private readonly ComponentInstance instance;
    private readonly Action changed;

    public ParamItem(ComponentInstance instance, ParamDef def, Action changed)
    {
        this.instance = instance;
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
                    instance.SetParam(Def.Key, Math.Clamp(d, Def.Min, Def.Max > Def.Min ? Def.Max : double.MaxValue));
                    IsInvalid = d < Def.Min || (Def.Max > Def.Min && d > Def.Max);
                    break;
                case ParamKind.Integer when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i):
                    instance.SetParam(Def.Key, Math.Clamp(i, (int)Def.Min, Def.Max > Def.Min ? (int)Def.Max : int.MaxValue).ToString(CultureInfo.InvariantCulture));
                    IsInvalid = i < Def.Min || (Def.Max > Def.Min && i > Def.Max);
                    break;
                case ParamKind.Text:
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
                instance.SetParam(Def.Key, value ? "True" : "False");
                changed();
            }
        }
    }
}
