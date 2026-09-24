using UnderWarden.Logic.Knowledge;
using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Floating inspect panel shown on long-press. Displays tier-gated monster info
/// or item statistics. Appears near the tapped tile and flips to avoid screen edges.
///
/// No .tscn file — built entirely in C# following the established panel pattern.
/// Invisible by default; shown by ShowMonster/ShowItem, dismissed by Hide.
/// </summary>
public sealed partial class InspectPanel : Control
{
    private const int PanelWidth  = 220;
    private const int PanelMinHeight = 80;
    private const int Padding = 12;
    private const int TapOffset = 16; // pixels offset from tap position

    private static readonly Color BackgroundColor  = new(0.08f, 0.08f, 0.12f, 0.92f);
    private static readonly Color HeaderColor      = new(1f, 1f, 1f, 1f);
    private static readonly Color TierColor        = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Color StatColor        = new(0.85f, 0.85f, 0.85f, 1f);
    private static readonly Color WarningColor     = new(1f, 0.65f, 0.0f, 1f);
    private static readonly Color AdviceColor      = new(1f, 1f, 0.3f, 1f);
    private static readonly Color CategoryColor    = new(0.55f, 0.75f, 0.95f, 1f);

    private PanelContainer? _panel;
    private VBoxContainer? _content;
    private Label? _nameLabel;
    private Label? _tierLabel;

    public override void _Ready()
    {
        // Overlay — sits on top of everything, ignores mouse so taps pass through when hidden.
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        BuildLayout();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Render tier-gated monster information and show the panel.</summary>
    public void ShowMonster(MonsterInfoView info)
    {
        if (_content == null) return;
        ClearContent();

        // Header: name
        AddLabel(info.Name, HeaderColor, 18, bold: true);

        // Tier badge
        string tierText = info.Tier switch
        {
            KnowledgeTier.Unknown    => "???",
            KnowledgeTier.Observed   => "Observed",
            KnowledgeTier.Battled    => "Battled",
            KnowledgeTier.Understood => "Understood",
            _                        => "",
        };
        AddLabel(tierText, TierColor, 13);

        if (info.Tier == KnowledgeTier.Unknown)
        {
            // Nothing more to show for unknown species
            Visible = true;
            return;
        }

        AddSeparator();

        // Tier 1+: faction, role, speed
        if (info.FactionLabel != null)
            AddStat("Faction", info.FactionLabel);
        if (info.RoleLabel != null)
            AddStat("Role", info.RoleLabel);
        if (info.SpeedLabel != null)
            AddStat("Speed", info.SpeedLabel);

        // Tier 2+: combat stats
        if (info.DurabilityLabel != null || info.DamageLabel != null)
        {
            AddSeparator();
            if (info.DurabilityLabel != null) AddStat("Durability", info.DurabilityLabel);
            if (info.DamageLabel != null)     AddStat("Damage",     info.DamageLabel);
            if (info.AccuracyLabel != null)   AddStat("Accuracy",   info.AccuracyLabel);
            if (info.EvasionLabel != null)    AddStat("Evasion",    info.EvasionLabel);
        }

        // Tier 3+: warnings and advice
        if (info.SpecialWarnings.Count > 0)
        {
            AddSeparator();
            foreach (var warning in info.SpecialWarnings)
                AddLabel(warning, WarningColor, 13);
        }

        if (info.AdviceLine != null)
        {
            if (info.SpecialWarnings.Count == 0) AddSeparator();
            AddLabel(info.AdviceLine, AdviceColor, 13);
        }

        Visible = true;
    }

    /// <summary>Render item stat lines and show the panel.</summary>
    public void ShowItem(ItemInspectView info)
    {
        if (_content == null) return;
        ClearContent();

        AddLabel(info.Name, HeaderColor, 18, bold: true);
        AddLabel(info.Category, CategoryColor, 13);

        if (info.StatLines.Count > 0)
        {
            AddSeparator();
            foreach (var line in info.StatLines)
                AddLabel(line, StatColor, 13);
        }

        Visible = true;
    }

    /// <summary>
    /// Render a world feature (prop, door, chest, trap, portal, etc.) and show the panel.
    /// Simpler than ShowMonster/ShowItem — just name and one-sentence description.
    /// </summary>
    public void ShowFeature(string displayName, string description, Vector2 nearPos)
    {
        if (_content == null) return;
        ClearContent();

        AddLabel(displayName, HeaderColor, 18, bold: true);

        if (!string.IsNullOrEmpty(description))
        {
            AddSeparator();
            AddLabel(description, StatColor, 13);
        }

        Visible = true;
    }

    /// <summary>Hide the panel.</summary>
    public new void Hide()
    {
        Visible = false;
    }

    /// <summary>
    /// Position the panel near a screen tap point.
    /// Offsets by TapOffset px right/up from the tap, then flips horizontally or
    /// vertically if the panel would overflow the viewport.
    /// Call after Show* so the panel's size is known.
    /// </summary>
    public void PositionNear(Vector2 screenPos, Vector2 viewportSize)
    {
        float x = screenPos.X + TapOffset;
        float y = screenPos.Y - TapOffset - PanelMinHeight;

        // Flip right → left if near right edge
        if (x + PanelWidth > viewportSize.X)
            x = screenPos.X - TapOffset - PanelWidth;

        // Flip up → down if near top edge
        if (y < 0)
            y = screenPos.Y + TapOffset;

        // Clamp to viewport bounds
        x = Mathf.Clamp(x, 0, viewportSize.X - PanelWidth);
        y = Mathf.Clamp(y, 0, viewportSize.Y - PanelMinHeight);

        Position = new Vector2(x, y);
    }

    // ── Layout helpers ─────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        _panel = new PanelContainer();

        var style = new StyleBoxFlat
        {
            BgColor = BackgroundColor,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft   = Padding,
            ContentMarginRight  = Padding,
            ContentMarginTop    = Padding,
            ContentMarginBottom = Padding,
        };
        _panel.AddThemeStyleboxOverride("panel", style);
        _panel.CustomMinimumSize = new Vector2(PanelWidth, PanelMinHeight);

        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 4);

        _panel.AddChild(_content);
        AddChild(_panel);
    }

    private void ClearContent()
    {
        if (_content == null) return;
        foreach (var child in _content.GetChildren())
            child.QueueFree();
    }

    private void AddLabel(string text, Color color, int fontSize, bool bold = false)
    {
        if (_content == null) return;

        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        _content.AddChild(label);
    }

    private void AddStat(string key, string value)
    {
        if (_content == null) return;

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 6);

        var keyLabel = new Label { Text = key + ":" };
        keyLabel.AddThemeColorOverride("font_color", TierColor);
        keyLabel.AddThemeFontSizeOverride("font_size", 13);
        hbox.AddChild(keyLabel);

        var valLabel = new Label { Text = value };
        valLabel.AddThemeColorOverride("font_color", StatColor);
        valLabel.AddThemeFontSizeOverride("font_size", 13);
        hbox.AddChild(valLabel);

        _content.AddChild(hbox);
    }

    private void AddSeparator()
    {
        if (_content == null) return;
        var sep = new HSeparator();
        sep.AddThemeColorOverride("color", new Color(0.35f, 0.35f, 0.45f, 1f));
        _content.AddChild(sep);
    }
}
