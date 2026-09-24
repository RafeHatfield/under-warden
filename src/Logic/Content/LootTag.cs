using YamlDotNet.Serialization;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Deserialized entry from config/loot_tags.yaml.
///
/// Each item in the game has exactly one LootTag entry that specifies:
///   - Which loot categories it belongs to (used by LootController for category selection)
///   - Its relative weight within those categories (higher = more likely when category is chosen)
///   - Which loot bands it can appear in (band_min=1 = B1 = depths 1-5)
///
/// Weights are relative within a category, not global. A weight-10 healing potion is 10x
/// more likely than a weight-1 item when the healing category is selected.
/// </summary>
public sealed class LootTag
{
    [YamlMember(Alias = "item_id")]
    public string ItemId { get; set; } = "";

    [YamlMember(Alias = "categories")]
    public List<string> Categories { get; set; } = new();

    [YamlMember(Alias = "weight")]
    public double Weight { get; set; } = 1.0;

    /// <summary>Minimum loot band (1-5) this item can appear in. 1 = B1 = depths 1-5.</summary>
    [YamlMember(Alias = "band_min")]
    public int BandMin { get; set; } = 1;

    /// <summary>Maximum loot band (1-5) this item can appear in. 5 = B5 = depths 21-25.</summary>
    [YamlMember(Alias = "band_max")]
    public int BandMax { get; set; } = 5;
}

/// <summary>Root YAML structure for loot_tags.yaml.</summary>
internal sealed class LootTagsFile
{
    [YamlMember(Alias = "loot_tags")]
    public List<LootTag> LootTags { get; set; } = new();
}
