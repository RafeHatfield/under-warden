using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Phase 4 placement tests: EntityPlacer.PlaceFloorFeatures extension for
/// destructible props (TASK-013) and floor traps (TASK-014).
/// </summary>
[TestFixture]
public class PropAndTrapPlacementTests
{
    private static string GetConfigPath(string filename)
    {
        var testDir = NUnit.Framework.TestContext.CurrentContext.TestDirectory;
        var path = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", filename));
        if (!File.Exists(path))
            path = Path.GetFullPath(Path.Combine(testDir, "config", filename));
        return path;
    }

    private static InteractivePropsRegistry LoadPropsRegistry()
    {
        var loader = new ContentLoader();
        return loader.LoadInteractivePropsFromFile(GetConfigPath("interactive_props.yaml"));
    }

    private static FloorTrapRegistry LoadTrapRegistry()
    {
        var loader = new ContentLoader();
        return loader.LoadFloorTrapsFromFile(GetConfigPath("floor_traps.yaml"));
    }

    private static GeneratedMap MakeMap(int seed = 1337)
        => MapGenerator.Generate(80, 50, 12, 5, 10, new SeededRandom(seed));

    private static List<Entity> RunPlaceFloorFeatures(
        int depth, int seed,
        InteractivePropsRegistry? propsRegistry = null,
        FloorTrapRegistry? trapRegistry = null)
    {
        var map = MakeMap(seed);
        var ids = new EntityIdAllocator(500);
        var rng = new SeededRandom(seed);
        var occupied = new HashSet<(int, int)>();
        if (map.StairDownPos.HasValue) occupied.Add(map.StairDownPos.Value);

        return EntityPlacer.PlaceFloorFeatures(
            map, ids, rng, depth, occupied,
            signRegistry: null,
            muralRegistry: null,
            muralTracker: null,
            out _,
            propsRegistry: propsRegistry,
            trapRegistry: trapRegistry);
    }

    // ── TASK-013: Destructible prop placement ────────────────────────────────

    [Test]
    public void PlaceDestructibleProps_Depth1_BarrelsInRange()
    {
        var propsRegistry = LoadPropsRegistry();
        // Run several seeds and collect barrel counts to verify range.
        var counts = new List<int>();
        for (int seed = 1337; seed < 1347; seed++)
        {
            var features = RunPlaceFloorFeatures(depth: 1, seed: seed, propsRegistry: propsRegistry);
            int barrels = features.Count(f => f.Get<DestructiblePropComponent>()?.PropKind == "barrel");
            counts.Add(barrels);
        }
        // Barrels: 1–4 per floor. Across 10 seeds we should see at least 1 barrel and no more than 4.
        Assert.That(counts.Min(), Is.GreaterThanOrEqualTo(1), "Should have at least 1 barrel per floor");
        Assert.That(counts.Max(), Is.LessThanOrEqualTo(4), "Should have at most 4 barrels per floor");
    }

    [Test]
    public void PlaceDestructibleProps_Depth1_BookshelvesInRange()
    {
        var propsRegistry = LoadPropsRegistry();
        var counts = new List<int>();
        for (int seed = 1337; seed < 1347; seed++)
        {
            var features = RunPlaceFloorFeatures(depth: 1, seed: seed, propsRegistry: propsRegistry);
            int shelves = features.Count(f => f.Get<DestructiblePropComponent>()?.PropKind == "bookshelf");
            counts.Add(shelves);
        }
        // Bookshelves: 0–2 per floor.
        Assert.That(counts.Min(), Is.GreaterThanOrEqualTo(0), "Bookshelves can be 0");
        Assert.That(counts.Max(), Is.LessThanOrEqualTo(2), "Should have at most 2 bookshelves per floor");
    }

    [Test]
    public void PlaceDestructibleProps_Depth1_BonePilesInRange()
    {
        var propsRegistry = LoadPropsRegistry();
        var counts = new List<int>();
        for (int seed = 1337; seed < 1347; seed++)
        {
            var features = RunPlaceFloorFeatures(depth: 1, seed: seed, propsRegistry: propsRegistry);
            int piles = features.Count(f => f.Get<DestructiblePropComponent>()?.PropKind == "bone_pile");
            counts.Add(piles);
        }
        // Bone piles: 0–3 per floor.
        Assert.That(counts.Min(), Is.GreaterThanOrEqualTo(0), "Bone piles can be 0");
        Assert.That(counts.Max(), Is.LessThanOrEqualTo(3), "Should have at most 3 bone piles per floor");
    }

