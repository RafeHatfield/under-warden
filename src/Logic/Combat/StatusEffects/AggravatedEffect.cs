using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is aggravated against a specific faction — will attack all members of that faction
/// regardless of normal allegiance rules.
/// Applied by: Aggravation Scroll.
///
/// IsPermanent=true — this effect never expires via duration; only cleared on entity death
/// or explicit dispel. NOTE: the faction system IS implemented (FactionRegistry +
/// BasicMonsterAI.ChooseTarget) — what is missing is the read hook: ChooseTarget does not yet
/// consult TargetFaction. Restoring the PoC's _check_enraged_against_faction override branch
/// (~25–40 lines) wires this up. See docs/balance/migration_loss_audit.md.
/// </summary>
public sealed class AggravatedEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "aggravated";

    /// <summary>
    /// IsPermanent=true — RemainingTurns is never decremented.
    /// Set to int.MaxValue as a sentinel that won't cause issues if IsPermanent check is bypassed.
    /// </summary>
    public int RemainingTurns { get; set; } = int.MaxValue;
    public bool IsPermanent => true;

    /// <summary>
    /// The faction this entity is now hostile toward.
    /// e.g. "orc", "undead", "humanoid"
    /// </summary>
    public string TargetFaction { get; set; } = "";
}
