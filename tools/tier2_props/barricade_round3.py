#!/usr/bin/env python3
"""#207 ROUND 3 — X-frame candidates under the per-object cold-naming judge (Rafe, 2026-09-13).

    python3 tools/tier2_props/barricade_round3.py generate c1|c2     # 3 seeds each (REST)
    python3 tools/tier2_props/barricade_round3.py land c1|c2         # pick by hold -> ids 9810/9811
    python3 tools/tier2_props/barricade_round3.py restore            # the walked X back (from git)

Hypothesis to TEST, not to assume: the X reads as "crossed planks" because it has no height — it
lies in the gap instead of standing in it. c1 = crossed stakes with visible planted feet, the
crossing lashed (wood-dark: the binding slot is #208-blocked, flagged to the palette lock).
c2 = the same, taller than wide, spanning the gap edge to edge. A third only if both miss,
recorded with why. Judge: .claude/skills/frame-critic/cold_name_object.py, >= 4 of 5 seats, on
the X's own cell — proven able to FAIL on the walked frame first (§13.5, Ruling 47).
"""
import os, sys, json, subprocess
import numpy as np
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import projection_props as pp
import projection_generate as pg
import v2_bitforge as v2

CANDS = {
    "c1": ("barricade_c1", "a standing barricade: two thick rough-hewn timber stakes crossed and lashed "
                           "together where they cross with dark bindings, each stake's foot planted "
                           "on the ground, weathered dark wood, bark and splinters, no grass, matte, unlit"),
    "c2": ("barricade_c2", "a tall standing barricade: two thick rough-hewn timber stakes crossed high "
                           "and lashed where they cross with dark bindings, their feet planted on the "
                           "ground, a low bar lashed between the feet, weathered dark wood, bark and "
                           "splinters, no grass, matte, unlit"),
    # #207 R2 (2026-09-23): the bar is the sawhorse read — removed. Three crossings of sharpened
    # stakes span the gap; the prompt never names a bar, a rail or a beam, and refuses them.
    "c3": ("barricade_c3", "a barricade of sharpened stakes: three pairs of thick rough-hewn timber "
                           "stakes crossed and lashed where they cross with dark bindings, feet "
                           "planted on the ground, the stakes cut to sharp points, weathered dark "
                           "wood, bark and splinters, no grass, matte, unlit"),
}
NEGATIVE = {
    "c3": "sawhorse, trestle, horizontal bar, rail, beam, plank, planks, boards, lumber pile, "
          "fence rail, ladder, bench, table",
}
IDS = [9810, 9811]
CANVAS = (128, 64)
RAW = os.path.join(pp.GEN, "raw_r3")


def generate(c):
    model, mat = CANDS[c]
    os.makedirs(RAW, exist_ok=True)
    ledger = v2.Ledger(RAW)
    native, init = pp.template(c, model, CANVAS)
    init.save(os.path.join(RAW, "init_%s.png" % c))
    print("pool before: %s" % v2.pool(v2.balance()))
    for seed in pp.SEEDS:
        out = "%s_s%d" % (c, seed)
        if os.path.exists(os.path.join(RAW, out + ".png")):
            continue
        payload = {"description": mat + pp.SUFFIX, "negative_description": NEGATIVE.get(c, "planks, boards, lumber pile, fence, ladder, bench"),
                   "image_size": {"width": CANVAS[0], "height": CANVAS[1]},
                   "init_image": v2.enc(init), "init_image_strength": pp.STRENGTH,
                   "no_background": True, "text_guidance_scale": 8, "seed": seed}
        img, row = pg.call(payload, ledger, out, {"prop": c, "seed": seed, "projection": "RULED", "round": "#207 r3"})
        print("  %-14s %s" % (out, row["verdict"]))
    print("pool after: %s" % v2.pool(v2.balance()))


def land(c):
    t = pp.alpha(os.path.join(RAW, "init_%s.png" % c))
    meas = []
    for s in pp.SEEDS:
        p = os.path.join(RAW, "%s_s%d.png" % (c, s))
        if os.path.exists(p):
            g = pp.alpha(p)
            meas.append((s, float((t & g).sum()) / float((t | g).sum() or 1)))
    meas.sort(key=lambda m: -m[1])
    seed, hold = meas[0]
    native = pp.downsample(Image.open(os.path.join(RAW, "%s_s%d.png" % (c, seed))).convert("RGBA"))
    for i, tid in enumerate(IDS):
        native.crop((i * 32, 0, (i + 1) * 32, 32)).save(pp.DEST % tid)
    rec = dict(candidate=c, model=CANDS[c][0], seed=seed, hold=round(hold, 4),
               ordered=[dict(seed=s, hold=round(h, 4)) for s, h in meas], ids=IDS)
    json.dump(rec, open(os.path.join(pp.GEN, "r3_%s_pick.json" % c), "w"), indent=1)
    print("  landed %s seed %d hold %.3f -> %s" % (c, seed, hold, IDS))


def restore():
    for tid in IDS:
        subprocess.run(["git", "checkout", "--", os.path.relpath(pp.DEST % tid, pp.REPO)], cwd=pp.REPO, check=True)
    print("  restored %s from git" % IDS)


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "generate": generate(sys.argv[2])
    elif cmd == "land": land(sys.argv[2])
    elif cmd == "restore": restore()
    else: raise SystemExit(__doc__)
