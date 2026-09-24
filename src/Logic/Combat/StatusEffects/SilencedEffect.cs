using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is silenced — cannot use scrolls or wands for the duration.
/// Applied by: Silence Scroll (single target).
/// Duration default: 3 turns.
/// Tick behavior (block item use) implemented in Phase 3 combat effects.
/// </summary>
public sealed class SilencedEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "silenced";
    public int RemainingTurns { get; set; } = 3;
    public bool IsPermanent => false;
}
