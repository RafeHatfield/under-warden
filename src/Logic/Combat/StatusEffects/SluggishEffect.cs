using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is sluggish — speed bonus ratio reduced, reducing bonus attack chances.
/// Applied by: Monster abilities.
/// PoC values: SpeedPenaltyRatio=0.5, RemainingTurns=10.
///
/// This is distinct from SlowedEffect (which skips turns entirely).
/// SluggishEffect reduces the bonus attack ratio from fast equipment.
///
/// TODO: Apply SpeedPenaltyRatio modifier to SpeedBonusTracker when bonus attack system
/// is fully wired. Currently the effect ticks and expires correctly but has no
/// combat outcome.
/// </summary>
public sealed class SluggishEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "sluggish";
    public int RemainingTurns { get; set; } = 10;
    public bool IsPermanent => false;

    /// <summary>Penalty ratio subtracted from SpeedBonusTracker. PoC value: 0.5f.</summary>
    public float SpeedPenaltyRatio { get; set; } = 0.5f;
}
