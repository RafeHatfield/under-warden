#!/usr/bin/env python3
"""RE-CAPTURE THE MORGUE'S PLANTS UNDER THE SHADOWED REGIME — the regime law's first duty.

    python3 tools/cast_shadows/recapture_plants.py [--only name,name]

LAW (Rafe, 2026-09-12): plants and the reference are captured under the deck's lighting regime
— scene, rig, AND shadow state. Every plant in the morgue pre-dates the cast-shadows round, so a
shadowed deck has none. This re-renders each cull FROM ITS OWN ASSET STATE IN HISTORY through
today's engine with the ratified shadow flags, and files the result as a new entry tagged
`regime: shadowed`. The culled bytes are never edited; the old entries stay as the unshadowed
regime's plants.

What "the culled asset state" is, per plant:

  props-unrecognizable   the pre-§12.2 prop tiles and scene at cb344578~1 (the build Rafe walked
                         2026-09-10: 10x15 px in a 32 px cell)
  objects-isometric      round one's candidate A tiles (9857-9863) and scene at f60ddb30
  cement-cap             the cap and wall families at 431c140f, in today's props room

The probes are stripped from the plant scenes: a probe declared for a one-lamp room refuses a
two-lamp capture, and a plant is a picture, not a measurement. Shapes are added so the props
cast (§3.2), and the fire keeps its light where the scene has a fire, so the plant's lighting
state matches the deck's rather than the cull's.

A scratch worktree at HEAD is overlaid per plant and restored after; nothing here touches the
working tree the session sits in.
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
MORGUE = os.path.join(REPO, ".claude/skills/frame-critic/morgue")
SCRATCH = os.environ.get("PLANT_SCRATCH", "/tmp/yarl-plant-recapture")
GODOT = "/Applications/Godot_mono.app/Contents/MacOS/Godot"
RIG = ["--tile-size", "32", "--tile-scale", "2.0", "--light-ambient", "1a1a22", "--light-color",
       "ffb066", "--light-energy", "1.6", "--light-radius-tiles", "6.0", "--light-falloff", "1.0",
       "--light-ambient-level", "1.5",
       "--floor-overlays", "res://src/Presentation/assets/tier1_floors/MANIFEST.json",
       "--ashlar-floor", "res://src/Presentation/assets/tier1_ashlar/MANIFEST.json",
       "--boundary-wall", "res://src/Presentation/assets/tier1_walls/MANIFEST.json",
       "--void-ring", "0",
       "--wall-bindings", "res://src/Presentation/assets/tier1_bindings/MANIFEST.json",
       "--wall-cap", "res://src/Presentation/assets/tier1_cap/MANIFEST.json",
       "--occluders", "all", "--shadow-softness", "12.0", "--shadow-darkness", "0.8",
       "--fire-flicker", "1"]
FIRE_LIGHT = {"color": "ff8a3c", "energy": 1.6, "radiusTiles": 4.0}

PLANTS = [
    dict(name="props-unrecognizable", commit="cb344578~1",
         files=["src/Presentation/assets/tier1_ashlar/tier1_ashlar_%d.png" % i
                for i in (9800, 9801, 9810, 9811, 9812, 9820)],
         scene="src/Presentation/assets/tier0_harness/scenes/tier1_props_review.json",
         scene_from_commit=True, subject=["objects"], axis=["construction"],
         # (4,14) -> (3,14): the cull's own placements seal (5,14) under today's connectivity
         # guard (marker north, barricades east and west, the pillar south). One cell west keeps
         # the picture — the cull is the SIZE of the props, not where one stands.
         moves={(9810, 4, 14): (3, 14)},
         verbatim="small, unrecognizable except the fire; colouring quite good",
         culled_by="Rafe, device walk, 2026-09-10 (the §12.2 ruling)"),
    dict(name="objects-isometric", commit="f60ddb30",
         files=["src/Presentation/assets/tier1_ashlar/tier1_ashlar_%d.png" % i
                for i in range(9857, 9864)],
         scene="src/Presentation/assets/tier0_harness/scenes/tier1_projection_A.json",
         scene_from_commit=True, subject=["objects"], axis=["construction"],
         verbatim="iso is clearly not a fit",
         culled_by="Rafe, device walk, 2026-09-11 (object-projection round one)"),
    dict(name="cement-cap", commit="431c140f",
         dirs=["src/Presentation/assets/tier1_cap", "src/Presentation/assets/tier1_walls"],
         scene="src/Presentation/assets/tier0_harness/scenes/tier1_props_review.json",
         scene_from_commit=False, subject=["walls"], axis=["construction"],
         verbatim="caps are still grey and read as cement, not stone",
         culled_by="Rafe, device walk, 2026-09-03"),
]


def git(*a, cwd=REPO):
    return subprocess.run(["git", "-C", cwd] + list(a), capture_output=True, text=True)


def overlay(plant, tree):
    """Copy the culled asset state from history into the scratch tree."""
    n = 0
    files = list(plant.get("files", []))
    for d in plant.get("dirs", []):
        r = git("ls-tree", "-r", "--name-only", plant["commit"], "--", d)
        files += [f for f in r.stdout.split() if not f.endswith(".import")]
    for f in files:
        r = subprocess.run(["git", "-C", REPO, "show", "%s:%s" % (plant["commit"], f)],
                           capture_output=True)
        if r.returncode != 0:
            print("   (absent at %s: %s)" % (plant["commit"], f)); continue
        dst = os.path.join(tree, f)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        open(dst, "wb").write(r.stdout)
        n += 1
    return n


def plant_scene(plant, tree):
    """The scene the plant renders in: from the cull's commit or today's, probes stripped,
    shapes and the fire's light added so the lighting state is the deck's."""
    if plant["scene_from_commit"]:
        r = subprocess.run(["git", "-C", REPO, "show", "%s:%s" % (plant["commit"], plant["scene"])],
                           capture_output=True, text=True)
        spec = json.loads(r.stdout)
    else:
        spec = json.load(open(os.path.join(REPO, plant["scene"])))
    spec["name"] = "plant_%s_shadowed" % plant["name"].replace("-", "_")
    spec["legibility"] = []
    for p in spec.get("props", []):
        mv = plant.get("moves", {}).get((p.get("tileId"), p.get("x"), p.get("y")))
        if mv:
            p["x"], p["y"] = mv
        p.setdefault("shape", "round" if p.get("tileId") in (9820, 9853, 9860, 9867) else "box")
        if p.get("tileId") == 9820:
            p["light"] = dict(FIRE_LIGHT)
    rel = "src/Presentation/assets/tier0_harness/scenes/%s.json" % spec["name"]
    json.dump(spec, open(os.path.join(tree, rel), "w"), indent=1)
    return rel


def capture(tree, scene_rel, out_png):
    log = out_png[:-4] + ".log"
    cmd = [GODOT, "--path", tree, "--resolution", "750x1334", "--art-scene-capture",
           "--capture-out", out_png, "--capture-width", "750", "--capture-height", "1334",
           "--corridor-scene", "res://" + scene_rel,
           "--tile-theme-config", "res://src/Presentation/assets/tier1_ashlar/tile_themes_tier1_ashlar.yaml"] + RIG
    with open(log, "w") as f:
        subprocess.run(cmd, stdout=f, stderr=subprocess.STDOUT, timeout=600)
    # A FRAME WRITTEN AFTER AN ERROR IS NOT A CAPTURE. The engine wrote a flat grey 750x1334
    # after the scene builder threw — a capture nobody would judge, filed as a plant. Refused.
    if os.path.exists(out_png) and "ERROR:" in open(log).read():
        os.remove(out_png)
        return False, log
    return os.path.exists(out_png), log


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    a = ap.parse_args()
    only = [x for x in a.only.split(",") if x]
    head = git("rev-parse", "HEAD").stdout.strip()
    if os.path.exists(SCRATCH):
        git("worktree", "remove", "--force", SCRATCH)
    r = git("worktree", "add", "--detach", SCRATCH, head)
    if r.returncode != 0:
        raise SystemExit("worktree add failed: " + r.stderr)
    try:
        shutil.copy(os.path.join(REPO, "export_presets.cfg"), SCRATCH)
        subprocess.run(["dotnet", "build", os.path.join(SCRATCH, "UnderWarden.Presentation.csproj")],
                       capture_output=True, text=True, check=True)
        morgue = json.load(open(os.path.join(MORGUE, "MORGUE.json")))
        for plant in PLANTS:
            if only and plant["name"] not in only:
                continue
            print("== %s @ %s" % (plant["name"], plant["commit"]))
            git("checkout", "--", ".", cwd=SCRATCH)          # restore HEAD's assets
            n = overlay(plant, SCRATCH)
            scene_rel = plant_scene(plant, SCRATCH)
            subprocess.run([GODOT, "--headless", "--path", SCRATCH, "--import"],
                           capture_output=True, timeout=900)
            out = os.path.join(MORGUE, "%s-shadowed.png" % plant["name"])
            ok, log = capture(SCRATCH, scene_rel, out)
            if not ok:
                tail = open(log).read().splitlines()[-4:]
                print("   FAILED — no frame:\n   " + "\n   ".join(tail))
                continue
            shutil.move(log, os.path.join(REPO, "tools/cast_shadows/evidence",
                                          "plant_%s_shadowed.log" % plant["name"]))
            sha = hashlib.sha256(open(out, "rb").read()).hexdigest()
            entry = {
                "file": "%s-shadowed.png" % plant["name"], "surface": ["combined"],
                "axis": plant["axis"], "subject": plant["subject"], "regime": "shadowed",
                "sha256": sha,
                "source": "RE-CAPTURED under the shadowed regime (Rafe's law, 2026-09-12) from the "
                          "culled asset state at %s (%d files overlaid) through the engine at %s, "
                          "with the ratified shadow flags (occluders all, softness 12, darkness 0.8, "
                          "fire lit, flicker on, void ring 0)" % (plant["commit"], n, head[:12]),
                "culled_by": plant["culled_by"], "verbatim": plant["verbatim"],
                "defect": next(e["defect"] for e in morgue["entries"]
                               if e["file"] == plant["name"] + ".png"),
                "provenance": "the unshadowed entry %s.png is this cull's original frame; this is "
                              "the same asset state under the deck's light" % plant["name"],
            }
            morgue["entries"] = [e for e in morgue["entries"] if e["file"] != entry["file"]] + [entry]
            print("   filed %s  sha %s" % (entry["file"], sha[:12]))
        json.dump(morgue, open(os.path.join(MORGUE, "MORGUE.json"), "w"), indent=2, ensure_ascii=False)
    finally:
        git("worktree", "remove", "--force", SCRATCH)


if __name__ == "__main__":
    main()
