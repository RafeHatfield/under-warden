using System.Text.RegularExpressions;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Persistence;
using UnderWarden.Logic.Persistence.Namespaces;
using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Full-screen overlay that presents Under-Warden pending memos to the player.
/// Shown after a run ends (between game-over screen and the main menu) whenever
/// PersistentRunState.UnderWarden.PendingMemos is non-empty.
///
/// Layout mirrors a bureaucratic correspondence inbox: subject list on the left,
/// formatted letter body on the right, a "Dismiss" button at the bottom.
///
/// Built entirely in code — no .tscn file. Pattern mirrors GameOverScreen.cs.
///
/// Dismiss flow:
///   1. Remove selected memo from pending queue and flush persistence.
///   2. Select the next memo, or fire InboxClosed if queue is now empty.
///
/// The panel does NOT take a snapshot of PendingMemos — it mutates the live list
/// directly so each dismiss is immediately durable.
/// </summary>
public sealed partial class MemoInboxPanel : Control
{
    // ── Layout constants ───────────────────────────────────────────────────────

    private const int SubjectListWidthPct = 40; // subject column is ~40% of panel width
    private const int TitleFontSize       = 32;
    private const int SubjectFontSize     = 18;
    private const int BodyFontSize        = 16;
    private const int ButtonFontSize      = 22;
    private const int ButtonHeight        = 64;

    // ── Godot nodes ───────────────────────────────────────────────────────────

    private VBoxContainer? _subjectColumn;
    private RichTextLabel? _bodyLabel;
    private TouchButton?   _dismissButton;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private PersistentRunState? _state;
    private IPersistencePathProvider? _provider;
    private int _selectedIndex = -1; // index into state.UnderWarden.PendingMemos

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired when the player has dismissed all pending memos and the panel should close.
    /// Main.cs wires this to navigate back to the main menu.
    /// </summary>
    public event Action? InboxClosed;

