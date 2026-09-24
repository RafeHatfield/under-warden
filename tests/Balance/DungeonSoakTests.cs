using System.Text.Json;
using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Tests for the dungeon soak infrastructure: DungeonSoakRunResult, OutcomeClassifier,
/// DungeonSoakSummary, and the enriched FloorRunMetrics fields.
///
/// Tests that run 20+ bot iterations are tagged [Category("Slow")] and excluded from
/// the default fast suite.
/// </summary>
[TestFixture]
[Description("Dungeon soak infrastructure: outcome classification, survival curves, JSONL round-trip")]
public class DungeonSoakTests
{
    private DungeonRunHarness _harness = null!;
    private const int BaseSeed = 1337;

    [OneTimeSetUp]
    public void Setup()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var entitiesPath = Path.Combine(testDir, "..", "..", "..", "..", "config", "entities.yaml");
        var levelTemplatesPath = Path.Combine(testDir, "..", "..", "..", "..", "config", "level_templates.yaml");

        Assert.That(File.Exists(entitiesPath), Is.True,
            $"entities.yaml not found at {entitiesPath}");
        Assert.That(File.Exists(levelTemplatesPath), Is.True,
            $"level_templates.yaml not found at {levelTemplatesPath}");

        var loader = new ContentLoader();
        var content = loader.LoadAllFromFile(entitiesPath);
        var entityFactory     = new EntityFactory();
        var itemFactory       = new ItemFactory(content.Items, entityFactory);
        var monsterFactory    = new MonsterFactory(content.Monsters, entityFactory, itemFactory);
        var consumableFactory = new ConsumableFactory(content.Consumables, entityFactory);
        var templates         = LevelTemplateRegistry.FromFile(levelTemplatesPath);

