using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is covered in acid — suppresses innate regeneration for the duration.
/// No tick damage. Duration-based: expires via StatusEffectProcessor.ProcessTurnEnd.
///
/// Suppression: while active on an entity, any InnateRegenComponent on the same entity
/// will not trigger during ProcessTurnStart. Player RegenerationEffect (from rings/potions)
/// is NOT suppressed — AcidEffect only targets InnateRegenComponent.
/// BurningEffect applies the same suppression rule — both fire and acid suppress innate regen.
///
/// Sources: acid_trap walk-over.
/// Weapon coating: when the player triggers an acid_trap, their equipped weapon gains
/// WeaponAcidCoating — handled separately in Phase 7 (TASK-020).
///
/// PoC alignment: new mechanic (acid_trap is a deviation addition, not in PoC).
/// </summary>
public sealed class AcidEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "acid";
    public int RemainingTurns { get; set; } = 8;
    public bool IsPermanent => false;
}
