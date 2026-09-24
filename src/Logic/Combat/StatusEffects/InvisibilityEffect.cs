using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is invisible — cannot be targeted by monsters using normal sight.
/// Applied by: Invisibility Scroll (self).
/// Duration default: 30 turns (PoC: invisibility.duration = 30).
/// Tick behavior (hide from monster FOV, breaks on attack) implemented in Phase 3 combat effects.
/// </summary>
public sealed class InvisibilityEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "invisible";
    public int RemainingTurns { get; set; } = 30;
    public bool IsPermanent => false;
}
