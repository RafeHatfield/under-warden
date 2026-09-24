using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Player is affected by the Orc Shaman's Chant of Dissonance — movement costs an extra
/// action (player skips every other turn while this is active).
///
/// Lifetime: applied by the shaman when it starts channeling; removed explicitly by the
/// shaman when the channel ends naturally (3 turns) or is interrupted (shaman takes damage).
/// RemainingTurns is set to a large sentinel (999) on application — the shaman AI manages
/// the lifecycle, not the duration tick.
///
/// Death cleanup: if the shaman dies while channeling, TurnController removes this effect
/// from the player in the same turn as the DeathEvent.
///
/// Movement penalty: shares the unified alternating-skip slot with SlowedEffect and
/// EngulfedEffect. The player skips every other turn (turnCount % 2 == 1).
/// Only one skip fires regardless of how many skip-class effects are active.
///
/// PoC reference: orc_shaman_ai.py lines 233–313; chant_move_energy_tax = 1.
/// </summary>
public sealed class DissonantChantEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "dissonant_chant";

    /// <summary>
    /// Large sentinel — effect is not removed by duration tick.
    /// The shaman AI calls entity.Remove&lt;DissonantChantEffect&gt;() explicitly when the
    /// channel ends (natural or interrupted) or when the shaman dies.
    /// </summary>
    public int RemainingTurns { get; set; } = 999;
    public bool IsPermanent => false;

    /// <summary>Extra movement energy cost. PoC value: 1 (moves cost 2 AP instead of 1).</summary>
    public int MoveEnergyTax { get; set; } = 1;

    /// <summary>
    /// Entity ID of the shaman that started this chant.
    /// Used for targeted cleanup when the shaman dies mid-channel.
    /// </summary>
    public int ChantingShamanId { get; set; }
}
