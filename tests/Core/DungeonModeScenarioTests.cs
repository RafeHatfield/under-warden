using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Integration tests for dungeon-mode scenario loading.
/// Exercises the full pipeline without Godot:
///   ScenarioDefinition (programmatic) → LevelTemplateRegistry.FromSingleDepth
///   → DungeonFloorBuilder.Build → verify entities on map.
///
/// Covers TASK-007 of the dungeon-mode-testing feature.
/// </summary>
[TestFixture]
public class DungeonModeScenarioTests
{
    // Minimal content YAML with a monster, consumable, scroll, wand, and equipment piece.
    // The scroll and wand definitions must match what PlaceGuaranteedSpawns will look up.
    private const string ContentYaml = @"
monsters:
  test_orc:
    name: Test Orc
    stats:
      hp: 20
      power: 0
      defense: 0
      xp: 10
      damage_min: 3
      damage_max: 5
      strength: 12
      dexterity: 10
      constitution: 10
      accuracy: 3
      evasion: 1
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

scrolls:
  scroll_of_lightning:
    name: Scroll of Lightning
    spell_id: lightning
    targeting: auto_closest
    damage: 40
    range: 5
    char: '~'

wands:
  wand_of_confusion:
    name: Wand of Confusion
    spell_id: confusion
    targeting: auto_closest
    is_wand: true
    min_charges: 3
    max_charges: 6
    charge_cap: 10
    char: /

weapons:
  test_sword:
    name: Test Sword
    slot: main_hand
    damage_min: 4
    damage_max: 8
    char: ')'
";

    private static (
        MonsterFactory monsters,
        ItemFactory items,
        ConsumableFactory consumables,
        SpellItemFactory spellItems
    ) BuildFactories()
    {
        var loader = new ContentLoader();
        var bundle = loader.LoadAll(ContentYaml);
        var entityFactory = new EntityFactory(startId: 1);
        return (
            new MonsterFactory(bundle.Monsters, entityFactory),
            new ItemFactory(bundle.Items, entityFactory),
            new ConsumableFactory(bundle.Consumables, entityFactory),
            new SpellItemFactory(bundle.SpellItems, entityFactory)
        );
    }

    // ─── ScenarioDefinition fields ────────────────────────────────────────────

    [Test]
    public void ScenarioDefinition_DungeonMode_DefaultsFalse()
    {
        // Existing scenario code paths must be unaffected — DungeonMode must default false.
        var scenario = new ScenarioDefinition();
        Assert.That(scenario.DungeonMode, Is.False);
        Assert.That(scenario.GuaranteedSpawns, Is.Null);
    }

    [Test]
    public void ScenarioDefinition_DungeonMode_DeserializesFromYaml()
    {
        const string yaml = @"
scenario_id: test_dungeon
name: Test Dungeon
dungeon_mode: true
depth: 1
guaranteed_spawns:
  mode: replace
  monsters:
    - type: test_orc
      count: 1
  items:
    - type: healing_potion
      count: 1
";
        var loader = new ContentLoader();
        var scenario = loader.LoadScenario(yaml);

        Assert.That(scenario.DungeonMode, Is.True, "dungeon_mode: true must deserialize correctly");
        Assert.That(scenario.GuaranteedSpawns, Is.Not.Null, "guaranteed_spawns must deserialize");
        Assert.That(scenario.GuaranteedSpawns!.Mode, Is.EqualTo("replace"));
        Assert.That(scenario.GuaranteedSpawns.Monsters, Has.Count.EqualTo(1));
        Assert.That(scenario.GuaranteedSpawns.Monsters[0].Type, Is.EqualTo("test_orc"));
        Assert.That(scenario.GuaranteedSpawns.Items, Has.Count.EqualTo(1));
        Assert.That(scenario.GuaranteedSpawns.Items[0].Type, Is.EqualTo("healing_potion"));
    }

    [Test]
    public void ScenarioDefinition_ExistingScenario_NoDungeonModeField_StillDeserializes()
    {
        // Backward compatibility: YAML without dungeon_mode must still deserialize cleanly.
        const string yaml = @"
scenario_id: legacy
name: Legacy Scenario
depth: 1
turn_limit: 100
runs: 40
map_width: 12
map_height: 12
player_start_x: 3
player_start_y: 6
player:
  hp: 54
  strength: 12
  dexterity: 14
  constitution: 12
  accuracy: 2
  evasion: 1
  damage_min: 1
  damage_max: 4
monsters: []
items: []
";
        var loader = new ContentLoader();
        var scenario = loader.LoadScenario(yaml);

        // Defaults must apply — no dungeon_mode field = false
        Assert.That(scenario.DungeonMode, Is.False);
        Assert.That(scenario.GuaranteedSpawns, Is.Null);
        Assert.That(scenario.ScenarioId, Is.EqualTo("legacy"));
    }

