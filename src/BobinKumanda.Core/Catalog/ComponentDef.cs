using System.Globalization;
using BobinKumanda.Core.Drawing;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;
using BobinKumanda.Core.Simulation;

namespace BobinKumanda.Core.Catalog;

public enum ComponentCategory
{
    Supply,
    Protection,
    Control,
    Contacts,
    Relays,
    Sensors,
    Loads,
    Motors,
    Systems,
    Misc
}

/// <summary>Cihaz grubu içindeki rol: bobin gibi durumu belirleyen "ana" eleman, onu izleyen kontaklar ya da tek başına elemanlar.</summary>
public enum DeviceRole
{
    Standalone,
    Master,
    Follower
}

/// <summary>Bir klemensin tanımı. Konum yerel (döndürülmemiş) koordinattadır.</summary>
public sealed record TerminalDef(string Id, Vec Position, Side Side, string? Number = null);

public enum ParamKind { Number, Integer, Bool, Choice, Text }

public sealed record ParamDef(
    string Key,
    ParamKind Kind,
    string Default,
    string? Unit = null,
    double Min = 0,
    double Max = 0,
    string[]? Choices = null)
{
    public string NameKey => "param." + Key;

    public string ChoiceNameKey(string choice) => "choice." + Key + "." + choice;

    public static ParamDef Number(string key, double def, string? unit, double min, double max) =>
        new(key, ParamKind.Number, def.ToString("R", CultureInfo.InvariantCulture), unit, min, max);

    public static ParamDef Integer(string key, int def, int min, int max) =>
        new(key, ParamKind.Integer, def.ToString(CultureInfo.InvariantCulture), null, min, max);

    public static ParamDef Choice(string key, string def, params string[] choices) =>
        new(key, ParamKind.Choice, def, Choices: choices);

    public static ParamDef Bool(string key, bool def) =>
        new(key, ParamKind.Bool, def ? "True" : "False");
}

/// <summary>
/// Katalogdaki bir eleman tipi. Yeni eleman eklemek için bu sınıftan türeyip <see cref="ComponentCatalog"/>'a kaydetmek yeterli.
/// </summary>
public abstract class ComponentDef
{
    public abstract string TypeId { get; }
    public abstract ComponentCategory Category { get; }

    /// <summary>IEC 81346 harf kodu: K (röle), S (buton), F (koruma), M (motor), H (lamba), Q (şalter), B (sensör)...</summary>
    public virtual string LabelPrefix => "X";

    public virtual DeviceRole Role => DeviceRole.Standalone;

    /// <summary>Numaralandırma yerine sabit etiket (ör. besleme için "L1").</summary>
    public virtual string? DefaultLabel => null;

    /// <summary>Döndürülmemiş sınır kutusu boyutu.</summary>
    public abstract Vec Size { get; }

    public abstract IReadOnlyList<TerminalDef> Terminals { get; }

    public virtual IReadOnlyList<ParamDef> Params => [];

    /// <summary>Etiketin varsayılan yeri (elemanın konumuna göre, dünya ekseninde).</summary>
    public virtual Vec DefaultLabelOffset => new(0, -12);

    /// <summary>Eski EKTS'deki ElementType değeri; .esc dönüşümünde kullanılır.</summary>
    public virtual int? LegacyType => null;

    /// <summary>Eski programdaki bağlantı ucu sayısı; sonradan eklenen klemensler (motorda PE) eski dosyada yoktur.</summary>
    public virtual int LegacyTerminalCount => Terminals.Count;

    /// <summary>Paletteki arama için ek anahtar kelimeler (iki dilde).</summary>
    public virtual string Tags => "";

    /// <summary>Paletteki klavye kısayolu (tek harf), yoksa null.</summary>
    public virtual char? Shortcut => null;

    /// <summary>Paletten gizli (yalnızca eski dosyalardan gelen elemanlar için).</summary>
    public virtual bool Hidden => false;

    /// <summary>Elemanın içinde kalıcı olarak birbirine bağlı klemensler (klemens, PE barası...).</summary>
    public virtual IReadOnlyList<(string A, string B)> NetLinks => [];

