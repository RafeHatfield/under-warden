using System.Text.Json;
using System.Text.RegularExpressions;
using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Balance.Transcript;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Tests for the enriched LLM-testing transcript (Analyst/Player shared format).
/// Covers the two invariants the system leans on (deterministic entity-ID assignment;
/// verbatim narrative text reaching the transcript) plus schema well-formedness and
/// full action_taken serialization.
///
/// See docs/llm-testing/shared-transcript-schema.md and 00-overview.md.
/// </summary>
[TestFixture]
[Description("Enriched LLM-testing transcript: schema, determinism, verbatim text capture")]
public class EnrichedTranscriptTests
{
    private string _entitiesPath = null!;
    private string _templatesPath = null!;
    private VoiceLineRegistry _voice = null!;
    private MemoRegistry _memos = null!;
    private const int BaseSeed = 1337;

    [OneTimeSetUp]
    public void Setup()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        string Cfg(params string[] parts) =>
            Path.Combine(new[] { testDir, "..", "..", "..", "..", "config" }.Concat(parts).ToArray());

        _entitiesPath = Cfg("entities.yaml");
        _templatesPath = Cfg("level_templates.yaml");
        Assert.That(File.Exists(_entitiesPath), Is.True, $"entities.yaml not found at {_entitiesPath}");

        // Voice pools, merged in Main.cs order (weighing_audit is a different schema, excluded).
        _voice = VoiceLineRegistry.LoadFromYaml(File.ReadAllText(Cfg("voice_lines", "hollowmark.yaml")));
        foreach (var f in new[] { "quipping_shade.yaml", "possession.yaml", "catalog_past_selves.yaml" })
            _voice.Merge(VoiceLineRegistry.LoadFromYaml(File.ReadAllText(Cfg("voice_lines", f))));

