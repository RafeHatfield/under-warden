# World-Class Review & Roadmap — 2026-07-06

Full-codebase review of The Under-Warden plus a review of the Claude Code setup across both
repos. Goal: identify everything standing between this project and "among the best roguelikes
built," ordered so each phase unblocks the next. Findings below were **verified empirically on a
clean Linux machine** (fresh clone, fresh .NET 8 SDK) unless marked otherwise.

**Verdict up front:** the game's bones are genuinely excellent — the logic/presentation split is
clean (zero Godot imports in `src/Logic/`), the balance pipeline is a real instrument, the
findings log is doing the job of institutional memory, and 2,040 fast tests pass. The gaps are
not in design discipline; they are in **reproducibility** (the project currently builds only on
one machine), **one measurement-identity bug** that froze balance work, **a missing mobile-critical
feature** (mid-run save), and **doc rot** that misleads future sessions.

---

## Part 1 — What was verified and fixed on this branch

These three fixes are committed on this branch, each verified end-to-end here:

### FIX-1: CI has been red on every main push since 2026-05-22 (~6 weeks)
- **Cause:** `balance.yml` runs bare `dotnet restore`; the repo root has TWO solution files
  (`UnderWarden.sln` + the Godot editor's `UnderWarden.Presentation.sln`, added with the
  bot-personas graphical work) → `MSB1011` on every run. Nothing after restore executes — **no
  tests and no balance gate have run in CI since May 22.** The findings log's "CI gates on the
  full matrix" has been aspirational for six weeks.
- **Fix:** `dotnet restore UnderWarden.sln` (comment added explaining why).
- **Lesson for process:** a red CI badge was invisible because nothing surfaces it. See Part 4
  (Claude setup) — this is exactly what a PR-based flow + CI subscription solves.

### FIX-2: `nuget.config` pinned the build to one Mac
- **Cause:** committed `godot-local` package source pointing at
  `/Applications/Godot_mono.app/...` — `NU1301` on any other machine (including CI, once FIX-1
  lands). Verified the entire solution (Logic, Tests, Harness, Presentation, analyzers) restores
  from nuget.org alone.
- **Fix:** removed the `godot-local` source. If a custom Godot build is ever needed, add it as a
  **user-level** source on the machine that needs it (`dotnet nuget add source ... --configfile
  ~/.config/NuGet/NuGet.Config`), never in the repo.

### FIX-3: Test project didn't compile on a clean machine
- **Cause:** floating package versions (`NUnit 4.*`, `17.*`). A clean restore pulls the newest
  NUnit 4.x, which added `Action` overloads → `CS0121` ambiguous-call errors in 10 files. Local
  machines with an older cached NUnit build fine — classic works-on-my-machine drift.
- **Fix:** pinned `NUnit 4.3.2`, `NUnit3TestAdapter 4.6.0`, `Microsoft.NET.Test.Sdk 17.11.1`,
  `NUnit.Analyzers 4.4.0`. Verified: fast suite **2040 passed / 0 failed**, and the full CI
  sequence (restore → fast tests → `--suite` vs committed baseline) passes: **15/15 PASS**.

### FIND-005: the FIND-004 "hidden damage reduction" mystery is solved
Full entry written to `docs/balance/balance_findings.md`. Summary: there is no damage-reduction
mechanism. Two metric families share the names `H_PM`/`H_MP` — `AggregatedMetrics` (hits-based)
vs `PressureModel` (rounds-based, what the harness prints). FIND-004 verified the hits-based
definition, then read rounds-based numbers. Measured truth at depth 7: orcs land 6.35 dmg/hit at
36% accuracy; hits-based **ttk ≈ 5.6 vs target 4 (1.4×)** and **ttd ≈ 8.8 vs target 4 (2.2×)** —
a real but modest reconciliation, not the feared 10×. **Lethality tuning is unblocked.**

---

## Part 2 — Codebase: issues, edge cases, logic problems (prioritized)

### P0 — Reproducibility & correctness of the instrument

1. **Metric identity split (root cause of FIND-003/004 churn).** Rename the two families so the
   unit is in the name: `TtkHits`/`TtdHits` (hits-based; evaluate representative scenarios
   against ETP ttk/ttd) and `RoundsToKill`/`RoundsToDie` (rounds-based; keep for stress-band
   regression). Fix the wrong "hits" doc-comments on `PressureModel.H_PM/H_MP`. Print both,
   labeled. ~Half-day, prevents every future misdiagnosis of the game's core feel numbers.
   Files: `src/Logic/Balance/PressureModel.cs`, `RunMetrics.cs`, `tools/Harness/Program.cs`.

2. **Add a lock file + CI as the ground truth.** With versions now pinned, add
   `<RestorePackagesWithLockFile>` + commit `packages.lock.json`, and make CI restore with
   `--locked-mode`. Also update deprecated action versions when convenient (GitHub warns Node 20
   actions break June 16, 2026 — that's **this month**: bump `actions/checkout@v4→v5`,
   `setup-dotnet`, `upload-artifact` or set `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24`).