    /// <summary>Aynı etiketli bütün örnekler tek bir iletken sayılır (sayfa bağlantı oku).</summary>
    public virtual bool LinksByLabel => false;

    /// <summary>Etiketi sembolün içine kendisi yazar (sayfa bağlantı oku); dışarıda ayrıca gösterilmez.</summary>
    public virtual bool DrawsOwnLabel => false;

    /// <summary>Yardımcı kontak: numarası cihazdaki sırasına göre verilir (13-14, 21-22, 33-34...).</summary>
    public virtual bool AutoNumbered => false;

    /// <summary>Sıra numarasına göre klemens numarası; sıra numarası olmayan elemanlarda tanımdaki numara.</summary>
    public virtual string? NumberFor(TerminalDef t, ContactNumber n) => t.Number;

    /// <summary>Etiketin altında gösterilen kısa değer (zaman rölesinde "3 s").</summary>
    public virtual string? SubLabel(ComponentInstance c) => null;

    /// <summary>Klemens numaraları gösterilir. Besleme, motor ve hazır sistemlerde uç adları sembolün içindedir.</summary>
    public virtual bool ShowsTerminalNumbers => Category is not (ComponentCategory.Supply or ComponentCategory.Systems or ComponentCategory.Motors);

    /// <summary>
    /// Alt yazının (değer, süre) ve altındaki çapraz referansın yeri. Yatay elemanda sembolün altında ortada;
    /// dikey çizilen elemanda alttaki kablonun üstüne binmesin diye etiketin altında.
    /// </summary>
    public virtual (Vec Anchor, TextAlign Align) SubLabelPlace(ComponentInstance c, Circuit? circuit = null)
    {
        if ((c.Rotation & 1) == 1)
        {
            return (LabelWorld(c, circuit) + new Vec(0, CircuitRenderer.LabelSize * 1.2 + 4), TextAlign.Left);
        }
        Vec size = WorldSize(c);
        return (c.Position + new Vec(size.X / 2, size.Y + 6), TextAlign.Center);
    }

    public string Name => Loc.T("comp." + TypeId);
    public string Info => Loc.TryT("info." + TypeId) ?? "";

    public abstract void Draw(ISymbolPainter p, DrawContext ctx);

    /// <summary>Simülasyon modeli. Pasif elemanlar (klemens, sayfa bağlantısı) null döndürebilir.</summary>
    public abstract SimComponent? CreateSim(ComponentInstance instance);

    public TerminalDef? FindTerminal(string id)
    {
        foreach (TerminalDef t in Terminals)
        {
            if (t.Id == id)
            {
                return t;
            }
        }
        return null;
    }

    public ParamDef? FindParam(string key)
    {
        foreach (ParamDef p in Params)
        {
            if (p.Key == key)
            {
                return p;
            }
        }
        return null;
    }

    public void ApplyDefaults(ComponentInstance instance)
    {
        foreach (ParamDef p in Params)
        {
            instance.Params.TryAdd(p.Key, p.Default);
        }
    }

    public Vec WorldSize(ComponentInstance c) => Placement.RotatedSize(Size, c.Rotation);

    public RectD Bounds(ComponentInstance c)
    {
        Vec s = WorldSize(c);
        return new RectD(c.Position.X, c.Position.Y, s.X, s.Y);
    }

    public Vec TerminalWorld(ComponentInstance c, TerminalDef t) =>
        Placement.ToWorld(t.Position, Size, c.Rotation, c.Mirror, c.Position);

    public Side TerminalSide(ComponentInstance c, TerminalDef t) =>
        Placement.RotateSide(t.Side, c.Rotation, c.Mirror);

    /// <summary>Etiketin sol üst köşesi. Devre verilirse varsayılan yer devreye göre seçilebilir (zaman rölesi kontağı).</summary>
    public Vec LabelWorld(ComponentInstance c, Circuit? circuit = null) => c.Position + (c.LabelOffset ?? DefaultLabelOffsetFor(c, circuit));

    protected virtual Vec DefaultLabelOffsetFor(ComponentInstance c, Circuit? circuit) =>
        (c.Rotation & 1) == 1 ? new Vec(WorldSize(c).X + 4, 0) : DefaultLabelOffset;
}
