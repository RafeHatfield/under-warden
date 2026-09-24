using System.Text;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Generates a multi-section human-readable text report from a DungeonSoakSummary.
///
/// Port of PoC's bot_survivability_report.py and eco_balance_report.py, unified into
/// a single dungeon-focused report. Suitable for CI output or developer review.
///
/// The report is a pure string — no file I/O. The caller writes it where needed.
/// All sections degrade gracefully: 0 deaths, 0 kills, all survived, no telemetry.
/// </summary>
public static class DungeonSoakReport
{
    private const int BarMaxWidth = 40;

    /// <summary>
    /// Generate a full soak report from the given summary.
    ///
    /// The summary must have been produced by DungeonSoakSummary.ComputeFrom().
    /// Sections are skipped or noted when data is unavailable (e.g. no telemetry),
    /// but the method never throws on valid (even empty) summaries.
    /// </summary>
    public static string Generate(DungeonSoakSummary summary) => Generate(summary, targets: null);

    /// <summary>
    /// Generate a full soak report, optionally including the role-aware Floor Health section.
    ///
    /// When <paramref name="targets"/> is provided, a "Floor Health" section renders each floor's
    /// OBSERVED death% against the TARGET band, the classifier's VERDICT (the role-aware health reading),
    /// the Δ from the band, and — beneath any too-hard floor — the lever attribution (which dial the
    /// deaths implicate). Survival rate stays the balance verdict; the lever line is attribution only.
    /// When null, the report omits Floor Health (back-compat with callers that have no target table).
    /// </summary>
    public static string Generate(DungeonSoakSummary summary, TargetTable? targets,
        ClassifierConfig? classifierConfig = null, LeverConfig? leverConfig = null)
    {
        var sb = new StringBuilder();

        AppendOverview(sb, summary);
        AppendSurvivalCurve(sb, summary);
        AppendDeathClassification(sb, summary);
        AppendFloorEfficiency(sb, summary);
        if (targets != null)
            AppendFloorHealth(sb, summary, targets, classifierConfig ?? new ClassifierConfig(), leverConfig ?? new LeverConfig());
        AppendBotEfficiency(sb, summary);
        AppendVoiceLineHistogram(sb, summary);
        AppendAnomalies(sb, summary);

        return sb.ToString();
    }

    // ── Section 1: Overview ─────────────────────────────────────────────────

    private static void AppendOverview(StringBuilder sb, DungeonSoakSummary summary)
    {
        sb.AppendLine("=== YARL Dungeon Soak Report ===");

        // Use ConfiguredFloors when known (live soak); fall back to max depth reached (JSONL reader).
        int floorCount = summary.ConfiguredFloors > 0
            ? summary.ConfiguredFloors
            : summary.SurvivalCurve.Count;
        // Base seed: the lowest seed seen across runs, or 0 if no runs.
        int baseSeed = summary.Runs.Count > 0
            ? summary.Runs.Min(r => r.Seed)
            : 0;

        sb.AppendLine($"Runs: {summary.RunsAttempted} | Floors: {floorCount} | Base Seed: {baseSeed}");
        sb.AppendLine($"Survival Rate: {summary.SurvivalRate * 100:F1}%");
        sb.AppendLine($"Avg Floors Completed: {summary.AvgFloorsCompleted:F1} / {floorCount}");
        sb.AppendLine($"Avg Total Turns: {summary.AvgTotalTurns:F1}");
        sb.AppendLine();
    }

    // ── Section 2: Survival Curve ───────────────────────────────────────────

    private static void AppendSurvivalCurve(StringBuilder sb, DungeonSoakSummary summary)
    {
        sb.AppendLine("Survival Curve:");

        if (summary.SurvivalCurve.Count == 0)
        {
            sb.AppendLine("  (no floor data)");
            sb.AppendLine();
            return;
        }

        for (int i = 0; i < summary.SurvivalCurve.Count; i++)
        {
            double fraction = summary.SurvivalCurve[i];
            int barWidth    = (int)(fraction * BarMaxWidth);
            string bar      = new string('#', barWidth);
            sb.AppendLine($"  Floor {i + 1,2}: {fraction * 100,6:F1}%  {bar}");
        }

        sb.AppendLine();
    }

