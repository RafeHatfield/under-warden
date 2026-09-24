using System.Text;
using System.Text.Json;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;

namespace UnderWarden.Logic.Balance.Transcript;

/// <summary>
/// Accumulates an enriched run transcript while a dungeon run executes, then
/// serializes it to JSONL (header line, one line per turn, summary line).
///
/// Created per run. The harness drives it: <see cref="BeginFloor"/> at each floor
/// entry, <see cref="RecordTurn"/> after each resolved turn, <see cref="Finish"/>
/// once the run ends, then <see cref="WriteJsonl"/>.
///
/// Voice resolution: VoiceLineEvents arrive carrying TriggerId only. The recorder
/// resolves them against an injected <see cref="VoiceLineRegistry"/> using a
/// DEDICATED rng seeded from the run seed — NEVER the game rng — so transcript
/// capture cannot perturb the deterministic game stream that replay depends on.
/// If no registry is injected, ResolvedText stays null (still greppable as a gap).
/// </summary>
public sealed class TranscriptRecorder
{
    private readonly int _seed;
    private readonly string _persona;
    private readonly string _playerType;
    private readonly string? _llmModel;
    private readonly VoiceLineRegistry? _voiceRegistry;

    // Dedicated rng + fired-set for voice resolution. Seeded deterministically from
    // the run seed so the same run resolves the same lines on re-record, while staying
    // completely isolated from GameState.Rng.
    private readonly SeededRandom _voiceRng;
    private readonly HashSet<string> _voiceFired = new();

    private readonly List<TurnRecord> _turns = new();
    private readonly List<double[]> _hpProfile = new();
    private readonly SystemTriggerLog _triggers = new();

    // Accumulated structural judgments from LLM Player turns. Empty for bot runs.
    private readonly List<StructuralAssessment> _judgments = new();

    private static readonly JsonSerializerOptions JsonOptions = BuildOptions();

    public TranscriptRecorder(int seed, string persona, VoiceLineRegistry? voiceRegistry,
        string playerType = "bot", string? llmModel = null)
    {
        _seed = seed;
        _persona = persona;
        _playerType = playerType;
        _llmModel = llmModel;
        _voiceRegistry = voiceRegistry;
        // Distinct from any game-floor seed derivation; voice resolution is cosmetic
        // to the transcript and must never collide with gameplay rng streams.
        _voiceRng = new SeededRandom(seed ^ 0x5F37_59DF);
    }

    /// <summary>Record the HP profile point at floor entry.</summary>
    public void BeginFloor(int depth, double hpPct)
    {
        _hpProfile.Add(new[] { (double)depth, Round(hpPct) });
    }

    /// <summary>
    /// Append a TurnRecord. <paramref name="events"/> is the turn's event list;
    /// VoiceLineEvents are resolved in place and system triggers are updated.
    /// <paramref name="vitals"/> carries the post-action scalar fields the rubric predicates read.
    /// <paramref name="decisionContext"/> and <paramref name="structuralAssessment"/> are LLM
    /// Player metadata — null for bot runs (zero behavior change for existing callers).
    /// Non-null assessments with a real Judgment are also accumulated into the run-level
    /// StructuralJudgments list.
    /// </summary>
    public void RecordTurn(
        int turn, int floor, TurnVitals vitals,
        PlayerAction action, IReadOnlyList<TurnEvent> events,
        string? decisionContext = null,
        StructuralAssessment? structuralAssessment = null)
    {
        ResolveVoiceLines(events);
        UpdateTriggers(turn, events);

        // Stamp the turn number onto the assessment so RunSummary.StructuralJudgments
        // carries turn refs as plan-player §6 requires. StructuralAssessment is a class
        // (not a record), so we create a new instance with Turn set rather than using `with`.
        StructuralAssessment? stampedAssessment = structuralAssessment != null
            ? new StructuralAssessment
              {
                  Judgment = structuralAssessment.Judgment,
                  Note     = structuralAssessment.Note,
                  Turn     = turn,
              }
            : null;

        // Accumulate per-turn assessments with real judgments into the run-level list.
        if (stampedAssessment != null && !string.IsNullOrEmpty(stampedAssessment.Judgment))
            _judgments.Add(stampedAssessment);

        _turns.Add(new TurnRecord
        {
            Turn                 = turn,
            Floor                = floor,
            PlayerHpPct          = Round(vitals.PlayerHpPct),
            AvailableActionCount = vitals.AvailableActionCount,
            IsGameOver           = vitals.IsGameOver,
            RunAggressionTally   = vitals.RunAggressionTally,
            PossessionActive     = vitals.PossessionActive,
            ControlledEntityId   = vitals.ControlledEntityId,
            PlayerEntityId       = vitals.PlayerEntityId,
            ActionTaken          = ActionTakenBuilder.From(action),
            Events               = events,
            DecisionContext      = decisionContext,
            StructuralAssessment = stampedAssessment,
        });
    }

    /// <summary>
    /// Accumulate a structural judgment that is NOT tied to a per-turn assessment — e.g.
    /// floor_summary and reflection entries that arrive in the same turn's ExtraJudgments list.
    /// The caller is responsible for stamping the Turn field before calling this method.
    /// </summary>
    public void AddStructuralJudgment(StructuralAssessment assessment)
    {
        if (!string.IsNullOrEmpty(assessment.Judgment))
            _judgments.Add(assessment);
    }

