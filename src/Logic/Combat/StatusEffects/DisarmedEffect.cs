using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity cannot use its equipped weapon — attacks use unarmed (fist) damage only.
/// Applied by: Disarm Scroll.
/// Duration default: 3 turns.
/// Tick behavior (weapon damage suppression) implemented in Phase 3 combat effects.
/// </summary>
public sealed class DisarmedEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "disarmed";
    public int RemainingTurns { get; set; } = 3;
    public bool IsPermanent => false;
}
