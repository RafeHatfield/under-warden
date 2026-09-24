#!/usr/bin/env python3
"""RE-AUTHOR THE BOUNDARY'S PROPS AT THE RULED PROJECTION — bible §3.2 (Rafe, 2026-09-12).

    python3 tools/tier2_props/projection_props.py generate    # templates -> 3 seeds each (REST)
    python3 tools/tier2_props/projection_props.py land        # pick by hold, land into the prop ids

Cabinet oblique, receding RIGHT, k = 1/2 per screen axis — projection_mesh.RULED, the geometry of
the build Rafe walked. Each prop is a MODEL projected through that function (structure authored),
and generation supplies surface at init_image_strength 150 (§13.7: generation cannot be told a
projection). Materials are the identity cards' words; colouring stays in kind (§12.2 froze it).

    B-PROP-001  marker        9800/9801  1x2   dressed post, rope lashings, driven pins
    B-PROP-002a barricade_a   9810/9811  2x1   crossed baulks — one lies ON the other (#207)
    B-PROP-002b barricade_b   9812/9813  2x1   bound stack
    B-PROP-003  fire          9820       1x1   THE ROUND EXCEPTION: the ring re-rendered as a
                                              true circle with a vertical body; the landed
                                              interior (fuel, flame) composited back — that read
                                              passed the gate and has no face to project

These ARE the game's prop ids. This is a props pass, not a ruling instrument; it lands under
§12's cold-naming walk and installs only on a frame-critic PASS.
"""
import json
import os
import shutil
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(REPO, "tools/pixellab/probe_6_4"))
import projection_mesh as pm          # noqa: E402
import projection_generate as pg      # noqa: E402
import v2_bitforge as v2              # noqa: E402

DEST = os.path.join(REPO, "src/Presentation/assets/tier1_ashlar/tier1_ashlar_%d.png")
GEN = os.path.join(HERE, "gen", "props_ruled")
RAW = os.path.join(GEN, "raw")
SEEDS = [1337, 1338, 1339]
STRENGTH = 150

PROPS = [
    # key, model, canvas (2x), ids (row-major), material description
    ("marker", "marker", (64, 128), [9800, 9801],
     "an old standing stone, a menhir: ONE single tall tapering block of pale weathered grey "
     "limestone, smooth worn faces, a dark rope lashing low around it held with driven iron "
     "pins, no ornament, no carving, matte, unlit"),
    ("barricade_a", "barricade_a", (128, 64), [9810, 9811],
     "a heap of thick rough-hewn timber baulks lying crossed over one another, weathered dark "
     "wood, bark and splinters, a rope lashing at the join, no grass, matte, unlit"),
    ("barricade_b", "barricade_b", (128, 64), [9812, 9813],
     "a standing A-frame barricade: two thick rough-hewn timber baulks stood on end and leaning "
     "together at the top, a bar lashed across low behind them, thick rope bindings wrapped "
     "tight at the apex and at every joint, weathered dark wood, bark and splinters, no grass, "
     "matte, unlit"),
    ("fire", "fire_ring", (64, 64), [9820],
     "a low ring of fire-blackened stones seen from above, soot and ash, dark grey rock, "
     "matte, unlit"),
]
# what the cold seats named it INSTEAD — refused by name on the endpoint's own field
NEGATIVE = {"marker": "bricks, masonry, stone courses, mortar joints, wooden crate, planks, barrel, "
                      "straps, cardboard box"}
SUFFIX = (". Keep the exact shape, outline and faces of the input. Dungeon prop sprite, "
          "no glow, no cast shadow, transparent background.")


def template(key, model, canvas):
    fn = pm.round_exception("RULED") if model == "fire_ring" else None
    # NO OUTLINE. The projector's 1px edge is a diagram convention for telling planes apart; the
    # generator kept it and five seats read 'heavy black outlines' on the timber — §12.1's baked
    # outline, ruled out 2026-08-24. Planes separate by value alone in what ships.
    native = pm.render("RULED", model, canvas, zoom=0.5, fn=fn, outline=False)
    arr = np.asarray(native).astype(int)
    rng = np.random.RandomState(abs(hash(key)) % (2 ** 31))
    noise = rng.randint(-pg.GRAIN, pg.GRAIN + 1, size=arr.shape[:2] + (1,))
    op = arr[:, :, 3:4] > 0
    arr[:, :, :3] = np.clip(arr[:, :, :3] + noise * op, 0, 255)
    native = Image.fromarray(arr.astype(np.uint8))
    return native, native.resize(canvas, Image.NEAREST)


