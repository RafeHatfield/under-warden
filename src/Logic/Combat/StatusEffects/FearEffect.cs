using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is terrified — flees from the source of fear at full speed.
/// Applied by: Fear Scroll (AoE centered on caster).
/// Duration default: 15 turns (PoC: fear.duration = 15).
/// Tick behavior (flee AI override) implemented in Phase 2 movement effects.
/// </summary>
public sealed class FearEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "fear";
    public int RemainingTurns { get; set; } = 15;
    public bool IsPermanent => false;
}
