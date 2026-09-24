using System.Collections.Generic;
using UnderWarden.Logic.ECS;
using Godot;

namespace UnderWarden.Presentation.Map;

/// <summary>
/// The Tier 0 light rig: ambient darkness plus one carried warm point light.
///
/// ART-BIBLE-v0 §6.1 rules that lighting is engine-rendered, not painted in, and §6.3 rules
/// that assets are authored to RECEIVE light rather than depict it. §6.3 also states the
/// consequence that makes this class a prerequisite rather than a nicety: a receive-light asset
/// looks flat and slightly disappointing when captured unlit, so a capture without a light rig
/// judges the candidate with the wrong instrument.
///
/// §6.2 gives the Boundary region its character — "carried fire; warm, moving, unreliable" —
/// and then says in terms: "Only the Boundary's values are derived at the pilot. The rest are
/// PLACEHOLDER." No numeric value in this file is therefore law. Every one of them is supplied
/// by the harness config or the review marker and echoed into the capture log, so that a reader
/// of any capture can see exactly which unratified values produced it. This class deliberately
/// declares no default rig: a caller must state its numbers, because a default here would
/// become a de facto derived value by the back door.
///
/// The light texture is a procedurally-generated radial falloff, not an art asset. It is a
/// lighting primitive with no palette, no register, and nothing to judge.
///
/// ---------------------------------------------------------------------------------------
/// §6.2.1 — THE TIER-ONE PRECONDITION. Why this class became live and tunable.
/// ---------------------------------------------------------------------------------------
/// RULED (Rafe, 2026-08-27, at the device gate):
///
///     The §6.2 rig values — radius, falloff, ambient — get a readability-tuning pass before
///     any asset is judged through them. The value stack must be legible at GAMEPLAY DISTANCE,
///     not at two tiles. This is a precondition, not a task: no tier-one asset round starts
///     until it is done.
///
/// A tuning pass needs the numbers to move while someone is looking at the scene, on the device,
/// so <see cref="Radius"/>, <see cref="Falloff"/> and <see cref="AmbientLevel"/> are settable at
/// run time and <see cref="ReviewRigPanel"/> puts them behind on-screen controls.
///
/// EVERY DEFAULT HERE REPRODUCES THE PREVIOUS RIG EXACTLY. Falloff 1.0 is the plain smoothstep
/// this class has always drawn; AmbientLevel 1.0 is the marker's ambient colour unscaled. The
/// session that added the knobs does not get to move them — §6.2.1 gives that pass to Rafe, and
/// a builder who nudged a number "to make it look right" would be ratifying the rig by the back
/// door and re-firing §6.2's re-derivation rule without anybody deciding to.
///
/// ---------------------------------------------------------------------------------------
/// ⚠ AND THE LAMP DID NOT FOLLOW THE PLAYER. Fixed here; recorded because it is a finding.
/// ---------------------------------------------------------------------------------------
/// <c>Attach</c> positioned the light at the player's spawn tile and the instance was then
/// dropped on the floor — no reference kept, no update anywhere. Every headless capture was
/// taken on the spawn frame, so captures were correct and nothing went red. **The device WALK
/// was not.** Walking moved the figure out of a stationary pool of light.
///
/// It matters more than a nicety because §6.5's entire derivation rests on the premise:
/// *"the player IS the lamp, and stands south of a north wall — so the face is always one tile
/// nearer the light than its own top."* A lamp anchored to the spawn tile does not deliver that
/// relationship anywhere except at spawn, and §6.2.1's pass — legibility ACROSS the lit radius,
/// at gameplay distance — cannot be run through it at all. <see cref="Follow"/> closes it.
/// </summary>
public sealed class ReviewLighting
{
    /// <summary>
    /// The rig, carried as a record so the exact values that produced a capture can be logged
    /// verbatim and reproduced. RULED for the Boundary (§6.2.1, Ruling 56, 2026-08-28); still
    /// PLACEHOLDER for every other region, which derives its own at its own gate.
    /// </summary>
    public readonly record struct Params(
        Color Ambient,        // CanvasModulate hue — the darkness the light is read against
        Color LightColor,     // carried-fire tint (§6.2 Boundary: warm)
        float Energy,         // PointLight2D energy; 0.0 is the "lighting is live" control
        float RadiusTiles,    // reach of the carried light, in TILES, not pixels — §4.3 marks
                              // tile size PLACEHOLDER and a pixel radius would hard-code one
        float Falloff,        // shape of the radial ramp. 1.00 is the plain smoothstep — the
                              // identity curve, and RULED as such (§6.2.1, Ruling 56).
        float AmbientLevel);  // scales Ambient's brightness, hue held. RULED at 0.70.

