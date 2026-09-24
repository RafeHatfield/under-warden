using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnderWarden.Analyst;

/// <summary>One transcript's evaluation, as fed to the aggregator.</summary>
public sealed class RunEvaluation
{
    public required string RunId { get; init; }
    public required string PlayerType { get; init; }
    public required DetectionResult Detection { get; init; }

    /// <summary>Boolean system-trigger fire flags from the run's RunSummary (heatmap source).</summary>
    public required IReadOnlyDictionary<string, bool> SystemTriggers { get; init; }
}

// ── Rollup records ──────────────────────────────────────────────────────────

/// <summary>A bug category that fired in ≥1 run. Confidence = frequency "N of M runs" (no synthesized score).</summary>
public sealed class CandidateRollup
{
    public required string Category { get; init; }
    public required string Mechanism { get; init; }
    public required string Description { get; init; }
    public required int RunsWithCandidate { get; init; }
    public required int TotalRuns { get; init; }
    public required int TotalInstances { get; init; }
    public string? ExampleEvidence { get; init; }
}

/// <summary>
/// THE AUDIT TRAIL, rolled up: per predicate category, how much it actually RAN across the batch.
/// "0 candidates" is only trustworthy beside "ran N total times across M runs, skipped in K".
/// </summary>
public sealed class CoverageRollup
{
    public required string Category { get; init; }
    public required long TotalTurnsEvaluated { get; init; }
    public required int RunsEvaluated { get; init; }
    public required int RunsSkipped { get; init; }
    public required int TotalCandidates { get; init; }
}

/// <summary>A category that was skipped in ≥1 run, with the distinct reasons.</summary>
public sealed class SkipRollup
{
    public required string Category { get; init; }
    public required string Mechanism { get; init; }
    public required int RunsSkipped { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
}

/// <summary>Neutral heatmap datum: fire rate of one system trigger across the batch, plus its class.</summary>
public sealed class HeatmapEntry
{
    public required string Trigger { get; init; }
    public required int FiredInRuns { get; init; }
    public required int TotalRuns { get; init; }
    public required double FireRate { get; init; }
    public required string TriggerClass { get; init; }   // content | mechanism | unclassified
}

/// <summary>A mechanism trigger at 0× across the batch — UNVERIFIED. Routes, does not conclude.</summary>
public sealed class BlindSpot
{
    public required string Trigger { get; init; }
    public required string Route { get; init; }
}

/// <summary>
/// Batch-level aggregate (plan-analyst §2, Phase 4). Predicate-only. The coherence and
/// structural-judgment sections are present-but-N/A slots so later phases drop in without a
/// reshape — they are NOT faked. The headline is the coverage roll-up: silence always travels
/// with its ran-count.
/// </summary>
public sealed class AggregateReport
{
    public required string BatchDir { get; init; }
    public required int RunsEvaluated { get; init; }
    public required int RunsFailedToLoad { get; init; }
    public required IReadOnlyList<string> FailedFiles { get; init; }

    /// <summary>Bug categories that fired, with "N of M runs" frequency.</summary>
    public required IReadOnlyList<CandidateRollup> BugCandidates { get; init; }

    /// <summary>Audit trail: every predicate category's total turns evaluated + ran/skipped counts.</summary>
    public required IReadOnlyList<CoverageRollup> PredicateCoverage { get; init; }

    /// <summary>Categories skipped in any run, rolled up with reasons.</summary>
    public required IReadOnlyList<SkipRollup> SkippedMechanisms { get; init; }

    /// <summary>Neutral per-trigger fire rates across the batch.</summary>
    public required IReadOnlyList<HeatmapEntry> SystemTriggerHeatmap { get; init; }

    /// <summary>Mechanism triggers at 0× — the bulk instrument's blind spots, routed to scripted/LLM runs.</summary>
    public required IReadOnlyList<BlindSpot> MechanismBlindSpots { get; init; }

    /// <summary>Phase 3 slot — empty until coherence_dimensions land. Not faked.</summary>
    public string CoherenceStatus { get; init; } =
        "N/A — coherence pass not run (rubric coherence_dimensions empty; Analyst Phase 3).";

    /// <summary>LLM-Player-only slot — N/A for bot-only batches.</summary>
    public string StructuralJudgmentStatus { get; init; } =
        "N/A — bot-only batch (structural judgments are emitted by LLM Player runs).";

    public string Note { get; init; } = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    // ── Build ────────────────────────────────────────────────────────────────