def generate():
    os.makedirs(RAW, exist_ok=True)
    ledger = v2.Ledger(RAW)
    print("pool before: %s" % v2.pool(v2.balance()))
    for key, model, canvas, ids, mat in PROPS:
        native, init = template(key, model, canvas)
        init.save(os.path.join(RAW, "init_%s.png" % key))
        for seed in SEEDS:
            out = "%s_s%d" % (key, seed)
            if os.path.exists(os.path.join(RAW, out + ".png")):
                continue
            payload = {"description": mat + SUFFIX,
                       "negative_description": NEGATIVE.get(key, ""),
                       "image_size": {"width": canvas[0], "height": canvas[1]},
                       "init_image": v2.enc(init), "init_image_strength": STRENGTH,
                       "no_background": True, "text_guidance_scale": 8, "seed": seed}
            img, row = pg.call(payload, ledger, out, {"prop": key, "seed": seed, "projection": "RULED"})
            print("  %-18s %s" % (out, row["verdict"]))
    print("pool after: %s" % v2.pool(v2.balance()))


def alpha(p):
    return np.asarray(Image.open(p).convert("RGBA"))[:, :, 3] > 0


def downsample(im):
    a = np.asarray(im, dtype=np.uint8)
    return Image.fromarray(a[::2, ::2].copy())


def land():
    picks = {}
    man_rows = []
    for key, model, canvas, ids, _ in PROPS:
        t = alpha(os.path.join(RAW, "init_%s.png" % key))
        meas = []
        for s in SEEDS:
            p = os.path.join(RAW, "%s_s%d.png" % (key, s))
            if not os.path.exists(p):
                continue
            g = alpha(p)
            meas.append((s, float((t & g).sum()) / float((t | g).sum() or 1)))
        meas.sort(key=lambda m: -m[1])
        seed, hold = meas[0]
        picks[key] = dict(seed=seed, hold=round(hold, 4),
                          ordered=[dict(seed=s, hold=round(h, 4)) for s, h in meas])
        gen = Image.open(os.path.join(RAW, "%s_s%d.png" % (key, seed))).convert("RGBA")
        native = downsample(gen)
        if key == "fire":
            # THE ROUND EXCEPTION'S COMPOSITE: the landed interior (fuel + flame) inside the
            # ring's inner radius — from the sprite the gate could name — over the new ring.
            # The ring's top circle sits (r + h) above the object's base line, which render()
            # anchors at H - 4*zoom; the object is centred horizontally.
            r_out, r_in, hgt, zoom = 26, 19, 8, 0.5
            w, h = native.size
            cx, cy = w / 2.0 - 0.5, (h - 4 * zoom) - (r_out + hgt) * zoom - 0.5
            yy, xx = np.mgrid[0:h, 0:w]
            mask = (xx - cx) ** 2 + (yy - cy) ** 2 <= ((r_in - 1) * zoom) ** 2
            old = np.asarray(Image.open(DEST % 9820).convert("RGBA"))
            na = np.asarray(native).copy()
            sel = mask & (old[:, :, 3] > 0)
            na[sel] = old[sel]
            native = Image.fromarray(na)
        w_cells = 2 if canvas[0] == 128 else 1
        h_cells = 2 if canvas[1] == 128 else 1
        for i, tid in enumerate(ids):
            col, row = i % w_cells, i // w_cells
            cell = native.crop((col * 32, row * 32, (col + 1) * 32, (row + 1) * 32))
            cell.save(DEST % tid)
        opaque = float((np.asarray(native)[:, :, 3] > 0).mean())
        print("  %-12s seed %d hold %.3f  -> ids %s  opaque %.2f" % (key, seed, hold, ids, opaque))
        man_rows.append(dict(prop=key, model=model, ids=ids, seed=seed, hold=round(hold, 4),
                             opaque=round(opaque, 3)))
    os.makedirs(GEN, exist_ok=True)
    json.dump(picks, open(os.path.join(GEN, "picks.json"), "w"), indent=1)
    # the props manifest: method and projection, so the next session does not re-derive it
    mf = os.path.join(HERE, "MANIFEST.json")
    man = json.load(open(mf))
    man["projection"] = {
        "clause": "bible §3.2 — RULED (Rafe, on device, 2026-09-12)",
        "rule": "cabinet oblique, receding RIGHT, k = 1/2 per screen axis (projection_mesh.RULED)",
        "round_exception": "the fire ring: true-circle top, vertical body; interior composited from the landed sprite",
        "method": "structure authored as a 3D model and projected (projection_mesh.py); surface by v2 "
                  "/create-image-pixflux img2img at strength 150 from the projected template, 3 seeds, "
                  "pick by silhouette IoU; 2:1 by sampling",
        "landed": man_rows,
    }
    json.dump(man, open(mf, "w"), indent=1)
    print("manifest: projection recorded")


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    {"generate": generate, "land": land}.get(cmd, lambda: print(__doc__))()
