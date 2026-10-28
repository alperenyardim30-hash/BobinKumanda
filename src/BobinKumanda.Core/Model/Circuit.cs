using System.Globalization;

namespace BobinKumanda.Core.Model;

/// <summary>Bir devre belgesi: sayfalar, elemanlar, kablolar, ek noktaları ve açıklama metinleri.</summary>
public sealed class Circuit
{
    public const int CurrentFormatVersion = 1;

    public TitleBlock Title { get; set; } = new();
    public List<Sheet> Sheets { get; set; } = [new Sheet { Id = "s1", Name = "" }];
    public List<ComponentInstance> Components { get; set; } = [];
    public List<Wire> Wires { get; set; } = [];
    public List<Junction> Junctions { get; set; } = [];
    public List<TextNote> Texts { get; set; } = [];

    private int nextId = 1;

    public string NewId(string prefix)
    {
        if (nextId == 1)
        {
            RecalculateNextId();
        }
        return prefix + (nextId++).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Yüklemeden sonra çağrılır; yeni kimliklerin mevcutlarla çakışmasını önler.</summary>
    public void RecalculateNextId()
    {
        int max = 0;
        foreach (string id in AllIds())
        {
            int i = id.Length - 1;
            while (i >= 0 && char.IsDigit(id[i]))
            {
                i--;
            }
            if (i < id.Length - 1 && int.TryParse(id.AsSpan(i + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int n))
            {
                max = Math.Max(max, n);
            }
        }
        nextId = max + 1;
    }

    private IEnumerable<string> AllIds()
    {
        foreach (var s in Sheets) yield return s.Id;
        foreach (var c in Components) yield return c.Id;
        foreach (var w in Wires) yield return w.Id;
        foreach (var j in Junctions) yield return j.Id;
        foreach (var t in Texts) yield return t.Id;
    }

    public ComponentInstance? FindComponent(string? id) =>
        id == null ? null : Components.Find(c => c.Id == id);

    public Junction? FindJunction(string? id) =>
        id == null ? null : Junctions.Find(j => j.Id == id);

    public Wire? FindWire(string? id) =>
        id == null ? null : Wires.Find(w => w.Id == id);

    public Sheet? FindSheet(string? id) =>
        id == null ? null : Sheets.Find(s => s.Id == id);
}

public sealed class Sheet
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>Öğrenci/çizim bilgileri; yazdırma ve resim çıktısının köşesindeki antette görünür.</summary>
public sealed class TitleBlock
{
    public string CircuitName { get; set; } = "";
    public string StudentName { get; set; } = "";
    public string StudentNumber { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string School { get; set; } = "";
    public string Teacher { get; set; } = "";
    public string Date { get; set; } = "";
    public string Notes { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty =>
        CircuitName.Length + StudentName.Length + StudentNumber.Length + ClassName.Length +
        School.Length + Teacher.Length + Date.Length == 0;
}

public sealed class ComponentInstance
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Label { get; set; } = "";
    public string Sheet { get; set; } = "s1";

    /// <summary>Döndürülmüş sınır kutusunun sol üst köşesi.</summary>
    public Vec Position { get; set; }

    /// <summary>0..3, saat yönünde 90° adımlar.</summary>
    public int Rotation { get; set; }

    public bool Mirror { get; set; }

    /// <summary>Etiketin (K1) elemanın konumuna göre yeri; null ise varsayılan yer kullanılır.</summary>
    public Vec? LabelOffset { get; set; }

    public Dictionary<string, string> Params { get; set; } = new(StringComparer.Ordinal);

    public string GetParam(string key, string fallback) =>
        Params.TryGetValue(key, out string? v) ? v : fallback;

    public double GetDouble(string key, double fallback) =>
        Params.TryGetValue(key, out string? v) &&
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;

    public int GetInt(string key, int fallback) =>
        Params.TryGetValue(key, out string? v) &&
        int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : fallback;

    public bool GetBool(string key, bool fallback) =>
        Params.TryGetValue(key, out string? v) && bool.TryParse(v, out bool b) ? b : fallback;

    public void SetParam(string key, double value) =>
        Params[key] = value.ToString("R", CultureInfo.InvariantCulture);

    public void SetParam(string key, string value) => Params[key] = value;

    public ComponentInstance Clone() => new()
    {
        Id = Id,
        Type = Type,
        Label = Label,
        Sheet = Sheet,
        Position = Position,
        Rotation = Rotation,
        Mirror = Mirror,
        LabelOffset = LabelOffset,
        Params = new Dictionary<string, string>(Params, StringComparer.Ordinal)
    };
}

/// <summary>Kablo ucu: bir elemanın klemensine, bir ek noktasına bağlı ya da boşta olabilir.</summary>
public sealed class WireEnd
{
    public string? Component { get; set; }
    public string? Terminal { get; set; }
    public string? Junction { get; set; }

    public bool IsTerminal => Component != null && Terminal != null;
    public bool IsJunction => Junction != null;
    public bool IsFree => !IsTerminal && !IsJunction;

    public static WireEnd ToTerminal(string component, string terminal) => new() { Component = component, Terminal = terminal };
    public static WireEnd ToJunction(string junction) => new() { Junction = junction };
    public static WireEnd Free() => new();

    public WireEnd Clone() => new() { Component = Component, Terminal = Terminal, Junction = Junction };

    public bool SameTarget(WireEnd other) =>
        (IsTerminal && other.IsTerminal && Component == other.Component && Terminal == other.Terminal) ||
        (IsJunction && other.IsJunction && Junction == other.Junction);
}

public sealed class Wire
{
    public string Id { get; set; } = "";
    public string Sheet { get; set; } = "s1";
    public WireEnd A { get; set; } = new();
    public WireEnd B { get; set; } = new();

    /// <summary>Uç noktalar dahil, dik açılı köşe noktaları.</summary>
    public List<Vec> Points { get; set; } = [];

    public Wire Clone() => new()
    {
        Id = Id,
        Sheet = Sheet,
        A = A.Clone(),
        B = B.Clone(),
        Points = [.. Points]
    };
}

public sealed class Junction
{
    public string Id { get; set; } = "";
    public string Sheet { get; set; } = "s1";
    public Vec Position { get; set; }
}

public sealed class TextNote
{
    public string Id { get; set; } = "";
    public string Sheet { get; set; } = "s1";
    public Vec Position { get; set; }
    public Vec Size { get; set; } = new(120, 20);
    public string Text { get; set; } = "";
}
