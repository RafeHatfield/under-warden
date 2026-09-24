using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace UnderWarden.Analyst;

/// <summary>
/// The per-run evaluation report (plan-analyst §2). v1 is predicate-only: `coherence` is empty
/// (Phase 3) and `structural_summary` is null for bot runs. `predicate_coverage` and
/// `skipped_mechanisms` are first-class so a zero-candidate result is auditable — you can see
/// every category RAN and how many turns it checked, distinguishing "clean" from "never ran".
/// </summary>
public sealed class EvaluationReport
{
    public string RunId { get; init; } = "";
    public string Persona { get; init; } = "";
    public string PlayerType { get; init; } = "";
    public string? LlmModel { get; init; }
    public bool ReplayAvailable { get; init; }

    public IReadOnlyList<BugCandidate> BugCandidates { get; init; } = Array.Empty<BugCandidate>();

    /// <summary>Per-predicate coverage: turns evaluated + candidates found. The audit trail.</summary>
    public IReadOnlyList<PredicateCoverage> PredicateCoverage { get; init; } = Array.Empty<PredicateCoverage>();

    /// <summary>Categories that did NOT run, and why (unimplemented mechanism, or missing field).</summary>
    public IReadOnlyList<CategorySkip> SkippedMechanisms { get; init; } = Array.Empty<CategorySkip>();

    /// <summary>Empty in v1 — the coherence pass (Phase 3) needs the full rubric.</summary>
    public IReadOnlyDictionary<string, object> Coherence { get; init; } = new Dictionary<string, object>();

    /// <summary>System-trigger coverage echoed from RunSummary. Null if the run had no summary.</summary>
    public JsonNode? SystemCoverage { get; init; }

    /// <summary>LLM Player runs only — null for bot runs (no structural judgments).</summary>
    public object? StructuralSummary { get; init; }

    /// <summary>Deterministic factual summary (NOT an LLM call this phase).</summary>
    public string AnalystNote { get; init; } = "";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Assemble a report from a loaded transcript + detection result.</summary>
    public static EvaluationReport Build(LoadedTranscript t, DetectionResult result)
    {
        int predicateCats = result.Coverage.Count;
        int turns = t.Turns.Count;
        int skippedCount = result.Skipped.Count;

        var note =
            $"Predicate scan: {predicateCats} predicate categor{(predicateCats == 1 ? "y" : "ies")} " +
            $"evaluated across {turns} turn{(turns == 1 ? "" : "s")}; " +
            $"{result.Candidates.Count} bug candidate{(result.Candidates.Count == 1 ? "" : "s")}. " +
            (skippedCount > 0
                ? $"{skippedCount} categor{(skippedCount == 1 ? "y" : "ies")} skipped (see skipped_mechanisms)."
                : "No categories skipped.");

        return new EvaluationReport
        {
            RunId = t.RunId,
            Persona = t.Persona,
            PlayerType = t.PlayerType,
            LlmModel = t.LlmModel,
            ReplayAvailable = t.ReplayAvailable,
            BugCandidates = result.Candidates,
            PredicateCoverage = result.Coverage,
            SkippedMechanisms = result.Skipped,
            SystemCoverage = t.Summary?.SystemTriggers?.DeepClone(),
            StructuralSummary = null,
            AnalystNote = note,
        };
    }
}
