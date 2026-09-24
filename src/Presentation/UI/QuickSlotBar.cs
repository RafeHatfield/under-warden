using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Consumable and wand quick-slot bar. Two-row grid with page-based scrolling.
///
/// Layout:
///   [WeaponSlot (spans both rows) | separator | GridContainer (2 rows × N cols)]
///
/// Weapon slot (left, fixed): shows current MainHand weapon icon + name. Spans both rows.
///   Tap  → WeaponTapped
///   Long-press → WeaponLongPressed
///
/// Item grid (right, pages): 2 rows of consumable/wand/scroll slots. Slot size computed
///   from available bar height. Columns computed from available width.
///   Tap  → ItemTapped(entityId)
///   Long-press → ItemLongPressed(entityId)
///   Swipe ≥50px left  → next page
///   Swipe ≥50px right → prev page
///
/// No .tscn file — built entirely in C#.
/// </summary>
public sealed partial class QuickSlotBar : Control
{
    // ── Fixed constants ───────────────────────────────────────────────────────
    private const int  SlotGap            = 4;    // px — gap between item slots
    private const int  RowGap             = 6;    // px — gap between the two rows
    private const int  SeparatorWidth     = 1;    // px — weapon/items divider
    private const int  BarMargin          = 8;    // px — outer margin on all sides
    private const int  CornerRadius       = 6;
    private const int  BarHeight          = 154;  // px — fixed by Main.tscn anchors

    private const float LongPressThreshold = 0.4f;
    private const float DragCancelDistance = 24f;
    private const float ScrollTapThreshold = 8f;  // px — below = tap, above = gesture
    private const float PageFlipThreshold  = 50f; // px — swipe needed to flip a page

    // ── Slot tint colors ─────────────────────────────────────────────────────
    private static readonly Color TintHealth  = new(0.6f,  0.15f, 0.15f, 0.7f);
    private static readonly Color TintArcane  = new(0.15f, 0.15f, 0.6f,  0.7f);
    private static readonly Color TintScroll  = new(0.5f,  0.4f,  0.1f,  0.7f);
    private static readonly Color TintWand    = new(0.4f,  0.1f,  0.5f,  0.7f);
    private static readonly Color TintDefault = new(0.15f, 0.15f, 0.2f,  0.6f);

    /// <summary>Icon tint for a slot the rules would currently refuse (cooldown, no charges).</summary>
    private static readonly Color UnavailableIconTint = new(0.45f, 0.45f, 0.45f, 0.55f);

    // ── Computed geometry (set in _Ready before BuildLayout) ──────────────────
    private int _slotSize;     // height of one item slot
    private int _weaponSlotW;  // width of weapon slot
    private int _weaponSlotH;  // height of weapon slot (= 2 rows)
    private int _slotsPerRow;  // columns per row in item grid

    // ── Public surface ────────────────────────────────────────────────────────

    /// <summary>Injected by Main after construction. Used for item sprite lookups.</summary>
    public SpriteMapping? SpriteMappingInstance { get; set; }

    /// <summary>Fires when the player taps an item slot. Argument is the item's entity ID.</summary>
    public event Action<int>? ItemTapped;

    /// <summary>Fires when the player long-presses (0.4s) an item slot.</summary>
    public event Action<int>? ItemLongPressed;

    /// <summary>Fires when the player taps the weapon indicator.</summary>
    public event Action? WeaponTapped;

    /// <summary>Fires when the player long-presses the weapon indicator.</summary>
    public event Action? WeaponLongPressed;

    // ── Slot rect surface ─────────────────────────────────────────────────────
    public IReadOnlyList<(int ItemId, Rect2 LocalRect)> SlotRects => _slotRects;

