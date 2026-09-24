using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Core;

/// <summary>
/// Handles portal pair placement, entity teleportation, and portal cleanup.
///
/// 3-step casting cycle (Wand of Portals):
///   Step 1 (Ready → EntrancePlaced):
///     Entrance placed at caster's feet. Registered on map, NOT yet in state.Portals.
///     state.Portals invariant (always 0 or 2) is preserved throughout.
///   Step 2 (EntrancePlaced → BothPlaced):
///     Exit placed at target tile. Both portals linked and added to state.Portals atomically.
///     Any monsters on either portal tile are immediately displaced through the portal.
///   Step 3 (BothPlaced → Ready):
///     Both portals removed. Wand recharged (conceptually — wand is Infinite, step resets).
///
/// Additional rules:
///   - Only one portal pair active at a time. Step 1 recycles any existing pair first.
///   - Portals are bidirectional: entrance ↔ exit.
///   - No teleport chaining: PortalComponent.UsedThisTurn prevents re-triggering on arrival.
///     Cleared at end-of-turn via ClearPortalUsedFlags.
///   - Portals do not persist between floors. ClearPortals + ResetPortalWandState on transition.
///   - Both player and monsters can traverse portals.
///   - Placement validates: tile must be walkable, not a stair, not the same as the other portal.
/// </summary>
public static class PortalSystem
{
    // ─── 3-Step State Machine ─────────────────────────────────────────────────

    /// <summary>
    /// Drive the 3-step portal casting cycle. Called by TurnController when the player uses
    /// the Wand of Portals. The current step is read from the wand entity's
    /// PortalCastStateComponent (created on first use if absent).
    ///
    /// Returns emitted events, or null if the cast was invalid (bad target tile, etc.).
    /// </summary>
    public static List<TurnEvent>? HandlePortalCast(
        Entity caster, GameState state, Entity wand,
        int? targetX, int? targetY,
        EntityFactory entityFactory)
    {
        var stateComp = wand.GetOrAdd<PortalCastStateComponent>();
        var events = new List<TurnEvent>();

        switch (stateComp.Step)
        {
            case PortalCastStep.Ready:
                return PlaceEntrance(caster, state, wand, stateComp, entityFactory, targetX, targetY);

            case PortalCastStep.EntrancePlaced:
                return PlaceExit(caster, state, wand, stateComp, entityFactory,
                    targetX, targetY, events);

            case PortalCastStep.BothPlaced:
                return ResetPortals(state, stateComp, events);

            default:
                return null;
        }
    }

    /// <summary>Returns the current portal cast step for the given wand entity.</summary>
    public static PortalCastStep GetPortalCastStep(Entity wand) =>
        wand.Get<PortalCastStateComponent>()?.Step ?? PortalCastStep.Ready;

    /// <summary>
    /// Cancel a pending entrance (Step = EntrancePlaced): remove entrance from map,
    /// reset wand to Ready. Returns a PortalEntranceCancelledEvent if cleanup happened.
    /// Called directly from GameController when the player cancels targeting — no turn consumed.
    /// </summary>
    public static PortalEntranceCancelledEvent? CancelPendingEntrance(Entity wand, GameState state)
    {
        var comp = wand.Get<PortalCastStateComponent>();
        if (comp?.Step != PortalCastStep.EntrancePlaced || comp.PendingEntrance == null)
            return null;

        state.Map.UnregisterEntity(comp.PendingEntrance);
        int entranceId = comp.PendingEntrance.Id;
        comp.Step = PortalCastStep.Ready;
        comp.PendingEntrance = null;
        return new PortalEntranceCancelledEvent { ActorId = 0, EntranceEntityId = entranceId };
    }

    /// <summary>
    /// Reset portal wand state on floor transition. The pending entrance entity (if any)
    /// belongs to the old floor's map which is being abandoned — no unregister needed.
    /// </summary>
    public static void ResetPortalWandState(Entity wand)
    {
        var comp = wand.Get<PortalCastStateComponent>();
        if (comp == null) return;
        comp.Step = PortalCastStep.Ready;
        comp.PendingEntrance = null;
    }

    // ─── Step handlers ────────────────────────────────────────────────────────

