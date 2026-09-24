using System.Text.Json.Nodes;
using UnderWarden.Analyst;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Tests for the Phase-4 batch pipeline (BatchAnalyzer + AggregateReport).
///
/// Standing invariant under test: silence travels with its ran-count. "0 candidates across the
/// batch" is only meaningful beside the audit-trail roll-up (turns evaluated per category, runs
/// ran, runs skipped). And the heatmap is neutral data; coverage_semantics is applied on top to
/// ROUTE 0× mechanism triggers as UNVERIFIED blind spots — never to conclude "broken".
/// </summary>
[TestFixture]
[Description("Analyst batch: audit-trail roll-up survives aggregation; 0x mechanism triggers route as blind spots")]
public class AnalystBatchTests
{
    private string _rubricPath = null!;
    private string _tmpDir = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        _rubricPath = Path.Combine(testDir, "..", "..", "..", "..", "config", "rubric", "v1.yaml");
        Assert.That(File.Exists(_rubricPath), Is.True, $"v1.yaml not found at {_rubricPath}");
    }

    [SetUp]
    public void PerTest()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "yarl-batch-" + TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(_tmpDir);
    }

    [TearDown]
    public void Cleanup()
    {
        if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, recursive: true);
    }

    private Rubric Rubric() => RubricLoader.LoadFromFile(_rubricPath);
    private AggregateReport Analyze() => BatchAnalyzer.Analyze(_tmpDir, Rubric(), concurrency: 4);

    // ── The audit-trail invariant ───────────────────────────────────────────────

    [Test]
    [Description("Audit trail survives aggregation: turns-evaluated and runs-ran summed across the batch")]
    public void AuditTrail_RollsUpAcrossRuns()
    {
        WriteRun("r1", turns: 3);
        WriteRun("r2", turns: 5);
        WriteRun("r3", turns: 2);

        var agg = Analyze();
        Assert.That(agg.RunsEvaluated, Is.EqualTo(3));

        var softLock = agg.PredicateCoverage.Single(c => c.Category == "soft_lock");
        Assert.That(softLock.TotalTurnsEvaluated, Is.EqualTo(10), "3+5+2 turns must sum.");
        Assert.That(softLock.RunsEvaluated, Is.EqualTo(3), "Ran in every run.");
        Assert.That(softLock.RunsSkipped, Is.EqualTo(0));
        Assert.That(agg.PredicateCoverage.Count, Is.EqualTo(5), "All five predicate categories in the audit trail.");
        Assert.That(agg.BugCandidates, Is.Empty, "Clean batch — but provably ran (see coverage).");
    }

    [Test]
    [Description("A run missing a predicate's field rolls up as a SKIP, never as silent-clean")]
    public void MissingField_RollsUpAsSkip()
    {
        WriteRun("ok", turns: 2);
        WriteRunMissingField("broken", turns: 2, dropField: "run_aggression_tally");

        var agg = Analyze();
        var skip = agg.SkippedMechanisms.SingleOrDefault(s => s.Category == "aggression_tally_negative");
        Assert.That(skip, Is.Not.Null, "Missing field must surface as a roll-up skip.");
        Assert.That(skip!.RunsSkipped, Is.EqualTo(1));

        var cov = agg.PredicateCoverage.Single(c => c.Category == "aggression_tally_negative");
        Assert.That(cov.RunsEvaluated, Is.EqualTo(1), "Ran only in the complete run.");
        Assert.That(cov.RunsSkipped, Is.EqualTo(1), "Skipped in the run missing the field.");
    }

    // ── Bug-candidate frequency ─────────────────────────────────────────────────

    [Test]
    [Description("Bug-candidate confidence is reported as frequency 'N of M runs', not a synthesized score")]
    public void BugCandidate_FrequencyAcrossRuns()
    {
        WriteRun("clean1", turns: 2);
        WriteRun("clean2", turns: 2);
        WriteViolatingRun("bad", hpPct: 1.7);  // hp_out_of_range fires

        var agg = Analyze();
        var hp = agg.BugCandidates.Single(c => c.Category == "hp_out_of_range");
        Assert.That(hp.RunsWithCandidate, Is.EqualTo(1));
        Assert.That(hp.TotalRuns, Is.EqualTo(3));
        Assert.That(hp.ExampleEvidence, Does.Contain("player_hp_pct=1.7"));
    }

    // ── Heatmap + blind-spot routing ────────────────────────────────────────────

    [Test]
    [Description("A MECHANISM trigger at 0x across the batch is routed as an UNVERIFIED blind spot, not 'broken'")]
    public void MechanismTriggerZero_IsBlindSpot_Routed()
    {
        WriteRun("a", turns: 1, possessionUsed: false, muralRead: false);
        WriteRun("b", turns: 1, possessionUsed: false, muralRead: false);

        var agg = Analyze();

        var possession = agg.SystemTriggerHeatmap.Single(h => h.Trigger == "possession_used");
        Assert.That(possession.TriggerClass, Is.EqualTo("mechanism"));
        Assert.That(possession.FiredInRuns, Is.EqualTo(0));

        var spot = agg.MechanismBlindSpots.SingleOrDefault(b => b.Trigger == "possession_used");
        Assert.That(spot, Is.Not.Null, "0x mechanism trigger must be a routed blind spot.");
        // ROUTE, not CONCLUDE: it points at a targeted/scripted run and explicitly disclaims "broken".
        Assert.That(spot!.Route, Does.Contain("unverified").And.Contains("targeted"));
        Assert.That(spot.Route, Does.Contain("does NOT mean broken"),
            "Must explicitly disclaim the broken conclusion (route, not conclude).");
    }

    [Test]
    [Description("A mechanism trigger that fired in >=1 run is NOT a blind spot; content 0x is never flagged")]
    public void FiredMechanism_NotBlindSpot_And_ContentZero_NotFlagged()
    {
        WriteRun("a", turns: 1, possessionUsed: true, muralRead: false);   // possession fired here
        WriteRun("b", turns: 1, possessionUsed: false, muralRead: false);

        var agg = Analyze();

        var possession = agg.SystemTriggerHeatmap.Single(h => h.Trigger == "possession_used");
        Assert.That(possession.FiredInRuns, Is.EqualTo(1));
        Assert.That(possession.FireRate, Is.EqualTo(0.5).Within(0.001));
        Assert.That(agg.MechanismBlindSpots.Any(b => b.Trigger == "possession_used"), Is.False,
            "Fired-in-1-run mechanism is exercised, not a blind spot.");

        // mural_read is CONTENT and 0x — must NOT be flagged as a blind spot.
        var mural = agg.SystemTriggerHeatmap.Single(h => h.Trigger == "mural_read");
        Assert.That(mural.TriggerClass, Is.EqualTo("content"));
        Assert.That(mural.FiredInRuns, Is.EqualTo(0));
        Assert.That(agg.MechanismBlindSpots.Any(b => b.Trigger == "mural_read"), Is.False,
            "Content triggers are never blind spots, even at 0x.");
    }

    // ── Deferred slots + resilience ─────────────────────────────────────────────

    [Test]
    [Description("Coherence and structural-judgment slots are present-but-N/A, not faked")]
    public void DeferredSlots_AreNotApplicable()
    {
        WriteRun("r1", turns: 1);
        var agg = Analyze();
        Assert.That(agg.CoherenceStatus, Does.Contain("N/A"));
        Assert.That(agg.StructuralJudgmentStatus, Does.Contain("N/A"));

        // And they serialize into the JSON (slot-in ready).
        var node = JsonNode.Parse(agg.ToJson())!.AsObject();
        Assert.That(node["coherence_status"]!.GetValue<string>(), Does.Contain("N/A"));
        Assert.That(node["structural_judgment_status"]!.GetValue<string>(), Does.Contain("N/A"));
    }

    [Test]
    [Description("A malformed transcript is recorded as a load failure and never aborts the batch")]
    public void MalformedTranscript_RecordedAsFailure_BatchContinues()
    {
        WriteRun("good", turns: 2);
        File.WriteAllText(Path.Combine(_tmpDir, "bad.jsonl"), "{ not valid json");

        var agg = Analyze();
        Assert.That(agg.RunsEvaluated, Is.EqualTo(1), "The good run still evaluates.");
        Assert.That(agg.RunsFailedToLoad, Is.EqualTo(1));
        Assert.That(agg.FailedFiles.Single(), Does.Contain("bad.jsonl"));
    }

    [Test]
    [Description("Aggregation is deterministic regardless of parallel completion order")]
    public void Aggregation_IsDeterministic()
    {
        for (int i = 0; i < 12; i++) WriteRun($"run-{i:D2}", turns: i + 1);
        var a = BatchAnalyzer.Analyze(_tmpDir, Rubric(), concurrency: 8).ToJson();
        var b = BatchAnalyzer.Analyze(_tmpDir, Rubric(), concurrency: 1).ToJson();
        Assert.That(a, Is.EqualTo(b), "Same batch must produce an identical aggregate at any concurrency.");
    }

    // ── Fixture writers ─────────────────────────────────────────────────────────

    private void WriteRun(string id, int turns, bool possessionUsed = false, bool muralRead = false)
        => File.WriteAllText(Path.Combine(_tmpDir, $"{id}.jsonl"),
            BuildTranscript(id, turns, hpPct: 0.8, possessionUsed, muralRead, dropField: null));

    private void WriteViolatingRun(string id, double hpPct)
        => File.WriteAllText(Path.Combine(_tmpDir, $"{id}.jsonl"),
            BuildTranscript(id, turns: 2, hpPct: hpPct, false, false, dropField: null));

    private void WriteRunMissingField(string id, int turns, string dropField)
        => File.WriteAllText(Path.Combine(_tmpDir, $"{id}.jsonl"),
            BuildTranscript(id, turns, hpPct: 0.8, false, false, dropField));

    private static string BuildTranscript(
        string runId, int turns, double hpPct, bool possessionUsed, bool muralRead, string? dropField)
    {
        var lines = new List<string>
        {
            new JsonObject
            {
                ["record_type"] = "header", ["schema_version"] = 1, ["run_id"] = runId,
                ["persona"] = "balanced", ["player_type"] = "bot", ["replay_available"] = true,
            }.ToJsonString(),
        };

        for (int t = 1; t <= turns; t++)
        {
            var turn = new JsonObject
            {
                ["record_type"] = "turn", ["turn"] = t, ["floor"] = 1,
                ["player_hp_pct"] = hpPct,
                ["available_action_count"] = 5,
                ["is_game_over"] = false,
                ["run_aggression_tally"] = 0,
                ["possession_active"] = false,
                ["controlled_entity_id"] = 0,
                ["player_entity_id"] = 0,
                ["action_taken"] = new JsonObject { ["kind"] = "Wait" },
                ["events"] = new JsonArray(),
            };
            if (dropField != null) turn.Remove(dropField);
            lines.Add(turn.ToJsonString());
        }

        lines.Add(new JsonObject
        {
            ["record_type"] = "summary",
            ["hp_profile"] = new JsonArray(),
            ["system_triggers"] = new JsonObject
            {
                ["possession_used"] = possessionUsed,
                ["possession_first_turn"] = null,   // non-bool metadata — must be excluded from heatmap
                ["orc_rep_changed"] = false,
                ["mural_read"] = muralRead,
                ["past_sasha_encountered"] = false,
                ["weighing_reached"] = false,
                ["weighing_guardian_allied_count"] = 0,  // numeric — excluded from heatmap
            },
            ["run_aggression_tally"] = 0,
            ["memos_delivered"] = new JsonArray(),
        }.ToJsonString());

        return string.Join("\n", lines);
    }
}
