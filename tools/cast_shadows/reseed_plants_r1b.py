#!/usr/bin/env python3
"""RE-SEED THE DECK'S PLANTS AGAINST THE CHANGED FLOOR PLAN — #212 R1b (Rafe, 2026-09-30).

    python3 tools/cast_shadows/reseed_plants_r1b.py [--only name,name]

    "Because the floor plan changes, re-seed the deck's captures against the changed scene and say
     so in the run report; the reference (Rafe's shadowed frame) is unchanged."

The review room gained a doorway (13,6) and a bay (11-13,7-11), and the Λ moved to the corridor
mouth. A plant must differ from the deck on the axis under test and as little else as possible
(LOOP-PROCESS §4.0a), so every plant captured IN THIS ROOM is re-rendered in the new floor plan
with its own one defect, exactly as its morgue entry records it:

  cement-cap            the cap and wall families at 431c140f, in today's room
  props-unrecognizable  the cull's props and placements (cb344578~1) plus the two new carves
  searchlight-edges     today's room at the culled marks: softness 8.0, the fire at 2.5 tiles
  lambda-against-wall   today's room with the Λ (2x1, 443b4a39) back against the north wall
  lambda-in-the-face    the same, with the drawn edit: the against-wall shift `tileH - margin + 16`

objects-isometric renders its own projection scene, not this room, and is not re-seeded.

THE CULLED BYTES ARE NEVER EDITED. Each re-seed is a NEW file (`<name>-r1b.png`) and a NEW entry;
the entry it replaces is marked `retired_as_control` with the ruling quoted, and stays.

Work Mac (Rafe, 2026-09-25): every Godot run is muted; imports are headless; the capture is the one
windowed run per plant and goes through GODOT (the background wrapper). Each plant renders in a
fresh APFS clone of this tree, so nothing here touches the working tree the session sits in.
"""
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(REPO, ".claude/skills/frame-critic"))
import frame_critic as fc                                           # noqa: E402

MORGUE = os.path.join(REPO, ".claude/skills/frame-critic/morgue")
SCRATCH = os.environ.get("PLANT_SCRATCH", "/tmp/yarl-plant-reseed")
BIN = "/Applications/Godot_mono.app/Contents/MacOS/Godot"
GODOT = os.environ.get("GODOT", BIN)                                # the windowed capture
SCENE = "src/Presentation/assets/tier0_harness/scenes/tier1_props_review.json"
DUNGEON = "src/Presentation/Map/DungeonRenderer.cs"
LAMBDA_AT = "443b4a39"                                              # the walked 2x1 Λ sprite
RULING = ("Because the floor plan changes, re-seed the deck's captures against the changed scene and "
          "say so in the run report; the reference (Rafe's shadowed frame) is unchanged. "
          "— Rafe, R1b ruling, 2026-09-30")
R1B_CARVES = [{"x0": 11, "y0": 7, "x1": 13, "y1": 11}, {"x0": 13, "y0": 6, "x1": 13, "y1": 6}]


def rig(softness="12.0"):
    return ["--tile-size", "32", "--tile-scale", "2.0", "--light-ambient", "1a1a22",
            "--light-color", "ffb066", "--light-energy", "1.6", "--light-radius-tiles", "6.0",
            "--light-falloff", "1.0", "--light-ambient-level", "1.5",
            "--floor-overlays", "res://src/Presentation/assets/tier1_floors/MANIFEST.json",
            "--ashlar-floor", "res://src/Presentation/assets/tier1_ashlar/MANIFEST.json",
            "--boundary-wall", "res://src/Presentation/assets/tier1_walls/MANIFEST.json",
            "--void-ring", "0",
            "--wall-bindings", "res://src/Presentation/assets/tier1_bindings/MANIFEST.json",
            "--wall-cap", "res://src/Presentation/assets/tier1_cap/MANIFEST.json",
            "--occluders", "all", "--shadow-softness", softness, "--shadow-darkness", "0.8",
            "--fire-flicker", "1"]


def lambda_against_wall(spec):
    for p in spec["props"]:
        if p["tileId"] == 9812:
            p.update(x=9, y=12, w=2, h=1, layout=[9812, 9813])
    return spec


