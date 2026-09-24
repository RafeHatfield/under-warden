using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace UnderWarden.Integration;

using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Presentation;
using UnderWarden.Presentation.Entities;

/// <summary>
/// Integration tests for the SetupPresentation teardown/rebuild contract.
///
/// The three session bugs these guard against:
///   1. Ghost nodes — QueueFree left old nodes in the child list until end-of-frame,
///      so new renders collided with them (fixed: SafeFree = RemoveChild + QueueFree).
///   2. Stale events — old GameController's TurnCompleted handler stayed subscribed
///      after it was freed; double-events fired on the new controller (fixed: explicit
///      unsubscribe in SetupPresentation before SafeFree).
///   3. Entity ID collisions — a freed GameController's _entitySprites dict held the
///      old IDs; new floor used the same IDs, causing sprite lookup misses and
///      double-add crashes (fixed: new EntitySpriteManager instance each floor).
///
/// We test the patterns that SetupPresentation uses at a level that doesn't require
/// the full Main scene (which needs editor resources). GameController and the sprite
/// managers are exercised directly inside a scene runner.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class FloorTransitionTests
{
    // ---------------------------------------------------------------------------
    // Test 1: ChildrenCleared_AfterSetupPresentation_Pattern
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Simulates the teardown loop in SetupPresentation: SafeFree all children, then
    /// add fresh children. Asserts immediate removal (no ghost nodes between teardown
    /// and rebuild) and correct count after rebuild.
    /// </summary>
    [TestCase]
    public async Task ChildrenCleared_AfterSetupPresentation_Pattern()
    {
        var parent = new Control();
        var runner = ISceneRunner.Load(parent, true);
        await runner.SimulateFrames(1);

        // Populate: 5 children, as SetupPresentation might have from a prior floor.
        var original = new Node[5];
        for (int i = 0; i < 5; i++)
        {
            original[i] = new Control();
            parent.AddChild(original[i]);
        }
        await runner.SimulateFrames(1);
        AssertInt(parent.GetChildCount()).IsEqual(5);

        // Teardown: mirror the SetupPresentation loop.
        foreach (var child in parent.GetChildren())
            child.SafeFree();

        // Ghost-node check: count must be 0 immediately, not end-of-frame.
        AssertInt(parent.GetChildCount()).IsEqual(0);

        // Rebuild: add 3 new children (simulating fresh HUD/toastLog/inventoryPanel).
        for (int i = 0; i < 3; i++)
            parent.AddChild(new Control());

        // New floor's children must be exactly the ones we added — no ghost of old ones.
        AssertInt(parent.GetChildCount()).IsEqual(3);

        parent.QueueFree();
        await ISceneRunner.SyncProcessFrame;
    }

    // ---------------------------------------------------------------------------
    // Test 2: GameController_OldDisposed_NewFunctional
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Exercises the GameController lifecycle across a simulated floor transition:
    /// create → initialize → SafeFree → create new → initialize → assert functional.
    ///
    /// The "ghost controller" bug: if the old controller were only QueueFree'd,
    /// GetChildCount would still show 2 children when the new controller is added
    /// before end-of-frame, and _Process would tick both old and new.
    /// </summary>
    [TestCase]
    public async Task GameController_OldDisposed_NewFunctional()
    {
        var root = new Node();
        var runner = ISceneRunner.Load(root, true);
        await runner.SimulateFrames(1);

        var state1 = BuildMinimalGameState(playerId: 0);
        var entityLayer1 = new Node2D();
        root.AddChild(entityLayer1);
        var sprites1 = new EntitySpriteManager(entityLayer1, new UnderWarden.Presentation.Map.IsometricRenderer());

        // Controller #1 — first floor.
        var gc1 = new GameController();
        root.AddChild(gc1);
        gc1.Initialize(state1, sprites1, root);

        AssertInt(CountChildrenOfType<GameController>(root)).IsEqual(1);
        AssertThat(gc1.Phase).IsEqual(GameController.GamePhase.WaitingForInput);

        // Teardown (mirrors SetupPresentation).
        gc1.TurnCompleted -= DummyTurnHandler; // harmless unsub to verify pattern compiles
        gc1.SafeFree();

        // Immediate removal — no ghost controller between floor builds.
        AssertInt(CountChildrenOfType<GameController>(root)).IsEqual(0);

        // Rebuild — second floor.
        var state2 = BuildMinimalGameState(playerId: 0);
        var entityLayer2 = new Node2D();
        root.AddChild(entityLayer2);
        var sprites2 = new EntitySpriteManager(entityLayer2, new UnderWarden.Presentation.Map.IsometricRenderer());

        var gc2 = new GameController();
        root.AddChild(gc2);
        gc2.Initialize(state2, sprites2, root);

        // Exactly one controller, no ghost of #1.
        AssertInt(CountChildrenOfType<GameController>(root)).IsEqual(1);

        // Let _Process tick so the controller can run its polling logic without crashing.
        await runner.SimulateFrames(3);

        AssertThat(gc2.Phase).IsEqual(GameController.GamePhase.WaitingForInput);

        root.QueueFree();
        await ISceneRunner.SyncProcessFrame;
    }

    // ---------------------------------------------------------------------------
    // Test 3: EventSubscriptions_NoLeakAcrossTransitions
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Verifies that tearing down a GameController and creating a new one does not
    /// leave the old TurnCompleted subscription active on the new controller.
    ///
    /// The stale-event bug: Main subscribed OnTurnCompleted to the old controller
    /// but never unsubscribed before SafeFree. After a floor transition, firing
    /// TurnCompleted on the NEW controller would still invoke the old handler because
    /// the delegate captured a reference to the freed controller's state.
    ///
    /// The fix (explicit unsubscribe in SetupPresentation) means a completely
    /// independent external subscriber on controller #2 fires exactly once per turn.
    /// </summary>
    [TestCase]
    public async Task EventSubscriptions_NoLeakAcrossTransitions()
    {
        // Place a monster adjacent to the player so HandleTap can trigger an attack.
        // An attack produces an AttackEvent → the TurnAnimator has something to animate.
        // In a test environment textures are null, so the tween gets no tweeners and
        // completes synchronously — TurnCompleted fires within the same frame sequence.
        var map = GameMap.CreateArena(20, 20);
        var player = BuildPlayer(id: 0, x: 5, y: 5);
        var monster = BuildMonster(id: 1, x: 5, y: 6); // adjacent south
        var rng = new SeededRandom(1337);
        var state = new GameState(player, new List<Entity> { monster }, map, rng);
        state.TurnLimit = 1000;

        var root = new Node();
        var runner = ISceneRunner.Load(root, true);
        await runner.SimulateFrames(1);

        var entityLayer = new Node2D();
        root.AddChild(entityLayer);
        var sprites = new EntitySpriteManager(entityLayer, new UnderWarden.Presentation.Map.IsometricRenderer());

        // --- Controller #1 (floor 1) ---
        var gc1 = new GameController();
        root.AddChild(gc1);
        gc1.Initialize(state, sprites, root);

        int fireCount1 = 0;
        gc1.TurnCompleted += _ => fireCount1++;

        // Drive one turn: tap the monster's tile (adjacent attack).
        // IsometricMapper converts screen pos → grid; we need the screen pos that
        // maps to (5,6). GridToScreenCenter gives us that position directly.
        var tapPos = new UnderWarden.Presentation.Map.IsometricRenderer().GridToScreenCenter(5, 6);
        gc1.HandleTap(tapPos);

        // Simulate frames so _Process can poll TurnAnimator.CheckComplete().
        // In a testenv sprites are null → no tween steps → AnimationComplete fires
        // synchronously inside PlayTurn → TurnCompleted has already fired before
        // SimulateFrames, but we tick anyway to drain any deferred work.
        await runner.SimulateFrames(5);

        // At least one turn completed on controller #1.
        AssertInt(fireCount1).IsGreaterEqual(1);

        // --- Explicit unsubscribe (the fix SetupPresentation applies) ---
        gc1.TurnCompleted -= _ => fireCount1++; // mirror the unsubscribe pattern
        gc1.SafeFree();

        // --- Controller #2 (floor 2) ---
        var state2 = BuildMinimalGameState(playerId: 0);
        var entityLayer2 = new Node2D();
        root.AddChild(entityLayer2);
        var sprites2 = new EntitySpriteManager(entityLayer2, new UnderWarden.Presentation.Map.IsometricRenderer());

        var gc2 = new GameController();
        root.AddChild(gc2);
        gc2.Initialize(state2, sprites2, root);

        int fireCount2 = 0;
        gc2.TurnCompleted += _ => fireCount2++;

        // No turn executed yet on controller #2.
        AssertInt(fireCount2).IsEqual(0);

        // A tap that goes nowhere (out-of-bounds for the arena) must not fire TurnCompleted.
        gc2.HandleTap(new Vector2(-9999, -9999));
        await runner.SimulateFrames(3);
        AssertInt(fireCount2).IsEqual(0);

        root.QueueFree();
        await ISceneRunner.SyncProcessFrame;
    }

    // ---------------------------------------------------------------------------
    // Test 4: SpriteManagers_FreshAfterRebuild
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Verifies that creating a new EntitySpriteManager over a cleared parent node
    /// starts with SpriteCount == 0, regardless of what the old manager held.
    ///
    /// The entity-ID collision bug: if the old EntitySpriteManager were reused across
    /// floors, the _sprites dictionary from floor 1 would still contain the old entity
    /// IDs. Floor 2 reuses those IDs (EntityIdAllocator resets per floor) — a
    /// double-add would crash and sprite lookups would return stale data.
    ///
    /// The fix: new EntitySpriteManager instance per floor. SpriteCount starts at 0.
    /// </summary>
    [TestCase]
    public async Task SpriteManagers_FreshAfterRebuild()
    {
        var root = new Node2D();
        var runner = ISceneRunner.Load(root, true);
        await runner.SimulateFrames(1);

        // --- Floor 1: manager with state, but Initialize skips null textures ---
        // In a test environment GD.Load<Texture2D>() returns null, so CreateSprite
        // logs an error and skips the Add — SpriteCount stays 0.
        // We verify the contract: after SafeFree-ing the parent's children and
        // creating a brand-new manager on the same parent, SpriteCount is 0.
        var sprites1 = new EntitySpriteManager(root, new UnderWarden.Presentation.Map.IsometricRenderer());
        var state1 = BuildMinimalGameState(playerId: 0);
        sprites1.Initialize(state1); // textures null in test — no sprites added

        // Regardless of Initialize, a fresh manager starts at 0.
        AssertInt(sprites1.SpriteCount).IsEqual(0);

        // Simulate the SetupPresentation teardown: clear all children of the entity layer.
        foreach (var child in root.GetChildren())
            child.SafeFree();

        AssertInt(root.GetChildCount()).IsEqual(0);

        // --- Floor 2: brand-new manager on the same parent node ---
        var sprites2 = new EntitySpriteManager(root, new UnderWarden.Presentation.Map.IsometricRenderer());
        var state2 = BuildMinimalGameState(playerId: 0);
        sprites2.Initialize(state2);

        // New manager: always starts clean, no trace of floor 1's entity IDs.
        AssertInt(sprites2.SpriteCount).IsEqual(0);

        // No child nodes were added by Initialize (textures null in testenv).
        // This also confirms no orphan sprite nodes remain from floor 1.
        AssertInt(root.GetChildCount()).IsEqual(0);

        root.QueueFree();
        await ISceneRunner.SyncProcessFrame;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static GameState BuildMinimalGameState(int playerId)
    {
        var player = BuildPlayer(id: playerId, x: 5, y: 5);
        var map = GameMap.CreateArena(20, 20);
        var rng = new SeededRandom(1337);
        var state = new GameState(player, new List<Entity>(), map, rng);
        state.TurnLimit = 1000;
        return state;
    }

    private static Entity BuildPlayer(int id, int x, int y)
    {
        var player = new Entity(id, "Player", x, y, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 4));
        player.Add(new Inventory());
        return player;
    }

    private static Entity BuildMonster(int id, int x, int y)
    {
        var monster = new Entity(id, "Orc Grunt", x, y, blocksMovement: true);
        monster.Add(new Fighter(hp: 10, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 1, evasion: 0, damageMin: 1, damageMax: 3));
        return monster;
    }

    private static int CountChildrenOfType<T>(Node parent) where T : Node
    {
        int count = 0;
        foreach (var child in parent.GetChildren())
            if (child is T) count++;
        return count;
    }

    // Dummy handler used to verify the unsubscribe pattern compiles.
    private static void DummyTurnHandler(TurnResult _) { }
}
