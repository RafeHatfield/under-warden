#!/usr/bin/env python3
"""#212 R1 RULED — the Λ-frame moves to an opening (Rafe, 2026-09-25, option d).

    python3 tools/tier2_props/lambda_opening.py generate     # 3 seeds (REST)
    python3 tools/tier2_props/lambda_opening.py land         # pick by hold -> id 9812 (1x1)

"The Λ-frame is a barricade; its placement is an opening, not a wall face. Move it to a gap or
corridor mouth where it spans edge to edge (same placement rule as the X-frame)." The review scene's
only two-cell gap holds the X, so B stands across the one-cell corridor mouth at (8,11), re-authored
one cell wide from `projection_mesh.barricade_b1` with its standing height kept (§12.2 does not give
way). Same pipeline as barricade_round3: projected template -> img2img at the ruled strength ->
pick by silhouette hold. Judge: cold naming on the barricade family word (subject `barricade`).
"""
import json
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import projection_props as pp
import projection_generate as pg
import v2_bitforge as v2

KEY, MODEL = "b1", "barricade_b1"
MAT = ("a barricade: two thick rough-hewn timber baulks stood on end and leaning together into a "
       "peak, lashed with rope where they meet, a low timber bar lashed across between the feet, "
       "weathered dark wood, bark and splinters, no grass, matte, unlit")
NEGATIVE = "sawhorse, trestle, ladder, bench, table, tent, planks, boards, lumber pile, fence"
IDS = [9812]
CANVAS = (64, 64)
RAW = os.path.join(pp.GEN, "raw_r1_lambda")


def generate():
    os.makedirs(RAW, exist_ok=True)
    ledger = v2.Ledger(RAW)
    native, init = pp.template(KEY, MODEL, CANVAS)
    init.save(os.path.join(RAW, "init_%s.png" % KEY))
    print("pool before: %s" % v2.pool(v2.balance()))
    for seed in pp.SEEDS:
        out = "%s_s%d" % (KEY, seed)
        if os.path.exists(os.path.join(RAW, out + ".png")):
            continue
        payload = {"description": MAT + pp.SUFFIX, "negative_description": NEGATIVE,
                   "image_size": {"width": CANVAS[0], "height": CANVAS[1]},
                   "init_image": v2.enc(init), "init_image_strength": pp.STRENGTH,
                   "no_background": True, "text_guidance_scale": 8, "seed": seed}
        img, row = pg.call(payload, ledger, out, {"prop": KEY, "seed": seed, "projection": "RULED",
                                                  "round": "#212 R1 opening"})
        print("  %-10s %s" % (out, row["verdict"]))
    print("pool after: %s" % v2.pool(v2.balance()))


def land():
    t = pp.alpha(os.path.join(RAW, "init_%s.png" % KEY))
    meas = []
    for s in pp.SEEDS:
        p = os.path.join(RAW, "%s_s%d.png" % (KEY, s))
        if os.path.exists(p):
            g = pp.alpha(p)
            meas.append((s, float((t & g).sum()) / float((t | g).sum() or 1)))
    meas.sort(key=lambda m: -m[1])
    seed, hold = meas[0]
    native = pp.downsample(Image.open(os.path.join(RAW, "%s_s%d.png" % (KEY, seed))).convert("RGBA"))
    native.save(pp.DEST % IDS[0])
    rec = dict(candidate=KEY, model=MODEL, seed=seed, hold=round(hold, 4),
               ordered=[dict(seed=s, hold=round(h, 4)) for s, h in meas], ids=IDS)
    json.dump(rec, open(os.path.join(pp.GEN, "r1_lambda_opening_pick.json"), "w"), indent=1)
    print("  landed %s seed %d hold %.3f -> %s  (native %s)" % (KEY, seed, hold, IDS, native.size))


if __name__ == "__main__":
    {"generate": generate, "land": land}[sys.argv[1]]()
