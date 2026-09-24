using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// Tracks damage type resistances and vulnerabilities for an entity.
/// Resistance halves damage of that type. Vulnerability doubles it.
/// Applied after base damage calc, before minimum floor.
/// </summary>
public sealed class DamageModifiers : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>Damage type that this entity resists (takes half damage). Null = none.</summary>
    public string? Resistance { get; set; }

    /// <summary>Damage type that this entity is vulnerable to (takes double damage). Null = none.</summary>
    public string? Vulnerability { get; set; }

    /// <summary>
    /// Apply resistance/vulnerability to damage based on the incoming damage type.
    /// Returns the modified damage amount.
    /// </summary>
    public int ApplyTo(int damage, string? damageType)
    {
        if (damageType == null || damage <= 0)
            return damage;

        string dt = damageType.ToLowerInvariant();

        if (Vulnerability != null && dt == Vulnerability.ToLowerInvariant())
            return damage * 2;

        if (Resistance != null && dt == Resistance.ToLowerInvariant())
            return Math.Max(1, damage / 2);

        return damage;
    }
}
