using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Presentation.Map;
using Godot;

using UnderWarden.Presentation;


namespace UnderWarden.Presentation.Entities;

/// <summary>
/// Manages Sprite2D nodes for game entities. Creates sprites on game start,
/// updates positions each turn, removes sprites when entities die.
///
/// Sprite lookup priority (entity sprites):
///   1. SpeciesTag.TypeId → SpriteMapping.GetSpriteBase() (primary path, all MonsterFactory entities)
///   2. Name-substring heuristics (fallback for hand-constructed test entities without SpeciesTag)
///
/// Player entity has no SpeciesTag — uses SpriteMapping.PlayerSprite directly.
///
/// Characters use native sprite size scaled to 48px equivalent, bottom-center aligned to the
/// iso tile diamond center (Option A from art direction).
/// </summary>
public sealed class EntitySpriteManager
{
    private const string FallbackSprite = "heroes/goblin";

    private readonly Node2D _parent;
    private readonly SpriteMapping? _spriteMapping;
    private readonly IMapRenderer _renderer;
    private readonly Dictionary<int, Sprite2D> _sprites = new();

    /// <summary>§183's hero light response. See the shader's own header for the ruling.</summary>
    private const string HeroShaderPath = "res://src/Presentation/assets/shaders/hero_light.gdshader";
    // Cached grid positions — avoids O(n) monster scan in UpdateVisibility each turn.
    private readonly Dictionary<int, (int X, int Y)> _positions = new();

    public EntitySpriteManager(Node2D entityLayerNode, SpriteMapping spriteMapping, IMapRenderer renderer)
    {
        _parent = entityLayerNode;
        _spriteMapping = spriteMapping;
        _renderer = renderer;
    }

    /// <summary>
    /// Test-only constructor — no SpriteMapping needed when textures are always null in the
    /// test environment (GD.Load returns null, CreateSprite skips silently).
    /// </summary>
    public EntitySpriteManager(Node2D entityLayerNode, IMapRenderer renderer)
    {
        _parent = entityLayerNode;
        _spriteMapping = null;
        _renderer = renderer;
    }

    /// <summary>Number of live entity sprites (player + monsters). Useful for debug overlay.</summary>
    public int SpriteCount => _sprites.Count;

    /// <summary>
    /// Create sprites for all entities in the game state.
    /// Call once at game start.
    /// </summary>
    public void Initialize(GameState state)
    {
        // Player entity has no SpeciesTag — use PlayerSprite from tileset config directly.
        // If no SpriteMapping (test-only constructor path), CreateSprite will skip with a log.
        CreateSprite(state.Player, _spriteMapping?.PlayerSprite ?? FallbackSprite,
                     isHero: true);

        foreach (var monster in state.Monsters)
        {
            // Corpses have DUAL MEMBERSHIP: a dead monster stays in state.Monsters AND is added to
            // state.Corpses (TurnController.MakeCorpse), keeping its SpeciesTag. During live play the
            // death path removes its LIVE sprite (GameController on DeathEvent). Corpses are rendered
            // separately by CorpseSpriteManager (from state.Corpses, under the corpse treatment) — this
            // manager owns only the LIVING. Without this skip, resume would re-sprite corpses as LIVE
            // monsters (device symptom B). Fresh floors have no corpses, so no change there.
            if (monster.Has<CorpseComponent>()) continue;
            CreateSprite(monster, InferSpriteBase(monster));
        }
    }

    /// <summary>
    /// Update all sprite positions to match current entity positions.
    /// Call after each turn is processed.
    /// </summary>
    public void UpdatePositions(GameState state)
    {
        UpdateSpritePosition(state.Player);
        foreach (var monster in state.Monsters)
        {
            if (monster.Get<Fighter>()?.IsAlive == true)
                UpdateSpritePosition(monster);
        }
    }

    /// <summary>
    /// Apply status effect tints to monster sprites for visual debuff feedback.
    /// Call after UpdateVisibility() so visibility state is already set.
    ///
    /// Priority (highest wins if multiple effects active):
    ///   poison → burning → disorientation → sleep → any other debuff → no tint
    ///
    /// Player sprite is intentionally excluded — the HUD StatusEffectBar covers that.
    /// </summary>
    public void UpdateStatusTints(GameState state)
    {
        foreach (var monster in state.Monsters)
        {
            if (!_sprites.TryGetValue(monster.Id, out var sprite)) continue;
            sprite.Modulate = ChooseStatusTint(monster);
        }
    }

    /// <summary>
    /// Apply fog-of-war visibility to entity sprites.
    /// Uses the cached position dictionary — O(sprites) not O(sprites × monsters).
    /// </summary>
    public void UpdateVisibility(GameState state)
    {
        foreach (var (entityId, sprite) in _sprites)
        {
            if (!_positions.TryGetValue(entityId, out var pos))
            {
                sprite.Visible = false;
                continue;
            }
            sprite.Visible = state.Map.IsVisible(pos.X, pos.Y);
        }
    }

