using System.Text.Json;
using System.Text.Json.Serialization;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.IO;

/// <summary>
/// Bobin Kumanda'nın kendi dosya biçimi (.bobin): sürüm alanlı, okunabilir JSON. Kayıt önce geçici dosyaya yazılır,
/// sonra eskisinin yerine konur; yazma yarıda kesilse bile eski dosya bozulmaz.
/// Programın ilk deneme sürümü aynı içeriği .kmlab uzantısı ve "KumandaLab" adıyla yazıyordu; onlar da açılır.
/// </summary>
public static class CircuitFile
{
    public const string Extension = ".bobin";
    public const string LegacyExtension = ".kmlab";
    public const string FormatName = "BobinKumanda";
    private const string LegacyFormatName = "KumandaLab";
    public const long MaxFileBytes = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 32
    };

    private sealed class Envelope
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public string? App { get; set; }
        public Circuit? Circuit { get; set; }
    }

    public static void Save(Circuit circuit, Stream stream)
    {
        var env = new Envelope
        {
            Format = FormatName,
            Version = Circuit.CurrentFormatVersion,
            App = typeof(CircuitFile).Assembly.GetName().Version?.ToString(3),
            Circuit = circuit
        };
        JsonSerializer.Serialize(stream, env, Options);
    }

    private static readonly JsonSerializerOptions CompactOptions = new(Options) { WriteIndented = false };

    /// <summary>Geri alma ve pano için girintisiz UTF-8 JSON.</summary>
    public static byte[] ToBytes(Circuit circuit) =>
        JsonSerializer.SerializeToUtf8Bytes(new Envelope
        {
            Format = FormatName,
            Version = Circuit.CurrentFormatVersion,
            Circuit = circuit
        }, CompactOptions);

    public static Circuit FromBytes(byte[] data) => Load(new MemoryStream(data, writable: false));

    public static void Save(Circuit circuit, string path)
    {
        string full = Path.GetFullPath(path);
        string tmp = full + ".tmp";
        using (FileStream fs = File.Create(tmp))
        {
            Save(circuit, fs);
        }
        File.Move(tmp, full, overwrite: true);
    }

    public static Circuit Load(string path)
    {
        using FileStream fs = File.OpenRead(path);
        return Load(fs);
    }

    public static Circuit Load(Stream stream)
    {
        if (stream.CanSeek && stream.Length - stream.Position > MaxFileBytes)
        {
            throw new CircuitFileException(Loc.T("file.corrupt"));
        }
        Envelope? env;
        try
        {
            env = JsonSerializer.Deserialize<Envelope>(stream, Options);
        }
        catch (JsonException e)
        {
            throw new CircuitFileException(Loc.T("file.corrupt"), e);
        }
        if (env == null || env.Format is not (FormatName or LegacyFormatName) || env.Circuit == null)
        {
            throw new CircuitFileException(Loc.T("file.corrupt"));
        }
        if (env.Version > Circuit.CurrentFormatVersion)
        {
            throw new CircuitFileException(Loc.T("file.newer"));
        }
        Circuit c = env.Circuit;
        Normalize(c);
        return c;
    }

    /// <summary>Elle düzenlenmiş ya da eksik dosyalarda boş listeleri ve sayfa kimliklerini onarır.</summary>
    private static void Normalize(Circuit c)
    {
        c.Title ??= new TitleBlock();
        c.Sheets ??= [];
        c.Components ??= [];
        c.Wires ??= [];
        c.Junctions ??= [];
        c.Texts ??= [];
        c.Sheets.RemoveAll(s => s == null);
        c.Components.RemoveAll(x => x == null);
        c.Wires.RemoveAll(w => w == null || w.Points == null);
        c.Junctions.RemoveAll(j => j == null);
        c.Texts.RemoveAll(t => t == null);
        if (c.Sheets.Count == 0)
        {
            c.Sheets.Add(new Sheet { Id = "s1" });
        }
        string first = c.Sheets[0].Id;
        var sheetIds = c.Sheets.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (ComponentInstance x in c.Components)
        {
            x.Params ??= new Dictionary<string, string>(StringComparer.Ordinal);
            if (!sheetIds.Contains(x.Sheet))
            {
                x.Sheet = first;
            }
        }
        foreach (Wire w in c.Wires)
        {
            w.A ??= WireEnd.Free();
            w.B ??= WireEnd.Free();
            if (w.Points.Count == 1)
            {
                w.Points.Add(w.Points[0]);
            }
            if (!sheetIds.Contains(w.Sheet))
            {
                w.Sheet = first;
            }
        }
        foreach (Junction j in c.Junctions)
        {
            if (!sheetIds.Contains(j.Sheet))
            {
                j.Sheet = first;
            }
        }
        foreach (TextNote t in c.Texts)
        {
            t.Text ??= "";
            if (!sheetIds.Contains(t.Sheet))
            {
                t.Sheet = first;
            }
        }
        c.RecalculateNextId();
    }
}
