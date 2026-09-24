using YamlDotNet.Serialization;

namespace UnderWarden.Logic.Content;

/// <summary>
/// One entry in the floor_item_pool YAML section.
/// Defines an item that can appear as floor loot, with a relative spawn weight and depth range.
///
/// max_depth: Items can age out — a club is irrelevant past depth 15.
/// Default of 99 means no cap unless explicitly specified.
/// The LootController's band system (loot_tags.yaml) is the canonical depth-gating mechanism;
/// max_depth here is a coarse legacy filter for the flat-pool fallback path.
/// </summary>
public sealed class FloorItemPoolEntry
{
    [YamlMember(Alias = "item")]
    public string ItemId { get; set; } = "";

    [YamlMember(Alias = "weight")]
    public int Weight { get; set; } = 10;

    [YamlMember(Alias = "min_depth")]
    public int MinDepth { get; set; } = 1;

    /// <summary>Maximum depth this item can appear in the flat fallback pool. Default 99 = no cap.</summary>
    [YamlMember(Alias = "max_depth")]
    public int MaxDepth { get; set; } = 99;
}
