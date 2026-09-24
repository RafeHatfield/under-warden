#!/bin/bash
# VERIFY A REVIEW BUILD ON THE HANDSET. Not "it exported" — "it is installed, it booted, and it
# booted into the scene it was supposed to."
#
# WHY THIS EXISTS
# ---------------
# `build_review_app.sh --no-install` exiting 0 says the export -> xcodebuild chain ran. It says
# nothing about whether anything reached the device, and a session reported a build as
# "verified on device" on exactly that basis. It was not installed at all.
#
# This project already has the logged instance that makes the distinction non-negotiable: the
# Hollowmark ribbon was silent on device while every headless capture was clean, because NativeAOT
# had not registered the YAML DTOs. A build that compiles, exports, installs AND LAUNCHES can
# still do nothing — so the only evidence that counts is the app's own log, pulled back off the
# handset.
#
# It reports the three identifiers a walk needs to be evidence under LOOP-PROCESS §2.3:
#   BUNDLE ID   read off the DEVICE, not claimed by the app. An app asserting its own identity is
#               the weakest possible evidence of it.
#   BUILD       version/build as the device records it, plus the app's own builtAt stamp.
#   COMMIT      stamped into the review marker at build time and reported at boot.
#
# Usage:  tools/tier0_harness/verify_on_device.sh [--device <id-or-name>] [--bundle <id>]
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DEVICE="${TIER0_DEVICE:-}"
BUNDLE="${TIER0_BUNDLE_ID:-com.rafehatfield.underwarden.tier0}"
OUT="$ROOT/tools/tier1_floors/evidence"
CHECK_ONLY=""

while [ $# -gt 0 ]; do
	case "$1" in
		--device) DEVICE="$2"; shift 2 ;;
		--bundle) BUNDLE="$2"; shift 2 ;;
		--out)    OUT="$2"; shift 2 ;;
		# --check-log runs THE CHECKS ONLY, against a log already on disk, and touches no device.
		# It exists so the checks can be shown to FAIL — bible §13.5, no instrument's pass counts
		# until it has demonstrated it can fail — WITHOUT copying their expressions into a test.
		# A test that reimplements the thing it tests proves the reimplementation.
		--check-log) CHECK_ONLY="$2"; shift 2 ;;
		*) echo "unknown arg: $1" >&2; exit 2 ;;
	esac
done

if [ -n "$CHECK_ONLY" ]; then
	LOG="$CHECK_ONLY"
	[ -f "$LOG" ] || { echo "no such log: $LOG" >&2; exit 2; }
	echo "== CHECKS ONLY, against $LOG (no device touched)"
fi

if [ -z "$CHECK_ONLY" ]; then
# DEVICE RESOLUTION FROM STRUCTURED OUTPUT, NOT FROM COLUMNS.
#
# The first version parsed `devicectl list devices` with awk, taking $(NF-2) as the identifier.
# The Model column contains spaces — "iPhone SE (3rd generation)" — so it picked up "(3rd" and
# then queried a device that does not exist. `device info apps` returned nothing, and the script
# reported NOT INSTALLED about an app that was installed. **A confident wrong answer from a bad
# input**, which is the failure this whole harness exists to prevent.
if [ -z "$DEVICE" ]; then
	DEVJSON="$(mktemp)"
	xcrun devicectl list devices --json-output "$DEVJSON" >/dev/null 2>&1 || true
	DEVICE="$(python3 - "$DEVJSON" <<'PY'
import json, sys
try:
    d = json.load(open(sys.argv[1]))
except Exception:
    sys.exit(0)
for x in d.get("result", {}).get("devices", []):
    print(x.get("identifier", ""))
    break
PY
)"
	rm -f "$DEVJSON"
fi
[ -n "$DEVICE" ] || { echo "no paired device found (xcrun devicectl list devices)" >&2; exit 1; }

mkdir -p "$OUT"
echo "== device: $DEVICE"

