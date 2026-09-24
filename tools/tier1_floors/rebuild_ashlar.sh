#!/bin/zsh
# Rebuild the ashlar family, its plant, their themes and both paint checks — IN THE ONE ORDER
# THAT IS CORRECT.
#
# The order is not obvious and getting it wrong fails quietly, which is why it is a script rather
# than a list in a README:
#
#   * compose_ashlar REWRITES MANIFEST.json from scratch. Anything a later step added to it —
#     the theme name, the wall-mock hashes, the paint check — is gone. So it goes first, always.
#   * emit_paint_check reads the ATLASES, so it cannot run before they exist.
#   * plant_ashlar copies the candidate's manifest, so it must run after compose; it drops the
#     inherited paint check because that check describes the candidate's pixels and would make
#     the plant REFUSE TO LAY — the control arm silenced by the candidate's evidence.
#   * the plant therefore needs its own check, generated from its own atlases.
#   * export_theme_ashlar writes theme keys into both manifests, so it goes last.
#
# Then the two that this repo will let you get wrong in a way nothing reports (see REPORT §5):
# build the ROOT csproj and import the ROOT path. The copies under src/Presentation/ succeed and
# change nothing Godot runs.
set -e
cd "$(dirname "$0")/../.."

echo "== 1. the family (625 atlases + the grain bank)"
python3 tools/tier1_floors/compose_ashlar.py | tail -5

echo "== 2. finished pixels for the engine to reproduce"
python3 tools/tier1_floors/emit_paint_check.py | tail -3

echo "== 3. LOOP-PROCESS §4's plant"
python3 tools/tier1_floors/plant_ashlar.py | tail -2

echo "== 4. the plant's own paint check"
python3 tools/tier1_floors/emit_paint_check.py \
  --assets src/Presentation/assets/tier1_ashlar_plant | tail -3

echo "== 5. themes and the wall mocks"
python3 tools/tier1_floors/export_theme_ashlar.py --plant | tail -2

echo "== 6. the instruments"
python3 tools/tier1_floors/verify_atlas_path.py --plants | tail -5
python3 tools/tier1_floors/probe_stone_address.py | grep -E "UNADDRESSABLE|VERDICT"

echo
echo "Next, from the repo root and NOT from src/Presentation:"
echo "  dotnet build UnderWarden.Presentation.csproj"
echo "  /Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --import"
