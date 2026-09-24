using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Creates feature entity instances (chests, signposts, murals, props, traps) for dungeon floor placement.
///
/// Features differ from monsters: no Fighter, no AI, no ETP budget.
/// They block movement (BlocksMovement=true) and respond to bump-interaction in TurnController.
/// IDs come from the per-floor EntityIdAllocator to avoid collision with monsters and items.
/// </summary>
public static class FeatureFactory
{
    /// <summary>
    /// Create a chest entity at the given position.
    /// Chest starts closed. LootItemIds are resolved at floor-gen time for determinism.
    /// </summary>
    public static Entity CreateChest(int x, int y, EntityIdAllocator ids, List<string>? lootItemIds = null)
    {
        var entity = new Entity(ids.Next(), "Chest", x, y, blocksMovement: true);
        entity.Add(new ChestComponent
        {
            IsOpen = false,
            LootItemIds = lootItemIds ?? new List<string>(),
        });
        return entity;
    }

    /// <summary>
    /// Create a locked chest entity at the given position.
    /// Adds both ChestComponent and LockableComponent; chest starts closed and locked.
    /// Must be opened with a KeyItemComponent matching LockColorId.
    /// </summary>
    public static Entity CreateLockedChest(int x, int y, EntityIdAllocator ids, int lockColorId)
    {
        var entity = new Entity(ids.Next(), "Locked Chest", x, y, blocksMovement: true);
        entity.Add(new ChestComponent
        {
            IsOpen = false,
            LootItemIds = new List<string>(),
        });
        entity.Add(new LockableComponent
        {
            LockColorId = lockColorId,
            IsLocked = true,
        });
        return entity;
    }

    /// <summary>
    /// Create a key item entity at the given position.
    /// Keys are non-blocking floor items (BlocksMovement=false) that the player can pick up.
    /// The key entity has KeyItemComponent and ItemTag so it participates in inventory and
    /// floor-item rendering pipelines.
    /// </summary>
    public static Entity CreateKeyItem(int x, int y, EntityIdAllocator ids, int lockColorId)
    {
        var entity = new Entity(ids.Next(), "Key", x, y, blocksMovement: false);
        entity.Add(new KeyItemComponent
        {
            LockColorId = lockColorId,
        });
        entity.Add(new ItemTag("key"));
        return entity;
    }

    /// <summary>
    /// Create a signpost entity at the given position.
    /// Message and signType are assigned at placement time from SignpostMessageRegistry.
    /// </summary>
    public static Entity CreateSignpost(int x, int y, EntityIdAllocator ids, string message, string signType)
    {
        var entity = new Entity(ids.Next(), "Signpost", x, y, blocksMovement: true);
        entity.Add(new SignpostComponent
        {
            Message = message,
            SignType = signType,
            HasBeenRead = false,
        });
        return entity;
    }

    /// <summary>
    /// Create a mural entity at the given position.
    /// Text and muralId come from MuralRegistry; tileId is the visual variant (5036-5038).
    /// Wall-adjacent placement is the caller's responsibility (EntityPlacer.PlaceFloorFeatures).
    /// </summary>
    public static Entity CreateMural(int x, int y, EntityIdAllocator ids, string text, string muralId, int tileId = 5036)
    {
        var entity = new Entity(ids.Next(), "Mural", x, y, blocksMovement: true);
        entity.Add(new MuralComponent
        {
            Text = text,
            MuralId = muralId,
            TileId = tileId,
            HasBeenExamined = false,
        });
        return entity;
    }

