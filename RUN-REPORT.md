# RUN REPORT — overnight queue, 2026-09-13

Lane `art/queue-2026-09-13`, off main `f8a3f40d`. Autonomy rules: no turn ends until a trigger
or the queue empties. Install-latest on PASS. **Palette lock fenced off** — nothing here touches
§5.1 / #204's ladder snapping.

| # | item | state |
|---|---|---|
| 5 | GPU headroom — one measurement with vsync off, report, no tuning | pending |
| 4 | Fire — expose the remaining PLACEHOLDER values (radius, tint) on the panel; rule nothing | pending |
| 1 | #207 barricade flips — A stands across the line, held, bindings gripping; B breaks the pitch; cold naming says "a barricade" | pending |
| 2 | #212 prop placement / depth order — props sit against walls under the top band | pending |
| 3 | #211 wall ends, corners, pillars gain the east face at ½ depth (§3.2) | pending |
| — | frame critic (5 seats, shadowed regime) → install-latest on PASS | pending |

Order: 5 and 4 are independent and small; 1 changes the props; 2 and 3 change the engine's
picture; the critic runs once, last, on the frozen tree.

## Log

### 5. GPU headroom — DONE (reported, not tuned)
- Vsync cannot be disabled on the SE: `DisplayServer.WindowSetVsyncMode(Disabled)` + `MaxFps 0`
  produced the identical 16.67 ms windows (iOS paces every frame). Process delta proves the frame
  is MET and nothing else.
- So the renderer's own per-viewport measurement was added to `[Perf]`: **CPU render
  1.41–1.65 ms/frame** (≈15 ms of headroom on the CPU side) with 216 wall occluders, 3 prop
  occluders, two shadow-casting lights, softness 12. **GPU: the timestamp query returns 0.00 on
  iOS Forward Mobile / Metal — no instrument.** The line now prints `NO-INSTRUMENT` rather than a
  zero. The next tool is Xcode's GPU frame capture, outside this queue.
- Evidence: `tools/cast_shadows/evidence/headroom_boot.log`, `headroom_install.log`.
  Build `headroom` (SKIPPED-REVIEW, vsync off, own bundle id) stays on the phone for the record.

