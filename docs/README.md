# The Under-Warden — Documentation Index

_Last verified: 2026-07-12 against commit 86b6f10_

## Systems Reference (What's Implemented)

- [systems/INDEX.md](systems/INDEX.md) — **Start here**: index of all system docs with implementation status
- [systems/WEAPONS.md](systems/WEAPONS.md) — All weapons, damage types, enchantment, two-handed weapons
- [systems/ARMOUR.md](systems/ARMOUR.md) — All armour pieces, slots, AC values
- [systems/POTIONS.md](systems/POTIONS.md) — All potions, drink/throw mechanics
- [systems/SCROLLS_AND_WANDS.md](systems/SCROLLS_AND_WANDS.md) — All scrolls and wands, spell system
- [systems/RINGS.md](systems/RINGS.md) — All 16 rings (10 functional, 6 stubs)
- [systems/MONSTERS.md](systems/MONSTERS.md) — All monsters, AI types, special abilities
- [systems/STATUS_EFFECTS.md](systems/STATUS_EFFECTS.md) — All status effects, durations, values
- [systems/COMBAT.md](systems/COMBAT.md) — Combat resolution, crits, speed/momentum, damage-type resistance/vulnerability
- [systems/MAP_GENERATION_AND_PROPS.md](systems/MAP_GENERATION_AND_PROPS.md) — Floor gen, props, signposts, chests, placed traps
- [systems/LOOT_AND_IDENTIFICATION.md](systems/LOOT_AND_IDENTIFICATION.md) — Loot bands, categories, identification
- [systems/GROUND_HAZARDS.md](systems/GROUND_HAZARDS.md) — Fire and poison gas hazards (distinct from placed traps)
- [systems/DEPTH_BANDS_AND_BOONS.md](systems/DEPTH_BANDS_AND_BOONS.md) — Bands B1–B5, boon awards, ETP budgets

## Design

- [DESIGN_PRINCIPLES.md](DESIGN_PRINCIPLES.md) — Architecture, gameplay, and code quality principles
- [PLAYER_PAIN_POINTS.md](PLAYER_PAIN_POINTS.md) — Research on what players hate about roguelikes and our decisions
- [PLAYER_PROGRESSION_DOCTRINE.md](PLAYER_PROGRESSION_DOCTRINE.md) — Depth Boons system design (why not XP, mathematical constraints)
- [DEPTH_PRESSURE_MODEL.md](DEPTH_PRESSURE_MODEL.md) — H_PM/H_MP invariants, target curves, scaling math
- [TRADITIONAL_ROGUELIKE_FEATURES.md](TRADITIONAL_ROGUELIKE_FEATURES.md) — Feature wishlist from Oct 2025 (partially stale — cross-check against systems/INDEX.md)
- [floor-and-room-design.md](floor-and-room-design.md) — Floor and room layout design
- [YARL_MOBILE_LAYOUT_SPEC.md](YARL_MOBILE_LAYOUT_SPEC.md) — Mobile UI layout spec
- [2d-vs-iso.md](2d-vs-iso.md) — 2D top-down vs isometric rendering analysis

## Balance

- [balance/balance_system_overview.md](balance/balance_system_overview.md) — ETP encounter budgeting, loot bands, pity system
- [balance/BALANCE_PIPELINE_PLAYBOOK.md](balance/BALANCE_PIPELINE_PLAYBOOK.md) — Scenario → harness → metrics → tuning workflow
- [balance/tuning_cheat_sheet.md](balance/tuning_cheat_sheet.md) — Quick-reference for common tuning tasks
- [balance/combat_metrics_guide.md](balance/combat_metrics_guide.md) — Hit rates, combat ratio, scenario families
- [balance/loot_baseline.md](balance/loot_baseline.md) — Loot distribution targets and baseline data
- [balance/balance_coverage_map.md](balance/balance_coverage_map.md) — Scenario coverage map
- [balance/threat_archetypes.md](balance/threat_archetypes.md) — Baseline / escalator / spike archetype taxonomy
- [balance/b1_engagement_calibration.md](balance/b1_engagement_calibration.md) — B1 orc-density / loadout calibration record
- [balance/balance_strategy.md](balance/balance_strategy.md) — Balance strategy notes
- [balance/migration_loss_audit.md](balance/migration_loss_audit.md) — Python→C# migration parity audit
- [balance/balance_findings.md](balance/balance_findings.md) — **Running decision log** (FIND-NNN records; current-state, kept intact)

## LLM Testing

- [llm-testing/00-overview.md](llm-testing/00-overview.md) — LLM-driven testing system overview (Analyst + Player)
- [llm-testing/plan-analyst.md](llm-testing/plan-analyst.md) — Analyst thread (rubric engine, bug detection)
- [llm-testing/plan-player.md](llm-testing/plan-player.md) — LLM Player thread (hybrid decision mode)
- [llm-testing/shared-transcript-schema.md](llm-testing/shared-transcript-schema.md) — Enriched transcript schema
- [llm-testing/analyst-production-readiness.md](llm-testing/analyst-production-readiness.md) — Analyst production-readiness notes

## Build & Device

- [ios-export-pipeline.md](ios-export-pipeline.md) — **Headless iOS build + install** (`tools/ios_build.sh`): one command from tree to phone, and the failure modes it guards against

## Art

- [ART-BIBLE-v0.md](ART-BIBLE-v0.md) — **The art direction. The only live one.** Still a draft; clauses are marked LOCKED / PROVISIONAL / PLACEHOLDER and a PLACEHOLDER is not usable as a gate. The revision is stated in the file's own status line — this index does not mirror it.
- [ART-LOOP-PROCESS-v0.md](ART-LOOP-PROCESS-v0.md) — **How art work is done and certified.** The bible says what is correct; this says how a round runs and what may land. Two gates: a blind LLM critic ends a round, only Rafe in-scene lands an asset. §1.2 is the eyes-only rule: anything that gates an art round is a picture judged by eyes, instruments gate nothing, and no build installs without a frame-critic verdict.
- [FRAME-CRITIC.json](FRAME-CRITIC.json) — what the frame critic captures and what it shows the seat: surface, capture command, crop, asset bar, approved capture. The one file an art session edits when it moves to a new surface; the mechanism is `.claude/skills/frame-critic`.
- [art/tier1/](art/tier1/) — Tier 1 (floors and walls) scaffolding: the gate table, the identity-card schema, and the blind-critic prompt. Deliberately thin — a gate is `UNPROVEN` until it has been shown to go red.
- The **Oryx-conformance track is closed** (2026-08). Its bible, lint spec, acceptance-scene spec, gate package, and generation walkthroughs are archived under [archive/oryx-track/](archive/oryx-track/), each with a RETIRED banner. Do not work to them.

## Reference

- [reference_rogue_wizards.md](reference_rogue_wizards.md) — Primary visual/UX reference game analysis
- [archive/oryx-track/reference_tileset_research.md](archive/oryx-track/reference_tileset_research.md) — Tileset research — **RETIRED**, chose the Oryx baseline the art bible reverses

## Story

- [story/THE_UNDER_WARDEN_story.md](story/THE_UNDER_WARDEN_story.md) — Current Under-Warden narrative
- [under_warden_style_bible.md](under_warden_style_bible.md) — Under-Warden voice/style bible
- Superseded drafts and unimplemented story proposals have been moved to [archive/](archive/) — see `archive/MANIFEST.md`.

## Archive

- [archive/](archive/) — Historic development narrative and superseded docs (e.g. PHASES.md, stale handoff memos, old story drafts). See `archive/MANIFEST.md` for the full list and the reason each was archived. Nothing here is current-state reference.
