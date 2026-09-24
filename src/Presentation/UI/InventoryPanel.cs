using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using Godot;


namespace UnderWarden.Presentation.UI;

/// <summary>
/// Inventory strip panel. Shows the player's current inventory as a horizontal
/// row of item slots, each with an icon and stack count badge.
/// Equipped items get a gold highlight border.
///
/// Click handling bypasses Godot Button hit-testing entirely — the integer
/// stretch scale mode causes Button click rects to be offset from their
/// visual position. Instead, InventoryPanel handles _GuiInput directly and
/// hit-tests against tracked slot rects.
///
/// No .tscn file — built entirely in C# following the same pattern as HUD.cs.
/// </summary>
/// <remarks>
/// Superseded by <see cref="QuickSlotBar"/> (Phase 3 mobile layout overhaul).
/// Kept for reference during transition. Do not use in new code.
/// </remarks>
[Obsolete("Use QuickSlotBar instead. InventoryPanel is superseded by the Phase 3 mobile layout.")]
public sealed partial class InventoryPanel : Control
{
    private const int SlotWidth  = 52;
    private const int SlotHeight = 52;
    private const int IconSize   = 48;
    private const int SlotSeparation = 4;

    private static readonly Color FallbackConsumableColor = new(0.2f, 0.7f, 0.2f, 1f);
    private static readonly Color FallbackSpellItemColor  = new(0.4f, 0.4f, 0.9f, 1f);
    private static readonly Color FallbackDefaultColor    = new(0.5f, 0.5f, 0.5f, 1f);

    /// <summary>
    /// Injected by Main after construction. Used for item sprite lookups.
    /// Must be set before Initialize is called.
    /// </summary>
    public SpriteMapping? SpriteMappingInstance { get; set; }

    private Label? _headerLabel;
    private HBoxContainer? _itemStrip;
    private Label? _emptyLabel;

    // Tracked slot positions for manual hit-testing (bypasses Godot Button coords bug).
    private readonly List<(int ItemId, Rect2 LocalRect)> _slotRects = new();
    private readonly List<(int ItemId, Rect2 LocalRect)> _dropRects = new();

    /// <summary>
    /// Exposes slot rects for the RectDebugDraw overlay. Panel-local coordinates.
    /// </summary>
    public IReadOnlyList<(int ItemId, Rect2 LocalRect)> SlotRects => _slotRects;

    // Cached per-refresh — used by BuildIcon for identification-aware sprite lookup.
    private IdentificationRegistry? _registry;
    private AppearancePool? _pool;

    // ── Long-press detection constants ────────────────────────────────────────
    private const float LongPressThreshold = 0.4f; // seconds
    private const float DragCancelDistance = 24f;  // pixels — cancel if moved more than this (24px tolerates mobile touch jitter)

    // ── Long-press state ──────────────────────────────────────────────────────
    private int   _pressedItemId    = -1;
    private Vector2 _pressPosition  = Vector2.Zero;
    private float _pressHeldTime    = 0f;
    private bool  _longPressFired   = false;

    /// <summary>
    /// Fires when the player taps an item slot. Argument is the item's entity ID.
    /// </summary>
    public event Action<int>? ItemTapped;

    /// <summary>Fires when the player taps the drop button on an item slot.</summary>
    public event Action<int>? ItemDropRequested;

    /// <summary>
    /// Fires when the player long-presses (0.4s) an item slot.
    /// GameController uses this to show the ActionSheet.
    /// </summary>
    public event Action<int>? ItemLongPressed;

    public override void _Ready()
    {
        BuildLayout();
        // Accept mouse input at the panel level for manual hit-testing.
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>
    /// Accumulate hold time for long-press detection. Fires ItemLongPressed when threshold reached.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_pressedItemId < 0 || _longPressFired) return;

        _pressHeldTime += (float)delta;
        if (_pressHeldTime >= LongPressThreshold)
        {
            _longPressFired = true;
            int itemId = _pressedItemId;
            _pressedItemId = -1; // prevent re-firing
            ItemLongPressed?.Invoke(itemId);
        }
    }

