using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace UnderWarden.Integration;

using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Presentation.UI;

/// <summary>
/// Integration tests for ToastLog node lifecycle.
/// Verifies the overflow eviction never exceeds MaxToasts,
/// which was the root cause of the infinite loop freeze.
///
/// The original bug: SpawnToast had a while(_stack.GetChildCount() >= MaxToasts) loop
/// that checked child count but removed from _activeToasts — mismatch meant the loop
/// never terminated once MaxToasts was reached. These tests cover that exact path.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ToastLogTests
{
    private ISceneRunner _runner = null!;

    // Player ID used consistently across helper methods and the system under test.
    private const int PlayerId = 1;
    private const int MonsterId = 2;

    [BeforeTest]
    public void Setup()
    {
        // Minimal scene: root Control with ToastLog child.
        var root = new Control();
        var toast = new ToastLog();
        toast.Name = "ToastLog";
        root.AddChild(toast);
        _runner = ISceneRunner.Load(root, true);
    }

    /// <summary>
    /// Regression test for the infinite loop bug.
    ///
    /// Calls RecordTurn 10 times in a tight loop with 2 AttackEvents each (20 total events)
    /// without advancing frames between calls. The old bug would have spun forever once the
    /// internal list hit MaxToasts=5. Completion within the test timeout IS the assertion.
    /// </summary>
    [TestCase]
    public void RecordTurn_ManyEvents_NoInfiniteLoop()
    {
        var toast = _runner.FindChild("ToastLog") as ToastLog;
        AssertObject(toast).IsNotNull();
        toast!.SetPlayerId(PlayerId);

        var (state, _) = BuildMinimalState();

        // 10 calls × 2 events = 20 toast-worthy events spammed with no frame advance.
        // If the infinite loop bug is present, this never returns.
        for (int i = 0; i < 10; i++)
        {
            var result = BuildTurnResult(turnNumber: i + 1, eventCount: 2);
            toast.RecordTurn(result, state);
        }

        // Reaching here means no infinite loop. Count should be bounded by MaxToasts.
        AssertInt(toast.ToastCount).IsLessEqual(5);
    }

    /// <summary>
    /// Verifies the toast count stays at or below MaxToasts=5 across many turns,
    /// even with frame advances between calls (simulates real game usage).
    ///
    /// 20 calls × 2 events each = 40 toast-worthy events over 20 frames.
    /// Without overflow eviction, count would grow unbounded. With the bug, infinite loop.
    /// </summary>
    [TestCase]
    public async Task SpamToasts_NeverExceedsMaxToasts()
    {
        var toast = _runner.FindChild("ToastLog") as ToastLog;
        AssertObject(toast).IsNotNull();
        toast!.SetPlayerId(PlayerId);

        var (state, _) = BuildMinimalState();

        for (int i = 0; i < 20; i++)
        {
            var result = BuildTurnResult(turnNumber: i + 1, eventCount: 2);
            toast.RecordTurn(result, state);

            // Advance one frame so _Process runs between bursts — exercises the
            // fade/cleanup path alongside the overflow eviction path.
            await _runner.SimulateFrames(1);

            AssertInt(toast.ToastCount).IsLessEqual(5);
        }
    }

    /// <summary>
    /// Smoke test: process 60 frames without crashing.
    /// Exercises _Process() which handles fade and timer-based cleanup.
    /// </summary>
    [TestCase]
    public async Task ToastLog_ProcessFrames_NoCrash()
    {
        await _runner.SimulateFrames(60);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal GameState: player + one monster, small arena map.
    /// Enough for FormatEvent/GetEntityName to resolve names without null-reference.
    /// </summary>
    private static (GameState state, Entity monster) BuildMinimalState()
    {
        var player = new Entity(PlayerId, "Player", x: 1, y: 1, blocksMovement: true);
        player.Add(new Fighter(hp: 20));

        var monster = new Entity(MonsterId, "Goblin", x: 3, y: 3, blocksMovement: true);
        monster.Add(new Fighter(hp: 10));

        var map = GameMap.CreateArena(10, 10);
        var rng = new SeededRandom(1337);
        var state = new GameState(player, new List<Entity> { monster }, map, rng);

        return (state, monster);
    }

    /// <summary>
    /// Builds a TurnResult containing <paramref name="eventCount"/> AttackEvents from
    /// the player hitting the monster. All events produce a visible toast message.
    /// </summary>
    private static TurnResult BuildTurnResult(int turnNumber, int eventCount)
    {
        var events = new List<TurnEvent>();
        for (int i = 0; i < eventCount; i++)
        {
            events.Add(new AttackEvent
            {
                ActorId = PlayerId,
                TargetId = MonsterId,
                Hit = true,
                Damage = 3,
                IsCritical = false,
                IsBonusAttack = false,
                TargetKilled = false,
            });
        }

        return new TurnResult
        {
            TurnNumber = turnNumber,
            Events = events,
        };
    }
}