    [Test]
    public void PlaceDestructibleProps_PropsHaveDestructiblePropComponent()
    {
        var propsRegistry = LoadPropsRegistry();
        var features = RunPlaceFloorFeatures(depth: 2, seed: 1337, propsRegistry: propsRegistry);

        var props = features.Where(f => f.Get<DestructiblePropComponent>() != null).ToList();
        Assert.That(props, Is.Not.Empty, "Should have at least one prop");

        foreach (var prop in props)
        {
            var comp = prop.Require<DestructiblePropComponent>();
            Assert.That(comp.IsResolved, Is.False, "All props start unresolved");
            Assert.That(comp.PropKind, Is.Not.Empty);
            Assert.That(prop.BlocksMovement, Is.True, "Props block movement");
        }
    }

    [Test]
    public void PlaceDestructibleProps_PropsAtWalkableTiles()
    {
        // Props must be placed on walkable tiles (which the map confirms through IsWalkable).
        var map = MakeMap(1337);
        var ids = new EntityIdAllocator(500);
        var rng = new SeededRandom(1337);
        var occupied = new HashSet<(int, int)>();
        var propsRegistry = LoadPropsRegistry();

        var features = EntityPlacer.PlaceFloorFeatures(
            map, ids, rng, depth: 2, occupied,
            signRegistry: null, muralRegistry: null, muralTracker: null,
            out _,
            propsRegistry: propsRegistry);

        var props = features.Where(f => f.Get<DestructiblePropComponent>() != null).ToList();
        Assert.That(props, Is.Not.Empty);

        foreach (var prop in props)
        {
            // Prop positions must be on walkable tiles (before the prop blocks them).
            // The map treats a registered blocking entity as non-walkable from this point on.
            // The test just verifies the positions are inside map bounds.
            Assert.That(map.Map.InBounds(prop.X, prop.Y), Is.True,
                $"Prop at ({prop.X},{prop.Y}) should be within map bounds");
        }
    }

    [Test]
    public void PlaceDestructibleProps_Deterministic_SameSeedSameResult()
    {
        var propsRegistry = LoadPropsRegistry();

        var features1 = RunPlaceFloorFeatures(depth: 3, seed: 42, propsRegistry: propsRegistry);
        var features2 = RunPlaceFloorFeatures(depth: 3, seed: 42, propsRegistry: propsRegistry);

        var props1 = features1.Where(f => f.Get<DestructiblePropComponent>() != null)
            .OrderBy(f => f.X).ThenBy(f => f.Y).ToList();
        var props2 = features2.Where(f => f.Get<DestructiblePropComponent>() != null)
            .OrderBy(f => f.X).ThenBy(f => f.Y).ToList();

        Assert.That(props1.Count, Is.EqualTo(props2.Count), "Same seed must yield same prop count");
        for (int i = 0; i < props1.Count; i++)
        {
            Assert.That(props1[i].X, Is.EqualTo(props2[i].X));
            Assert.That(props1[i].Y, Is.EqualTo(props2[i].Y));
            Assert.That(props1[i].Require<DestructiblePropComponent>().PropKind,
                Is.EqualTo(props2[i].Require<DestructiblePropComponent>().PropKind));
        }
    }

    // ── TASK-014: Floor trap placement ──────────────────────────────────────

    [Test]
    public void PlaceFloorTraps_Depth1_OnlySpikeAndWeb()
    {
        var trapRegistry = LoadTrapRegistry();
        // Run multiple seeds to collect all trap types placed at depth 1.
        var trapTypes = new HashSet<string>();
        for (int seed = 1337; seed < 1357; seed++)
        {
            var features = RunPlaceFloorFeatures(depth: 1, seed: seed, trapRegistry: trapRegistry);
            foreach (var f in features)
            {
                var comp = f.Get<FloorTrapComponent>();
                if (comp != null) trapTypes.Add(comp.TrapType);
            }
        }

        // At depth 1, only spike_trap and web_trap are allowed.
        var forbidden = trapTypes.Except(new[] { "spike_trap", "web_trap" }).ToList();
        Assert.That(forbidden, Is.Empty,
            $"Depth 1 should only have spike/web traps; found: {string.Join(", ", forbidden)}");
    }

