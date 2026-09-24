using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is blinded — suffers an accuracy penalty on all attacks.
/// Applied by: Sunburst Potion.
/// PoC values: AccuracyPenalty=4, RemainingTurns=10.
/// Accuracy penalty applied in Phase 3 combat effects (hit formula).
/// </summary>
public sealed class BlindedEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "blinded";
    public int RemainingTurns { get; set; } = 10;
    public bool IsPermanent => false;

    /// <summary>Subtracted from the attacker's to-hit roll. PoC value: 4.</summary>
    public int AccuracyPenalty { get; set; } = 4;
}