PLANTS = [
    dict(name="cement-cap", replaces="cement-cap-shadowed.png", axis=["construction"],
         subject=["walls"], commit="431c140f",
         dirs=["src/Presentation/assets/tier1_cap", "src/Presentation/assets/tier1_walls"]),
    dict(name="props-unrecognizable", replaces="props-unrecognizable-shadowed.png",
         axis=["construction"], subject=["objects"], commit="cb344578~1",
         files=["src/Presentation/assets/tier1_ashlar/tier1_ashlar_%d.png" % i
                for i in (9800, 9801, 9810, 9811, 9812, 9820)],
         scene_from_commit=True, moves={(9810, 4, 14): (3, 14)}, add_carves=True),
    dict(name="searchlight-edges", replaces="searchlight-edges.png", axis=["cast-edge"],
         subject=None, softness="8.0", fire_radius=2.5),
    dict(name="lambda-against-wall", replaces="lambda-against-wall.png", axis=["scale"],
         subject=["objects"], commit=LAMBDA_AT,
         files=["src/Presentation/assets/tier1_ashlar/tier1_ashlar_%d.png" % i for i in (9812, 9813)],
         spec_edit=lambda_against_wall),
    dict(name="lambda-in-the-face", replaces="lambda-in-the-face.png", axis=["grounding"],
         subject=["objects"], commit=LAMBDA_AT,
         files=["src/Presentation/assets/tier1_ashlar/tier1_ashlar_%d.png" % i for i in (9812, 9813)],
         spec_edit=lambda_against_wall,
         code_edit=("float shift = tileH - margin - ReviewLighting.PropBaseRun(tileH);",
                    "float shift = tileH - margin + 16;")),
]


def git_show(rev, path):
    r = subprocess.run(["git", "-C", REPO, "show", "%s:%s" % (rev, path)], capture_output=True)
    return r.stdout if r.returncode == 0 else None


def git_ls(rev, d):
    r = subprocess.run(["git", "-C", REPO, "ls-tree", "-r", "--name-only", rev, "--", d],
                       capture_output=True, text=True)
    return [f for f in r.stdout.split() if not f.endswith(".import")]


def fresh_tree():
    if os.path.exists(SCRATCH):
        shutil.rmtree(SCRATCH)
    subprocess.run(["cp", "-c", "-R", REPO, SCRATCH], check=True)   # APFS clone, .godot cache included


def overlay(plant, tree):
    files = list(plant.get("files", []))
    for d in plant.get("dirs", []):
        files += git_ls(plant["commit"], d)
    n = 0
    for f in files:
        b = git_show(plant["commit"], f)
        if b is None:
            print("   (absent at %s: %s)" % (plant["commit"], f))
            continue
        dst = os.path.join(tree, f)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        open(dst, "wb").write(b)
        n += 1
    return n


