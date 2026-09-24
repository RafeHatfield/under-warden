using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Player is engulfed by a slime — movement is impeded.
/// Applied on any successful hit by a monster with EngulfsOnHitTag.
/// Duration: 3 turns. Refreshes to 3 while adjacent to any slime (EngulfsOnHitTag monster).
///
/// Movement penalty: shares the unified alternating-skip slot with SlowedEffect and
/// DissonantChantEffect. The player skips every other turn (turnCount % 2 == 1).
/// Only one skip fires regardless of how many skip-class effects are active — see
/// StatusEffectProcessor.ProcessTurnStart unified gate.
///
/// PoC reference: tests/test_engulf_mechanics.py; entities.yaml slime entries.
/// PoC values: duration=3, refresh on adjacency, no RNG (always applies on hit).
/// </summary>
public sealed class EngulfedEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "engulfed";
    public int RemainingTurns { get; set; } = 3;
    public bool IsPermanent => false;
}
