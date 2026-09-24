using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Endgame;
using UnderWarden.Logic.Persistence;
using BoonTable = System.Collections.Generic.IReadOnlyDictionary<int, UnderWarden.Logic.Balance.BoonDefinition>;
using FloorItemPool = System.Collections.Generic.IReadOnlyList<UnderWarden.Logic.Content.FloorItemPoolEntry>;
using IdentifiableItemDef = (string id, UnderWarden.Logic.Content.ItemCategory category);
using PityTrackerType = UnderWarden.Logic.Balance.PityTracker;

namespace UnderWarden.Logic.Core;

/// <summary>
/// Assembles a complete GameState for one dungeon floor.
///
/// This is the dungeon-mode entry point — the equivalent of GameStateFactory.FromScenario
/// but for procedurally generated floors. GameStateFactory.FromScenario is NEVER touched.
///
/// Build() is deterministic for the same depth + rng seed. Callers should seed the rng as:
///   baseSeed + depth * 1_000_003
/// ...to get distinct-but-reproducible floors per depth.
///
/// Dependency graph:
///   LevelTemplateRegistry → per-floor overrides (generation params, guaranteed spawns)
///   MapGenerator → geometry (rooms, corridors)
///   EntityPlacer → monsters, items, stairs
///   PlayerCarryForward → stat persistence when descending
/// </summary>
public sealed class DungeonFloorBuilder
{
    // Defaults used when no level template override is found for a depth.
    // Room sizes tuned for mobile: SPD-style small rooms that fit on screen.
    // 5–10 tile sides → most rooms fully visible without scrolling on a phone.
    private const int DefaultMapWidth = 120;
    private const int DefaultMapHeight = 80;
    private const int DefaultMaxRooms = 150;
    private const int DefaultMinRoomSize = 5;
    private const int DefaultMaxRoomSize = 10;

    private readonly LevelTemplateRegistry _templates;
    private readonly MonsterFactory _monsterFactory;
    private readonly ItemFactory _itemFactory;
    private readonly ConsumableFactory _consumableFactory;
    private readonly FloorItemPool _floorItemPool;
    private readonly SpellItemFactory? _spellItemFactory;
    private readonly BoonTable? _boonTable;
    private readonly Content.PropRegistry? _propRegistry;
    private readonly Content.SignpostMessageRegistry? _signpostRegistry;
    private readonly Content.MuralRegistry? _muralRegistry;
    private readonly Content.LootTagRegistry? _lootTagRegistry;
    private readonly Content.LootPolicyConfig? _lootPolicy;
    private readonly Content.InteractivePropsRegistry? _interactivePropsRegistry;
    private readonly Content.FloorTrapRegistry? _floorTrapRegistry;

    /// <summary>
    /// All identifiable item definitions (potions, scrolls, wands, rings) for this run.
    /// Used by AppearancePool construction at the start of each run.
    /// Populated from ConsumableFactory and SpellItemFactory if provided to the constructor.
    /// </summary>
    private readonly List<IdentifiableItemDef> _identifiableItems;

    public DungeonFloorBuilder(
        LevelTemplateRegistry templates,
        MonsterFactory monsterFactory,
        ItemFactory itemFactory,
        ConsumableFactory consumableFactory,
        FloorItemPool? floorItemPool = null,
        SpellItemFactory? spellItemFactory = null,
        BoonTable? boonTable = null,
        Content.PropRegistry? propRegistry = null,
        Content.SignpostMessageRegistry? signpostRegistry = null,
        Content.MuralRegistry? muralRegistry = null,
        Content.LootTagRegistry? lootTagRegistry = null,
        Content.LootPolicyConfig? lootPolicy = null,
        Content.InteractivePropsRegistry? interactivePropsRegistry = null,
        Content.FloorTrapRegistry? floorTrapRegistry = null)
    {
        _templates = templates;
        _monsterFactory = monsterFactory;
        _itemFactory = itemFactory;
        _consumableFactory = consumableFactory;
        _floorItemPool = floorItemPool ?? [];
        _spellItemFactory = spellItemFactory;
        _boonTable = boonTable;
        _propRegistry = propRegistry;
        _signpostRegistry = signpostRegistry;
        _muralRegistry = muralRegistry;
        _lootTagRegistry = lootTagRegistry;
        _lootPolicy = lootPolicy;
        _interactivePropsRegistry = interactivePropsRegistry;
        _floorTrapRegistry = floorTrapRegistry;

        // Collect identifiable item types for AppearancePool construction.
        // Potions come from ConsumableFactory; scrolls/wands come from SpellItemFactory.
        _identifiableItems = BuildIdentifiableItemList(consumableFactory, spellItemFactory);
    }

