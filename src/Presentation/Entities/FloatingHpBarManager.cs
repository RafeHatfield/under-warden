using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Presentation.Map;
using Godot;

namespace UnderWarden.Presentation.Entities;

/// <summary>
/// Manages small red HP bar nodes above damaged enemy sprites.
///
/// Not a Godot Node — this is a plain C# class that creates and owns child nodes
/// parented to the EntityLayer (world space). Bars move with the camera automatically
/// because they are children of a world-space Node2D.
///
/// Bars are only shown when:
///   - The monster is visible (in player FOV)
///   - The monster has taken damage (Hp &lt; MaxHp)
///
/// Bar node structure per monster:
///   Node2D (root, child of entityLayer)
///   ├── ColorRect (background, dark semi-transparent, 32×4px)
///   └── ColorRect (fill, red, (hp/maxHp)*32 × 4px)
///
/// Positioning: sprite.GlobalPosition + upward offset (-24px Y).
/// The bar root is offset left by half the bar width so it centers over the sprite.
/// </summary>
public sealed class FloatingHpBarManager
{
    private const float BarWidth    = 22f;  // slightly narrower than 24px tile
    private const float BarHeight   = 3f;
    private const float VerticalOffset = -16f; // just above top of 24px sprite (centered at origin)

    private static readonly Color BgColor   = new(0.1f, 0.1f, 0.1f, 0.8f);
    private static readonly Color FillColor = new(0.85f, 0.15f, 0.15f, 1f);

    private readonly Node2D _entityLayer;
    private readonly IMapRenderer _renderer;

    // Keyed by entity ID. Each value is a Node2D root containing bg + fill ColorRects.
    private readonly Dictionary<int, (Node2D Root, ColorRect Fill)> _bars = new();

    public FloatingHpBarManager(Node2D entityLayer, IMapRenderer renderer)
    {
        _entityLayer = entityLayer;
        _renderer = renderer;
    }

    /// <summary>
    /// Update HP bars for all alive monsters. Call after entity visibility has been updated
    /// for the turn (i.e. after EntitySpriteManager.UpdateVisibility).
    ///
    /// - Creates a bar if the monster is visible and damaged (Hp &lt; MaxHp).
    /// - Updates an existing bar's fill width to reflect current HP.
    /// - Hides/removes bars for full-HP or non-visible monsters.
    /// - Cleans up bars for monsters no longer in AliveMonsters.
    ///
    /// Bar position is derived from the monster's logical grid coordinate (not the sprite's
    /// current animated position) so the bar snaps to the correct tile immediately when
    /// the turn resolves rather than lagging one turn behind animated movement.
    /// </summary>
    public void Refresh(GameState state, EntitySpriteManager spriteManager)
    {
        // Build a set of IDs that are currently alive so we can prune stale bars below.
        var aliveIds = new HashSet<int>(state.AliveMonsters.Count);

        foreach (var monster in state.AliveMonsters)
        {
            aliveIds.Add(monster.Id);

            var fighter = monster.Get<Fighter>();
            if (fighter == null) continue;

            bool visible = state.Map.IsVisible(monster.X, monster.Y);
            bool damaged = fighter.Hp < fighter.MaxHp;

            if (!visible || !damaged)
            {
                // Hide or remove existing bar — not worth showing right now.
                RemoveBar(monster.Id);
                continue;
            }

            // Monster is visible and damaged — check sprite exists (guard, not expected to fail).
            if (spriteManager.GetSprite(monster.Id) == null)
            {
                RemoveBar(monster.Id);
                continue;
            }

            // Position from logical grid coordinate — not the sprite's animated position.
            // Sprites are tweened between positions; reading sprite.GlobalPosition during
            // OnTurnCompleted (before PlayTurn starts) gives the PRE-move position, causing
            // the bar to lag one turn behind whenever the monster moves. Using the logical
            // position keeps the bar anchored to where the monster will be after animation.
            var spriteLocalPos = _renderer.GridToScreenCenter(monster.X, monster.Y);
            var barPos = spriteLocalPos + new Vector2(-BarWidth / 2f, VerticalOffset);

            if (_bars.TryGetValue(monster.Id, out var existing))
            {
                // Update existing bar position and fill width.
                existing.Root.Position = barPos;
                UpdateFill(existing.Fill, fighter.Hp, fighter.MaxHp);
            }
            else
            {
                // Create a new bar for this monster.
                var (root, fill) = CreateBar(barPos);
                UpdateFill(fill, fighter.Hp, fighter.MaxHp);
                _bars[monster.Id] = (root, fill);
            }
        }

        // Remove bars for monsters that are no longer alive (died this turn).
        var toRemove = new List<int>();
        foreach (var id in _bars.Keys)
        {
            if (!aliveIds.Contains(id))
                toRemove.Add(id);
        }
        foreach (var id in toRemove)
            RemoveBar(id);
    }

    /// <summary>
    /// Remove all bars immediately. Call on floor transitions.
    /// </summary>
    public void Clear()
    {
        foreach (var (root, _) in _bars.Values)
            root.SafeFree();
        _bars.Clear();
    }

    // -------------------------------------------------------------------------

    private (Node2D Root, ColorRect Fill) CreateBar(Vector2 position)
    {
        var root = new Node2D
        {
            // position is in entityLayer local space (from GridToScreenCenter).
            Position = position,
            // Bars render on top of entity sprites — use a high ZIndex.
            ZIndex = 100,
        };

        var bg = new ColorRect
        {
            Color = BgColor,
            Size  = new Vector2(BarWidth, BarHeight),
            // Position is relative to root — (0,0) puts the bg flush with root.
            Position = Vector2.Zero,
        };

        var fill = new ColorRect
        {
            Color = FillColor,
            // Width is set by UpdateFill; height is fixed.
            Size  = new Vector2(BarWidth, BarHeight),
            Position = Vector2.Zero,
        };

        root.AddChild(bg);
        root.AddChild(fill);
        _entityLayer.AddChild(root);

        return (root, fill);
    }

    private static void UpdateFill(ColorRect fill, int hp, int maxHp)
    {
        float fraction = maxHp > 0 ? Math.Clamp((float)hp / maxHp, 0f, 1f) : 0f;
        fill.Size = new Vector2(BarWidth * fraction, BarHeight);
    }

    private void RemoveBar(int entityId)
    {
        if (_bars.Remove(entityId, out var entry))
            entry.Root.SafeFree();
    }
}