def plant_scene(plant, tree):
    if plant.get("scene_from_commit"):
        spec = json.loads(git_show(plant["commit"], SCENE))
    else:
        spec = json.load(open(os.path.join(REPO, SCENE)))
    if plant.get("add_carves"):
        spec["carve"] = spec["carve"] + [dict(c) for c in R1B_CARVES]
    spec["name"] = "plant_%s_r1b" % plant["name"].replace("-", "_")
    spec["legibility"] = []                                           # a plant is a picture, not a measurement
    for p in spec["props"]:
        mv = plant.get("moves", {}).get((p.get("tileId"), p.get("x"), p.get("y")))
        if mv:
            p["x"], p["y"] = mv
        p.setdefault("shape", "round" if p.get("tileId") == 9820 else "box")
        if p.get("tileId") == 9820:
            light = dict(p.get("light") or {"color": "ff8a3c", "energy": 1.6, "radiusTiles": 4.0})
            if plant.get("fire_radius"):
                light["radiusTiles"] = plant["fire_radius"]
            p["light"] = light
    if plant.get("spec_edit"):
        spec = plant["spec_edit"](spec)
    rel = "src/Presentation/assets/tier0_harness/scenes/%s.json" % spec["name"]
    json.dump(spec, open(os.path.join(tree, rel), "w"), indent=1, ensure_ascii=False)
    return rel


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    a = ap.parse_args()
    only = [x for x in a.only.split(",") if x]
    head = subprocess.run(["git", "-C", REPO, "rev-parse", "HEAD"], capture_output=True,
                          text=True).stdout.strip()
    raw = open(os.path.join(MORGUE, "MORGUE.json")).read()
    morgue = json.loads(raw)
    cfg = json.load(open(os.path.join(REPO, "docs/FRAME-CRITIC.json")))
    ev = os.path.join(REPO, "tools/cast_shadows/evidence/plants_r1b")
    os.makedirs(ev, exist_ok=True)
    filed = []
    try:
        for plant in PLANTS:
            if only and plant["name"] not in only:
                continue
            print("== %s" % plant["name"])
            fresh_tree()
            n = overlay(plant, SCRATCH) if plant.get("commit") else 0
            scene_rel = plant_scene(plant, SCRATCH)
            if plant.get("code_edit"):
                p = os.path.join(SCRATCH, DUNGEON)
                src = open(p).read()
                old, new = plant["code_edit"]
                if old not in src:
                    print("   FAILED — the drawn edit's anchor is not in %s" % DUNGEON)
                    continue
                open(p, "w").write(src.replace(old, new))
                b = subprocess.run(["dotnet", "build", os.path.join(SCRATCH, "UnderWarden.Presentation.csproj"),
                                    "-v", "q", "-nologo"], capture_output=True, text=True)
                if b.returncode != 0:
                    print("   FAILED — the edited engine does not build"); continue
            subprocess.run([BIN, "--headless", "--audio-driver", "Dummy", "--path", SCRATCH, "--import"],
                           capture_output=True, timeout=1800)
            out = os.path.join(ev, "%s-r1b.png" % plant["name"])
            log = out[:-4] + ".log"
            if os.path.exists(out):
                os.remove(out)
            cmd = [GODOT, "--path", SCRATCH, "--resolution", "750x1334", "--art-scene-capture",
                   "--capture-out", out, "--capture-width", "750", "--capture-height", "1334",
                   "--corridor-scene", "res://" + scene_rel, "--tile-theme-config",
                   "res://src/Presentation/assets/tier1_ashlar/tile_themes_tier1_ashlar.yaml"] \
                + rig(plant.get("softness", "12.0"))
            with open(log, "w") as f:
                f.write(" ".join(cmd) + "\n\n")
                f.flush()
                subprocess.run(cmd, stdout=f, stderr=subprocess.STDOUT, timeout=900)
            text = open(log, errors="ignore").read()
            # A FRAME WRITTEN AFTER AN ERROR, OR A FLAT ONE, IS NOT A CAPTURE (2026-09-30).
            if "ERROR:" in text or fc.CAPTURE_FAILED_MARKER in text or not os.path.exists(out):
                if os.path.exists(out):
                    os.remove(out)
                print("   FAILED — no frame:\n   " + "\n   ".join(text.splitlines()[-4:]))
                continue
            blank, why = fc.blank_frame(out, cfg.get("crop"))
            if blank:
                os.remove(out)
                print("   FAILED — %s" % why)
                continue
            fname = "%s-r1b.png" % plant["name"]
            shutil.move(out, os.path.join(MORGUE, fname))
            sha = hashlib.sha256(open(os.path.join(MORGUE, fname), "rb").read()).hexdigest()
            old = next(e for e in morgue["entries"] if e["file"] == plant["replaces"])
            entry = {
                "file": fname, "surface": ["combined"], "axis": plant["axis"],
                "subject": plant["subject"] if plant["subject"] is not None else old.get("subject"),
                "regime": "shadowed", "sha256": sha,
                "source": ("RE-SEEDED against the R1b floor plan (doorway (13,6), bay (11-13,7-11), the Λ at "
                           "the corridor mouth) by ruling (Rafe, 2026-09-30) — the defect of %s reproduced "
                           "in today's room through the engine at %s%s. Muted, background capture; "
                           "log tools/cast_shadows/evidence/plants_r1b/%s.log"
                           % (plant["replaces"], head[:12],
                              (" with %d culled files overlaid from %s" % (n, plant["commit"])) if n else "",
                              fname[:-4])),
                "culled_by": old.get("culled_by"), "verbatim": old.get("verbatim"),
                "defect": old.get("defect"),
                "provenance": "re-seed of %s, which stays in the morgue as retired_as_control" % plant["replaces"],
            }
            if not entry["subject"]:
                entry.pop("subject")
            old["retired_as_control"] = {"when": "2026-09-30", "ruling": RULING,
                                         "replaced_by": fname}
            morgue["entries"] = [e for e in morgue["entries"] if e["file"] != fname] + [entry]
            filed.append(fname)
            print("   filed %s  sha %s" % (fname, sha[:12]))
    finally:
        json.dump(morgue, open(os.path.join(MORGUE, "MORGUE.json"), "w"), indent=1, ensure_ascii=False)
        open(os.path.join(MORGUE, "MORGUE.json"), "a").write("\n" if raw.endswith("\n") else "")
        if os.path.exists(SCRATCH):
            shutil.rmtree(SCRATCH)
    print("\nfiled %d: %s" % (len(filed), ", ".join(filed) or "(none)"))
    return 0 if len(filed) == len([p for p in PLANTS if not only or p["name"] in only]) else 1


if __name__ == "__main__":
    raise SystemExit(main())
