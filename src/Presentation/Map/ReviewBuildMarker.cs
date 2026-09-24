using Godot;
using System.Text.Json;

namespace UnderWarden.Presentation.Map;

/// <summary>
/// Tier 0 review-build marker (ART-BIBLE-v0 §13.1).
///
/// §13.1 requires the verdict to come from the production renderer, in the lit scene, at true
/// display size, ON DEVICE. That last word is the problem this class solves: the review corridor
/// is otherwise reachable only via --corridor-scene, and an iOS app is launched by the OS with
/// no command line at all. Without this, the app could be verified installed on the reference
/// device and still have no way to display the corridor — the device leg of the harness would be
/// an install check wearing a review check's clothes.
///
/// A REVIEW BUILD is therefore identified by a data file baked into the export. If
/// res://src/Presentation/assets/tier0_harness/REVIEW_BUILD.json exists at boot, the app boots
/// straight into the review corridor with the rig that file names. If it does not exist — which
/// is every normal build, because only the harness writes it — nothing here runs and boot is
/// bit-for-bit unchanged. The file is deliberately NOT committed; only REVIEW_BUILD.json.template
/// is, so a review build cannot be created by accident or leak into a player build.
///
/// The rig is carried in the file rather than defaulted in code for the same reason
/// <see cref="ReviewLighting"/> refuses defaults: §6.2 marks the light values PLACEHOLDER, and a
/// default compiled into the engine would quietly become the derived value.
/// </summary>
public sealed class ReviewBuildMarker
{
    public const string Path = "res://src/Presentation/assets/tier0_harness/REVIEW_BUILD.json";

    public string ScenePath { get; private init; } = "";
    public string ThemeConfigPath { get; private init; } = "";
    public ReviewLighting.Params Light { get; private init; }

    /// <summary>
    /// res:// path to a floor family's MANIFEST.json, or null. Present because ART-BIBLE-v0
    /// §8.3's overlays are NOT a tile role — a cell may carry none, one or two of them, chosen
    /// per instance — so they cannot be selected the way `themeConfig` selects tiles, and a
    /// review build has no command line to pass them on. Absent, the scene draws base tiles
    /// only, which is exactly what every review build before tier one did.
    /// </summary>
    public string? FloorOverlays { get; private init; }

    /// <summary>
    /// res:// path to the EDGE-MATCHED floor family's MANIFEST.json, or null. Separate from
    /// <see cref="FloorOverlays"/> because they are different objects: the overlays are the
    /// incident placed per instance, this is the BASE tile set whose edges must agree with the
    /// edges of their neighbours.
    /// </summary>
    public string? WangFloor { get; private init; }
    public string? AshlarFloor { get; private init; }

    /// <summary>
    /// res:// path to the tier-one WALL family's MANIFEST.json, or null. Absent, the scene keeps
    /// whatever the theme's mask table chose — which for every build before this one was the
    /// tier-0 magenta programmer-art mock, and deliberately so.
    /// </summary>
    public string? BoundaryWall { get; private init; }

    /// <summary>
    /// Which of the wall family's void candidates the build starts on. NOT A RULED VALUE: the
    /// void is Rafe's to choose at the gate (§13.1), and the rig panel switches it live so the
    /// choice is made by walking rather than by rebuilding.
    /// </summary>
    public int? VoidChoice { get; private init; }

    /// <summary>
    /// CAPTURE-TIME override of the wall manifest's ruled <c>void_ring</c>, mirroring
    /// <c>--void-ring</c>. Added 2026-09-05, and its absence made a device walk incomparable
    /// with the ladder it was meant to be walked against.
    ///
    /// Every desktop capture of the combined room is taken at <c>--void-ring 1</c>, the
    /// flat-dark fallback, because §12.1a's occluder is still outstanding and at the ruled 0 the
    /// lamp lights 192 cells of solid rock and the room has no outside. An iOS app receives no
    /// command line, so without this key the handset showed a DIFFERENT ROOM from every frame
    /// the walk compares it to — not magenta, not obviously wrong, and therefore worse.
    ///
    /// Null leaves the manifest's ruled value alone, so an older marker is unaffected.
    /// </summary>
    public int? VoidRing { get; private init; }

