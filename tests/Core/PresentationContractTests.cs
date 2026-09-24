using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests the contracts that the Presentation layer depends on from Logic.
/// No Godot — these run in the standard test runner.
///
/// The Presentation layer cares about:
///   - TurnResult.GameOver matches GameState.IsGameOver (safe to drive UI from either)
///   - AliveMonsters cache is consistent with physical state after kills
///   - Entity IDs are unique across a floor (Presentation indexes sprites by ID)
///   - TurnEvents carry the fields Presentation needs to animate
/// </summary>
[TestFixture]
[Description("Contracts between Logic output and Presentation layer expectations")]
public class PresentationContractTests
{
    private DungeonFloorBuilder _floorBuilder = null!;
    private const int BaseSeed = 1337;

    [OneTimeSetUp]
    public void Setup()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var entitiesPath = Path.Combine(testDir, "..", "..", "..", "..", "config", "entities.yaml");
        var levelTemplatesPath = Path.Combine(testDir, "..", "..", "..", "..", "config", "level_templates.yaml");

        Assert.That(File.Exists(entitiesPath), Is.True, $"entities.yaml not found at {entitiesPath}");
        Assert.That(File.Exists(levelTemplatesPath), Is.True, $"level_templates.yaml not found at {levelTemplatesPath}");

        var loader = new ContentLoader();
        var content = loader.LoadAllFromFile(entitiesPath);
        var entityFactory = new EntityFactory();
        var itemFactory = new ItemFactory(content.Items, entityFactory);
        var monsterFactory = new MonsterFactory(content.Monsters, entityFactory, itemFactory);
        var consumableFactory = new ConsumableFactory(content.Consumables, entityFactory);
        var templates = LevelTemplateRegistry.FromFile(levelTemplatesPath);

