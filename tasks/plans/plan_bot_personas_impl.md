# Feature: Bot Personas + Graphical Bot Mode

## Status: needs-review

## Current State
- TASK-001 through TASK-011 complete. 1873 tests passing (was 1838).
- TASK-012 (manual smoke test) pending — requires Godot editor.
- Build tracking file: `tasks/bot_personas_build.md`
- Key behavioral change: panic heal now requires 2+ adjacent enemies (PoC-correct). Balance baseline updated.
- Full 5×15 matrix report at `reports/bot_survivability_v1.md`.
- Next: reviewer should check BotBrain engagement distance behavior, panic heal alignment to PoC, and verify TASK-012 smoke test when Godot is available.

---

## Overview

Two related but distinct capabilities, shipped in one plan:

1. **Headless persona system.** Generalise `BotBrain` so the harness can drive scenarios and dungeon soak with five named playstyles (`balanced`, `cautious`, `aggressive`, `greedy`, `speedrunner`). Each persona is a `BotPersonaConfig` record loaded from `config/bot_personas.yaml`. Persona is selectable via `--persona <name>` on `harness --scenario` and `harness --dungeon`, and via the existing `player_bot` field on scenario YAML.

2. **Graphical bot mode in Godot.** Hand control of the active game to `BotBrain` while the Godot scene is running. Toggleable from a debug-build hotkey or debug menu. Bot decisions go through the same `PlayerAction` → `TurnController.ProcessTurn(...)` pipeline as human taps — they enter `GameController` exactly where the human input does. Configurable turn delay so a human can watch the bot play in real time. HUD indicator shows persona and active state. Human input is fully restored when bot mode is toggled off.

3. **Survivability report.** A `harness --bot-report` command that runs the acceptance matrix (15 scenarios) with each persona, producing a 5×15 survival-rate table and a markdown report — port of `~/development/rlike/tools/bot_survivability_report.py`.

The architectural boundary holds: all decision logic lives in the logic layer (`src/Logic/Balance/BotBrain.cs`). The presentation layer's contribution is a `BotPlayerDriver` Godot node that ticks the bot on a timer and feeds the resulting `PlayerAction` into the existing controller. Zero new game rules in `src/Presentation/`.

---

## Reference

- **PoC bot brain (canonical):** `~/development/rlike/io_layer/bot_brain.py` — 2295 lines. The `PERSONAS` and `PERSONA_HEAL_CONFIG` dictionaries at lines 80-217 are the source of truth for persona thresholds. The decide_action pipeline (lines 326-893) shows the strict priority order: panic heal → threshold heal → retreat → floor-complete handling → adjacency override → state machine.
- **PoC graphical bot wiring:** `~/development/rlike/io_layer/bot_input.py` — `BotInputSource` implements the `InputSource` protocol. It is consumed by the engine input loop the same way keyboard input is. Key idea: bot is a polymorphic input source, not a parallel game loop. It returns an `ActionDict` per frame; the engine processes one action per turn. This is exactly the abstraction we want in the C# side, but it needs to be adapted to Godot's event-driven controller rather than a per-frame poll.
- **PoC telemetry:** `~/development/rlike/io_layer/bot_metrics.py` — `BotDecisionTelemetry` and `BotMetricsRecorder`. Our C# `BotDecisionRecord` and `IBotTelemetryRecorder` already cover the same surface; persona name is the missing field.
- **PoC survivability tool:** `~/development/rlike/tools/bot_survivability_report.py` — reads soak JSONL, computes per-scenario death counts, HP-at-heal percentiles, deaths-with-unused-potions. Output is markdown. Our `--bot-report` extends this with the persona dimension (the PoC tool runs one persona at a time).
- **Current C# bot:** `src/Logic/Balance/BotBrain.cs` is a static class with hardcoded `balanced` behavior. `BotConfig.HealThreshold` (0.30) and `BotConfig.PanicThreshold` (0.15) are global constants in `ScenarioHarness.cs`.
- **Current C# telemetry:** `src/Logic/Balance/BotTelemetry.cs` — already records per-decision context. Needs a persona field on the record and on the run summary.
- **Current dispatch:** `ScenarioHarness.cs:114-124` — single conditional: `if (scenario.Player.PlayerBot == "ranged_net_arrow") use RangedNetArrowBot else use BotBrain`. Extension point for persona selection.
- **Acceptance matrix:** `tools/Harness/SuiteRunner.cs:36-71` — `Matrix` is the 15-scenario list. `--bot-report` reuses this.
- **Scenarios already using `player_bot`:** `config/levels/scenario_ranged_viability_arena.yaml`, `scenario_ranged_adjacent_punish_arena.yaml`, `scenario_ranged_max_range_denial_arena.yaml`, `scenario_skirmisher_vs_ranged_net_identity.yaml` — all set `player_bot: "ranged_net_arrow"`. Plan must preserve these.

### Persona thresholds (PoC-verified — copy these verbatim into YAML)

From `bot_brain.py:80-127` (`BotPersonaConfig`) and `bot_brain.py:186-217` (`PERSONA_HEAL_CONFIG`). One source of truth per persona — engagement/loot from the first table, healing from the second:

| Persona | retreat_hp | base_heal | panic_hp | panic_enemies | combat_engage | loot_priority | prefer_stairs | avoid_combat | combat_healing |
|---------|-----------|-----------|----------|---------------|---------------|---------------|---------------|--------------|----------------|
| balanced     | 0.25 | 0.30 | 0.15 | 2 | 8  | 1 | false | false | true |
| cautious     | 0.40 | 0.50 | 0.30 | 2 | 5  | 1 | false | true  | true |
| aggressive   | 0.10 | 0.20 | 0.10 | 3 | 12 | 0 | false | false | true |
| greedy       | 0.25 | 0.30 | 0.15 | 2 | 6  | 2 | false | false | true |
| speedrunner  | 0.30 | 0.40 | 0.20 | 2 | 4  | 0 | true  | true  | true |

