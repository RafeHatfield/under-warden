using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

[TestFixture]
[Description("Multi-floor smoke tests — exercises DungeonFloorBuilder + TurnController + BotBrain across floor transitions")]
public class MultiFloorSmokeTests
{
    private DungeonFloorBuilder _floorBuilder = null!;
    private const int BaseSeed = 1337;
    private const int MaxTurnsPerFloor = 500;

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
    /// Run the bot through N floors. Returns total turns taken.
    /// The bot uses BotBrain for combat decisions. When the floor is clear,
    /// it paths toward the stair and descends.
    /// </summary>
    private int RunFloors(int floorCount, int seed)
    {
        int totalTurns = 0;
        Entity? player = null;

        for (int depth = 1; depth <= floorCount; depth++)
        {
            var rng = new SeededRandom(seed + depth * 1_000_003);
            var state = _floorBuilder.Build(depth, rng, player);
            int floorTurns = 0;

            while (!state.IsGameOver && floorTurns < MaxTurnsPerFloor)
            {
                PlayerAction action;

                if (state.IsFloorClear && state.StairDown != null)
                {
                    // All monsters dead — navigate to stair and descend
                    if (state.PlayerOnStairDown)
                    {
                        action = PlayerAction.Descend;
                    }
                    else
                    {
                        // Path toward stair
                        var path = Pathfinder.AStar(
                            state.Map,
                            state.Player.X, state.Player.Y,
                            state.StairDown.X, state.StairDown.Y,
                            state.Player,
                            canPassDoors: true);
                        if (path != null && path.Count > 0)
                        {
                            var (nx, ny) = path[0];
                            action = PlayerAction.MoveTo(nx, ny);
                        }
                        else
                        {
                            action = PlayerAction.Wait;
                        }
                    }
                }
                else
                {
                    // Normal bot logic — fight and heal via BotBrain
                    var botAction = BotBrain.Decide(
                        state.Player,
                        state.PlayerFighter,
                        state.PlayerInventory,
                        state.Monsters,
                        state.Map);

                    // BotBrain.ToPlayerAction uses greedy MoveToward which gets stuck at walls.
                    // Override MoveToward with A* so the bot can reach monsters in other rooms.
                    if (botAction.Type == BotAction.ActionType.MoveToward && botAction.Target != null)
                    {
                        var target = botAction.Target;
                        var path = Pathfinder.AStar(
                            state.Map,
                            state.Player.X, state.Player.Y,
                            target.X, target.Y,
                            state.Player,
                            canPassDoors: true);
                        if (path != null && path.Count > 0)
                        {
                            var (nx, ny) = path[0];
                            action = PlayerAction.MoveTo(nx, ny);
                        }
                        else
                        {
                            action = PlayerAction.Wait; // unreachable target
                        }
                    }
                    else
                    {
                        action = BotBrain.ToPlayerAction(botAction);
                    }
                }

                var result = TurnController.ProcessTurn(state, action);
                floorTurns++;

                // Descend event signals we've moved to the next floor
                if (result.Events.Any(e => e is DescendEvent))
                    break;
            }

            totalTurns += floorTurns;

            // If the player died on this floor, stop — don't carry a dead player to the next floor
            if (!state.PlayerFighter.IsAlive)
                break;

            player = state.Player; // carry player forward to next floor
        }

        return totalTurns;
    }

    [Test]
    [Description("3 floors with seed 1337 completes without exceptions")]
    public void ThreeFloors_NoExceptions()
    {
        Assert.DoesNotThrow(() => RunFloors(3, BaseSeed));
    }

    [Test]
    [Description("5 floors complete within bounded turn count")]
    public void FiveFloors_BoundedTurns()
    {
        int turns = RunFloors(5, BaseSeed);
        Assert.That(turns, Is.LessThan(2500), $"5-floor run took {turns} turns — bot may be stuck");
    }

    [Test]
    [Description("Entity IDs are unique within each floor (no collision between starting gear and map entities)")]
    public void FiveFloors_NoEntityIdCollisions()
    {
        Entity? player = null;

        for (int depth = 1; depth <= 5; depth++)
        {
            var rng = new SeededRandom(BaseSeed + depth * 1_000_003);
            var state = _floorBuilder.Build(depth, rng, player);

            var ids = new HashSet<int>();
            var duplicates = new List<string>();

            void CheckId(int id, string label)
            {
                if (!ids.Add(id))
                    duplicates.Add($"Duplicate ID {id}: {label} (depth {depth})");
            }

            CheckId(state.Player.Id, "Player");

            // Starting gear in equipment slots
            var equipment = state.Player.Get<Equipment>();
            if (equipment?.MainHand != null)
                CheckId(equipment.MainHand.Id, $"Equipment:MainHand({equipment.MainHand.Name})");
            var chest = equipment?.GetSlot(EquipmentSlot.Chest);
            if (chest != null)
                CheckId(chest.Id, $"Equipment:Chest({chest.Name})");

            // Starting gear in inventory
            var inventory = state.PlayerInventory;
            if (inventory != null)
                foreach (var item in inventory.Items)
                    CheckId(item.Id, $"Inventory:{item.Name}");

            foreach (var m in state.Monsters)
                CheckId(m.Id, $"Monster:{m.Name}");

            foreach (var item in state.FloorItems)
                CheckId(item.Id, $"FloorItem:{item.Name}");

            if (state.StairDown != null)
                CheckId(state.StairDown.Id, "StairDown");

            Assert.That(duplicates, Is.Empty, string.Join("\n", duplicates));

            // Advance a few turns so player carries forward with updated state
            for (int t = 0; t < 5 && !state.IsGameOver; t++)
            {
                var botAction = BotBrain.Decide(
                    state.Player,
                    state.PlayerFighter,
                    state.PlayerInventory,
                    state.Monsters,
                    state.Map);
                TurnController.ProcessTurn(state, BotBrain.ToPlayerAction(botAction));
            }
            player = state.Player;
        }
    }

    [Test]
    [Description("Same seed produces identical turn counts (determinism check)")]
    public void ThreeFloors_Deterministic()
    {
        int turns1 = RunFloors(3, BaseSeed);
        int turns2 = RunFloors(3, BaseSeed);
        Assert.That(turns1, Is.EqualTo(turns2), "Same seed should produce identical results");
    }
}
