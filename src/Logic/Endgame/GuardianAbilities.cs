using System.Collections.Generic;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Endgame;

/// <summary>
/// Guardian combat abilities for the Weighing. Orchestration (TASK-009) calls these; the mechanics
/// live here so they're testable in isolation.
/// </summary>
public static class GuardianAbilities
{
    /// <summary>
    /// The Warden-of-Wardens savage form turns one of Sasha's allies against him (decision 3).
    ///
    /// Mechanically this is <see cref="EnragedEffect"/> (HostileToAll), NOT the possession primitive —
    /// that primitive never drove NPCs. An enraged ally targets the nearest entity regardless of
    /// allegiance (BasicMonsterAI's enrage branch), so it turns on Sasha and the other allies.
    ///
    /// Reversible by design: Hollowmark's spell-break (the Dispel spell) removes the EnragedEffect
    /// like any status effect, and the ally reverts to its player_ally targeting — fighting for Sasha
    /// again. No bespoke restore path needed.
    ///
    /// Duration defaults long (the turn persists until dispelled); orchestration/balance may tune it.
    /// </summary>
    public static void TurnAllyHostile(Entity ally, List<TurnEvent> events, int duration = 999)
    {
        if (ally.Has<EnragedEffect>()) return;
        ally.GetOrAdd<EnragedEffect>().RemainingTurns = duration;
        events.Add(new StatusAppliedEvent
        {
            ActorId = ally.Id,
            TargetId = ally.Id,
            EffectName = "enraged",
            Duration = duration,
        });
    }
}
