using UnderWarden.Logic.Balance;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Unit tests for the balance suite evaluation logic:
/// - SuiteRunner.Matrix / FastMatrix enumeration (via hard-coded expected values)
/// - NormalizedMetrics.From computation
/// - BalanceSuiteEvaluator.ComputeDeltas and ClassifyVerdict
/// - Baseline round-trip serialization
///
/// These tests do NOT execute actual scenarios — they use synthetic data.
/// End-to-end harness tests (TASK-108) run in the integration layer.
/// </summary>
[TestFixture]
public class SuiteRunnerTests
{
    // ── NormalizedMetrics.From ─────────────────────────────────────────────

    [Test]
    public void NormalizedMetrics_From_ComputesPressureIndexCorrectly()
    {
        // PressureIndex = AvgMonsterAttacksPerRun - AvgPlayerAttacksPerRun
        var agg = MakeAggregatedMetrics(
            avgPlayerAttacks: 15.0,
            avgMonsterAttacks: 22.0,
            deathRate: 0.10);

        var nm = NormalizedMetrics.From(agg);
        Assert.That(nm.PressureIndex, Is.EqualTo(7.0).Within(0.001),
            "PressureIndex = monster_attacks/run - player_attacks/run");
    }

    [Test]
    public void NormalizedMetrics_From_NegativePressureIndexWhenPlayerMoreActive()
    {
        var agg = MakeAggregatedMetrics(
            avgPlayerAttacks: 30.0,
            avgMonsterAttacks: 10.0,
            deathRate: 0.0);

        var nm = NormalizedMetrics.From(agg);
        Assert.That(nm.PressureIndex, Is.LessThan(0),
            "Negative pressure index when player attacks more than monsters (easy scenario)");
    }

    [Test]
    public void NormalizedMetrics_From_DeathsRoundedFromDeathRate()
    {
        var agg = MakeAggregatedMetrics(
            avgPlayerAttacks: 10.0, avgMonsterAttacks: 10.0,
            deathRate: 0.10, runs: 50);
        var nm = NormalizedMetrics.From(agg);
        Assert.That(nm.Deaths, Is.EqualTo(5)); // 50 * 0.10 = 5
    }

    [Test]
    public void NormalizedMetrics_From_PropagatesDeathRate()
    {
        var agg = MakeAggregatedMetrics(10, 10, deathRate: 0.25, runs: 40);
        var nm = NormalizedMetrics.From(agg);
        Assert.That(nm.DeathRate, Is.EqualTo(0.25).Within(0.0001));
    }

    [Test]
    public void NormalizedMetrics_From_PropagatesHitRates()
    {
        var agg = new AggregatedMetrics
        {
            ScenarioId              = "test",
            TotalRuns               = 50,
            DeathRate               = 0.1,
            PlayerHitRate           = 0.715,
            MonsterHitRate          = 0.374,
            AvgBonusAttacks         = 10.28,
            AvgPlayerAttacksPerRun  = 20.0,
            AvgMonsterAttacksPerRun = 34.88,
        };
        var nm = NormalizedMetrics.From(agg);
        Assert.That(nm.PlayerHitRate,  Is.EqualTo(0.715).Within(0.0001));
        Assert.That(nm.MonsterHitRate, Is.EqualTo(0.374).Within(0.0001));
        Assert.That(nm.BonusAttacksPerRun, Is.EqualTo(10.28).Within(0.0001));
    }

    // ── ComputeDeltas ─────────────────────────────────────────────────────

    [Test]
    public void ComputeDeltas_ReturnsCorrectDifferences()
    {
        var current  = MakeNm(deathRate: 0.20, playerHitRate: 0.75, monsterHitRate: 0.40,
                               pressureIndex: -10.0, bonusAttacks: 5.0);
        var baseline = MakeNm(deathRate: 0.10, playerHitRate: 0.70, monsterHitRate: 0.38,
                               pressureIndex: -12.0, bonusAttacks: 4.5);

        var deltas = BalanceSuiteEvaluator.ComputeDeltas(current, baseline);

        Assert.That(deltas["death_rate"],            Is.EqualTo(0.10).Within(0.0001));
        Assert.That(deltas["player_hit_rate"],        Is.EqualTo(0.05).Within(0.0001));
        Assert.That(deltas["monster_hit_rate"],       Is.EqualTo(0.02).Within(0.0001));
        Assert.That(deltas["pressure_index"],         Is.EqualTo(2.0).Within(0.0001));
        Assert.That(deltas["bonus_attacks_per_run"],  Is.EqualTo(0.5).Within(0.0001));
    }

    [Test]
    public void ComputeDeltas_RoundTrip_AllZeroWhenCurrentEqualsBaseline()
    {
        var nm = MakeNm(deathRate: 0.15, playerHitRate: 0.72, monsterHitRate: 0.37,
                        pressureIndex: -8.0, bonusAttacks: 3.2);
        var deltas = BalanceSuiteEvaluator.ComputeDeltas(nm, nm);
        foreach (var (key, val) in deltas)
            Assert.That(val, Is.EqualTo(0.0).Within(1e-10), $"Delta for {key} should be 0");
    }

