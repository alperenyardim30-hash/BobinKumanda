using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Drawing;

/// <summary>Sembol çiziminde kullanılan anlamsal renkler; gerçek rengi temaya göre çizici seçer.</summary>
public enum Ink
{
    Normal,
    Muted,
    Energized,
    Fault,
    Accent,
    Active,
    Background,
    Custom
}

public readonly record struct Paint(Ink Ink, uint Rgb = 0, double Alpha = 1)
{
    public static Paint Of(Ink ink, double alpha = 1) => new(ink, 0, alpha);
    public static Paint Color(uint rgb, double alpha = 1) => new(Ink.Custom, rgb, alpha);
}

public enum TextAlign { Left, Center, Right }

/// <summary>
/// Sembol çizim tarzı. School: MEB atölye kitapları ve eski EKTS'deki tarz (─┤ ├─ kontaklar, çapraz çizgili bobin,
/// köprülü butonlar). Iec: IEC 60617 bıçaklı kontaklar.
/// </summary>
public enum SymbolStyle { School, Iec }

/// <summary>
/// Sembolleri yerel (döndürülmemiş) koordinatlarda çizen arayüz. Döndürme/ayna dönüşümünü uygulama tarafı yapar;
/// yazılar her zaman dik kalır.
/// </summary>
public interface ISymbolPainter
{
    void Line(Vec a, Vec b, Paint ink, double width = 1, bool dashed = false);
    void Polyline(IReadOnlyList<Vec> points, Paint ink, double width = 1, bool closed = false, Paint? fill = null, bool dashed = false);
    void Rect(Vec topLeft, Vec size, Paint ink, double width = 1, Paint? fill = null, bool dashed = false);
    void Circle(Vec center, double radius, Paint ink, double width = 1, Paint? fill = null);
    void Text(Vec anchor, string text, double size, Paint ink, TextAlign align = TextAlign.Center, bool bold = false);
}

/// <summary>Simülasyon sırasında sembolün görünümünü belirleyen anlık durum.</summary>
public sealed class SymbolState
{
    /// <summary>Bobin enerjili, lamba yanıyor, motor dönüyor vb.</summary>
    public bool Active { get; set; }

    /// <summary>Kontak takımlarının konumu; her kutup için ayrı olabilir.</summary>
    public bool[] Closed { get; set; } = [];

    /// <summary>Elemanın elle/mekanik konumu (buton basılı, sınır anahtarı tetikli, şalter konumu).</summary>
    public int Position { get; set; }

    /// <summary>0..1 parlaklık, hız, seviye, ilerleme gibi değerler.</summary>
    public double Level { get; set; }

    public double Progress { get; set; } = -1;

    /// <summary>+1 / -1 dönüş yönü.</summary>
    public int Direction { get; set; }

    public string? Text { get; set; }
    public bool Fault { get; set; }
    public bool Warning { get; set; }
    public bool Tripped { get; set; }
}

public sealed class DrawContext
{
    public required ComponentInstance Instance { get; init; }
    public required ComponentDef Def { get; init; }

    /// <summary>Düzenleme modunda null.</summary>
    public SymbolState? State { get; init; }

    public bool ShowTerminalNumbers { get; init; } = true;

    public SymbolStyle Style { get; init; } = SymbolStyle.School;

    public bool School => Style == SymbolStyle.School;

    /// <summary>Dönen motor gibi animasyonlar için saniye cinsinden zaman.</summary>
    public double AnimTime { get; init; }

    /// <summary>Kontakların bağlı olduğu bobinin tipini bulmak için (zaman rölesi kontağı işareti).</summary>
    public Circuit? Circuit { get; init; }

    /// <summary>Klemens numarası (ör. K1'in ikinci NO kontağı için 23/24). Yoksa tanımdaki varsayılan kullanılır.</summary>
    public Func<string, string?>? TerminalNumber { get; init; }

    public bool Simulating => State != null;

    /// <summary>Aynı bağlam, başka bir sembol tarzıyla (okul tarzında karşılığı olmayan parçalar için).</summary>
    public DrawContext WithStyle(SymbolStyle style) => style == Style ? this : new DrawContext
    {
        Instance = Instance,
        Def = Def,
        State = State,
        ShowTerminalNumbers = ShowTerminalNumbers,
        Style = style,
        AnimTime = AnimTime,
        Circuit = Circuit,
        TerminalNumber = TerminalNumber
    };

    public bool IsClosed(int pole, bool restClosed)
    {
        if (State == null || State.Closed.Length == 0)
        {
            return restClosed;
        }
        return State.Closed[Math.Min(pole, State.Closed.Length - 1)];
    }
}
