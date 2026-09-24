using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.AI;

/// <summary>
/// Skeleton AI: Shield Wall formation + standard melee.
///
/// Each turn, before deciding movement or attack:
///   1. Count adjacent (4-way: N/S/E/W) skeleton allies in state.AliveMonsters.
///   2. Write ShieldWallComponent.CurrentAcBonus = count * AcBonusPerAlly.
///   3. Fall through to BasicMonsterAI for movement and attack decisions.
///
/// The cached CurrentAcBonus is read by CombatResolver.ResolveAttack() without
/// needing to pass GameState into the resolver — SkeletonAI owns the write,
/// CombatResolver owns the read.
/// </summary>
public static class SkeletonAI
{
    private static readonly (int dx, int dy)[] Cardinals = [(0, -1), (0, 1), (-1, 0), (1, 0)];

    public static MonsterAction Decide(Entity skeleton, GameState state)
    {
        UpdateShieldWall(skeleton, state);
        return BasicMonsterAI.Decide(skeleton, state);
    }

    /// <summary>
    /// Scan 4-way adjacent tiles for skeleton allies and update the cached AC bonus.
    /// Only counts living (IsAlive) monsters with a ShieldWallComponent on the adjacent tile.
    /// </summary>
    internal static void UpdateShieldWall(Entity skeleton, GameState state)
    {
        var sw = skeleton.Get<ShieldWallComponent>();
        if (sw == null) return;

        int adjacentSkeletons = 0;
        foreach (var (dx, dy) in Cardinals)
        {
            int nx = skeleton.X + dx;
            int ny = skeleton.Y + dy;

            foreach (var other in state.AliveMonsters)
            {
                if (other.Id == skeleton.Id) continue;
                if (other.X == nx && other.Y == ny && other.Has<ShieldWallComponent>())
                {
                    adjacentSkeletons++;
                    break; // at most one entity per tile
                }
            }
        }

        sw.CurrentAcBonus = adjacentSkeletons * sw.AcBonusPerAlly;
    }
}
