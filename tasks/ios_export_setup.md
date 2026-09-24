# iOS Export Setup

## Rafe's Side (hardware/account setup)

- [x] **Verify Xcode version** — need Xcode 16.x (16.3 or 16.4). Avoid Xcode 26 (known Godot export failures). Check via `Xcode > About Xcode`
- [x] **Apple Developer account** — sign in with Apple ID in `Xcode > Settings > Accounts > Add Apple ID`. Free account is fine for initial device testing (3 devices, 7-day profile expiry). Paid ($99/yr) needed later for TestFlight/App Store
- [x] **Get Team ID** — NOT NEEDED upfront. Xcode auto-handles signing when you select your Personal Team in the exported project. The export preset has PLACEHOLDER which is fine — Xcode overrides it
- [x] **Connect iPhone via USB** — tap "Trust This Computer" on the phone. Xcode may download device support files (~5 min first time)
- [x] **Enable Developer Mode on iPhone** — `Settings > Privacy & Security > Developer Mode > ON`. Phone will restart
- [x] **Download Godot export templates** — in Godot editor: `Editor > Manage Export Templates > Download for Current Version` (~700MB)
- [x] **~~Share Team ID with Claude~~** — not needed, Xcode handles signing directly

## Claude's Side (project code changes) -- DONE

- [x] **NativeAOT trimming preservation** — created `src/Logic/NativeAOT.rd.xml` preserving all 20 YAML-mapped types. Referenced from Logic.csproj via `<TrimmerRootDescriptor>`. This tells the NativeAOT trimmer to keep reflection metadata for all YAML deserialization targets.
- [x] **Fix Dictionary<string, object> stubs** — replaced with `Dictionary<string, string>` in LevelOverride.cs and SpecialRoomDef.cs. `object` type is incompatible with NativeAOT (trimmer can't determine concrete types).
- [x] **Create iOS export preset** — `export_presets.cfg` with bundle ID `com.rafehatfield.underwarden`, arm64 architecture. Team ID is PLACEHOLDER — will update when Rafe provides it.
- [x] **Audit [Export] + Resource usage** — zero `[Export]` attributes in the codebase. Clean.
- [x] **Export path** — recommended path `~/Desktop/yarl-ios/` has no spaces. Project internal paths are fine.

## First Deploy Steps (after both sides done)

1. Give Claude your Team ID — I'll update the export preset
2. In Godot: `Project > Export > iOS > Export Project` — save to `~/Desktop/yarl-ios/`
3. Open the `.xcodeproj` in Xcode
4. Set signing team to your Personal Team (Signing & Capabilities tab)
5. Select your iPhone from the run destination dropdown
6. Hit Run — Xcode signs, installs, launches on device

## Known Risks

- **C# iOS export is still "experimental" in Godot 4.6** — NativeAOT trimming can cause runtime crashes from reflection usage that's invisible at compile time
- **YamlDotNet reflection may still need source generator** — the rd.xml trimming preservation is the conservative first approach. If YAML deserialization crashes on device, the next step is migrating to `StaticDeserializerBuilder` with `[YamlSerializable]` attributes (well-scoped follow-up task)
- **.NET 8 required** — .NET 9 broke iOS exports (icudt.dat not bundled). Project already targets net8.0, so we're fine
- **Xcode 26 incompatible** — tracked in godotengine/godot#111213, no fix yet
