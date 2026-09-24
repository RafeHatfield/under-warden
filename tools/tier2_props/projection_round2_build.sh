#!/bin/bash
# OBJECT-PROJECTION RULING, ROUND TWO — from staged picks to five captures.
#
#   tools/tier2_props/projection_round2_build.sh land      # picks -> reserved ids 9850-9884, re-import
#   tools/tier2_props/projection_round2_build.sh capture   # ground + five candidate scenes, headless
#   tools/tier2_props/projection_round2_build.sh install <cand>   # one SKIPPED-REVIEW device build
#   tools/tier2_props/projection_round2_build.sh push      # install the five BUILT apps + verify each
#
# `push` exists because the handset went unavailable mid-session: every build completed and
# was stamped, only devicectl failed. It installs what `install` built, nothing else — the
# critic gate and the walk precheck already ran on those builds and the stamp is in the marker.
#
# The install is SKIPPED-REVIEW BY DESIGN (§13.2: this ruling is Rafe's eye, never a seat's), and
# it says so on the phone. Every candidate goes under its own bundle id so five builds sit side by
# side; projW and projA REPLACE round one's builds of the same name, which are superseded.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"
GODOT="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
GEN=tools/tier2_props/gen/projection2
EV=tools/tier1_floors/evidence
CANDS="W OL OR Odeep A"

RIG="--tile-size 32 --tile-scale 2.0 --light-ambient 1a1a22 --light-color ffb066 --light-energy 1.6 \
--light-radius-tiles 6.0 --light-falloff 1.0 --light-ambient-level 1.5 \
--floor-overlays res://src/Presentation/assets/tier1_floors/MANIFEST.json \
--ashlar-floor res://src/Presentation/assets/tier1_ashlar/MANIFEST.json \
--boundary-wall res://src/Presentation/assets/tier1_walls/MANIFEST.json --void-ring 1 \
--wall-bindings res://src/Presentation/assets/tier1_bindings/MANIFEST.json \
--wall-cap res://src/Presentation/assets/tier1_cap/MANIFEST.json"

case "${1:-}" in
  land)
    python3 tools/tier2_props/projection_pick2.py --stage
    for c in $CANDS; do
      python3 tools/tier2_props/land_scaled_props.py --plan $GEN/plan_$c.json --out $GEN/landed_$c.json
    done
    # a changed PNG under an existing .import is not re-imported by a capture launch
    "$GODOT" --headless --path "$ROOT" --import >/dev/null 2>&1 || true
    python3 tools/tier2_props/projection_round2.py measure
    ;;
  capture)
    for s in ground $CANDS; do
      "$GODOT" --path "$ROOT" --resolution 750x1334 --art-scene-capture \
        --capture-out $EV/proj2_$s.png --capture-width 750 --capture-height 1334 \
        --corridor-scene res://src/Presentation/assets/tier0_harness/scenes/tier1_projection2_$s.json \
        --tile-theme-config res://src/Presentation/assets/tier1_ashlar/tile_themes_tier1_ashlar.yaml \
        $RIG > $EV/proj2_$s.log 2>&1 || true
      echo "== $s: $(grep -c 'legibility(' $EV/proj2_$s.log) probes, $(grep -o 'verdict=[A-Z]*' $EV/proj2_$s.log | tail -1)"
    done
    ;;
  install)
    c="$2"
    OUT="$ROOT/.ios-build-proj$c" YARL_SKIP_CRITIC=1 \
    TIER0_BUNDLE_ID="com.rafehatfield.underwarden.proj$c" \
    TIER0_APP_NAME="YARL proj$c" \
    TIER0_SCENE="res://src/Presentation/assets/tier0_harness/scenes/tier1_projection2_$c.json" \
    tools/tier0_harness/build_review_app.sh
    ;;
  push)
    DEV="${DEVICE_ID:-5DB969FF-269C-5A8A-86EB-99EC9FF22397}"
    for c in $CANDS; do
      APP="$(find "$ROOT/.ios-build-proj$c/xcodeproj/dd/Build/Products/Debug-iphoneos" -maxdepth 1 -name '*.app' -print -quit)"
      [ -n "$APP" ] || { echo "== $c: NOT BUILT (run install $c first)"; continue; }
      echo "== $c: installing $(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$APP/Info.plist")"
      xcrun devicectl device install app --device "$DEV" "$APP" > $EV/proj2_push_$c.log 2>&1 \
        && echo "   installed" || { tail -3 $EV/proj2_push_$c.log; continue; }
      TIER0_BUNDLE_ID="com.rafehatfield.underwarden.proj$c" \
      TIER0_SCENE="res://src/Presentation/assets/tier0_harness/scenes/tier1_projection2_$c.json" \
        tools/tier0_harness/verify_on_device.sh --out $EV > $EV/proj2_verify_$c.log 2>&1 \
        && echo "   verified" || { echo "   VERIFY FAILED:"; tail -5 $EV/proj2_verify_$c.log; }
    done
    ;;
  verify)
    # the verification alone — the expectation follows TIER0_SCENE, which `push` now passes
    for c in $CANDS; do
      TIER0_BUNDLE_ID="com.rafehatfield.underwarden.proj$c" \
      TIER0_SCENE="res://src/Presentation/assets/tier0_harness/scenes/tier1_projection2_$c.json" \
        tools/tier0_harness/verify_on_device.sh --out $EV > $EV/proj2_verify_$c.log 2>&1 \
        && echo "== $c: verified" || { echo "== $c: VERIFY FAILED:"; grep "MISS\|FAIL" $EV/proj2_verify_$c.log | head -5; }
    done
    ;;
  *) sed -n '2,14p' "$0"; exit 2 ;;
esac
