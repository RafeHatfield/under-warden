#!/bin/bash
# The Under-Warden — headless iOS build + install. No manual Xcode step, ever.
#
# Ported from Gemfall's tools/ios_build.sh (~/development/deathmatch, commit f2a15bc),
# which proved the export -> xcodebuild -> devicectl chain runs fully unattended.
# This version adds the preflights a C#/.NET Godot project needs and that a
# GDScript project does not. See docs/ios-export-pipeline.md for the why.
#
#   export (Godot mono, headless)  ->  xcodebuild  ->  verify bundle identity  ->  devicectl install
#
# Usage:
#   tools/ios_build.sh                                      # build this tree, install to the default device
#   tools/ios_build.sh --no-install                         # build only
#   tools/ios_build.sh --bundle-id com.x.y --name "YARL b"  # side-by-side variant install
#   tools/ios_build.sh --tree /path/to/patched/copy         # build a throwaway patched tree
#   tools/ios_build.sh --release                            # export-release instead of export-debug
#   tools/ios_build.sh --import                             # force reimport before exporting
#
# Env overrides: GODOT, DEVICE_ID, TEAM_ID, OUT, PRESET.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

TREE="$ROOT"
BUNDLE_ID=""                 # empty => whatever export_presets.cfg specifies
DISPLAY_NAME=""              # empty => whatever the preset specifies
INSTALL=1
FORCE_IMPORT=0
EXPORT_MODE="--export-debug"
CONFIG="Debug"

# The C#/Mono editor build. The plain Godot.app CANNOT open this project — see preflight.
GODOT="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
DEVICE_ID="${DEVICE_ID:-5DB969FF-269C-5A8A-86EB-99EC9FF22397}"   # Rafe's iPhone SE 3 ("Jiminy Cricket")
TEAM_ID="${TEAM_ID:-S2WHBSKV97}"
PRESET="${PRESET:-iOS}"
OUT="${OUT:-$ROOT/.ios-build}"
XCPROJ_NAME="UnderWarden"

while [ $# -gt 0 ]; do
	case "$1" in
		--tree)         TREE="$(cd "$2" && pwd)"; shift 2 ;;
		--bundle-id)    BUNDLE_ID="$2"; shift 2 ;;
		--name)         DISPLAY_NAME="$2"; shift 2 ;;
		--device)       DEVICE_ID="$2"; shift 2 ;;
		--out)          OUT="$2"; shift 2 ;;
		--preset)       PRESET="$2"; shift 2 ;;
		--no-install)   INSTALL=0; shift ;;
		--import)       FORCE_IMPORT=1; shift ;;
		--release)      EXPORT_MODE="--export-release"; CONFIG="Release"; shift ;;
		-h|--help)      sed -n '2,25p' "$0"; exit 0 ;;
		*) echo "unknown arg: $1" >&2; exit 2 ;;
	esac
done

# --- preflight ----------------------------------------------------------------
# Every one of these has cost real time when it failed silently downstream.
fail() { echo "FATAL: $*" >&2; exit 1; }
warn() { echo "WARN: $*" >&2; }

[ -x "$GODOT" ] || fail "Godot not at $GODOT (override with GODOT=...)"

# C#-SPECIFIC. The Under-Warden is a .NET project; the standard Godot build has no
# C# support and fails on it in ways that do not name the cause. /Applications holds
# both Godot.app and Godot_mono.app here, so picking the wrong one is one typo away.
GODOT_VERSION="$("$GODOT" --version 2>/dev/null | tail -1)"
case "$GODOT_VERSION" in
	*mono*|*dotnet*) : ;;
	*) fail "'$GODOT' is not a C#/Mono Godot build (reports: $GODOT_VERSION)
  This project is C#. Use the .NET editor build:
    GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot $0 ..." ;;
esac

command -v xcodebuild >/dev/null || fail "xcodebuild not on PATH — install Xcode command line tools"
command -v dotnet     >/dev/null || fail "dotnet not on PATH — .NET 8 SDK required to export a C# project"

# C#-SPECIFIC. Godot shells out to 'dotnet publish -r ios-arm64' during export. Without
# the ios workload that publish fails inside Godot's export log, not on your terminal.
dotnet workload list 2>/dev/null | grep -qE '^\s*ios\s' \
	|| fail "the .NET 'ios' workload is not installed — Godot's export shells out to
  'dotnet publish -r ios-arm64' and will fail inside the export log.
  Fix:  dotnet workload install ios"

# export_presets.cfg is TRACKED in this repo (unlike Gemfall, where it is gitignored),
# so a normal checkout has it. A worktree pinned to a commit that predates the preset
# still will not, and Godot's error ("no export preset") does not say why.
[ -f "$TREE/export_presets.cfg" ] || fail "no export_presets.cfg in $TREE
  Fix:  cp $ROOT/export_presets.cfg $TREE/"
grep -q "^name=\"$PRESET\"" "$TREE/export_presets.cfg" \
	|| fail "no export preset named '$PRESET' in $TREE/export_presets.cfg
  Presets present: $(grep '^name=' "$TREE/export_presets.cfg" | tr '\n' ' ')"

