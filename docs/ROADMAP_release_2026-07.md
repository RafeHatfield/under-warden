# The Under-Warden — Release Roadmap

**Date:** July 2026
**Supersedes:** `REVIEW_launch_readiness_2026-07.md` (v1) and `_v2.md` — the review findings are folded in here as work items; this document is the plan of record for completing the game for release.
**Scope decision (Rafe, July 2026):** full game as designed — 25 floors, five regions, three NPCs, four mid-run bosses, complete voice bill. No densify-and-shrink. This roadmap plans to that scope; the tradeoff accepted is a later launch rather than a smaller one.
**Verified against:** `main` post-discovery/reconciliation (docs stamped @ `86b6f10`; evidence in `reports/work_discovery_2026-07.md`, `reports/doc_reconciliation_2026-07.md`, `docs/balance/balance_findings.md` FIND-005).

---

## 1. Where the game stands (compressed from the review)

**Done and verified:** core roguelike loop; 28 monsters, 4 factions; traps (9 types), resistance, item stacking, two-handed weapons; possession through Phase 7; cross-run persistence (15 namespaces, migrations, daily seeds); Under-Warden memo pipeline; Weighing endgame framework (6 endings wired); depth-based tile theming mechanism; balance harness + Analyst + LLM Player stack; CI dispatches on clean machines with fast tests green (2,223 passed / 1 skipped as of 2026-07-16) — but the baseline-gated acceptance suite is RED (5 PASS / 3 WARN / 7 FAIL), pending the FIND-006 ruling and re-baseline in M2; docs reconciled and stamped.

**Confirmed absent (never built; not recoverable from any ref):** mid-run save/resume; Hollowmark ribbon UI (logic-side `VoiceLineRegistry` only); dialogue system; Borrek/Vesh/Hael as entities; Warden of Reven, Tide-Hunger, Hollow King, Weigher of Hearts; region content identity (mechanism live, themes not authored); audio; ~15–20K words of the 24–30K voice bill (≈9.9K written, distribution inverted — endgame deepest, Hollowmark thinnest at 254 words); 6 no-op rings; stair-up ascent.

**Open design rulings:** combat pace (true gap ttk 1.4× / ttd 2.2× vs ETP intent, entangled with the merged orc/troll HP bump); FIND-001 keen dagger; ring stubs (implement vs remove); archive deletion.

---

## 2. Roadmap structure

Seven milestones, ordered by dependency, not calendar. Each has an exit gate — measurable where the instruments allow, playtest-judged where they don't. Session estimates come from the repo's own archived budgets where they exist and are marked (est.) where they're mine; treat all of them as rough. Two tracks run the whole time in parallel with the milestone chain:

- **Track V (voice/content authoring)** — design-thread work, you + Claude, gated per milestone below. This is the long pole (~15–20K words) and starts now.
- **Track L (launch runway)** — Steam page, assets, demo packaging. Starts now at low intensity; §M7 is its convergence point.
- **Track A (art conformance)** — Oryx conformance burn-down + asset pipeline, added 2026-07-24. Runs parallel to the milestone chain; gates M7's Steam capsule/screenshot work specifically (§Track A).

```
M0 → M1 → M2 → M3 → M6 → M7
         (V runs M0–M6 continuously; L runs M0–M7; A runs parallel, gates M7 assets; M4/M5 slot after M2, parallel with M3)
```

---

## M0 — Instrument integrity (prerequisite, small)

The measurement and process fixes that everything downstream trusts.

