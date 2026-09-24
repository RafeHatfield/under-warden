using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Runs the orc baseline scenario at multiple depths to validate
/// that depth scaling produces increasing pressure.
/// </summary>
[TestFixture]
public class DepthProbeTests
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
      dexterity: 10
      constitution: 12
      accuracy: 4
      evasion: 1
    char: ""o""
    color: [63, 127, 63]
    blocks: true
    faction: ""orc""
    tags: [""humanoid"", ""living""]
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

    private ScenarioDefinition MakeProbe(int depth) => new()
    {
        ScenarioId = $"depth{depth}_orc_probe",
        Depth = depth,
        TurnLimit = 100,
        Runs = 50,
        Player = new ScenarioPlayer
        {
            Hp = 54, Strength = 12, Dexterity = 14, Constitution = 12,
            Accuracy = 2, Evasion = 1, DamageMin = 1, DamageMax = 4,
            Weapon = "dagger", Armor = "leather_armor",
        },
        Monsters = new() { new() { Type = "orc_grunt", Count = 3 } },
        Items = new() { new() { Type = "healing_potion", Count = 1 } },
    };

    [Test]
    public void DeathRate_IncreasesWithDepth()
    {
        var d2 = _harness.Run(MakeProbe(2), baseSeed: 1337);
        var d5 = _harness.Run(MakeProbe(5), baseSeed: 1337);
        var d9 = _harness.Run(MakeProbe(9), baseSeed: 1337);

        // Deeper depths should be harder (higher death rate)
        Assert.That(d5.DeathRate, Is.GreaterThanOrEqualTo(d2.DeathRate),
            $"Depth 5 ({d5.DeathRate:P0}) should be >= Depth 2 ({d2.DeathRate:P0})");
        Assert.That(d9.DeathRate, Is.GreaterThanOrEqualTo(d5.DeathRate),
            $"Depth 9 ({d9.DeathRate:P0}) should be >= Depth 5 ({d5.DeathRate:P0})");
    }

    [Test]
    public void PrintDepthCurve()
    {
        TestContext.WriteLine("=== Depth Pressure Curve (3x orc + dagger + leather + 1 potion) ===");
        TestContext.WriteLine("");
        TestContext.WriteLine(string.Format("  {0,-8} {1,10} {2,10} {3,12} {4,12} {5,10}",
            "Depth", "Death%", "AvgTurns", "PlayerHit%", "MonsterHit%", "AvgKills"));

        foreach (int depth in new[] { 1, 2, 3, 5, 7, 9 })
        {
            var agg = _harness.Run(MakeProbe(depth), baseSeed: 1337);
            TestContext.WriteLine(string.Format("  {0,-8} {1,9:P0} {2,10:F1} {3,11:P0} {4,12:P0} {5,10:F1}",
                depth, agg.DeathRate, agg.AvgTurns, agg.PlayerHitRate, agg.MonsterHitRate, agg.AvgMonstersKilled));
        }

        Assert.Pass();
    }
}
