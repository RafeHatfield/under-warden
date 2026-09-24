using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity has a magical shield — +4 AC for the duration.
/// Applied by: Shield Scroll (self).
/// Duration default: 10 turns (PoC: shield.duration = 10).
/// AC bonus is read at combat resolution time in CombatResolver.GetEffectiveAC — the
/// base ArmorClass stat is never mutated by this effect.
/// </summary>
public sealed class ShieldEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "shield";
    public int RemainingTurns { get; set; } = 10;
    public bool IsPermanent => false;

    /// <summary>AC bonus granted by this shield effect. Default +4 (PoC value).</summary>
    public int AcBonus { get; set; } = 4;
}
