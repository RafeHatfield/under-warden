/// <summary>
/// YARL Balance Pipeline CLI
///
/// Usage (run from project root):
///   dotnet run --project tools/Harness -- --scenario depth1_tuned
///   dotnet run --project tools/Harness -- --scenario depth1_tuned --runs 100 --seed 42
///   dotnet run --project tools/Harness -- --all
///   dotnet run --project tools/Harness -- --all --runs 50
///   dotnet run --project tools/Harness -- --scenario depth1_tuned --json
///   dotnet run --project tools/Harness -- --dungeon --floors 6 --runs 100 --seed 1337
///   dotnet run --project tools/Harness -- --dungeon --floors 3 --runs 50 --jsonl reports/soak.jsonl
///   dotnet run --project tools/Harness -- --dungeon --floors 3 --runs 10 --report
///   dotnet run --project tools/Harness -- --report --jsonl-in reports/soak.jsonl
/// </summary>

using System.Text.Json;
using System.Text.Json.Serialization;
using UnderWarden.Harness.LlmPlayer;
using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Balance.LlmPlayer;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

const string EntitiesFile        = "config/entities.yaml";
const string LevelsDir           = "config/levels";
const string LevelTemplatesFile  = "config/level_templates.yaml";
const string DepthBoonsFile      = "config/depth_boons.yaml";
const string LootTagsFile        = "config/loot_tags.yaml";
const string LootPolicyFile      = "config/loot_policy.yaml";
const string TargetTableFile     = "config/balance/target_table.yaml";
const string SoakBaselineFile    = "reports/baselines/soak_baseline.json";
const string GearProfilesFile    = "config/balance/gear_profiles.yaml";

// ─── Parse args ────────────────────────────────────────────────────────────

string? scenarioId   = null;
bool    runAll       = false;
bool    jsonOutput   = false;
bool    dungeonMode  = false;
bool    suiteMode    = false;
bool    suitefast    = false;
bool    botReportMode = false;
string? botReportMatrix = null;  // "fast" | "full"
string? botReportOut    = null;
string? suiteOutDir  = null;
string? baselinePath = null;
bool    updateBaseline = false;
bool    depthReportMode = false;
string? depthReportIn   = null;
string? depthReportOut  = null;
bool    artSceneDumpMode = false;
bool    etpSanityMode   = false;
bool    etpSanityStrict = false;
int?    etpSanityDepth  = null;
int     etpSanityRuns   = 1;
bool    verbose      = false;
bool    printReport  = false;
bool    transcriptMode = false;
string? llmTranscriptDir = null;  // --llm-transcript <dir>: emit enriched JSONL, one file per run
int?    runsOverride = null;
int     seed         = 1337;
// Default to 10 floors (B1+B2) for soak runs. The canonical dungeon is 25 floors (B1-B5),
// but 10 is enough to verify band-density and pity behaviour without long run times.
int     floors       = 10;
int     startFloor   = 1;       // --start-floor N: staged-start soak begins at depth N (step 8)
string? gearName     = null;    // --gear <profile>: staged-start gear profile key (b1..b5)
string? jsonlPath    = null;
string? jsonlInPath  = null;
string? personaName  = null;  // --persona <name>, default: null → balanced
string  playerMode   = "bot"; // --player bot|llm, default: bot

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--scenario":  scenarioId   = args[++i]; break;
        case "--all":       runAll       = true;      break;
        case "--runs":      runsOverride = int.Parse(args[++i]); break;
        case "--seed":      seed         = int.Parse(args[++i]); break;
        case "--json":      jsonOutput   = true;      break;
        case "--dungeon":   dungeonMode  = true;      break;
        case "--suite":     suiteMode    = true;      break;
        case "--fast":      suitefast    = true;      break;
        case "--out-dir":   suiteOutDir  = args[++i]; break;
        case "--baseline":  baselinePath = args[++i]; break;
        case "--update-baseline": updateBaseline = true; break;
        case "--depth-report": depthReportMode = true; break;
        case "--in":  depthReportIn  = args[++i]; break;
        case "--out": depthReportOut = args[++i]; break;
        case "--art-scene-dump": artSceneDumpMode = true; break;
        case "--etp-sanity":   etpSanityMode   = true; break;
        case "--strict":       etpSanityStrict = true; break;
        case "--depth":        etpSanityDepth  = int.Parse(args[++i]); break;
        case "--transcript": transcriptMode = true;  break;
        case "--llm-transcript":
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
            {
                Console.Error.WriteLine("ERROR: --llm-transcript requires an output directory argument.");
                return 1;
            }
            llmTranscriptDir = args[++i];
            break;
        case "--floors":    floors       = int.Parse(args[++i]); break;
        case "--start-floor": startFloor = int.Parse(args[++i]); break;
        case "--gear":
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
            {
                Console.Error.WriteLine("ERROR: --gear requires a profile name (b1, b2, b3, b4, b5).");
                return 1;
            }
            gearName = args[++i];
            break;
        case "--verbose":   verbose      = true;      break;
        case "--report":    printReport  = true;      break;
        case "--jsonl":
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
            {
                Console.Error.WriteLine("ERROR: --jsonl requires a file path argument.");
                return 1;
            }
            jsonlPath = args[++i];
            break;
        case "--jsonl-in":
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
            {
                Console.Error.WriteLine("ERROR: --jsonl-in requires a file path argument.");
                return 1;
            }
            jsonlInPath = args[++i];
            break;
        case "--persona":
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
            {
                Console.Error.WriteLine("ERROR: --persona requires a name argument (balanced, cautious, aggressive, greedy, speedrunner).");
                return 1;
            }
            personaName = args[++i];
            break;
        case "--player":
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
            {
                Console.Error.WriteLine("ERROR: --player requires a mode argument (bot|llm).");
                return 1;
            }
            playerMode = args[++i].ToLowerInvariant();
            if (playerMode != "bot" && playerMode != "llm")
            {
                Console.Error.WriteLine($"ERROR: --player must be 'bot' or 'llm' (got '{playerMode}').");
                return 1;
            }
            break;
        case "--bot-report":
            botReportMode = true;
            break;
        case "--matrix":
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                botReportMatrix = args[++i];
            break;
        case "--help": case "-h":
            PrintHelp(); return 0;
    }
}

// ─── Validate mode ─────────────────────────────────────────────────────────

if (suiteMode && (dungeonMode || scenarioId != null || runAll))
{
    Console.Error.WriteLine("ERROR: --suite is mutually exclusive with --dungeon, --scenario, and --all.");
    return 1;
}

if (dungeonMode && (scenarioId != null || runAll))
{
    Console.Error.WriteLine("ERROR: --dungeon is mutually exclusive with --scenario and --all.");
    return 1;
}

// --jsonl-in without --report makes no sense: the data would be read and discarded.
if (jsonlInPath != null && !printReport)
{
    Console.Error.WriteLine("ERROR: --jsonl-in requires --report (otherwise the data is unused).");
    return 1;
}

