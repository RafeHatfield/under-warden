using Godot;

namespace UnderWarden.Presentation.Map;

/// <summary>
/// THE RIG LADDER — the §6.2.1 readability-tuning pass, on the device, in Rafe's hands.
///
/// ART-BIBLE-v0 §6.2.1, RULED (Rafe, 2026-08-27, at the device gate):
///
///     The §6.2 rig values — radius, falloff, ambient — get a readability-tuning pass before
///     any asset is judged through them. The value stack must be legible at GAMEPLAY DISTANCE,
///     not at two tiles. This is a precondition, not a task: no tier-one asset round starts
///     until it is done.
///
/// The clause says what the pass owes: legibility stated as a distance and measured there;
/// §6.5's stack surviving the falloff ACROSS the lit radius rather than only at its middle;
/// §6.2's arc preserved; and the ratified values written back into the bible. This panel exists
/// so the first three can be answered by walking rather than by argument, and so the fourth has
/// something to write back — every setting a walk was taken at is logged, verbatim, into the
/// device diagnostic that can be pulled off the phone.
///
/// WHY THE KNOBS ARE HERE AND NOT IN A CONFIG FILE. §6.2.1's reasoning is about ORDER:
/// *"every wall round so far has judged art through them anyway, and the coupling flag shows the
/// art bending itself to fit an undecided rig. That is backwards."* A number edited in a config,
/// rebuilt, redeployed and walked ten minutes later is not a tuning pass, it is ten separate
/// judgements of ten different scenes. The pass needs the value to move while the eye is on the
/// floor.
///
/// WHAT THIS PANEL REFUSES TO DO. It does not pick a value, propose one, recommend one, or start
/// anywhere other than exactly where the marker's rig already was. §6.2.1 gives the pass to the
/// human gate, and a builder who shipped a "better" starting point would have ratified the rig
/// without anybody deciding to — and, per §6.2's re-derivation rule, silently invalidated every
/// authored ratio derived against the old numbers. The panel moves numbers. It has no opinion.
///
/// It is built ONLY in a review build (<see cref="ReviewBuildMarker"/>). Nothing here can reach
/// a player build.
/// </summary>
public sealed partial class ReviewRigPanel : VBoxContainer
{
    private readonly ReviewLighting _rig;
    private Label? _energy, _radius, _falloff, _ambient, _walk;
    private VBoxContainer? _body;
    private int _walkNumber;

    /// <summary>Buttons are sized for a thumb on the reference device, not for a mouse.</summary>
    private const float BtnSize = 44f;

    // ── CLEARING THE MSG BUTTON ───────────────────────────────────────────────────────────────
    //
    // RULED at the 2026-09-06 rig walk: *"Msg button overlaps the bottom rig control — unreadable."*
    //
    // Both controls anchor to the BOTTOM-LEFT of the same ViewportOverlay. MsgButton sits at
    // x 8..52 with its bottom 8px off the edge; this panel's rect ran down to -8 as well, so its
    // LAST row — the void selector, added after construction — landed underneath it. The panel
    // was not wrong about its own layout; it was wrong about what else lives in that corner.
    //
    // These are derived from MsgButton's own numbers rather than typed as a literal, because a
    // hard-coded 60 is a value that silently stops clearing the thing it was measured against.
    private const float MsgButtonSize = 44f;    // MsgButton.ButtonSize
    private const float MsgMarginBottom = 8f;   // MsgButton.MarginBottom
    private const float Gap = 8f;
    /// <summary>Where this panel's bottom edge must stop to leave the Msg button readable.</summary>
    private const float BottomClear = -(MsgMarginBottom + MsgButtonSize + Gap);   // -60
    /// <summary>
    /// Nominal height. <c>GrowVertical = Begin</c> means content taller than this pushes the TOP
    /// upward rather than spilling over the bottom, so this is a floor and not a cap — which is
    /// what lets a knob be added without re-tuning it. It was raised with the energy row (#174).
    /// </summary>
    private const float NominalHeight = 316f;

    public ReviewRigPanel(ReviewLighting rig)
    {
        _rig = rig;
        Name = "ReviewRigPanel";

        AnchorLeft = 0f; AnchorRight = 0f; AnchorTop = 1f; AnchorBottom = 1f;
        OffsetLeft = 8f; OffsetRight = 232f;
        // The bottom edge stops above the Msg button; the top follows from the nominal height.
        // GrowVertical.Begin then grows the panel UPWARD, away from the corner both controls
        // wanted, so a future row cannot push it back down onto the button.
        OffsetBottom = BottomClear;
        OffsetTop = BottomClear - NominalHeight;
        GrowVertical = GrowDirection.Begin;

        var toggle = new Button { Text = "RIG ▸", TooltipText = "§6.2.1 rig pass" };
        toggle.AddThemeFontSizeOverride("font_size", 16);
        toggle.CustomMinimumSize = new Vector2(96, BtnSize);
        AddChild(toggle);

        // STARTS COLLAPSED. The first capture through this panel had it open, and it covered the
        // left third of the scene and half its height — the floor it exists to make judgeable.
        // §13.1's verdict comes from the lit scene, so a control that stands in front of the
        // scene by default is measuring itself. One tap opens it.
        _body = new VBoxContainer { Name = "RigBody", Visible = false };
        AddChild(_body);

        // Collapsible, and that is a requirement rather than a convenience: the panel sits on
        // top of the floor it exists to make judgeable, and §13.1's verdict comes from the lit
        // scene. A control that permanently covers a fifth of the scene is measuring itself.
        toggle.Pressed += () =>
        {
            _body.Visible = !_body.Visible;
            toggle.Text = _body.Visible ? "RIG ▾" : "RIG ▸";
        };

        // ENERGY FIRST, because it is the quantity #174 moved and the one Ruling 56 re-opens.
        // A rig-energy walk that cannot reach it ratifies nothing, which is what happened on
        // 2026-09-05 when a build went to the handset with this row missing.
        _energy  = AddRow(_body, "energy", ReviewLighting.EnergyStep,
                          d => _rig.Energy += d, () => $"{_rig.Energy:0.00}");
        _radius  = AddRow(_body, "radius", ReviewLighting.RadiusStep,
                          d => _rig.Radius += d, () => $"{_rig.Radius:0.0} tiles");
        _falloff = AddRow(_body, "falloff", ReviewLighting.FalloffStep,
                          d => _rig.Falloff += d, () => $"{_rig.Falloff:0.00}");
        _ambient = AddRow(_body, "ambient", ReviewLighting.AmbientStep,
                          d => _rig.AmbientLevel += d, () => $"{_rig.AmbientLevel:0.00}");

        var mark = new Button { Text = "MARK WALK" };
        mark.AddThemeFontSizeOverride("font_size", 15);
        mark.CustomMinimumSize = new Vector2(0, BtnSize);
        mark.Pressed += MarkWalk;
        _body.AddChild(mark);

        _walk = new Label { Text = "walk 0 — nothing marked yet" };
        _walk.AddThemeFontSizeOverride("font_size", 12);
        _body.AddChild(_walk);

        LogSettings("rig:start");
    }