        _floorBuilder = new DungeonFloorBuilder(templates, monsterFactory, itemFactory, consumableFactory);
    }

    /// <summary>
    /// Create a minimal scenario-mode state (IsDungeonMode=false) with one weak monster
    /// adjacent to the player. Scenario mode ends when all monsters die, which is what
    /// this test drives toward.
    /// </summary>
    private static GameState CreateWeakMonsterState(int seed = BaseSeed)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 6, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 5, evasion: 0, damageMin: 4, damageMax: 8));
        map.RegisterEntity(player);

        // Very low HP monster — will die in a small number of attacks
        var monster = new Entity(1, "Weak Orc", 4, 6, blocksMovement: true);
        monster.Add(new Fighter(hp: 1, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 3, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(monster);

        return new GameState(player, new List<Entity> { monster }, map, rng, turnLimit: 50);
    }

    /// <summary>
    /// After every ProcessTurn, TurnResult.GameOver must equal state.IsGameOver.
    /// The Presentation layer uses TurnResult.GameOver to gate the game-over screen;
    /// if these diverge, the screen will fire at the wrong time.
    /// </summary>
    [Test]
    [Description("TurnResult.GameOver mirrors GameState.IsGameOver after every turn")]
    public void ProcessTurn_GameOverMatchesState()
    {
        var state = CreateWeakMonsterState();
        var monster = state.Monsters[0];

        int safetyLimit = state.TurnLimit + 5;
        for (int t = 0; t < safetyLimit; t++)
        {
            var action = monster.Require<Fighter>().IsAlive
                ? PlayerAction.Attack(monster)
                : PlayerAction.Wait;

            var result = TurnController.ProcessTurn(state, action);

            Assert.That(result.GameOver, Is.EqualTo(state.IsGameOver),
                $"Turn {t}: TurnResult.GameOver={result.GameOver} but state.IsGameOver={state.IsGameOver}");

            if (result.GameOver) break;
        }

        // The monster had 1 HP — the game must have ended within the turn limit
        Assert.That(state.IsGameOver, Is.True, "Game should have ended — weak monster should be dead");
    }

    /// <summary>
    /// After a kill, AliveMonsters must exclude the dead entity.
    /// The Presentation layer iterates AliveMonsters for sprite updates — stale entries
    /// would leave ghost sprites on screen.
    ///
    /// Also validates that Monsters still contains the entity (combat log lookups by ID
    /// need to find entities even after death).
    /// </summary>
    [Test]
    [Description("AliveMonsters excludes dead entities; Monsters retains them for ID lookup")]
    public void AliveMonsters_ExcludesDeadAfterKill()
    {
        var state = CreateWeakMonsterState();
        var monster = state.Monsters[0];

        Assert.That(state.Monsters.Count, Is.EqualTo(1));
        Assert.That(state.AliveMonsters.Count, Is.EqualTo(1));

        // Keep attacking until the monster dies (1 HP so first successful hit kills it)
        int turns = 0;
        while (monster.Require<Fighter>().IsAlive && turns < 20)
        {
            TurnController.ProcessTurn(state, PlayerAction.Attack(monster));
            turns++;
        }

        Assert.That(monster.Require<Fighter>().IsAlive, Is.False,
            "Monster with 1 HP should be dead after a successful attack");

        // The Presentation contract: alive list is empty, full list is intact
        Assert.That(state.AliveMonsters.Count, Is.EqualTo(0),
            "AliveMonsters should be empty after the only monster dies");
        Assert.That(state.Monsters.Count, Is.EqualTo(1),
            "Monsters (full list) should still contain the dead entity for ID lookup purposes");
    }

    /// <summary>
    /// All entity IDs on a dungeon floor must be unique.
    /// The Presentation layer uses IDs as dictionary keys for sprite nodes — a collision
    /// would silently overwrite one entity's sprite with another's.
    ///
    /// The ID collision bug was fixed in Task 1.2 (EntityIdAllocator starts after shared
    /// EntityFactory counter). This test is a regression guard.
    /// </summary>
    [Test]
    [Description("All entity IDs on a dungeon floor are unique — regression guard for ID collision fix")]
    public void EntityIds_NoDuplicatesOnFloor()
    {
        var rng = new SeededRandom(BaseSeed);
        var state = _floorBuilder.Build(depth: 1, rng);
        AssertIdsUnique(state, BaseSeed);
    }

    /// <summary>
    /// Runs the ID uniqueness invariant across 20 independent seeds.
    /// A single-seed test can miss allocation bugs that only surface when the
    /// EntityIdAllocator wraps or when a particular entity mix triggers a collision.
    /// 20 seeds covers enough variance to catch off-by-one and reset bugs.
    /// </summary>
    [Test]
    [Description("Entity IDs are unique across 20 random seeds — guards against ID allocation regression")]
    public void EntityIds_UniqueAcross20Seeds()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var rng = new SeededRandom(seed);
            var state = _floorBuilder.Build(1, rng);
            AssertIdsUnique(state, seed);
        }
    }

    /// <summary>
    /// Collects every entity ID present on the floor (player, equipment, inventory,
    /// monsters, floor items, stair) and asserts no duplicates exist.
    /// Extracted so both the single-seed and multi-seed tests share the same logic.
    /// </summary>
    private static void AssertIdsUnique(GameState state, int seed)
    {
        var ids = new HashSet<int>();
        var duplicates = new List<string>();

        void CheckId(int id, string label)
        {
            if (!ids.Add(id))
                duplicates.Add($"Duplicate ID {id}: {label}");
        }

        CheckId(state.Player.Id, "Player");

        // Equipment slots
        var equipment = state.Player.Get<Equipment>();
        if (equipment?.MainHand != null)
            CheckId(equipment.MainHand.Id, $"Equipment:MainHand({equipment.MainHand.Name})");
        var chest = equipment?.GetSlot(EquipmentSlot.Chest);
        if (chest != null)
            CheckId(chest.Id, $"Equipment:Chest({chest.Name})");

        // Inventory items
        var inventory = state.Player.Get<Inventory>();
        if (inventory != null)
            foreach (var item in inventory.Items)
                CheckId(item.Id, $"Inventory:{item.Name}");

        foreach (var m in state.Monsters)
            CheckId(m.Id, $"Monster:{m.Name}");

        foreach (var item in state.FloorItems)
            CheckId(item.Id, $"FloorItem:{item.Name}");

        if (state.StairDown != null)
            CheckId(state.StairDown.Id, "StairDown");

        Assert.That(duplicates, Is.Empty,
            $"Seed {seed}: ID collisions found:\n{string.Join("\n", duplicates)}");
    }

    /// <summary>
    /// Event types must carry the fields Presentation needs to build animations.
    ///
    /// AttackEvent.TargetId > 0: Presentation looks up the target sprite by ID.
    ///   (Player ID is 0, so any TargetId > 0 means a valid non-player target.)
    ///
    /// HealEvent.ItemName not empty: toast log shows "Used {ItemName}".
    ///
    /// DeathEvent.ActorId > 0: death events on the player use ActorId == player.Id == 0;
    ///   monster deaths should have ActorId > 0.
    ///
    /// MoveEvent.ToX/ToY non-negative: negative positions indicate an off-map destination,
    ///   which would cause the presentation layer to place sprites outside the grid.
    /// </summary>
    [Test]
    [Description("TurnEvents carry required non-null/valid fields for Presentation animation")]
    public void TurnEvents_HaveRequiredFields()
    {
        var state = CreateWeakMonsterState(seed: BaseSeed + 7);
        var monster = state.Monsters[0];

        // Add a healing potion so we can verify HealEvents too
        var inventory = state.Player.GetOrAdd<Inventory>();
        var healItem = new Entity(99, "Healing Potion", 0, 0, blocksMovement: false);
        healItem.Add(new Consumable(healAmount: 20));
        inventory.Add(healItem);

        // Force player HP low so BotBrain will trigger healing on first possible turn
        state.Player.Require<Fighter>().TakeDamage(50); // player has 54 HP, so now at 4 HP (below 15% panic threshold)

        var allEvents = new List<TurnEvent>();

        int turns = 0;
        while (!state.IsGameOver && turns < 30)
        {
            var botAction = BotBrain.Decide(
                state.Player,
                state.PlayerFighter,
                state.PlayerInventory,
                state.Monsters,
                state.Map);
            var result = TurnController.ProcessTurn(state, BotBrain.ToPlayerAction(botAction));
            allEvents.AddRange(result.Events);
            turns++;
        }

        // Validate AttackEvents targeting monsters have non-zero TargetId
        var monsterTargetAttacks = allEvents
            .OfType<AttackEvent>()
            .Where(e => e.ActorId == state.Player.Id)
            .ToList();

        foreach (var atk in monsterTargetAttacks)
            Assert.That(atk.TargetId, Is.GreaterThan(0),
                $"AttackEvent from player has TargetId={atk.TargetId} — Presentation needs a valid monster ID");

        // Guarantee at least one HealEvent so the field contract is always verified.
        // BotBrain may have consumed the potion already (or the game ended before healing
        // was needed). Issue one explicit heal turn if none were observed yet.
        var healEvents = allEvents.OfType<HealEvent>().ToList();
        if (healEvents.Count == 0 && !state.IsGameOver)
        {
            // Re-add the heal item if it was consumed during the bot loop
            var stillHasPotion = state.PlayerInventory?.FindFirst(
                item => item.Get<Consumable>()?.IsHealing == true) != null;
            if (!stillHasPotion)
            {
                var freshPotion = new Entity(98, "Healing Potion", 0, 0, blocksMovement: false);
                freshPotion.Add(new Consumable(healAmount: 20));
                state.Player.GetOrAdd<Inventory>().Add(freshPotion);
                healItem = freshPotion;
            }

            var healResult = TurnController.ProcessTurn(state, PlayerAction.UseItem(healItem));
            allEvents.AddRange(healResult.Events);
            healEvents = allEvents.OfType<HealEvent>().ToList();
        }

        Assert.That(healEvents.Count, Is.GreaterThan(0),
            "Expected at least one HealEvent — test setup should guarantee the player heals");
        foreach (var heal in healEvents)
            Assert.That(heal.ItemName, Is.Not.Null.And.Not.Empty,
                $"HealEvent has null/empty ItemName — toast log will show blank");

        // Validate MoveEvents have non-negative destinations
        var moveEvents = allEvents.OfType<MoveEvent>().ToList();
        foreach (var move in moveEvents)
        {
            Assert.That(move.ToX, Is.GreaterThanOrEqualTo(0),
                $"MoveEvent has negative ToX={move.ToX}");
            Assert.That(move.ToY, Is.GreaterThanOrEqualTo(0),
                $"MoveEvent has negative ToY={move.ToY}");
        }

        // Validate DeathEvents for monsters have ActorId > 0
        // (player ID is 0; monster deaths must have ActorId matching a monster, which is > 0)
        var monsterDeaths = allEvents
            .OfType<DeathEvent>()
            .Where(e => e.ActorId != state.Player.Id)
            .ToList();

        foreach (var death in monsterDeaths)
            Assert.That(death.ActorId, Is.GreaterThan(0),
                $"Monster DeathEvent has ActorId={death.ActorId} — Presentation can't identify which sprite to remove");

        // Sanity: we should have observed some events over ~30 turns
        Assert.That(allEvents.Count, Is.GreaterThan(0),
            "No events observed in 30 turns — something is wrong with the test setup");
    }

    /// <summary>
    /// In dungeon mode, killing all monsters does NOT trigger IsGameOver.
    /// The Presentation layer must NOT show the game-over screen just because the floor
    /// is clear — the player still needs to find and descend the stair.
    ///
    /// This is the opposite of scenario mode, where all-monsters-dead IS the win condition.
    /// The IsDungeonMode flag on GameState is what guards this distinction.
    /// </summary>
    [Test]
    [Description("Dungeon mode: killing all monsters does NOT trigger IsGameOver")]
    public void ProcessTurn_DungeonMode_AllMonstersDead_NotGameOver()
    {
        var rng = new SeededRandom(BaseSeed);
        var map = GameMap.CreateArena(12, 12);
        map.RevealAll();

        var player = new Entity(0, "Player", 3, 6, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 5, evasion: 0, damageMin: 4, damageMax: 8));
        map.RegisterEntity(player);

        // 1-HP monster placed adjacent to the player — will die on first successful hit
        var monster = new Entity(1, "Weak Orc", 4, 6, blocksMovement: true);
        monster.Add(new Fighter(hp: 1, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 3, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(monster);

        // Dungeon mode — killing all monsters should NOT end the game
        var state = new GameState(player, new List<Entity> { monster }, map, rng, turnLimit: 200)
        {
            IsDungeonMode = true,
            CurrentDepth = 1,
        };

        // Attack until the monster dies (1 HP — first hit kills it)
        int turns = 0;
        while (monster.Require<Fighter>().IsAlive && turns < 20)
        {
            TurnController.ProcessTurn(state, PlayerAction.Attack(monster));
            turns++;
        }

        Assert.That(monster.Require<Fighter>().IsAlive, Is.False,
            "Monster with 1 HP should be dead after a successful attack");
        Assert.That(state.AliveMonsters.Count, Is.EqualTo(0),
            "AliveMonsters should be empty after the only monster dies");

        // Process one more turn after the kill — IsGameOver must remain false in dungeon mode
        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(result.GameOver, Is.False,
            "TurnResult.GameOver must be false in dungeon mode after all monsters die — stair descent has not occurred");
        Assert.That(state.IsGameOver, Is.False,
            "GameState.IsGameOver must be false in dungeon mode after all monsters die — floor-clear is not game-over");
    }

    /// <summary>
    /// DescendEvent must carry the correct NewDepth.
    /// The Presentation layer reads NewDepth to build the next floor at the right depth.
    /// An off-by-one here would silently generate the wrong floor template.
    /// </summary>
    [Test]
    [Description("DescendEvent carries correct NewDepth")]
    public void DescendEvent_HasCorrectNewDepth()
    {
        var rng = new SeededRandom(BaseSeed);
        var map = GameMap.CreateArena(12, 12);
        map.RevealAll();

        // Player at (3,6)
        var player = new Entity(0, "Player", 3, 6, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 5, evasion: 0, damageMin: 4, damageMax: 8));
        map.RegisterEntity(player);

        // Stair placed at the player's position — PlayerOnStairDown returns true immediately
        var stair = new Entity(2, "Stair Down", 3, 6, blocksMovement: false);
        map.RegisterEntity(stair);

        const int startDepth = 2; // Arbitrary non-1 depth to catch off-by-one bugs

        // No monsters — IsFloorClear=true from the start (empty AliveMonsters in dungeon mode)
        var state = new GameState(player, new List<Entity>(), map, rng, turnLimit: 200)
        {
            IsDungeonMode = true,
            CurrentDepth = startDepth,
            StairDown = stair,
        };

        // Preconditions: player on stair, floor clear
        Assert.That(state.PlayerOnStairDown, Is.True,
            "Player should be on the stair before issuing Descend");
        Assert.That(state.IsFloorClear, Is.True,
            "Floor should be clear (no monsters) before issuing Descend");

        var result = TurnController.ProcessTurn(state, PlayerAction.Descend);

        var descendEvent = result.Events.OfType<DescendEvent>().FirstOrDefault();
        Assert.That(descendEvent, Is.Not.Null,
            "DescendEvent should be emitted when player descends from the stair");
        Assert.That(descendEvent!.NewDepth, Is.EqualTo(startDepth + 1),
            $"DescendEvent.NewDepth should be {startDepth + 1} (currentDepth + 1), got {descendEvent.NewDepth}");
    }
}
