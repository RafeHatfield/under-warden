using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;

namespace UnderWarden.Logic.AI;

/// <summary>
/// Orc Chieftain AI: Rally Cry + Sonic Bellow + hang-back positioning.
///
/// Decision priority each turn:
///   1. Dead → Wait
///   2. Awareness update
///   3. Not alerted → Wait
///   4. Status effect overrides (FearEffect, DisorientationEffect, EntangledEffect)
///   5. Player within DangerRadius (≤2) → panic retreat
///   6. Rally Cry check (once only): 2+ orc allies in RallyRange → apply RallyEffect to all orc faction allies
///   7. Sonic Bellow check (once only): HP &lt; 50% → apply CrippledEffect to player
///   8. Too close (dist &lt; PreferredDistanceMin) → retreat one step
///   9. Too far (dist > PreferredDistanceMax) → advance one step via A*
///  10. Adjacent → Attack
///  11. Wait (in preferred range)
///
/// Fast Pressure (bonus attacks) is handled automatically by SpeedBonusTracker (speed_bonus: 0.25 in YAML).
/// </summary>
public static class OrcChieftainAI
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

        var chieftain = monster.Get<OrcChieftainComponent>();
        if (chieftain == null) return BasicMonsterAI.Decide(monster, state);

        int dist = monster.ChebyshevDistanceTo(player.X, player.Y);

        // 5. Panic retreat.
        if (dist <= chieftain.DangerRadius)
            return BasicMonsterAI.DecideFlee(monster, player, state);

        // 6. Rally Cry — fires once when 2+ orc allies are within RallyRange.
        if (!chieftain.RallyCried)
        {
            var nearbyOrcAllies = state.AliveMonsters
                .Where(m => m.Id != monster.Id
                    && m.Get<AiComponent>()?.Faction == "orc"
                    && monster.ChebyshevDistanceTo(m.X, m.Y) <= chieftain.RallyRange)
                .ToList();

            if (nearbyOrcAllies.Count >= chieftain.RallyMinAllies)
            {
                chieftain.RallyCried = true;
                // Apply RallyEffect to the chieftain itself and all orc allies in range.
                // Set ChieftainId so the effect can be removed when the chieftain takes damage.
                var chieftainRally = StatusEffectProcessor.ApplyEffect<RallyEffect>(monster, 1000);
                if (chieftainRally != null) chieftainRally.ChieftainId = monster.Id;

                foreach (var ally in nearbyOrcAllies)
                {
                    // Cleanse FearEffect from allies at rally time (PoC: rally_cleanse).
                    if (ally.Has<FearEffect>())
                    {
                        ally.Remove<FearEffect>();
                        // We'd emit StatusExpiredEvent here but we don't have the events list.
                        // TurnController's OnAttackDamageTaken handles the rally-broken events.
                        // For fear cleanse at rally time: it silently removes (PoC doesn't emit an event here).
                    }
                    var allyRally = StatusEffectProcessor.ApplyEffect<RallyEffect>(ally, 1000);
                    if (allyRally != null) allyRally.ChieftainId = monster.Id;
                }
                // Fall through to positioning — rally cry is instant and doesn't consume the turn.
            }
        }

        // 7. Sonic Bellow — fires once when HP drops below threshold.
        if (!chieftain.BellowedAtLowHp && fighter != null)
        {
            double hpFraction = fighter.MaxHp > 0 ? (double)fighter.Hp / fighter.MaxHp : 1.0;
            if (hpFraction < chieftain.BellowHpThreshold)
            {
                chieftain.BellowedAtLowHp = true;
                StatusEffectProcessor.ApplyEffect<CrippledEffect>(player, chieftain.BellowDebuffDuration);
                // Fall through to positioning.
            }
        }

        // 8. Reposition: too close → retreat one step.
        if (dist < chieftain.PreferredDistanceMin)
        {
            var retreatAction = BasicMonsterAI.DecideFlee(monster, player, state);
            if (retreatAction.Kind != MonsterAction.ActionKind.Wait) return retreatAction;
        }

        // 9. Reposition: too far → advance one step.
        if (dist > chieftain.PreferredDistanceMax)
        {
            var path = Pathfinder.AStar(state.Map, monster.X, monster.Y, player.X, player.Y, movingEntity: monster);
            if (path != null && path.Count > 0)
                return MonsterAction.MoveTo(path[0].X, path[0].Y);
        }

        // 10. Adjacent → attack (melee fallback).
        if (adjacent && !target.Has<InvisibilityEffect>())
            return MonsterAction.Attack(target);

        // 11. Wait.
        return MonsterAction.Wait();
    }
}
