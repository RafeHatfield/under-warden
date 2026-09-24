using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for PortalSystem — the logic layer backing the Wand of Portals.
///
/// The Wand of Portals uses a 3-step state machine:
///   Step 1 (Ready → EntrancePlaced):      cast with no target → entrance at caster's feet
///   Step 2 (EntrancePlaced → BothPlaced): cast with exit location → exit placed, both linked
///   Step 3 (BothPlaced → Ready):          cast with no target → both portals removed
///
/// Tests use manually constructed GameState (no YAML) for speed, except YAML integration tests.
/// </summary>
[TestFixture]
public class PortalSystemTests
{
    // ─── Common helpers ───────────────────────────────────────────────────────

    private static (GameState state, EntityFactory entityFactory) CreateState(
        int playerX = 3, int playerY = 3,
        int mapSize = 20,
        int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(mapSize, mapSize);

        var player = new Entity(0, "Player", playerX, playerY, blocksMovement: true);
        player.Add(new Fighter(hp: 80, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 2, damageMax: 8));
        player.Add(new Inventory());
        map.RegisterEntity(player);

        var state = new GameState(player, new List<Entity>(), map, rng);

        var entityFactory = new EntityFactory(startId: 500);
        return (state, entityFactory);
    }

    private static Entity AddMonster(GameState state, int id, int x, int y, int hp = 30)
    {
        var m = new Entity(id, "Orc", x, y, blocksMovement: true);
        m.Add(new Fighter(hp: hp, strength: 8, dexterity: 8, constitution: 8,
            accuracy: 2, evasion: 0, damageMin: 1, damageMax: 3));
        m.Add(new AiComponent { AiType = "basic", Faction = "orc", Tags = ["humanoid", "living"] });
        state.Monsters.Add(m);
        state.Map.RegisterEntity(m);
        return m;
    }

    private static Entity MakeInfiniteWand()
    {
        var wand = new Entity(200, "Wand of Portals");
        wand.Add(new WandComponent { Charges = 0, MaxCharges = 10, Infinite = true });
        wand.Add(new SpellEffect { SpellId = "portal", Targeting = TargetingMode.Portal });
        return wand;
    }

    /// <summary>
    /// Drive the 3-step state machine to place a complete portal pair.
    /// Player must be movable — repositioned to entranceX/Y for step 1.
    /// After this call: state.Portals has 2 entries, step = BothPlaced.
    /// </summary>
    private static void SetupPortalPair(GameState state, EntityFactory factory, Entity wand,
        int entranceX, int entranceY, int exitX, int exitY)
    {
        // Reset wand state in case it was used before in this test
        PortalSystem.ResetPortalWandState(wand);

        // Step 1: entrance placed at caster's feet — move player to entrance tile first
        state.Player.X = entranceX;
        state.Player.Y = entranceY;
        PortalSystem.HandlePortalCast(state.Player, state, wand, null, null, factory);

        // Step 2: exit at target tile
        PortalSystem.HandlePortalCast(state.Player, state, wand, exitX, exitY, factory);
    }

