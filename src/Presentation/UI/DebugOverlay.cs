using Godot;
using System.Diagnostics;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Lightweight debug overlay. Polls every 1 second and renders an 8-line
/// summary of live game state:
///
///   [DBG] FPS:60  Phase:WaitForInput
///   Turn:47  Depth:2  Sprites:12/7
///   Tweens:0  Toasts:3  Inv:4/25
///   Heap:5MB  Last:Attack → 2 evts
///
/// Only created in debug builds (OS.IsDebugBuild() in Main._Ready).
/// SetGameState() must be called after each floor is built so the overlay
/// holds up-to-date references. All stored references are nullable so the
/// overlay degrades gracefully before the first floor loads or during transitions.
/// </summary>
public sealed partial class DebugOverlay : Control
{
    private const double PollInterval = 1.0;

    private Label? _label;
    private double _nextPollTime;

    // Weak references to game objects — set each floor via SetGameState().
    // Kept nullable so the overlay renders a reduced view before wiring is complete.
    private UnderWarden.Presentation.GameController? _controller;
    private UnderWarden.Logic.Core.GameState? _gameState;
    private UnderWarden.Presentation.Entities.EntitySpriteManager? _entitySprites;
    private UnderWarden.Presentation.Entities.ItemSpriteManager? _itemSprites;
    private ToastLog? _toastLog;

    // Cached summary of the last completed turn, updated via the TurnCompleted event.
    private string _lastTurnSummary = "—";

    public override void _Ready()
    {
        // Anchor to bottom-right so it doesn't overlap HUD or inventory.
        SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        OffsetLeft   = -300f;
        OffsetTop    = -115f;
        OffsetRight  = -8f;
        OffsetBottom = -8f;
        MouseFilter  = MouseFilterEnum.Ignore;
        ZIndex       = 200;

        var bg = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        _label = new Label
        {
            AutowrapMode        = TextServer.AutowrapMode.Off,
            HorizontalAlignment = HorizontalAlignment.Left,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontSizeOverride("font_size", 14);
        _label.AddThemeColorOverride("font_color", new Color(0.6f, 1f, 0.6f, 1f));
        _label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_label);

        SetProcess(true);
    }

    /// <summary>
    /// Provide the overlay with references to live game objects.
    /// Call from Main after SetupPresentation() each time a floor is built.
    /// Safe to call multiple times — unsubscribes from the previous controller first.
    /// </summary>
    public void SetGameState(
        UnderWarden.Presentation.GameController? controller,
        UnderWarden.Logic.Core.GameState? state,
        UnderWarden.Presentation.Entities.EntitySpriteManager? entitySprites,
        UnderWarden.Presentation.Entities.ItemSpriteManager? itemSprites,
        ToastLog? toastLog)
    {
        // Unsubscribe from previous controller to avoid double-firing or stale callbacks.
        if (_controller != null)
            _controller.TurnCompleted -= OnTurnCompleted;

        _controller    = controller;
        _gameState     = state;
        _entitySprites = entitySprites;
        _itemSprites   = itemSprites;
        _toastLog      = toastLog;
        _lastTurnSummary = "—";

        if (_controller != null)
            _controller.TurnCompleted += OnTurnCompleted;
    }

    public override void _Process(double delta)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (now < _nextPollTime) return;
        _nextPollTime = now + PollInterval;

        int fps          = (int)Engine.GetFramesPerSecond();
        string phase     = _controller != null ? _controller.Phase.ToString() : "—";
        int turn         = _gameState?.TurnCount ?? 0;
        int depth        = _gameState?.CurrentDepth ?? 0;
        int entitySprites = _entitySprites?.SpriteCount ?? 0;
        int itemSprites  = _itemSprites?.SpriteCount ?? 0;
        int tweens       = TweenTracker.ActiveCount;
        int toasts       = _toastLog?.ToastCount ?? 0;
        int invCount     = _gameState?.PlayerInventory?.Count ?? 0;
        // Capacity is a static const on Inventory — cannot be accessed via instance reference.
        int invCap       = UnderWarden.Logic.ECS.Inventory.Capacity;
        long gcHeapMb    = GC.GetTotalMemory(false) / (1024 * 1024);

        if (_label != null)
            _label.Text =
                $"[DBG] FPS:{fps}  Phase:{phase}\n" +
                $"Turn:{turn}  Depth:{depth}  Sprites:{entitySprites}/{itemSprites}\n" +
                $"Tweens:{tweens}  Toasts:{toasts}  Inv:{invCount}/{invCap}\n" +
                $"Heap:{gcHeapMb}MB  Last:{_lastTurnSummary}";
    }

    // -------------------------------------------------------------------------

    private void OnTurnCompleted(UnderWarden.Logic.Core.TurnResult result)
    {
        // Summarise the turn in a single short string — no allocations at poll time.
        // Example: "Attack → 3 evts"
        string kind = result.Events.Count > 0 ? result.Events[0].GetType().Name.Replace("Event", "") : "?";
        _lastTurnSummary = $"{kind} \u2192 {result.Events.Count} evts";
    }
}

/// <summary>
/// Static counter for live Tween objects. Incremented by TurnAnimator and
/// PlayerCamera when a Tween is created, decremented when Kill() is called.
/// Expected value: 0–2 between turns. Growing unboundedly = missing Kill() somewhere.
/// </summary>
public static class TweenTracker
{
    private static int _activeCount;
    public static int ActiveCount => _activeCount;

    public static void Created()   => System.Threading.Interlocked.Increment(ref _activeCount);
    public static void Killed()    => System.Threading.Interlocked.Decrement(ref _activeCount);
}