    private static List<IdentifiableItemDef> BuildIdentifiableItemList(
        ConsumableFactory consumableFactory, SpellItemFactory? spellItemFactory)
    {
        var list = new List<IdentifiableItemDef>();

        foreach (var id in consumableFactory.AvailableIds)
        {
            var def = consumableFactory.GetDefinition(id);
            if (def != null && def.Category != ItemCategory.Other)
                list.Add((id, def.Category));
        }

        if (spellItemFactory != null)
        {
            foreach (var id in spellItemFactory.AvailableIds)
            {
                var def = spellItemFactory.GetDefinition(id);
                // Exclude infinite wands (e.g. wand_of_portals) — they are always granted at run
                // start, always known to the player, and must never enter the mystery pool.
                if (def != null && def.Category != ItemCategory.Other && !def.Infinite)
                    list.Add((id, def.Category));
            }
        }

        return list;
    }

    /// <summary>
    /// Build a complete GameState for the given depth.
    ///
    /// If existingPlayer is non-null, carries HP/equipment/inventory forward via PlayerCarryForward.
    /// If null, this is the start of a new run — no carry-forward needed.
    ///
    /// identificationRegistry and appearancePool:
    ///   - Null = new run: creates fresh instances from run seed.
    ///   - Non-null = floor transition: passes the existing instances through unchanged.
    ///     Registry and pool are NOT reset between floors — only reset on new game.
    ///
    /// The returned state has IsDungeonMode=true and StairDown set (or null if no stair was placed).
    /// </summary>
    public GameState Build(int depth, SeededRandom rng, Entity? existingPlayer = null,
        IdentificationRegistry? identificationRegistry = null,
        AppearancePool? appearancePool = null,
        Difficulty difficulty = Difficulty.Medium,
        BoonTracker? boonTracker = null,
        bool explorationMode = false,
        MuralTracker? muralTracker = null,
        PityTrackerType? pityTracker = null,
        PersistentRunState? persistentState = null)
    {
        // The Weighing floor (25) is a hand-authored arena, not procedural — see decision memo.
        // It bypasses the generation pipeline entirely; the orchestrator populates it on the first turn.
        if (WeighingConstants.IsWeighingFloor(depth))
            return BuildWeighingArena(depth, rng, existingPlayer, identificationRegistry,
                appearancePool, difficulty, boonTracker, muralTracker, pityTracker, persistentState);

        // Resolve per-depth override (null = use defaults for everything)
        var levelOverride = _templates.GetLevelOverride(depth);
        var genParams = levelOverride?.Parameters;
        var guaranteedSpawns = levelOverride?.GuaranteedSpawns;
        var stairRules = levelOverride?.Stairs;

        var encounterBudget = levelOverride?.EncounterBudget;
        int roomEtpMax = encounterBudget?.EtpMax ?? EntityPlacer.DefaultRoomEtpMax;
        bool allowSpike = encounterBudget?.AllowSpike ?? false;

        // Generation parameters — override wins, else defaults
        int mapWidth = genParams?.MapWidth ?? DefaultMapWidth;
        int mapHeight = genParams?.MapHeight ?? DefaultMapHeight;
        int maxRooms = genParams?.MaxRooms ?? DefaultMaxRooms;
        int minRoomSize = genParams?.MinRoomSize ?? DefaultMinRoomSize;
        int maxRoomSize = genParams?.MaxRoomSize ?? DefaultMaxRoomSize;

        // Generate the floor geometry (and place props if a registry was provided)
        var generatedMap = MapGenerator.Generate(
            mapWidth, mapHeight, maxRooms, minRoomSize, maxRoomSize, rng, stairRules,
            depth: depth, propRegistry: _propRegistry);

        // Assign visual themes to rooms and corridors.
        // Themes are purely cosmetic — they drive sprite selection in DungeonRenderer.
        // This runs AFTER generation so it does not alter the rng consumption sequence.
        AssignTileThemes(generatedMap, depth, rng);

        // Create or carry forward player — always ID 0.
        // Must happen BEFORE EntityIdAllocator is initialized so that any starting gear
        // created by CreateDefaultPlayer() (dagger, armor, potion) advances the shared
        // EntityFactory counter first.
        Entity player;
        if (existingPlayer != null)
        {
            // Clear all status effects on floor transition — no carry-over between floors.
            // Policy: simplest approach that avoids confusing persistent debuffs across floors.
            StatusEffectProcessor.ClearAllEffects(existingPlayer);
            player = PlayerCarryForward.Apply(existingPlayer);

            // Reset portal wand state — portals don't persist between floors.
            // The portal entities belong to the old GameState (abandoned on transition),
            // so only the wand's step tracker needs resetting here.
            var inventory = existingPlayer.Get<Inventory>();
            if (inventory != null)
            {
                foreach (var invItem in inventory.Items)
                    if (invItem.Get<PortalCastStateComponent>() != null)
                        PortalSystem.ResetPortalWandState(invItem);
            }

            // Restore ring effects that are NOT preserved in the carried Fighter stats.
            // Stat rings (protection/strength/dexterity/constitution/might) survive because
            // PlayerCarryForward copies the live Fighter (which has ring bonuses baked in).
            // But RingMaxHpBonus, SpeedBonusTracker.RingRatio, and FreeActionTag are NOT
            // in Fighter's constructor and are NOT copied — ReapplyRingEffects restores them.
            TurnController.ReapplyRingEffects(player);

            // Re-apply boon max HP bonus — same pattern as RingMaxHpBonus above.
            // The new Fighter from PlayerCarryForward copies BoonMaxHpBonus, but
            // that's baked into the carry-forward path. Depth boon for THIS floor
            // is applied below after state construction.
        }
        else
        {
            // Fresh player for a new run — caller is responsible for providing a properly
            // configured entity. For now, DungeonFloorBuilder creates a default player.
            // This path is used in tests; production code should pass an existing player.
            player = CreateDefaultPlayer();
        }

        // ID allocator: start AFTER the highest ID already consumed by the shared EntityFactory.
        // CreateDefaultPlayer() above may have consumed IDs 1, 2, 3 for dagger/armor/potion.
        // Starting from NextId guarantees map-placed entities never collide with gear IDs.
        var ids = new EntityIdAllocator(startFrom: _monsterFactory.EntityFactory.NextId);

        // Place player at the map's spawn point and register
        player.X = generatedMap.PlayerSpawn.X;
        player.Y = generatedMap.PlayerSpawn.Y;
        generatedMap.Map.RegisterEntity(player);

        // Create identification registry and pool BEFORE FillRooms so items placed during
        // floor generation receive correct pre-identification decisions.
        // Floor transitions pass existing instances through unchanged (registry persists across floors).
        // New runs create fresh instances from the run seed.
        var finalRegistry = identificationRegistry ?? new IdentificationRegistry();
        var finalPool = appearancePool ?? new AppearancePool(_identifiableItems, rng.Seed);

        // Pre-identify infinite wands (e.g. wand_of_portals) in the registry.
        // Infinite wands are always given to the player at run start — they are never mystery items.
        // Without this, TryIdentifyOnUse would fire "you realize it was a wand of X" on first use,
        // showing the wrong name (another wand's appearance-pool slot bleeds through).
        if (_spellItemFactory != null)
        {
            foreach (var id in _spellItemFactory.AvailableIds)
            {
                var def = _spellItemFactory.GetDefinition(id);
                if (def?.Infinite == true)
                    finalRegistry.Identify(id);
            }
        }

        // PityTracker: carry forward from previous floor, or create fresh for new run.
        // Initialize category tracking from the loot policy (idempotent — safe to call on carry-forward).
        var finalPityTracker = pityTracker ?? (_lootPolicy != null ? new PityTrackerType() : null);
        if (finalPityTracker != null && _lootPolicy != null)
            finalPityTracker.InitializeTrackedCategories(_lootPolicy.TrackedCategories);

        // Place entities (monsters + items)
        var allMonsters = new List<Entity>();
        var allFloorItems = new List<Entity>();

        bool hasGuaranteedSpawns = guaranteedSpawns != null &&
            (guaranteedSpawns.Monsters.Count > 0 ||
             guaranteedSpawns.Items.Count > 0 ||
             guaranteedSpawns.Equipment.Count > 0);

        if (hasGuaranteedSpawns)
        {
            var guaranteed = EntityPlacer.PlaceGuaranteedSpawns(
                generatedMap, guaranteedSpawns!, _monsterFactory, _itemFactory, _consumableFactory,
                rng, depth, ids, spellItems: _spellItemFactory);

            // Separate monsters from items.
            // Monsters block movement (BlocksMovement=true from MonsterDefinition.Blocks).
            // Items/consumables do not. We cannot use Has<Fighter>() here because
            // CopyComponents stores components under the IComponent key, not the concrete type key.
            foreach (var entity in guaranteed)
                if (entity.BlocksMovement)
                { if (!explorationMode) allMonsters.Add(entity); }
                else
                    allFloorItems.Add(entity);

            // "replace" mode: only guaranteed spawns, no procedural fill
            // "additional" mode: guaranteed spawns + procedural fill
            if (guaranteedSpawns!.Mode != "replace")
            {
                var filled = EntityPlacer.FillRooms(
                    generatedMap, genParams, _monsterFactory, _consumableFactory,
                    rng, depth, ids,
                    roomEtpMax: roomEtpMax,
                    allowSpike: allowSpike,
                    items: _itemFactory, floorItemPool: _floorItemPool,
                    spellItems: _spellItemFactory,
                    identRegistry: finalRegistry, appearancePool: finalPool,
                    difficulty: difficulty,
                    lootTagRegistry: _lootTagRegistry,
                    lootPolicy: _lootPolicy,
                    pityTracker: finalPityTracker);

                foreach (var entity in filled)
                    if (entity.BlocksMovement)
                    { if (!explorationMode) allMonsters.Add(entity); }
                    else
                        allFloorItems.Add(entity);
            }
        }
        else
        {
            // No guaranteed spawns — full procedural fill
            var filled = EntityPlacer.FillRooms(
                generatedMap, genParams, _monsterFactory, _consumableFactory,
                rng, depth, ids,
                roomEtpMax: roomEtpMax,
                allowSpike: allowSpike,
                items: _itemFactory, floorItemPool: _floorItemPool,
                spellItems: _spellItemFactory,
                identRegistry: finalRegistry, appearancePool: finalPool,
                difficulty: difficulty,
                lootTagRegistry: _lootTagRegistry,
                lootPolicy: _lootPolicy,
                pityTracker: finalPityTracker);

            foreach (var entity in filled)
                if (entity.BlocksMovement)
                { if (!explorationMode) allMonsters.Add(entity); }
                else
                    allFloorItems.Add(entity);
        }

        // Place stair down
        var stairDown = EntityPlacer.PlaceStairDown(generatedMap, targetDepth: depth + 1, ids);

        // Place floor features (chests, signs, murals, props, traps) — after monsters and items
        // so they don't collide. Build an occupied set from all entities already placed.
        var allFeatures = new List<Entity>();
        // Locked door positions to register in GameState after features are placed.
        // Declared outside the if-block so it is accessible for state registration below.
        var allLockedDoorPlacements = new List<(int X, int Y, int LockColorId)>();
        if (_signpostRegistry != null || _muralRegistry != null || _floorItemPool.Count > 0
            || _interactivePropsRegistry != null || _floorTrapRegistry != null)
        {
            // MuralTracker: carry forward per-run to prevent same mural appearing twice in a run.
            // On new run (no existing tracker), create fresh. On floor transition, carry forward.
            var finalMuralTracker = muralTracker ?? (_muralRegistry != null ? new MuralTracker() : null);

            // Build occupied set from all entities placed so far (player, monsters, items, stair).
            var occupied = new HashSet<(int, int)>();
            occupied.Add((generatedMap.PlayerSpawn.X, generatedMap.PlayerSpawn.Y));
            if (generatedMap.StairDownPos.HasValue) occupied.Add(generatedMap.StairDownPos.Value);
            if (generatedMap.StairUpPos.HasValue) occupied.Add(generatedMap.StairUpPos.Value);
            foreach (var m in allMonsters) occupied.Add((m.X, m.Y));
            foreach (var fi in allFloorItems) occupied.Add((fi.X, fi.Y));

            allFeatures = EntityPlacer.PlaceFloorFeatures(
                generatedMap, ids, rng, depth, occupied,
                _signpostRegistry, _muralRegistry, finalMuralTracker,
                out var lockedDoorPlacements,
                _floorItemPool, _spellItemFactory, _itemFactory, _consumableFactory,
                finalRegistry, finalPool, difficulty,
                lootTagRegistry: _lootTagRegistry, lootPolicy: _lootPolicy,
                propsRegistry: _interactivePropsRegistry, trapRegistry: _floorTrapRegistry);

            allLockedDoorPlacements = lockedDoorPlacements;

            // Don't pass mural tracker for new-run case — Build() doesn't have the old state.
            // Caller (GameController) must thread muralTracker through floor transitions.
            // Store finalMuralTracker back so the caller can read it off the returned GameState.
            muralTracker = finalMuralTracker;
        }

        // Place static portal pair (depth ≥ 3 only, 1 pair per floor).
        // Portals go into state.Portals (same list used by wand-placed portals) so
        // PortalSystem.CheckPortalCollision fires when the player walks onto either tile.
        // Build the occupied set from all entities placed on this floor so far.
        var allPortals = new List<Entity>();
        if (depth >= 3)
        {
            var portalOccupied = new HashSet<(int, int)>();
            portalOccupied.Add((generatedMap.PlayerSpawn.X, generatedMap.PlayerSpawn.Y));
            if (generatedMap.StairDownPos.HasValue) portalOccupied.Add(generatedMap.StairDownPos.Value);
            if (generatedMap.StairUpPos.HasValue)   portalOccupied.Add(generatedMap.StairUpPos.Value);
            foreach (var m  in allMonsters)  portalOccupied.Add((m.X,  m.Y));
            foreach (var fi in allFloorItems) portalOccupied.Add((fi.X, fi.Y));
            foreach (var f  in allFeatures)  portalOccupied.Add((f.X,  f.Y));

            allPortals = EntityPlacer.PlacePortalPairs(generatedMap, ids, rng, depth, portalOccupied);
        }

        // BoonTracker: carry forward from previous floor, or create fresh for new run
        var finalBoonTracker = boonTracker ?? new BoonTracker();

        var state = new GameState(player, allMonsters, generatedMap.Map, rng, turnLimit: 10_000)
        {
            IsDungeonMode = true,
            CurrentDepth = depth,
            StairDown = stairDown,
            IdentificationRegistry = finalRegistry,
            AppearancePool = finalPool,
            Difficulty = difficulty,
            BoonTracker = finalBoonTracker,
            BoonTable = _boonTable,
            Props = generatedMap.Props,
            MuralTracker = muralTracker,
            PityTracker = finalPityTracker,
            PersistentState = persistentState,
            // Expose the live id allocator (already used for this floor's placement) so its next-id
            // watermark is captured by the mid-run save — post-resume spawns then never reuse an id the
            // original allocator had already advanced past (M1.4 4b; closes the 4a.3b-2 carry-forward).
            IdAllocator = ids,
            // Expose rooms for ETP sanity analysis (EtpSanityHarness.AnalyzeLevel)
            Rooms = generatedMap.Rooms,
        };

        // Unshriven Geas: when the push-the-marker job is complete (spec §6.10), floors 4-8
        // get slightly more orc and less undead in encounter composition. The dynamic weight
        // adjustment is deferred until the encounter pool supports per-floor weight overrides;
        // this stub is the read point that will drive it.
        // TODO: apply geas encounter composition tweak (more orc, less undead, floors 4-8)
        // when encounter pool weight override system is implemented.
        _ = persistentState?.UnshrivenGeas.MarkerPushed;

        // Apply depth boon for this floor (first visit only).
        // Must happen after state construction so the boon table is available,
        // and after PlayerCarryForward + ReapplyRingEffects so Fighter is ready.
        if (_boonTable != null)
            BoonSystem.ApplyDepthBoonIfEligible(player, depth, finalBoonTracker, _boonTable);

        // Register floor items into state
        foreach (var item in allFloorItems)
            state.FloorItems.Add(item);

        // Register features into state (chests, signs, murals, props, traps).
        // Key items from locked chest pairs are in allFeatures but must go into FloorItems
        // because they are non-blocking pickups, not interactive features.
        foreach (var feature in allFeatures)
        {
            if (feature.Get<UnderWarden.Logic.ECS.KeyItemComponent>() != null)
                state.FloorItems.Add(feature);
            else
                state.Features.Add(feature);
        }

        // Register static portal pair into state.Portals.
        // state.Portals is the list PortalSystem.CheckPortalCollision reads — adding static portals
        // here makes walk-over teleportation fire automatically with no additional wiring.
        foreach (var portal in allPortals)
            state.Portals.Add(portal);

        // Register locked door positions so TurnController can look up lock colors on bump.
        // allLockedDoorPlacements is populated by EntityPlacer.PlaceFloorFeatures when
        // PlaceLockedDoorPair runs (depth ≥ 2). Empty in scenario mode or at depth 1.
        foreach (var (dx, dy, colorId) in allLockedDoorPlacements)
            state.LockedDoors[(dx, dy)] = colorId;

        // Compute initial FOV so the player sees their starting area immediately.
        // This must happen after IsDungeonMode=true is set — RecomputeFov is a no-op
        // when IsDungeonMode=false (scenario mode).
        state.RecomputeFov();

        return state;
    }

