namespace UnderWarden.Logic.Content;

/// <summary>
/// All content loaded from an entities YAML file.
/// </summary>
public sealed class ContentBundle
{
    public Dictionary<string, MonsterDefinition> Monsters { get; init; } = new();
    public Dictionary<string, ItemDefinition> Items { get; init; } = new();
    public Dictionary<string, ConsumableDefinition> Consumables { get; init; } = new();
    public IReadOnlyList<FloorItemPoolEntry> FloorItemPool { get; init; } = [];

    /// <summary>
    /// All scroll and wand spell definitions keyed by spell item ID (e.g. "scroll_of_lightning").
    /// Includes both is_wand=false (scrolls) and is_wand=true (wands) entries.
    /// </summary>
    public Dictionary<string, SpellDefinition> SpellItems { get; init; } = new();
}
