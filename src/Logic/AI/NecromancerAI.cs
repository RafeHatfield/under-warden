using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;

namespace UnderWarden.Logic.AI;

/// <summary>
/// Necromancer AI: raise fresh corpses, hang back from player, fall through to BasicMonsterAI.
///
/// Decision priority (matches PoC necromancer_ai.py):
///   1. Tick cooldown (always, before other checks)
///   2. Raise: cooldown == 0 AND raisable corpse within RaiseRange AND tile not blocked → raise
///   3. Seek corpse: raisable corpse exists but out of range → pathfind toward it
///      (respecting DangerRadius — skip if next step puts necromancer within DangerRadius of player)
///   4. Retreat: player within DangerRadius → flee
///   5. Preferred range: if outside PreferredDistanceMin–Max and no corpses → reposition
///   6. Fallback: BasicMonsterAI (attack if adjacent, approach if aware)
///
/// Both "necromancer" and "plague_necromancer" ai_types dispatch here.
/// Plague augmentation (plague_carrier tag + stat boost) is applied in TurnController
/// after the raise, not here.
/// </summary>
public static class NecromancerAI
{
    public static MonsterAction Decide(Entity necromancer, GameState state)
    {
        var aiComp = necromancer.Get<AiComponent>();
        var necComp = necromancer.Get<NecromancerAiComponent>();
        if (necComp == null) return BasicMonsterAI.Decide(necromancer, state);

        // 1. Tick cooldown
        if (necComp.CooldownRemaining > 0)
            necComp.CooldownRemaining--;

        BasicMonsterAI.UpdateAwareness(necromancer, state.Player, state);
        var alerted = necromancer.Get<AlertedState>();
        if (alerted == null) return MonsterAction.Wait();

        var target = state.Player;
        double distToPlayer = Math.Sqrt(
            Math.Pow(necromancer.X - target.X, 2) +
            Math.Pow(necromancer.Y - target.Y, 2));

        // 2. Raise corpse if ready and one is in range
        if (necComp.CooldownRemaining == 0)
        {
            var corpse = RaiseDeadResolver.FindNearestRaisableCorpse(
                necromancer, state.Corpses, state, necComp.RaiseRange);
            if (corpse != null)
                return MonsterAction.RaiseDead(corpse);
        }

        // 3. Seek corpse if one exists but is out of raise range
        var anyCorporse = state.Corpses.FirstOrDefault(
            c => c.Get<CorpseComponent>()?.CanBeRaised == true);

        if (anyCorporse != null)
        {
            // Pathfind toward the corpse, respecting danger radius
            var path = Pathfinder.AStar(state.Map,
                necromancer.X, necromancer.Y, anyCorporse.X, anyCorporse.Y, necromancer);
            if (path != null && path.Count > 0)
            {
                var next = path[0];
                double distNextToPlayer = Math.Sqrt(
                    Math.Pow(next.X - target.X, 2) + Math.Pow(next.Y - target.Y, 2));

                // Don't step within danger radius of player while seeking
                if (distNextToPlayer >= necComp.DangerRadius)
                    return MonsterAction.MoveTo(next.X, next.Y);
            }
        }

        // 4. Retreat if player is too close
        if (distToPlayer < necComp.DangerRadius)
        {
            var retreat = BasicMonsterAI.DecideFlee(necromancer, target, state);
            if (retreat.Kind != MonsterAction.ActionKind.Wait) return retreat;
        }

        // 5. Reposition to preferred distance (hang-back) when no corpses to seek
        if (anyCorporse == null)
        {
            if (distToPlayer < necComp.PreferredDistanceMin && distToPlayer >= necComp.DangerRadius)
            {
                var retreat = BasicMonsterAI.DecideFlee(necromancer, target, state);
                if (retreat.Kind != MonsterAction.ActionKind.Wait) return retreat;
            }
        }

        // 6. Fallback: BasicMonsterAI (attack if adjacent, approach otherwise)
        return BasicMonsterAI.Decide(necromancer, state);
    }
}