### 4. Fire knobs — DONE (exposed, not ruled)
- Rig panel gains `fire r` (reach, 1.0–8.0 tiles, step 0.5; rebuilds the fire's falloff texture)
  and `fire tint` (a ladder of seven warm hues from `ff6a1e` to `ffd4a0`, `ff8a3c` at 1/7 as
  today's). Energy stays at Rafe's 1.6 mark. The settings line now carries `fire_radius` and
  `fire_tint` so a MARK WALK records them. Nothing ruled.

### 1. #207 barricade flips — DONE (cold naming PASS ×3, the word is "barricade")
- **The bar first:** `cold_naming.py`'s barricade term now accepts only the object (barricade,
  barrier, blockade, roadblock, cheval de frise, obstacle) and refuses what the walk and the card
  reject by name (bench, bed, bricks, sticks, pile, jumble, fence…). "timber / logs / planks" no
  longer count — Rafe's words: reads as wood but not as a barricade. Stricter, so the pre-§12.2
  calibration frame still MISSes it.
- **Variant A stands across the line, held:** an X-frame — two baulks stood on end and crossed at
  the centre (`tiltbox`, a new x–z rotation in `projection_mesh`), a bar lashed behind the
  crossing, rope wrapped at the crossing and both bar ends (§7.1). Chest-high (44 of 64). And
  it is PLACED across a line: the gap between the west wall (2,15) and the pillar (5,15) — in
  open floor the same frame read as "crossed broken planks"; closing a gap it read as
  "a cross-braced barricade" / "a broken wooden barricade".
- **Variant B:** "break the pitch" was done first (unequal, skewed, offset baulks, one leaning)
  and three of three seats still called it *a bench or table* — parallel beams at any pitch are
  furniture-shaped. So B is the family's other crossing: a Λ-frame, two baulks leaning together
  at the apex, a bar lashed low, rope at apex and bar ends. Read as "a wooden A-frame …
  wooden barricade", "sawhorse-style barricade". Placed against the north wall's east end at
  (9,12) beside the fire (its cast shadow), one cell in from the frame edge.
- Generation as before (projected template → pixflux surface at strength 150, three seeds, pick
  by hold: A 0.978, B 0.961). The scene's pocket at (3,13) sealed with A in place until B moved;
  the 56 scene tests are green. Cold naming PASS on three consecutive runs.
- Residual, for Rafe's eye: seats hedge with "broken" / "sawhorse-style" — a standing frame in a
  front elevation on a top-down floor is §3.2's inherent tension, and the seats read it as
  fallen or as a workshop object. The word is met; whether the register is, is the walk's.

### 2. #212 prop placement / depth order — DONE
- **The law, as built:** a floor prop whose north neighbours are all wall is AGAINST that wall.
  Its sprites shift north so its base line sits on the shared edge — the reveal's foot, which is
  where §3 says the wall's south surface rises from — measured off the sprite's own bottom
  transparent rows, not typed. It draws over the face (its row sorts later). The wall's **cap
  band** (the cap texture's upper half) is re-laid as a child at the prop's own z, added after
  it, so the top surface stays in front: **face < prop < cap band**. Its occluder follows the
  shift, so its shadow starts at the wall's foot too. Floor props only; #167's wall-top props are
  untouched. `DungeonRenderer` (the shift, `PropShift`, `CapBandCells`), `Tier1BoundaryWall`
  (`Tier1CapBand`, reported as `cap_bands_over_props=N`), `ReviewLighting` (the occluder).
- Between props in adjacent rows the order was already right (row-major sort); recorded.
- Demonstrated on B against the north wall beside the fire: base at the foot, apex under the
  band. Cold naming still PASS.
- Not done, named: where a prop's base sits in a cell that is NOT against a wall stays at the
  §12.2 render anchor (4 px above the cell's south edge); a base-line rule for open floor is
  #212's second half and would need a walk.

### 3. #211 wall ends, corners, pillars — DONE
- **Where:** a wall cell with floor to its east that is not a north–south run (north and south
  both wall). That is the east end of an E–W run, a corner, a pillar, and the corridor mouth's
  jamb — the "wall face band simply cut where the corridor passes through" that r002's seat
  flagged. Continuous runs stay two-plane (§3 walls). 5 east faces in the props room.
- **Geometry (§3.2, ½ per axis):** the block is a cell deep, so the run is 16 native right and
  16 up; the parallelogram hangs off the reveal's right edge, top-right corner on the cap's back
  edge, bottom-left on the face's foot. Each column samples one column of the cell's OWN face, so
  the courses recede diagonally — a side, not a stretched front. Value 0.75 of the face, a 1-px
  seam at the arris (§12.1 form), matching the ½-depth sides the landed props carry.
- Sorted at +5 (a wall-top prop's slot): at +1 it drew under the east floor cell's overlay
  children and was not in the frame at all — found by looking, not by the counter.
- `Tier1BoundaryWall` (`Tier1EastFace`, reported as `east_faces=N`).

### Frame critic — round 1, lane `art/queue-2026-09-13`: INSTALL-LATEST (five seats)
- All three shadowed plants dealt and caught; above the reference in 2 of 5, not below in 4 of 5;
  flagged by 3. Build id `334727fdfd7c`, commit `00596c72`. `CRITIC-VERDICT.json` and
  `history/r001-art_queue-2026-09-13.json` on disk.
- Flip list (6 items), NOT YET DISPOSED: the Λ-frame "straddles the face band and sits on the cap
  — reads as floating" (that is #212's own subject and the first thing to read on the walk); the
  X-frame wants a contact shadow (#207); a floor hotspot (#184); "redraw the slab from above"
  (§3.2 CLOSED); the right-column falloff and the face-band repeat (the reference's standing
  items, #202/#197).

## ▶ RESUMED (Rafe typed "continue")

### Dispositions — six flips, every one cited, `critic_gate.check_dispositions` → problems: none
| # | flip | state | cites | why |
|---|---|---|---|---|
| 0 | Λ-frame "straddles the face band, sits on the cap — floating" | **PARKED** | #212 | #212's own question, built this queue and not yet walked; Rafe's brief quoted. First thing on the walk. |
| 1 | X-brace wants a contact shadow / grounding | ROUTED-ALREADY | #207 | "props float" is #207's first word; a painted contact shadow is §6.3's baked shadow — the engine's cast shadow follows the occluder. |
| 2 | floor hotspot under the figure | ROUTED-ALREADY | #184 | the lamp at 1.60, held and ratified; the clamp is #184's lever. |
| 3 | "redraw the slab from above" | CLOSED | §3.2 | the candidate rejected on the handset; the marker landed as "the standing stone". |
| 4 | right column stays warm to the top | ROUTED-ALREADY | #205 | that column is the fire's wall; reach 4.0 tiles is PLACEHOLDER, now on the panel as `fire r` / `fire tint`, not ruled (item 4). |
| 5 | face-band beam-and-pin repeat | **ROUTED** | #208 | the bindings family delivered at one ink at every station; per-segment variety is that item's term under §8.3.1. Routed by verified citation — Rafe audits at the walk. |

### Install-latest — the gated build, no override
- `critic_gate`: verdict INSTALL-LATEST, build id matched the tree (verdict + history excluded from the id), plant
  caught, **GATE OPEN**. Walk preconditions ok (11 ruled fixes checked). Marker: occluders=all softness=12.0
  darkness=0.8 flicker=1 void ring 0; identity `d764757e+dirty` (the dirt is the dispositions).
- Exported, built, installed to the SE as `com.rafehatfield.catacombsofyarl.tier0` (two devicectl retries, then
  installed). Log: `tools/cast_shadows/evidence/queue_install/build.log`.
- `verify_on_device.sh`: first two runs refused — handset LOCKED. Third run (phone unlocked):
  **VERIFIED ON DEVICE** — identity `d764757e+dirty review=GATED`, booted `tier1_props_review`, rig live,
  `cap_bands_over_props=2 east_faces=5`, `shadows: mode=all wall_occluders=216 prop_occluders=3 fire_lights=1
  softness=12 flicker=on`, steady window 60.0 fps / render_cpu 1.37 ms / GPU NO-INSTRUMENT. Logs:
  `tools/cast_shadows/evidence/queue_install/{verify,DEVICE-tier1-boot}.log`.

### Handset housekeeping (not acted on — Rafe's call)
Still installed beside Tier0: `projA projOL projOR projOdeep projW` (the §3.2 candidates; projOdeep is the walked
one), `perfall perfnone headroom` (measurement builds, SKIPPED-REVIEW). All superseded by this install.

### Next
1. Rafe walks: the Λ-frame against the north wall (#212) first; then the X-frame across the gap (#207); the east
   faces on the corridor jamb, pillar and run ends (#211); the `fire r` / `fire tint` rows (#205, not ruled).

## WALK VERDICTS (Rafe, on the handset, 2026-09-13 — build d764757e+dirty / commit 5c368c9d)

Fire PASSES. Λ-frame, X-frame and the corridor jamb FAIL. Pillar end and run ends PASS.

### 0. Housekeeping — folded into PR #214
- **0a — was the walked build byte-for-byte a sha?** No. The handset reported `d764757e+dirty`:
  the dirt was the six dispositions written into `CRITIC-VERDICT.json` after the verdict and before
  the build (the verdict is outside the build id, so the gate matched; the identity line does not
  exclude it). The walked pixels are 5c368c9d's — that commit added only the verdict, dispositions
  and logs — but *"verdicts bind to a sha, never to +dirty"* is the law now: **the verdict is
  committed BEFORE the build**, so the identity reads a clean sha. The reinstall from a commit is
  the deliverable build below (one build carrying every ruling), not a second install of this one.
- **0b — handset cleared.** Uninstalled `projA projOL projOR projOdeep projW perfall perfnone
  headroom`. The SE now carries the shipped game and `tier0` only.
- **0c — FIRE RULED.** *"the fire is good."* PLACEHOLDER stripped from `fire r` / `fire tint`;
  ratified at the panel values — energy 1.6, reach 4.0 tiles, tint `ff8a3c`, flicker ON — and
  recorded in §6.2's live table as required engine flags (a fire's `light` block states all three;
  the engine supplies no default — `CorridorReviewSceneBuilder` throws on a missing one). #204
  stays open as-is.
- **0d — #211 partial.** Pillar end and run ends PASS: *"a wall turning a corner."* East face at
  ½ depth RATIFIED for wall ends and pillars (§3.2). The corridor jamb is REOPENED — round 1.

### Rounds owed, one PR each, in order (stacked on #214)
1. **#211 corridor jamb** — measure first: jamb face structure/hue vs the pillar's east face;
   wedge edge-gradient width vs three other cast edges (softness 12.0). Bar declared before
   the round. One-way door: any §6.2 value → STOP.
2. **#212 Λ-frame** — measure first: base row vs reveal-foot row at (9,12); cap pixels over the
   prop's lower half; Λ albedo vs X albedo unlit. Bar: base==foot, zero cap over lower half, one
   wood albedo.
3. **#207 X-frame** — hypothesis: no height. Up to three candidates, blind cold naming judges;
   all miss → STOP, bible gap.

Deliverable: one build on the SE with every ruling; walk order jamb → Λ-frame → X-frame.

## ROUND 1 — #211 the corridor jamb (branch `art/jamb-211`)

### Measured first (§13.11) — before any code moved
Captures at the ratified regime with flicker OFF for the pair (flicker phase is ±8 % noise on the
fire; the shadows measured are the lamp's), `--occluders all` vs `--occluders none`, ratio map =
lit/unshadowed per pixel (`tools/cast_shadows/evidence/jamb_m_{shadow,noshadow}.png`).

**(a) Is the jamb's east face a flat quad?** No. Both east faces are built by the same code
path — each samples one column of its OWN cell's face per output column, value 0.75, seam 0.5 —
and the frame agrees: in the unshadowed capture the jamb's east face has luminance std 42.4
(cv 0.55) against its front face's 30.6 (cv 0.40); the pillar's 30.8 (cv 0.77) against 12.0
(cv 0.41). Hue as r/b, east over own front: jamb 0.96, pillar 1.02. Same construction, same
quarry hue; the courses are there. The hypothesis "flat quad" is measured false.

**(b) Is the wedge edge narrower than the scene's other cast edges?** No — and the hard edge is
not a shadow edge at all. The penumbra on the corridor floor beside the jamb runs ratio
0.54→0.84 over ~28 px and 0.69→0.90 over ~30 px; the pillar's shadow edge on the floor
0.79→0.93 over ~33–40 px. All within one PCF kernel of each other at softness 12.0. The
zero-width edge in the picture is the **east-face sprite's own boundary**: it sits on the
first-surface light mask (inherited from its parent wall sprite, `Tier1BoundaryWall.cs` — every
child of a ring-1 cell takes `PropLightMask`), so it is lit at ratio **1.00** while the corridor
floor it hangs over sits in the jamb's own shadow at 0.54. A parallelogram lit at full, pasted
over a shadowed floor, with a 45° top edge parallel to the shadow's 45° boundary 32 px above it:
that is *"a hard diagonal edge, not a face; … a straight edge that looks like the occluder's
shadow mask."* The pillar's east face is on the same mask and passes because the lamp at
(6,12) is north-east of it — the face is toward the lamp, the floor beside it is lit, nothing
contrasts.

**Named cause: the light-mask exemption of the first surface, inherited by the east face.**
An east face is the first surface only when the lamp is east of the cell; when the lamp is
west it is behind the front face and §12.1a says it receives. Not a rig value. Not an occluder
without softness. Not a mask clip.

### Bar, declared before the build
1. **Face.** Under an ambient-only capture (lamp energy 0 — a builder's measurement, not a
   gate capture), the jamb's and the pillar's east faces have luminance cv within 0.10 and r/b
   within 0.10 of each other, and each carries course structure (cv ≥ 0.30).
2. **Edge.** In the ratio map at the regime, the step across the jamb's east-face boundary is
   ≤ 0.10 (no exempt-sprite step: the face carries the same shadow as the floor it hangs over),
   the wedge penumbra's 10–90 % width is within ±10 px of the mean of three other cast edges,
   and the pillar's east face stays lit (ratio ≥ 0.90 — the lamp is on its side).
3. **Gate.** Five blind seats, plants on both axes — the walls' construction plant
   (`cement-cap-shadowed`) and a cast-edge plant seeded from Rafe's shadow-walk cull (softness
   8.0: *"shadow edges are still traceable lines … a lantern doesn't throw searchlights"*) — no
   strong-majority regression on the shadowed reference; INSTALL-LATEST.
One-way door honoured: no §6.2 value moves. The fix is one mask bit on one new plane.

### Fix
`Tier1EastFace` takes the ground light mask (lit by every lamp, shadowed like the floor) instead
of inheriting the first-surface exemption. Its own cell's occluder then does the facing for
free: lamp west → the face is in the cell's shadow with the floor beside it; lamp east → lit.

### Measured after (the same instruments, `tools/cast_shadows/evidence/jamb/`)
| term | declared | measured | |
|---|---|---|---|
| 1 face cv within 0.10 of the pillar's | ≤ 0.10 | jamb 0.35 vs pillar 0.53 (Δ 0.18) | **NOT MET as written** |
| 1 face hue within 0.10 | ≤ 0.10 | 0.99 vs 0.99 | met |
| 1 course structure cv ≥ 0.30 | ≥ 0.30 | 0.35 | met |
| 2 ratio step across the outline | ≤ 0.10 | 0.37 (pre-fix 0.39) | **NOT MET as written** |
| 2 wedge penumbra within ±10 px of other cast edges | ±10 | 28–30 px vs 33–40 px | met |
| 2 pillar's east face stays lit | ≥ 0.90 | 0.93 | met |

**Impeachment (LOOP-PROCESS §8 — the bar is held frozen, cleared honestly, impeached here, never
re-tuned once the answer is visible).** Two terms were mis-specified, and both in the same way:
they named the wrong reference.
- Term 1 compared the jamb's face to the *pillar's*. Flat-lit, the family's own face tiles vary
  cv 0.13–0.36 on the fronts and 0.18–0.53 on the east faces, and each east face tracks its own
  front (jamb 0.35 vs 0.34; pillar 0.53 vs 0.36; run ends 0.30/0.23, 0.18/0.13). "Same
  construction as the pillar's" is true — one code path, one column of its own face per column —
  and the number that says so is the face against its own front, not against another cell's tile.
- Term 2 assumed a with/without-occluder luminance ratio is a pure shadow factor. It is not: the
  rig's shadow colour is hue-replacing (in shadow the lamp contributes the ambient hue, §12.1a
  "the same found rock, unlit"), so the ratio depends on the material's albedo and on the fire's
  share. The face at 0.98 and the floor ring at 0.61 are the same shadow on different materials.
  The measure that answers "does the face carry the floor's shadow" is the lamp-only RGB:
  **in the jamb's shadow the face reads (33,33,42) beside a floor at (40,41,53)** — the same
  ambient-hued dark at its own albedo — and **its hue flips with the shadow exactly as the floor's
  does** (2.41 → 0.80 against the floor's 1.96 → 0.82). Pre-fix it stayed at 2.89 in both frames.
  With a mask of 4 (no light at all) it reads (6,6,6): the shadow is the lamp's leak, as ruled.
Neither impeachment loosens anything the fix had to clear — the terms that carry the round's
question (the edge is not a shadow edge; the face now takes the shadow; the pillar keeps its
light; no rig value moved) are measured, not asserted. The gate is the critic's and the walk is
Rafe's; the bar was a builder's instrument and it gates nothing (bible §13.4, SKILL §2).

### Plants on both axes
- `cement-cap-shadowed` — walls, construction (existing).
- **`searchlight-edges` — SEEDED**: the shadow walk's provisional reference at softness 8.0,
  Rafe's cull verbatim (*"shadow edges are still traceable lines … a lantern doesn't throw
  searchlights"*), axis `cast-edge`, wrong on exactly that axis at deck scale.
- `jamb-hard-edge` — the walked frame, Rafe's jamb cull verbatim, **recorded, not dealt**: a
  30-px defect in a 750-px deck is a control a seat cannot see, and a missed correct plant voids
  the round on the judge (wall lane round 5, §1.2.1). Tagged axis `form`; Rafe may re-tag.
- Runner: `docs/FRAME-CRITIC.json` `axis` may be a list; the draw takes every entry wrong on
  either axis (`frame_critic.py pick_plant`). Single-axis decks unchanged (the queue's deck still
  draws its three).

### ⛔ STOP — broken-judge (lane `art/jamb-211`, round 3). The line is stopped. `STALL-REPORT.md`.
- r001 and r002 on this lane ran ONE seat each — the runner's default was 1, contradicting §13.13;
  both are on disk and are **not gate verdicts** (their INSTALL-LATEST lines in the stall report's
  table are single-sample). The default is five now (90a65911).
- r003, five seats, plants on both axes: seats 2, 3, 5 caught theirs; **seat 1 missed
  `searchlight-edges`** (ranked it FIRST, above the approved frame, and flagged nothing in it);
  **seat 4 missed `cement-cap-shadowed`** (ranked it 2nd, unflagged — a plant every seat has caught
  since 2026-09-12). Two live-plant misses in one round is the broken-judge term; findings are not
  read (`flip_list_withheld`). Rank data, for the record only: the build 3 of 4 in four seats,
  not below the reference in 3 of 5.
- **What Rafe rules:** (1) whether `searchlight-edges` (softness 8.0 against the reference's 12.0,
  fire at 2.5 tiles) is a control a seat must catch at deck scale — two of three seats did, one
  preferred it; if not, re-tag it (it stays, nothing is deleted) and the deck falls back to
  `cement-cap-shadowed` alone on this subject; (2) clear the guard by added artifact
  (`JUDGE-CLEARED.json`, lane `art/jamb-211`, rounds 1–3, the ruling verbatim) — a broken judge
  cannot be cleared by a lane ruling, only by his word on the judge.
- Nothing installs while this stands. Rounds 2 (#212) and 3 (#207) are MEASURED and their fixes
  and bars are written below; neither has run a seat — "never run a further round past a broken
  judge" (CLAUDE.md). The Λ-frame fix is a patch on disk (`scratchpad/patch_212.py`) waiting for
  the judge; the X-frame's per-object cold-naming judge (`cold_name_object.py`) is written and
  UNPROVEN (§13.5 — its `--expect FAIL` control on the walked frame has not run).

## ROUND 2 — #212 the Λ-frame (measured, bar declared, NOT BUILT — judge stopped)
**Measured** (flat-lit and regime captures under `tools/cast_shadows/evidence/jamb/`):
- Base row: the sprite's bottom wood row at y 544 against the reveal foot at 546/547 — the
  SPRITE is at the foot, as the report said. The eye was right anyway: §3.2's base parallelogram
  rises ½·0.35·cell = 11 px up-right from that row (the footprint occluder draws exactly it), so the
  whole footprint lay over the FACE. The feet were inside the wall; the eye put the prop on top of
  it. Both sentences were true and they were about different rows.
- Cap over the prop: `cap_bands_over_props=2` — the cap's upper half re-laid at the prop's z.
  The law says never.
- Albedo, flat-lit wood pixels: Λ (84,62,42) vs X (91,66,44); hue ratios 1.36/1.45 vs 1.38/1.49 —
  one wood. At the regime Λ reads L 73, r/g 1.74 against X's 47, 1.63 because Λ is 1.4 tiles from
  the fire (reach 4.0) and X is 6.3 — they are not under the same fire.
**Bar (declared):** (1) the base parallelogram's FAR edge on the reveal-foot row — the sprite's
bottom wood row at foot + 11 px (±1), on the floor cell; (2) zero cap pixels over the prop —
no `Tier1CapBand` node, the apex rows read as wood; (3) one wood albedo, flat-lit ±10 per channel
(met by construction, above); (4) plant `lambda-on-the-cap` (the walked frame, Rafe's cull
verbatim, axes `grounding` + `wood-value`, subject objects — a seat flagged this defect unaided in
the queue round, so it is deck-visible); five seats; INSTALL-LATEST.
**Fix (patch on disk, not applied):** `shift = cell − margin − ReviewLighting.PropBaseRun(cell)`
with `PropBaseDepth = 0.35` the one constant the occluder also uses; the cap-band re-lay removed.

## ROUND 3 — #207 the X-frame (judge written, unproven; candidates not authored)
Hypothesis to test: the X has no height. Judge: `cold_name_object.py` — one object located by the
seat's own WHERE inside a box, ≥ 4 of 5 seats with an accept word and no refuse word; must FAIL on
the walked frame first (§13.5). Candidates in order: crossed stakes with visible planted feet and
the crossing lashed in wood-dark (bindings #208-blocked, flagged to the palette lock); the same
taller than wide, gap edge to edge; a third recorded with why. All three miss → STOP, bible gap.

### ▶ JUDGE RULING (Rafe, 2026-09-13) — cleared; the jamb round is INSTALL-LATEST
- `JUDGE-CLEARED.json` written with his words: the plant stands (*"Edge width is the axis this
  round judges. A seat that ranks the cull first is scoring its own taste, not the reference."*);
  seats 1 and 4 re-drawn on the frozen r003 frame; the two one-seat rounds read as VOID and
  **count for nothing** (runner: `rounds_voided` in the marker; still numbered, in no guard, no
  series). Standing law kept: verdict committed before build; five seats never one; nothing deleted.
- Runner: a re-draw the marker names by round and seat runs under the broken-judge guard it
  answers — the first reading of "count for nothing" fed the voided rounds into the VOID streak
  and fired the guard once more (2f49ce81 fixes it; `prove_gate.py` green).
- **Re-draws:** seat 1 (searchlight-edges) CAUGHT; seat 4 (cement-cap-shadowed) CAUGHT — and put
  the build FIRST, above the reference. r003 holds five caught ballots: above the reference 1/5,
  not below 3/5 (majority), **INSTALL-LATEST**, build `7607dbdf11c6` at 2f49ce81.
- Five flips disposed (`check_dispositions: none`): the pillar-as-orphan-tile → #193 (its sixth
  seat); marker texture → #204 (it casts already, from its footprint); cracks across joints →
  #194; "cut the brazier by half" → CLOSED, fire RULED today; "pixel-step the shadow edges" →
  CLOSED, softness 12.0 ratified and the plant ruling names edge width as the axis.
