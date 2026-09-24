using System.Collections.Generic;
using UnderWarden.Logic.Voice;
using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// The Hollowmark ribbon (docs/systems/voice_delivery.md §Ribbon contract). Its OWN Control — NOT
/// ToastLog / MessageLogPanel. Lines STACK: up to <see cref="MaxCards"/> attributed cards, newest on
/// top, each tagged "✦ HOLLOWMARK" so the player knows who is speaking. Dismiss is a device setting:
/// Manual (default) keeps each card until tapped — nothing missed mid-fight — while Timed fades each
/// after its own duration. Portrait-safe near the top, clear of the bottom touch controls.
///
/// The scheduler no longer supersedes by tier (that was for a single-slot ribbon); it still filters by
/// cooldown / mode / once-per-run and delivers at most one line per turn, which this stacks.
/// </summary>
public sealed partial class VoiceRibbon : Control
{
    [Signal] public delegate void HistoryRequestedEventHandler();
    [Signal] public delegate void QuietRequestedEventHandler();

    private const int FontSize = 22;
    private const int MaxCards = 3;                         // stack cap; oldest drops off the bottom
    private const double FadeSeconds = 0.35;               // dismiss/expire fade-out
    private static readonly Color Purple = new("b090d0");

    // One live line on the stack: its card node + when it auto-expires (MaxValue in Manual mode).
    private sealed class Card
    {
        public required PanelContainer Node;
        public double ExpireAt;
        public bool Fading;
    }

    private readonly List<Card> _cards = new();            // index 0 = newest (top)
    private VBoxContainer? _stack;
    private HBoxContainer? _controls;
    private PanelContainer? _historyPanel;
    private RichTextLabel? _historyText;

    public override void _Ready()
    {
        // Portrait-safe: a top band, inset from the edges, tall enough for the 3-card stack + controls,
        // clear of the bottom touch controls.
        SetAnchorsPreset(LayoutPreset.TopWide);
        OffsetTop = 96;                // below the status bar
        OffsetLeft = 12;
        OffsetRight = -12;
        OffsetBottom = 360;            // room for MaxCards cards + the controls row
        MouseFilter = MouseFilterEnum.Ignore;   // container passes through; cards opt back in

        // Root column: a right-aligned controls row, then the card stack below it.
        var root = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);

