using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Core;

/// <summary>
/// Builds the fixed, authored art-acceptance-test scene (docs/art_test_scene_spec_v2.md §3).
///
/// This is authored floor data, not procedural generation. It exists specifically because
/// ScenarioDefinition (config/levels/*.yaml) has no vocabulary for room geometry or static
/// props, and RoomPropPlacer's archetype-recipe placement has no mechanism to pin exact
/// tile IDs or guarantee specific asset co-presence — the two things this scene needs.
///
/// v3 repack (docs/art_test_scene_spec_v2.md ruling): the first capture (PR #8) measured
/// the actual visible tile rect at the ruled reference resolution/zoom as (1,10)-(12,23)
/// against the original 14x18 room — most of the room fell outside the frame. This is the
/// same content, repacked into an 8x9 interior sized to fit entirely inside that visible
/// window with a full tile of margin on every side. See
/// ArtAcceptanceSceneBuilderTests.Build_AllContentIsFullyInFrameWithMargin for the
/// machine-derived proof (it reproduces the camera math independently, not by trusting
/// this class's own numbers).
///
/// Room absolute position (not just shape) matters here: FloorComposer's worn-tile noise
/// (docs/art_test_scene_spec_v2.md item 4) is a function of a cell's absolute (x,y). Every
/// blocking prop calls MarkPropCell (matching production — RoomPropPlacer does the same), which
/// makes its cell non-walkable; FloorComposer's Pass 1 only assigns a floor variant to walkable
/// cells, so prop cells are never worn-eligible. In this densely-packed 8x9 room that leaves a
/// small worn-eligible pool, and at the room's first-tried absolute position none exceeded the
/// worn threshold (noise > 0.72) — a real, verified-by-live-capture zero. The room is therefore
/// shifted by exactly (0,+1) — same shape, same relative content layout, one tile down — enough
/// to bring one eligible cell's noise sample above threshold (0.84) so the scene satisfies spec
/// §5's "≥1 floor_worn in frame". This is a worn-coverage lever (translating the room), NOT a
/// renderer change: FloorComposer's seed stays Render's own default (0), untouched. (The shift
/// is independent of the prop-pedestal render bug — that was FloorComposer's edge-darkening pass
/// wrongly treating prop cells as walls, fixed at source there; props no longer affect shading,
/// and the shift's worn value is unchanged by that fix — verified worn=1 at (4,9) before & after.)
/// </summary>
public static class ArtAcceptanceSceneBuilder
{
    // Interior room is 8 wide x 9 tall (x: 1..8, y: 2..10), with a 1-tile wall border and a
    // 1-tile door + corridor stub on the south wall. Total map: 10 x 13.
    private const int MapWidth = 10;
    private const int MapHeight = 13;
    private const int RoomX0 = 1, RoomY0 = 2, RoomX1 = 8, RoomY1 = 10;
    private const int DoorX = 5, DoorY = 11;
    private const int CorridorStubX = 5, CorridorStubY = 12;

    // Player anchor. Chosen (alongside the room dimensions above) so the full room —
    // walls, door, and corridor stub included — sits inside the visible tile rect with
    // exactly the required 1-tile margin on every side, per
    // ArtAcceptanceSceneBuilderTests.Build_AllContentIsFullyInFrameWithMargin. The
    // visible-rect computation is translation-invariant in (player, room) together, so this
    // is the same margin proof as the pre-shift layout, verified again after the shift by a
    // live --art-scene-capture run (see the capture-harness PR description).
    private const int PlayerX = 5, PlayerY = 7;

