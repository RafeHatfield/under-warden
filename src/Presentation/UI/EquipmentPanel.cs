using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Equipment panel overlay. Shows all 8 equipment slots in a body-map layout
/// and a horizontal "In Pack" strip of equippable inventory items.
///
/// Layout (portrait, 720px wide):
///
///   [Head]
///   [LeftRing] [Neck] [RightRing]
///   [OffHand]  [Chest] [MainHand]
///   [Feet]
///   ─────────────────────────────
///   IN PACK
///   [item] [item] [item] …
///
/// Tapping an occupied equipment slot → UnequipRequested event.
/// Tapping an In Pack item → EquipRequested event.
/// The [✕] button is the only close mechanism (full-screen: no "outside" exists).
///
/// Manual hit-testing (same approach as InventoryPanel) bypasses Godot Button
/// click-rect offset bug under integer stretch scale mode.
/// </summary>
public sealed partial class EquipmentPanel : Control
{
    private const int SlotSize       = 90;  // px — each equipment slot is a square
    private const int SlotLabelH     = 20;  // px — slot label below each slot
    private const int PackColumns    = 6;   // columns in the In Pack grid
    private const int PackSlotGap    = 6;   // px — gap between pack slots
    private int _packSlotSize;              // computed from viewport width in BuildLayout

    private static readonly Color SlotBgEmpty    = new(0.12f, 0.12f, 0.18f, 0.9f);
    private static readonly Color SlotBgOccupied = new(0.18f, 0.16f, 0.10f, 0.95f);
    private static readonly Color SlotHighlight  = new(0.85f, 0.70f, 0.15f, 0.50f);
    private static readonly Color PackSlotBg     = new(0.12f, 0.12f, 0.18f, 0.9f);
    private static readonly Color FallbackWeapon = new(0.9f, 0.75f, 0.1f, 1f);
    private static readonly Color FallbackArmor  = new(0.2f, 0.4f, 0.9f, 1f);
    private static readonly Color FallbackRing   = new(0.7f, 0.3f, 0.8f, 1f);
    private static readonly Color FallbackDefault= new(0.5f, 0.5f, 0.5f, 1f);

    /// <summary>
    /// Injected by Main after construction. Used for item sprite lookups.
    /// Must be set before Show/Refresh is called.
    /// </summary>
    public SpriteMapping? SpriteMappingInstance { get; set; }

    // ── Long-press detection constants ────────────────────────────────────────
    private const float LongPressThreshold = 0.4f;
    private const float DragCancelDistance = 24f;

    // Long-press state — tracks the pressed slot or pack item
    // Slot long-press uses SlotId = (int)slot; pack item uses SlotId = -1 and ItemId = id
    private bool  _pressIsEquippedSlot = false;
    private EquipmentSlot _pressedSlot = EquipmentSlot.MainHand;
    private int   _pressedPackItemId = -1;
    private Vector2 _pressPosition  = Vector2.Zero;
    private float _pressHeldTime    = 0f;
    private bool  _longPressFired   = false;
    private bool  _pressing         = false;

    /// <summary>Fires when the player taps an occupied equipment slot to unequip it.</summary>
    public event Action<EquipmentSlot>? UnequipRequested;

    /// <summary>Fires when the player taps an In Pack item to equip it.</summary>
    public event Action<int>? EquipRequested;

    /// <summary>Fires when the player taps the drop button on an In Pack item.</summary>
    public event Action<int>? ItemDropRequested;

    /// <summary>
    /// Fires when the player long-presses an occupied equipment slot.
    /// Parameters: (slot, itemId of the equipped item).
    /// </summary>
    public event Action<EquipmentSlot, int>? EquippedItemLongPressed;

    /// <summary>
    /// Fires when the player long-presses an In Pack item slot.
    /// Parameter: itemId.
    /// </summary>
    public event Action<int>? PackItemLongPressed;