3. **Duplicate Presentation csproj drift.** `UnderWarden.Presentation.csproj` exists at repo
   root (Godot editor project, `<Compile Include="src/Presentation/**">`) AND at
   `src/Presentation/` (in `UnderWarden.sln`) — with **already-diverged** trimming/
   globalization settings (root: `TrimMode=partial`; nested: `IsTrimmable=false` +
   `InvariantGlobalization`). One of these is what ships to iOS; the other is what CI compiles.
   Decide which is canonical, delete or thin the other into an import of shared props
   (`Directory.Build.props` is the right home for the iOS/AOT settings). This is DEBT-class today
   and a shipping bug the day the settings matter (they already differ on trimming, which was the
   cause of a real iOS crash per the comments).

4. **`RunMetrics` monster-attribution edge case.** Any non-player `AttackEvent` counts as
   "monster" — allied guardians (Weighing ally-fallback), raised undead fighting monsters, and
   confused monster-vs-monster hits all pollute `MonsterDamageDealt`/`MonsterHits` and therefore
   ttd. Harmless in today's orc scenarios; wrong the day a scenario includes allies or
   necromancers (both exist). Tag events with faction or attacker-target pair and filter to
   attacks *on the player* for ttd.

### P1 — Architecture debt that will start taxing velocity

5. **`TurnController.cs` is 3,179 lines** and still growing (every new system lands another
   resolver hook in it). It's the god-object risk for this codebase. Decompose along the seams it
   already has: player-action resolution, monster-turn resolution, status-effect ticking,
   death/loot routing — each a static class taking `GameState` + events list. Mechanical, low
   risk, big payoff for every future feature. (Same pattern applies to `Presentation/Main.cs` at
   2,118 lines, lower priority.)

6. **Open MEDIUM debt from the 2026-03-30 audit is still open** and two items bite harder as
   content grows:
   - DEBT-008 dual entity-ID allocators (now **three** with `WeighingOrchestrator.NextId`,
     per DEBT-016). Unify before save/resume work (item 8) — serialized IDs make this much more
     expensive to fix later.
   - DEBT-009 `ContentLoader.Merge` default-value sentinels: a child YAML entry **cannot override
     a stat back to 0/default** — silent wrong data as monster variants multiply. Nullable
     mergeable fields fix it.
   - DEBT-007 stale `AliveMonsters` cache; DEBT-015/016/017/018 from the Weighing review.
7. **Determinism guardrails.** The core discipline is good (`SeededRandom` injected everywhere;
   only benign `DateTime.UtcNow` in a report filename). Two gaps: (a) no test asserts a full
   dungeon-run event stream is identical across two runs of the same seed — add a "golden seed"
   test so a stray `Dictionary` iteration or `HashSet` ordering can't silently break replays;
   (b) `InvariantGlobalization` differs between the two Presentation csprojs (see item 3), which
   can change string-dependent behavior between editor and device builds.

### P2 — The features that separate "solid" from "world-class"

8. **Mid-run save/resume — the single biggest missing feature for a mobile-first roguelike.**
   There is no serialization of an in-progress run; iOS suspending the app = lost run = the top
   rage-quit trigger in your own `PLAYER_PAIN_POINTS.md`. The deterministic engine gives you the
   elegant implementation: **event-sourced saves** — persist (seed, floor, action log), resume =
   replay. It's cheap to store, impossible to save-scum accidentally, doubles as a bug-report
   format ("attach the save" = full repro), and the existing atomic-write persistence layer
   (`PersistentRunState.Flush`) is the right home. Prereqs: golden-seed determinism test (item 7),
   ID unification (item 6/DEBT-008). This should be the next major feature after the balance pass.

9. **Finish the lethality reconciliation with correct numbers** (now unblocked by FIND-005).
   Sequence: (a) metric renames land first (item 1); (b) re-pose the decision with hits-based data
   — current game is 1.4×/2.2× over ETP intent, and monster accuracy (36%) is as big a ttd lever
   as HP; (c) tune one variable at a time toward the chosen target, re-baseline, playtest the feel
   at depths 2/5/7. The FIND-001 dagger-identity fix and FIND-002 matrix-axes cleanup belong in
   this same pass.

10. **Doc rot is actively misleading.** `TRADITIONAL_ROGUELIKE_FEATURES.md` still lists "no item
    identification (THE defining mechanic)" and "missing rings" as critical gaps — both are
    implemented (`IdentifiableItem`, `AppearancePool`, `RingEffectComponent`). `PHASES.md` was
    last updated 2024-12-14. For a project that leans on docs as agent context, stale docs are
    worse than missing ones: an agent reading the features doc would re-plan work that's done.
    Cheap fix: a documenter-agent pass that reconciles those two docs against the code, and a
    rule that feature docs get a "status: implemented in X" stamp when plans close.