Note: the PoC's `BotPersonaConfig.potion_hp_threshold` is documented as DEPRECATED in favor of `PERSONA_HEAL_CONFIG.base_heal_threshold` (see bot_brain.py:71). C# implementation collapses these into one field per persona to avoid the PoC's vestigial duplication.

### STUCK detection thresholds (PoC-verified)

From `bot_brain.py:33`: `STUCK_THRESHOLD = 8`. The C# current bot has no stuck detection at all. The PoC behavior:
- In COMBAT, if player and target position both unchanged AND the action is not an attack: increment `_stuck_counter`.
- At `_stuck_counter >= 8`: drop the target, fall back to EXPLORE, set "do-not-re-target" flag.
- Plus a movement-blocked path (`_movement_blocked_count >= 3` triggers `bot_abort_run`).
- Plus oscillation detection: deque of last 6 positions; if pattern A↔B repeats 4+ times, drop to EXPLORE.

The instructions specify "8 consecutive turns → random movement; 15 → abort scenario." That is a simplification of the PoC's logic that combines stuck-on-target (8 turns) with a hard abort threshold (15 — not present in the PoC, but implied by user spec). Plan tasks the simpler version because our scenarios are arenas (no exploration), so most PoC stuck cases (autoexplore blocked, stairs-walking stuck) do not apply. The dungeon harness already has `MaxTurnsPerFloor = 1000` as a hard ceiling — that is the equivalent of the PoC's `bot_abort_run`.

---

## Architecture

### Logic layer

```
src/Logic/Balance/
  BotBrain.cs               — refactor: static methods become instance methods on BotBrain class.
                              Decide(...) takes a BotPersonaConfig (default = Personas.Balanced).
  BotPersonaConfig.cs       — NEW: record with all persona thresholds.
  BotPersonaRegistry.cs     — NEW: loads config/bot_personas.yaml, exposes Get(name) and Defaults.
  BotConfig.cs              — refactor: keep HealThreshold/PanicThreshold as the `balanced` defaults
                              so existing call-sites compile, but delegate to Personas.Balanced.
  ScenarioHarness.cs        — accept optional persona name; default `balanced`.
  DungeonRunHarness.cs      — accept optional persona name; default `balanced`.
  BotTelemetry.cs           — add Persona field to BotDecisionRecord and BotRunSummary.

src/Logic/Content/
  BotPersonaLoader.cs       — NEW: YAML deserialization (parallel to LootTagRegistry pattern).
```

### Presentation layer

```
src/Presentation/
  Bot/
    BotPlayerDriver.cs      — NEW: Godot Node. Owns a Timer; on each tick, calls BotBrain.Decide,
                               converts to PlayerAction, calls GameController.OnActionChosen.
                               Toggles itself off cleanly when bot mode disables.
    BotModeHud.cs           — NEW: Control overlay showing "BOT: balanced [F4 to stop]"
                               or similar. Attached to UILayer.
  GameController.cs         — minimal change: expose a public InjectBotAction(PlayerAction) so
                               the driver doesn't need access to OnActionChosen internals.
                               (OnActionChosen is already the canonical entry from input — we
                                surface it as a method, not via a new event.)
  Main.cs                   — handle F4 (debug build only) to toggle bot mode.
                               Also surface a debug-menu item via DebugOverlay (which already
                               exists at Main.cs:42).
```

### Tools

```
tools/Harness/
  Program.cs                — add --persona <name> for --scenario and --dungeon modes.
                              Add --bot-report (new mode) that runs the matrix × personas.
  BotSurvivabilityReport.cs — NEW: aggregates JSONL across N runs × M personas, emits markdown.

config/
  bot_personas.yaml         — NEW. 5 personas verbatim from the table above.
```

### Tests

```
tests/Balance/
  BotPersonaConfigTests.cs       — NEW: YAML loads, defaults work, unknown names fall back gracefully.
  BotBrainPersonaTests.cs        — NEW: each persona produces measurably different decisions in
                                    identical scenarios (e.g. aggressive ignores loot, cautious
                                    heals earlier, speedrunner descends faster).
  BotBrainStuckTests.cs          — NEW: stuck detection drops target after 8 same-position turns.
  ScenarioHarnessPersonaTests.cs — NEW: --persona flag selects the right brain; backward compat
                                    when persona omitted.
  BotSurvivabilityReportTests.cs — NEW: 5×N matrix shape, table parses cleanly.
tests/Presentation/  (existing dir if any)
  BotPlayerDriverTests.cs        — NEW: driver produces PlayerAction without Godot runtime
                                    (the driver class must be testable as a POCO consumer of BotBrain).
```

---

## Tasks

Tasks are ordered by dependency. Each is sized for one builder/tester session.

- [x] TASK-001: Introduce `BotPersonaConfig` record + 5 hardcoded defaults
  - Status: complete
  - Layer: logic
  - Type: system
  - Dependencies: none
  - Deliverables:
    - `src/Logic/Balance/BotPersonaConfig.cs` — `public sealed record BotPersonaConfig(string Name, double RetreatHpThreshold, double BaseHealThreshold, double PanicHpThreshold, int PanicMultiEnemyCount, int CombatEngagementDistance, int LootPriority, bool PreferStairs, bool AvoidCombat, bool AllowCombatHealing)`.
    - `src/Logic/Balance/BotPersonaRegistry.cs` — `public static class BotPersonaRegistry` with a `public static IReadOnlyDictionary<string, BotPersonaConfig> Defaults` field built from the threshold table above. `public static BotPersonaConfig Get(string? name)` returns Defaults["balanced"] when name is null/unknown (logs a warning via `Console.Error.WriteLine` when unknown, never throws).
    - `BotConfig` stays but its `HealThreshold` and `PanicThreshold` constants become `=> BotPersonaRegistry.Defaults["balanced"].BaseHealThreshold` etc. so existing call-sites keep compiling.
  - Acceptance criteria:
    - `BotPersonaRegistry.Defaults.Count == 5` with the exact 5 keys above.
    - `BotPersonaRegistry.Get("balanced").BaseHealThreshold == 0.30` (PoC-exact).
    - `BotPersonaRegistry.Get(null).Name == "balanced"`.
    - `BotPersonaRegistry.Get("nonsense").Name == "balanced"` and a stderr warning is emitted once.
    - `BotConfig.HealThreshold == 0.30` still (no caller breaks).
    - `dotnet test --filter "Category!=Slow"` passes.

