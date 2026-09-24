# Task: LLM Player Implementation (Thread 3)

Plan source: `docs/llm-testing/plan-player.md`, `docs/llm-testing/00-overview.md`

## Current State

**Status: Phase 4 COMPLETE (2026-06-12). Phase 5 (persona calibration) is next.**

Just done: Extracted hook detection logic into `SignificantEventDetector` (Logic layer,
testable). `LlmBotBrain.OnTurnResolved` now delegates to `_detector.ProcessTurn(...)` via
an `isOrcEntity` resolver closure. 14 new tests in `SignificantEventDetectorTests.cs`.
All 2211 tests pass (0 failed). Logic and Harness builds clean.

Next step: Phase 5 — Persona calibration. Run Reader and SystemExplorer on same seeds,
confirm prompt differences, measure token budgets, tune system prompts.

Open issues:
- `LlmBotBrain.BuildSystemPrompt` is now public static (needed by Program.cs).
  The `Create` factory method still works; the change is backward-compatible.
- `StructuralAssessment` is a class, not a record — `with` expressions don't apply.
  Used explicit copy-construction with `new StructuralAssessment { ... }` everywhere.
- `BotBrain? botBrain` is now nullable in RunSingle (null when llmBrain != null).
  Accessed as `botBrain!.Decide(...)` in the bot-path branch where it is guaranteed non-null.

Implementation deviations from spec:
- Default LLM transcript run count is 1 (not unlimited) when `--player llm`. Spec said
  `runsOverride ?? 10` but for LLM runs 1 is the safer default (API cost). Override with
  `--runs N` as usual.
- `Console.WriteLine` (stdout) used for per-run progress in the LLM branch to match the
  spec's example output. Bot branch keeps `Console.Error.WriteLine` for consistency with
  existing behavior.

---

## Hardening Notes (2026-06-12)

Changes made during the hardening review — read these before building:

1. **`IPlayerBrain.Decide` now returns `PlayerDecision`, not `PlayerAction`.** The old
   signature gave the harness no path to get `reasoning`/`structural_assessment` into
   `TranscriptRecorder.RecordTurn`. The TurnRecord fields already exist
   (`DecisionContext`, `StructuralAssessment` — EnrichedTranscript.cs:154-155); they just
   need plumbing.
2. **Added `OnTurnResolved` hook to `IPlayerBrain`.** The brain never sees
   `turnResult.Events` otherwise — RECENT EVENTS and every Phase-4 event hook
   (first_possession, mural_read, near-death detection across turns) depend on it.
3. **`OnRunEnd` is synchronous and returns the narrative string.** `Finish()` is called
   inside `RunSingle` (DungeonRunHarness.cs:750) and `ToJsonl()` immediately after —
   fire-and-forget would lose the narrative. Death reflection folds into this call
   (there is no Decide call after the death turn; the loop exits on `IsGameOver`).
4. **`GameStateDescriber.Describe` returns `StateDescription { Text, Actions }`.** Each
   menu entry carries a pre-compiled concrete `PlayerAction` (A*-pathed one-step moves
   for "move toward X" entries). Previously there was no binding between the rendered
   menu and executable actions.
5. **LLM path skips the harness floor-clear auto-stair-navigation branch**
   (DungeonRunHarness.cs:462-496). If kept, Reader/Explorer could never examine murals
   after floor clear — defeating both personas. The menu includes a pathed
   "move toward the staircase" entry instead.
6. **System prompt corrected: descend does NOT require a clear floor.**
   `TurnController.ResolveDescend` (TurnController.cs:1514) gates only on dungeon mode +
   standing on the stair. Prompt now states the true rule.
7. **`player_type` = `"llm"`, persona goes in the `persona` header field** ("reader" |
   "system_explorer"). The previous "llm_reader"/"llm_system_explorer" values violated
   the schema (`TranscriptHeader.PlayerType` is documented `"bot" | "llm"`).
8. **Response parsing moved to the Logic layer** (`LlmResponseParser` + `StructuredOutput`
   models, pure System.Text.Json). The test project references Logic + Analyst only — the
   Phase 2 parse/fallback tests were unreachable with parsing in tools/Harness. Only the
   API client wrapper stays in Harness.
9. **`Anthropic.SDK` package name VERIFIED on NuGet** (v5.10.0, tghamm, 2.1M downloads).
   Pin `5.*`. (The official `Anthropic` package v12.x also exists; staying with
   `Anthropic.SDK` as locked — mature, supports prompt caching via cache_control.)
10. **`StructuralAssessment` gains optional `int? Turn`** so
    `RunSummary.StructuralJudgments` carries turn refs as plan-player §6 requires.
    Optional nullable addition — no schema version bump (per EnrichedTranscript.cs bump
    rules).
11. **Floor-summary and reflection answers get dedicated optional output-schema fields**
    (`floor_summary`, `reflection`) — the action/reasoning fields had nowhere to carry
    them. Captured as StructuralAssessment entries with reserved judgment labels.
