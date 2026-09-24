using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity's skin hardens like bark — gains significant AC bonus for the duration.
/// Applied by: Root Potion secondary effect.
/// PoC values: AcBonus=4, RemainingTurns=8.
/// AC bonus is read at combat resolution time in CombatResolver.GetEffectiveAC — the
/// base ArmorClass stat is never mutated by this effect. Stacks with ShieldEffect
/// and ProtectionEffect.
/// </summary>
public sealed class BarkskinEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "barkskin";
    public int RemainingTurns { get; set; } = 8;
    public bool IsPermanent => false;

    /// <summary>AC bonus granted. PoC value: +4.</summary>
    public int AcBonus { get; set; } = 4;
}