    /// <summary>
    /// Build the Weighing arena (floor 25) — the hand-authored Tribunal Hall. Bypasses procedural
    /// generation: fixed map, player placed at the well, no monsters (the orchestrator raises the
    /// Guardians on the first turn), no down-stair (the descent ends here; the run resolves via the
    /// orchestration into GameState.Ending). Carries the player forward exactly like a normal floor.
    /// </summary>
    private GameState BuildWeighingArena(int depth, SeededRandom rng, Entity? existingPlayer,
        IdentificationRegistry? identificationRegistry, AppearancePool? appearancePool,
        Difficulty difficulty, BoonTracker? boonTracker, MuralTracker? muralTracker,
        PityTrackerType? pityTracker, PersistentRunState? persistentState)
    {
        var arena = WeighingArenaDefinition.Build();

        Entity player;
        if (existingPlayer != null)
        {
            StatusEffectProcessor.ClearAllEffects(existingPlayer);
            player = PlayerCarryForward.Apply(existingPlayer);
            TurnController.ReapplyRingEffects(player);
        }
        else
        {
            player = CreateDefaultPlayer();
        }

        var start = arena.FirstAnchor("player_start") ?? (1, 1);
        player.X = start.Item1;
        player.Y = start.Item2;
        arena.Map.RegisterEntity(player);

        var finalRegistry = identificationRegistry ?? new IdentificationRegistry();
        var finalPool = appearancePool ?? new AppearancePool(_identifiableItems, rng.Seed);
        var finalBoonTracker = boonTracker ?? new BoonTracker();
        var finalPityTracker = pityTracker ?? (_lootPolicy != null ? new PityTrackerType() : null);

        var state = new GameState(player, new List<Entity>(), arena.Map, rng, turnLimit: 10_000)
        {
            IsDungeonMode = true,
            CurrentDepth = depth,
            StairDown = null, // final floor — no descent
            IdentificationRegistry = finalRegistry,
            AppearancePool = finalPool,
            Difficulty = difficulty,
            BoonTracker = finalBoonTracker,
            BoonTable = _boonTable,
            MuralTracker = muralTracker,
            PityTracker = finalPityTracker,
            PersistentState = persistentState,
            WeighingArena = arena,
        };

        state.RecomputeFov();
        return state;
    }

