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
/// GameState SaveMidRun/LoadMidRun (scenario mode) + atomic file I/O + the S1 round-trip soak
/// (M1.4 4a.3b). S2 divergence and dungeon-mode subsystems are 4a.3b-2.
/// </summary>
[TestFixture]
public class MidRunGameStateTests
{
    private MonsterFactory _monsters = null!;
    private ItemFactory _items = null!;
    private ConsumableFactory _consumables = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        const string yaml = @"
monsters:
  orc_grunt:
    name: ""Orc""
    stats: { hp: 28, xp: 35, damage_min: 4, damage_max: 6, strength: 14, dexterity: 10, constitution: 12, accuracy: 4, evasion: 1 }
    char: ""o""
    blocks: true
    faction: ""orc""
weapons:
  short_sword:
    name: ""Short Sword""
    slot: main_hand
    damage_min: 3
    damage_max: 6
    to_hit_bonus: 1
armor:
  leather_armor:
    name: ""Leather Armor""
    slot: chest
    armor_class_bonus: 2
consumables:
  healing_potion:
    name: ""Healing Potion""
    heal_amount: 40
";
        var bundle = new ContentLoader().LoadAll(yaml);
        var ef = new EntityFactory();
        _monsters = new MonsterFactory(bundle.Monsters, ef);
        _items = new ItemFactory(bundle.Items, ef);
        _consumables = new ConsumableFactory(bundle.Consumables, ef);
    }

    private static ScenarioDefinition Scenario(int depth) => new()
    {
        ScenarioId = "midrun_test",
        Depth = depth,
        TurnLimit = 500,
        MapWidth = 14,
        MapHeight = 14,
        PlayerStartX = 3,
        PlayerStartY = 6,
        Player = new ScenarioPlayer { Hp = 54, Strength = 14, Weapon = "short_sword", Armor = "leather_armor" },
        Monsters = new List<ScenarioMonster>
        {
            new() { Type = "orc_grunt", Count = 3 },
        },
        Items = new List<ScenarioItem> { new() { Type = "healing_potion", Count = 2 } },
    };

    private GameState BuildAndAdvance(int depth, int seed, int turns)
    {
        var state = GameStateFactory.FromScenario(Scenario(depth), seed, _monsters, _items, _consumables);
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
    public void SaveLoad_PreservesCoreState()
    {
        var state = BuildAndAdvance(depth: 3, seed: 1337, turns: 25);
        var loaded = MidRunSerializer.LoadMidRun(MidRunSerializer.SaveMidRun(state));

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Player.Id, Is.EqualTo(state.Player.Id));
            Assert.That(loaded.PlayerFighter.Hp, Is.EqualTo(state.PlayerFighter.Hp));
            Assert.That(loaded.Monsters.Count, Is.EqualTo(state.Monsters.Count));
            Assert.That(loaded.TurnCount, Is.EqualTo(state.TurnCount));
            Assert.That(loaded.Rng.CallCount, Is.EqualTo(state.Rng.CallCount));
            Assert.That(loaded.Rng.Seed, Is.EqualTo(state.Rng.Seed));
            Assert.That(loaded.Map.Width, Is.EqualTo(state.Map.Width));
            Assert.That(loaded.CurrentDepth, Is.EqualTo(state.CurrentDepth));
            // Post-load RNG continues the exact sequence.
            Assert.That(loaded.Rng.Next(1_000_000), Is.EqualTo(state.Rng.Next(1_000_000)));
        });
    }

    [Test]
    public void IdAllocatorWatermark_RestoredSoNewIdsNeverCollide()
    {
        var state = BuildAndAdvance(depth: 2, seed: 99, turns: 15);
        var dto = MidRunSerializer.SaveMidRun(state);

        // Scenario has no allocator; simulate a dungeon watermark above every saved id.
        int maxSavedId = dto.Entities.Entities.Max(e => e.Id);
        dto.IsDungeonMode = false; // keep scenario load path
        dto.IdAllocatorWatermark = maxSavedId + 1;

        var loaded = MidRunSerializer.LoadMidRun(dto);
        var savedIds = dto.Entities.Entities.Select(e => e.Id).ToHashSet();
        for (int i = 0; i < 20; i++)
        {
            int fresh = loaded.IdAllocator!.Next();
            Assert.That(savedIds.Contains(fresh), Is.False, $"allocated id {fresh} collides with a saved entity id.");
        }
    }

    [Test]
    public void FileIO_AtomicRoundTrip()
    {
        var dto = MidRunSerializer.SaveMidRun(BuildAndAdvance(depth: 1, seed: 7, turns: 10));
        string path = Path.Combine(Path.GetTempPath(), $"midrun_{Guid.NewGuid():N}.json");
        try
        {
            MidRunFile.SaveMidRunToFile(dto, path);
            Assert.That(File.Exists(path + ".tmp"), Is.False, ".tmp must be moved, not left behind.");
            var result = MidRunFile.LoadMidRunFromFile(path);
            Assert.That(result.IsOk, Is.True);
            Assert.That(ToJson(result.Save!), Is.EqualTo(ToJson(dto)), "file round-trip must be byte-identical.");
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void FileIO_MissingFile_ReturnsFileNotFound()
    {
        var result = MidRunFile.LoadMidRunFromFile(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.json"));
        Assert.That(result.Status, Is.EqualTo(MidRunLoadStatus.FileNotFound));
    }

    [Test]
    public void FileIO_TruncatedSave_FailsToCorrupt()
    {
        var dto = MidRunSerializer.SaveMidRun(BuildAndAdvance(depth: 1, seed: 7, turns: 10));
        string path = Path.Combine(Path.GetTempPath(), $"midrun_{Guid.NewGuid():N}.json");
        try
        {
            MidRunFile.SaveMidRunToFile(dto, path);
            var full = File.ReadAllText(path);
            File.WriteAllText(path, full.Substring(0, full.Length / 2)); // truncate
            var result = MidRunFile.LoadMidRunFromFile(path);
            Assert.That(result.Status, Is.EqualTo(MidRunLoadStatus.Corrupt));
            Assert.That(result.Save, Is.Null);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void FileIO_GarbageSave_FailsToCorrupt()
    {
        string path = Path.Combine(Path.GetTempPath(), $"midrun_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "this is not json {{{ ]]]");
            var result = MidRunFile.LoadMidRunFromFile(path);
            Assert.That(result.Status, Is.EqualTo(MidRunLoadStatus.Corrupt));
        }
        finally { File.Delete(path); }
    }

    // ── S1 soak: serialize → deserialize → serialize is byte-identical ─────────
    [Test]
    public void S1_RoundTripStability_ByteIdentical([Values(1337, 7, 99, 2024, 55555)] int seed,
                                                    [Values(1, 3, 5)] int depth)
    {
        var state = BuildAndAdvance(depth, seed, turns: 35);
        var dto1 = MidRunSerializer.SaveMidRun(state);
        string json1 = ToJson(dto1);

        var reloaded = MidRunSerializer.LoadMidRun(dto1);
        var dto2 = MidRunSerializer.SaveMidRun(reloaded);
        string json2 = ToJson(dto2);

        Assert.That(json2, Is.EqualTo(json1), $"S1 byte-identity failed at seed={seed} depth={depth}.");
    }
}
