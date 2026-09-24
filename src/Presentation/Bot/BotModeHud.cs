using UnderWarden.Presentation.Bot;
using Godot;

namespace UnderWarden.Presentation.Bot;

/// <summary>
/// HUD overlay showing bot mode state. Displays at top-center when active.
///
/// Shows: "BOT: balanced [1.0s/turn]  F4=off  F5=persona  F6=speed"
/// Hidden completely when bot mode is inactive.
///
/// Attach to UILayer in Main.tscn. Call Initialize(driver) after driver is constructed.
/// </summary>
public sealed partial class BotModeHud : Control
{
    private Label? _label;
    private Panel? _background;
    private BotPlayerDriver? _driver;

    public override void _Ready()
    {
        // Background panel — dark semi-transparent
        _background = new Panel();
        AddChild(_background);

        _label = new Label();
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Center;
        AddChild(_label);

        // Style the background
        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0, 0, 0, 0.7f);
        styleBox.SetCornerRadiusAll(4);
        _background.AddThemeStyleboxOverride("panel", styleBox);

        // Position at top-center
        SetAnchorsPreset(LayoutPreset.TopWide);
        CustomMinimumSize = new Vector2(0, 32);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;

        Visible = false; // hidden by default
    }

    /// <summary>
    /// Attach to a BotPlayerDriver to receive enable/disable events.
    /// Call from Main._Ready() after driver is constructed.
    /// </summary>
    public void Initialize(BotPlayerDriver driver)
    {
        _driver = driver;
        _driver.EnabledChanged += OnDriverEnabledChanged;
        UpdateDisplay();
    }

    /// <summary>Update the HUD text (called externally on persona/speed change).</summary>
    public void RefreshDisplay() => UpdateDisplay();

    private void OnDriverEnabledChanged(bool enabled)
    {
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (_driver == null) { Visible = false; return; }

        Visible = _driver.Enabled;
        if (!Visible) return;

        string speed = _driver.TurnDelaySeconds <= 0.01f ? "max"
            : _driver.TurnDelaySeconds <= 0.11f ? "very fast"
            : _driver.TurnDelaySeconds <= 0.26f ? "fast"
            : _driver.TurnDelaySeconds <= 0.51f ? "brisk"
            : "watch";

        if (_label != null)
            _label.Text = $"BOT: {_driver.Persona.Name}  [{speed}]  F4=off  F5=persona  F6=speed";

        // Resize background to fit the label
        if (_label != null && _background != null)
        {
            var textSize = _label.Size;
            _background.Size = textSize + new Vector2(16, 8);
            _background.Position = new Vector2((_label.Size.X - _background.Size.X) / 2, -4);
            _label.Size = _background.Size;
        }
    }
}
