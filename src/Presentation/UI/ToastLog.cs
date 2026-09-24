using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using Godot;

using UnderWarden.Presentation;
using System.Globalization;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Semantic category for a toast message. Controls the left-border accent color.
/// </summary>
public enum ToastCategory
{
    /// <summary>Kills, heals, buffs, identification, item pickups.</summary>
    Positive,
    /// <summary>Damage taken by player, debuffs applied to player, player death.</summary>
    Danger,
    /// <summary>Misses, status expiry, generic messages, monster-only events.</summary>
    Neutral,
}

/// <summary>
/// Floating toast messages overlaid on the dungeon. Each combat event
/// appears as a line of text that fades out after a few seconds.
///
/// Messages stack vertically (newest at bottom), slide upward as new
/// messages push them, and are removed once fully transparent.
///
/// No persistent background — the dungeon shows through.
/// </summary>
public sealed partial class ToastLog : Control
{
    private const float DisplayDuration = 2.5f;  // seconds before fade begins
    private const float FadeDuration    = 0.8f;  // seconds to fade to zero
    private const int   MaxToasts       = 5;
    private const int   FontSize        = 24;
    private const int   HistorySize     = 20;   // messages to keep for MessageLogPanel recall

    // Layout insets, all relative to the ToastLog node's own rect — never to a window size.
    // The node is anchored full-width in Main.tscn and vertically bounded to the clear strip
    // between the StatusBar and the QuickSlotBar, so these are the only numbers needed.
    private const int   SideMargin      = 8;    // left/right inset from the node edges
    private const int   BottomMargin    = 10;   // gap above the QuickSlotBar boundary
    private const int   StackHeight     = 500;  // tall enough for MaxToasts wrapped messages

    private int _playerId;
    private VBoxContainer? _stack;
    // No shared style — each toast creates its own StyleBoxFlat so the left-border color
    // can vary per message. At MaxToasts=5 visible at once, the GC cost is negligible.
    private FontFile? _monoFont;

    // Timer-based cleanup: avoids Callable.From() leak in Godot 4 C#.
    // Each toast records its creation time; _Process culls expired toasts.
    private readonly List<(RichTextLabel Label, double ExpireTime, double FadeEndTime)> _activeToasts = new();

    // Circular message history for recall. Plain strings (no nodes).
    private readonly List<string> _history = new();

    public override void _Ready()
    {
        BuildLayout();
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        for (int i = _activeToasts.Count - 1; i >= 0; i--)
        {
            var (label, expireTime, fadeEndTime) = _activeToasts[i];

            if (now >= fadeEndTime)
            {
                // Fade complete — remove immediately (no Callable.From needed).
                label.SafeFree();
                _activeToasts.RemoveAt(i);
            }
            else if (now >= expireTime)
            {
                // In fade phase — lerp alpha from 1 to 0.
                float t = (float)((now - expireTime) / FadeDuration);
                var m = label.Modulate;
                label.Modulate = new Color(m.R, m.G, m.B, 1f - t);
            }
        }
    }

    public void SetPlayerId(int playerId) => _playerId = playerId;

    /// <summary>Number of currently-visible toasts. Useful for debug overlay.</summary>
    public int ToastCount => _activeToasts.Count;

    /// <summary>
    /// Read-only view of the message history, oldest-first. Each entry is the raw BBCode
    /// string that was displayed as a toast. Used by MessageLogPanel to populate the log.
    /// </summary>
    public IReadOnlyList<string> History => _history;

    /// <summary>Show an arbitrary message as a toast (e.g. auto-explore stop reason).</summary>
    public void AddMessage(string text) => SpawnToast(text, ToastCategory.Neutral);

    /// <summary>Add messages from a completed turn's events.</summary>
    public void RecordTurn(TurnResult result, GameState state)
    {
        foreach (var evt in result.Events)
        {
            var (msg, category) = FormatEvent(evt, state);
            if (msg != null)
                SpawnToast(msg, category);
        }
    }

    // -------------------------------------------------------------------------

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        var font = GD.Load<FontFile>("res://src/Presentation/assets/fonts/PixeloidMono.ttf");
        font.Antialiasing = TextServer.FontAntialiasing.None;
        font.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
        font.Hinting = TextServer.Hinting.None;
        _monoFont = font;