    // ⚠ NO PARAMETER HERE HAS A C# DEFAULT, and that is deliberate rather than an oversight.
    //
    // Falloff and AmbientLevel were declared with `= 1.0f` when they were introduced, so that
    // adding them broke no caller. Once Ruling 56 made them law that convenience became the
    // hazard: a caller omitting them would be silently lit by the identity while claiming the
    // ratified rig. Removing the defaults makes the compiler the enforcement — every construction
    // site must state all six values, which is the same discipline the engine already applies to
    // its command line.

    // Knob ranges. NOT art values and NOT rig values — they are the ends of the travel the
    // review panel offers, wide enough that Rafe's pass is not fenced in by a builder's guess
    // at the answer. §6.2.1 gives the pass to the human; this only decides how far the dial goes.
    public const float MinRadius = 2.0f,  MaxRadius = 14.0f, RadiusStep = 0.5f;
    // ENERGY, ADDED 2026-09-05 — and its absence blocked the walk it was needed for.
    //
    // #174 re-opened Ruling 56 because the floor's delivered value moved by 1.44x, and ENERGY is
    // the quantity that moved. The panel offered radius, falloff and ambient and not this one, so
    // a build was put on the handset for a rig-energy walk that could not reach the rig value.
    // **A walk cannot set what it cannot touch**, and the round's blind critic could not have
    // caught it: a frame critic judges the rendered frame and sees no panel wiring at all.
    //
    // Step 0.05 rather than the coarser steps above, because the interesting interval is narrow:
    // Ruling 56's 1.6 delivers a floor maximum of 247.9 and round 29's seat asked for no floor
    // pixel above ~232, which lands at 1.25 — seven steps, walkable without overshooting it.
    //
    // MIN IS 0.0 ON PURPOSE. harness_config.yaml keeps energy 0.0 as the "lighting is live"
    // positive control, and a control reachable from the panel is a control that can be taken on
    // the device rather than only in a capture.
    public const float MinEnergy = 0.0f,  MaxEnergy = 4.0f,  EnergyStep = 0.05f;
    public const float MinFalloff = 0.30f, MaxFalloff = 4.0f, FalloffStep = 0.1f;
    public const float MinAmbient = 0.0f,  MaxAmbient = 4.0f, AmbientStep = 0.1f;

    private Params _p;
    private CanvasModulate? _ambient;
    private PointLight2D? _light;
    private int _tileW = 1, _tileH = 1;
    private int _lampTileX, _lampTileY;

    public ReviewLighting(Params p) => _p = p;

    public Params Current => _p;

