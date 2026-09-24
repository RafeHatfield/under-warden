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