    /// <summary>
    /// Add the VOID row — the darkness beyond the walls, switched live.
    ///
    /// It is a row on this panel rather than a build flag for the same reason the rig knobs are:
    /// three near-blacks rebuilt and redeployed one at a time are three separate judgements of
    /// three different walks, and what is actually being asked is *which of these is the room's
    /// outside*. That is a comparison, and a comparison needs the images adjacent in TIME if it
    /// cannot have them adjacent in space.
    ///
    /// The panel does not rank them, label one as expected, or start anywhere but where the build
    /// started. The void has no clause in the bible yet and this session proposes none.
    /// </summary>
    public void AddVoidRow(int count, System.Func<int> get, System.Action<int> set)
    {
        if (_body == null || count <= 1) return;
        AddRow(_body, "void", 1f,
               d => set(((get() + (int)d) % count + count) % count),
               () => $"{get() + 1}/{count}");
    }

    /// <summary>
    /// The cast-shadows rows, for Rafe's walk: shadows on/off (the perf A/B on one build and a
    /// walk control), softness (a ladder from a hard edge to soft), and the fire's flicker
    /// toggle — present, default OFF, and NOT the builder's to decide (§9.2 vs the tended
    /// exception; Rafe rules at the gate with both states in hand).
    /// </summary>
    public void AddShadowRows()
    {
        if (_body == null) return;
        AddRow(_body, "shadows", 1f,
               d => _rig.ShadowsEnabled = !_rig.ShadowsEnabled,
               () => _rig.ShadowsEnabled ? "on" : "off");
        AddRow(_body, "softness", ReviewLighting.SoftnessStep,
               d => _rig.ShadowSoftness += d, () => $"{_rig.ShadowSoftness:0.0}");
        AddRow(_body, "darkness", ReviewLighting.DarknessStep,
               d => _rig.ShadowDarkness += d, () => $"{_rig.ShadowDarkness:0.0}");
        if (_rig.FireLightCount > 0)
            AddRow(_body, "fire", ReviewLighting.FireStep,
                   d => _rig.FireEnergy += d, () => $"{_rig.FireEnergy:0.0}");
        if (_rig.FireLightCount > 0)
            AddRow(_body, "flicker", 1f,
                   d => _rig.FireFlicker = !_rig.FireFlicker,
                   () => _rig.FireFlicker ? "on" : "off");
    }

    private Label AddRow(Control parent, string name, float step,
                         System.Action<float> nudge, System.Func<string> read)
    {
        var row = new HBoxContainer();
        var label = new Label { Text = $"{name} {read()}" };
        label.AddThemeFontSizeOverride("font_size", 14);
        label.CustomMinimumSize = new Vector2(126, BtnSize);
        label.VerticalAlignment = VerticalAlignment.Center;

        var minus = new Button { Text = "−" };
        var plus  = new Button { Text = "+" };
        foreach (var b in new[] { minus, plus })
        {
            b.AddThemeFontSizeOverride("font_size", 20);
            b.CustomMinimumSize = new Vector2(BtnSize, BtnSize);
        }

        void Apply(float d)
        {
            nudge(d);
            label.Text = $"{name} {read()}";
            LogSettings($"rig:{name}");
        }

        minus.Pressed += () => Apply(-step);
        plus.Pressed  += () => Apply(+step);

        row.AddChild(label);
        row.AddChild(minus);
        row.AddChild(plus);
        parent.AddChild(row);
        return label;
    }

    /// <summary>
    /// Stamp a numbered walk. §6.2.1 owes the bible a set of ratified values, and a walk that
    /// was not written down cannot supply them: on iOS there is no console, so this goes through
    /// Diag, which writes to the app container and can be pulled back off the device. The same
    /// mechanism that proved the review corridor actually booted.
    /// </summary>
    private void MarkWalk()
    {
        _walkNumber++;
        string line = $"[Tier1] WALK {_walkNumber}: {_rig.Settings()}";
        GD.Print(line);
        Diag.Log(line);
        if (_walk != null) _walk.Text = $"walk {_walkNumber} marked";
    }

    private void LogSettings(string tag)
    {
        string line = $"[Tier1] {tag}: {_rig.Settings()}";
        GD.Print(line);
        Diag.Log(line);
    }
}
