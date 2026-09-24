using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is bleeding — takes 1–2 damage per turn depending on severity.
/// Also attracts nearby undead (within BleedAttractionRadius) each tick.
///
/// Sources: spike_trap, spike_burst payloads (trapped barrels/chests).
/// Cleared: duration expiry or healing potion (severity 1 only).
///
/// PoC alignment:
///   PoC spike_trap applies bleed. Values from plan: severity 1 = 1 dmg/turn, severity 2 = 2 dmg/turn.
///   Undead attraction is a new addition (PoC has no bleed-attraction mechanic).
/// </summary>
public sealed class BleedEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "bleed";
    public int RemainingTurns { get; set; } = 3;
    public bool IsPermanent => false;

    /// <summary>Severity 1 (standard bleed) or 2 (deep wound). Affects DamagePerTick and clearability.</summary>
    public int Severity { get; set; } = 1;

    /// <summary>Damage per turn. Derived from Severity: 1 at sev 1, 2 at sev 2.</summary>
    public int DamagePerTick => Severity == 1 ? 1 : 2;

    /// <summary>
    /// Radius within which undead monsters without a current target are attracted toward the bleeding entity.
    /// PoC deviation: new mechanic. Cap of 2 undead alerted per tick to prevent overwhelming surges.
    /// </summary>
    public const int BleedAttractionRadius = 6;
    public const int BleedAttractionCapPerTick = 2;
}
