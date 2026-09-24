using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Full-screen main menu shown at game startup and after game-over.
///
/// Signals are Godot signals so they integrate naturally with the scene tree.
/// "Testing Mode" button is only visible in debug builds — zero cost in release.
/// </summary>
public sealed partial class MainMenuPanel : Control
{
    [Signal] public delegate void NewGameRequestedEventHandler();
    [Signal] public delegate void ExploreModeRequestedEventHandler();
    [Signal] public delegate void TestingModeRequestedEventHandler();
    [Signal] public delegate void OptionsRequestedEventHandler();

    public override void _Ready()
    {
        BuildLayout();
    }

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // Consume all input — nothing behind the menu should receive taps.
        MouseFilter = MouseFilterEnum.Stop;

        // Semi-transparent dark backdrop
        var bg = new ColorRect
        {
            Color       = new Color(0f, 0f, 0f, 0.82f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Centered content container
        var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        vbox.GrowHorizontal    = GrowDirection.Both;
        vbox.GrowVertical      = GrowDirection.Both;
        vbox.CustomMinimumSize = new Vector2(320, 0);
        vbox.AddThemeConstantOverride("separation", 16);
        AddChild(vbox);

        // Game title
        var titleLabel = new Label
        {
            Text                = "The Under-Warden",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 48);
        vbox.AddChild(titleLabel);

        // Subtitle
        var subtitleLabel = new Label
        {
            Text                = "A Roguelike",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        subtitleLabel.AddThemeFontSizeOverride("font_size", 20);
        subtitleLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f, 1f));
        vbox.AddChild(subtitleLabel);

        // Spacer between subtitle and buttons
        var spacer = new Control { CustomMinimumSize = new Vector2(0, 32), MouseFilter = MouseFilterEnum.Ignore };
        vbox.AddChild(spacer);

        // New Game button
        var newGameBtn = new TouchButton
        {
            Text              = "New Game",
            FontSize          = 24,
            BackgroundColor   = new Color(0.15f, 0.45f, 0.15f, 0.95f),
            CustomMinimumSize = new Vector2(280, 64),
        };
        newGameBtn.Pressed += () => EmitSignal(SignalName.NewGameRequested);
        vbox.AddChild(newGameBtn);

        // Debug-only buttons
        if (OS.IsDebugBuild())
        {
            var exploreBtn = new TouchButton
            {
                Text              = "Explore Mode",
                FontSize          = 24,
                BackgroundColor   = new Color(0.15f, 0.35f, 0.45f, 0.95f),
                CustomMinimumSize = new Vector2(280, 64),
            };
            exploreBtn.Pressed += () => EmitSignal(SignalName.ExploreModeRequested);
            vbox.AddChild(exploreBtn);

            var testingBtn = new TouchButton
            {
                Text              = "Testing Mode",
                FontSize          = 24,
                BackgroundColor   = new Color(0.3f, 0.25f, 0.5f, 0.95f),
                CustomMinimumSize = new Vector2(280, 64),
            };
            testingBtn.Pressed += () => EmitSignal(SignalName.TestingModeRequested);
            vbox.AddChild(testingBtn);
        }

        // Options button
        var optionsBtn = new TouchButton
        {
            Text              = "Options",
            FontSize          = 24,
            BackgroundColor   = new Color(0.25f, 0.25f, 0.35f, 0.95f),
            CustomMinimumSize = new Vector2(280, 64),
        };
        optionsBtn.Pressed += () => EmitSignal(SignalName.OptionsRequested);
        vbox.AddChild(optionsBtn);
    }
}
