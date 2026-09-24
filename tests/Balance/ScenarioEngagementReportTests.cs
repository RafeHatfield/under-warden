using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// READ-LEVEL tests for ScenarioEngagementReport. Same outcome-testing discipline that protects the
/// classifiers and the dungeon soak report: a controlled scenario with a known composition and known
/// intended verdict must make the report SAY that verdict and attribute the right lever — so the
/// Layer-1 engagement reading can't silently start mislabeling.
///
/// Most tests are synthetic (no engine) so they run in the fast suite. The one Slow test exercises the
/// real engine on a small scenario to confirm EngagementTracker fires end-to-end.
/// </summary>
[TestFixture]
public class ScenarioEngagementReportTests
{
    // B1 targets: 5-15% death band; baseline hits-to-down 9, spike 3. Lever expectations "normal".
    private static TargetTable B1Targets() => new(new[]
    {
        new TargetRegion("B1", 1, 5, new TargetBand(0.05, 0.15),
            new Dictionary<ThreatArchetype, ArchetypeTarget>
            {
                [ThreatArchetype.Baseline]  = new(9),
                [ThreatArchetype.Spike]     = new(3),
                [ThreatArchetype.Escalator] = new(4),
                [ThreatArchetype.Fused]     = new(3),
            },
            new LeverExpectation(DamagePerHit: 5.0, KillerHitRate: 0.5,
                CounterattacksLanded: 4.0, DistinctAttackers: 2.0, AttackFrequency: 1.0)),
    });

    // Build minimal AggregatedMetrics from a death-rate + death records. No engine needed.
    private static AggregatedMetrics Metrics(string id, int depth, double deathRate,
        IReadOnlyList<PlayerDeathRecord> deaths, bool hasSpike = false)
        => new()
        {
            ScenarioId = id,
            Depth      = depth,
            TotalRuns  = 100,
            DeathRate  = deathRate,
            MonsterHitRate = 0.5,
            PlayerHitRate  = 0.6,
            TtkHits = 5.0, TtdHits = 4.0,
            AvgTurns = 45.0,
            Deaths   = deaths,
            HasSpike = hasSpike,
        };

    private static PlayerDeathRecord Death(ThreatArchetype arch, int hits = 9,
        double dmgPerHit = 5.0, double hitRate = 0.5, int counters = 4, int attackers = 2)
        => new()
        {
            Depth = 1, KillerArchetype = arch, HitsToDown = hits,
            DamagePerHit = dmgPerHit, KillerHitRate = hitRate,
            CounterattacksLanded = counters, DistinctAttackers = attackers, EngagementTurns = hits,
        };

    [Test]
    public void HealthyComposition_SaysHealthy_NoLeverLine()
    {
        // 2-orc control: 8% death in band → Healthy.
        var m = Metrics("b1_orc_2", 1, 0.08, new[] { Death(ThreatArchetype.Baseline) });
        var report = ScenarioEngagementReport.Format(m, B1Targets());
        Assert.That(report, Does.Contain("Healthy"));
        Assert.That(report, Does.Not.Contain("levers:"), "healthy engagement must not print lever attribution");
    }

    [Test]
    public void TooHardComposition_SaysTooHard_WithLeverLine()
    {
        // 5-orc scenario: 80% death well above band.
        var deaths = Enumerable.Range(0, 80)
            .Select(_ => Death(ThreatArchetype.Baseline, hits: 9, dmgPerHit: 5.0, attackers: 5))
            .ToList();
        var m = Metrics("b1_orc_5", 1, 0.80, deaths);
        var report = ScenarioEngagementReport.Format(m, B1Targets());
        Assert.That(report, Does.Contain("TooHard"));
        Assert.That(report, Does.Contain("levers:"));
    }