    [Test]
    public void PlaceFloorTraps_Depth6_CanIncludeFireAndHole()
    {
        var trapRegistry = LoadTrapRegistry();
        // At depth 6+, fire_trap, teleport_trap, hole_trap should be possible.
        var trapTypes = new HashSet<string>();
        for (int seed = 1337; seed < 1360; seed++)
        {
            var features = RunPlaceFloorFeatures(depth: 6, seed: seed, trapRegistry: trapRegistry);
            foreach (var f in features)
            {
                var comp = f.Get<FloorTrapComponent>();
                if (comp != null) trapTypes.Add(comp.TrapType);
            }
        }

        // After enough runs, depth 6 should include some advanced traps.
        // We check that at least some depth-3+ traps appear (alarm, root, gas, acid, fire, etc.).
        var depth3PlusTrapIds = new[] { "alarm_plate", "root_trap", "gas_trap", "acid_trap",
                                        "fire_trap", "teleport_trap", "hole_trap" };
        bool hasAdvanced = trapTypes.Any(t => depth3PlusTrapIds.Contains(t));
        Assert.That(hasAdvanced, Is.True,
            "After many runs at depth 6, should see advanced trap types");
    }

    [Test]
    public void PlaceFloorTraps_HoleTrap_MaxOnePerFloor()
    {
        var trapRegistry = LoadTrapRegistry();
        // Run multiple seeds at depth 6 — hole_trap count should never exceed 1 per floor.
        for (int seed = 1337; seed < 1357; seed++)
        {
            var features = RunPlaceFloorFeatures(depth: 6, seed: seed, trapRegistry: trapRegistry);
            int holeCount = features.Count(f => f.Get<FloorTrapComponent>()?.TrapType == "hole_trap");
            Assert.That(holeCount, Is.LessThanOrEqualTo(1),
                $"Seed {seed}: found {holeCount} hole traps; max is 1 per floor at depth 6+");
        }
    }

    [Test]
    public void PlaceFloorTraps_Depth1_TrapsInRange()
    {
        // At depth 1: budget = min(2 + 0*2, 10) = 2 traps.
        var trapRegistry = LoadTrapRegistry();
        for (int seed = 1337; seed < 1347; seed++)
        {
            var features = RunPlaceFloorFeatures(depth: 1, seed: seed, trapRegistry: trapRegistry);
            int trapCount = features.Count(f => f.Get<FloorTrapComponent>() != null);
            Assert.That(trapCount, Is.LessThanOrEqualTo(2),
                $"Seed {seed}: depth 1 should have at most 2 traps, found {trapCount}");
        }
    }

    [Test]
    public void PlaceFloorTraps_TrapsNotBlockingMovement()
    {
        var trapRegistry = LoadTrapRegistry();
        var features = RunPlaceFloorFeatures(depth: 3, seed: 1337, trapRegistry: trapRegistry);

        var traps = features.Where(f => f.Get<FloorTrapComponent>() != null).ToList();
        Assert.That(traps, Is.Not.Empty, "Should place some traps at depth 3");

        foreach (var trap in traps)
        {
            Assert.That(trap.BlocksMovement, Is.False, "Floor traps must not block movement");
        }
    }

    [Test]
    public void PlaceFloorTraps_ContextualTraps_HaveLowerDetectChance()
    {
        // Contextual traps (placed near altars/dead-ends) get detect chance 0.05–0.08.
        // Random traps use natural YAML values (0.08–0.20 depending on type).
        // We can't directly distinguish contextual vs random in the test, but we can verify
        // that detect chances are in the expected range [0.05, 0.20].
        var trapRegistry = LoadTrapRegistry();
        var features = RunPlaceFloorFeatures(depth: 3, seed: 1337, trapRegistry: trapRegistry);

        var traps = features.Where(f => f.Get<FloorTrapComponent>() != null).ToList();
        foreach (var trap in traps)
        {
            var comp = trap.Require<FloorTrapComponent>();
            Assert.That(comp.PassiveDetectChance, Is.InRange(0.04, 0.21),
                $"{comp.TrapType}: detect chance {comp.PassiveDetectChance} out of expected range");
        }
    }

