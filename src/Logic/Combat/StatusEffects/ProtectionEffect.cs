using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is under magical protection — gains AC bonus for the duration.
/// Applied by: Potion of Protection.
/// PoC values: AcBonus=3, RemainingTurns=10.
/// AC bonus is read at combat resolution time in CombatResolver.GetEffectiveAC — the
/// base ArmorClass stat is never mutated by this effect.
/// </summary>
public sealed class ProtectionEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "protection";
    public int RemainingTurns { get; set; } = 10;
    public bool IsPermanent => false;

    /// <summary>AC bonus granted. PoC value: +3.</summary>
    public int AcBonus { get; set; } = 3;
}