# The iOS export TEMPLATE is separate from the editor and is not in the repo. A C#
# project needs the .mono templates specifically — the plain ones will not do.
TPL_ROOT="$HOME/Library/Application Support/Godot/export_templates"
TPL_DIR="$(echo "$GODOT_VERSION" | sed -E 's/\.(official|custom_build)\..*$//')"
if [ ! -d "$TPL_ROOT/$TPL_DIR" ]; then
	if ls "$TPL_ROOT" 2>/dev/null | grep -q mono; then
		warn "no export templates for exactly '$TPL_DIR'; found: $(ls "$TPL_ROOT" | tr '\n' ' ')"
	else
		fail "no Godot .NET export templates installed — Editor > Manage Export Templates
  (a C# project needs the .mono templates, not the standard ones)"
	fi
fi

security find-identity -v -p codesigning 2>/dev/null | grep -q "Apple Development" \
	|| fail "no 'Apple Development' codesigning identity in the keychain"

# C#-SPECIFIC. project.godot's assembly_name and the csproj's AssemblyName must agree,
# or Godot builds an assembly it then cannot find at load. This repo has shipped that
# mismatch to main before; it presents as a blank window, not as an error.
ASM_GODOT="$(sed -n 's/^project\/assembly_name="\(.*\)"$/\1/p' "$TREE/project.godot" | head -1)"
CSPROJ=""
if [ -n "$ASM_GODOT" ]; then
	CSPROJ="$TREE/$ASM_GODOT.csproj"
	if [ -f "$CSPROJ" ]; then
		ASM_CS="$(sed -n 's/.*<AssemblyName>\(.*\)<\/AssemblyName>.*/\1/p' "$CSPROJ" | head -1)"
		[ -z "$ASM_CS" ] || [ "$ASM_CS" = "$ASM_GODOT" ] \
			|| fail "assembly name mismatch — Godot will not find the built assembly at runtime
  project.godot  project/assembly_name = $ASM_GODOT
  $(basename "$CSPROJ")  <AssemblyName> = $ASM_CS"
	else
		fail "project.godot sets project/assembly_name=\"$ASM_GODOT\" but there is no
  $ASM_GODOT.csproj beside project.godot. Godot resolves the C# project file as
  <project_dir>/<assembly_name>.csproj, so its .NET publish will fail — and it fails
  SILENTLY, producing an installable app containing no C# at all.
  Root csproj present: $(cd "$TREE" && ls *.csproj 2>/dev/null | tr '\n' ' ')
  Fix: set project/assembly_name to the csproj basename."
	fi
fi
# Two .sln files sit at the repo root, so 'dotnet build <dir>' is ambiguous (MSB1011).
# Always name the Godot csproj.
[ -n "$CSPROJ" ] || CSPROJ="$(find "$TREE" -maxdepth 1 -name '*.csproj' -print -quit)"

mkdir -p "$OUT"
rm -rf "${OUT:?}/xcodeproj"
mkdir -p "$OUT/xcodeproj"

# --- 0. prepare a cold tree ---------------------------------------------------
# A fresh worktree or an rsync'd --tree copy has no .godot/, so nothing is imported
# and the export produces an app with no assets. Build once, then import.
if [ "$FORCE_IMPORT" = "1" ] || [ ! -d "$TREE/.godot" ]; then
	echo "== prepare (cold tree: dotnet build + import)"
	dotnet build "$CSPROJ" > "$OUT/dotnet-build.log" 2>&1 \
		|| { tail -20 "$OUT/dotnet-build.log" >&2; fail "dotnet build failed — see $OUT/dotnet-build.log"; }
	"$GODOT" --headless --path "$TREE" --import > "$OUT/import.log" 2>&1 \
		|| { tail -20 "$OUT/import.log" >&2; fail "godot import failed — see $OUT/import.log"; }
fi

# --- 1. export ----------------------------------------------------------------
echo "== export ($EXPORT_MODE, preset '$PRESET') from $TREE"
"$GODOT" --headless --path "$TREE" "$EXPORT_MODE" "$PRESET" "$OUT/xcodeproj/$XCPROJ_NAME.xcodeproj" \
	> "$OUT/export.log" 2>&1 \
	|| { tail -30 "$OUT/export.log" >&2; fail "godot export failed — see $OUT/export.log"; }

# C#-SPECIFIC AND CRITICAL. If the .NET publish fails, Godot logs the error, carries on
# packing the .pck, and STILL EXITS 0. You then get a signed, installable app with no
# managed code in it at all — it launches to a black screen. Never trust the exit code.
if grep -qE "Export \.NET Project|Failed to build project" "$OUT/export.log"; then
	grep -E "ERROR|error" "$OUT/export.log" | head -10 >&2
	fail "the .NET publish failed, so this build would contain NO C# code (Godot still exits 0).
  Most common cause: project.godot's project/assembly_name must be the BASENAME OF THE
  CSPROJ — Godot resolves the project file as <assembly_name>.csproj. See
  docs/ios-export-pipeline.md.
  Full log: $OUT/export.log"
fi

# Godot names the generated project after the export path, but do not assume it:
# read the scheme back out rather than hardcoding one that may drift.
XCPROJ="$(find "$OUT/xcodeproj" -maxdepth 2 -name '*.xcodeproj' -print -quit)"
[ -n "$XCPROJ" ] || { tail -30 "$OUT/export.log" >&2; fail "export produced no .xcodeproj — see $OUT/export.log"; }
SCHEME="$(basename "$XCPROJ" .xcodeproj)"
XCDIR="$(dirname "$XCPROJ")"

# --- 2. xcodebuild ------------------------------------------------------------
# -allowProvisioningUpdates is what makes this unattended: it lets Xcode mint
# the provisioning profile without opening the GUI.
echo "== xcodebuild ($CONFIG, scheme '$SCHEME')"
XCARGS=(
	-project "$(basename "$XCPROJ")"
	-scheme "$SCHEME"
	-configuration "$CONFIG"
	-destination 'generic/platform=iOS'
	-allowProvisioningUpdates
	-derivedDataPath ./dd
	DEVELOPMENT_TEAM="$TEAM_ID"
)
[ -n "$BUNDLE_ID" ]    && XCARGS+=( PRODUCT_BUNDLE_IDENTIFIER="$BUNDLE_ID" )
[ -n "$DISPLAY_NAME" ] && XCARGS+=( INFOPLIST_KEY_CFBundleDisplayName="$DISPLAY_NAME" )

( cd "$XCDIR" && xcodebuild "${XCARGS[@]}" build > "$OUT/xcodebuild.log" 2>&1 ) \
	|| { grep -E "error:|BUILD FAILED" "$OUT/xcodebuild.log" | head -20 >&2
	     fail "xcodebuild failed — see $OUT/xcodebuild.log"; }

APP="$(find "$XCDIR/dd/Build/Products/$CONFIG-iphoneos" -maxdepth 1 -name '*.app' -print -quit 2>/dev/null || true)"
[ -n "$APP" ] && [ -d "$APP" ] || fail "no .app produced under $XCDIR/dd/Build/Products/$CONFIG-iphoneos"

# --- 3. verify the bundle actually carries what we asked for ------------------
# A silently-ignored override is the nastiest failure here: you get N apps all
# with the same bundle id, each install replacing the last, and a side-by-side
# comparison that is quietly comparing a build to itself.
GOT_ID=$(/usr/libexec/PlistBuddy -c "Print :CFBundleIdentifier" "$APP/Info.plist")
GOT_NAME=$(/usr/libexec/PlistBuddy -c "Print :CFBundleDisplayName" "$APP/Info.plist" 2>/dev/null || echo "")
if [ -n "$BUNDLE_ID" ] && [ "$GOT_ID" != "$BUNDLE_ID" ]; then
	fail "bundle id override did not take (asked $BUNDLE_ID, got $GOT_ID)"
fi
if [ -n "$DISPLAY_NAME" ] && [ "$GOT_NAME" != "$DISPLAY_NAME" ]; then
	fail "display name override did not take (asked '$DISPLAY_NAME', got '$GOT_NAME')"
fi

# C#-SPECIFIC. Second half of the same trap: confirm from the ARTEFACT that the managed
# code is really in there. NativeAOT lands the C# as a framework in the bundle; a build
# whose .NET publish silently failed has an otherwise complete, signable, installable
# bundle with that framework simply missing.
if [ -n "$ASM_GODOT" ]; then
	[ -d "$APP/Frameworks/$ASM_GODOT.framework" ] \
		|| fail "no $ASM_GODOT.framework in the app bundle — the C# code did not make it in.
  This app would install and launch to a black screen.
  Bundle has: $(ls "$APP/Frameworks" 2>/dev/null | tr '\n' ' ')"
fi

echo "   built: ${GOT_NAME:-$SCHEME}  ($GOT_ID)  [C#: $ASM_GODOT.framework]"

# --- 4. install ---------------------------------------------------------------
if [ "$INSTALL" = "1" ]; then
	echo "== devicectl install -> $DEVICE_ID"
	# devicectl intermittently drops the tunnel on the first attempt
	# ("device disconnected immediately after connecting", CoreDeviceError 4000).
	# It is a flake, not a build problem — one retry keeps the run unattended.
	INSTALLED=0
	for attempt in 1 2 3; do
		if xcrun devicectl device install app --device "$DEVICE_ID" "$APP" > "$OUT/install.log" 2>&1; then
			INSTALLED=1; break
		fi
		echo "   install attempt $attempt failed; retrying..." >&2
		sleep 5
	done
	[ "$INSTALLED" = "1" ] || { tail -20 "$OUT/install.log" >&2
		echo "  (is the device unlocked and connected? list: xcrun devicectl list devices)" >&2
		fail "install failed after 3 attempts — see $OUT/install.log"; }
	echo "   installed."
else
	echo "   (skipped install; app at $APP)"
fi
