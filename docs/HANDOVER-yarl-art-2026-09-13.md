# YARL ART DIRECTION — HANDOVER TO A NEW CHAT (2026-09-13)

*Paste this as the first message of the new chat. It carries the state of the art track so
nothing is re-derived. The repo is ground truth for everything below; if this doc and the
repo disagree, the repo wins.*

## Who does what
- **Rafe** merges PRs, walks builds on the iPhone SE, and gives verdicts in his own words
  (three sentences: *wowed / this works, keep going / no + why*). He is the human gate
  (§13.2) and the only thing that seeds `approved_capture`. He is not a graphic artist and
  says so; decisions are framed as eye-verdicts, and Claude translates them into law.
- **Claude (this chat)** drafts CC session prompts (paste blocks), vets reports against the
  repo not the prose, and rules on design questions Rafe delegates. Claude never touches the
  repo directly.
- **Claude Code (CC)** builds, runs the frame-critic, opens PRs, installs to the phone, and
  runs autonomously under the autonomy amendment. CC sessions end turns and wait — a nudge
  restarts them. One worktree per session.

## The game
Catacombs of Yarl / *The Under-Warden* — solo-built iOS roguelike (Godot 4 + C#, 32×32 tiles
at ×2 on the SE, portrait). Register: "the world does not notice you" — administered
underworld, nothing staged, wear is earned, light withdraws with depth. Story bible:
`docs/THE_UNDER_WARDEN_story.md`. Art bible: `docs/ART-BIBLE-v0.md` (v0.14+). Process:
`docs/ART-LOOP-PROCESS-v0.md`. Review mechanism: `.claude/skills/frame-critic/SKILL.md`.
CLAUDE.md is standing law for CC.

## STATE — the environment is DONE and on main (f8a3f40d)
Every layer ratified at Rafe's gate, every value a required engine flag (silent defaults
drift):
- **Floor family** (tier one, closed): ashlar bond, edge-matched, corner-keyed values,
  field-scale cracks, differential wear keyed to a TrafficField, route polyline. Palette
  ladder 11 rungs (working scaffold, NOT the locked palette). Anchor = area-weighted mean.
- **Walls**: two-plane (§3 ratified), hued to the floor's quarry, caps with field-scale slab
  structure, bindings on faces only. §6.5 row 1 retired (cap never brighter than floor).
- **Object projection (§3.2 RULED)**: cabinet oblique, receding RIGHT, ½ depth; round
  objects true-circle top + vertical body; characters unaffected. Isometric and flat
  front+top rejected by Rafe on device.
- **Rig (§6.2 re-ratified with shadows, 2026-09-13)**: energy 1.60, radius 6.0, falloff
  1.00, ambient 1.50 (units in the table), void ring 0, shadows on (occluders all), softness
  12.0, darkness 0.8, fire 1.6, flicker ON (ruled: the tended fire is §9.2's exception).
  Lamp fix #174 landed (floor and walls on one arithmetic). Shadow = the ambient, never
  black paint; the first surface is light-mask-exempt, everything behind receives, every
  edge casts. SE holds 60fps (vsync-locked; GPU headroom unmeasured).
- **Props landed**: marker stone ("the standing stone"), orc fire ("a well laid out
  campfire") — passed Rafe's cold-naming test at readability scale (§12.2: real proportions,
  larger than true scale, per Warcraft/D2, never cartoon). Barricade family reads as wood
  but not as a barricade — flips on #207.
- **Shadowed reference seeded** from Rafe's marked frame (approved/shadowed-2026-09-13.png).

## THE MECHANISM (why it can run overnight)
Frame-critic: blind `claude -p` seats judge delivered frames only; instruments are builder's
tools and gate nothing (§13.11/§13.12: an instrument's input must be no wider than what it
measures; assertions derive, never copy). Picture-plants per axis AND subject, captured under
the deck's lighting regime; a plant miss voids the SEAT (re-drawn once), not the round.
PASS-INSTALL = five-seat vote, block only on strong-majority regression (≥4/5 below the
reference), item's measured exit met, no unrouted flags (dispositions by verified
citation, §13.14). Progress guard stops on stall/thrash/no-change, not laps; VOIDs don't
count; clears by added artifact, never by deleting evidence. **Autonomy amendment
(§1.1.4/§1.1.5)**: CC returns to Rafe only for a one-way door, a bible gap, or a broken
judge; everything else CC rules under the bible and records. Device-gate culls are
seat-blind (§13.2); rig walks may run SKIPPED-REVIEW (§1.2.2a) but never seed a reference.
Only Rafe's walk seeds `approved_capture`.

## OPEN, none blocking (overnight queue may already be running)
#207 barricade flips (stand across the line, held; break stacked pitch) · #212 prop placement
against walls · #211 wall ends/corners get the east face at ½ depth · fire's remaining
PLACEHOLDER values on the panel · GPU headroom (vsync off) · #201 PARKED until palette lock
(3× face-set expansion) · #193 contrast-preserving shoulder · #197 hatch motif · #198 halo ·
#204 lamp warms grey stone toward wood.

## THE DOORS — Rafe's, next
1. **Sasha's identity card** — `docs/SASHA-IDENTITY-CARD.md`, DRAFT. Rafe's brief: competent,
   self-assured, understated; well-worn non-ostentatious gear; expression = posture and head
   tilt; Hollowmark (brass wand, always present) is the warmth channel. **Five questions
   open**: weapon (sword vs killer's short blade), head (bare/hood/hat), expression ruling
   (posture-only?), facings (2 mirrored vs 4), the knee (worth a frame?). The current hero
   sprite is the Oryx placeholder — no appearance work until the Sasha session.
2. **The palette lock (§5.1)** — deferred since the bible was drafted; the corpus now exists
   to derive from. One-way door. Precedes the orc rehearsal and Sasha. Binding/metal/rope
   slots are its first requirement (#208).
3. Sequence after: props polish → palette lock → orc as pipeline rehearsal (skeleton/rotation/
   attachment tools proven on a body nobody's attached to; "Warcraft's skeleton, Yarl's
   skin") → Sasha.

## WORKING RULES LEARNED (don't relearn)
- Art PRs target main, one at a time; a stacked PR's "merged" means merged into its base.
- Every build announcement lists the rulings it contains; walks are Rafe's cadence, not
  per-PASS. If Rafe sees no build, check the RIG panel for the expected knobs before
  assuming it's missing.
- A seat's percept is evidence; its explanation is a hypothesis — measure before building.
- Named failures become laws; gates prove they can fail before they count (Ruling 47);
  bars are declared before rounds and name their destination.
- Rafe's supervision has out-instrumented the instruments repeatedly (keyline, grey walls,
  tile-quantized wear, the projection door). Trust the eye; translate it.
- Every prompt Claude writes must end with something Rafe can see, or say what it buys.
