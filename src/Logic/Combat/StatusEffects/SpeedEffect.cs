using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity moves faster — bonus speed ratio applied to action economy.
/// Applied by: Potion of Speed.
/// PoC values: SpeedBonusRatio=0.5, RemainingTurns=20.
///
/// The PoC implements speed as a bonus ratio affecting additional attack chances
/// from fast equipment (SpeedBonusTracker). SpeedEffect increases this ratio.
/// This is distinct from SlowedEffect (which skips turns).
///
/// TODO: Apply SpeedBonusRatio modifier to SpeedBonusTracker when bonus attack system
/// is fully wired. Currently the effect ticks and expires correctly but has no
/// combat outcome.
/// </summary>
public sealed class SpeedEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "speed";
    public int RemainingTurns { get; set; } = 20;
    public bool IsPermanent => false;

    /// <summary>Bonus ratio added to SpeedBonusTracker. PoC value: 0.5f.</summary>
    public float SpeedBonusRatio { get; set; } = 0.5f;
}
