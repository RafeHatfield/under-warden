using UnderWarden.Analyst;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// RubricLoader validation tests. The loader is strict by design: a check that cannot run must
/// fail LOUDLY at load, never silently. So a missing/unknown mechanism, a missing predicate, a
/// malformed predicate, and a schema mismatch are all fatal — and the recognized-but-unimplemented
/// mechanisms (text_pattern/llm_judged/trigger_consequence) load fine and log-and-skip at detect.
/// </summary>
[TestFixture]
[Description("Analyst RubricLoader: strict validation; stub mechanisms recognized; real rubric loads")]
public class AnalystRubricLoaderTests
{
    [Test]
    [Description("The real config/rubric/v1.yaml loads with the four runnable predicates + structural schema")]
    public void RealRubricV1_Loads()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", "rubric", "v1.yaml");
        Assert.That(File.Exists(path), Is.True, $"v1.yaml not found at {path}");

        var rubric = RubricLoader.LoadFromFile(path);

        Assert.That(rubric.SchemaVersion, Is.EqualTo(1));
        var predicateCats = rubric.BugCategories.Where(c => c.Mechanism == Mechanisms.Predicate).Select(c => c.Name).ToHashSet();
        Assert.That(predicateCats, Is.SupersetOf(new[]
        {
            "soft_lock", "hp_out_of_range", "aggression_tally_negative", "possession_body_inconsistent",
        }), "All four RUNNABLE-NOW predicate categories must load.");
        Assert.That(rubric.StructuralJudgmentSchema, Is.EquivalentTo(new[]
        {
            "dead_action_space", "forced_move", "novel_encounter", "system_unreachable",
        }));
    }

    [Test]
    [Description("Unknown mechanism is a fatal load error (never silently skipped)")]
    public void UnknownMechanism_Fatal()
    {
        const string yaml = """
            schema_version: 1
            bug_categories:
              weird:
                mechanism: telepathy
                description: "not a thing"
            """;
        var ex = Assert.Throws<RubricLoadException>(() => RubricLoader.LoadFromText(yaml));
        Assert.That(ex!.Message, Does.Contain("unknown mechanism").IgnoreCase);
    }

    [Test]
    [Description("Missing mechanism field is fatal")]
    public void MissingMechanism_Fatal()
    {
        const string yaml = """
            schema_version: 1
            bug_categories:
              no_mech:
                description: "missing mechanism"
            """;
        var ex = Assert.Throws<RubricLoadException>(() => RubricLoader.LoadFromText(yaml));
        Assert.That(ex!.Message, Does.Contain("mechanism"));
    }

    [Test]
    [Description("A predicate category with no predicate expression is fatal")]
    public void PredicateMissingExpression_Fatal()
    {
        const string yaml = """
            schema_version: 1
            bug_categories:
              empty_pred:
                mechanism: predicate
                description: "no predicate string"
            """;
        var ex = Assert.Throws<RubricLoadException>(() => RubricLoader.LoadFromText(yaml));
        Assert.That(ex!.Message, Does.Contain("predicate"));
    }

    [Test]
    [Description("A malformed predicate expression is fatal at load, not a silent never-fire")]
    public void MalformedPredicate_Fatal()
    {
        const string yaml = """
            schema_version: 1
            bug_categories:
              broken:
                mechanism: predicate
                predicate: "available_action_count == "
            """;
        Assert.Throws<RubricLoadException>(() => RubricLoader.LoadFromText(yaml));
    }

    [Test]
    [Description("Schema-version mismatch is fatal")]
    public void SchemaVersionMismatch_Fatal()
    {
        const string yaml = """
            schema_version: 99
            bug_categories: {}
            """;
        var ex = Assert.Throws<RubricLoadException>(() => RubricLoader.LoadFromText(yaml));
        Assert.That(ex!.Message, Does.Contain("schema_version"));
    }

    [Test]
    [Description("A stub mechanism (text_pattern) loads fine and is log-and-skipped at detection, not run")]
    public void StubMechanism_LoadsAndSkips()
    {
        const string yaml = """
            schema_version: 1
            bug_categories:
              soft_lock:
                mechanism: predicate
                predicate: "available_action_count == 0 and not is_game_over"
              em_dash:
                mechanism: text_pattern
                pattern: "—"
            """;
        var rubric = RubricLoader.LoadFromText(yaml);
        Assert.That(rubric.BugCategories.Count, Is.EqualTo(2));

        // A minimal one-turn transcript (no violation).
        const string jsonl = """
            {"record_type":"header","schema_version":1,"run_id":"r","persona":"p","player_type":"bot","replay_available":true}
            {"record_type":"turn","turn":1,"floor":1,"available_action_count":5,"is_game_over":false,"action_taken":{"kind":"Wait"},"events":[]}
            {"record_type":"summary","hp_profile":[],"system_triggers":{},"memos_delivered":[]}
            """;
        var result = new BugDetector(rubric).Detect(TranscriptLoader.LoadFromText(jsonl));

        Assert.That(result.Skipped.Select(s => s.Category), Does.Contain("em_dash"),
            "text_pattern category must be recognized and skipped, not run or rejected.");
        Assert.That(result.Coverage.Select(c => c.Category), Does.Contain("soft_lock"),
            "the predicate category must still run alongside the skipped stub.");
        Assert.That(result.Skipped.Single(s => s.Category == "em_dash").Reason, Does.Contain("not implemented"));
    }
}
