using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Scrolling combat log. Converts TurnResult events to readable text.
/// Shows the last N messages, newest at bottom.
/// </summary>
public sealed partial class CombatLog : Control
{
    private const int MaxMessages = 3;

    private RichTextLabel? _label;
    private readonly List<string> _messages = new();

    private int _playerId;

    public override void _Ready()
    {
        BuildLayout();
    }

    public void SetPlayerId(int playerId) => _playerId = playerId;

    /// <summary>Add messages from a completed turn's events.</summary>
    public void RecordTurn(TurnResult result, GameState state)
    {
        foreach (var evt in result.Events)
        {
            var msg = FormatEvent(evt, state);
            if (msg != null)
                AddMessage(msg);
        }
    }

    private void AddMessage(string msg)
    {
        _messages.Add(msg);
        while (_messages.Count > MaxMessages)
            _messages.RemoveAt(0);
        Redraw();
    }

    private void Redraw()
    {
        if (_label == null) return;

        _label.Clear();
        foreach (var msg in _messages)
            _label.AppendText(msg + "\n");
    }

    private string? FormatEvent(TurnEvent evt, GameState state)
    {
        bool isPlayer = evt.ActorId == _playerId;
        string actorName = isPlayer ? "You" : GetEntityName(evt.ActorId, state);
        string targetName(int id) => id == _playerId ? "you" : GetEntityName(id, state).ToLower();

        return evt switch
        {
            AttackEvent { Hit: false } atk =>
                $"[color=gray]{actorName} miss{(isPlayer ? "" : "es")} {targetName(atk.TargetId)}.[/color]",

            AttackEvent { IsCritical: true } atk =>
                $"[color=yellow]CRITICAL! {actorName} hit{(isPlayer ? "" : "s")} {targetName(atk.TargetId)} for {atk.Damage} damage![/color]",

            AttackEvent { IsBonusAttack: true } atk =>
                $"[color=cyan]Bonus attack! {actorName} hit{(isPlayer ? "" : "s")} {targetName(atk.TargetId)} for {atk.Damage}.[/color]",

            AttackEvent atk =>
                $"{actorName} hit{(isPlayer ? "" : "s")} {targetName(atk.TargetId)} for {atk.Damage} damage.",

            HealEvent heal =>
                $"[color=lime]{actorName} drink{(isPlayer ? "" : "s")} a potion. (+{heal.AmountHealed} HP)[/color]",

            DeathEvent death when death.ActorId == _playerId =>
                "[color=red]You die...[/color]",

            DeathEvent death =>
                $"[color=green]{GetEntityName(death.ActorId, state)} dies![/color]",

            _ => null,
        };
    }

    private static string GetEntityName(int id, GameState state)
    {
        if (state.Player.Id == id) return "Player";
        var monster = state.Monsters.FirstOrDefault(m => m.Id == id);
        return monster?.Name ?? "Unknown";
    }

    private void BuildLayout()
    {
        // Fill the container node defined in Main.tscn (200px BottomWide)
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var bg = new ColorRect { Color = new Color(0.05f, 0.05f, 0.1f, 0.80f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(margin);

        _label = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _label.AddThemeFontSizeOverride("normal_font_size", 13);
        margin.AddChild(_label);
    }
}