    // Hit-test tracking — (slot/itemId, localRect relative to this panel).
    private readonly List<(EquipmentSlot Slot, Rect2 Rect)> _equippedRects  = new();
    private readonly List<(int ItemId, Rect2 Rect)>         _packRects      = new();
    private readonly List<(int ItemId, Rect2 Rect)>         _packDropRects  = new();

    // Stats label — rebuilt each Refresh.
    private Label? _statsLabel;

    // Slot row containers — rebuilt each Refresh.
    private Control? _slotGrid;
    private Control? _packStrip;

    // Keep the last refreshed state so long-press handlers can look up slot contents.
    private GameState? _currentState;

    public override void _Ready()
    {
        BuildLayout();
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
    }

    /// <summary>Accumulate hold time and fire long-press events when threshold reached.</summary>
    public override void _Process(double delta)
    {
        if (!_pressing || _longPressFired) return;

        _pressHeldTime += (float)delta;
        if (_pressHeldTime >= LongPressThreshold)
        {
            _longPressFired = true;
            _pressing = false;

            if (_pressIsEquippedSlot && _currentState != null)
            {
                // Find the item in the pressed slot
                var equipment = _currentState.Player.Get<Equipment>();
                var item = equipment?.GetSlot(_pressedSlot);
                if (item != null)
                    EquippedItemLongPressed?.Invoke(_pressedSlot, item.Id);
            }
            else if (_pressedPackItemId >= 0)
            {
                PackItemLongPressed?.Invoke(_pressedPackItemId);
            }
        }
    }

    public void Show(GameState state)
    {
        Visible = true;
        Refresh(state);
    }

    public void Refresh(GameState state)
    {
        _currentState = state;
        if (!IsInsideTree() || !Visible) return;
        RefreshStats(state);
        RebuildSlotGrid(state);
        RebuildPackStrip(state);
        CallDeferred(MethodName._ComputeHitRects);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Input — manual hit-test against tracked rects
    // ─────────────────────────────────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            // Cancel long-press on drag
            if (_pressing && !_longPressFired)
            {
                if (_pressPosition.DistanceTo(motion.Position) > DragCancelDistance)
                    CancelEquipLongPress();
            }
            return;
        }

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            var pos = mb.Position;

