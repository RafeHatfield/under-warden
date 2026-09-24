using System.Text.Json;
using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Persistence.MidRun;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

/// <summary>
/// Dungeon-mode SaveMidRun/LoadMidRun (M1.4 4a.3b-2): full-floor round-trip incl. the subsystem
/// serializers, and the delay-fuse open-chest MISFED integration test. Weighing floor (25) is 4a.3b-3.
/// </summary>
[TestFixture]
public class MidRunDungeonTests
{
    private const string ContentYaml = @"
monsters:
  orc_grunt:
    name: Orc Grunt
    stats: { hp: 20, power: 0, defense: 0, xp: 25, damage_min: 3, damage_max: 5, strength: 12, dexterity: 10, constitution: 10, accuracy: 3, evasion: 1 }
    char: o
    ai_type: basic
    blocks: true
    faction: orc
    tags: [humanoid, living]
    etp_base: 20
consumables:
  healing_potion:
    name: Healing Potion
    heal_amount: 20
";

    private MonsterFactory _monsters = null!;
    private ItemFactory _items = null!;
    private ConsumableFactory _consumables = null!;

    private DungeonFloorBuilder NewBuilder()
    {
        var bundle = new ContentLoader().LoadAll(ContentYaml);
        var ef = new EntityFactory(startId: 1);
        _monsters = new MonsterFactory(bundle.Monsters, ef);
        _items = new ItemFactory(bundle.Items, ef);
        _consumables = new ConsumableFactory(bundle.Consumables, ef);
        var registry = LevelTemplateRegistry.FromYaml("levels: {}");
        return new DungeonFloorBuilder(registry, _monsters, _items, _consumables);
    }

    private GameState BuildDungeon(int depth, int seed, int turns)
    {
        var state = NewBuilder().Build(depth, new SeededRandom(seed));
        var bot = new BotBrain(BotPersonaRegistry.Get("balanced"));
        for (int t = 0; t < turns && !state.IsGameOver; t++)
        {
            var action = BotBrain.ToPlayerAction(
                bot.Decide(state.Player, state.PlayerFighter, state.PlayerInventory, state.Monsters, state.Map, null));
            TurnController.ProcessTurn(state, action);
        }
        return state;
    }

    private static string ToJson(MidRunSaveDto dto) =>
        JsonSerializer.Serialize(dto, MidRunSaveJsonContext.Default.MidRunSaveDto);

    [Test]
    public void DungeonSaveLoad_PreservesSubsystemsAndCore()
    {
        var state = BuildDungeon(depth: 3, seed: 1337, turns: 30);
        Assume.That(state.IsDungeonMode, Is.True);

        var loaded = MidRunSerializer.LoadMidRun(MidRunSerializer.SaveMidRun(state));

        Assert.Multiple(() =>
        {
            Assert.That(loaded.IsDungeonMode, Is.True);
            Assert.That(loaded.CurrentDepth, Is.EqualTo(state.CurrentDepth));
            Assert.That(loaded.Rng.CallCount, Is.EqualTo(state.Rng.CallCount));
            Assert.That(loaded.Monsters.Count, Is.EqualTo(state.Monsters.Count));
            // Subsystems survived.
            Assert.That(loaded.IdentificationRegistry, Is.Not.Null);
            Assert.That(loaded.AppearancePool, Is.Not.Null);
            Assert.That(loaded.BoonTracker!.VisitedDepths, Is.EquivalentTo(state.BoonTracker!.VisitedDepths));
            Assert.That(loaded.Rooms!.Count, Is.EqualTo(state.Rooms!.Count));
            // Post-load RNG continues the exact sequence.
            Assert.That(loaded.Rng.Next(1_000_000), Is.EqualTo(state.Rng.Next(1_000_000)));
        });
    }