- [x] TASK-002: YAML loader for `config/bot_personas.yaml`
  - Status: complete
  - Layer: logic
  - Type: system
  - Dependencies: TASK-001
  - Deliverables:
    - `config/bot_personas.yaml` — top-level `personas:` map; one entry per persona with all fields from the threshold table. Verbatim PoC values.
    - `src/Logic/Content/BotPersonaLoader.cs` — `public static class BotPersonaLoader { public static IReadOnlyDictionary<string, BotPersonaConfig> LoadFromFile(string path); }`. Uses YamlDotNet (already a dependency — see other content loaders).
    - `BotPersonaRegistry` gains a `LoadFromFile(string path)` that replaces `Defaults` with loaded values; if the file is missing, `Defaults` keeps the hardcoded table. (Hardcoded table is the safety net for tests that don't load YAML.)
  - Acceptance criteria:
    - `config/bot_personas.yaml` exists and parses without error.
    - Loaded values match the threshold table exactly (assertion in test).
    - When the YAML file is deleted, `BotPersonaRegistry.Defaults` is still populated from code (hardcoded fallback works).
    - Schema test: missing fields use C# record defaults; extra fields are ignored without throwing.
    - `dotnet test --filter "Category!=Slow"` passes.

- [x] TASK-003: Refactor `BotBrain.Decide` to accept `BotPersonaConfig`
  - Status: complete
  - Layer: logic
  - Type: system
  - Dependencies: TASK-001
  - Deliverables:
    - `BotBrain.Decide(...)` gains a `BotPersonaConfig? persona = null` parameter. When null, use `BotPersonaRegistry.Get("balanced")`. Other params unchanged.
    - All decision thresholds inside `Decide` and its helpers read from `persona.XXX` instead of `BotConfig.XXX`. The 6 decision rules in the current implementation become:
      1. **Panic heal** — `hpFraction <= persona.PanicHpThreshold && hpFraction < 1.0 && adjacent.Count >= persona.PanicMultiEnemyCount && HasHealingPotion(inventory)`. (Note: the current C# bot panics on HP alone; PoC requires multi-enemy. This task aligns to PoC.)
      2. **Threshold heal** — `hpFraction <= persona.BaseHealThreshold && HasHealingPotion(inventory) && (persona.AllowCombatHealing || adjacent.Count == 0)`.
      3. **Opportunistic loot** — only when `persona.LootPriority > 0`. `searchRadius = 3` for priority 1, `searchRadius = 6` for priority 2 (greedy deviates further). Skip entirely when `LootPriority == 0` (aggressive, speedrunner).
         - Note: the PoC's greedy bot also auto-equips better weapons/armor found on the floor (`bot_brain.py:580-589`). This plan defers gear pickup — `LootPriority` controls only potion-pickup search radius in this pass. `greedy` and `balanced` will behave similarly in gear-heavy floors. Porting `auto_equip_better_items` is a follow-up task. See the Deferred section at the bottom of this plan.
      4. **Retreat to choke** — when `adjacent.Count >= 2 && hpFraction < persona.RetreatHpThreshold` AND `!persona.AvoidCombat` (avoid_combat personas use a different rule, see #5).
      5. **Avoid-combat detour** — when `persona.AvoidCombat == true` and the nearest enemy is NOT adjacent and Manhattan distance `abs(dx) + abs(dy) <= persona.CombatEngagementDistance`: take one step away from the nearest enemy (direction `(player.x - enemy.x, player.y - enemy.y)` normalized). If no walkable retreat tile, fall through to rule 6.
      6. **Engage / move** — unchanged from current code, but only chase enemies whose Manhattan distance `abs(dx) + abs(dy)` is `<= persona.CombatEngagementDistance`. Beyond that, Wait.
    - **Distance-metric note:** `CombatEngagementDistance` is compared against Manhattan distance (`abs(dx) + abs(dy)`) to mirror the PoC's `bot_brain.py` semantics. This is asymmetric with the rest of `BotBrain`, which uses Chebyshev distance for adjacency checks and weapon-range gates — that asymmetry is intentional and matches the PoC. Builder should not "normalize" the two metrics; the engagement-distance values in the persona table (4, 5, 6, 8, 12) were tuned against Manhattan in the PoC.
    - `BotBrain.ToPlayerAction` unchanged.
    - The static surface of `BotBrain` is preserved (`Decide(...)`, `ToPlayerAction(...)`) so existing tests compile.
  - Acceptance criteria:
    - Default behavior (no persona arg) is byte-identical to current behavior for at least 3 existing scenarios — verified by running each scenario before/after the refactor with seed 1337 and comparing AggregatedMetrics. Pick `depth2_orc_baseline`, `depth3_orc_brutal`, `depth5_zombie`.
    - `BotBrain.Decide(player, fighter, inv, monsters, map, persona: BotPersonaRegistry.Get("aggressive"))` returns an `Attack` action even at 15% HP when a potion is in inventory and panic_multi_enemy_count would NOT trigger (1 adjacent enemy). Test by direct decision call.
    - `BotBrain.Decide(..., persona: BotPersonaRegistry.Get("speedrunner"), floorItems: [potion at distance 2])` returns the move-toward-enemy action, NOT the potion-pickup, because `LootPriority == 0`.
    - `dotnet test --filter "Category!=Slow"` passes.

- [x] TASK-004: Port STUCK detection from PoC
  - Status: complete
  - Layer: logic
  - Type: system
  - Dependencies: TASK-003
  - Deliverables:
    - Convert `BotBrain` from a fully-static class to a class with optional state. The static `Decide` stays as a convenience wrapper that lazily constructs a per-call instance — but `ScenarioHarness` and `DungeonRunHarness` create a `BotBrain` instance per run so stuck state persists across turns.
    - Add `private int _stuckCounter`, `private (int X, int Y)? _lastPlayerPos`, `private (int X, int Y)? _lastTargetPos`, `private Entity? _stuckDroppedTarget` to the instance.
    - After computing the decision but before returning: if action is "attack nearest" and player position unchanged AND target position unchanged AND last action was also a move-toward (not attack), increment `_stuckCounter`. Reset to 0 on any positional change OR on an attack action.
    - At `_stuckCounter >= 8`: set `_stuckDroppedTarget = current target`, clear `_stuckCounter`, return `BotAction.None` (Wait) for this turn. On the next turn, refuse to re-target `_stuckDroppedTarget` until a different enemy is closer or `_stuckDroppedTarget` dies.
    - At `_stuckCounter >= 15` (PoC + user spec): return a `BotAction.AbortRun` sentinel. New action type. Both harnesses interpret this as "end the run with outcome=stuck" and record the abort reason in metrics. (`OutcomeClassifier.Stuck` — add a new outcome string.)
    - **AbortRun plumbing (explicit):**
      - Add `AbortRun` to the `ActionType` enum (or whichever enum backs `BotAction.Kind` — builder picks the right type after grepping).
      - `BotBrain.ToPlayerAction` handles `AbortRun` by returning `PlayerAction.Wait` (a sentinel value — harnesses intercept the underlying `BotAction.AbortRun` before this conversion path runs and never call `ToPlayerAction` for an abort, but the case must still be safe).
      - Both harnesses (`ScenarioHarness.RunOnce` and `DungeonRunHarness.Run`): if the `BotBrain` instance returns `AbortRun`, break the run loop immediately; set `metrics.PlayerDied = true` (abort counts as death-equivalent for now); add a new bool `WasAborted` field to `RunMetrics` and set it `true`.
      - `OutcomeClassifier` gains an `"aborted"` outcome string returned when `WasAborted == true`. Downstream consumers (`PressureModel`, soak report aggregation) treat `aborted` the same as `died` for metric purposes — abort contributes to `Death%`. Document this in the classifier's XML comment.
    - Static `BotBrain.Decide(...)` (the existing call surface used by tests that don't care about state) keeps working by constructing a transient instance — stuck detection is then per-call, which means it never triggers in the static path. Document this clearly in XML comments. Tests assert this behavior.
  - Acceptance criteria:
    - Unit test: a `BotBrain` instance, fed 9 turns of "move toward enemy where neither moves," returns Wait on turn 9 and does NOT target the same enemy on turn 10.
    - Unit test: same instance, 15 stuck turns → returns `BotAction.AbortRun`.
    - Acceptance suite (15 scenarios × seed 1337) shows no new failures; an abort during a scenario counts as a death-equivalent in metrics for now. Document this design choice in the file.
    - Regression: the legacy static `BotBrain.Decide` is unchanged in observable behavior for the suite.
    - `dotnet test --filter "Category!=Slow"` passes.

- [x] TASK-005: Add Persona field to telemetry
  - Status: complete
  - Layer: logic
  - Type: system
  - Dependencies: TASK-003
  - Deliverables:
    - `BotDecisionRecord` gains `public string Persona { get; init; }`.
    - `BotRunSummary` gains `public string Persona { get; init; }` (set once per run from the recorder's first decision).
    - `BotDecisionContext` gains `public string Persona { get; init; }` — passed in by both harnesses.
    - `BotBrain.EmitDecision` writes the persona from the context.
    - JSONL writer in `tools/Harness/Program.cs` (`RunSoakWithStreaming`) includes `persona` at the run level.
  - Acceptance criteria:
    - Running `harness --dungeon --persona aggressive --jsonl reports/test.jsonl` produces a file where every line has `"persona": "aggressive"`.
    - Existing JSONL files without a `persona` field still parse (offline `--report` mode does not fail) — default to `"balanced"` when missing.
    - `dotnet test --filter "Category!=Slow"` passes.

- [x] TASK-006: `--persona` flag on harness scenario and dungeon modes
  - Status: complete
  - Layer: logic + tool
  - Type: system
  - Dependencies: TASK-003, TASK-005
  - Deliverables:
    - `tools/Harness/Program.cs` parses `--persona <name>`. Default: `balanced`. Unknown name: stderr warning + fall back to `balanced` (matches `BotPersonaRegistry.Get`).
    - `ScenarioHarness.RunOnce` and `DungeonRunHarness.Run/RunSoak` accept an optional `BotPersonaConfig` parameter (default = balanced). Pass it through to `BotBrain.Decide` via `BotDecisionContext`.
    - **Instance-based wiring (so stuck detection from TASK-004 actually fires):**
      - `ScenarioHarness.RunOnce` constructs **one** `BotBrain` instance per call (instead of calling the static `BotBrain.Decide(...)` path) and passes the persona config to it. All per-turn decisions in that run go through the same instance so `_stuckCounter` / `_lastPlayerPos` / `_stuckDroppedTarget` persist across turns.
      - `DungeonRunHarness.Run` likewise constructs **one** `BotBrain` instance per `Run()` call (per-run, not per-floor — stuck state spans floor transitions, which matches the PoC).
      - The static `BotBrain.Decide(...)` wrapper remains in place for backward compatibility with tests that don't need stuck state — without this wiring change, TASK-004's stuck detection would be silently dead in scenario mode.
    - `ScenarioHarness.cs:114-124` dispatch becomes a 3-way switch:
      - `player_bot: "ranged_net_arrow"` → `RangedNetArrowBot.Decide` (unchanged behavior)
      - `player_bot: "balanced"` / `"cautious"` / `"aggressive"` / `"greedy"` / `"speedrunner"` → `BotBrain.Decide` with that persona
      - Unknown / null → use the `--persona` CLI value (default `balanced`). YAML `player_bot` wins over CLI when both are set.
    - Help text in `PrintHelp()` documents the flag.
  - Acceptance criteria:
    - `harness --scenario depth3_orc_brutal --persona aggressive` runs without error and the JSON output includes `"persona": "aggressive"`.
    - `harness --scenario scenario_ranged_viability_arena --persona aggressive` still uses `RangedNetArrowBot` (YAML wins).
    - `harness --dungeon --persona cautious --runs 5` survives more often than `--persona aggressive` (statistical, but should hold on small N).
    - Backward compat: every existing harness invocation in `Makefile`, CI workflow, and docs continues to work unchanged.
    - `dotnet test --filter "Category!=Slow"` passes.

- [x] TASK-007: `--bot-report` mode (per-persona survivability matrix)
  - Status: complete
  - Layer: tool
  - Type: analysis
  - Dependencies: TASK-006
  - Deliverables:
    - `tools/Harness/BotSurvivabilityReport.cs` — new class.
    - `Program.cs` gains `--bot-report` mode. Flags: `--matrix fast|full` (defaults to `fast`, mirroring SuiteRunner's FastMatrix; `full` = SuiteRunner.Matrix). `--runs <n>` overrides per-scenario run count (default 20 in fast mode, 50 in full). `--out <path>` writes the markdown; stdout otherwise.
    - For each (persona × scenario) pair: run the scenario with that persona, record death rate, avg turns, avg H_PM, avg H_MP, BotRunSummary.HealDecisions.
    - Output markdown shape (5 personas × N scenarios), exactly the format requested:
      ```
                          balanced  cautious  aggressive  greedy  speedrunner
      depth2_orc_baseline    8%       4%         22%        9%        15%
      depth3_orc_brutal     12%      7%         38%       13%        28%
      ...
      ```
      Plus secondary tables: average HP-at-heal per persona, avg turns to clear per persona, deaths-with-unused-potions per persona × scenario.
    - Deterministic seeding: each (persona, scenario, run_idx) gets a unique seed via the existing `SeedDerivation.Stable(scenario_id, run_idx, base_seed)`. Personas share the same seed sequence so the encounter layout matches across personas (same seed → same monster spawn positions, same initial inventory, same map). Combat roll sequences diverge from turn 1 onward as personas take different actions and consume different RNG draws. N=50 run counts wash out the resulting stochastic variance at the aggregate level — survival-rate deltas across personas reflect policy, not RNG drift.
  - Acceptance criteria:
    - `dotnet run --project tools/Harness -- --bot-report --matrix fast --runs 5` completes in under 90 seconds on a developer Mac.
    - Output table has 5 columns (personas) and N rows (scenarios in matrix), all cells filled.
    - `cautious` survival rate >= `balanced` survival rate on at least 3 of 6 fast-matrix scenarios (sanity check that personas actually differ — softer than 4/6 because arena scenarios penalize `avoid_combat` with a wasted turn; see Risk #4).
    - `aggressive` survival rate <= `balanced` on at least 4 of 6 fast-matrix scenarios.
    - `dotnet test --filter "Category!=Slow"` passes (includes a snapshot test for the markdown format).

- [x] TASK-008: Tests for persona behavior differences
  - Status: complete
  - Layer: logic
  - Type: test
  - Dependencies: TASK-003, TASK-004
  - Deliverables:
    - `tests/Balance/BotBrainPersonaTests.cs` — at minimum one test per persona showing a decision that distinguishes it:
      - `Aggressive_DoesNotDeviateForFloorPotion` — 1 enemy at distance 3, potion at distance 2 → expect move-toward-enemy.
      - `Greedy_DeviatesForFloorPotionUpToRadius6` — potion at distance 5, no adjacent enemies → expect move-toward-potion.
      - `Cautious_HealsAt50Percent` — HP at 0.49, 1 potion, no enemies adjacent → expect Heal.
      - `Speedrunner_DoesNotEngageAtDistance5` — enemy at distance 5 (above engagement_distance 4) → expect Wait or move-toward-stair (whichever the impl produces — but NOT Attack/MoveToward).
      - `Balanced_MatchesLegacyBehavior` — same decisions as today for `depth2_orc_baseline` (smoke check).
    - `tests/Balance/BotBrainStuckTests.cs` — stuck detection unit tests (covered in TASK-004 deliverables; this task makes them comprehensive).
  - Acceptance criteria:
    - All 5 persona tests pass.
    - Test category `Bot` is used so they can be selected with `--filter Category=Bot` if needed.
    - `dotnet test --filter "Category!=Slow"` passes.

- [x] TASK-009: `BotPlayerDriver` Godot node + injection point
  - Status: complete
  - Layer: presentation
  - Type: system
  - Dependencies: TASK-003
  - Deliverables:
    - `src/Presentation/GameController.cs` — extract a `public void SubmitBotAction(PlayerAction action)` method that wraps `OnActionChosen` (which is currently private). `SubmitBotAction` is a thin no-arg-validation wrapper; gates on `Phase == GamePhase.WaitingForInput` exactly like `OnActionChosen` does. This is the canonical injection point for the bot.
    - `src/Presentation/Bot/BotPlayerDriver.cs` — new `partial class BotPlayerDriver : Node`. Public API:
      ```csharp
      public sealed partial class BotPlayerDriver : Node {
          public BotPersonaConfig Persona { get; private set; } = BotPersonaRegistry.Get("balanced");
          public bool Enabled { get; private set; }
          public float TurnDelaySeconds { get; set; } = 0.25f;  // 4 turns/sec default
          public event Action<bool>? EnabledChanged;
          public void Initialize(GameController controller, GameState state);
          public void SetPersona(string name);
          public void Enable();
          public void Disable();
      }
      ```
    - Internal loop: a Godot `Timer` child node. On each `timeout` signal: if `Enabled` AND `controller.Phase == GamePhase.WaitingForInput` AND `!state.IsGameOver`, build a `BotBrain` instance (one per Enable() call, so stuck state persists across turns), call `BotBrain.Decide(...)`, convert to `PlayerAction`, call `controller.SubmitBotAction(action)`. The `TurnCompleted` event from the controller is the signal that the next decision can be made — but we use the timer rather than the event to control speed.
    - For `MoveToward` actions, the driver does the same A* path override that `DungeonRunHarness` does (lines 368-401 of `DungeonRunHarness.cs`) — directly converting `MoveToward(enemy)` into a one-step `MoveTo(nx, ny)` via `Pathfinder.AStar`. The driver shares that helper with the harness — extract a `BotActionConverter.ToPlayerActionWithPathing(BotAction, GameState)` into `src/Logic/Balance/`.
    - Disable() stops the timer, clears the bot instance, fires `EnabledChanged(false)`.
  - Acceptance criteria:
    - `BotPlayerDriver` compiles and is creatable in a unit test (no scene tree required — Godot.Timer is mockable or replaced with an interface for testing).
    - In the running Godot game, `Initialize` → `Enable` → bot starts taking turns at 4/sec. `Disable` returns control to the human.
    - Bot actions go through `TurnController.ProcessTurn` via `SubmitBotAction` → `OnActionChosen`. Verified by stepping through with the debugger (or via Diag.Log line presence) — no parallel turn pipeline exists.
    - Existing human input is unaffected when `Enabled == false`.
    - `dotnet test --filter "Category!=Slow"` passes (logic-layer tests don't reach the driver; presentation tests for the driver use a fake controller).

- [x] TASK-009b: BotPlayerDriver exploration + floor-clear logic (makes graphical bot actually playable)
  - Status: complete
  - Layer: presentation
  - Type: system
  - Dependencies: TASK-009
  - Rationale: TASK-009 as specified ships a non-functional graphical bot in dungeon mode — it clears visible enemies then freezes (`BotBrain` returns Wait forever; no floor-clear → stair logic; no auto-explore integration; pathing into walls via fog-hidden monsters). This task closes those gaps so the bot survives a floor transition (required by TASK-012's acceptance).
  - Deliverables:
    - **Monster visibility filtering.** `BotPlayerDriver` passes `state.Monsters.Where(m => state.Map.IsVisible(m.X, m.Y) && m.Get<Fighter>()?.IsAlive == true).ToList()` as the monster list to `BotBrain.Decide` (not all floor monsters). This prevents the bot from path-planning to enemies on the other side of unexplored walls. The headless harnesses keep their existing all-monsters call — they run in fully-visible scenario arenas.
    - **Floor-clear → stair.** After `BotBrain.Decide` returns `Wait` (or after the converter produces a `PlayerAction.Wait`), and `visibleAlive.Count == 0`, check `state.AliveMonsters.Count == 0` (whole-floor clear). If true AND a known downstair position is in `state` (visible or remembered via FOW), submit `PlayerAction.Descend`. If true AND no stair is known yet, fall through to auto-explore.
    - **Auto-explore integration.** When no visible enemies AND the floor is not fully clear (or stair is unknown), submit `PlayerAction.AutoExplore`. This action already exists and is handled by `TurnController` — the driver simply submits it on the next timer tick. `TurnController` runs the existing auto-explore system, which navigates through unrevealed tiles. The driver does NOT re-implement pathing; it submits the action and waits for `TurnCompleted`.
    - **Re-engage on enemy reveal.** When auto-explore reveals a new enemy mid-traversal, `TurnController.AutoExplore` will already abort and return to `WaitingForInput`. The driver's next tick re-runs `BotBrain.Decide`, which now sees the visible enemy via the filtering deliverable above, and engages normally. No special handling needed.
    - **Loop termination.** If `state.IsGameOver`, call `Disable()` (which stops the timer and fires `EnabledChanged(false)`). Guard at the top of the timer-tick handler so a dead bot never submits another action.
  - Acceptance criteria:
    - Manual smoke: bot enables on a fresh floor, clears all visible enemies, auto-explores into unseen rooms, engages monsters as they appear, and submits `Descend` when standing on the stair after clearing the floor.
    - Unit test (using a fake controller + fake GameState): driver submits `AutoExplore` when monster list is empty and the floor has unrevealed tiles.
    - Unit test: driver submits `Descend` when monster list is empty AND `AliveMonsters.Count == 0` AND a downstair position is known AND the player is standing on it.
    - Unit test: driver calls `Disable()` once when `state.IsGameOver` flips to true.
    - `dotnet test --filter "Category!=Slow"` passes.

- [x] TASK-010: F4 hotkey + debug-menu toggle + HUD indicator
  - Status: complete
  - Layer: presentation
  - Type: system
  - Dependencies: TASK-009
  - Deliverables:
    - `src/Presentation/Main.cs` — in `_UnhandledInput`, add handler for `Key.F4` (debug builds only, gated by `OS.IsDebugBuild()`). On press, toggle `_botDriver.Enabled`. Adjacent to the existing F3 handler at Main.cs:1014.
    - `Main.cs` creates a single `BotPlayerDriver` at scene setup time (after `_gameController` is initialized) and stores it as `_botDriver`. Re-initialised across floor transitions.
    - `src/Presentation/Bot/BotModeHud.cs` — new `Control` overlay. Shows a small label at the top-center of the screen: "BOT MODE — balanced  [F4]". Background is a 70%-opaque dark rect so it's readable over the map. Hidden when `BotPlayerDriver.Enabled == false`.
    - Persona cycling: while bot mode is active, `Key.F5` cycles through `balanced → cautious → aggressive → greedy → speedrunner → balanced`. HUD label updates immediately.
    - Speed cycling: while bot mode is active, `Key.F6` cycles `TurnDelaySeconds` through `1.0 → 0.5 → 0.25 → 0.1 → 0.0 → 1.0` ("watch / brisk / fast / very fast / max"). HUD label appends the speed band.
    - `DebugOverlay` (already at Main.cs:42) gains a "Bot mode" section showing the same toggle/persona/speed state plus a "Stop bot" button. Toggling via the overlay buttons emits the same enabled/disabled events.
  - Acceptance criteria:
    - F4 in a debug build toggles bot mode visibly (HUD appears, bot starts playing within one tick).
    - F4 in a release build does nothing (and the BotPlayerDriver is not instantiated, saving memory).
    - Toggling off mid-turn lets the current animation finish, then returns to WaitingForInput — no broken animation states.
    - F5 cycles persona without disabling bot mode (HUD label updates, decisions change starting next tick).
    - F6 cycles speed visibly (delay actually changes).
    - Manual test: open the game in Godot editor, press F4, watch the bot clear the first dungeon floor with the default persona. No crashes, no orphaned UI.
    - `dotnet test --filter "Category!=Slow"` passes (no new failures; presentation-only changes have minimal test surface).

- [x] TASK-011: 5×15 acceptance matrix verification run
  - Status: complete
  - Layer: tool (verification)
  - Type: analysis
  - Dependencies: TASK-006, TASK-007
  - Deliverables:
    - Run `dotnet run --project tools/Harness -- --bot-report --matrix full --runs 50 --out reports/bot_survivability_v1.md` and commit the resulting markdown file.
    - Companion JSON: `dotnet run --project tools/Harness -- --bot-report --matrix full --runs 50 --json --out reports/bot_survivability_v1.json` (if `--json` is not supported by the report command, add it). Used by the documenter agent to track over time.
    - A short paragraph in the same markdown file noting interesting findings: which personas die where, and any unexpected outliers (e.g. cautious dies more than balanced — that's a signal of an over-conservative heal threshold).
  - Acceptance criteria:
    - Report file exists at `reports/bot_survivability_v1.md`.
    - The 5×15 table is complete (no empty cells).
    - Sanity floor: `cautious` survival rate on `depth2_orc_baseline` is >= `aggressive` survival rate on the same scenario. If it is not, flag a regression — the personas are not behaving as designed.
    - Wall-clock runtime under 15 minutes on a developer Mac (this is the bound; if it's too slow, drop to `--runs 25`).

- [ ] TASK-012: Visual smoke test of graphical bot mode
  - Status: pending — requires Godot editor
  - Layer: presentation (manual)
  - Type: test
  - Dependencies: TASK-009b, TASK-010
  - Note: "Bot survives at least one floor transition" (below) is achievable only with TASK-009b's exploration + floor-clear logic in place. Without TASK-009b the bot freezes after clearing visible enemies and this checklist cannot pass.
  - Deliverables:
    - Manual checklist captured in the task file when complete:
      - Bot mode toggles on with F4. HUD appears.
      - Bot moves and attacks on each timer tick.
      - Bot drinks a potion when low HP (visible in HUD + toast log).
      - Bot descends a staircase when the floor is clear.
      - Bot survives at least one floor transition without crash.
      - Persona swap via F5 changes behavior visibly within 5 turns.
      - Speed swap via F6 changes tick rate visibly.
      - Toggling off returns control to human input (tap an adjacent tile, see the player move).
    - Any bugs found are logged as follow-up tasks; the smoke test passes once the above 8 items are checked.
  - Acceptance criteria:
    - Checklist captured in task file.
    - No crashes, leaked nodes, or stuck phases reported.

---

## Risks and Decisions

- **`BotConfig` legacy field compatibility (TASK-001).** `BotConfig.HealThreshold` and `PanicThreshold` are referenced from `BotTelemetry.cs`, `DungeonRunHarness.cs` (the `BuildNavigateRecord` helper), and possibly others. They must continue to work as the `balanced` defaults after the refactor — converting them from `const double` to `static readonly double` (computed from the registry) breaks nothing because they were never used as compile-time constants. Builder should grep `BotConfig\.` to confirm before changing.

- **PoC's `BotPersonaConfig` has two heal thresholds (TASK-001 design choice).** The PoC has both `potion_hp_threshold` (deprecated) on `BotPersonaConfig` and `base_heal_threshold` on `PersonaHealConfig`. We collapse into one field per persona (`BaseHealThreshold`) on the C# side. This is a deliberate simplification — the PoC's duplication is a refactor artifact, not a design intent. Document this in the BotPersonaConfig XML comment.

- **Stuck detection in static BotBrain (TASK-004).** Tests that call the static `BotBrain.Decide(...)` won't see stuck detection trigger because state is per-instance. This is intentional: existing tests pass unchanged. The dungeon and scenario harnesses construct a per-run instance and DO see stuck detection. The graphical bot driver also constructs one instance per `Enable()` call. Builder should NOT chase down "stuck detection broke test X" without first checking whether the test uses the static path (expected) or instance path (legitimate failure).

- **Avoid-combat persona behavior (TASK-003, rule 5).** `cautious` and `speedrunner` both have `avoid_combat: true`. The PoC behavior is: skip non-adjacent enemies entirely and stay in EXPLORE. In our arena scenarios there's nowhere to go, so the bot will end up just waiting — which is fine for the survival rate metric but may produce odd-looking transcripts. If this is unacceptable, the rule becomes "take one step away from the nearest enemy" (kiting). Spec'd as "take one step away" in TASK-003 deliverables; flag if scenarios show this looking worse than PoC.

- **`SubmitBotAction` exposure (TASK-009).** Exposing `OnActionChosen` as a public `SubmitBotAction` is a small API surface increase on `GameController`. It is gated on `Phase == WaitingForInput`. We could alternatively make `BotPlayerDriver` a friend of `GameController` via internal visibility — but C# `internal` crosses the Logic/Presentation boundary only if they're in the same assembly. They are (`UnderWarden.Presentation.csproj` is one assembly, GameController is in it, BotPlayerDriver will be too) — so `internal` works. Picked `public` for clarity; builder may prefer `internal` and that is fine.

- **Timer vs TurnCompleted event (TASK-009).** Two options for driving the bot:
  (a) Subscribe to `GameController.TurnCompleted` and immediately schedule the next decision via `CallDeferred`.
  (b) Use a Godot `Timer` ticking at `TurnDelaySeconds` and gate on `Phase == WaitingForInput`.
  (b) is preferred because it cleanly supports configurable delay (so the human can watch) and it doesn't fire decisions during animations. (a) would coalesce decisions to the animation frame rate and make speed control awkward.

- **Bot mode + targeting/possession (TASK-009).** The bot should NOT make decisions while the game is in `Targeting` or `Possess` phases — `Phase == WaitingForInput` is the only valid gate. If the human enters targeting mode mid-bot-session (somehow), the bot pauses naturally because the phase changes. No special handling needed.

- **JSONL schema migration (TASK-005).** Adding `persona` to JSONL is forward-compatible only if downstream readers tolerate missing fields. The C# offline-report path (`SoakJsonlReader`) currently deserializes via `System.Text.Json` with `JsonIgnoreCondition.WhenWritingNull` — missing fields default to null/0/"", so adding `persona` is safe. Old JSONL files default to `persona: "balanced"` (no migration needed).

- **Acceptance suite re-baseline (TASK-006).** The acceptance suite (`harness --suite`) does not use personas — it uses the default (balanced) and writes one baseline. If the refactor in TASK-003 introduces ANY behavioral drift in the balanced persona, the baseline will need a one-time `--update-baseline` run. Builder must confirm with a before/after suite run on seed 1337 that nothing drifted. If anything drifts: investigate why before updating the baseline.

- **Persona-aware target bands (deferred).** Future work: `PressureModel` target bands (Death%, H_PM, H_MP) are designed for the balanced bot. Aggressive bots will exceed Death% targets by design; the suite should classify those as PROBE-equivalent. For now, only the balanced persona's runs feed the suite — the matrix-vs-persona table (`--bot-report`) is a separate report that does not influence verdicts. This keeps the existing acceptance pipeline intact. Revisit when persona-specific bands are designed.

- **Mobile builds + BotPlayerDriver (TASK-009/010).** The driver compiles unconditionally but is only constructed when `OS.IsDebugBuild()`. iOS NativeAOT export will trim the BotPlayerDriver if no code path references it in release — that is desired. Builder must NOT instantiate the driver from any release-build code path. Check via grep before merging.

---

## Test Plan Summary (for the tester agent)

Fast suite (default): `dotnet test --filter "Category!=Slow"` — must pass after every task.

Per-task test focus:
- TASK-001/002: unit tests on `BotPersonaRegistry` + YAML loader.
- TASK-003: unit tests on persona-specific decisions (5 personas × ≥1 decision each).
- TASK-004: stuck detection unit tests + integration via scenario harness.
- TASK-005: JSONL serialization includes persona; offline reader tolerates missing persona.
- TASK-006: CLI flag parsed correctly; YAML wins over flag; unknown persona name → warning + balanced.
- TASK-007: snapshot test for the markdown table format.
- TASK-008: comprehensive persona behavior tests.
- TASK-009: presentation-layer test for `BotPlayerDriver` using a fake controller (no Godot scene tree needed).
- TASK-010: no automated test — manual checklist in TASK-012.
- TASK-011: scenario-harness verification run, output in reports/.
- TASK-012: manual visual smoke test.

Final verification: `harness --bot-report --matrix full --runs 50` produces a 5×15 table where cautious >= balanced and aggressive <= balanced on majority of scenarios. If not, the persona thresholds did not port correctly — return to TASK-003.

---

## Deferred

- **Auto-equip better gear (PoC `bot_brain.py:580-589`).** The PoC's greedy bot scans floor items, compares weapons/armor against equipped, and auto-equips upgrades. This plan defers that behavior. Consequence: in floors with significant gear loot, `greedy` and `balanced` personas will perform similarly because `LootPriority` in this pass only governs potion-pickup radius. Follow-up task: port `auto_equip_better_items` and extend `LootPriority` semantics to cover weapon/armor pickup-and-equip decisions.
- **Persona-aware target bands.** `PressureModel` Death% / H_PM / H_MP target bands assume the balanced bot. Aggressive runs will exceed Death% targets by design — currently classified ad-hoc. Future work: per-persona band sets, fed back into the suite verdict logic.

---

## Review Notes (2026-05-21)

Six issues raised in critical review, all addressed via targeted edits (no task renumbering, no architecture change):

- **C1 — Distance metric inconsistency.** TASK-003 rules 5 and 6 now specify Manhattan distance for `CombatEngagementDistance` to match the PoC's `bot_brain.py`. Added an asymmetry note: the rest of `BotBrain` uses Chebyshev for adjacency/range checks; engagement distance mirrors the PoC's Manhattan metric. The persona table values (4, 5, 6, 8, 12) were tuned against Manhattan and must not be "normalized."
- **C2 — Misleading "fair comparison" claim.** TASK-007's seeding note replaced. Personas share encounter layout (same seed → same spawn positions / inventory / map), but combat RNG streams diverge from turn 1 onward as different actions consume different draws. N=50 run counts wash out the resulting variance at the aggregate level. Wording fix only; no design change.
- **C3 — Static-vs-instance harness wiring gap.** TASK-006 now explicitly requires `ScenarioHarness.RunOnce` and `DungeonRunHarness.Run` to construct one `BotBrain` instance per call (not the static path), so TASK-004's stuck detection actually fires in scenario mode. Static `BotBrain.Decide` wrapper kept for backward compat in tests that don't need stuck state.
- **C4 — `AbortRun` plumbing not specified.** TASK-004 now explicitly lists the enum addition, `ToPlayerAction` fallback (`PlayerAction.Wait` sentinel), harness interception (break loop, mark `PlayerDied = true`, new `RunMetrics.WasAborted`), and `OutcomeClassifier` `"aborted"` outcome (treated as died by `PressureModel` and soak aggregation).
- **C5 — Graphical bot exploration unspecified.** New TASK-009b inserted between TASK-009 and TASK-010. Covers visible-monster filtering (no path-planning through walls), floor-clear → `Descend`, `AutoExplore` integration via `TurnController`, re-engage on reveal, and `Disable()` on game-over. TASK-012's "survives at least one floor transition" criterion now depends on TASK-009b.
- **I2 — Auto-equip not ported, loot_priority misbehaves.** TASK-003's `LootPriority` deliverable annotated: in this pass it governs only potion-pickup search radius; PoC's `auto_equip_better_items` is deferred to a follow-up (added to the new Deferred section). TASK-007's `cautious >= balanced` sanity check softened from 4-of-6 to 3-of-6 fast-matrix scenarios to account for arena scenarios where `avoid_combat` wastes a turn (see Risk #4).

Task numbering is unchanged. TASK-009b is the only insertion. No subsequent tasks reference task numbers > 010 by literal index; the dependency graph is updated where needed (TASK-012 now also depends on TASK-009b).
