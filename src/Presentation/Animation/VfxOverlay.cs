using UnderWarden.Logic.Core;
using UnderWarden.Presentation.Map;
using Godot;

namespace UnderWarden.Presentation.Animation;

/// <summary>
/// Renders visual effects (area flashes, path trails, projectile travel, status indicators)
/// on top of the game world using two pre-allocated node pools.
///
/// Design constraints:
/// - Sprite2D pool (64 nodes) for sprite-based FX (fireball, lightning, dragon fart).
/// - ColorRect pool (16 nodes) for area tile flashes (background color highlights).
///   ColorRects cannot display textures — kept for tile flash use only.
/// - Pool nodes are allocated once at construction — never freed, just hidden/shown.
///   Avoids per-cast allocation and GCHandle leaks from Callable.From lambdas.
/// - Speed multiplier is passed at each call site — VfxOverlay never stores it.
/// - Cleanup uses TweenProperty to set Visible=false — NOT Callable.From lambdas.
/// - Label nodes (status indicators, glyph projectiles) are single-use, freed via
///   TweenCallback(Callable.From(static)) — acceptable since no closure capture.
///
/// ZIndex per pooled node: renderer.GetTileSortOrder(x,y) + 5 (above tiles, below UI).
/// </summary>
public sealed class VfxOverlay
{
    private const int SpritePoolSize = 64;
    private const int RectPoolSize = 16;

    // Derived from the renderer's TileWidth so ColorRects auto-scale with tile size.
    // For 24px tiles (TopDownRenderer): TileHalfSize = 12 → 24×24 rect.
    // For 32px tiles (IsometricRenderer): TileHalfSize = 16 → 32×32 rect.
    private int TileHalfSize => _renderer.TileWidth / 2;

    private readonly Node2D _layer;
    private readonly IMapRenderer _renderer;

    // Sprite2D pool — for sprite-based directional and cycling FX.
    private readonly Sprite2D[] _spritePool;
    private int _spriteNext;

    // ColorRect pool — for area tile color flashes (no texture needed).
    private readonly ColorRect[] _rectPool;
    private int _rectNext;

