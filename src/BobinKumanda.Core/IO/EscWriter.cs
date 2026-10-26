using System.Text;

namespace BobinKumanda.Core.IO;

/// <summary>
/// Eski EKTS'nin okuyabileceği .esc dosyası yazar: iki BinaryFormatter (MS-NRBF) grafiği, imza metni ve altı listeli
/// ArrayList. BinaryFormatter kullanılmaz; kayıtlar elle yazılır. Tip ve kütüphane adları eski programınkilerle aynıdır
/// (ESCS.Elements+ElementType, System.Drawing.Point...). Nesneler eski programın yazdığı sırayla (genişlik öncelikli,
/// başvurular önce, nesneler sonra) yazılır.
/// </summary>
public static class EscWriter
{
    private const string EktsLibrary = "EKTS, Version=1.0.2012.17010, Culture=neutral, PublicKeyToken=null";
    private const string DrawingLibrary = "System.Drawing, Version=1.0.3300.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";

    public static void Write(EscDocument doc, string path)
    {
        using FileStream fs = File.Create(path);
        Write(doc, fs);
    }

    public static byte[] ToBytes(EscDocument doc)
    {
        using var ms = new MemoryStream();
        Write(doc, ms);
        return ms.ToArray();
    }

    public static void Write(EscDocument doc, Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        // 1. grafik: imza
        Header(w);
        w.Write((byte)6);
        w.Write(1);
        w.Write(EscDocument.Signature);
        w.Write((byte)11);
        // 2. grafik: devre
        new GraphWriter(w).Write(Build(doc));
    }

    private static void Header(BinaryWriter w)
    {
        w.Write((byte)0);
        w.Write(1);
        w.Write(-1);
        w.Write(1);
        w.Write(0);
    }

    // ---- Eski programın nesne grafiği ----

    private abstract class Node;

    private sealed class ListNode(List<object?> items) : Node
    {
        public List<object?> Items { get; } = items;
    }

    private sealed class EnumNode(string type, int value) : Node
    {
        public string Type { get; } = type;
        public int Value { get; } = value;
    }

    private sealed class PointNode(int x, int y, bool size = false) : Node
    {
        public int X { get; } = x;
        public int Y { get; } = y;
        public bool IsSize { get; } = size;
    }

    private sealed class ColorNode(short knownColor) : Node
    {
        public short KnownColor { get; } = knownColor;
    }

    /// <summary>ArrayList'in iç dizisi; eski .NET'teki gibi 16'dan başlayıp ikiye katlanan kapasiteyle yazılır.</summary>
    private sealed class ArrayNode(List<object?> items) : Node
    {
        public List<object?> Items { get; } = items;
    }

    private static ListNode L(IEnumerable<object?> items) => new([.. items]);

    private static PointNode P(EscPoint p) => new(p.X, p.Y);

    private static ListNode Build(EscDocument doc)
    {
        var elements = doc.Elements.Select(e => (object?)L(
        [
            new EnumNode("ESCS.Elements+ElementType", e.Type),
            P(e.Location), P(e.Label1Location), P(e.Label2Location),
            e.Label1, e.Label2,
            L(e.Links.Select(i => (object?)i)),
            L(e.ConnPoints.Select(i => (object?)i)),
            e.Label2Visible,
            e.Rotation,
            new ColorNode(KnownColor(e.ColorName))
        ]));
        var lines = doc.Lines.Select(l => (object?)L([l.Kind, P(l.A), P(l.B), l.ConnA, l.ConnB]));
        var dots = doc.Dots.Select(d => (object?)L([P(d.Origin), L(d.ConnPoints.Select(i => (object?)i))]));
        var cps = doc.ConnPoints.Select(c => (object?)L([new EnumNode("ESCS.CPoint+Sides", (int)c.Side), c.ConnectedTo]));
        var texts = doc.Texts.Select(t => (object?)L([P(t.Location), new PointNode(t.Size.X, t.Size.Y, size: true), t.Text]));
        return L(
        [
            L(elements), L(lines), L(dots), L(cps), L(texts),
            L([doc.SummaryVisible, P(doc.SummaryLocation)])
        ]);
    }

    /// <summary>System.Drawing.KnownColor: eski programın lamba renk menüsündeki renkler.</summary>
    private static short KnownColor(string? name) => name switch
    {
        "Black" => 35,
        "Blue" => 37,
        "Green" => 79,
        "Magenta" => 107,
        "Orange" => 122,
        "Red" => 141,
        "White" => 164,
        "Yellow" => 166,
        _ => 0
    };

    private sealed class GraphWriter(BinaryWriter w)
    {
        private readonly Dictionary<Node, int> ids = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, int> classIds = [];
        private readonly Dictionary<string, int> libraries = [];
        private readonly Dictionary<string, int> strings = new(StringComparer.Ordinal);
        private readonly Queue<Node> pending = new();
        private int nextId = 1;

        private int IdOf(Node n)
        {
            if (!ids.TryGetValue(n, out int id))
            {
                id = nextId++;
                ids[n] = id;
                pending.Enqueue(n);
            }
            return id;
        }

