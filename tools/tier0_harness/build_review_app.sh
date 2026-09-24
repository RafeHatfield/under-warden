#!/bin/bash
# Tier 0 REVIEW BUILD — put the lit review corridor on the reference device.
#
# ART-BIBLE-v0 §13.1: the verdict comes from the production renderer, in the lit scene, at true
# display size, ON DEVICE. An iOS app gets no command line, so --corridor-scene cannot reach it.
# This script bakes REVIEW_BUILD.json into the export, which makes the app boot straight into the
# corridor (see ReviewBuildMarker), and installs it under its OWN bundle id so it sits beside the
# real game instead of replacing it.
#
# The marker is written before the export and removed afterwards, always — a leftover marker
# would turn the next ordinary build into a review build without anyone noticing.
#
#   tools/tier0_harness/build_review_app.sh                 # build + install the review build
#   tools/tier0_harness/build_review_app.sh --no-install    # build only
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"

# ================= THE DEVICE GATE'S PRECONDITIONS =================
#
# STANDING ORDER (Rafe, 2026-09-03): no build installs to the phone unless, on THAT EXACT BUILD,
# its round is valid, its diagnostic seat passed its axis, a whole-frame comparative seat has run
# without culling, and every currently-ruled fix is present.
#
# Enforced HERE rather than remembered, because this session has twice recorded that a rule
# depending on my restraint is not a rule, and was right both times. Rafe's walk is the LAST gate.
#
# --no-install still builds: a build you cannot walk is still worth compiling, and blocking that
# would only teach me to reach for the override.
#
# ── THE FRAME-CRITIC GATE COMES FIRST ─────────────────────────────────────────────────────────
#
# An art round is judged by EYES ON DELIVERED FRAMES and by nothing else. A fresh blind seat is
# shown this build's capture, the asset bar, the last frame Rafe approved and one known-bad frame
# he personally culled, shuffled and unlabelled, and asked which it would ship. That verdict —
# for THIS EXACT BUILD, tracked changes and untracked files folded in — is what opens this gate.
#
# It runs BEFORE gate_precheck.py because it is the stronger claim: the preconditions below ask
# whether a round was conducted properly, this asks whether the picture is any good.
#
# YARL_SKIP_CRITIC=1 installs anyway and stamps the build SKIPPED-REVIEW, which the phone then
# shows on screen. A bypassed gate is always visible to whoever is holding the handset.
REVIEW_STATUS=""
if [ "${1:-}" != "--no-install" ]; then
  set +e
  python3 "$ROOT/.claude/skills/frame-critic/critic_gate.py"
  CRITIC_RC=$?
  set -e
  case "$CRITIC_RC" in
    0)  ;;
    10) REVIEW_STATUS="SKIPPED-REVIEW" ;;
    *)  echo
        echo "REFUSING TO BUILD FOR INSTALL — no frame-critic PASS for this build."
        echo "Run with --no-install to compile without putting it on the handset."
        exit 2 ;;
  esac

  # TIER0_MARKER_ONLY stops before the export, so there is no artefact for these preconditions to
  # be about — they ask whether a build offered to a WALK was properly conducted. The critic gate
  # above still runs and still decides the stamp, which is the point of the diagnostic.
  if [ "${TIER0_MARKER_ONLY:-}" != "1" ] \
     && ! python3 "$ROOT/tools/tier0_harness/gate_precheck.py"; then
    echo
    echo "REFUSING TO BUILD FOR INSTALL. Fix the conditions above, or run with --no-install"
    echo "to compile without putting it on the handset."
    exit 2
  fi
fi
MARKER="$ROOT/src/Presentation/assets/tier0_harness/REVIEW_BUILD.json"
TEMPLATE="$MARKER.template"
BUNDLE_ID="${TIER0_BUNDLE_ID:-com.rafehatfield.underwarden.tier0}"
NAME="${TIER0_APP_NAME:-YARL Tier0}"

[ -f "$TEMPLATE" ] || { echo "missing $TEMPLATE" >&2; exit 1; }

cleanup() { rm -f "$MARKER"; echo "== marker removed (a leftover would silently make the next build a review build)"; }
trap cleanup EXIT

cp "$TEMPLATE" "$MARKER"
# TIER0_THEME points the review build at an alternate tile theme — a §6.4 survivor set, say —
# without editing the committed template. The template stays the default so an ordinary review
# build is unchanged, and the substitution is echoed because a device build that quietly showed
# different tiles than the operator expected would be the worst possible review artefact.
if [ -n "${TIER0_THEME:-}" ]; then
  python3 - "$MARKER" "$TIER0_THEME" <<'PY'
import json, sys
path, theme = sys.argv[1], sys.argv[2]
with open(path) as f:
    d = json.load(f)