    public static GameState Build(
        MonsterFactory monsterFactory,
        ItemFactory itemFactory,
        ConsumableFactory consumableFactory)
    {
        var map = BuildMap();

        var player = CreatePlayer();
        player.X = PlayerX;
        player.Y = PlayerY;
        map.RegisterEntity(player);

        var monsters = new List<Entity>();
        AddMonster(monsters, map, monsterFactory, "orc_grunt", x: 3, y: 3);  // adjacent to anvil (2,3)
        AddMonster(monsters, map, monsterFactory, "troll", x: 4, y: 7);       // adjacent to table (4,6)

        var props = BuildProps();
        foreach (var prop in props)
            if (prop.BlocksMovement)
                map.MarkPropCell(prop.X, prop.Y);

        // Features (chest/sign/mural) and the key item share one EntityIdAllocator, started
        // after the shared EntityFactory's counter — same pattern as DungeonFloorBuilder
        // (see DungeonFloorBuilder.cs: `new EntityIdAllocator(startFrom: _monsterFactory.EntityFactory.NextId)`)
        // so ids never collide with monster/item entities created above.
        var ids = new EntityIdAllocator(startFrom: monsterFactory.EntityFactory.NextId);

        var features = BuildFeatures(ids);
        foreach (var feature in features)
            map.RegisterEntity(feature);

        var floorItems = BuildFloorItems(ids, itemFactory, consumableFactory);
        foreach (var item in floorItems)
            map.RegisterEntity(item);

        map.RevealAll(); // Static scene — everything visible from the first frame, no fog.

        // GameState.Rng is a required constructor parameter but is never drawn from here —
        // this scene runs no turns and rolls nothing (spec §4: "no seeds, no rolls"). The
        // fixed 0 is an inert placeholder, not a seed choice.
        var state = new GameState(player, monsters, map, new SeededRandom(0), turnLimit: 1)
        {
            IsDungeonMode = true,
            CurrentDepth = 1,
            Props = props,
        };
        foreach (var item in floorItems)
            state.FloorItems.Add(item);
        foreach (var feature in features)
            state.Features.Add(feature);

        return state;
    }

    private static GameMap BuildMap()
    {
        var map = new GameMap(MapWidth, MapHeight, allWalls: true);

        for (int x = RoomX0; x <= RoomX1; x++)
            for (int y = RoomY0; y <= RoomY1; y++)
                map.SetTile(x, y, TileKind.Floor);

        map.SetTile(DoorX, DoorY, TileKind.Door);
        map.SetTile(CorridorStubX, CorridorStubY, TileKind.Floor);

        // TileTheme.Grey resolves to the "sandstone" config key (the only theme currently
        // defined — see DungeonRenderer.ThemeToConfigName). Default-initialized to Grey
        // already; set explicitly for readability.
        map.SetTileThemeRect(0, 0, MapWidth - 1, MapHeight - 1, TileTheme.Grey);

        return map;
    }

