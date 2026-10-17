# R1b r005 — the device gate refuses: `lane-window-held` fails on the new floor plan

The critic's gate is OPEN (r005 INSTALL-LATEST, c8932ed3, eight flips disposed). The device
gate's preconditions (`tools/tier0_harness/gate_precheck.py`, `docs/GATE-CONDITIONS.json`) refuse:

    4. ruled fixes absent from this build: lane-window-held
       (on-lane 0.1051 BELOW / lane-vs-flank 0.5141 against §13.8's 0.1440 (identity 0.685),
        measured on combined)

No override exists for this condition and none was sought. **No build installed.**

## When it broke: the R1b carve, not the depth fix

The gate's own instrument (`measure_lane_window.measure`) on every delivered frame of this lane:

| frame | what changed | on-lane | lane-vs-flank | window |
|---|---|---|---|---|
| 443b4a39 (R2, the last install) | — | 0.3136 | 0.3144 | held |
| 303aebdd (r003) | R1b floor plan: bay (11–13,7–11), doorway (13,6), Λ at (8,11) | 0.1063 | 0.5179 | **broken** |
| 8f24eee0 (r004) | — | 0.1061 | 0.5170 | broken |
| depth_confirm / c8932ed3 (r005) | depth fix | 0.1057 / 0.1051 | 0.515 | broken |

## Why: the walked lane moved under the lamp

The traffic field (from the capture log) re-routes. In R2 the trodden lane ran up the corridor
(column 8, rows 8–10). With the Λ across the corridor mouth, traffic goes round through the new
bay and doorway, and in the lit part of the frame the lane now runs through the tiles between the
player's lamp and the fire.

Lane tiles (traffic level ≥ 7) the window judges (median luminance ≥ 60):

| frame | judged lane tiles (median luminance) |
|---|---|
| R2 | (6,12) 190.6 |
| r005 | (8,12) **228.2**, (7,13) **221.3**, (6,14) 138.0 |

The lane now lies on the two brightest floor tiles in the frame. There, the polish washes the
joints out and the stones lose their identity: "a path you cannot recognise as the same masonry
is a decal". That is the failure the law names, measured on this build, and the "hot on worn lanes"
tuning is #184's. `POLISH_LANE_GAIN` (0.3) is ratified and §6.2-adjacent: a one-way door, not
moved.

## What's being asked for

A ruling. Options are in the session's report to Rafe.
