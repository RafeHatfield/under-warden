namespace UnderWarden.Logic.ECS;

/// <summary>
/// The effect a ring provides. Phase 1 effects are fully wired.
/// Phase 2 effects are present in the enum and YAML but their equip/unequip
/// handling is a no-op until their parent systems land.
/// </summary>
public enum RingEffectKind
{
    // Phase 1 — fully implemented
    Protection,     // +BaseDefense
    Strength,       // +Strength
    Dexterity,      // +Dexterity
    Constitution,   // +Constitution, +RingMaxHpBonus, +Hp on equip
    Might,          // +DamageMin/DamageMax
    Regeneration,   // passive heal 1 HP every 5 turns
    Speed,          // +SpeedBonusTracker.RingRatio (covers both speed and hummingbird rings)
    FreeAction,     // immune to slow/paralysis (adds FreeActionTag)
    Teleportation,  // 20% on-hit: teleport player to random open tile

    // Phase 2 — stubs only; equip/unequip does nothing until parent system lands
    Resistance,
    Clarity,
    Invisibility,
    Searching,
    Wizardry,
    Luck,
}

/// <summary>
/// Component attached to ring item entities. Carries the ring's effect type and numeric strength.
///
/// Strength semantics by kind:
///   Protection/Strength/Dexterity/Constitution/Might: raw stat delta (e.g. 2 for +2)
///   Regeneration: interval in turns (heal every N turns)
///   Speed: not used directly — the ring_effect "speed" vs "hummingbird" drives the ratio
///   FreeAction: ignored (presence of the component is the flag)
///   Teleportation: probability percentage (e.g. 20 for 20%)
/// </summary>
public sealed class RingEffectComponent : IComponent
{
    public Entity? Owner { get; set; }

    public RingEffectKind Kind { get; }

    /// <summary>Effect strength from YAML effect_strength field.</summary>
    public int Strength { get; }

    /// <summary>
    /// Speed ratio delta for Speed-kind rings.
    /// Ring of Speed = 0.10, Ring of Hummingbird = 0.25.
    /// Stored separately because the strength field uses integer YAML but ratios are double.
    /// </summary>
    public double SpeedRatio { get; }

    public RingEffectComponent(RingEffectKind kind, int strength, double speedRatio = 0.0)
    {
        Kind = kind;
        Strength = strength;
        SpeedRatio = speedRatio;
    }
}
