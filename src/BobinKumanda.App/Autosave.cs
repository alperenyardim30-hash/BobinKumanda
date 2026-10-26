using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using BobinKumanda.Core.IO;
using BobinKumanda.Core.Model;

namespace BobinKumanda.App;

/// <summary>
/// Kaydedilmemiş değişiklikleri iki dakikada bir %LOCALAPPDATA%\BobinKumanda\autosave klasörüne yazar. Program düzgün
/// kapanınca ya da devre kaydedilince dosya silinir; elektrik kesilir ya da program çökerse sonraki açılışta kurtarılır.
/// Aynı anda açık birkaç pencere birbirinin dosyasına dokunmaz: dosya adında süreç numarası vardır.
/// </summary>
public sealed class Autosave
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    private readonly string file;

    public Autosave()
    {
        using Process me = Process.GetCurrentProcess();
        file = Path.Combine(Folder, string.Create(CultureInfo.InvariantCulture,
            $"{Environment.ProcessId}-{me.StartTime.ToUniversalTime().Ticks}{CircuitFile.Extension}"));
    }

    public static string Folder => Path.Combine(AppSettings.Folder, "autosave");

    private sealed record Meta(string? OriginalPath, string Name, DateTime SavedAt);

    public void Save(Circuit circuit, string? originalPath, string name)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            CircuitFile.Save(circuit, file);
            File.WriteAllText(file + ".json", JsonSerializer.Serialize(new Meta(originalPath, name, DateTime.Now)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Yazılamayan otomatik kayıt kullanıcıyı rahatsız etmez
        }
    }

    public void Clear()
    {
        TryDelete(file);
        TryDelete(file + ".json");
    }

    public sealed record Orphan(string Path, string? OriginalPath, string Name, DateTime SavedAt);

    /// <summary>Artık çalışmayan pencerelerden kalan otomatik kayıtlar (en yenisi önce).</summary>
    public static List<Orphan> FindOrphans()
    {
        var list = new List<Orphan>();
        try
        {
            if (!Directory.Exists(Folder))
            {
                return list;
            }
            foreach (string f in Directory.GetFiles(Folder, "*" + CircuitFile.Extension))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                int dash = name.IndexOf('-');
                if (dash > 0 && int.TryParse(name.AsSpan(0, dash), CultureInfo.InvariantCulture, out int pid) &&
                    long.TryParse(name.AsSpan(dash + 1), CultureInfo.InvariantCulture, out long ticks) && IsAlive(pid, ticks))
                {
                    continue;
                }
                Meta? meta = null;
                try
                {
                    meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(f + ".json"));
                }
                catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
                {
                }
                list.Add(new Orphan(f, meta?.OriginalPath, meta?.Name ?? "", meta?.SavedAt ?? File.GetLastWriteTime(f)));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        list.Sort((a, b) => b.SavedAt.CompareTo(a.SavedAt));
        return list;
    }

    /// <summary>Aynı numaralı süreç hâlâ o dosyayı yazan program mı (numaralar yeniden kullanılabilir).</summary>
    private static bool IsAlive(int pid, long startTicks)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return p.StartTime.ToUniversalTime().Ticks == startTicks;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static void Discard(Orphan o)
    {
        TryDelete(o.Path);
        TryDelete(o.Path + ".json");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
