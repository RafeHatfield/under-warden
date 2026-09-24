# Tier 1 — the Boundary floor family

**The first real candidate art on this project, and the systems that make it judgeable.**

ART-BIBLE-v0 §8.3 (LAW) divides the objects, and this directory is built along that line:

| | authored | carries | judged |
|---|---|---|---|
| **base tile** | once, per material | material only — **no incident** | never alone; only as laid |
| **variant / overlay** | per instance, randomised | the incident — cracks, wear, marks, channel | in the field it produces |

Nothing here lands. §13.1 gives the landing gate to Rafe, in-scene, on device.

---

## Running it

```bash
# --- the instruments, first. No pass counts until they have demonstrated they can fail. ---
python3 tools/tier1_floors/field_laws.py --controls          # 5 planted defects, one per axis
python3 tools/tier1_floors/calibrate_against_bar.py          # §13.6: the accepted corpus

# --- generation. Declares its budget and refusals before the first call. ---
python3 tools/tier1_floors/generate.py --wave base    --dry-run
python3 tools/tier1_floors/generate.py --wave base            # 40, conditioned on C-GAB
python3 tools/tier1_floors/generate.py --wave overlay         # 32, unconditioned
python3 tools/tier1_floors/screen_wave.py --wave base         # counts, per code

# --- composition. Refuses to certify a family that fails its own screen. ---
python3 tools/tier1_floors/compose_family.py                  # base + channel + incident + 24 oriented
python3 tools/tier1_floors/export_theme.py                    # theme yaml + the wall mocks
python3 tools/tier1_floors/plant_family.py                    # LOOP-PROCESS §4's plant
python3 tools/tier1_floors/field_preview.py                   # the lattice, with both anchors

# --- in scene, lit, at device pixel size (§2.1). ---
python3 tools/tier0_harness/capture_corridor.py \
  --out tools/tier1_floors/evidence/scene_family.png \
  --theme-config res://src/Presentation/assets/tier1_floors/tile_themes_tier1_floors.yaml \
  --scene-spec src/Presentation/assets/tier0_harness/scenes/tier1_floor_review.json \
  --floor-overlays res://src/Presentation/assets/tier1_floors/MANIFEST.json \
  --log-out tools/tier1_floors/evidence/scene_family.log

# --- the blind seats. Round is VOID if the plant seat misses the plant. ---
python3 tools/tier1_floors/run_seats.py F1 F2 F3 F4 --round 2

# --- ON DEVICE. The only thing that decides anything. ---
TIER0_SCENE=res://src/Presentation/assets/tier0_harness/scenes/tier1_floor_review.json \
TIER0_THEME=res://src/Presentation/assets/tier1_floors/tile_themes_tier1_floors.yaml \
TIER1_OVERLAYS=res://src/Presentation/assets/tier1_floors/MANIFEST.json \
tools/tier0_harness/build_review_app.sh
```

A fresh worktree needs `dotnet build UnderWarden.Presentation.csproj` and
`Godot --headless --path . --import` before any capture renders, and **the import must be re-run
after any PNG is rewritten** — otherwise the engine draws the previous bytes and nothing says so.

---

## The parts

| path | what |
|---|---|
| `prompts/base_material.json` | asks for MATERIAL and nothing else. Every deleted phrase from the §6.4 subject is listed with why. |
| `prompts/incident_overlay.json` | asks for the incident. The vocabulary the other prompt refuses. |
| `generate.py` | the two waves. Budget, levers and refusals declared before the first call. |
| `field_laws.py` | **the instrument.** Four geometric tests, run on the 3×3 tiling — the input `ring_instrument` structurally cannot have. Positive controls in `--controls`. |
| `calibrate_against_bar.py` | §13.6's operating point, from the accepted corpus. Measurements leave; pixels never do. |
| `compose_family.py` | the family. Generation supplies material, procedure supplies architecture (§13.7). |
| `export_theme.py` | the theme, and the wall mocks copied in unchanged with their hashes. |
| `plant_family.py` | LOOP-PROCESS §4's plant. Never lands, never shown to Rafe. |
| `field_preview.py` | the lattice score and its two anchors. An ordering; rules nothing. |
| `run_seats.py`, `seat_prompt.txt` | the blind seats and the plant control. |
| `REPORT.md` | what the round found. Read this one. |

Engine side: `FloorIncidentPlanner` (placement, no Godot), `Tier1FloorOverlays` (drawing),
`ReviewRigPanel` + `ReviewLighting` (§6.2.1's knobs).

---

## Rules this directory holds itself to

**No instrument scores a register clause** (§13.4). Every test in `field_laws` is geometry —
connected components, containment, edge continuity, periodicity. *Nothing is staged*, *the art
plays it straight* and *nothing is ruined, things are used up* have **NO INSTRUMENT** here and
are carried at the human gate. There is no dread score and no staging detector.

**No pass counts until the instrument has failed** (§13.5). `field_laws --controls` plants one
defect per axis and requires each to fire, *and* requires a legal construction to come back
clean — an instrument that reds on everything is as decorative as one that greens on everything.

**The bar shows a bar is reachable; it never sets one** (§13.3's origination rule). About 40% of
the asset bar's own laid floor cells would fail Yarl's incident threshold, and the same corpus
carries frames and grids this bible forbids outright.

**A step that runs and changes nothing must go red** (LOOP-PROCESS §4.2). `compose_family`
refuses to certify a family failing its own screen; the engine reports whether the overlay system
attached on every boot; oriented variants are re-screened rather than assumed to inherit a
verdict — which is how a reflection-dependent bug in `grid` was found.

**This session tuned no rig number.** §6.2.1 gives that pass to the human gate. The knobs exist;
their defaults reproduce the previous rig exactly.