    // ─── LevelTemplateRegistry.FromSingleDepth ────────────────────────────────

    [Test]
    public void FromSingleDepth_ReturnsOverrideForConfiguredDepth()
    {
        var levelOverride = new LevelOverride
        {
            GuaranteedSpawns = new GuaranteedSpawns
            {
                Mode = "replace",
                Monsters = new List<SpawnEntry> { new() { Type = "test_orc", CountMin = 1, CountMax = 1 } },
            }
        };

        var registry = LevelTemplateRegistry.FromSingleDepth(1, levelOverride);

        Assert.That(registry.GetLevelOverride(1), Is.SameAs(levelOverride),
            "GetLevelOverride(1) must return the registered override");
    }

    [Test]
    public void FromSingleDepth_ReturnsNullForOtherDepths()
    {
        var levelOverride = new LevelOverride();
        var registry = LevelTemplateRegistry.FromSingleDepth(1, levelOverride);

        Assert.That(registry.GetLevelOverride(2), Is.Null, "Depth 2 is not configured — must return null");
        Assert.That(registry.GetLevelOverride(0), Is.Null, "Depth 0 is not configured — must return null");
    }

    [Test]
    public void FromSingleDepth_ConfiguredDepths_ContainsOnlyRegisteredDepth()
    {
        var registry = LevelTemplateRegistry.FromSingleDepth(3, new LevelOverride());
        Assert.That(registry.ConfiguredDepths, Is.EquivalentTo(new[] { 3 }));
    }

    // ─── Full pipeline: dungeon-mode scenario build ───────────────────────────

    [Test]
    public void DungeonModeScenario_Build_ProducesProceeduralFloor()
    {
        // Build a dungeon-mode scenario programmatically and verify the floor is procedural.
        var (monsters, items, consumables, spellItems) = BuildFactories();

        var levelOverride = new LevelOverride
        {
            Parameters = new GenerationParameters { MaxRooms = 8, MapWidth = 60, MapHeight = 40 },
            GuaranteedSpawns = new GuaranteedSpawns
            {
                Mode = "replace",
                Monsters = new List<SpawnEntry>
                {
                    new() { Type = "test_orc", CountMin = 1, CountMax = 1 },
                },
            }
        };

        var registry = LevelTemplateRegistry.FromSingleDepth(1, levelOverride);
        var builder = new DungeonFloorBuilder(registry, monsters, items, consumables,
            spellItemFactory: spellItems);

        var state = builder.Build(1, new SeededRandom(1337));

        // Floor is procedural
        Assert.That(state.IsDungeonMode, Is.True, "IsDungeonMode must be true");
        Assert.That(state.CurrentDepth, Is.EqualTo(1));

        // Map has rooms — not a flat arena (>1 room expected from procedural generation)
        Assert.That(state.Map.Width, Is.EqualTo(60), "Map width matches override parameters");
        Assert.That(state.Map.Height, Is.EqualTo(40), "Map height matches override parameters");

        // Stair down is present
        Assert.That(state.StairDown, Is.Not.Null, "Dungeon floor must have a stair down");
        Assert.That(state.Map.IsWalkable(state.StairDown!.X, state.StairDown.Y), Is.True);
    }

    [Test]
    public void DungeonModeScenario_GuaranteedMonster_AppearsOnFloor()
    {
        var (monsters, items, consumables, spellItems) = BuildFactories();

        var levelOverride = new LevelOverride
        {
            Parameters = new GenerationParameters { MaxRooms = 6, MapWidth = 60, MapHeight = 40 },
            GuaranteedSpawns = new GuaranteedSpawns
            {
                Mode = "replace",
                Monsters = new List<SpawnEntry>
                {
                    new() { Type = "test_orc", CountMin = 2, CountMax = 2 },
                },
            }
        };

        var registry = LevelTemplateRegistry.FromSingleDepth(1, levelOverride);
        var builder = new DungeonFloorBuilder(registry, monsters, items, consumables,
            spellItemFactory: spellItems);
        var state = builder.Build(1, new SeededRandom(1337));

        Assert.That(state.Monsters, Has.Count.EqualTo(2), "Exactly 2 guaranteed orcs expected");
        Assert.That(state.Monsters.All(m => m.Name == "Test Orc"), Is.True);
    }