    /// <summary>
    /// Assign TileTheme to every room tile and corridor tile in the generated map.
    /// Each room gets one theme (cohesive look within a room). Corridors always get Dirt.
    /// This method consumes rng — call it after MapGenerator.Generate() finishes.
    /// </summary>
    private static void AssignTileThemes(GeneratedMap generatedMap, int depth, SeededRandom rng)
    {
        var map = generatedMap.Map;

        // Corridors: set Dirt theme first (rooms will overwrite tiles that overlap corridors).
        // Iterate the full map — any Corridor tile gets Dirt.
        for (int x = 0; x < map.Width; x++)
            for (int y = 0; y < map.Height; y++)
                if (map.GetTileKind(x, y) == TileKind.Corridor)
                    map.SetTileTheme(x, y, TileTheme.Dirt);

        // Rooms: each room picks one theme. The theme rect overwrites corridors that run
        // through the interior, which is the desired behavior (room wins over corridor).
        foreach (var room in generatedMap.Rooms)
        {
            var theme = PickRoomTheme(depth, rng);
            // Room bounds are [X, X+Width) x [Y, Y+Height) — SetTileThemeRect uses inclusive bounds
            map.SetTileThemeRect(room.X, room.Y, room.X + room.Width - 1, room.Y + room.Height - 1, theme);
        }
    }

