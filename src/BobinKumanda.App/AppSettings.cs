using System.IO;
using System.Text.Json;

namespace BobinKumanda.App;

/// <summary>Kullanıcı ayarları: %LOCALAPPDATA%\BobinKumanda\settings.json. Okunamazsa varsayılanlar kullanılır.</summary>
public sealed class AppSettings
{
    public const int MaxRecent = 10;

    public string Language { get; set; } = "tr";
    public bool DarkTheme { get; set; }
    public bool TerminalNumbers { get; set; }
    public bool Grid { get; set; } = true;
    public bool Flow { get; set; } = true;
    public bool ShowPalette { get; set; } = true;
    public bool Sound { get; set; } = true;
    public bool CrossReference { get; set; } = true;
    public double Speed { get; set; } = 1;
    public List<string> RecentFiles { get; set; } = [];

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BobinKumanda");

    /// <summary>İlk deneme sürümü ayarları burada tutuyordu; yeni klasörde ayar yoksa oradan alınır.</summary>
    private static string LegacyFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KumandaLab", "settings.json");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            string path = File.Exists(FilePath) ? FilePath : LegacyFilePath;
            if (File.Exists(path))
            {
                AppSettings s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
                // Klasör taşınınca ya da elle düzenlenince aynı dosya iki kez görünmesin
                s.RecentFiles = (s.RecentFiles ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return s;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Bozuk ayar dosyası programın açılmasını engellemesin
        }
        return new AppSettings();
    }

    /// <summary>Betikle deneme sırasında kullanıcının ayarları ve son dosyaları değişmesin.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ReadOnly { get; set; }

    public void Save()
    {
        if (ReadOnly)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Okul bilgisayarlarında yazma izni olmayabilir; ayarlar bu oturumla sınırlı kalır
        }
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecent)
        {
            RecentFiles.RemoveRange(MaxRecent, RecentFiles.Count - MaxRecent);
        }
    }
}
