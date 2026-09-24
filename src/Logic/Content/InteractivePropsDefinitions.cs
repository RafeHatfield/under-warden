using YamlDotNet.Serialization;

namespace UnderWarden.Logic.Content;

// ─────────────────────────────────────────────────────────────────────────────
// DTOs for config/interactive_props.yaml
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Root structure for config/interactive_props.yaml.
/// Top-level keys: interactive_props (prop definitions) + trap_payloads (named payload tables).
/// </summary>
public sealed class InteractivePropsFile
{
    [YamlMember(Alias = "interactive_props")]
    public Dictionary<string, InteractivePropDefinition> Props { get; set; } = new();

    [YamlMember(Alias = "trap_payloads")]
    public Dictionary<string, TrapPayloadDefinition> TrapPayloads { get; set; } = new();
}

/// <summary>
/// Definition for a single interactive prop type (barrel, bookshelf, bone_pile).
/// </summary>
public sealed class InteractivePropDefinition
{
    [YamlMember(Alias = "closed_tile_id")]
    public int ClosedTileId { get; set; }

    [YamlMember(Alias = "open_tile_id")]
    public int OpenTileId { get; set; }

    [YamlMember(Alias = "loot")]
    public PropLootConfig? Loot { get; set; }

    /// <summary>
    /// Probability (0.0–1.0) that a trap fires when this prop is bumped.
    /// 0.0 = never trapped (bookshelf). 0.15 = barrel default.
    /// </summary>
    [YamlMember(Alias = "trap_chance")]
    public double TrapChance { get; set; }

    /// <summary>
    /// Weighted table of named trap payload IDs from the trap_payloads section.
    /// Only used when TrapChance > 0.
    /// </summary>
    [YamlMember(Alias = "trap_table")]
    public List<WeightedPayloadEntry>? TrapTable { get; set; }

    // ── Bone pile rouse fields ──────────────────────────────────────────────

    /// <summary>Probability (0.0–1.0) that bumping this prop rouses a nearby monster.</summary>
    [YamlMember(Alias = "rouse_chance")]
    public double RouseChance { get; set; }

    /// <summary>Monster type ID to spawn when rouse fires (e.g. "zombie").</summary>
    [YamlMember(Alias = "rouse_monster")]
    public string? RouseMonster { get; set; }

    /// <summary>Chebyshev radius within which to search for a free spawn tile.</summary>
    [YamlMember(Alias = "rouse_radius")]
    public int RouseRadius { get; set; } = 4;

    /// <summary>Minimum dungeon depth at which rouse can fire. 0 disables the gate.</summary>
    [YamlMember(Alias = "rouse_min_depth")]
    public int RouseMinDepth { get; set; }
}

/// <summary>
/// Loot drop configuration for an interactive prop.
/// </summary>
public sealed class PropLootConfig
{
    /// <summary>
    /// Category weights for loot drops.
    /// Keys: "potion", "scroll", "weapon", "armor", "nothing".
    /// Values: integer weights (sum to anything; selection is proportional).
    /// </summary>
    [YamlMember(Alias = "weights")]
    public Dictionary<string, int> Weights { get; set; } = new();

    /// <summary>Minimum dungeon depth for loot to be eligible. Defaults to 1.</summary>
    [YamlMember(Alias = "min_depth")]
    public int MinDepth { get; set; } = 1;
}

/// <summary>
/// A weighted entry in a prop's trap_table: maps a named payload to a selection weight.
/// </summary>
public sealed class WeightedPayloadEntry
{
    [YamlMember(Alias = "weight")]
    public int Weight { get; set; }

    /// <summary>References a key in the top-level trap_payloads dictionary.</summary>
    [YamlMember(Alias = "payload")]
    public string Payload { get; set; } = "";
}

/// <summary>
/// A named, reusable trap payload definition (e.g. "spike_burst_small").
/// </summary>
public sealed class TrapPayloadDefinition
{
    [YamlMember(Alias = "actions")]
    public List<TrapActionDefinition> Actions { get; set; } = new();
}

/// <summary>
/// A single action within a trap payload definition.
/// Maps directly to the TrapAction runtime class via FeatureFactory.
/// </summary>
public sealed class TrapActionDefinition
{
    [YamlMember(Alias = "kind")]
    public string Kind { get; set; } = "";

    [YamlMember(Alias = "amount")]
    public int Amount { get; set; }

    [YamlMember(Alias = "duration")]
    public int Duration { get; set; }

    [YamlMember(Alias = "radius")]
    public int Radius { get; set; }

    [YamlMember(Alias = "target")]
    public string Target { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────
// DTOs for config/floor_traps.yaml
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Root structure for config/floor_traps.yaml.
/// </summary>
public sealed class FloorTrapsFile
{
    [YamlMember(Alias = "floor_traps")]
    public Dictionary<string, FloorTrapDefinition> Traps { get; set; } = new();
}

/// <summary>
/// Definition for a single floor trap type.
/// PoC-canonical fields from config/entities.yaml map_traps block.
/// </summary>
public sealed class FloorTrapDefinition
{
    [YamlMember(Alias = "visible_tile_id")]
    public int VisibleTileId { get; set; }

    /// <summary>
    /// Presentation-layer color modulate [r, g, b, a].
    /// Null means no modulation (standard tile color).
    /// Used to distinguish traps that share a tile ID (e.g. root_trap vs web_trap on 430).
    /// </summary>
    [YamlMember(Alias = "tile_modulate")]
    public List<float>? TileModulate { get; set; }

    [YamlMember(Alias = "is_detectable")]
    public bool IsDetectable { get; set; } = true;

    [YamlMember(Alias = "passive_detect_chance")]
    public double PassiveDetectChance { get; set; } = 0.10;

    [YamlMember(Alias = "actions")]
    public List<TrapActionDefinition> Actions { get; set; } = new();
}
