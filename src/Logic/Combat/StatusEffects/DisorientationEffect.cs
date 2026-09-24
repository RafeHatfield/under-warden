using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is disoriented — moves in random directions, loses directional control.
/// Applied by: Teleport scroll misfire (10% chance per PoC teleport definition).
/// This is the canonical confused-movement effect for both players and monsters.
/// The separate ConfusedEffect stub has been removed — DisorientationEffect covers both sources.
///
/// Duration: 3 turns by default.
/// Behavior: movement direction replaced with a random direction each turn.
/// Movement ONLY — attacks are not randomized (PoC-verified).
/// Tick behavior (random movement override) implemented in Phase 2 movement effects.
/// </summary>
public sealed class DisorientationEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "disoriented";
    public int RemainingTurns { get; set; } = 3;
    public bool IsPermanent => false;
}
