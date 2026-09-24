using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Knowledge;
using UnderWarden.Logic.Map;
using UnderWarden.Presentation.Animation;
using UnderWarden.Presentation.Entities;
using UnderWarden.Presentation.Input;
using UnderWarden.Presentation.Map;
using UnderWarden.Presentation.UI;
using Godot;

namespace UnderWarden.Presentation;

/// <summary>
/// Orchestrates the full game loop. Owns GameState, drives TurnController,
/// coordinates input → processing → animation → input cycle.
///
/// State machine:
///   WaitingForInput → (player taps) → Processing → (turn resolved) → Animating → (done) → WaitingForInput
///   Any state → GameOver (player dies or all monsters dead)
/// </summary>
public sealed partial class GameController : Node
{
    public enum GamePhase { WaitingForInput, Processing, Animating, GameOver, Targeting }

    /// <summary>Animation speed multiplier during auto-explore. 1.0 = normal, lower = faster.</summary>
    public const float AutoExploreSpeedMultiplier = 0.25f;

    /// <summary>Animation speed multiplier during multi-step click-to-move. Between auto-explore and normal.</summary>
    public const float ClickToMoveSpeedMultiplier = 0.55f;

    private GameState? _state;
    private MonsterFactory? _monsterFactory;
    private EntityFactory? _portalEntityFactory;
    private InputHandler _input = new();
    private EntitySpriteManager? _entitySprites;
    private ItemSpriteManager? _itemSprites;
    private CorpseSpriteManager? _corpseSprites;
    private QuickSlotBar? _inventoryPanel;
    private EquipmentPanel? _equipmentPanel;
    private ToastLog? _toastLog;
    private TurnAnimator? _animator;
    private LongPressDetector? _longPress;
    private InspectPanel? _inspectPanel;
    private ActionSheet? _actionSheet;
    private IMapRenderer _renderer = new TopDownRenderer(); // safe default
    private Node2D? _gameView; // needed to ToLocal() in OnLongPress

    // When true, long-pressing props/doors/traps/portals shows the feature inspect panel.
    // Controlled by game_settings.yaml: show_prop_inspect. Default true.
    private bool _showPropInspect = true;

    // Holds the item being thrown — needed to route LocationChosen back to ThrowItem.
    private Entity? _pendingThrowItem;

    // Stored between OnActionChosen and OnAnimationComplete so we can fire the transition
    // after animations finish rather than immediately when the event is emitted.
    private DescendEvent? _pendingDescend;

    // Set when the portal wand enters targeting mode for exit placement (step 2).
    // Used to clean up the pending entrance if the player cancels targeting.
    private Entity? _pendingPortalWand;

    // True while the player is in possession-targeting mode (§3 of plan_possession_system.md).
    // Taps route to HandlePossessionTargetingTap instead of the normal InputHandler flow.
    private bool _possessionTargetingActive;

    // Multi-step click-to-move: A* path queued step-by-step, one step per turn.
    private Queue<(int X, int Y)>? _pendingPath;
    // HP snapshot at path start — interrupt if player takes damage mid-path.
    private int _pathInterruptHp = -1;
    // Monster IDs visible when path started — only interrupt on NEW monsters entering FOV.
    // Empty until Phase 2 (FOV) lands; CheckPathInterrupts is a no-op for monster visibility until then.
    private readonly HashSet<int> _pathStartVisibleMonsterIds = new();
    private bool _autoExploreMode;

    public GamePhase Phase { get; private set; } = GamePhase.WaitingForInput;

    // ── Held quick-slot input ─────────────────────────────────────────────────
    //
    // Quick-slot input that arrives while a turn is resolving is HELD, not dropped.
    // Exactly one is held: a newer press replaces an older one (queue-one,
    // replace-latest). It fires the moment the phase returns to WaitingForInput.
    //
    // Why this exists: the phase gate used to early-return, silently. That is fine for
    // one animation frame and wrong for the two windows that actually matter — a
    // click-to-move path and auto-explore both re-enter ExecuteTurn from
    // OnAnimationComplete, so the phase stays Processing/Animating for the WHOLE run.
    // Every quick-slot press across those turns vanished, which reads as the bar being
    // dead for a few turns and then healing itself. Worse, the two gestures disagreed:
    // a tap on the MAP cancels the run (HandleTap), but the QuickSlotBar calls
    // AcceptEvent() on its own _GuiInput, so a quick-slot press never reached that path
    // and died at the gate instead. See #114.
    //
    // Deliberately queue-ONE: a roguelike turn is a commitment, and replaying a backlog
    // of taps against state the player can no longer see is worse than losing the extra
    // presses. The held press is re-validated on release, so it can never resolve against
    // an item that has since gone stale.
    private enum HeldInputKind { None, Tap, LongPress }
    private HeldInputKind _heldInputKind = HeldInputKind.None;
    private int           _heldInputItemId;

    /// <summary>True while auto-explore is actively running.</summary>
    public bool IsAutoExploreActive => _autoExploreMode;

    /// <summary>True while possession targeting is active (player taps resolve to Possess).</summary>
    public bool IsPossessionTargetingActive => _possessionTargetingActive;

    /// <summary>
    /// Injection point for the graphical bot driver (BotPlayerDriver).
    /// Submits a pre-computed PlayerAction directly to ExecuteTurn, bypassing human input
    /// path-following, auto-explore cancellation, and click-to-move logic.
    ///
    /// Guards:
    /// - Phase must be WaitingForInput (bot cannot act during animations or game over)
    /// - The player must be controlling themselves, not a possessed monster
    ///   (bot does not drive possession hosts; the driver should call Disable() on possession)
    ///
    /// Only callable from BotPlayerDriver — not from human input code paths.
    /// Debug builds only: BotPlayerDriver is never instantiated in release builds.
    /// </summary>
    public void SubmitBotAction(PlayerAction action)
    {
        if (_state == null) return;
        if (Phase != GamePhase.WaitingForInput) return;

        // Bot does not drive possessed hosts — possession is a human-controlled feature.
        // The driver should already have called Disable() on possession entry, but guard
        // here as a safety net.
        if (!ReferenceEquals(_state.ControlledEntity, _state.Player)) return;

        ExecuteTurn(action);
    }

    /// <summary>Fired each time a turn completes. UI can update from this.</summary>
    public event Action<TurnResult>? TurnCompleted;

    /// <summary>Fired when the game ends.</summary>
    public event Action<bool>? GameEnded; // true = player won

    /// <summary>
    /// Fired after animations complete when the player has descended a staircase.
    /// Carries the new depth the player is arriving at.
    /// Main listens to this to tear down the current floor and build the next one.
    /// </summary>
    public event Action<int>? FloorTransitionRequested;

    /// <summary>
    /// Initialize the controller with a loaded game state and scene nodes.
    /// Call after creating GameState and setting up the scene.
    /// </summary>
    public override void _Process(double delta)
    {
        // Poll tween completion instead of using TweenCallback + Callable.From.
        // Callable.From(delegate) on a non-GodotObject creates a GCHandle that
        // Godot 4.6 C# doesn't reliably release, causing memory explosion in combat.
        _animator?.CheckComplete();
    }

