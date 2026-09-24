using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Converts a BotAction to a PlayerAction suitable for TurnController consumption.
///
/// The simple BotBrain.ToPlayerAction() uses GameMap.MoveToward for MoveToward and MoveTo
/// actions — a greedy directional step that gets stuck at walls in dungeon layouts.
/// This class provides ToPlayerActionWithPathing() which uses A* for BOTH target types so
/// the bot can navigate across rooms and corridors.
///
/// Previously MoveTo (used for loot, retreat, choke) fell through to the greedy path, which
/// meant the bot almost never reached floor items because any wall between it and the item
/// blocked the direct-line step. Now all navigation goes through A*.
///
/// Used by both DungeonRunHarness (headless) and BotPlayerDriver (graphical).
/// </summary>
public static class BotActionConverter
{
    /// <summary>
    /// Convert a BotAction to a PlayerAction, using A* pathing for all movement actions.
    ///
    /// MoveToward (entity target) and MoveTo (coordinate target — loot, retreat, choke):
    ///   both compute a full A* path and return the single first step. Falls back to Wait
    ///   if the destination is unreachable.
    ///
    /// All other action types: delegate to BotBrain.ToPlayerAction().
    /// </summary>
    public static PlayerAction ToPlayerActionWithPathing(BotAction botAction, GameState state)
    {
        if (botAction.Type == BotAction.ActionType.MoveToward && botAction.Target != null)
            return AStarStep(state, botAction.Target.X, botAction.Target.Y);

        // MoveTo is used for coordinate targets: loot items, retreat to choke, avoid-combat.
        // These previously used greedy MoveToward and couldn't navigate around walls.
        if (botAction.Type == BotAction.ActionType.MoveTo)
            return AStarStep(state, botAction.TargetX, botAction.TargetY);

        return BotBrain.ToPlayerAction(botAction);
    }

    /// <summary>Compute one A*-pathed step from the player toward (tx,ty), routing around traps.</summary>
    private static PlayerAction AStarStep(GameState state, int tx, int ty)
    {
        var trapTiles = Pathfinder.DetectedTrapTiles(state.Features);
        var path = Pathfinder.AStar(
            state.Map,
            state.Player.X, state.Player.Y,
            tx, ty,
            state.Player,
            canPassDoors: true,
            avoidTiles: trapTiles.Count > 0 ? trapTiles : null);

        // Fallback: if trap-avoiding path fails, try without avoidance
        path ??= Pathfinder.AStar(
            state.Map,
            state.Player.X, state.Player.Y,
            tx, ty,
            state.Player,
            canPassDoors: true);

        if (path != null && path.Count > 0)
        {
            var (nx, ny) = path[0];
            return PlayerAction.MoveTo(nx, ny);
        }
        return PlayerAction.Wait; // unreachable
    }
}