    private static List<TurnEvent>? PlaceEntrance(
        Entity caster, GameState state, Entity wand,
        PortalCastStateComponent stateComp, EntityFactory entityFactory,
        int? targetX, int? targetY)
    {
        // Use targeted tile; fall back to caster's position (e.g. in tests that pass null)
        int entrX = targetX ?? caster.X;
        int entrY = targetY ?? caster.Y;

        var events = new List<TurnEvent>();

        // Recycle any existing portal pair first
        var removeEvent = RemoveAllPortals(state);
        if (removeEvent != null) events.Add(removeEvent);

        // Validate target tile
        if (!state.Map.IsWalkable(entrX, entrY)) return null;
        if (IsStairTile(entrX, entrY, state)) return null;

        var entrance = entityFactory.Create("Portal Entrance", entrX, entrY);
        entrance.Add(new PortalComponent { Type = PortalType.Entrance, LinkedPortalId = -1 });
        state.Map.RegisterEntity(entrance);

        // Track in component only — NOT in state.Portals yet (preserves 0-or-2 invariant)
        stateComp.PendingEntrance = entrance;
        stateComp.Step = PortalCastStep.EntrancePlaced;

        events.Add(new PortalPlacedEvent
        {
            ActorId = caster.Id,
            PlacerId = caster.Id,
            Type = PortalType.Entrance,
            PortalEntityId = entrance.Id,
            X = entrX,
            Y = entrY,
        });

        return events;
    }

    private static List<TurnEvent>? PlaceExit(
        Entity caster, GameState state, Entity wand,
        PortalCastStateComponent stateComp, EntityFactory entityFactory,
        int? targetX, int? targetY, List<TurnEvent> events)
    {
        if (!targetX.HasValue || !targetY.HasValue) return null;
        int exitX = targetX.Value, exitY = targetY.Value;

        // Recover pending entrance — if it's gone somehow, reset and bail
        var entrance = stateComp.PendingEntrance;
        if (entrance == null)
        {
            stateComp.Step = PortalCastStep.Ready;
            return null;
        }

        // Validate exit tile
        if (!state.Map.IsWalkable(exitX, exitY)) return null;
        if (IsStairTile(exitX, exitY, state)) return null;
        if (exitX == entrance.X && exitY == entrance.Y) return null;

        // Create exit and cross-link
        var exit = entityFactory.Create("Portal Exit", exitX, exitY);
        var entranceComp = entrance.Get<PortalComponent>()!;
        var exitComp = exit.Add(new PortalComponent { Type = PortalType.Exit });
        entranceComp.LinkedPortalId = exit.Id;
        exitComp.LinkedPortalId = entrance.Id;

        // Add both to state.Portals atomically — invariant now satisfied
        state.Map.RegisterEntity(exit);
        state.Portals.Add(entrance);
        state.Portals.Add(exit);

        stateComp.PendingEntrance = null;
        stateComp.Step = PortalCastStep.BothPlaced;

        events.Add(new PortalPlacedEvent
        {
            ActorId = caster.Id,
            PlacerId = caster.Id,
            Type = PortalType.Exit,
            PortalEntityId = exit.Id,
            X = exitX,
            Y = exitY,
        });

        // Displace any monsters standing on either portal tile at placement time.
        // Monsters don't know about portals — they walk into them. Placing the exit
        // on an occupied tile is intentional (displacement mechanic).
        // Check both portals: a monster may have walked onto the entrance during step 1.
        foreach (var monster in state.Monsters)
        {
            if (monster.Get<Fighter>()?.IsAlive != true) continue;
            var teleportEvt = CheckPortalCollision(monster, state);
            if (teleportEvt != null) events.Add(teleportEvt);
        }

        return events;
    }

    private static List<TurnEvent> ResetPortals(
        GameState state, PortalCastStateComponent stateComp, List<TurnEvent> events)
    {
        var removeEvent = RemoveAllPortals(state);
        if (removeEvent != null) events.Add(removeEvent);

        stateComp.Step = PortalCastStep.Ready;
        stateComp.PendingEntrance = null;
        return events;
    }