d["themeConfig"] = theme
with open(path, "w") as f:
    json.dump(d, f, indent=2)
PY
  echo "== theme override: $TIER0_THEME"
fi
# TIER0_SCENE points the review build at an alternate scene spec. Added for the sighted round's
# §13.1 gate build: that round was judged on the MIXED DISTRIBUTION scene (§2.2 — room, corners
# and a one-wide chokepoint), not on the corridor junction the template names, and a device build
# showing a different scene than the round it is meant to ratify is the wrong picture. Same
# reasoning and same echo as TIER0_THEME: the substitution is announced, because the operator
# must be able to see which scene they are walking.
if [ -n "${TIER0_SCENE:-}" ]; then
  python3 - "$MARKER" "$TIER0_SCENE" <<'PY'
import json, sys
path, scene = sys.argv[1], sys.argv[2]
with open(path) as f:
    d = json.load(f)
d["scene"] = scene
with open(path, "w") as f:
    json.dump(d, f, indent=2)
PY
  echo "== scene override: $TIER0_SCENE"
fi

# ── THE WALK GATE ─────────────────────────────────────────────────────────────────────────────
#
# RULED (Rafe, 2026-09-05). A build offered for a NAMED WALK asserts that walk's requirements
# before it may install, and it is checked on THE MARKER THAT ACTUALLY SHIPS — after the theme and
# scene overrides above, because those are precisely what can make a build unwalkable.
#
# The occasion: a build reached the handset for the #174 rig-energy walk with NO ENERGY KNOB in
# the RIG panel and the tier-0 stub theme under it, so the floor rendered magenta. A rig walk that
# cannot reach the rig value, and shows the wrong surface, ratifies nothing.
#
# ⚠ IT IS NOT BYPASSED BY YARL_SKIP_CRITIC, and that is deliberate. That override waives the ART
# VERDICT; this asks whether the artefact can perform the walk at all. §1.2.2a permits walking a
# SKIPPED-REVIEW build for a rig gate — which makes this check MORE load-bearing, not less, since
# it is then the only mechanical thing standing between an unwalkable build and a ratification.
#
# ⚠ AND IT IS NOT A CRITIC CHANGE. The frame critic judges the delivered frame and has no view of
# panel wiring or marker contents; it passed correctly on the build that shipped. Folding this
# into it would weaken it to catch something it structurally cannot see.
if [ "${1:-}" != "--no-install" ] && [ "${TIER0_MARKER_ONLY:-}" != "1" ]; then
  if ! python3 "$ROOT/tools/tier0_harness/walk_precheck.py" "$MARKER"; then
    echo
    echo "REFUSING TO BUILD FOR INSTALL — see the walk requirements above."
    echo "Run with --no-install to compile without putting it on the handset."
    exit 2
  fi
fi
# TIER1_OVERLAYS points the review build at a floor family's MANIFEST.json — ART-BIBLE-v0 §8.3's
# incident overlays and §8.2.1's trodden channel. Same reasoning and same echo as the two
# overrides above: an overlay is NOT a tile role (a cell may carry none, one or two of them,
# chosen per instance), so it cannot ride in on TIER0_THEME. And a device build that quietly
# showed a floor with no incident on it would be the wrong picture at the one gate that decides
# anything (§13.1), which is why the substitution is announced rather than assumed.
if [ -n "${TIER1_OVERLAYS:-}" ]; then
  python3 - "$MARKER" "$TIER1_OVERLAYS" <<'PY'
import json, sys
path, manifest = sys.argv[1], sys.argv[2]
with open(path) as f:
    d = json.load(f)
d["floorOverlays"] = manifest
with open(path, "w") as f:
    json.dump(d, f, indent=2)
PY
  echo "== floor overlays: $TIER1_OVERLAYS"
fi

# TIER1_ASHLAR points the review build at the course-aligned ashlar family's MANIFEST.json.
#
# IT NEEDS ITS OWN KNOB, and the absence of one is a gap this session found rather than inherited
# deliberately: the edge-matched family that preceded it had `--wang-floor` on the capture harness
# and NOTHING here, so every device build since it was written has shown whatever the theme
# picked, under the family's name, at the one gate that decides anything (section 13.1).
#
# It cannot ride in on TIER0_THEME for the same reason the overlays cannot: the family is not a
# tile role. The theme's floor entry is a magenta placeholder that exists only so a sprite is
# present to repaint, and every walkable cell is repainted by Tier1AshlarFloor from that cell's
# stone addresses. Set the theme and forget this, and the phone shows magenta — loud, which is
# the point (LOOP-PROCESS section 4.2), but still the wrong picture.
if [ -n "${TIER1_ASHLAR:-}" ]; then
  python3 - "$MARKER" "$TIER1_ASHLAR" <<'PY'