// --report --jsonl-in --dungeon: ambiguous data source. Pick one.
if (printReport && jsonlInPath != null && dungeonMode)
{
    Console.Error.WriteLine("ERROR: --report with both --dungeon and --jsonl-in is ambiguous. Pick one data source.");
    return 1;
}

// ─── OFFLINE REPORT MODE (--report --jsonl-in) ─────────────────────────────

if (printReport && jsonlInPath != null && !dungeonMode)
{
    if (!File.Exists(jsonlInPath))
    {
        Console.Error.WriteLine($"ERROR: JSONL input file not found: '{jsonlInPath}'");
        return 1;
    }

    Console.Error.WriteLine($"Reading soak data from {jsonlInPath}...");
    var offlineSummary = SoakJsonlReader.ReadFromFile(jsonlInPath);
    Console.Error.WriteLine($"Loaded {offlineSummary.RunsAttempted} runs.");

    var offlineReport = DungeonSoakReport.Generate(offlineSummary);
    Console.WriteLine(offlineReport);
    return 0;
}

// ─── SUITE MODE ────────────────────────────────────────────────────────────

if (suiteMode)
{
    if (!File.Exists(EntitiesFile))
    {
        Console.Error.WriteLine($"ERROR: '{EntitiesFile}' not found. Run from project root.");
        return 1;
    }

    ScenarioRunner suiteRunner;
    try   { suiteRunner = ScenarioRunner.FromEntitiesFile(EntitiesFile); }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR loading entities: {ex.Message}"); return 1; }

    string outDirPath = suiteOutDir
        ?? Path.Combine("reports", "balance_suite", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
    var outDir = new DirectoryInfo(outDirPath);

    int exitCode = SuiteRunner.Run(
        suiteRunner,
        LevelsDir,
        outDir,
        seed,
        suitefast,
        baselinePath,
        updateBaseline,
        out var suiteResults);

    // Print brief summary table to stdout
    Console.WriteLine();
    Console.WriteLine($"=== Balance Suite ({(suitefast ? "fast" : "full")}, seed {seed}) ===");
    Console.WriteLine();
    foreach (var r in suiteResults)
        Console.WriteLine($"  {r.ScenarioId,-35}  {r.Verdict}");
    Console.WriteLine();

    int passCount  = suiteResults.Count(r => r.Verdict == "PASS");
    int warnCount  = suiteResults.Count(r => r.Verdict == "WARN");
    int failCount  = suiteResults.Count(r => r.Verdict == "FAIL");
    int probeCount = suiteResults.Count(r => r.Verdict == "PROBE");
    int noBaselineCount = suiteResults.Count(r => r.Verdict == "NO_BASELINE");

    if (noBaselineCount > 0)
        Console.WriteLine($"  Status: NO_BASELINE ({noBaselineCount} scenarios — run --update-baseline to create baseline)");
    else
        Console.WriteLine($"  Results: {passCount} PASS  {warnCount} WARN  {failCount} FAIL  {probeCount} PROBE");
    Console.WriteLine();
    Console.WriteLine($"  Report: {outDir.FullName}/balance_report.md");

    return exitCode;
}

// ─── DEPTH REPORT MODE ─────────────────────────────────────────────────────

if (depthReportMode)
{
    if (string.IsNullOrEmpty(depthReportIn))
    {
        Console.Error.WriteLine("ERROR: --depth-report requires --in <dir-or-file>");
        return 1;
    }

    IReadOnlyList<UnderWarden.Logic.Balance.DepthPressureReport.DepthCurvePoint> points;
    try
    {
        points = DepthReportLoader.Load(depthReportIn);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR loading depth report data: {ex.Message}");
        return 1;
    }

    if (points.Count == 0)
    {
        Console.Error.WriteLine("No depth data found in input.");
        return 1;
    }

    var report = UnderWarden.Logic.Balance.DepthPressureReport.FormatFullReport(points);

    if (!string.IsNullOrEmpty(depthReportOut))
    {
        File.WriteAllText(depthReportOut, report,
            new System.Text.UTF8Encoding(false));
        Console.Error.WriteLine($"Report written to {depthReportOut}");
    }
    else
    {
        Console.WriteLine(report);
    }

    return 0;
}

// ─── ART SCENE DUMP MODE ───────────────────────────────────────────────────
// Produces the docs/art_test_scene_spec_v2.md §4 determinism evidence: two independent
// cold-start builds of ArtAcceptanceSceneBuilder's fixed scene, dumped to text, diffed.
// Fresh ContentLoader/EntityFactory/*Factory instances each time — a true cold start,
// not reuse of warm state that could mask a hidden ordering dependency.

if (artSceneDumpMode)
{
    if (!File.Exists(EntitiesFile))
    {
        Console.Error.WriteLine("ERROR: config/entities.yaml required for --art-scene-dump. Run from project root.");
        return 1;
    }

    string BuildDump()
    {
        var loader = new ContentLoader();
        var content = loader.LoadAllFromFile(EntitiesFile);
        var entityFactory = new EntityFactory();
        var itemFactory = new ItemFactory(content.Items, entityFactory);
        var consumableFactory = new ConsumableFactory(content.Consumables, entityFactory);
        var monsterFactory = new MonsterFactory(content.Monsters, entityFactory, itemFactory);
        var state = ArtAcceptanceSceneBuilder.Build(monsterFactory, itemFactory, consumableFactory);
        return ArtAcceptanceSceneDump.Dump(state);
    }

    var dump1 = BuildDump();
    var dump2 = BuildDump();

    const string evidenceDir = "tools/art_lint/scene_evidence";
    Directory.CreateDirectory(evidenceDir);
    File.WriteAllText(Path.Combine(evidenceDir, "run1_floor_dump.txt"), dump1);
    File.WriteAllText(Path.Combine(evidenceDir, "run2_floor_dump.txt"), dump2);

    bool identical = dump1 == dump2;
    var diffResult = identical
        ? "IDENTICAL — 0 differences.\n"
        : "DIFFERENCES FOUND — see run1_floor_dump.txt / run2_floor_dump.txt.\n";
    File.WriteAllText(Path.Combine(evidenceDir, "diff_result.txt"), diffResult);

    Console.WriteLine(identical
        ? "Determinism check PASSED: two cold builds are byte-identical."
        : "Determinism check FAILED: cold builds differ — see scene_evidence/.");
    return identical ? 0 : 1;
}

// ─── ETP SANITY MODE ───────────────────────────────────────────────────────

if (etpSanityMode)
{
    if (!File.Exists(EntitiesFile) || !File.Exists(LevelTemplatesFile))
    {
        Console.Error.WriteLine("ERROR: entities.yaml and level_templates.yaml required for --etp-sanity. Run from project root.");
        return 1;
    }

    const string EtpConfigFile = "config/etp_config.yaml";
    if (!File.Exists(EtpConfigFile))
    {
        Console.Error.WriteLine($"ERROR: '{EtpConfigFile}' not found.");
        return 1;
    }

    var etpCfg = UnderWarden.Logic.Balance.Etp.EtpConfigLoader.FromFile(EtpConfigFile);
    DungeonFloorBuilder etpBuilder;
    try
    {
        etpBuilder = BuildDungeonFloorBuilder(EntitiesFile, LevelTemplatesFile, DepthBoonsFile);
    }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR loading content: {ex.Message}"); return 1; }

    int[] depths = etpSanityDepth.HasValue
        ? [etpSanityDepth.Value]
        : UnderWarden.Logic.Balance.Etp.EtpSanityHarness.DefaultDepths;

    // RunSanity writes the CSV header itself; pass Console.Out as the CSV output stream
    int exitCode = UnderWarden.Logic.Balance.Etp.EtpSanityHarness.RunSanity(
        etpBuilder, etpCfg,
        depths:        depths,
        strict:        etpSanityStrict,
        verbose:       verbose,
        runsPerDepth:  etpSanityRuns,
        csvOut:        Console.Out);

    if (etpSanityStrict && exitCode != 0)
        Console.Error.WriteLine("ETP sanity FAILED: OVER violations detected in normal rooms.");
    return exitCode;
}

if (!dungeonMode && scenarioId == null && !runAll && !botReportMode)
{
    Console.Error.WriteLine("Usage: harness --scenario <id> | --all | --dungeon | --suite | --bot-report | --depth-report | --etp-sanity [options]");
    Console.Error.WriteLine("Run with --help for details.");
    return 1;
}

// ─── Resolve persona ──────────────────────────────────────────────────────────

// Validate persona name early — unknown name emits a warning (not error) and falls back.
// When --player llm, persona is validated later in the LLM branch (accepts reader|system_explorer).
BotPersonaConfig? resolvedPersona = null;
if (personaName != null && playerMode != "llm")
{
    resolvedPersona = BotPersonaRegistry.Get(personaName);
    // BotPersonaRegistry.Get() already emits a stderr warning on unknown names.
    if (resolvedPersona.Name != personaName)
    {
        // Unknown persona — warning was already emitted by registry; use balanced fallback.
    }
}
// When personaName is null, resolvedPersona is null → harnesses default to balanced.

if (!File.Exists(EntitiesFile))
{
    Console.Error.WriteLine($"ERROR: '{EntitiesFile}' not found.");
    Console.Error.WriteLine("Run from the project root (the directory containing config/).");
    return 1;
}

// ─── BOT REPORT MODE ──────────────────────────────────────────────────────────

if (botReportMode)
{
    if (!File.Exists(EntitiesFile))
    {
        Console.Error.WriteLine($"ERROR: '{EntitiesFile}' not found. Run from project root.");
        return 1;
    }

    int reportRuns = runsOverride ?? (botReportMatrix == "full" ? 50 : 20);
    bool useFastMatrix = botReportMatrix != "full";
    var reportOutPath = botReportOut ?? (args.Contains("--out") ? args[Array.IndexOf(args, "--out") + 1] : null);

    Console.Error.WriteLine($"Running bot survivability report (matrix={botReportMatrix ?? "fast"}, runs={reportRuns})...");

    ScenarioRunner reportRunner;
    try { reportRunner = ScenarioRunner.FromEntitiesFile(EntitiesFile); }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR loading entities: {ex.Message}"); return 1; }

    var report = BotSurvivabilityReport.Generate(reportRunner, LevelsDir, useFastMatrix, reportRuns, seed);
    if (reportOutPath != null)
    {
        var dir = Path.GetDirectoryName(reportOutPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(reportOutPath, report, new System.Text.UTF8Encoding(false));
        Console.Error.WriteLine($"Report written to {reportOutPath}");
    }
    else
    {
        Console.WriteLine(report);
    }
    return 0;
}

// ─── DUNGEON MODE ──────────────────────────────────────────────────────────

if (dungeonMode)
{
    if (!File.Exists(LevelTemplatesFile))
    {
        Console.Error.WriteLine($"ERROR: '{LevelTemplatesFile}' not found.");
        return 1;
    }

    int runs = runsOverride ?? 100;

    Console.Error.WriteLine($"Building dungeon floor factory...");

    DungeonFloorBuilder floorBuilder;
    try
    {
        floorBuilder = BuildDungeonFloorBuilder(EntitiesFile, LevelTemplatesFile, DepthBoonsFile);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR loading content: {ex.Message}");
        return 1;
    }

    var harness = new DungeonRunHarness(floorBuilder);

    if (transcriptMode)
    {
        Console.Error.WriteLine($"Generating run transcript (seed {seed}, {floors} floors)...");
        var text = harness.RunTranscript(floors, seed);
        Console.WriteLine(text);
        return 0;
    }

    if (llmTranscriptDir != null)
    {
        int transcriptRuns = runsOverride ?? (playerMode == "llm" ? 1 : 10);

        // Load voice + memo registries so verbatim text and memos reach the transcript.
        // Missing files are non-fatal — the corresponding fields stay empty/null (a greppable gap).
        var voiceRegistry = LoadVoiceRegistry();
        var memoRegistry  = LoadMemoRegistry();
        if (voiceRegistry == null)
            Console.Error.WriteLine("  WARN: voice line YAML not loaded — VoiceLineEvent.resolved_text will be null.");
        if (memoRegistry == null)
            Console.Error.WriteLine("  WARN: memo YAML not loaded — memos_delivered will be empty.");

        try
        {
            Directory.CreateDirectory(llmTranscriptDir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: cannot create directory '{llmTranscriptDir}': {ex.Message}");
            return 1;
        }

        if (playerMode == "llm")
        {
            // ── LLM Player transcript mode ──────────────────────────────────────────
            // Requires ANTHROPIC_API_KEY and --persona reader|system_explorer.
            // Runs one at a time (LLM API calls are expensive); fresh harness per run.

            var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.Error.WriteLine("Error: ANTHROPIC_API_KEY environment variable is not set.");
                return 1;
            }

            string llmPersonaName = personaName ?? "reader";
            if (llmPersonaName != "reader" && llmPersonaName != "system_explorer")
            {
                Console.Error.WriteLine($"Error: --persona must be 'reader' or 'system_explorer' for --player llm (got '{llmPersonaName}').");
                return 1;
            }
            var llmPersonaEnum = llmPersonaName == "reader" ? LlmPersona.Reader : LlmPersona.SystemExplorer;

            // Load config — use defaults if the file is missing.
            var configPath = Path.Combine("config", "llm_player", $"{llmPersonaName}.yaml");
            var config = LlmPlayerConfig.FromYaml(configPath);

            Console.Error.WriteLine($"Generating {transcriptRuns} LLM Player transcript(s) " +
                                    $"({floors} floors, seed {seed}, persona {llmPersonaName}, model {config.Model}) -> {llmTranscriptDir}");

            for (int i = 0; i < transcriptRuns; i++)
            {
                int runSeed = seed + i;

                // Build system prompt before constructing the brain — static method.
                string systemPrompt = LlmBotBrain.BuildSystemPrompt(llmPersonaEnum);

                // Fresh harness and client per run: entity IDs are deterministic only from a fresh
                // EntityFactory; replay fidelity depends on it (see existing bot-path comment).
                var runHarness = new DungeonRunHarness(
                    BuildDungeonFloorBuilder(EntitiesFile, LevelTemplatesFile, DepthBoonsFile));

                using var client = new AnthropicTurnClient(apiKey, config.Model, systemPrompt);
                using var brain  = new LlmBotBrain(client, llmPersonaEnum, config.FallbackPersona, config.MaxTurns);

                var (result, jsonl) = runHarness.RunWithLlmPlayer(
                    floors, runSeed, brain, llmPersonaName, config.Model,
                    voiceRegistry: voiceRegistry, memoRegistry: memoRegistry);

                var outPath = Path.Combine(llmTranscriptDir, $"run-{runSeed}.jsonl");
                File.WriteAllText(outPath, jsonl, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                Console.WriteLine($"  run {i + 1}/{transcriptRuns} seed={runSeed} outcome={result.Outcome} floors={result.FloorsCompleted} -> {outPath}");
            }
        }
        else
        {
            // ── Bot transcript mode (existing behavior) ──────────────────────────
            Console.Error.WriteLine($"Generating {transcriptRuns} enriched LLM-testing transcript(s) " +
                                    $"({floors} floors, seed {seed}) -> {llmTranscriptDir}");

            for (int i = 0; i < transcriptRuns; i++)
            {
                int runSeed = seed + i;
                // Build a FRESH harness per run. Entity IDs are deterministic only from a fresh
                // EntityFactory; the factory counter is process-shared mutable state that is not
                // reset per run, so reusing a harness drifts absolute IDs across runs (gameplay
                // is unaffected, but transcript IDs would no longer match a fresh replay).
                // A fresh factory per run guarantees replay fidelity: replaying (seed + actions)
                // in a fresh harness reproduces the exact IDs in this transcript.
                var runHarness = new DungeonRunHarness(
                    BuildDungeonFloorBuilder(EntitiesFile, LevelTemplatesFile, DepthBoonsFile));
                var (_, jsonl) = runHarness.RunWithTranscript(floors, runSeed, resolvedPersona, voiceRegistry, memoRegistry);
                var outPath = Path.Combine(llmTranscriptDir, $"run-{runSeed}.jsonl");
                // UTF-8 without BOM — JSONL is read as an ASCII-compatible bytestream.
                File.WriteAllText(outPath, jsonl, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                Console.Error.WriteLine($"  wrote {outPath}");
            }
        }

        return 0;
    }

    string personaLabel = resolvedPersona?.Name ?? "balanced";

    // Staged-start (step 8): resolve --gear profile + validate --start-floor before the soak.
    GearProfile? gearProfile = null;
    if (gearName != null)
    {
        if (!File.Exists(GearProfilesFile))
        {
            Console.Error.WriteLine($"ERROR: --gear requires {GearProfilesFile} (not found).");
            return 1;
        }
        var profiles = GearProfileLoader.FromFile(GearProfilesFile);
        if (!profiles.TryGetValue(gearName, out gearProfile))
        {
            Console.Error.WriteLine($"ERROR: unknown gear profile '{gearName}'. Available: {string.Join(", ", profiles.Keys)}.");
            return 1;
        }
    }
    if (startFloor < 1 || startFloor > floors)
    {
        Console.Error.WriteLine($"ERROR: --start-floor {startFloor} must be between 1 and --floors ({floors}).");
        return 1;
    }
    if (startFloor > 1)
        Console.Error.WriteLine($"Staged start: depths {startFloor}..{floors}, gear '{gearName ?? "(default — none specified, using floor-1 loadout)"}'");

    Console.Error.WriteLine($"Running {runs} soak runs ({floors} floors, seed {seed}, persona {personaLabel})...");

    // Optional JSONL streaming writer — opened before the soak so each run flushes immediately.
    // If a run crashes or the process is killed mid-session, partial output is still readable.
    StreamWriter? jsonlWriter = null;
    if (jsonlPath != null)
    {
        try
        {
            var dir = Path.GetDirectoryName(jsonlPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            // Use UTF-8 without BOM — JSONL files are read as ASCII-compatible bytestreams;
            // a BOM at the start of the first line will confuse standard JSON parsers.
            jsonlWriter = new StreamWriter(jsonlPath, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: Cannot open JSONL output file '{jsonlPath}': {ex.Message}");
            return 1;
        }
    }

    DungeonSoakSummary summary;
    try
    {
        summary = RunSoakWithStreaming(harness, floors, runs, seed, jsonlWriter, resolvedPersona,
            startDepth: startFloor, gearProfile: gearProfile);
    }
    finally
    {
        jsonlWriter?.Flush();
        jsonlWriter?.Dispose();
    }

    PrintDungeonTable(summary, runs, seed, floors);
    if (verbose)
        PrintBotVerbose(summary);

    // --report: generate full text report and print to stdout after the summary table.
    if (printReport)
    {
        // Load the role-aware target table so the report includes the Floor Health section.
        // Missing table → plain report (no health section) rather than a hard failure.
        TargetTable? targets = File.Exists(TargetTableFile) ? TargetTableLoader.FromFile(TargetTableFile) : null;
        var report = DungeonSoakReport.Generate(summary, targets);
        Console.WriteLine(report);
    }

    // Baseline delta: --update-baseline writes the snapshot; otherwise diff against it (step 7).
    // So a tuning change reads as a signed Δ vs the prior run instead of in a vacuum.
    {
        string soakBaselinePath = baselinePath ?? SoakBaselineFile;
        var current = SoakBaseline.FromSummary(summary);
        if (updateBaseline)
        {
            current.Save(soakBaselinePath);
            Console.WriteLine($"Soak baseline updated: {soakBaselinePath} ({current.Runs} runs, {current.Floors.Count} floors)");
        }
        else if (File.Exists(soakBaselinePath))
        {
            var baseline = SoakBaseline.Load(soakBaselinePath);
            Console.WriteLine(SoakBaselineDeltaReport.Format(current, baseline));
        }
        else
        {
            Console.WriteLine($"(No soak baseline at {soakBaselinePath} — run with --update-baseline to create one.)");
        }
    }

    return 0;
}

// ─── SCENARIO MODE: Build runner ──────────────────────────────────────────

ScenarioRunner runner;
try   { runner = ScenarioRunner.FromEntitiesFile(EntitiesFile); }
catch (Exception ex) { Console.Error.WriteLine($"ERROR loading entities: {ex.Message}"); return 1; }

// ─── Gather scenario paths ─────────────────────────────────────────────────

List<string> paths;
if (runAll)
{
    paths = Directory.GetFiles(LevelsDir, "scenario_depth*.yaml").OrderBy(f => f).ToList();
    if (paths.Count == 0) { Console.Error.WriteLine($"No scenario_depth*.yaml found in '{LevelsDir}'."); return 1; }
}
else
{
    var path = FindScenario(scenarioId!);
    if (path == null) { Console.Error.WriteLine($"Scenario not found: '{scenarioId}'"); return 1; }
    paths = [path];
}

// ─── Run scenarios ─────────────────────────────────────────────────────────

var results = new List<AggregatedMetrics>();
foreach (var path in paths)
{
    Console.Error.Write($"  Running {Path.GetFileNameWithoutExtension(path)}...");
    try
    {
        var m = runner.RunFromFile(path, seed, runsOverride, resolvedPersona);
        results.Add(m);
        Console.Error.WriteLine($" {m.TotalRuns} runs.");
    }
    catch (Exception ex) { Console.Error.WriteLine($" ERROR: {ex.Message}"); }
}

if (results.Count == 0) { Console.Error.WriteLine("No results."); return 1; }

// ─── Scenario JSON output (--all --jsonl writes one line per scenario) ─────

if (jsonlPath != null && !dungeonMode)
{
    try
    {
        var dir = Path.GetDirectoryName(jsonlPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var w = new StreamWriter(jsonlPath, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        foreach (var m in results)
        {
            var pm = ToPresssureMetrics(m);
            var eval = PressureModel.EvaluateProvisional(pm);
            var line = new
            {
                scenario_id      = m.ScenarioId,
                name             = m.Name,
                depth            = m.Depth,
                runs             = m.TotalRuns,
                seed             = m.Seed,
                death_rate       = m.DeathRate,
                // Rounds-based (band-gated). On-disk keys kept as h_pm/h_mp for round-trip stability.
                h_pm             = pm.RoundsToKill,
                h_mp             = pm.RoundsToDie,
                // Hits-based (per landed swing) — surfaced alongside for FIND-005 disambiguation.
                ttk_hits         = m.TtkHits,
                ttd_hits         = m.TtdHits,
                dpr_p            = pm.DPR_P,
                dpr_m            = pm.DPR_M,
                avg_turns        = m.AvgTurns,
                evaluation       = eval,
            };
            w.WriteLine(JsonSerializer.Serialize(line));
            w.Flush();
        }
    }
    catch (Exception ex) { Console.Error.WriteLine($"ERROR writing JSONL: {ex.Message}"); }
}

// ─── Scenario output ───────────────────────────────────────────────────────

if (jsonOutput)
{
    var evals = results.Select(m =>
    {
        var pm   = ToPresssureMetrics(m);
        var eval = PressureModel.EvaluateProvisional(pm);
        return new
        {
            scenario_id       = m.ScenarioId,
            name              = m.Name,
            depth             = m.Depth,
            runs              = m.TotalRuns,
            seed              = m.Seed,
            death_rate        = m.DeathRate,
            // Rounds-based (band-gated). On-disk keys kept as h_pm/h_mp for round-trip stability.
            h_pm              = pm.RoundsToKill,
            h_mp              = pm.RoundsToDie,
            // Hits-based (per landed swing) — surfaced alongside for FIND-005 disambiguation.
            ttk_hits          = m.TtkHits,
            ttd_hits          = m.TtdHits,
            dpr_p             = pm.DPR_P,
            dpr_m             = pm.DPR_M,
            avg_turns         = m.AvgTurns,
            player_hit_rate   = m.PlayerHitRate,
            monster_hit_rate  = m.MonsterHitRate,
            evaluation        = eval,
        };
    }).ToList();
    Console.WriteLine(JsonSerializer.Serialize(evals, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (results.Count == 1) PrintSingle(results[0]);
else                     PrintTable(results);

// --report: append role-aware engagement-health section for each scenario.
if (printReport)
{
    TargetTable? targets = File.Exists(TargetTableFile) ? TargetTableLoader.FromFile(TargetTableFile) : null;
    if (targets != null)
        foreach (var m in results)
            Console.WriteLine(ScenarioEngagementReport.Format(m, targets));
}

return 0;

// ─── Bot verbose output ────────────────────────────────────────────────────

/// <summary>
/// Print aggregate bot action distribution and heal behavior across all soak runs.
/// Only printed when --verbose is passed with --dungeon.
/// Skipped silently if no run has BotSummary data (e.g., legacy runs without telemetry).
/// </summary>
void PrintBotVerbose(DungeonSoakSummary soakSummary)
{
    // Collect all non-null BotSummary objects
    var summaries = soakSummary.Runs
        .Where(r => r.BotSummary != null)
        .Select(r => r.BotSummary!)
        .ToList();

    if (summaries.Count == 0)
        return;

    // Aggregate action counts across all runs
    var totalActionCounts = new Dictionary<string, long>();
    var totalReasonCounts = new Dictionary<string, long>();
    long totalDecisions   = 0;
    double healHpSum      = 0.0;
    int healRunCount      = 0;
    int deathsWithUnused  = 0;
    int deathCount        = 0;

    foreach (var s in summaries)
    {
        totalDecisions += s.TotalDecisions;
        foreach (var (key, count) in s.ActionCounts)
        {
            totalActionCounts.TryGetValue(key, out long existing);
            totalActionCounts[key] = existing + count;
        }
        foreach (var (key, count) in s.ReasonCounts)
        {
            totalReasonCounts.TryGetValue(key, out long existing);
            totalReasonCounts[key] = existing + count;
        }
        if (s.HealDecisions > 0)
        {
            healHpSum   += s.AvgHpWhenHealing * s.HealDecisions;
            healRunCount += s.HealDecisions;
        }
        deathsWithUnused += s.DeathsWithUnusedPotions;
    }

    // Count runs that ended in death (for context on DeathsWithUnusedPotions)
    deathCount = soakSummary.Runs.Count(r => r.Outcome == UnderWarden.Logic.Balance.OutcomeClassifier.Died);

    Console.WriteLine("Bot Action Distribution (across all runs):");
    foreach (var (action, count) in totalActionCounts.OrderByDescending(kv => kv.Value))
    {
        double pct = totalDecisions > 0 ? (double)count / totalDecisions * 100.0 : 0.0;
        Console.WriteLine($"  {action,-20} {pct,5:F1}%  ({count:N0} decisions)");
    }
    Console.WriteLine();

    double avgHpHealing = healRunCount > 0 ? healHpSum / healRunCount * 100.0 : 0.0;
    string deathsNote = deathCount > 0
        ? $"{deathsWithUnused} / {deathCount} deaths ({(double)deathsWithUnused / deathCount * 100.0:F1}%)"
        : "0 deaths";

    Console.WriteLine("Heal Behavior:");
    Console.WriteLine($"  Avg HP% when healing:       {avgHpHealing:F1}%");
    Console.WriteLine($"  Deaths with unused potions: {deathsNote}");
    Console.WriteLine();
}

// ─── Dungeon soak helpers ─────────────────────────────────────────────────

/// <summary>
/// Builds a fully configured DungeonFloorBuilder with all factories including
/// SpellItemFactory and boon table. The test setup in DungeonRunTests is intentionally
/// lightweight (no SpellItemFactory). The harness needs the full factory chain so
/// wands/scrolls spawn and depth boons apply, reflecting actual gameplay.
/// </summary>
DungeonFloorBuilder BuildDungeonFloorBuilder(string entitiesPath, string templatesPath, string boonsPath)
{
    var loader  = new ContentLoader();
    var content = loader.LoadAllFromFile(entitiesPath);

    var entityFactory    = new EntityFactory();
    var itemFactory      = new ItemFactory(content.Items, entityFactory);
    var spellItemFactory = new SpellItemFactory(content.SpellItems, entityFactory);
    var monsterFactory   = new MonsterFactory(content.Monsters, entityFactory, itemFactory);
    var consumableFactory = new ConsumableFactory(content.Consumables, entityFactory);

    var templates = LevelTemplateRegistry.FromFile(templatesPath);

    System.Collections.Generic.IReadOnlyDictionary<int, BoonDefinition>? boonTable = null;
    if (File.Exists(boonsPath))
        boonTable = loader.LoadBoonsFromFile(boonsPath);

    var floorItemPool = content.FloorItemPool;

    UnderWarden.Logic.Content.LootTagRegistry? lootTagRegistry = null;
    if (File.Exists(LootTagsFile))
        lootTagRegistry = UnderWarden.Logic.Content.LootTagRegistry.FromFile(LootTagsFile);

    UnderWarden.Logic.Content.LootPolicyConfig? lootPolicy = null;
    if (File.Exists(LootPolicyFile))
        lootPolicy = UnderWarden.Logic.Content.LootPolicyConfig.FromFile(LootPolicyFile);

    return new DungeonFloorBuilder(
        templates,
        monsterFactory,
        itemFactory,
        consumableFactory,
        floorItemPool: floorItemPool,
        spellItemFactory: spellItemFactory,
        boonTable: boonTable,
        lootTagRegistry: lootTagRegistry,
        lootPolicy: lootPolicy);
}

/// <summary>
/// Load and merge the voice line pools (Hollowmark + Quipping Shade + catalog + possession),
/// matching Main.cs load order so transcript-resolved lines match the live game's pools.
/// Returns null if the primary file is absent.
/// </summary>
VoiceLineRegistry? LoadVoiceRegistry()
{
    const string dir = "config/voice_lines";
    var hollowmark = Path.Combine(dir, "hollowmark.yaml");
    if (!File.Exists(hollowmark)) return null;

    var registry = VoiceLineRegistry.LoadFromYaml(File.ReadAllText(hollowmark));
    // Matches Main.cs: weighing_audit.yaml is a DIFFERENT schema (WeighingAuditRegistry), not merged here.
    foreach (var name in new[] { "quipping_shade.yaml", "possession.yaml", "catalog_past_selves.yaml" })
    {
        var p = Path.Combine(dir, name);
        if (File.Exists(p))
            registry.Merge(VoiceLineRegistry.LoadFromYaml(File.ReadAllText(p)));
    }
    return registry;
}

/// <summary>Load the Under-Warden memo registry. Returns null if the memo file is absent.</summary>
MemoRegistry? LoadMemoRegistry()
{
    const string memos = "config/under_warden/memos.yaml";
    const string causes = "config/under_warden/cause_display_names.yaml";
    if (!File.Exists(memos)) return null;
    var causesYaml = File.Exists(causes) ? File.ReadAllText(causes) : "";
    return MemoRegistry.LoadFromYaml(File.ReadAllText(memos), causesYaml);
}

/// <summary>
/// Run the soak, streaming JSONL output per-run if a writer is provided.
/// Stream-writes so partial output survives Ctrl-C or crashes.
/// </summary>
DungeonSoakSummary RunSoakWithStreaming(
    DungeonRunHarness harness, int floorCount, int runCount, int baseSeed, StreamWriter? writer, BotPersonaConfig? persona = null,
    int startDepth = 1, GearProfile? gearProfile = null)
{
    var soakOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        // Do NOT apply snake_case to dictionary keys — they are data values (e.g. "orc_brute",
        // "Attack") that downstream analysis keys on. SnakeCaseLower on keys would mangle them.
        DictionaryKeyPolicy         = null,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    var allResults = new List<DungeonSoakRunResult>(runCount);

    for (int i = 0; i < runCount; i++)
    {
        int runSeed = baseSeed + i;
        DungeonSoakRunResult result;

        try
        {
            // RunSingle is called via RunSoak — but we need per-run access for streaming.
            // Use a 1-run mini-soak to get the structured result.
            var miniSummary = harness.RunSoak(floors: floorCount, runs: 1, baseSeed: runSeed, persona: persona,
                startDepth: startDepth, gearProfile: gearProfile);
            result = miniSummary.Runs[0];
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [soak] run {i} (seed {runSeed}) threw: {ex.Message}");
            result = new DungeonSoakRunResult
            {
                Seed          = runSeed,
                Outcome       = OutcomeClassifier.Exception,
                FailureType   = OutcomeClassifier.FailureException,
                FailureDetail = ex.Message,
                FloorsCompleted = 0,
                PerFloor      = Array.Empty<FloorRunMetrics>(),
            };
        }

        allResults.Add(result);

        if (writer != null)
        {
            writer.WriteLine(JsonSerializer.Serialize(result, soakOptions));
            writer.Flush(); // stream-write: don't lose data on crash/Ctrl-C
        }

        // Progress indicator every 10 runs
        if ((i + 1) % 10 == 0 || i + 1 == runCount)
            Console.Error.WriteLine($"  {i + 1}/{runCount} runs complete...");
    }

    return DungeonSoakSummary.ComputeFrom(allResults, configuredFloors: floorCount);
}

void PrintDungeonTable(DungeonSoakSummary summary, int runs, int baseSeed, int floorCount)
{
    Console.WriteLine();
    Console.WriteLine($"=== YARL Dungeon Soak ({runs} runs, seed {baseSeed}, {floorCount} floors) ===");
    Console.WriteLine();
    Console.WriteLine($"  Survival rate:       {summary.SurvivalRate:P1}");
    Console.WriteLine($"  Avg floors completed: {summary.AvgFloorsCompleted:F2}");
    Console.WriteLine($"  Avg total turns:      {summary.AvgTotalTurns:F1}");
    Console.WriteLine();

    // Per-depth table
    Console.WriteLine($"  {"Depth",5}  {"Death%",7}  {"Avg Turns",9}  {"Avg Kills",9}  {"Survival%",9}");
    Console.WriteLine($"  {"-----",5}  {"------",7}  {"---------",9}  {"---------",9}  {"---------",9}");

    int maxDepth = floorCount;
    for (int d = 1; d <= maxDepth; d++)
    {
        // Runs that attempted this depth
        var floorRuns = summary.Runs
            .Select(r => r.PerFloor.FirstOrDefault(f => f.Depth == d))
            .Where(f => f != null)
            .Select(f => f!)
            .ToList();

        if (floorRuns.Count == 0) continue;

        double deathPct    = summary.DeathRateByFloor.TryGetValue(d, out double dr) ? dr * 100.0 : 0.0;
        double avgTurns    = floorRuns.Average(f => f.TurnsTaken);
        double avgKills    = floorRuns.Average(f => f.MonstersKilled);
        double survivalPct = summary.SurvivalCurve.Count >= d ? summary.SurvivalCurve[d - 1] * 100.0 : 0.0;

        Console.WriteLine($"  {d,5}  {deathPct,6:F1}%  {avgTurns,9:F1}  {avgKills,9:F1}  {survivalPct,8:F1}%");
    }

    // Per-depth loot telemetry — only printed if any floor has LootCategoryCounts data
    bool hasLootData = summary.Runs
        .Any(r => r.PerFloor.Any(f => f.LootCategoryCounts != null));

    if (hasLootData)
    {
        Console.WriteLine();
        Console.WriteLine("  Loot Category Rates (avg items/floor across all runs that reached depth):");
        Console.WriteLine($"  {"Depth",5}  {"Total",6}  {"healing",8}  {"panic",6}  {"offense",8}  {"utility",8}  {"wpn_up",7}  {"arm_up",7}  {"rare",5}  {"pity",5}");
        Console.WriteLine($"  {"-----",5}  {"-----",6}  {"-------",8}  {"-----",6}  {"-------",8}  {"-------",8}  {"------",7}  {"------",7}  {"----",5}  {"----",5}");

        for (int d = 1; d <= maxDepth; d++)
        {
            var floorRuns = summary.Runs
                .Select(r => r.PerFloor.FirstOrDefault(f => f.Depth == d))
                .Where(f => f?.LootCategoryCounts != null)
                .Select(f => f!)
                .ToList();

            if (floorRuns.Count == 0) continue;

            double Avg(string cat) => floorRuns.Average(f =>
                f.LootCategoryCounts!.TryGetValue(cat, out int v) ? v : 0);

            double total   = floorRuns.Average(f => f.LootCategoryCounts!.Values.Sum());
            double healing = Avg("healing");
            double panic   = Avg("panic");
            double offense = Avg("offensive");
            double utility = Avg("utility");
            double wpnUp   = Avg("upgrade_weapon");
            double armUp   = Avg("upgrade_armor");
            double rare    = Avg("rare");
            double pityFires = floorRuns.Average(f => f.LootHardPityFires);

            Console.WriteLine($"  {d,5}  {total,5:F1}  {healing,8:F2}  {panic,6:F2}  {offense,8:F2}  {utility,8:F2}  {wpnUp,7:F2}  {armUp,7:F2}  {rare,5:F2}  {pityFires,5:F2}");
        }
    }

    Console.WriteLine();

    // Failure types
    if (summary.FailureTypeCounts.Count > 0)
    {
        Console.WriteLine("  Failure Types:");
        foreach (var (type, count) in summary.FailureTypeCounts.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"    {type}: {count}");
        Console.WriteLine();
    }

    // Top killers
    if (summary.KillerCounts.Count > 0)
    {
        Console.WriteLine("  Top Killers:");
        foreach (var (killer, count) in summary.KillerCounts.OrderByDescending(kv => kv.Value).Take(10))
            Console.WriteLine($"    {killer}: {count}");
        Console.WriteLine();
    }
}

// ─── Scenario helpers ─────────────────────────────────────────────────────

PressureMetrics ToPresssureMetrics(AggregatedMetrics m) =>
    PressureModel.Compute(m, m.Depth, m.AvgMonsterMaxHp, (int)Math.Round(m.AvgPlayerMaxHp));

string? FindScenario(string id)
{
    var byFilename = Path.Combine(LevelsDir, $"scenario_{id}.yaml");
    if (File.Exists(byFilename)) return byFilename;

    var asGiven = Path.Combine(LevelsDir, $"{id}.yaml");
    if (File.Exists(asGiven)) return asGiven;

    foreach (var f in Directory.GetFiles(LevelsDir, "*.yaml"))
        foreach (var line in File.ReadLines(f))
        {
            var t = line.TrimStart();
            if (t.StartsWith("scenario_id:") && t.Contains(id)) return f;
            if (t.Length > 0 && !t.StartsWith('#') && !t.StartsWith("scenario_id:") && !t.StartsWith("name:"))
                break;
        }

    return null;
}

void PrintSingle(AggregatedMetrics m)
{
    var pm   = ToPresssureMetrics(m);
    var eval = PressureModel.EvaluateProvisional(pm);
    var sep  = new string('─', 60);

    Console.WriteLine();
    Console.WriteLine($"  {m.ScenarioId}  ({m.TotalRuns} runs, seed {m.Seed})  Depth {m.Depth}");
    if (!string.IsNullOrEmpty(m.Name)) Console.WriteLine($"  {m.Name}");
    Console.WriteLine($"  {sep}");

    Console.WriteLine(MetricLine("Death Rate",
        $"{m.DeathRate:P1}", eval.DeathRate_Target, eval.DeathRate_Status));
    // Rounds-based family (HP ÷ damage-per-turn) — the bands gate on these.
    Console.WriteLine(MetricLine("RoundsToKill",
        $"{pm.RoundsToKill:F1}", eval.RoundsToKill_Target, eval.RoundsToKill_Status));
    Console.WriteLine(MetricLine("RoundsToDie",
        $"{pm.RoundsToDie:F1}",  eval.RoundsToDie_Target, eval.RoundsToDie_Status));
    Console.WriteLine($"  {"(above: rounds — turns, incl. misses/approach)"}");
    // Hits-based family (HP ÷ damage-per-landed-hit) — surfaced for per-swing analysis (FIND-005).
    Console.WriteLine($"  {"TtkHits",-12} {m.TtkHits,8:F1}    {"TtdHits",-12} {m.TtdHits:F1}    (hits — per landed swing)");
    Console.WriteLine($"  {"DPR_P",-12} {pm.DPR_P,8:F2}    {"DPR_M",-12} {pm.DPR_M:F2}");
    Console.WriteLine($"  {"Avg Turns",-12} {m.AvgTurns,8:F1}    Player Hit%: {m.PlayerHitRate:P0}    Monster Hit%: {m.MonsterHitRate:P0}");
    Console.WriteLine($"  {sep}");

    var findings = PressureModel.Diagnose(eval);
    foreach (var f in findings) Console.WriteLine($"  {f}");
    Console.WriteLine();
}

void PrintTable(List<AggregatedMetrics> ms)
{
    Console.WriteLine();
    Console.WriteLine($"=== YARL Balance Report ({seed} seed) ===");
    Console.WriteLine($"  RtK/RtD = rounds (turns, band-gated);  Ttk/Ttd = hits (per landed swing, ungated)  [FIND-005]");
    Console.WriteLine();
    Console.WriteLine($"  {"Scenario",-28}  {"D",2}  {"Death%",7}  {"[Band]",9}  {"RtK",5}  {"[Band]",7}  {"RtD",5}  {"[Band]",7}  {"Ttk",5}  {"Ttd",5}  Status");
    Console.WriteLine($"  {new string('-', 28)}  {"--"}  {"-------"}  {"---------"}  {"-----"}  {"-------"}  {"-----"}  {"-------"}  {"-----"}  {"-----"}  ------");

    int passes = 0, fails = 0, probes = 0;
    var failedScenarios = new List<AggregatedMetrics>();

    foreach (var m in ms)
    {
        var pm   = ToPresssureMetrics(m);
        var eval = PressureModel.EvaluateProvisional(pm);

        string dr   = $"{m.DeathRate:P1}";
        string drB  = $"[{eval.DeathRate_Target.Min:P0}-{eval.DeathRate_Target.Max:P0}]";
        string rtk  = $"{pm.RoundsToKill:F1}";
        string rtkB = $"[{eval.RoundsToKill_Target.Min:F1}-{eval.RoundsToKill_Target.Max:F1}]";
        string rtd  = $"{pm.RoundsToDie:F1}";
        string rtdB = $"[{eval.RoundsToDie_Target.Min:F1}-{eval.RoundsToDie_Target.Max:F1}]";
        string ttk  = $"{m.TtkHits:F1}";
        string ttd  = $"{m.TtdHits:F1}";

        string status;
        if (m.IsProbe)
        {
            probes++;
            bool rtkOk = eval.RoundsToKill_Status == "OK";
            bool rtdOk = eval.RoundsToDie_Status == "OK";
            status = $"PROBE{(!rtkOk ? "(RtK)" : !rtdOk ? "(RtD)" : "")}";
        }
        else
        {
            bool pass = eval.AllInBand;
            if (pass) passes++; else { fails++; failedScenarios.Add(m); }
            status = pass ? "PASS" : $"FAIL({(eval.RoundsToKill_Status != "OK" ? "RtK " : "")}{(eval.RoundsToDie_Status != "OK" ? "RtD " : "")}{(eval.DeathRate_Status != "OK" ? "Death%" : "")})";
        }

        Console.WriteLine($"  {m.ScenarioId,-28}  {m.Depth,2}  {dr,7}  {drB,9}  {rtk,5}  {rtkB,7}  {rtd,5}  {rtdB,7}  {ttk,5}  {ttd,5}  {status}");
    }

    Console.WriteLine();
    string probeNote = probes > 0 ? $" / {probes} PROBE" : "";
    Console.WriteLine($"  Results: {passes} PASS / {fails} FAIL{probeNote}  ({ms.Count} total)");
    Console.WriteLine();

    if (failedScenarios.Count > 0)
    {
        Console.WriteLine("=== Diagnosis ===");
        Console.WriteLine();
        foreach (var m in failedScenarios)
        {
            Console.WriteLine($"  {m.ScenarioId} (depth {m.Depth}):");
            var findings = PressureModel.Diagnose(PressureModel.EvaluateProvisional(ToPresssureMetrics(m)));
            foreach (var f in findings) Console.WriteLine($"    {f}");
        }
        Console.WriteLine();
    }
}

string MetricLine(string label, string value, TargetBand band, string status)
{
    string tag = status switch { "OK" => "[PASS]", "HIGH" => "[HIGH]", "LOW" => "[LOW ]", _ => "[----]" };
    string bandStr = $"[{band.Min:G4}-{band.Max:G4}]";
    return $"  {label,-12} {value,8}    target: {bandStr,-14}  {tag}";
}

void PrintHelp()
{
    Console.WriteLine("YARL Balance Harness");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project tools/Harness -- --scenario <id> [options]");
    Console.WriteLine("  dotnet run --project tools/Harness -- --all [options]");
    Console.WriteLine("  dotnet run --project tools/Harness -- --dungeon [options]");
    Console.WriteLine();
    Console.WriteLine("Scenario mode options:");
    Console.WriteLine("  --scenario <id>   Run a specific scenario by scenario_id");
    Console.WriteLine("  --all             Run all scenario_depth*.yaml files");
    Console.WriteLine("  --runs <n>        Override run count (default: from YAML, usually 50)");
    Console.WriteLine("  --seed <n>        Base seed (default: 1337)");
    Console.WriteLine("  --json            Output results as JSON");
    Console.WriteLine("  --jsonl <path>    Write per-scenario JSON lines to file");
    Console.WriteLine();
    Console.WriteLine("Dungeon mode options:");
    Console.WriteLine("  --dungeon         Run dungeon soak (multi-floor bot campaign)");
    Console.WriteLine("  --floors <n>      Number of dungeon floors per run (default: 6)");
    Console.WriteLine("  --runs <n>        Number of soak runs (default: 100)");
    Console.WriteLine("  --seed <n>        Base seed — run i uses seed+i (default: 1337)");
    Console.WriteLine("  --player <mode>   Player type: bot (default) or llm");
    Console.WriteLine("  --persona <name>  Bot persona (balanced|cautious|aggressive|greedy|speedrunner)");
    Console.WriteLine("                    For --player llm: reader (default) or system_explorer");
    Console.WriteLine("  --jsonl <path>    Stream per-run results as JSONL to file");
    Console.WriteLine("  --verbose         Print bot action distribution and heal behavior after table");
    Console.WriteLine("  --report          Generate and print full analysis report after the soak");
    Console.WriteLine("  --transcript       Print a single-run narrative transcript (use with --dungeon)");
    Console.WriteLine();
    Console.WriteLine("LLM Player mode (--player llm):");
    Console.WriteLine("  Requires: --dungeon --llm-transcript <dir> --player llm");
    Console.WriteLine("  Requires: ANTHROPIC_API_KEY environment variable");
    Console.WriteLine("  Example: dotnet run --project tools/Harness -- --dungeon --llm-transcript reports/llm \\");
    Console.WriteLine("               --player llm --persona reader --floors 10 --runs 1 --seed 1337");
    Console.WriteLine();
    Console.WriteLine("Bot report mode:");
    Console.WriteLine("  --bot-report      Run 5×N persona survivability matrix");
    Console.WriteLine("  --matrix <fast|full>  Scenario matrix (default: fast = 6 scenarios, full = 15)");
    Console.WriteLine("  --runs <n>        Runs per scenario per persona (default: 20 fast, 50 full)");
    Console.WriteLine("  --out <path>      Write markdown report to file (default: stdout)");
    Console.WriteLine();
    Console.WriteLine("Offline report options:");
    Console.WriteLine("  --report --jsonl-in <path>  Read saved JSONL and print analysis report");
    Console.WriteLine("                              (no soak run needed)");
    Console.WriteLine();
    Console.WriteLine("  --help, -h        Show this help");
    Console.WriteLine();
    Console.WriteLine("Must be run from the project root (the directory containing config/).");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run --project tools/Harness -- --scenario depth1_tuned");
    Console.WriteLine("  dotnet run --project tools/Harness -- --all --runs 100");
    Console.WriteLine("  dotnet run --project tools/Harness -- --all --json > reports/latest.json");
    Console.WriteLine("  dotnet run --project tools/Harness -- --dungeon --floors 3 --runs 20 --seed 1337");
    Console.WriteLine("  dotnet run --project tools/Harness -- --dungeon --floors 6 --runs 100 --jsonl reports/soak.jsonl");
    Console.WriteLine("  dotnet run --project tools/Harness -- --dungeon --floors 3 --runs 10 --report");
    Console.WriteLine("  dotnet run --project tools/Harness -- --dungeon --floors 3 --runs 10 --jsonl reports/soak.jsonl --report");
    Console.WriteLine("  dotnet run --project tools/Harness -- --report --jsonl-in reports/soak.jsonl");
    Console.WriteLine("  dotnet run --project tools/Harness -- --report --jsonl-in reports/soak.jsonl > reports/report.txt");
}
