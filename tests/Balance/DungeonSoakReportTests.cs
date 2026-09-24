using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using UnderWarden.Logic.Balance;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Tests for Phase 3 infrastructure: DungeonSoakReport generation and SoakJsonlReader.
///
/// All tests use synthetic data and do not depend on the game engine or Godot.
/// JSONL round-trip tests write to temp files, cleaned up in TearDown.
/// </summary>
[TestFixture]
[Description("DungeonSoakReport generation and SoakJsonlReader round-trip")]
public class DungeonSoakReportTests
{
    private readonly List<string> _tempFiles = new();

    [TearDown]
    public void TearDown()
    {
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); } catch { /* best-effort cleanup */ }
        }
        _tempFiles.Clear();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonlOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DictionaryKeyPolicy         = null,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Create a minimal survived run for use in synthetic summaries.</summary>
    private static DungeonSoakRunResult MakeSurvivedRun(int seed, int floors = 3)
    {
        var perFloor = Enumerable.Range(1, floors)
            .Select(d => new FloorRunMetrics
            {
                Depth          = d,
                TurnsTaken     = 40 + d * 5,
                MonstersKilled = 3,
                PlayerHpAtEnd  = 40,
                PlayerMaxHp    = 55,
                Descended      = true,
                PlayerDied     = false,
            })
            .ToList();

        return new DungeonSoakRunResult
        {
            Seed                = seed,
            Outcome             = OutcomeClassifier.Survived,
            FailureType         = OutcomeClassifier.FailureNone,
            FailureDetail       = "",
            DeepestFloorReached = floors,
            FloorsCompleted     = floors,
            TotalTurns          = perFloor.Sum(f => f.TurnsTaken),
            TotalKills          = perFloor.Sum(f => f.MonstersKilled),
            FinalHp             = 40,
            FinalMaxHp          = 55,
            FinalHpFraction     = 40.0 / 55.0,
            PotionsUsed         = 1,
            PotionsRemaining    = 0,
            BoonsAcquired       = 0,
            DurationSeconds     = 1.0,
            PerFloor            = perFloor,
        };
    }

    /// <summary>Create a minimal death run for use in synthetic summaries.</summary>
    private static DungeonSoakRunResult MakeDeathRun(int seed, int deathFloor = 2,
        string killer = "orc_brute", int potionsRemaining = 0)
    {
        var perFloor = new List<FloorRunMetrics>();
        for (int d = 1; d <= deathFloor; d++)
        {
            bool isDeathFloor = d == deathFloor;
            perFloor.Add(new FloorRunMetrics
            {
                Depth          = d,
                TurnsTaken     = 45,
                MonstersKilled = isDeathFloor ? 1 : 3,
                PlayerHpAtEnd  = isDeathFloor ? 0 : 35,
                PlayerMaxHp    = 55,
                Descended      = !isDeathFloor,
                PlayerDied     = isDeathFloor,
            });
        }

        return new DungeonSoakRunResult
        {
            Seed                = seed,
            Outcome             = OutcomeClassifier.Died,
            FailureType         = OutcomeClassifier.FailureDeath,
            FailureDetail       = killer,
            DeepestFloorReached = deathFloor,
            FloorsCompleted     = deathFloor - 1,
            TotalTurns          = perFloor.Sum(f => f.TurnsTaken),
            TotalKills          = perFloor.Sum(f => f.MonstersKilled),
            FinalHp             = 0,
            FinalMaxHp          = 55,
            FinalHpFraction     = 0.0,
            PotionsUsed         = 0,
            PotionsRemaining    = potionsRemaining,
            BoonsAcquired       = 0,
            DurationSeconds     = 0.8,
            PerFloor            = perFloor,
        };
    }

    /// <summary>Create a max_turns run (hit turn limit without dying).</summary>
    private static DungeonSoakRunResult MakeMaxTurnsRun(int seed, int stuckFloor = 2)
    {
        var perFloor = new List<FloorRunMetrics>();
        for (int d = 1; d <= stuckFloor; d++)
        {
            bool isStuckFloor = d == stuckFloor;
            perFloor.Add(new FloorRunMetrics
            {
                Depth          = d,
                TurnsTaken     = isStuckFloor ? 500 : 45,
                MonstersKilled = 2,
                PlayerHpAtEnd  = 30,
                PlayerMaxHp    = 55,
                Descended      = !isStuckFloor,
                HitMaxTurns    = isStuckFloor,
            });
        }

        return new DungeonSoakRunResult
        {
            Seed                = seed,
            Outcome             = OutcomeClassifier.MaxTurns,
            FailureType         = OutcomeClassifier.FailureMaxTurns,
            FailureDetail       = $"Floor {stuckFloor}: hit turn limit",
            DeepestFloorReached = stuckFloor,
            FloorsCompleted     = stuckFloor - 1,
            TotalTurns          = perFloor.Sum(f => f.TurnsTaken),
            TotalKills          = perFloor.Sum(f => f.MonstersKilled),
            FinalHp             = 30,
            FinalMaxHp          = 55,
            FinalHpFraction     = 30.0 / 55.0,
            PotionsUsed         = 0,
            PotionsRemaining    = 0,
            BoonsAcquired       = 0,
            DurationSeconds     = 2.5,
            PerFloor            = perFloor,
        };
    }

    private string WriteTempJsonl(IEnumerable<DungeonSoakRunResult> runs)
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);

        using var writer = new StreamWriter(path, append: false,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        foreach (var run in runs)
        {
            writer.WriteLine(JsonSerializer.Serialize(run, JsonlOptions));
        }
        return path;
    }

    // ── Test 1: Report from synthetic data ────────────────────────────────────

    [Test]
    [Description("Report from synthetic 5-run data: 2 survived, 2 died, 1 max_turns — check survival rate, section presence, max_turns in death classification")]
    public void Report_SyntheticData_CorrectSurvivalRateAndSections()
    {
        var runs = new List<DungeonSoakRunResult>
        {
            MakeSurvivedRun(seed: 1337),
            MakeSurvivedRun(seed: 1338),
            MakeDeathRun(seed: 1339, killer: "orc_brute"),
            MakeDeathRun(seed: 1340, killer: "zombie"),
            MakeMaxTurnsRun(seed: 1341),
        };

        var summary = DungeonSoakSummary.ComputeFrom(runs);
        string report = DungeonSoakReport.Generate(summary);

        TestContext.WriteLine(report);

        // 2 survived out of 5 = 40%
        Assert.That(report, Does.Contain("Survival Rate: 40.0%"),
            "Survival rate should be 40.0% (2/5 survived)");

        // max_turns appears in death classification
        Assert.That(report, Does.Contain("max_turns"),
            "max_turns failure type should appear in Death Classification section");

        // All 6 expected section headers are present
        Assert.That(report, Does.Contain("=== YARL Dungeon Soak Report ==="),
            "Section 1: overview header");
        Assert.That(report, Does.Contain("Survival Curve:"),
            "Section 2: survival curve");
        Assert.That(report, Does.Contain("Death Classification"),
            "Section 3: death classification");
        Assert.That(report, Does.Contain("Floor Efficiency:"),
            "Section 4: floor efficiency");
        Assert.That(report, Does.Contain("Bot Efficiency:"),
            "Section 5: bot efficiency");
        Assert.That(report, Does.Contain("Anomalies:"),
            "Section 6: anomalies");
    }

    // ── Test 2: Report with zero deaths ──────────────────────────────────────

    [Test]
    [Description("Report when all runs survived: 100% survival, empty death section, no anomalies")]
    public void Report_ZeroDeaths_ShowsFullSurvivalAndNoAnomalies()
    {
        var runs = new List<DungeonSoakRunResult>
        {
            MakeSurvivedRun(seed: 1337),
            MakeSurvivedRun(seed: 1338),
            MakeSurvivedRun(seed: 1339),
            MakeSurvivedRun(seed: 1340),
            MakeSurvivedRun(seed: 1341),
        };

        var summary = DungeonSoakSummary.ComputeFrom(runs);
        string report = DungeonSoakReport.Generate(summary);

        TestContext.WriteLine(report);

        Assert.That(report, Does.Contain("Survival Rate: 100.0%"),
            "All runs survived — survival rate must be 100%");

        // Death classification section should note zero deaths
        Assert.That(report, Does.Contain("No deaths recorded").Or.Contain("0 deaths"),
            "Death section should indicate no deaths");

        // Anomalies section should say none detected
        Assert.That(report, Does.Contain("None detected"),
            "No anomalies expected when all runs survived cleanly");
    }

    // ── Test 3: Report with no telemetry ─────────────────────────────────────

    [Test]
    [Description("Report when all BotSummary are null: bot section skips with explanatory note")]
    public void Report_NoTelemetry_BotSectionOmittedWithNote()
    {
        var runs = new List<DungeonSoakRunResult>
        {
            MakeSurvivedRun(seed: 1337),
            MakeDeathRun(seed: 1338),
        };
        // Ensure BotSummary is null on all runs (default from helpers)
        Assert.That(runs.All(r => r.BotSummary == null), Is.True,
            "Precondition: helpers must not set BotSummary");

        var summary = DungeonSoakSummary.ComputeFrom(runs);
        string report = DungeonSoakReport.Generate(summary);

        TestContext.WriteLine(report);

        // Bot section header should still be present
        Assert.That(report, Does.Contain("Bot Efficiency:"),
            "Bot section header should appear even when telemetry is absent");

        // But telemetry-unavailable note should be in the section
        Assert.That(report, Does.Contain("Bot telemetry not available"),
            "Bot section should explain why data is absent");
    }

    // ── Test 4: Survival curve is monotonically non-increasing ───────────────

    [Test]
    [Description("Survival curve percentages in report text are non-increasing from floor 1 to last floor")]
    public void Report_SurvivalCurve_IsMonotonicInText()
    {
        // Create runs that die on progressively later floors to generate a realistic curve.
        var runs = new List<DungeonSoakRunResult>
        {
            MakeSurvivedRun(seed: 1337, floors: 4),
            MakeSurvivedRun(seed: 1338, floors: 4),
            MakeDeathRun(seed: 1339, deathFloor: 2),
            MakeDeathRun(seed: 1340, deathFloor: 3),
            MakeDeathRun(seed: 1341, deathFloor: 4),
        };

        var summary = DungeonSoakSummary.ComputeFrom(runs);
        string report = DungeonSoakReport.Generate(summary);

        TestContext.WriteLine(report);

        // Parse survival curve percentages from the report text.
        // Lines look like: "  Floor  1:  100.0%  ########"
        var matches = Regex.Matches(report, @"Floor\s+\d+:\s+(\d+\.\d+)%");
        var percentages = matches.Select(m => double.Parse(m.Groups[1].Value)).ToList();

        Assert.That(percentages.Count, Is.GreaterThan(1),
            "Should have at least 2 survival curve entries to verify monotonicity");

        for (int i = 1; i < percentages.Count; i++)
        {
            Assert.That(percentages[i], Is.LessThanOrEqualTo(percentages[i - 1]),
                $"Survival curve must be non-increasing: floor {i + 1} ({percentages[i]}%) > floor {i} ({percentages[i - 1]}%)");
        }
    }

    // ── Test 5: JSONL round-trip ──────────────────────────────────────────────

    [Test]
    [Description("Serialize 5 runs to JSONL, read back via SoakJsonlReader, verify field equality")]
    public void JsonlRoundTrip_BasicFields_Preserved()
    {
        var originalRuns = new List<DungeonSoakRunResult>
        {
            MakeSurvivedRun(seed: 100),
            MakeSurvivedRun(seed: 101),
            MakeDeathRun(seed: 102, killer: "orc_brute"),
            MakeDeathRun(seed: 103, killer: "zombie"),
            MakeMaxTurnsRun(seed: 104),
        };

        string path = WriteTempJsonl(originalRuns);

        var summary = SoakJsonlReader.ReadFromFile(path);

        Assert.That(summary.RunsAttempted, Is.EqualTo(5),
            "Should read back exactly 5 runs");

        // Verify individual run fields match, matched by seed
        foreach (var original in originalRuns)
        {
            var restored = summary.Runs.FirstOrDefault(r => r.Seed == original.Seed);
            Assert.That(restored, Is.Not.Null,
                $"Run with seed {original.Seed} should be in the round-tripped summary");

            Assert.That(restored!.Outcome,         Is.EqualTo(original.Outcome),
                $"Seed {original.Seed}: Outcome must survive round-trip");
            Assert.That(restored.FloorsCompleted,  Is.EqualTo(original.FloorsCompleted),
                $"Seed {original.Seed}: FloorsCompleted must survive round-trip");
            Assert.That(restored.TotalTurns,       Is.EqualTo(original.TotalTurns),
                $"Seed {original.Seed}: TotalTurns must survive round-trip");
            Assert.That(restored.TotalKills,       Is.EqualTo(original.TotalKills),
                $"Seed {original.Seed}: TotalKills must survive round-trip");
        }
    }

    // ── Test 6: JSONL with BotSummary ────────────────────────────────────────

    [Test]
    [Description("Round-trip a run that includes BotSummary with ActionCounts — dictionary survives serialization")]
    public void JsonlRoundTrip_WithBotSummary_DictionarySurvives()
    {
        var run = MakeSurvivedRun(seed: 200);
        run.BotSummary = new BotRunSummary
        {
            TotalDecisions = 100,
            FloorsVisited  = 3,
            ActionCounts   = new Dictionary<string, int>
            {
                ["Attack"]          = 45,
                ["MoveToward"]      = 35,
                ["Heal"]            = 8,
                ["NavigateToStair"] = 12,
            },
            ReasonCounts   = new Dictionary<string, int>
            {
                ["attack_lowest_hp"] = 45,
                ["navigate_stair"]   = 12,
            },
            ContextCounts  = new Dictionary<string, int>
            {
                ["in_combat"] = 50,
                ["exploring"] = 40,
            },
            AvgHpWhenHealing        = 0.27,
            HealDecisions           = 8,
            DeathsWithUnusedPotions = 0,
        };

        string path = WriteTempJsonl(new[] { run });

        var summary = SoakJsonlReader.ReadFromFile(path);
        Assert.That(summary.RunsAttempted, Is.EqualTo(1));

        var restored = summary.Runs[0];
        Assert.That(restored.BotSummary, Is.Not.Null,
            "BotSummary should survive the JSONL round-trip");

        var bot = restored.BotSummary!;
        Assert.That(bot.TotalDecisions, Is.EqualTo(100));
        Assert.That(bot.ActionCounts.ContainsKey("Attack"), Is.True,
            "ActionCounts dictionary key 'Attack' must survive round-trip");
        Assert.That(bot.ActionCounts["Attack"], Is.EqualTo(45),
            "ActionCounts['Attack'] value must be preserved");
        Assert.That(bot.ActionCounts["NavigateToStair"], Is.EqualTo(12));
        Assert.That(bot.AvgHpWhenHealing, Is.EqualTo(0.27).Within(0.001));
    }

    // ── Test 7: JSONL malformed line handling ─────────────────────────────────

    [Test]
    [Description("3 valid lines + 1 garbage line: ReadFromFile returns 3 runs without throwing")]
    public void JsonlReader_MalformedLine_SkipsAndContinues()
    {
        var validRuns = new List<DungeonSoakRunResult>
        {
            MakeSurvivedRun(seed: 300),
            MakeDeathRun(seed: 301),
            MakeSurvivedRun(seed: 302),
        };

        // Write valid JSONL lines with a garbage line injected in the middle
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);

        using (var writer = new StreamWriter(path, append: false,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.WriteLine(JsonSerializer.Serialize(validRuns[0], JsonlOptions));
            writer.WriteLine(JsonSerializer.Serialize(validRuns[1], JsonlOptions));
            writer.WriteLine("this is not valid json {{{{{{");  // garbage line
            writer.WriteLine(JsonSerializer.Serialize(validRuns[2], JsonlOptions));
        }

        DungeonSoakSummary summary = null!;
        Assert.DoesNotThrow(() =>
        {
            summary = SoakJsonlReader.ReadFromFile(path);
        }, "ReadFromFile must not throw when encountering malformed lines");

        Assert.That(summary.RunsAttempted, Is.EqualTo(3),
            "Should read exactly 3 valid runs, skipping the malformed line");
    }

    // ── Test 8: Anomaly detection ─────────────────────────────────────────────

    [Test]
    [Description("Anomaly detection: 0 kills run, max_turns run, died-with-3-potions run all appear in anomalies section")]
    public void Report_AnomalyDetection_FindsAllAnomalousRuns()
    {
        // Run with 0 kills — survived but never engaged
        var zeroKillRun = MakeSurvivedRun(seed: 400);
        // Override TotalKills and floor MonstersKilled to 0
        zeroKillRun = new DungeonSoakRunResult
        {
            Seed                = 400,
            Outcome             = OutcomeClassifier.Survived,
            FailureType         = OutcomeClassifier.FailureNone,
            FailureDetail       = "",
            DeepestFloorReached = 3,
            FloorsCompleted     = 3,
            TotalTurns          = 150,
            TotalKills          = 0,  // anomaly: zero kills
            FinalHp             = 50,
            FinalMaxHp          = 55,
            FinalHpFraction     = 50.0 / 55.0,
            PotionsUsed         = 0,
            PotionsRemaining    = 2,
            BoonsAcquired       = 0,
            DurationSeconds     = 1.0,
            PerFloor            =
            [
                new FloorRunMetrics { Depth = 1, TurnsTaken = 50, MonstersKilled = 0, PlayerMaxHp = 55, Descended = true },
                new FloorRunMetrics { Depth = 2, TurnsTaken = 50, MonstersKilled = 0, PlayerMaxHp = 55, Descended = true },
                new FloorRunMetrics { Depth = 3, TurnsTaken = 50, MonstersKilled = 0, PlayerMaxHp = 55, Descended = true },
            ],
        };

        // Max_turns run
        var maxTurnsRun = MakeMaxTurnsRun(seed: 401);

        // Run that died with 3 potions remaining
        var wastedPotionsRun = MakeDeathRun(seed: 402, killer: "orc_brute", potionsRemaining: 3);

        // Normal run to pad out the summary
        var normalRun = MakeSurvivedRun(seed: 403);

        var runs = new List<DungeonSoakRunResult>
        {
            zeroKillRun,
            maxTurnsRun,
            wastedPotionsRun,
            normalRun,
        };

        var summary = DungeonSoakSummary.ComputeFrom(runs);
        string report = DungeonSoakReport.Generate(summary);

        TestContext.WriteLine(report);

        // Split into lines to isolate the Anomalies section
        // Find everything after "Anomalies:" header
        int anomalyIdx = report.IndexOf("Anomalies:", StringComparison.Ordinal);
        Assert.That(anomalyIdx, Is.GreaterThanOrEqualTo(0), "Anomalies section must be present");
        string anomalySection = report.Substring(anomalyIdx);

        Assert.That(anomalySection, Does.Contain("0 kills").Or.Contain("0 kill"),
            "Zero-kills anomaly must appear in anomalies section");

        Assert.That(anomalySection, Does.Contain("max turn").Or.Contain("turn limit"),
            "Max-turns anomaly must appear in anomalies section");

        Assert.That(anomalySection, Does.Contain("3"),
            "Wasted-potions anomaly mentioning '3 healing potions' must appear in anomalies section");
    }
}
