# R1b after the depth fix — STOP on the naming bar

Ruling (Rafe, R1b-r004, 2026-10-12): fix the depth order as #211's first slice, confirm the cause by
render, re-run the per-object naming check. Bar unchanged: ≥ 4/5 name a barricade-family word;
"A-frame", "sawhorse", "trestle", "plank", "sign", "door" are misses. If naming misses: STOP with
the words; the next round is a design round on the X-frame pattern, not authored until now.

## 1. The cause, confirmed by render

`Tier1BoundaryWall` east face ZIndex +5 → +1 (`src/Presentation/Map/Tier1BoundaryWall.cs`). One
muted capture with the order changed (`tools/tier1_floors/evidence/depth_confirm.png`).
`depth_before_after.png`: left r004, right the fix. The Λ's west leg and the whole crossbar,
hidden under the face of (7,11) at 2·11+5 = 27 over the prop's 24, now draw. The X-frames' left
crossing, overdrawn by the west wall end's face (the R2 #211 routing), is whole too. No face
disappeared: they still draw over the floor they hang across. Floor overlays drew nothing in either
capture (BAKED-BY-FLOOR), so nothing sits between +1 and +5 today. Fast suite green (2564).

## 2. Naming, same tool, same box (495,440,575,550) — `cold_name_depth_fixed.json`

| seat | the seat's words | tool (`barricade`) | under the ruling's miss words |
|---|---|---|---|
| 1 | a wooden x-shaped barricade (crossed stakes / sawhorse) | NAMED | MISS — sawhorse |
| 2 | a wooden a-frame sawhorse or crossed-plank barricade | NAMED | MISS — a-frame, sawhorse, plank |
| 3 | a wooden x-shaped barricade (sawhorse-style spiked barrier) | NAMED | MISS — sawhorse |
| 4 | two crossed wooden beams leaning against the wall, like an a-frame brace or sawhorse | MISS | MISS |
| 5 | wooden a-frame barricade (crossed timber beams) | NAMED | MISS — a-frame |

The tool's `barricade` subject does not refuse "sawhorse" and printed PASS 4/5. Scored with the
ruling's miss words as refusals (the tool's own rule: an accept word and no refuse word), it is
**0 of 5**. The same five answers, re-scored, no seat re-run
(`cold_name_depth_fixed_rescored.json`).

Progress across the three readings of this Λ: 1/5 hidden (r004) → 3/5 at the old wall place →
4/5 "barricade" with every one hedged "sawhorse/A-frame" after the fix. The fix restored the
shape; the shape reads as a sawhorse.

## 3. Next, per the ruling — NOT authored

A design round on the X-frame pattern: two narrow A-frames side by side in the one-cell mouth,
sharpened braces pointing down the corridor (away from the nearest orc fire), wood ladder only.
