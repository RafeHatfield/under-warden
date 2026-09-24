using UnderWarden.Logic.Balance;
using NUnit.Framework;
using DPR = UnderWarden.Logic.Balance.DepthPressureReport;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Tests for DepthPressureReport formatters and DeriveMultiplierRecommendation math.
/// Uses synthetic DepthCurvePoints — no harness runs needed.
/// </summary>
[TestFixture]
public class DepthPressureReportTests
{
    // ── DeriveMultiplierRecommendation ────────────────────────────────────

    [Test]
    public void DeriveMultiplier_WhenObservedInRange_ReturnsMult1AndNoAdjust()
    {
        // Depth 3 provisional RoundsToDie band: [28, 52] — put obs at 38 (center)
        var p = MakePoint(depth: 3, h_pm: 8.0, h_mp: 38.0, dpr_m: 2.0, monsterHitRate: 0.37);
        var r = DPR.DeriveMultiplierRecommendation(p);

        Assert.That(r.AdjustmentNeeded, Is.False);
        Assert.That(r.RecommendedDamageMultiplier, Is.EqualTo(1.0).Within(0.001),
            "In-range RoundsToDie → multiplier 1.0");
    }

    [Test]
    public void DeriveMultiplier_WhenObservedHmpTooHigh_MultGreaterThan1()
    {
        // RoundsToDie = 60 but target band max is ~52 — monsters need to hit harder
        var p = MakePoint(depth: 3, h_pm: 8.0, h_mp: 60.0, dpr_m: 1.0, monsterHitRate: 0.37);
        var r = DPR.DeriveMultiplierRecommendation(p);

        Assert.That(r.AdjustmentNeeded, Is.True);
        Assert.That(r.RecommendedDamageMultiplier, Is.GreaterThan(1.0),
            "RoundsToDie too high → mult > 1 (monsters need more damage)");
    }

    [Test]
    public void DeriveMultiplier_WhenObservedHmpTooLow_MultLessThan1()
    {
        // RoundsToDie = 10 is below min ~28 — monsters currently too lethal, reduce damage
        var p = MakePoint(depth: 3, h_pm: 8.0, h_mp: 10.0, dpr_m: 5.0, monsterHitRate: 0.37);
        var r = DPR.DeriveMultiplierRecommendation(p);

        Assert.That(r.AdjustmentNeeded, Is.True);
        Assert.That(r.RecommendedDamageMultiplier, Is.LessThan(1.0),
            "RoundsToDie too low → mult < 1 (monsters too lethal)");
    }

    // ── FormatScalingDiagnosis categories ────────────────────────────────

    [Test]
    public void FormatScalingDiagnosis_BalancedScaling_WhenHpmRisingAndHmpFalling()
    {
        // RoundsToKill Δ = +0.7 (>0.5), RoundsToDie Δ = -2.0 (<-1.0) → BALANCED SCALING
        var curve = new[]
        {
            MakePoint(depth: 1, h_pm: 7.0, h_mp: 42.0, dpr_m: 1.3, monsterHitRate: 0.37),
            MakePoint(depth: 3, h_pm: 7.7, h_mp: 40.0, dpr_m: 1.5, monsterHitRate: 0.37),
        };
        var report = DPR.FormatScalingDiagnosis(curve);
        Assert.That(report, Does.Contain("BALANCED SCALING"),
            "RoundsToKill Δ > +0.5 and RoundsToDie Δ < -1.0 should diagnose BALANCED SCALING");
    }

    [Test]
    public void FormatScalingDiagnosis_HpHeavy_WhenHpmRisingButHmpStable()
    {
        // RoundsToKill Δ = +0.8 (>0.5), RoundsToDie Δ = +0.3 (between -1 and +1) → HP-HEAVY SCALING
        var curve = new[]
        {
            MakePoint(depth: 1, h_pm: 7.0, h_mp: 40.0, dpr_m: 1.3, monsterHitRate: 0.37),
            MakePoint(depth: 5, h_pm: 7.8, h_mp: 40.3, dpr_m: 1.3, monsterHitRate: 0.37),
        };
        var report = DPR.FormatScalingDiagnosis(curve);
        Assert.That(report, Does.Contain("HP-HEAVY SCALING"),
            "RoundsToKill rising, RoundsToDie stable → HP-HEAVY SCALING");
    }

    [Test]
    public void FormatScalingDiagnosis_SpikeLethality_WhenHpmFlatHmpDropping()
    {
        // RoundsToKill Δ = 0.1 (<0.3), RoundsToDie Δ = -2.0 (<-1.5) → SPIKE LETHALITY
        var curve = new[]
        {
            MakePoint(depth: 1, h_pm: 8.0, h_mp: 42.0, dpr_m: 1.3, monsterHitRate: 0.37),
            MakePoint(depth: 5, h_pm: 8.1, h_mp: 40.0, dpr_m: 1.6, monsterHitRate: 0.37),
        };
        var report = DPR.FormatScalingDiagnosis(curve);
        Assert.That(report, Does.Contain("SPIKE LETHALITY"),
            "RoundsToKill flat, RoundsToDie dropping fast → SPIKE LETHALITY");
    }