    /// <summary>
    /// Hit-tests a global (viewport-space) position against the item slots.
    /// Used by GameController's hover-to-inspect path (mouse hover, no button held) —
    /// separate from the press-and-hold ActionSheet flow in _GuiInput/_Process below.
    /// Returns the item's entity ID, or null if the position isn't over any slot.
    /// </summary>
    public int? GetItemIdAtGlobalPosition(Vector2 globalPos)
    {
        var localPos = globalPos - GetGlobalRect().Position;
        foreach (var (itemId, rect) in _slotRects)
        {
            if (rect.HasPoint(localPos))
                return itemId;
        }
        return null;
    }

    // ── Internal nodes ────────────────────────────────────────────────────────
    private Control?       _weaponSlot;
    private TextureRect?   _weaponIcon;
    private Label?         _weaponNameLabel;
    private GridContainer? _itemGrid;

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly List<(int ItemId, Rect2 LocalRect)> _slotRects = new();
    private Rect2 _weaponSlotRect = default(Rect2);

    private IdentificationRegistry? _registry;
    private AppearancePool?          _pool;

    /// <summary>
    /// Per-item availability for the current Refresh, keyed by entity ID. Built from
    /// QuickSlotModel so the greyed-out state mirrors the rules' own gates rather than
    /// re-deriving them here.
    /// </summary>
    private readonly Dictionary<int, QuickSlotEntry> _slotModel = new();

    // ── Paging state ──────────────────────────────────────────────────────────
    private List<Entity> _allItems  = new();
    private int _currentPage = 0;
    private int _pageCount   = 1;

    // ── Long-press state — ItemId >= 0 = item slot; -2 = weapon slot ─────────
    private int     _pressedId      = -1;
    private Vector2 _pressPosition  = Vector2.Zero;
    private float   _pressHeldTime  = 0f;
    private bool    _longPressFired = false;

    // ── Page-gesture state ────────────────────────────────────────────────────
    private bool _isPageGesture = false;

    private const int WeaponSlotSentinel = -2;

    public override void _Ready()
    {
        ComputeSlotGeometry();
        BuildLayout();
        MouseFilter = MouseFilterEnum.Stop;
    }

    private void ComputeSlotGeometry()
    {
        // Slot size from available bar height (two rows + one row gap, minus top/bottom margins).
        _slotSize    = (BarHeight - 2 * BarMargin - RowGap) / 2;  // = 66
        _weaponSlotH = 2 * _slotSize + RowGap;                    // = 138
        _weaponSlotW = _slotSize + 6;                              // = 72

        // Columns from available width after weapon slot and chrome.
        var viewportWidth = (int)GetViewport().GetVisibleRect().Size.X;
        int gridW = viewportWidth - 2 * BarMargin - _weaponSlotW - SeparatorWidth - SlotGap;
        _slotsPerRow = Mathf.Max(1, (gridW + SlotGap) / (_slotSize + SlotGap));
    }