12. **Mural/species access verified:** `entity.Get<MuralComponent>().Text` exists
    (MuralComponent.cs:7); `Entity.Name` is already the human-readable display name
    (used as killerName in the harness) — the "species pretty-print" deferral is
    removed; use `Name` directly, `SpeciesTag.TypeId` for stable ids.
13. **`max_turns` semantics defined:** per-run LLM API-call budget. When exceeded, the
    brain permanently switches to the fallback bot for the rest of the run (logged once
    as a FallbackEvent with reason "api_budget_exhausted"). **UPDATED (post-build):**
    `LlmMaxTurnsPerFloor = 200` was added as a per-floor hard cap for the LLM path
    (vs `MaxTurnsPerFloor = 1000` for the bot path), along with a stuck-detection ladder
    (warn at 4 turns no-progress via `InjectPromptBlock`, force-descend at 12).
    This supersedes the original "no new mechanism" note — the cost-bounding rationale
    is that 200 turns ≈ $0.09/floor and hybrid mode (API only at decision points) makes
    this ample for real gameplay. Confirmed acceptable by trial run: 3 floors, survived,
    no HitMaxTurns fired.
14. **CLI note:** `--persona` currently validates against bot persona names
    (Program.cs:130). When `--player llm`, validation must accept `reader|system_explorer`
    instead.

---

## Architecture Decisions (locked) <!-- verified -->

