namespace UnderWarden.Logic.ECS;

/// <summary>
/// Holds a list of trap actions that fire when a trap triggers or a prop is bumped.
/// Shared by FloorTrapComponent and DestructiblePropComponent so both paths use the
/// same TrapActionResolver.
/// </summary>
public sealed class TrapPayloadComponent : IComponent
{
    public Entity? Owner { get; set; }
    public List<TrapAction> Actions { get; init; } = new();
}

/// <summary>
/// A single effect within a trap payload.
///
/// Kinds and semantics:
///   "damage"       — direct HP damage via Fighter.TakeDamage(Amount)
///   "bleed"        — BleedEffect(severity=Amount, duration=Duration)
///   "acid"         — AcidEffect(duration=Duration); suppresses InnateRegenComponent
///   "burning"      — BurningEffect(DamagePerTurn=3, duration=Duration)
///   "poison"       — PoisonEffect(duration=Duration)
///   "slow"         — SlowedEffect(severity=Amount, duration=Duration)
///   "entangle"     — EntangledEffect(duration=Duration)
///   "teleport"     — random walkable tile; emits TeleportEvent(Reason="trap")
///   "alert_faction"— find faction monsters in Radius, set Alerted; Target=faction name
///   "descend"      — emit DescendEvent(Cause="hole_trap"); reuses existing descent pipeline
///   "spawn_monster"— find walkable tile in Radius, spawn Target via MonsterFactory
/// </summary>
public sealed class TrapAction
{
    /// <summary>
    /// "damage" | "bleed" | "acid" | "burning" | "poison" | "slow" |
    /// "entangle" | "teleport" | "alert_faction" | "descend" | "spawn_monster"
    /// </summary>
    public string Kind { get; init; } = "";

    /// <summary>Damage amount (for "damage"), or severity (for "bleed", "slow").</summary>
    public int Amount { get; init; }

    /// <summary>Duration in turns for status effects.</summary>
    public int Duration { get; init; }

    /// <summary>Search radius for "alert_faction" and "spawn_monster".</summary>
    public int Radius { get; init; }

    /// <summary>Faction name (for "alert_faction") or monster type ID (for "spawn_monster").</summary>
    public string Target { get; init; } = "";
}