    [Test]
    public void FormatScalingDiagnosis_FlatScaling_WhenNeitherMetricChanges()
    {
        // RoundsToKill Δ ≈0, RoundsToDie Δ ≈0 → FLAT SCALING
        var curve = new[]
        {
            MakePoint(depth: 1, h_pm: 8.0, h_mp: 40.0, dpr_m: 1.3, monsterHitRate: 0.37),
            MakePoint(depth: 5, h_pm: 8.1, h_mp: 40.2, dpr_m: 1.3, monsterHitRate: 0.37),
        };
        var report = DPR.FormatScalingDiagnosis(curve);
        Assert.That(report, Does.Contain("FLAT SCALING"),
            "Both metrics stable → FLAT SCALING");
    }

    [Test]
    public void FormatScalingDiagnosis_InsufficientData_ReturnsEarlyMessage()
    {
        var curve = new[] { MakePoint(depth: 1, h_pm: 8.0, h_mp: 40.0, dpr_m: 1.3, monsterHitRate: 0.37) };
        var report = DPR.FormatScalingDiagnosis(curve);
        Assert.That(report, Does.Contain("Insufficient depth data"));
    }

    // ── Attrition indicator ───────────────────────────────────────────────

    [Test]
    public void AttritionIndicator_HighRatio_IsAttrition()
    {
        // RoundsToKill=8, RoundsToDie=12 → ratio 0.667 > 0.6 → ATTRITION
        var curve = new[]
        {
            MakePoint(depth: 1, h_pm: 8.0, h_mp: 12.0, dpr_m: 4.5, monsterHitRate: 0.37),
            MakePoint(depth: 3, h_pm: 8.1, h_mp: 12.1, dpr_m: 4.5, monsterHitRate: 0.37),
        };
        var report = DPR.FormatScalingDiagnosis(curve);
        Assert.That(report, Does.Contain("ATTRITION"),
            "RoundsToKill/RoundsToDie > 0.6 should label as ATTRITION");
    }

    [Test]
    public void AttritionIndicator_LowRatio_IsLethal()
    {
        // RoundsToKill=3, RoundsToDie=40 → ratio 0.075 < 0.3 → LETHAL
        var curve = new[]
        {
            MakePoint(depth: 1, h_pm: 3.0, h_mp: 40.0, dpr_m: 1.3, monsterHitRate: 0.37),
            MakePoint(depth: 3, h_pm: 3.1, h_mp: 40.1, dpr_m: 1.3, monsterHitRate: 0.37),
        };
        var report = DPR.FormatScalingDiagnosis(curve);
        Assert.That(report, Does.Contain("LETHAL"),
            "RoundsToKill/RoundsToDie < 0.3 should label as LETHAL");
    }

    // ── Format methods produce non-empty output ───────────────────────────

    [Test]
    public void FormatPressureTable_ProducesTable()
    {
        var curve = TwoCurveSample();
        var table = DPR.FormatPressureTable(curve);
        Assert.That(table, Does.Contain("OBSERVED DEPTH PRESSURE CURVE"));
        Assert.That(table, Does.Contain("RoundsToKill"));
        Assert.That(table, Does.Contain("RoundsToDie"));
    }

    [Test]
    public void FormatTargetComparison_ProducesComparisonTable()
    {
        var curve = TwoCurveSample();
        var table = DPR.FormatTargetComparison(curve);
        Assert.That(table, Does.Contain("TARGET CURVE COMPARISON"));
    }

    [Test]
    public void FormatMultiplierRecommendations_ProducesTable()
    {
        var curve = TwoCurveSample();
        var table = DPR.FormatMultiplierRecommendations(curve);
        Assert.That(table, Does.Contain("DAMAGE MULTIPLIER RECOMMENDATIONS"));
    }

    [Test]
    public void FormatFullReport_ContainsAllFourSections()
    {
        var curve = TwoCurveSample();
        var report = DPR.FormatFullReport(curve);
        Assert.That(report, Does.Contain("OBSERVED DEPTH PRESSURE CURVE"));
        Assert.That(report, Does.Contain("TARGET CURVE COMPARISON"));
        Assert.That(report, Does.Contain("DAMAGE MULTIPLIER RECOMMENDATIONS"));
        Assert.That(report, Does.Contain("SCALING SYSTEM DIAGNOSIS"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static DPR.DepthCurvePoint MakePoint(
        int depth, double h_pm, double h_mp, double dpr_m,
        double monsterHitRate = 0.37, double dpr_p = 2.5, double deathRate = 0.1)
    {
        return new DPR.DepthCurvePoint(
            Depth:           depth,
            ScenarioId:      $"test_depth{depth}",
            RoundsToKill:            h_pm,
            RoundsToDie:            h_mp,
            DPR_P:           dpr_p,
            DPR_M:           dpr_m,
            PlayerHitRate:   0.70,
            MonsterHitRate:  monsterHitRate,
            DmgPerEncounter: dpr_m * 10,
            TurnsPerKill:    h_pm / dpr_p,
            DeathRate:       deathRate);
    }

    private static List<DPR.DepthCurvePoint> TwoCurveSample() =>
    [
        MakePoint(depth: 1, h_pm: 7.5, h_mp: 42.0, dpr_m: 1.2),
        MakePoint(depth: 3, h_pm: 8.0, h_mp: 38.0, dpr_m: 1.4),
    ];
}