    /// <summary>
    /// Pick a room theme based on dungeon depth.
    /// Base theme is determined by depth band; ~15% chance of an "accent" room one step deeper.
    /// </summary>
    private static TileTheme PickRoomTheme(int depth, SeededRandom rng)
    {
        // Base theme by depth band — matches design doc: grey → crypt → moss
        TileTheme baseTheme = depth switch
        {
            <= 3 => TileTheme.Grey,
            <= 6 => TileTheme.Crypt,
            _    => TileTheme.Moss,
        };

        // ~15% chance: accent room one step deeper than the base — adds variety without chaos
        if (rng.Next(0, 100) < 15)
        {
            baseTheme = baseTheme switch
            {
                TileTheme.Grey  => TileTheme.Crypt,
                TileTheme.Crypt => TileTheme.Moss,
                TileTheme.Moss  => TileTheme.Crypt, // cycle back at deepest
                _               => baseTheme,
            };
        }

        return baseTheme;
    }

    /// <summary>
    /// Create a default starting player for new runs (no carry-forward).
    /// Stats and starting gear match the Python prototype (initialize_new_game.py).
    ///
    /// Starting gear: dagger (main hand), leather armor (chest), 1 healing potion (inventory).
    /// Unarmed damage 1-2 is overridden by equipped dagger (1-4) in CombatResolver.
    ///
    /// NOTE: IDs for starting gear items come from EntityFactory's internal counter. Build()
    /// reads _monsterFactory.EntityFactory.NextId after this method returns to initialize the
    /// EntityIdAllocator, so map-placed entities always start AFTER the last gear ID assigned here.
    ///
    /// TODO: Move starting gear config into entities.yaml player section so it's data-driven
    /// rather than hardcoded here. Low priority until the full item system is built out.
    /// </summary>
    private Entity CreateDefaultPlayer()
    {
        var player = new Entity(0, "Player", 0, 0, blocksMovement: true);
        player.Add(new Combat.Fighter(
            hp: 54,
            strength: 14,
            dexterity: 14,
            constitution: 14,
            accuracy: 3,
            evasion: 0,
            damageMin: 1,
            damageMax: 2) // unarmed; dagger equipped below overrides this in CombatResolver
        {
            CanOpenDoors = true, // player can always open doors
        });

        // Run-scoped aggression tally (TASK-003) — accumulates unprovoked kills across the run.
        // Fresh here per run; carried across floors by PlayerCarryForward.
        player.Add(new RunAggressionTally());

        // Starting equipment — from PoC initialize_new_game.py
        var equipment = new Combat.Equipment();
        player.Add(equipment);

        var dagger = _itemFactory.Create("dagger");
        if (dagger != null)
            equipment.SetSlot(Combat.EquipmentSlot.MainHand, dagger);

        var armor = _itemFactory.Create("leather_armor");
        if (armor != null)
            equipment.SetSlot(Combat.EquipmentSlot.Chest, armor);

        // Starting inventory: 3 healing potions + Wand of Portals (player's core traversal tool).
        // 3 potions (each heals 40 HP) = ~186 effective HP at panic/threshold thresholds.
        // Floor 1 spawns 5-10 orcs that deal ~7 avg damage per hit at 35% hit rate — 1 potion
        // is not enough. The pity system (now ported — PityTracker/LootController) guarantees
        // additional floor drops; 3 starting potions bridge floor 1 before pity ramps in.
        var inventory = new Inventory();
        player.Add(inventory);
        for (int i = 0; i < 3; i++)
        {
            var potion = _consumableFactory.Create("healing_potion");
            if (potion != null)
                inventory.Add(potion);
        }

        // Wand of Portals: always granted at run start. Uses a deterministic dummy rng since
        // the wand is infinite — charge count doesn't matter. The wand is the player's core
        // traversal ability and is never absent from the starting loadout.
        if (_spellItemFactory != null)
        {
            var dummyRng = new SeededRandom(0);
            var portalWand = _spellItemFactory.CreateWand("wand_of_portals", dummyRng);
            if (portalWand != null)
                inventory.Add(portalWand);

            // Hollowmark's Spell-Break: always granted at run start. Sasha's narrative wand —
            // dispels status effects, and frees past-Sasha corpses held by the Under-Warden.
            var spellBreakWand = _spellItemFactory.CreateWand("wand_of_spell_break", dummyRng);
            if (spellBreakWand != null)
                inventory.Add(spellBreakWand);
        }

        return player;
    }