        // Use the lightweight builder (no SpellItemFactory/boons) — sufficient for data-integrity tests.
        var floorBuilder = new DungeonFloorBuilder(templates, monsterFactory, itemFactory, consumableFactory);
        _harness = new DungeonRunHarness(floorBuilder);
    }

    // ── Test 1: Soak determinism ─────────────────────────────────────────────

    /// <summary>
    /// Two RunSoak calls with the same parameters produce identical aggregate statistics.
    /// Guards against any non-deterministic state leaking into the soak loop.
    ///
    /// Tagged Slow — 5 runs × 3 floors = 15 floor builds.
    /// </summary>
    [Test]
    [Category("Slow")]
    [Description("RunSoak is deterministic: identical aggregate stats for the same seed")]
    public void RunSoak_Deterministic()
    {
        var summary1 = _harness.RunSoak(floors: 3, runs: 5, baseSeed: BaseSeed);
        var summary2 = _harness.RunSoak(floors: 3, runs: 5, baseSeed: BaseSeed);

        Assert.That(summary1.SurvivalRate, Is.EqualTo(summary2.SurvivalRate),
            "SurvivalRate must be identical for the same seed");
        Assert.That(summary1.AvgFloorsCompleted, Is.EqualTo(summary2.AvgFloorsCompleted),
            "AvgFloorsCompleted must be identical for the same seed");

        Assert.That(summary1.Runs.Count, Is.EqualTo(summary2.Runs.Count),
            "Run count must match");

        for (int i = 0; i < summary1.Runs.Count; i++)
        {
            var r1 = summary1.Runs[i];
            var r2 = summary2.Runs[i];
            Assert.That(r1.Outcome, Is.EqualTo(r2.Outcome),
                $"Run {i}: Outcome differs between identical soak calls");
            Assert.That(r1.FloorsCompleted, Is.EqualTo(r2.FloorsCompleted),
                $"Run {i}: FloorsCompleted differs between identical soak calls");
            Assert.That(r1.TotalTurns, Is.EqualTo(r2.TotalTurns),
                $"Run {i}: TotalTurns differs between identical soak calls");
        }
    }

    // ── Test 2: OutcomeClassifier — death ────────────────────────────────────

    [Test]
    [Description("OutcomeClassifier: PlayerDied=true produces outcome='died' with killer in detail")]
    public void OutcomeClassifier_Death_ReturnsCorrectOutcome()
    {
        var result = new DungeonRunResult
        {
            Seed            = 1337,
            FloorsAttempted = 1,
            FloorsCompleted = 0,
            PlayerDied      = true,
            PerFloor        = [new FloorRunMetrics { Depth = 1, PlayerDied = true }],
        };

        var (outcome, failureType, failureDetail) = OutcomeClassifier.Classify(result, "orc_brute");

        Assert.That(outcome,       Is.EqualTo(OutcomeClassifier.Died));
        Assert.That(failureType,   Is.EqualTo(OutcomeClassifier.FailureDeath));
        Assert.That(failureDetail, Does.Contain("orc_brute"),
            "Killer name must appear in failure detail");
    }

    [Test]
    [Description("OutcomeClassifier: death with null killer produces 'unknown' failure detail")]
    public void OutcomeClassifier_Death_NullKillerProducesUnknown()
    {
        var result = new DungeonRunResult
        {
            Seed            = 1337,
            FloorsAttempted = 1,
            FloorsCompleted = 0,
            PlayerDied      = true,
            PerFloor        = [new FloorRunMetrics { Depth = 1, PlayerDied = true }],
        };

        var (outcome, failureType, failureDetail) = OutcomeClassifier.Classify(result, killerName: null);

        Assert.That(outcome,       Is.EqualTo(OutcomeClassifier.Died));
        Assert.That(failureType,   Is.EqualTo(OutcomeClassifier.FailureDeath));
        Assert.That(failureDetail, Is.EqualTo("unknown"),
            "Null killer name should produce 'unknown' failure detail");
    }

    // ── Test 3: OutcomeClassifier — survived ─────────────────────────────────

    [Test]
    [Description("OutcomeClassifier: FloorsCompleted == FloorsAttempted produces 'survived'")]
    public void OutcomeClassifier_Survived_ReturnsCorrectOutcome()
    {
        var result = new DungeonRunResult
        {
            Seed            = 1337,
            FloorsAttempted = 3,
            FloorsCompleted = 3,
            PlayerDied      = false,
            PerFloor        =
            [
                new FloorRunMetrics { Depth = 1, Descended = true },
                new FloorRunMetrics { Depth = 2, Descended = true },
                new FloorRunMetrics { Depth = 3, Descended = true },
            ],
        };

        var (outcome, failureType, failureDetail) = OutcomeClassifier.Classify(result, killerName: null);

        Assert.That(outcome,     Is.EqualTo(OutcomeClassifier.Survived));
        Assert.That(failureType, Is.EqualTo(OutcomeClassifier.FailureNone));
        Assert.That(failureDetail, Is.Empty, "Survived runs have no failure detail");
    }

    // ── Test 4: OutcomeClassifier — max_turns ────────────────────────────────

    [Test]
    [Description("OutcomeClassifier: floor with HitMaxTurns=true produces 'max_turns'")]
    public void OutcomeClassifier_MaxTurns_ReturnsCorrectOutcome()
    {
        var result = new DungeonRunResult
        {
            Seed            = 1337,
            FloorsAttempted = 2,
            FloorsCompleted = 1,
            PlayerDied      = false,
            PerFloor        =
            [
                new FloorRunMetrics { Depth = 1, Descended = true },
                new FloorRunMetrics { Depth = 2, HitMaxTurns = true },
            ],
        };

        var (outcome, failureType, failureDetail) = OutcomeClassifier.Classify(result, killerName: null);

        Assert.That(outcome,     Is.EqualTo(OutcomeClassifier.MaxTurns));
        Assert.That(failureType, Is.EqualTo(OutcomeClassifier.FailureMaxTurns));
        Assert.That(failureDetail, Does.Contain("2"),
            "Failure detail should mention the floor depth that hit the turn limit");
    }

    // ── Test 5: SurvivalCurve monotonicity ──────────────────────────────────

    /// <summary>
    /// The survival curve must be monotonically non-increasing.
    /// SurvivalCurve[d] represents the fraction of runs reaching floor d+1.
    /// Once a run dies, it cannot "un-die" and reach a later floor.
    ///
    /// Tagged Slow — 20 runs × 5 floors = 100 floor builds.
    /// </summary>
    [Test]
    [Category("Slow")]
    [Description("SurvivalCurve is monotonically non-increasing across all depths")]
    public void SurvivalCurve_Monotonic()
    {
        var summary = _harness.RunSoak(floors: 5, runs: 20, baseSeed: BaseSeed);

        TestContext.WriteLine($"Survival curve ({summary.RunsAttempted} runs):");
        for (int i = 0; i < summary.SurvivalCurve.Count; i++)
            TestContext.WriteLine($"  Floor {i + 1}: {summary.SurvivalCurve[i]:P1}");

        Assert.That(summary.SurvivalCurve.Count, Is.GreaterThan(0),
            "Survival curve must have at least one entry");

        for (int i = 1; i < summary.SurvivalCurve.Count; i++)
        {
            Assert.That(summary.SurvivalCurve[i], Is.LessThanOrEqualTo(summary.SurvivalCurve[i - 1]),
                $"SurvivalCurve[{i}] ({summary.SurvivalCurve[i]:P2}) must be <= " +
                $"SurvivalCurve[{i-1}] ({summary.SurvivalCurve[i-1]:P2}) — curve must not increase");
        }
    }

    // ── Test 6: FloorRunMetrics enrichment ──────────────────────────────────

    [Test]
    [Description("FloorRunMetrics.PlayerMaxHp > 0 for all floor entries after a soak run")]
    public void FloorRunMetrics_PlayerMaxHp_IsPositive()
    {
        var summary = _harness.RunSoak(floors: 3, runs: 3, baseSeed: BaseSeed);

        foreach (var run in summary.Runs)
        {
            foreach (var floor in run.PerFloor)
            {
                Assert.That(floor.PlayerMaxHp, Is.GreaterThan(0),
                    $"Floor {floor.Depth} of run seed {run.Seed}: PlayerMaxHp should be positive");
            }
        }
    }

    // ── Test 7: JSONL round-trip ─────────────────────────────────────────────

    [Test]
    [Description("DungeonSoakRunResult serializes to JSON and key fields survive round-trip")]
    public void DungeonSoakRunResult_JsonRoundTrip()
    {
        // Build a representative result without running the harness
        var original = new DungeonSoakRunResult
        {
            Seed                = 42,
            Outcome             = OutcomeClassifier.Survived,
            FailureType         = OutcomeClassifier.FailureNone,
            FailureDetail       = "",
            DeepestFloorReached = 3,
            FloorsCompleted     = 3,
            TotalTurns          = 150,
            TotalKills          = 12,
            FinalHp             = 38,
            FinalMaxHp          = 54,
            FinalHpFraction     = 38.0 / 54.0,
            PotionsUsed         = 2,
            PotionsRemaining    = 1,
            BoonsAcquired       = 2,
            DurationSeconds     = 1.23,
            PerFloor            =
            [
                new FloorRunMetrics { Depth = 1, TurnsTaken = 48, MonstersKilled = 4, PlayerMaxHp = 54 },
                new FloorRunMetrics { Depth = 2, TurnsTaken = 52, MonstersKilled = 4, PlayerMaxHp = 54 },
                new FloorRunMetrics { Depth = 3, TurnsTaken = 50, MonstersKilled = 4, PlayerMaxHp = 54 },
            ],
        };

        // Serialize with snake_case property names — matches harness JSONL output convention
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy  = null, // keys are data values, not property names
        };

        string json = JsonSerializer.Serialize(original, options);

        // Verify it's valid JSON
        Assert.DoesNotThrow(() => JsonDocument.Parse(json),
            "Serialized result must be valid JSON");

        // Verify snake_case property naming
        Assert.That(json, Does.Contain("\"seed\""),
            "seed field should use snake_case");
        Assert.That(json, Does.Contain("\"floors_completed\""),
            "FloorsCompleted should serialize as 'floors_completed'");
        Assert.That(json, Does.Contain("\"failure_detail\""),
            "FailureDetail should serialize as 'failure_detail'");

        // Deserialize using the same options
        var deserialized = JsonSerializer.Deserialize<DungeonSoakRunResult>(json, options);
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Seed,            Is.EqualTo(original.Seed));
        Assert.That(deserialized.Outcome,          Is.EqualTo(original.Outcome));
        Assert.That(deserialized.FloorsCompleted,  Is.EqualTo(original.FloorsCompleted));
        Assert.That(deserialized.TotalTurns,       Is.EqualTo(original.TotalTurns));
        Assert.That(deserialized.FinalHp,          Is.EqualTo(original.FinalHp));
    }

    // ── Additional: SurvivalRate in [0, 1] ──────────────────────────────────

    [Test]
    [Description("SurvivalRate is always in [0.0, 1.0]")]
    public void RunSoak_SurvivalRate_InValidRange()
    {
        var summary = _harness.RunSoak(floors: 3, runs: 3, baseSeed: BaseSeed);

        Assert.That(summary.SurvivalRate, Is.InRange(0.0, 1.0),
            $"SurvivalRate {summary.SurvivalRate:P2} is outside [0, 1]");
    }
}
