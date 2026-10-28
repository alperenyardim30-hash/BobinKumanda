using System.Text;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Localization;

namespace BobinKumanda.Core.Help;

/// <summary>Eleman kataloğunun Markdown listesi (depodaki docs/ ve wiki için): kategori, ad, etiket, klemensler, açıklama.</summary>
public static class ComponentReference
{
    private static readonly ComponentCategory[] Order =
    [
        ComponentCategory.Supply, ComponentCategory.Protection, ComponentCategory.Control, ComponentCategory.Relays,
        ComponentCategory.Contacts, ComponentCategory.Sensors, ComponentCategory.Loads, ComponentCategory.Motors,
        ComponentCategory.Systems, ComponentCategory.Misc
    ];

    public static string Markdown(string language)
    {
        bool tr = language == "tr";
        var sb = new StringBuilder();
        sb.Append(tr ? "# Elemanlar\n\n" : "# Components\n\n");
        sb.Append(tr
            ? "Bu sayfa programın eleman kataloğundan otomatik üretilir. Etiket sütunu otomatik verilen IEC harf kodudur; klemens sütunu klemens numaralarıdır.\n\n"
            : "This page is generated from the program's component catalogue. The label column is the IEC letter code given automatically; the terminal column lists terminal numbers.\n\n");
        int total = 0;
        foreach (ComponentCategory cat in Order)
        {
            var defs = ComponentCatalog.All.Where(d => d.Category == cat && !d.Hidden).ToList();
            if (defs.Count == 0)
            {
                continue;
            }
            sb.Append("## ").Append(Loc.TryTIn(language, "cat." + cat) ?? cat.ToString()).Append("\n\n");
            sb.Append(tr ? "| Eleman | Etiket | Klemensler | Açıklama |\n" : "| Component | Label | Terminals | Description |\n");
            sb.Append("|---|---|---|---|\n");
            foreach (ComponentDef d in defs)
            {
                string terms = string.Join(" ", d.Terminals.Select(t => t.Number ?? t.Id).Where(t => t.Length > 0).Distinct());
                sb.Append("| **").Append(Cell(Loc.TIn(language, "comp." + d.TypeId))).Append("** | `").Append(d.DefaultLabel ?? d.LabelPrefix + "1").Append("` | ")
                  .Append(Cell(terms)).Append(" | ").Append(Cell(Loc.TryTIn(language, "info." + d.TypeId) ?? "")).Append(" |\n");
                total++;
            }
            sb.Append('\n');
        }
        sb.Append(tr ? $"Toplam {total} eleman.\n" : $"{total} components in total.\n");
        return sb.ToString();
    }

    private static string Cell(string s) => s.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