import json, sys
path, manifest = sys.argv[1], sys.argv[2]
with open(path) as f:
    d = json.load(f)
d["ashlarFloor"] = manifest
with open(path, "w") as f:
    json.dump(d, f, indent=2)
PY
  echo "== ashlar floor: $TIER1_ASHLAR"
fi
# TIER1_WALLS / TIER1_BINDINGS / TIER1_VOID — the wall family, the orc layer over it, and which
# void candidate the walk starts on.
#
# Three knobs rather than one because they are three different objects and §8.3.1 requires the
# first two to stay separate: the wall is the material and the bindings are the incident, and a
# binding baked into a segment is a repair repeated on every cell that segment lands on.
#
# Omit TIER1_WALLS and the phone shows the TIER-0 MAGENTA MOCKS. That is loud on purpose and is
# the only reason it is safe to have a knob at all. Omit TIER1_BINDINGS and the walls are bare —
# which is NOT loud, and is the failure worth naming here: §7.1's *show me what holds this
# together* would be answered with nothing, and the answer would look like a design decision.
#
# TIER1_VOID is a STARTING POSITION, not a value. The rig panel's VOID row switches it live,
# because §13.1 gives the choice to Rafe in the scene and three rebuilt candidates are three
# walks rather than one comparison.
if [ -n "${TIER1_WALLS:-}" ]; then
  python3 - "$MARKER" "$TIER1_WALLS" <<'PY'
import json, sys
path, manifest = sys.argv[1], sys.argv[2]
with open(path) as f:
    d = json.load(f)
d["boundaryWall"] = manifest
with open(path, "w") as f:
    json.dump(d, f, indent=2)
PY
  echo "== boundary wall: $TIER1_WALLS"
fi
if [ -n "${TIER1_BINDINGS:-}" ]; then
  python3 - "$MARKER" "$TIER1_BINDINGS" <<'PY'
import json, sys
path, manifest = sys.argv[1], sys.argv[2]
with open(path) as f:
    d = json.load(f)
d["wallBindings"] = manifest
with open(path, "w") as f:
    json.dump(d, f, indent=2)
PY
  echo "== wall bindings: $TIER1_BINDINGS"
fi
# TIER1_WALLS_CAP points the review build at the cap field's MANIFEST.json — the wall TOPS.
#
# Omit it and the walls fall back to the family's own per-tile top plane, which is what the device
# gate saw and rejected: a lattice at tile frequency, featureless inside each cell, reading as dim
# floor rather than as the top of a thick wall. The cap is one seamless field cut into windows the
# engine picks BY WORLD POSITION, so this knob changes what the tops are made of, not how they are
# lit. It is separate from TIER1_WALLS because the arms share a cap and the plant must not.
if [ -n "${TIER1_WALLS_CAP:-}" ]; then
  python3 - "$MARKER" "$TIER1_WALLS_CAP" <<'PY'
import json, sys
path, manifest = sys.argv[1], sys.argv[2]
with open(path) as f:
    d = json.load(f)
d["wallCap"] = manifest
with open(path, "w") as f:
    json.dump(d, f, indent=2)
PY
  echo "== wall cap: $TIER1_WALLS_CAP"
fi
if [ -n "${TIER1_VOID:-}" ]; then
  python3 - "$MARKER" "$TIER1_VOID" <<'PY'
import json, sys
path, choice = sys.argv[1], sys.argv[2]
with open(path) as f:
    d = json.load(f)
d["voidChoice"] = int(choice)
with open(path, "w") as f:
    json.dump(d, f, indent=2)
PY
  echo "== void candidate (STARTING POSITION ONLY, the panel switches it): $TIER1_VOID"
fi
# TIER1_VOID_RING — the void ring the handset runs. The template carries the 2026-09-05 interim
# fallback (1); a shadow build runs 0, because the void is dark by occlusion now (§12.1a) and a
# ring under it would be the outline the clause forbids, laid twice.
if [ -n "${TIER1_VOID_RING:-}" ]; then
  python3 - "$MARKER" "$TIER1_VOID_RING" <<'PY'
import json, sys
path, ring = sys.argv[1], sys.argv[2]
with open(path) as f:
    d = json.load(f)
d["voidRing"] = int(ring)
with open(path, "w") as f:
    json.dump(d, f, indent=2)
PY
  echo "== void ring: $TIER1_VOID_RING (marker; the manifest's ruled 0 is what a shadow build runs)"
fi

# TIER1_OCCLUDERS / TIER1_SHADOW_SOFTNESS / TIER1_FIRE_FLICKER — the cast-shadows round's knobs,
# mirroring --occluders / --shadow-softness / --fire-flicker for a handset that has no command
# line. Omit them and the build has NO occluders (the marker's default), which is every build
# before the round; a shadow build must say so, and the engine echoes what it got.
if [ -n "${TIER1_OCCLUDERS:-}" ]; then
  python3 - "$MARKER" "$TIER1_OCCLUDERS" "${TIER1_SHADOW_SOFTNESS:-12.0}" "${TIER1_FIRE_FLICKER:-1}" "${TIER1_SHADOW_DARKNESS:-0.8}" <<'PY'
