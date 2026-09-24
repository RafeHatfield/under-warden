using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is heroic — gains a significant bonus to attack accuracy and damage.
/// Applied by: Potion of Heroism (drink effect).
/// PoC values: AttackBonus=+3 to-hit, DamageBonus=+3 damage, duration=30 turns.
///
/// Stacks additively with RallyEffect (chieftain ability) and FocusedEffect (sunburst potion) —
/// they are separate component types so no conflict.
/// Read by CombatResolver alongside RallyEffect and FocusedEffect.
/// </summary>
public sealed class HeroismEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "heroism";
    public int RemainingTurns { get; set; } = 30;
    public bool IsPermanent => false;

    /// <summary>Added to the entity's attack roll (to-hit). PoC value: +3.</summary>
    public int AttackBonus { get; set; } = 3;

    /// <summary>Added to the entity's damage on hit. PoC value: +3.</summary>
    public int DamageBonus { get; set; } = 3;
}
