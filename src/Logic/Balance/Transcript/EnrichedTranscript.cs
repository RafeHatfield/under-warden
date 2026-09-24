namespace UnderWarden.Logic.Balance.Transcript;

// ─────────────────────────────────────────────────────────────────────────────
// Enriched run transcript — the shared JSONL format consumed by the Analyst
// (Thread 2) and emitted by the LLM Player (Thread 3).
//
// One JSONL file per run: line 1 is the header, then one TurnRecord per turn,
// then a single RunSummary appended last. See
// docs/llm-testing/shared-transcript-schema.md for the authoritative spec.
//
// Property naming: the JSONL is emitted with JsonNamingPolicy.SnakeCaseLower
// (configured in TranscriptRecorder), so C# PascalCase maps to snake_case keys
// (e.g. PlayerHpPct -> "player_hp_pct"). The `record_type` discriminator is
// fixed on each type via a constant property.
//
// Schema version: 1. Bump only on a removed field, a type/semantics change, or
// a new REQUIRED field. Optional nullable additions do not bump.
//
// PROVISIONAL-PENDING-RUBRIC: the mechanical-event capture set (the `events`
// array of each turn) is intentionally open. New TurnEvent subtypes are captured
// automatically by TurnEventJsonConverter — no change here when the rubric's
// silent_failure inventory adds trigger events.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Current schema version. See file header for bump rules.</summary>
public static class TranscriptSchema
{
    public const int Version = 1;
}

/// <summary>Line 1 of every transcript. Identifies the run and its top-level outcome.</summary>
public sealed class TranscriptHeader
{
    public string RecordType => "header";
    public int SchemaVersion => TranscriptSchema.Version;

    public string RunId { get; init; } = "";
    public string Persona { get; init; } = "";

    /// <summary>"bot" | "llm".</summary>
    public string PlayerType { get; init; } = "bot";

    /// <summary>Null for bot runs; model id for LLM Player runs.</summary>
    public string? LlmModel { get; init; }

    public int Seed { get; init; }
    public int DepthReached { get; init; }

    /// <summary>EndingType / outcome string; null if the run was interrupted.</summary>
    public string? Ending { get; init; }

    public int TurnCount { get; init; }
    public int FloorCount { get; init; }

    /// <summary>True iff every TurnRecord carries a full action_taken object.</summary>
    public bool ReplayAvailable { get; init; }
}

/// <summary>
/// The full resolved player action for a turn — the replay prerequisite. Entity
/// references are flattened to deterministic-under-seed IDs plus stable type
/// strings for human readability.
/// </summary>
public sealed class ActionTaken
{
    /// <summary>PlayerAction.ActionKind string.</summary>
    public string Kind { get; init; } = "";

    /// <summary>Entity ID of an attack/move/possess target. Null when not applicable.</summary>
    public int? TargetEntityId { get; init; }

    /// <summary>Species/item TypeId of the target, for readability. Null when not applicable.</summary>
    public string? TargetEntityType { get; init; }

    /// <summary>Destination/target tile (Move, location-targeted spells, throws). Null otherwise.</summary>
    public int? TargetX { get; init; }
    public int? TargetY { get; init; }

    /// <summary>Second target tile — portal exit (Wand of Portals). Null otherwise.</summary>
    public int? TargetX2 { get; init; }
    public int? TargetY2 { get; init; }

    /// <summary>Entity ID of an item used/dropped/thrown/equipped/cast. Null otherwise.</summary>
    public int? ItemEntityId { get; init; }

    /// <summary>Stable item type string ("healing_potion"), for readability. Null otherwise.</summary>
    public string? ItemTypeId { get; init; }

    /// <summary>EquipmentSlot string (UnequipItem). Null otherwise.</summary>
    public string? Slot { get; init; }

    /// <summary>Ability id (UseMonsterAbility). Null otherwise.</summary>
    public string? AbilityId { get; init; }
}

/// <summary>One record per turn. The bulk of a transcript.</summary>
public sealed class TurnRecord
{
    public string RecordType => "turn";

    /// <summary>Monotonic turn number across the whole run (1-based).</summary>
    public int Turn { get; init; }

    /// <summary>Dungeon depth (1-based) the turn occurred on.</summary>
    public int Floor { get; init; }

    /// <summary>Player HP fraction at the START of the turn (0..1).</summary>
    public double PlayerHpPct { get; init; }

    /// <summary>
    /// Count of meaningful actions available to the player this turn. Formula
    /// (documented, rubric may refine): walkable adjacent tiles + adjacent living
    /// monsters + (1 if a healing item is held) + (1 if standing on the down stair).
    /// 0 only in a genuine soft-lock (boxed in, no items, no stair) — the signal the
    /// soft_lock / dead_action_space predicates key on.
    /// </summary>
    public int AvailableActionCount { get; init; }