1. **Metric-family rename** per FIND-005: `TtkHits`/`TtdHits` vs `RoundsToKill`/`RoundsToDie`; fix `PressureModel` doc-comments; harness prints both families labeled. No lethality tuning before this lands.
2. **PR-based flow with visible CI** — the red-badge-for-six-weeks failure gets a process answer while stakes are low: all work via PRs, CI status surfaced (the world-class review's Part 4 recommendation).
3. Housekeeping rulings (**resolved 2026-07-16**): `docs/archive/` **KEPT** — retained as provenance cited by this roadmap and `tasks/plans/`, fenced as non-current-state in its MANIFEST. Stair-up **NOT wired** — the game is a one-way descent; the tile is retained as an entry marker with honest flavor text ("there is no going back"), no ascent logic added.

**Exit gate:** harness output shows both metric families; a deliberately-red test PR is visibly red to you within one session. **All three items resolved — M0 closed 2026-07-16.**

## M1 — Mobile viability + voice delivery surface (build next, before any content)

Two features every later milestone playtests through:

1. **Mid-run save/resume.** Serialize full world state on background/kill; deterministic logic layer and seeded RNG make this bounded. Acceptance: kill the process mid-combat → relaunch → identical state; plus a harness soak proving save/load round-trips don't perturb determinism (gate it in CI permanently). This is the single most important missing feature for a 25-floor mobile game — at full scope, runs are hours long and *every* run will cross an interruption.
2. **Hollowmark ribbon UI.** The presentation-layer delivery surface for the game's protagonist relationship (`ToastLog` already ruled out in the archived design notes). Include duration setting and a ribbon-history view from day one (accessibility items that are cheap now, retrofits later).

**Exit gate:** a full floor played on device, interrupted twice, resumed losslessly, with Hollowmark lines delivered through the ribbon.
**(est.)** 3–5 CC sessions combined.

## M2 — Combat feel lock (blocks boss design and all balance work)

The durable-vs-lethal ruling at its true magnitude, per doctrine: feel first, numbers chase, one lever at a time with delta confirmation.

1. Resolve the entanglement first: the merged orc HP 28→40 / troll 30→48 bump pushes durable while the ETP intent says lethal — harness-validate it against target bands and rule on it jointly with the pace decision (findings-log instruction).
2. Tune toward the ruling. Note from FIND-005: monster accuracy (measured 36%) moves felt danger without touching the damage model — likely the first lever.
3. Fix or pull the keen dagger (FIND-001) and audit for other dominated loot.
4. Re-baseline the suite; new baselines committed.

**Why it blocks M3:** all four bosses must be tuned against the *final* pace; tuning bosses twice is the waste to avoid.
**Exit gate:** ttk/ttd (hits-based) inside the bands you set for the ruling, across the B1/B2 representative scenarios; you playtest depth 1–7 and sign off on feel.
**(est.)** 2–4 CC sessions plus your playtest passes.

## M3 — The mid-run spine at full scope (the big build)

Everything that puts *someone* on floors 1–24. Sub-items ordered so each is independently shippable to main behind the existing content-config pattern.

1. **Dialogue system** — the delivery mechanism for NPCs; three-state reputation model per the archived v3 design notes (no numeric tuning). (est. 2 sessions engineering; text via Track V.)
2. **Borrek, Vesh, Hael as entities + arcs** — three-state arcs reachable in 2–4 runs per the v3 reshape. (Archived notes budget ~3–4 sessions including arc wiring.)
3. **Four bosses via the boss-template reshape** (archived notes: ~2 sessions for the template, then per-boss config): Warden of Reven, Tide-Hunger, Hollow King, Weigher of Hearts. Placement per story docs; encounter design honors the composition-is-the-unit rule — each boss ships with its supporting group, tuned in the harness against M2's locked pace.
4. **Region identity ×5** — the tile-theming mechanism is live; author the five region themes (tiles, palettes, prop sets, signpost/mural pools per region). Art curation is the long pole here, not code.
5. **Past-Sasha and memo density check at depth 25** — verify cross-run features pace correctly across the full 25-floor spread (they were designed against this scope, but soak it).

**Exit gate:** a full 25-floor run contains: all three NPCs encounterable, all four bosses at their depths, five visually distinct regions, and no floor span >4 floors with zero authored encounter/NPC/boss/past-Sasha beat (set the exact spacing number after one full playtest — the principle is "no dead stretches," measured, not guessed).
**(est.)** 10–14 CC sessions of engineering plus art curation time; the widest-variance estimate in this roadmap.

## M4 — Voice completion (Track V, gated here; runs continuously from M0)

Authoring order is by player exposure, inverting the current corpus:

1. **Hollowmark** — full 17-trigger taxonomy to a pool depth where the once-per-run no-repeat guarantee holds across a complete 25-floor run (currently 254 words; the flagship voice is the emptiest file).
2. **Quipping shade** (211 words currently) — the audible payoff of cross-run persistence.
3. **Memo body[1+] variants** for every trigger that can fire twice; the weariness progression is the marketing centerpiece — it must never visibly repeat.
4. **NPC dialogue + boss voice** — feeds M3 as its systems land.
5. **Endgame completion** — the four known remaining items: 2 combat-death ending texts, ally-fallback lines, Refuse/Swap UI copy (already deepest at 5,270 words; last for that reason).

**Quality gate, run inside the content pipeline, not as a separate pass:** rubric v1 + anti-tell lint on every batch; register spec conformance (Sasha says "work," never "combat"; substrate principle).
**Exit gate (CI-enforced via the Analyst stack):** soak runs show zero Hollowmark repeats in a full run and shade/memo repeat-rates under thresholds you set; lint clean.
**Scale:** ~15–20K words to write. At a sustainable nights-and-weekends authoring pace this is the schedule's long pole — which is why it starts at M0, not after M3.

## M5 — Sensory and accessibility pass (parallel with M3, after M1)

1. **Minimal SFX set** (archived notes budget 3–5 sessions + asset curation): hits, deaths, potion, stairs, traps, UI taps, ribbon chime, one ambient loop per region (pairs with M3.4's region work).
2. **Rings ruling executed:** implement the 6 stubs or remove them from loot pools — full-scope release argues for implementing at least `ring_of_resistance` (the mechanic already exists; the ring just doesn't invoke it) and removing any that still can't earn their slot. Your call per ring; no stub ships equippable.
3. **Accessibility set:** text-size setting; ribbon duration + history (done in M1 if built as specced); color-blind audit of faction/status tints and quick-slot coding; reduced-motion setting.

**Exit gate:** device playtest with sound on feels finished; no equippable item is a no-op; the audit checklist is green.

## M6 — Full-game validation (after M2 + M3; the release-candidate gate)

1. **Full-depth soaks:** floor-health classifier across all 25 floors; escalator validation on winnable baselines; boss encounter outcomes inside their bands.
2. **Weighing balance pass** (the deferred TASK-011) — now against real full-run save data.
3. **First-session stopwatch beats,** now testable end-to-end: Hollowmark speaks inside 60 seconds; one tactical-interaction discovery inside floor one; death→memo loop lands by session end (guarantee off a near-death if needed). Possession stays untutorialized — discovery-layer reward.
4. **Hallway tests:** five SPD players + five newcomers, first fifteen minutes, watching for confusion — bots measure liveness, not comprehension.
5. **Full regression:** fast suite, suite baselines, determinism soak with save/load, voice no-repeat soak — all green in CI on the release branch.

**Exit gate:** you complete a full 25-floor run on device, interruptions and all, and would honestly recommend it to a stranger. Everything above is instrumentation in service of that sentence.

## M7 — Launch runway (Track L converges; page work starts now)

1. **Now, regardless of milestone state:** Steam page live — wishlists accrue during everything above, and roguelike discovery happens on Steam, not the App Store.
2. **Demo build:** first region free, cross-run persistence active in the demo so the memo/catalog hooks set before the paywall; single unlock, no ads, no other IAP. Enter the next available Steam Next Fest with it.
3. **Store assets, voice-forward:** lead with a memo screenshot (a body[2] near-silent one), a ribbon line mid-combat, a possession shot, a clean portrait-UI shot. Copy leads with personality, not genre bona fides; "portrait-native, one-handed" stated explicitly. No literary-inspiration references anywhere public.
4. **Price ruling:** $6.99–$9.99, your call (my starting suggestion remains $7.99; first sale event is the experiment).
5. **Platform sequencing:** mobile + Steam near-simultaneous if bandwidth allows; Steam trailing ~2 months if it doesn't. Keyboard support is the one real Steam port cost — budget it.
6. **Post-launch plan, published as a roadmap at launch:** with the full game shipping at once, the depth-expansion update no longer exists as the marketing beat — plan the first post-launch beat from other material (new possession targets, challenge badges/daily-seed events, a sixth ending are all candidates already in the design space). Quarterly cadence, SPD playbook.

---

## Track A — Art conformance (added 2026-07-24; runs parallel, gates M7 assets)

**Concluded 2026-08 — findings banked, direction superseded by art reimagining (new thread); infrastructure retained.** The Oryx-conformance track is closed by ruling. The sprint-exit finding stands: **canon-component assembly is viable for novel props, but novel concepts without canon vocabulary are not producible by this pipeline** (the bed is the named exhibit). PR #129 (the Round E / E2 / E3 conformance work) was closed as superseded without merging — its pending candidates do not land; its findings are banked (in the closed PR and session memory). Issue #51 (variant/config wiring) closed unstarted. **Retained infrastructure** (already on `main`): the art lint (Part A machine checks + the F1–F3 structural-fineness family), `ReviewSceneBuilder` + the in-scene review protocol, the generated-assets manifest, and the acceptance/capture harness — reusable by the new art-reimagining thread. Foundation bug #128 (FloorComposer Pass-1 prop-pedestal) is an independent engine defect and remains open. The entries below are the prior in-progress record, retained for history.

**Sweep complete (2026-08).** The style-register sweep is closed: the register is named
(Shattered-Pixel/Oryx — chunky, low-detail, bold-read), and all 67 fineness-ranked assets were
verdicted via the **in-scene review protocol**, now the standing Part B mechanism (candidates are
judged only as rendered through the production renderer beside canon neighbours; `ReviewSceneBuilder`
+ `review_capture.py`; contact sheets are a pre-filter only). The fineness lint family **F1–F3 is
permanent**; **F4 (edge_density) was demoted-with-evidence to advisory** (it could not separate
Rafe's labeled rejects from his keeps or from canon). The manifest reconciles to **77 files with 0
nonconformant** — every entry is canon, canon-derived, eye-approved regen, pipeline-conformant, or
accepted-by-eye (parked-with-dignity set: sack/beds/benches/anvil/club). Also ruled: the two-plane
perspective rule (front face + top surface only; no side faces). **Remaining open art surface:** the
#51 config PR (bookshelves spawn + shelf-family re-key — held on Rafe's go); multi-tile prop support
(#109, future engine capability for canon banquet tables 600-607); and play-review follow-ups
(nightstand colour, flagged provisional).

**Done:** Oryx master palette + snap pipeline; art lint (Part A machine checks +
Part B rubric) as a standing PR gate for asset changes; provenance-derived
generated-assets manifest (80 files); acceptance test scene (authored data →
production renderer) + capture harness (iPhone SE 750×1334 baseline);
burn-downs 1–2a–2b: 64/80 conformant, incl. canon chest family and derived
worn floor; PixelLab palette-lock correction (PIXELLAB_CONVENTIONS.md,
2026-07-24) + locked candidate bank (~1,170 quarantined candidates:
murals ×3 directions, prop variety, textures, M3 design references).

**Remaining, in order:**
1. Parked-six palette-locked round (anvil, armor_stand, club, mushroom_cluster,
   rock, water_barrel) — candidates → picks → land. Club may need the
   outline-repair script (also unlocks banked items if kept).
2. Mural ruling — design conversation over the 150 banked candidates
   (heraldic / banner / pictorial). NOTE: M3 item 4 already plans
   per-region signpost/mural pools, so this ruling should decide the mural
   *language*, with per-region motifs deferred to M3.
3. Banked items 4002–4004 ruling: exempt-as-banked or delete.
4. Variant expansion (later, optional): wire banked picks + extra worn
   variants into props.yaml/tile_themes.yaml variant lists as new IDs.

**Exit gate:** acceptance scene passes full Part B rubric on the reference
device. **M7 dependency:** Steam capsule + screenshots do not start until
this gate passes. (Manifest + lint remain permanent infrastructure past exit.)

Live state (issue numbers, picks, ruling status) tracked in GitHub Issues
under `thread:art`; this section is the definition, not the state.

---

## 3. Dependency summary and what to start this week

**Critical path:** M0 → M1 → M2 → M3 → M6 → M7-launch. M4 (voice) paces the whole schedule and runs continuously; M5 slots anywhere after M1; M7's page/wishlist work starts immediately.

This week, concretely: (1) CC session for M0 items 1–2 (metric rename, PR flow); (2) design thread opens the Hollowmark taxonomy authoring (Track V, batch one); (3) Steam page skeleton (Track L); (4) your two rulings that unblock later work at zero build cost — archive deletion and the per-ring implement/remove call.

**Risks to monitor across the roadmap:** voice quality drift at volume (lint inside the pipeline, not after); M3 estimate variance (widest in the plan — re-estimate after the dialogue system lands); solo-bandwidth on simultaneous platform launch; scope creep re-entering through the boss designs (composition-tuned, template-built, per the reshape — resist bespoke mechanics per boss).

**The three sentences this roadmap exists to make true:** the game survives a phone call; Hollowmark never repeats herself in a run; there is always someone worth meeting in the next four floors.