    public void Initialize(GameState state, EntitySpriteManager entitySprites, Node animationRoot,
        ItemSpriteManager? itemSprites = null, QuickSlotBar? inventoryPanel = null,
        EquipmentPanel? equipmentPanel = null, ToastLog? toastLog = null,
        MonsterFactory? monsterFactory = null, IMapRenderer? renderer = null,
        Node2D? gameView = null, EntityFactory? portalEntityFactory = null,
        VfxOverlay? vfxOverlay = null, bool showPropInspect = true,
        CorpseSpriteManager? corpseSprites = null)
    {
#if DEBUG
        System.Diagnostics.Debug.Assert(_animator == null,
            "GameController.Initialize called twice on the same instance — event double-subscribe risk");
#endif
        _state = state;
        _monsterFactory = monsterFactory;
        _portalEntityFactory = portalEntityFactory;
        _entitySprites = entitySprites;
        _itemSprites = itemSprites;
        _corpseSprites = corpseSprites;
        _inventoryPanel = inventoryPanel;
        _equipmentPanel = equipmentPanel;
        _toastLog = toastLog;
        _renderer = renderer ?? new TopDownRenderer();
        _gameView = gameView;
        _showPropInspect = showPropInspect;
        _animator = new TurnAnimator(animationRoot, entitySprites, _renderer, vfxOverlay);
        _animator.AnimationComplete += OnAnimationComplete;

        _input.SetState(state);
        _input.SetRenderer(_renderer);
        _input.ActionChosen += OnActionChosen;
        _input.TargetChosen += OnTargetChosen;
        _input.LocationChosen += OnLocationChosen;
        _input.TargetingCancelled += OnTargetingCancelled;
        _input.SetAcceptingInput(true);

        // Set up long-press detection and inspect panel.
        // Both are created as children of this Node so they participate in the scene tree.
        _longPress = new LongPressDetector();
        AddChild(_longPress);
        _longPress.LongPressDetected += OnLongPress;

        // Both InspectPanel and ActionSheet must live in UILayer (CanvasLayer) to render
        // on top of all game tiles and entity sprites. GameController is a plain Node —
        // its children render in world space behind CanvasLayer UI.
        var uiLayer = GetTree()?.CurrentScene?.GetNode<CanvasLayer>("UILayer");

        _inspectPanel = new InspectPanel();
        if (uiLayer != null)
            uiLayer.AddChild(_inspectPanel);
        else
            AddChild(_inspectPanel); // fallback (editor/test context)

        _actionSheet = new ActionSheet();
        if (uiLayer != null)
            uiLayer.AddChild(_actionSheet);
        else
            AddChild(_actionSheet); // fallback (editor/test context)
        _actionSheet.ActionSelected += OnActionSheetSelected;

        // Wire inventory long-press
        if (_inventoryPanel != null)
            _inventoryPanel.ItemLongPressed += HandleInventoryLongPress;

        // Wire equipment panel long-press events
        if (_equipmentPanel != null)
        {
            _equipmentPanel.EquippedItemLongPressed += HandleEquippedSlotLongPress;
            _equipmentPanel.PackItemLongPressed     += HandlePackItemLongPress;
        }

        _pendingPath = null;
        _autoExploreMode = false;
        ClearHeldInput();
    }

    /// <summary>
    /// Handle a tap on an inventory slot.
    /// - Potions (Consumable with HealAmount > 0): issue UseItem immediately.
    /// - Scrolls / Wands (SpellEffect): dispatch via HandleScrollOrWandUse.
    ///   Self/AutoClosest/AoeSelf → immediate CastSpell action.
    ///   SingleTarget/Location → enter targeting mode, wait for player to pick a target.
    /// </summary>
    public void HandleInventoryTap(int itemId)
    {
        Diag.Log($"HandleInventoryTap: itemId={itemId}, phase={Phase}");
        if (_state == null) { Diag.Log("  BLOCKED: no state"); return; }

        // Mid-turn: hold it rather than swallow it. Released by DrainHeldInput().
        if (Phase != GamePhase.WaitingForInput)
        {
            HoldInput(HeldInputKind.Tap, itemId);
            return;
        }

        var inventory = _state.PlayerInventory;
        if (inventory == null) { Diag.Log("  BLOCKED: no inventory"); return; }

        var item = inventory.FindFirst(e => e.Id == itemId);
        if (item == null) { Diag.Log($"  BLOCKED: no item with id={itemId}"); return; }

        // An unavailable item is a free no-op — never a lost turn, never a lost item.
        //
        // This is the spent-wand ruling (PR #113) applied at the seam that covers every
        // availability source instead of one. It has to sit HERE, before OnActionChosen:
        // TryHeal drinks whatever item it is handed, so a tap on a cooldown-blocked potion
        // used to spend the turn, consume the potion AND re-arm the cooldown — on a slot the
        // bar was drawing dimmed with a countdown at the time. The dim promised a refusal the
        // rules never made.
        //
        // Availability comes from QuickSlotModel, the same call the bar dims from, so the
        // greyed-out state and the refusal can no longer disagree.
        var (available, block, cooldownTurns) = QuickSlotModel.Availability(item, _state.PlayerFighter);
        if (!available)
        {
            _toastLog?.AddMessage(RefusalMessage(item, block, cooldownTurns));
            Diag.Log($"  BLOCKED: {item.Name} unavailable ({block}) — free no-op, no turn consumed.");
            return;
        }

        // Check if item is a scroll or wand.
        // Throwable potions no longer enter targeting on tap — tap = drink (obvious action).
        // Throw is accessed via long-press → action sheet → Throw.
        var spellEffect = item.Get<SpellEffect>();
        if (spellEffect != null)
        {
            HandleScrollOrWandUse(item, spellEffect);
            return;
        }

        Diag.Log($"  -> UseItem({item.Name})");
        OnActionChosen(PlayerAction.UseItem(item));
    }

    // ─── Held quick-slot input ────────────────────────────────────────────────

    /// <summary>
    /// Hold a quick-slot press that arrived mid-turn. Replaces whatever was held —
    /// the newest press is the one the player still means.
    /// </summary>
    private void HoldInput(HeldInputKind kind, int itemId)
    {
        _heldInputKind   = kind;
        _heldInputItemId = itemId;
        Diag.Log($"  HELD ({kind}) itemId={itemId} until phase returns to WaitingForInput (phase={Phase})");
    }

    /// <summary>
    /// Forget any held press. Called whenever the player commits to something else —
    /// they acted by another route, entered targeting, changed floor, or died. A press
    /// held across one of those would surface at a moment the player is no longer in.
    /// </summary>
    private void ClearHeldInput()
    {
        if (_heldInputKind == HeldInputKind.None) return;
        Diag.Log($"  held input ({_heldInputKind}) cleared — superseded");
        _heldInputKind = HeldInputKind.None;
    }