11. **Player-facing polish gaps worth planning next** (from the features doc, adjusted for what's
    actually implemented): auto-explore exists; consider next — message-log search/filter on
    mobile, damage-number/HP-bar accessibility options, colorblind-safe palette check for the
    tile themes, and a death recap ("killed by X on depth Y — the last 5 turns") which turns the
    #1 pain point into a story players share. Each is presentation-layer, none threatens balance.

---

## Part 3 — Suggested execution order

| Phase | Work | Size |
|-------|------|------|
| 0 (this branch) | CI restore fix, nuget source removal, NUnit pins, FIND-005 | done |
| 1 | Metric renames + both-families output; lock file; action bumps; RunMetrics ally filter | ~2 sessions |
| 2 | Lethality reconciliation pass (decision → tuning → re-baseline → playtest); dagger identity; matrix axes | ~3–4 sessions |
| 3 | TurnController decomposition; DEBT-008/009 unification; golden-seed determinism test | ~3 sessions |
| 4 | Event-sourced mid-run save/resume (mobile-critical) | ~3–4 sessions |
| 5 | Doc reconciliation pass; death recap + accessibility polish | ~2 sessions |

Phases 1→2 are strictly ordered (don't tune on ambiguous metrics). 3 before 4 (don't serialize
dual IDs). 5 can interleave anywhere.

---

## Part 4 — Claude setup review (solo dev, both repos)

What's working well — keep: per-repo `CLAUDE.md` with persona + architecture rules (they're
specific and enforceable, not vibes); the six/five agent split with task-file coordination;
`tasks/plans/` + handoff files as session memory; the findings log as append-only decision record.
This is genuinely better than most team setups. Improvements:

1. **Make CI the loop-closer — the biggest gap this review exposed.** Six weeks of red CI went
   unnoticed because work lands directly on `main` and nothing watches the badge. Recommended
   flow on Claude Code web/desktop: work on a branch → PR → let the session **subscribe to PR
   activity** (`subscribe_pr_activity`) so CI failures come back to the agent that caused them
   and get fixed before merge. Even solo, the PR is the checkpoint where an agent babysits CI to
   green. Alternative/addition: a scheduled Routine that checks the latest main workflow run
   daily and opens a session only when it's red.

2. **Fix machine-coupled config in checked-in settings.** `ringmark/.claude/settings.json`'s
   PreToolUse push-hook hardcodes `/Users/rafehatfield/...` and Homebrew paths — on any other
   machine (including remote sessions) the hook dies silently and the lint/type gate never runs,
   the same failure shape as the nuget.config issue. Use `$CLAUDE_PROJECT_DIR` and bare `npx`
   (PATH-resolved), and prefer failing loud (`"decision":"block"` on hook error) over skipping.
   Catacombs has no `.claude/settings.json` at all — add one with a permission allowlist for
   `dotnet test/run/build`, `git` read ops, and the harness invocations, so remote/background
   sessions don't stall on prompts.

3. **Add a SessionStart hook for web sessions (both repos).** Remote containers start cold; a
   session-start hook that installs the .NET 8 SDK (catacombs) / runs `npm ci` (ringmark) means
   every cloud session can actually run the test suite instead of reviewing blind. (This session
   had to hand-install the SDK before it could verify anything.)

4. **Point agents at the balance playbook explicitly.** The analyst/reviewer agent prompts
   restate metric definitions loosely ("H_PM = hits to kill monster") — that exact looseness
   caused FIND-003/004. After the Part 2 renames land, update `.claude/agents/*.md` and
   `CLAUDE.md` to reference the canonical definitions in `docs/balance/combat_metrics_guide.md`
   rather than restating them (single source of truth applies to prompts too).

5. **Encode the verification habit.** The builder agent says "include harness verification" but
   nothing enforces "fast suite green before commit." A cheap PreToolUse hook on `git commit` in
   catacombs (mirror of ringmark's push hook, made portable per item 2) running
   `dotnet build tests/ --nologo -clp:ErrorsOnly` catches the CS0121-class breakage at the source.
   Full `dotnet test` is too slow for a hook — CI (item 1) is the right place for the suite.

6. **Session memory: prefer one INDEX over many handoffs.** `tasks/handoffs/` has 8 one-shot
   handoff files; `tasks/plans/INDEX.md` exists but handoffs aren't linked from it. Fold handoff
   state into the plan files they belong to (or a single `tasks/NOW.md` that says "current thread,
   next action, open decisions") — fewer places for a fresh session to check, less chance of
   acting on a stale handoff.

7. **The "Report model(s) used" global directive** in both CLAUDE.mds: harmless, but note it
   won't survive into artifacts (commits/PRs), by design. If the intent is auditability of which
   model wrote which code, a commit-trailer convention would do it better.

---

## Part 5 — What was deliberately NOT done here

- No balance tuning (Phase 2 needs the metric renames first, and tuning is a design decision).
- No TurnController refactor (needs its own plan + review cycle).
- No changes to ringmark code (findings are in Part 4; its settings fix touches Rafe's local
  workflow, so it's recommended rather than applied).