    /// <summary>
    /// Create a sprite for a newly spawned monster (e.g. a slime spawned by split).
    /// Call after the child entity has been added to state.Monsters.
    /// </summary>
    public void SpawnMonster(Entity monster)
    {
        CreateSprite(monster, InferSpriteBase(monster));
    }

    /// <summary>
    /// Remove the sprite for a dead entity (fade or instant).
    /// </summary>
    public void RemoveEntity(int entityId)
    {
        if (_sprites.Remove(entityId, out var sprite))
        {
            sprite.SafeFree();
            _positions.Remove(entityId);
        }
    }

    /// <summary>
    /// Get the sprite for an entity, if it exists.
    /// </summary>
    public Sprite2D? GetSprite(int entityId)
    {
        return _sprites.GetValueOrDefault(entityId);
    }

    private void CreateSprite(Entity entity, string spriteBase, bool isHero = false)
    {
        if (_spriteMapping == null)
        {
            GD.PrintErr($"[EntitySpriteManager] No SpriteMapping — cannot create sprite for '{entity.Name}'");
            return;
        }

        string framePath = _spriteMapping.GetFramePath(spriteBase, 1); // Frame 1 = idle
        var texture = GD.Load<Texture2D>(framePath);
        if (texture == null)
        {
            GD.PrintErr($"Missing sprite: {framePath}");
            return;
        }

        var screenPos = _renderer.GridToScreenCenter(entity.X, entity.Y);

        // Scale compensation: UF sprites are 48px native; 16bf are 24px native.
        // Integer 2x upscaling on 16bf gives clean pixel art without blurring.
        // Scale = 48 / native_size so UF stays at 1.0, 16bf renders at 2.0.
        float scale = 48f / _spriteMapping.SpriteSize;

        // Y offset grounds entity feet on the tile surface. Positive = down, negative = up.
        // Tileset config may override via entity_y_offset; otherwise uses default formula.
        // DO NOT change this formula — it is calibrated for the current iso tiles.
        float offsetY = _spriteMapping.GetEntityYOffset(texture.GetHeight(), scale);

        var sprite = new Sprite2D
        {
            Texture = texture,
            Position = screenPos,
            Centered = true,
            Scale = new Vector2(scale, scale),
            Offset = new Vector2(0, offsetY),
            ZIndex = _renderer.GetEntitySortOrder(entity.X, entity.Y),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };

        // ── §183: THE HERO RECEIVES LESS OF THE LAMP'S TOP END ───────────────────────────────
        //
        // RULED by Rafe at the 2026-09-06 rig walk: *"warmest is never brightest"*. Measured on
        // the combined frame at the ratified rig, the hero's cell read p95 242.7 and max 250.0
        // against lit floor either side of him at 203.5 and 207.4.
        //
        // ⚠ THAT WAS READ AS "the inverse of the clause" AND IT WAS NOT — OVERTURNED at the
        // human gate 2026-09-09. Being above the ground is what a light source does; the defect
        // was that 8.8% of his pixels were pinned within two levels of his max, i.e. CLIPPED.
        // "Warmest never brightest" means never blown. The shader now rolls that top end off
        // before the clamp and leaves the figure where it stands.
        //
        // THE PLAYER ONLY, and that is a decision rather than an oversight. §10 rules the player
        // unit; nothing rules a monster's response, and giving every entity a hero law would be
        // legislating for a population this walk never looked at. A monster that needs the same
        // treatment can be given the same material, deliberately, at its own gate.
        //
        // It is a LIGHT RESPONSE and touches nothing else: no rig value moves (§6.2.1 keeps a
        // character decision out of a region's lighting law), no albedo is repainted, and with
        // the shoulder inactive the shader is the identity — the sprite is lit exactly as a
        // sprite with no material at all. `hero_knee` above any reachable value is the null.
        if (isHero)
        {
            var shader = ResourceLoader.Load<Shader>(HeroShaderPath);
            if (shader != null)
                sprite.Material = new ShaderMaterial { Shader = shader };
            else
                GD.PrintErr($"[EntitySpriteManager] hero light shader missing: {HeroShaderPath}");
        }

        _parent.AddChild(sprite);
        _sprites[entity.Id] = sprite;
        _positions[entity.Id] = (entity.X, entity.Y);
    }

    private void UpdateSpritePosition(Entity entity)
    {
        if (!_sprites.TryGetValue(entity.Id, out var sprite))
            return;

        sprite.Position = _renderer.GridToScreenCenter(entity.X, entity.Y);
        sprite.ZIndex = _renderer.GetEntitySortOrder(entity.X, entity.Y);
        _positions[entity.Id] = (entity.X, entity.Y);
    }

