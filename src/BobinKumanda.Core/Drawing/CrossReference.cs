using System.Globalization;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Components;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Drawing;

/// <summary>Yardımcı kontağın cihazdaki sırası ve zaman gecikmeli olup olmadığı.</summary>
public readonly record struct ContactNumber(int Sequence, bool Timed);

/// <summary>Çapraz referans satırı: bobinin altında, o bobine ait bir kontak (13-14) ve başka sayfadaysa sayfası.</summary>
public sealed record CrossRefEntry(string ContactId, string Text, string Sheet);

/// <summary>
/// Kontak numaraları ve çapraz referans. Aynı etiketli yardımcı kontaklar okuma sırasıyla (sayfa, yukarıdan aşağı,
/// soldan sağa) 1, 2, 3... sıra numarası alır. Zaman rölesinin gecikmeli kontakları ise rölenin değiştirici
/// kontak takımları gibi numaralanır: ilk NC 15-16, ilk NO 17-18, ikinciler 25-26 ve 27-28.
/// Her ana cihazın (bobin, buton) altında kontaklarının listesi gösterilir.
/// </summary>
public static class CrossReference
{
    public static Dictionary<string, ContactNumber> Numbers(Circuit circuit)
    {
        var result = new Dictionary<string, ContactNumber>(StringComparer.Ordinal);
        var timers = new HashSet<string>(circuit.Components
            .Where(c => ComponentCatalog.Find(c.Type) is TimerOnDef or TimerOffDef)
            .Select(c => c.Label.Trim()), StringComparer.OrdinalIgnoreCase);
        foreach (var group in Followers(circuit).Where(f => f.Def.AutoNumbered).GroupBy(f => f.C.Label.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            bool timerRelay = timers.Contains(group.Key);
            int seq = 1, timedNc = 0, timedNo = 0;
            foreach (var f in group)
            {
                bool timed = FollowerContactDef.TimedFor(circuit, f.C) != TimedMark.None;
                if (timed && timerRelay && f.Def is AuxContactDefBase aux)
                {
                    int n = aux.Kind switch
                    {
                        (true, false) => ++timedNc,
                        (false, true) => ++timedNo,
                        _ => timedNc = timedNo = Math.Max(timedNc, timedNo) + 1
                    };
                    result[f.C.Id] = new ContactNumber(n, true);
                    continue;
                }
                result[f.C.Id] = new ContactNumber(seq++, timed);
            }
        }
        return result;
    }

    /// <summary>Her ana cihaz kimliği için kontak listesi.</summary>
    public static Dictionary<string, List<CrossRefEntry>> Mirror(Circuit circuit, IReadOnlyDictionary<string, ContactNumber> numbers)
    {
        var sheetIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < circuit.Sheets.Count; i++)
        {
            sheetIndex[circuit.Sheets[i].Id] = i + 1;
        }
        var masters = new Dictionary<string, ComponentInstance>(StringComparer.OrdinalIgnoreCase);
        foreach (ComponentInstance c in circuit.Components)
        {
            if (ComponentCatalog.Find(c.Type) is { Role: DeviceRole.Master } && c.Label.Trim().Length > 0)
            {
                masters.TryAdd(c.Label.Trim(), c);
            }
        }
        var result = new Dictionary<string, List<CrossRefEntry>>(StringComparer.Ordinal);
        foreach (var (c, def) in Followers(circuit))
        {
            if (!masters.TryGetValue(c.Label.Trim(), out ComponentInstance? m))
            {
                continue;
            }
            numbers.TryGetValue(c.Id, out ContactNumber n);
            string text = ContactText(def, n);
            if (text.Length == 0)
            {
                continue;
            }
            if (c.Sheet != m.Sheet && sheetIndex.TryGetValue(c.Sheet, out int si))
            {
                text += "/" + si.ToString(CultureInfo.InvariantCulture);
            }
            if (!result.TryGetValue(m.Id, out var list))
            {
                list = [];
                result[m.Id] = list;
            }
            list.Add(new CrossRefEntry(c.Id, text, c.Sheet));
        }
        return result;
    }

    /// <summary>Kontağın kısa adı: "13-14", ana kontaklarda "1-2,3-4,5-6".</summary>
    public static string ContactText(ComponentDef def, ContactNumber n)
    {
        var numbers = def.Terminals.Select(t => def.NumberFor(t, n) ?? "").Where(s => s.Length > 0).ToList();
        if (numbers.Count == 0)
        {
            return "";
        }
        if (numbers.Count == 2)
        {
            return numbers[0] + "-" + numbers[1];
        }
        if (numbers.Count == 3)
        {
            // değiştirici: ortak-NC/NO
            return numbers[0] + "-" + numbers[1] + "/" + numbers[2];
        }
        var pairs = new List<string>();
        for (int i = 0; i + 1 < numbers.Count; i += 2)
        {
            pairs.Add(numbers[i] + "-" + numbers[i + 1]);
        }
        return string.Join(",", pairs);
    }

    private static IEnumerable<(ComponentInstance C, ComponentDef Def)> Followers(Circuit circuit)
    {
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < circuit.Sheets.Count; i++)
        {
            order[circuit.Sheets[i].Id] = i;
        }
        return circuit.Components
            .Select(c => (C: c, Def: ComponentCatalog.Find(c.Type)))
            .Where(x => x.Def is { Role: DeviceRole.Follower } && x.C.Label.Trim().Length > 0)
            .Select(x => (x.C, Def: x.Def!))
            .OrderBy(x => order.GetValueOrDefault(x.C.Sheet, int.MaxValue))
            .ThenBy(x => x.C.Position.Y)
            .ThenBy(x => x.C.Position.X);
    }
}
