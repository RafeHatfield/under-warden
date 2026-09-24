using UnderWarden.Presentation.Input;
using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Full-screen overlay shown during targeting mode.
/// Displays a "Cancel" button at the top of the screen and shows visual
/// feedback (color tint indicator) that the player is in targeting mode.
///
/// The overlay is transparent to mouse/touch events except the Cancel button,
/// so taps on the dungeon still reach the game scene and are handled by
/// InputHandler (in targeting mode) to pick targets or locations.
///
/// Lifecycle:
///   GameController enters Targeting phase → Show()
///   Player picks target or cancels         → Hide()
///
/// Plan reference: plan_spell_wand_scroll_system.md §2.6
/// </summary>
public sealed partial class TargetingOverlay : Control
{
    // Fired when the player taps the Cancel button.
    // GameController wires this to GameController.CancelTargeting().
    public event Action? CancelPressed;

    private Label? _promptLabel;
    private TouchButton? _cancelButton;
    private ColorRect? _tintBar;

    public override void _Ready()
    {
        BuildLayout();
        // Start hidden — GameController calls Show() when entering targeting mode.
        Visible = false;
        // Let pointer events pass through to the dungeon view below.
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>
    /// Show the targeting overlay with the given prompt text.
    /// Called by GameController when entering targeting mode.
    /// </summary>
    public void Show(string promptText)
    {
        if (_promptLabel != null)
            _promptLabel.Text = promptText;
        Visible = true;
        Diag.Log($"TargetingOverlay.Show: '{promptText}'");
    }

    public new void Hide()
    {
        Visible = false;
        Diag.Log("TargetingOverlay.Hide");
    }

    private void BuildLayout()
    {
        // Anchor full screen so we cover the entire viewport.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Overlay is mouse-passthrough except the Cancel button container.
        MouseFilter = MouseFilterEnum.Ignore;

        // Thin tint bar at the top to signal targeting mode (semi-transparent blue-white).
        _tintBar = new ColorRect
        {
            Color = new Color(0.3f, 0.6f, 1f, 0.15f),
            CustomMinimumSize = new Vector2(0, 56),
            LayoutMode = 1,
        };
        _tintBar.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _tintBar.OffsetBottom = 56;
        _tintBar.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_tintBar);

        // Horizontal container for prompt + cancel button along the top bar.
        var bar = new HBoxContainer
        {
            LayoutMode = 1,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        bar.OffsetBottom = 56;
        bar.AddThemeConstantOverride("separation", 12);
        AddChild(bar);

        // Prompt label
        _promptLabel = new Label
        {
            Text = "Tap a target",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _promptLabel.AddThemeFontSizeOverride("font_size", 22);
        _promptLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.95f, 1f, 1f));
        bar.AddChild(_promptLabel);

        // Cancel button — the only interactive element in the overlay
        _cancelButton = new TouchButton
        {
            Text = "Cancel",
            FontSize = 20,
            BackgroundColor = new Color(0.7f, 0.2f, 0.2f, 0.9f),
            CustomMinimumSize = new Vector2(100, 44),
        };
        _cancelButton.MouseFilter = MouseFilterEnum.Stop; // capture taps on the button
        _cancelButton.Pressed += OnCancelPressed;
        bar.AddChild(_cancelButton);
    }

    private void OnCancelPressed()
    {
        Diag.Log("TargetingOverlay: Cancel pressed");
        CancelPressed?.Invoke();
    }
}