    /// <summary>
    /// Check if the given entity is standing on a portal and teleport it to the linked portal.
    ///
    /// Returns a PortalTeleportEvent if teleportation occurred, null otherwise.
    /// Updates the entity's position in-place. The caller is responsible for emitting the event.
    ///
    /// Anti-chaining: if the arrival portal's UsedThisTurn flag is set, we do NOT teleport again.
    /// After teleportation, marks the destination portal as UsedThisTurn to prevent chain triggers.
    ///
    /// The entity's own portal (the one they just stepped onto) is intentionally NOT flagged —
    /// it resets next turn via ClearPortalUsedFlags. This allows future traversal.
    /// </summary>
    public static PortalTeleportEvent? CheckPortalCollision(Entity entity, GameState state)
    {
        if (state.Portals.Count == 0) return null;

        // Find a portal at the entity's current position
        var portal = state.Portals.FirstOrDefault(p =>
            p.X == entity.X && p.Y == entity.Y);

        if (portal == null) return null;

        var portalComp = portal.Get<PortalComponent>();
        if (portalComp == null) return null;

        // Anti-chaining: if this portal was just used this turn, don't re-trigger
        if (portalComp.UsedThisTurn) return null;

        // Find the linked exit portal
        var linked = state.Portals.FirstOrDefault(p => p.Id == portalComp.LinkedPortalId);
        if (linked == null) return null;

        var linkedComp = linked.Get<PortalComponent>();
        if (linkedComp == null) return null;

        // Record origin before moving
        int fromX = entity.X;
        int fromY = entity.Y;

        // Teleport: update entity position directly (the map uses a flat list —
        // no spatial hash to update, just the entity's X/Y fields, same as SpellResolver.ResolveTeleport).
        entity.X = linked.X;
        entity.Y = linked.Y;

        // Flag the destination portal as used this turn to prevent immediate re-teleport
        linkedComp.UsedThisTurn = true;

        return new PortalTeleportEvent
        {
            ActorId = entity.Id,
            EntityId = entity.Id,
            FromX = fromX,
            FromY = fromY,
            ToX = entity.X,
            ToY = entity.Y,
            PortalEntryId = portal.Id,
            PortalExitId = linked.Id,
        };
    }

    /// <summary>
    /// Remove all active portals from the map and clear state.Portals.
    /// Called on floor transitions so portals do not bleed across floors.
    /// Also clears UsedThisTurn flags (moot, but clean).
    /// Returns a PortalRemovedEvent if portals existed, null otherwise.
    /// </summary>
    public static PortalRemovedEvent? ClearPortals(GameState state)
    {
        return RemoveAllPortals(state);
    }

    /// <summary>
    /// Clear the UsedThisTurn flag on all active portals.
    /// Must be called at end-of-turn to allow portals to fire again next turn.
    /// </summary>
    public static void ClearPortalUsedFlags(GameState state)
    {
        foreach (var portal in state.Portals)
        {
            var comp = portal.Get<PortalComponent>();
            if (comp != null) comp.UsedThisTurn = false;
        }
    }

    // ─── Private helpers ───────────────────────────────────────────────────────

    private static PortalRemovedEvent? RemoveAllPortals(GameState state)
    {
        if (state.Portals.Count == 0) return null;

        // Find entrance and exit for the event payload
        int entranceId = -1;
        int exitId = -1;
        foreach (var p in state.Portals)
        {
            var comp = p.Get<PortalComponent>();
            if (comp?.Type == PortalType.Entrance) entranceId = p.Id;
            else if (comp?.Type == PortalType.Exit) exitId = p.Id;

            // Unregister from map — entity still exists in memory but is no longer on the map
            state.Map.UnregisterEntity(p);
        }

        state.Portals.Clear();

        // Only emit if we had a complete pair; partial pairs (shouldn't happen) still clean up
        if (entranceId != -1 && exitId != -1)
        {
            return new PortalRemovedEvent
            {
                ActorId = 0, // system action — no specific actor
                EntranceEntityId = entranceId,
                ExitEntityId = exitId,
            };
        }

        return null;
    }

    /// <summary>
    /// Returns true if the given tile is occupied by a stair-down entity.
    /// Portal placement on stairs is blocked: teleporting to a stair tile would
    /// immediately trigger floor descent, which is almost certainly unintended.
    /// </summary>
    private static bool IsStairTile(int x, int y, GameState state)
    {
        return state.StairDown != null
            && state.StairDown.X == x
            && state.StairDown.Y == y;
    }
}
