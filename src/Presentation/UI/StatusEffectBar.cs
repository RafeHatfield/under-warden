using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.ECS;
using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Displays active status effects as badges in the HUD.
/// Each badge shows the effect short name and remaining turns: [Poison 7], [Shield 4].
///
/// Color coding:
///   Debuffs (poison, burning, slowed, disarmed, blinded, sleep, weakness, entangled,
///            disoriented, silenced, immobilized, fear): red/orange
///   Buffs   (shield, protection, barkskin, regeneration, speed, focused, invisible): green/blue
///   Neutral (sluggish, aggravated): gray
///
/// Updated from the player entity via Refresh(). Clears when no effects are active.
/// </summary>
public sealed partial class StatusEffectBar : Control
{
    private HBoxContainer? _badgeRow;

    // Badge colors by category
    private static readonly Color DebuffColor  = new(1f, 0.4f, 0.4f);   // red/orange
    private static readonly Color BuffColor    = new(0.4f, 1f, 0.6f);   // green
    private static readonly Color NeutralColor = new(0.7f, 0.7f, 0.7f); // gray

    // Short display names for each effect — keyed on EffectName property.
    private static readonly Dictionary<string, string> ShortNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["poison"]     = "Psn",
        ["burning"]    = "Burn",
        ["plague"]     = "Plg",
        ["slowed"]     = "Slw",
        ["immobilized"] = "Imm",
        ["sleep"]      = "Slp",
        ["disarmed"]   = "Dis",
        ["blinded"]    = "Bld",
        ["weakness"]   = "Wk",
        ["entangled"]  = "Ent",
        ["disoriented"] = "Cnf",
        ["silenced"]   = "Sil",
        ["fear"]       = "Fea",
        ["shield"]     = "Shd",
        ["protection"] = "Pro",
        ["barkskin"]   = "Brk",
        ["regeneration"] = "Reg",
        ["speed"]      = "Spd",
        ["focused"]    = "Foc",
        ["invisibility"] = "Inv",
        ["sluggish"]   = "Slg",
        ["enraged"]    = "Rge",
        ["taunted"]    = "Tnt",
        ["aggravated"] = "Agg",
    };

    // Debuff effect names (used for color assignment).
    private static readonly HashSet<string> DebuffNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "poison", "burning", "plague", "slowed", "immobilized", "sleep",
        "disarmed", "blinded", "weakness", "entangled", "disoriented",
        "silenced", "fear", "enraged",
    };

    // Buff effect names (everything not in debuff or neutral is treated as neutral fallback).
    private static readonly HashSet<string> BuffNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "shield", "protection", "barkskin", "regeneration", "speed", "focused", "invisibility",
        "heroism",
    };

    // Skip-turn effects are the most severe — shown first when badge count is capped.
    // These completely deny the player their turn.
    private static readonly HashSet<string> SkipTurnNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "immobilized", "sleep", "slowed",
    };

    // DOT effects deal damage each turn — second-highest severity.
    private static readonly HashSet<string> DotNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "poison", "burning", "plague",
    };

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _badgeRow = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
        };
        _badgeRow.AddThemeConstantOverride("separation", 4);
        AddChild(_badgeRow);
    }

    /// <summary>
    /// Refresh the badge row from the player entity's current status effects.
    /// Call this from HUD.Refresh() after every turn.
    /// </summary>
    public void Refresh(Entity? player)
    {
        if (_badgeRow == null) return;

        // Clear existing badges.
        foreach (Node child in _badgeRow.GetChildren())
            child.SafeFree();

        if (player == null) return;

        // Collect all active effects.
        var effects = player.GetAllComponents().OfType<IStatusEffect>().ToList();
        if (effects.Count == 0) return;

        // Sort by severity so that the most dangerous effects survive the 5-badge cap.
        // Priority: skip-turn (0) > DOT (1) > other debuff (2) > buff (3) > neutral (4).
        // Within each tier: alphabetical by effect name.
        effects.Sort((a, b) =>
        {
            int sevA = Severity(a.EffectName);
            int sevB = Severity(b.EffectName);
            if (sevA != sevB) return sevA.CompareTo(sevB);
            return string.Compare(a.EffectName, b.EffectName, StringComparison.OrdinalIgnoreCase);
        });

        // Cap at 5 badges. Priority order: skip-turn > DOT > debuff > buff > neutral.
        // If more than 5 effects are active, show the highest-priority 5 and an overflow indicator.
        const int MaxBadges = 5;
        int overflow = effects.Count - MaxBadges;

        var visible = overflow > 0 ? effects.Take(MaxBadges).ToList() : effects;
        foreach (var effect in visible)
        {
            var badge = BuildBadge(effect);
            _badgeRow.AddChild(badge);
        }

        if (overflow > 0)
        {
            // Show how many effects are hidden so the player knows something is off-screen.
            var overflowLabel = new Label
            {
                Text        = $"+{overflow}",
                MouseFilter = MouseFilterEnum.Ignore,
            };
            overflowLabel.AddThemeFontSizeOverride("font_size", 18);
            overflowLabel.AddThemeColorOverride("font_color", NeutralColor);
            _badgeRow.AddChild(overflowLabel);
        }
    }

    private Control BuildBadge(IStatusEffect effect)
    {
        string shortName = ShortNames.TryGetValue(effect.EffectName, out var sn) ? sn : effect.EffectName[..3].ToUpper();
        string text = effect.IsPermanent ? shortName : $"{shortName} {effect.RemainingTurns}";
        Color color = EffectColor(effect.EffectName);

        var label = new Label
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_color", color);

        // Minimal background.
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor               = new Color(0.05f, 0.05f, 0.1f, 0.7f),
            CornerRadiusTopLeft   = 3,
            CornerRadiusTopRight  = 3,
            CornerRadiusBottomLeft  = 3,
            CornerRadiusBottomRight = 3,
            ContentMarginLeft   = 5f,
            ContentMarginRight  = 5f,
            ContentMarginTop    = 1f,
            ContentMarginBottom = 1f,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        panel.AddChild(label);

        return panel;
    }

    private static Color EffectColor(string effectName) =>
        DebuffNames.Contains(effectName) ? DebuffColor :
        BuffNames.Contains(effectName)  ? BuffColor :
        NeutralColor;

    /// <summary>
    /// Severity score for badge priority when count exceeds MaxBadges.
    /// Lower = shown first (more severe).
    ///   0 = skip-turn (immobilized, sleep, slowed)
    ///   1 = DOT (poison, burning, plague)
    ///   2 = other debuff
    ///   3 = buff
    ///   4 = neutral
    /// </summary>
    private static int Severity(string effectName)
    {
        if (SkipTurnNames.Contains(effectName)) return 0;
        if (DotNames.Contains(effectName))      return 1;
        if (DebuffNames.Contains(effectName))   return 2;
        if (BuffNames.Contains(effectName))     return 3;
        return 4;
    }
}