    public static AggregateReport Build(
        string batchDir, IReadOnlyList<RunEvaluation> runs,
        IReadOnlyList<(string File, string Error)> failures, CoverageSemantics semantics)
    {
        // Deterministic order regardless of parallel completion.
        var ordered = runs.OrderBy(r => r.RunId, StringComparer.Ordinal).ToList();
        int m = ordered.Count;

        // ── Bug candidates: group by category, frequency = runs containing ≥1. ──
        var byCategory = ordered
            .SelectMany(r => r.Detection.Candidates.Select(c => (r.RunId, c)))
            .GroupBy(x => x.c.Category)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        var candidates = byCategory.Select(g => new CandidateRollup
        {
            Category          = g.Key,
            Mechanism         = g.First().c.Mechanism,
            Description       = g.First().c.Description,
            RunsWithCandidate = g.Select(x => x.RunId).Distinct().Count(),
            TotalRuns         = m,
            TotalInstances    = g.Count(),
            ExampleEvidence   = g.First().c.EvidenceSnippet,
        }).ToList();

        // ── Coverage roll-up (THE audit trail). Union of all categories that ran or skipped. ──
        var coverageCats = ordered
            .SelectMany(r => r.Detection.Coverage.Select(c => c.Category)
                .Concat(r.Detection.Skipped.Select(s => s.Category)))
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal);

        var coverage = coverageCats.Select(cat =>
        {
            long turns = ordered.Sum(r => r.Detection.Coverage.Where(c => c.Category == cat).Sum(c => (long)c.TurnsEvaluated));
            int ran = ordered.Count(r => r.Detection.Coverage.Any(c => c.Category == cat));
            int skipped = ordered.Count(r => r.Detection.Skipped.Any(s => s.Category == cat));
            int cands = ordered.Sum(r => r.Detection.Coverage.Where(c => c.Category == cat).Sum(c => c.CandidatesFound));
            return new CoverageRollup
            {
                Category = cat,
                TotalTurnsEvaluated = turns,
                RunsEvaluated = ran,
                RunsSkipped = skipped,
                TotalCandidates = cands,
            };
        }).ToList();

        // ── Skips roll-up. ──
        var skips = ordered
            .SelectMany(r => r.Detection.Skipped)
            .GroupBy(s => s.Category)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new SkipRollup
            {
                Category = g.Key,
                Mechanism = g.First().Mechanism,
                RunsSkipped = g.Count(),
                Reasons = g.Select(s => s.Reason).Distinct().ToList(),
            }).ToList();

        // ── Heatmap (neutral data) + semantic interpretation. ──
        var triggerKeys = ordered.SelectMany(r => r.SystemTriggers.Keys).Distinct()
            .OrderBy(k => k, StringComparer.Ordinal).ToList();

        var heatmap = triggerKeys.Select(key =>
        {
            int fired = ordered.Count(r => r.SystemTriggers.TryGetValue(key, out var b) && b);
            var cls = semantics.Classify(key);
            return new HeatmapEntry
            {
                Trigger = key,
                FiredInRuns = fired,
                TotalRuns = m,
                FireRate = m > 0 ? (double)fired / m : 0.0,
                TriggerClass = cls.ToString().ToLowerInvariant(),
            };
        }).ToList();

        // Blind spots: MECHANISM-class triggers at 0× (when zero-rate is an evidence gap). ROUTE, don't conclude.
        var blindSpots = heatmap
            .Where(h => h.TriggerClass == "mechanism" && h.FiredInRuns == 0 && semantics.MechanismZeroRateIsEvidenceGap)
            .Select(h => new BlindSpot
            {
                Trigger = h.Trigger,
                Route = $"unverified — exercise '{h.Trigger}' with a targeted/scripted run (or an LLM persona). " +
                        "If the consequence fires there it is healthy-but-unexercised; if not, it is dead. " +
                        "0× from a bot batch does NOT mean broken.",
            }).ToList();

        int totalCandidates = candidates.Sum(c => c.TotalInstances);
        int skippedCats = skips.Count;
        var note =
            $"{m} run{(m == 1 ? "" : "s")} evaluated" +
            (failures.Count > 0 ? $" ({failures.Count} failed to load)" : "") +
            $"; {totalCandidates} bug candidate instance{(totalCandidates == 1 ? "" : "s")} across {candidates.Count} categor{(candidates.Count == 1 ? "y" : "ies")}; " +
            $"{coverage.Count} predicate categor{(coverage.Count == 1 ? "y" : "ies")} in the audit trail" +
            (skippedCats > 0 ? $", {skippedCats} skipped in some runs" : ", none skipped") +
            $"; {blindSpots.Count} mechanism blind-spot{(blindSpots.Count == 1 ? "" : "s")} (0× — unverified).";