    private static Entity CreatePlayer()
    {
        // Default scenario-player stats (matches ScenarioPlayer defaults in ScenarioDefinition.cs).
        // Equipment/inventory are intentionally empty — spec §3 does not require specific
        // player gear, only "standard gameplay HUD visible".
        var player = new Entity(0, "Player", 0, 0, blocksMovement: true);
        player.Add(new Fighter(
            hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        player.Add(new SpeedBonusTracker(baseRatio: 0.25));
        return player;
    }

    private static void AddMonster(
        List<Entity> monsters, GameMap map, MonsterFactory monsterFactory,
        string monsterId, int x, int y)
    {
        // rng: null deterministically skips equipment-roll randomness (see class docstring) —
        // not an oversight, the mechanism spec §4 relies on for "no rolls".
        var monster = monsterFactory.Create(monsterId, x, y, depth: 1, rng: null);
        if (monster == null)
            throw new InvalidOperationException(
                $"ArtAcceptanceSceneBuilder: monster '{monsterId}' not found in content — " +
                "spec §3 requires it. Check config/entities.yaml.");
        monsters.Add(monster);
        map.RegisterEntity(monster);
    }

    /// <summary>
    /// Room props (furniture placed via PlacedProp — the same record RoomPropPlacer emits).
    /// Tile IDs are pinned explicitly per spec §3; props.yaml's variant lists are irrelevant
    /// here (authored data, not a procedural roll).
    /// </summary>
    private static List<PlacedProp> BuildProps()
    {
        return new List<PlacedProp>
        {
            // Smithy cluster (wall_adjacent forge + tool_rack, row y=2 against the top wall)
            // plus free_standing anvil — §3 "wall_adjacent" / "free_standing".
            new("forge",      X: 2, Y: 2, FootprintW: 1, FootprintH: 1, BlocksMovement: true,  TileId: 5011),
            new("tool_rack",  X: 4, Y: 2, FootprintW: 1, FootprintH: 1, BlocksMovement: true,  TileId: 5089),
            new("anvil",      X: 2, Y: 3, FootprintW: 1, FootprintH: 1, BlocksMovement: true,  TileId: 5001),

            // Free-standing candelabra — §3 "free_standing". Placed at (6,4), one clear tile
            // below the wall mural at (6,2): at (6,3) their stacked silhouettes merged in the
            // capture (gate finding 2026-08), so the candelabra is separated by an empty cell.
            new("candelabra", X: 6, Y: 4, FootprintW: 1, FootprintH: 1, BlocksMovement: true,  TileId: 5080),

            // Center table + chairs — §3 "center".
            // Table tile pinned to 5053 — the canon-substituted variant (canon world tile 319),
            // so the scene shows the register-corrected table next to the canon chairs (5051/5056/
            // 5057 ← canon 321). The other table variants (5052/5054/5055) are still generated and
            // go through the Part B fineness sweep.
            new("table",      X: 4, Y: 6, FootprintW: 1, FootprintH: 1, BlocksMovement: true,  TileId: 5053),
            new("chair",      X: 3, Y: 6, FootprintW: 1, FootprintH: 1, BlocksMovement: true,  TileId: 5051),
            new("chair",      X: 5, Y: 6, FootprintW: 1, FootprintH: 1, BlocksMovement: true,  TileId: 5056),
            new("chair",      X: 4, Y: 5, FootprintW: 1, FootprintH: 1, BlocksMovement: true,  TileId: 5057),

            // Floor overlays — §3 "floor_overlay". Non-blocking.
            new("rubble",     X: 2, Y: 5, FootprintW: 1, FootprintH: 1, BlocksMovement: false, TileId: 5078),
            new("rubble",     X: 7, Y: 5, FootprintW: 1, FootprintH: 1, BlocksMovement: false, TileId: 5079),
            new("puddle",     X: 6, Y: 5, FootprintW: 1, FootprintH: 1, BlocksMovement: false, TileId: 5110),

            // Canon barrel (268) beside generated sack (5102) — §3 nearest-equivalent pairing.
            new("barrel",     X: 7, Y: 8, FootprintW: 1, FootprintH: 1, BlocksMovement: true,  TileId: 268),
            new("sack",       X: 8, Y: 8, FootprintW: 1, FootprintH: 1, BlocksMovement: true,  TileId: 5102),
        };
    }

    /// <summary>
    /// Interactive features (chests, sign, mural) — rendered by DungeonRenderer Pass 5 via
    /// ChestComponent/SignpostComponent/MuralComponent, resolved through TileThemeConfig
    /// (chest/sign) or MuralComponent.TileId (mural) exactly as generated floors are.
    /// </summary>
    private static List<Entity> BuildFeatures(EntityIdAllocator ids)
    {
        var chestClosed = FeatureFactory.CreateChest(2, 8, ids);

        var chestOpen = FeatureFactory.CreateChest(6, 8, ids);
        chestOpen.Get<ChestComponent>()!.IsOpen = true;

        var sign = FeatureFactory.CreateSignpost(
            8, 2, ids,
            message: "The forge has gone cold, but the anvil remembers every strike.",
            signType: "lore");

        // tileId 5075 pinned explicitly — the heraldic_shield mural landed in the burn-down
        // (the earlier gold_landscape pictorial was retired per the 2026-07 mural ruling).
        var mural = FeatureFactory.CreateMural(
            6, 2, ids,
            text: "A heraldic shield, its blazon still bright on a plain field.",
            muralId: "art_scene_mural_heraldic_shield",
            tileId: 5075);

        return new List<Entity> { chestClosed, chestOpen, sign, mural };
    }

    /// <summary>
    /// Floor items: the key (5039, via the same world_24x24 direct-render path
    /// ItemSpriteManager uses for KeyItemComponent) plus 2-3 canon items within two tiles
    /// of it, forcing a direct canon-vs-generated item comparison per spec §3.
    /// </summary>
    private static List<Entity> BuildFloorItems(
        EntityIdAllocator ids, ItemFactory itemFactory, ConsumableFactory consumableFactory)
    {
        var key = FeatureFactory.CreateKeyItem(4, 9, ids, lockColorId: 0);

        var items = new List<Entity> { key };

        var potion = consumableFactory.Create("healing_potion");
        if (potion == null)
            throw new InvalidOperationException(
                "ArtAcceptanceSceneBuilder: consumable 'healing_potion' not found — check config/entities.yaml.");
        potion.X = 3; potion.Y = 9;
        items.Add(potion);

        var dagger = itemFactory.Create("dagger");
        if (dagger == null)
            throw new InvalidOperationException(
                "ArtAcceptanceSceneBuilder: item 'dagger' not found — check config/entities.yaml.");
        dagger.X = 5; dagger.Y = 9;
        items.Add(dagger);

        var club = itemFactory.Create("club");
        if (club == null)
            throw new InvalidOperationException(
                "ArtAcceptanceSceneBuilder: item 'club' not found — check config/entities.yaml.");
        club.X = 4; club.Y = 10;
        items.Add(club);

        return items;
    }
}
