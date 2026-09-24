using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Tags a monster entity with its <see cref="ThreatArchetype"/> for role-aware balance health.
/// Attached at spawn by MonsterFactory from the def's threat_archetype string. Lets the soak harness
/// attribute a player death to the killer's role — and scan a floor for live escalators — directly off
/// the entity, without re-consulting the content layer. Sibling-in-spirit to ECS.SpeciesTag.
///
/// Absent when the monster def has no threat_archetype (unclassified): its kills carry no archetype.
/// </summary>
public sealed class ThreatArchetypeTag : IComponent
{
    public Entity? Owner { get; set; }

    public ThreatArchetype Archetype { get; }

    public ThreatArchetypeTag(ThreatArchetype archetype)
    {
        Archetype = archetype;
    }

    /// <summary>
    /// Parse a YAML threat_archetype string ("baseline" | "spike" | "escalator" | "fused") to the enum.
    /// Returns null for null/empty/unrecognized input — the monster is then left unclassified.
    /// </summary>
    public static ThreatArchetype? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return Enum.TryParse<ThreatArchetype>(raw, ignoreCase: true, out var a) ? a : null;
    }
}