    // ── Section 3: Death Classification ────────────────────────────────────

    private static void AppendDeathClassification(StringBuilder sb, DungeonSoakSummary summary)
    {
        int totalDeaths = summary.FailureTypeCounts.TryGetValue(OutcomeClassifier.FailureDeath, out int d) ? d : 0;
        // "Deaths" for the header = all non-survived, non-exception outcomes
        int totalFailures = summary.RunsAttempted - summary.RunsSurvived;

        sb.AppendLine($"Death Classification ({totalFailures} deaths):");

        if (totalFailures == 0)
        {
            sb.AppendLine("  No deaths recorded.");
            sb.AppendLine();
            return;
        }

        // Failure type table
        sb.AppendLine($"  {"Failure Type",-20}  {"Count",5}    {"%" ,6}");
        sb.AppendLine($"  {"--------------------",-20}  {"-----",5}    {"------",6}");

        foreach (var (type, count) in summary.FailureTypeCounts
            .Where(kvp => kvp.Key != OutcomeClassifier.FailureNone)
            .OrderByDescending(kvp => kvp.Value))
        {
            double pct = totalFailures > 0 ? (double)count / totalFailures * 100.0 : 0.0;
            sb.AppendLine($"  {type,-20}  {count,5}    {pct,5:F1}%");
        }

        sb.AppendLine();

        // Top killers (combat deaths only)
        if (summary.KillerCounts.Count > 0)
        {
            sb.AppendLine($"  Top Killers (combat deaths):");
            var sorted = summary.KillerCounts.OrderByDescending(kvp => kvp.Value).ToList();
            int topN   = Math.Min(5, sorted.Count);
            int topSum = sorted.Take(topN).Sum(kvp => kvp.Value);

            for (int i = 0; i < topN; i++)
            {
                var (killer, count) = sorted[i];
                double pct = totalDeaths > 0 ? (double)count / totalDeaths * 100.0 : 0.0;
                sb.AppendLine($"  {killer,-22}  {count,4}   {pct,5:F1}%");
            }

            if (sorted.Count > topN)
            {
                int others    = sorted.Skip(topN).Sum(kvp => kvp.Value);
                double otherPct = totalDeaths > 0 ? (double)others / totalDeaths * 100.0 : 0.0;
                sb.AppendLine($"  {"(others)",-22}  {others,4}   {otherPct,5:F1}%");
            }

            sb.AppendLine();
        }
    }

    // ── Section 4: Floor Efficiency ─────────────────────────────────────────

    private static void AppendFloorEfficiency(StringBuilder sb, DungeonSoakSummary summary)
    {
        sb.AppendLine("Floor Efficiency:");

        if (summary.Runs.Count == 0)
        {
            sb.AppendLine("  (no run data)");
            sb.AppendLine();
            return;
        }

        // Aggregate per-floor stats from the run list (keyed by Depth, 1-based).
        var byDepth = GroupByDepth(summary);

        if (byDepth.Count == 0)
        {
            sb.AppendLine("  (no floor data)");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"  {"Depth",5}   {"Avg Turns",9}   {"Avg Kills",9}   {"Avg HP End",10}   {"Death%",6}");
        sb.AppendLine($"  {"-----",5}   {"---------",9}   {"---------",9}   {"----------",10}   {"------",6}");

        foreach (var (depth, floors) in byDepth)
        {
            double avgTurns  = floors.Average(f => f.TurnsTaken);
            double avgKills  = floors.Average(f => f.MonstersKilled);
            double deathPct  = DeathPctFraction(floors) * 100.0;

            // Avg HP displayed as "current/max" using the average of both fields.
            // Players with MaxHp=0 (exception runs) are excluded from the average.
            var hpFloors = floors.Where(f => f.PlayerMaxHp > 0).ToList();
            string hpStr;
            if (hpFloors.Count > 0)
            {
                // Clamp to 0: fatal hits leave Hp negative; we want "0" not "-5" in the report.
                int avgHp    = (int)Math.Round(hpFloors.Average(f => Math.Max(0, f.PlayerHpAtEnd)));
                int avgMaxHp = (int)Math.Round(hpFloors.Average(f => f.PlayerMaxHp));
                hpStr = $"{avgHp}/{avgMaxHp}";
            }
            else
            {
                hpStr = "n/a";
            }

            sb.AppendLine($"  {depth,5}   {avgTurns,9:F1}   {avgKills,9:F1}   {hpStr,10}   {deathPct,5:F1}%");
        }

        sb.AppendLine();
    }

