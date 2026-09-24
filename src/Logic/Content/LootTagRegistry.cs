using UnderWarden.Logic.Balance;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Loads and indexes config/loot_tags.yaml.
///
/// Provides fast category+band lookups for LootController. Built once at startup,
/// read-only after construction — thread-safe for concurrent harness runs.
///
/// The registry indexes items per category for O(1) category lookup.
/// Band filtering is applied at query time (not at load time) so the same
/// entry can serve multiple category+band combinations efficiently.
/// </summary>
public sealed class LootTagRegistry
{
    // category → all LootTag entries that include that category
    private readonly Dictionary<string, List<LootTag>> _byCategory;

    // item_id → LootTag for direct lookup (validation, test assertions)
    private readonly Dictionary<string, LootTag> _byItemId;

    private LootTagRegistry(
        Dictionary<string, List<LootTag>> byCategory,
        Dictionary<string, LootTag> byItemId)
    {
        _byCategory = byCategory;
        _byItemId = byItemId;
    }

    /// <summary>
    /// Load from a YAML string. Uses YamlDotNet with underscore naming convention.
    /// </summary>
    public static LootTagRegistry FromYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithObjectFactory(new AotObjectFactory())
            .IgnoreUnmatchedProperties()
            .Build();

        var file = deserializer.Deserialize<LootTagsFile>(yaml);
        var tags = file?.LootTags ?? new List<LootTag>();

        return BuildIndex(tags);
    }

    /// <summary>Load from a file path. Convenience wrapper for tests and harness.</summary>
    public static LootTagRegistry FromFile(string path)
        => FromYaml(File.ReadAllText(path));

    /// <summary>
    /// Get all LootTag entries for a given category that are available in the given band.
    ///
    /// Returns entries where band_min &lt;= bandInt &lt;= band_max.
    /// Results are ordered by weight descending (heaviest items first) for deterministic
    /// selection behaviour — the caller still does weighted random, but the order is stable.
    ///
    /// Returns an empty list if the category is unknown or no items are available in-band.
    /// </summary>
    public IReadOnlyList<LootTag> GetItemsForCategory(string category, LootBand band)
    {
        int bandInt = (int)band;
        if (!_byCategory.TryGetValue(category, out var entries))
            return Array.Empty<LootTag>();

        return entries
            .Where(t => t.BandMin <= bandInt && t.BandMax >= bandInt)
            .OrderByDescending(t => t.Weight)
            .ToList();
    }

    /// <summary>All category names present in the registry.</summary>
    public IReadOnlyList<string> GetAllCategories()
        => _byCategory.Keys.OrderBy(k => k).ToList();

    /// <summary>
    /// Look up a single LootTag by item ID.
    /// Returns null if the item is not in the registry.
    /// </summary>
    public LootTag? GetTag(string itemId)
        => _byItemId.TryGetValue(itemId, out var tag) ? tag : null;

    /// <summary>Total number of item entries in the registry.</summary>
    public int Count => _byItemId.Count;

    private static LootTagRegistry BuildIndex(List<LootTag> tags)
    {
        var byCategory = new Dictionary<string, List<LootTag>>(StringComparer.OrdinalIgnoreCase);
        var byItemId = new Dictionary<string, LootTag>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag.ItemId))
                continue;

            byItemId[tag.ItemId] = tag;

            foreach (var category in tag.Categories)
            {
                if (string.IsNullOrWhiteSpace(category)) continue;

                if (!byCategory.TryGetValue(category, out var list))
                {
                    list = new List<LootTag>();
                    byCategory[category] = list;
                }
                list.Add(tag);
            }
        }

        return new LootTagRegistry(byCategory, byItemId);
    }
}
