using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Tests the impact of the momentum/speed system on combat balance.
/// </summary>
[TestFixture]
public class MomentumTests
{
    private ScenarioRunner _runner = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", "entities.yaml");
        _runner = ScenarioRunner.FromEntitiesFile(path);
    }

    private string ScenarioPath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", "levels", name);

    [Test]
    public void SpeedBonus_IncreasesDPR()
    {
        // Both scenario_depth2_orc_baseline and scenario_depth2_orc_speed already default to
        // speed_bonus=0.25 (ScenarioDefinition default), so comparing files doesn't isolate the
        // speed bonus effect. Use inline scenarios that differ ONLY in speed_bonus.
        //
        // With 0% death rate (sequential fights), total damage = sum of enemy HP = constant.
        // The observable effect of speed_bonus is higher total damage (more bonus attacks per turn).
        var monsters = new List<ScenarioMonster>
        {
            new() { Type = "orc_grunt", Count = 1, Position = new[] { 8, 6 } },
            new() { Type = "orc_grunt", Count = 1, Position = new[] { 9, 4 } },
            new() { Type = "orc_grunt", Count = 1, Position = new[] { 10, 6 } },
        };
        var items = new List<ScenarioItem> { new() { Type = "healing_potion", Count = 1 } };

        var noSpeedDef = new ScenarioDefinition
        {
            ScenarioId = "speed_test_no_bonus", Depth = 2, TurnLimit = 100, Runs = 50,
            Player = new ScenarioPlayer
            {
                Hp = 54, Strength = 14, Dexterity = 14, Constitution = 14,
                DamageMin = 1, DamageMax = 4, Weapon = "dagger", Armor = "leather_armor",
                SpeedBonus = 0.0,
            },
            Monsters = monsters, Items = items,
        };
        var withSpeedDef = new ScenarioDefinition
        {
            ScenarioId = "speed_test_with_bonus", Depth = 2, TurnLimit = 100, Runs = 50,
            Player = new ScenarioPlayer
            {
                Hp = 54, Strength = 14, Dexterity = 14, Constitution = 14,
                DamageMin = 1, DamageMax = 4, Weapon = "dagger", Armor = "leather_armor",
                SpeedBonus = 0.25,
            },
            Monsters = monsters, Items = items,
        };

        var noSpeed   = _runner.Run(noSpeedDef,   baseSeed: 1337);
        var withSpeed = _runner.Run(withSpeedDef, baseSeed: 1337);

        // Speed bonus fires bonus attacks when player_speed > orc_speed (0.25 > 0).
        // With speed=0, no bonus attacks fire at all.
        Assert.That(withSpeed.AvgBonusAttacks, Is.GreaterThan(noSpeed.AvgBonusAttacks),
            $"Speed=0.25 bonus attacks {withSpeed.AvgBonusAttacks:F1} should exceed speed=0.0 {noSpeed.AvgBonusAttacks:F1}");
        Assert.That(noSpeed.AvgBonusAttacks, Is.EqualTo(0.0),
            "No bonus attacks expected with speed_bonus=0.0 vs orcs with speed=0.0");
    }

    [Test]
    public void PrintMomentumImpact()
    {
        var baseline = _runner.RunFromFile(ScenarioPath("scenario_depth2_orc_baseline.yaml"));
        var speed = _runner.RunFromFile(ScenarioPath("scenario_depth2_orc_speed.yaml"));

        var pmBase = PressureModel.Compute(baseline, 2, 29, 55);
        var pmSpeed = PressureModel.Compute(speed, 2, 29, 55);

        var hpmTarget = PressureModel.GetRoundsToKillTarget(2);

        TestContext.WriteLine("=== Momentum Impact: Depth 2, 3x Orc ===");
        TestContext.WriteLine($"    RoundsToKill target: {hpmTarget.Min}-{hpmTarget.Max}");
        TestContext.WriteLine("");
        TestContext.WriteLine(string.Format("  {0,-18} {1,10} {2,10}",
            "Metric", "No Speed", "Speed 0.25"));
        TestContext.WriteLine(string.Format("  {0,-18} {1,10:P0} {2,10:P0}",
            "Death Rate", baseline.DeathRate, speed.DeathRate));
        TestContext.WriteLine(string.Format("  {0,-18} {1,10:F1} {2,10:F1}",
            "Avg Turns", baseline.AvgTurns, speed.AvgTurns));
        TestContext.WriteLine(string.Format("  {0,-18} {1,10:F1} {2,10:F1}",
            "Avg Player DMG", baseline.AvgPlayerDamageDealt, speed.AvgPlayerDamageDealt));
        TestContext.WriteLine(string.Format("  {0,-18} {1,10:F1} {2,10:F1}",
            "Avg Kills", baseline.AvgMonstersKilled, speed.AvgMonstersKilled));
        TestContext.WriteLine(string.Format("  {0,-18} {1,10:F2} {2,10:F2}",
            "DPR_P", pmBase.DPR_P, pmSpeed.DPR_P));
        TestContext.WriteLine(string.Format("  {0,-18} {1,10:F1} {2,10:F1}",
            "RoundsToKill", pmBase.RoundsToKill, pmSpeed.RoundsToKill));
        TestContext.WriteLine(string.Format("  {0,-18} {1,10} {2,10}",
            "RoundsToKill Status", hpmTarget.Status(pmBase.RoundsToKill), hpmTarget.Status(pmSpeed.RoundsToKill)));

        Assert.Pass();
    }
}