    [Test]
    public void DungeonModeScenario_GuaranteedConsumable_AppearsOnFloor()
    {
        var (monsters, items, consumables, spellItems) = BuildFactories();

        var levelOverride = new LevelOverride
        {
            Parameters = new GenerationParameters { MaxRooms = 6, MapWidth = 60, MapHeight = 40 },
            GuaranteedSpawns = new GuaranteedSpawns
            {
                Mode = "replace",
                Items = new List<SpawnEntry>
                {
                    new() { Type = "healing_potion", CountMin = 2, CountMax = 2 },
                },
            }
        };

        var registry = LevelTemplateRegistry.FromSingleDepth(1, levelOverride);
        var builder = new DungeonFloorBuilder(registry, monsters, items, consumables,
            spellItemFactory: spellItems);
        var state = builder.Build(1, new SeededRandom(1337));

        var potions = state.FloorItems.Where(e => e.Name == "Healing Potion").ToList();
        Assert.That(potions, Has.Count.EqualTo(2), "Exactly 2 guaranteed healing potions expected");
    }

    [Test]
    public void DungeonModeScenario_GuaranteedScroll_AppearsOnFloor_WithSpellEffectComponent()
    {
        // Verifies that PlaceGuaranteedSpawns can resolve scroll IDs via SpellItemFactory.
        // This is the regression test for the bug where scroll guaranteed_spawns silently failed.
        var (monsters, items, consumables, spellItems) = BuildFactories();

        var levelOverride = new LevelOverride
        {
            Parameters = new GenerationParameters { MaxRooms = 6, MapWidth = 60, MapHeight = 40 },
            GuaranteedSpawns = new GuaranteedSpawns
            {
                Mode = "replace",
                Items = new List<SpawnEntry>
                {
                    new() { Type = "scroll_of_lightning", CountMin = 1, CountMax = 1 },
                },
            }
        };

        var registry = LevelTemplateRegistry.FromSingleDepth(1, levelOverride);
        var builder = new DungeonFloorBuilder(registry, monsters, items, consumables,
            spellItemFactory: spellItems);
        var state = builder.Build(1, new SeededRandom(1337));

        var scrolls = state.FloorItems.Where(e => e.Name == "Scroll of Lightning").ToList();
        Assert.That(scrolls, Has.Count.EqualTo(1), "Guaranteed scroll_of_lightning must appear on floor");

        var scroll = scrolls[0];
        Assert.That(scroll.Get<SpellEffect>(), Is.Not.Null,
            "Scroll entity must have SpellEffect component");
        Assert.That(scroll.Get<Consumable>(), Is.Not.Null,
            "Scroll entity must have Consumable component (consumed on use)");
    }

    [Test]
    public void DungeonModeScenario_GuaranteedWand_AppearsOnFloor_WithWandComponent()
    {
        // Verifies that PlaceGuaranteedSpawns can resolve wand IDs via SpellItemFactory.
        var (monsters, items, consumables, spellItems) = BuildFactories();

        var levelOverride = new LevelOverride
        {
            Parameters = new GenerationParameters { MaxRooms = 6, MapWidth = 60, MapHeight = 40 },
            GuaranteedSpawns = new GuaranteedSpawns
            {
                Mode = "replace",
                Items = new List<SpawnEntry>
                {
                    new() { Type = "wand_of_confusion", CountMin = 1, CountMax = 1 },
                },
            }
        };

        var registry = LevelTemplateRegistry.FromSingleDepth(1, levelOverride);
        var builder = new DungeonFloorBuilder(registry, monsters, items, consumables,
            spellItemFactory: spellItems);
        var state = builder.Build(1, new SeededRandom(1337));

        var wands = state.FloorItems.Where(e => e.Name == "Wand of Confusion").ToList();
        Assert.That(wands, Has.Count.EqualTo(1), "Guaranteed wand_of_confusion must appear on floor");

        var wand = wands[0];
        Assert.That(wand.Get<WandComponent>(), Is.Not.Null,
            "Wand entity must have WandComponent");
        Assert.That(wand.Get<SpellEffect>(), Is.Not.Null,
            "Wand entity must have SpellEffect component");
    }

