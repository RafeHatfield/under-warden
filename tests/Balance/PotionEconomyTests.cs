using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Tests how potion count affects survival at depth 2 baseline.
/// Isolates potion economy from bot intelligence.
/// </summary>
[TestFixture]
public class PotionEconomyTests
{
    private ScenarioHarness _harness = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        const string yaml = @"
monsters:
  orc:
    stats:
      hp: 28
      xp: 35
      damage_min: 4
      damage_max: 6
      strength: 14
      constitution: 12
      accuracy: 4
      evasion: 1
    char: ""o""
    color: [63, 127, 63]
    blocks: true
    tags: [""humanoid""]
    etp_base: 27
  orc_grunt:
    name: ""Orc""
    extends: orc
weapons:
  dagger:
    slot: ""main_hand""
    damage_min: 1
    damage_max: 4
    to_hit_bonus: 1
armor:
  leather_armor:
    slot: ""chest""
    armor_class_bonus: 2
consumables:
  healing_potion:
    heal_amount: 40
";
        var loader = new ContentLoader();
        var ef = new EntityFactory();
        _harness = new ScenarioHarness(
            new MonsterFactory(loader.LoadMonsters(yaml), ef),
            new ItemFactory(loader.LoadItems(yaml), ef),
            new ConsumableFactory(loader.LoadConsumables(yaml), ef));
    }

    private ScenarioDefinition MakeScenario(int potions) => new()
    {
        ScenarioId = $"depth2_3orc_{potions}pot",
        Depth = 2, TurnLimit = 100, Runs = 50,
        Player = new ScenarioPlayer
        {
            Hp = 54, Strength = 12, Dexterity = 14, Constitution = 12,
            Accuracy = 2, Evasion = 1, DamageMin = 1, DamageMax = 4,
            Weapon = "dagger", Armor = "leather_armor",
        },
        Monsters = new() { new() { Type = "orc_grunt", Count = 3 } },
        Items = potions > 0
            ? new() { new() { Type = "healing_potion", Count = potions } }
            : new(),
    };

    [Test]
    public void PrintPotionEconomy()
    {
        TestContext.WriteLine("=== Potion Economy: Depth 2, 3x Orc Grunt (seed 1337, 50 runs) ===");
        TestContext.WriteLine("");
        TestContext.WriteLine(string.Format("  {0,-10} {1,10} {2,10} {3,10} {4,10}",
            "Potions", "Death%", "AvgTurns", "AvgKills", "AvgPotUsed"));

        foreach (int potions in new[] { 0, 1, 2, 3, 5 })
        {
            var agg = _harness.Run(MakeScenario(potions), baseSeed: 1337);
            var singleRuns = new List<RunMetrics>();
            for (int i = 0; i < 50; i++)
                singleRuns.Add(_harness.RunOnce(MakeScenario(potions), 1337 + i));
            double avgPotUsed = singleRuns.Average(r => r.PotionsUsed);

            TestContext.WriteLine(string.Format("  {0,-10} {1,9:P0} {2,10:F1} {3,10:F1} {4,10:F1}",
                potions, agg.DeathRate, agg.AvgTurns, agg.AvgMonstersKilled, avgPotUsed));
        }

        Assert.Pass();
    }

    [Test]
    public void MorePotions_LowerDeathRate()
    {
        var zero = _harness.Run(MakeScenario(0), baseSeed: 1337);
        var three = _harness.Run(MakeScenario(3), baseSeed: 1337);

        Assert.That(three.DeathRate, Is.LessThan(zero.DeathRate),
            $"3 potions ({three.DeathRate:P0}) should be better than 0 ({zero.DeathRate:P0})");
    }
}
