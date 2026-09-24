#!/usr/bin/env python3
"""#212 R1 — THE Λ-FRAME'S SCALE AGAINST THE WALL IT STANDS BEFORE. A builder's tool (§1.2): it
gates nothing, it measures where the round would aim.

Rafe's flip (walk, 2026-09-23): "it seems taller than the wall." Before anything moves, three
numbers the brief asked for, each derived from the engine's own sources rather than read by eye
(bible §13.12):

  1. the apex row against the cap's top row at (9,12)
  2. the prop's standing height against the wall's face height, in tiles
  3. whether the apex overdraws the cap band

Geometry comes from the capture log's `grid map` probe, the sprite from its tile PNGs, and the
placement from the same arithmetic DungeonRenderer applies to a prop against a wall
(shift = tileH - margin - PropBaseRun(tileH), PropBaseRun = 0.5 * 0.35 * tileH). The face band is
read from the wall family's face tile (its opaque rows are the face; the rest of the cell is the
cap's), because the face is drawn as a child sprite filling its own cell.

    python3 tools/cast_shadows/measure_lambda_scale.py [frame.png] [capture.log]
"""
import json
import re
import subprocess
import sys

import numpy as np
from PIL import Image

FRAME = sys.argv[1] if len(sys.argv) > 1 else "tools/tier1_floors/evidence/combined.png"
LOG = sys.argv[2] if len(sys.argv) > 2 else "tools/tier1_floors/evidence/combined.log"
SPRITES = ["src/Presentation/assets/tier1_ashlar/tier1_ashlar_9812.png",
           "src/Presentation/assets/tier1_ashlar/tier1_ashlar_9813.png"]
PROP_X, PROP_Y = 9, 12                 # the Λ's west cell (scene tier1_props_review.json)
PROP_BASE_DEPTH = 0.35                 # ReviewLighting.PropBaseDepth — the one constant the occluder uses
FACE_TILE = "src/Presentation/assets/tier1_walls/tier1_wall_9400.png"   # a face segment of the landed family


def grid(log):
    m = re.search(r"grid map: centre00=\(([-\d.]+),([-\d.]+)\) pitch=\((\d+),(\d+)\)", open(log).read())
    cx, cy, px, py = float(m[1]), float(m[2]), int(m[3]), int(m[4])
    return cx, cy, px, py


def opaque_rows(path):
    a = np.asarray(Image.open(path).convert("RGBA"))
    rows = np.where((a[..., 3] > 2).any(1))[0]
    return int(rows.min()), int(rows.max()), a.shape[0]


def main():
    cx, cy, px, py = grid(LOG)
    tile_h = py
    cell_top = cy + PROP_Y * py - py / 2          # top of the prop's cell = the reveal foot
    foot = cell_top                               # the wall (9,11)'s face ends here
    wall_top = foot - tile_h                      # top row of the wall cell (9,11) = its cap window's top

    tops, bots, native = [], [], None
    for s in SPRITES:
        t, b, native = opaque_rows(s)
        tops.append(t); bots.append(b)
    scale = tile_h / native
    margin = min((native - 1 - b) * scale for b in bots)
    run = 0.5 * PROP_BASE_DEPTH * tile_h
    shift = tile_h - margin - run
    apex = cell_top - shift + min(tops) * scale
    sprite_bottom = cell_top - shift + (max(bots) + 1) * scale
    sprite_h = (max(bots) + 1 - min(tops)) * scale
    standing_h = sprite_h - run                    # the base parallelogram's run is depth, not height

    # the face height, from the FACE TILE'S OWN opaque rows — the same derivation the prop gets.
    # (A first version located the arris from the frame's luminance profile and returned 2.5 px:
    # the fire's light makes the steepest step in that column something other than the arris. An
    # instrument that returned a wrong number without going red is §4.2's failure, so the frame
    # heuristic is gone and the source is read instead.)
    ft, fb, fnative = opaque_rows(FACE_TILE)
    face_h = (fb + 1 - ft) * (tile_h / fnative)
    arris = foot - face_h

    # The figure's height is NOT measured here. The placeholder is an Oryx 24x24 creature whose
    # display scale depends on the tileset loader, and a keyline heuristic on the lit frame
    # returned 2 px. It is read by eye off the frame (rows 553..601, ~48 px) and recorded as such
    # in the report, never as this script's output.
    figure_h = None

    commit = subprocess.run(["git", "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
    out = dict(
        producer=dict(script="tools/cast_shadows/measure_lambda_scale.py", commit=commit,
                      frame=FRAME, log=LOG),
        grid=dict(centre00=[cx, cy], pitch=[px, py]),
        wall=dict(reveal_foot_y=foot, arris_y=arris, cap_top_y=wall_top,
                  face_px=face_h, face_tiles=round(face_h / tile_h, 3)),
        prop=dict(apex_y=round(apex, 1), sprite_bottom_y=round(sprite_bottom, 1),
                  sprite_px=sprite_h, base_run_px=run, standing_px=round(standing_h, 1),
                  standing_tiles=round(standing_h / tile_h, 3), shift_px=round(shift, 1)),
        figure=dict(px=None, note="not measured by this script — see the round report (read by eye, "
                                  "placeholder Oryx knight, not Sasha)"),
        answers=dict(
            apex_below_cap_top_px=round(apex - wall_top, 1),
            apex_overdraws_cap_band_px=round(max(0.0, arris - apex), 1),
            prop_over_wall_height=round(standing_h / face_h, 2),
        ),
    )
    print(json.dumps(out, indent=1))


if __name__ == "__main__":
    main()
