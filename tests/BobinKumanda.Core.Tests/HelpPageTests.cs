using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Help;

namespace BobinKumanda.Core.Tests;

public class HelpPageTests
{
    /// <summary>Yardım sayfası iki dilde de tam üretilir: metin, her eleman ve sembolü. KLAB_HELP varsa klasöre yazılır.</summary>
    [Theory]
    [InlineData("tr", "Başlarken")]
    [InlineData("en", "Getting started")]
    public void Help_page_contains_text_and_every_component(string lang, string heading)
    {
        string html = HelpPage.Build(lang, "1.0.0");
        Assert.Contains(heading, html);
        Assert.DoesNotContain("{{", html);
        Assert.Contains("1.0.0", html);
        int cards = html.Split("class=\"card\"").Length - 1;
        Assert.Equal(ComponentCatalog.All.Count(d => !d.Hidden), cards);
        Assert.True(html.Split("<svg").Length - 1 >= cards);
        string? dir = Environment.GetEnvironmentVariable("KLAB_HELP");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"help-{lang}.html"), html);
        }
    }
}