    /// <summary>
    /// Create a destructible prop entity (barrel, bookshelf, bone_pile) from a definition.
    ///
    /// Loot is pre-resolved from the definition's loot weights at placement time so that
    /// the same seed always produces the same loot regardless of when the player bumps it.
    /// Pre-resolved loot entities are created and stored in LootEntityIds; they live in a
    /// separate "prop loot staging" list until the prop is resolved (bumped), at which point
    /// they are moved to FloorItems for auto-pickup.
    ///
    /// Trap and rouse are resolved from the definition using the provided RNG:
    ///   - trap_chance roll → if true, select payload from trap_table (weighted)
    ///   - rouse_chance roll → if true and depth ≥ rouse_min_depth, set RouseAction
    ///
    /// Barrels block movement (players cannot walk through them).
    /// Bookshelves and bone piles also block movement.
    /// </summary>
    public static (Entity Feature, List<Entity> LootEntities) CreateDestructibleProp(
        int x, int y,
        string propKind,
        InteractivePropDefinition def,
        InteractivePropsRegistry registry,
        EntityIdAllocator ids,
        SeededRandom rng,
        int depth,
        ConsumableFactory? consumables = null,
        ItemFactory? items = null,
        SpellItemFactory? spellItems = null,
        IdentificationRegistry? identRegistry = null,
        AppearancePool? appearancePool = null,
        Difficulty difficulty = Difficulty.Medium)
    {
        var entity = new Entity(ids.Next(), FormatPropName(propKind), x, y, blocksMovement: true);
        var lootEntities = new List<Entity>();

        // ── Pre-resolve loot ──────────────────────────────────────────────────
        var lootEntityIds = new List<int>();
        if (def.Loot != null && depth >= def.Loot.MinDepth)
        {
            // Select a loot category from the weighted table.
            string category = SelectLootCategory(def.Loot.Weights, rng);

            if (category != "nothing")
            {
                var lootEntity = CreateLootEntity(category, depth, rng, ids, consumables, items, spellItems,
                    identRegistry, appearancePool, difficulty);
                if (lootEntity != null)
                {
                    // Position the loot at the prop's location; EntityPlacer moves it to player tile on resolve.
                    lootEntity.X = x;
                    lootEntity.Y = y;
                    lootEntities.Add(lootEntity);
                    lootEntityIds.Add(lootEntity.Id);
                }
            }
        }

        // ── Pre-resolve trap ──────────────────────────────────────────────────
        TrapPayloadComponent? trapPayload = null;
        if (def.TrapChance > 0 && rng.NextDouble() < def.TrapChance && def.TrapTable?.Count > 0)
        {
            string? payloadId = SelectWeightedPayload(def.TrapTable, rng);
            if (payloadId != null && registry.TryGetPayload(payloadId, out var payloadDef))
            {
                trapPayload = BuildPayloadComponent(payloadDef);
            }
        }

        // ── Pre-resolve rouse ─────────────────────────────────────────────────
        TrapAction? rouseAction = null;
        if (def.RouseChance > 0 && !string.IsNullOrEmpty(def.RouseMonster))
        {
            bool depthOk = def.RouseMinDepth <= 0 || depth >= def.RouseMinDepth;
            if (depthOk && rng.NextDouble() < def.RouseChance)
            {
                rouseAction = new TrapAction
                {
                    Kind   = "spawn_monster",
                    Target = def.RouseMonster,
                    Radius = def.RouseRadius,
                };
            }
        }

        entity.Add(new DestructiblePropComponent
        {
            PropKind      = propKind,
            IsResolved    = false,
            LootEntityIds = lootEntityIds,
            TrapPayload   = trapPayload,
            RouseAction   = rouseAction,
            ClosedTileId  = def.ClosedTileId,
            OpenTileId    = def.OpenTileId,
        });

        return (entity, lootEntities);
    }

    /// <summary>
    /// Create a floor trap entity from a definition.
    ///
    /// Floor traps do not block movement (players and monsters walk over them to trigger).
    /// The payload is built from the definition's action list.
    /// </summary>
    public static Entity CreateFloorTrap(
        int x, int y,
        string trapType,
        FloorTrapDefinition def,
        EntityIdAllocator ids)
    {
        // Floor traps do NOT block movement — they trigger on walk-over.
        var entity = new Entity(ids.Next(), FormatTrapName(trapType), x, y, blocksMovement: false);

        var payload = new TrapPayloadComponent();
        foreach (var actionDef in def.Actions)
        {
            payload.Actions.Add(new TrapAction
            {
                Kind     = actionDef.Kind,
                Amount   = actionDef.Amount,
                Duration = actionDef.Duration,
                Radius   = actionDef.Radius,
                Target   = actionDef.Target,
            });
        }

        float[]? modulate = def.TileModulate != null ? def.TileModulate.ToArray() : null;

        entity.Add(new FloorTrapComponent
        {
            TrapType            = trapType,
            IsSpent             = false,
            IsDetected          = false,
            IsDetectable        = def.IsDetectable,
            PassiveDetectChance = def.PassiveDetectChance,
            Payload             = payload,
            VisibleTileId       = def.VisibleTileId,
            TileModulate        = modulate,
        });

        return entity;
    }