    /// <summary>
    /// Build a starting player from a staged-start gear profile (0c step 8) — region-appropriate
    /// equipment + stats instead of the floor-1 default. Mirrors CreateDefaultPlayer's structure (same
    /// shared factories, so gear IDs advance the same EntityFactory counter) but reads slots/stats/potion
    /// count from the profile. The Wand of Portals + Spell-Break are always granted, same as a normal run.
    /// </summary>
    public Entity CreateGearedPlayer(Balance.GearProfile profile)
    {
        var player = new Entity(0, "Player", 0, 0, blocksMovement: true);
        player.Add(new Combat.Fighter(
            hp: 54 + profile.BonusHp,
            strength: profile.Strength ?? 14,
            dexterity: profile.Dexterity ?? 14,
            constitution: profile.Constitution ?? 14,
            accuracy: 3,
            evasion: 0,
            damageMin: 1,
            damageMax: 2)
        {
            CanOpenDoors = true,
        });

        player.Add(new RunAggressionTally());

        var equipment = new Combat.Equipment();
        player.Add(equipment);
        EquipFromProfile(equipment, Combat.EquipmentSlot.MainHand, profile.MainHand);
        EquipFromProfile(equipment, Combat.EquipmentSlot.Chest,    profile.Chest);
        EquipFromProfile(equipment, Combat.EquipmentSlot.OffHand,  profile.OffHand);
        EquipFromProfile(equipment, Combat.EquipmentSlot.Head,     profile.Head);
        EquipFromProfile(equipment, Combat.EquipmentSlot.Feet,     profile.Feet);

        var inventory = new Inventory();
        player.Add(inventory);
        for (int i = 0; i < profile.HealingPotions; i++)
        {
            var potion = _consumableFactory.Create("healing_potion");
            if (potion != null)
                inventory.Add(potion);
        }

        if (_spellItemFactory != null)
        {
            var dummyRng = new SeededRandom(0);
            var portalWand = _spellItemFactory.CreateWand("wand_of_portals", dummyRng);
            if (portalWand != null)
                inventory.Add(portalWand);
            var spellBreakWand = _spellItemFactory.CreateWand("wand_of_spell_break", dummyRng);
            if (spellBreakWand != null)
                inventory.Add(spellBreakWand);
        }

        return player;
    }

    /// <summary>Create + equip an item id into a slot, if the id is non-empty and the item exists.</summary>
    private void EquipFromProfile(Combat.Equipment equipment, Combat.EquipmentSlot slot, string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return;
        var item = _itemFactory.Create(itemId);
        if (item != null)
            equipment.SetSlot(slot, item);
    }
}
