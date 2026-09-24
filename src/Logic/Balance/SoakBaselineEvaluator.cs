namespace UnderWarden.Logic.Balance;

/// <summary>
/// Pure delta + verdict logic for soak-vs-baseline comparison. Soak analogue of BalanceSuiteEvaluator:
/// current − baseline per depth, with PASS/WARN/FAIL keyed on death-rate drift (the balance verdict's
/// own axis). Extracted from any IO so it is unit-testable without a harness run.
/// </summary>
public static class SoakBaselineEvaluator
{
    /// <summary>Death-rate drift thresholds for WARN/FAIL — verbatim from the suite's "death_rate" row.</summary>
    public static readonly (double Warn, double Fail) DeathRateDrift = (0.10, 0.20);

    /// <summary>One depth's deltas vs baseline. Verdict is keyed on |death-rate drift| (the balance axis).</summary>
    public sealed record FloorDelta(
        int Depth,
        double DeathRateDelta,
        double AvgTurnsDelta,
        double AvgKillsDelta,
        string Verdict,
        bool HadBaseline);

    /// <summary>PASS / WARN / FAIL on the magnitude of death-rate drift (both directions count).</summary>
    public static string ClassifyVerdict(double deathRateDelta)
    {
        double abs = Math.Abs(deathRateDelta);
        if (abs >= DeathRateDrift.Fail) return "FAIL";
        if (abs >= DeathRateDrift.Warn) return "WARN";
        return "PASS";
    }

    /// <summary>
    /// Per-depth deltas (current − baseline). Depths present in current but not baseline get verdict
    /// "NO_BASELINE" and zero deltas (a newly-reached depth has nothing to compare against).
    /// </summary>
    public static IReadOnlyList<FloorDelta> ComputeDeltas(SoakBaseline current, SoakBaseline baseline)
    {
        var baseByDepth = baseline.Floors.ToDictionary(f => f.Depth);
        var deltas = new List<FloorDelta>(current.Floors.Count);

        foreach (var cur in current.Floors)
        {
            if (baseByDepth.TryGetValue(cur.Depth, out var b))
            {
                double drDelta = cur.DeathRate - b.DeathRate;
                deltas.Add(new FloorDelta(
                    Depth: cur.Depth,
                    DeathRateDelta: drDelta,
                    AvgTurnsDelta: cur.AvgTurns - b.AvgTurns,
                    AvgKillsDelta: cur.AvgKills - b.AvgKills,
                    Verdict: ClassifyVerdict(drDelta),
                    HadBaseline: true));
            }
            else
            {
                deltas.Add(new FloorDelta(cur.Depth, 0, 0, 0, "NO_BASELINE", false));
            }
        }

        return deltas;
    }

    /// <summary>Headline survival-rate drift (current − baseline). Positive = more runs survived.</summary>
    public static double SurvivalRateDelta(SoakBaseline current, SoakBaseline baseline)
        => current.SurvivalRate - baseline.SurvivalRate;

    /// <summary>The worst per-floor verdict across the run (FAIL &gt; WARN &gt; PASS; NO_BASELINE ignored).</summary>
    public static string OverallVerdict(IReadOnlyList<FloorDelta> deltas)
    {
        string worst = "PASS";
        foreach (var d in deltas)
        {
            if (d.Verdict == "FAIL") return "FAIL";
            if (d.Verdict == "WARN") worst = "WARN";
        }
        return worst;
    }
}
