using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// How the player selects the target for a spell.
/// Self and AutoClosest require no UI interaction — the GameController resolves them immediately.
/// Targeted modes require the player to pick a tile or entity before the spell fires.
/// </summary>
public enum TargetingMode
{
    /// <summary>No target needed — spell affects caster or entire map.</summary>
    Self,

    /// <summary>Auto-picks the nearest visible enemy. No UI. Fails gracefully if none visible.</summary>
    AutoClosest,

    /// <summary>AoE centered on caster — radius from SpellEffect.Radius.</summary>
    AoeSelf,

    /// <summary>Player taps an entity to target (deferred — Phase 3).</summary>
    SingleTarget,

    /// <summary>Player taps a floor tile (deferred — Phase 3).</summary>
    Location,

    /// <summary>Two-step portal placement (deferred — Phase 5).</summary>
    Portal,
}

/// <summary>
/// Spell payload attached to a scroll or wand entity.
/// Scrolls: paired with Consumable (for consumption on use).
/// Wands: paired with WandComponent (for charge tracking).
///
/// SpellId drives dispatch in SpellResolver — no virtual methods, one switch.
/// All numeric parameters (damage, radius, etc.) live here so the resolver stays
/// data-driven and spell stats can be tuned in YAML without touching C#.
/// </summary>
public sealed class SpellEffect : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>
    /// Canonical spell identifier, e.g. "lightning", "earthquake", "magic_mapping".
    /// Must match a handler in SpellResolver.Resolve.
    /// </summary>
    public string SpellId { get; set; } = "";

    /// <summary>How the player selects a target for this spell.</summary>
    public TargetingMode Targeting { get; set; } = TargetingMode.Self;

    /// <summary>Base damage dealt (for direct-damage spells). 0 if not applicable.</summary>
    public int Damage { get; set; }

    /// <summary>AoE radius in tiles. 0 = single target.</summary>
    public int Radius { get; set; }

    /// <summary>Max range in tiles for auto-target spells. 0 = no range limit.</summary>
    public int Range { get; set; }

    /// <summary>Duration in turns for applied status effects. 0 = instant.</summary>
    public int Duration { get; set; }

    /// <summary>
    /// Probability (0.0–1.0) that the spell misfires and picks a random target.
    /// Used by Teleport Scroll (0.10 = 10% misfire). 0 = no misfire chance.
    /// </summary>
    public double MisfireChance { get; set; }

    /// <summary>
    /// Spell ID for the throw effect of a throwable potion. When set, the
    /// presentation layer enters targeting mode instead of immediately drinking.
    /// Null for drink-only potions and all scrolls/wands.
    /// </summary>
    public string? ThrowSpellId { get; set; }
}
