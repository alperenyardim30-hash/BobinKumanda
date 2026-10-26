using System.Formats.Nrbf;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;
using BobinKumanda.Core.Localization;

namespace BobinKumanda.Core.IO;

public sealed class CircuitFileException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// .esc dosyasını <see cref="NrbfDecoder"/> ile okur. BinaryFormatter kullanılmaz ve dosyadaki hiçbir tip örneklenmez:
/// yalnızca sayı, metin, nokta ve liste kayıtları okunur. Bu yüzden kötü niyetle hazırlanmış bir dosya kod çalıştıramaz;
/// en kötü durumda "dosya okunamadı" hatası verilir.
/// </summary>
public static class EscReader
{
    /// <summary>Bozuk bir dosyanın belleği doldurmaması için her listedeki en fazla kayıt sayısı.</summary>
    public const int MaxItems = 100_000;

    public const long MaxFileBytes = 32L * 1024 * 1024;

    public static EscDocument Read(string path)
    {
        using FileStream fs = File.OpenRead(path);
        return Read(fs);
    }

    public static EscDocument Read(Stream stream)
    {
        if (stream.CanSeek && stream.Length - stream.Position > MaxFileBytes)
        {
            throw new CircuitFileException(Loc.T("esc.tooLarge"));
        }
        try
        {
            SerializationRecord signature = NrbfDecoder.Decode(stream, leaveOpen: true);
            if (signature is not PrimitiveTypeRecord<string> { Value: EscDocument.Signature })
            {
                throw new CircuitFileException(Loc.T("esc.notEkts"));
            }
            SerializationRecord root = NrbfDecoder.Decode(stream, leaveOpen: true);
            return Parse(root);
        }
        catch (CircuitFileException)
        {
            throw;
        }
        catch (Exception e) when (e is SerializationException or NotSupportedException or InvalidCastException or
                                       KeyNotFoundException or FormatException or OverflowException or
                                       EndOfStreamException or InvalidOperationException or ArgumentException or
                                       IndexOutOfRangeException or DecoderFallbackException)
        {
            throw new CircuitFileException(Loc.T("esc.corrupt"), e);
        }
    }

    private static EscDocument Parse(SerializationRecord root)
    {
        SerializationRecord?[] parts = Items(root);
        if (parts.Length < 4)
        {
            throw new CircuitFileException(Loc.T("esc.corrupt"));
        }
        var doc = new EscDocument();

        foreach (SerializationRecord? r in Items(parts[0]))
        {
            SerializationRecord?[] f = Items(r);
            if (f.Length < 8)
            {
                throw new CircuitFileException(Loc.T("esc.corrupt"));
            }
            var e = new EscElement
            {
                Type = Int(f[0]),
                Location = Point(f[1]),
                Label1Location = Point(f[2]),
                Label2Location = Point(f[3]),
                Label1 = Text(f[4]),
                Label2 = Text(f[5])
            };
            e.Links.AddRange(Items(f[6]).Select(Int));
            e.ConnPoints.AddRange(Items(f[7]).Select(Int));
            // Eski sürümlerin dosyalarında son alanlar yoktur; eski program da bu sırayla okur.
            if (f.Length >= 9)
            {
                e.Label2Visible = Bool(f[8]);
            }
            if (f.Length >= 10)
            {
                e.Rotation = Int(f[9]) & 3;
            }
            if (f.Length >= 11)
            {
                e.ColorName = ColorName(f[10]);
            }
            doc.Elements.Add(e);
        }

        foreach (SerializationRecord? r in Items(parts[1]))
        {
            SerializationRecord?[] f = Items(r);
            if (f.Length < 5)
            {
                throw new CircuitFileException(Loc.T("esc.corrupt"));
            }
            doc.Lines.Add(new EscLine { Kind = Text(f[0]), A = Point(f[1]), B = Point(f[2]), ConnA = Int(f[3]), ConnB = Int(f[4]) });
        }

        foreach (SerializationRecord? r in Items(parts[2]))
        {
            SerializationRecord?[] f = Items(r);
            if (f.Length < 2)
            {
                throw new CircuitFileException(Loc.T("esc.corrupt"));
            }
            var d = new EscDot { Origin = Point(f[0]) };
            d.ConnPoints.AddRange(Items(f[1]).Select(Int));
            doc.Dots.Add(d);
        }

        foreach (SerializationRecord? r in Items(parts[3]))
        {
            SerializationRecord?[] f = Items(r);
            if (f.Length < 2)
            {
                throw new CircuitFileException(Loc.T("esc.corrupt"));
            }
            int side = Int(f[0]);
            doc.ConnPoints.Add(new EscConnPoint
            {
                Side = side is >= 0 and <= 4 ? (EscSide)side : EscSide.Null,
                ConnectedTo = Int(f[1])
            });
        }

        if (parts.Length >= 5)
        {
            foreach (SerializationRecord? r in Items(parts[4]))
            {
                SerializationRecord?[] f = Items(r);
                if (f.Length < 3)
                {
                    throw new CircuitFileException(Loc.T("esc.corrupt"));
                }
                doc.Texts.Add(new EscText { Location = Point(f[0]), Size = Size(f[1]), Text = Text(f[2]) });
            }
        }

        if (parts.Length >= 6)
        {
            SerializationRecord?[] f = Items(parts[5]);
            if (f.Length >= 2)
            {
                doc.SummaryVisible = Bool(f[0]);
                doc.SummaryLocation = Point(f[1]);
            }
        }
        return doc;
    }

