namespace UnderWarden.Logic.Balance;

/// <summary>
/// Pure-logic evaluation functions for the balance suite.
/// Extracted from SuiteRunner so they can be tested without Harness IO dependencies.
///
/// Drift thresholds match PoC THRESHOLDS dict (balance_suite.py:64-70) verbatim.
/// </summary>
public static class BalanceSuiteEvaluator
{
    /// <summary>
    /// Drift thresholds for WARN/FAIL classification.
    /// Verbatim from PoC THRESHOLDS (balance_suite.py:64-70).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (double Warn, double Fail)> Thresholds =
        new Dictionary<string, (double Warn, double Fail)>
        {
            ["death_rate"]            = (0.10, 0.20),
            ["player_hit_rate"]       = (0.05, 0.10),
            ["monster_hit_rate"]      = (0.05, 0.10),
            ["pressure_index"]        = (5.0,  10.0),
            ["bonus_attacks_per_run"] = (2.0,   4.0),
        };

    /// <summary>
    /// Compute per-metric deltas: current - baseline.
    /// Positive delta = metric increased relative to baseline.
    /// PoC: compute_deltas() in balance_suite.py:154-169.
    /// </summary>
    public static Dictionary<string, double> ComputeDeltas(
        NormalizedMetrics current, NormalizedMetrics baseline)
    {
        return new Dictionary<string, double>
        {
            ["death_rate"]            = current.DeathRate            - baseline.DeathRate,
            ["player_hit_rate"]       = current.PlayerHitRate        - baseline.PlayerHitRate,
            ["monster_hit_rate"]      = current.MonsterHitRate       - baseline.MonsterHitRate,
            ["pressure_index"]        = current.PressureIndex        - baseline.PressureIndex,
            ["bonus_attacks_per_run"] = current.BonusAttacksPerRun   - baseline.BonusAttacksPerRun,
        };
    }

    /// <summary>
    /// PASS/WARN/FAIL based on absolute delta magnitudes.
    /// PoC: classify_verdict() in balance_suite.py:172-190.
    ///
    /// Any metric crossing FAIL threshold → FAIL (short-circuit).
    /// Otherwise any metric crossing WARN threshold → WARN.
    /// Else → PASS.
    /// Thresholds apply to |delta| (both positive and negative drift trigger).
    /// </summary>
    public static string ClassifyVerdict(Dictionary<string, double> deltas)
    {
        string verdict = "PASS";
        foreach (var (key, delta) in deltas)
        {
            if (!Thresholds.TryGetValue(key, out var t)) continue;
            double abs = Math.Abs(delta);
            if (abs >= t.Fail) return "FAIL";
            if (abs >= t.Warn) verdict = "WARN";
        }
        return verdict;
    }
}
