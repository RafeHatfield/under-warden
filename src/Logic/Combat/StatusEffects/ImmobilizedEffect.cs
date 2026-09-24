using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity cannot move but can still attack adjacent targets.
/// Applied by: Glue Scroll / Wand of Glue.
/// Duration default: 5 turns.
/// Tick behavior: StatusEffectProcessor.ProcessTurnStart returns skipTurn=true, blocking all actions.
/// </summary>
public sealed class ImmobilizedEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "immobilized";
    public int RemainingTurns { get; set; } = 5;
    public bool IsPermanent => false;
}
