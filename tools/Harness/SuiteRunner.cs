using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnderWarden.Logic.Balance;

/// <summary>
/// Balance acceptance suite runner.
///
/// Runs a fixed 15-scenario matrix (port of PoC SCENARIO_MATRIX from
/// ~/development/rlike/tools/balance_suite.py:34-61), emits per-scenario JSON metrics,
/// a markdown balance report, and a machine-readable verdict.json for CI.
///
/// Key behaviors:
/// - No baseline: status=NO_BASELINE, acceptance_status=PASS, exit 0
/// - With baseline: compares each scenario against stored NormalizedMetrics,
///   classifies PASS/WARN/FAIL per metric delta, rolls up to overall verdict
/// - --update-baseline: writes new baseline regardless of verdict, exits 0
/// - --strict: exit 1 on any FAIL; else exit 0
///
/// Drift thresholds (verbatim from PoC THRESHOLDS dict, balance_suite.py:64-70):
///   death_rate:          WARN ≥ 0.10, FAIL ≥ 0.20
///   player_hit_rate:     WARN ≥ 0.05, FAIL ≥ 0.10
///   monster_hit_rate:    WARN ≥ 0.05, FAIL ≥ 0.10
///   pressure_index:      WARN ≥ 5.0,  FAIL ≥ 10.0
///   bonus_attacks_per_run: WARN ≥ 2.0, FAIL ≥ 4.0
/// </summary>
public static class SuiteRunner
{
    // ── Matrix definition ─────────────────────────────────────────────────────

    public sealed record SuiteEntry(string ScenarioId, int Runs, int TurnLimit);

    /// <summary>
    /// Full 15-scenario matrix. Verbatim from PoC SCENARIO_MATRIX (balance_suite.py:34-61).
    /// </summary>
    public static IReadOnlyList<SuiteEntry> Matrix { get; } =
    [
        // Depth 3 orc brutal + weapon variants
        new("depth3_orc_brutal",          50, 110),
        new("depth3_orc_brutal_keen",     50, 110),
        new("depth3_orc_brutal_vicious",  50, 110),
        new("depth3_orc_brutal_fine",     50, 110),
        new("depth3_orc_brutal_masterwork", 50, 110),
        // Depth 5 zombie + weapon variants
        new("depth5_zombie",              50, 150),
        new("depth5_zombie_keen",         50, 150),
        new("depth5_zombie_vicious",      50, 150),
        new("depth5_zombie_fine",         50, 150),
        new("depth5_zombie_masterwork",   50, 150),
        // Depth 2 orc baseline + weapon variants
        new("depth2_orc_baseline",        40, 100),
        new("depth2_orc_baseline_keen",   40, 100),
        new("depth2_orc_baseline_vicious",40, 100),
        new("depth2_orc_baseline_fine",   40, 100),
        new("depth2_orc_baseline_masterwork", 40, 100),
    ];

    /// <summary>
    /// Fast 6-scenario subset: one baseline scenario per depth band.
    /// Suitable for inner-loop local development (roughly 2-3 minutes vs 6+ for full suite).
    /// </summary>
    public static IReadOnlyList<SuiteEntry> FastMatrix { get; } =
    [
        new("depth2_orc_baseline", 20, 100),
        new("depth3_orc_brutal",   20, 110),
        new("depth5_zombie",       20, 150),
        // Three weapon variants to check weapon progression signal
        new("depth2_orc_baseline_fine",       20, 100),
        new("depth3_orc_brutal_fine",         20, 110),
        new("depth5_zombie_fine",             20, 150),
    ];

    // Pure-logic evaluation delegated to BalanceSuiteEvaluator (in Logic layer, testable)

    // ── Run ───────────────────────────────────────────────────────────────────

    public sealed record SuiteResult(
        string ScenarioId,
        int Runs,
        AggregatedMetrics Metrics,
        NormalizedMetrics Normalized,
        string Verdict,   // "PASS" | "WARN" | "FAIL" | "PROBE" | "NO_BASELINE"
        Dictionary<string, double>? Deltas);