    /// <summary>
    /// Release a held press now that the phase is WaitingForInput again.
    ///
    /// Re-validated, never replayed blind: the item must still be in the inventory and
    /// still available. A potion drunk by a queued heal, or a wand spent during the walk,
    /// is dropped in silence — the press was made against a bar that no longer exists,
    /// and a refusal toast for it would be noise about a decision the player has moved on
    /// from. Refusals the player CAN act on still come from HandleInventoryTap as normal.
    ///
    /// Clears the held slot before dispatching: the dispatch re-enters ExecuteTurn and
    /// sets Phase back to Processing, so a press left in place would re-fire forever.
    /// </summary>
    private void DrainHeldInput()
    {
        if (_heldInputKind == HeldInputKind.None) return;
        if (_state == null || Phase != GamePhase.WaitingForInput) return;

        var kind   = _heldInputKind;
        int itemId = _heldInputItemId;
        _heldInputKind = HeldInputKind.None;

        var item = FindItemById(itemId);
        if (item == null)
        {
            Diag.Log($"  held input ({kind}) dropped — item {itemId} is gone");
            return;
        }

        if (kind == HeldInputKind.Tap
            && !QuickSlotModel.Availability(item, _state.PlayerFighter).Available)
        {
            Diag.Log($"  held tap dropped — {item.Name} is no longer available");
            return;
        }

        Diag.Log($"  releasing held input ({kind}) for {item.Name}");
        if (kind == HeldInputKind.Tap) HandleInventoryTap(itemId);
        else                           HandleInventoryLongPress(itemId);
    }

    /// <summary>
    /// What to tell the player when a tap is refused. One line per QuickSlotBlock, so the
    /// toast names the same rule the slot is dimmed for. "The wand is spent." is carried over
    /// verbatim from the spent-wand fix — the wording was already ruled on.
    /// </summary>
    private static string RefusalMessage(Entity item, QuickSlotBlock block, int cooldownTurns) => block switch
    {
        QuickSlotBlock.NoCharges      => "The wand is spent.",
        QuickSlotBlock.PotionCooldown => $"Not yet — {cooldownTurns} more turn{(cooldownTurns == 1 ? "" : "s")}.",
        _                             => $"You cannot use the {item.Name} right now.",
    };

    /// <summary>
    /// Dispatch a scroll or wand use.
    ///
    /// Self / AutoClosest / AoeSelf: no targeting UI — fire CastSpell immediately.
    ///   AutoClosest: find closest visible enemy first; if none, toast "No visible targets".
    /// SingleTarget / Location: enter targeting mode.
    /// Portal: wand-of-portals two-step (deferred to Phase 5).
    /// </summary>
    private void HandleScrollOrWandUse(Entity item, SpellEffect spell)
    {
        Diag.Log($"HandleScrollOrWandUse: {item.Name} targeting={spell.Targeting}");

        // The spent-wand refusal that used to live here now runs for every availability source
        // at the tap seam in HandleInventoryTap, which is this method's only caller. Keeping a
        // second copy of it here is the duplication that let the bar and the rules drift apart
        // in the first place. The ruling it encodes is unchanged: a spent wand costs neither a
        // turn nor a trip into targeting mode, and infinite wands keep working.

        switch (spell.Targeting)
        {
            case TargetingMode.Self:
            case TargetingMode.AoeSelf:
                // No target required — fire immediately
                OnActionChosen(PlayerAction.CastSpell(item));
                break;

            case TargetingMode.AutoClosest:
                // Resolve target now in the presentation layer (closest visible enemy)
                var closest = FindClosestVisibleEnemy(spell.Range);
                if (closest == null)
                {
                    _toastLog?.AddMessage("No visible targets in range.");
                    Diag.Log("HandleScrollOrWandUse: no visible targets for AutoClosest");
                    return; // Do NOT consume item or turn
                }
                OnActionChosen(PlayerAction.CastSpell(item, targetEntityId: closest.Id));
                break;

            case TargetingMode.SingleTarget:
                EnterTargetingMode(new TargetingState
                {
                    Item  = item,
                    Spell = spell,
                    Mode  = TargetingMode.SingleTarget,
                    Range = spell.Range,
                });
                break;

            case TargetingMode.Location:
                EnterTargetingMode(new TargetingState
                {
                    Item   = item,
                    Spell  = spell,
                    Mode   = TargetingMode.Location,
                    Range  = spell.Range,
                    Radius = spell.Radius,
                });
                break;

            case TargetingMode.Portal:
            {
                if (_state == null) break;
                var step = PortalSystem.GetPortalCastStep(item);
                switch (step)
                {
                    case PortalCastStep.Ready:
                        // Step 1: pick entrance location
                        _pendingPortalWand = item;
                        EnterTargetingMode(new TargetingState
                        {
                            Item  = item,
                            Spell = spell,
                            Mode  = TargetingMode.Location,
                            Range = 0,
                        }, showGenericToast: false);
                        _toastLog?.AddMessage("Tap a tile to place the portal entrance.");
                        break;
                    case PortalCastStep.EntrancePlaced:
                        // Step 2: pick exit location
                        _pendingPortalWand = item;
                        EnterTargetingMode(new TargetingState
                        {
                            Item  = item,
                            Spell = spell,
                            Mode  = TargetingMode.Location,
                            Range = 0,
                        }, showGenericToast: false);
                        _toastLog?.AddMessage("Tap a tile to place the exit portal. Tap yourself to cancel.");
                        break;
                    case PortalCastStep.BothPlaced:
                        // Step 3: reset — removes both portals
                        _toastLog?.AddMessage("Portals removed. Wand recharged.");
                        OnActionChosen(PlayerAction.CastSpell(item));
                        break;
                }
                break;
            }

            default:
                // Unknown targeting mode — treat as Self
                OnActionChosen(PlayerAction.CastSpell(item));
                break;
        }
    }

    // HandleThrowablePotion removed (TASK-006): tap on throwable potions now drinks immediately.
    // Throw is accessed via long-press → action sheet → "Throw" → targeting mode.
    // OnDrinkSelfRequested removed: self-tap during targeting always cancels (no drink path).

    // ─── Action Sheet ─────────────────────────────────────────────────────────

    /// <summary>
    /// Show the action sheet for a long-pressed inventory slot item.
    /// Determines whether the item is currently equipped so the sheet shows the right actions.
    ///
    /// Deliberately NOT availability-gated, and must stay that way. Long-press is how the
    /// player inspects, drops and throws — all still legal for a spent wand or a potion on
    /// cooldown. Availability governs USE, not access to the item. (The sheet's own Use entry
    /// routes back through HandleInventoryTap, which is where the refusal lives.)
    /// </summary>
    public void HandleInventoryLongPress(int itemId)
    {
        if (_state == null) return;

        // Held, not swallowed — same reason as the tap. A long-press during a walk was the
        // second live sighting of the phase-gate swallow: the action sheet simply never
        // opened, with nothing on screen to say why.
        if (Phase != GamePhase.WaitingForInput)
        {
            HoldInput(HeldInputKind.LongPress, itemId);
            return;
        }

        var item = FindItemById(itemId);
        if (item == null) return;

        bool isEquipped = IsItemEquipped(item);
        _actionSheet?.Show(item, isEquipped);
    }

    /// <summary>Show the action sheet for a long-pressed equipped slot.</summary>
    public void HandleEquippedSlotLongPress(EquipmentSlot slot, int itemId)
    {
        if (_state == null || Phase != GamePhase.WaitingForInput) return;
        var item = FindItemById(itemId);
        if (item == null) return;

        _actionSheet?.Show(item, isEquipped: true);
    }

    /// <summary>Show the action sheet for a long-pressed pack item in the equipment panel.</summary>
    public void HandlePackItemLongPress(int itemId)
    {
        if (_state == null || Phase != GamePhase.WaitingForInput) return;
        var item = FindItemById(itemId);
        if (item == null) return;

        _actionSheet?.Show(item, isEquipped: false);
    }

