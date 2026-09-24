using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;

namespace UnderWarden.Logic.AI;

/// <summary>
/// Orc Shaman AI: Chant of Dissonance + Crippling Hex + hang-back positioning.
///
/// Decision priority each turn:
///   1. Dead → Wait
///   2. Awareness update
///   3. Not alerted → Wait
///   4. Status effect overrides (FearEffect, DisorientationEffect, EntangledEffect)
///   5. If IsChanneling → continue/finish channel; return Wait
///   6. Tick cooldowns (HexCooldownRemaining, ChantCooldownRemaining)
///   7. Player within DangerRadius (≤2) → panic retreat (highest priority after status + channel)
///   8. Try Chant: silenced → skip; in range AND off cooldown → start channel, return Wait
///   9. Crippling Hex — fire when ready and player is in range
///  10. Too close (dist &lt; PreferredDistanceMin) → retreat one step
///  11. Too far (dist > PreferredDistanceMax) → advance one step via A*
///  12. Adjacent → Attack
///  13. Wait (in preferred range, both abilities on cooldown)
///
/// Chant priority over Hex: PoC uses Chant > Hex (orc_shaman_ai.py lines 233–313).
/// Channel turn: shaman waits while channeling — no attack, no move.
/// </summary>
public static class OrcShamanAI
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

        var shaman = monster.Get<OrcShamanComponent>();
        if (shaman == null) return BasicMonsterAI.Decide(monster, state);

        // 5. If channeling: continue or finish the channel.
        // Channel consumes the shaman's turn — no attack, no movement.
        // Interrupt on damage is handled externally in TurnController.OnAttackDamageTaken.
        if (shaman.IsChanneling)
        {
            shaman.ChantTurnsRemaining--;
            if (shaman.ChantTurnsRemaining <= 0)
            {
                // Channel ended naturally — apply cooldown and remove the effect from the player.
                shaman.IsChanneling = false;
                shaman.ChantCooldownRemaining = shaman.ChantCooldownTurns;

                // Remove DissonantChantEffect from the player if it came from this shaman.
                var chantEffect = player.Get<DissonantChantEffect>();
                if (chantEffect != null && chantEffect.ChantingShamanId == monster.Id)
                    player.Remove<DissonantChantEffect>();
                // Note: StatusExpiredEvent is not emitted here (no events list in AI.Decide).
                // TurnController handles event emission for interrupt; natural expiry silently removes.
            }
            // Channel consumes the turn regardless of whether it finished this turn.
            return MonsterAction.Wait();
        }

        // 6. Tick cooldowns.
        if (shaman.HexCooldownRemaining > 0)
            shaman.HexCooldownRemaining--;
        if (shaman.ChantCooldownRemaining > 0)
            shaman.ChantCooldownRemaining--;

        int dist = monster.ChebyshevDistanceTo(player.X, player.Y);

        // 7. Panic retreat — player too close, flee immediately (highest priority after status + channel).
        if (dist <= shaman.DangerRadius)
            return BasicMonsterAI.DecideFlee(monster, player, state);

        // 8. Try Chant of Dissonance (priority: Chant > Hex, per PoC).
        // Silenced shaman cannot cast — skip attempt. Not silenced: check range + cooldown.
        if (!monster.Has<SilencedEffect>()
            && shaman.ChantCooldownRemaining == 0
            && dist <= shaman.ChantRange)
        {
            // Apply DissonantChantEffect to the player with this shaman's ID as the owner.
            // RemainingTurns=999 sentinel — shaman manages lifecycle explicitly.
            var effect = StatusEffectProcessor.ApplyEffect<DissonantChantEffect>(player, 999);
            if (effect != null)
            {
                effect.ChantingShamanId = monster.Id;
                shaman.IsChanneling = true;
                shaman.ChantTurnsRemaining = shaman.ChantDuration;
                shaman.ChantTargetEntityId = player.Id;
            }
            // Channel start consumes the turn.
            return MonsterAction.Wait();
        }

        // 9. Crippling Hex — fire when ready and player is in range.
        if (shaman.HexCooldownRemaining == 0 && dist <= shaman.HexRange)
        {
            StatusEffectProcessor.ApplyEffect<CrippledEffect>(player, shaman.HexDuration);
            shaman.HexCooldownRemaining = shaman.HexCooldownTurns;
            // Fall through to positioning — the shaman doesn't waste its turn just standing still.
        }

        // 10. Reposition: too close → retreat one step (maximize distance from player).
        if (dist < shaman.PreferredDistanceMin)
        {
            var retreatAction = BasicMonsterAI.DecideFlee(monster, player, state);
            if (retreatAction.Kind != MonsterAction.ActionKind.Wait) return retreatAction;
        }

        // 11. Reposition: too far → advance one step toward player.
        if (dist > shaman.PreferredDistanceMax)
        {
            var path = Pathfinder.AStar(state.Map, monster.X, monster.Y, player.X, player.Y, movingEntity: monster);
            if (path != null && path.Count > 0)
                return MonsterAction.MoveTo(path[0].X, path[0].Y);
        }

        // 12. Adjacent → attack (melee fallback when player is inside preferred range).
        if (adjacent && !target.Has<InvisibilityEffect>())
            return MonsterAction.Attack(target);

        // 13. Wait (in preferred range, both abilities on cooldown).
        return MonsterAction.Wait();
    }
}
