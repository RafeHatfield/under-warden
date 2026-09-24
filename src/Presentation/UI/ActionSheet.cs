using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;
using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Contextual action sheet shown when the player long-presses an inventory or equipment slot.
/// Shows item name + stats header and a vertical list of action buttons.
/// Dismisses on action tap or tap outside the sheet.
///
/// Actions by item type:
///   Healing / buff potions: Drink / Throw / Drop
///   Debuff potions (ThrowSpellId set): Drink / Throw / Drop
///   Scrolls: Use / Throw / Drop
///   Wands: Use / Throw / Drop
///   Weapon (unequipped): Equip / Throw / Drop
///   Weapon (equipped): Unequip / Throw / Drop
///   Armor / Ring (unequipped): Equip / Throw / Drop
///   Armor / Ring (equipped): Unequip / Throw / Drop
///
/// No .tscn — built entirely in C# following EquipmentPanel / InventoryPanel patterns.
/// </summary>
public sealed partial class ActionSheet : Control
{
    private const int ButtonHeight    = 56;   // min 48px touch target + padding
    private const int SheetWidth      = 300;
    private const int HeaderHeight    = 60;
    private const int ButtonSeparation = 6;
    private const int MarginH         = 20;
    private const int MarginV         = 16;

    private static readonly Color BackdropColor  = new(0f, 0f, 0f, 0.55f);
    private static readonly Color SheetBgColor   = new(0.07f, 0.07f, 0.12f, 0.97f);
    private static readonly Color HeaderBgColor  = new(0.10f, 0.10f, 0.18f, 1.0f);
    private static readonly Color ButtonColor    = new(0.15f, 0.15f, 0.22f, 1.0f);
    private static readonly Color ButtonHoverColor = new(0.22f, 0.22f, 0.32f, 1.0f);
    private static readonly Color ItemNameColor  = new(1.0f, 0.90f, 0.50f, 1.0f);
    private static readonly Color StatsColor     = new(0.70f, 0.70f, 0.80f, 1.0f);
    private static readonly Color ActionColor    = new(0.92f, 0.92f, 1.00f, 1.0f);

    // ─── Events ───────────────────────────────────────────────────────────────

    /// <summary>Fired when the player taps an action button.</summary>
    public event Action<int, ActionSheetAction>? ActionSelected;

    // ─── State ────────────────────────────────────────────────────────────────

    private Control? _sheet;
    private int _activeItemId = -1;

    /// <summary>Hit-test rects for action buttons — built each Show() call.</summary>
    private readonly List<(ActionSheetAction Action, Rect2 Rect)> _buttonRects = new();

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        // Full-viewport overlay — stops all input when visible
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Show the action sheet for the given item.
    /// isEquipped: true when the item is currently in an equipment slot (shows Unequip instead of Equip).
    /// </summary>
    public void Show(Entity item, bool isEquipped)
    {
        _activeItemId = item.Id;
        var actions = DetermineActions(item, isEquipped);
        BuildSheet(item, actions);
        Visible = true;
    }

    public void Dismiss()
    {
        Visible = false;
        _activeItemId = -1;
        _buttonRects.Clear();
    }

    // ─── Input ────────────────────────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (!Visible) return;

        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            AcceptEvent();
            var pos = mb.Position;

            // Check action buttons
            foreach (var (action, rect) in _buttonRects)
            {
                if (rect.HasPoint(pos))
                {
                    int itemId = _activeItemId;
                    Dismiss();
                    ActionSelected?.Invoke(itemId, action);
                    return;
                }
            }

