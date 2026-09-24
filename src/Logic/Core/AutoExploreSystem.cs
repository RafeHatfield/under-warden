using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;

namespace UnderWarden.Logic.Core;

/// <summary>
/// Drives auto-explore behaviour. Stateless — all state lives in AutoExploreState on the player.
/// Uses Dijkstra to find the nearest unexplored tile, A* to path there.
/// Checks interrupt conditions before each step.
/// </summary>
public static class AutoExploreSystem
{
    /// <summary>
    /// Activate auto-explore. Takes snapshot of current explored tiles and visible monsters.
    /// Safe to call repeatedly — resets state each time.
    /// </summary>
    public static void Activate(GameState state)
    {
        var ae = state.Player.GetOrAdd<AutoExploreState>();
        ae.IsActive = true;
        ae.StopReason = null;
        ae.CurrentPath.Clear();
        ae.StuckCounter = 0;
        ae.LastExpectedPosition = null;
        ae.LastHp = state.PlayerFighter.Hp;
        ae.ResetPositionHistory();

        // Snapshot explored tiles — two-pass strategy uses this to prioritise NEW discoveries
        ae.ExploredSnapshot.Clear();
        for (int x = 0; x < state.Map.Width; x++)
            for (int y = 0; y < state.Map.Height; y++)
                if (state.Map.IsExplored(x, y))
                    ae.ExploredSnapshot.Add((x, y));

        // Snapshot monsters currently in FOV — don't interrupt for these
        ae.KnownMonsterIds.Clear();
        foreach (var m in state.AliveMonsters)
            if (state.Map.IsVisible(m.X, m.Y))
                ae.KnownMonsterIds.Add(m.Id);

        // Snapshot interesting items on already-explored tiles — don't re-interrupt for these.
        // Uses explored (ever seen) rather than currently visible: prevents re-alerting when
        // the player walks back near something they already found in a previous explore run.
        ae.KnownItemIds.Clear();
        foreach (var item in state.FloorItems)
            if (IsInterestingItem(item) && state.Map.IsExplored(item.X, item.Y))
                ae.KnownItemIds.Add(item.Id);

        // Snapshot features on already-explored tiles — same rationale as items above.
        ae.KnownFeatureIds.Clear();
        foreach (var feature in state.Features)
            if (state.Map.IsExplored(feature.X, feature.Y))
                ae.KnownFeatureIds.Add(feature.Id);

        // Snapshot stairs already out of fog-of-war — don't interrupt for these.
        // Uses IsExplored (ever seen) not IsVisible (currently in FOV): once the player
        // has uncovered the stair tile from FoW, re-activating explore should not stop again.
        ae.KnownStairs.Clear();
        if (state.StairDown != null && state.Map.IsExplored(state.StairDown.X, state.StairDown.Y))
            ae.KnownStairs.Add((state.StairDown.X, state.StairDown.Y));
    }

    /// <summary>
    /// Get the next move action. Returns null and deactivates if stopped.
    /// Call this once per turn when auto-explore is active.
    /// </summary>
    public static PlayerAction? GetNextAction(GameState state)
    {
        var ae = state.Player.Get<AutoExploreState>();
        if (ae == null || !ae.IsActive) return null;

        var stopReason = CheckInterrupts(state, ae);
        if (stopReason != null)
        {
            Stop(ae, stopReason);
            return null;
        }

        if (ae.CurrentPath.Count == 0 && !FindAndSetPath(state, ae))
        {
            Stop(ae, "Exploration complete");
            return null;
        }

        ae.LastExpectedPosition = ae.CurrentPath[0];
        ae.CurrentPath.RemoveAt(0);
        return PlayerAction.MoveTo(ae.LastExpectedPosition.Value.X, ae.LastExpectedPosition.Value.Y);
    }

    /// <summary>
    /// True if the floor item is worth stopping auto-explore for.
    /// Potions/scrolls/wands/rings have IdentifiableItem; weapons/armor/rings have Equippable.
    /// Using component presence avoids fragile typeId prefix matching.
    /// </summary>
    private static bool IsInterestingItem(Entity item) =>
        item.Get<IdentifiableItem>() != null || item.Get<Combat.Equippable>() != null;

    // How close a new monster or item must be to interrupt auto-explore.
    // The FOV radius is 8, but the screen shows roughly 5-6 tiles. Using 5 here means
    // only threats actually visible on screen will stop the player — matching Rogue Wizards
    // behaviour where off-screen discoveries don't interrupt. Monsters beyond this radius
    // are still revealed in the map fog-of-war; they just don't stop the run.
    private const int AlertRadius = 5;

