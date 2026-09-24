using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Full-screen overlay shown on win or defeat.
/// Shows outcome, basic stats, and a "Play Again" button.
/// Uses _GuiInput for the replay button (same as InventoryPanel) to avoid
/// the Godot integer stretch coordinate bug that offsets Button hit areas.
/// </summary>
public sealed partial class GameOverScreen : Control
{
    private Label?       _titleLabel;
    private Label?       _statsLabel;
    private TouchButton? _replayButton;

    public event Action? ReplayRequested;

    public override void _Ready()
    {
        BuildLayout();
        Visible = false;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            // Consume all clicks on the overlay — the TouchButton handles its own tap.
            AcceptEvent();
        }
    }

    /// <summary>Show the screen with the given outcome and stat text.</summary>
    public void Show(bool playerWon, string stats)
    {
        if (_titleLabel != null)
        {
            _titleLabel.Text         = playerWon ? "Victory!" : "Defeated";
            _titleLabel.SelfModulate = playerWon ? Colors.Gold : Colors.OrangeRed;
        }

        if (_statsLabel != null)
            _statsLabel.Text = stats;

        Visible = true;
    }

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        // Semi-transparent backdrop
        var bg = new ColorRect { Color = new Color(0, 0, 0, 0.75f), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        vbox.GrowHorizontal    = GrowDirection.Both;
        vbox.GrowVertical      = GrowDirection.Both;
        vbox.CustomMinimumSize = new Vector2(300, 0);
        vbox.AddThemeConstantOverride("separation", 24);
        AddChild(vbox);

        _titleLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", 48);
        vbox.AddChild(_titleLabel);

        _statsLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode        = TextServer.AutowrapMode.Word,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        _statsLabel.AddThemeFontSizeOverride("font_size", 20);
        vbox.AddChild(_statsLabel);

        // Use TouchButton — avoids hit-area offset bug under integer stretch scale on iOS.
        _replayButton = new TouchButton
        {
            Text              = "Play Again",
            FontSize          = 24,
            BackgroundColor   = new Color(0.2f, 0.5f, 0.2f, 0.9f),
            CustomMinimumSize = new Vector2(0, 64),
        };
        _replayButton.Pressed += () => ReplayRequested?.Invoke();
        vbox.AddChild(_replayButton);
    }
}
