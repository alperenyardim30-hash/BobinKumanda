using System.Globalization;
using BobinKumanda.Core.Catalog;
using BobinKumanda.Core.Model;

namespace BobinKumanda.Core.Editing;

/// <summary>
/// IEC 81346 harf koduyla otomatik etiket: K1, K2, S1, H1... Kontaklar (izleyen elemanlar) kendi numarasını almaz,
/// bağlı olduğu bobinin etiketini taşır.
/// </summary>
public static class Labeler
{
    /// <summary>Bu önekle henüz kullanılmayan en küçük numaralı etiket (K1 silindiyse yeni röle yine K1 olur).</summary>
    public static string Next(Circuit circuit, ComponentDef def, IEnumerable<string>? alsoTaken = null)
    {
        if (def.DefaultLabel != null)
        {
            return def.DefaultLabel;
        }
        string prefix = def.LabelPrefix;
        var used = new HashSet<int>();
        void Take(string label)
        {
            if (Number(label, prefix) is int n)
            {
                used.Add(n);
            }
        }
        foreach (ComponentInstance c in circuit.Components)
        {
            ComponentDef? d = ComponentCatalog.Find(c.Type);
            if (d != null && d.Role != DeviceRole.Follower)
            {
                Take(c.Label);
            }
        }
        if (alsoTaken != null)
        {
            foreach (string l in alsoTaken)
            {
                Take(l);
            }
        }
        int i = 1;
        while (used.Contains(i))
        {
            i++;
        }
        return prefix + i.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>"K12" → 12 (önek "K" ise). "KT1" K öneki için sayılmaz.</summary>
    public static int? Number(string label, string prefix)
    {
        string l = label.Trim();
        if (l.Length <= prefix.Length || !l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return int.TryParse(l.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : null;
    }

    /// <summary>Kontakların bağlanabileceği ana cihazların etiketleri (röleler önce), tekrarsız.</summary>
    public static List<string> MasterLabels(Circuit circuit)
    {
        var relays = new List<string>();
        var others = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ComponentInstance c in circuit.Components)
        {
            if (ComponentCatalog.Find(c.Type) is { Role: DeviceRole.Master } d && c.Label.Trim().Length > 0 && seen.Add(c.Label.Trim()))
            {
                (d.Category == ComponentCategory.Relays ? relays : others).Add(c.Label.Trim());
            }
        }
        relays.Sort(NaturalCompare);
        others.Sort(NaturalCompare);
        relays.AddRange(others);
        return relays;
    }

    /// <summary>K2 &lt; K10 olacak şekilde karşılaştırma.</summary>
    public static int NaturalCompare(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                int c = long.Parse(a.AsSpan(si, Math.Min(i - si, 18)), CultureInfo.InvariantCulture)
                    .CompareTo(long.Parse(b.AsSpan(sj, Math.Min(j - sj, 18)), CultureInfo.InvariantCulture));
                if (c != 0)
                {
                    return c;
                }
            }
            else
            {
                int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0)
                {
                    return c;
                }
                i++;
                j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