    [Test]
    public void DungeonModeScenario_GuaranteedEquipment_AppearsOnFloor()
    {
        var (monsters, items, consumables, spellItems) = BuildFactories();

        var levelOverride = new LevelOverride
        {
            Parameters = new GenerationParameters { MaxRooms = 6, MapWidth = 60, MapHeight = 40 },
            GuaranteedSpawns = new GuaranteedSpawns
            {
                Mode = "replace",
                Equipment = new List<SpawnEntry>
                {
                    new() { Type = "test_sword", CountMin = 1, CountMax = 1 },
                },
            }
        };

        var registry = LevelTemplateRegistry.FromSingleDepth(1, levelOverride);
        var builder = new DungeonFloorBuilder(registry, monsters, items, consumables,
            spellItemFactory: spellItems);
        var state = builder.Build(1, new SeededRandom(1337));

        var swords = state.FloorItems.Where(e => e.Name == "Test Sword").ToList();
        Assert.That(swords, Has.Count.EqualTo(1), "Guaranteed test_sword must appear on floor");
        Assert.That(swords[0].Get<Equippable>(), Is.Not.Null,
            "Equipment entity must have Equippable component");
    }

    [Test]
    public void DungeonModeScenario_Player_HasDefaultStats()
    {
        // Dungeon-mode scenarios use CreateDefaultPlayer() — not scenario-override stats.
        var (monsters, items, consumables, spellItems) = BuildFactories();

        var levelOverride = new LevelOverride
        {
            Parameters = new GenerationParameters { MaxRooms = 6, MapWidth = 60, MapHeight = 40 },
        };

        var registry = LevelTemplateRegistry.FromSingleDepth(1, levelOverride);
        var builder = new DungeonFloorBuilder(registry, monsters, items, consumables,
            spellItemFactory: spellItems);
        var state = builder.Build(1, new SeededRandom(1337));

        var fighter = state.Player.Get<Fighter>();
        Assert.That(fighter, Is.Not.Null, "Player must have Fighter component");
        Assert.That(fighter!.MaxHp, Is.EqualTo(56), "Default player MaxHp");
    }

    [Test]
    public void DungeonModeScenario_AllSpawnTypes_Pipeline_E2E()
    {
        // End-to-end: one of each spawn type (monster, potion, scroll, wand, equipment)
        // all in mode=replace. Verifies the full PlaceGuaranteedSpawns pipeline.
        var (monsters, items, consumables, spellItems) = BuildFactories();

        var levelOverride = new LevelOverride
        {
            Parameters = new GenerationParameters { MaxRooms = 8, MapWidth = 60, MapHeight = 40 },
            GuaranteedSpawns = new GuaranteedSpawns
            {
                Mode = "replace",
                Monsters = new List<SpawnEntry>
                {
                    new() { Type = "test_orc", CountMin = 1, CountMax = 1 },
                },
                Items = new List<SpawnEntry>
                {
                    new() { Type = "healing_potion",   CountMin = 1, CountMax = 1 },
                    new() { Type = "scroll_of_lightning", CountMin = 1, CountMax = 1 },
                    new() { Type = "wand_of_confusion",  CountMin = 1, CountMax = 1 },
                },
                Equipment = new List<SpawnEntry>
                {
                    new() { Type = "test_sword", CountMin = 1, CountMax = 1 },
                },
            }
        };

        var registry = LevelTemplateRegistry.FromSingleDepth(1, levelOverride);
        var builder = new DungeonFloorBuilder(registry, monsters, items, consumables,
            spellItemFactory: spellItems);
        var state = builder.Build(1, new SeededRandom(1337));

        // Monster
        Assert.That(state.Monsters.Any(m => m.Name == "Test Orc"), Is.True, "Test Orc must appear");

        // Potion
        Assert.That(state.FloorItems.Any(e => e.Name == "Healing Potion"), Is.True,
            "Healing Potion must appear");

        // Scroll with SpellEffect
        var scroll = state.FloorItems.FirstOrDefault(e => e.Name == "Scroll of Lightning");
        Assert.That(scroll, Is.Not.Null, "Scroll of Lightning must appear");
        Assert.That(scroll!.Get<SpellEffect>(), Is.Not.Null, "Scroll must have SpellEffect");

        // Wand with WandComponent
        var wand = state.FloorItems.FirstOrDefault(e => e.Name == "Wand of Confusion");
        Assert.That(wand, Is.Not.Null, "Wand of Confusion must appear");
        Assert.That(wand!.Get<WandComponent>(), Is.Not.Null, "Wand must have WandComponent");

        // Equipment
        var sword = state.FloorItems.FirstOrDefault(e => e.Name == "Test Sword");
        Assert.That(sword, Is.Not.Null, "Test Sword must appear");
        Assert.That(sword!.Get<Equippable>(), Is.Not.Null, "Sword must have Equippable");
    }
}