            // Tap outside sheet — dismiss without action
            bool insideSheet = _sheet != null &&
                new Rect2(_sheet.GlobalPosition, _sheet.Size).HasPoint(pos);
            if (!insideSheet)
                Dismiss();
        }
    }

    // ─── Action determination ──────────────────────────────────────────────────

    /// <summary>Return the ordered list of actions for this item type.</summary>
    private static List<ActionSheetAction> DetermineActions(Entity item, bool isEquipped)
    {
        var actions = new List<ActionSheetAction>();

        var consumable = item.Get<Consumable>();
        var spellEffect = item.Get<SpellEffect>();
        var equippable = item.Get<Equippable>();

        if (consumable?.IsPotion == true)
        {
            // All potions: Drink / Throw / Drop
            actions.Add(ActionSheetAction.Use);
            actions.Add(ActionSheetAction.Throw);
            actions.Add(ActionSheetAction.Drop);
        }
        else if (consumable != null || (spellEffect != null && equippable == null))
        {
            // Scrolls, wands: Use / Throw / Drop
            actions.Add(ActionSheetAction.Use);
            actions.Add(ActionSheetAction.Throw);
            actions.Add(ActionSheetAction.Drop);
        }
        else if (equippable != null)
        {
            // Weapons, armor, rings
            if (isEquipped)
                actions.Add(ActionSheetAction.Unequip);
            else
                actions.Add(ActionSheetAction.Equip);
            actions.Add(ActionSheetAction.Throw);
            actions.Add(ActionSheetAction.Drop);
        }
        else
        {
            // Unknown item type — generic junk path
            actions.Add(ActionSheetAction.Throw);
            actions.Add(ActionSheetAction.Drop);
        }

        return actions;
    }

    // ─── Layout construction ──────────────────────────────────────────────────

    private void BuildSheet(Entity item, List<ActionSheetAction> actions)
    {
        // Clear previous sheet
        foreach (var child in GetChildren())
            child.SafeFree();
        _buttonRects.Clear();

        // Semi-transparent full-screen backdrop (click-through to _GuiInput for dismissal)
        var backdrop = new ColorRect
        {
            Color = BackdropColor,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        // Calculate sheet height: header + buttons + margins
        int totalButtons = actions.Count;
        int sheetHeight = HeaderHeight + MarginV
            + totalButtons * ButtonHeight
            + (totalButtons - 1) * ButtonSeparation
            + MarginV;

        // Centered sheet panel
        _sheet = new Control
        {
            AnchorLeft   = 0.5f, AnchorRight  = 0.5f,
            AnchorTop    = 0.5f, AnchorBottom  = 0.5f,
            OffsetLeft   = -(SheetWidth / 2f),
            OffsetRight  =  (SheetWidth / 2f),
            OffsetTop    = -(sheetHeight / 2f),
            OffsetBottom =  (sheetHeight / 2f),
            MouseFilter  = MouseFilterEnum.Ignore,
        };
        AddChild(_sheet);

        // Background
        var sheetBg = new ColorRect { Color = SheetBgColor };
        sheetBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        sheetBg.MouseFilter = MouseFilterEnum.Ignore;
        _sheet.AddChild(sheetBg);

        // Header: item name + stats
        var header = new ColorRect
        {
            Color = HeaderBgColor,
            Position = new Vector2(0, 0),
            Size = new Vector2(SheetWidth, HeaderHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _sheet.AddChild(header);

        var nameLabel = new Label
        {
            Text = item.Name,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Position = new Vector2(MarginH, 4),
            Size = new Vector2(SheetWidth - MarginH * 2, 28),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        nameLabel.AddThemeColorOverride("font_color", ItemNameColor);
        header.AddChild(nameLabel);

        string stats = BuildStatsLine(item);
        if (!string.IsNullOrEmpty(stats))
        {
            var statsLabel = new Label
            {
                Text = stats,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Position = new Vector2(MarginH, 32),
                Size = new Vector2(SheetWidth - MarginH * 2, 22),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            statsLabel.AddThemeColorOverride("font_color", StatsColor);
            header.AddChild(statsLabel);
        }

        // Action buttons — use deferred position tracking since the sheet isn't in the tree yet
        // Store positions relative to sheet; _ComputeButtonRects is called after AddChild
        _pendingButtonData = (actions, sheetHeight);
        CallDeferred(MethodName._ComputeButtonRects);
    }

    private (List<ActionSheetAction> actions, int sheetHeight) _pendingButtonData;

    private void _ComputeButtonRects()
    {
        var (actions, _) = _pendingButtonData;
        if (_sheet == null) return;

        _buttonRects.Clear();

        int y = HeaderHeight + MarginV;
        for (int i = 0; i < actions.Count; i++)
        {
            var action = actions[i];

            // Button background rect (relative to sheet)
            var btnBg = new ColorRect
            {
                Color = ButtonColor,
                Position = new Vector2(MarginH, y),
                Size = new Vector2(SheetWidth - MarginH * 2, ButtonHeight),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _sheet.AddChild(btnBg);

            // Button label
            var btn = new Label
            {
                Text = ActionLabel(action),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Position = new Vector2(MarginH, y),
                Size = new Vector2(SheetWidth - MarginH * 2, ButtonHeight),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            btn.AddThemeColorOverride("font_color", ActionColor);
            _sheet.AddChild(btn);

            // Track global rect for hit-testing
            // Sheet position is relative to this control's origin; we need global coords.
            // Since _sheet uses anchors, GlobalPosition resolves after it's in the tree.
            var sheetGlobal = _sheet.GlobalPosition;
            var buttonRect = new Rect2(
                sheetGlobal.X + MarginH,
                sheetGlobal.Y + y,
                SheetWidth - MarginH * 2,
                ButtonHeight);
            _buttonRects.Add((action, buttonRect));

            y += ButtonHeight + ButtonSeparation;
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string BuildStatsLine(Entity item)
    {
        var equippable = item.Get<Equippable>();
        if (equippable != null)
        {
            if (equippable.IsWeapon)
                return $"Dmg {equippable.DamageMin}–{equippable.DamageMax}";
            if (equippable.ArmorClassBonus > 0)
                return $"AC +{equippable.ArmorClassBonus}";
        }

        var consumable = item.Get<Consumable>();
        if (consumable?.IsPotion == true && consumable.HealAmount > 0)
            return $"Heals {consumable.HealAmount} HP";

        return "";
    }

    private static string ActionLabel(ActionSheetAction action) => action switch
    {
        ActionSheetAction.Use     => "Drink",
        ActionSheetAction.Throw   => "Throw",
        ActionSheetAction.Drop    => "Drop",
        ActionSheetAction.Equip   => "Equip",
        ActionSheetAction.Unequip => "Unequip",
        _ => action.ToString(),
    };
}

/// <summary>Actions available on the action sheet.</summary>
public enum ActionSheetAction
{
    Use,
    Throw,
    Drop,
    Equip,
    Unequip,
}