    // ── Rubric v1 RUNNABLE-NOW predicate fields (config/rubric/v1.yaml) ──
    // These are top-level TurnRecord fields so the single-record predicate evaluator
    // reads them directly. Captured POST-action (the state the turn resolved into).

    /// <summary>Game-over flag after this turn resolved. Pairs with available_action_count
    /// for the `soft_lock` predicate (avail==0 and not is_game_over).</summary>
    public bool IsGameOver { get; init; }

    /// <summary>Total unprovoked cross-faction kills this run so far (increment-only).
    /// Feeds `aggression_tally_negative` (sanity) and the run-level value_reconciliation.</summary>
    public int RunAggressionTally { get; init; }

    /// <summary>True iff a player-initiated PossessionEffect exists in the game this turn —
    /// derived independently of ControlledEntity so `possession_body_inconsistent` is not
    /// circular (effect present but control not transferred -> bug).</summary>
    public bool PossessionActive { get; init; }

    /// <summary>Entity currently driven by player input (the possessed host, or the player).</summary>
    public int ControlledEntityId { get; init; }

    /// <summary>The home-body player entity id (always 0; captured explicitly for the predicate).</summary>
    public int PlayerEntityId { get; init; }

    /// <summary>The full resolved action. Always present for replay-capable runs.</summary>
    public ActionTaken? ActionTaken { get; init; }

    /// <summary>
    /// All TurnEvents that fired this turn, serialized verbatim. Each carries an
    /// `event_type` discriminator (see TurnEventJsonConverter, registered in the
    /// recorder's options). Player-facing text (mural/sign/dialogue/voice ResolvedText)
    /// is embedded here verbatim.
    /// </summary>
    public IReadOnlyList<Core.TurnEvent> Events { get; init; } = Array.Empty<Core.TurnEvent>();

    // ── LLM Player only — null in bot runs ──
    public string? DecisionContext { get; init; }
    public StructuralAssessment? StructuralAssessment { get; init; }
}

/// <summary>
/// Per-turn vitals the harness captures POST-action and hands to the recorder. Groups the
/// scalar TurnRecord fields so the capture call stays readable and new rubric predicate
/// fields can be added here without churning the RecordTurn signature.
/// </summary>
public readonly record struct TurnVitals(
    double PlayerHpPct,
    int AvailableActionCount,
    bool IsGameOver,
    int RunAggressionTally,
    bool PossessionActive,
    int ControlledEntityId,
    int PlayerEntityId);

/// <summary>LLM Player structural self-report for a turn. Vocabulary from the rubric.</summary>
public sealed class StructuralAssessment
{
    public string Judgment { get; init; } = "";
    public string Note { get; init; } = "";
    /// <summary>Monotonic turn number (1-based) at which this assessment was produced.
    /// Null for assessments not associated with a specific turn (e.g., run-end summaries).</summary>
    public int? Turn { get; init; }
}

/// <summary>
/// Which systems were touched this run, and on which turn (null if never). The
/// minimal set is rubric-independent and wired here; the rubric may extend it.
/// </summary>
public sealed class SystemTriggerLog
{
    public bool PossessionUsed { get; set; }
    public int? PossessionFirstTurn { get; set; }
    public bool OrcRepChanged { get; set; }
    public int? OrcRepChangeTurn { get; set; }
    public bool PastSashaEncountered { get; set; }
    public bool MuralRead { get; set; }
    public int? MuralFirstTurn { get; set; }
    public bool WeighingReached { get; set; }
    public int WeighingGuardianAlliedCount { get; set; }
}

/// <summary>A memo delivered this run, captured verbatim for analyst text checks.</summary>
public sealed class MemoRecord
{
    public string Key { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Body { get; init; } = "";
}

/// <summary>Last line of every transcript. Run-level rollup.</summary>
public sealed class RunSummary
{
    public string RecordType => "summary";

    /// <summary>[floor, player_hp_pct at floor entry] pairs, in floor order.</summary>
    public IReadOnlyList<double[]> HpProfile { get; init; } = Array.Empty<double[]>();

    public SystemTriggerLog SystemTriggers { get; init; } = new();

    /// <summary>
    /// Final RunAggressionTally for the run. The `aggression_tally_increment`
    /// value_reconciliation detector reconstructs an expected count from the qualifying-act
    /// events in the turn stream and compares it to this final value — so no per-increment
    /// event clutters the stream (silent-failure-inventory.md, value_reconciliation).
    /// </summary>
    public int RunAggressionTally { get; init; }

    public IReadOnlyList<MemoRecord> MemosDelivered { get; init; } = Array.Empty<MemoRecord>();

    // ── LLM Player only — null/empty in bot runs ──
    public IReadOnlyList<StructuralAssessment> StructuralJudgments { get; init; }
        = Array.Empty<StructuralAssessment>();
    public string? RunNarrative { get; init; }
}
