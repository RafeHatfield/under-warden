using Godot;
using System.Collections.Generic;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Full-screen overlay that displays the complete message history as a scrollable log.
/// Opened by the Msg button; closed by the X button. No game state is mutated here.
///
/// Each entry renders as a RichTextLabel with BBCode enabled, so existing toast
/// formatting (colors, bold, etc.) carries over with zero additional work.
///
/// Auto-scrolls to the bottom on Open() so the most recent messages are immediately
/// visible. Uses CallDeferred to scroll after layout resolves.
///
/// Pattern mirrors EquipmentPanel: full-screen ColorRect backdrop + inset panel
/// container + header row (title + X) + scrollable body.
/// </summary>
public sealed partial class MessageLogPanel : Control
{
    private ScrollContainer? _scrollContainer;
    private VBoxContainer?   _messageList;

    public override void _Ready()
    {
        BuildLayout();
        // Full-screen: must eat all input while visible so dungeon taps don't bleed through.
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Populate the log with <paramref name="history"/> and show the panel.
    /// Clears any previously rendered entries first.
    /// </summary>
    public void Open(IReadOnlyList<string> history)
    {
        PopulateMessages(history);
        Visible = true;
        // Scroll to bottom after layout resolves — layout doesn't know final size during this frame.
        CallDeferred(MethodName.ScrollToBottom);
    }

    /// <summary>Hide the panel. No game-state side effects.</summary>
    public void Close()
    {
        Visible = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Layout construction (once in _Ready)
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        // Fill full viewport.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Semi-transparent dark backdrop — slightly more opaque than EquipmentPanel so
        // messages are easy to read against the dungeon background.
        var backdrop = new ColorRect
        {
            Color       = new Color(0.05f, 0.05f, 0.1f, 0.95f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        // Inset container — 16px margins so content never touches screen edges.
        var inset = new MarginContainer();
        inset.AddThemeConstantOverride("margin_left",   16);
        inset.AddThemeConstantOverride("margin_right",  16);
        inset.AddThemeConstantOverride("margin_top",    16);
        inset.AddThemeConstantOverride("margin_bottom", 16);
        inset.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        inset.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(inset);

        // Outer VBox: header + divider + scroll body.
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        vbox.MouseFilter = MouseFilterEnum.Ignore;
        inset.AddChild(vbox);

        // Header row: "MESSAGE LOG" title + close button.
        var header = new HBoxContainer();
        header.MouseFilter = MouseFilterEnum.Ignore;
        vbox.AddChild(header);

        // Load PixeloidSans-Bold for the header title; falls back to default if missing.
        var boldFont = GD.Load<FontFile>("res://src/Presentation/assets/fonts/PixeloidSans-Bold.ttf");

        var title = new Label
        {
            Text                = "MESSAGE LOG",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment   = VerticalAlignment.Center,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", Colors.White);
        if (boldFont != null) title.AddThemeFontOverride("font", boldFont);
        header.AddChild(title);

        // X close button — same style as EquipmentPanel close button.
        var closeBtn = new TouchButton
        {
            Text              = "✕",
            FontSize          = 24,
            BackgroundColor   = new Color(0.4f, 0.1f, 0.1f, 0.9f),
            CustomMinimumSize = new Vector2(44, 44),
        };
        closeBtn.Pressed += Close;
        header.AddChild(closeBtn);

        // Thin divider line.
        var divider = new ColorRect
        {
            Color             = new Color(0.4f, 0.4f, 0.5f, 0.6f),
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter       = MouseFilterEnum.Ignore,
        };
        vbox.AddChild(divider);

        // Scroll container fills all remaining vertical space.
        _scrollContainer = new ScrollContainer
        {
            SizeFlagsVertical        = SizeFlags.ExpandFill,
            SizeFlagsHorizontal      = SizeFlags.ExpandFill,
            HorizontalScrollMode     = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode       = ScrollContainer.ScrollMode.Auto,
        };
        // Pass so clicks on scroll areas still reach _GuiInput if needed, but the
        // container itself should catch scrolling gestures.
        _scrollContainer.MouseFilter = MouseFilterEnum.Pass;
        vbox.AddChild(_scrollContainer);

        // VBox holding individual message entries.
        _messageList = new VBoxContainer();
        _messageList.AddThemeConstantOverride("separation", 4);
        _messageList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _messageList.MouseFilter         = MouseFilterEnum.Ignore;
        _scrollContainer.AddChild(_messageList);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Populate
    // ─────────────────────────────────────────────────────────────────────────

    private void PopulateMessages(IReadOnlyList<string> history)
    {
        if (_messageList == null) return;

        // Remove previous entries.
        foreach (var child in _messageList.GetChildren())
            child.SafeFree();

        if (history.Count == 0)
        {
            var empty = new Label
            {
                Text        = "(no messages yet)",
                MouseFilter = MouseFilterEnum.Ignore,
            };
            empty.AddThemeFontSizeOverride("font_size", 18);
            empty.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f, 1f));
            _messageList.AddChild(empty);
            return;
        }

        // Load monospace font for message text — same font as toasts for visual consistency.
        var monoFont = GD.Load<FontFile>("res://src/Presentation/assets/fonts/PixeloidMono.ttf");
        if (monoFont != null)
        {
            monoFont.Antialiasing        = TextServer.FontAntialiasing.None;
            monoFont.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
            monoFont.Hinting             = TextServer.Hinting.None;
        }

        // Render oldest → newest (top → bottom) so reading down == reading forward in time.
        foreach (var bbcode in history)
        {
            var entry = new RichTextLabel
            {
                BbcodeEnabled       = true,
                FitContent          = true,
                AutowrapMode        = TextServer.AutowrapMode.Word,
                ScrollActive        = false,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter         = MouseFilterEnum.Ignore,
            };
            if (monoFont != null) entry.AddThemeFontOverride("normal_font", monoFont);
            entry.AddThemeFontSizeOverride("normal_font_size", 18);
            entry.AddThemeColorOverride("default_color", Colors.White);
            entry.AppendText(bbcode);

            _messageList.AddChild(entry);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Deferred scroll-to-bottom
    // ─────────────────────────────────────────────────────────────────────────

    private void ScrollToBottom()
    {
        if (_scrollContainer == null) return;
        var bar = _scrollContainer.GetVScrollBar();
        if (bar != null)
            _scrollContainer.ScrollVertical = (int)bar.MaxValue;
    }
}
