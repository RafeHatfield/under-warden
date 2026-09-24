using UnderWarden.Logic.ECS;
using UnderWarden.Presentation.UI;
using Godot;

namespace UnderWarden.Presentation.Map;

/// <summary>
/// Controls how the camera follows the player.
/// HardFollow — player is always centred. Map moves every step.
/// Deadzone   — camera stays fixed while the player is within the inner safe zone;
///              only scrolls when the player nears the viewport edge.
/// </summary>
public enum CameraMode { HardFollow, Deadzone }

/// <summary>
/// Player-following camera. Positions and scales GameView so the player tile
/// stays centered in the playable viewport area — the strip between the HUD
/// margins at the top and bottom of the screen.
///
/// Replaces the old CenterView approach, which fit the entire map into the
/// viewport. At 40x40 that produced ~12px tiles — illegible. A fixed zoom with
/// a following camera is the correct model for a mobile roguelike.
/// </summary>
public static class PlayerCamera
{
    // DefaultZoom is now sourced from the renderer. This constant stays for legacy call sites
    // that don't yet pass a renderer (e.g. tests). Will be removed when all call sites migrate.
    // Value matches TopDownRenderer.DefaultZoom (3.0f for 24px tiles at ~10 tiles wide on 720px).
    public const float DefaultZoom = 3.0f;

    // Phase 1 layout margins:
    //   Top: 90px StatusBar
    //   Bottom: 154px QuickSlotBar + 128px MenuButtons + 51px BottomSafeArea = 333px
    // The camera centers the player in the clear viewport strip between these two zones.
    public const float UiTopMargin    = 90f;
    public const float UiBottomMargin = 333f;

    /// <summary>
    /// Active camera mode. Defaults to Deadzone — best feel for turn-based movement.
    /// Set to HardFollow to compare side-by-side at runtime without recompiling.
    /// </summary>
    public static CameraMode ActiveMode { get; set; } = CameraMode.Deadzone;

    // Stored so we can Kill() before creating the next one.
    // Godot 4 tweens are not auto-freed on completion — must kill explicitly.
    private static Tween? _lastCameraTween;

    /// <summary>
    /// Cancel any in-flight camera tween without repositioning the camera.
    /// Call this when the user starts a drag so the tween doesn't fight their input.
    /// </summary>
    public static void CancelTween()
    {
        if (_lastCameraTween == null) return;
        _lastCameraTween.Kill();
        TweenTracker.Killed();
        _lastCameraTween = null;
    }

    /// <summary>
    /// Position and scale GameView so the player is centered in the available
    /// viewport area (accounting for HUD margins above and below).
    /// Instant snap — correct for floor loads and initial setup.
    /// Always hard-follows regardless of ActiveMode; floor transitions should
    /// always snap directly to the player position.
    /// </summary>
    public static void Update(Node2D gameView, Entity player, float zoom = DefaultZoom,
        IMapRenderer? renderer = null)
    {
        // Kill any in-flight camera tween from the previous floor before snapping.
        // Without this, the stale tween overrides the position we set here and the
        // new floor appears grey (camera is animating to the old floor's position).
        if (_lastCameraTween != null) { _lastCameraTween.Kill(); TweenTracker.Killed(); _lastCameraTween = null; }

        var viewport = gameView.GetViewport().GetVisibleRect().Size;
        var r = renderer ?? (IMapRenderer)new TopDownRenderer();
        var playerScreen = r.GridToScreen(player.X, player.Y);

        float availableH = viewport.Y - UiTopMargin - UiBottomMargin;
        float centerY = UiTopMargin + availableH / 2f;

        gameView.Scale = new Vector2(zoom, zoom);
        gameView.Position = new Vector2(
            viewport.X / 2f - playerScreen.X * zoom,
            centerY - playerScreen.Y * zoom
        );
    }

    /// <summary>
    /// Smoothly animate GameView to the player's new position using a Tween.
    /// Use this for per-turn movement. Dispatches to the active camera mode:
    ///   Deadzone   — only scrolls when the player leaves the inner safe zone.
    ///   HardFollow — always re-centres on the player.
    /// animRoot is the Node that owns the Tween (typically Main).
    /// duration: seconds for the camera to reach target position.
    /// </summary>
    public static void AnimateTo(Node2D gameView, Entity player, Node animRoot,
        float duration = 0.12f, float zoom = DefaultZoom, IMapRenderer? renderer = null)
    {
        var r = renderer ?? (IMapRenderer)new TopDownRenderer();
        if (ActiveMode == CameraMode.Deadzone)
            AnimateToDeadzone(gameView, player, animRoot, duration, zoom, r);
        else
            AnimateToHardFollow(gameView, player, animRoot, duration, zoom, r);
    }