# ---- 1. IS IT ACTUALLY INSTALLED? -----------------------------------------------------------
#
# "COULD NOT ASK" AND "THE DEVICE SAID NO" ARE DIFFERENT ANSWERS and must not share an exit path.
# A disconnected or locked handset returns no app list, and reporting that as NOT INSTALLED sends
# somebody to rebuild and redeploy something that is already there.
echo "== installed apps, as the DEVICE reports them"
if ! APPS="$(xcrun devicectl device info apps --device "$DEVICE" 2>&1)"; then
	echo ""
	echo "CANNOT ASK THE DEVICE — the query failed. This is NOT a statement about whether the" >&2
	echo "app is installed; it means the handset did not answer. Is it connected and unlocked?" >&2
	echo "  xcrun devicectl list devices" >&2
	echo "$APPS" | tail -5 >&2
	exit 7
fi
echo "$APPS" | grep -iE "catacombs|yarl" || true
if ! echo "$APPS" | grep -qE "^[[:space:]]*[A-Za-z]"; then
	echo ""
	echo "CANNOT ASK THE DEVICE — it returned an empty app list, which no paired device does." >&2
	echo "Treating this as unanswered rather than as 'not installed'." >&2
	exit 7
fi
if ! echo "$APPS" | grep -q "$BUNDLE"; then
	echo ""
	echo "NOT INSTALLED: the device answered, and $BUNDLE is not among the apps it listed." >&2
	echo "An export exiting 0 is not a deployment. Run build_review_app.sh WITHOUT --no-install." >&2
	exit 3
fi
LINE="$(echo "$APPS" | grep "$BUNDLE" | head -1)"
echo ""
echo "== FOUND ON DEVICE: $LINE"

# ---- 2. LAUNCH IT, so it writes a fresh log -------------------------------------------------
echo "== launching $BUNDLE"
xcrun devicectl device process launch --device "$DEVICE" --terminate-existing "$BUNDLE" \
	> "$OUT/device-launch.log" 2>&1 || {
		tail -10 "$OUT/device-launch.log" >&2
		echo "launch failed — is the device unlocked?" >&2; exit 4; }
sleep 12    # let it boot, build the scene, and flush its first lines

# ---- 3. PULL THE APP'S OWN LOG --------------------------------------------------------------
# Diag writes to OS.GetUserDataDir(), which is Documents/ inside the app data container.
echo "== pulling Documents/diag.log"
rm -f "$OUT/DEVICE-tier1-boot.log"
xcrun devicectl device copy from --device "$DEVICE" \
	--domain-type appDataContainer --domain-identifier "$BUNDLE" \
	--source Documents/diag.log --destination "$OUT/DEVICE-tier1-boot.log" \
	> "$OUT/device-copy.log" 2>&1 || {
		tail -10 "$OUT/device-copy.log" >&2
		echo "log pull failed — the app may not have written one (is it a debug build?)" >&2
		exit 5; }

# ---- 4. WHAT THE LOG HAS TO SAY -------------------------------------------------------------
LOG="$OUT/DEVICE-tier1-boot.log"
echo ""
echo "== the three identifiers, from the handset"
grep -m1 "BUILD IDENTITY" "$LOG" || echo "  (no BUILD IDENTITY line — build predates the stamp)"
echo "  bundle (device): $BUNDLE"

fi   # end of the device interaction