    private static string? CheckInterrupts(GameState state, AutoExploreState ae)
    {
        // 1. New monster in visible FOV and within alert radius (not known at activation)
        foreach (var m in state.AliveMonsters)
            if (state.Map.IsVisible(m.X, m.Y)
                && state.Player.ChebyshevDistanceTo(m.X, m.Y) <= AlertRadius
                && !ae.KnownMonsterIds.Contains(m.Id))
                return $"Monster spotted: {m.Name}";

        // 2. Interesting floor items visible and in range — skip ones known at activation
        foreach (var item in state.FloorItems)
        {
            if (!IsInterestingItem(item)) continue;
            if (ae.KnownItemIds.Contains(item.Id)) continue;
            if (state.Map.IsVisible(item.X, item.Y)
                && state.Player.ChebyshevDistanceTo(item.X, item.Y) <= AlertRadius)
                return $"Found: {ItemDisplay.GetDisplayName(item, state.IdentificationRegistry, state.AppearancePool)}";
        }

        // 3. Interactive features (chests, signs, murals) visible and in range
        foreach (var feature in state.Features)
        {
            if (ae.KnownFeatureIds.Contains(feature.Id)) continue;
            if (!state.Map.IsVisible(feature.X, feature.Y)) continue;
            if (state.Player.ChebyshevDistanceTo(feature.X, feature.Y) > AlertRadius) continue;

            var chest = feature.Get<ChestComponent>();
            if (chest != null && !chest.IsOpen) return "Found: chest";

            if (feature.Get<SignpostComponent>() != null) return "Found: sign";
            if (feature.Get<MuralComponent>() != null) return "Found: mural";
        }

        // 3. (Stairs found interrupt removed — auto-explore runs until every tile is uncovered.
        //    Pass 3 of FindAndSetPath guides the player to stairs after the floor is fully explored.)

        // 4. Damage taken
        if (state.PlayerFighter.Hp < ae.LastHp)
            return "Took damage";

        // 5. Stuck — didn't reach expected position
        if (ae.LastExpectedPosition.HasValue)
        {
            var (ex, ey) = ae.LastExpectedPosition.Value;
            if (state.Player.X != ex || state.Player.Y != ey)
            {
                ae.StuckCounter++;
                if (ae.StuckCounter >= 3) return "Movement blocked";
            }
            else
            {
                ae.StuckCounter = 0;
            }
        }

        // 6. Oscillation detection
        ae.RecordPosition(state.Player.X, state.Player.Y);
        if (ae.IsOscillating()) return "Movement oscillation detected";

        // Update tracking state for next call
        ae.LastHp = state.PlayerFighter.Hp;
        if (state.StairDown != null && state.Map.IsExplored(state.StairDown.X, state.StairDown.Y))
            ae.KnownStairs.Add((state.StairDown.X, state.StairDown.Y));

        return null;
    }

    private static bool FindAndSetPath(GameState state, AutoExploreState ae)
    {
        var dijkstra = Pathfinder.DijkstraMap(state.Map, state.Player.X, state.Player.Y, canPassDoors: true);

        // Exclude cells occupied by blocking entities (chests, signs, murals, monsters).
        // DijkstraMap ignores entities and assigns distances through them, but A* cannot
        // path TO such a cell as a move destination — the player would bump-interact instead.
        // Filtering here prevents auto-explore from ever targeting a feature or monster cell,
        // which would cause it to auto-open chests or walk into monsters.
        static bool NotBlocked(GameMap map, int x, int y) => !map.IsBlocked(x, y);

        // Pass 1: prefer tiles not in the explored snapshot (new discoveries)
        var target = Pathfinder.NearestWhere(dijkstra, state.Map.Width, state.Map.Height,
            (x, y) => !state.Map.IsExplored(x, y) && !ae.ExploredSnapshot.Contains((x, y))
                      && NotBlocked(state.Map, x, y));

        // Pass 2: fall back to any unexplored tile (finish isolated pockets)
        target ??= Pathfinder.NearestWhere(dijkstra, state.Map.Width, state.Map.Height,
            (x, y) => !state.Map.IsExplored(x, y) && NotBlocked(state.Map, x, y));

        // Pass 3: map fully explored — path to the stair down if it exists and we're not on it
        if (target == null && state.StairDown != null
            && (state.Player.X != state.StairDown.X || state.Player.Y != state.StairDown.Y))
        {
            target = (state.StairDown.X, state.StairDown.Y);
        }

        if (target == null) return false;

        var path = Pathfinder.AStar(state.Map,
            state.Player.X, state.Player.Y,
            target.Value.X, target.Value.Y,
            state.Player,
            canPassDoors: true);

        if (path == null || path.Count == 0) return false;

        ae.CurrentPath.Clear();
        ae.CurrentPath.AddRange(path);
        return true;
    }

    public static void Stop(AutoExploreState ae, string reason)
    {
        ae.IsActive = false;
        ae.StopReason = reason;
        ae.CurrentPath.Clear();
    }
}
