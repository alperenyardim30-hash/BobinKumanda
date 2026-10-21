using System.Reflection;

namespace KumandaLab.Core.Catalog;

/// <summary>Tüm eleman tiplerinin kaydı. Bu derlemedeki ComponentDef türevleri otomatik bulunur.</summary>
public static class ComponentCatalog
{
    private static readonly Dictionary<string, ComponentDef> ById;
    private static readonly Dictionary<int, ComponentDef> ByLegacy;

    public static IReadOnlyList<ComponentDef> All { get; }

    static ComponentCatalog()
    {
        var defs = new List<ComponentDef>();
        foreach (Type t in typeof(ComponentCatalog).Assembly.GetTypes())
        {
            if (!t.IsAbstract && typeof(ComponentDef).IsAssignableFrom(t) &&
                t.GetConstructor(BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes) != null)
            {
                defs.Add((ComponentDef)Activator.CreateInstance(t)!);
            }
        }
        defs.Sort((a, b) => a.Category != b.Category
            ? a.Category.CompareTo(b.Category)
            : string.CompareOrdinal(a.TypeId, b.TypeId));
        All = defs;
        ById = defs.ToDictionary(d => d.TypeId, StringComparer.Ordinal);
        ByLegacy = defs.Where(d => d.LegacyType.HasValue).ToDictionary(d => d.LegacyType!.Value);
    }

    public static ComponentDef? Find(string typeId) => ById.GetValueOrDefault(typeId);

    public static ComponentDef Get(string typeId) =>
        ById.TryGetValue(typeId, out ComponentDef? d) ? d : throw new KeyNotFoundException("Bilinmeyen eleman tipi: " + typeId);

    public static ComponentDef? FindLegacy(int legacyType) => ByLegacy.GetValueOrDefault(legacyType);
}