    /// <summary>
    /// Dispatch the selected action from the action sheet.
    /// </summary>
    private void OnActionSheetSelected(int itemId, ActionSheetAction action)
    {
        if (_state == null || Phase != GamePhase.WaitingForInput) return;
        var item = FindItemById(itemId);
        if (item == null) return;

        switch (action)
        {
            case ActionSheetAction.Use:
                // Tap-equivalent: drink potion, use scroll, etc.
                HandleInventoryTap(itemId);
                break;

            case ActionSheetAction.Throw:
                // Enter throw targeting mode — any tile is valid (not just monsters)
                EnterThrowTargeting(item);
                break;

            case ActionSheetAction.Drop:
                HandleDropRequest(itemId);
                break;

            case ActionSheetAction.Equip:
                HandleEquipRequest(itemId);
                break;

            case ActionSheetAction.Unequip:
            {
                // Find the slot this item is in and unequip it
                var equippable = item.Get<Equippable>();
                if (equippable != null)
                    HandleUnequipRequest(equippable.Slot);
                break;
            }
        }
    }

    /// <summary>
    /// Enter throw targeting mode for the given item.
    /// Any tile (walkable or with monster) is a valid target — you can throw at empty ground.
    /// On location chosen, fires PlayerAction.ThrowItem.
    /// </summary>
    private void EnterThrowTargeting(Entity item)
    {
        _pendingThrowItem = item;
        // Use Location targeting so any tile (walkable or occupied) is valid.
        // Range 10 = PoC fixed throw range.
        var spell = item.Get<SpellEffect>();
        EnterTargetingMode(new TargetingState
        {
            Item  = item,
            // Spell is required by TargetingState. Use the item's spell if present,
            // or a synthetic Self spell for non-spell items (junk, weapons).
            Spell = spell ?? new SpellEffect { SpellId = "_throw_placeholder", Targeting = TargetingMode.Location },
            Mode  = TargetingMode.Location,
            Range = 10,
        }, showGenericToast: false);
        string throwName = _state != null
            ? ItemDisplay.GetDisplayName(item, _state.IdentificationRegistry, _state.AppearancePool)
            : item.Name;
        _toastLog?.AddMessage($"Tap a tile to throw {throwName}. Tap yourself to cancel.");
    }

    /// <summary>
    /// Look up an item by ID from both inventory and equipment slots.
    /// Returns null if the item can't be found.
    /// </summary>
    private Entity? FindItemById(int itemId)
    {
        if (_state == null) return null;

        var inventory = _state.PlayerInventory;
        if (inventory != null)
        {
            var found = inventory.FindFirst(e => e.Id == itemId);
            if (found != null) return found;
        }

        // Check equipped slots
        var equipment = _state.Player.Get<Equipment>();
        if (equipment != null)
        {
            foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
            {
                var item = equipment.GetSlot(slot);
                if (item?.Id == itemId) return item;
            }
        }

        return null;
    }

