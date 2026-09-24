using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;

namespace UnderWarden.Logic.AI;

/// <summary>
/// Orc Skirmisher AI: Pouncing Leap + standard melee fallback.
///
/// Decision priority each turn:
///   1. Dead → Wait
///   2. Awareness update
///   3. Not alerted → Wait
///   4. Status effect overrides (FearEffect, DisorientationEffect, EntangledEffect)
///   5. Decrement leap cooldown
///   6. In leap range (3-6 tiles) AND cooldown == 0 → Pouncing Leap (2-tile lunge toward player)
///   7. Adjacent → Attack
///   8. Pursue via A* / greedy fallback
///   9. Wait
///
/// Fast Pressure (occasional bonus attacks) is handled automatically by SpeedBonusTracker
/// (speed_bonus: 0.20 in YAML) — no special AI logic required.
/// </summary>
public static class SkirmisherAI
{
    public static MonsterAction Decide(Entity monster, GameState state)
    {
        // 1. Dead guard.
        var fighter = monster.Get<Fighter>();
        if (fighter != null && !fighter.IsAlive)
            return MonsterAction.Wait();

        var player = state.Player;

        // 2. Awareness update.
        BasicMonsterAI.UpdateAwareness(monster, player, state);

        // 3. Not alerted → idle.
        var alerted = monster.Get<AlertedState>();
        if (alerted == null) return MonsterAction.Wait();

        // 4. Status effect AI overrides.
        if (monster.Has<FearEffect>())
            return BasicMonsterAI.DecideFlee(monster, player, state);

        var target = BasicMonsterAI.ChooseTarget(monster, player, state);
        bool adjacent = monster.ChebyshevDistanceTo(target.X, target.Y) <= 1;

        if (monster.Has<DisorientationEffect>())
        {
            if (adjacent && !target.Has<InvisibilityEffect>()) return MonsterAction.Attack(target);
            return BasicMonsterAI.DecideRandomMove(monster, state);
        }

        if (monster.Has<EntangledEffect>())
        {
            if (adjacent && !target.Has<InvisibilityEffect>()) return MonsterAction.Attack(target);
            return MonsterAction.Wait();
        }

        // 5. Tick leap cooldown.
        var skirmisher = monster.Get<SkirmisherComponent>();
        if (skirmisher != null && skirmisher.LeapCooldownRemaining > 0)
            skirmisher.LeapCooldownRemaining--;

        // 6. Pouncing Leap: fires when in range 3-6 and cooldown == 0.
        if (skirmisher != null && skirmisher.LeapCooldownRemaining == 0)
        {
            int dist = monster.ChebyshevDistanceTo(target.X, target.Y);
            if (dist >= skirmisher.LeapRangeMin && dist <= skirmisher.LeapRangeMax)
            {
                // Move 2 tiles toward the target. Check step-2 first, fall back to step-1.
                int dx = Math.Sign(target.X - monster.X);
                int dy = Math.Sign(target.Y - monster.Y);

                int step2X = monster.X + 2 * dx;
                int step2Y = monster.Y + 2 * dy;
                int step1X = monster.X + dx;
                int step1Y = monster.Y + dy;

                skirmisher.LeapCooldownRemaining = skirmisher.LeapCooldownTurns;

                if (state.Map.CanMoveTo(step2X, step2Y))
                    return MonsterAction.MoveTo(step2X, step2Y);
                if (state.Map.CanMoveTo(step1X, step1Y))
                    return MonsterAction.MoveTo(step1X, step1Y);
                // Both blocked (cornered leap) — skip the leap, reset the cooldown so we try again next turn.
                skirmisher.LeapCooldownRemaining = 0;
            }
        }

        // 7. Adjacent → attack.
        if (adjacent && !target.Has<InvisibilityEffect>())
            return MonsterAction.Attack(target);

        // 8. Pursue via A*.
        int pursueX = monster.Has<EnragedEffect>() ? target.X : alerted.LastKnownPlayerX;
        int pursueY = monster.Has<EnragedEffect>() ? target.Y : alerted.LastKnownPlayerY;

        var path = Pathfinder.AStar(state.Map, monster.X, monster.Y, pursueX, pursueY, movingEntity: monster);
        if (path != null && path.Count > 0)
            return MonsterAction.MoveTo(path[0].X, path[0].Y);

        // Greedy fallback.
        if (monster.X != pursueX || monster.Y != pursueY)
        {
            int dx = Math.Sign(pursueX - monster.X);
            int dy = Math.Sign(pursueY - monster.Y);
            if (dx != 0 && dy != 0 && state.Map.CanMoveTo(monster.X + dx, monster.Y + dy))
                return MonsterAction.MoveTo(monster.X + dx, monster.Y + dy);
            if (dx != 0 && state.Map.CanMoveTo(monster.X + dx, monster.Y))
                return MonsterAction.MoveTo(monster.X + dx, monster.Y);
            if (dy != 0 && state.Map.CanMoveTo(monster.X, monster.Y + dy))
                return MonsterAction.MoveTo(monster.X, monster.Y + dy);
        }

        return MonsterAction.Wait();
    }
}
