namespace KumandaLab.Core.IO;

/// <summary>
/// Eski EKTS .esc dosyasının ham içeriği, yeni modele çevrilmeden önceki hali. Dosya iki BinaryFormatter grafiğidir:
/// imza metni ("BU DOSYA EKTS PROGRAMININ DOSYASIDIR") ve altı listeden oluşan bir ArrayList:
/// elemanlar, çizgiler, ek noktaları, bağlantı noktaları, açıklama metinleri, özet paneli (eski dosyalarda yok).
/// Bağlantılar bağlantı noktası listesindeki sıra numaralarıyla kurulur: her nokta en fazla bir başka noktaya bağlıdır.
/// </summary>
public sealed class EscDocument
{
    public const string Signature = "BU DOSYA EKTS PROGRAMININ DOSYASIDIR";

    public List<EscElement> Elements { get; } = [];
    public List<EscLine> Lines { get; } = [];
    public List<EscDot> Dots { get; } = [];
    public List<EscConnPoint> ConnPoints { get; } = [];
    public List<EscText> Texts { get; } = [];

    public bool SummaryVisible { get; set; }
    public EscPoint SummaryLocation { get; set; }
}

public readonly record struct EscPoint(int X, int Y)
{
    public Vec ToVec() => new(X, Y);
}

/// <summary>Eski CPoint.Sides: Null, Left, Top, Right, Bottom (aynı sırayla).</summary>
public enum EscSide { Null, Left, Top, Right, Bottom }

public sealed class EscElement
{
    /// <summary>Eski Elements.ElementType değeri (0 = Role ... 26 = Asansor).</summary>
    public int Type { get; set; }
    public EscPoint Location { get; set; }

    /// <summary>Ad etiketi (K1, START...) ve ikinci etiket (zaman rölesinde süre) konumları, çizim alanına göre.</summary>
    public EscPoint Label1Location { get; set; }
    public EscPoint Label2Location { get; set; }
    public string Label1 { get; set; } = "";
    public string Label2 { get; set; } = "";

    /// <summary>Bağlı elemanların sırası: bobin için kontakları, kontak için bobini.</summary>
    public List<int> Links { get; } = [];

    /// <summary>Bu elemanın klemenslerinin bağlantı noktası numaraları, eski InitCPoint sırasıyla.</summary>
    public List<int> ConnPoints { get; } = [];

    public bool Label2Visible { get; set; }
    public int Rotation { get; set; }

    /// <summary>Lamba rengi; eski programda yalnızca kırmızı, mavi, yeşil, mor ve sarı seçilebiliyordu. Boşsa null.</summary>
    public string? ColorName { get; set; }
}

/// <summary>Tek bir yatay ("h") ya da dikey ("v") çizgi parçası. Kablolar bu parçaların uç uca eklenmesiyle oluşur.</summary>
public sealed class EscLine
{
    public string Kind { get; set; } = "h";
    public EscPoint A { get; set; }
    public EscPoint B { get; set; }
    public int ConnA { get; set; }
    public int ConnB { get; set; }
}

/// <summary>Ek noktası; dört yöne birer bağlantı noktası (alt, sol, sağ, üst) vardır.</summary>
public sealed class EscDot
{
    public EscPoint Origin { get; set; }
    public List<int> ConnPoints { get; } = [];
}

public sealed class EscConnPoint
{
    public EscSide Side { get; set; }

    /// <summary>Bağlı olduğu bağlantı noktasının sırası, bağlı değilse -1.</summary>
    public int ConnectedTo { get; set; } = -1;
}

public sealed class EscText
{
    public EscPoint Location { get; set; }
    public EscPoint Size { get; set; }
    public string Text { get; set; } = "";
}
