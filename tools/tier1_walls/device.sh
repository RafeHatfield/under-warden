#!/bin/zsh
# THE WALL SESSION'S OWN SLOT ON THE HANDSET.
#
#   device.sh build     export, install, and stamp the wall review build
#   device.sh verify     ask the device what it is actually running
#
# WHY THIS FILE EXISTS AT ALL. Review builds install under a bundle id, and two live sessions
# sharing one id means the last install silently wins. That happened: a wall session overwrote the
# floor gate's build on this handset, and the only symptom was the floor's scene check going MISS
# — which reads as *your build is wrong* when the truth is *your build is gone*. The default slot
# `…underwarden.tier0` stays with the floor gate; the walls take their own, here, so it cannot
# be forgotten at three in the morning.
#
# EVERY MANIFEST BELOW IS LOAD-BEARING and the failure modes differ, which is the same reason
# `capture.sh` is a script and not a line in a README:
#   TIER1_ASHLAR      omit and the floor is a MAGENTA placeholder
#   TIER1_OVERLAYS    omit and plane-boundary occlusion disappears (bible §12.1's form)
#   TIER1_WALLS       omit and the walls are the tier-0 magenta mocks
#   TIER1_BINDINGS    omit and the walls are bare — §7.1's "show me what holds this" answered
#                     with nothing, silently
#   TIER1_WALLS_CAP   omit and the tops fall back to the per-tile top plane the gate rejected
set -e
cd "$(dirname "$0")/../.."

export TIER0_BUNDLE_ID="${TIER0_BUNDLE_ID:-com.rafehatfield.underwarden.tier1walls}"
export TIER0_APP_NAME="${TIER0_APP_NAME:-YARL Tier1 Walls}"
export TIER0_SCENE="${TIER0_SCENE:-res://src/Presentation/assets/tier0_harness/scenes/tier1_wall_review.json}"
export TIER0_THEME="${TIER0_THEME:-res://src/Presentation/assets/tier1_ashlar/tile_themes_tier1_ashlar.yaml}"
export TIER1_OVERLAYS="${TIER1_OVERLAYS:-res://src/Presentation/assets/tier1_floors/MANIFEST.json}"
export TIER1_ASHLAR="${TIER1_ASHLAR:-res://src/Presentation/assets/tier1_ashlar/MANIFEST.json}"
export TIER1_WALLS="${TIER1_WALLS:-res://src/Presentation/assets/tier1_walls/MANIFEST.json}"
export TIER1_BINDINGS="${TIER1_BINDINGS:-res://src/Presentation/assets/tier1_bindings/MANIFEST.json}"
export TIER1_WALLS_CAP="${TIER1_WALLS_CAP:-res://src/Presentation/assets/tier1_cap/MANIFEST.json}"

# The wall checks are opt-in in the verifier, because a floor gate build legitimately has no wall
# family and a check that fails on a build never meant to satisfy it teaches the operator to
# ignore checks. A wall build always wants them, so they are not optional here.
export TIER0_EXPECT_WALLS=1

# ── THE GATE IS NOT HERE, AND THAT IS THE POINT ───────────────────────────────────────────────
#
# This file used to carry its own install gate — a round-validity check of my own, wired in front
# of the build. It is gone, because `.claude/skills/frame-critic` landed on main and the skill is
# explicit about why a second one is worse than none:
#
#     "Two callers, one implementation — a gate reimplemented in a second place has two
#      behaviours and the second one is always the lenient one."
#
# `build_review_app.sh` now runs `critic_gate.py` before it exports, and a PreToolUse hook runs
# the same implementation against anything that would put a build on a device. My version checked
# whether a ROUND was conducted properly; the critic checks whether the PICTURE is any good, which
# is the stronger claim and the one that matters. Nothing here re-implements it.
#
# The override is the skill's, and it is visible from the handset:
#     YARL_SKIP_CRITIC=1 tools/tier1_walls/device.sh build
# installs and stamps SKIPPED-REVIEW, which the app draws on screen.
case "${1:-}" in
  build)  shift; exec tools/tier0_harness/build_review_app.sh "$@" ;;
  verify) shift; exec tools/tier0_harness/verify_on_device.sh \
                      --out tools/tier1_walls/evidence "$@" ;;
  *) echo "usage: tools/tier1_walls/device.sh {build|verify} [args...]" >&2; exit 2 ;;
esac