    [Test]
    public void HighHitRate_AttributesToArmor_NotMonsterDamage()
    {
        // The scenario-level proof: normal per-hit damage + high hit-rate → Armor.
        // In a CONTROLLED scenario this is unambiguous — the armor signal isn't contaminated by
        // bot-pulled chaos, unlike the full-soak "Density" reading that prompted the Layer-1 reframe.
        var deaths = Enumerable.Range(0, 60)
            .Select(_ => Death(ThreatArchetype.Baseline, hits: 9, dmgPerHit: 5.0, hitRate: 0.90))
            .ToList();
        var m = Metrics("b1_armor_test", 1, 0.60, deaths);
        var report = ScenarioEngagementReport.Format(m, B1Targets());
        Assert.That(report, Does.Contain("Armor"));
        Assert.That(report, Does.Not.Contain("MonsterDamage"),
            "normal per-hit damage must NOT implicate the monster-damage lever");
    }

    [Test]
    public void HighDensity_AttributesToDensity()
    {
        // 6 distinct attackers (above expected 2) → Density lever.
        var deaths = Enumerable.Range(0, 50)
            .Select(_ => Death(ThreatArchetype.Baseline, attackers: 6))
            .ToList();
        var report = ScenarioEngagementReport.Format(Metrics("b1_density", 1, 0.50, deaths), B1Targets());
        Assert.That(report, Does.Contain("Density"));
    }

    [Test]
    public void FastBaselineDeaths_SayBaselineBroken_InBandButSecretlyLethal()
    {
        // 10% death (IN band) but the deaths are fast-to-baseline → BaselineBroken verdict.
        var deaths = Enumerable.Range(0, 10)
            .Select(_ => Death(ThreatArchetype.Baseline, hits: 2, dmgPerHit: 12.0)) // 2 hits << target 9
            .ToList();
        var report = ScenarioEngagementReport.Format(Metrics("b1_broken_baseline", 1, 0.10, deaths), B1Targets());
        Assert.That(report, Does.Contain("BaselineBroken"));
        Assert.That(report, Does.Contain("MonsterDamage"),
            "fast death with high damage-per-hit → monster-damage lever");
    }

    [Test]
    public void ReportIncludesPressureMetrics()
    {
        var report = ScenarioEngagementReport.Format(
            Metrics("b1_orc_4", 1, 0.42, new[] { Death(ThreatArchetype.Baseline) }), B1Targets());
        Assert.That(report, Does.Contain("TtkHits"));
        Assert.That(report, Does.Contain("TtdHits"));
    }

    /// <summary>
    /// Full-stack real-engine test: run b1_orc_2 (the control scenario) and confirm the
    /// EngagementTracker fires end-to-end, per-death records are populated, and the report renders.
    /// </summary>
    [Test, Category("Slow")]
    public void RealEngine_B1Orc2_EngagementTrackerPopulatesDeathRecords()
    {
        // ScenarioRunner owns entity loading; use RunFromFile to exercise the full path.
        var runner  = ScenarioRunner.FromEntitiesFile(FindPath("config", "entities.yaml"));
        var metrics = runner.RunFromFile(FindPath("config", "levels", "scenario_b1_orc_2.yaml"), baseSeed: 1337);
        var table   = TargetTableLoader.FromFile(FindPath("config", "balance", "target_table.yaml"));
        var report  = ScenarioEngagementReport.Format(metrics, table);

        Assert.That(report, Does.Contain("b1_orc_2"));
        Assert.That(report, Does.Contain("Death%"));
        // Deaths from orc_grunt must carry Baseline archetype (the whole point of the bridge).
        foreach (var d in metrics.Deaths)
            Assert.That(d.KillerArchetype, Is.EqualTo(ThreatArchetype.Baseline),
                "orc_grunt kills must attribute to Baseline archetype");
    }

    private static string FindPath(params string[] parts)
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var rel = Path.Combine(parts);
        var p = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", rel));
        if (File.Exists(p)) return p;
        p = Path.GetFullPath(Path.Combine(testDir, rel));
        if (File.Exists(p)) return p;
        throw new FileNotFoundException($"{rel} not found");
    }
}