    public VfxOverlay(Node2D vfxLayerNode, IMapRenderer renderer)
    {
        _layer = vfxLayerNode;
        _renderer = renderer;
        _spritePool = new Sprite2D[SpritePoolSize];
        _rectPool = new ColorRect[RectPoolSize];

        // Pre-allocate Sprite2D pool — pixel art, centered, hidden by default.
        for (int i = 0; i < SpritePoolSize; i++)
        {
            var sprite = new Sprite2D
            {
                Visible = false,
                Centered = true,
                ZAsRelative = false,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            };
            _layer.AddChild(sprite);
            _spritePool[i] = sprite;
        }

        // Pre-allocate ColorRect pool — for tile flash effects.
        // Size is set in BorrowRect (after renderer is stored) so it reflects TileWidth.
        for (int i = 0; i < RectPoolSize; i++)
        {
            var rect = new ColorRect
            {
                Visible = false,
                ZAsRelative = false,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _layer.AddChild(rect);
            _rectPool[i] = rect;
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Flash all given tiles simultaneously with <paramref name="color"/>,
    /// fading out over <paramref name="durationSecs"/>.
    /// Uses the ColorRect pool — no sprite, pure color highlight.
    /// </summary>
    public void AppendAreaEffect(Tween tween, IReadOnlyList<(int X, int Y)> tiles,
        Color color, float durationSecs)
    {
        if (tiles.Count == 0) return;

        // First tile is a sequential step (advances tween timeline to "area starts").
        // All subsequent tiles are parallel so they all flash simultaneously.
        bool first = true;
        foreach (var (x, y) in tiles)
        {
            var rect = BorrowRect(x, y, color);

            // Reveal when the area step is reached.
            if (first)
                tween.TweenProperty(rect, "visible", true, 0.0f);
            else
                tween.Parallel().TweenProperty(rect, "visible", true, 0.0f);

            // Fade alpha to 0 in parallel with the reveal (reveal is 0-duration so effectively simultaneous).
            tween.Parallel().TweenProperty(rect, "modulate:a", 0.0f, durationSecs)
                 .SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);

            first = false;
        }
    }

    /// <summary>
    /// Flash tiles sequentially along a path with a solid color (glyph fallback path).
    /// Each tile lights up for <paramref name="perTileSecs"/> then fades before the next begins.
    /// Uses the ColorRect pool.
    /// </summary>
    public void AppendPathEffect(Tween tween, IReadOnlyList<(int X, int Y)> tiles,
        Color color, float perTileSecs)
    {
        if (tiles.Count == 0) return;

        foreach (var (x, y) in tiles)
        {
            var rect = BorrowRect(x, y, color);

            float holdTime = perTileSecs * 0.3f;
            float fadeTime = perTileSecs * 0.7f;

            // Reveal tile when path reaches it (sequential — each tile waits for previous).
            tween.TweenProperty(rect, "visible", true, 0.0f);
            tween.TweenInterval(holdTime);
            tween.TweenProperty(rect, "modulate:a", 0.0f, fadeTime)
                 .SetEase(Tween.EaseType.Out);
        }
    }

    /// <summary>
    /// Animate cycling sprites sequentially along a path (e.g. lightning bolt).
    /// Each tile in the path gets sprite cycleSprites[i % cycleSprites.Length].
    /// If impactSprite is non-null, the final tile shows it at 1.3x scale for 0.2s.
    /// Uses the Sprite2D pool.
    /// </summary>
    public void AppendPathEffect(Tween tween, IReadOnlyList<(int X, int Y)> tiles,
        string[] cycleSprites, string? impactSprite, float perTileSecs)
    {
        if (tiles.Count == 0 || cycleSprites.Length == 0) return;

        for (int i = 0; i < tiles.Count; i++)
        {
            var (x, y) = tiles[i];
            bool isLast = i == tiles.Count - 1;
            string path = (isLast && impactSprite != null) ? impactSprite : cycleSprites[i % cycleSprites.Length];

            var sprite = BorrowSprite(x, y);
            LoadTextureOrFallback(sprite, path);

            if (isLast && impactSprite != null)
            {
                // Impact tile: reveal at scale pop, fade + scale normalize in parallel.
                sprite.Scale = Vector2.One * 1.3f;
                tween.TweenProperty(sprite, "visible", true, 0.0f);
                tween.TweenProperty(sprite, "modulate:a", 0.0f, 0.2f)
                     .SetEase(Tween.EaseType.Out);
                tween.Parallel().TweenProperty(sprite, "scale", Vector2.One, 0.2f)
                     .SetEase(Tween.EaseType.Out);
            }
            else
            {
                float holdTime = perTileSecs * 0.3f;
                float fadeTime = perTileSecs * 0.7f;

                // Reveal when path reaches this tile, hold briefly, then fade.
                tween.TweenProperty(sprite, "visible", true, 0.0f);
                tween.TweenInterval(holdTime);
                tween.TweenProperty(sprite, "modulate:a", 0.0f, fadeTime)
                     .SetEase(Tween.EaseType.Out);
            }
        }
    }

    /// <summary>
    /// Animate a sprite projectile travelling from <paramref name="from"/> to
    /// <paramref name="to"/>. Picks the correct directional sprite from the 8-element
    /// octant array [E, SE, S, SW, W, NW, N, NE] based on screen-space direction.
    /// Uses the Sprite2D pool.
    /// </summary>
    public void AppendTravelEffect(Tween tween, (int X, int Y) from, (int X, int Y) to,
        string[] directionalSprites, float speedPerTile)
    {
        var fromScreen = _renderer.GridToScreenCenter(from.X, from.Y);
        var toScreen   = _renderer.GridToScreenCenter(to.X, to.Y);

        int tileDist = Math.Max(1,
            Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y));
        float travelDur = speedPerTile * tileDist;

        float dx = toScreen.X - fromScreen.X;
        float dy = toScreen.Y - fromScreen.Y;
        int octant = GetOctant(dx, dy);
        string spritePath = directionalSprites[octant % directionalSprites.Length];

        var sprite = BorrowSprite(from.X, from.Y);
        LoadTextureOrFallback(sprite, spritePath);
        sprite.Position = fromScreen;

        // Reveal when the travel step is reached (not before), then move, then hide.
        tween.TweenProperty(sprite, "visible", true, 0.0f);
        tween.TweenProperty(sprite, "position", toScreen, travelDur)
             .SetEase(Tween.EaseType.InOut)
             .SetTrans(Tween.TransitionType.Linear);
        tween.TweenProperty(sprite, "visible", false, 0.0f);
    }

    /// <summary>
    /// Glyph-based projectile travel (fallback when no sprite available).
    /// The Label is a single-use node freed after travel via TweenCallback.
    /// </summary>
    public void AppendTravelEffect(Tween tween, (int X, int Y) from, (int X, int Y) to,
        Color color, string glyph, float speedPerTile)
    {
        var fromScreen = _renderer.GridToScreenCenter(from.X, from.Y);
        var toScreen   = _renderer.GridToScreenCenter(to.X, to.Y);

        int tileDist = Math.Max(1,
            Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y));
        float travelDur = speedPerTile * tileDist;

        var label = new Label
        {
            Text = glyph,
            Modulate = color,
            Position = fromScreen,
            ZIndex = 10,
            ZAsRelative = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _layer.AddChild(label);

        tween.TweenProperty(label, "position", toScreen, travelDur)
             .SetEase(Tween.EaseType.InOut)
             .SetTrans(Tween.TransitionType.Linear);

        // Callable.From(static-ish) is acceptable here — no non-GodotObject closure.
        tween.TweenCallback(Callable.From(() => label.SafeFree()));
    }

    /// <summary>
    /// Fireball burst: 8 Sprite2D nodes spread outward from <paramref name="center"/>
    /// in all 8 compass directions, each showing the appropriate directional sprite.
    /// All 8 run in parallel, fading alpha 1→0 over <paramref name="durationSecs"/>.
    /// Each travels ~2 tiles outward from center during the fade.
    /// </summary>
    public void AppendDirectionalBurst(Tween tween, (int X, int Y) center,
        string[] directionalSprites, float durationSecs)
    {
        var centerScreen = _renderer.GridToScreenCenter(center.X, center.Y);
        // 2 tile-widths of screen travel — readable at any zoom level.
        float spreadPx = _renderer.TileWidth * 2f;

        // Octant directions in screen-space: [E, SE, S, SW, W, NW, N, NE]
        // Angles in radians matching GetOctant output order.
        var angles = new float[]
        {
            0f,                          // 0 = E
            MathF.PI / 4f,               // 1 = SE
            MathF.PI / 2f,               // 2 = S
            3f * MathF.PI / 4f,          // 3 = SW
            MathF.PI,                    // 4 = W
            -3f * MathF.PI / 4f,         // 5 = NW
            -MathF.PI / 2f,              // 6 = N
            -MathF.PI / 4f,              // 7 = NE
        };

        bool first = true;
        for (int i = 0; i < 8; i++)
        {
            string spritePath = directionalSprites[i % directionalSprites.Length];
            var sprite = BorrowSprite(center.X, center.Y);
            LoadTextureOrFallback(sprite, spritePath);
            sprite.Position = centerScreen;
            sprite.Modulate = Colors.White;

            float angle = angles[i];
            var endPos = centerScreen + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * spreadPx;

            // Reveal all 8 sprites simultaneously when burst step is reached.
            // First sprite is sequential (advances timeline to "burst starts"), rest are parallel.
            if (first)
                tween.TweenProperty(sprite, "visible", true, 0.0f);
            else
                tween.Parallel().TweenProperty(sprite, "visible", true, 0.0f);

            // Movement: parallel with reveal (and with each other).
            tween.Parallel().TweenProperty(sprite, "position", endPos, durationSecs)
                 .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);

            // Fade in parallel with movement.
            tween.Parallel().TweenProperty(sprite, "modulate:a", 0.0f, durationSecs)
                 .SetEase(Tween.EaseType.In);

            first = false;
        }
    }

