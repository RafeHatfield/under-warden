using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity regenerates HP each turn.
/// Applied by: Regeneration ring (passive while equipped; this component tracks the active tick).
/// PoC values: HealPerTurn=2, RemainingTurns=20.
/// HOT tick handled in StatusEffectProcessor.ProcessTurnStart — emits HotHealEvent.
/// </summary>
public sealed class RegenerationEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "regeneration";
    public int RemainingTurns { get; set; } = 20;
    public bool IsPermanent => false;

    /// <summary>HP restored per turn. PoC value: 2. Capped at MaxHp.</summary>
    public int HealPerTurn { get; set; } = 2;
}
