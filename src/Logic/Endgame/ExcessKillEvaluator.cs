using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Endgame;

/// <summary>
/// Decides whether a monster kill counts as dealt by Sasha for the excess metric (TASK-003).
///
/// The excess metric measures cruelty by the victim-provoked predicate, and it must not be evadable
/// by killing through a proxy. So a kill dealt while possessing a host DOES count — but only if
/// SASHA is the one possessing (PlayerInitiated, possessor == player). Kills by hosts possessed by
/// anything else (WardenInitiated, NPC-on-NPC) or by enraged former allies are not Sasha's choice.
///
/// The victim-provoked test (HasAttackedPlayerTag) is applied separately and is unchanged — this
/// helper only answers "was the killing hand Sasha's?".
/// </summary>
public static class ExcessKillEvaluator
{
    /// <summary>
    /// True if the kill was dealt by Sasha: his own body (<paramref name="killerId"/> == player),
    /// or a host he is actively possessing. <paramref name="killer"/> is the killer entity if it is
    /// a monster (null when the killer is the player or is not resolvable).
    /// </summary>
    public static bool DealtByPlayer(Entity? killer, int killerId, int playerId)
    {
        if (killerId == playerId) return true;
        if (killer == null) return false;
        var poss = killer.Get<PossessionEffect>();
        return poss is { Source: PossessionSource.PlayerInitiated } && poss.PossessorEntityId == playerId;
    }
}
