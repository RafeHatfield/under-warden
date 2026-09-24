using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Presentation.Map;
using Godot;

namespace UnderWarden.Presentation.Entities;

/// <summary>
/// Manages Sprite2D nodes for corpses — dead monsters that leave visible remains.
///
/// A corpse is the same entity as the monster that died, transformed in-place: Fighter is
/// stripped, a CorpseComponent is added, and the entity is added to state.Corpses while staying
/// in state.Monsters (dual membership). It keeps its SpeciesTag, so its remains render as the
/// species' own sprite under a "corpse treatment": rotated ~90°, darkened/desaturated, faded.
///
/// This manager is deliberately separate from EntitySpriteManager (which renders LIVE monsters
/// and skips corpses) so the two lifecycles never fight: EntitySpriteManager's status-tint and
/// position passes only touch the living, and corpses reconcile purely against state.Corpses.
///
/// Lifecycle model — reconcile-against-state, not event-driven:
///   • Sync(state) is the single source of truth. It creates a sprite for every corpse in
///     state.Corpses that lacks one (the live-play death seam and resume both surface here) and
///     removes any sprite whose entity is no longer in state.Corpses (raised by a necromancer,
///     or the list was cleared on a floor change). Every corpse world-exit routes through
///     state.Corpses, so Sync covers them all with no per-path wiring.
///   • Initialize(state) is just the first Sync on floor load / resume.
///   • UpdateVisibility(state) is FOV-only, matching the item convention (visible only while the
///     tile is currently in FOV; corpses are not remembered and not drawn on the minimap).
///
/// Z-order: corpses sort exactly one below a co-located live entity (GetEntitySortOrder - 1), so a
/// living monster or the player walking onto the tile draws on top of the remains.
/// </summary>
public sealed class CorpseSpriteManager
{
    private const string FallbackSprite = "heroes/goblin";

    // ── Corpse treatment (proposed; device-judged per the ruling) ────────────────────────────
    // Rotated ~90° (fallen), darkened + desaturated toward a dull warm-grey, and faded to 75%.
    // Modulate multiplies the source texture, so channels < 1 darken; the near-equal RGB pulls
    // saturation down; alpha 0.75 reads as "not a live threat". Tune on device, then lock.
    private const float CorpseRotationRadians = Mathf.Pi / 2f;      // 90°
    private static readonly Color CorpseModulate = new(0.50f, 0.45f, 0.42f, 0.75f);

    private readonly Node2D _parent;
    private readonly SpriteMapping? _spriteMapping;
    private readonly IMapRenderer _renderer;
    private readonly Dictionary<int, Sprite2D> _sprites = new();

    public CorpseSpriteManager(Node2D entityLayerNode, SpriteMapping spriteMapping, IMapRenderer renderer)
    {
        _parent = entityLayerNode;
        _spriteMapping = spriteMapping;
        _renderer = renderer;
    }

    /// <summary>Test-only constructor — textures are null in the test environment (CreateSprite skips).</summary>
    public CorpseSpriteManager(Node2D entityLayerNode, IMapRenderer renderer)
    {
        _parent = entityLayerNode;
        _spriteMapping = null;
        _renderer = renderer;
    }

    /// <summary>Number of live corpse sprites. Useful for the debug overlay.</summary>
    public int SpriteCount => _sprites.Count;

    /// <summary>Build corpse sprites for the current state. Call on floor load / resume.</summary>
    public void Initialize(GameState state) => Sync(state);

    /// <summary>
    /// Reconcile corpse sprites against state.Corpses: create any missing, free any stale.
    /// Call once per turn (after ProcessTurn) alongside the entity/item update passes.
    /// </summary>
    public void Sync(GameState state)
    {
        // Create sprites for corpses that don't have one yet (death seam + resume).
        foreach (var corpse in state.Corpses)
        {
            if (!_sprites.ContainsKey(corpse.Id))
                CreateSprite(corpse);
        }

        // Free sprites whose corpse has left the world (raised, or floor-change clear).
        if (_sprites.Count == 0) return;
        var live = new HashSet<int>(state.Corpses.Count);
        foreach (var corpse in state.Corpses)
            live.Add(corpse.Id);

        List<int>? stale = null;
        foreach (var id in _sprites.Keys)
        {
            if (!live.Contains(id))
                (stale ??= new List<int>()).Add(id);
        }
        if (stale != null)
            foreach (var id in stale)
                RemoveCorpse(id);
    }

    /// <summary>Apply FOV visibility to corpse sprites. Call after each turn's FOV recompute.</summary>
    public void UpdateVisibility(GameState state)
    {
        foreach (var (entityId, sprite) in _sprites)
        {
            var corpse = state.Corpses.FirstOrDefault(c => c.Id == entityId);
            sprite.Visible = corpse != null && state.Map.IsVisible(corpse.X, corpse.Y);
        }
    }

    /// <summary>Free the sprite for a corpse that left the world (e.g. raised into a monster).</summary>
    public void RemoveCorpse(int entityId)
    {
        if (_sprites.Remove(entityId, out var sprite))
            sprite.SafeFree();
    }

    private void CreateSprite(Entity corpse)
    {
        if (_spriteMapping == null)
        {
            GD.PrintErr($"[CorpseSpriteManager] No SpriteMapping — cannot create corpse sprite for '{corpse.Name}'");
            return;
        }

        string framePath = _spriteMapping.GetFramePath(InferSpriteBase(corpse), 1); // Frame 1 = idle
        var texture = GD.Load<Texture2D>(framePath);
        if (texture == null)
        {
            GD.PrintErr($"[CorpseSpriteManager] Missing sprite: {framePath}");
            return;
        }

        var screenPos = _renderer.GridToScreenCenter(corpse.X, corpse.Y);
        float scale = 48f / _spriteMapping.SpriteSize;
        float offsetY = _spriteMapping.GetEntityYOffset(texture.GetHeight(), scale);

        var sprite = new Sprite2D
        {
            Texture = texture,
            Position = screenPos,
            Centered = true,
            Scale = new Vector2(scale, scale),
            Offset = new Vector2(0, offsetY),
            Rotation = CorpseRotationRadians,
            Modulate = CorpseModulate,
            // One below a co-located live entity (odd sort order) so the living draw on top.
            ZIndex = _renderer.GetEntitySortOrder(corpse.X, corpse.Y) - 1,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Visible = false, // Hidden until FOV reveals the tile (item convention).
        };

        _parent.AddChild(sprite);
        _sprites[corpse.Id] = sprite;
    }

    /// <summary>
    /// Resolve the sprite base for a corpse via its retained SpeciesTag — the same primary path
    /// EntitySpriteManager uses for the living, so remains match the creature that died.
    /// </summary>
    private string InferSpriteBase(Entity corpse)
    {
        var tag = corpse.Get<SpeciesTag>();
        if (tag != null && _spriteMapping != null)
        {
            var spriteBase = _spriteMapping.GetSpriteBase(tag.TypeId);
            if (spriteBase != null) return spriteBase;
            GD.PrintErr($"[CorpseSpriteManager] No sprite mapping for species '{tag.TypeId}' — using fallback.");
        }
        return FallbackSprite;
    }
}
