using YamlDotNet.Serialization;

namespace UnderWarden.Logic.Balance.Etp;

/// <summary>
/// ETP budget values for a single room encounter.
/// Ported from etp_config.yaml bands[B1..B5].room_etp.
/// </summary>
public sealed class RoomEtpBudget
{
    [YamlMember(Alias = "min")]
    public double Min { get; set; }

    [YamlMember(Alias = "max")]
    public double Max { get; set; }
}

/// <summary>
/// ETP budget values for an entire dungeon floor.
/// </summary>
public sealed class FloorEtpBudget
{
    [YamlMember(Alias = "min")]
    public double Min { get; set; }

    [YamlMember(Alias = "max")]
    public double Max { get; set; }
}

/// <summary>
/// Per-band difficulty configuration.
/// Ported from PoC etp.py:31-104, BandConfig dataclass.
/// </summary>
public sealed class BandConfig
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = "";

    [YamlMember(Alias = "floor_min")]
    public int FloorMin { get; set; }

    [YamlMember(Alias = "floor_max")]
    public int FloorMax { get; set; }

    [YamlMember(Alias = "hp_multiplier")]
    public double HpMultiplier { get; set; } = 1.0;

    [YamlMember(Alias = "damage_multiplier")]
    public double DamageMultiplier { get; set; } = 1.0;

    [YamlMember(Alias = "perk_unlock")]
    public bool PerkUnlock { get; set; } = false;

    [YamlMember(Alias = "room_etp")]
    public RoomEtpBudget RoomEtp { get; set; } = new();

    [YamlMember(Alias = "floor_etp")]
    public FloorEtpBudget FloorEtp { get; set; } = new();

    [YamlMember(Alias = "target_ttk_hits")]
    public int TargetTtkHits { get; set; }

    [YamlMember(Alias = "target_ttd_hits")]
    public int TargetTtdHits { get; set; }
}