echo ""
echo "== did it boot into the right scene, with the rig live?"
FAIL=0
check() {   # a name, and the pattern that proves it
	if grep -q "$2" "$LOG"; then
		echo "  OK    $1"
	else
		echo "  MISS  $1   (expected /$2/)"
		FAIL=1
	fi
}
# THE THEME CHECK USED TO NAME A DIRECTORY, AND THE DIRECTORY MOVED.
#
# It read `tile_theme_config=.*tier1_floors`, which was true when the tier-one family lived in
# `assets/tier1_floors/`. The course-aligned ashlar family lives in `assets/tier1_ashlar/`, so a
# correct build failed this check while the log plainly showed the right theme in force.
#
# The temptation is to widen the pattern until it passes. That is relaxing a test after reading a
# result, and it is the same error as deriving a plant's word list from a transcript. What follows
# is STRICTER than what it replaces, in two ways it could not manage before:
#
#   CONSISTENCY. The theme and the floor family must name the SAME directory. A hardcoded path
#   could never catch a build that laid one family's tiles under another family's theme — the
#   failure that actually costs a gate, because it looks entirely plausible.
#
#   THE FLOOR ACTUALLY LAID. `missing=0` and all three cross-checks green, ON THE HANDSET. The old
#   check could not see whether a single tile had been placed, let alone whether the engine
#   reproduced the composer's bond arithmetic, its material arithmetic and its finished pixels.
# THE SCENE NAME IS A PARAMETER, and it became one the first time a build was verified that was
# not the floor gate's. It read `tier1_floor_review` literally, so the WALL review build - the
# correct scene, correctly booted - came back MISS. The fix is not to widen it to `tier1_.*`,
# which would green a build that booted the wrong tier-one scene; it is to state which scene the
# operator asked for and check for THAT.
# THE EXPECTATION FOLLOWS THE BUILD. `TIER0_SCENE` is what the BUILD was told to boot, so setting
# it the same way the build did carries the expectation with it and there is nothing extra to
# remember. `TIER0_EXPECT_SCENE` still overrides, for checking a log whose build env is gone.
#
# ⚠ AND THE DEFAULT WENT STALE THE MOMENT THE MARKER STARTED CARRYING THE SCENE. `tier1_floor_review`
# was hardcoded here when a review build was told what to boot by TIER0_SCENE or by nothing at all.
# Since 2026-09-06 REVIEW_BUILD.json.template names the scene — it is what the handset actually
# reads — so the combined build booted `tier1_combined_review`, correctly, and this check reported
# MISS against a scene nobody had asked for. A false negative on the one instrument that reads the
# handset is worse than no check: it teaches the operator to skim past NOT VERIFIED.
#
# THE DEFAULT NOW FOLLOWS THE SAME FILE THE BUILD DOES, which is this clause's own stated principle
# rather than a widening. It cannot green a build that booted the wrong scene: the expectation comes
# from the CONFIG and is compared against what the ENGINE LOGGED, so a build that booted something
# other than what it was told still misses. The hardcoded value survives only as the last fallback,
# for a template that cannot be read.
EXPECT_SCENE="tier1_floor_review"
_TPL="$ROOT/src/Presentation/assets/tier0_harness/REVIEW_BUILD.json.template"
if [ -f "$_TPL" ]; then
	_MARKER_SCENE="$(python3 - "$_TPL" <<'PYEOF'
import json, os, sys
try:
    s = json.load(open(sys.argv[1])).get("scene") or ""
    print(os.path.basename(s).removesuffix(".json"))
except Exception:
    print("")
PYEOF
)"
	[ -n "$_MARKER_SCENE" ] && EXPECT_SCENE="$_MARKER_SCENE"
fi
if [ -n "${TIER0_SCENE:-}" ]; then
	EXPECT_SCENE="$(basename "${TIER0_SCENE%.json}")"
fi
EXPECT_SCENE="${TIER0_EXPECT_SCENE:-$EXPECT_SCENE}"
check "booted the review scene"        "corridor scene: $EXPECT_SCENE"

# ⚠ IS THIS EVEN THE BUILD YOU MADE? Two sessions building review apps share one bundle id and the
# last install silently wins. A wall session overwrote a floor gate build on this handset and the
# only symptom was the scene check going MISS — which reads as *your build is wrong* when the
# truth is *your build is gone*. The handset reports the commit it was built from; compare it to
# HEAD. Adopted from the floor gate's verify path, where the incident happened.
DEVICE_STAMP="$(grep -oE 'BUILD IDENTITY: commit=[0-9a-f]+(\+dirty)?' "$LOG" 2>/dev/null \
                | head -1 | sed 's/.*commit=//' || true)"
