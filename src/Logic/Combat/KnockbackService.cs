using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// Canonical knockback service for 1-tile ranged knockback procs.
///
/// Ranged knockback is simpler than melee knockback: exactly 1 tile, no stagger,
/// no power-delta scaling, no Oath of Chains bonus. Direction is signed vector
/// from attacker to target, each axis clamped to ±1.
///
/// Used exclusively by RangedCombatService (10% proc on successful ranged hit).
/// PoC reference: services/knockback_service.py apply_knockback_single_tile()
/// </summary>
public static class KnockbackService
{
    /// <summary>
    /// Attempt to knock the target back exactly 1 tile in the direction away from the attacker.
    ///
    /// Stopped at the first blocked tile (wall or entity). Returns 0 if immediately blocked.
    /// Does NOT apply stagger on block (ranged knockback is softer than melee knockback).
    /// Does NOT respect EntangledEffect on the target — knockback is a physical push, not
    /// a voluntary move, so entangle does not prevent it.
    ///
    /// Returns:
    ///   int TilesMoved — 0 or 1. Caller only emits RangedKnockbackEvent when > 0.
    /// </summary>
    public static int TryKnockBackOneTile(Entity attacker, Entity target, GameMap map, GameState state)
    {
        // Direction: signed vector from attacker to target, each axis clamped to ±1.
        int dx = Math.Sign(target.X - attacker.X);
        int dy = Math.Sign(target.Y - attacker.Y);

        // Edge case: same tile (d=0). Pick an arbitrary direction (positive X).
        // Unlikely in practice but handled deterministically.
        if (dx == 0 && dy == 0) dx = 1;

        int destX = target.X + dx;
        int destY = target.Y + dy;

        // Out-of-bounds or wall: blocked, 0 tiles moved.
        if (!map.InBounds(destX, destY)) return 0;
        if (!map.IsWalkable(destX, destY)) return 0;

        // Entity blocking the destination (any living monster or the player).
        bool occupied = state.Monsters.Any(m => m.Get<Fighter>()?.IsAlive == true && m.X == destX && m.Y == destY)
                     || (state.Player.X == destX && state.Player.Y == destY);
        if (occupied) return 0;

        // Execute the move — unregistered and re-registered to keep map entity index consistent.
        map.UnregisterEntity(target);
        target.X = destX;
        target.Y = destY;
        map.RegisterEntity(target);

        return 1;
    }
}
