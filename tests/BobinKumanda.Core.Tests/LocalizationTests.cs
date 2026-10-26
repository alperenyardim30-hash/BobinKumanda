using System.Text.RegularExpressions;
using BobinKumanda.Core.Localization;

namespace BobinKumanda.Core.Tests;

/// <summary>Metin tablosu: her satır "anahtar | Türkçe | English" olmalı ve koddaki her anahtar tabloda bulunmalı.</summary>
public partial class LocalizationTests
{
    private static string? RepoRoot()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "BobinKumanda.sln")))
                {
                    return dir.FullName;
                }
            }
        }
        return null;
    }

    [Fact]
    public void Every_line_of_the_string_table_has_both_languages()
    {
        string? root = RepoRoot();
        Assert.SkipWhen(root == null, "kaynak klasörü bulunamadı");
        string[] lines = File.ReadAllLines(Path.Combine(root!, "src", "BobinKumanda.Core", "Localization", "Strings.txt"));
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i];
            if (l.Length == 0 || l.StartsWith('#'))
            {
                continue;
            }
            string[] parts = l.Split(" | ");
            // Satır sonuna yazılan çok satırlı metinler tabloyu sessizce bozar; yeni satır için \n kullanılır
            Assert.True(parts.Length == 3, $"Strings.txt satır {i + 1}: üç bölüm olmalı: {l}");
            Assert.True(parts[1].Trim().Length > 0 && parts[2].Trim().Length > 0, $"Strings.txt satır {i + 1}: boş çeviri");
        }
    }

    /// <summary>
    /// Koddaki sabit anahtarlar: Loc.T("ui.x"), L("log.x") (simülasyon elemanlarında), SetHint("ui.x"), kaydırıcı ve
    /// düğme adları ("slider.x", "action.x") ve XAML'de {l:Loc ui.x}.
    /// </summary>
    [GeneratedRegex("""(?:(?:Loc\.T|Loc\.TryT|SetHint|\bL)\(\s*"([a-z]+\.[A-Za-z0-9.\-]+)"|\{l:Loc\s+([a-z]+\.[A-Za-z0-9.\-]+)\}|"((?:slider|action)\.[A-Za-z]+)")""")]
    private static partial Regex KeyPattern();

    [Fact]
    public void Every_key_used_in_the_code_exists()
    {
        string? root = RepoRoot();
        Assert.SkipWhen(root == null, "kaynak klasörü bulunamadı");
        var missing = new List<string>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root!, "src"), "*.*", SearchOption.AllDirectories))
        {
            if (!(file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".xaml", StringComparison.Ordinal)) ||
                file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }
            foreach (Match m in KeyPattern().Matches(File.ReadAllText(file)))
            {
                string key = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
                if (!Loc.Has(key))
                {
                    missing.Add($"{Path.GetFileName(file)}: {key}");
                }
            }
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_component_parameter_and_choice_has_a_name()
    {
        var missing = new List<string>();
        foreach (Catalog.ComponentDef def in Catalog.ComponentCatalog.All)
        {
            foreach (string key in new[] { "comp." + def.TypeId, "info." + def.TypeId })
            {
                if (!Loc.Has(key))
                {
                    missing.Add(key);
                }
            }
            foreach (Catalog.ParamDef p in def.Params)
            {
                if (!Loc.Has(p.NameKey))
                {
                    missing.Add(def.TypeId + ": " + p.NameKey);
                }
                foreach (string c in p.Choices ?? [])
                {
                    // sayılar (anma akımı, gerilim) olduğu gibi gösterilir
                    if (!double.TryParse(c, System.Globalization.CultureInfo.InvariantCulture, out _) && !Loc.Has(p.ChoiceNameKey(c)))
                    {
                        missing.Add(def.TypeId + ": " + p.ChoiceNameKey(c));
                    }
                }
            }
        }
        Assert.Empty(missing.Distinct());
    }
}
