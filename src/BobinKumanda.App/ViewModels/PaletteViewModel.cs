using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using BobinKumanda.App.Rendering;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Localization;

namespace BobinKumanda.App.ViewModels;

public sealed partial class PaletteItem : ObservableObject
{
    public PaletteItem(ComponentDef def) => Def = def;

    public ComponentDef Def { get; }
    public string TypeId => Def.TypeId;
    public string Name => Def.Name;
    public string Info => Def.Info;
    public string Shortcut => Def.Shortcut is char c ? c.ToString() : "";
    public bool HasShortcut => Def.Shortcut != null;

    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }

    public string ToolTipText => Def.Shortcut is char c ? $"{Name}  ({c})\n{Info}" : $"{Name}\n{Info}";

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Info));
        OnPropertyChanged(nameof(ToolTipText));
    }
}

public sealed partial class PaletteGroup : ObservableObject
{
    public PaletteGroup(ComponentCategory category) => Category = category;

    public ComponentCategory Category { get; }
    public string Title => Loc.TryT("cat." + Category) ?? Category.ToString();
    public ObservableCollection<PaletteItem> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    public void Refresh() => OnPropertyChanged(nameof(Title));
}

/// <summary>Soldaki eleman paleti: kategorilere ayrılmış liste ve arama (ad, etiket harfi ve anahtar kelimelerde).</summary>
public sealed partial class PaletteViewModel : ObservableObject
{
    /// <summary>Kumanda devresi kurarken en sık gidilen sıra: besleme, butonlar, bobinler, kontaklar, yükler, motorlar.</summary>
    private static readonly ComponentCategory[] Order =
    [
        ComponentCategory.Supply, ComponentCategory.Control, ComponentCategory.Relays, ComponentCategory.Contacts,
        ComponentCategory.Loads, ComponentCategory.Motors, ComponentCategory.Protection, ComponentCategory.Sensors,
        ComponentCategory.Systems, ComponentCategory.Misc
    ];

    private readonly List<PaletteItem> all;
    private readonly List<PaletteGroup> groups;

    public PaletteViewModel()
    {
        all = ComponentCatalog.All.Where(d => !d.Hidden).Select(d => new PaletteItem(d)).ToList();
        groups = Order.Select(c => new PaletteGroup(c)).ToList();
        Loc.LanguageChanged += () =>
        {
            foreach (PaletteItem i in all)
            {
                i.Refresh();
            }
            foreach (PaletteGroup g in groups)
            {
                g.Refresh();
            }
            Filter();
        };
        Filter();
    }

    public ObservableCollection<PaletteGroup> Groups { get; } = [];

    public IReadOnlyList<PaletteItem> AllItems => all;

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasResults { get; private set; } = true;

    partial void OnSearchTextChanged(string value) => Filter();

    /// <summary>Aramaya uyan ilk eleman (Enter ile yerleştirmek için).</summary>
    public PaletteItem? FirstMatch => Groups.SelectMany(g => g.Items).FirstOrDefault();

    public void SetTheme(CanvasTheme theme)
    {
        foreach (PaletteItem i in all)
        {
            i.Icon = SymbolIcons.Create(i.Def, theme);
        }
    }

    private void Filter()
    {
        string q = SearchText.Trim();
        CompareInfo cmp = Loc.Culture.CompareInfo;
        bool Match(PaletteItem i) =>
            q.Length == 0 ||
            cmp.IndexOf(i.Name, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0 ||
            cmp.IndexOf(i.Def.Tags, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0 ||
            string.Equals(i.Def.LabelPrefix, q, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(i.Shortcut, q, StringComparison.OrdinalIgnoreCase);

        Groups.Clear();
        foreach (PaletteGroup g in groups)
        {
            g.Items.Clear();
            foreach (PaletteItem i in all.Where(i => i.Def.Category == g.Category && Match(i)))
            {
                g.Items.Add(i);
            }
            if (g.Items.Count > 0)
            {
                Groups.Add(g);
            }
        }
        HasResults = Groups.Count > 0;
        OnPropertyChanged(nameof(FirstMatch));
    }

    public PaletteItem? ByShortcut(char key) =>
        all.FirstOrDefault(i => i.Def.Shortcut is char c && char.ToUpperInvariant(c) == char.ToUpperInvariant(key));
}