    public override void _Ready()
    {
        BuildLayout();
        Visible = false;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!Visible) return;
        // Consume all clicks on the backdrop to prevent fallthrough to the game view.
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            AcceptEvent();
    }

    /// <summary>
    /// Show the inbox for a set of pending memos. If the queue is empty, fires
    /// InboxClosed immediately without showing the panel.
    ///
    /// provider is required for flush-on-dismiss; registry is accepted for future
    /// use (re-render on selection) but is not currently consumed.
    /// </summary>
    public void Show(
        PersistentRunState state,
        IPersistencePathProvider provider,
        MemoRegistry? registry = null)
    {
        _ = registry; // not needed yet — available for future use (e.g. re-resolve slots)

        if (state.UnderWarden.PendingMemos.Count == 0)
        {
            // Nothing to show — go straight to close.
            InboxClosed?.Invoke();
            return;
        }

        _state    = state;
        _provider = provider;

        RefreshSubjectList();
        SelectMemo(0);
        Visible = true;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>Rebuild the subject list column from the current pending queue.</summary>
    private void RefreshSubjectList()
    {
        if (_subjectColumn == null || _state == null) return;

        // Free all existing subject rows before repopulating.
        foreach (var child in _subjectColumn.GetChildren())
            child.QueueFree();

        var memos = _state.UnderWarden.PendingMemos;
        for (var i = 0; i < memos.Count; i++)
        {
            var memo    = memos[i];
            var capturedIndex = i;

            // Each row is a TouchButton so it participates in hit-testing correctly
            // under integer stretch scale (same reason as GameOverScreen's replay button).
            var row = new TouchButton
            {
                Text              = TruncateSubject(memo.Subject),
                FontSize          = SubjectFontSize,
                BackgroundColor   = new Color(0.15f, 0.15f, 0.2f, 0.9f),
                CornerRadius      = 4,
                CustomMinimumSize = new Vector2(0, 44),
            };
            row.Pressed += () => SelectMemo(capturedIndex);
            _subjectColumn.AddChild(row);
        }
    }

    /// <summary>Select the memo at the given index and show its body.</summary>
    private void SelectMemo(int index)
    {
        if (_state == null) return;

        var memos = _state.UnderWarden.PendingMemos;
        if (memos.Count == 0 || index < 0 || index >= memos.Count)
        {
            _selectedIndex = -1;
            if (_bodyLabel != null)
                _bodyLabel.Text = "";
            if (_dismissButton != null)
            {
                _dismissButton.Text     = "Dismiss";
                _dismissButton.Disabled = true;
            }
            return;
        }

        _selectedIndex = index;
        var memo = memos[index];

        if (_bodyLabel != null)
            _bodyLabel.Text = ApplyEmphasis(memo.Body);

        if (_dismissButton != null)
        {
            _dismissButton.Text     = "Dismiss";
            _dismissButton.Disabled = false;
        }

        // Visually highlight the selected row: tint it darker to indicate selection.
        // Subject list rows are the children of _subjectColumn in order.
        if (_subjectColumn != null)
        {
            var children = _subjectColumn.GetChildren();
            for (var i = 0; i < children.Count; i++)
            {
                if (children[i] is TouchButton btn)
                {
                    btn.BackgroundColor = i == index
                        ? new Color(0.25f, 0.25f, 0.4f, 1f)   // selected: brighter blue-grey
                        : new Color(0.15f, 0.15f, 0.2f, 0.9f); // unselected: dark
                }
            }
        }
    }

    /// <summary>
    /// Dismiss the selected memo: remove from the live list, flush persistence,
    /// then advance to the next memo or fire InboxClosed.
    /// </summary>
    private void OnDismissPressed()
    {
        if (_state == null || _provider == null) return;
        if (_selectedIndex < 0) return;

        var memos = _state.UnderWarden.PendingMemos;
        if (_selectedIndex >= memos.Count) return;

        memos.RemoveAt(_selectedIndex);
        _state.MarkDirty();
        _state.Flush(_provider, GD.PrintErr);

        if (memos.Count == 0)
        {
            // Queue exhausted — close the panel.
            Visible = false;
            InboxClosed?.Invoke();
            return;
        }

        // Rebuild list; keep selection at the same index if possible, else last item.
        RefreshSubjectList();
        var nextIndex = Math.Min(_selectedIndex, memos.Count - 1);
        SelectMemo(nextIndex);
    }

    // ── Layout construction ────────────────────────────────────────────────────

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        // Semi-transparent dark backdrop — intercepts all clicks.
        var bg = new ColorRect { Color = new Color(0, 0, 0, 0.75f), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Outer padding container centred in the screen.
        // MarginContainer provides a fixed inset on all sides so the panel doesn't
        // fill the full screen edge-to-edge on large screens.
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_top",    40);
        margin.AddThemeConstantOverride("margin_bottom", 40);
        margin.AddThemeConstantOverride("margin_left",   32);
        margin.AddThemeConstantOverride("margin_right",  32);
        AddChild(margin);

        // Root VBox: title / content row / dismiss button.
        var rootVBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        rootVBox.AddThemeConstantOverride("separation", 12);
        margin.AddChild(rootVBox);

        // ── Title ─────────────────────────────────────────────────────────────

        var title = new Label
        {
            Text                = "Correspondence",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        title.AddThemeFontSizeOverride("font_size", TitleFontSize);
        title.AddThemeColorOverride("font_color", new Color(0.85f, 0.78f, 0.55f, 1f)); // parchment gold
        rootVBox.AddChild(title);

        // ── Content row (subject list | body) ─────────────────────────────────

        var contentRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        contentRow.AddThemeConstantOverride("separation", 12);
        contentRow.SizeFlagsVertical = SizeFlags.ExpandFill;
        rootVBox.AddChild(contentRow);

        // Left column: scrollable subject list.
        var leftScroll = new ScrollContainer
        {
            MouseFilter      = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.Fill,
            SizeFlagsVertical   = SizeFlags.ExpandFill,
            CustomMinimumSize   = new Vector2(0, 0),
        };
        // Size ratio: ~40% left, ~60% right.
        // HBoxContainer children use stretch ratios via SizeFlags.ExpandFill + ratio.
        leftScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        leftScroll.SizeFlagsStretchRatio = 0.4f;
        contentRow.AddChild(leftScroll);

        _subjectColumn = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        _subjectColumn.AddThemeConstantOverride("separation", 6);
        _subjectColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        leftScroll.AddChild(_subjectColumn);

        // Right column: body text (RichTextLabel with BBCode).
        var rightScroll = new ScrollContainer
        {
            MouseFilter      = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical   = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.6f,
        };
        contentRow.AddChild(rightScroll);

        _bodyLabel = new RichTextLabel
        {
            BbcodeEnabled       = true,
            FitContent          = true,
            AutowrapMode        = TextServer.AutowrapMode.Word,
            MouseFilter         = MouseFilterEnum.Ignore,
            ScrollActive        = false, // outer scroll container handles scrolling
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _bodyLabel.AddThemeFontSizeOverride("normal_font_size", BodyFontSize);
        _bodyLabel.AddThemeFontSizeOverride("bold_font_size",   BodyFontSize);
        rightScroll.AddChild(_bodyLabel);

        // ── Dismiss button ─────────────────────────────────────────────────────

        _dismissButton = new TouchButton
        {
            Text              = "Dismiss",
            FontSize          = ButtonFontSize,
            BackgroundColor   = new Color(0.35f, 0.25f, 0.1f, 0.9f), // parchment amber
            CornerRadius      = 6,
            CustomMinimumSize = new Vector2(0, ButtonHeight),
            Disabled          = true, // disabled until a memo is selected
        };
        _dismissButton.Pressed += OnDismissPressed;
        rootVBox.AddChild(_dismissButton);
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Convert **text** markdown emphasis to BBCode bold tags.
    /// Called before setting RichTextLabel.Text.
    /// </summary>
    private static string ApplyEmphasis(string text) =>
        Regex.Replace(text, @"\*\*(.+?)\*\*", "[b]$1[/b]");

    /// <summary>
    /// Truncate long subjects so they fit in the narrow subject column.
    /// Adds "…" if truncated.
    /// </summary>
    private static string TruncateSubject(string subject, int maxLen = 40) =>
        subject.Length <= maxLen ? subject : subject[..maxLen] + "…";
}
