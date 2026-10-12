# R1b r004 — STOP on the cold-naming bar

**The panel passed. The R1 bar did not.** Per the R1b ruling ("if it misses, STOP with the seats'
words"), no build and no PR. Ending for Rafe.

## The panel (r004, after the r003 ruling)

INSTALL-LATEST. Five of five seats caught their plants (lambda-against-wall-r1b,
objects-isometric-shadowed); the build ranked at or above the reference in 5 of 5 seats and first
in 2. Seven flips, all ROUTED-ALREADY with resolving citations (#210, #204 ×2, #194 ×2, #205,
§10.5). The gate opens. `history/r004-art_lambda-opening-212.json`.

## The bar: cold naming of the Λ at the corridor mouth (8,11)

`cold_name_object.py --subject barricade --box 495,440,575,550` on the r004 build frame
(`cold_name_r004.json`). Needs ≥ 4 of 5. **Got 1 of 5.**

| seat | verdict | the seat's words |
|---|---|---|
| 1 | MISS | wooden plank or broken door propped at an angle in the wall gap |
| 2 | MISS | an object i cannot identify (a leaning wooden post with a reddish top, possibly a broken signpost or stake) |
| 3 | NAMED | a wooden crossed-stake piece at the wall edge, possibly another barricade fragment |
| 4 | MISS | a broken wooden object in the wall gap, possibly a smashed sign or door frame — an object i cannot identify |
| 5 | MISS | an object i cannot identify (an angled orange wooden plank with a block, at the doorway edge) |

No seat saw an A-frame.

## Baseline: the Λ at its old place, same scene, same tool (diagnostic, not a round)

`lambda-against-wall-r1b.png`, box 570,480,700,565 (`cold_name_baseline_old_position.json`).
**3 of 5** named it a barricade, and **all five** saw an A-frame:

| seat | verdict | the seat's words |
|---|---|---|
| 1 | NAMED | wooden a-frame barricade or stake frame |
| 2 | NAMED | a wooden a-frame barricade (sawhorse-style spiked barrier) |
| 3 | MISS | a wooden a-frame sawhorse or trestle |
| 4 | MISS | wooden a-frame trestle or brace leaning against the wall, with small orange crate-like pieces behind it |
| 5 | NAMED | a wooden a-frame barricade or sawhorse |

So the move is a regression (3/5 → 1/5, A-frame read 5/5 → 0/5). The old place never held the
≥ 4/5 bar either: "sawhorse/trestle" took the other two seats, which is the risk the R1b brief named.

## Why: the west half of the Λ is drawn over

`lambda_new_vs_old.png` (left: r004 at (8,11); right: the old place). The landed sprite
(`tier1_ashlar_9812.png`, 32×32) has both legs and the crossbar. In the frame only the east leg
reads. The west leg and the west half of the crossbar sit exactly under the corridor mouth's dark
diagonal jamb wedge — the wedge an earlier round placed at deck (505–540, 390–430), i.e. frame
(505–540, 480–520), and routed to #211. TopDownRenderer draws wall faces at Z +5 and floor props
at +2 (the depth-order defect already measured and routed to #211 in the R2 dispositions), so the
wedge paints over the prop. What's left is one lit plank leaning on a stub, which is what four
seats described.

This is read from the pixels and the documented Z order; it has not been confirmed by a render
with the order changed.

## What's being asked for

A ruling. Options are in the session's report to Rafe.
