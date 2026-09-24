namespace UnderWarden.Logic.Balance;

/// <summary>
/// Aggregate statistics across all runs in a dungeon soak session.
/// Produced by DungeonRunHarness.RunSoak() and consumed by the CLI and analysis tools.
///
/// The survival curve enables answering: "at which floor does the bot start dying significantly?"
/// DeathRateByFloor enables: "which floor is the deadliest for this seed batch?"
/// KillerCounts enables: "which monster is killing the bot most often?"
///
/// Port of PoC's SoakSessionResult (engine/soak_harness.py).
/// </summary>
public sealed class DungeonSoakSummary
{
    /// <summary>Total runs attempted (including exception runs).</summary>
    public int RunsAttempted { get; init; }

    /// <summary>Runs where the bot survived all requested floors.</summary>
    public int RunsSurvived { get; init; }

    /// <summary>RunsSurvived / RunsAttempted. 0.0 if no runs attempted.</summary>
    public double SurvivalRate { get; init; }

    /// <summary>Average number of floors completed per run.</summary>
    public double AvgFloorsCompleted { get; init; }

    /// <summary>Average total turns across all floors per run.</summary>
    public double AvgTotalTurns { get; init; }

    /// <summary>Average total monsters killed per run.</summary>
    public double AvgTotalKills { get; init; }

    /// <summary>
    /// Fraction of runs where the player died on each floor depth.
    /// Key: depth (1-based). Value: fraction of all runs where player died on that depth.
    /// Only contains keys for depths where at least one death occurred.
    /// </summary>
    public IReadOnlyDictionary<int, double> DeathRateByFloor { get; init; }
        = new Dictionary<int, double>();

    /// <summary>
    /// Counts per failure type string (e.g. "none": 80, "death": 15, "max_turns": 5).
    /// Includes all outcome categories including "exception".
    /// </summary>
    public IReadOnlyDictionary<string, int> FailureTypeCounts { get; init; }
        = new Dictionary<string, int>();

    /// <summary>
    /// Counts per killer name for runs where failure_type == "death".
    /// Key: entity name (e.g. "orc_brute"). Value: number of runs killed by that entity.
    /// </summary>
    public IReadOnlyDictionary<string, int> KillerCounts { get; init; }
        = new Dictionary<string, int>();

    /// <summary>
    /// Monotonically non-increasing survival curve.
    /// SurvivalCurve[d] = fraction of runs that REACHED floor d+1 (0-indexed, so [0] = floor 1).
    /// SurvivalCurve[0] is always 1.0 (every run attempts floor 1).
    /// Length equals the requested floor count.
    /// Used for: "what fraction of runs made it past floor 3?"
    /// </summary>
    public IReadOnlyList<double> SurvivalCurve { get; init; } = Array.Empty<double>();

    /// <summary>Full list of per-run results (in run order, seeds baseSeed+0..baseSeed+N-1).</summary>
    public IReadOnlyList<DungeonSoakRunResult> Runs { get; init; }
        = Array.Empty<DungeonSoakRunResult>();

    /// <summary>
    /// The number of floors the soak was configured to run, as passed to RunSoak().
    /// 0 means unknown (e.g. loaded from JSONL). When 0, reports should fall back to
    /// SurvivalCurve.Count (the max floor depth actually reached).
    /// </summary>
    public int ConfiguredFloors { get; init; }

    /// <summary>
    /// Aggregated voice line trigger IDs across all runs, with total emission counts.
    /// Only populated by ComputeFrom() when any run has VoiceLineHits data.
    /// </summary>
    public IReadOnlyDictionary<string, int> VoiceLineHits { get; init; }
        = new Dictionary<string, int>();

