using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

[TestFixture]
public class PressureModelTests
{
    [Test]
    public void TargetBands_CorrectForDepths()
    {
        // Prototype bands from PoC target_bands.py (union of per-depth ranges within each band)
        var hpm1 = PressureModel.GetRoundsToKillTarget(1);
        Assert.That(hpm1.Min, Is.EqualTo(6.0));
        Assert.That(hpm1.Max, Is.EqualTo(9.0));

        var hpm3 = PressureModel.GetRoundsToKillTarget(3);
        Assert.That(hpm3.Min, Is.EqualTo(8.0));
        Assert.That(hpm3.Max, Is.EqualTo(11.0));

        // RoundsToDie: depth1 [20-24], depth5 (band 5-6) [16-21]
        var hmp1 = PressureModel.GetRoundsToDieTarget(1);
        Assert.That(hmp1.Min, Is.EqualTo(20.0));
        Assert.That(hmp1.Max, Is.EqualTo(24.0));

        var hmp5 = PressureModel.GetRoundsToDieTarget(5);
        Assert.That(hmp5.Min, Is.EqualTo(16.0));
        Assert.That(hmp5.Max, Is.EqualTo(21.0));
    }

    [Test]
    public void TargetBand_ContainsAndStatus()
    {
        var band = new TargetBand(3.5, 4.5);

        Assert.That(band.Contains(4.0), Is.True);
        Assert.That(band.Contains(3.0), Is.False);
        Assert.That(band.Contains(5.0), Is.False);

        Assert.That(band.Status(4.0), Is.EqualTo("OK"));
        Assert.That(band.Status(3.0), Is.EqualTo("LOW"));
        Assert.That(band.Status(5.0), Is.EqualTo("HIGH"));
    }

    [Test]
    public void Compute_BasicMetrics()
    {
        var agg = new AggregatedMetrics
        {
            ScenarioId = "test",
            TotalRuns = 50,
            AvgTurns = 30,
            AvgPlayerDamageDealt = 60,
            AvgMonsterDamageDealt = 45,
            AvgMonstersKilled = 2,
            DeathRate = 0.2,
        };

        var pm = PressureModel.Compute(agg, depth: 2, avgMonsterHp: 28, playerMaxHp: 55);

        // DPR_P = 60 / 30 = 2.0
        Assert.That(pm.DPR_P, Is.EqualTo(2.0).Within(0.01));
        // DPR_M = 45 / 30 = 1.5
        Assert.That(pm.DPR_M, Is.EqualTo(1.5).Within(0.01));
        // RoundsToKill = 28 / 2.0 = 14.0
        Assert.That(pm.RoundsToKill, Is.EqualTo(14.0).Within(0.01));
        // RoundsToDie = 55 / 1.5 = 36.67
        Assert.That(pm.RoundsToDie, Is.EqualTo(36.67).Within(0.1));
        // DMG/encounter = 45 / 2 = 22.5
        Assert.That(pm.DmgPerEncounter, Is.EqualTo(22.5).Within(0.01));
    }

    [Test]
    public void PressureRatio_Categories()
    {
        // Attrition: RoundsToKill high relative to RoundsToDie (> 0.6)
        var attrition = new PressureMetrics { RoundsToKill = 8, RoundsToDie = 10 };
        Assert.That(attrition.PressureRatio, Is.EqualTo(0.8).Within(0.01));

        // Balanced: 0.3-0.6
        var balanced = new PressureMetrics { RoundsToKill = 4, RoundsToDie = 10 };
        Assert.That(balanced.PressureRatio, Is.EqualTo(0.4).Within(0.01));

        // Spike: < 0.3
        var spike = new PressureMetrics { RoundsToKill = 2, RoundsToDie = 10 };
        Assert.That(spike.PressureRatio, Is.EqualTo(0.2).Within(0.01));
    }
}

/// <summary>
/// Integration: run real scenarios and compute pressure invariants.
/// </summary>
[TestFixture]
public class PressureAnalysisTests
{
    private ScenarioRunner _runner = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var entitiesPath = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", "entities.yaml");
        _runner = ScenarioRunner.FromEntitiesFile(entitiesPath);
    }

    private string ScenarioPath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", "levels", name);

    [Test]
    public void PrintPressureReport()
    {
        // Orc grunt base HP = 28, CON 12 (mod +1) → MaxHP 29 at depth 1-2
        // At depth 5 with 1.25x scaling: ceil(28 * 1.25) = 35, +1 CON = 36
        // Player MaxHP = 54 + CON mod 1 = 55

        var scenarios = new[]
        {
            ("scenario_depth2_orc_baseline.yaml", 2, 29.0),  // orc 28 HP + 1 CON
            ("scenario_depth2_orc_1v1.yaml", 2, 29.0),
            ("scenario_depth5_orc_pressure.yaml", 5, 36.0),  // 28 * 1.25 = 35 ceil + 1 CON
            ("scenario_depth5_zombie.yaml", 5, 25.0),         // zombie 24 HP + 2 CON (14), no scaling at d5
        };

        TestContext.WriteLine("=== Pressure Model Report (seed 1337) ===");
        TestContext.WriteLine("");
        TestContext.WriteLine(string.Format(
            "  {0,-28} {1,6} {2,7} {3,7} {4,7} {5,7} {6,8} {7,8}",
            "Scenario", "Depth", "DPR_P", "DPR_M", "RoundsToKill", "RoundsToDie", "Ratio", "Death%"));

        foreach (var (file, depth, monsterHp) in scenarios)
        {
            var agg = _runner.RunFromFile(ScenarioPath(file));
            var pm = PressureModel.Compute(agg, depth, monsterHp, playerMaxHp: 55);

            var hpmTarget = PressureModel.GetRoundsToKillTarget(depth);
            var hmpTarget = PressureModel.GetRoundsToDieTarget(depth);

            TestContext.WriteLine(string.Format(
                "  {0,-28} {1,6} {2,7:F2} {3,7:F2} {4,7:F1} {5,7:F1} {6,8:F2} {7,7:P0}",
                pm.ScenarioId, depth, pm.DPR_P, pm.DPR_M, pm.RoundsToKill, pm.RoundsToDie,
                pm.PressureRatio, pm.DeathRate));
            TestContext.WriteLine(string.Format(
                "  {0,28} {1,6} {2,7} {3,7} {4,4}-{5,-3}{6} {7,3}-{8,-4}{9}",
                "", "", "", "",
                hpmTarget.Min, hpmTarget.Max, hpmTarget.Status(pm.RoundsToKill),
                hmpTarget.Min, hmpTarget.Max, hmpTarget.Status(pm.RoundsToDie)));
        }

        Assert.Pass();
    }
}