            if (mb.Pressed)
            {
                // Drop buttons always fire immediately — no long-press.
                foreach (var (itemId, rect) in _packDropRects)
                {
                    if (rect.HasPoint(pos))
                    {
                        AcceptEvent();
                        ItemDropRequested?.Invoke(itemId);
                        return;
                    }
                }

                // Equipped slots — start long-press timer
                foreach (var (slot, rect) in _equippedRects)
                {
                    if (rect.HasPoint(pos))
                    {
                        AcceptEvent();
                        _pressing = true;
                        _pressIsEquippedSlot = true;
                        _pressedSlot = slot;
                        _pressedPackItemId = -1;
                        _pressPosition = pos;
                        _pressHeldTime = 0f;
                        _longPressFired = false;
                        return;
                    }
                }

                // Pack items — start long-press timer
                foreach (var (itemId, rect) in _packRects)
                {
                    if (rect.HasPoint(pos))
                    {
                        AcceptEvent();
                        _pressing = true;
                        _pressIsEquippedSlot = false;
                        _pressedPackItemId = itemId;
                        _pressPosition = pos;
                        _pressHeldTime = 0f;
                        _longPressFired = false;
                        return;
                    }
                }

                // Click landed outside all interactive slots but still inside the full-screen
                // panel. With a full-screen layout there is no "outside the panel", so we no
                // longer auto-dismiss on background taps. The X button is the only close path.
                AcceptEvent();
            }
            else
            {
                // Mouse released — if timer hasn't fired, treat as a normal tap
                if (_pressing && !_longPressFired)
                {
                    bool wasEquippedSlot = _pressIsEquippedSlot;
                    var slot = _pressedSlot;
                    int packItemId = _pressedPackItemId;
                    CancelEquipLongPress();

                    AcceptEvent();
                    if (wasEquippedSlot)
                        UnequipRequested?.Invoke(slot);
                    else if (packItemId >= 0)
                        EquipRequested?.Invoke(packItemId);
                }
                else
                {
                    CancelEquipLongPress();
                }
            }
        }
    }

    private void CancelEquipLongPress()
    {
        _pressing = false;
        _pressHeldTime = 0f;
        _longPressFired = false;
        _pressedPackItemId = -1;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Layout construction (once in _Ready)
    // ─────────────────────────────────────────────────────────────────────────

    private Control? _panelContainer;

    private void BuildLayout()
    {
        // Full-viewport backdrop — semi-transparent black, eats all clicks.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var backdrop = new ColorRect
        {
            Color       = new Color(0f, 0f, 0f, 0.60f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        // Full-screen panel container — fills the entire screen with 16px insets on all
        // sides so content never touches the screen edges.
        // MouseFilter=Ignore so all clicks bubble up to EquipmentPanel._GuiInput,
        // where the unified hit-test runs against tracked slot rects.
        _panelContainer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _panelContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panelContainer.OffsetLeft   =  16f;
        _panelContainer.OffsetRight  = -16f;
        _panelContainer.OffsetTop    =  16f;
        _panelContainer.OffsetBottom = -16f;
        AddChild(_panelContainer);

        // Panel background.
        var panelBg = new ColorRect { Color = new Color(0.07f, 0.07f, 0.12f, 0.97f) };
        panelBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        panelBg.MouseFilter = MouseFilterEnum.Ignore;
        _panelContainer.AddChild(panelBg);

        // Inner VBox for all panel content.
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left",   16);
        margin.AddThemeConstantOverride("margin_right",  16);
        margin.AddThemeConstantOverride("margin_top",    12);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.MouseFilter = MouseFilterEnum.Ignore;
        _panelContainer.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        vbox.MouseFilter = MouseFilterEnum.Ignore;
        margin.AddChild(vbox);

        // Header row: title + close button.
        var headerRow = new HBoxContainer();
        headerRow.MouseFilter = MouseFilterEnum.Ignore;
        vbox.AddChild(headerRow);

        var titleLabel = new Label
        {
            Text                = "EQUIPMENT",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 26);
        titleLabel.AddThemeColorOverride("font_color", Colors.White);
        headerRow.AddChild(titleLabel);

        var closeBtn = new TouchButton
        {
            Text              = "✕",
            FontSize          = 24,
            BackgroundColor   = new Color(0.4f, 0.1f, 0.1f, 0.9f),
            CustomMinimumSize = new Vector2(44, 44),
        };
        closeBtn.Pressed += Hide;
        headerRow.AddChild(closeBtn);

        // Stats line — rebuilt each Refresh.
        _statsLabel = new Label
        {
            Text        = "",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _statsLabel.AddThemeFontSizeOverride("font_size", 16);
        _statsLabel.AddThemeColorOverride("font_color", new Color(0.80f, 0.85f, 0.90f, 1f));
        vbox.AddChild(_statsLabel);

        // Slot grid placeholder — rebuilt each Refresh.
        _slotGrid = new Control
        {
            CustomMinimumSize   = new Vector2(0, SlotSize * 4 + SlotLabelH * 4 + 8 * 3),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        vbox.AddChild(_slotGrid);

        // Divider.
        var divider = new ColorRect
        {
            Color             = new Color(0.4f, 0.4f, 0.5f, 0.6f),
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter       = MouseFilterEnum.Ignore,
        };
        vbox.AddChild(divider);

        // In Pack label.
        var packLabel = new Label
        {
            Text        = "IN PACK",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        packLabel.AddThemeFontSizeOverride("font_size", 18);
        packLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f, 1f));
        vbox.AddChild(packLabel);

        // Pack grid: fixed columns, no scroll. Slot size computed from available viewport width.
        var viewportWidth = GetViewport().GetVisibleRect().Size.X;
        const float panelPadding = 64f; // 16+16 panelContainer insets + 16+16 inner margin
        _packSlotSize = Mathf.FloorToInt((viewportWidth - panelPadding - (PackColumns - 1) * PackSlotGap) / PackColumns);

        var packGrid = new GridContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical   = SizeFlags.ShrinkBegin,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        packGrid.Columns = PackColumns;
        packGrid.AddThemeConstantOverride("h_separation", PackSlotGap);
        packGrid.AddThemeConstantOverride("v_separation", PackSlotGap);
        vbox.AddChild(packGrid);

        // _packStrip is the GridContainer; RebuildPackStrip adds/clears slot children directly.
        _packStrip = packGrid;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Slot grid — rebuilt each Refresh()
    // ─────────────────────────────────────────────────────────────────────────

    private void RebuildSlotGrid(GameState state)
    {
        if (_slotGrid == null) return;

        foreach (var child in _slotGrid.GetChildren())
            child.SafeFree();

        var equipment = state.Player.Get<Equipment>();

        // Body-map rows, each as an HBoxContainer.
        // Rows are stacked in a VBox positioned to fill _slotGrid.
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        vbox.MouseFilter = MouseFilterEnum.Ignore;
        _slotGrid.AddChild(vbox);

        // Row 1: Head (center)
        vbox.AddChild(MakeSingleSlotRow(EquipmentSlot.Head, "Head", equipment));

        // Row 2: RightRing | Neck | LeftRing  (person faces us — their right is our left)
        vbox.AddChild(MakeTripleSlotRow(
            EquipmentSlot.RightRing, "R. Ring",
            EquipmentSlot.Neck,      "Neck",
            EquipmentSlot.LeftRing,  "L. Ring",
            equipment));

        // Row 3: MainHand | Chest | OffHand  (person faces us — their right hand is our left)
        vbox.AddChild(MakeTripleSlotRow(
            EquipmentSlot.MainHand, "Main Hand",
            EquipmentSlot.Chest,    "Chest",
            EquipmentSlot.OffHand,  "Off Hand",
            equipment));

        // Row 4: Feet (center)
        vbox.AddChild(MakeSingleSlotRow(EquipmentSlot.Feet, "Feet", equipment));
    }

    /// <summary>A row with one slot centered using expand-fill spacers.</summary>
    private Control MakeSingleSlotRow(EquipmentSlot slot, string label, Equipment? equipment)
    {
        var row = new HBoxContainer();
        row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.MouseFilter = MouseFilterEnum.Ignore;

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(BuildEquipSlot(slot, label, equipment));
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        return row;
    }

    /// <summary>
    /// A row with three slots distributed evenly using expand-fill spacers between them.
    /// Four equal spacers ensure the outer slots are inset symmetrically and the
    /// center slot lands exactly at the horizontal midpoint of the panel.
    /// </summary>
    private Control MakeTripleSlotRow(
        EquipmentSlot slotL, string labelL,
        EquipmentSlot slotC, string labelC,
        EquipmentSlot slotR, string labelR,
        Equipment? equipment)
    {
        var row = new HBoxContainer();
        row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.MouseFilter = MouseFilterEnum.Ignore;

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(BuildEquipSlot(slotL, labelL, equipment));
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(BuildEquipSlot(slotC, labelC, equipment));
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(BuildEquipSlot(slotR, labelR, equipment));
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        return row;
    }

    private Control BuildEquipSlot(EquipmentSlot slot, string slotLabel, Equipment? equipment)
    {
        var item = equipment?.GetSlot(slot);
        bool occupied = item != null;

        var container = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(SlotSize, SlotSize + SlotLabelH),
            MouseFilter       = MouseFilterEnum.Ignore,
        };
        container.AddThemeConstantOverride("separation", 2);
        // Store slot as metadata for hit-rect association.
        container.SetMeta("equip_slot", (int)slot);

        // Slot box.
        var slotBox = new Control
        {
            CustomMinimumSize = new Vector2(SlotSize, SlotSize),
            MouseFilter       = MouseFilterEnum.Ignore,
        };

        var bg = new ColorRect
        {
            Color       = occupied ? SlotBgOccupied : SlotBgEmpty,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        slotBox.AddChild(bg);

        if (occupied)
        {
            // Gold highlight border for occupied slots.
            var highlight = new ColorRect
            {
                Color             = SlotHighlight,
                CustomMinimumSize = new Vector2(0, 0),
                MouseFilter       = MouseFilterEnum.Ignore,
            };
            highlight.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            highlight.OffsetLeft = 2; highlight.OffsetTop = 2;
            highlight.OffsetRight = -2; highlight.OffsetBottom = -2;
            slotBox.AddChild(highlight);

            // Item icon.
            var icon = BuildItemIcon(item!, SlotSize - 8);
            icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            icon.OffsetLeft = 4; icon.OffsetTop = 4;
            icon.OffsetRight = -4; icon.OffsetBottom = -4;
            icon.MouseFilter = MouseFilterEnum.Ignore;
            slotBox.AddChild(icon);
        }
        else
        {
            // Empty slot: show slot name as placeholder.
            var emptyLabel = new Label
            {
                Text                = slotLabel,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                MouseFilter         = MouseFilterEnum.Ignore,
            };
            emptyLabel.AddThemeFontSizeOverride("font_size", 13);
            emptyLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.5f, 1f));
            emptyLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            slotBox.AddChild(emptyLabel);
        }

        container.AddChild(slotBox);

        // Item name below slot (occupied) or slot label (empty but smaller).
        var nameLabel = new Label
        {
            Text                = occupied ? TruncateName(item!.Name, 10) : "",
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize   = new Vector2(SlotSize, SlotLabelH),
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        nameLabel.AddThemeFontSizeOverride("font_size", 13);
        nameLabel.AddThemeColorOverride("font_color",
            occupied ? new Color(1f, 0.92f, 0.65f, 1f) : new Color(0.4f, 0.4f, 0.5f, 1f));
        container.AddChild(nameLabel);

        return container;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // In Pack strip — rebuilt each Refresh()
    // ─────────────────────────────────────────────────────────────────────────

    private void RebuildPackStrip(GameState state)
    {
        if (_packStrip == null) return;

        foreach (var child in _packStrip.GetChildren())
            child.SafeFree();

        var inventory = state.PlayerInventory;
        var equipment = state.Player.Get<Equipment>();

        // Collect equipped item IDs so we can exclude them from the pack display.
        var equippedIds = new HashSet<int>();
        if (equipment != null)
            foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
            {
                var e = equipment.GetSlot(slot);
                if (e != null) equippedIds.Add(e.Id);
            }

        // Only show equippable items that are in inventory (not currently equipped).
        var equippables = inventory?.Items
            .Where(item => item.Get<Equippable>() != null && !equippedIds.Contains(item.Id))
            .ToList() ?? new List<Entity>();

        if (equippables.Count == 0)
        {
            var emptyLabel = new Label
            {
                Text        = "(none)",
                MouseFilter = MouseFilterEnum.Ignore,
            };
            emptyLabel.AddThemeFontSizeOverride("font_size", 16);
            emptyLabel.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.5f, 1f));
            _packStrip.AddChild(emptyLabel);
            return;
        }

        foreach (var item in equippables)
        {
            var slot = BuildPackSlot(item);
            _packStrip.AddChild(slot);
        }
    }

    private Control BuildPackSlot(Entity item)
    {
        var container = new Control
        {
            CustomMinimumSize = new Vector2(_packSlotSize, _packSlotSize),
            MouseFilter       = MouseFilterEnum.Ignore,
        };
        container.SetMeta("pack_item_id", item.Id);

        var bg = new ColorRect { Color = PackSlotBg, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        container.AddChild(bg);

        var icon = BuildItemIcon(item, _packSlotSize - 8);
        icon.OffsetLeft = 4; icon.OffsetTop = 4;
        icon.OffsetRight = -4; icon.OffsetBottom = -4;
        icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        icon.MouseFilter = MouseFilterEnum.Ignore;
        container.AddChild(icon);

        // Item name tooltip-style label at bottom.
        var nameLabel = new Label
        {
            Text                = TruncateName(item.Name, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Bottom,
            MouseFilter         = MouseFilterEnum.Ignore,
            AnchorLeft   = 0f, AnchorTop    = 0f,
            AnchorRight  = 1f, AnchorBottom = 1f,
            OffsetRight  = -2f, OffsetBottom = -2f,
        };
        nameLabel.AddThemeFontSizeOverride("font_size", 11);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f, 1f));
        container.AddChild(nameLabel);

        // Drop button: "×" in top-right corner
        var dropBtn = new Label
        {
            Text                = "×",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Top,
            AnchorLeft   = 0f, AnchorTop    = 0f,
            AnchorRight  = 1f, AnchorBottom = 0f,
            OffsetRight  = -1f, OffsetTop   = 1f, OffsetBottom = 22f,
            MouseFilter  = MouseFilterEnum.Ignore,
        };
        dropBtn.AddThemeFontSizeOverride("font_size", 16);
        dropBtn.SelfModulate = new Color(0.9f, 0.4f, 0.4f, 0.85f);
        container.AddChild(dropBtn);

        return container;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Hit rect computation (deferred — called after layout resolves)
    // ─────────────────────────────────────────────────────────────────────────

    private void _ComputeHitRects()
    {
        _equippedRects.Clear();
        _packRects.Clear();
        _packDropRects.Clear();

        var panelOrigin = GetGlobalRect().Position;

        // Walk slot grid: find Control nodes with "equip_slot" metadata.
        if (_slotGrid != null)
            WalkForEquipSlots(_slotGrid, panelOrigin);

        // Walk pack strip: find Control nodes with "pack_item_id" metadata.
        if (_packStrip != null)
            WalkForPackItems(_packStrip, panelOrigin);
    }

    private void WalkForEquipSlots(Node node, Vector2 panelOrigin)
    {
        if (node is Control c && c.HasMeta("equip_slot"))
        {
            var slot = (EquipmentSlot)(int)c.GetMeta("equip_slot");
            // Only add to hit list if the slot is occupied (the equip_slot container is
            // always present, but we only want clicks on occupied slots to fire Unequip).
            // We determine "occupied" by checking if the slotBox child has an item icon.
            // Simpler: the EquipRequested/UnequipRequested logic in _GuiInput guards.
            // Include all slots in hit rects; TurnController's ResolveUnequip guards empty slots.
            var globalRect = c.GetGlobalRect();
            var localRect = new Rect2(globalRect.Position - panelOrigin, globalRect.Size);
            _equippedRects.Add((slot, localRect));
        }

        foreach (var child in node.GetChildren())
            WalkForEquipSlots(child, panelOrigin);
    }

    private void WalkForPackItems(Node node, Vector2 panelOrigin)
    {
        if (node is Control c && c.HasMeta("pack_item_id"))
        {
            int itemId = (int)c.GetMeta("pack_item_id");
            var globalRect = c.GetGlobalRect();
            var localRect = new Rect2(globalRect.Position - panelOrigin, globalRect.Size);
            _packRects.Add((itemId, localRect));
            // Drop button: top-right 22×22 of the slot
            var dropRect = new Rect2(localRect.Position + new Vector2(localRect.Size.X - 22, 0), new Vector2(22, 22));
            _packDropRects.Add((itemId, dropRect));
            return; // Don't descend into pack slot children.
        }

        foreach (var child in node.GetChildren())
            WalkForPackItems(child, panelOrigin);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Stats line — refreshed each Refresh()
    // ─────────────────────────────────────────────────────────────────────────

    private void RefreshStats(GameState state)
    {
        if (_statsLabel == null) return;
        _statsLabel.Text = ComputeStatsLine(state);
    }

    private static string ComputeStatsLine(GameState state)
    {
        var fighter = state.PlayerFighter;
        var equip = state.Player.Get<Equipment>();
        var weapon = equip?.MainHand?.Get<Equippable>();

        int strMod = fighter.StrengthMod;
        int dexMod = fighter.DexterityMod;
        int toHitBonus = dexMod + (equip?.TotalToHitBonus ?? 0);
        int ac = fighter.BaseArmorClass + (equip?.TotalArmorClassBonus ?? 0);
        int hp = fighter.Hp;
        int maxHp = fighter.MaxHp;

        string atk;
        if (weapon != null && weapon.IsWeapon)
            atk = $"{weapon.DamageMin + strMod}–{weapon.DamageMax + strMod}";
        else
            atk = $"{fighter.DamageMin + strMod}–{fighter.DamageMax + strMod}";

        string hit = toHitBonus >= 0 ? $"+{toHitBonus}" : $"{toHitBonus}";

        var line1 = $"HP {hp}/{maxHp}   ATK {atk}   HIT {hit}   AC {ac}";

        var speed = state.Player.Get<SpeedBonusTracker>();
        if (speed == null || speed.SpeedBonusRatio <= 0)
            return line1;

        int pct       = (int)Math.Round(speed.SpeedBonusRatio * 100);
        int threshold = Mathf.CeilToInt((float)(1.0 / speed.SpeedBonusRatio));
        int counter   = speed.AttackCounter;

        string sources = "";
        if (speed.BaseRatio > 0 && (speed.EquipmentRatio > 0 || speed.RingRatio > 0))
        {
            var parts = new List<string>();
            if (speed.EquipmentRatio > 0) parts.Add($"equip +{(int)Math.Round(speed.EquipmentRatio * 100)}%");
            if (speed.RingRatio > 0)      parts.Add($"ring +{(int)Math.Round(speed.RingRatio * 100)}%");
            sources = $" ({string.Join(", ", parts)})";
        }
        else if (speed.BaseRatio <= 0)
        {
            var parts = new List<string>();
            if (speed.EquipmentRatio > 0) parts.Add($"equip");
            if (speed.RingRatio > 0)      parts.Add($"ring");
            sources = parts.Count > 0 ? $" ({string.Join(" + ", parts)})" : "";
        }

        return $"{line1}\nSPD +{pct}%{sources}   [{counter}/{threshold}]";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private Control BuildItemIcon(Entity item, int size)
    {
        // Primary: ItemTag.TypeId (set by ItemFactory for all YAML-created items)
        var tag = item.Get<ItemTag>();
        var spriteKey = tag?.TypeId ?? item.Name.ToLowerInvariant().Replace(' ', '_');
        var spritePath = SpriteMappingInstance?.GetItemSpritePath(spriteKey);

        if (spritePath != null)
        {
            var texture = GD.Load<Texture2D>(spritePath);
            if (texture != null)
            {
                return new TextureRect
                {
                    Texture             = texture,
                    StretchMode         = TextureRect.StretchModeEnum.KeepAspectCentered,
                    CustomMinimumSize   = new Vector2(size, size),
                    SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                };
            }
        }

        return new ColorRect
        {
            Color             = FallbackEquipColor(item),
            CustomMinimumSize = new Vector2(size, size),
        };
    }

    private static Color FallbackEquipColor(Entity item)
    {
        var eq = item.Get<Equippable>();
        if (eq == null) return FallbackDefault;
        return eq.Slot switch
        {
            EquipmentSlot.LeftRing or EquipmentSlot.RightRing or EquipmentSlot.Neck => FallbackRing,
            _ => eq.IsWeapon ? FallbackWeapon : FallbackArmor,
        };
    }

    private static string TruncateName(string name, int maxLen) =>
        name.Length <= maxLen ? name : name[..maxLen] + "…";
}