    /// <summary>
    /// Compute aggregate statistics from a list of completed run results.
    ///
    /// <paramref name="configuredFloors"/> is the number of floors requested for the soak.
    /// Pass 0 when the floor count is unknown (offline JSONL analysis, tests).
    /// </summary>
    public static DungeonSoakSummary ComputeFrom(List<DungeonSoakRunResult> runs, int configuredFloors = 0)
    {
        if (runs.Count == 0)
        {
            return new DungeonSoakSummary
            {
                RunsAttempted = 0,
                Runs          = Array.Empty<DungeonSoakRunResult>(),
            };
        }

        int survived = runs.Count(r => r.Outcome == OutcomeClassifier.Survived);

        // ── Failure type counts ──────────────────────────────────────────────
        var failureCounts = new Dictionary<string, int>();
        foreach (var r in runs)
        {
            failureCounts.TryGetValue(r.FailureType, out int existing);
            failureCounts[r.FailureType] = existing + 1;
        }

        // ── Killer counts (deaths only) ──────────────────────────────────────
        var killerCounts = new Dictionary<string, int>();
        foreach (var r in runs.Where(r => r.FailureType == OutcomeClassifier.FailureDeath
            && !string.IsNullOrEmpty(r.FailureDetail)))
        {
            string killer = r.FailureDetail;
            killerCounts.TryGetValue(killer, out int k);
            killerCounts[killer] = k + 1;
        }

        // ── Death rate by floor ──────────────────────────────────────────────
        // For each run that ended in death, attribute the death to the floor where the
        // player died. The floor is the last PerFloor entry with PlayerDied=true.
        var deathsByFloor = new Dictionary<int, int>();
        foreach (var r in runs.Where(r => r.Outcome == OutcomeClassifier.Died))
        {
            var deathFloor = r.PerFloor.LastOrDefault(f => f.PlayerDied);
            if (deathFloor != null)
            {
                deathsByFloor.TryGetValue(deathFloor.Depth, out int d);
                deathsByFloor[deathFloor.Depth] = d + 1;
            }
        }
        var deathRateByFloor = deathsByFloor.ToDictionary(
            kvp => kvp.Key,
            kvp => (double)kvp.Value / runs.Count);

        // ── Survival curve ───────────────────────────────────────────────────
        // Determine the maximum floor depth across all runs to size the curve.
        // Exception runs (PerFloor empty) contribute 0 to max depth but that's fine —
        // we still track those floors via other runs.
        //
        // The curve is sized by the maximum floor depth seen across all non-exception runs.
        // If all runs are exceptions, curve is empty.
        int maxDepth = runs.Max(r => r.PerFloor.Count > 0 ? r.PerFloor[^1].Depth : 0);

        double[] curve = maxDepth > 0 ? new double[maxDepth] : Array.Empty<double>();
        if (maxDepth > 0)
        {
            // curve[d] = fraction of runs that had a floor entry for depth d+1.
            // "Reached depth d+1" means the run has a PerFloor entry at that depth —
            // even if the player died on that floor, they reached it.
            // SurvivalCurve[0] = floor 1 = always 1.0 (everyone attempts floor 1).
            for (int d = 0; d < maxDepth; d++)
            {
                int depth = d + 1;
                int reached = runs.Count(r => r.PerFloor.Any(f => f.Depth == depth));
                curve[d] = (double)reached / runs.Count;
            }

            // Enforce monotonicity: each entry must be <= previous entry.
            // Floating-point arithmetic could produce tiny inversions; clamp to maintain invariant.
            for (int i = 1; i < curve.Length; i++)
            {
                if (curve[i] > curve[i - 1])
                    curve[i] = curve[i - 1];
            }
        }

        // ── Voice-line hits (aggregate across all runs) ──────────────────────────
        var voiceLineHits = new Dictionary<string, int>();
        foreach (var r in runs.Where(r => r.VoiceLineHits != null))
        {
            foreach (var (triggerId, count) in r.VoiceLineHits!)
            {
                voiceLineHits.TryGetValue(triggerId, out int existing);
                voiceLineHits[triggerId] = existing + count;
            }
        }

        return new DungeonSoakSummary
        {
            RunsAttempted      = runs.Count,
            RunsSurvived       = survived,
            SurvivalRate       = (double)survived / runs.Count,
            AvgFloorsCompleted = runs.Average(r => r.FloorsCompleted),
            AvgTotalTurns      = runs.Average(r => r.TotalTurns),
            AvgTotalKills      = runs.Average(r => r.TotalKills),
            DeathRateByFloor   = deathRateByFloor,
            FailureTypeCounts  = failureCounts,
            KillerCounts       = killerCounts,
            SurvivalCurve      = curve,
            Runs               = runs,
            ConfiguredFloors   = configuredFloors,
            VoiceLineHits      = voiceLineHits,
        };
    }
}
