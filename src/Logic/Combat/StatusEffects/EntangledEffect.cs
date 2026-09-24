using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is entangled by roots — cannot move, but can still attack adjacent targets.
/// Applied by: Root Potion, root trap.
/// PoC values: RemainingTurns=5.
/// Movement block implemented in Phase 2 movement effects.
/// Unlike ImmobilizedEffect, entangled entities CAN still attack.
/// </summary>
public sealed class EntangledEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "entangled";
    public int RemainingTurns { get; set; } = 5;
    public bool IsPermanent => false;
}
