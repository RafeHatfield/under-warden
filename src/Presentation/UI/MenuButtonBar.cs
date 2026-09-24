using UnderWarden.Logic.Content;
using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Horizontal bar containing Gear and Explore buttons, placed in the MenuButtons
/// zone below the quick-slot bar. Built entirely in code — no .tscn file.
///
/// Phase 2 of the mobile layout overhaul: restores the Gear and Explore button
/// functionality that was temporarily removed from HUD in Phase 1.
///
/// Layout: 8px horizontal margin on each side, 8px gap between buttons.
/// Each button gets SizeFlags.ExpandFill so they split available width evenly.
/// Minimum height is enforced at 48px per Apple HIG touch target guidelines.
///
/// Gear button:    dark gold background, opens the equipment panel.
/// Explore button: dark green background, triggers auto-explore. Updates to
///                 show "Exploring..." with yellow tint while active.
/// </summary>
public sealed partial class MenuButtonBar : Control
{
    // Colors defined in task spec — kept as constants for easy tuning.
    private static readonly Color GearBgColor         = new(0.25f, 0.20f, 0.10f, 0.9f);
    private static readonly Color ExploreBgColor      = new(0.15f, 0.35f, 0.15f, 0.9f);
    private static readonly Color ExploreActiveColor  = new(0.40f, 0.40f, 0.05f, 0.9f);
    private static readonly Color PossessBgColor      = new(0.30f, 0.10f, 0.40f, 0.9f); // purple
    private static readonly Color ExitPossessBgColor  = new(0.50f, 0.10f, 0.10f, 0.9f); // dark red
    private static readonly Color CancelBgColor       = new(0.30f, 0.30f, 0.30f, 0.9f); // grey
    private static readonly Color MenuBgColor         = new(0.18f, 0.18f, 0.22f, 0.9f); // dark neutral

    private const int HorizontalMargin = 8;
    private const int ButtonGap        = 8;
    private const int MinButtonHeight  = 48;
    private const int FontSize         = 22;

    public enum PossessionMode { Idle, Targeting, Active }

    private TouchButton? _gearButton;
    private TouchButton? _exploreButton;
    private TouchButton? _possessButton;
    private TouchButton? _exitPossessionButton;
    private TouchButton? _cancelTargetingButton;
    private HBoxContainer? _idleButtons;   // [Gear][Explore][Possess]
    private HBoxContainer? _activeButtons; // [Exit Possession]
    private HBoxContainer? _targetingButtons; // [Gear][Explore][Cancel]

    // C# events — no Godot signal allocation. Null-safe: callers must handle null subscribers.
    public event Action? GearRequested;
    public event Action? ExploreRequested;
    public event Action? PossessRequested;
    public event Action? ExitPossessionRequested;
    public event Action? CancelPossessionTargetingRequested;
    public event Action? MenuRequested;

    public override void _Ready()
    {
        // Fill the MenuButtons zone container entirely.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        BuildLayout();
    }

    /// <summary>
    /// Reflect current auto-explore state on the Explore button.
    /// Called from Main.OnTurnCompleted after each turn.
    /// No-op during possession (explore button is hidden).
    /// </summary>
    public void SetAutoExploreActive(bool active)
    {
        if (_exploreButton == null) return;

        if (active)
        {
            _exploreButton.Text = "Exploring...";
            _exploreButton.BackgroundColor = ExploreActiveColor;
        }
        else
        {
            _exploreButton.Text = "Explore";
            _exploreButton.BackgroundColor = ExploreBgColor;
        }
    }