import json, sys
path, occ, soft, flick, dark = sys.argv[1:6]
with open(path) as f:
    d = json.load(f)
d["occluders"] = occ
d["shadowSoftness"] = float(soft)
d["shadowDarkness"] = float(dark)
d["fireFlicker"] = flick == "1"
with open(path, "w") as f:
    json.dump(d, f, indent=2)
PY
  echo "== cast shadows: occluders=$TIER1_OCCLUDERS softness=${TIER1_SHADOW_SOFTNESS:-12.0} darkness=${TIER1_SHADOW_DARKNESS:-0.8} flicker=${TIER1_FIRE_FLICKER:-1}"
fi

# TIER1_VSYNC=0 — a HEADROOM measurement build: vsync off so [Perf] reports render cost. Never
# on a gate build; the engine echoes it so the log says which kind of numbers it holds.
if [ "${TIER1_VSYNC:-1}" = "0" ]; then
  python3 - "$MARKER" <<'PY'
import json, sys
path = sys.argv[1]
with open(path) as f:
    d = json.load(f)
d["vsync"] = False
with open(path, "w") as f:
    json.dump(d, f, indent=2)
PY
  echo "== vsync: OFF (headroom measurement build)"
fi

# STAMP THE BUILD'S OWN IDENTITY INTO THE MARKER.
#
# LOOP-PROCESS §2.3: every evidence file records the commit hash of the code that produced it,
# and a hash mismatch at a ruling invalidates the evidence. A DEVICE BUILD carried none — the
# headless captures stamp their commit, the app did not — so "is the thing on the phone the thing
# in the branch?" could only be answered by trusting whoever built it.
#
# That is exactly the question a device verification exists to answer, and it is why exit 0 from
# an export is not a deployment. The app now reports its commit and build time into Diag at boot,
# where they can be pulled back off the handset.
COMMIT="$(git -C "$ROOT" rev-parse HEAD 2>/dev/null || echo UNKNOWN)"
if [ -n "$(git -C "$ROOT" status --porcelain 2>/dev/null | head -1)" ]; then
  COMMIT="$COMMIT+dirty"
fi
BUILT_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
# reviewStatus RIDES WITH THE IDENTITY, and that placement is the point: it is not a build option,
# it is a fact about what this build was allowed past. Empty on a gated build; SKIPPED-REVIEW when
# YARL_SKIP_CRITIC waved it through, in which case the app draws it on screen for as long as the
# build is on the handset. An override you cannot see from the phone is the same as no gate.
python3 - "$MARKER" "$COMMIT" "$BUILT_AT" "${REVIEW_STATUS:-}" <<'PY'
import json, sys
path, commit, built, status = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
with open(path) as f:
    d = json.load(f)
d["commit"] = commit
d["builtAt"] = built
d["reviewStatus"] = status or None
with open(path, "w") as f:
    json.dump(d, f, indent=2)
PY
echo "== build identity: commit=$COMMIT built=$BUILT_AT"
if [ -n "${REVIEW_STATUS:-}" ]; then
  echo "== ⚠ $REVIEW_STATUS — this build has no frame-critic PASS. The phone will say so."
fi
echo "== marker written: $(basename "$MARKER")"
echo "== grid: $(python3 -c 'import json,sys;d=json.load(open(sys.argv[1]));print("tile %s at x%s" % (d.get("tileSize","default"), d.get("tileScale","default")))' "$MARKER")"
echo "== bundle id: $BUNDLE_ID   name: $NAME"

# TIER0_MARKER_ONLY=1 stops here, after the marker is written and BEFORE the export, and prints
# it. It exists so the SKIPPED-REVIEW stamp can be shown to appear — LOOP-PROCESS §4, no check's
# pass counts until it has demonstrated it can fail — without an xcodebuild and a handset, and
# without a test that reimplements the stamping and then proves the reimplementation.
#
# The marker is printed rather than left behind because the trap removes it on exit, and it must:
# a leftover marker turns the next ordinary build into a review build.
if [ "${TIER0_MARKER_ONLY:-}" = "1" ]; then
  echo "== MARKER ONLY — stopping before the export. The marker as written:"
  cat "$MARKER"
  echo
  exit 0
fi

# The marker is a new res:// file, so it must be imported before it can be packed.
"${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}" --headless --path "$ROOT" --import >/dev/null 2>&1 || true

"$ROOT/tools/ios_build.sh" --bundle-id "$BUNDLE_ID" --name "$NAME" "$@"
