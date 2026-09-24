using YamlDotNet.Serialization;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Data-driven definition of a species-specific ability usable during possession.
/// Loaded from entities.yaml monster definitions. Currently infrastructure-only —
/// populated when Hall Wardens and other ability-bearing species ship.
/// </summary>
public sealed class MonsterAbilityDefinition
{
    [YamlMember(Alias = "ability_id")]
    public string AbilityId { get; set; } = "";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = "";

    /// <summary>
    /// Drives TurnController dispatch. E.g. "grapple", "rally", "soul_drain".
    /// Unrecognised action types resolve as Wait (graceful degradation).
    /// </summary>
    [YamlMember(Alias = "action_type")]
    public string ActionType { get; set; } = "";

    [YamlMember(Alias = "range")]
    public int Range { get; set; } = 1;
}