    /// <summary>
    /// Dragon fart cone: each tile in <paramref name="tiles"/> gets a sprite determined
    /// by the screen-space direction from <paramref name="origin"/> to that tile.
    /// All tiles appear simultaneously, fading alpha 1→0 over <paramref name="durationSecs"/>.
    /// Uses the Sprite2D pool.
    /// </summary>
    public void AppendDirectionalAreaEffect(Tween tween, (int X, int Y) origin,
        IReadOnlyList<(int X, int Y)> tiles, string[] directionalSprites, float durationSecs)
    {
        if (tiles.Count == 0) return;

        var originScreen = _renderer.GridToScreenCenter(origin.X, origin.Y);

        bool first = true;
        foreach (var (x, y) in tiles)
        {
            var tileScreen = _renderer.GridToScreenCenter(x, y);
            float dx = tileScreen.X - originScreen.X;
            float dy = tileScreen.Y - originScreen.Y;

            // For tiles exactly at origin, default to E (index 0).
            int octant = (dx == 0f && dy == 0f) ? 0 : GetOctant(dx, dy);
            string spritePath = directionalSprites[octant % directionalSprites.Length];

            var sprite = BorrowSprite(x, y);
            LoadTextureOrFallback(sprite, spritePath);
            sprite.Modulate = Colors.White;

            // Reveal all cone tiles simultaneously when cone step is reached.
            // First is sequential (advances timeline), rest are parallel.
            if (first)
                tween.TweenProperty(sprite, "visible", true, 0.0f);
            else
                tween.Parallel().TweenProperty(sprite, "visible", true, 0.0f);

            // Fade in parallel with reveal (and with each other).
            tween.Parallel().TweenProperty(sprite, "modulate:a", 0.0f, durationSecs)
                 .SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);

            first = false;
        }
    }

    /// <summary>
    /// Display a floating glyph indicator at <paramref name="screenPos"/>,
    /// drifting upward and fading over <paramref name="durationSecs"/>.
    /// Used for per-entity status effect notifications (e.g. "z" for sleep).
    /// </summary>
    public void AppendStatusIndicator(Tween tween, Vector2 screenPos, string glyph,
        Color color, float durationSecs)
    {
        var label = new Label
        {
            Text = glyph,
            Modulate = color,
            Position = screenPos,
            ZIndex = 12,
            ZAsRelative = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _layer.AddChild(label);

        var endPos = screenPos + new Vector2(0, -8f);

        tween.TweenProperty(label, "position", endPos, durationSecs)
             .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(label, "modulate:a", 0.0f, durationSecs)
             .SetEase(Tween.EaseType.In);

        tween.TweenCallback(Callable.From(() => label.SafeFree()));
    }

    /// <summary>
    /// Hide all pooled nodes immediately and reset modulate. Call on floor transition.
    /// </summary>
    public void ClearAll()
    {
        foreach (var sprite in _spritePool)
        {
            sprite.Visible = false;
            sprite.Modulate = Colors.White;
            sprite.Scale = Vector2.One;
        }
        foreach (var rect in _rectPool)
        {
            rect.Visible = false;
            rect.Modulate = Colors.White;
        }
    }

    // ── Pool management ────────────────────────────────────────────────────────

    /// <summary>Borrow next Sprite2D from the pool and position it at the given grid tile.</summary>
    private Sprite2D BorrowSprite(int gridX, int gridY)
    {
        var sprite = _spritePool[_spriteNext];
        _spriteNext = (_spriteNext + 1) % SpritePoolSize;

        var screenCenter = _renderer.GridToScreenCenter(gridX, gridY);
        sprite.Position = screenCenter;
        sprite.Modulate = Colors.White;
        sprite.Scale = Vector2.One;
        sprite.ZIndex = _renderer.GetTileSortOrder(gridX, gridY) + 5;
        // Do NOT set Visible=true here — each caller controls when visibility starts
        // so nodes don't appear before their tween step is reached.
        sprite.Visible = false;

        return sprite;
    }

    /// <summary>Borrow next ColorRect from the pool and position it at the given grid tile.</summary>
    private ColorRect BorrowRect(int gridX, int gridY, Color color)
    {
        var rect = _rectPool[_rectNext];
        _rectNext = (_rectNext + 1) % RectPoolSize;

        int half = TileHalfSize;
        var screenCenter = _renderer.GridToScreenCenter(gridX, gridY);
        rect.Size = new Vector2(half * 2, half * 2);
        rect.Position = screenCenter - new Vector2(half, half);
        rect.Modulate = new Color(color, 0.8f);
        rect.ZIndex = _renderer.GetTileSortOrder(gridX, gridY) + 5;
        // Do NOT set Visible=true here — callers reveal via TweenProperty when the step is reached.
        rect.Visible = false;

        return rect;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute octant index from screen-space direction vector (Y increases downward).
    /// Returns index into [E, SE, S, SW, W, NW, N, NE] array.
    /// </summary>
    private static int GetOctant(float screenDx, float screenDy)
    {
        double angle = Math.Atan2(screenDy, screenDx); // -π to π
        int octant = (int)Math.Round(angle / (Math.PI / 4));
        return ((octant % 8) + 8) % 8;
    }

    /// <summary>
    /// Load a texture from <paramref name="path"/> and assign it to the sprite.
    /// If the resource doesn't exist or fails to load, set Texture = null silently
    /// rather than crashing — maintains fallback resilience.
    /// </summary>
    private static void LoadTextureOrFallback(Sprite2D sprite, string path)
    {
        // ResourceLoader.Exists checks without throwing on missing files.
        if (ResourceLoader.Exists(path))
            sprite.Texture = GD.Load<Texture2D>(path);
        else
            sprite.Texture = null;
    }
}
