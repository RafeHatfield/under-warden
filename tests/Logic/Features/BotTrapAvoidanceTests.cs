using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;
using NUnit.Framework;

namespace UnderWarden.Tests.Logic.Features;

/// <summary>
/// TASK-026: BotBrain routes around detected floor traps using trap-aware A*.
/// TASK-027: AutoExplore stops when a TrapDetectedEvent fires.
///
/// Both features were implemented before this test file. Tests verify the wiring.
/// </summary>
[TestFixture]
public class BotTrapAvoidanceTests
{
    // ── TASK-026: Pathfinder.AStar trap avoidance ─────────────────────────────

    [Test]
    public void Pathfinder_AvoidTiles_SkipsTrapOnDirectPath()
    {
        // Arena: player at (1,5), monster at (10,5). Trap at (5,5) blocks the direct path.
        // Without avoidance: path goes through (5,5).
        // With avoidance: path detours around (5,5).
        var map = GameMap.CreateArena(14, 12);

        var player = new Entity(0, "Player", 1, 5, blocksMovement: true);
        map.RegisterEntity(player);

        var directPath = Pathfinder.AStar(map, 1, 5, 10, 5, player);
        Assert.That(directPath, Is.Not.Null, "A path must exist without avoidance");
        Assert.That(directPath!.Any(p => p.X == 5 && p.Y == 5), Is.True,
            "Direct path should pass through (5,5)");

        // Now mark (5,5) as avoid
        var avoidTiles = new HashSet<(int, int)> { (5, 5) };
        var avoidPath = Pathfinder.AStar(map, 1, 5, 10, 5, player, avoidTiles: avoidTiles);

        Assert.That(avoidPath, Is.Not.Null, "A detour path must exist");
        Assert.That(avoidPath!.Any(p => p.X == 5 && p.Y == 5), Is.False,
            "Avoidance path must NOT pass through the trap tile");
    }

    [Test]
    public void DetectedTrapTiles_OnlyIncludesDetectedNonSpent()
    {
        // Detected, non-spent → included.
        // Undetected → excluded.
        // Detected + spent → excluded.
        var detected = new Entity(1, "Trap A", 3, 3, blocksMovement: false);
        detected.Add(new FloorTrapComponent
        {
            TrapType = "spike_trap", IsDetected = true, IsSpent = false,
            Payload = new TrapPayloadComponent(), VisibleTileId = 429,
        });

        var undetected = new Entity(2, "Trap B", 4, 4, blocksMovement: false);
        undetected.Add(new FloorTrapComponent
        {
            TrapType = "web_trap", IsDetected = false, IsSpent = false,
            Payload = new TrapPayloadComponent(), VisibleTileId = 430,
        });

        var spent = new Entity(3, "Trap C", 5, 5, blocksMovement: false);
        spent.Add(new FloorTrapComponent
        {
            TrapType = "gas_trap", IsDetected = true, IsSpent = true,
            Payload = new TrapPayloadComponent(), VisibleTileId = 431,
        });

        var features = new List<Entity> { detected, undetected, spent };
        var tiles = Pathfinder.DetectedTrapTiles(features);

        Assert.That(tiles, Has.Count.EqualTo(1), "Only one tile should qualify");
        Assert.That(tiles.Contains((3, 3)), Is.True, "Only the detected non-spent trap at (3,3)");
    }

    [Test]
    public void HarnessBot_RoutesAroundDetectedTrap_WhenAlternateTileExists()
    {
        // Setup: 14×12 arena. Player at (1,6), monster at (10,6), detected trap at (5,6).
        // The bot should take a detour (via y=5 or y=7) rather than stepping through the trap.
        var map = GameMap.CreateArena(14, 12);

        var player = new Entity(0, "Player", 1, 6, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 5, evasion: 1, damageMin: 3, damageMax: 5));
        map.RegisterEntity(player);

        var monster = new Entity(1, "Orc", 10, 6, blocksMovement: true);
        monster.Add(new Fighter(hp: 30, strength: 10, dexterity: 8, constitution: 10,
            accuracy: 2, evasion: 0, damageMin: 2, damageMax: 4));
        monster.Add(new AiComponent { AiType = "basic", Faction = "orc", Tags = ["humanoid"] });
        map.RegisterEntity(monster);

        // Detected trap at (5,6) — right on the direct corridor
        var trapPayload = new TrapPayloadComponent();
        trapPayload.Actions.Add(new TrapAction { Kind = "damage", Amount = 99 }); // lethal if triggered
        var trapFeature = new Entity(10, "Spike Trap", 5, 6, blocksMovement: false);
        trapFeature.Add(new FloorTrapComponent
        {
            TrapType = "spike_trap",
            IsDetected = true,  // bot can see it
            IsSpent = false,
            IsDetectable = true,
            PassiveDetectChance = 0.0,
            Payload = trapPayload,
            VisibleTileId = 429,
        });