    /// <summary>
    /// Handle clicks directly — manual hit-test against slot rects.
    /// Godot's Button hit-testing is broken under integer stretch scale mode
    /// (click rect offset from visual rect). This bypasses it entirely.
    ///
    /// Long-press: mouse-down starts a 0.4s timer. If held: fire ItemLongPressed.
    ///             If released before threshold: fire ItemTapped.
    ///             If dragged (>8px): cancel both.
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            // Cancel long-press if the pointer moves too far from the initial press
            if (_pressedItemId >= 0 && !_longPressFired)
            {
                if (_pressPosition.DistanceTo(motion.Position) > DragCancelDistance)
                    CancelLongPress();
            }
            return;
        }

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            var localPos = mb.Position;

            if (mb.Pressed)
            {
                // Mouse button down — check drop rects first (they don't participate in long-press)
                foreach (var (itemId, rect) in _dropRects)
                {
                    if (rect.HasPoint(localPos))
                    {
                        AcceptEvent();
                        ItemDropRequested?.Invoke(itemId);
                        return;
                    }
                }

                // Check slot rects — start long-press timer
                foreach (var (itemId, rect) in _slotRects)
                {
                    if (rect.HasPoint(localPos))
                    {
                        AcceptEvent();
                        _pressedItemId  = itemId;
                        _pressPosition  = localPos;
                        _pressHeldTime  = 0f;
                        _longPressFired = false;
                        return;
                    }
                }

                // Click inside panel but no slot — consume
                AcceptEvent();
            }
            else
            {
                // Mouse button released — if timer hasn't fired yet, treat as a tap
                if (_pressedItemId >= 0 && !_longPressFired)
                {
                    int itemId = _pressedItemId;
                    CancelLongPress();
                    Diag.Log($"InventoryPanel hit-test: itemId={itemId} at {localPos}");
                    AcceptEvent();
                    ItemTapped?.Invoke(itemId);
                }
                else
                {
                    CancelLongPress();
                }
            }
        }
    }

    private void CancelLongPress()
    {
        _pressedItemId  = -1;
        _pressHeldTime  = 0f;
        _longPressFired = false;
    }

    public void Initialize(GameState state)
    {
        Refresh(state);
    }

    public void Refresh(GameState state)
    {
        _registry = state.IdentificationRegistry;
        _pool     = state.AppearancePool;
        var inventory = state.PlayerInventory;

        // Quick-bar shows consumables, scrolls, and wands — equippables live in the equipment panel.
        var quickBarItems = inventory?.Items
            .Where(item => item.Get<Consumable>() != null || item.Get<SpellEffect>() != null)
            .ToList() ?? new List<Entity>();

        if (_headerLabel != null)
            _headerLabel.Text = $"QUICK-BAR  {quickBarItems.Count}";

        if (_itemStrip == null) return;

        foreach (var child in _itemStrip.GetChildren())
            child.SafeFree();

        _slotRects.Clear();

        if (quickBarItems.Count == 0)
        {
            if (_emptyLabel != null)  _emptyLabel.Visible = true;
            if (_itemStrip != null)   _itemStrip.Visible  = false;
            return;
        }

        if (_emptyLabel != null) _emptyLabel.Visible = false;
        if (_itemStrip != null)  _itemStrip.Visible  = true;

        foreach (var item in quickBarItems)
        {
            var slot = BuildSlot(item, isEquipped: false);
            _itemStrip.AddChild(slot);
        }

        // Compute slot rects after layout resolves.
        CallDeferred(MethodName._ComputeSlotRects);
    }

    private void _ComputeSlotRects()
    {
        _slotRects.Clear();
        _dropRects.Clear();
        if (_itemStrip == null) return;

        var panelOrigin = GetGlobalRect().Position;

        foreach (var child in _itemStrip.GetChildren())
        {
            if (child is Control c && c.HasMeta("item_id"))
            {
                int itemId = (int)c.GetMeta("item_id");
                // Convert slot's global rect to panel-local coords for _GuiInput hit-testing.
                var globalRect = c.GetGlobalRect();
                var localRect = new Rect2(globalRect.Position - panelOrigin, globalRect.Size);
                _slotRects.Add((itemId, localRect));
                Diag.Log($"  slot itemId={itemId} localRect={localRect}");

                // Drop button: top-right 20×20 of the slot
                var dropRect = new Rect2(localRect.Position + new Vector2(localRect.Size.X - 20, 0), new Vector2(20, 20));
                _dropRects.Add((itemId, dropRect));
            }
        }
    }

    // -------------------------------------------------------------------------
    // Layout
    // -------------------------------------------------------------------------

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var bg = new ColorRect { Color = new Color(0f, 0f, 0f, 0.65f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(bg);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left",   12);
        margin.AddThemeConstantOverride("margin_right",  12);
        margin.AddThemeConstantOverride("margin_top",    6);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);
        vbox.MouseFilter = MouseFilterEnum.Ignore;
        margin.AddChild(vbox);

        var headerRow = new HBoxContainer();
        headerRow.MouseFilter = MouseFilterEnum.Ignore;
        vbox.AddChild(headerRow);

        _headerLabel = new Label
        {
            Text = "INVENTORY  0/25",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _headerLabel.AddThemeFontSizeOverride("font_size", 14);
        headerRow.AddChild(_headerLabel);

        _emptyLabel = new Label
        {
            Text    = "(empty)",
            Visible = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _emptyLabel.AddThemeFontSizeOverride("font_size", 14);
        _emptyLabel.SelfModulate = new Color(0.6f, 0.6f, 0.6f, 1f);
        vbox.AddChild(_emptyLabel);

        _itemStrip = new HBoxContainer
        {
            Visible             = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical   = SizeFlags.ExpandFill,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        _itemStrip.AddThemeConstantOverride("separation", SlotSeparation);
        vbox.AddChild(_itemStrip);
    }

    // -------------------------------------------------------------------------
    // Per-slot construction (visual only — no click handling on the slot itself)
    // -------------------------------------------------------------------------

    private Control BuildSlot(Entity item, bool isEquipped)
    {
        var slot = new Control
        {
            CustomMinimumSize   = new Vector2(SlotWidth, SlotHeight),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        // Store item ID as metadata so _ComputeSlotRects can read it back.
        slot.SetMeta("item_id", item.Id);

        var bg = new ColorRect { Color = new Color(0.15f, 0.15f, 0.2f, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        slot.AddChild(bg);

        var iconContainer = BuildIconWithBadge(item, isEquipped);
        slot.AddChild(iconContainer);

        var dropBtn = new Label
        {
            Text = "×",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            AnchorLeft   = 0f, AnchorTop    = 0f,
            AnchorRight  = 1f, AnchorBottom = 0f,
            OffsetRight  = -1f, OffsetTop   = 1f, OffsetBottom = 20f,
            MouseFilter  = MouseFilterEnum.Ignore,
        };
        dropBtn.AddThemeFontSizeOverride("font_size", 14);
        dropBtn.SelfModulate = new Color(0.9f, 0.4f, 0.4f, 0.8f);
        slot.AddChild(dropBtn);

        return slot;
    }

    private Control BuildIconWithBadge(Entity item, bool isEquipped)
    {
        var container = new Control
        {
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var icon = BuildIcon(item, isEquipped);
        icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        icon.MouseFilter = MouseFilterEnum.Ignore;
        container.AddChild(icon);

        var consumable = item.Get<Consumable>();
        var wand = item.Get<WandComponent>();
        string? badgeText = null;
        if (consumable != null)
            badgeText = $"{consumable.StackSize}";
        else if (wand != null)
            badgeText = wand.Infinite ? "∞" : $"{wand.Charges}";
        // Scrolls (SpellEffect without WandComponent) show no badge — single use, no count.

        if (badgeText != null)
        {
            var badge = new Label
            {
                Text                = badgeText,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Bottom,
                AnchorLeft   = 0f, AnchorTop    = 0f,
                AnchorRight  = 1f, AnchorBottom = 1f,
                OffsetRight  = -2f, OffsetBottom = -1f,
                MouseFilter  = MouseFilterEnum.Ignore,
            };
            badge.AddThemeFontSizeOverride("font_size", 16);
            badge.SelfModulate = Colors.White;
            container.AddChild(badge);
        }

        return container;
    }

    private Control BuildIcon(Entity item, bool isEquipped)
    {
        // Use ItemDisplay.GetSpriteKey so unidentified potions/scrolls/wands show their
        // mystery sprite (e.g. black bottle "36") rather than the true type ID.
        var spriteKey = ItemDisplay.GetSpriteKey(item, _registry, _pool);
        var spritePath = SpriteMappingInstance?.GetItemSpritePath(spriteKey);

        if (spritePath != null)
        {
            var texture = GD.Load<Texture2D>(spritePath);
            if (texture != null)
            {
                var texRect = new TextureRect
                {
                    Texture             = texture,
                    StretchMode         = TextureRect.StretchModeEnum.KeepAspectCentered,
                    CustomMinimumSize   = new Vector2(IconSize, IconSize),
                    SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                };
                if (isEquipped)
                    texRect.SelfModulate = new Color(1.0f, 0.9f, 0.4f, 1f);
                return texRect;
            }
        }

        return new ColorRect
        {
            Color             = FallbackColor(item),
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
    }

    private static Color FallbackColor(Entity item)
    {
        if (item.Get<Consumable>() != null) return FallbackConsumableColor;
        if (item.Get<SpellEffect>() != null) return FallbackSpellItemColor;
        return FallbackDefaultColor;
    }
}
