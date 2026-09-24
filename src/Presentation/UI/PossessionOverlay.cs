using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Dual HP bar widget shown during Active possession (§10 of plan_possession_system.md).
///
/// Layout:
///   SASHA (home)  ████████░░░░░  17/30   ← drained per turn, red tint when ≤25%
///   ORC  (host)   ██████████████  42/45  ← host HP, normal tint
///   Possession: 6 turns | Drain: 2/turn  ← contextual info line
///
/// Designed to replace the single HUD HP bar during possession. HUD.cs creates one
/// instance, shows it when ControlledEntity != Player, hides it otherwise.
/// Built entirely in code — no .tscn file.
/// </summary>
public sealed partial class PossessionOverlay : Control
{
    // Home body row
    private ColorRect? _homeBarBg;
    private ColorRect? _homeBarFill;
    private Label? _homeLabel;
    private Label? _homeHpLabel;

    // Host row
    private ColorRect? _hostBarBg;
    private ColorRect? _hostBarFill;
    private Label? _hostLabel;
    private Label? _hostHpLabel;

    // Info line
    private Label? _infoLabel;

    private static readonly Color HomeDrainedColor = new(0.7f, 0.1f, 0.1f, 1f);  // red when ≤25%
    private static readonly Color HomeHealthyColor  = new(0.2f, 0.4f, 0.8f, 1f);  // blue tint
    private static readonly Color HostBarColor      = new(0.2f, 0.7f, 0.2f, 1f);  // green
    private static readonly Color TrackColor        = new(0.1f, 0.1f, 0.1f, 0.9f);

    public override void _Ready()
    {
        BuildLayout();
    }

    /// <summary>
    /// Refresh both bars and info line from current GameState.
    /// Call after every turn while possession is active.
    /// </summary>
    public void Refresh(GameState state)
    {
        var homeFighter = state.PlayerFighter;
        var host = state.ControlledEntity;
        var hostFighter = host.Get<Fighter>();
        var effect = FindPossessionEffect(state);

        RefreshBar(_homeBarBg, _homeBarFill, homeFighter?.Hp ?? 0, homeFighter?.MaxHp ?? 1,
            HpColor(homeFighter?.Hp ?? 0, homeFighter?.MaxHp ?? 1, isHome: true));

        if (_homeHpLabel != null && homeFighter != null)
            _homeHpLabel.Text = $"{homeFighter.Hp}/{homeFighter.MaxHp}";

        if (_hostLabel != null)
            _hostLabel.Text = host.Name.ToUpper();

        RefreshBar(_hostBarBg, _hostBarFill, hostFighter?.Hp ?? 0, hostFighter?.MaxHp ?? 1,
            HpColor(hostFighter?.Hp ?? 0, hostFighter?.MaxHp ?? 1, isHome: false));

        if (_hostHpLabel != null && hostFighter != null)
            _hostHpLabel.Text = $"{hostFighter.Hp}/{hostFighter.MaxHp}";

        if (_infoLabel != null && effect != null)
        {
            int turnsHeld = state.TurnCount - effect.EnteredTurn;
            _infoLabel.Text = $"Possession: {turnsHeld} turns | Drain: {effect.DrainPerTurn}/turn";
        }
    }

    // ─── Layout ───────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var sans = LoadPixelFont("res://src/Presentation/assets/fonts/PixeloidSans.ttf");
        var bold = LoadPixelFont("res://src/Presentation/assets/fonts/PixeloidSans-Bold.ttf");

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 3);
        AddChild(vbox);

        (_homeBarBg, _homeBarFill, _homeLabel, _homeHpLabel) =
            BuildBarRow(vbox, "SASHA", HomeHealthyColor, bold, sans);

        (_hostBarBg, _hostBarFill, _hostLabel, _hostHpLabel) =
            BuildBarRow(vbox, "HOST", HostBarColor, bold, sans);

        _infoLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _infoLabel.AddThemeFontOverride("font", sans);
        _infoLabel.AddThemeFontSizeOverride("font_size", 13);
        _infoLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        vbox.AddChild(_infoLabel);
    }

    private static (ColorRect bg, ColorRect fill, Label nameLabel, Label hpLabel) BuildBarRow(
        VBoxContainer parent, string defaultName, Color fillColor, FontFile bold, FontFile sans)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        parent.AddChild(row);

        var nameLabel = new Label
        {
            Text = defaultName,
            CustomMinimumSize = new Vector2(60, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameLabel.AddThemeFontOverride("font", bold);
        nameLabel.AddThemeFontSizeOverride("font_size", 13);
        nameLabel.AddThemeColorOverride("font_color", Colors.White);
        row.AddChild(nameLabel);

        var barContainer = new Control();
        barContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        barContainer.CustomMinimumSize = new Vector2(0, 16);
        row.AddChild(barContainer);

        var bg = new ColorRect
        {
            Color = TrackColor,
            AnchorRight = 1f,
            AnchorBottom = 1f,
        };
        barContainer.AddChild(bg);

        var fill = new ColorRect
        {
            Color = fillColor,
            AnchorLeft   = 0f,
            AnchorTop    = 0f,
            AnchorRight  = 0f,
            AnchorBottom = 1f,
            Size = new Vector2(200f, 0f),
        };
        barContainer.AddChild(fill);

        var hpLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorRight  = 1f,
            AnchorBottom = 1f,
        };
        hpLabel.AddThemeFontOverride("font", sans);
        hpLabel.AddThemeFontSizeOverride("font_size", 12);
        hpLabel.AddThemeColorOverride("font_color", Colors.White);
        hpLabel.MouseFilter = MouseFilterEnum.Ignore;
        barContainer.AddChild(hpLabel);

        return (bg, fill, nameLabel, hpLabel);
    }

    private static void RefreshBar(ColorRect? bg, ColorRect? fill, int hp, int maxHp, Color color)
    {
        if (fill == null || bg == null) return;
        float availableWidth = bg.Size.X > 0 ? bg.Size.X : bg.CustomMinimumSize.X;
        float frac = maxHp > 0 ? Math.Clamp((float)hp / maxHp, 0f, 1f) : 0f;
        fill.Size = new Vector2(availableWidth * frac, fill.Size.Y);
        fill.Color = color;
    }

    private static Color HpColor(int hp, int maxHp, bool isHome)
    {
        if (!isHome) return HostBarColor;
        float frac = maxHp > 0 ? (float)hp / maxHp : 0f;
        return frac <= 0.25f ? HomeDrainedColor : HomeHealthyColor;
    }

    private static Logic.Combat.StatusEffects.PossessionEffect? FindPossessionEffect(GameState state)
    {
        foreach (var m in state.Monsters)
        {
            var eff = m.Get<Logic.Combat.StatusEffects.PossessionEffect>();
            if (eff?.Source == Logic.Combat.StatusEffects.PossessionSource.PlayerInitiated
                && eff.PossessorEntityId == state.Player.Id)
                return eff;
        }
        return null;
    }

    private static FontFile LoadPixelFont(string path)
    {
        var font = GD.Load<FontFile>(path);
        font.Antialiasing = TextServer.FontAntialiasing.None;
        font.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
        font.Hinting = TextServer.Hinting.None;
        return font;
    }
}