        _controls = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = MouseFilterEnum.Ignore };
        _controls.AddChild(MakeGlyphButton("⋯", () => EmitSignal(SignalName.HistoryRequested)));        // history
        _controls.AddChild(MakeGlyphButton("\U0001F910", () => EmitSignal(SignalName.QuietRequested))); // 🤫 quiet floor
        _controls.Visible = false;    // hidden until there's something to control
        root.AddChild(_controls);

        _stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _stack.AddThemeConstantOverride("separation", 6);
        root.AddChild(_stack);

        SetProcess(true);
    }

    private Button MakeGlyphButton(string glyph, System.Action onPressed)
    {
        var b = new Button
        {
            Text = glyph,
            Flat = true,
            CustomMinimumSize = new Vector2(44, 44),   // touch target
            MouseFilter = MouseFilterEnum.Stop,
        };
        b.Pressed += () => onPressed();
        return b;
    }

    /// <summary>
    /// Push a delivered line onto the top of the stack. In Manual mode the card stays until tapped (or
    /// until pushed off by the cap); in Timed mode it fades after <paramref name="seconds"/>.
    /// </summary>
    public void ShowLine(string text, float seconds, bool manualDismiss)
    {
        if (_stack == null) return;

        var card = new Card
        {
            Node = BuildCardNode(text),
            ExpireAt = manualDismiss ? double.MaxValue : Now() + seconds,
        };
        WireDismiss(card);

        _stack.AddChild(card.Node);
        _stack.MoveChild(card.Node, 0);   // newest on top
        _cards.Insert(0, card);

        // Cap: drop the oldest (bottom) immediately when a 4th arrives — snappy, no fade.
        while (_cards.Count > MaxCards)
        {
            var oldest = _cards[^1];
            _cards.RemoveAt(_cards.Count - 1);
            oldest.Node.QueueFree();
        }

        if (_controls != null) _controls.Visible = true;
    }

    private PanelContainer BuildCardNode(string text)
    {
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        var bg = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.05f, 0.09f, 0.86f),
            BorderColor = Purple,
            ContentMarginLeft = 12, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 8,
        };
        bg.SetBorderWidthAll(0);
        bg.BorderWidthLeft = 3;
        bg.SetCornerRadiusAll(6);
        card.AddThemeStyleboxOverride("panel", bg);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 1);
        card.AddChild(col);

        // Attribution header — glyph + name, so the player knows Hollowmark is speaking.
        var header = new Label
        {
            Text = "✦ HOLLOWMARK",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        header.AddThemeFontSizeOverride("font_size", 13);
        header.AddThemeColorOverride("font_color", new Color(0.69f, 0.56f, 0.82f, 0.95f));
        col.AddChild(header);

        var line = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,                    // card sizes to the wrapped line; width is the full ribbon
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore, // the card (parent) captures the tap-to-dismiss
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        line.AddThemeFontSizeOverride("normal_font_size", FontSize);
        line.Text = $"[i][color=#b090d0]{text}[/color][/i]";
        col.AddChild(line);

        return card;
    }

    private void WireDismiss(Card card)
    {
        card.Node.GuiInput += @event =>
        {
            if (@event is InputEventScreenTouch { Pressed: true }
                or InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                StartFadeOut(card);
        };
    }

    public override void _Process(double delta)
    {
        if (_cards.Count == 0) return;
        double now = Now();
        foreach (var c in _cards.ToArray())
            if (!c.Fading && now >= c.ExpireAt)
                StartFadeOut(c);
    }

    /// <summary>Fade a card out then free it (tap-dismiss or timed expiry).</summary>
    private void StartFadeOut(Card card)
    {
        if (card.Fading) return;
        card.Fading = true;
        var tween = CreateTween();
        tween.TweenProperty(card.Node, "modulate:a", 0.0, FadeSeconds);
        tween.TweenCallback(Callable.From(() => RemoveCard(card)));
    }

    private void RemoveCard(Card card)
    {
        _cards.Remove(card);
        if (Godot.GodotObject.IsInstanceValid(card.Node)) card.Node.QueueFree();
        if (_cards.Count == 0 && _controls != null) _controls.Visible = false;
    }

    /// <summary>Clear the whole stack (e.g., on a new run/floor). No fade.</summary>
    public void ClearAll()
    {
        foreach (var c in _cards) if (Godot.GodotObject.IsInstanceValid(c.Node)) c.Node.QueueFree();
        _cards.Clear();
        if (_controls != null) _controls.Visible = false;
    }

    private static double Now() => Time.GetTicksMsec() / 1000.0;

    /// <summary>
    /// Diagnostic snapshot for the device diagnostic (E): stack depth and the top card's on-screen rect,
    /// so a "delivered" line that renders off-screen or degenerate is caught.
    /// </summary>
    public string DiagState()
    {
        var top = _cards.Count > 0 ? _cards[0].Node : null;
        var r = top?.GetGlobalRect() ?? new Rect2();
        int layer = GetParent() is CanvasLayer cl ? cl.Layer : -999;
        var vp = GetViewportRect().Size;
        return $"ribbonVisible={Visible} cards={_cards.Count} " +
               $"topRect=({r.Position.X:0},{r.Position.Y:0} {r.Size.X:0}x{r.Size.Y:0}) " +
               $"canvasLayer={layer} viewport={vp.X:0}x{vp.Y:0}";
    }

    /// <summary>Toggle the history popup, rendering the scheduler's run-scoped last-20 (newest first).</summary>
    public void ToggleHistory(IReadOnlyList<VoiceHistoryEntry> history)
    {
        if (_historyPanel != null && _historyPanel.Visible)
        {
            _historyPanel.Visible = false;
            return;
        }
        EnsureHistoryPanel();
        var lines = new List<string>();
        for (int i = history.Count - 1; i >= 0; i--)
            lines.Add($"[color=#8878a0]· {history[i].Line}[/color]");
        _historyText!.Text = history.Count == 0
            ? "[color=#6a6a6a][i]No lines yet this run.[/i][/color]"
            : string.Join("\n", lines);
        _historyPanel!.Visible = true;
    }

    private void EnsureHistoryPanel()
    {
        if (_historyPanel != null) return;
        _historyPanel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        _historyPanel.SetAnchorsPreset(LayoutPreset.TopWide);
        _historyPanel.OffsetTop = 54;
        var box = new StyleBoxFlat { BgColor = new Color(0.05f, 0.04f, 0.08f, 0.94f) };
        box.SetCornerRadiusAll(6);
        box.ContentMarginLeft = box.ContentMarginRight = box.ContentMarginTop = box.ContentMarginBottom = 10;
        _historyPanel.AddThemeStyleboxOverride("panel", box);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 260) };
        _historyPanel.AddChild(scroll);
        _historyText = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _historyText.AddThemeFontSizeOverride("normal_font_size", 18);
        scroll.AddChild(_historyText);
        AddChild(_historyPanel);
        _historyPanel.Visible = false;
    }
}