    // -------------------------------------------------------------------------
    // Private implementations
    // -------------------------------------------------------------------------

    /// <summary>
    /// Hard-follow: always re-centre on the player. Map moves every step.
    /// Extracted from the original AnimateTo so it can serve as both the
    /// HardFollow mode and the internal reference implementation.
    /// </summary>
    private static void AnimateToHardFollow(Node2D gameView, Entity player, Node animRoot,
        float duration, float zoom, IMapRenderer renderer)
    {
        var viewport = gameView.GetViewport().GetVisibleRect().Size;
        var playerScreen = renderer.GridToScreen(player.X, player.Y);

        float availableH = viewport.Y - UiTopMargin - UiBottomMargin;
        float centerY = UiTopMargin + availableH / 2f;

        var targetPos = new Vector2(
            viewport.X / 2f - playerScreen.X * zoom,
            centerY - playerScreen.Y * zoom
        );

        // Scale is fixed — only position animates
        gameView.Scale = new Vector2(zoom, zoom);

        if (_lastCameraTween != null) { _lastCameraTween.Kill(); TweenTracker.Killed(); }
        var tween = animRoot.CreateTween();
        _lastCameraTween = tween;
        TweenTracker.Created();
        tween.TweenProperty(gameView, "position", targetPos, duration)
             .SetEase(Tween.EaseType.Out)
             .SetTrans(Tween.TransitionType.Quad);
    }

    /// <summary>
    /// Deadzone / edge-scroll: camera stays fixed while the player is inside the
    /// inner safe zone (the central deadzoneRatio fraction of the usable viewport).
    /// Only re-centres when the player walks outside that zone.
    ///
    /// deadzoneRatio = 0.30 means 30% inset from each edge → inner 40% is safe.
    /// Larger ratio = less scrolling; smaller ratio = closer to hard-follow.
    /// </summary>
    private static void AnimateToDeadzone(Node2D gameView, Entity player, Node animRoot,
        float duration, float zoom, IMapRenderer renderer, float deadzoneRatio = 0.30f)
    {
        var viewport = gameView.GetViewport().GetVisibleRect().Size;
        var playerScreen = renderer.GridToScreen(player.X, player.Y);

        // Compute where the player currently appears on screen.
        // GameView is positioned so that: screenPos = gameView.Position + worldPos * zoom
        var playerOnScreen = gameView.Position + playerScreen * zoom;

        // Deadzone bounds — inset from each edge by deadzoneRatio of the usable area.
        float deadzoneX = viewport.X * deadzoneRatio;
        float deadzoneY = (viewport.Y - UiTopMargin - UiBottomMargin) * deadzoneRatio;

        float minX = deadzoneX;
        float maxX = viewport.X - deadzoneX;
        float minY = UiTopMargin + deadzoneY;
        float maxY = viewport.Y - UiBottomMargin - deadzoneY;

        bool outsideDeadzone = playerOnScreen.X < minX || playerOnScreen.X > maxX
                            || playerOnScreen.Y < minY || playerOnScreen.Y > maxY;

        // Player still in the safe zone — no scroll needed this turn.
        if (!outsideDeadzone) return;

        // Re-centre on the player.
        float availableH = viewport.Y - UiTopMargin - UiBottomMargin;
        float centerY = UiTopMargin + availableH / 2f;
        var targetPos = new Vector2(
            viewport.X / 2f - playerScreen.X * zoom,
            centerY - playerScreen.Y * zoom
        );

        gameView.Scale = new Vector2(zoom, zoom);
        if (_lastCameraTween != null) { _lastCameraTween.Kill(); TweenTracker.Killed(); }
        var tween = animRoot.CreateTween();
        _lastCameraTween = tween;
        TweenTracker.Created();
        tween.TweenProperty(gameView, "position", targetPos, duration)
             .SetEase(Tween.EaseType.Out)
             .SetTrans(Tween.TransitionType.Quad);
    }
}