**Layer placement:**
- `IPlayerBrain` + `PlayerDecision` → `src/Logic/Balance/LlmPlayer/IPlayerBrain.cs` (Logic layer)
- `GameStateDescriber` + `StateDescription` + `AvailableAction` + `DescriberContext` →
  `src/Logic/Balance/LlmPlayer/GameStateDescriber.cs` (Logic layer, pure C#, no API)
- `LlmPersona` enum → `src/Logic/Balance/LlmPlayer/LlmPersona.cs` (Logic layer)
- `StructuredOutput` + `LlmResponseParser` → `src/Logic/Balance/LlmPlayer/LlmResponseParser.cs`
  (Logic layer — pure System.Text.Json, fully testable from tests/)
- `LlmFallbackEvent` → `src/Logic/Core/TurnEvent.cs` (Logic layer; TurnEventJsonConverter
  serializes new subtypes automatically — the documented open seam)
- `LlmBotBrain` + `AnthropicTurnClient` → `tools/Harness/LlmPlayer/` (Harness project,
  has Anthropic SDK)
- `LlmPlayerConfig` → `tools/Harness/LlmPlayer/LlmPlayerConfig.cs` (Harness project, loaded from YAML)
- Config YAML → `config/llm_player/reader.yaml`, `config/llm_player/system_explorer.yaml`

**IPlayerBrain interface (REVISED — see Hardening Notes 1–3):**
```csharp
namespace UnderWarden.Logic.Balance.LlmPlayer;

/// <summary>One turn's decision plus the LLM metadata the transcript captures.</summary>
public sealed record PlayerDecision(
    PlayerAction Action,
    string? Reasoning,                                  // → TurnRecord.DecisionContext
    Transcript.StructuralAssessment? Assessment,        // → TurnRecord.StructuralAssessment
    bool UsedFallback = false,
    string? FallbackReason = null);                     // → LlmFallbackEvent when UsedFallback

public interface IPlayerBrain
{
    /// <summary>Decide the turn. Must never throw — internal fallback on any failure.</summary>
    PlayerDecision Decide(GameState state);

    /// <summary>Called by the harness after ProcessTurn with the turn's resolved events.
    /// Feeds RECENT EVENTS and the Phase-4 significant-event hook detection.</summary>
    void OnTurnResolved(int turn, IReadOnlyList<TurnEvent> events, GameState state);

    /// <summary>Called at the start of each floor (after Build, before the first Decide).
    /// depth == the new floor. For depth > first floor, the brain queues the
    /// FLOOR {depth-1} COMPLETE summary prompt for the next Decide.</summary>
    void OnFloorEnter(int depth);

    /// <summary>Called once after the run ends, BEFORE TranscriptRecorder.Finish.
    /// Synchronous (own internal timeout, 30s). Returns the end-of-run narrative
    /// (null on failure/timeout — recorded as null, never blocks the run).
    /// When the ending is a death, the death-reflection prompt is part of this call.</summary>
    string? OnRunEnd(string endingLabel);
}
```

**DungeonRunHarness integration (REVISED — see Hardening Notes 3, 5):**
- `RunSingle` gains optional `IPlayerBrain? llmBrain = null` parameter.
- When `llmBrain != null`:
  - The floor-clear/forceDescend auto-stair-navigation branch is SKIPPED — the brain
    decides every turn. (The describer's menu carries a pathed stair-step entry.)
  - `BotBrain`/ForceDescend/AbortRun signals do not apply at the harness level; the
    fallback bot lives INSIDE LlmBotBrain (it maps fallback ForceDescend → pathed step
    toward stair, AbortRun → Wait — the run never aborts from a fallback turn).
  - `enableTelemetry: false` (BotTelemetryRecorder records BotBrain decisions, which
    don't happen on this path; BotSummary stays null).
  - Turn-loop order: `llmBrain.Decide(state)` → `ProcessTurn` → append `LlmFallbackEvent`
    to the events list if `decision.UsedFallback` → `RecordTurn(..., decision.Reasoning,
    decision.Assessment)` → `llmBrain.OnTurnResolved(turn, events, state)`.
  - `OnFloorEnter(depth)` called right after `transcriptRecorder.BeginFloor(...)`.
  - Just before `transcriptRecorder.Finish(...)` (DungeonRunHarness.cs:750):
    `string? narrative = llmBrain.OnRunEnd(outcome)` → passed as `runNarrative`.
- The stuck backstop for the LLM path is `MaxTurnsPerFloor` (1000): a floor that times
  out proceeds to the next floor exactly as today (HitMaxTurns). No new mechanism.
- `TurnRecord.available_action_count` keeps the EXISTING harness formula
  (`ComputeAvailableActionCount`, DungeonRunHarness.cs:851) for predicate consistency
  across bot and LLM runs. The LLM's forced_move judgment keys on the MENU length —
  these are different numbers by design; do not unify them.
- New public entry point (mirrors `RunWithTranscript`, DungeonRunHarness.cs:274):
```csharp
public (DungeonSoakRunResult Result, string Jsonl) RunWithLlmPlayer(
    int floors, int seed, IPlayerBrain brain, string personaName, string llmModel,
    VoiceLineRegistry? voiceRegistry = null, MemoRegistry? memoRegistry = null)
// constructs TranscriptRecorder(seed, personaName, voiceRegistry,
//   playerType: "llm", llmModel: llmModel) and calls RunSingle(llmBrain: brain, ...)
```

**Anthropic SDK:** <!-- verified -->
- Add `Anthropic.SDK` (pin `5.*`) to `tools/Harness/Harness.csproj` ONLY. Verified on
  NuGet: v5.10.0, github.com/tghamm/Anthropic.SDK.
- API key via environment variable `ANTHROPIC_API_KEY`. Missing key = fatal at startup
  with a clear error (NOT a per-turn fallback storm).
- Model: `claude-haiku-4-5-20251001` (configurable in YAML).
- Plain message completion + JSON-in-text parsing (no tool-use). `max_tokens: 400`
  per turn (end-of-run call: 800). Parser strips markdown code fences if present.
- System prompt sent with `cache_control: ephemeral` (Anthropic.SDK 5.x supports it) —
  this is the ~2650-token cacheable block from plan-player §4.
- Timeout: 10s per turn; on failure → internal BotBrain fallback decision
  (`UsedFallback = true`).

**Structured output schema (REVISED — added floor_summary/reflection carriers):**
```jsonc
{
  "action_index": 3,
  "action_label": "Move north toward the staircase",
  "reasoning": "The orc is already dead; the stair is my best path forward.",
  "structural_assessment": {            // null when not warranted (most turns)
    "judgment": "forced_move",
    "note": "Only the move action was available; combat was resolved."
  },
  "floor_summary": null,                // string; non-null ONLY on turns whose prompt
                                        // included a FLOOR COMPLETE block
  "reflection": null                    // string; non-null ONLY on turns whose prompt
                                        // included a REFLECTION block
}
```
Capture rules (TranscriptRecorder accumulates into `RunSummary.StructuralJudgments`):
- `structural_assessment` → `StructuralAssessment { Judgment, Note, Turn }` (also set on
  the TurnRecord).
- `floor_summary` → `StructuralAssessment { Judgment = "floor_summary", Note = <text>, Turn }`.
- `reflection` → `StructuralAssessment { Judgment = "reflection:<hook_name>", Note = <text>, Turn }`
  (hook name supplied by LlmBotBrain, which knows which hook it appended).

**Mural text access:** <!-- verified --> `state.Features` → `entity.Get<MuralComponent>()?.Text`
(MuralComponent.cs:7, `Text` is `string`, non-null). Signposts:
`entity.Get<SignpostComponent>()`. Direct — no registry needed.

**Monster display names:** <!-- verified --> `Entity.Name` IS the human-readable name
(the harness already uses it for killerName). Use it directly in THREATS. Stable type id
when needed: `entity.Get<SpeciesTag>()?.TypeId` (same pattern as ActionTakenBuilder).

**Per-floor hook timing:** `LlmBotBrain` tracks `_currentDepth`. When `OnFloorEnter(depth)`
is called with depth > last, it queues the FLOOR {depth-1} COMPLETE prompt to prepend to
the NEXT `Decide`. First floor of the run queues nothing. This matches plan-player §9's
"pre-turn hook site" requirement without a one-turn-delay mechanism in the harness.

---

## Phases

### Phase 1 — IPlayerBrain + GameStateDescriber ✅ COMPLETE

**Files created:**
- `src/Logic/Balance/LlmPlayer/LlmPersona.cs` ✅
- `src/Logic/Balance/LlmPlayer/IPlayerBrain.cs` ✅ (PlayerDecision record + interface)
- `src/Logic/Balance/LlmPlayer/GameStateDescriber.cs` ✅ (AvailableAction, StateDescription, DescriberContext, Describe, SummarizeEvents)
- `tests/Balance/GameStateDescriberTests.cs` ✅ (11 tests)

**Implementation notes:**
- PlayerDecision.ExtraJudgments added per hardening note 11 (floor_summary/reflection carriers) — it's empty by default and costs nothing on the Phase 1 path.
- SummarizeEvents pattern-matches on concrete TurnEvent subtypes; unrecognized types return null and are silently skipped (no clutter, no crash).
- Features block for Reader uses `[dir] wall` for murals (e.g. "MURAL (east wall)"), not a compass prefix — matches the template spec format.
- Direction helper correctly returns "nearby" for (0,0) — only fires if player is on the same tile as an entity (edge case in practice).
- Token budget test uses 3400-char proxy for 850 tokens on empty floor; actual is well under (~1200 chars on empty floor).
- Test for `OnStair_YieldsDescendEntry` mutates the stair entity X/Y directly (Entity fields are mutable) — clean, no harness needed.

**GameStateDescriber signature (REVISED — menu must bind to concrete actions):**
```csharp
public sealed record AvailableAction(string Label, PlayerAction Action);

public sealed record StateDescription(string Text, IReadOnlyList<AvailableAction> Actions);

/// <summary>Brain-held state the describer can't derive from GameState.</summary>
public sealed record DescriberContext(
    IReadOnlyList<string> RecentEventLines,   // last 3 one-line summaries, oldest first
    string? PendingPromptBlock);              // FLOOR COMPLETE / REFLECTION block, or null

public static StateDescription Describe(GameState state, LlmPersona persona, DescriberContext ctx)
```
The brain owns RecentEventLines (built in `OnTurnResolved`) and PendingPromptBlock
(queued by hooks). Phase 1 tests pass canned values. Event-line summarization itself is
a Phase-1 helper: `GameStateDescriber.SummarizeEvents(IReadOnlyList<TurnEvent>) → string`
(1 line, e.g. "You hit Orc Brute for 6; Orc Brute hit you for 4").

Template structure (see plan-player.md §2):
```
=== TURN {N} | FLOOR {D} | {HP}/{MAX_HP} HP ({HP_PCT}%) ===

SITUATION
{1-2 sentence spatial summary}

THREATS  (nearest 5, sorted by threat level)
- {monster.Name}: {distance} tiles {direction}, {threat_level: low/med/high}

ITEMS ON FLOOR  (nearest 3)
- {item.Name} at {distance} tiles {direction}

FEATURES
{persona-aware: Reader gets verbatim mural text; Explorer gets "Mural: Ancient inscription (readable)"}

RECENT EVENTS (last 3 turns)
- T{N-2}: {ctx.RecentEventLines}
- T{N-1}: ...

{ctx.PendingPromptBlock — FLOOR COMPLETE / REFLECTION, omit when null}

AVAILABLE ACTIONS
1. {action label}
...

{PERSONA INSTRUCTION — see plan §3}
```

**Computing available actions (the menu — every entry pre-compiled to a PlayerAction):**

There is no `TurnController.ComputeAvailableActions`; the describer builds the menu.
Verified action surface (PlayerAction.cs factories + TurnController gates):
- `Attack(monster)` — one entry per ADJACENT (Chebyshev ≤1) living monster:
  "Attack {Name} (adjacent, {hp state})"
- `MoveTo(x, y)` via one A* step (`Pathfinder.AStar`, same call shape as the harness
  stair-nav, canPassDoors: true) — semantic move entries, NOT 8 raw directions:
  - "Move toward {nearest monster Name}" (when any living monster, not adjacent)
  - "Move toward the staircase" (when `state.StairDown != null` and not on it)
  - "Move toward {item.Name}" (nearest floor item, ≤10 tiles)
  - "Move toward the {mural/sign/chest}" (nearest unexamined feature, ≤10 tiles;
    bump-interaction = MoveTo the feature tile when adjacent)
  - If a path target is unreachable (A* null), omit that entry.
- `UseItem(potion)` — "Drink a healing potion ({count} left, you are at {hp_pct}%)"
  when inventory holds one.
- `Descend` — "Descend the staircase{`, monsters remain on this floor` when not clear}"
  when `state.PlayerOnStairDown` (engine rule — floor clear NOT required; verified
  TurnController.ResolveDescend:1514).
- `Wait` — always last: "Wait (do nothing)".
Deferred from the menu in v1 (explicitly NOT offered; revisit with possession scenarios):
Possess/ExitPossession, ThrowItem, CastSpell, Equip/Unequip, RangedAttack. Mark TODO.

Index from 1. The brain executes `Actions[action_index - 1].Action` verbatim.

**SITUATION block:**
- Visible-enemy count from `state.AliveMonsters` within 10 tiles (distance proxy for
  LOS in v1 — see deferred), nearest stair distance, whether floor is clear
  (`state.IsFloorClear`).

**THREATS:**
- Distance = Chebyshev from player. Threat level: high = adjacent (≤1), med = ≤4, low = >4.
- Display `monster.Name` directly (verified — no lookup needed).
- Filter to within 10 tiles (LOS deferred).

**Direction helper:** 8-directional from (dx,dy) → "north", "northeast", etc.

**Token budget tests:**
- Base description < 850 tokens for both personas
- Reader surcharge (with mural) ≤ 500 tokens for mural block, total ≤ 1300
- Use character count approximation: 4 chars ≈ 1 token
- Verbatim mural blocks hard-truncated at 500 tokens (2000 chars) with `[...]`

**Deferred (mark TODO in code):**
- Verbatim Hollowmark voice lines in CONTEXT block — voice lines are possession-gated;
  bots never fire them; the v1 menu doesn't offer Possess, so no Hollowmark lines can
  fire. Wire CONTEXT when the menu grows possession entries.
- LOS computation (simplified: 10-tile distance threshold instead of true FOV for now)
- Possession/throw/equip/wand menu entries (see menu list above)

**Tests:**
- `Describe` for empty floor returns valid StateDescription with all sections; Actions
  contains at least Wait
- `Describe` with monsters lists threats sorted by distance; adjacent monster yields an
  Attack entry whose `Action.Kind == Attack` and target matches
- "Move toward" entries hold a one-step MoveTo whose destination is adjacent to the player
- Reader persona with mural entity shows verbatim `MuralComponent.Text`
- Explorer persona with mural entity shows compressed form (and NOT the verbatim text)
- On-stair state yields a Descend entry; off-stair does not
- ctx.PendingPromptBlock is rendered when non-null, absent when null
- Token budget within limits (character count proxy); 500-token mural truncation fires

---

### Phase 2 — Response parsing (Logic) + LlmBotBrain core (Harness) ✅ COMPLETE (2026-06-12)

**Files to create:**
- `src/Logic/Balance/LlmPlayer/LlmResponseParser.cs` — `StructuredOutput` model +
  `TryParse(string raw, int actionCount, out StructuredOutput result, out string error)`.
  Pure System.Text.Json. Strips code fences. Validates: action_index in [1, actionCount];
  structural_assessment.judgment ∈ {dead_action_space, forced_move, novel_encounter,
  system_unreachable} when non-null (invalid judgment = parse failure → fallback).
- `src/Logic/Core/TurnEvent.cs` — add `LlmFallbackEvent : TurnEvent { string Reason }`
  (serialized automatically by TurnEventJsonConverter).
- `tools/Harness/LlmPlayer/AnthropicTurnClient.cs` — thin SDK wrapper: system prompt
  (with cache_control), user message in, raw text out, 10s timeout, never throws
  (returns null + error string).
- `tools/Harness/LlmPlayer/LlmBotBrain.cs`
- `tools/Harness/LlmPlayer/LlmPlayerConfig.cs`
- `config/llm_player/reader.yaml`, `config/llm_player/system_explorer.yaml`
- `tests/Balance/LlmResponseParserTests.cs` (Logic-layer — reachable from the test
  project, which references Logic only; do NOT add a Harness project reference)

**Modify:**
- `tools/Harness/Harness.csproj` — add `Anthropic.SDK` package (pin `5.*`)

**LlmBotBrain responsibilities:**
- Implement `IPlayerBrain`.
- `Decide(state)`:
  1. Build `DescriberContext` (recent-event lines from its ring buffer; pending
     floor-summary/reflection block, then clear it).
  2. `GameStateDescriber.Describe(state, persona, ctx)` → StateDescription.
  3. If API budget (`max_turns`) exhausted → fallback (reason "api_budget_exhausted",
     logged once at WARN, every subsequent turn silently fallback with same reason).
  4. Call `AnthropicTurnClient`; on null/timeout → fallback (reason "api_error"/"timeout").
  5. `LlmResponseParser.TryParse(raw, Actions.Count, ...)`; on failure → fallback
     (reason "parse_error: ...").
  6. Success → `PlayerDecision(Actions[idx-1].Action, reasoning, assessment)`.
     If the turn's prompt carried a hook block, lift `floor_summary`/`reflection` into
     the assessment-capture path (see schema capture rules above).
- Fallback decision: internal `BotBrain` instance (persona from `fallback_persona`
  YAML key) — `BotBrain.Decide(state.Player, state.PlayerFighter, state.PlayerInventory,
  state.Monsters, state.Map, floorItems: state.FloorItems)` →
  `BotActionConverter.ToPlayerActionWithPathing(botAction, state)`. Map fallback
  BotAction.ForceDescend → one A* step toward stair (or Wait); AbortRun → Wait. The LLM
  path never aborts a run.
- `OnTurnResolved`: push `GameStateDescriber.SummarizeEvents(events)` into the 3-entry
  ring buffer; run Phase-4 hook detection (Phase 4 wires the prompts; the detection
  plumbing lands here).
- `OnFloorEnter(depth)`: queue floor-summary block when depth > last seen; update depth.
- `OnRunEnd(ending)`: one synchronous API call (30s timeout), returns narrative or null.
  Includes the death reflection when the ending is a death (see Hardening Note 3).

**System prompt structure (static, cacheable ~2650 tokens) — CORRECTED descend rule:**
```
You are playing a roguelike dungeon game. Your goal is to survive as long as possible
while [persona instruction].

GAME RULES:
- Turn-based: one action per turn, chosen from the numbered AVAILABLE ACTIONS list
- Combat: attacking an adjacent monster is a melee strike
- Healing: drink potions from inventory
- Progression: descend the staircase when standing on it. You MAY descend with monsters
  still alive on the floor; clearing the floor first is usually safer but is a choice,
  not a rule.
- Death is permanent; no save/reload

STRUCTURAL JUDGMENT VOCABULARY:
dead_action_space: all available options had equivalent/arbitrary outcomes
forced_move: only one meaningful action existed
novel_encounter: first time interacting with this entity or mechanic
system_unreachable: mechanic was present but had no accessible entry point

OUTPUT FORMAT (JSON only, no other text):
{
  "action_index": <integer, 1-based index from AVAILABLE ACTIONS>,
  "action_label": <string, brief description of what you're doing>,
  "reasoning": <string, 1-2 sentences explaining your choice>,
  "structural_assessment": <null OR {"judgment": "<vocabulary term>", "note": "<brief explanation>"}>,
  "floor_summary": <null; when the prompt contains a FLOOR ... COMPLETE block, answer it here as a string>,
  "reflection": <null; when the prompt contains a REFLECTION block, answer it here as a string>
}

Produce structural_assessment only when:
- Only one action was available (forced_move)
- All available actions had equivalent expected outcomes (dead_action_space)
- You encountered an entity or mechanic for the first time (novel_encounter)
- A mechanic was present but you had no clear way to interact with it (system_unreachable)
Otherwise, structural_assessment is null.

[PERSONA INSTRUCTION — varies by persona, verbatim from plan-player.md §3]
```

**Config YAML (reader.yaml):**
```yaml
persona: reader
model: claude-haiku-4-5-20251001
max_turns: 1500            # per-run LLM API-call budget; exceeded → permanent bot fallback
fallback_persona: balanced
structural_assessment_threshold: 1
significant_event_hooks:
  - near_death
  - first_possession
  - death                  # handled inside OnRunEnd, not as a per-turn prompt
  - first_orc_interaction
  - mural_read
```
YAML loading: manual key=value parse or YamlDotNet — Harness project already uses
YamlDotNet transitively via Logic loaders; use the existing Logic-side loader patterns.

**Tests (all against `LlmResponseParser` in tests/Balance — no Harness reference):**
- Parse valid JSON → StructuredOutput with correct index/reasoning
- Parse response wrapped in ```json fences → succeeds
- Parse response with structural_assessment → non-null, judgment validated
- structural_assessment with out-of-vocabulary judgment → TryParse false
- Malformed JSON → TryParse false with error
- action_index 0 / actionCount+1 → TryParse false
- floor_summary / reflection round-trip when present

LlmBotBrain itself (API orchestration) gets no unit tests — it is verified by the
Phase 3 end-to-end run. Keep it thin enough that this is acceptable.

**Implementation notes (2026-06-12):**
- 12 new tests in `tests/Balance/LlmResponseParserTests.cs` (10 required + 2 bonus
  coverage: all-valid-judgments sweep and boundary action_index==actionCount).
- `LlmResponseParser` uses `[JsonPropertyName]` attributes for snake_case mapping
  (explicit, not relying on `JsonNamingPolicy` — avoids ambiguity with CI parsing options).
- `LlmFallbackEvent` appended to `src/Logic/Core/TurnEvent.cs` — TurnEventJsonConverter
  picks it up automatically via reflection; no registration needed.
- `AnthropicTurnClient` sends system prompt with `CacheControl { Type = CacheControlType.ephemeral }`
  using `PromptCacheType.FineGrained` mode (manual cache-point control). API key
  validated at constructor time with a clear `InvalidOperationException`.
- `LlmBotBrain.BuildSystemPrompt` uses string concatenation (not raw string literal) to
  avoid C# raw-string `{{` escaping conflicts with the JSON example block in the prompt.
- `LlmPlayerConfig` uses manual key:value line parsing — no additional YAML library
  dependency needed in the Harness startup path.
- Phase 4 hook prompts are verbatim from plan-player §5 and wired in Phase 2 already
  (near_death, first_possession, first_orc, mural_read) — Phase 4 is purely additive.
- `Anthropic.SDK` pinned to `5.*` in Harness.csproj.

**Files created:**
- `src/Logic/Core/TurnEvent.cs` — added `LlmFallbackEvent`
- `src/Logic/Balance/LlmPlayer/LlmResponseParser.cs` — `StructuredOutput`, `StructuredOutputAssessment`, `LlmResponseParser.TryParse`
- `tests/Balance/LlmResponseParserTests.cs` — 12 tests
- `tools/Harness/LlmPlayer/AnthropicTurnClient.cs`
- `tools/Harness/LlmPlayer/LlmBotBrain.cs`
- `tools/Harness/LlmPlayer/LlmPlayerConfig.cs`
- `config/llm_player/reader.yaml`
- `config/llm_player/system_explorer.yaml`

**Files modified:**
- `tools/Harness/Harness.csproj` — added `Anthropic.SDK 5.*` package reference

---

### Phase 3 — Harness Integration ✅ COMPLETE (2026-06-12)

**Files modified:**
- `src/Logic/Balance/Transcript/EnrichedTranscript.cs` — added `int? Turn` to `StructuralAssessment`
- `src/Logic/Balance/Transcript/TranscriptRecorder.cs` — playerType/llmModel constructor params,
  optional decisionContext/structuralAssessment on RecordTurn, AddStructuralJudgment method,
  optional runNarrative on Finish, _judgments accumulator
- `src/Logic/Balance/DungeonRunHarness.cs` — IPlayerBrain? llmBrain on RunSingle, RunWithLlmPlayer
  public method, full LLM path through turn loop (skip auto-stair, OnFloorEnter, fallback event
  injection, transcript wiring, OnTurnResolved, OnRunEnd-before-Finish)
- `tools/Harness/LlmPlayer/LlmBotBrain.cs` — BuildSystemPrompt made public static
- `tools/Harness/Program.cs` — --player bot|llm flag, LLM branch in --llm-transcript section,
  persona validation bypass for llm mode, updated help text

**Modify (spec reference):**
- `src/Logic/Balance/DungeonRunHarness.cs` — add `IPlayerBrain? llmBrain` param to
  `RunSingle`; add `RunWithLlmPlayer` public method (see Architecture Decisions for the
  exact integration contract: skip auto-stair branch, turn-loop order, OnRunEnd-before-
  Finish, enableTelemetry: false)
- `src/Logic/Balance/Transcript/TranscriptRecorder.cs` — see below
- `src/Logic/Balance/Transcript/EnrichedTranscript.cs` — add `int? Turn` to
  `StructuralAssessment` (optional nullable — NO schema version bump)
- `tools/Harness/Program.cs` — add `--player llm` flag; when set, `--persona` accepts
  `reader|system_explorer` (NOT the bot persona list — branch the validation at
  Program.cs:127-130); requires `--llm-transcript <dir>`; requires `ANTHROPIC_API_KEY`
  (fatal error otherwise); builds a FRESH harness per run (same as the existing
  --llm-transcript loop at Program.cs:456-472 — replay fidelity depends on it)

**TranscriptRecorder changes (concrete):** <!-- verified against current source -->
- Constructor gains `string playerType = "bot", string? llmModel = null`; stored and
  emitted in the header (replacing the hardcoded `PlayerType = "bot"`, `LlmModel = null`
  at TranscriptRecorder.cs:100-101). Values: `"llm"` for the player (`persona` field
  carries "reader"/"system_explorer").
- `RecordTurn` gains optional `string? decisionContext = null,
  StructuralAssessment? structuralAssessment = null` → set on the TurnRecord
  (`DecisionContext`/`StructuralAssessment` fields already exist, EnrichedTranscript.cs:154-155).
  Non-null assessments (with `Turn` set) are also accumulated into a private list.
- New method `AddStructuralJudgment(StructuralAssessment a)` for floor_summary/reflection
  entries that the brain lifts out of responses (harness calls it when the decision
  carries them — simplest plumbing: include them on PlayerDecision via the Assessment
  path? NO — keep PlayerDecision as specced; LlmBotBrain returns floor_summary/reflection
  folded into `PlayerDecision.Assessment` is WRONG (a turn can have both). Instead:
  `PlayerDecision` gains `IReadOnlyList<StructuralAssessment> ExtraJudgments` (default
  empty) and the harness forwards each to `AddStructuralJudgment`.)
- `Finish` gains optional `string? runNarrative = null` → `RunSummary.RunNarrative`;
  `RunSummary.StructuralJudgments` built from the accumulated list.
- Bot path passes nothing new — zero behavior change for existing callers (existing
  transcript tests must stay green unmodified).

**CLI:**
```
dotnet run --project tools/Harness -- --dungeon --llm-transcript <dir> \
    --player llm --persona reader --floors 10 --runs 1 --seed 1337
```

**Validate:**
- Complete single run (seed 1337, 5 floors) — doesn't crash; cost sanity-check the
  token usage line
- Transcript round-trips through Analyst schema validation
  (`dotnet run --project tools/Analyst -- --transcript <jsonl> --rubric config/rubric/v1.yaml`)
- Header: `player_type` == "llm", `persona` == "reader", `llm_model` set
- `decision_context` non-null on LLM-decided turns; `LlmFallbackEvent` appears in the
  event stream on injected-failure turns
- `structural_judgments` in RunSummary carries turn refs
- Replay: action sequence from transcript → same floor state at turn 50 (fresh harness,
  same seed — the established invariant)
- Bot-path regression: existing `EnrichedTranscriptTests` green, byte-identical bot
  transcript for seed 1337

---

### Phase 4 — Self-Assessment Hooks ✅ COMPLETE (2026-06-12)

Hook detection extracted to `SignificantEventDetector` (Logic layer). `LlmBotBrain`
delegates via an `isOrcEntity: Func<int, bool>` closure over `state.AliveMonsters`.
Prompt texts verbatim from plan-player.md §5.

**Files created:**
- `src/Logic/Balance/LlmPlayer/SignificantEventDetector.cs` — `HookFired` record + `SignificantEventDetector` class
- `tests/Balance/SignificantEventDetectorTests.cs` — 14 tests covering all hooks

**Files modified:**
- `tools/Harness/LlmPlayer/LlmBotBrain.cs` — removed inline hook detection fields; added
  `_detector` field + single `_detector.ProcessTurn(...)` call in `OnTurnResolved`

**Implementation notes:**
- `ProcessTurn` takes a `Func<int, bool> isOrcEntity` resolver instead of `GameState` —
  keeps the Logic layer free of game-state coupling while remaining fully testable.
- LlmBotBrain queues only the first hook per turn (v1 constraint: one block per turn).
  Additional simultaneous hooks are dropped silently; documented in code.
- Return order (priority): near_death > first_orc_interaction > mural_read > first_possession.
  This matches the task spec.
- Re-arm check is performed BEFORE threshold check within the same ProcessTurn call, so
  recovery and re-fire cannot happen in the same turn (key hysteresis invariant).
- All 2211 tests green; both Logic and Harness builds clean.

---

### Phase 5 — Persona Calibration ⬜ NOT STARTED

**Validation:**
- Run Reader and SystemExplorer on same 3 seeds (1337, 1338, 1339), 10 floors
- Confirm: Reader transcripts contain verbatim mural text in prompts (spot-check via a
  `--dump-prompts <dir>` debug flag on the Harness — add it in this phase); Explorer
  receives compressed form
- Confirm: transcripts differ in structural_judgment distribution
- Tune system prompts if output is undesirable (prompt text lives in LlmBotBrain /
  config — no Logic changes expected)

**Token budget measurement:**
- Log actual token counts per turn (API response usage field) to stderr summary
- Verify Reader surcharge on mural floors is within 100-400 token estimate
- Flag if any turn exceeds 1300 tokens total input (non-cached)
- Verify cache hits: cached input tokens should dominate after turn 1

---

## Deferred Items (follow-up, not blocking)

- **True LOS computation** — use 10-tile distance threshold for now; real FOV later
- **Verbatim Hollowmark lines in CONTEXT block** — possession-gated; v1 menu has no
  Possess entry so they cannot fire; wire CONTEXT + Possess menu entries together
- **Possession / throw / equip / wand / ranged menu entries** — v1 menu is
  move/attack/potion/descend/examine/wait; System Explorer's reach grows with the menu
  (note: until then, expect `system_unreachable` judgments from the Explorer about
  these — that is correct output, not a bug)
- **Auto-travel macro** ("travel to staircase" expanding to multiple turns without API
  calls) — cost optimization, measure Phase 5 cost first
- **Per-turn memo delivery + MemoDeliveredEvent** — deferred per overview doc;
  RunSummary.memos_delivered is sufficient for now
- **Trigger_consequence spine** — needs open contracts (geas, past-Sasha, Assembly,
  Debt); not part of this task

## Resolved (was deferred/open)

- ~~Species pretty-print~~ — `Entity.Name` is already the display name; use it directly
- ~~Anthropic SDK package name~~ — `Anthropic.SDK` verified on NuGet (v5.10.0)
- ~~Mural text availability (plan-player §9)~~ — confirmed direct via MuralComponent.Text
- ~~Per-floor hook timing (plan-player §9)~~ — OnFloorEnter + queued block; no harness
  delay mechanism needed
- ~~player_type value~~ — "llm" per schema; persona field disambiguates

---

## Do NOT

- Touch the rubric (config/rubric/v1.yaml) — it's locked
- Implement trigger_consequence evaluator
- Add NativeAOT annotations (Harness project has PublishTrimmed=false)
- Add Anthropic SDK to `src/Logic/UnderWarden.Logic.csproj` — Harness project only.
  The Logic layer must build and test with zero network/SDK dependencies.
- Add a tools/Harness ProjectReference to the test project — parsing/detection logic
  that needs tests belongs in `src/Logic/Balance/LlmPlayer/`
- Change `ComputeAvailableActionCount` or its TurnRecord field semantics — rubric
  predicates depend on it being identical across bot and LLM runs
- Reuse a harness instance across LLM runs — fresh harness per run (replay fidelity;
  see Program.cs:459-464 comment)
- Use "fair" anywhere in prompt text or assessment vocabulary
- Bump the transcript schema version — every addition here is optional-nullable
