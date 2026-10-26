using System.IO;
using System.Media;

namespace BobinKumanda.App;

/// <summary>
/// Korna, buzzer, siren ve zil sesleri. Sesler kayıt dosyası olmadan bellekte üretilir (tam periyotlu, kesintisiz döngü);
/// Windows'un SoundPlayer'ı ile çalınır. Aynı anda tek ses çalar.
/// </summary>
internal sealed class SoundService
{
    private const int Rate = 22050;
    private readonly Dictionary<string, byte[]> cache = [];
    private SoundPlayer? player;
    private MemoryStream? stream;
    private string? playing;

    public void Play(string? kind)
    {
        if (kind == playing)
        {
            return;
        }
        Stop();
        if (kind == null)
        {
            return;
        }
        if (!cache.TryGetValue(kind, out byte[]? wav))
        {
            wav = Wave(kind);
            cache[kind] = wav;
        }
        try
        {
            stream = new MemoryStream(wav, writable: false);
            player = new SoundPlayer(stream);
            player.PlayLooping();
            playing = kind;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or TimeoutException)
        {
            // Ses aygıtı yoksa (okul bilgisayarında hoparlör kapalı) sessiz devam et
            Stop();
        }
    }

    public void Stop()
    {
        try
        {
            player?.Stop();
        }
        catch (InvalidOperationException)
        {
        }
        player?.Dispose();
        stream?.Dispose();
        player = null;
        stream = null;
        playing = null;
    }

    private static byte[] Wave(string kind)
    {
        double seconds = kind switch { "siren" => 2, _ => 1 };
        int n = (int)(Rate * seconds);
        var samples = new double[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            switch (kind)
            {
                case "buzzer":
                    // 2,4 kHz, saniyede 4 kez kesik
                    samples[i] = Square(2400 * t) * (((int)(t * 8) & 1) == 0 ? 0.5 : 0);
                    break;
                case "siren":
                    // 600 → 1200 → 600 Hz üçgen tarama; ortalama 900 Hz × 2 s = tam periyot
                    double f = 600 + 600 * (t < 1 ? t : 2 - t);
                    phase += 2 * Math.PI * f / Rate;
                    samples[i] = Math.Sin(phase) * 0.8;
                    break;
                case "bell":
                    // saniyede 16 çekiç vuruşu, sönümlü metal tını
                    double local = t % (1.0 / 16);
                    double env = Math.Exp(-local * 60);
                    samples[i] = (Math.Sin(2 * Math.PI * 1300 * t) + 0.5 * Math.Sin(2 * Math.PI * 3100 * t)) * env * 0.7;
                    break;
                default:
                    // korna: 400 + 500 Hz, harmonikli
                    samples[i] = 0.5 * Saw(400 * t) + 0.4 * Saw(500 * t);
                    break;
            }
        }
        return Encode(samples, 0.22);
    }

    private static double Square(double cycles) => (cycles % 1) < 0.5 ? 1 : -1;

    private static double Saw(double cycles) => 2 * (cycles % 1) - 1;

    /// <summary>16 bit tek kanallı PCM WAV.</summary>
    private static byte[] Encode(double[] samples, double volume)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int data = samples.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + data);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(Rate);
        w.Write(Rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(data);
        foreach (double s in samples)
        {
            w.Write((short)Math.Round(Math.Clamp(s * volume, -1, 1) * short.MaxValue));
        }
        w.Flush();
        return ms.ToArray();
    }
}
