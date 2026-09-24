# Headless iOS build & install — The Under-Warden

**Status:** working and verified end-to-end 2026-08-21. Debug build exported, signed, and
installed onto Rafe's iPhone SE 3 with no GUI, no Xcode window, and no manual step.

Mobile is the primary target, so a device build is the only honest check that a change works.
That only stays true if getting a build onto the phone is cheap — otherwise it quietly becomes
the step that gets skipped, and device-only regressions (see the July device-storm triage,
issues #58/#59) go unnoticed until Rafe finds them by hand.

**It is now a single command.**

```
tools/ios_build.sh
```

The pipeline, in full:

```
Godot mono --headless --export-debug  ->  xcodebuild  ->  verify the artefact  ->  xcrun devicectl install
```

No `open UnderWarden.xcodeproj`. No clicking Run. No provisioning dialog. It is scriptable,
so it can run inside an agent task or a fix-and-verify loop without a human in the loop.

---

## Quick start

```bash
tools/ios_build.sh                    # build this tree, install to Rafe's iPhone SE 3
tools/ios_build.sh --no-install       # build only (CI / smoke check)
tools/ios_build.sh --release          # export-release + Release configuration
tools/ios_build.sh --tree /tmp/copy   # build a throwaway patched copy of the tree
tools/ios_build.sh --import           # force dotnet build + reimport first (cold tree)
```

Side-by-side variants — installs **alongside** the main app instead of replacing it. This is how
you put two builds on the phone to compare them:

```bash
tools/ios_build.sh --bundle-id com.rafehatfield.underwarden.probe --name "YARL Probe"
```

Env overrides: `GODOT`, `DEVICE_ID`, `TEAM_ID`, `OUT`, `PRESET`.

Build output goes to `.ios-build/` (gitignored), including `export.log`, `xcodebuild.log`, and
`install.log` — read those, not the console summary, when something fails.

---

## Verified environment (2026-08-21)

| Component | Version / value |
|---|---|
| Godot | `4.7.stable.mono.official.5b4e0cb0f` — **the .NET build**, `/Applications/Godot_mono.app` |
| Godot export template | `4.7.stable.mono`, installed via Editor → Manage Export Templates |
| .NET SDK | `8.0.422`, with the `ios` workload installed |
| Xcode | `26.6` (build `17F113`) |
| Signing | automatic, `Apple Development` identity in keychain |
| `DEVELOPMENT_TEAM` | `S2WHBSKV97` |
| Device | Rafe's iPhone SE 3 "Jiminy Cricket", `5DB969FF-269C-5A8A-86EB-99EC9FF22397` |

Find the device id with `xcrun devicectl list devices`.

---

## The four steps, and why each is there

**1. Export.** `Godot --headless --path TREE --export-debug "iOS" OUT/UnderWarden.xcodeproj`
Godot's iOS exporter emits an Xcode *project*, not an `.ipa` — that is the whole reason this is
scriptable. The preset sets `application/export_project_only=true`, so Godot stops at the project
and leaves the build to `xcodebuild`. For a C# project this step also shells out to
`dotnet publish -r ios-arm64`, which compiles the game's C# with NativeAOT into a framework.

**2. Build.** `xcodebuild -project … -scheme … -destination 'generic/platform=iOS' -allowProvisioningUpdates`
**`-allowProvisioningUpdates` is the flag that makes it unattended.** Without it, Xcode wants to
mint the provisioning profile through the GUI and the script hangs or fails. `-derivedDataPath ./dd`
keeps build output local and predictable instead of buried in `~/Library/Developer/Xcode/DerivedData`.
The scheme name is read back out of the generated project with `find`, not hardcoded, so a rename
upstream does not silently break the script.

**3. Verify the artefact.** Read `CFBundleIdentifier` and `CFBundleDisplayName` back out of the
built `Info.plist`, and confirm the C# framework is actually inside the bundle. See the failure
modes below — this is not paranoia, it is the difference between a working build and a black screen.

**4. Install.** `xcrun devicectl device install app --device ID path/to/App.app`
`devicectl` is the current Xcode path — not `ios-deploy`, and not `xcrun simctl` (that is the
simulator, which does not exercise touch input, real screen size, or device performance).

---

## Failure modes — each of these cost real time

### The .NET publish fails silently and Godot still exits 0

**This is the big one, and it was the live roadblock when this pipeline was ported.** If Godot's
`dotnet publish` step fails, Godot logs the error, carries on packing the `.pck`, prints
"completed with warnings", and **exits 0**. `xcodebuild` then happily builds and signs a complete,
installable app — that contains no C# code whatsoever. It installs. It launches. It shows nothing.

Nothing in the console output tells you. The only signals are `ERROR: Export .NET Project` buried
in `export.log`, and the absence of `UnderWarden.Presentation.framework` from the bundle's
`Frameworks/` directory. The script now checks both and refuses to report success.

**The cause, when it happened here:** `project.godot`'s `project/assembly_name` must be the
**basename of the csproj**, because Godot resolves the C# project file as
`<project_dir>/<assembly_name>.csproj`. Commit `3994acd` set it to `UnderWarden` while the
project file is `UnderWarden.Presentation.csproj`. Godot looked for a file that does not exist,
its publish failed, and every iOS export from that commit onward produced a C#-less app. The script
preflights this before spending a build on it.

### The wrong Godot binary

Both `/Applications/Godot.app` and `/Applications/Godot_mono.app` exist on this machine. Only the
mono one can open a C# project, and the failure from the other does not name the cause. The script
runs `--version` and refuses anything without `mono` in it.

### The `ios` .NET workload is separate

`dotnet publish -r ios-arm64` needs it, and without it the failure appears inside Godot's export
log rather than on your terminal — see the silent-failure trap above. `dotnet workload install ios`.

### The export template is separate from the editor

It is not in the repo, and a C# project needs the `.mono` templates specifically. A machine with
Godot installed can still fail to export until templates are installed.

### A silently-ignored bundle override

If `PRODUCT_BUNDLE_IDENTIFIER` or `INFOPLIST_KEY_CFBundleDisplayName` fails to apply, you get N apps
sharing one bundle id and one name — each install *replacing* the previous one. On the home screen
it looks like N builds; on disk there is one, and a side-by-side comparison is quietly comparing a
build to itself. **Always verify from the built `Info.plist`, never from the command you issued.**

### `devicectl` drops the tunnel

`ERROR: The device disconnected immediately after connecting` (CoreDeviceError 4000) is a flake,
not a build problem — it hit once during verification and succeeded on an immediate retry. The
script retries three times. If all three fail, check the phone is unlocked and connected.

### Cold trees have nothing imported

A fresh worktree or an rsync'd `--tree` copy has no `.godot/`, so no assets are imported and the
export produces an app with no content. The script detects a missing `.godot/` and runs
`dotnet build` + `--headless --import` first. Force it with `--import`.

**The cold import can segfault, and a plain retry does not recover it.** On this project (~3,600
importable assets) Godot 4.7's headless `--import` intermittently **segfaults during "Preparing files
to reimport"**, before any textures are imported. The crash leaves a corrupt partial `.godot/`: a
second `--import` sees the stale metadata, re-imports only the fonts (~6 entries), and exits 0 — so
the export then ships a nearly-textureless app that *looks* built. A complete import is ~7,272
entries; `ls .godot/imported | wc -l` is the quick check.

Recovery is to import from *truly empty*, not to retry in place:

```
rm -rf .godot
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --import
ls .godot/imported | wc -l          # expect ~7272, not ~6
```

Warm checkouts skip import entirely (the script only imports when `.godot/` is absent), so this only
bites a first import or CI. Verified 2026-08-21: after the clean reimport the full
export → xcodebuild → install chain completed and installed to the device.

### Patch throwaway copies, never the working tree

When a build needs temporary source edits, `rsync` the tree to scratch, patch the copy, and build
with `--tree`. That keeps experimental edits from being committed, and makes the build immune to
another session committing on the same branch mid-run.

---

## Provenance

Ported from Gemfall's `tools/ios_build.sh` (`~/development/deathmatch`, commit `f2a15bc`), which
established the export → xcodebuild → devicectl chain and the "verify the artefact, not the
command" discipline. Gemfall is a GDScript project; every C#-specific preflight here — the mono
binary check, the `ios` workload check, the assembly-name check, and the framework-in-bundle
check — is new, and each one corresponds to a failure observed while porting.

Verified on 2026-08-21 by four runs: a default build, a build against the broken `assembly_name`
(correctly refused), a variant with overridden bundle id and display name (both confirmed from the
built plist), and a clean end-to-end build and install.