        public void Write(ListNode root)
        {
            Header(w);
            IdOf(root);
            while (pending.Count > 0)
            {
                Node n = pending.Dequeue();
                switch (n)
                {
                    case ListNode l:
                        WriteList(l);
                        break;
                    case ArrayNode a:
                        WriteArray(a);
                        break;
                    case EnumNode e:
                        WriteClass(e.Type, EktsLibrary, ["value__"], [(byte)8], ids[e], () => w.Write(e.Value));
                        break;
                    case PointNode p:
                        WriteClass(p.IsSize ? "System.Drawing.Size" : "System.Drawing.Point", DrawingLibrary,
                            p.IsSize ? ["width", "height"] : ["x", "y"], [8, 8], ids[p], () =>
                            {
                                w.Write(p.X);
                                w.Write(p.Y);
                            });
                        break;
                    case ColorNode c:
                        WriteColor(c);
                        break;
                }
            }
            w.Write((byte)11);
        }

        private void WriteList(ListNode l)
        {
            int id = ids[l];
            var array = new ArrayNode(l.Items);
            if (classIds.TryGetValue("System.Collections.ArrayList", out int meta))
            {
                w.Write((byte)1);
                w.Write(id);
                w.Write(meta);
            }
            else
            {
                classIds["System.Collections.ArrayList"] = id;
                // SystemClassWithMembersAndTypes: _items (object[]), _size, _version (Int32)
                w.Write((byte)4);
                w.Write(id);
                w.Write("System.Collections.ArrayList");
                w.Write(3);
                w.Write("_items");
                w.Write("_size");
                w.Write("_version");
                w.Write((byte)5);
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((byte)8);
                w.Write((byte)8);
            }
            w.Write((byte)9);
            w.Write(IdOf(array));
            w.Write(l.Items.Count);
            w.Write(l.Items.Count);
        }

        private void WriteArray(ArrayNode a)
        {
            int capacity = 16;
            while (capacity < a.Items.Count)
            {
                capacity *= 2;
            }
            w.Write((byte)16);
            w.Write(ids[a]);
            w.Write(capacity);
            int nulls = 0;
            void FlushNulls()
            {
                while (nulls > 0)
                {
                    int n = Math.Min(nulls, 255);
                    if (n == 1)
                    {
                        w.Write((byte)10);
                    }
                    else
                    {
                        w.Write((byte)13);
                        w.Write((byte)n);
                    }
                    nulls -= n;
                }
            }
            foreach (object? item in a.Items)
            {
                if (item == null)
                {
                    nulls++;
                    continue;
                }
                FlushNulls();
                WriteValue(item);
            }
            nulls += capacity - a.Items.Count;
            FlushNulls();
        }

        /// <summary>Dizi elemanı: metin satır içi, kutulu sayı/mantıksal değer satır içi, nesne başvuru olarak.</summary>
        private void WriteValue(object item)
        {
            switch (item)
            {
                case string s:
                    WriteString(s);
                    break;
                case int i:
                    w.Write((byte)8);
                    w.Write((byte)8);
                    w.Write(i);
                    break;
                case bool b:
                    w.Write((byte)8);
                    w.Write((byte)1);
                    w.Write(b);
                    break;
                case Node n:
                    w.Write((byte)9);
                    w.Write(IdOf(n));
                    break;
                default:
                    throw new ArgumentException("yazılamayan değer: " + item.GetType());
            }
        }

        private void WriteString(string s)
        {
            // Eski program aynı metin örneğini (h, v, "...") bir kez yazıp sonra başvuruyla gösterir
            if (strings.TryGetValue(s, out int existing))
            {
                w.Write((byte)9);
                w.Write(existing);
                return;
            }
            int id = nextId++;
            strings[s] = id;
            w.Write((byte)6);
            w.Write(id);
            w.Write(s);
        }

        private int Library(string name)
        {
            if (!libraries.TryGetValue(name, out int id))
            {
                id = nextId++;
                libraries[name] = id;
                w.Write((byte)12);
                w.Write(id);
                w.Write(name);
            }
            return id;
        }

        private void WriteClass(string type, string library, string[] members, byte[] primitives, int id, Action values)
        {
            if (classIds.TryGetValue(type, out int meta))
            {
                w.Write((byte)1);
                w.Write(id);
                w.Write(meta);
                values();
                return;
            }
            int lib = Library(library);
            classIds[type] = id;
            w.Write((byte)5);
            w.Write(id);
            w.Write(type);
            w.Write(members.Length);
            foreach (string m in members)
            {
                w.Write(m);
            }
            foreach (byte _ in primitives)
            {
                w.Write((byte)0);
            }
            foreach (byte p in primitives)
            {
                w.Write(p);
            }
            w.Write(lib);
            values();
        }

        /// <summary>System.Drawing.Color: value (Int64), knownColor (Int16), state (Int16), name (String).</summary>
        private void WriteColor(ColorNode c)
        {
            int id = ids[c];
            const short knownColorValid = 1;
            void Values()
            {
                w.Write(0L);
                w.Write(c.KnownColor);
                w.Write(c.KnownColor != 0 ? knownColorValid : (short)0);
                w.Write((byte)10); // name: null
            }
            if (classIds.TryGetValue("System.Drawing.Color", out int meta))
            {
                w.Write((byte)1);
                w.Write(id);
                w.Write(meta);
                Values();
                return;
            }
            int lib = Library(DrawingLibrary);
            classIds["System.Drawing.Color"] = id;
            w.Write((byte)5);
            w.Write(id);
            w.Write("System.Drawing.Color");
            w.Write(4);
            w.Write("value");
            w.Write("knownColor");
            w.Write("state");
            w.Write("name");
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((byte)1);
            w.Write((byte)9);
            w.Write((byte)7);
            w.Write((byte)7);
            w.Write(lib);
            Values();
        }
    }
}