    /// <summary>System.Collections.ArrayList kaydının dolu elemanları.</summary>
    private static SerializationRecord?[] Items(SerializationRecord? record)
    {
        if (record is not ClassRecord list || list.TypeName.FullName != "System.Collections.ArrayList")
        {
            throw new CircuitFileException(Loc.T("esc.corrupt"));
        }
        int size = list.GetInt32("_size");
        if (list.GetArrayRecord("_items") is not SZArrayRecord<SerializationRecord> items)
        {
            throw new CircuitFileException(Loc.T("esc.corrupt"));
        }
        if (size < 0 || size > MaxItems || items.Length < size)
        {
            throw new CircuitFileException(Loc.T("esc.corrupt"));
        }
        SerializationRecord?[] all = items.GetArray();
        return all.Length == size ? all : all[..size];
    }

    private static int Int(SerializationRecord? r) => r switch
    {
        PrimitiveTypeRecord<int> p => p.Value,
        // Kutulanmış enum (ElementType, CPoint.Sides): tek alanı value__
        ClassRecord c when c.HasMember("value__") => c.GetInt32("value__"),
        _ => throw new CircuitFileException(Loc.T("esc.corrupt"))
    };

    private static bool Bool(SerializationRecord? r) =>
        r is PrimitiveTypeRecord<bool> p ? p.Value : throw new CircuitFileException(Loc.T("esc.corrupt"));

    private static string Text(SerializationRecord? r) => r switch
    {
        null => "",
        PrimitiveTypeRecord<string> s => s.Value,
        PrimitiveTypeRecord p => Convert.ToString(p.Value, CultureInfo.InvariantCulture) ?? "",
        _ => throw new CircuitFileException(Loc.T("esc.corrupt"))
    };

    private static EscPoint Point(SerializationRecord? r) =>
        r is ClassRecord c && c.HasMember("x") && c.HasMember("y")
            ? new EscPoint(c.GetInt32("x"), c.GetInt32("y"))
            : throw new CircuitFileException(Loc.T("esc.corrupt"));

    private static EscPoint Size(SerializationRecord? r) =>
        r is ClassRecord c && c.HasMember("width") && c.HasMember("height")
            ? new EscPoint(c.GetInt32("width"), c.GetInt32("height"))
            : throw new CircuitFileException(Loc.T("esc.corrupt"));

    // System.Drawing.KnownColor değerleri: eski programın renk menüsündekiler ve birkaç yaygın renk
    private static readonly Dictionary<int, string> KnownColors = new()
    {
        [35] = "Black", [37] = "Blue", [79] = "Green", [107] = "Magenta", [122] = "Orange",
        [141] = "Red", [164] = "White", [166] = "Yellow"
    };

    /// <summary>System.Drawing.Color kaydının adı; boş renk için null. Bilinmeyen ARGB renkleri "#RRGGBB" olarak döner.</summary>
    private static string? ColorName(SerializationRecord? r)
    {
        if (r is not ClassRecord c || !c.HasMember("state"))
        {
            return null;
        }
        short state = c.GetInt16("state");
        const short knownColorValid = 1, argbValid = 2;
        if ((state & knownColorValid) != 0)
        {
            return KnownColors.GetValueOrDefault(c.GetInt16("knownColor"));
        }
        if ((state & argbValid) != 0)
        {
            return "#" + (c.GetInt64("value") & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);
        }
        return null;
    }
}
