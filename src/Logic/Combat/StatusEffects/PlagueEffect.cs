using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is infected with plague — takes damage each turn for the duration.
/// Applied by: Plague Scroll. Only affects corporeal (flesh-and-blood) creatures.
/// Corporeal check uses the "corporeal_flesh" tag on MonsterDefinition.
/// Duration default: 20 turns at 1 damage/turn.
/// </summary>
public sealed class PlagueEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "plague";
    public int RemainingTurns { get; set; } = 20;
    public bool IsPermanent => false;

    /// <summary>Damage applied per turn while this effect is active.</summary>
    public int DamagePerTurn { get; set; } = 1;
}