    /// <summary>
    /// Attach the rig under the world node. UI lives on separate CanvasLayers, each of which is
    /// its own canvas, so a CanvasModulate here darkens the dungeon and leaves the HUD alone.
    /// </summary>
    public void Attach(Node2D gameView, int tileWidth, int tileHeight, int playerTileX, int playerTileY)
    {
        _tileW = tileWidth;
        _tileH = tileHeight;

        _ambient = new CanvasModulate { Name = "ReviewAmbient", Color = ScaledAmbient() };
        gameView.AddChild(_ambient);

        _light = new PointLight2D
        {
            Name         = "ReviewCarriedLight",
            Texture      = BuildRadialFalloff(ResolveTextureSize(), _p.Falloff),
            Color        = _p.LightColor,
            Energy       = _p.Energy,
            TextureScale = 1.0f,
            BlendMode    = Light2D.BlendModeEnum.Add,
            ZIndex       = 0,
            RangeItemCullMask  = GroundLightMask | PropLightMask,
            ShadowItemCullMask = GroundLightMask,
        };
        gameView.AddChild(_light);
        _lights.Add(_light);
        Follow(playerTileX, playerTileY);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // CAST SHADOWS — the lamp meets the walls (§12.1a) and the objects (§3.2), one mechanism.
    // ═══════════════════════════════════════════════════════════════════════════════════════
    //
    // Receive-light-native: §6.3 forbids a baked drop shadow, and an engine shadow from the actual
    // lamp is that clause's whole point. The shadow moves when the player moves and lands on
    // geometry, not on the grid — which is why it does not reintroduce §12.1's ring.
    //
    // THE ONE RULE THAT FIXES BOTH FAILURES: the occluder begins BEHIND the visible reveal. The
    // §12.1a attempt (r29) covered each wall cell with a quad whose light-facing edge cast, so a
    // wall shadowed its own face (exposed wall 37.90 -> 5.51). With the light-facing edges CULLED,
    // only the far edges cast: the lamp lights the first surface it meets — face and cap — and
    // stops behind it. Which cull mode does that depends on the polygon's winding, so the mode is
    // a PARAMETER and the choice is measured, not asserted (tools/cast_shadows/).
    //
    // OBJECTS DO NOT SELF-SHADOW, BY MASK RATHER THAN BY GEOMETRY. A prop's sprite is drawn
    // NORTH of its footprint on screen — exactly where a lamp from the south throws the
    // footprint's shadow. No occluder shape avoids that in a 2D light, so props sit on a light
    // mask the lamps illuminate but never shadow (PropLightMask); the floor and walls receive
    // both. The ½-depth right side is therefore lit whatever the lamp does.

    /// <summary>Canvas light-mask bit for props: lit by every lamp, shadowed by none.</summary>
    public const int PropLightMask = 2;

    /// <summary>A box prop's PLAN DEPTH as a fraction of the cell (§3.2 footprint). One number,
    /// two consumers: the footprint occluder's base parallelogram, and the against-wall placement
    /// that puts that parallelogram's FAR edge on the reveal's foot (#212, Rafe 2026-09-13). The
    /// screen rise of the base is ½ of it (k = ½ per §3.2).</summary>
    public const float PropBaseDepth = 0.35f;
    public static float PropBaseRun(float cellH) => 0.5f * PropBaseDepth * cellH;
    private const int GroundLightMask = 1;

    private readonly List<PointLight2D> _lights = new();
    private readonly List<PointLight2D> _fireLights = new();
    private float _shadowSoftness;
    private float _shadowDarkness = 1.0f;
    private bool _shadowsEnabled = true;
    private bool _fireFlicker;
    private float _flickerT;
    private string _occluderMode = "none";

    // SOFTNESS — RAISED AT THE SHADOW WALK (Rafe, 2026-09-13): "8.0 is the knob's ceiling and
    // shadow edges are still traceable lines ... a lantern doesn't throw searchlights (§1:
    // nothing is staged)." 8 was a builder's guess at the travel; Godot's shadow_filter_smooth
    // runs to 64, PCF13 is already the widest kernel. The ceiling is the engine's, the step is
    // coarser so the ladder is walkable, and the default moves to Rafe's mark.
    public const float MinSoftness = 0f, MaxSoftness = 64f, SoftnessStep = 2f;
    public const float MinDarkness = 0f, MaxDarkness = 1f, DarknessStep = 0.1f;

    private static OccluderPolygon2D.CullModeEnum ParseCull(string mode) => mode switch
    {
        "cw"  => OccluderPolygon2D.CullModeEnum.Clockwise,
        "ccw" => OccluderPolygon2D.CullModeEnum.CounterClockwise,
        _     => OccluderPolygon2D.CullModeEnum.Disabled,
    };

    /// <summary>
    /// §12.1a — THE VOID IS DARK BY OCCLUSION, NOT BY A RING (RULED, Rafe, 2026-09-03).
    /// One LightOccluder2D per solid cell, culled so only the edges facing AWAY from a lamp
    /// cast. `mode` is "cw", "ccw" or "all" (the r29 failure, kept as the control).
    /// </summary>
    public void AddOccluders(GameMap map, Node2D gameView, string mode = "cw")
    {
        if (_light == null) return;
        _occluderMode = mode;
        foreach (var l in _lights) l.ShadowEnabled = _shadowsEnabled;
        ApplyShadowStyle();

        var poly = new OccluderPolygon2D
        {
            CullMode = ParseCull(mode),
            Polygon = new[]
            {
                new Vector2(0, 0), new Vector2(_tileW, 0),
                new Vector2(_tileW, _tileH), new Vector2(0, _tileH),
            },
        };

        var root = new Node2D { Name = "ReviewOccluders" };
        gameView.AddChild(root);
        int n = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (!map.IsWallTile(x, y)) continue;
                root.AddChild(new LightOccluder2D
                {
                    Occluder = poly,
                    Position = new Vector2(x * _tileW, y * _tileH),
                });
                n++;
            }
        }
        _occluderCount = n;
    }

    private int _occluderCount, _propOccluderCount;
    public int OccluderCount => _occluderCount;
    public int PropOccluderCount => _propOccluderCount;
    public string OccluderMode => _occluderMode;

    /// <summary>
    /// Object occluders from FOOTPRINTS (§3.2). A box prop's base is read off its own sprite —
    /// the opaque extent of the bottom rows of its bottom cell — and projected as the cabinet
    /// base parallelogram (½·depth up and right); a round prop's is the plan circle. Never the
    /// sprite: the sprite is what the shadow must not be shaped by.
    /// </summary>
    public void AddPropOccluders(IReadOnlyList<PlacedProp> props, TileLayer layer, Node2D gameView,
                                 string mode = "cw")
    {
        if (_light == null) return;
        var root = new Node2D { Name = "ReviewPropOccluders" };
        gameView.AddChild(root);
        int n = 0;
        for (int i = 0; i < props.Count; i++)
        {
            var p = props[i];
            if (p.OnWallTop) continue;
            // AN EMITTER GETS NO OCCLUDER. A light inside its own closed occluder polygon casts
            // shadow in every direction — measured: the fire's footprint occluder took the whole
            // room's floor from 62.90 to ~52. Its sprite still sits on the prop mask below.
            bool emitter = p.Light != null;
            // the sprite of the bottom-left cell carries the base
            int bottomOffset = (p.FootprintH - 1) * p.FootprintW;
            if (!layer.PropSprites.TryGetValue((i, bottomOffset), out var cell)
                && !layer.PropSprites.TryGetValue((i, 0), out cell)) continue;
            if (cell.Sprite is not Sprite2D sp || sp.Texture == null) continue;

            // Every prop sprite: lit by the lamps, never shadowed (see the mask note above).
            for (int c = -1; c < p.FootprintW * p.FootprintH; c++)
                if (layer.PropSprites.TryGetValue((i, c), out var cs) && cs.Sprite is CanvasItem ci)
                    ci.LightMask = PropLightMask;
            if (emitter) continue;

            float cellW = _tileW, cellH = _tileH;
            float scaleX = cellW / sp.Texture.GetWidth();     // sprite pixels -> screen pixels
            float scaleY = cellH / sp.Texture.GetHeight();
            Vector2[] pts;
            if (p.Footprint == "round")
            {
                // the plan circle: centred on the cell, radius 0.42 of the cell — the fire ring's
                // r_out (26 of 64) at §12.2 scale
                float r = 0.42f * Mathf.Min(cellW, cellH) * p.FootprintW;
                float cx = p.FootprintW * cellW / 2f, cy = p.FootprintH * cellH / 2f + 0.06f * cellH;
                const int N = 16;
                pts = new Vector2[N];
                for (int k = 0; k < N; k++)
                {
                    float ang = Mathf.Tau * k / N;
                    pts[k] = new Vector2(cx + r * Mathf.Cos(ang), cy + r * Mathf.Sin(ang));
                }
            }
            else
            {
                // the base extent, from the sprite's own bottom rows (alpha), full footprint width
                var img = sp.Texture.GetImage();
                int th = img.GetHeight(), tw = img.GetWidth();
                int x0 = tw, x1 = -1, yb = -1;
                for (int y = th - 1; y >= th - Mathf.Max(2, th / 5); y--)
                {
                    for (int x = 0; x < tw; x++)
                    {
                        if (img.GetPixel(x, y).A <= 0.01f) continue;
                        if (yb < 0) yb = y;
                        x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
                    }
                }
                if (x1 < 0) { x0 = 0; x1 = tw - 1; yb = th - 1; }
                // the right-hand cells of a wide footprint extend the base
                float left = x0 * scaleX;
                float right = (p.FootprintW - 1) * cellW + (x1 + 1) * scaleX;
                float bottom = (p.FootprintH - 1) * cellH + (yb + 1) * scaleY;
                float run = PropBaseRun(cellH);               // plan depth 0.35 cell; k = ½ per §3.2
                pts = new[]
                {
                    new Vector2(left, bottom), new Vector2(right, bottom),
                    new Vector2(right + run, bottom - run), new Vector2(left + run, bottom - run),
                };
            }
            // #212: an against-wall prop's sprites were shifted north to the wall's foot; its
            // footprint — and so its shadow — goes with it.
            float shiftY = layer.PropShift.TryGetValue(i, out var sh) ? sh : 0f;
            root.AddChild(new LightOccluder2D
            {
                Occluder = new OccluderPolygon2D { CullMode = ParseCull(mode), Polygon = pts },
                Position = new Vector2(p.X * _tileW, p.Y * _tileH - shiftY),
            });
            n++;
        }
        _propOccluderCount = n;
    }

    /// <summary>
    /// A prop that EMITS — the orc fire, the first second light source (B-PROP-003, #205).
    /// Static, warm, its own shadows; the flicker flag is present and OFF (§9.2 — Rafe rules at
    /// the gate, both states demonstrated).
    /// </summary>
    public int AddPropLights(IReadOnlyList<PlacedProp> props, Node2D gameView)
    {
        int n = 0;
        foreach (var p in props)
        {
            if (p.Light == null) continue;
            int size = Mathf.Max(Mathf.RoundToInt(p.Light.RadiusTiles * Mathf.Max(_tileW, _tileH) * 2f), 2);
            var l = new PointLight2D
            {
                Name         = $"PropLight_{p.TileId}",
                Texture      = BuildRadialFalloff(size, 1.0f),
                Color        = new Color(p.Light.Color),
                Energy       = p.Light.Energy,
                BlendMode    = Light2D.BlendModeEnum.Add,
                Position     = new Vector2(p.X * _tileW + p.FootprintW * _tileW / 2f,
                                           p.Y * _tileH + p.FootprintH * _tileH / 2f),
                ShadowEnabled = _shadowsEnabled,
                RangeItemCullMask  = GroundLightMask | PropLightMask,
                ShadowItemCullMask = GroundLightMask,
            };
            l.SetMeta("base_energy", p.Light.Energy);
            l.SetMeta("radius_tiles", p.Light.RadiusTiles);
            int ti = System.Array.IndexOf(FireTints, p.Light.Color.ToLowerInvariant());
            l.SetMeta("tint_index", ti < 0 ? 0 : ti);
            gameView.AddChild(l);
            _lights.Add(l);
            _fireLights.Add(l);
            n++;
        }
        ApplyShadowStyle();
        return n;
    }

    /// <summary>
    /// THE SHADOW IS THE AMBIENT, AND IN GODOT'S TERMS THAT IS A BLACK SHADOW COLOUR.
    ///
    /// `Light2D.ShadowColor` is not what remains in shadow — it is what THIS LAMP CONTRIBUTES
    /// there. Set to the ambient hue it ADDS ambient on top of the CanvasModulate ambient, and
    /// the first capture came back with blue-grey wedges brighter than the unlit floor beyond
    /// the lamp's reach (measured before this note was written). The ruled look — §12.1a, "the
    /// same found rock, unlit" — is the lamp contributing NOTHING in shadow, so what remains is
    /// the ambient-hued rock the CanvasModulate already delivers. RGB 0 here; the hue of the
    /// shadow is §6.2's ambient because the ambient is all that is left, and the instrument
    /// proves it: shadowed floor == floor outside the radius, never 0.
    ///
    /// ALPHA is the darkness knob — how much of the lamp leaks into shadow. 1.0 is full
    /// occlusion (physically right); the walk may want a fill. Softness = PCF width.
    /// </summary>
    private void ApplyShadowStyle()
    {
        foreach (var l in _lights)
        {
            // THE LEAK IS THE AMBIENT'S HUE, NOT NEUTRAL GREY — flip 2 of the shadow walk (Rafe,
            // 2026-09-13): "dropping darkness below 0.8 fills shadows with grey lamp-leak, not
            // dark. Tint the leak toward the ruled ambient hue (§6.2), so lowering darkness lets
            // the ambient dark through; shadow = ambient." rgb is what leaks (measured), so the
            // leak carries the ambient's chroma at the ambient's own channel ratios, scaled to
            // the leak amount: a shadow that fills in fills in with the room's dark.
            float leak = 1f - _shadowDarkness;
            var amb = _p.Ambient;
            float peak = Mathf.Max(amb.R, Mathf.Max(amb.G, amb.B));
            var hue = peak > 0f ? new Color(amb.R / peak, amb.G / peak, amb.B / peak) : new Color(1, 1, 1);
            l.ShadowColor = new Color(leak * hue.R, leak * hue.G, leak * hue.B, 1f);   // alpha inert
            if (_shadowSoftness <= 0f)
            {
                l.ShadowFilter = Light2D.ShadowFilterEnum.None;
            }
            else
            {
                l.ShadowFilter = Light2D.ShadowFilterEnum.Pcf13;
                l.ShadowFilterSmooth = _shadowSoftness;
            }
        }
    }

    /// <summary>The rig-panel knob for Rafe's walk: 0 is a hard edge; the ladder climbs to soft.</summary>
    public float ShadowSoftness
    {
        get => _shadowSoftness;
        set { _shadowSoftness = Mathf.Clamp(value, MinSoftness, MaxSoftness); ApplyShadowStyle(); }
    }

    /// <summary>
    /// How much of the lamp leaks into its shadows: 1.0 none (full occlusion), 0 all of it.
    ///
    /// MEASURED SEMANTICS OF `Light2D.ShadowColor` ON THIS ENGINE (Godot 4.7 mono), because two
    /// guesses were wrong first. RGB is the FRACTION OF THE LAMP that still reaches a shadowed
    /// pixel — the ambient hue (≈0.15 grey) leaked 15% and read as a wash; black leaks none.
    /// ALPHA IS INERT: 0.0, 0.5 and 1.0 delivered the identical shadowed value (probe (6,15) at
    /// 0.0686 all three times). So darkness d is `ShadowColor = (1−d, 1−d, 1−d)`, and the hue of
    /// a shadow is §6.2's ambient because at d = 1 the ambient is all that is left — §12.1a's
    /// "the same found rock, unlit". A fill light was tried and refused: additive, it lifted the
    /// LIT floor too (62.9 → 69.7).
    /// </summary>
    public float ShadowDarkness
    {
        get => _shadowDarkness;
        set { _shadowDarkness = Mathf.Clamp(value, MinDarkness, MaxDarkness); ApplyShadowStyle(); }
    }

    /// <summary>Shadows on/off, live — the perf A/B on one handset build, and a walk control.</summary>
    public bool ShadowsEnabled
    {
        get => _shadowsEnabled;
        set { _shadowsEnabled = value; foreach (var l in _lights) l.ShadowEnabled = value; }
    }

    /// <summary>§9.2 vs the tended exception — RULED ON (Rafe, shadow walk, 2026-09-13).</summary>
    public bool FireFlicker
    {
        get => _fireFlicker;
        set
        {
            _fireFlicker = value;
            if (!value)
                foreach (var l in _fireLights) l.Energy = (float)l.GetMeta("base_energy");
        }
    }

    public int FireLightCount => _fireLights.Count;

    /// <summary>The fire's energy, live — flip 3 of the shadow walk (#205): "it needs real radius
    /// and energy so the barricade beside it throws a shadow away from it." Scales every fire
    /// light's base; the flicker rides on top. RULED 1.6 (Rafe, props walk 2026-09-13: "the fire is
    /// good"); the row stays so a walk can still compare, and a changed value is a re-ruling.</summary>
    public const float MinFire = 0f, MaxFire = 4f, FireStep = 0.1f;

    // REACH AND TINT — RULED (Rafe, props walk on the handset, 2026-09-13): "the fire is good."
    // Exposed as knobs for that walk and ratified where they stood: reach 4.0 tiles, tint ff8a3c
    // (§6.2's live table, required by the engine — the scene's `light` block states all three).
    // The knobs stay so a walk can still compare; the tint is a LADDER of warm hues rather than
    // three channel sliders — a walk sets a colour by choosing, not by mixing.
    public const float MinFireRadius = 1.0f, MaxFireRadius = 8.0f, FireRadiusStep = 0.5f;
    public static readonly string[] FireTints =
        { "ff8a3c", "ff7a28", "ff6a1e", "ff9a4c", "ffb066", "ffc890", "ffd4a0" };

    public float FireRadiusTiles
    {
        get => _fireLights.Count > 0 ? (float)_fireLights[0].GetMeta("radius_tiles") : 0f;
        set
        {
            float v = Mathf.Clamp(value, MinFireRadius, MaxFireRadius);
            foreach (var l in _fireLights)
            {
                l.SetMeta("radius_tiles", v);
                int size = Mathf.Max(Mathf.RoundToInt(v * Mathf.Max(_tileW, _tileH) * 2f), 2);
                l.Texture = BuildRadialFalloff(size, 1.0f);
            }
        }
    }

    public int FireTintIndex
    {
        get => _fireLights.Count > 0 ? (int)_fireLights[0].GetMeta("tint_index") : 0;
        set
        {
            int i = ((value % FireTints.Length) + FireTints.Length) % FireTints.Length;
            foreach (var l in _fireLights) { l.SetMeta("tint_index", i); l.Color = new Color(FireTints[i]); }
        }
    }
    public string FireTint => _fireLights.Count > 0 ? FireTints[FireTintIndex] : "-";
    public float FireEnergy
    {
        get => _fireLights.Count > 0 ? (float)_fireLights[0].GetMeta("base_energy") : 0f;
        set
        {
            float v = Mathf.Clamp(value, MinFire, MaxFire);
            foreach (var l in _fireLights) { l.SetMeta("base_energy", v); if (!_fireFlicker) l.Energy = v; }
        }
    }

    /// <summary>Per frame. A minimal, low-frequency intensity variance — two slow sines, ±8%.</summary>
    public void Tick(double delta)
    {
        if (!_fireFlicker || _fireLights.Count == 0) return;
        _flickerT += (float)delta;
        float k = 1f + 0.05f * Mathf.Sin(_flickerT * 2f * Mathf.Pi * 1.3f)
                     + 0.03f * Mathf.Sin(_flickerT * 2f * Mathf.Pi * 2.1f + 1.0f);
        foreach (var l in _fireLights) l.Energy = (float)l.GetMeta("base_energy") * k;
    }

    /// <summary>
    /// Move the carried light onto a tile. The player IS the lamp (§6.2, §6.5), so this is
    /// called every frame from the tile the controlled figure is standing on rather than once
    /// at spawn. Centred on the tile, not its corner: a half-tile offset is visible at true
    /// display size.
    /// </summary>
    public void Follow(int tileX, int tileY)
    {
        _lampTileX = tileX;
        _lampTileY = tileY;
        if (_light == null) return;
        _light.Position = new Vector2(tileX * _tileW + _tileW / 2f,
                                      tileY * _tileH + _tileH / 2f);
    }

    // --- the §6.2.1 knobs ---------------------------------------------------------------

    public float Radius
    {
        get => _p.RadiusTiles;
        set
        {
            _p = _p with { RadiusTiles = Mathf.Clamp(value, MinRadius, MaxRadius) };
            RebuildTexture();
        }
    }

    public float Falloff
    {
        get => _p.Falloff;
        set
        {
            _p = _p with { Falloff = Mathf.Clamp(value, MinFalloff, MaxFalloff) };
            RebuildTexture();
        }
    }

    /// <summary>
    /// The lamp's energy — the §6.2 rig value #174 moved and Ruling 56 re-opened.
    ///
    /// Unlike the three below it this changes no texture: PointLight2D applies energy per frame,
    /// and the floor's ShaderMaterial now reads LIGHT_ENERGY (#174) so both planes answer it by
    /// the same arithmetic. Before that fix this setter would have moved the walls and left the
    /// floor where it was, which is exactly the defect and exactly why the knob is worth having.
    /// </summary>
    public float Energy
    {
        get => _p.Energy;
        set
        {
            _p = _p with { Energy = Mathf.Clamp(value, MinEnergy, MaxEnergy) };
            if (_light != null) _light.Energy = _p.Energy;
        }
    }

    public float AmbientLevel
    {
        get => _p.AmbientLevel;
        set
        {
            _p = _p with { AmbientLevel = Mathf.Clamp(value, MinAmbient, MaxAmbient) };
            if (_ambient != null) _ambient.Color = ScaledAmbient();
            ApplyShadowStyle();     // the shadow tint IS the ambient hue (§6.2)
        }
    }

    /// <summary>
    /// The ambient the scene is actually darkened by: §6.2's hue at the tuned level.
    ///
    /// The knob scales BRIGHTNESS and holds HUE, deliberately. §6.2 gives the Boundary a
    /// character — warm, carried fire — and a knob that walked the colour as well as the level
    /// would let a readability pass quietly restyle the region. This is a readability tuning,
    /// not a licence to relight the Boundary (§6.2.1's third bullet).
    /// </summary>
    private Color ScaledAmbient()
    {
        float k = _p.AmbientLevel;
        return new Color(Mathf.Clamp(_p.Ambient.R * k, 0f, 1f),
                         Mathf.Clamp(_p.Ambient.G * k, 0f, 1f),
                         Mathf.Clamp(_p.Ambient.B * k, 0f, 1f),
                         _p.Ambient.A);
    }

    private void RebuildTexture()
    {
        if (_light == null) return;
        _light.Texture = BuildRadialFalloff(ResolveTextureSize(), _p.Falloff);
    }

    /// <summary>
    /// Texture diameter in pixels for the configured radius. Derived from the tile size the
    /// renderer is actually using rather than from any constant in this file — tile size is a
    /// parameter here, per §4.3.
    /// </summary>
    private int ResolveTextureSize()
    {
        float tile = Mathf.Max(_tileW, _tileH);
        return Mathf.Max(Mathf.RoundToInt(_p.RadiusTiles * tile * 2f), 2);
    }

    /// <summary>
    /// Radial falloff, generated per-pixel. Smoothstep rather than linear so the edge of the
    /// carried light does not read as a hard disc at true display size.
    ///
    /// `falloff` shapes the ramp without moving its reach: it is an exponent on the smoothstep,
    /// so 1.0 is exactly the curve this class drew before the knob existed, above 1.0 pulls the
    /// pool in tight around the lamp, and below 1.0 carries more light out to the radius. It is
    /// the knob §6.2.1 names second, and the gate's own diagnosis of what is wrong — "the pool
    /// is narrow, the falloff is steep, and §6.5's stack is legible in a band around the player
    /// and gone outside it" — is a statement about this curve rather than about the radius.
    /// </summary>
    private static Texture2D BuildRadialFalloff(int size, float falloff)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float r = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) - r;
                float dy = (y + 0.5f) - r;
                float d  = Mathf.Sqrt(dx * dx + dy * dy) / r;   // 0 at centre, 1 at edge
                float a  = d >= 1f ? 0f : 1f - Mathf.SmoothStep(0f, 1f, d);
                if (!Mathf.IsEqualApprox(falloff, 1.0f))
                    a = Mathf.Pow(a, falloff);
                img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// The lamp's RELATIVE illumination at a world point: ambient, plus the carried light's own
    /// radial ramp. 1.0 is ambient alone; the lit ground beside the figure is several times that.
    ///
    /// It exists so the §12.1 contact occlusion can be anchored to AMBIENT rather than scaled by
    /// the lamp — *a boundary scaled by an absent lamp is a boundary that isn't drawn* (RULED,
    /// 2026-09-02). The occlusion is FORM, and form does not fade because nothing is shining on
    /// it. This is the rig's own curve, read from the rig, and NOT a second copy of it: the same
    /// `1 - smoothstep(d)` raised to the same falloff that `BuildRadialFalloff` bakes into the
    /// texture, so the two cannot drift apart.
    /// </summary>
    /// ⚠ IN TILES, NOT IN PIXELS, AND THE FIRST VERSION WAS IN PIXELS. It measured
    /// `host.GlobalPosition` against `_light.GlobalPosition`, and at the moment the floor overlays
    /// attach the lamp's global transform is not yet resolved — so every cell came back at
    /// ambient and the occlusion stacked EVERYWHERE. The capture said so plainly: all three range
    /// bands rose by about a third (47.80 -> 63.54, 15.64 -> 21.93, 3.85 -> 5.23) where only the
    /// third was meant to move at all. Tile indices need no transform to be right.
    public float RelativeIlluminationAtTile(int tileX, int tileY)
    {
        float amb = Mathf.Max(_p.AmbientLevel, 0.0001f);
        if (_p.RadiusTiles <= 0f) return 1.0f;
        float dx = tileX - _lampTileX, dy = tileY - _lampTileY;
        float d = Mathf.Sqrt(dx * dx + dy * dy) / _p.RadiusTiles;
        float a = d >= 1f ? 0f : 1f - Mathf.SmoothStep(0f, 1f, d);
        if (!Mathf.IsEqualApprox(_p.Falloff, 1.0f)) a = Mathf.Pow(a, _p.Falloff);
        return 1.0f + (_p.Energy * a) / amb;
    }

    /// <summary>One line, written into the capture log so a capture carries its own rig.</summary>
    public string Describe(int tileWidth, int tileHeight)
        => $"ambient={_p.Ambient.ToHtml(false)}@{_p.AmbientLevel:0.##} " +
           $"(effective {ScaledAmbient().ToHtml(false)}) " +
           $"light={_p.LightColor.ToHtml(false)} energy={_p.Energy:0.###} " +
           $"radius_tiles={_p.RadiusTiles:0.###} falloff={_p.Falloff:0.##} " +
           $"tile={tileWidth}x{tileHeight} tex={ResolveTextureSize()}px " +
           // The light values are RULED FOR THE BOUNDARY (§6.2.1, Ruling 56, 2026-08-28) and this
           // string used to stamp every capture "ALL VALUES UNDERIVED". Left alone it would
           // mislabel the evidence in the opposite direction from before — a capture claiming its
           // rig was a guess when the rig is law. Tile size is separate and unchanged: RULED as to
           // value, PLACEHOLDER as to derivation (§4.3), which is not the same status and is not
           // collapsed into one phrase.
           "(light: RULED for the Boundary — §6.2.1 Ruling 56; other regions PLACEHOLDER. " +
           "tile: RULED value, §4.3 derivation outstanding)";

    /// <summary>
    /// The three §6.2.1 knobs alone, in the form a settings log wants: short, greppable, and
    /// complete enough that a walk can be reproduced from one line.
    /// </summary>
    public string Settings()
        => $"radius={_p.RadiusTiles:0.##} falloff={_p.Falloff:0.##} " +
           $"ambient={_p.AmbientLevel:0.##} ({ScaledAmbient().ToHtml(false)}) " +
           $"energy={_p.Energy:0.###} " +
           $"shadows={(_shadowsEnabled ? "on" : "off")}({_occluderMode}) softness={_shadowSoftness:0.#} " +
           $"darkness={_shadowDarkness:0.#} " +
           $"fire_lights={_fireLights.Count} fire_energy={FireEnergy:0.##} fire_radius={FireRadiusTiles:0.#} " +
           $"fire_tint={FireTint} flicker={(_fireFlicker ? "on" : "off")}";
}