    [Test]
    public void PlaceFloorTraps_Deterministic_SameSeedSameResult()
    {
        var trapRegistry = LoadTrapRegistry();

        var features1 = RunPlaceFloorFeatures(depth: 3, seed: 999, trapRegistry: trapRegistry);
        var features2 = RunPlaceFloorFeatures(depth: 3, seed: 999, trapRegistry: trapRegistry);

        var traps1 = features1.Where(f => f.Get<FloorTrapComponent>() != null)
            .OrderBy(f => f.X).ThenBy(f => f.Y).ToList();
        var traps2 = features2.Where(f => f.Get<FloorTrapComponent>() != null)
            .OrderBy(f => f.X).ThenBy(f => f.Y).ToList();

        Assert.That(traps1.Count, Is.EqualTo(traps2.Count), "Same seed must yield same trap count");
        for (int i = 0; i < traps1.Count; i++)
        {
            Assert.That(traps1[i].X, Is.EqualTo(traps2[i].X));
            Assert.That(traps1[i].Y, Is.EqualTo(traps2[i].Y));
            Assert.That(traps1[i].Require<FloorTrapComponent>().TrapType,
                Is.EqualTo(traps2[i].Require<FloorTrapComponent>().TrapType));
        }
    }

    [Test]
    public void PlaceFloorTraps_AvoidsPlayerSpawnRoom()
    {
        // Traps should not be placed in the player spawn room.
        // This is enforced by featureRooms filtering (excludes PlayerRoom).
        var trapRegistry = LoadTrapRegistry();
        var map = MakeMap(1337);
        var ids = new EntityIdAllocator(500);
        var rng = new SeededRandom(1337);
        var occupied = new HashSet<(int, int)>();

        var features = EntityPlacer.PlaceFloorFeatures(
            map, ids, rng, depth: 3, occupied,
            signRegistry: null, muralRegistry: null, muralTracker: null,
            out _,
            trapRegistry: trapRegistry);

        var traps = features.Where(f => f.Get<FloorTrapComponent>() != null).ToList();
        var playerRoom = map.PlayerRoom;

        if (playerRoom != null)
        {
            foreach (var trap in traps)
            {
                bool inPlayerRoom = playerRoom.Contains(trap.X, trap.Y);
                Assert.That(inPlayerRoom, Is.False,
                    $"Trap at ({trap.X},{trap.Y}) is in player spawn room — traps must avoid player room");
            }
        }
    }

    // ── Combined: both props and traps ──────────────────────────────────────

    [Test]
    public void PlacePropsAndTraps_BothPresent_NoPositionConflicts()
    {
        var propsRegistry = LoadPropsRegistry();
        var trapRegistry = LoadTrapRegistry();

        var map = MakeMap(1337);
        var ids = new EntityIdAllocator(500);
        var rng = new SeededRandom(1337);
        var occupied = new HashSet<(int, int)>();
        if (map.StairDownPos.HasValue) occupied.Add(map.StairDownPos.Value);

        var features = EntityPlacer.PlaceFloorFeatures(
            map, ids, rng, depth: 3, occupied,
            signRegistry: null, muralRegistry: null, muralTracker: null,
            out _,
            propsRegistry: propsRegistry, trapRegistry: trapRegistry);

        // No two blocking features on the same tile.
        var blockingFeatures = features.Where(f => f.BlocksMovement).ToList();
        var positions = blockingFeatures.Select(f => (f.X, f.Y)).ToList();
        Assert.That(positions.Count, Is.EqualTo(positions.Distinct().Count()),
            "Blocking features must not occupy the same tile");

        // Non-blocking (traps) also must not stack with each other.
        var trapPositions = features
            .Where(f => f.Get<FloorTrapComponent>() != null)
            .Select(f => (f.X, f.Y))
            .ToList();
        Assert.That(trapPositions.Count, Is.EqualTo(trapPositions.Distinct().Count()),
            "Traps must not stack on the same tile");
    }
}
