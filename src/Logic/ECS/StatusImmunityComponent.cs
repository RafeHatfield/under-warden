namespace UnderWarden.Logic.ECS;

/// <summary>
/// Marks an entity as immune to specific status effects.
/// Attached by MonsterFactory when MonsterDefinition.StatusImmunities is non-null.
/// Uses YAML immunity key strings (e.g. "confusion", "slow", "fear", "poison", "bleed").
/// Checked by StatusEffectProcessor.ApplyEffect before applying any effect.
/// </summary>
public sealed class StatusImmunityComponent : IComponent
{
    public Entity? Owner { get; set; }

    private readonly HashSet<string> _immunities;

    public StatusImmunityComponent(IEnumerable<string> immunities)
    {
        _immunities = new HashSet<string>(immunities, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsImmuneTo(string immunityKey) => _immunities.Contains(immunityKey);

    /// <summary>The immunity keys, exposed read-only so the mid-run serializer can persist them
    /// (the set is otherwise private). Not used by gameplay — mutation stays via the constructor.</summary>
    public IReadOnlyCollection<string> Immunities => _immunities;
}
