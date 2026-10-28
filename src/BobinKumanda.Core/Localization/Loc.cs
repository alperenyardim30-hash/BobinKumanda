using System.Globalization;
using System.Reflection;

namespace BobinKumanda.Core.Localization;

/// <summary>
/// Çok dilli metinler. Kaynak: gömülü Strings.txt; her satır "anahtar | tr | en". Yeni dil eklemek için sütun eklemek yeterli.
/// </summary>
public static class Loc
{
    public static readonly string[] Languages = ["tr", "en"];

    private static readonly Dictionary<string, string[]> Table = Load();
    private static int languageIndex;

    public static event Action? LanguageChanged;

    public static string Language
    {
        get => Languages[languageIndex];
        set
        {
            int index = Array.IndexOf(Languages, value);
            if (index < 0)
            {
                index = 0;
            }
            if (index != languageIndex)
            {
                languageIndex = index;
                LanguageChanged?.Invoke();
            }
        }
    }

    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "tr" ? "tr-TR" : "en-US");

    public static bool Has(string key) => Table.ContainsKey(key);

    public static string T(string key)
    {
        if (Table.TryGetValue(key, out string[]? row))
        {
            string text = row[languageIndex];
            return text.Length > 0 ? text : row[0];
        }
        return key;
    }

    public static string T(string key, params object?[] args)
    {
        try
        {
            return string.Format(Culture, T(key), args);
        }
        catch (FormatException)
        {
            return T(key);
        }
    }

    /// <summary>
    /// Belirli bir dildeki metin; programın o anki dilini değiştirmez (aynı anda iki dilde belge üretirken genel ayar
    /// değişirse paralel çalışan işler yanlış dilde metin alır).
    /// </summary>
    public static string TIn(string language, string key)
    {
        int index = Math.Max(0, Array.IndexOf(Languages, language));
        if (Table.TryGetValue(key, out string[]? row))
        {
            string text = row[index];
            return text.Length > 0 ? text : row[0];
        }
        return key;
    }

    public static string? TryTIn(string language, string key) => Table.ContainsKey(key) ? TIn(language, key) : null;

    /// <summary>Anahtar yoksa null döner (isteğe bağlı açıklamalar için).</summary>
    public static string? TryT(string key) => Table.ContainsKey(key) ? T(key) : null;

    private static Dictionary<string, string[]> Load()
    {
        var table = new Dictionary<string, string[]>(StringComparer.Ordinal);
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BobinKumanda.Strings.txt");
        if (stream == null)
        {
            return table;
        }
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            string[] parts = line.Split(" | ");
            if (parts.Length < 2)
            {
                continue;
            }
            var row = new string[Languages.Length];
            for (int i = 0; i < row.Length; i++)
            {
                row[i] = i + 1 < parts.Length ? parts[i + 1].Trim().Replace("\\n", "\n") : "";
            }
            table[parts[0].Trim()] = row;
        }
        return table;
    }
}