        _stack = new VBoxContainer
        {
            // Stack messages from the bottom up, spanning the usable width.
            Alignment           = BoxContainer.AlignmentMode.End,
            SizeFlagsHorizontal = SizeFlags.Fill,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        _stack.AddThemeConstantOverride("separation", 2);

        // Bottom-WIDE, not bottom-left. The stack used to be pinned to a hardcoded 320px
        // (CustomMinimumSize plus OffsetRight=328), which on the project's 720px viewport
        // is 44% of the screen — long messages wrapped after a handful of words with two
        // thirds of the width empty beside them. Anchoring to both edges and insetting by
        // SideMargin means the wrap measure now follows the viewport instead of a number
        // typed against one.
        //
        // Vertical bounds come from the tscn node (offset_top=90, offset_bottom=-333), so
        // OffsetBottom=-10 sits 10px above the QuickSlotBar boundary with no chrome height
        // repeated here.
        _stack.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _stack.SetOffset(Side.Bottom, -BottomMargin);
        _stack.SetOffset(Side.Left,    SideMargin);
        _stack.SetOffset(Side.Right,  -SideMargin);
        _stack.SetOffset(Side.Top,    -StackHeight);
        AddChild(_stack);
    }

    private void SpawnToast(string bbcode, ToastCategory category, float displayDuration = DisplayDuration)
    {
        if (_stack == null) return;

        // Record in history (strip bbcode tags for plain text history).
        _history.Add(bbcode);
        if (_history.Count > HistorySize)
            _history.RemoveAt(0);

        // Enforce max visible toasts — remove oldest if over limit.
        while (_stack.GetChildCount() >= MaxToasts)
        {
            var oldest = _stack.GetChild(0);
            oldest.SafeFree();
            _activeToasts.RemoveAll(t => t.Label == oldest);
        }

        var label = new RichTextLabel
        {
            BbcodeEnabled       = true,
            FitContent          = true,
            // Word-wrap within the available width so long messages don't run off-screen.
            AutowrapMode        = TextServer.AutowrapMode.Word,
            ScrollActive        = false,
            MouseFilter         = MouseFilterEnum.Ignore,
            // Fill width so wrapping has room to work; stack container constrains the max width.
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        if (_monoFont != null) label.AddThemeFontOverride("normal_font", _monoFont);
        label.AddThemeFontSizeOverride("normal_font_size", FontSize);
        label.AddThemeColorOverride("default_color", Colors.White);
        label.AppendText(bbcode);

        // Each toast gets its own StyleBoxFlat so the left-border color can differ per category.
        // The left border provides a quick visual read: green = good, red = bad, gray = neutral.
        Color borderColor = category switch
        {
            ToastCategory.Positive => new Color(0.3f, 0.9f, 0.3f),
            ToastCategory.Danger   => new Color(0.9f, 0.3f, 0.3f),
            _                      => new Color(0.5f, 0.5f, 0.5f),
        };
        var style = new StyleBoxFlat
        {
            BgColor                 = new Color(0f, 0f, 0f, 0.55f),
            CornerRadiusTopLeft     = 4,
            CornerRadiusTopRight    = 4,
            CornerRadiusBottomLeft  = 4,
            CornerRadiusBottomRight = 4,
            // Left margin is wider to accommodate the 3px border stripe without crowding text.
            ContentMarginLeft       = 11f,
            ContentMarginRight      = 8f,
            ContentMarginTop        = 2f,
            ContentMarginBottom     = 2f,
            BorderWidthLeft         = 3,
            BorderColor             = borderColor,
        };
        label.AddThemeStyleboxOverride("normal", style);

        _stack.AddChild(label);

        double now = Time.GetTicksMsec() / 1000.0;
        _activeToasts.Add((label, now + displayDuration, now + displayDuration + FadeDuration));
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Formats an event into a BBCode string and classifies it as Positive, Danger, or Neutral.
    /// Returns (null, Neutral) for events that should produce no toast.
    /// </summary>
    private (string? text, ToastCategory category) FormatEvent(TurnEvent evt, GameState state)
    {
        bool isPlayer = evt.ActorId == _playerId;
        string actorName = isPlayer ? "You" : GetEntityName(evt.ActorId, state);
        string targetName(int id) => id == _playerId ? "you" : GetEntityName(id, state).ToLower();

        return evt switch
        {
            // Misses are neutral regardless of who is attacking.
            AttackEvent { Hit: false } atk =>
                ($"[color=gray]{actorName} miss{(isPlayer ? "" : "es")} {targetName(atk.TargetId)}.[/color]",
                 ToastCategory.Neutral),

            // Critical hit by player = very positive; by monster on player = danger.
            AttackEvent { IsCritical: true } atk when isPlayer =>
                ($"[color=yellow]CRIT! {actorName} hit{(isPlayer ? "" : "s")} {targetName(atk.TargetId)} for {atk.Damage}![/color]",
                 ToastCategory.Positive),

            AttackEvent { IsCritical: true } atk =>
                ($"[color=yellow]CRIT! {actorName} hit{(isPlayer ? "" : "s")} {targetName(atk.TargetId)} for {atk.Damage}![/color]",
                 atk.TargetId == _playerId ? ToastCategory.Danger : ToastCategory.Neutral),

            // Bonus attacks: positive when player lands them, danger when monster lands them on player.
            AttackEvent { IsBonusAttack: true } atk =>
                ($"[color=cyan]+{atk.Damage} bonus hit on {targetName(atk.TargetId)}[/color]",
                 isPlayer ? ToastCategory.Positive : (atk.TargetId == _playerId ? ToastCategory.Danger : ToastCategory.Neutral)),

            // Normal hit: danger if monster hits player, positive if player hits monster.
            AttackEvent atk =>
                ($"{actorName} hit{(isPlayer ? "" : "s")} {targetName(atk.TargetId)} for [color=white]{atk.Damage}[/color].",
                 isPlayer ? ToastCategory.Positive : (atk.TargetId == _playerId ? ToastCategory.Danger : ToastCategory.Neutral)),

            HealEvent heal =>
                ($"[color=lime]+{heal.AmountHealed} HP[/color] from {heal.ItemName}.",
                 ToastCategory.Positive),

            // Player death is the clearest danger signal.
            DeathEvent death when death.ActorId == _playerId =>
                ("[color=red]You die...[/color]",
                 ToastCategory.Danger),

            // Monster death is a positive outcome.
            DeathEvent =>
                ($"[color=green]{GetEntityName(evt.ActorId, state)} dies![/color]",
                 ToastCategory.Positive),

            SplitEvent split when split.ChildIds.Count > 0 =>
                ($"[color=yellow]The {GetEntityName(split.OriginalId, state)} splits into " +
                 $"{split.ChildIds.Count} {GetEntityName(split.ChildIds[0], state).ToLower()}s![/color]",
                 ToastCategory.Neutral),

            SplitEvent split =>
                ($"[color=yellow]The {GetEntityName(split.OriginalId, state)} splits![/color]",
                 ToastCategory.Neutral),

            // Pickup by player = positive; by monster = neutral.
            PickUpEvent pickup when isPlayer =>
                ($"You pick up {pickup.ItemName}.",
                 ToastCategory.Positive),

            PickUpEvent pickup =>
                ($"{actorName} picks up {pickup.ItemName}.",
                 ToastCategory.Neutral),

            // The player's own item use, announced from the use seam. Plain white and
            // Neutral - it is a statement of what you did, not an outcome, so it reads in
            // the same voice as an ordinary hit line rather than competing with the effect
            // message that follows it.
            PlayerItemUseEvent use =>
                ($"You {use.Verb} the {use.ItemName}.",
                 ToastCategory.Neutral),

            ItemUseEvent { Success: true } use =>
                ($"[color=yellow]{actorName} uses {use.ItemName}![/color]",
                 isPlayer ? ToastCategory.Positive : ToastCategory.Neutral),

            ItemUseEvent { FailureMode: "fizzle" } use =>
                ($"[color=gray]{actorName} tries {use.ItemName}... it fizzles.[/color]",
                 ToastCategory.Neutral),

            ItemUseEvent { FailureMode: "wrong_target" } use =>
                ($"[color=lime]{actorName} fumbles {use.ItemName} — it backfires![/color]",
                 isPlayer ? ToastCategory.Danger : ToastCategory.Neutral),

            ItemUseEvent { FailureMode: "equipment_damage" } use =>
                ($"[color=orange]{actorName} mishandles {use.ItemName} — weapon damaged![/color]",
                 isPlayer ? ToastCategory.Danger : ToastCategory.Neutral),

            // ── Status effect events ─────────────────────────────────────────────────
            // StatusAppliedEvent has no IsNegative flag; in the current design all statuses
            // applied to the player are debuffs (poison, slow, confusion, etc.) so we treat
            // them as Danger. Revisit if positive player buffs are added.
            StatusAppliedEvent statusApplied when statusApplied.TargetId == _playerId =>
                ($"[color=orange]You are {statusApplied.EffectName}![/color]",
                 ToastCategory.Danger),

            StatusAppliedEvent statusApplied =>
                ($"[color=orange]The {GetEntityName(statusApplied.TargetId, state).ToLower()} is {statusApplied.EffectName}![/color]",
                 ToastCategory.Neutral),

            // Expire: player regaining normal status is neutral (it was already bad, now it's over).
            StatusExpiredEvent expired when expired.EntityId == _playerId && expired.Reason == "duration" =>
                ($"[color=gray]The {expired.EffectName} fades.[/color]",
                 ToastCategory.Neutral),

            StatusExpiredEvent expired when expired.EntityId == _playerId =>
                ($"[color=gray]You are no longer {expired.EffectName}.[/color]",
                 ToastCategory.Neutral),

            StatusExpiredEvent expired when expired.Reason == "duration" =>
                ($"[color=gray]The {GetEntityName(expired.EntityId, state).ToLower()} is no longer {expired.EffectName}.[/color]",
                 ToastCategory.Neutral),

            StatusExpiredEvent expired =>
                ($"[color=gray]{GetEntityName(expired.EntityId, state)} is no longer {expired.EffectName}.[/color]",
                 ToastCategory.Neutral),

            // DOT damage on player = danger; on monster = neutral background info.
            DotDamageEvent dot when dot.EntityId == _playerId =>
                ($"[color=orange]{Capitalize(dot.EffectName)} deals {dot.Damage} damage.[/color]",
                 ToastCategory.Danger),

            DotDamageEvent dot =>
                ($"[color=orange]{Capitalize(dot.EffectName)} damages the {GetEntityName(dot.EntityId, state).ToLower()}.[/color]",
                 ToastCategory.Neutral),

            // HOT healing on player = positive; on monster = neutral.
            HotHealEvent hot when hot.EntityId == _playerId =>
                ($"[color=lime]+{hot.Amount} HP from {hot.EffectName}.[/color]",
                 ToastCategory.Positive),

            HotHealEvent hot =>
                ($"[color=green]{GetEntityName(hot.EntityId, state)} regenerates {hot.Amount} HP.[/color]",
                 ToastCategory.Neutral),

            // Skip-turn on player = danger; on monster = neutral.
            SkipTurnEvent skip when skip.EntityId == _playerId =>
                ($"[color=yellow]You are {skip.EffectName}![/color]",
                 ToastCategory.Danger),

            SkipTurnEvent skip =>
                ($"[color=gray]The {GetEntityName(skip.EntityId, state).ToLower()} is {skip.EffectName}![/color]",
                 ToastCategory.Neutral),

            // ── Identification events ─────────────────────────────────────────────────
            IdentificationEvent ident when ident.Trigger == "scroll_of_identify" =>
                ($"[color=aqua]{ident.IdentifiedName} identified![/color]",
                 ToastCategory.Positive),

            IdentificationEvent ident =>
                ($"[color=aqua]You realize this was a {ident.IdentifiedName}![/color]",
                 ToastCategory.Positive),

            _ => (null, ToastCategory.Neutral),
        };
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s :
        char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..];

    private static string GetEntityName(int id, GameState state)
    {
        if (state.Player.Id == id) return "Player";
        return state.Monsters.FirstOrDefault(m => m.Id == id)?.Name ?? "Unknown";
    }
}
