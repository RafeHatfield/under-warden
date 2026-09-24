/// <summary>
/// YARL LLM Analyst — Phase 2 (BugDetector + single-run report).
///
/// Reads an enriched run transcript (JSONL), evaluates it against a rubric YAML, and writes a
/// per-run EvaluationReport (JSON). v1 implements the `predicate` mechanism only; text_pattern,
/// llm_judged, and trigger_consequence are recognized by dispatch and log-and-skip.
///
/// Usage (from project root):
///   dotnet run --project tools/Analyst -- \
///     --transcript reports/llm-transcripts/run-1337.jsonl \
///     --rubric config/rubric/v1.yaml \
///     --report reports/analyst/run-1337.json
///
///   # --report omitted -> report is printed to stdout.
///   # --model is accepted but UNUSED in v1 (no LLM pass until Phase 3).
/// </summary>

using UnderWarden.Analyst;

string? transcriptPath = null;
string? batchDir = null;
string? rubricPath = null;
string? reportPath = null;
string? aggregatePath = null;     // findings.md (batch)
string? aggregateJsonPath = null; // structured aggregate JSON (batch)
int concurrency = Environment.ProcessorCount;
string? model = null;   // accepted, unused until the coherence pass (Phase 3)
bool traceMode = false;
bool traceVerbose = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--transcript":     transcriptPath = Next(); break;
        case "--batch":          batchDir = Next(); break;
        case "--rubric":         rubricPath = Next(); break;
        case "--report":         reportPath = Next(); break;
        case "--aggregate":      aggregatePath = Next(); break;
        case "--aggregate-json": aggregateJsonPath = Next(); break;
        case "--concurrency":    concurrency = int.Parse(Next()); break;
        case "--model":          model = Next(); break;
        case "--trace":          traceMode = true; break;
        case "--verbose":        traceVerbose = true; break;
        default:
            Console.Error.WriteLine($"ERROR: unknown argument '{args[i]}'.");
            return 2;
    }

    string Next()
    {
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
        {
            Console.Error.WriteLine($"ERROR: {args[i]} requires a value.");
            Environment.Exit(2);
        }
        return args[++i];
    }
}

// ── TRACE MODE (no rubric needed) ───────────────────────────────────────────
if (traceMode)
{
    if (transcriptPath == null)
    {
        Console.Error.WriteLine("Usage: --trace --transcript <run.jsonl> [--verbose]");
        return 2;
    }
    string jsonl;
    try { jsonl = File.ReadAllText(transcriptPath); }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR: {ex.Message}"); return 1; }
    Console.WriteLine(TraceRenderer.Render(jsonl, new TraceOptions { Verbose = traceVerbose }));
    return 0;
}

if (rubricPath == null || (transcriptPath == null) == (batchDir == null))
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  trace:  --trace --transcript <run.jsonl> [--verbose]");
    Console.Error.WriteLine("  single: --transcript <run.jsonl> --rubric <rubric.yaml> [--report <out.json>]");
    Console.Error.WriteLine("  batch:  --batch <dir> --rubric <rubric.yaml> [--aggregate <findings.md>] [--aggregate-json <out.json>] [--concurrency N]");
    return 2;
}

if (model != null)
    Console.Error.WriteLine($"NOTE: --model '{model}' is accepted but unused in v1 (predicate-only; no LLM pass until Phase 3).");

Rubric rubric;
try
{
    rubric = RubricLoader.LoadFromFile(rubricPath);
}
catch (RubricLoadException ex)
{
    Console.Error.WriteLine($"ERROR loading rubric: {ex.Message}");
    return 1;
}

int predicateCount = rubric.BugCategories.Count(c => c.Mechanism == Mechanisms.Predicate);
int stubCount = rubric.BugCategories.Count - predicateCount;
Console.Error.WriteLine(
    $"Rubric v{rubric.SchemaVersion}: {rubric.BugCategories.Count} bug categories " +
    $"({predicateCount} predicate, {stubCount} stubbed/other).");

// ── BATCH MODE ──────────────────────────────────────────────────────────────
if (batchDir != null)
{
    AggregateReport aggregate;
    try
    {
        Console.Error.WriteLine($"Analyzing batch '{batchDir}' (concurrency {concurrency})...");
        aggregate = BatchAnalyzer.Analyze(batchDir, rubric, concurrency);
    }
    catch (TranscriptLoadException ex)
    {
        Console.Error.WriteLine($"ERROR: {ex.Message}");
        return 1;
    }

    var findings = aggregate.ToFindingsMarkdown();

    if (aggregatePath != null)
    {
        WriteTo(aggregatePath, findings);
        Console.Error.WriteLine($"Wrote {aggregatePath}");
    }
    if (aggregateJsonPath != null)
    {
        WriteTo(aggregateJsonPath, aggregate.ToJson());
        Console.Error.WriteLine($"Wrote {aggregateJsonPath}");
    }
    if (aggregatePath == null && aggregateJsonPath == null)
        Console.WriteLine(findings);

    Console.Error.WriteLine($"  {aggregate.Note}");
    foreach (var b in aggregate.MechanismBlindSpots)
        Console.Error.WriteLine($"  BLIND SPOT (0x, unverified): {b.Trigger}");
    // Exit 2 when bug candidates are found, so CI / shell scripts can gate on findings.
    // Exit 0 = clean batch; exit 1 = tool error; exit 2 = candidates found.
    return aggregate.BugCandidates.Count > 0 ? 2 : 0;
}

// ── SINGLE-RUN MODE ─────────────────────────────────────────────────────────
string jsonlText;
try
{
    jsonlText = File.ReadAllText(transcriptPath!);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR reading transcript: {ex.Message}");
    return 1;
}

// Human-readable run report first — gives the reader context before the predicate JSON.
Console.WriteLine(RunReporter.Render(jsonlText));

LoadedTranscript transcript;
try
{
    transcript = TranscriptLoader.LoadFromText(jsonlText, transcriptPath!);
}
catch (TranscriptLoadException ex)
{
    Console.Error.WriteLine($"ERROR loading transcript: {ex.Message}");
    return 1;
}

var result = new BugDetector(rubric).Detect(transcript);
var report = EvaluationReport.Build(transcript, result);
var reportJson = report.ToJson();

if (reportPath != null)
{
    WriteTo(reportPath, reportJson);
    Console.Error.WriteLine($"Wrote {reportPath}");
}
else
{
    Console.WriteLine(reportJson);
}

Console.Error.WriteLine($"  {report.AnalystNote}");
foreach (var skip in result.Skipped)
    Console.Error.WriteLine($"  SKIP {skip.Category} [{skip.Mechanism}]: {skip.Reason}");

return 0;

static void WriteTo(string path, string content)
{
    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    File.WriteAllText(path, content);
}
