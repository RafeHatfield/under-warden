using System.Text;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Renders the role-aware engagement-health section for a single controlled scenario. Soak analogue:
/// Layer-1 engagement verdict where DistinctAttackers = the actual composition (no bot-pulled chaos),
/// so "Density" means composition density — trustworthy for tuning decisions. Sits beside the existing
/// PoC pressure metrics (TtkHits / TtdHits / hit-rates) rather than replacing them.
///
/// Balance verdict = death rate vs target band (the multivariate outcome). The FloorHealth verdict and
/// lever attribution are the DIAGNOSTIC layer, consulted after the band flags a composition.
/// </summary>
public static class ScenarioEngagementReport
{
    /// <summary>
    /// Generate the engagement-health section. Returns an empty string when the scenario has no
    /// deaths to attribute (all survived — the section is moot; death rate alone tells the story).
    /// </summary>
    public static string Format(AggregatedMetrics metrics, TargetTable targets,
        ClassifierConfig? classifierCfg = null, LeverConfig? leverCfg = null)
    {
        var cfg   = classifierCfg ?? new ClassifierConfig();
        var lCfg  = leverCfg ?? new LeverConfig();

        var target      = targets.ForDepth(metrics.Depth);
        // Per-composition band (from scenario's target_death_pct) takes priority over the depth-region
        // band — it encodes the INTENT for this specific composition (Layer-1), not the general depth
        // pressure (Layer-2). Fall back to the region band only when no per-composition band is present.
        var band        = metrics.EngagementBand ?? target.DeathPct;
        var expectation = targets.LeverExpectationForDepth(metrics.Depth);

        var deaths = metrics.Deaths
            .Where(d => d.KillerArchetype.HasValue)
            .Select(d => new DeathRecord(d.KillerArchetype!.Value, d.HitsToDown))
            .ToList();

        var observed = new FloorObserved(
            DeathPct: metrics.DeathRate,
            Deaths: deaths,
            HasSpike: metrics.HasSpike,
            HasEscalator: metrics.HasEscalator,
            EscalatorReachable: false,
            Escalator: null);

        // The FloorTarget the classifier checks against: per-composition death-pct band (Layer-1)
        // for the verdict, combined with the region's archetype targets (unchanged).
        var classifierTarget = new FloorTarget(band, target.ByArchetype);
        var verdict = FloorHealthClassifier.Classify(observed, classifierTarget, cfg);

        var sb = new StringBuilder();
        sb.AppendLine($"Engagement Health (role-aware): {metrics.ScenarioId}");
        sb.AppendLine($"  Runs: {metrics.TotalRuns} | Depth: {metrics.Depth}");

        string bandStr   = $"{band.Min * 100:F0}-{band.Max * 100:F0}%";
        string deltaStr  = band.Contains(metrics.DeathRate) ? "—"
            : band.Above(metrics.DeathRate) ? $"+{(metrics.DeathRate - band.Max) * 100:F1}pp"
            : $"-{(band.Min - metrics.DeathRate) * 100:F1}pp";
        sb.AppendLine(
            $"  Death%: {metrics.DeathRate * 100:F1}%  |  Target: {bandStr}  |  Δ: {deltaStr}  |  Verdict: {verdict}");

        // Lever attribution — only when the floor is flagged (too-hard or secretly-lethal baseline).
        bool flagged = band.Above(metrics.DeathRate) || verdict == FloorHealth.BaselineBroken;
        if (flagged && expectation != null && metrics.Deaths.Count > 0)
        {
            var tally = new Dictionary<BalanceLever, int>();
            foreach (var d in metrics.Deaths)
            {
                var dominant = LeverAttributionClassifier.Dominant(d, expectation, lCfg);
                if (dominant != null)
                {
                    tally.TryGetValue(dominant.Value, out int c);
                    tally[dominant.Value] = c + 1;
                }
            }
            string body = tally.Count == 0
                ? "none implicated (signals within tolerance)"
                : string.Join(" · ", tally.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} ×{kv.Value}"));
            sb.AppendLine($"    └─ levers: {body}");
        }

        // PoC pressure metrics alongside — so both the new and old reading are visible.
        string potionStr = metrics.AvgPotionsUsed > 0
            ? $"  |  Avg potions/fight: {metrics.AvgPotionsUsed:F2}"
            : "";
        sb.AppendLine(
            $"  TtkHits: {metrics.TtkHits:F1}  |  TtdHits: {metrics.TtdHits:F1}  |  " +
            $"Monster hit rate: {metrics.MonsterHitRate * 100:F0}%  |  " +
            $"Player hit rate: {metrics.PlayerHitRate * 100:F0}%  |  " +
            $"Avg turns: {metrics.AvgTurns:F0}{potionStr}");

        return sb.ToString();
    }
}