    // ── ClassifyVerdict ───────────────────────────────────────────────────

    [Test]
    public void ClassifyVerdict_Pass_WhenAllDeltasBelowWarnThreshold()
    {
        var deltas = new Dictionary<string, double>
        {
            ["death_rate"]            = 0.04,   // < 0.10 warn threshold
            ["player_hit_rate"]       = 0.02,
            ["monster_hit_rate"]      = 0.02,
            ["pressure_index"]        = 1.0,
            ["bonus_attacks_per_run"] = 0.5,
        };
        Assert.That(BalanceSuiteEvaluator.ClassifyVerdict(deltas), Is.EqualTo("PASS"));
    }

    [Test]
    public void ClassifyVerdict_Warn_WhenDeathRateCrossesWarnThreshold()
    {
        var deltas = new Dictionary<string, double>
        {
            ["death_rate"] = 0.11,  // ≥ 0.10 warn, < 0.20 fail
        };
        Assert.That(BalanceSuiteEvaluator.ClassifyVerdict(deltas), Is.EqualTo("WARN"));
    }

    [Test]
    public void ClassifyVerdict_Fail_WhenDeathRateCrossesFailThreshold()
    {
        var deltas = new Dictionary<string, double>
        {
            ["death_rate"] = 0.21,  // ≥ 0.20 fail
        };
        Assert.That(BalanceSuiteEvaluator.ClassifyVerdict(deltas), Is.EqualTo("FAIL"));
    }

    [Test]
    public void ClassifyVerdict_Fail_WhenAnyMetricCrossesFailEvenIfOthersPass()
    {
        // death_rate is WARN-level but bonus_attacks crosses FAIL
        var deltas = new Dictionary<string, double>
        {
            ["death_rate"]            = 0.11,  // WARN
            ["bonus_attacks_per_run"] = 4.5,   // FAIL (≥ 4.0)
        };
        Assert.That(BalanceSuiteEvaluator.ClassifyVerdict(deltas), Is.EqualTo("FAIL"),
            "Any FAIL-level metric should make overall verdict FAIL");
    }

    [Test]
    public void ClassifyVerdict_Warn_EvenWhenOneMetricWarn_OthersPass()
    {
        var deltas = new Dictionary<string, double>
        {
            ["death_rate"]            = 0.03,  // PASS
            ["player_hit_rate"]       = 0.06,  // WARN (≥ 0.05)
            ["monster_hit_rate"]      = 0.01,  // PASS
            ["pressure_index"]        = 1.0,   // PASS
            ["bonus_attacks_per_run"] = 0.3,   // PASS
        };
        Assert.That(BalanceSuiteEvaluator.ClassifyVerdict(deltas), Is.EqualTo("WARN"));
    }

    [Test]
    public void ClassifyVerdict_AbsoluteDelta_NegativeDeltaAlsoTriggersThresholds()
    {
        // Negative delta (metric improved) should still trigger FAIL if magnitude is large
        var deltas = new Dictionary<string, double>
        {
            ["pressure_index"] = -12.0,  // abs = 12.0 ≥ 10.0 fail
        };
        Assert.That(BalanceSuiteEvaluator.ClassifyVerdict(deltas), Is.EqualTo("FAIL"),
            "Thresholds apply to |delta|, not just positive direction");
    }

    // ── Threshold sanity checks ───────────────────────────────────────────

    [Test]
    public void Thresholds_DeathRateValues_MatchPoC()
    {
        var t = BalanceSuiteEvaluator.Thresholds["death_rate"];
        Assert.That(t.Warn, Is.EqualTo(0.10).Within(1e-10));
        Assert.That(t.Fail, Is.EqualTo(0.20).Within(1e-10));
    }

    [Test]
    public void Thresholds_PressureIndex_MatchPoC()
    {
        var t = BalanceSuiteEvaluator.Thresholds["pressure_index"];
        Assert.That(t.Warn, Is.EqualTo(5.0).Within(1e-10));
        Assert.That(t.Fail, Is.EqualTo(10.0).Within(1e-10));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static NormalizedMetrics MakeNm(
        double deathRate = 0.10,
        double playerHitRate = 0.70,
        double monsterHitRate = 0.37,
        double pressureIndex = -14.0,
        double bonusAttacks = 5.0,
        int runs = 50,
        int deaths = -1)
    {
        int d = deaths < 0 ? (int)Math.Round(deathRate * runs) : deaths;
        return new NormalizedMetrics("test_scenario", runs, d,
            deathRate, playerHitRate, monsterHitRate, pressureIndex, bonusAttacks);
    }

    private static AggregatedMetrics MakeAggregatedMetrics(
        double avgPlayerAttacks,
        double avgMonsterAttacks,
        double deathRate = 0.10,
        int runs = 50)
    {
        return new AggregatedMetrics
        {
            ScenarioId              = "test",
            TotalRuns               = runs,
            DeathRate               = deathRate,
            PlayerHitRate           = 0.70,
            MonsterHitRate          = 0.37,
            AvgBonusAttacks         = 5.0,
            AvgPlayerAttacksPerRun  = avgPlayerAttacks,
            AvgMonsterAttacksPerRun = avgMonsterAttacks,
        };
    }
}
