using System.Numerics;
using KumandaLab.Core.Catalog;
using KumandaLab.Core.Drawing;
using KumandaLab.Core.Model;

namespace KumandaLab.Core.Simulation;

public enum InteractionKind
{
    /// <summary>Fare basıldı (anlık butonlar basılı kalır).</summary>
    Press,
    /// <summary>Fare bırakıldı.</summary>
    Release,
    /// <summary>Kalıcı değiştir (kilitli buton, şalter, sınır anahtarı...).</summary>
    Toggle,
    /// <summary>Kaydırıcı değeri (seviye, basınç, sıcaklık) veya konum.</summary>
    SetValue,
    Reset,
    Test
}

public readonly record struct Interaction(InteractionKind Kind, double Value = 0);

public sealed record Readout(string Name, string Value);

/// <summary>Etiketle bulunan ana cihaz (bobin, buton, koruma rölesi...). Kontaklar bunun kanallarını izler.</summary>
public interface IDeviceMaster
{
    SimComponent Component { get; }

    /// <summary>Kontakların varsayılan olarak izlediği kanal (zaman rölesinde "timed").</summary>
    string DefaultChannel { get; }

    bool ChannelActive(string channel);
}

/// <summary>Bir elemanın simülasyon modeli. Her çözümden önce Stamp, sonra Evaluate çağrılır.</summary>
public abstract class SimComponent
{
    public ComponentInstance Instance { get; internal set; } = null!;
    public ComponentDef Def { get; internal set; } = null!;
    protected internal Simulator Sim { get; internal set; } = null!;

    internal int[] TerminalNodes = [];
    internal int InternalBase;

    public string Id => Instance.Id;
    public string Label => Instance.Label.Length > 0 ? Instance.Label : Def.Name;

    public SymbolState Visual { get; } = new();

    /// <summary>Elemanın içindeki ek düğüm sayısı (ör. 3 uçlu motorun yıldız noktası).</summary>
    public virtual int InternalNodeCount => 0;

    /// <summary>Kullanıcının tıklayarak etkileşebildiği eleman mı.</summary>
    public virtual bool IsInteractive => false;

    /// <summary>Zamanla değişen süreç (hızlanma, ısınma) varsa her adımda Advance çağrılır.</summary>
    public virtual bool NeedsTicks => false;

    protected Pin T(int index) => new(TerminalNodes[index], index);

    protected Pin Internal(int k) => new(InternalBase + k, -1);

    protected static Pin Ground => Pin.Ground;

    internal NetPoint TerminalPoint(int index) => NetPoint.Terminal(Instance.Id, Def.Terminals[index].Id);

    public bool IsConnected(int terminal) => Sim.Netlist.Nets[TerminalNodes[terminal]].Points.Count > 1;

    /// <summary>Simülasyon kurulurken, etiketle bağlı cihazları bulmak için.</summary>
    public virtual void Bind(DeviceRegistry devices) { }

    public abstract void Stamp(Stamper s);

    /// <summary>Çözümden sonra durum değişikliği olduysa true döner (yeniden çözülür).</summary>
    public virtual bool Evaluate(Solution sol) => false;

    /// <summary>Kararlı duruma gelince sürekli uyarıları bildirir.</summary>
    public virtual void Check(Solution sol) { }

    /// <summary>Sürekli süreçleri ilerletir; elektriksel model değiştiyse true.</summary>
    public virtual bool Advance(double dt, Solution sol) => false;

    public virtual void Interact(Interaction action) { }

    public virtual void UpdateVisual(Solution sol) { }

    public virtual IEnumerable<Readout> Readouts(Solution sol) => [];

    // Yardımcılar

    protected static double Mag(Complex c) => c.Magnitude;

    protected string L(string key, params object?[] args) => Localization.Loc.T(key, args);
}

/// <summary>Simülasyon sırasında etiket → ana cihaz eşlemesi.</summary>
public sealed class DeviceRegistry
{
    private readonly Dictionary<string, IDeviceMaster> masters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<IDeviceMaster>> duplicates = new(StringComparer.OrdinalIgnoreCase);

    internal void Register(IDeviceMaster master)
    {
        string key = Normalize(master.Component.Instance.Label);
        if (key.Length == 0)
        {
            return;
        }
        if (masters.TryGetValue(key, out IDeviceMaster? existing))
        {
            if (!duplicates.TryGetValue(key, out var list))
            {
                list = [existing];
                duplicates[key] = list;
            }
            list.Add(master);
            return;
        }
        masters[key] = master;
    }

    public IDeviceMaster? Find(string label) => masters.GetValueOrDefault(Normalize(label));

    public IEnumerable<KeyValuePair<string, List<IDeviceMaster>>> Duplicates => duplicates;

    public static string Normalize(string label) => label.Trim();
}
