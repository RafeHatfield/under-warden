namespace UnderWarden.Analyst;

/// <summary>A single detected bug candidate. `Turn` is null for run-level checks.</summary>
public sealed class BugCandidate
{
    public int? Turn { get; init; }
    public required string Category { get; init; }
    public required string Mechanism { get; init; }
    public required string Description { get; init; }
    public required string EvidenceSnippet { get; init; }
}

/// <summary>
/// Records that a category did NOT run, and why. This is the auditability backbone of the
/// acceptance gate: a category that reports zero candidates is only trustworthy if we can tell
/// it actually RAN. Skipped categories (unimplemented mechanism, or a predicate that referenced
/// a field absent from the transcript) are surfaced explicitly rather than counted as "clean".
/// </summary>
public sealed class CategorySkip
{
    public required string Category { get; init; }
    public required string Mechanism { get; init; }
    public required string Reason { get; init; }
}

/// <summary>Per-category record of how thoroughly a predicate ran (turns checked, hits found).</summary>
public sealed class PredicateCoverage
{
    public required string Category { get; init; }
    public required int TurnsEvaluated { get; init; }
    public required int CandidatesFound { get; init; }
}

public sealed class DetectionResult
{
    public required IReadOnlyList<BugCandidate> Candidates { get; init; }
    public required IReadOnlyList<CategorySkip> Skipped { get; init; }
    public required IReadOnlyList<PredicateCoverage> Coverage { get; init; }
}

/// <summary>
/// Dispatches each rubric bug_category to its mechanism implementation. v1 implements the
/// `predicate` mechanism only; `text_pattern`, `llm_judged`, and `trigger_consequence` are
/// recognized by dispatch and log-and-skip (they are stubs this phase). The dispatch loop does
/// NOT branch on category NAME — only on `mechanism` — so new categories of an implemented
/// mechanism need zero detector changes (Constraint A).
/// </summary>
public sealed class BugDetector
{
    private readonly Rubric _rubric;

    public BugDetector(Rubric rubric) => _rubric = rubric;

    public DetectionResult Detect(LoadedTranscript transcript)
    {
        var candidates = new List<BugCandidate>();
        var skipped = new List<CategorySkip>();
        var coverage = new List<PredicateCoverage>();

        foreach (var category in _rubric.BugCategories)
        {
            switch (category.Mechanism)
            {
                case Mechanisms.Predicate:
                    RunPredicate(category, transcript, candidates, skipped, coverage);
                    break;

                case Mechanisms.TextPattern:
                case Mechanisms.LlmJudged:
                case Mechanisms.TriggerConsequence:
                    skipped.Add(new CategorySkip
                    {
                        Category = category.Name,
                        Mechanism = category.Mechanism,
                        Reason = $"mechanism '{category.Mechanism}' is not implemented in v1 (recognized; log-and-skip).",
                    });
                    break;

                default:
                    // Unreachable — RubricLoader rejects unknown mechanisms. Defensive only.
                    skipped.Add(new CategorySkip
                    {
                        Category = category.Name,
                        Mechanism = category.Mechanism,
                        Reason = "unrecognized mechanism reached dispatch (should have been rejected at load).",
                    });
                    break;
            }
        }

        return new DetectionResult { Candidates = candidates, Skipped = skipped, Coverage = coverage };
    }

    private static void RunPredicate(
        BugCategory category, LoadedTranscript transcript,
        List<BugCandidate> candidates, List<CategorySkip> skipped, List<PredicateCoverage> coverage)
    {
        var predicate = category.Predicate!;
        int evaluated = 0;
        int found = 0;

        // v1: scope is per-turn (runtime). Each TurnRecord is one record.
        foreach (var turn in transcript.Turns)
        {
            bool fired;
            try
            {
                fired = predicate.Evaluate(turn.Fields);
            }
            catch (PredicateException ex)
            {
                // A field the predicate needs is absent from the transcript: the check CANNOT
                // run. Surface it as a skip (with the reason), not as a clean pass. Stop after
                // the first record — the schema is uniform, so it will fail on every record.
                skipped.Add(new CategorySkip
                {
                    Category = category.Name,
                    Mechanism = category.Mechanism,
                    Reason = $"predicate could not evaluate — {ex.Message}",
                });
                return;
            }

            evaluated++;
            if (fired)
            {
                found++;
                candidates.Add(new BugCandidate
                {
                    Turn = turn.Turn,
                    Category = category.Name,
                    Mechanism = category.Mechanism,
                    Description = category.Description,
                    EvidenceSnippet = BuildEvidence(predicate, turn),
                });
            }
        }

        coverage.Add(new PredicateCoverage
        {
            Category = category.Name,
            TurnsEvaluated = evaluated,
            CandidatesFound = found,
        });
    }

    /// <summary>Render the firing record's referenced field values, e.g. "available_action_count=0, is_game_over=false at turn 412".</summary>
    private static string BuildEvidence(PredicateExpression predicate, LoadedTurn turn)
    {
        var parts = predicate.ReferencedFields
            .Select(name => turn.Fields.TryGetValue(name, out var v) ? $"{name}={v}" : $"{name}=<absent>");
        return $"{string.Join(", ", parts)} at turn {turn.Turn}";
    }
}