DEVICE_COMMIT="${DEVICE_STAMP%+dirty}"
LOCAL_COMMIT="$(git -C "$ROOT" rev-parse HEAD 2>/dev/null || true)"
if [ -n "$DEVICE_COMMIT" ] && [ -n "$LOCAL_COMMIT" ]; then
	if [ "${LOCAL_COMMIT#$DEVICE_COMMIT}" != "$LOCAL_COMMIT" ]; then
		# A DIRTY BUILD MATCHES ON SHA AND NOT ON PIXELS, so it is reported as what it is. The
		# commit check answers "is this the build you made"; a stamp that says +dirty means the
		# sha alone cannot answer it, and saying OK there would be the claim without the evidence.
		if [ "$DEVICE_STAMP" != "$DEVICE_COMMIT" ]; then
			echo "  OK*   handset is at this HEAD, but the build was DIRTY ($DEVICE_STAMP)"
			echo "        The sha matches; the working tree at build time did not. Commit and"
			echo "        rebuild before treating this as a reproducible gate build (§2.3)."
		else
			echo "  OK    the build on the handset is this working copy's HEAD"
		fi
	elif git merge-base --is-ancestor "$DEVICE_COMMIT" HEAD 2>/dev/null; then
		# ── YOUR OWN HEAD MOVED ON, WHICH IS NOT THE HAZARD THIS CHECK EXISTS FOR ───────────
		#
		# The guard was written because a wall session overwrote the floor gate's build on this
		# handset and the only symptom was a scene check going MISS — "your build is wrong" when
		# the truth was "your build is gone". That hazard is a device commit this working copy
		# has NEVER SEEN. A device commit that is an ANCESTOR of HEAD is the opposite: it is a
		# build this copy made, with work committed after it.
		#
		# Reporting both as "A DIFFERENT BUILD" made the operator do the archaeology every time,
		# and an instrument that cries wolf on the ordinary case is how the real one gets
		# skimmed past. So the two are now told apart, and the ancestor case says exactly what
		# is owed: whether anything SHIPPED changed since, because that is what decides if the
		# handset is still carrying the gated art.
		BEHIND="$(git rev-list --count "$DEVICE_COMMIT"..HEAD 2>/dev/null || echo '?')"
		# `grep -v` exits 1 when NOTHING survives it — i.e. exactly when nothing shipped changed —
		# and under pipefail that killed the script silently at this assignment, so the OK* line
		# below could never be reached. Found 2026-09-12 verifying five ancestor builds.
		SHIPPED_CHANGED="$(git diff --name-only "$DEVICE_COMMIT" HEAD -- \
			src/Presentation/assets src/Presentation/Map src/Logic 2>/dev/null \
			| { grep -v '\.import$' || true; } | wc -l | tr -d ' ')"
		if [ "$SHIPPED_CHANGED" = "0" ]; then
			echo "  OK*   the handset build is an ANCESTOR of HEAD ($BEHIND commit(s) behind)"
			echo "        and NOTHING SHIPPED changed since — the device carries this art."
		else
			echo "  MISS  the handset build is $BEHIND commit(s) behind HEAD, and"
			echo "        $SHIPPED_CHANGED shipped file(s) changed since. The device is NOT"
			echo "        carrying what this working copy would build. Re-install, or verify"
			echo "        against the commit the gate covered."
			git diff --name-only "$DEVICE_COMMIT" HEAD -- \
				src/Presentation/assets src/Presentation/Map src/Logic 2>/dev/null \
				| grep -v '\.import$' | sed 's/^/          /' || true
			echo "        ⚠ SOURCE changing is not the same as the PICTURE changing. If the"
			echo "          delivered frame is byte-identical the handset still carries the"
			echo "          gated art — but that has to be measured, not assumed, and this"
			echo "          check deliberately will not assume it."
		fi
	else
		echo "  MISS  THE HANDSET IS RUNNING A DIFFERENT BUILD"
		echo "        device: $DEVICE_STAMP"
		echo "        HEAD:   $LOCAL_COMMIT"
		echo "        Review builds from different sessions share a bundle id and the last"
		echo "        install wins. Rebuild under your own TIER0_BUNDLE_ID rather than racing"
		echo "        for the default one."
		FAIL=1
	fi