        return new AggregateReport
        {
            BatchDir = batchDir,
            RunsEvaluated = m,
            RunsFailedToLoad = failures.Count,
            FailedFiles = failures.Select(f => $"{f.File}: {f.Error}").ToList(),
            BugCandidates = candidates,
            PredicateCoverage = coverage,
            SkippedMechanisms = skips,
            SystemTriggerHeatmap = heatmap,
            MechanismBlindSpots = blindSpots,
            Note = note,
        };
    }

    // ── findings.md ────────────────────────────────────────────────────────────

    public string ToFindingsMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Batch Analysis — findings");
        sb.AppendLine();
        sb.AppendLine($"- Batch: `{BatchDir}`");
        sb.AppendLine($"- Runs evaluated: **{RunsEvaluated}**" + (RunsFailedToLoad > 0 ? $" ({RunsFailedToLoad} failed to load)" : ""));
        sb.AppendLine($"- {Note}");
        sb.AppendLine();

        if (RunsFailedToLoad > 0)
        {
            sb.AppendLine("## Load failures");
            foreach (var f in FailedFiles) sb.AppendLine($"- {f}");
            sb.AppendLine();
        }

        // ── Bug candidates (frequency, human-judgeable) ──
        sb.AppendLine("## Bug candidates (frequency)");
        if (BugCandidates.Count == 0)
        {
            sb.AppendLine("None fired. (See the audit trail below — this is *ran-and-found-nothing*, not *never-ran*.)");
        }
        else
        {
            sb.AppendLine("| category | mechanism | appeared in | instances | example |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var c in BugCandidates)
                sb.AppendLine($"| `{c.Category}` | {c.Mechanism} | **{c.RunsWithCandidate} of {c.TotalRuns} runs** | {c.TotalInstances} | {c.ExampleEvidence} |");
        }
        sb.AppendLine();

        // ── Audit trail (the headline) ──
        sb.AppendLine("## Predicate audit trail — silence travels with its ran-count");
        sb.AppendLine("| category | total turns evaluated | runs ran | runs skipped | candidates |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var c in PredicateCoverage)
            sb.AppendLine($"| `{c.Category}` | {c.TotalTurnsEvaluated:N0} | {c.RunsEvaluated} | {c.RunsSkipped} | {c.TotalCandidates} |");
        sb.AppendLine();

        if (SkippedMechanisms.Count > 0)
        {
            sb.AppendLine("### Skipped categories (did NOT run somewhere)");
            foreach (var s in SkippedMechanisms)
                sb.AppendLine($"- `{s.Category}` [{s.Mechanism}] — skipped in {s.RunsSkipped} run(s): {string.Join("; ", s.Reasons)}");
            sb.AppendLine();
        }

        // ── Heatmap (neutral) ──
        sb.AppendLine("## System-trigger heatmap (neutral fire rates)");
        sb.AppendLine("| trigger | class | fired in | rate |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var h in SystemTriggerHeatmap)
            sb.AppendLine($"| `{h.Trigger}` | {h.TriggerClass} | {h.FiredInRuns}/{h.TotalRuns} | {h.FireRate:P0} |");
        sb.AppendLine();
        sb.AppendLine("> Content triggers are never flagged (any rate, including zero, is fine). " +
                      "Mechanism triggers: low-but-nonzero is coherent playstyle. Classification bridges the " +
                      "rubric's detector-named examples to the transcript's system_triggers keys by stem; " +
                      "`unclassified` triggers are reported neutrally pending the rubric's per-trigger classification.");
        sb.AppendLine();

        // ── Blind spots (the high-value output) ──
        sb.AppendLine("## Mechanism blind spots (0× across the batch — UNVERIFIED, routed)");
        if (MechanismBlindSpots.Count == 0)
        {
            sb.AppendLine("None — every classified mechanism trigger fired at least once.");
        }
        else
        {
            sb.AppendLine("These are the things this bulk (bot) instrument never exercises. 0× is **not** *broken* — " +
                          "it is *unverified*, with two indistinguishable causes (mechanism dead, or instrument never " +
                          "creates the condition). Route each to a targeted/scripted run or an LLM persona:");
            sb.AppendLine();
            foreach (var b in MechanismBlindSpots)
                sb.AppendLine($"- **`{b.Trigger}`** — {b.Route}");
        }
        sb.AppendLine();

        // ── Deferred slots ──
        sb.AppendLine("## Coherence");
        sb.AppendLine(CoherenceStatus);
        sb.AppendLine();
        sb.AppendLine("## Structural judgments");
        sb.AppendLine(StructuralJudgmentStatus);
        sb.AppendLine();

        return sb.ToString();
    }
}