    [Test]
    public void OpenChest_AfterSaveLoad_YieldsRealLoot()
    {
        var state = BuildDungeon(depth: 2, seed: 4242, turns: 30);

        // Inject an unopened prop holding a pre-generated loot potion, on a free tile next to the player.
        // Fresh ids computed across EVERY entity (the floor uses two id sources) so nothing collides.
        int freshBase = new[] { state.Player }
            .Concat(state.Monsters).Concat(state.FloorItems).Concat(state.Corpses)
            .Concat(state.Features).Concat(state.Portals).Concat(state.Map.Entities)
            .Select(e => e.Id).Append(state.StairDown?.Id ?? 0).Max() + 1;

        var loot = new Entity(freshBase, "healing_potion");
        loot.Add(new Consumable(healAmount: 20, isPotion: true));
        loot.Add(new ItemTag("healing_potion"));

        var (px, py) = (state.Player.X, state.Player.Y);
        (int X, int Y) cell = new[] { (px + 1, py), (px - 1, py), (px, py + 1), (px, py - 1) }
            .First(c => state.Map.InBounds(c.Item1, c.Item2) && state.Map.IsWalkable(c.Item1, c.Item2)
                        && !state.Monsters.Any(m => m.X == c.Item1 && m.Y == c.Item2));
        var prop = new Entity(freshBase + 1, "barrel", cell.X, cell.Y, blocksMovement: true);
        prop.Add(new DestructiblePropComponent { PropKind = "barrel", LootEntityIds = new List<int> { loot.Id } });
        prop.Add(new ChestLootStash(new List<Entity> { loot }));
        state.Features.Add(prop);
        state.Map.RegisterEntity(prop);

        // Save → load. The loot lives only in the stash (in no floor list) — the delay fuse.
        var loaded = MidRunSerializer.LoadMidRun(MidRunSerializer.SaveMidRun(state));

        // THE delay-fuse: after save/load the loot lives ONLY inside the stash (in no floor list yet),
        // and it is a REAL entity with its correct component state — this is the MISFED guarantee.
        var loadedProp = loaded.Features.Single(f => f.Get<DestructiblePropComponent>() != null);
        var stashedLoot = loadedProp.Require<ChestLootStash>().Items.Single();
        Assert.That(stashedLoot.Id, Is.EqualTo(loot.Id));
        Assert.That(stashedLoot.Require<Consumable>().HealAmount, Is.EqualTo(20), "restored loot keeps its component state.");
        Assert.That(stashedLoot.Require<ItemTag>().TypeId, Is.EqualTo("healing_potion"));
        Assert.That(loaded.FloorItems.Any(i => i.Id == loot.Id), Is.False, "loot is not on the floor until the chest opens.");

        // Open it: bump the prop tile → resolves and releases the loot from the stash into the world.
        loaded.Player.X = loadedProp.X - 1;
        loaded.Player.Y = loadedProp.Y;
        loaded.PlayerFighter.Hp = loaded.PlayerFighter.MaxHp;
        Assume.That(loaded.IsGameOver, Is.False);
        TurnController.ProcessTurn(loaded, PlayerAction.MoveTo(loadedProp.X, loadedProp.Y));

        Assert.That(loadedProp.Require<DestructiblePropComponent>().IsResolved, Is.True, "the bump must resolve the prop.");
        Assert.That(loadedProp.Require<ChestLootStash>().Items, Is.Empty,
            "opening the chest after load releases the loot from the stash (the delay fuse fires post-load).");
    }

    [Test]
    public void S1_Dungeon_ByteIdentical([Values(1337, 7, 99, 2024, 55555)] int seed,
                                         [Values(1, 3, 5)] int depth)
    {
        var state = BuildDungeon(depth, seed, turns: 30);
        var dto1 = MidRunSerializer.SaveMidRun(state);
        string json1 = ToJson(dto1);

        var reloaded = MidRunSerializer.LoadMidRun(dto1);
        var dto2 = MidRunSerializer.SaveMidRun(reloaded);
        string json2 = ToJson(dto2);

        Assert.That(json2, Is.EqualTo(json1), $"dungeon S1 byte-identity failed at seed={seed} depth={depth}.");
    }
}
