using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.AI;

/// <summary>
/// Lich AI: Soul Bolt (2-turn telegraph) + necromancer corpse economy.
///
/// Priority (from PoC lich_ai.py):
///   1. If charging (has ChargingSoulBoltEffect): resolve if target still in LOS+range, else fizzle
///   2. If Soul Bolt off cooldown: start charge if player in LOS+range
///   3. Fallback to NecromancerAI (raise dead, seek corpse, retreat, approach)
/// </summary>
public static class LichAI
{
    public static MonsterAction Decide(Entity lich, GameState state)
    {
        var lichComp = lich.Get<LichAiComponent>();
        if (lichComp == null) return NecromancerAI.Decide(lich, state);

        // Tick Soul Bolt cooldown
        if (lichComp.SoulBoltCooldownRemaining > 0)
            lichComp.SoulBoltCooldownRemaining--;

        // Awareness check — lich must know the player is there
        BasicMonsterAI.UpdateAwareness(lich, state.Player, state);
        var alerted = lich.Get<AlertedState>();
        if (alerted == null) return MonsterAction.Wait();

        var target = state.Player;
        double dx = lich.X - target.X;
        double dy = lich.Y - target.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        bool hasLos = state.Map.IsVisible(lich.X, lich.Y);

        bool isCharging = lich.Has<ChargingSoulBoltEffect>();

        // Priority 1: Resolve charging Soul Bolt
        if (isCharging)
        {
            lich.Remove<ChargingSoulBoltEffect>();
            if (hasLos && dist <= lichComp.SoulBoltRange)
            {
                lichComp.SoulBoltCooldownRemaining = lichComp.SoulBoltCooldownTurns;
                return MonsterAction.SoulBolt(target);
            }
            // else: fizzle — charge wasted, fall through to other actions
        }

        // Priority 2: Start Soul Bolt charge
        if (lichComp.SoulBoltCooldownRemaining == 0 && !isCharging
            && hasLos && dist <= lichComp.SoulBoltRange)
        {
            lich.Add(new ChargingSoulBoltEffect());
            return MonsterAction.Channel("Soul Bolt");
        }

        // Priority 3: Fall through to necromancer behavior
        return NecromancerAI.Decide(lich, state);
    }
}
