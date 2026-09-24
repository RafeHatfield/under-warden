using UnderWarden.Logic.Content;

namespace UnderWarden.Logic.ECS;

/// <summary>
/// Attached to monster entities that have species-specific abilities usable during possession.
/// MonsterFactory attaches this when the definition's Abilities list is non-empty.
/// Currently infrastructure-only — populated when Hall Wardens and ability-bearing species ship.
/// </summary>
public sealed class HostAbilityComponent : IComponent
{
    public Entity? Owner { get; set; }
    public IReadOnlyList<MonsterAbilityDefinition> Abilities { get; init; } = Array.Empty<MonsterAbilityDefinition>();
}