    /// <summary>
    /// Switch the button bar between idle (Gear/Explore/Possess), targeting (Cancel),
    /// and active-possession (Exit Possession) modes.
    /// Called from Main.OnTurnCompleted after events are processed.
    ///
    /// abilities: species-specific ability buttons shown during Active possession.
    /// Currently always empty — Hall Wardens and other ability-bearing species ship in Phase 8+.
    /// Passed here so callers have the correct signature when abilities land.
    /// </summary>
    public void SetPossessionMode(PossessionMode mode, IReadOnlyList<MonsterAbilityDefinition>? abilities = null)
    {
        if (_idleButtons != null)    _idleButtons.Visible    = mode == PossessionMode.Idle;
        if (_activeButtons != null)  _activeButtons.Visible  = mode == PossessionMode.Active;
        if (_targetingButtons != null) _targetingButtons.Visible = mode == PossessionMode.Targeting;

        // abilities: rendered in Phase 8+ when species definitions include ability lists.
        // Currently always empty; dynamic buttons will be added to _activeButtons here.
        _ = abilities; // parameter accepted, not yet used
    }

    // -------------------------------------------------------------------------
    // Layout — all built in code, no scene file dependency.
    // -------------------------------------------------------------------------

    private void BuildLayout()
    {
        // Outer margin container so buttons don't touch screen edges.
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left",   HorizontalMargin);
        margin.AddThemeConstantOverride("margin_right",  HorizontalMargin);
        margin.AddThemeConstantOverride("margin_top",    8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(margin);

        // Stack overlapping button sets — only one is visible at a time.
        var stack = new Control();
        stack.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddChild(stack);

        // ── Idle mode: [Menu] [Gear] [Explore] [Possess] ──────────────────────
        _idleButtons = MakeHBox(stack);

        // Menu button: fixed narrow width so it doesn't crowd the gameplay buttons.
        var menuButton = new TouchButton
        {
            Text            = "Menu",
            FontSize        = FontSize,
            BackgroundColor = MenuBgColor,
            CornerRadius    = 6,
        };
        menuButton.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        menuButton.SizeFlagsVertical   = SizeFlags.ExpandFill;
        menuButton.CustomMinimumSize   = new Vector2(72, MinButtonHeight);
        menuButton.Pressed            += () => MenuRequested?.Invoke();
        _idleButtons.AddChild(menuButton);

        _gearButton = MakeButton("Gear", GearBgColor, () => GearRequested?.Invoke());
        _idleButtons.AddChild(_gearButton);

        _exploreButton = MakeButton("Explore", ExploreBgColor, () => ExploreRequested?.Invoke());
        _idleButtons.AddChild(_exploreButton);

        _possessButton = MakeButton("Possess", PossessBgColor, () => PossessRequested?.Invoke());
        _idleButtons.AddChild(_possessButton);

        // ── Active possession mode: [Exit Possession] ─────────────────────────
        _activeButtons = MakeHBox(stack);
        _activeButtons.Visible = false;

        _exitPossessionButton = MakeButton("Exit Possession", ExitPossessBgColor,
            () => ExitPossessionRequested?.Invoke());
        _activeButtons.AddChild(_exitPossessionButton);

        // ── Targeting mode: [Gear (hidden)] [Explore (hidden)] [Cancel] ───────
        _targetingButtons = MakeHBox(stack);
        _targetingButtons.Visible = false;

        _cancelTargetingButton = MakeButton("Cancel", CancelBgColor,
            () => CancelPossessionTargetingRequested?.Invoke());
        _targetingButtons.AddChild(_cancelTargetingButton);
    }

    private static HBoxContainer MakeHBox(Control parent)
    {
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", ButtonGap);
        hbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hbox.SizeFlagsVertical   = SizeFlags.ExpandFill;
        hbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        parent.AddChild(hbox);
        return hbox;
    }

    private static TouchButton MakeButton(string text, Color color, Action onPressed)
    {
        var btn = new TouchButton
        {
            Text            = text,
            FontSize        = FontSize,
            BackgroundColor = color,
            CornerRadius    = 6,
        };
        btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        btn.SizeFlagsVertical   = SizeFlags.ExpandFill;
        btn.CustomMinimumSize   = new Vector2(0, MinButtonHeight);
        btn.Pressed            += onPressed;
        return btn;
    }
}