    /// <summary>
    /// Resolve the sprite base for a monster entity.
    ///
    /// Primary path: SpeciesTag.TypeId → SpriteMapping lookup.
    /// All entities created by MonsterFactory have a SpeciesTag — this path covers 100%
    /// of normal gameplay cases.
    ///
    /// Fallback: name-substring heuristics from the old static SpriteMapping.
    /// Retained for hand-constructed test entities (e.g. in unit tests or debug scenarios)
    /// that may not have a SpeciesTag. A GD.PrintErr fires so gaps surface immediately.
    /// </summary>
    private string InferSpriteBase(Entity monster)
    {
        // Primary: use SpeciesTag for all factory-created monsters (the normal path)
        var tag = monster.Get<SpeciesTag>();
        if (tag != null)
        {
            if (_spriteMapping != null)
            {
                var spriteBase = _spriteMapping.GetSpriteBase(tag.TypeId);
                if (spriteBase != null) return spriteBase;

                // TypeId exists but isn't in the tileset YAML — missing mapping, not a code bug.
                GD.PrintErr($"[EntitySpriteManager] No sprite mapping for species '{tag.TypeId}' in tileset '{_spriteMapping.SpriteSize}px' — using fallback.");
            }
            return FallbackSprite;
        }

        // Fallback: name-substring heuristics for entities without SpeciesTag.
        // This path should never fire in production. If it does, something is wrong upstream.
        GD.PrintErr($"[EntitySpriteManager] Entity '{monster.Name}' (id={monster.Id}) has no SpeciesTag — using name heuristic fallback.");
        return InferSpriteBaseFromName(monster.Name);
    }

    /// <summary>
    /// Legacy name-substring sprite inference. Kept as a last resort for entities
    /// without SpeciesTag (hand-constructed test entities, debug scenarios).
    /// Never called for normal gameplay — MonsterFactory always adds SpeciesTag.
    /// </summary>
    private static string InferSpriteBaseFromName(string name)
    {
        string nameLower = name.ToLowerInvariant();

        if (nameLower.Contains("large slime") || nameLower == "large_slime")
            return "monsters/slime_red";
        if (nameLower.Contains("slime"))
            return "monsters/slime_green";

        if (nameLower.Contains("orc") && (nameLower.Contains("brute") || nameLower.Contains("warrior")))
            return "heroes/goblin_warrior";
        if (nameLower.Contains("orc grunt") || nameLower == "orc_grunt")
            return "heroes/goblin";
        if (nameLower.Contains("orc"))
            return "heroes/goblin";

        if (nameLower.Contains("zombie"))
            return "heroes/zombie_a";

        return FallbackSprite;
    }

    /// <summary>
    /// Select the sprite tint color based on active status effects.
    ///
    /// Priority (most visually severe wins when multiple effects are active):
    ///   burning (orange) > poison/plague (green) > sleep (blue)
    ///   > disoriented/confused (purple) > feared (yellow) > entangled (brown)
    ///   > any other debuff (subtle gray) > normal (white)
    ///
    /// Tint is only visible when the sprite is visible (FOV handled by UpdateVisibility).
    /// Player sprite is intentionally excluded — the HUD StatusEffectBar covers that.
    /// </summary>
    private static Color ChooseStatusTint(Entity entity)
    {
        // Burning is the highest-severity visible state — bright orange, hard to miss.
        if (entity.Has<BurningEffect>())        return new Color(1f,   0.55f, 0.1f);  // orange

        // Poison and Plague share the green family — Plague is a stronger variant.
        if (entity.Has<PlagueEffect>())         return new Color(0.3f, 0.9f,  0.3f);  // vivid green
        if (entity.Has<PoisonEffect>())         return new Color(0.5f, 1f,    0.5f);  // lighter green

        // Sleep: blue tint (entity is unconscious).
        if (entity.Has<SleepEffect>())          return new Color(0.5f, 0.7f,  1f);    // blue

        // Disorientation/confusion: purple.
        if (entity.Has<DisorientationEffect>()) return new Color(0.8f, 0.5f,  1f);    // purple

        // Fear: yellow (fleeing/panicked).
        if (entity.Has<FearEffect>())           return new Color(1f,   0.9f,  0.3f);  // yellow

        // Entangled: earthy brown (rooted in place).
        if (entity.Has<EntangledEffect>())      return new Color(0.7f, 0.5f,  0.3f);  // brown

        // Any remaining IStatusEffect gets a subtle gray tint (visible debuff cue, indeterminate type).
        bool hasAnyEffect = entity.GetAllComponents().OfType<IStatusEffect>().Any();
        if (hasAnyEffect) return new Color(0.8f, 0.8f, 0.8f);

        return Colors.White;
    }
}
