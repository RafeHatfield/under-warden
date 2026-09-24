using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is weakened — deals less damage on attacks.
/// Applied by: Monster abilities.
/// PoC values: DamagePenalty=2, RemainingTurns=8.
/// Damage penalty applied in Phase 3 combat effects (minimum 1 damage preserved).
/// </summary>
public sealed class WeaknessEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "weakness";
    public int RemainingTurns { get; set; } = 8;
    public bool IsPermanent => false;

    /// <summary>Subtracted from damage on each hit. PoC value: 2. Final damage minimum: 1.</summary>
    public int DamagePenalty { get; set; } = 2;
}