    private RunSummary? _summary;
    private TranscriptHeader? _header;

    /// <summary>
    /// Finalize the transcript. <paramref name="memos"/> are memos delivered this
    /// run (verbatim); pass an empty list when none. <paramref name="ending"/> is
    /// the outcome string (null if interrupted). <paramref name="runNarrative"/> is
    /// the LLM end-of-run narrative string — null for bot runs (zero behavior change).
    /// </summary>
    public void Finish(
        string runId, int depthReached, int floorCount, int turnCount,
        string? ending, IReadOnlyList<MemoRecord> memos, int runAggressionTally = 0,
        string? runNarrative = null)
    {
        _header = new TranscriptHeader
        {
            RunId           = runId,
            Persona         = _persona,
            PlayerType      = _playerType,
            LlmModel        = _llmModel,
            Seed            = _seed,
            DepthReached    = depthReached,
            Ending          = ending,
            TurnCount       = turnCount,
            FloorCount      = floorCount,
            // Replay-capable: every turn carries a full action_taken object (always
            // true for runs this recorder produces).
            ReplayAvailable = _turns.All(t => t.ActionTaken != null),
        };

        _summary = new RunSummary
        {
            HpProfile           = _hpProfile,
            SystemTriggers      = _triggers,
            RunAggressionTally  = runAggressionTally,
            MemosDelivered      = memos,
            StructuralJudgments = _judgments.Count > 0
                ? _judgments.ToArray()
                : Array.Empty<StructuralAssessment>(),
            RunNarrative        = runNarrative,
        };
    }

    /// <summary>Serialize the finalized transcript to JSONL (one record per line).</summary>
    public void WriteJsonl(TextWriter writer)
    {
        if (_header == null || _summary == null)
            throw new InvalidOperationException("Finish() must be called before WriteJsonl().");

        writer.WriteLine(JsonSerializer.Serialize(_header, JsonOptions));
        foreach (var turn in _turns)
            writer.WriteLine(JsonSerializer.Serialize(turn, JsonOptions));
        writer.WriteLine(JsonSerializer.Serialize(_summary, JsonOptions));
    }

    /// <summary>Serialize to a JSONL string (convenience for tests).</summary>
    public string ToJsonl()
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        WriteJsonl(sw);
        return sb.ToString();
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private void ResolveVoiceLines(IReadOnlyList<TurnEvent> events)
    {
        if (_voiceRegistry == null) return;
        foreach (var evt in events)
        {
            if (evt is VoiceLineEvent v && v.ResolvedText == null)
                v.ResolvedText = _voiceRegistry.GetLine(v.TriggerId, _voiceRng, _voiceFired);
        }
    }

    private void UpdateTriggers(int turn, IReadOnlyList<TurnEvent> events)
    {
        foreach (var evt in events)
        {
            switch (evt)
            {
                case OrcRepChangedEvent:
                    if (!_triggers.OrcRepChanged)
                    {
                        _triggers.OrcRepChanged = true;
                        _triggers.OrcRepChangeTurn = turn;
                    }
                    break;

                case PossessionEnteredEvent:
                    if (!_triggers.PossessionUsed)
                    {
                        _triggers.PossessionUsed = true;
                        _triggers.PossessionFirstTurn = turn;
                    }
                    break;

                case MuralExaminedEvent:
                    if (!_triggers.MuralRead)
                    {
                        _triggers.MuralRead = true;
                        _triggers.MuralFirstTurn = turn;
                    }
                    break;

                case WeighingDialogueEvent:
                    _triggers.WeighingReached = true;
                    break;

                case VoiceLineEvent v when v.TriggerId.StartsWith("past_sasha", StringComparison.Ordinal):
                    _triggers.PastSashaEncountered = true;
                    break;
            }
        }
        // NOTE: orc_rep_changed / orc_rep_change_turn and weighing_guardian_allied_count
        // are NOT derivable from the current event stream — orc reputation changes emit
        // no TurnEvent, and the Weighing allied count lives in endgame state the bot
        // harness never reaches. Left at defaults. See the stranded-event report in
        // docs/llm-testing/00-overview.md; the rubric thread will wire these when the
        // mechanical-trigger inventory lands.
    }

    private static double Round(double v) => Math.Round(v, 4);

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
            // ValueTuple members are fields, not properties; include them so tuple-typed
            // event fields (e.g. RangedKnockbackEvent.Direction) serialize as data rather
            // than empty objects.
            IncludeFields = true,
            // Verbatim text capture is the whole point: the analyst greps the as-fired
            // strings. The default encoder escapes apostrophes, em dashes, '<', etc. to
            // \uXXXX, which defeats text-pattern checks (e.g. a house-style em-dash rule).
            // Relaxed escaping keeps the text human-readable and greppable. JSONL is still
            // valid JSON; the only unescaped risk (raw newlines) cannot occur since STJ
            // always escapes control characters.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        };
        options.Converters.Add(new TurnEventJsonConverter());
        return options;
    }
}
