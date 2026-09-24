using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;
using NUnit.Framework;

namespace UnderWarden.Tests.Logic;

[TestFixture]
public class AutoExploreTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build a dungeon-mode GameState with a small carved map (walls on border, floor inside).
    /// IsDungeonMode=true so RecomputeFov is active.
    /// </summary>
    private static GameState MakeDungeonState(int width = 15, int height = 15,
        int playerX = 7, int playerY = 7)
    {
        var map = new GameMap(width, height, allWalls: true);
        // Carve interior as floor
        for (int x = 1; x < width - 1; x++)
            for (int y = 1; y < height - 1; y++)
                map.SetTile(x, y, TileKind.Floor);

        var player = new Entity(0, "Player", playerX, playerY, blocksMovement: true);
        player.Add(new Fighter(hp: 20, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var state = new GameState(player, new List<Entity>(), map, new SeededRandom(42))
        {
            IsDungeonMode = true,
            CurrentDepth = 1
        };
        state.RecomputeFov();
        return state;
    }

    /// <summary>
    /// Add a monster to an existing state's map and monster list.
    /// Note: Monsters list is read-only via property but backed by the list passed to constructor.
    /// We store the list and pass it in to allow mutation in tests.
    /// </summary>
    private static (GameState State, List<Entity> MonsterList) MakeDungeonStateWithMonsterList(
        int width = 25, int height = 25, int playerX = 2, int playerY = 2)
    {
        var map = new GameMap(width, height, allWalls: true);
        for (int x = 1; x < width - 1; x++)
            for (int y = 1; y < height - 1; y++)
                map.SetTile(x, y, TileKind.Floor);

        var player = new Entity(0, "Player", playerX, playerY, blocksMovement: true);
        player.Add(new Fighter(hp: 20, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var monsters = new List<Entity>();
        var state = new GameState(player, monsters, map, new SeededRandom(42))
        {
            IsDungeonMode = true,
            CurrentDepth = 1
        };
        state.RecomputeFov();
        return (state, monsters);
    }

    private static Entity MakeMonster(int id, int x, int y)
    {
        var m = new Entity(id, "TestOrc", x, y, blocksMovement: true);
        m.Add(new Fighter(hp: 10, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 2));
        return m;
    }

    // -----------------------------------------------------------------------
    // Activation
    // -----------------------------------------------------------------------

    [Test]
    public void AutoExplore_Activate_SetsIsActive()
    {
        var state = MakeDungeonState();

        AutoExploreSystem.Activate(state);

        var ae = state.Player.Get<AutoExploreState>();
        Assert.That(ae, Is.Not.Null, "AutoExploreState component should be added on Activate");
        Assert.That(ae!.IsActive, Is.True);
    }

    [Test]
    public void AutoExplore_Activate_ClearsStopReason()
    {
        var state = MakeDungeonState();
        // Pre-set a stale stop reason to confirm Activate clears it
        var ae = state.Player.GetOrAdd<AutoExploreState>();
        ae.StopReason = "stale reason";

        AutoExploreSystem.Activate(state);

        Assert.That(ae.StopReason, Is.Null);
    }

    // -----------------------------------------------------------------------
    // Basic movement
    // -----------------------------------------------------------------------

    [Test]
    public void AutoExplore_GetNextAction_ReturnsMove_WhenUnexploredTilesExist()
    {
        // Small 15x15 map — player starts at (7,7) and can only see nearby tiles.
        // There will be many unexplored tiles further away.
        var state = MakeDungeonState();

        AutoExploreSystem.Activate(state);
        var action = AutoExploreSystem.GetNextAction(state);

        Assert.That(action, Is.Not.Null, "Should return a move action when unexplored tiles exist");
        Assert.That(action!.Kind, Is.EqualTo(PlayerAction.ActionKind.Move));
        Assert.That(action.TargetX, Is.Not.Null);
        Assert.That(action.TargetY, Is.Not.Null);
    }

    [Test]
    public void AutoExplore_StopsWhenFullyExplored()
    {
        // Use a very small map so we can fully explore it in a reasonable number of steps
        var state = MakeDungeonState(width: 9, height: 9, playerX: 4, playerY: 4);

        AutoExploreSystem.Activate(state);

        // Simulate exploration: get action, teleport player to destination, recompute FOV
        // Loop until auto-explore stops or we hit a safety limit
        const int maxIterations = 200;
        int iterations = 0;
        PlayerAction? action;

        do
        {
            action = AutoExploreSystem.GetNextAction(state);
            if (action == null) break;

            // Teleport player to target (bypasses TurnController — tests AutoExploreSystem logic)
            state.Player.X = action.TargetX!.Value;
            state.Player.Y = action.TargetY!.Value;
            state.RecomputeFov();

            iterations++;
        }
        while (iterations < maxIterations);

        var ae = state.Player.Get<AutoExploreState>();
        Assert.That(action, Is.Null, "GetNextAction should return null when exploration completes");
        Assert.That(ae?.StopReason, Does.Contain("complete").IgnoreCase,
            "StopReason should indicate exploration complete");
    }

    // -----------------------------------------------------------------------
    // Interrupt: new monster in FOV
    // -----------------------------------------------------------------------

    [Test]
    public void AutoExplore_StopsOnNewMonsterInFov()
    {
        // Use a large map so FOV doesn't immediately cover the monster at spawn
        var (state, monsters) = MakeDungeonStateWithMonsterList(width: 25, height: 25,
            playerX: 2, playerY: 2);

        // Place monster far away (outside initial FOV radius of 8)
        var monster = MakeMonster(1, 20, 20);
        monsters.Add(monster);
        state.Map.RegisterEntity(monster);

        AutoExploreSystem.Activate(state);

        // Verify first action is valid (monster not in FOV yet)
        var firstAction = AutoExploreSystem.GetNextAction(state);
        Assert.That(firstAction, Is.Not.Null, "Auto-explore should start moving");

        // Teleport player next to the monster so it enters FOV
        state.Player.X = 20;
        state.Player.Y = 19;
        state.RecomputeFov();

        // Monster should now be visible — next GetNextAction should stop
        var stopAction = AutoExploreSystem.GetNextAction(state);
        Assert.That(stopAction, Is.Null, "Should stop when new monster enters FOV");

        var ae = state.Player.Get<AutoExploreState>();
        Assert.That(ae?.StopReason, Does.Contain("Monster").IgnoreCase);
    }

    [Test]
    public void AutoExplore_DoesNotStopForMonsterVisibleAtActivation()
    {
        // Monster visible at activation should be in KnownMonsterIds — don't interrupt for it
        var (state, monsters) = MakeDungeonStateWithMonsterList(width: 15, height: 15,
            playerX: 7, playerY: 7);

        // Place monster adjacent to player (definitely in initial FOV)
        var monster = MakeMonster(1, 8, 7);
        monsters.Add(monster);
        state.Map.RegisterEntity(monster);
        state.RecomputeFov();

        Assert.That(state.Map.IsVisible(8, 7), Is.True, "Monster must be visible at activation");

        AutoExploreSystem.Activate(state);

        // First GetNextAction should NOT be null — monster was visible at activation
        var action = AutoExploreSystem.GetNextAction(state);
        Assert.That(action, Is.Not.Null,
            "Should not stop for a monster that was visible at activation");
    }

    // -----------------------------------------------------------------------
    // Interrupt: damage taken
    // -----------------------------------------------------------------------

    [Test]
    public void AutoExplore_StopsOnDamageTaken()
    {
        var state = MakeDungeonState();

        AutoExploreSystem.Activate(state);

        // Simulate taking damage — reduce HP directly
        state.PlayerFighter.Hp -= 5;

        var action = AutoExploreSystem.GetNextAction(state);
        Assert.That(action, Is.Null, "Should stop when player takes damage");

        var ae = state.Player.Get<AutoExploreState>();
        Assert.That(ae?.StopReason, Does.Contain("damage").IgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Interrupt: oscillation detection
    // -----------------------------------------------------------------------

    [Test]
    public void AutoExplore_OscillationDetected_IsOscillating_ReturnsTrue()
    {
        var ae = new AutoExploreState();

        // Record A-B-A-B-A-B pattern
        ae.RecordPosition(1, 1); // A
        ae.RecordPosition(2, 2); // B
        ae.RecordPosition(1, 1); // A
        ae.RecordPosition(2, 2); // B
        ae.RecordPosition(1, 1); // A
        ae.RecordPosition(2, 2); // B

        Assert.That(ae.IsOscillating(), Is.True, "Should detect A-B-A-B-A-B oscillation");
    }

    [Test]
    public void AutoExplore_NoOscillation_IsOscillating_ReturnsFalse()
    {
        var ae = new AutoExploreState();

        // Forward movement — no oscillation
        ae.RecordPosition(1, 1);
        ae.RecordPosition(2, 2);
        ae.RecordPosition(3, 3);
        ae.RecordPosition(4, 4);
        ae.RecordPosition(5, 5);
        ae.RecordPosition(6, 6);

        Assert.That(ae.IsOscillating(), Is.False, "Forward movement should not be detected as oscillation");
    }

    [Test]
    public void AutoExplore_InsufficientHistory_IsOscillating_ReturnsFalse()
    {
        var ae = new AutoExploreState();

        // Only 4 positions — not enough for full pattern detection
        ae.RecordPosition(1, 1);
        ae.RecordPosition(2, 2);
        ae.RecordPosition(1, 1);
        ae.RecordPosition(2, 2);

        Assert.That(ae.IsOscillating(), Is.False, "Need 6 positions to detect oscillation");
    }

    // -----------------------------------------------------------------------
    // Interrupt: stuck detection
    // -----------------------------------------------------------------------

    [Test]
    public void AutoExplore_StuckCounter_Stops_AfterThreeMissedSteps()
    {
        var state = MakeDungeonState();

        AutoExploreSystem.Activate(state);

        // Get first action — this sets LastExpectedPosition
        var action = AutoExploreSystem.GetNextAction(state);
        Assert.That(action, Is.Not.Null, "Precondition: should get initial action");

        var ae = state.Player.Get<AutoExploreState>()!;
        // Do NOT move the player — simulate blocked movement.
        // LastExpectedPosition is now set to somewhere the player isn't.
        // Each call to GetNextAction increments StuckCounter when position doesn't match.

        // Call 1 — StuckCounter goes to 1
        var a1 = AutoExploreSystem.GetNextAction(state);
        // Call 2 — StuckCounter goes to 2
        var a2 = AutoExploreSystem.GetNextAction(state);
        // Call 3 — StuckCounter reaches 3 → should stop
        var a3 = AutoExploreSystem.GetNextAction(state);

        // At least one of these should be null (stop at count >= 3)
        // a3 should definitely be null if stuck 3 times (the 3rd miss triggers the stop)
        Assert.That(ae.IsActive, Is.False, "Auto-explore should deactivate after 3 blocked steps");
        Assert.That(ae.StopReason, Does.Contain("blocked").IgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Stairs do not interrupt — explore runs until every tile is uncovered
    // -----------------------------------------------------------------------

    [Test]
    public void AutoExplore_NewlyUncoveredStairs_DoNotInterrupt()
    {
        // 30×30 map, player at (2,2) — stair at (28,28) is well outside FOV
        var state = MakeDungeonState(width: 30, height: 30, playerX: 2, playerY: 2);

        var stair = new Entity(99, "StairDown", 28, 28, blocksMovement: false);
        state.StairDown = stair;
        state.RecomputeFov();

        Assert.That(state.Map.IsExplored(28, 28), Is.False, "Stair must be unexplored for this test");

        AutoExploreSystem.Activate(state);

        // Simulate stairs entering FOV for the first time mid-run
        state.Map.SetVisible(28, 28);

        // Stair visibility must NOT stop auto-explore — the player wants to finish the floor
        var action1 = AutoExploreSystem.GetNextAction(state);
        var ae = state.Player.Get<AutoExploreState>()!;

        if (action1 == null)
        {
            Assert.That(ae.StopReason, Does.Not.Contain("Stairs").IgnoreCase,
                "Stairs becoming visible must not interrupt auto-explore");
        }
        // If action1 is non-null, auto-explore correctly continued — test passes
    }

    [Test]
    public void AutoExplore_AlreadyExploredStairs_DoNotInterrupt()
    {
        var state = MakeDungeonState();

        // Place stair adjacent to player (will be in initial FOV)
        var stair = new Entity(99, "StairDown", 8, 7, blocksMovement: false);
        state.StairDown = stair;
        state.RecomputeFov(); // stair tile becomes explored

        Assert.That(state.Map.IsExplored(8, 7), Is.True, "Stair must be explored for this test");

        AutoExploreSystem.Activate(state);

        // Stair was already explored at activation → in KnownStairs → must not interrupt
        var action1 = AutoExploreSystem.GetNextAction(state);
        var ae = state.Player.Get<AutoExploreState>()!;

        if (action1 == null)
        {
            Assert.That(ae.StopReason, Does.Not.Contain("Stairs").IgnoreCase,
                "Already-explored stairs must not interrupt auto-explore");
        }
    }

    // -----------------------------------------------------------------------
    // Position history buffer rollover
    // -----------------------------------------------------------------------

    [Test]
    public void AutoExplore_PositionHistory_CircularBuffer_RollsOver()
    {
        var ae = new AutoExploreState();

        // Fill more than 6 positions — the buffer should only keep the last 6
        for (int i = 0; i < 8; i++)
            ae.RecordPosition(i, i);

        // After 8 records, positions 0-5 should map to (2,2),(3,3),(4,4),(5,5),(6,6),(7,7)
        // IsOscillating checks specific pattern — just verify it doesn't throw and returns false
        Assert.That(ae.IsOscillating(), Is.False,
            "Monotonically increasing positions should not be detected as oscillation");
    }

    // -----------------------------------------------------------------------
    // Deactivation state consistency
    // -----------------------------------------------------------------------

    [Test]
    public void AutoExplore_AfterStop_IsActiveIsFalse()
    {
        var state = MakeDungeonState();

        AutoExploreSystem.Activate(state);
        // Force stop by taking damage
        state.PlayerFighter.Hp -= 5;
        AutoExploreSystem.GetNextAction(state);

        var ae = state.Player.Get<AutoExploreState>()!;
        Assert.That(ae.IsActive, Is.False);
        Assert.That(ae.CurrentPath, Is.Empty, "Path should be cleared on stop");
    }

    [Test]
    public void AutoExplore_GetNextAction_WhenNotActive_ReturnsNull()
    {
        var state = MakeDungeonState();

        // Don't call Activate — AutoExploreState has IsActive=false by default
        state.Player.GetOrAdd<AutoExploreState>(); // ensure component exists but inactive

        var action = AutoExploreSystem.GetNextAction(state);
        Assert.That(action, Is.Null);
    }

    [Test]
    public void AutoExplore_GetNextAction_WhenNoComponent_ReturnsNull()
    {
        var state = MakeDungeonState();
        // Don't activate — no component added at all

        var action = AutoExploreSystem.GetNextAction(state);
        Assert.That(action, Is.Null);
    }

    // -----------------------------------------------------------------------
    // Feature blocking: chests, signs, murals
    // -----------------------------------------------------------------------

    /// <summary>
    /// A chest placed between the player and unexplored area should never be
    /// targeted as a move destination by auto-explore. The player stops adjacent
    /// to it (or finds another route) — never bumps it open automatically.
    ///
    /// Map layout (11x9 corridor): player at (1,4), chest at (5,4).
    /// Tiles to the right of the chest (6-9, 4) are unexplored.
    /// Auto-explore must NOT return a path step that puts the player on (5,4).
    /// </summary>
    [Test]
    public void AutoExplore_DoesNotAutoOpen_ChestInPath()
    {
        int width = 11, height = 9;
        var map = new GameMap(width, height, allWalls: true);
        // Carve a horizontal corridor
        for (int x = 1; x < width - 1; x++)
            map.SetTile(x, 4, TileKind.Floor);

        var player = new Entity(0, "Player", 1, 4, blocksMovement: true);
        player.Add(new Fighter(hp: 20, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        // Place a chest at (5,4) — blocking the corridor
        var ids = new EntityIdAllocator();
        ids.Next(); // skip 0 (player)
        var chest = FeatureFactory.CreateChest(5, 4, ids, new List<string>());
        map.RegisterEntity(chest);

        var monsters = new List<Entity>();
        var state = new GameState(player, monsters, map, new SeededRandom(42))
        {
            IsDungeonMode = true,
            CurrentDepth = 1
        };
        state.Features.Add(chest);

        // Reveal only the left portion — tiles right of chest are unexplored
        state.Map.SetVisible(1, 4);
        state.Map.SetVisible(2, 4);
        state.Map.SetVisible(3, 4);
        state.Map.SetVisible(4, 4);
        // Chest cell at (5,4) is explored (FOV reached it), right side is unknown
        state.Map.SetVisible(5, 4);

        AutoExploreSystem.Activate(state);

        // Run several turns of auto-explore. Track every step target.
        var stepTargets = new List<(int X, int Y)>();
        const int maxSteps = 30;
        for (int i = 0; i < maxSteps; i++)
        {
            var action = AutoExploreSystem.GetNextAction(state);
            if (action == null) break;

            int tx = action.TargetX!.Value;
            int ty = action.TargetY!.Value;
            stepTargets.Add((tx, ty));

            // Move player to requested position
            state.Player.X = tx;
            state.Player.Y = ty;
            state.RecomputeFov();
        }

        // The chest cell (5,4) must NEVER be a targeted step
        Assert.That(stepTargets, Does.Not.Contain((5, 4)),
            "Auto-explore must not target the chest cell — that would auto-open it");
    }
}
