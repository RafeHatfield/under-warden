using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Logic.Features;

/// <summary>
/// Tests for chest, signpost, and mural bump-interaction via TurnController.
///
/// All tests use a small arena map with player adjacent to the feature being tested.
/// Deterministic seed 1337 used throughout.
/// </summary>
[TestFixture]
public class FeatureInteractionTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a minimal state with no monsters. Player at (3,5).
    /// Feature will be placed at (4,5) — directly to the player's right.
    /// </summary>
    private static (GameState state, Entity feature) CreateStateWithFeature(
        Entity feature, int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4) { CanOpenDoors = true });
        map.RegisterEntity(player);

        // Feature is at (4,5), adjacent right of player
        feature.X = 4;
        feature.Y = 5;
        map.RegisterEntity(feature);

        var state = new GameState(player, new List<Entity>(), map, rng);

        // Add feature to state.Features
        state.Features.Add(feature);

        return (state, feature);
    }

    /// <summary>
    /// Move action targeting (4,5) — the tile directly right of player.
    /// </summary>
    private static PlayerAction MoveRight() =>
        PlayerAction.MoveTo(4, 5);

    // ─────────────────────────────────────────────────────────────────────────
    // Chest tests
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void BumpClosedChest_Opens_EmitsChestOpenedEvent()
    {
        var ids = new EntityIdAllocator(startFrom: 10);
        var chest = new Entity(ids.Next(), "Chest", 0, 0, blocksMovement: true);
        chest.Add(new ChestComponent());

        var (state, _) = CreateStateWithFeature(chest);
        int turnBefore = state.TurnCount;

        var result = TurnController.ProcessTurn(state, MoveRight());

        var openEvent = result.Events.OfType<ChestOpenedEvent>().FirstOrDefault();
        Assert.That(openEvent, Is.Not.Null, "Should emit ChestOpenedEvent");
        Assert.That(openEvent!.X, Is.EqualTo(4));
        Assert.That(openEvent.Y, Is.EqualTo(5));
    }

    [Test]
    public void BumpClosedChest_SetIsOpenTrue()
    {
        var ids = new EntityIdAllocator(startFrom: 10);
        var chest = new Entity(ids.Next(), "Chest", 0, 0, blocksMovement: true);
        var chestComp = new ChestComponent();
        chest.Add(chestComp);

        var (state, _) = CreateStateWithFeature(chest);

        TurnController.ProcessTurn(state, MoveRight());

        Assert.That(chestComp.IsOpen, Is.True, "Chest should be open after bump");
    }

    [Test]
    public void BumpClosedChest_FirstTap_LootStaysOnChestTile()
    {
        // Two-step UX: first bump opens the chest, items appear on the chest tile (not auto-collected).
        var ids = new EntityIdAllocator(startFrom: 10);
        var chest = new Entity(ids.Next(), "Chest", 0, 0, blocksMovement: true);
        chest.Add(new ChestComponent());

        var item1 = new Entity(ids.Next(), "Potion", 0, 0, false);
        var item2 = new Entity(ids.Next(), "Scroll", 0, 0, false);
        chest.Add(new ChestLootStash(new List<Entity> { item1, item2 }));

        var (state, _) = CreateStateWithFeature(chest);
        state.Player.Add(new Inventory());

        TurnController.ProcessTurn(state, MoveRight()); // first tap — opens chest

        // Items should be on the chest tile (4,5), not yet in inventory
        Assert.That(state.FloorItems.Count, Is.EqualTo(2), "Items should be on the chest tile after open");
        Assert.That(state.FloorItems.All(i => i.X == 4 && i.Y == 5), Is.True,
            "Items should be at the chest tile position");
        var inventory = state.Player.Get<Inventory>();
        Assert.That(inventory!.Items.Count, Is.EqualTo(0), "No items auto-collected on first tap");
    }

    [Test]
    public void BumpOpenChest_SecondTap_LootGoesToPlayerInventory()
    {
        // Two-step UX: second bump on an open chest collects loot into inventory.
        var ids = new EntityIdAllocator(startFrom: 10);
        var chest = new Entity(ids.Next(), "Chest", 0, 0, blocksMovement: true);
        var chestComp = new ChestComponent { IsOpen = true }; // chest already open
        chest.Add(chestComp);

        var (state, _) = CreateStateWithFeature(chest);
        state.Player.Add(new Inventory());

        // Place items on the chest tile (simulating what happens after first tap)
        var item1 = new Entity(ids.Next(), "Potion", 4, 5, false);
        var item2 = new Entity(ids.Next(), "Scroll", 4, 5, false);
        state.FloorItems.Add(item1);
        state.FloorItems.Add(item2);
        state.Map.RegisterEntity(item1);
        state.Map.RegisterEntity(item2);

        TurnController.ProcessTurn(state, MoveRight()); // second tap — collect loot

        var inventory = state.Player.Get<Inventory>();
        Assert.That(inventory!.Items.Count, Is.EqualTo(2), "Both loot items should be in inventory after second tap");
        Assert.That(state.FloorItems.Count, Is.EqualTo(0), "No items should remain on floor after collection");
        Assert.That(chestComp.IsLooted, Is.True, "Chest should be marked looted");
    }

    [Test]
    public void BumpOpenChest_SecondTap_EmitsChestLootedEvent()
    {
        // Two-step UX: second tap emits ChestLootedEvent (presentation swaps sprite to empty).
        var ids = new EntityIdAllocator(startFrom: 10);
        var chest = new Entity(ids.Next(), "Chest", 0, 0, blocksMovement: true);
        chest.Add(new ChestComponent { IsOpen = true });

        var (state, _) = CreateStateWithFeature(chest);

        var result = TurnController.ProcessTurn(state, MoveRight());

        var lootEvt = result.Events.OfType<ChestLootedEvent>().FirstOrDefault();
        Assert.That(lootEvt, Is.Not.Null, "Should emit ChestLootedEvent on second tap");
        Assert.That(lootEvt!.X, Is.EqualTo(4));
        Assert.That(lootEvt.Y, Is.EqualTo(5));
    }

    [Test]
    public void TwoStepChest_FullFlow_OpenThenCollect()
    {
        // Full two-step flow: bump 1 opens, bump 2 collects.
        var ids = new EntityIdAllocator(startFrom: 10);
        var chest = new Entity(ids.Next(), "Chest", 0, 0, blocksMovement: true);
        chest.Add(new ChestComponent());

        var item1 = new Entity(ids.Next(), "Potion", 0, 0, false);
        var item2 = new Entity(ids.Next(), "Scroll", 0, 0, false);
        chest.Add(new ChestLootStash(new List<Entity> { item1, item2 }));

        var (state, _) = CreateStateWithFeature(chest);
        state.Player.Add(new Inventory());

        // Bump 1: open
        var result1 = TurnController.ProcessTurn(state, MoveRight());
        Assert.That(result1.Events.OfType<ChestOpenedEvent>().Any(), Is.True, "Bump 1 should emit ChestOpenedEvent");
        Assert.That(result1.Events.OfType<ChestLootedEvent>().Any(), Is.False, "Bump 1 should not emit ChestLootedEvent");
        Assert.That(state.FloorItems.Count, Is.EqualTo(2), "Items on chest tile after bump 1");

        // Bump 2: collect
        var result2 = TurnController.ProcessTurn(state, MoveRight());
        Assert.That(result2.Events.OfType<ChestLootedEvent>().Any(), Is.True, "Bump 2 should emit ChestLootedEvent");
        var inventory = state.Player.Get<Inventory>();
        Assert.That(inventory!.Items.Count, Is.EqualTo(2), "Both items in inventory after bump 2");
        Assert.That(state.FloorItems.Count, Is.EqualTo(0), "No floor items after bump 2");
    }

    [Test]
    public void BumpClosedChest_EmitsDroppedItemIds()
    {
        var ids = new EntityIdAllocator(startFrom: 10);
        var chest = new Entity(ids.Next(), "Chest", 0, 0, blocksMovement: true);
        chest.Add(new ChestComponent());

        var item1 = new Entity(ids.Next(), "Potion", 0, 0, false);
        chest.Add(new ChestLootStash(new List<Entity> { item1 }));

        var (state, _) = CreateStateWithFeature(chest);

        var result = TurnController.ProcessTurn(state, MoveRight());

        var openEvent = result.Events.OfType<ChestOpenedEvent>().First();
        Assert.That(openEvent.DroppedItemIds, Contains.Item(item1.Id),
            "ChestOpenedEvent should list the dropped item ID");
    }

    [Test]
    public void BumpClosedChest_CostsTurn_MonstersAct()
    {
        // Arrange: closed chest with a nearby monster
        var ids = new EntityIdAllocator(startFrom: 10);
        var chest = new Entity(ids.Next(), "Chest", 0, 0, blocksMovement: true);
        chest.Add(new ChestComponent());

        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4) { CanOpenDoors = true });
        map.RegisterEntity(player);

        chest.X = 4; chest.Y = 5;
        map.RegisterEntity(chest);

        // Monster far from player (won't attack) — placed to confirm monster turns run
        var monster = new Entity(ids.Next(), "Orc", 9, 9, blocksMovement: true);
        monster.Add(new Fighter(hp: 28, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        map.RegisterEntity(monster);

        var state = new GameState(player, new List<Entity> { monster }, map, rng);
        state.Features.Add(chest);

        int turnBefore = state.TurnCount;

        TurnController.ProcessTurn(state, MoveRight());

        // Turn should advance (chest opening costs a turn)
        Assert.That(state.TurnCount, Is.EqualTo(turnBefore + 1),
            "Opening a chest should advance TurnCount");
    }

    [Test]
    public void BumpLootedChest_NothingHappens_NoChestEvent()
    {
        // A fully looted chest (IsOpen=true, IsLooted=true) — bumping it does nothing and is a free action.
        var ids = new EntityIdAllocator(startFrom: 10);
        var chest = new Entity(ids.Next(), "Chest", 0, 0, blocksMovement: true);
        chest.Add(new ChestComponent { IsOpen = true, IsLooted = true }); // Fully looted

        var (state, _) = CreateStateWithFeature(chest);
        int turnBefore = state.TurnCount;

        var result = TurnController.ProcessTurn(state, MoveRight());

        // No chest events for bumping a looted chest
        Assert.That(result.Events.OfType<ChestOpenedEvent>().Any(), Is.False, "No ChestOpenedEvent for looted chest");
        Assert.That(result.Events.OfType<ChestLootedEvent>().Any(), Is.False, "No ChestLootedEvent for looted chest");

        // No movement (looted chest still blocks)
        Assert.That(state.Player.X, Is.EqualTo(3), "Player should not move into looted chest");
        Assert.That(state.Player.Y, Is.EqualTo(5));

        // Free action — no turn consumed
        Assert.That(state.TurnCount, Is.EqualTo(turnBefore), "Bumping a looted chest is a free action");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Signpost tests
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void BumpSignpost_EmitsSignpostReadEvent()
    {
        var ids = new EntityIdAllocator(startFrom: 10);
        var sign = new Entity(ids.Next(), "Signpost", 0, 0, blocksMovement: true);
        sign.Add(new SignpostComponent { Message = "Beware!", SignType = "warning" });

        var (state, _) = CreateStateWithFeature(sign);

        var result = TurnController.ProcessTurn(state, MoveRight());

        var readEvent = result.Events.OfType<SignpostReadEvent>().FirstOrDefault();
        Assert.That(readEvent, Is.Not.Null, "Should emit SignpostReadEvent");
        Assert.That(readEvent!.Message, Is.EqualTo("Beware!"));
        Assert.That(readEvent.SignType, Is.EqualTo("warning"));
        Assert.That(readEvent.X, Is.EqualTo(4));
        Assert.That(readEvent.Y, Is.EqualTo(5));
    }

    [Test]
    public void BumpSignpost_SetsHasBeenReadTrue()
    {
        var ids = new EntityIdAllocator(startFrom: 10);
        var sign = new Entity(ids.Next(), "Signpost", 0, 0, blocksMovement: true);
        var signComp = new SignpostComponent { Message = "Hello", SignType = "lore" };
        sign.Add(signComp);

        var (state, _) = CreateStateWithFeature(sign);

        TurnController.ProcessTurn(state, MoveRight());

        Assert.That(signComp.HasBeenRead, Is.True, "HasBeenRead should be true after reading");
    }

    [Test]
    public void BumpSignpost_FreeAction_TurnCountUnchanged()
    {
        var ids = new EntityIdAllocator(startFrom: 10);
        var sign = new Entity(ids.Next(), "Signpost", 0, 0, blocksMovement: true);
        sign.Add(new SignpostComponent { Message = "Free read", SignType = "hint" });

        var (state, _) = CreateStateWithFeature(sign);
        int turnBefore = state.TurnCount;

        TurnController.ProcessTurn(state, MoveRight());

        Assert.That(state.TurnCount, Is.EqualTo(turnBefore),
            "Reading a sign is a free action — TurnCount must not advance");
    }

    [Test]
    public void BumpSignpost_FreeAction_PlayerDoesNotMove()
    {
        var ids = new EntityIdAllocator(startFrom: 10);
        var sign = new Entity(ids.Next(), "Signpost", 0, 0, blocksMovement: true);
        sign.Add(new SignpostComponent { Message = "Stay here", SignType = "directional" });

        var (state, feature) = CreateStateWithFeature(sign);
        var player = state.Player;
        int playerXBefore = player.X;
        int playerYBefore = player.Y;

        TurnController.ProcessTurn(state, MoveRight());

        Assert.That(player.X, Is.EqualTo(playerXBefore), "Player X should not change");
        Assert.That(player.Y, Is.EqualTo(playerYBefore), "Player Y should not change");
    }

    [Test]
    public void BumpSignpost_FreeAction_MonsterDoesNotAct()
    {
        // Arrange: monster adjacent to player — if monster acts it would try to attack
        var ids = new EntityIdAllocator(startFrom: 10);
        var sign = new Entity(ids.Next(), "Signpost", 0, 0, blocksMovement: true);
        sign.Add(new SignpostComponent { Message = "Safe", SignType = "lore" });

        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4) { CanOpenDoors = true });
        map.RegisterEntity(player);

        sign.X = 4; sign.Y = 5;
        map.RegisterEntity(sign);

        // Monster adjacent to player (will attack if its turn runs)
        var monster = new Entity(ids.Next(), "Orc", 2, 5, blocksMovement: true);
        monster.Add(new Fighter(hp: 28, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        map.RegisterEntity(monster);

        var state = new GameState(player, new List<Entity> { monster }, map, rng);
        state.Features.Add(sign);

        var result = TurnController.ProcessTurn(state, MoveRight());

        // Monster should NOT have attacked (free action skips monster turns)
        var monsterAttacks = result.Events.OfType<AttackEvent>()
            .Where(e => e.ActorId == monster.Id)
            .ToList();
        Assert.That(monsterAttacks, Is.Empty,
            "Monster should not act during a free-action sign read");
    }

    [Test]
    public void BumpSignpost_ReadAgain_StillEmitsEvent()
    {
        // Re-reading a sign is allowed — same message each time
        var ids = new EntityIdAllocator(startFrom: 10);
        var sign = new Entity(ids.Next(), "Signpost", 0, 0, blocksMovement: true);
        sign.Add(new SignpostComponent { Message = "Old lore", SignType = "lore" });

        var (state, _) = CreateStateWithFeature(sign);

        TurnController.ProcessTurn(state, MoveRight()); // first read
        var result2 = TurnController.ProcessTurn(state, MoveRight()); // second read

        var readEvent = result2.Events.OfType<SignpostReadEvent>().FirstOrDefault();
        Assert.That(readEvent, Is.Not.Null, "Re-reading a sign should still emit SignpostReadEvent");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mural tests
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void BumpMural_EmitsMuralExaminedEvent()
    {
        var ids = new EntityIdAllocator(startFrom: 10);
        var mural = new Entity(ids.Next(), "Mural", 0, 0, blocksMovement: true);
        mural.Add(new MuralComponent { Text = "Ancient symbols.", MuralId = "mural_001" });

        var (state, _) = CreateStateWithFeature(mural);

        var result = TurnController.ProcessTurn(state, MoveRight());

        var examEvent = result.Events.OfType<MuralExaminedEvent>().FirstOrDefault();
        Assert.That(examEvent, Is.Not.Null, "Should emit MuralExaminedEvent");
        Assert.That(examEvent!.Text, Is.EqualTo("Ancient symbols."));
        Assert.That(examEvent.MuralId, Is.EqualTo("mural_001"));
        Assert.That(examEvent.X, Is.EqualTo(4));
        Assert.That(examEvent.Y, Is.EqualTo(5));
    }

    [Test]
    public void BumpMural_SetsHasBeenExaminedTrue()
    {
        var ids = new EntityIdAllocator(startFrom: 10);
        var mural = new Entity(ids.Next(), "Mural", 0, 0, blocksMovement: true);
        var muralComp = new MuralComponent { Text = "Carved scene.", MuralId = "mural_002" };
        mural.Add(muralComp);

        var (state, _) = CreateStateWithFeature(mural);

        TurnController.ProcessTurn(state, MoveRight());

        Assert.That(muralComp.HasBeenExamined, Is.True, "HasBeenExamined should be true after examining");
    }

    [Test]
    public void BumpMural_CostsTurn_TurnCountAdvances()
    {
        var ids = new EntityIdAllocator(startFrom: 10);
        var mural = new Entity(ids.Next(), "Mural", 0, 0, blocksMovement: true);
        mural.Add(new MuralComponent { Text = "A great battle.", MuralId = "mural_003" });

        var (state, _) = CreateStateWithFeature(mural);
        int turnBefore = state.TurnCount;

        TurnController.ProcessTurn(state, MoveRight());

        Assert.That(state.TurnCount, Is.EqualTo(turnBefore + 1),
            "Examining a mural costs a turn — TurnCount must advance");
    }

    [Test]
    public void BumpMural_PlayerDoesNotMove()
    {
        var ids = new EntityIdAllocator(startFrom: 10);
        var mural = new Entity(ids.Next(), "Mural", 0, 0, blocksMovement: true);
        mural.Add(new MuralComponent { Text = "Stationary.", MuralId = "mural_004" });

        var (state, _) = CreateStateWithFeature(mural);
        var player = state.Player;
        int playerXBefore = player.X;
        int playerYBefore = player.Y;

        TurnController.ProcessTurn(state, MoveRight());

        Assert.That(player.X, Is.EqualTo(playerXBefore));
        Assert.That(player.Y, Is.EqualTo(playerYBefore));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MuralTracker uniqueness tests
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void MuralTracker_UniquePerFloor_NoDuplicates()
    {
        var yaml = @"
murals:
  - id: m1
    text: First
    min_depth: 1
    max_depth: 10
  - id: m2
    text: Second
    min_depth: 1
    max_depth: 10
  - id: m3
    text: Third
    min_depth: 1
    max_depth: 10
";
        var registry = UnderWarden.Logic.Content.MuralRegistry.FromYaml(yaml);
        var tracker = new MuralTracker();
        var rng = new SeededRandom(1337);

        // Select two murals for floor 1
        tracker.ResetForFloor();
        var first = tracker.GetUniqueMuralForFloor(1, registry, rng);
        var second = tracker.GetUniqueMuralForFloor(1, registry, rng);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(first!.Value.Id, Is.Not.EqualTo(second!.Value.Id),
            "Two murals on the same floor should have different IDs");
    }

    [Test]
    public void MuralTracker_PoolExhaustion_ResetAndAllowsReuse()
    {
        // Pool of 2 murals — after 2 selections the pool exhausts and must reset
        var yaml = @"
murals:
  - id: m1
    text: One
    min_depth: 1
    max_depth: 10
  - id: m2
    text: Two
    min_depth: 1
    max_depth: 10
";
        var registry = UnderWarden.Logic.Content.MuralRegistry.FromYaml(yaml);
        var tracker = new MuralTracker();
        var rng = new SeededRandom(1337);

        tracker.ResetForFloor();
        tracker.GetUniqueMuralForFloor(1, registry, rng); // exhaust m1 or m2
        tracker.GetUniqueMuralForFloor(1, registry, rng); // exhaust the other

        // Third selection: pool exhausted, must reset and return a valid mural
        var third = tracker.GetUniqueMuralForFloor(1, registry, rng);

        Assert.That(third, Is.Not.Null,
            "After pool exhaustion, tracker should reset and return a valid mural");
    }

    [Test]
    public void MuralTracker_DepthFiltering_ExcludesWrongDepthMurals()
    {
        var yaml = @"
murals:
  - id: deep
    text: Deep lore
    min_depth: 5
    max_depth: 10
";
        var registry = UnderWarden.Logic.Content.MuralRegistry.FromYaml(yaml);
        var tracker = new MuralTracker();
        var rng = new SeededRandom(1337);

        tracker.ResetForFloor();

        // At depth 1, no murals match — should return null
        var result = tracker.GetUniqueMuralForFloor(1, registry, rng);

        Assert.That(result, Is.Null,
            "No murals should be available at depth 1 when min_depth is 5");
    }
}
