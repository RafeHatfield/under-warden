using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is focused — gains an accuracy bonus on attacks.
/// Applied by: Sunburst Potion secondary effect.
/// PoC values: AccuracyBonus=3, RemainingTurns=8.
/// Accuracy bonus applied in Phase 3 combat effects (hit formula).
/// </summary>
public sealed class FocusedEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "focused";
    public int RemainingTurns { get; set; } = 8;
    public bool IsPermanent => false;

    /// <summary>Added to the attacker's to-hit roll. PoC value: +3.</summary>
    public int AccuracyBonus { get; set; } = 3;
}
