using UnderWarden.Logic.Balance;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Step 7 baseline-delta: snapshot a soak (SoakBaseline.FromSummary), persist it (JSON round-trip),
/// and diff the next soak against it (SoakBaselineEvaluator) so a tuning change reads as a signed Δ vs
/// the prior run. Verdict is keyed on death-rate drift, mirroring BalanceSuiteEvaluator. The delta
/// REPORT is tested at the read level — a regressed floor must SAY FAIL and show its drift.
/// </summary>
[TestFixture]
public class SoakBaselineTests
{
    private static DungeonSoakRunResult Run(int seed, int floors, int? deathDepth)
    {
        var per = new List<FloorRunMetrics>();
        for (int d = 1; d <= floors; d++)
        {
            bool died = deathDepth == d;
            per.Add(new FloorRunMetrics
            {
                Depth = d, TurnsTaken = 40, MonstersKilled = 3,
                PlayerHpAtEnd = died ? 0 : 30, PlayerMaxHp = 55,
                PlayerDied = died, Descended = !died,
            });
            if (died) break;
        }
        bool anyDeath = deathDepth != null;
        return new DungeonSoakRunResult
        {
            Seed = seed,
            Outcome = anyDeath ? OutcomeClassifier.Died : OutcomeClassifier.Survived,
            FailureType = anyDeath ? OutcomeClassifier.FailureDeath : OutcomeClassifier.FailureNone,
            FailureDetail = "",
            DeepestFloorReached = per[^1].Depth,
            FloorsCompleted = anyDeath ? deathDepth!.Value - 1 : floors,
            TotalTurns = per.Sum(f => f.TurnsTaken),
            TotalKills = per.Sum(f => f.MonstersKilled),
            FinalHp = anyDeath ? 0 : 30,
            FinalMaxHp = 55,
            FinalHpFraction = anyDeath ? 0.0 : 30.0 / 55.0,
            PerFloor = per,
        };
    }

    [Test]
    public void FromSummary_ComputesPerDepthDeathRate_AndSurvival()
    {
        // 10 runs, 2 floors. 2 die on floor 1; the other 8 reach floor 2 and survive.
        var runs = new List<DungeonSoakRunResult>();
        for (int i = 0; i < 2; i++) runs.Add(Run(i, floors: 2, deathDepth: 1));
        for (int i = 2; i < 10; i++) runs.Add(Run(i, floors: 2, deathDepth: null));
        var summary = DungeonSoakSummary.ComputeFrom(runs, configuredFloors: 2);

        var baseline = SoakBaseline.FromSummary(summary);
        Assert.That(baseline.Runs, Is.EqualTo(10));
        Assert.That(baseline.SurvivalRate, Is.EqualTo(0.8).Within(1e-9));
        Assert.That(baseline.Floors, Has.Count.EqualTo(2));
        Assert.That(baseline.Floors[0].DeathRate, Is.EqualTo(0.2).Within(1e-9), "2/10 died on floor 1");
        Assert.That(baseline.Floors[1].DeathRate, Is.EqualTo(0.0).Within(1e-9), "0/8 died on floor 2");
    }

    [Test]
    public void Json_RoundTrips()
    {
        var original = new SoakBaseline(0.72, 100, new[]
        {
            new SoakFloorMetrics(1, 0.08, 44.5, 5.1, 0.62),
            new SoakFloorMetrics(2, 0.15, 51.0, 6.3, 0.48),
        });
        var restored = SoakBaseline.FromJson(original.ToJson());

        Assert.That(restored.SurvivalRate, Is.EqualTo(0.72));
        Assert.That(restored.Runs, Is.EqualTo(100));
        Assert.That(restored.Floors, Has.Count.EqualTo(2));
        Assert.That(restored.Floors[1].Depth, Is.EqualTo(2));
        Assert.That(restored.Floors[1].DeathRate, Is.EqualTo(0.15));
        Assert.That(restored.Floors[0].AvgTurns, Is.EqualTo(44.5));
    }

    [TestCase(0.08, "PASS")]  // +0.08 < 0.10 warn
    [TestCase(0.13, "WARN")]  // 0.10 ≤ 0.13 < 0.20
    [TestCase(0.20, "FAIL")]  // ≥ 0.20
    [TestCase(-0.22, "FAIL")] // drift FAILs in either direction
    public void ClassifyVerdict_OnDeathRateDriftMagnitude(double delta, string expected)
        => Assert.That(SoakBaselineEvaluator.ClassifyVerdict(delta), Is.EqualTo(expected));

    [Test]
    public void ComputeDeltas_MatchesByDepth_AndFlagsNewDepth()
    {
        var baseline = new SoakBaseline(0.8, 100, new[] { new SoakFloorMetrics(7, 0.12, 40, 5, 0.6) });
        var current  = new SoakBaseline(0.6, 100, new[]
        {
            new SoakFloorMetrics(7, 0.32, 45, 4, 0.4),  // regressed
            new SoakFloorMetrics(8, 0.10, 50, 6, 0.5),  // new depth, no baseline
        });

        var deltas = SoakBaselineEvaluator.ComputeDeltas(current, baseline);
        var d7 = deltas.First(d => d.Depth == 7);
        Assert.That(d7.DeathRateDelta, Is.EqualTo(0.20).Within(1e-9));
        Assert.That(d7.Verdict, Is.EqualTo("FAIL"));
        Assert.That(d7.HadBaseline, Is.True);

        var d8 = deltas.First(d => d.Depth == 8);
        Assert.That(d8.Verdict, Is.EqualTo("NO_BASELINE"));
        Assert.That(d8.HadBaseline, Is.False);

        Assert.That(SoakBaselineEvaluator.OverallVerdict(deltas), Is.EqualTo("FAIL"));
        Assert.That(SoakBaselineEvaluator.SurvivalRateDelta(current, baseline), Is.EqualTo(-0.2).Within(1e-9));
    }

    [Test]
    public void DeltaReport_RegressedFloor_SaysFail_AndShowsDrift()
    {
        var baseline = new SoakBaseline(0.80, 100, new[] { new SoakFloorMetrics(7, 0.12, 40, 5, 0.6) });
        var current  = new SoakBaseline(0.60, 100, new[] { new SoakFloorMetrics(7, 0.32, 45, 4, 0.4) });

        var report = SoakBaselineDeltaReport.Format(current, baseline);
        Assert.That(report, Does.Contain("Soak Delta vs Baseline:"));
        Assert.That(report, Does.Contain("12.0% → 32.0%"), "shows was→now death rate");
        Assert.That(report, Does.Contain("+20.0pp"), "shows the signed death-rate drift in points");
        Assert.That(report, Does.Contain("FAIL"));
        Assert.That(report, Does.Contain("Δ -20.0pp"), "headline survival drift");
        Assert.That(report, Does.Contain("Overall: FAIL"));
    }
}
