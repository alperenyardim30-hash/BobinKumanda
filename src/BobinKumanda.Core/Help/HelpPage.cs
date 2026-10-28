using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Help;

/// <summary>
/// Yardım sayfası: gömülü metin (Help/help-tr.html, help-en.html; adda ".tr." olsa derleyici uydu derlemeye koyardı) ve katalogdan o anda üretilen eleman listesi
/// (her elemanın sembolü SVG olarak). Tek, kendi içinde tam bir HTML dosyasıdır; internet gerekmez.
/// </summary>
public static class HelpPage
{
    public static string Build(string language, string version)
    {
        string body = Resource(language == "en" ? "help-en.html" : "help-tr.html") ?? "";
        return body
            .Replace("{{VERSION}}", WebUtility.HtmlEncode(version), StringComparison.Ordinal)
            .Replace("{{COMPONENTS}}", Components(language), StringComparison.Ordinal);
    }

    private static string? Resource(string name)
    {
        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream("BobinKumanda.Help." + name);
        if (s == null)
        {
            return null;
        }
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static readonly ComponentCategory[] Order =
    [
        ComponentCategory.Supply, ComponentCategory.Protection, ComponentCategory.Control, ComponentCategory.Relays,
        ComponentCategory.Contacts, ComponentCategory.Sensors, ComponentCategory.Loads, ComponentCategory.Motors,
        ComponentCategory.Systems, ComponentCategory.Misc
    ];

    /// <summary>Kategorilere ayrılmış eleman kartları: sembol, ad, etiket harfi, klemensler ve açıklama.</summary>
    private static string Components(string language)
    {
        var sb = new StringBuilder();
        foreach (ComponentCategory cat in Order)
        {
            var defs = ComponentCatalog.All.Where(d => d.Category == cat && !d.Hidden).ToList();
            if (defs.Count == 0)
            {
                continue;
            }
            sb.Append(CultureInfo.InvariantCulture, $"<h3 id=\"cat-{cat}\">{WebUtility.HtmlEncode(Loc.TryTIn(language, "cat." + cat) ?? cat.ToString())}</h3>\n<div class=\"cards\">\n");
            foreach (ComponentDef def in defs)
            {
                string terms = string.Join(" ", def.Terminals.Select(t => t.Number ?? t.Id).Where(t => t.Length > 0).Distinct());
                string name = Loc.TIn(language, "comp." + def.TypeId);
                sb.Append("<div class=\"card\"><div class=\"sym\">").Append(Symbol(def, name)).Append("</div><div>")
                  .Append(CultureInfo.InvariantCulture, $"<b>{WebUtility.HtmlEncode(name)}</b>")
                  .Append(CultureInfo.InvariantCulture, $"<span class=\"tag\">{WebUtility.HtmlEncode(def.DefaultLabel ?? def.LabelPrefix + "…")}</span>")
                  .Append(CultureInfo.InvariantCulture, $"<p>{WebUtility.HtmlEncode(Loc.TryTIn(language, "info." + def.TypeId) ?? "")}</p>")
                  .Append(terms.Length > 0 ? $"<p class=\"terms\">{WebUtility.HtmlEncode(terms)}</p>" : "")
                  .Append("</div></div>\n");
            }
            sb.Append("</div>\n");
        }
        return sb.ToString();
    }

    /// <summary>Sembolün SVG çizimi (okul tarzı, açık renk; koyu temada CSS ile ters çevrilir).</summary>
    private static string Symbol(ComponentDef def, string name)
    {
        const double scale = 1.6;
        var inst = new ComponentInstance { Id = "help", Type = def.TypeId, Label = def.DefaultLabel ?? def.LabelPrefix + "1" };
        def.ApplyDefaults(inst);
        double w = def.Size.X * scale + 8, h = def.Size.Y * scale + 8;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg viewBox=\"0 0 {w:0.#} {h:0.#}\" width=\"{w:0.#}\" height=\"{h:0.#}\" role=\"img\" aria-label=\"{WebUtility.HtmlEncode(name)}\">");
        try
        {
            def.Draw(new SvgPainter(sb, def, inst, new Vec(4, 4), scale), new DrawContext { Instance = inst, Def = def });
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {
            // çizilemeyen sembol yardım sayfasını bozmasın
        }
        sb.Append("</svg>");
        return sb.ToString();
    }
}