    /// <summary>
    /// Run the full or fast scenario matrix and write output files to outDir.
    /// Returns exit code: 0 = PASS/WARN/NO_BASELINE, 1 = FAIL (when baseline present).
    /// </summary>
    public static int Run(
        ScenarioRunner runner,
        string levelsDir,
        DirectoryInfo outDir,
        int seedBase,
        bool fast,
        string? baselinePath,
        bool updateBaseline,
        out List<SuiteResult> results)
    {
        var matrix = fast ? FastMatrix : Matrix;
        results = new List<SuiteResult>();

        // ── Load baseline ──────────────────────────────────────────────────
        var resolvedBaselinePath = baselinePath
            ?? Path.Combine("reports", "baselines", "balance_suite_baseline.json");
        Dictionary<string, NormalizedMetrics>? baseline = null;
        if (!updateBaseline && File.Exists(resolvedBaselinePath))
        {
            baseline = LoadBaseline(resolvedBaselinePath);
        }

        // ── Run each scenario ──────────────────────────────────────────────
        Console.Error.WriteLine($"Balance suite: {matrix.Count} scenarios, seed {seedBase}...");
        foreach (var entry in matrix)
        {
            Console.Error.Write($"  Running {entry.ScenarioId} ({entry.Runs} runs)...");
            try
            {
                var scenarioPath = FindScenarioPath(levelsDir, entry.ScenarioId);
                if (scenarioPath == null)
                {
                    Console.Error.WriteLine($" NOT FOUND — skipping.");
                    continue;
                }

                // Run with turn limit and run count overrides from the matrix
                var metrics = runner.RunFromFileWithOverrides(
                    scenarioPath, seedBase, entry.Runs, entry.TurnLimit);
                var normalized = NormalizedMetrics.From(metrics);

                string verdict;
                Dictionary<string, double>? deltas = null;

                if (metrics.IsProbe)
                {
                    verdict = "PROBE";
                }
                else if (baseline != null && baseline.TryGetValue(entry.ScenarioId, out var baselineMetrics))
                {
                    deltas  = ComputeDeltas(normalized, baselineMetrics);
                    verdict = ClassifyVerdict(deltas);
                }
                else
                {
                    verdict = "NO_BASELINE";
                }

                results.Add(new SuiteResult(entry.ScenarioId, entry.Runs, metrics, normalized, verdict, deltas));
                Console.Error.WriteLine($" {verdict}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($" ERROR: {ex.Message}");
            }
        }

        // ── Update baseline mode ────────────────────────────────────────────
        if (updateBaseline)
        {
            var baselineData = results.ToDictionary(
                r => r.ScenarioId,
                r => r.Normalized);
            SaveBaseline(resolvedBaselinePath, baselineData);
            Console.Error.WriteLine($"Baseline written to {resolvedBaselinePath}");
            WriteOutputFiles(outDir, results, baselineData, seedBase);
            return 0;
        }

        // ── Write output files ──────────────────────────────────────────────
        var currentSummary = results.ToDictionary(r => r.ScenarioId, r => r.Normalized);
        WriteOutputFiles(outDir, results, currentSummary, seedBase);

        // ── Exit code ───────────────────────────────────────────────────────
        bool anyFail = results.Any(r => r.Verdict == "FAIL");
        return anyFail ? 1 : 0;
    }

    // ── Delta computation — delegates to BalanceSuiteEvaluator (logic layer) ─

    /// <summary>
    /// Compute per-metric deltas: current - baseline.
    /// </summary>
    public static Dictionary<string, double> ComputeDeltas(
        NormalizedMetrics current, NormalizedMetrics baseline)
        => BalanceSuiteEvaluator.ComputeDeltas(current, baseline);

    /// <summary>
    /// PASS/WARN/FAIL based on absolute delta magnitudes.
    /// </summary>
    public static string ClassifyVerdict(Dictionary<string, double> deltas)
        => BalanceSuiteEvaluator.ClassifyVerdict(deltas);

    // ── Baseline IO ────────────────────────────────────────────────────────

    /// <summary>
    /// Load baseline from JSON file. Returns null if file is empty or malformed.
    /// Forward-compatible: extra fields in baseline JSON are ignored.
    /// </summary>
    public static Dictionary<string, NormalizedMetrics>? LoadBaseline(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var raw = JsonSerializer.Deserialize<Dictionary<string, BaselineEntry>>(json,
                JsonOptions);
            if (raw == null) return null;
            return raw.ToDictionary(
                kv => kv.Key,
                kv => new NormalizedMetrics(
                    kv.Value.ScenarioId ?? kv.Key,
                    kv.Value.Runs,
                    kv.Value.Deaths,
                    kv.Value.DeathRate,
                    kv.Value.PlayerHitRate,
                    kv.Value.MonsterHitRate,
                    kv.Value.PressureIndex,
                    kv.Value.BonusAttacksPerRun));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Save normalized metrics as baseline JSON.
    /// Uses full IEEE 754 precision (PoC uses Python's default json.dump — 15-17 digit floats).
    /// </summary>
    public static void SaveBaseline(string path, Dictionary<string, NormalizedMetrics> data)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Build serializable dict matching PoC baseline schema exactly
        var serializable = data.ToDictionary(
            kv => kv.Key,
            kv => new BaselineEntry
            {
                ScenarioId         = kv.Value.ScenarioId,
                Runs               = kv.Value.Runs,
                Deaths             = kv.Value.Deaths,
                DeathRate          = kv.Value.DeathRate,
                PlayerHitRate      = kv.Value.PlayerHitRate,
                MonsterHitRate     = kv.Value.MonsterHitRate,
                PressureIndex      = kv.Value.PressureIndex,
                BonusAttacksPerRun = kv.Value.BonusAttacksPerRun,
            });

        // Use UTF-8 without BOM — JSON files must not start with a BOM or standard parsers break.
        File.WriteAllText(path,
            JsonSerializer.Serialize(serializable, JsonOptionsPretty),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // ── Output file writers ────────────────────────────────────────────────

    private static void WriteOutputFiles(
        DirectoryInfo outDir,
        List<SuiteResult> results,
        Dictionary<string, NormalizedMetrics> summary,
        int seedBase)
    {
        outDir.Create();
        Directory.CreateDirectory(Path.Combine(outDir.FullName, "metrics", "raw"));

        // Per-scenario raw metrics
        foreach (var r in results)
        {
            var rawPath = Path.Combine(outDir.FullName, "metrics", "raw", $"{r.ScenarioId}.json");
            // No BOM in JSON files
            File.WriteAllText(rawPath,
                JsonSerializer.Serialize(r.Metrics, JsonOptionsPretty),
                new System.Text.UTF8Encoding(false));
        }

        // summary.json — normalized metrics for all scenarios (input for baseline)
        var summaryPath = Path.Combine(outDir.FullName, "summary.json");
        File.WriteAllText(summaryPath,
            JsonSerializer.Serialize(summary, JsonOptionsPretty),
            new System.Text.UTF8Encoding(false));

        // balance_report.md
        var reportPath = Path.Combine(outDir.FullName, "balance_report.md");
        File.WriteAllText(reportPath, GenerateMarkdownReport(results),
            new System.Text.UTF8Encoding(false));

        // verdict.json
        var verdictPath = Path.Combine(outDir.FullName, "verdict.json");
        File.WriteAllText(verdictPath, GenerateVerdictJson(results),
            new System.Text.UTF8Encoding(false));

        Console.Error.WriteLine($"Output written to {outDir.FullName}/");
    }

    // ── Markdown report (port of PoC generate_markdown_report) ────────────

    private static string GenerateMarkdownReport(List<SuiteResult> results)
    {
        bool hasBaseline = results.Any(r => r.Verdict is "PASS" or "WARN" or "FAIL");
        var verdictCounts = results
            .Where(r => r.Verdict is "PASS" or "WARN" or "FAIL" or "PROBE")
            .GroupBy(r => r.Verdict)
            .ToDictionary(g => g.Key, g => g.Count());

        var sb = new StringBuilder();
        sb.AppendLine("# Balance Suite Report");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine($"**Scenarios:** {results.Count}");
        sb.AppendLine();

        // Verdict summary
        sb.AppendLine("## Verdict Summary");
        sb.AppendLine();
        if (!hasBaseline)
        {
            sb.AppendLine("**Status:** NO_BASELINE");
            sb.AppendLine();
            sb.AppendLine("No baseline found. Run `harness --suite --update-baseline` to create one.");
        }
        else
        {
            sb.AppendLine($"- PASS: {verdictCounts.GetValueOrDefault("PASS", 0)}");
            sb.AppendLine($"- WARN: {verdictCounts.GetValueOrDefault("WARN", 0)}");
            sb.AppendLine($"- FAIL: {verdictCounts.GetValueOrDefault("FAIL", 0)}");
            if (verdictCounts.TryGetValue("PROBE", out int probeCount))
                sb.AppendLine($"- PROBE: {probeCount}");
        }

        // Per-scenario details
        sb.AppendLine();
        sb.AppendLine("## Scenario Details");

        foreach (var r in results.OrderBy(r => r.ScenarioId))
        {
            sb.AppendLine();
            sb.AppendLine($"### {r.ScenarioId}");
            sb.AppendLine();
            sb.AppendLine($"- Runs: {r.Normalized.Runs}");
            sb.AppendLine($"- Deaths: {r.Normalized.Deaths} (rate: {r.Normalized.DeathRate:P2})");
            sb.AppendLine($"- Player Hit Rate: {r.Normalized.PlayerHitRate:P2}");
            sb.AppendLine($"- Monster Hit Rate: {r.Normalized.MonsterHitRate:P2}");
            sb.AppendLine($"- Pressure Index: {r.Normalized.PressureIndex:F2}");
            sb.AppendLine($"- Bonus Attacks/Run: {r.Normalized.BonusAttacksPerRun:F2}");

            if (r.Deltas != null)
            {
                sb.AppendLine();
                sb.AppendLine($"**Verdict:** {r.Verdict}");
                sb.AppendLine();
                sb.AppendLine("**Deltas from Baseline:**");
                sb.AppendLine($"- Death Rate: {r.Deltas["death_rate"]:+0.##%;-0.##%;0%}");
                sb.AppendLine($"- Player Hit Rate: {r.Deltas["player_hit_rate"]:+0.##%;-0.##%;0%}");
                sb.AppendLine($"- Monster Hit Rate: {r.Deltas["monster_hit_rate"]:+0.##%;-0.##%;0%}");
                sb.AppendLine($"- Pressure Index: {r.Deltas["pressure_index"]:+0.##;-0.##;0}");
                sb.AppendLine($"- Bonus Attacks/Run: {r.Deltas["bonus_attacks_per_run"]:+0.##;-0.##;0}");
            }
        }

        return sb.ToString();
    }

    // ── verdict.json (port of PoC generate_verdict_json) ──────────────────

    private static string GenerateVerdictJson(List<SuiteResult> results)
    {
        bool hasBaseline = results.Any(r => r.Verdict is "PASS" or "WARN" or "FAIL");

        if (!hasBaseline)
        {
            var noBaselineDoc = new
            {
                status = "NO_BASELINE",
                acceptance_status = "PASS",
                timestamp = DateTime.UtcNow.ToString("O"),
                scenarios = results.Count,
                verdicts = new Dictionary<string, int>(),
            };
            return JsonSerializer.Serialize(noBaselineDoc, JsonOptionsPretty);
        }

        var verdictCounts = results
            .Where(r => r.Verdict is "PASS" or "WARN" or "FAIL" or "PROBE")
            .GroupBy(r => r.Verdict)
            .ToDictionary(g => g.Key, g => g.Count());

        string acceptanceStatus = "PASS";
        if (verdictCounts.GetValueOrDefault("FAIL", 0) > 0)
            acceptanceStatus = "FAIL";
        else if (verdictCounts.GetValueOrDefault("WARN", 0) > 0)
            acceptanceStatus = "WARN";

        var details = new Dictionary<string, object>();
        foreach (var r in results.Where(r => r.Deltas != null))
        {
            details[r.ScenarioId] = new
            {
                verdict = r.Verdict,
                deltas  = r.Deltas,
            };
        }

        var doc = new
        {
            status            = "COMPLETED",
            acceptance_status = acceptanceStatus,
            timestamp         = DateTime.UtcNow.ToString("O"),
            scenarios         = results.Count,
            verdicts          = verdictCounts,
            details           = details,
        };
        return JsonSerializer.Serialize(doc, JsonOptionsPretty);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string? FindScenarioPath(string levelsDir, string scenarioId)
    {
        var direct = Path.Combine(levelsDir, $"scenario_{scenarioId}.yaml");
        if (File.Exists(direct)) return direct;
        var alt = Path.Combine(levelsDir, $"{scenarioId}.yaml");
        if (File.Exists(alt)) return alt;
        return null;
    }

    // JSON options with snake_case for PoC compatibility
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly JsonSerializerOptions JsonOptionsPretty = new()
    {
        WriteIndented               = true,
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Intermediate record for loading/saving baseline JSON.
    /// Matches PoC schema: { scenario_id, runs, deaths, death_rate, ... }
    /// </summary>
    private sealed class BaselineEntry
    {
        [JsonPropertyName("scenario_id")]
        public string? ScenarioId { get; set; }
        [JsonPropertyName("runs")]
        public int Runs { get; set; }
        [JsonPropertyName("deaths")]
        public int Deaths { get; set; }
        [JsonPropertyName("death_rate")]
        public double DeathRate { get; set; }
        [JsonPropertyName("player_hit_rate")]
        public double PlayerHitRate { get; set; }
        [JsonPropertyName("monster_hit_rate")]
        public double MonsterHitRate { get; set; }
        [JsonPropertyName("pressure_index")]
        public double PressureIndex { get; set; }
        [JsonPropertyName("bonus_attacks_per_run")]
        public double BonusAttacksPerRun { get; set; }
    }
}