    /// <summary>
    /// res:// path to the ORC BINDING family's MANIFEST.json, or null. Separate from
    /// <see cref="BoundaryWall"/> because they are different objects and §8.3.1 requires them to
    /// stay that way: the wall is the material, the bindings are the incident, and a binding that
    /// ever gets baked into a wall segment is a repair repeated on every cell that segment lands
    /// on.
    /// </summary>
    public string? WallBindings { get; private init; }

    /// <summary>
    /// res:// path to the CAP field's MANIFEST.json, or null. Separate again, and for the same
    /// reason: the cap is not a tile set keyed by anything the wall family knows. It is one
    /// continuous field cut into windows chosen by WORLD POSITION, so it has no masks, no edge
    /// families and no ages — and a wall build without it falls back to the block cap the
    /// 2026-08-30 gate culled for its tile-frequency seams.
    /// </summary>
    public string? WallCap { get; private init; }

    /// <summary>
    /// The commit the build was made from, and when — stamped into the marker by
    /// build_review_app.sh. LOOP-PROCESS §2.3: evidence carries its producer's hash, and a
    /// hash mismatch at a ruling invalidates the evidence. Headless captures have always
    /// stamped their commit; the DEVICE BUILD did not, so the one artefact that decides
    /// anything (§13.1) was the one that could not say what it was made from.
    /// </summary>
    public string? Commit { get; private init; }
    public string? BuiltAt { get; private init; }

    /// <summary>
    /// What this build was allowed past, or null when it was gated normally. Currently one
    /// value: <c>SKIPPED-REVIEW</c>, stamped by build_review_app.sh when YARL_SKIP_CRITIC=1
    /// waved the build past the frame-critic gate.
    ///
    /// It is drawn ON SCREEN, in the corner, for as long as the build is on the handset. An
    /// override nobody can see from the phone is indistinguishable from a gate that does not
    /// work, and this project's ledger is a list of rules that depended on being remembered and
    /// eventually were not. Whoever is walking the build has to be able to tell that nothing
    /// looked at it — otherwise a measurement build gets walked as a gate build, and the verdict
    /// is taken on a frame no critic ever saw.
    /// </summary>
    public string? ReviewStatus { get; private init; }

    /// <summary>
    /// Tile size and integer scale for the review build, mirroring --tile-size / --tile-scale.
    ///
    /// These exist for the same reason the light values do, and the omission would have been
    /// the same bug: an iOS app receives no command line, so a device build had no way to be
    /// told the grid. It would have rendered at the renderer's default 24 while the desktop
    /// captures it is meant to be compared against were taken at 32 — and nothing would have
    /// said so. The device and the capture must be lit by the same rig AND drawn on the same
    /// grid, or they are not comparable and the §13.1 verdict is taken on the wrong picture.
    ///
    /// Null means "not stated in the marker", and the renderer's own defaults apply.
    /// </summary>
    /// <summary>Cast shadows: occluder cull mode ("none" | "cw" | "ccw" | "all"), softness, flicker.
    /// Mirrors --occluders / --shadow-softness / --fire-flicker for the handset.</summary>
    public string? Occluders { get; private init; }
    public float? ShadowSoftness { get; private init; }
    public float? ShadowDarkness { get; private init; }
    /// <summary>GPU HEADROOM MEASUREMENT (overnight queue, 2026-09-13): `vsync: false` uncaps the
    /// frame so [Perf] reports real render cost rather than the 16.67 ms the display hands back.
    /// A measurement build's flag; never set on a gate build.</summary>
    public bool? Vsync { get; private init; }
    public bool? FireFlicker { get; private init; }
    public int? TileSize { get; private init; }
    public float? TileScale { get; private init; }

    private static ReviewBuildMarker? _cached;
    private static bool _looked;

