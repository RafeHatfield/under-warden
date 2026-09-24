using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace UnderWarden.Analyst;

/// <summary>
/// Evaluates a directory of enriched transcripts in parallel against the predicate pipeline and
/// produces an <see cref="AggregateReport"/>. A single <see cref="BugDetector"/> is shared across
/// threads: it is reentrant (the rubric and its parsed predicates are immutable; Detect builds only
/// local state and PredicateExpression.Evaluate is pure). A transcript that fails to load is
/// recorded as a failure and skipped — one bad file never aborts the batch.
/// </summary>
public static class BatchAnalyzer
{
    public static AggregateReport Analyze(string batchDir, Rubric rubric, int concurrency)
    {
        if (!Directory.Exists(batchDir))
            throw new TranscriptLoadException($"Batch directory not found: {batchDir}");

        var files = Directory.GetFiles(batchDir, "*.jsonl").OrderBy(f => f, StringComparer.Ordinal).ToArray();

        var detector = new BugDetector(rubric);
        var runs = new ConcurrentBag<RunEvaluation>();
        var failures = new ConcurrentBag<(string, string)>();

        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, concurrency) };
        Parallel.ForEach(files, options, file =>
        {
            try
            {
                var transcript = TranscriptLoader.LoadFromFile(file);
                var detection = detector.Detect(transcript);
                runs.Add(new RunEvaluation
                {
                    RunId = string.IsNullOrEmpty(transcript.RunId) ? Path.GetFileName(file) : transcript.RunId,
                    PlayerType = transcript.PlayerType,
                    Detection = detection,
                    SystemTriggers = ExtractTriggerBooleans(transcript),
                });
            }
            catch (Exception ex)
            {
                failures.Add((Path.GetFileName(file), ex.Message));
            }
        });

        return AggregateReport.Build(batchDir, runs.ToList(), failures.ToList(), rubric.CoverageSemantics);
    }

    /// <summary>Lift the boolean fire flags out of RunSummary.system_triggers (the heatmap source).
    /// Non-boolean members (turn numbers, counts, nulls) are metadata, not fire/no-fire — excluded.</summary>
    private static Dictionary<string, bool> ExtractTriggerBooleans(LoadedTranscript transcript)
    {
        var map = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (transcript.Summary?.SystemTriggers is { } triggers)
        {
            foreach (var (key, node) in triggers)
                if (node is JsonValue v && v.TryGetValue<bool>(out var b))
                    map[key] = b;
        }
        return map;
    }
}
