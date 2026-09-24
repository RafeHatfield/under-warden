#!/bin/bash
# THE SE PERFORMANCE GATE — §6.4's "does the SE hold frame rate", answered on the handset.
#
#   YARL_SKIP_CRITIC=1 tools/cast_shadows/perf_on_device.sh
#
# Two MEASUREMENT builds, SKIPPED-REVIEW by design (they produce numbers, not verdicts), under
# their own bundle ids so they sit beside the gate build: occluders NONE (every build before the
# round) and occluders ALL with the fire lit. Each is installed, launched, and its boot log
# pulled; the [Perf] windows (240 frames, mean/p95/max ms) are the gate's evidence. A window
# past 16.7 ms/frame on the shadow build is a STOP, not a tune.
#   tools/cast_shadows/perf_on_device.sh push     # install the two BUILT apps and pull [Perf]
#
# `push` exists because the handset went unavailable while the builds ran: the apps are complete
# and stamped in .ios-build-perf{none,all}/ and only devicectl failed.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"
EV=tools/cast_shadows/evidence
SCENE=res://src/Presentation/assets/tier0_harness/scenes/tier1_props_review.json
DEV="${DEVICE_ID:-5DB969FF-269C-5A8A-86EB-99EC9FF22397}"
MODE="${1:-build}"
for v in none all; do
  if [ "$MODE" = "push" ]; then
    APP="$(find "$ROOT/.ios-build-perf$v/xcodeproj/dd/Build/Products/Debug-iphoneos" -maxdepth 1 -name '*.app' -print -quit)"
    [ -n "$APP" ] || { echo "== perf$v: NOT BUILT"; continue; }
    echo "== installing perf$v"
    xcrun devicectl device install app --device "$DEV" "$APP" > "$EV/perf_${v}_push.log" 2>&1 \
      || { tail -3 "$EV/perf_${v}_push.log"; continue; }
  else
  echo "== building perf$v"
  OUT="$ROOT/.ios-build-perf$v" \
  TIER0_BUNDLE_ID="com.rafehatfield.underwarden.perf$v" TIER0_APP_NAME="YARL perf$v" \
  TIER0_SCENE="$SCENE" TIER1_OCCLUDERS="$v" TIER1_SHADOW_SOFTNESS=1.0 TIER1_FIRE_FLICKER=0 \
    tools/tier0_harness/build_review_app.sh > "$EV/perf_${v}_install.log" 2>&1 \
    || { tail -5 "$EV/perf_${v}_install.log"; echo "build/install perf$v FAILED"; continue; }
  fi
  TIER0_BUNDLE_ID="com.rafehatfield.underwarden.perf$v" TIER0_SCENE="$SCENE" \
    tools/tier0_harness/verify_on_device.sh --out "$EV" > "$EV/perf_${v}_verify.log" 2>&1 || true
  cp "$EV/DEVICE-tier1-boot.log" "$EV/perf_${v}_boot.log"
  echo "== perf$v:"; grep "\[Perf\]" "$EV/perf_${v}_boot.log" | sed 's/^/   /' | cut -c1-140
done