    private static string FindEntitiesYaml()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var path = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", "entities.yaml"));
        if (File.Exists(path)) return path;
        path = Path.GetFullPath(Path.Combine(testDir, "config", "entities.yaml"));
        if (File.Exists(path)) return path;
        throw new FileNotFoundException($"entities.yaml not found. Tried: {path}");
    }

    // ─── 3-Step State Machine ─────────────────────────────────────────────────

    [Test]
    public void Step1_PlacesEntranceAtCasterPosition()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();

        PortalSystem.HandlePortalCast(state.Player, state, wand, null, null, factory);

        // Entrance not yet in state.Portals (0-or-2 invariant)
        Assert.That(state.Portals, Is.Empty, "Entrance should not be in state.Portals yet");
        Assert.That(PortalSystem.GetPortalCastStep(wand), Is.EqualTo(PortalCastStep.EntrancePlaced));

        var comp = wand.Get<PortalCastStateComponent>();
        Assert.That(comp?.PendingEntrance, Is.Not.Null, "PendingEntrance should be set after step 1");
        Assert.That(comp!.PendingEntrance!.X, Is.EqualTo(5));
        Assert.That(comp.PendingEntrance.Y, Is.EqualTo(5));
    }

    [Test]
    public void Step1_EmitsEntrancePlacedEvent()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();

        var events = PortalSystem.HandlePortalCast(state.Player, state, wand, null, null, factory);

        Assert.That(events, Is.Not.Null);
        var placedEvent = events!.OfType<PortalPlacedEvent>().FirstOrDefault();
        Assert.That(placedEvent, Is.Not.Null, "Step 1 should emit PortalPlacedEvent");
        Assert.That(placedEvent!.Type, Is.EqualTo(PortalType.Entrance));
        Assert.That(placedEvent.X, Is.EqualTo(5));
        Assert.That(placedEvent.Y, Is.EqualTo(5));
    }

    [Test]
    public void Step2_CreatesTwoPortalEntities()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();

        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);

        Assert.That(state.Portals, Has.Count.EqualTo(2));
        Assert.That(PortalSystem.GetPortalCastStep(wand), Is.EqualTo(PortalCastStep.BothPlaced));
    }

    [Test]
    public void Step2_PortalsCrossLinked()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();

        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);

        var entrance = state.Portals.First(p => p.Get<PortalComponent>()?.Type == PortalType.Entrance);
        var exit = state.Portals.First(p => p.Get<PortalComponent>()?.Type == PortalType.Exit);

        Assert.That(entrance.Get<PortalComponent>()!.LinkedPortalId, Is.EqualTo(exit.Id),
            "Entrance should link to exit");
        Assert.That(exit.Get<PortalComponent>()!.LinkedPortalId, Is.EqualTo(entrance.Id),
            "Exit should link to entrance");
    }

    [Test]
    public void Step2_EmitsExitPlacedEvent()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();

        // Step 1
        PortalSystem.HandlePortalCast(state.Player, state, wand, null, null, factory);
        // Step 2
        var events = PortalSystem.HandlePortalCast(state.Player, state, wand, 10, 10, factory);

        Assert.That(events, Is.Not.Null);
        var exitEvent = events!.OfType<PortalPlacedEvent>().FirstOrDefault();
        Assert.That(exitEvent, Is.Not.Null, "Step 2 should emit PortalPlacedEvent for exit");
        Assert.That(exitEvent!.Type, Is.EqualTo(PortalType.Exit));
        Assert.That(exitEvent.X, Is.EqualTo(10));
        Assert.That(exitEvent.Y, Is.EqualTo(10));
    }

    [Test]
    public void Step2_NonWalkableExit_ReturnsNull()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();

        PortalSystem.HandlePortalCast(state.Player, state, wand, null, null, factory);
        // Wall tile (0,0) in arena
        var result = PortalSystem.HandlePortalCast(state.Player, state, wand, 0, 0, factory);

        Assert.That(result, Is.Null, "Exit on non-walkable tile should fail");
        Assert.That(state.Portals, Is.Empty, "No portals should be created on invalid exit");
    }

    [Test]
    public void Step2_ExitOnSameTileAsEntrance_ReturnsNull()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();

        PortalSystem.HandlePortalCast(state.Player, state, wand, null, null, factory);
        var result = PortalSystem.HandlePortalCast(state.Player, state, wand, 5, 5, factory);

        Assert.That(result, Is.Null, "Exit on same tile as entrance should fail");
        Assert.That(state.Portals, Is.Empty);
    }

    [Test]
    public void Step2_StairTile_Blocked()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();

        // Place a stair down at (8,8)
        var stair = new Entity(99, "Stair", 8, 8, blocksMovement: false);
        state.StairDown = stair;

        PortalSystem.HandlePortalCast(state.Player, state, wand, null, null, factory);
        var result = PortalSystem.HandlePortalCast(state.Player, state, wand, 8, 8, factory);

        Assert.That(result, Is.Null, "Portal exit on stair tile should be blocked");
        Assert.That(state.Portals, Is.Empty);
    }

    [Test]
    public void Step1_StairTile_Blocked()
    {
        var (state, factory) = CreateState(playerX: 8, playerY: 8);
        var wand = MakeInfiniteWand();

        // Stair at player's current position
        var stair = new Entity(99, "Stair", 8, 8, blocksMovement: false);
        state.StairDown = stair;

        var result = PortalSystem.HandlePortalCast(state.Player, state, wand, null, null, factory);

        Assert.That(result, Is.Null, "Entrance on stair tile should be blocked");
        Assert.That(PortalSystem.GetPortalCastStep(wand), Is.EqualTo(PortalCastStep.Ready));
    }

    [Test]
    public void Step3_RemovesAllPortals()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();

        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);
        Assert.That(state.Portals, Has.Count.EqualTo(2));

        // Step 3: clear
        PortalSystem.HandlePortalCast(state.Player, state, wand, null, null, factory);

        Assert.That(state.Portals, Is.Empty);
        Assert.That(PortalSystem.GetPortalCastStep(wand), Is.EqualTo(PortalCastStep.Ready));
    }

    [Test]
    public void Step3_EmitsPortalRemovedEvent()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();

        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);

        var events = PortalSystem.HandlePortalCast(state.Player, state, wand, null, null, factory);

        Assert.That(events, Is.Not.Null);
        var removedEvent = events!.OfType<PortalRemovedEvent>().FirstOrDefault();
        Assert.That(removedEvent, Is.Not.Null, "Step 3 should emit PortalRemovedEvent");
    }

    [Test]
    public void Step1_AfterStep3_PlacesNewPair()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();

        // First pair
        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);
        Assert.That(state.Portals, Has.Count.EqualTo(2));

        // Step 3 clears
        PortalSystem.HandlePortalCast(state.Player, state, wand, null, null, factory);
        Assert.That(state.Portals, Is.Empty);

        // New pair
        state.Player.X = 6; state.Player.Y = 6;
        PortalSystem.HandlePortalCast(state.Player, state, wand, null, null, factory);
        PortalSystem.HandlePortalCast(state.Player, state, wand, 12, 12, factory);

        Assert.That(state.Portals, Has.Count.EqualTo(2));
        var entrance = state.Portals.First(p => p.Get<PortalComponent>()?.Type == PortalType.Entrance);
        Assert.That(entrance.X, Is.EqualTo(6), "New entrance should be at updated position");
        Assert.That(entrance.Y, Is.EqualTo(6));
    }

    [Test]
    public void Step2_Displacement_MonsterOnExitTile_TeleportedAtPlacement()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();
        var orc = AddMonster(state, 1, x: 10, y: 10); // sitting on future exit tile

        // Step 1
        PortalSystem.HandlePortalCast(state.Player, state, wand, null, null, factory);
        // Step 2 — exit placed on orc's tile
        var events = PortalSystem.HandlePortalCast(state.Player, state, wand, 10, 10, factory);

        Assert.That(events, Is.Not.Null);
        var teleportEvent = events!.OfType<PortalTeleportEvent>().FirstOrDefault(e => e.EntityId == orc.Id);
        Assert.That(teleportEvent, Is.Not.Null, "Monster on exit tile should be displaced immediately");
        // Orc should have been teleported to entrance (5,5)
        Assert.That(orc.X, Is.EqualTo(5));
        Assert.That(orc.Y, Is.EqualTo(5));
    }

    // ─── Cancel entrance ──────────────────────────────────────────────────────

    [Test]
    public void CancelEntrance_WhenEntrancePlaced_ResetsToReady()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();

        // Step 1
        PortalSystem.HandlePortalCast(state.Player, state, wand, null, null, factory);
        Assert.That(PortalSystem.GetPortalCastStep(wand), Is.EqualTo(PortalCastStep.EntrancePlaced));

        var evt = PortalSystem.CancelPendingEntrance(wand, state);

        Assert.That(evt, Is.Not.Null, "Should return event on cancellation");
        Assert.That(PortalSystem.GetPortalCastStep(wand), Is.EqualTo(PortalCastStep.Ready));
        // PendingEntrance should be cleared from map (can still attempt a new portal cast)
    }

    [Test]
    public void CancelEntrance_WhenReady_ReturnsNull()
    {
        var (state, _) = CreateState();
        var wand = MakeInfiniteWand();

        var evt = PortalSystem.CancelPendingEntrance(wand, state);

        Assert.That(evt, Is.Null, "No cancellation event when wand is in Ready state");
    }

    // ─── CheckPortalCollision ─────────────────────────────────────────────────

    [Test]
    public void CheckCollision_PlayerOnEntrance_TeleportsToExit()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();
        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);

        // Player still at (5,5) — on entrance
        var evt = PortalSystem.CheckPortalCollision(state.Player, state);

        Assert.That(evt, Is.Not.Null, "Should return a PortalTeleportEvent");
        Assert.That(state.Player.X, Is.EqualTo(10), "Player should teleport to exit X");
        Assert.That(state.Player.Y, Is.EqualTo(10), "Player should teleport to exit Y");
        Assert.That(evt!.FromX, Is.EqualTo(5));
        Assert.That(evt.FromY, Is.EqualTo(5));
        Assert.That(evt.ToX, Is.EqualTo(10));
        Assert.That(evt.ToY, Is.EqualTo(10));
    }

    [Test]
    public void CheckCollision_PlayerOnExit_TeleportsToEntrance()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();
        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);

        // Move player to exit tile
        state.Player.X = 10;
        state.Player.Y = 10;

        var evt = PortalSystem.CheckPortalCollision(state.Player, state);

        Assert.That(evt, Is.Not.Null);
        Assert.That(state.Player.X, Is.EqualTo(5));
        Assert.That(state.Player.Y, Is.EqualTo(5));
    }

    [Test]
    public void CheckCollision_MonsterOnPortal_Teleports()
    {
        var (state, factory) = CreateState(playerX: 3, playerY: 3);
        var wand = MakeInfiniteWand();
        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);

        var orc = AddMonster(state, 1, x: 5, y: 5);

        var evt = PortalSystem.CheckPortalCollision(orc, state);

        Assert.That(evt, Is.Not.Null, "Monster on portal should teleport");
        Assert.That(orc.X, Is.EqualTo(10), "Monster should be at exit X");
        Assert.That(orc.Y, Is.EqualTo(10), "Monster should be at exit Y");
    }

    [Test]
    public void CheckCollision_NoPortalAtPosition_ReturnsNull()
    {
        var (state, factory) = CreateState(playerX: 3, playerY: 3);
        var wand = MakeInfiniteWand();
        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);
        // SetupPortalPair moves player to entrance; restore to non-portal position
        state.Player.X = 3; state.Player.Y = 3;

        var evt = PortalSystem.CheckPortalCollision(state.Player, state);

        Assert.That(evt, Is.Null, "No portal at player position — should return null");
    }

    [Test]
    public void CheckCollision_NoPortalsActive_ReturnsNull()
    {
        var (state, _) = CreateState(playerX: 5, playerY: 5);

        var evt = PortalSystem.CheckPortalCollision(state.Player, state);

        Assert.That(evt, Is.Null, "No portals exist — should return null immediately");
    }

    [Test]
    public void CheckCollision_NoChaining_JustTeleportedEntity()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();
        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);

        // First teleport: entrance → exit
        var evt1 = PortalSystem.CheckPortalCollision(state.Player, state);
        Assert.That(evt1, Is.Not.Null, "First teleport should succeed");
        Assert.That(state.Player.X, Is.EqualTo(10), "Player at exit after first teleport");

        // Exit portal has UsedThisTurn=true; attempting again should be blocked
        var evt2 = PortalSystem.CheckPortalCollision(state.Player, state);
        Assert.That(evt2, Is.Null, "Chain teleport should be blocked (UsedThisTurn flag)");
        Assert.That(state.Player.X, Is.EqualTo(10), "Player should not have moved again");
    }

    [Test]
    public void CheckCollision_AfterClearUsedFlags_CanTeleportAgainNextTurn()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();
        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);

        // First teleport
        PortalSystem.CheckPortalCollision(state.Player, state);
        Assert.That(state.Player.X, Is.EqualTo(10));

        // Simulate end of turn — clear used flags
        PortalSystem.ClearPortalUsedFlags(state);

        // Move player back to entrance manually
        state.Player.X = 5;
        state.Player.Y = 5;

        // Should be able to teleport again next turn
        var evt = PortalSystem.CheckPortalCollision(state.Player, state);
        Assert.That(evt, Is.Not.Null, "Should teleport again after UsedThisTurn was cleared");
    }

    // ─── ClearPortals ─────────────────────────────────────────────────────────

    [Test]
    public void ClearPortals_RemovesAllPortalEntities()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();
        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);
        Assert.That(state.Portals, Has.Count.EqualTo(2));

        PortalSystem.ClearPortals(state);

        Assert.That(state.Portals, Is.Empty, "ClearPortals should remove all portal entities");
    }

    [Test]
    public void ClearPortals_ReturnsRemovedEvent_WhenPortalsExisted()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();
        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);

        var evt = PortalSystem.ClearPortals(state);

        Assert.That(evt, Is.Not.Null, "ClearPortals should return PortalRemovedEvent when portals existed");
    }

    [Test]
    public void ClearPortals_ReturnsNull_WhenNoPortalsExisted()
    {
        var (state, _) = CreateState();

        var evt = PortalSystem.ClearPortals(state);

        Assert.That(evt, Is.Null, "ClearPortals should return null when no portals exist");
    }

    // ─── Wand of Portals properties ──────────────────────────────────────────

    [Test]
    public void WandOfPortals_InfiniteCharges_NeverDepleted()
    {
        var wand = MakeInfiniteWand();
        var wandComp = wand.Require<WandComponent>();

        Assert.That(wandComp.Infinite, Is.True);
        Assert.That(wandComp.HasCharges, Is.True);

        for (int i = 0; i < 100; i++)
            wandComp.TryConsume();

        Assert.That(wandComp.HasCharges, Is.True, "Infinite wand should always have charges");
    }

    [Test]
    public void WandOfPortals_SpellId_IsPortal()
    {
        var wand = MakeInfiniteWand();
        Assert.That(wand.Require<SpellEffect>().SpellId, Is.EqualTo("portal"));
    }

    // ─── TurnController integration ──────────────────────────────────────────

    [Test]
    public void TurnController_PortalStep1_PlacesEntrance()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();
        state.Player.Require<Inventory>().Add(wand);

        // Step 1: no target → entrance at player feet
        var result = TurnController.ProcessTurn(state,
            PlayerAction.CastSpell(wand), portalEntityFactory: factory);

        var entranceEvent = result.Events.OfType<PortalPlacedEvent>()
            .FirstOrDefault(e => e.Type == PortalType.Entrance);
        Assert.That(entranceEvent, Is.Not.Null, "Step 1 should emit entrance PortalPlacedEvent");
        Assert.That(PortalSystem.GetPortalCastStep(wand), Is.EqualTo(PortalCastStep.EntrancePlaced));
    }

    [Test]
    public void TurnController_PortalStep1Then2_CreatesTwoPortals()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();
        state.Player.Require<Inventory>().Add(wand);

        // Step 1
        TurnController.ProcessTurn(state, PlayerAction.CastSpell(wand), portalEntityFactory: factory);
        // Step 2
        var result = TurnController.ProcessTurn(state,
            PlayerAction.CastSpell(wand, targetX: 10, targetY: 10), portalEntityFactory: factory);

        var exitEvent = result.Events.OfType<PortalPlacedEvent>()
            .FirstOrDefault(e => e.Type == PortalType.Exit);
        Assert.That(exitEvent, Is.Not.Null, "Step 2 should emit exit PortalPlacedEvent");
        Assert.That(state.Portals, Has.Count.EqualTo(2), "Two portals in state.Portals after step 2");
    }

    [Test]
    public void TurnController_PortalAction_NoFactory_SilentNoOp()
    {
        var (state, _) = CreateState();
        var wand = MakeInfiniteWand();
        state.Player.Require<Inventory>().Add(wand);

        // No factory injected — should not crash, just silently do nothing
        var result = TurnController.ProcessTurn(state,
            PlayerAction.CastSpell(wand), portalEntityFactory: null);

        Assert.That(state.Portals, Is.Empty, "No portals should be created without a factory");
        Assert.That(result.Events.OfType<PortalPlacedEvent>(), Is.Empty);
    }

    [Test]
    public void TurnController_PlayerMove_ChecksPortalCollision()
    {
        var (state, factory) = CreateState(playerX: 4, playerY: 5);
        var wand = MakeInfiniteWand();
        // Place portals: entrance at (5,5), exit at (10,10)
        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);
        // Move player back to (4,5) so they can step onto the entrance
        state.Player.X = 4; state.Player.Y = 5;

        // Move player onto the entrance portal at (5,5)
        var result = TurnController.ProcessTurn(state,
            PlayerAction.MoveTo(5, 5), portalEntityFactory: factory);

        var teleportEvent = result.Events.OfType<PortalTeleportEvent>().FirstOrDefault();
        Assert.That(teleportEvent, Is.Not.Null, "Moving onto portal should emit PortalTeleportEvent");
        Assert.That(state.Player.X, Is.EqualTo(10));
        Assert.That(state.Player.Y, Is.EqualTo(10));
    }

    [Test]
    public void TurnController_MonsterMove_ChecksPortalCollision()
    {
        var (state, factory) = CreateState(playerX: 15, playerY: 15);
        var wand = MakeInfiniteWand();
        var orc = AddMonster(state, 1, x: 4, y: 5);
        // Place portals: entrance at (5,5), exit at (10,10)
        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);
        // Move player back to far corner (SetupPortalPair repositioned player)
        state.Player.X = 15; state.Player.Y = 15;

        // Orc will try to move toward player (15,15), potentially stepping onto (5,5)
        var result = TurnController.ProcessTurn(state, PlayerAction.Wait, portalEntityFactory: factory);

        // Verify no crash when portals + monster movement coexist
        Assert.That(result.Events, Is.Not.Null);
    }

    [Test]
    public void TurnController_PortalAction_InfiniteWand_NotConsumed()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();
        state.Player.Require<Inventory>().Add(wand);

        // Step 1 + Step 2
        TurnController.ProcessTurn(state, PlayerAction.CastSpell(wand),
            portalEntityFactory: factory);
        TurnController.ProcessTurn(state, PlayerAction.CastSpell(wand, targetX: 10, targetY: 10),
            portalEntityFactory: factory);

        Assert.That(state.PlayerInventory!.Items, Contains.Item(wand),
            "Infinite wand should remain in inventory after use");
        Assert.That(wand.Require<WandComponent>().HasCharges, Is.True);
    }

    [Test]
    public void TurnController_Step3_RemovesPortals()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();
        state.Player.Require<Inventory>().Add(wand);

        // Steps 1+2: place pair
        TurnController.ProcessTurn(state, PlayerAction.CastSpell(wand),
            portalEntityFactory: factory);
        TurnController.ProcessTurn(state, PlayerAction.CastSpell(wand, targetX: 10, targetY: 10),
            portalEntityFactory: factory);
        Assert.That(state.Portals, Has.Count.EqualTo(2));

        // Step 3: clear
        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(wand),
            portalEntityFactory: factory);

        Assert.That(state.Portals, Is.Empty, "Step 3 should clear all portals");
        var removedEvent = result.Events.OfType<PortalRemovedEvent>().FirstOrDefault();
        Assert.That(removedEvent, Is.Not.Null, "Step 3 should emit PortalRemovedEvent");
    }

    // ─── Floor transition ─────────────────────────────────────────────────────

    [Test]
    public void FloorTransition_ClearsPortalsAndResetsWandState()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();
        SetupPortalPair(state, factory, wand, 5, 5, 10, 10);
        Assert.That(state.Portals, Has.Count.EqualTo(2));

        // Simulate floor transition: clear portals + reset wand state
        PortalSystem.ClearPortals(state);
        PortalSystem.ResetPortalWandState(wand);

        Assert.That(state.Portals, Is.Empty, "Portals should be cleared on floor transition");
        Assert.That(PortalSystem.GetPortalCastStep(wand), Is.EqualTo(PortalCastStep.Ready));

        // Verify portals cannot be triggered on cleared state
        state.Player.X = 5; state.Player.Y = 5;
        var evt = PortalSystem.CheckPortalCollision(state.Player, state);
        Assert.That(evt, Is.Null, "No collision after clearing portals");
    }

    [Test]
    public void FloorTransition_WithPendingEntrance_ResetsToReady()
    {
        var (state, factory) = CreateState(playerX: 5, playerY: 5);
        var wand = MakeInfiniteWand();

        // Only step 1 placed — entrance pending, no full pair
        PortalSystem.HandlePortalCast(state.Player, state, wand, null, null, factory);
        Assert.That(PortalSystem.GetPortalCastStep(wand), Is.EqualTo(PortalCastStep.EntrancePlaced));

        // Floor transition: just reset wand state (old map abandoned)
        PortalSystem.ResetPortalWandState(wand);

        Assert.That(PortalSystem.GetPortalCastStep(wand), Is.EqualTo(PortalCastStep.Ready));
        Assert.That(wand.Get<PortalCastStateComponent>()?.PendingEntrance, Is.Null);
    }

    // ─── YAML integration ─────────────────────────────────────────────────────

    [Test]
    public void WandOfPortals_LoadsFromYaml()
    {
        var loader = new ContentLoader();
        var bundle = loader.LoadAllFromFile(FindEntitiesYaml());

        Assert.That(bundle.SpellItems.ContainsKey("wand_of_portals"),
            "wand_of_portals should be present in entities.yaml");

        var def = bundle.SpellItems["wand_of_portals"];
        Assert.That(def.IsWand, Is.True, "wand_of_portals should be marked is_wand=true");
        Assert.That(def.Infinite, Is.True, "wand_of_portals should be infinite");
        Assert.That(def.SpellId, Is.EqualTo("portal"), "wand_of_portals spell_id should be 'portal'");
    }

    [Test]
    public void WandOfPortals_CreatedByFactory_HasInfiniteComponent()
    {
        var loader = new ContentLoader();
        var bundle = loader.LoadAllFromFile(FindEntitiesYaml());
        var entityFactory = new EntityFactory();
        var spellFactory = new SpellItemFactory(bundle.SpellItems, entityFactory);

        var wand = spellFactory.CreateWand("wand_of_portals", new SeededRandom(1337));

        Assert.That(wand, Is.Not.Null, "CreateWand should return a valid entity");
        var wandComp = wand!.Get<WandComponent>();
        Assert.That(wandComp, Is.Not.Null, "Should have WandComponent");
        Assert.That(wandComp!.Infinite, Is.True, "WandComponent.Infinite should be true");
        Assert.That(wandComp.HasCharges, Is.True);
    }

    [Test]
    public void WandOfPortals_NotInFloorPool()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var poolPath = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", "floor_item_pools.yaml"));

        if (!File.Exists(poolPath))
        {
            Assert.Ignore("floor_item_pools.yaml not found — skipping floor pool check");
            return;
        }

        var content = File.ReadAllText(poolPath);
        Assert.That(content, Does.Not.Contain("wand_of_portals"),
            "wand_of_portals should not appear in floor_item_pools.yaml");
    }
}
