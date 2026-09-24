using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is slowed — takes an action only every other turn.
/// Applied by: Slow Scroll / Wand of Slow.
/// Duration default: 10 turns.
/// Tick behavior: StatusEffectProcessor.ProcessTurnStart skips the entity's action
/// on odd-numbered turns (TurnCount % 2 == 1).
/// </summary>
public sealed class SlowedEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "slowed";
    public int RemainingTurns { get; set; } = 10;
    public bool IsPermanent => false;
}
