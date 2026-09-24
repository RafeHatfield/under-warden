using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.ECS;

/// <summary>
/// Tests for PROP-011: best-of-N layout scoring in RoomPropPlacer.
///
/// Key properties under test:
/// 1. Determinism — same seed always produces the same layout.
/// 2. N=1 equivalence — single-candidate mode is still deterministic and valid.
/// 3. Parent RNG advances a fixed amount (N steps) regardless of which candidate wins.
/// 4. More candidates yield equal-or-better average prop counts than fewer.
/// 5. Basic validity — non-Generic archetype, candidates=1 still returns a non-empty list
///    (when the room is large enough to place at least one required prop).
/// </summary>
[TestFixture]
public class BestOfNLayoutTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string PropsYamlPath() =>
        Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", "props.yaml"));

    private static PropRegistry LoadRegistry() =>
        new ContentLoader().LoadPropsFromFile(PropsYamlPath());

    /// <summary>
    /// Build a walkable room with a south-side corridor entrance.
    /// </summary>
    private static (Room Room, GameMap Map) MakeRoom(
        int width, int height,
        RoomArchetype archetype,
        bool addCorridorEntrance = true)
    {
        int mapW = width + 6;
        int mapH = height + 6;
        var map = new GameMap(mapW, mapH, allWalls: true);
        var room = new Room(3, 3, width, height) { Archetype = archetype };

        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
                map.SetTile(x, y, TileKind.Floor);

        if (addCorridorEntrance)
            map.SetTile(room.CenterX, room.Y + room.Height, TileKind.Corridor);

        return (room, map);
    }

    // -------------------------------------------------------------------------
    // Test 1: Determinism — same room + same seed → identical layout
    // -------------------------------------------------------------------------

    [Test]
    public void Deterministic_SameSeedProducesSameLayout()
    {
        var registry = LoadRegistry();

        var archetypes = new[]
        {
            RoomArchetype.Library,
            RoomArchetype.Storage,
            RoomArchetype.Crypt,
            RoomArchetype.Shrine,
        };

        foreach (var archetype in archetypes)
        {
            for (int seed = 0; seed < 10; seed++)
            {
                var (room1, map1) = MakeRoom(10, 10, archetype);
                var props1 = RoomPropPlacer.PlaceProps(room1, map1, depth: 2, registry, new SeededRandom(seed));

                var (room2, map2) = MakeRoom(10, 10, archetype);
                var props2 = RoomPropPlacer.PlaceProps(room2, map2, depth: 2, registry, new SeededRandom(seed));

                Assert.That(props1.Count, Is.EqualTo(props2.Count),
                    $"{archetype} seed={seed}: prop count differs between identical runs");

                for (int i = 0; i < props1.Count; i++)
                {
                    Assert.That(props1[i].PropId, Is.EqualTo(props2[i].PropId),
                        $"{archetype} seed={seed}: prop[{i}].PropId differs");
                    Assert.That(props1[i].X, Is.EqualTo(props2[i].X),
                        $"{archetype} seed={seed}: prop[{i}].X differs");
                    Assert.That(props1[i].Y, Is.EqualTo(props2[i].Y),
                        $"{archetype} seed={seed}: prop[{i}].Y differs");
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Test 2: N=1 equivalence — single candidate, still deterministic and valid
    // -------------------------------------------------------------------------

    [Test]
    public void N1_Candidates_DeterministicAndValid()
    {
        var registry = LoadRegistry();

        for (int seed = 0; seed < 10; seed++)
        {
            var (room1, map1) = MakeRoom(10, 10, RoomArchetype.Library);
            var props1 = RoomPropPlacer.PlaceProps(room1, map1, depth: 1, registry, new SeededRandom(seed), candidates: 1);

            var (room2, map2) = MakeRoom(10, 10, RoomArchetype.Library);
            var props2 = RoomPropPlacer.PlaceProps(room2, map2, depth: 1, registry, new SeededRandom(seed), candidates: 1);

            Assert.That(props1.Count, Is.EqualTo(props2.Count),
                $"candidates=1 seed={seed}: prop count differs");

            for (int i = 0; i < props1.Count; i++)
            {
                Assert.That(props1[i].PropId, Is.EqualTo(props2[i].PropId),
                    $"candidates=1 seed={seed}: prop[{i}].PropId differs");
                Assert.That(props1[i].X, Is.EqualTo(props2[i].X),
                    $"candidates=1 seed={seed}: prop[{i}].X differs");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Test 3: Parent RNG advances fixed N steps — downstream RNG state predictable
    // -------------------------------------------------------------------------

    /// <summary>
    /// After PlaceProps returns, the parent RNG should have advanced by exactly N steps
    /// (one per child seed generated) regardless of which candidate won.
    ///
    /// We verify this indirectly: two identical RNGs both call PlaceProps with the same
    /// candidate count, then both draw a value — those values must match. This would fail
    /// if some runs consumed different amounts of the parent RNG depending on candidate
    /// internals (which use child RNGs, not the parent).
    /// </summary>
    [Test]
    public void ParentRng_AdvancesFixedAmount_PerCandidateCount()
    {
        var registry = LoadRegistry();

        foreach (int candidateCount in new[] { 1, 3, 5 })
        {
            var rng1 = new SeededRandom(999);
            var rng2 = new SeededRandom(999);

            var (room1, map1) = MakeRoom(10, 10, RoomArchetype.Storage);
            var (room2, map2) = MakeRoom(10, 10, RoomArchetype.Storage);

            RoomPropPlacer.PlaceProps(room1, map1, depth: 1, registry, rng1, candidates: candidateCount);
            RoomPropPlacer.PlaceProps(room2, map2, depth: 1, registry, rng2, candidates: candidateCount);

            // Both RNGs started with the same seed and ran PlaceProps with the same N.
            // The parent RNG advances N steps for seed generation, then stops.
            // Child RNGs handle all placement — so the parent advances by exactly N.
            int nextVal1 = rng1.Next(1_000_000);
            int nextVal2 = rng2.Next(1_000_000);

            Assert.That(nextVal1, Is.EqualTo(nextVal2),
                $"candidates={candidateCount}: parent RNG state diverged between two identical runs — " +
                $"indicates non-deterministic parent RNG consumption");
        }
    }

    /// <summary>
    /// Verify that N=3 and N=5 produce different parent RNG states (because they consume
    /// a different number of parent RNG draws), confirming the advance is proportional to N.
    /// </summary>
    [Test]
    public void ParentRng_DifferentN_ProducesDifferentNextValue()
    {
        var registry = LoadRegistry();

        var rng3 = new SeededRandom(999);
        var rng5 = new SeededRandom(999);

        var (room3, map3) = MakeRoom(10, 10, RoomArchetype.Storage);
        var (room5, map5) = MakeRoom(10, 10, RoomArchetype.Storage);

        RoomPropPlacer.PlaceProps(room3, map3, depth: 1, registry, rng3, candidates: 3);
        RoomPropPlacer.PlaceProps(room5, map5, depth: 1, registry, rng5, candidates: 5);

        int nextVal3 = rng3.Next(1_000_000);
        int nextVal5 = rng5.Next(1_000_000);

        // N=3 advances parent by 3; N=5 advances parent by 5.
        // The next draw after those two different offsets should be different.
        Assert.That(nextVal3, Is.Not.EqualTo(nextVal5),
            "N=3 and N=5 should leave the parent RNG at different states " +
            "(parent advance is proportional to N, not to which candidate wins)");
    }

    // -------------------------------------------------------------------------
    // Test 4: More candidates → equal or better average prop count
    // -------------------------------------------------------------------------

    /// <summary>
    /// Over 50 rooms with seeds 1-50, average prop count with N=5 should be >= N=1.
    /// This is a statistical test — ties are allowed, strict improvement is expected but
    /// depends on room size and recipe variance.
    /// </summary>
    [Test]
    public void MoreCandidates_EqualOrBetterAveragePropCount()
    {
        var registry = LoadRegistry();

        int totalN1 = 0;
        int totalN5 = 0;
        int runs = 50;

        for (int seed = 1; seed <= runs; seed++)
        {
            var (room1, map1) = MakeRoom(10, 10, RoomArchetype.Storage);
            var props1 = RoomPropPlacer.PlaceProps(room1, map1, depth: 1, registry, new SeededRandom(seed), candidates: 1);
            totalN1 += props1.Count;

            var (room5, map5) = MakeRoom(10, 10, RoomArchetype.Storage);
            var props5 = RoomPropPlacer.PlaceProps(room5, map5, depth: 1, registry, new SeededRandom(seed), candidates: 5);
            totalN5 += props5.Count;
        }

        double avgN1 = (double)totalN1 / runs;
        double avgN5 = (double)totalN5 / runs;

        Assert.That(avgN5, Is.GreaterThanOrEqualTo(avgN1),
            $"N=5 average prop count ({avgN5:F2}) should be >= N=1 ({avgN1:F2}). " +
            $"Best-of-N should never produce worse results than single-candidate.");
    }

    // -------------------------------------------------------------------------
    // Test 5: candidates=1 returns a valid non-empty list for non-Generic archetype
    // -------------------------------------------------------------------------

    [Test]
    public void Candidates1_NonGenericArchetype_ReturnsValidList()
    {
        var registry = LoadRegistry();

        // Storage requires barrel + crate (both required, Chance=1.0)
        // A 10x10 room is large enough to always place at least one.
        var (room, map) = MakeRoom(10, 10, RoomArchetype.Storage);
        var props = RoomPropPlacer.PlaceProps(room, map, depth: 1, registry, new SeededRandom(42), candidates: 1);

        Assert.That(props, Is.Not.Null, "PlaceProps with candidates=1 must not return null");
        Assert.That(props.Count, Is.GreaterThan(0),
            "Storage archetype in a 10x10 room with candidates=1 should place at least one prop");
    }

    // -------------------------------------------------------------------------
    // Test 6: Generic archetype produces non-blocking scatter with any candidate count
    // -------------------------------------------------------------------------

    [Test]
    public void Generic_Archetype_OnlyNonBlockingWithAnyN()
    {
        var registry = LoadRegistry();

        foreach (int n in new[] { 1, 3, 5 })
        {
            var (room, map) = MakeRoom(10, 10, RoomArchetype.Generic);
            var props = RoomPropPlacer.PlaceProps(room, map, depth: 1, registry, new SeededRandom(1337), candidates: n);
            Assert.That(props.All(p => !p.BlocksMovement), Is.True,
                $"candidates={n}: Generic archetype must produce only non-blocking props");
        }
    }
}