        _memos = MemoRegistry.LoadFromYaml(
            File.ReadAllText(Cfg("under_warden", "memos.yaml")),
            File.ReadAllText(Cfg("under_warden", "cause_display_names.yaml")));
    }

    /// <summary>
    /// Build a harness with a FRESH EntityFactory. Entity IDs are deterministic only from a
    /// fresh factory; the counter is not reset per run, so a reused harness drifts absolute
    /// IDs across runs. Replay reproduces from a fresh factory, so the determinism invariant
    /// must be checked across fresh harnesses — exactly how the transcript CLI generates runs.
    /// </summary>
    private DungeonRunHarness BuildHarness()
    {
        var content = new ContentLoader().LoadAllFromFile(_entitiesPath);
        var entityFactory = new EntityFactory();
        var itemFactory = new ItemFactory(content.Items, entityFactory);
        var monsterFactory = new MonsterFactory(content.Monsters, entityFactory, itemFactory);
        var consumableFactory = new ConsumableFactory(content.Consumables, entityFactory);
        var templates = LevelTemplateRegistry.FromFile(_templatesPath);
        return new DungeonRunHarness(
            new DungeonFloorBuilder(templates, monsterFactory, itemFactory, consumableFactory));
    }

    // ── Invariant 1: deterministic entity-ID assignment under a fixed seed ──────

    [Test]
    [Description("Invariant 1: same seed -> byte-identical transcript (modulo run_id), proving deterministic entity-IDs and replay foundation")]
    public void SameSeed_ProducesIdenticalTranscript()
    {
        var (_, a) = BuildHarness().RunWithTranscript(floors: 3, seed: BaseSeed, voiceRegistry: _voice, memoRegistry: _memos);
        var (_, b) = BuildHarness().RunWithTranscript(floors: 3, seed: BaseSeed, voiceRegistry: _voice, memoRegistry: _memos);

        // run_id is a fresh GUID per run by design — strip it, everything else must match.
        string Strip(string s) => Regex.Replace(s, "\"run_id\":\"[^\"]*\"", "\"run_id\":\"X\"");
        Assert.That(Strip(a), Is.EqualTo(Strip(b)),
            "Same seed must produce an identical transcript: entity IDs, action targets, events, and resolved text are all deterministic.");
    }

    [Test]
    [Description("Invariant 1: action_taken target entity IDs are identical across same-seed runs")]
    public void SameSeed_IdenticalActionTargetEntityIds()
    {
        var ids1 = TargetEntityIdSequence(BuildHarness().RunWithTranscript(3, BaseSeed, voiceRegistry: _voice, memoRegistry: _memos).Jsonl);
        var ids2 = TargetEntityIdSequence(BuildHarness().RunWithTranscript(3, BaseSeed, voiceRegistry: _voice, memoRegistry: _memos).Jsonl);
        Assert.That(ids1, Is.Not.Empty, "Expected at least one targeted action (attack/move-toward).");
        Assert.That(ids1, Is.EqualTo(ids2), "Targeted entity IDs must be deterministic under a fixed seed.");
    }

    [Test]
    [Description("Invariant 1 corollary: transcript capture (voice resolution + memo eval) does not perturb the deterministic game stream")]
    public void TranscriptCapture_DoesNotAlterGameOutcome()
    {
        var harness = BuildHarness();
        var plain = harness.Run(floors: 3, baseSeed: BaseSeed);
        var (withTranscript, _) = harness.RunWithTranscript(3, BaseSeed, voiceRegistry: _voice, memoRegistry: _memos);

        Assert.Multiple(() =>
        {
            Assert.That(withTranscript.TotalTurns, Is.EqualTo(plain.TotalTurns), "TotalTurns must match the plain run.");
            Assert.That(withTranscript.TotalKills, Is.EqualTo(plain.TotalKills), "TotalKills must match the plain run.");
            Assert.That(withTranscript.FloorsCompleted, Is.EqualTo(plain.FloorsCompleted), "FloorsCompleted must match.");
        });
    }

    // ── Invariant 2: verbatim narrative text reaching the transcript ────────────

    [Test]
    [Description("Invariant 2 (voice): VoiceLineEvent resolves to verbatim text in the serialized transcript")]
    public void VoiceLineEvent_ResolvedTextReachesTranscript()
    {
        // VoiceLineEvents are possession-gated, so standard bot runs never fire one.
        // Verify the resolution wiring directly: a recorder with the real registry must
        // populate resolved_text from the trigger's pool and serialize it verbatim.
        const string trigger = "possession_exit_voluntary";
        var expectedLine = _voice.GetLine(trigger, new SeededRandom(1), firedSet: new HashSet<string>());
        Assert.That(expectedLine, Is.Not.Null.And.Not.Empty, "Test fixture: trigger pool must be populated.");

        var recorder = new TranscriptRecorder(seed: 42, persona: "balanced", voiceRegistry: _voice);
        recorder.BeginFloor(1, 1.0);
        var evt = new VoiceLineEvent { ActorId = 0, TriggerId = trigger };
        recorder.RecordTurn(
            1, 1,
            new TurnVitals(PlayerHpPct: 1.0, AvailableActionCount: 5, IsGameOver: false,
                RunAggressionTally: 0, PossessionActive: false, ControlledEntityId: 0, PlayerEntityId: 0),
            PlayerAction.Wait, new TurnEvent[] { evt });
        recorder.Finish("run", 1, 1, 1, "survived", System.Array.Empty<MemoRecord>());

        var jsonl = recorder.ToJsonl();
        Assert.That(evt.ResolvedText, Is.Not.Null.And.Not.Empty, "ResolvedText must be populated on the event.");
        Assert.That(jsonl, Does.Contain("resolved_text"), "Serialized event must carry a resolved_text field.");
        Assert.That(jsonl, Does.Contain(evt.ResolvedText!), "The verbatim resolved line must appear in the transcript.");
    }

    [Test]
    [Description("Invariant 2 (memos): a death run captures Under-Warden memos verbatim in RunSummary")]
    public void DeathRun_CapturesVerbatimMemos()
    {
        // Seed 1337 dies on floor 3 -> death_first (always) + floor_low (floor <= 3).
        var (result, jsonl) = BuildHarness().RunWithTranscript(3, BaseSeed, voiceRegistry: _voice, memoRegistry: _memos);
        Assume.That(result.Outcome, Is.EqualTo(OutcomeClassifier.Died), "Fixture seed expected to die within 3 floors.");

        var summary = ParseSummary(jsonl);
        var memos = summary.GetProperty("memos_delivered");
        Assert.That(memos.GetArrayLength(), Is.GreaterThan(0), "A death run must deliver at least one memo.");

        var first = memos[0];
        Assert.That(first.GetProperty("key").GetString(), Is.Not.Empty);
        Assert.That(first.GetProperty("subject").GetString(), Is.Not.Empty);
        Assert.That(first.GetProperty("body").GetString(), Does.Contain("Under-Warden"),
            "Memo body must be the verbatim formatted text, greppable by analyst text checks.");
    }

    // ── Schema well-formedness + full action_taken serialization ────────────────

    [Test]
    [Description("Schema: header/turn/summary structure, record_type discriminators, schema_version")]
    public void Transcript_IsWellFormedJsonl()
    {
        var (_, jsonl) = BuildHarness().RunWithTranscript(3, BaseSeed, voiceRegistry: _voice, memoRegistry: _memos);
        var lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines.Length, Is.GreaterThanOrEqualTo(3), "header + >=1 turn + summary.");

        var header = JsonDocument.Parse(lines[0]).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(header.GetProperty("record_type").GetString(), Is.EqualTo("header"));
            Assert.That(header.GetProperty("schema_version").GetInt32(), Is.EqualTo(1));
            Assert.That(header.GetProperty("player_type").GetString(), Is.EqualTo("bot"));
            Assert.That(header.GetProperty("replay_available").GetBoolean(), Is.True);
        });

        // Every middle line is a well-formed turn record carrying action_taken.
        for (int i = 1; i < lines.Length - 1; i++)
        {
            var turn = JsonDocument.Parse(lines[i]).RootElement;
            Assert.That(turn.GetProperty("record_type").GetString(), Is.EqualTo("turn"), $"line {i}");
            Assert.That(turn.TryGetProperty("action_taken", out var act), Is.True, $"line {i} missing action_taken");
            Assert.That(act.GetProperty("kind").GetString(), Is.Not.Empty, $"line {i} action_taken.kind");
        }

        var summary = JsonDocument.Parse(lines[^1]).RootElement;
        Assert.That(summary.GetProperty("record_type").GetString(), Is.EqualTo("summary"));
        Assert.That(summary.TryGetProperty("hp_profile", out var hp), Is.True);
        Assert.That(hp.GetArrayLength(), Is.GreaterThan(0), "HP profile has a point per floor entered.");
        Assert.That(summary.TryGetProperty("system_triggers", out _), Is.True);
    }

    [Test]
    [Description("action_taken: an Attack turn carries the target entity ID and stable type string")]
    public void AttackAction_CarriesTargetEntityIdAndType()
    {
        var (_, jsonl) = BuildHarness().RunWithTranscript(3, BaseSeed, voiceRegistry: _voice, memoRegistry: _memos);
        var lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        JsonElement? attack = null;
        foreach (var line in lines)
        {
            var el = JsonDocument.Parse(line).RootElement;
            if (el.TryGetProperty("action_taken", out var act)
                && act.ValueKind == JsonValueKind.Object
                && act.GetProperty("kind").GetString() == "Attack")
            {
                attack = act;
                break;
            }
        }

        Assert.That(attack, Is.Not.Null, "Expected at least one Attack action in a 3-floor run.");
        var a = attack!.Value;
        Assert.That(a.GetProperty("target_entity_id").ValueKind, Is.EqualTo(JsonValueKind.Number),
            "Attack must record the resolved target entity ID (replay prerequisite).");
        Assert.That(a.GetProperty("target_entity_type").GetString(), Is.Not.Empty,
            "Attack must record the target's stable type string for readability.");
    }

    [Test]
    [Description("Rubric v1 RUNNABLE-NOW predicates can read every field they reference on a real TurnRecord + RunSummary")]
    public void TurnRecord_CarriesRubricV1PredicateFields()
    {
        var (_, jsonl) = BuildHarness().RunWithTranscript(3, BaseSeed, voiceRegistry: _voice, memoRegistry: _memos);
        var lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // A representative turn record carries all four predicates' fields, correctly typed.
        var turn = JsonDocument.Parse(lines[1]).RootElement;
        Assert.Multiple(() =>
        {
            // soft_lock: available_action_count, is_game_over
            Assert.That(turn.GetProperty("available_action_count").ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(turn.GetProperty("is_game_over").ValueKind, Is.AnyOf(JsonValueKind.True, JsonValueKind.False));
            // hp_out_of_range: player_hp_pct
            Assert.That(turn.GetProperty("player_hp_pct").ValueKind, Is.EqualTo(JsonValueKind.Number));
            // aggression_tally_negative: run_aggression_tally
            Assert.That(turn.GetProperty("run_aggression_tally").ValueKind, Is.EqualTo(JsonValueKind.Number));
            // possession_body_inconsistent: possession_active, controlled_entity_id, player_entity_id
            Assert.That(turn.GetProperty("possession_active").ValueKind, Is.AnyOf(JsonValueKind.True, JsonValueKind.False));
            Assert.That(turn.GetProperty("controlled_entity_id").ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(turn.GetProperty("player_entity_id").ValueKind, Is.EqualTo(JsonValueKind.Number));
        });

        // Sanity: the increment-only invariants actually hold across the real run, and the
        // run-level reconciliation tally is present in RunSummary.
        foreach (var line in lines)
        {
            var el = JsonDocument.Parse(line).RootElement;
            if (el.GetProperty("record_type").GetString() != "turn") continue;
            Assert.That(el.GetProperty("run_aggression_tally").GetInt32(), Is.GreaterThanOrEqualTo(0),
                "aggression_tally_negative predicate must never fire on a clean run.");
            var hp = el.GetProperty("player_hp_pct").GetDouble();
            Assert.That(hp, Is.InRange(0.0, 1.0), "hp_out_of_range predicate must never fire on a clean run.");
        }

        var summary = JsonDocument.Parse(lines[^1]).RootElement;
        Assert.That(summary.TryGetProperty("run_aggression_tally", out var tally), Is.True);
        Assert.That(tally.GetInt32(), Is.GreaterThanOrEqualTo(0));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static List<int> TargetEntityIdSequence(string jsonl)
    {
        var ids = new List<int>();
        foreach (var line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var el = JsonDocument.Parse(line).RootElement;
            if (el.TryGetProperty("action_taken", out var act)
                && act.ValueKind == JsonValueKind.Object
                && act.TryGetProperty("target_entity_id", out var tid)
                && tid.ValueKind == JsonValueKind.Number)
            {
                ids.Add(tid.GetInt32());
            }
        }
        return ids;
    }

    private static JsonElement ParseSummary(string jsonl)
    {
        var lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return JsonDocument.Parse(lines[^1]).RootElement.Clone();
    }
}
