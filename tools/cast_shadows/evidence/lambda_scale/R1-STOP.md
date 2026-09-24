# #212 R1 — the Λ-frame's scale: STOP, §1.1.4(b), measured, NOT BUILT

**Item:** #212, Rafe's walk flip (2026-09-23): *"it seems taller than the wall."* Grounding passed at
his eye (*"it looks against the wall"*), so #212's grounding law closes; this is the new scale flip.

**The trigger:** the bible contradicts itself on the question this round needs answered, so this is a
ruling gap under §1.1.4(b), quoted below. The brief's own clause gives the STOP: *"If the measurement
shows the apex already below the cap top and the read is projection, that is a bible gap on §3.2 →
STOP and return with the numbers."* The apex is already below the cap top. The read turns out to be
**real height, not projection**, and that is exactly why the bar cannot be built. Nothing was
generated, placed or landed, and no seat was spent. Under §1.1.5 the run continued to R2.

## The numbers

Derived by `tools/cast_shadows/measure_lambda_scale.py` from the grid probe, the sprite tiles and the
wall family's face tile (`measure.json`), all at commit `1dc5c202`, on the landed build's frame
`tools/tier1_floors/evidence/combined.png` (xframe r001). They agree with the frame: see
`rows_annotated.png` (exposure ×2, rows marked).

| | screen y | |
|---|---|---|
| cap top: the top row of wall cell (9,11), the cap window's top | **481.5** | |
| **Λ apex** (sprite row 0, after the against-wall shift of 50.8 px) | **494.7** | 13.2 px **below** the cap top |
| arris: the face's top row (face tile opaque in native rows 16–31) | **513.5** | the apex is 18.8 px **above** it |
| reveal foot | 545.5 | |
| placeholder figure (Oryx knight, not Sasha), read by eye | ~553 → ~601 | **~48 px** |

| height | px | tiles | |
|---|---|---|---|
| wall face: the wall's standing height in §3's two-plane grammar | **32** | **0.50** | |
| Λ sprite | 62 | 0.97 | |
| Λ standing height (the sprite minus the ½·0.35·cell base run, which is depth) | **50.8** | **0.79** | **1.59× the wall** |
| figure | ~48 | ~0.75 | the Λ's sprite is ~1.3× the figure |

**The three answers the brief asked for:**
1. **Apex vs cap top:** 13.2 px below. `apex ≤ cap top` already holds.
2. **Prop height vs face height:** 0.79 tile against 0.50 tile, so the prop stands **1.59× the wall's height**.
3. **Overdraw:** yes. The apex covers the lower 18.8 px of the cap band.

## Over-scale, or projection? Over-scale, in world height

The cap band above the arris is the wall's **top surface receding in depth** (½·d on screen under
§3.2's geometry, 32 px for a one-cell wall), not more wall height. A prop standing in front of the face
rises "above the top of that wall" when its **height** passes the wall's height, which on screen is
when it crosses the **arris**, not the cap top. The Λ crosses the arris by 18.8 px because it is
50.8 px tall against a 32-px wall. Rafe's eye read the geometry correctly. The short face is not a
projection artefact; it is the wall's authored height (half a tile). So:

- **`apex ≤ cap top` is the wrong row for the law.** It is already satisfied by a prop 1.59× the
  wall's height, and it would still be satisfied by one about 2× (apex at the cap top = standing
  ≈ 64 − 11 = 53 px against the wall's 32).
- **The law in world terms** (*"never rises above the top of that wall"*) puts the apex at or below
  the **arris**: standing height ≤ **32 px** (0.50 tile).

## Why this cannot be built: the bible's contradiction, quoted

- **§12.2** (RULED 2026-09-10): *"A barricade is **chest-high and wide**."* *"A prop's size
  relative to the tile and to the character is **exaggerated** until its identifying feature reads
  at device 1×."* This round's bar adds *"still larger than true scale, never cartoon."*
- **§3 walls** (RATIFIED 2026-09-09) draw the face at **16 native px, half a tile**, which is
  **shorter than the figure** (~48 px): the walls themselves are far below true scale beside a person.
- **The new law:** *"a prop standing against a wall never rises above the top of that wall."*

Chest-high at true scale beside a ~48-px figure is **~31 px** (≈0.65 of the figure). The wall law
caps the prop at **32 px**. So "larger than true scale" and "not above the wall" leave a window of
**about one pixel**, and the brief's own **under-scale plant** (*"Λ at true scale, toy"*) sits
**inside** that window. It is the only Λ the law would admit. The bar's two terms cannot both be
met by a Λ the brief itself would reject.

⚠ The figure is the placeholder knight, not Sasha (tier four, §10). If Sasha is authored taller, true
scale rises with her and the window becomes negative.

## What Rafe rules. Options, not a recommendation to build

1. **The law is world height; §12.2 yields for props against walls.** The Λ stands ≤ 32 px, at true
   scale. Cost: the toy read §12.2 was written to prevent, on the one prop that is against a wall.
2. **The law is the screen row `apex ≤ cap top`.** It is already met, so the Λ stays as landed and
   the "taller than the wall" read is closed as the face's authored height. Cost: Rafe's own flip
   stays unanswered on the handset.
3. **Walls are too short for the objects §12.2 asks for.** Raise §3's face height. A **one-way door**
   (§3 ratified; §1.1.4(a)); it touches every wall, the cap, the bindings and the shadows.
4. **The barricade does not stand against a wall.** Move B off the north wall (a scene edit), so the
   wall-height law never applies to it. Cost: #212's placement law loses its only instance in the
   review scene.

## Plants: prepared, not captured

The brief's plants (over-scale, apex 1 tile above the cap; under-scale, true scale) were **not
drawn**, because no bar exists for them to control yet. They are one scale edit each from the landed
sprite once the ruling names the bar. Ruling 47 applies: each proves it can be caught before its
round counts.