    /// <summary>
    /// True when this is a review build. Result is cached: boot asks twice (once for the theme
    /// override, once for the scene) and the file must not be read differently between them.
    /// </summary>
    public static bool TryLoad(out ReviewBuildMarker? marker)
    {
        if (_looked)
        {
            marker = _cached;
            return _cached != null;
        }
        _looked = true;
        marker = null;

        if (!Godot.FileAccess.FileExists(Path))
            return false;

        try
        {
            using var f = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Read);
            if (f == null) return false;

            using var doc = JsonDocument.Parse(f.GetAsText());
            var root = doc.RootElement;
            var light = root.GetProperty("light");

            _cached = new ReviewBuildMarker
            {
                ScenePath       = root.GetProperty("scene").GetString() ?? "",
                ThemeConfigPath = root.GetProperty("themeConfig").GetString() ?? "",
                // EVERY RIG VALUE IS REQUIRED. No key here has a fallback.
                //
                // The first version of this defaulted falloff and ambientLevel to the identity
                // "so a marker written before Ruling 56 still boots to the rig it was built
                // with". That reasoning is wrong and it is the exact failure the same session
                // spent a commit message warning about: a ratified value that can be silently
                // defaulted is a ratified value that can silently drift. A pre-ruling marker
                // SHOULD fail here — it describes a rig that is no longer law, and booting it
                // quietly under the ratified rig's name is how a walk gets taken through numbers
                // nobody decided.
                //
                // GetProperty throws when the key is absent; TryLoad's catch reports it and the
                // app boots the menu instead of the corridor, which is a loud, visible failure
                // rather than a silent substitution.
                Light = new ReviewLighting.Params(
                    Ambient:      new Color(light.GetProperty("ambient").GetString()),
                    LightColor:   new Color(light.GetProperty("color").GetString()),
                    Energy:       (float)light.GetProperty("energy").GetDouble(),
                    RadiusTiles:  (float)light.GetProperty("radiusTiles").GetDouble(),
                    Falloff:      (float)light.GetProperty("falloff").GetDouble(),
                    AmbientLevel: (float)light.GetProperty("ambientLevel").GetDouble()),
                FloorOverlays = root.TryGetProperty("floorOverlays", out var fo)
                            ? fo.GetString() : null,
                WangFloor = root.TryGetProperty("wangFloor", out var wf) ? wf.GetString() : null,
                AshlarFloor = root.TryGetProperty("ashlarFloor", out var af) ? af.GetString() : null,
                BoundaryWall = root.TryGetProperty("boundaryWall", out var bw)
                            ? bw.GetString() : null,
                VoidChoice = root.TryGetProperty("voidChoice", out var vch)
                            ? vch.GetInt32() : (int?)null,
                VoidRing = root.TryGetProperty("voidRing", out var vrg)
                             && vrg.ValueKind == System.Text.Json.JsonValueKind.Number
                           ? vrg.GetInt32() : (int?)null,
                WallBindings = root.TryGetProperty("wallBindings", out var wb)
                            ? wb.GetString() : null,
                WallCap = root.TryGetProperty("wallCap", out var wc) ? wc.GetString() : null,
                Commit  = root.TryGetProperty("commit",  out var cm) ? cm.GetString() : null,
                BuiltAt = root.TryGetProperty("builtAt", out var ba) ? ba.GetString() : null,
                ReviewStatus = root.TryGetProperty("reviewStatus", out var rs)
                            ? rs.GetString() : null,
                Occluders = root.TryGetProperty("occluders", out var oc) ? oc.GetString() : null,
                ShadowSoftness = root.TryGetProperty("shadowSoftness", out var ss)
                                 && ss.ValueKind == System.Text.Json.JsonValueKind.Number
                               ? (float)ss.GetDouble() : (float?)null,
                ShadowDarkness = root.TryGetProperty("shadowDarkness", out var sd)
                                 && sd.ValueKind == System.Text.Json.JsonValueKind.Number
                               ? (float)sd.GetDouble() : (float?)null,
                Vsync = root.TryGetProperty("vsync", out var vs)
                        && (vs.ValueKind == System.Text.Json.JsonValueKind.True
                            || vs.ValueKind == System.Text.Json.JsonValueKind.False)
                      ? vs.GetBoolean() : (bool?)null,
                FireFlicker = root.TryGetProperty("fireFlicker", out var ff)
                              && (ff.ValueKind == System.Text.Json.JsonValueKind.True
                                  || ff.ValueKind == System.Text.Json.JsonValueKind.False)
                            ? ff.GetBoolean() : (bool?)null,
                TileSize  = root.TryGetProperty("tileSize", out var ts)
                            ? ts.GetInt32() : (int?)null,
                TileScale = root.TryGetProperty("tileScale", out var sc)
                            ? (float)sc.GetDouble() : (float?)null,
            };
        }
        catch (System.Exception ex)
        {
            // A malformed marker must not silently boot the normal game: a reviewer would then be
            // looking at the menu and wondering where the corridor went.
            GD.PrintErr($"[Tier0] REVIEW_BUILD.json present but unreadable — {ex.Message}");
            return false;
        }

        marker = _cached;
        return marker != null;
    }
}