    public override void _Process(double delta)
    {
        if (_pressedId == -1 || _longPressFired) return;

        _pressHeldTime += (float)delta;
        if (_pressHeldTime >= LongPressThreshold)
        {
            _longPressFired = true;
            int pressedId = _pressedId;
            _pressedId = -1;

            if (pressedId == WeaponSlotSentinel)
                WeaponLongPressed?.Invoke();
            else
                ItemLongPressed?.Invoke(pressedId);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventScreenTouch touch)
        {
            if (touch.Pressed) OnPointerDown(touch.Position);
            else               OnPointerUp(touch.Position);
            AcceptEvent();
            return;
        }

        if (@event is InputEventScreenDrag drag)
        {
            OnPointerMove(drag.Position);
            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed) OnPointerDown(mb.Position);
            else            OnPointerUp(mb.Position);
            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            if (_pressedId != -1 || _isPageGesture)
            {
                OnPointerMove(motion.Position);
                AcceptEvent();
            }
        }
    }

    private void OnPointerDown(Vector2 pos)
    {
        _pressPosition  = pos;
        _pressHeldTime  = 0f;
        _longPressFired = false;
        _isPageGesture  = false;

        if (_weaponSlotRect != default(Rect2) && _weaponSlotRect.HasPoint(pos))
        {
            _pressedId = WeaponSlotSentinel;
            return;
        }

        foreach (var (itemId, rect) in _slotRects)
        {
            if (rect.HasPoint(pos))
            {
                _pressedId = itemId;
                return;
            }
        }

        _pressedId = -1;
    }

    private void OnPointerMove(Vector2 pos)
    {
        if (_pressedId == -1 && !_isPageGesture) return;

        float dx = pos.X - _pressPosition.X;
        if (!_isPageGesture && MathF.Abs(dx) > ScrollTapThreshold)
        {
            _isPageGesture = true;
            CancelLongPress();
        }
    }

    private void OnPointerUp(Vector2 pos)
    {
        if (_isPageGesture)
        {
            _isPageGesture = false;
            float dx = pos.X - _pressPosition.X;

            if (dx < -PageFlipThreshold)
            {
                _currentPage = Mathf.Min(_currentPage + 1, _pageCount - 1);
                RefreshCurrentPage();
            }
            else if (dx > PageFlipThreshold)
            {
                _currentPage = Mathf.Max(_currentPage - 1, 0);
                RefreshCurrentPage();
            }

            CancelLongPress();
            return;
        }

        if (_longPressFired)
        {
            CancelLongPress();
            return;
        }

        if (_pressedId != -1)
        {
            int pressedId = _pressedId;
            CancelLongPress();

            if (pressedId == WeaponSlotSentinel)
                WeaponTapped?.Invoke();
            else
                ItemTapped?.Invoke(pressedId);
        }
    }

    private void CancelLongPress()
    {
        _pressedId    = -1;
        _pressHeldTime = 0f;
        _longPressFired = false;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Initialize(GameState state) => Refresh(state);

    public void Refresh(GameState state)
    {
        _registry = state.IdentificationRegistry;
        _pool     = state.AppearancePool;

        _slotModel.Clear();
        foreach (var entry in QuickSlotModel.Build(state))
            _slotModel[entry.ItemId] = entry;

        RefreshWeaponSlot(state);
        RefreshItemSlots(state);
    }

    // ── Weapon slot ───────────────────────────────────────────────────────────

    private void RefreshWeaponSlot(GameState state)
    {
        if (_weaponIcon == null || _weaponNameLabel == null) return;

        var equip  = state.Player.Get<Equipment>();
        var weapon = equip?.MainHand;

        if (weapon == null)
        {
            _weaponIcon.Texture   = null;
            _weaponNameLabel.Text = "Fists";
            return;
        }

        var spriteKey  = ItemDisplay.GetSpriteKey(weapon, _registry, _pool);
        var spritePath = SpriteMappingInstance?.GetItemSpritePath(spriteKey);

        _weaponIcon.Texture = spritePath != null ? GD.Load<Texture2D>(spritePath) : null;

        var name = weapon.Name;
        _weaponNameLabel.Text = name.Length > 6 ? name[..6] : name;
    }

    // ── Item slots ────────────────────────────────────────────────────────────

    private void RefreshItemSlots(GameState state)
    {
        _allItems = state.PlayerInventory?.Items
            .Where(item => item.Get<Consumable>() != null || item.Get<SpellEffect>() != null)
            .ToList() ?? new List<Entity>();

        int slotsPerPage = _slotsPerRow * 2;
        _pageCount = slotsPerPage == 0 ? 1 : Mathf.Max(1, (_allItems.Count + slotsPerPage - 1) / slotsPerPage);
        _currentPage = Mathf.Clamp(_currentPage, 0, _pageCount - 1);

        RefreshCurrentPage();
    }

    private void RefreshCurrentPage()
    {
        if (_itemGrid == null) return;

        foreach (var child in _itemGrid.GetChildren())
            child.SafeFree();
        _slotRects.Clear();

        int slotsPerPage = _slotsPerRow * 2;
        int start = _currentPage * slotsPerPage;
        int end   = Mathf.Min(start + slotsPerPage, _allItems.Count);

        for (int i = start; i < end; i++)
            _itemGrid.AddChild(BuildItemSlot(_allItems[i]));

        CallDeferred(MethodName._ComputeSlotRects);
    }

    private Control BuildItemSlot(Entity item)
    {
        var slot = new Control
        {
            CustomMinimumSize   = new Vector2(_slotSize, _slotSize),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical   = SizeFlags.ShrinkCenter,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        slot.SetMeta("item_id", item.Id);

        // Unavailable = the rules would refuse this tap right now (potion still on cooldown,
        // wand out of charges). Drawn dimmed so the slot reads as "not yet" rather than silently
        // doing nothing. Availability comes from QuickSlotModel — never re-derived here.
        bool unavailable = _slotModel.TryGetValue(item.Id, out var model) && !model.Available;

        var bg = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = unavailable ? SlotTintColor(item).Darkened(0.55f) : SlotTintColor(item);
        bgStyle.CornerRadiusTopLeft     = CornerRadius;
        bgStyle.CornerRadiusTopRight    = CornerRadius;
        bgStyle.CornerRadiusBottomLeft  = CornerRadius;
        bgStyle.CornerRadiusBottomRight = CornerRadius;
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        slot.AddChild(bg);

        var icon = BuildItemIcon(item);
        icon.MouseFilter = MouseFilterEnum.Ignore;
        if (unavailable)
            icon.Modulate = UnavailableIconTint;
        slot.AddChild(icon);

        // Remaining-turns readout, centered over the dimmed icon. Only shown for a real
        // countdown — a wand out of charges has no turn count to give, its "0" badge says it.
        if (unavailable && model.CooldownRemaining > 0)
        {
            var cooldown = new Label
            {
                Text                = $"{model.CooldownRemaining}",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                AnchorLeft   = 0f, AnchorTop    = 0f,
                AnchorRight  = 1f, AnchorBottom = 1f,
                MouseFilter  = MouseFilterEnum.Ignore,
            };
            cooldown.AddThemeFontSizeOverride("font_size", 26);
            cooldown.SelfModulate = Colors.White;
            slot.AddChild(cooldown);
        }

        var badgeText = GetBadgeText(item);
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
            badge.AddThemeFontSizeOverride("font_size", 14);
            badge.SelfModulate = Colors.White;
            slot.AddChild(badge);
        }

        return slot;
    }

    private Control BuildItemIcon(Entity item)
    {
        var spriteKey  = ItemDisplay.GetSpriteKey(item, _registry, _pool);
        var spritePath = SpriteMappingInstance?.GetItemSpritePath(spriteKey);

        if (spritePath != null)
        {
            var texture = GD.Load<Texture2D>(spritePath);
            if (texture != null)
            {
                return new TextureRect
                {
                    Texture     = texture,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    AnchorLeft  = 0f, AnchorTop    = 0f,
                    AnchorRight = 1f, AnchorBottom = 1f,
                    OffsetLeft  = 4f, OffsetTop    = 4f,
                    OffsetRight = -4f, OffsetBottom = -4f,
                };
            }
        }

        return new ColorRect
        {
            Color       = SlotTintColor(item).Lightened(0.2f),
            AnchorLeft  = 0f, AnchorTop    = 0f,
            AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft  = 4f, OffsetTop    = 4f,
            OffsetRight = -4f, OffsetBottom = -4f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
    }

    private static string? GetBadgeText(Entity item)
    {
        var consumable = item.Get<Consumable>();
        if (consumable != null)
            return $"{consumable.StackSize}";

        var wand = item.Get<WandComponent>();
        if (wand != null)
            return wand.Infinite ? "∞" : $"{wand.Charges}";

        return null;
    }

    private static Color SlotTintColor(Entity item)
    {
        if (item.Get<WandComponent>() != null) return TintWand;

        var consumable = item.Get<Consumable>();
        if (consumable != null)
        {
            if (consumable.IsPotion && consumable.IsHealing) return TintHealth;
            if (consumable.IsPotion) return TintArcane;
            return TintDefault;
        }

        if (item.Get<SpellEffect>() != null) return TintScroll;

        return TintDefault;
    }

    // ── Slot rect computation ─────────────────────────────────────────────────

    private void _ComputeSlotRects()
    {
        _slotRects.Clear();

        var panelOrigin = GetGlobalRect().Position;

        if (_weaponSlot != null)
        {
            var r = _weaponSlot.GetGlobalRect();
            _weaponSlotRect = new Rect2(r.Position - panelOrigin, r.Size);
        }

        if (_itemGrid == null) return;

        foreach (var child in _itemGrid.GetChildren())
        {
            if (child is Control c && c.HasMeta("item_id"))
            {
                int itemId = (int)c.GetMeta("item_id");
                var r      = c.GetGlobalRect();
                _slotRects.Add((itemId, new Rect2(r.Position - panelOrigin, r.Size)));
            }
        }
    }

    // ── Layout construction ───────────────────────────────────────────────────

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var bg = new ColorRect { Color = new Color(0f, 0f, 0f, 0.65f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(bg);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left",   BarMargin);
        margin.AddThemeConstantOverride("margin_right",  BarMargin);
        margin.AddThemeConstantOverride("margin_top",    BarMargin);
        margin.AddThemeConstantOverride("margin_bottom", BarMargin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(margin);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 0);
        hbox.MouseFilter = MouseFilterEnum.Ignore;
        margin.AddChild(hbox);

        // ── Weapon slot — spans both rows ─────────────────────────────────────
        _weaponSlot = BuildWeaponSlot();
        hbox.AddChild(_weaponSlot);

        // 1px vertical separator
        var separator = new ColorRect
        {
            Color             = new Color(0.5f, 0.5f, 0.6f, 0.8f),
            CustomMinimumSize = new Vector2(SeparatorWidth, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter       = MouseFilterEnum.Ignore,
        };
        hbox.AddChild(separator);

        // Small gap after separator
        var gap = new Control
        {
            CustomMinimumSize = new Vector2(SlotGap, 0),
            MouseFilter       = MouseFilterEnum.Ignore,
        };
        hbox.AddChild(gap);

        // ── Item grid — 2 rows, page-based ────────────────────────────────────
        _itemGrid = new GridContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical   = SizeFlags.ShrinkCenter,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        _itemGrid.Columns = _slotsPerRow;
        _itemGrid.AddThemeConstantOverride("h_separation", SlotGap);
        _itemGrid.AddThemeConstantOverride("v_separation", RowGap);
        hbox.AddChild(_itemGrid);
    }

    private Control BuildWeaponSlot()
    {
        var style = new StyleBoxFlat();
        style.BgColor               = new Color(0.2f, 0.18f, 0.1f, 0.8f);
        style.CornerRadiusTopLeft     = CornerRadius;
        style.CornerRadiusTopRight    = CornerRadius;
        style.CornerRadiusBottomLeft  = CornerRadius;
        style.CornerRadiusBottomRight = CornerRadius;

        var panel = new Panel
        {
            CustomMinimumSize = new Vector2(_weaponSlotW, _weaponSlotH),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter       = MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", style);

        _weaponIcon = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            AnchorLeft  = 0.1f, AnchorTop    = 0.05f,
            AnchorRight = 0.9f, AnchorBottom = 0.65f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        panel.AddChild(_weaponIcon);

        _weaponNameLabel = new Label
        {
            Text                = "—",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Bottom,
            AnchorLeft          = 0f, AnchorTop    = 0.6f,
            AnchorRight         = 1f, AnchorBottom = 1f,
            OffsetBottom        = -2f,
            MouseFilter         = MouseFilterEnum.Ignore,
            ClipText            = true,
        };
        _weaponNameLabel.AddThemeFontSizeOverride("font_size", 11);
        _weaponNameLabel.SelfModulate = new Color(0.9f, 0.85f, 0.7f, 1f);
        panel.AddChild(_weaponNameLabel);

        _weaponSlot = panel;
        return panel;
    }
}