    // ── Section 4b: Floor Health (role-aware) ───────────────────────────────
    //
    // The balance verdict is the SURVIVAL RATE vs the target band (multivariate by construction).
    // FloorHealthClassifier renders the role-aware verdict per floor; LeverAttributionClassifier
    // attributes too-hard floors to a tuning dial. This section is the working screen the engine feeds.

    private static void AppendFloorHealth(StringBuilder sb, DungeonSoakSummary summary, TargetTable targets,
        ClassifierConfig classifierCfg, LeverConfig leverCfg)
    {
        sb.AppendLine("Floor Health (role-aware):");
        sb.AppendLine($"  Verdict = survival rate vs band (balance); levers below = attribution. Overall survival: {summary.SurvivalRate * 100:F1}%");

        var byDepth = GroupByDepth(summary);
        if (byDepth.Count == 0)
        {
            sb.AppendLine("  (no floor data)");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"  {"Depth",5}   {"Observed",8}   {"Target Band",12}   {"Δ",8}   {"Verdict",-16}");
        sb.AppendLine($"  {"-----",5}   {"--------",8}   {"------------",12}   {"--------",8}   {"----------------",-16}");

        foreach (var (depth, floors) in byDepth)
        {
            double observed = DeathPctFraction(floors);
            var target = targets.ForDepth(depth);
            var band = target.DeathPct;

            // Only deaths with a classified killer feed archetype attribution; ALL deaths count in observed%.
            var deaths = floors
                .Where(f => f.Death?.KillerArchetype != null)
                .Select(f => new DeathRecord(f.Death!.KillerArchetype!.Value, f.Death!.HitsToDown))
                .ToList();

            var observedFloor = new FloorObserved(
                DeathPct: observed,
                Deaths: deaths,
                HasSpike: floors.Any(f => f.SpikePresent),
                HasEscalator: floors.Any(f => f.EscalatorPresent),
                EscalatorReachable: false, // produced by staged-start (step 8); escalator branch is moot until then
                Escalator: null);

            var verdict = FloorHealthClassifier.Classify(observedFloor, target, classifierCfg);

            string delta = band.Contains(observed) ? "—"
                : band.Above(observed) ? $"+{(observed - band.Max) * 100:F1}%"
                : $"-{(band.Min - observed) * 100:F1}%";
            string bandStr = $"{band.Min * 100:F0}-{band.Max * 100:F0}%";

            sb.AppendLine($"  {depth,5}   {observed * 100,7:F1}%   {bandStr,12}   {delta,8}   {verdict,-16}");

            // Lever attribution beneath too-hard floors (incl. secretly-lethal baseline, which can be in-band).
            bool tooHard = band.Above(observed) || verdict == FloorHealth.BaselineBroken;
            if (tooHard)
                AppendLeverAttribution(sb, floors, targets.LeverExpectationForDepth(depth), leverCfg);
        }

        sb.AppendLine();
    }