fi
check "incident overlays attached"     "floor overlays: cells="
check "rig panel constructed"          "\[Tier1\] rig:start:"
check "no losable state"               "losable-state check:"
check "floor family laid, every cross-check green" \
      "floor: laid=[1-9][0-9]* .*missing=0 .*edge_check=[0-9]*/OK stone_check=[0-9]*/OK paint_check=[0-9]*/OK"
# THE WALL FAMILY, CHECKED ONLY WHERE ONE WAS ASKED FOR. Set TIER0_EXPECT_WALLS=1 for a wall
# review build. It is opt-in rather than always-on because a floor gate build legitimately has no
# wall family, and a check that fails on a build that was never meant to satisfy it teaches the
# operator to ignore checks.
#
# What it requires is the same shape the floor's does: tiles actually laid, nothing missing, and
# the composer's boundary arithmetic reproduced BY THE ENGINE, on the handset. `missing=0` alone
# would pass a build that laid every cell from the wrong side of a disagreement.
if [ -n "${TIER0_EXPECT_WALLS:-}" ]; then
	check "wall family laid, edge families reproduced" \
	      "boundary wall: .*missing=0 .*edge_check=[0-9]*/OK"
	check "orc bindings placed"            "boundary wall: .*bindings=[1-9][0-9]*("
fi

# `|| true` ON BOTH, AND IT IS NOT DECORATION. Under `set -euo pipefail` a grep that matches
# nothing fails, and a failing command substitution ABORTS THE SCRIPT — so the single likeliest
# real failure, the floor never being laid at all, exited 1 with a bare shell error instead of
# reporting NOT VERIFIED. An abort and a verdict are different things, which is the same
# distinction this script already draws between "could not ask the device" and "the device said
# no". Caught by the failability test, not by reading the code.
THEME_DIR="$(grep -o 'tile_theme_config=res://[^ ]*' "$LOG" 2>/dev/null | head -1 \
	| sed 's#.*/assets/\([^/]*\)/.*#\1#' || true)"
FAMILY_DIR="$(grep -o 'floor: laid=.*manifest=res://[^ ]*' "$LOG" 2>/dev/null | head -1 \
	| sed 's#.*/assets/\([^/]*\)/.*#\1#' || true)"
if [ -n "$THEME_DIR" ] && [ "$THEME_DIR" = "$FAMILY_DIR" ]; then
	echo "  OK    theme and floor family agree   ($THEME_DIR)"
else
	echo "  MISS  theme and floor family disagree   (theme=${THEME_DIR:-none} family=${FAMILY_DIR:-none})"
	echo "        A build laying one family's tiles under another family's theme looks entirely"
	echo "        plausible in a screenshot. That is why this is checked and not assumed."
	FAIL=1
fi

echo ""
grep -m1 "floor overlays: cells=" "$LOG" | sed 's/^/  /' || true
grep -m1 "light rig:" "$LOG" | sed 's/^/  /' || true

echo ""
if [ "$FAIL" = "0" ]; then
	# The scene NAME is read back FROM THE LOG rather than restated, because a summary that names
	# a scene it did not check is the same class of claim as an app asserting its own bundle id.
	# It said it did this and it did not: it echoed `TIER0_EXPECT_SCENE`, so a run driven by
	# `TIER0_SCENE` alone reported VERIFIED "into tier1_floor_review" after checking, correctly,
	# for tier1_wall_review. The check was right and the sentence was a fabrication.
	BOOTED="$(grep -oE 'corridor scene: [A-Za-z0-9_.-]+' "$LOG" 2>/dev/null | head -1 \
		| sed 's/corridor scene: //' || true)"
	echo "VERIFIED ON DEVICE — installed, launched, booted into ${BOOTED:-$EXPECT_SCENE}, rig live."
	echo "log: ${LOG#$ROOT/}"
else
	echo "NOT VERIFIED — the app is installed but did not report what it should have." >&2
	echo "log: ${LOG#$ROOT/}" >&2
	exit 6
fi
