using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Tests how weapon tier affects pressure invariants.
/// Validates the pressure model diagnosis: "player needs more damage."
/// </summary>
[TestFixture]
public class WeaponProgressionTests
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
  shortsword:
    slot: ""main_hand""
    damage_min: 1
    damage_max: 6
    to_hit_bonus: 0
  longsword:
    slot: ""main_hand""
    damage_min: 1
    damage_max: 8
    to_hit_bonus: 0
  greatsword:
    slot: ""main_hand""
    damage_min: 2
    damage_max: 12
    to_hit_bonus: -1
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
        var content = loader.LoadAll(yaml);
        // Also add the extra weapons
        var items = loader.LoadItems(yaml);
        _harness = new ScenarioHarness(
            new MonsterFactory(content.Monsters, ef),
            new ItemFactory(items, ef),
            new ConsumableFactory(content.Consumables, ef));
    }

    private ScenarioDefinition MakeScenario(string weapon) => new()
    {
        ScenarioId = $"weapon_probe_{weapon}",
        Depth = 2, TurnLimit = 100, Runs = 50,
        Player = new ScenarioPlayer
        {
            Hp = 54, Strength = 12, Dexterity = 14, Constitution = 12,
            Accuracy = 2, Evasion = 1, DamageMin = 1, DamageMax = 4,
            Weapon = weapon, Armor = "leather_armor",
        },
        Monsters = new() { new() { Type = "orc_grunt", Count = 3 } },
        Items = new() { new() { Type = "healing_potion", Count = 2 } },
    };

    [Test]
    public void PrintWeaponProgression()
    {
        var hpmTarget = PressureModel.GetRoundsToKillTarget(2);
        var hmpTarget = PressureModel.GetRoundsToDieTarget(2);

        TestContext.WriteLine("=== Weapon Progression Probe: Depth 2, 3x Orc, 2 Potions ===");
        TestContext.WriteLine($"    RoundsToKill target: {hpmTarget.Min}-{hpmTarget.Max}    RoundsToDie target: {hmpTarget.Min}-{hmpTarget.Max}");
        TestContext.WriteLine("");
        TestContext.WriteLine(string.Format("  {0,-14} {1,7} {2,7} {3,6} {4,6} {5,8} {6,8}",
            "Weapon", "RoundsToKill", "RoundsToDie", "DPR_P", "Death%", "RoundsToKill?", "RoundsToDie?"));

        foreach (var weapon in new[] { "dagger", "shortsword", "longsword", "greatsword" })
        {
            var agg = _harness.Run(MakeScenario(weapon), baseSeed: 1337);
            var pm = PressureModel.Compute(agg, depth: 2, avgMonsterHp: 29, playerMaxHp: 55);

            TestContext.WriteLine(string.Format("  {0,-14} {1,7:F1} {2,7:F1} {3,6:F2} {4,5:P0} {5,8} {6,8}",
                weapon, pm.RoundsToKill, pm.RoundsToDie, pm.DPR_P, pm.DeathRate,
                hpmTarget.Status(pm.RoundsToKill), hmpTarget.Status(pm.RoundsToDie)));
        }

        Assert.Pass();
    }

    [Test]
    public void BetterWeapon_LowerRoundsToKill()
    {
        var dagger = _harness.Run(MakeScenario("dagger"), baseSeed: 1337);
        var longsword = _harness.Run(MakeScenario("longsword"), baseSeed: 1337);

        var pmDagger = PressureModel.Compute(dagger, 2, 29, 55);
        var pmLongsword = PressureModel.Compute(longsword, 2, 29, 55);

        Assert.That(pmLongsword.RoundsToKill, Is.LessThan(pmDagger.RoundsToKill),
            $"Longsword RoundsToKill {pmLongsword.RoundsToKill:F1} should be lower than dagger {pmDagger.RoundsToKill:F1}");
    }

    [Test]
    public void BetterWeapon_LowerDeathRate()
    {
        var dagger = _harness.Run(MakeScenario("dagger"), baseSeed: 1337);
        var longsword = _harness.Run(MakeScenario("longsword"), baseSeed: 1337);

        Assert.That(longsword.DeathRate, Is.LessThanOrEqualTo(dagger.DeathRate),
            $"Longsword death rate {longsword.DeathRate:P0} should be <= dagger {dagger.DeathRate:P0}");
    }
}
