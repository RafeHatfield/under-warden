using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is taunted and fixates on a specific target — will only attack TauntTargetId.
/// Applied by: Yo Mama Scroll / Wand of Yo Mama.
///
/// PoC uses duration=1000, which is effectively permanent through any combat encounter.
/// IsPermanent=false so the standard tick/expire cycle runs, but with 1000 turns it
/// will not expire during normal play. Tests should verify it persists through typical combat.
///
/// Design note: Used to redirect enemies — player can make an orc aggro another orc,
/// creating chaos in monster groups.
/// </summary>
public sealed class TauntedEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "taunted";

    /// <summary>
    /// 1000 turns = effectively permanent through any combat encounter (PoC behavior).
    /// IsPermanent=false so the turn engine still decrements this — it just takes a very
    /// long time to expire naturally.
    /// </summary>
    public int RemainingTurns { get; set; } = 1000;
    public bool IsPermanent => false;

    /// <summary>
    /// Entity ID of the taunt target.
    /// The taunted entity will fixate on this entity until it dies or the effect expires.
    /// -1 means unset / no target (should not occur after application).
    /// </summary>
    public int TauntTargetId { get; set; } = -1;
}
