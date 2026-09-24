using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

[TestFixture]
public class GameStateFactoryTests
{
    private MonsterFactory _monsterFactory = null!;
    private ItemFactory _itemFactory = null!;
    private ConsumableFactory _consumableFactory = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        const string yaml = @"
monsters:
  orc_grunt:
    name: ""Orc""
    stats:
      hp: 28
      xp: 35
      damage_min: 4
      damage_max: 6
      strength: 14
      dexterity: 10
      constitution: 12
      accuracy: 4
      evasion: 1
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
        var loader = new ContentLoader();
        var bundle = loader.LoadAll(yaml);
        var entityFactory = new EntityFactory();
        _monsterFactory = new MonsterFactory(bundle.Monsters, entityFactory);
        _itemFactory = new ItemFactory(bundle.Items, entityFactory);
        _consumableFactory = new ConsumableFactory(bundle.Consumables, entityFactory);
    }

    [Test]
    public void FromScenario_CreatesPlayer_AtCorrectPosition()
    {
        var scenario = CreateBasicScenario();
        var state = GameStateFactory.FromScenario(scenario, 1337, _monsterFactory);

        Assert.That(state.Player.X, Is.EqualTo(3));
        Assert.That(state.Player.Y, Is.EqualTo(6));
        Assert.That(state.Player.Name, Is.EqualTo("Player"));
        Assert.That(state.PlayerFighter.IsAlive, Is.True);
    }

    [Test]
    public void FromScenario_CreatesMonsters()
    {
        var scenario = CreateBasicScenario();
        var state = GameStateFactory.FromScenario(scenario, 1337, _monsterFactory);

        Assert.That(state.Monsters, Has.Count.EqualTo(2));
        Assert.That(state.Monsters[0].Name, Is.EqualTo("Orc"));
        Assert.That(state.Monsters[1].Name, Is.EqualTo("Orc"));
    }

    [Test]
    public void FromScenario_MonsterPositions_AreDistinct()
    {
        var scenario = CreateBasicScenario();
        var state = GameStateFactory.FromScenario(scenario, 1337, _monsterFactory);

        var positions = state.Monsters.Select(m => (m.X, m.Y)).ToList();
        Assert.That(positions.Distinct().Count(), Is.EqualTo(positions.Count),
            "Each monster should have a unique position");
    }

    [Test]
    public void FromScenario_WithEquipment_EquipsPlayerWeaponAndArmor()
    {
        var scenario = CreateBasicScenario();
        scenario.Player.Weapon = "short_sword";
        scenario.Player.Armor = "leather_armor";

        var state = GameStateFactory.FromScenario(scenario, 1337, _monsterFactory, _itemFactory);

        var equipment = state.Player.Get<Equipment>();
        Assert.That(equipment, Is.Not.Null);
        Assert.That(equipment!.MainHand, Is.Not.Null);
        Assert.That(equipment.Chest, Is.Not.Null);
    }

    [Test]
    public void FromScenario_WithPotions_AddsToInventory()
    {
        var scenario = CreateBasicScenario();
        scenario.Items.Add(new ScenarioItem { Type = "healing_potion", Count = 3 });

        var state = GameStateFactory.FromScenario(
            scenario, 1337, _monsterFactory, _itemFactory, _consumableFactory);

        var inventory = state.PlayerInventory;
        Assert.That(inventory, Is.Not.Null);
        // With stacking active, 3 healing potions of the same name occupy a single slot.
        Assert.That(inventory!.Count, Is.EqualTo(1), "Same-named consumables stack into one slot");
        Assert.That(inventory.Items[0].Require<Consumable>().StackSize, Is.EqualTo(3),
            "StackSize should be 3 — all three potions merged into the slot");
    }

    [Test]
    public void FromScenario_MapIsArena_12x12()
    {
        var scenario = CreateBasicScenario();
        var state = GameStateFactory.FromScenario(scenario, 1337, _monsterFactory);

        Assert.That(state.Map.Width, Is.EqualTo(12));
        Assert.That(state.Map.Height, Is.EqualTo(12));
    }

    [Test]
    public void FromScenario_Deterministic_SameSeedSameState()
    {
        var scenario = CreateBasicScenario();
        var s1 = GameStateFactory.FromScenario(scenario, 1337, _monsterFactory);
        var s2 = GameStateFactory.FromScenario(scenario, 1337, _monsterFactory);

        Assert.That(s1.Monsters.Count, Is.EqualTo(s2.Monsters.Count));
        for (int i = 0; i < s1.Monsters.Count; i++)
        {
            Assert.That(s1.Monsters[i].X, Is.EqualTo(s2.Monsters[i].X));
            Assert.That(s1.Monsters[i].Y, Is.EqualTo(s2.Monsters[i].Y));
        }
    }

    [Test]
    public void FromScenario_TurnLimitFromScenario()
    {
        var scenario = CreateBasicScenario();
        scenario.TurnLimit = 50;
        var state = GameStateFactory.FromScenario(scenario, 1337, _monsterFactory);

        Assert.That(state.TurnLimit, Is.EqualTo(50));
    }

    private static ScenarioDefinition CreateBasicScenario() => new()
    {
        ScenarioId = "test_basic",
        Name = "Test Basic",
        Depth = 1,
        TurnLimit = 100,
        Runs = 1,
        Player = new ScenarioPlayer
        {
            Hp = 54, Strength = 12, Dexterity = 14, Constitution = 12,
            Accuracy = 2, Evasion = 1, DamageMin = 1, DamageMax = 4,
        },
        Monsters = new List<ScenarioMonster>
        {
            new() { Type = "orc_grunt", Count = 2 },
        },
        Items = new List<ScenarioItem>(),
    };
}