    /// <summary>Returns true if the item is currently in any equipment slot.</summary>
    private bool IsItemEquipped(Entity item)
    {
        var equipment = _state?.Player.Get<Equipment>();
        if (equipment == null) return false;

        foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
        {
            if (equipment.GetSlot(slot)?.Id == item.Id) return true;
        }
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Enter targeting mode and notify the presentation.</summary>
    private void EnterTargetingMode(TargetingState targeting, bool showGenericToast = true)
    {
        Phase = GamePhase.Targeting;
        ClearHeldInput();
        _input.EnterTargetingMode(targeting);
        if (showGenericToast)
        {
            string displayName = _state != null
                ? ItemDisplay.GetDisplayName(targeting.Item, _state.IdentificationRegistry, _state.AppearancePool)
                : targeting.Item.Name;
            _toastLog?.AddMessage($"Tap a target for {displayName}. Tap yourself to cancel.");
        }
        Diag.Log($"GameController: entered targeting mode for {targeting.Item.Name}");
    }

    /// <summary>Called when the player picks a single-target monster during targeting mode.</summary>
    private void OnTargetChosen(Entity item, Entity target)
    {
        Diag.Log($"OnTargetChosen: item={item.Name} target={target.Name}(id={target.Id})");
        Phase = GamePhase.WaitingForInput;
        OnActionChosen(PlayerAction.CastSpell(item, targetEntityId: target.Id));
    }

    /// <summary>Called when the player picks a location tile during targeting mode.</summary>
    private void OnLocationChosen(Entity item, int x, int y)
    {
        Diag.Log($"OnLocationChosen: item={item.Name} location=({x},{y})");
        Phase = GamePhase.WaitingForInput;

        // If we entered targeting from the throw action sheet, route to ThrowItem.
        if (_pendingThrowItem != null && _pendingThrowItem.Id == item.Id)
        {
            _pendingThrowItem = null;
            OnActionChosen(PlayerAction.ThrowItem(item, x, y));
            return;
        }

        OnActionChosen(PlayerAction.CastSpell(item, targetX: x, targetY: y));
    }

    /// <summary>Called when targeting is cancelled — no action, no turn consumed.</summary>
    private void OnTargetingCancelled()
    {
        Diag.Log("OnTargetingCancelled: returning to WaitingForInput");
        Phase = GamePhase.WaitingForInput;
        _pendingThrowItem = null; // clear any pending throw session

        // Portal wand: cancel pending entrance placement, remove sprite
        if (_pendingPortalWand != null && _state != null)
        {
            var cancelEvt = PortalSystem.CancelPendingEntrance(_pendingPortalWand, _state);
            if (cancelEvt != null)
            {
                PortalEntranceCancelled?.Invoke(cancelEvt.EntranceEntityId);
                _toastLog?.AddMessage("Portal cancelled.");
            }
            _pendingPortalWand = null;
        }
        else
        {
            _toastLog?.AddMessage("Cancelled.");
        }
    }

    /// <summary>
    /// Fired when a pending portal entrance is cancelled so the presentation can despawn its sprite.
    /// Carries the entrance entity ID.
    /// </summary>
    public event Action<int>? PortalEntranceCancelled;

    /// <summary>
    /// Find the closest alive, visible monster within the given range.
    /// Used for AutoClosest spell targeting.
    /// </summary>
    private Entity? FindClosestVisibleEnemy(int maxRange)
    {
        if (_state == null) return null;

        Entity? closest = null;
        double closestDist = maxRange > 0 ? maxRange + 1.0 : double.MaxValue;

        foreach (var monster in _state.AliveMonsters)
        {
            if (_state.IsDungeonMode && !_state.Map.IsVisible(monster.X, monster.Y))
                continue;

            double dist = _state.Player.DistanceTo(monster.X, monster.Y);
            if (dist < closestDist)
            {
                closest = monster;
                closestDist = dist;
            }
        }

        return closest;
    }

    /// <summary>
    /// Handle a tap on the drop button for an inventory item.
    /// Issues a Drop action, placing the item on the floor at the player's feet.
    /// </summary>
    public void HandleDropRequest(int itemId)
    {
        if (_state == null || Phase != GamePhase.WaitingForInput) return;
        var inventory = _state.PlayerInventory;
        if (inventory == null) return;
        var item = inventory.FindFirst(e => e.Id == itemId);
        if (item == null) return;
        OnActionChosen(PlayerAction.Drop(item));
    }

    /// <summary>
    /// Handle a tap on an In Pack item in the equipment panel.
    /// Issues an EquipItem action via TurnController.
    /// </summary>
    public void HandleEquipRequest(int itemId)
    {
        if (_state == null || Phase != GamePhase.WaitingForInput) return;

        var inventory = _state.PlayerInventory;
        if (inventory == null) return;

        var item = inventory.FindFirst(e => e.Id == itemId);
        if (item == null) return;

        OnActionChosen(PlayerAction.Equip(item));
    }

    /// <summary>
    /// Handle a tap on an occupied equipment slot in the equipment panel.
    /// Issues an UnequipItem action via TurnController.
    /// </summary>
    public void HandleUnequipRequest(EquipmentSlot slot)
    {
        if (_state == null || Phase != GamePhase.WaitingForInput) return;
        OnActionChosen(PlayerAction.Unequip(slot));
    }

    /// <summary>
    /// Cancel the active targeting operation. Called by the TargetingOverlay cancel button.
    /// Delegates to InputHandler.CancelTargeting which fires TargetingCancelled.
    /// No turn consumed.
    /// </summary>
    public void CancelTargeting()
    {
        if (Phase != GamePhase.Targeting) return;
        Diag.Log("GameController.CancelTargeting called");
        _input.CancelTargeting();
    }

    /// <summary>
    /// Forward raw tap position from the scene's _Input handler.
    /// Dismisses any open inspect panel before routing to normal input handling.
    /// </summary>
    public void HandleTap(Vector2 screenPos)
    {
        // Dismiss inspect panel on any normal tap — the player is acting, not inspecting.
        _inspectPanel?.Hide();
        _longPress?.Cancel();

        // Any tap cancels auto-explore or path-following.
        if (_autoExploreMode || (_pendingPath != null && _pendingPath.Count > 0))
        {
            _autoExploreMode = false;
            _pendingPath = null;
            _animator!.SpeedMultiplier = 1.0f;
            PlayerCamera.ActiveMode = CameraMode.Deadzone;
            return;
        }

        // Possession targeting intercept — tap on a valid target possesses; anything else cancels.
        if (_possessionTargetingActive)
        {
            HandlePossessionTargetingTap(screenPos);
            return;
        }

        _input.HandleTap(screenPos);
    }

    private void HandlePossessionTargetingTap(Vector2 screenPos)
    {
        if (_state == null || _gameView == null) { CancelPossessionTargeting(); return; }

        // screenPos is already in local space — Main._UnhandledInput calls _gameView.ToLocal()
        // before passing to HandleTap. Do NOT call ToLocal() again here.
        var (gridX, gridY) = _renderer.ScreenToGrid(screenPos);

        // Tap on player's own tile → cancel
        if (gridX == _state.Player.X && gridY == _state.Player.Y)
        {
            CancelPossessionTargeting();
            return;
        }

        var target = _state.AliveMonsters.FirstOrDefault(m => m.X == gridX && m.Y == gridY);
        if (target != null && PossessionSystem.IsValidTarget(target, _state.Player, _state))
        {
            _possessionTargetingActive = false;
            OnActionChosen(PlayerAction.Possess(target));
        }
        else
        {
            // Invalid tap — cancel targeting
            CancelPossessionTargeting();
        }
    }

    /// <summary>
    /// Handle a long-press or stationary hover at the given screen position.
    /// Checks for a monster or floor item at that tile and shows the inspect panel.
    /// </summary>
    private void OnLongPress(Vector2 screenPos)
    {
        if (_state == null) return;

        // Check the quick-slot bar first — it lives in a CanvasLayer (screen space), same as
        // InspectPanel, so no ToLocal() transform is needed here (unlike the map check below).
        var hoveredItemId = _inventoryPanel?.GetItemIdAtGlobalPosition(screenPos);
        if (hoveredItemId.HasValue)
        {
            var invItem = _state.PlayerInventory?.Items.FirstOrDefault(i => i.Id == hoveredItemId.Value);
            if (invItem != null)
            {
                _inspectPanel?.ShowItem(ItemInspectView.From(invItem, _state.IdentificationRegistry, _state.AppearancePool));
                _inspectPanel?.PositionNear(screenPos, GetViewport()?.GetVisibleRect().Size ?? new Vector2(480, 854));
                return;
            }
        }

        // Apply the same ToLocal() transform that Main._UnhandledInput applies before HandleTap.
        // Without this, OnLongPress was passing raw screen coordinates to ScreenToGrid, which
        // expects GameView-local coordinates — causing long-press to resolve the wrong tile.
        var localPos = _gameView != null ? _gameView.ToLocal(screenPos) : screenPos;
        var (gridX, gridY) = _renderer.ScreenToGrid(localPos);

        if (!_state.Map.InBounds(gridX, gridY))
            return;

        // Check for a monster at this tile first (takes priority over floor items)
        var monster = _state.AliveMonsters.FirstOrDefault(m => m.X == gridX && m.Y == gridY);
        if (monster != null)
        {
            var speciesTag = monster.Get<Logic.ECS.SpeciesTag>();
            if (speciesTag != null)
            {
                // Look up the MonsterDefinition from the monster factory for stat label computation.
                // If no factory is available, show a minimal view using just the knowledge entry.
                if (_monsterFactory != null && _monsterFactory.TryGetDefinition(speciesTag.TypeId, out var def) && def != null)
                {
                    var info = _state.Knowledge.GetInfoView(speciesTag.TypeId, def);
                    _inspectPanel?.ShowMonster(info);
                    _inspectPanel?.PositionNear(screenPos, GetViewport()?.GetVisibleRect().Size ?? new Vector2(480, 854));
                    return;
                }
            }
        }

        // Check for a corpse at this tile. Ranks below live monsters (the alive check above uses
        // AliveMonsters, which excludes corpses) so a living monster standing on remains inspects as
        // the monster; only bare remains inspect as remains.
        var corpseEntity = _state.Corpses.FirstOrDefault(c => c.X == gridX && c.Y == gridY);
        if (corpseEntity != null)
        {
            var cc = corpseEntity.Get<Logic.ECS.CorpseComponent>();
            // NOTE (flag): "Remains of {Name}" is a proposed label — see docs/systems/visible_corpses.md.
            string remainsName = cc != null && !string.IsNullOrEmpty(cc.OriginalName)
                ? $"Remains of {cc.OriginalName}"
                : "Remains";
            string desc = cc != null && cc.CanBeRaised
                ? "The fresh corpse of a slain foe — the dead here can still be raised."
                : "The spent remains of a slain foe.";
            _inspectPanel?.ShowFeature(remainsName, desc, screenPos);
            _inspectPanel?.PositionNear(screenPos, GetViewport()?.GetVisibleRect().Size ?? new Vector2(480, 854));
            return;
        }

        // Check for a floor item at this tile
        var floorItem = _state.FloorItems.FirstOrDefault(i => i.X == gridX && i.Y == gridY);
        if (floorItem != null)
        {
            var itemInfo = ItemInspectView.From(floorItem, _state.IdentificationRegistry, _state.AppearancePool);
            _inspectPanel?.ShowItem(itemInfo);
            _inspectPanel?.PositionNear(screenPos, GetViewport()?.GetVisibleRect().Size ?? new Vector2(480, 854));
            return;
        }

        // Feature inspection (props, doors, traps, portals, etc.) — respects show_prop_inspect setting
        if (_showPropInspect)
        {
            // Check for feature entities (chests, signposts, murals, barrels, bone piles, etc.)
            var feature = _state.Features.FirstOrDefault(f => f.X == gridX && f.Y == gridY);
            if (feature != null)
            {
                var propId = ResolvePropInspectKey(feature);
                if (propId != null)
                {
                    var entry = UnderWarden.Logic.Content.PropDescriptionRegistry.Get(propId);
                    if (entry.HasValue)
                    {
                        _inspectPanel?.ShowFeature(entry.Value.Name, entry.Value.Description, screenPos);
                        _inspectPanel?.PositionNear(screenPos, GetViewport()?.GetVisibleRect().Size ?? new Vector2(480, 854));
                        return;
                    }
                }
            }

            // Check tile-based features (doors, portals, stairs, traps)
            var tileKind = _state.Map.GetTileKind(gridX, gridY);
            var tileKey = TileKindToInspectKey(tileKind);
            if (tileKey != null)
            {
                var entry = UnderWarden.Logic.Content.PropDescriptionRegistry.Get(tileKey);
                if (entry.HasValue)
                {
                    _inspectPanel?.ShowFeature(entry.Value.Name, entry.Value.Description, screenPos);
                    _inspectPanel?.PositionNear(screenPos, GetViewport()?.GetVisibleRect().Size ?? new Vector2(480, 854));
                    return;
                }
            }
        }

        // Nothing of interest at this tile — hide any existing panel
        _inspectPanel?.Hide();
    }

    /// <summary>
    /// Show a mural's full inscription text in the inspect panel.
    /// Called from Main.cs when a MuralExaminedEvent fires.
    /// Using the inspect panel (which supports autowrap) instead of the toast log prevents
    /// multi-line mural text from being dumped as a run-on message.
    /// </summary>
    public void ShowMuralInspect(string muralText)
    {
        if (_inspectPanel == null) return;
        var viewport = GetViewport()?.GetVisibleRect().Size ?? new Vector2(480, 854);
        _inspectPanel.ShowFeature("Ancient Inscription", muralText, viewport / 2);
        _inspectPanel.PositionNear(viewport / 2, viewport);
    }

    /// <summary>
    /// Determine the PropDescriptionRegistry key for a feature entity.
    /// Checks components in priority order: chest → signpost/mural → trap → destructible prop → portal.
    /// Returns null if no matching key can be resolved.
    /// </summary>
    private static string? ResolvePropInspectKey(Entity feature)
    {
        // Chest (may also have LockableComponent for locked chests)
        var chest = feature.Get<ChestComponent>();
        if (chest != null)
        {
            var lockable = feature.Get<LockableComponent>();
            if (lockable != null && lockable.IsLocked)
                return "__chest_locked";
            return chest.IsOpen ? "__chest_open" : "__chest_closed";
        }

        // Signpost
        var signpost = feature.Get<SignpostComponent>();
        if (signpost != null) return "__sign";

        // Mural
        var mural = feature.Get<MuralComponent>();
        if (mural != null) return "__mural";

        // Floor trap
        var trap = feature.Get<FloorTrapComponent>();
        if (trap != null) return TrapTypeToInspectKey(trap.TrapType);

        // Destructible prop (barrel, bookshelf, bone_pile) — use PropKind directly as registry key
        var destructible = feature.Get<DestructiblePropComponent>();
        if (destructible != null && !string.IsNullOrEmpty(destructible.PropKind))
            return destructible.PropKind;

        // Portal
        var portal = feature.Get<PortalComponent>();
        if (portal != null) return "__portal";

        return null;
    }

    /// <summary>
    /// Map a FloorTrapComponent.TrapType string to its PropDescriptionRegistry key.
    /// PoC-canonical trap type IDs are defined in FloorTrapComponent documentation.
    /// </summary>
    private static string? TrapTypeToInspectKey(string trapType) => trapType switch
    {
        "spike_trap"    => "__trap_spike",
        "web_trap"      => "__trap_web",
        "alarm_plate"   => "__trap_alarm",
        "root_trap"     => "__trap_root",
        "teleport_trap" => "__trap_teleport",
        "gas_trap"      => "__trap_gas",
        "fire_trap"     => "__trap_fire",
        "hole_trap"     => "__trap_hole",
        "acid_trap"     => "__trap_acid",
        _               => null,
    };

    /// <summary>
    /// Map a TileKind to its PropDescriptionRegistry key.
    /// Only tile kinds that have inspect-worthy content are covered — wall/floor/corridor return null.
    /// </summary>
    private static string? TileKindToInspectKey(TileKind kind) => kind switch
    {
        TileKind.Door       => "__door",
        TileKind.DoorOpen   => "__door",
        TileKind.LockedDoor => "__door_locked",
        TileKind.SecretDoor => "__secret_door",
        TileKind.StairDown  => "__stair_down",
        TileKind.StairUp    => "__stair_up",
        TileKind.Trap       => "__trap_spike",  // generic fallback for untyped Trap tiles
        _                   => null,
    };

    /// <summary>
    /// Enter possession targeting mode. Free action — no turn consumed.
    /// Taps during targeting resolve to Possess(host) or cancel.
    /// No-op if already possessing, in targeting, or if state is not ready.
    /// </summary>
    public void StartPossessionTargeting()
    {
        if (_state == null || Phase != GamePhase.WaitingForInput) return;
        if (!ReferenceEquals(_state.ControlledEntity, _state.Player)) return; // already possessing

        _possessionTargetingActive = true;
        // Fire the free action so TurnController sees it (updates state if needed).
        ExecuteTurn(PlayerAction.EnterPossessionTargeting());
        _toastLog?.AddMessage("Tap a monster to possess. Tap Cancel to abort.");
        Diag.Log("GameController: entered possession targeting mode");
    }

    /// <summary>Cancel possession targeting without consuming a turn.</summary>
    public void CancelPossessionTargeting()
    {
        if (!_possessionTargetingActive) return;
        _possessionTargetingActive = false;
        _toastLog?.AddMessage("Possession cancelled.");
        Diag.Log("GameController: cancelled possession targeting");
    }

    /// <summary>Voluntarily exit active possession. Free action — no turn consumed.</summary>
    public void ExitPossessionAction()
    {
        if (_state == null || Phase != GamePhase.WaitingForInput) return;
        if (ReferenceEquals(_state.ControlledEntity, _state.Player)) return; // not possessing
        OnActionChosen(PlayerAction.ExitPossession());
    }

    /// <summary>
    /// Begin auto-explore. Cancels any queued click-to-move path, activates
    /// AutoExploreSystem, and immediately steps the first move.
    /// No-op if state is not ready or game is not waiting for input.
    /// </summary>
    public void StartAutoExplore()
    {
        if (_state == null || Phase != GamePhase.WaitingForInput) return;
        Diag.Log("StartAutoExplore");
        _pendingPath = null;
        _autoExploreMode = true;
        _animator!.SpeedMultiplier = AutoExploreSpeedMultiplier;
        PlayerCamera.ActiveMode = CameraMode.HardFollow;
        AutoExploreSystem.Activate(_state);
        AdvanceAutoExplore();
    }

    private void AdvanceAutoExplore()
    {
        if (!_autoExploreMode || _state == null) return;
        var action = AutoExploreSystem.GetNextAction(_state);
        if (action == null)
        {
            _autoExploreMode = false;
            _animator!.SpeedMultiplier = 1.0f;
            PlayerCamera.ActiveMode = CameraMode.Deadzone;
            Phase = GamePhase.WaitingForInput;
            _input.SetAcceptingInput(true);
            DrainHeldInput();

            var reason = _state.Player.Get<AutoExploreState>()?.StopReason;
            if (reason != null)
                _toastLog?.AddMessage($"[color=#aaaaaa]Explore: {reason}[/color]");

            return;
        }
        ExecuteTurn(action);
    }

    private void OnActionChosen(PlayerAction action)
    {
        if (_state == null || Phase != GamePhase.WaitingForInput) return;

        // Any manual tap cancels auto-explore.
        if (_autoExploreMode)
            Diag.Log($"OnActionChosen: action={action.Kind}, cancelled auto-explore");
        _autoExploreMode = false;
        if (_animator != null) _animator.SpeedMultiplier = 1.0f;
        PlayerCamera.ActiveMode = CameraMode.Deadzone;
        if (_state != null)
            if (_state.Player.Get<AutoExploreState>() is { } ae) ae.IsActive = false;

        if (action.Kind == PlayerAction.ActionKind.Move
            && action.TargetX.HasValue && action.TargetY.HasValue)
        {
            int tx = action.TargetX.Value, ty = action.TargetY.Value;
            int dist = _state.Player.ChebyshevDistanceTo(tx, ty);

            if (dist > 1)
            {
                // Distant tap: compute A* path and queue all steps.
                var path = Pathfinder.AStar(
                    _state.Map,
                    _state.Player.X, _state.Player.Y,
                    tx, ty,
                    _state.Player,
                    canPassDoors: true);

                if (path == null || path.Count == 0) return; // Unreachable — silently ignore

                _pendingPath = new Queue<(int, int)>(path);
                _pathInterruptHp = _state.PlayerFighter.Hp;
                _animator!.SpeedMultiplier = ClickToMoveSpeedMultiplier;

                // Snapshot which monsters are currently visible so CheckPathInterrupts can
                // distinguish pre-existing visible monsters (safe to ignore) from newly-spotted
                // ones (should interrupt). Phase 2 FOV is live — populate from map visibility.
                _pathStartVisibleMonsterIds.Clear();
                foreach (var m in _state.AliveMonsters)
                    if (_state.Map.IsVisible(m.X, m.Y))
                        _pathStartVisibleMonsterIds.Add(m.Id);

                // Dequeue and execute the first step immediately.
                var (nx, ny) = _pendingPath.Dequeue();
                action = PlayerAction.MoveTo(nx, ny);
            }
            else
            {
                // Adjacent tap — clear any queued path, just take the step normally.
                _pendingPath = null;
            }
        }
        else
        {
            // Non-Move action (Attack, UseItem, Wait, Descend) — cancel any queued path.
            _pendingPath = null;
        }

        ExecuteTurn(action);
    }

    /// <summary>
    /// Execute one turn: process through TurnController, drive animations.
    /// Extracted from OnActionChosen so path continuation in OnAnimationComplete
    /// can reuse it without duplicating the turn-execution logic.
    /// </summary>
    private void ExecuteTurn(PlayerAction action)
    {
        Phase = GamePhase.Processing;
        _input.SetAcceptingInput(false);

        Diag.Log($"ExecuteTurn START action={action.Kind}");
        Diag.Event("ExecuteTurn", new { action = action.Kind.ToString(), turnCount = _state!.TurnCount });
        Diag.Mem("  pre-ProcessTurn");

        // Snapshot what the quick-slot bar currently shows, so the post-turn comparison can
        // detect a consume the event list doesn't describe (see the inventoryChanged block below).
        var slotsBefore    = QuickSlotModel.Build(_state!);
        var mainHandBefore = QuickSlotModel.MainHandItemId(_state!);

        var result = TurnController.ProcessTurn(_state!, action, _monsterFactory,
            portalEntityFactory: _portalEntityFactory);
        Diag.Log($"  ProcessTurn done: {result.Events.Count} events, gameOver={result.GameOver}");
        foreach (var evt in result.Events)
            Diag.Log($"    evt: {evt.GetType().Name}");
        Diag.Mem("  post-ProcessTurn");

        Diag.Log("  -> TurnCompleted");
        TurnCompleted?.Invoke(result);
        Diag.Log("  <- TurnCompleted");

        // Single pass over events — avoids 5 separate LINQ allocations per turn.
        bool inventoryChanged = false;
        bool equipmentChanged = false;
        bool identificationChanged = false;
        _pendingDescend = null;
        foreach (var evt in result.Events)
        {
            if (evt is DeathEvent dead)
                _entitySprites?.RemoveEntity(dead.ActorId);
            else if (evt is IdentificationEvent)
                { inventoryChanged = true; identificationChanged = true; }
            else if (evt is PickUpEvent pick)
                { _itemSprites?.RemoveItem(pick.ItemId); inventoryChanged = true; }
            else if (evt is DropEvent drop)
            {
                var dropped = _state!.FloorItems.FirstOrDefault(i => i.Id == drop.ItemId);
                if (dropped != null) _itemSprites?.CreateSprite(dropped);
                inventoryChanged = true;
            }
            else if (evt is HealEvent)
                inventoryChanged = true;
            else if (evt is ThrowEvent throwEvt)
            {
                inventoryChanged = true;
                // Weapons/junk land on the ground — spawn a floor sprite at the landing tile.
                if (throwEvt.ItemLandsOnGround)
                {
                    var landed = _state!.FloorItems.FirstOrDefault(i => i.Id == throwEvt.ItemId);
                    if (landed != null) _itemSprites?.CreateSprite(landed);
                }
            }
            else if (evt is EquipEvent or UnequipEvent)
                { inventoryChanged = true; equipmentChanged = true; }
            else if (evt is DescendEvent desc)
                _pendingDescend = desc;
        }

        // Re-sync floor item visibility: CreateSprite sets Visible=false by default, but
        // TurnCompleted (in Main.cs) ran UpdateVisibility before the event loop above. Any
        // sprites created by DropEvent/ThrowEvent during that loop would never be shown.
        _itemSprites?.UpdateVisibility(_state!);

        // Handle split and corrosion toast messages
        foreach (var evt in result.Events)
        {
            if (evt is SplitEvent splitEvt)
            {
                // Remove the original sprite and spawn sprites for all children
                _entitySprites?.RemoveEntity(splitEvt.OriginalId);
                foreach (var childId in splitEvt.ChildIds)
                {
                    var child = _state!.Monsters.FirstOrDefault(m => m.Id == childId);
                    if (child != null) _entitySprites?.SpawnMonster(child);
                }
            }
            else if (evt is RaiseDeadEvent raised)
            {
                // A necromancer/scroll raised a corpse: the SAME entity is transformed in-place from
                // corpse back to a living monster (RaiseDeadResolver). Its corpse sprite is freed by
                // the per-turn CorpseSpriteManager.Sync (the entity left state.Corpses); here we spawn
                // its LIVE sprite so the raised monster is visible immediately — the corpse visual
                // becomes the raised monster cleanly. RemoveEntity first guards against any stale node.
                var entity = _state!.Monsters.FirstOrDefault(m => m.Id == raised.RaisedEntityId);
                if (entity != null)
                {
                    _entitySprites?.RemoveEntity(raised.RaisedEntityId);
                    _entitySprites?.SpawnMonster(entity);
                }
            }
            else if (evt is CorrosionEvent corrEvt)
            {
                // Orange corrosion toast: "The Slime corrodes your Dagger! [75%]"
                int pct = corrEvt.BaseDamageMax > 0
                    ? (int)Math.Round((double)corrEvt.NewDamageMax / corrEvt.BaseDamageMax * 100)
                    : 100;
                _toastLog?.AddMessage(
                    $"[color=#ff8800]The {corrEvt.MonsterName} corrodes your {corrEvt.WeaponName}! [{pct}%][/color]");
                equipmentChanged = true; // weapon stats changed — refresh equipment panel
            }
        }

        // The event flags above drive sprite work that genuinely needs per-event data, but they
        // are the WRONG signal for "should the quick-slot bar redraw?" — that list has to name
        // every event type that can imply a consume, and it silently missed SpellEvent /
        // WandUseEvent. Drinking a buff potion (invisibility, speed) resolves through
        // ResolveSpellAction, which decrements the stack and emits neither: the bar kept showing
        // an item already drunk until some later turn happened to fire a listed event, and the
        // stale slot hit-tested to an entity no longer in the inventory, so tapping it did
        // nothing. Compare the bar's actual contents instead — no enumeration to drift.
        if (!inventoryChanged
            && (mainHandBefore != QuickSlotModel.MainHandItemId(_state!)
                || !slotsBefore.SequenceEqual(QuickSlotModel.Build(_state!))))
            inventoryChanged = true;

        if (identificationChanged)
            _itemSprites?.RefreshIdentifiedSprites(_state!);
        if (inventoryChanged)
            _inventoryPanel?.Refresh(_state!);
        if (equipmentChanged && _equipmentPanel?.Visible == true)
            _equipmentPanel?.Refresh(_state!);

        Diag.Log($"  -> PlayTurn (gameOver={result.GameOver})");
        if (result.GameOver)
        {
            Phase = GamePhase.GameOver;
            ClearHeldInput();
            _animator?.PlayTurn(result);
        }
        else
        {
            Phase = GamePhase.Animating;
            _animator?.PlayTurn(result);
        }
        Diag.Log("ExecuteTurn END");
    }

    private void OnAnimationComplete()
    {
        Diag.Log("OnAnimationComplete START");
        Diag.Event("AnimationComplete", new { phase = Phase.ToString() });
        if (_state == null) return;

        Diag.Log("  OAC: UpdatePositions");
        _entitySprites?.UpdatePositions(_state);

        if (Phase == GamePhase.GameOver)
        {
            Diag.Log("  OAC: GameOver -> GameEnded");
            // A Weighing victory resolves via GameState.Ending (the player is alive), not the
            // scenario all-monsters-dead formula. A loss Ending forces a defeat regardless. Off the
            // Weighing (Ending == None), fall back to the scenario PlayerWon.
            bool won = _state.IsDungeonVictory
                || (_state.Ending == UnderWarden.Logic.Endgame.EndingType.None && _state.PlayerWon);
            GameEnded?.Invoke(won);
            return;
        }

        if (_pendingDescend != null)
        {
            Diag.Log("  OAC: FloorTransition");
            var descend = _pendingDescend;
            _pendingDescend = null;
            // A press held on the old floor must not surface on the new one — same items,
            // entirely different situation. This path returns before the drain below.
            ClearHeldInput();
            FloorTransitionRequested?.Invoke(descend.NewDepth);
            return;
        }

        if (_autoExploreMode)
        {
            Diag.Log("  OAC: AutoExplore");
            AdvanceAutoExplore();
            return;
        }

        if (_pendingPath != null && _pendingPath.Count > 0)
        {
            Diag.Log($"  OAC: PathContinue ({_pendingPath.Count} steps left)");
            if (!CheckPathInterrupts())
            {
                var (nx, ny) = _pendingPath.Dequeue();
                Diag.Log($"  OAC: PathStep ({nx},{ny})");
                ExecuteTurn(PlayerAction.MoveTo(nx, ny));
                return;
            }
            Diag.Log("  OAC: PathInterrupt");
            _pendingPath = null;
            _animator!.SpeedMultiplier = 1.0f;
        }
        else if (_pendingPath != null && _pendingPath.Count == 0)
        {
            Diag.Log("  OAC: PathComplete");
            _pendingPath = null;
            _animator!.SpeedMultiplier = 1.0f;
        }

        // Auto-continue portal placement: if the entrance was just placed this turn,
        // immediately enter step-2 targeting so the player picks the exit without
        // having to tap the wand again. _pendingPortalWand persists from step 1.
        if (_pendingPortalWand != null &&
            PortalSystem.GetPortalCastStep(_pendingPortalWand) == PortalCastStep.EntrancePlaced)
        {
            var spell = _pendingPortalWand.Get<SpellEffect>();
            if (spell != null)
            {
                EnterTargetingMode(new TargetingState
                {
                    Item  = _pendingPortalWand,
                    Spell = spell,
                    Mode  = TargetingMode.Location,
                    Range = 0,
                }, showGenericToast: false);
                _toastLog?.AddMessage("Portal entrance placed. Tap a tile for the exit. Tap yourself to cancel.");
                Diag.Log("  OAC: PortalStep2Targeting");
                return;
            }
        }

        Diag.Log("  OAC: WaitingForInput");
        Phase = GamePhase.WaitingForInput;
        _input.SetAcceptingInput(true);

        // The turn is over — release anything the player pressed while it was resolving.
        // This is the end of a single animation AND the end of a whole path/auto-explore
        // run, because both funnel back through here.
        DrainHeldInput();
    }

    /// <summary>
    /// Returns true if an in-progress path should be stopped.
    ///
    /// Two interrupt conditions:
    /// 1. Player took damage since the path started (any hit stops pathing).
    /// 2. A monster that was NOT visible when the path started is now visible.
    ///    Pre-existing visible monsters are in the snapshot and don't interrupt —
    ///    this allows click-to-move to work usefully in rooms that already have visible enemies.
    ///
    /// Note: until Phase 2 (FOV) lands, GameMap has no IsVisible API and the monster
    /// snapshot is always empty, so condition 2 is a no-op. The structure is wired now
    /// and will activate automatically when Phase 2 adds IsVisible to GameMap.
    /// </summary>
    private bool CheckPathInterrupts()
    {
        if (_state == null) return true;

        // Damage taken since path started.
        if (_state.PlayerFighter.Hp < _pathInterruptHp) return true;

        // New monster entered FOV that wasn't visible when the path started.
        // Phase 2 FOV is live — interrupt if a monster not in the start snapshot is now visible.
        foreach (var m in _state.AliveMonsters)
            if (_state.Map.IsVisible(m.X, m.Y) && !_pathStartVisibleMonsterIds.Contains(m.Id))
                return true;

        return false;
    }
}
