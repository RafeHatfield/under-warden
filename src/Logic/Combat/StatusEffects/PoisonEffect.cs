using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is poisoned — takes 2 damage per turn for 10 turns.
/// Applied by: Plague Zombie hit, Dragon Fart.
/// PoC values: DamagePerTurn=2, RemainingTurns=10.
/// DOT does NOT wake a sleeping entity (SleepEffect.WakesOnAttackDamage applies to
/// attack hits only, not DOT — PoC-verified).
/// </summary>
public sealed class PoisonEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "poison";
    public int RemainingTurns { get; set; } = 10;
    public bool IsPermanent => false;

    /// <summary>Damage applied per turn. PoC value: 2.</summary>
    public int DamagePerTurn { get; set; } = 2;
}
