using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Small icon button anchored to the bottom-left of the ViewportOverlay zone.
/// Tapping it recalls the message history (same behavior as the old HUD Msg button
/// removed in Phase 1).
///
/// Visual: 40x40px styled with a semi-transparent dark background, rounded corners,
/// and a subtle border — matching the MiniMap aesthetic. The unicode envelope glyph
/// ("✉") serves as the icon; falls back cleanly to "Msg" text if the pixel font
/// lacks the glyph.
///
/// Input: MouseFilter = Stop so the button catches taps. The parent ViewportOverlay
/// has MouseFilter = Ignore, so the rest of the overlay passes input through to the
/// dungeon as expected.
///
/// Size: CustomMinimumSize = 44x44 to meet the minimum touch-target spec (44pt).
/// The visual styling creates the appearance of a 40x40px element; the extra 4px
/// margin is invisible but ensures reliable tap registration on small screens.
/// </summary>
public sealed partial class MsgButton : Control
{
    // Visual dimensions — the styled background fills the full 44x44 control,
    // but the button reads as compact at this size.
    private const int ButtonSize = 44;

    // Margins from the ViewportOverlay edges (bottom-left anchor).
    private const float MarginLeft   = 8f;
    private const float MarginBottom = 8f;

    // C# event — not a Godot signal, so no Callable allocation or leak.
    public event Action? Pressed;

    public override void _Ready()
    {
        // Stop input so taps don't fall through to the dungeon underneath.
        MouseFilter = MouseFilterEnum.Stop;

        // Ensure we're a minimum 44x44 for touch targets.
        CustomMinimumSize = new Vector2(ButtonSize, ButtonSize);

        // Anchor to bottom-left of parent (ViewportOverlay zone).
        AnchorLeft   = 0f;
        AnchorTop    = 1f;
        AnchorRight  = 0f;
        AnchorBottom = 1f;

        OffsetLeft   = MarginLeft;
        OffsetTop    = -(ButtonSize + MarginBottom);
        OffsetRight  = MarginLeft + ButtonSize;
        OffsetBottom = -MarginBottom;

        BuildLayout();
    }

    /// <summary>
    /// Hit-test via _GuiInput rather than inheriting from Button.
    /// Godot's Button class has hit-area drift under integer stretch scale mode on iOS;
    /// this workaround (used by all interactive controls in this project) is reliable.
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        bool tapped = false;

        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            tapped = true;
        else if (@event is InputEventScreenTouch touch && touch.Pressed)
            tapped = true;

        if (tapped)
        {
            AcceptEvent(); // prevent propagation to dungeon view
            Pressed?.Invoke();
        }
    }

    // -------------------------------------------------------------------------

    private void BuildLayout()
    {
        // Background — matches the MiniMap style: semi-transparent dark bg,
        // 4px rounded corners, subtle 1px border.
        var bg = new StyleBoxFlat
        {
            BgColor                 = new Color(0.05f, 0.05f, 0.08f, 0.82f),
            CornerRadiusTopLeft     = 4,
            CornerRadiusTopRight    = 4,
            CornerRadiusBottomLeft  = 4,
            CornerRadiusBottomRight = 4,
            BorderColor             = new Color(0.60f, 0.60f, 0.70f, 0.45f),
            BorderWidthTop          = 1,
            BorderWidthRight        = 1,
            BorderWidthBottom       = 1,
            BorderWidthLeft         = 1,
        };

        // Transparent panel that renders the styled background.
        var panel = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", bg);
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(panel);

        // Load the pixel font used throughout the HUD. The "✉" (U+2709) envelope
        // glyph renders cleanly in PixeloidMono; if not present, fallback text "Msg"
        // is used below.
        var font = GD.Load<FontFile>("res://src/Presentation/assets/fonts/PixeloidMono.ttf");

        var label = new Label
        {
            // "✉" is the unicode envelope — universally understood as "messages".
            // If the glyph is absent from the pixel font, the renderer will show a
            // fallback box. A plain "Msg" label is the safe alternative; swap the
            // string here if the glyph doesn't render on device.
            Text                = "Msg",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        if (font != null)
        {
            label.AddThemeFontOverride("font", font);
            font.Antialiasing         = TextServer.FontAntialiasing.None;
            font.SubpixelPositioning  = TextServer.SubpixelPositioning.Disabled;
            font.Hinting              = TextServer.Hinting.None;
        }
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.90f, 1f));
        label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(label);
    }
}