    /// <summary>
    /// Create a floor trap entity with a custom passive detect chance override.
    /// Used for contextual placement (traps near high-value rooms have lower detect chance).
    /// </summary>
    public static Entity CreateFloorTrapWithDetectOverride(
        int x, int y,
        string trapType,
        FloorTrapDefinition def,
        EntityIdAllocator ids,
        double passiveDetectChanceOverride)
    {
        var entity = new Entity(ids.Next(), FormatTrapName(trapType), x, y, blocksMovement: false);

        var payload = new TrapPayloadComponent();
        foreach (var actionDef in def.Actions)
        {
            payload.Actions.Add(new TrapAction
            {
                Kind     = actionDef.Kind,
                Amount   = actionDef.Amount,
                Duration = actionDef.Duration,
                Radius   = actionDef.Radius,
                Target   = actionDef.Target,
            });
        }

        float[]? modulate = def.TileModulate != null ? def.TileModulate.ToArray() : null;

        entity.Add(new FloorTrapComponent
        {
            TrapType            = trapType,
            IsSpent             = false,
            IsDetected          = false,
            IsDetectable        = def.IsDetectable,
            PassiveDetectChance = passiveDetectChanceOverride,  // contextual override
            Payload             = payload,
            VisibleTileId       = def.VisibleTileId,
            TileModulate        = modulate,
        });

        return entity;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>Build a runtime TrapPayloadComponent from a YAML TrapPayloadDefinition.</summary>
    private static TrapPayloadComponent BuildPayloadComponent(TrapPayloadDefinition def)
    {
        var payload = new TrapPayloadComponent();
        foreach (var actionDef in def.Actions)
        {
            payload.Actions.Add(new TrapAction
            {
                Kind     = actionDef.Kind,
                Amount   = actionDef.Amount,
                Duration = actionDef.Duration,
                Radius   = actionDef.Radius,
                Target   = actionDef.Target,
            });
        }
        return payload;
    }

    /// <summary>
    /// Select a loot category from the weights dictionary.
    /// Returns "nothing" when all weights are zero or the dictionary is empty.
    /// </summary>
    private static string SelectLootCategory(Dictionary<string, int> weights, SeededRandom rng)
    {
        int total = weights.Values.Sum();
        if (total <= 0) return "nothing";

        int roll = rng.Next(total);
        int cumulative = 0;
        // Sort by key for determinism (dictionary ordering is not guaranteed).
        foreach (var (category, weight) in weights.OrderBy(kv => kv.Key))
        {
            cumulative += weight;
            if (roll < cumulative)
                return category;
        }
        return "nothing";
    }

    /// <summary>
    /// Select a payload ID from a weighted trap table entry list.
    /// Returns null if the table is empty.
    /// </summary>
    private static string? SelectWeightedPayload(List<WeightedPayloadEntry> table, SeededRandom rng)
    {
        int total = table.Sum(e => e.Weight);
        if (total <= 0) return null;

        int roll = rng.Next(total);
        int cumulative = 0;
        foreach (var entry in table)
        {
            cumulative += entry.Weight;
            if (roll < cumulative)
                return entry.Payload;
        }
        return null;
    }

    /// <summary>
    /// Create a loot item entity for the given category. Returns null if creation fails.
    /// Category: "potion" | "scroll" | "weapon" | "armor"
    /// </summary>
    private static Entity? CreateLootEntity(
        string category, int depth, SeededRandom rng, EntityIdAllocator ids,
        ConsumableFactory? consumables, ItemFactory? items, SpellItemFactory? spellItems,
        IdentificationRegistry? identRegistry, AppearancePool? appearancePool, Difficulty difficulty)
    {
        return category switch
        {
            "potion" => CreatePotionLoot(consumables, identRegistry, appearancePool, rng, difficulty),
            "scroll" => CreateScrollLoot(spellItems, depth, identRegistry, appearancePool, rng, difficulty),
            "weapon" => CreateEquipmentLoot(items, "weapon", rng),
            "armor"  => CreateEquipmentLoot(items, "armor", rng),
            _        => null,
        };
    }

    private static Entity? CreatePotionLoot(ConsumableFactory? consumables,
        IdentificationRegistry? identRegistry, AppearancePool? pool, SeededRandom rng, Difficulty difficulty)
    {
        if (consumables == null) return null;
        var potionIds = consumables.AvailableIds
            .Where(id => consumables.GetDefinition(id)?.IsPotion == true)
            .OrderBy(id => id) // deterministic
            .ToList();
        if (potionIds.Count == 0) return null;
        string id = potionIds[rng.Next(potionIds.Count)];
        return consumables.Create(id, registry: identRegistry, pool: pool, rng: rng, difficulty: difficulty);
    }

    private static Entity? CreateScrollLoot(SpellItemFactory? spellItems, int depth,
        IdentificationRegistry? identRegistry, AppearancePool? pool, SeededRandom rng, Difficulty difficulty)
    {
        if (spellItems == null) return null;
        // Filter to scroll-only items (not wands).
        var scrollIds = spellItems.AvailableIds
            .Where(id => spellItems.GetDefinition(id)?.IsWand == false)
            .OrderBy(id => id) // deterministic
            .ToList();
        if (scrollIds.Count == 0) return null;
        string id = scrollIds[rng.Next(scrollIds.Count)];
        return spellItems.CreateScroll(id, registry: identRegistry, pool: pool, identRng: rng, difficulty: difficulty);
    }

    private static Entity? CreateEquipmentLoot(ItemFactory? items, string category, SeededRandom rng)
    {
        if (items == null) return null;

        // Filter by equipment slot: weapons = main_hand or off_hand; armor = head/body/feet/hands/etc.
        var catIds = items.AvailableIds
            .Where(id =>
            {
                var def = items.GetDefinition(id);
                if (def == null) return false;
                bool isWeapon = def.Slot is "main_hand" or "off_hand";
                return category == "weapon" ? isWeapon : !isWeapon;
            })
            .OrderBy(id => id) // deterministic
            .ToList();
        if (catIds.Count == 0) return null;
        string id = catIds[rng.Next(catIds.Count)];
        return items.Create(id);
    }

    private static string FormatPropName(string kind)
        => string.Join(" ", kind.Split('_').Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));

    private static string FormatTrapName(string kind)
        => string.Join(" ", kind.Split('_').Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));
}