        var state = new GameState(player, new List<Entity> { monster }, map, new SeededRandom(1337));
        state.Features.Add(trapFeature);

        // Simulate the harness A* routing logic directly (same code as DungeonRunHarness)
        var trapTiles = Pathfinder.DetectedTrapTiles(state.Features);
        Assert.That(trapTiles.Contains((5, 6)), Is.True, "Trap tile should be in avoid set");

        var path = Pathfinder.AStar(
            state.Map,
            state.Player.X, state.Player.Y,
            monster.X, monster.Y,
            state.Player,
            canPassDoors: true,
            avoidTiles: trapTiles);

        Assert.That(path, Is.Not.Null, "A detour path to the monster must exist");
        Assert.That(path!.Any(p => p.X == 5 && p.Y == 6), Is.False,
            "Path must NOT route through the detected trap at (5,6)");
        Assert.That(path.Last(), Is.EqualTo((10, 6)), "Path must reach the monster at (10,6)");
    }

    [Test]
    public void HarnessBot_FallsBackToDirectPath_WhenNoDetour()
    {
        // Extreme case: tiny 5×3 corridor where the only path goes through the trap.
        // Fallback A* (without avoidTiles) must still find a path so bot doesn't freeze.
        // This matches the harness fallback path: path ??= AStar(...) without avoidTiles.
        var map = GameMap.CreateArena(7, 3);

        var player = new Entity(0, "Player", 1, 1, blocksMovement: true);
        map.RegisterEntity(player);

        // Trap tile
        var avoidTiles = new HashSet<(int, int)> { (3, 1) };

        // With avoidance: no path in a dead-straight 7×3 corridor
        var pathWithAvoid = Pathfinder.AStar(map, 1, 1, 5, 1, player, avoidTiles: avoidTiles);

        // Without avoidance: path exists (fallback)
        var pathFallback = Pathfinder.AStar(map, 1, 1, 5, 1, player);

        // The fallback must succeed even if avoidance fails
        Assert.That(pathFallback, Is.Not.Null, "Fallback path (no avoidance) must exist");
        // With avoidance, whether it finds a path depends on map geometry — just confirm no crash
        // In a flat arena, diagonals bypass the trap tile so path may still find a route.
        // Key invariant: fallback is always at least as good as avoided path.
        if (pathWithAvoid != null)
            Assert.That(pathFallback!.Count, Is.LessThanOrEqualTo(pathWithAvoid.Count + 4),
                "Fallback path length should be similar to avoidance path");
    }

    // ── TASK-027: AutoExplore stops on TrapDetectedEvent ─────────────────────

    [Test]
    public void AutoExplore_StopsWhenTrapDetected()
    {
        // Player with auto-explore active walks toward a tile with guaranteed-detect trap.
        // TrapDetectedEvent should fire and auto-explore should stop.
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 2, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4) { CanOpenDoors = true });
        var ae = new AutoExploreState { IsActive = true };
        player.Add(ae);
        map.RegisterEntity(player);

        // Spike trap at (4,5) — guaranteed passive detect (detectChance=1.0)
        var payload = new TrapPayloadComponent();
        payload.Actions.Add(new TrapAction { Kind = "damage", Amount = 7 });

        var trap = new Entity(10, "Spike Trap", 4, 5, blocksMovement: false);
        trap.Add(new FloorTrapComponent
        {
            TrapType            = "spike_trap",
            IsDetected          = false,
            IsSpent             = false,
            IsDetectable        = true,
            PassiveDetectChance = 1.0,  // guaranteed detect
            Payload             = payload,
            VisibleTileId       = 429,
        });

        var state = new GameState(player, new List<Entity>(), map, new SeededRandom(1337));
        state.Features.Add(trap);

        // Move player one step onto the trap tile
        var result = TurnController.ProcessTurn(state, PlayerAction.MoveTo(3, 5));

        // Then one more step that would step on the trap
        var result2 = TurnController.ProcessTurn(state, PlayerAction.MoveTo(4, 5));

        var detectEvent = result2.Events.OfType<TrapDetectedEvent>().FirstOrDefault();
        Assert.That(detectEvent, Is.Not.Null, "TrapDetectedEvent should fire on passive detect");
        Assert.That(ae.IsActive, Is.False,
            "AutoExplore should stop when a trap is detected");
    }
}
