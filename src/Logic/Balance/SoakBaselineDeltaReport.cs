using System.Text;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Renders the "Soak Delta vs Baseline" section: per-depth death-rate / turns / kills drift of the
/// current soak against the stored baseline, plus the headline survival-rate drift and an overall
/// PASS/WARN/FAIL. This is what makes a tuning change legible — the prior run is the reference, so a
/// number that moved shows as a signed Δ next to its old value, not just an absolute.
/// </summary>
public static class SoakBaselineDeltaReport
{
    public static string Format(SoakBaseline current, SoakBaseline baseline)
    {
        var deltas = SoakBaselineEvaluator.ComputeDeltas(current, baseline);
        double survDelta = SoakBaselineEvaluator.SurvivalRateDelta(current, baseline);
        string overall = SoakBaselineEvaluator.OverallVerdict(deltas);

        var baseByDepth = baseline.Floors.ToDictionary(f => f.Depth);

        var sb = new StringBuilder();
        sb.AppendLine("Soak Delta vs Baseline:");
        sb.AppendLine(
            $"  Baseline: {baseline.Runs} runs, survival {baseline.SurvivalRate * 100:F1}%   |   " +
            $"Current: {current.Runs} runs, survival {current.SurvivalRate * 100:F1}%   " +
            $"(Δ {Signed(survDelta * 100)}pp)   Overall: {overall}");

        sb.AppendLine($"  {"Depth",5}   {"Death% was→now",18}   {"Δ",9}   {"Turns Δ",9}   {"Kills Δ",9}   {"Verdict",-11}");
        sb.AppendLine($"  {"-----",5}   {"------------------",18}   {"---------",9}   {"---------",9}   {"---------",9}   {"-----------",-11}");

        foreach (var d in deltas)
        {
            double curDeath = current.Floors.First(f => f.Depth == d.Depth).DeathRate;
            string wasNow = d.HadBaseline
                ? $"{baseByDepth[d.Depth].DeathRate * 100:F1}% → {curDeath * 100:F1}%"
                : $"(new) {curDeath * 100:F1}%";
            string drDelta = d.HadBaseline ? $"{Signed(d.DeathRateDelta * 100)}pp" : "—";
            string turnsDelta = d.HadBaseline ? Signed(d.AvgTurnsDelta) : "—";
            string killsDelta = d.HadBaseline ? Signed(d.AvgKillsDelta) : "—";

            sb.AppendLine($"  {d.Depth,5}   {wasNow,18}   {drDelta,9}   {turnsDelta,9}   {killsDelta,9}   {d.Verdict,-11}");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>Format a delta with an explicit sign and one decimal ("+2.0", "-3.5", "+0.0").</summary>
    private static string Signed(double value) => (value >= 0 ? "+" : "") + value.ToString("F1");
}