    private static void AppendLeverAttribution(
        StringBuilder sb, List<FloorRunMetrics> floors, LeverExpectation? expectation, LeverConfig leverCfg)
    {
        var deathRecords = floors.Where(f => f.Death != null).Select(f => f.Death!).ToList();
        if (expectation == null || deathRecords.Count == 0)
            return;

        var tally = new Dictionary<BalanceLever, int>();
        foreach (var d in deathRecords)
        {
            var dominant = LeverAttributionClassifier.Dominant(d, expectation, leverCfg);
            if (dominant != null)
            {
                tally.TryGetValue(dominant.Value, out int c);
                tally[dominant.Value] = c + 1;
            }
        }

        string body = tally.Count == 0
            ? "none implicated (signals within tolerance — composition/variance, not a single dial)"
            : "levers: " + string.Join(" · ", tally.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} ×{kv.Value}"));
        sb.AppendLine($"          └─ {body}");
    }

    // ── Shared floor aggregation helpers ─────────────────────────────────────

    /// <summary>Group every attempted floor across all runs by depth (1-based), preserving depth order.</summary>
    private static SortedDictionary<int, List<FloorRunMetrics>> GroupByDepth(DungeonSoakSummary summary)
    {
        var byDepth = new SortedDictionary<int, List<FloorRunMetrics>>();
        foreach (var run in summary.Runs)
        foreach (var floor in run.PerFloor)
        {
            if (!byDepth.TryGetValue(floor.Depth, out var list))
                byDepth[floor.Depth] = list = new List<FloorRunMetrics>();
            list.Add(floor);
        }
        return byDepth;
    }

    /// <summary>Observed death rate (fraction) for a depth = died / reached. The single source for death%.</summary>
    private static double DeathPctFraction(IReadOnlyList<FloorRunMetrics> floors)
        => floors.Count > 0 ? (double)floors.Count(f => f.PlayerDied) / floors.Count : 0.0;

    // ── Section 5: Bot Efficiency ───────────────────────────────────────────

    private static void AppendBotEfficiency(StringBuilder sb, DungeonSoakSummary summary)
    {
        sb.AppendLine("Bot Efficiency:");

        var botSummaries = summary.Runs
            .Where(r => r.BotSummary != null)
            .Select(r => r.BotSummary!)
            .ToList();

        if (botSummaries.Count == 0)
        {
            sb.AppendLine("  (Bot telemetry not available -- run with --telemetry to enable)");
            sb.AppendLine();
            return;
        }

        // Aggregate action counts across all runs
        var totalActionCounts = new Dictionary<string, long>();
        long totalDecisions   = 0;
        double healHpSum      = 0.0;
        int healDecisionSum   = 0;
        int deathsWithUnused  = 0;

        int combatDeaths = summary.Runs.Count(r => r.FailureType == OutcomeClassifier.FailureDeath);

        foreach (var bot in botSummaries)
        {
            totalDecisions += bot.TotalDecisions;

            foreach (var (key, count) in bot.ActionCounts)
            {
                totalActionCounts.TryGetValue(key, out long existing);
                totalActionCounts[key] = existing + count;
            }

            if (bot.HealDecisions > 0)
            {
                healHpSum       += bot.AvgHpWhenHealing * bot.HealDecisions;
                healDecisionSum += bot.HealDecisions;
            }

            deathsWithUnused += bot.DeathsWithUnusedPotions;
        }

        sb.AppendLine("  Action Distribution:");

        if (totalDecisions > 0)
        {
            foreach (var (action, count) in totalActionCounts.OrderByDescending(kv => kv.Value))
            {
                double pct = (double)count / totalDecisions * 100.0;
                sb.AppendLine($"    {action,-20} {pct,5:F1}%");
            }
        }
        else
        {
            sb.AppendLine("    (no decisions recorded)");
        }

        sb.AppendLine();
        sb.AppendLine("  Heal Behavior:");

        if (healDecisionSum > 0)
        {
            double avgHpPct = healHpSum / healDecisionSum * 100.0;
            string flag = avgHpPct > 35.0
                ? "  [WARN: healing too early — wasting potions]"
                : avgHpPct < 10.0
                    ? "  [WARN: healing too late — dying with potions]"
                    : "";
            sb.AppendLine($"    Avg HP% when healing: {avgHpPct:F1}%  (target: 15-30%){flag}");
        }
        else
        {
            sb.AppendLine("    Avg HP% when healing: n/a (no heal decisions)");
        }

        string deathsNote = combatDeaths > 0
            ? $"{deathsWithUnused} / {combatDeaths} combat deaths ({(double)deathsWithUnused / combatDeaths * 100.0:F1}%)"
            : "0 combat deaths";
        sb.AppendLine($"    Deaths with unused potions: {deathsNote}");

        sb.AppendLine();
    }

