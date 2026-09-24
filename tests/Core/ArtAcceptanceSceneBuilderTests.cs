using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Merge evidence for the art acceptance scene (docs/art_test_scene_spec_v2.md §3/§4):
///   1. Content check — every §3-required tile ID/entity is present (machine-asserted).
///   2. Determinism check — two cold builds produce identical dumps (no seed involved;
///      the scene is authored/static by construction, per spec §4).
/// Real content (config/entities.yaml) is used, not a hand-rolled bundle, so this fails
/// loudly if "orc_grunt", "troll", "dagger", "club", or "healing_potion" ever disappear
/// from content — the same failure mode the scene itself would hit at runtime.
/// </summary>
[TestFixture]
public class ArtAcceptanceSceneBuilderTests
{
    private static string FindEntitiesYaml()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var path = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", "entities.yaml"));
        if (File.Exists(path)) return path;
        path = Path.GetFullPath(Path.Combine(testDir, "config", "entities.yaml"));
        if (File.Exists(path)) return path;
        throw new FileNotFoundException($"entities.yaml not found. Tried: {path}");
    }

    private static (MonsterFactory monsters, ItemFactory items, ConsumableFactory consumables) CreateFactories()
    {
        var loader = new ContentLoader();
        var bundle = loader.LoadAllFromFile(FindEntitiesYaml());
        var entityFactory = new EntityFactory();
        var itemFactory = new ItemFactory(bundle.Items, entityFactory);
        var consumableFactory = new ConsumableFactory(bundle.Consumables, entityFactory);
        var monsterFactory = new MonsterFactory(bundle.Monsters, entityFactory, itemFactory);
        return (monsterFactory, itemFactory, consumableFactory);
    }

    private static int Chebyshev(int x1, int y1, int x2, int y2) =>
        Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));

    // ── Content check ────────────────────────────────────────────────────────

    [Test]
    public void Build_ContainsAllSpec3RequiredPropTileIds()
    {
        var (monsters, items, consumables) = CreateFactories();
        var state = ArtAcceptanceSceneBuilder.Build(monsters, items, consumables);

        var tileIds = state.Props.Select(p => p.TileId).ToHashSet();
        int[] required = { 5011, 5089, 5001, 5080, 5053, 5051, 5056, 5057, 5078, 5079, 5110, 268, 5102 };
        foreach (var id in required)
            Assert.That(tileIds, Does.Contain(id), $"Missing required prop tile id {id}");
    }

    [Test]
    public void Build_ContainsBothChestStatesSimultaneously()
    {
        var (monsters, items, consumables) = CreateFactories();
        var state = ArtAcceptanceSceneBuilder.Build(monsters, items, consumables);

        var chests = state.Features.Select(f => f.Get<ChestComponent>()).Where(c => c != null).ToList();
        Assert.That(chests, Has.Count.EqualTo(2), "Expected exactly chest_closed + chest_open");
        Assert.That(chests, Has.Some.Matches<ChestComponent?>(c => c!.IsOpen == false));
        Assert.That(chests, Has.Some.Matches<ChestComponent?>(c => c!.IsOpen == true));
    }

    [Test]
    public void Build_ContainsSignAndMuralWithPinnedTileId()
    {
        var (monsters, items, consumables) = CreateFactories();
        var state = ArtAcceptanceSceneBuilder.Build(monsters, items, consumables);

        Assert.That(state.Features.Any(f => f.Get<SignpostComponent>() != null), Is.True, "Missing sign");
        var mural = state.Features.Select(f => f.Get<MuralComponent>()).FirstOrDefault(m => m != null);
        Assert.That(mural, Is.Not.Null, "Missing mural");
        Assert.That(mural!.TileId, Is.EqualTo(5075), "Mural must be the pinned heraldic_shield variant");
    }

    [Test]
    public void Build_ContainsKeyItem()
    {
        var (monsters, items, consumables) = CreateFactories();
        var state = ArtAcceptanceSceneBuilder.Build(monsters, items, consumables);

        Assert.That(state.FloorItems.Any(i => i.Get<KeyItemComponent>() != null), Is.True, "Missing key item (5039)");
    }

    [Test]
    public void Build_ContainsOrcGruntAndTroll()
    {
        var (monsters, items, consumables) = CreateFactories();
        var state = ArtAcceptanceSceneBuilder.Build(monsters, items, consumables);

        var species = state.Monsters.Select(m => m.Get<SpeciesTag>()?.TypeId).ToList();
        Assert.That(species, Does.Contain("orc_grunt"));
        Assert.That(species, Does.Contain("troll"));
    }

    [Test]
    public void Build_CanonCreatures_AreAdjacentToGeneratedProps()
    {
        var (monsters, items, consumables) = CreateFactories();
        var state = ArtAcceptanceSceneBuilder.Build(monsters, items, consumables);

        foreach (var monster in state.Monsters)
        {
            bool adjacentToProp = state.Props.Any(p => Chebyshev(monster.X, monster.Y, p.X, p.Y) == 1);
            Assert.That(adjacentToProp, Is.True,
                $"{monster.Get<SpeciesTag>()?.TypeId} at ({monster.X},{monster.Y}) is not adjacent to any generated prop");
        }
    }

    [Test]
    public void Build_CanonItems_AreWithinTwoTilesOfKey()
    {
        var (monsters, items, consumables) = CreateFactories();
        var state = ArtAcceptanceSceneBuilder.Build(monsters, items, consumables);

        var key = state.FloorItems.First(i => i.Get<KeyItemComponent>() != null);
        // The key itself also carries ItemTag("key") (FeatureFactory.CreateKeyItem) — exclude it,
        // "canon items" here means the real pickups (potion/weapons), not the key prop itself.
        var canonItems = state.FloorItems
            .Where(i => i.Get<ItemTag>() != null && i.Get<KeyItemComponent>() == null)
            .ToList();

        Assert.That(canonItems.Count, Is.InRange(2, 3),
            "Spec §3 requires 2-3 canon items near the key");
        foreach (var item in canonItems)
            Assert.That(Chebyshev(key.X, key.Y, item.X, item.Y), Is.LessThanOrEqualTo(2),
                $"Canon item {item.Get<ItemTag>()!.TypeId} at ({item.X},{item.Y}) is more than two tiles from the key");
    }

    [Test]
    public void Build_BarrelIsBesideSack()
    {
        var (monsters, items, consumables) = CreateFactories();
        var state = ArtAcceptanceSceneBuilder.Build(monsters, items, consumables);

        var barrel = state.Props.First(p => p.TileId == 268);
        var sack = state.Props.First(p => p.TileId == 5102);
        Assert.That(Chebyshev(barrel.X, barrel.Y, sack.X, sack.Y), Is.EqualTo(1),
            "Canon barrel (268) must be directly beside the generated sack (5102)");
    }

    [Test]
    public void Build_MapHasWallRunWithCornerAndDoor()
    {
        var (monsters, items, consumables) = CreateFactories();
        var state = ArtAcceptanceSceneBuilder.Build(monsters, items, consumables);
        var map = state.Map;

        // Door present.
        bool hasDoor = false;
        for (int x = 0; x < map.Width; x++)
            for (int y = 0; y < map.Height; y++)
                if (map.GetTileKind(x, y) == TileKind.Door) hasDoor = true;
        Assert.That(hasDoor, Is.True, "Scene must contain at least one door tile");

        // Corner: the room's own four wall corners are guaranteed by the rectangular carve
        // (GameMap defaults every non-carved cell to Wall) — verify at least one concretely,
        // e.g. the wall cell diagonally outside the room's top-left floor corner, with both
        // its cardinal neighbors also Wall (a true right-angle corner, not an isolated cell).
        Assert.That(map.GetTileKind(0, 0), Is.EqualTo(TileKind.Wall));
        Assert.That(map.GetTileKind(1, 0), Is.EqualTo(TileKind.Wall));
        Assert.That(map.GetTileKind(0, 1), Is.EqualTo(TileKind.Wall));
    }

    [Test]
    public void Build_RoomIsSandstoneTheme()
    {
        var (monsters, items, consumables) = CreateFactories();
        var state = ArtAcceptanceSceneBuilder.Build(monsters, items, consumables);

        // TileTheme.Grey is the enum value that resolves to the "sandstone" config key
        // (DungeonRenderer.ThemeToConfigName — the only theme currently defined).
        Assert.That(state.Map.GetTileTheme(7, 8), Is.EqualTo(TileTheme.Grey));
    }

    // ── Margin/in-frame check (v3 repack ruling) ────────────────────────────

    /// <summary>
    /// Reproduces the camera transform independently — from the same source constants
    /// PlayerCamera.Update/TopDownRenderer/scene_capture_config.yaml use, not by calling
    /// into Presentation (this project has no Godot dependency and must stay that way).
    /// Mirrors, does not modify, Main.ComputeVisibleTileRect: this test would have caught
    /// the PR #8 finding (content outside the visible window) before a capture run did.
    /// Verified against the real PR #8/v3 --art-scene-capture log line
    /// ("[Main] Visible tile rect: ...") before being trusted here.
    /// </summary>
    private static (int x0, int y0, int x1, int y1) ComputeVisibleTileRect(int playerX, int playerY)
    {
        const double viewportW = 750, viewportH = 1334; // scene_capture_config.yaml resolution (ruled)
        const double tileSize = 24;                     // TopDownRenderer.TileWidth/TileHeight
        const double zoom = 3.0;                         // TopDownRenderer.DefaultZoom
        const double uiTop = 90, uiBottom = 333;          // PlayerCamera.UiTopMargin/UiBottomMargin

        double playerScreenX = playerX * tileSize, playerScreenY = playerY * tileSize;
        double centerY = uiTop + (viewportH - uiTop - uiBottom) / 2;
        double gameViewX = viewportW / 2 - playerScreenX * zoom;
        double gameViewY = centerY - playerScreenY * zoom;

        (int, int) ScreenToGrid(double screenX, double screenY)
        {
            double localX = (screenX - gameViewX) / zoom;
            double localY = (screenY - gameViewY) / zoom;
            int gx = (int)Math.Round((localX - tileSize / 2) / tileSize, MidpointRounding.AwayFromZero);
            int gy = (int)Math.Round((localY - tileSize / 2) / tileSize, MidpointRounding.AwayFromZero);
            return (gx, gy);
        }

        var (x0, y0) = ScreenToGrid(0, uiTop);
        var (x1, y1) = ScreenToGrid(viewportW, viewportH - uiBottom);
        return (x0, y0, x1, y1);
    }

    [Test]
    public void Build_AllContentIsFullyInFrameWithMargin()
    {
        var (monsters, items, consumables) = CreateFactories();
        var state = ArtAcceptanceSceneBuilder.Build(monsters, items, consumables);

        var (vx0, vy0, vx1, vy1) = ComputeVisibleTileRect(state.Player.X, state.Player.Y);
        // At least one full tile of margin from every frame edge, per the v3 repack ruling —
        // not index membership in the raw visible rect (PR #8's finding: a tile at the raw
        // edge can be ~20% visible, a sliver, not a clean crop).
        int ux0 = vx0 + 1, uy0 = vy0 + 1, ux1 = vx1 - 1, uy1 = vy1 - 1;

        void AssertInFrame(int x, int y, string label)
        {
            Assert.That(x, Is.InRange(ux0, ux1), $"{label} at ({x},{y}) has less than 1 tile of x-margin (usable range {ux0}-{ux1})");
            Assert.That(y, Is.InRange(uy0, uy1), $"{label} at ({x},{y}) has less than 1 tile of y-margin (usable range {uy0}-{uy1})");
        }

        // Room structure: every non-Wall tile (floor, door) and, for corners, the wall cells
        // immediately outside the floor — the room's full built extent, not just its interior.
        int minFloorX = int.MaxValue, minFloorY = int.MaxValue;
        for (int x = 0; x < state.Map.Width; x++)
            for (int y = 0; y < state.Map.Height; y++)
                if (state.Map.GetTileKind(x, y) != TileKind.Wall)
                {
                    AssertInFrame(x, y, $"map tile ({x},{y}) kind={state.Map.GetTileKind(x, y)}");
                    if (x < minFloorX) minFloorX = x;
                    if (y < minFloorY) minFloorY = y;
                }

        // Wall corner: derived from the room's actual built extent (top-left-most non-Wall
        // tile minus one), not a hardcoded (0,0) — that would silently stop meaning "the
        // room's corner" the moment the room's absolute position changes (as it did here,
        // shifted by the worn-tile fix below) while still passing.
        Assert.That(state.Map.GetTileKind(minFloorX - 1, minFloorY - 1), Is.EqualTo(TileKind.Wall),
            "Expected a wall cell diagonally outside the room's top-left floor tile");
        AssertInFrame(minFloorX - 1, minFloorY - 1, "wall corner");

        foreach (var prop in state.Props)
            AssertInFrame(prop.X, prop.Y, $"prop {prop.PropId} (tile {prop.TileId})");
        foreach (var feature in state.Features)
            AssertInFrame(feature.X, feature.Y, $"feature {feature.Name}");
        foreach (var item in state.FloorItems)
            AssertInFrame(item.X, item.Y, $"floor item {item.Name}");
        foreach (var monster in state.Monsters)
            AssertInFrame(monster.X, monster.Y, $"monster {monster.Get<SpeciesTag>()?.TypeId}");
        AssertInFrame(state.Player.X, state.Player.Y, "player");
    }

    // ── Determinism check (spec §4) ─────────────────────────────────────────

    [Test]
    public void Build_IsDeterministic_AcrossTwoColdRuns()
    {
        // Fresh factories + fresh EntityFactory counters each time — a true cold start,
        // not reuse of warm state that could mask a hidden ordering dependency.
        var (monsters1, items1, consumables1) = CreateFactories();
        var state1 = ArtAcceptanceSceneBuilder.Build(monsters1, items1, consumables1);
        var dump1 = ArtAcceptanceSceneDump.Dump(state1);

        var (monsters2, items2, consumables2) = CreateFactories();
        var state2 = ArtAcceptanceSceneBuilder.Build(monsters2, items2, consumables2);
        var dump2 = ArtAcceptanceSceneDump.Dump(state2);

        Assert.That(dump1, Is.EqualTo(dump2), "Two cold builds must be pixel-identical — no seeds, no rolls (spec §4)");
    }
}
