using YamlDotNet.Serialization;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// YAML deserialization class for config/depth_boons.yaml.
/// Maps depth (int) to boon definition.
/// </summary>
public sealed class DepthBoonConfig
{
    [YamlMember(Alias = "depth_boons")]
    public Dictionary<int, DepthBoonYamlEntry> DepthBoons { get; set; } = new();
}

public sealed class DepthBoonYamlEntry
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "display_name")]
    public string DisplayName { get; set; } = "";

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = "";

    [YamlMember(Alias = "hp_bonus")]
    public int HpBonus { get; set; }

    [YamlMember(Alias = "immediate_heal")]
    public int ImmediateHeal { get; set; }

    [YamlMember(Alias = "accuracy_bonus")]
    public int AccuracyBonus { get; set; }

    [YamlMember(Alias = "defense_bonus")]
    public int DefenseBonus { get; set; }

    [YamlMember(Alias = "min_damage_bonus")]
    public int MinDamageBonus { get; set; }

    public BoonDefinition ToBoonDefinition() => new(
        BoonId: Id,
        DisplayName: DisplayName,
        Description: Description,
        HpBonus: HpBonus,
        ImmediateHeal: ImmediateHeal,
        AccuracyBonus: AccuracyBonus,
        DefenseBonus: DefenseBonus,
        MinDamageBonus: MinDamageBonus
    );
}