    // ── Section 6: Voice Line Histogram ────────────────────────────────────

    private static void AppendVoiceLineHistogram(StringBuilder sb, DungeonSoakSummary summary)
    {
        sb.AppendLine("Voice Line Emissions:");

        if (summary.VoiceLineHits.Count == 0)
        {
            sb.AppendLine("  (no voice lines fired in this soak run)");
            sb.AppendLine();
            return;
        }

        int totalFires = summary.VoiceLineHits.Values.Sum();
        sb.AppendLine($"  Total fires: {totalFires} across {summary.RunsAttempted} runs");
        sb.AppendLine($"  {"Trigger ID",-55}  {"Count",5}    {"Rate/Run",8}");
        sb.AppendLine($"  {new string('-', 55)}  {"-----",5}    {"--------",8}");

        foreach (var (triggerId, count) in summary.VoiceLineHits.OrderByDescending(kv => kv.Value))
        {
            double perRun = summary.RunsAttempted > 0 ? (double)count / summary.RunsAttempted : 0.0;
            sb.AppendLine($"  {triggerId,-55}  {count,5}    {perRun,8:F2}");
        }

        sb.AppendLine();
    }

    // ── Section 7: Anomalies ────────────────────────────────────────────────

    private static void AppendAnomalies(StringBuilder sb, DungeonSoakSummary summary)
    {
        sb.AppendLine("Anomalies:");

        var lines = new List<string>();

        // Runs that hit the max turn limit
        int maxTurnsCount = summary.Runs.Count(r =>
            r.FailureType == OutcomeClassifier.FailureMaxTurns
            || r.FailureType == OutcomeClassifier.FailureStuck);
        if (maxTurnsCount > 0)
            lines.Add($"- {maxTurnsCount} run(s) hit max turn limit (possible stuck bot)");

        // Runs with 0 kills
        int zeroKillCount = summary.Runs.Count(r => r.TotalKills == 0);
        if (zeroKillCount > 0)
            lines.Add($"- {zeroKillCount} run(s) had 0 kills (bot may not have engaged enemies)");

        // Runs that died with >= 2 potions remaining (significant waste)
        var wastedPotionRuns = summary.Runs
            .Where(r => r.Outcome == OutcomeClassifier.Died && r.PotionsRemaining >= 2)
            .ToList();
        foreach (var run in wastedPotionRuns)
        {
            // Find the floor death occurred on
            var deathFloor = run.PerFloor.LastOrDefault(f => f.PlayerDied);
            string floorNote = deathFloor != null ? $" on floor {deathFloor.Depth}" : "";
            lines.Add($"- Run (seed {run.Seed}): died{floorNote} with {run.PotionsRemaining} healing potions remaining");
        }

        // Runs that survived but barely (HP fraction < 10%)
        var narrowSurvivalRuns = summary.Runs
            .Where(r => r.Outcome == OutcomeClassifier.Survived && r.FinalHpFraction < 0.1)
            .ToList();
        foreach (var run in narrowSurvivalRuns)
        {
            int pct = (int)(run.FinalHpFraction * 100);
            lines.Add($"- Run (seed {run.Seed}): survived with only {pct}% HP remaining");
        }

        if (lines.Count == 0)
        {
            sb.AppendLine("  None detected.");
        }
        else
        {
            foreach (var line in lines)
                sb.AppendLine($"  {line}");
        }

        sb.AppendLine();
    }
}
