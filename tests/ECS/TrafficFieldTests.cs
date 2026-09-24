using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.ECS;

/// <summary>
/// Tests for the traffic field — the scalar that decides where a floor is worn.
///
/// It exists because the device gate ruled that wear is a property of TRAFFIC, not of rooms, and
/// the field it replaced varied with noise: a blind seat compared a corridor mouth against a dead
/// corner and found "same hatch density, same edge condition, same joint depth".
///
/// What these tests protect is the HIERARCHY, because that is the part a capture cannot check
/// cheaply and a seat can only check by eye. The renderer's job is to make the field visible; this
/// file's job is to prove the field says the right thing before anyone looks at it.
/// </summary>
[TestFixture]
public class TrafficFieldTests
{
    // A level shaped like a sentence: entry room, corridor, middle room, corridor, exit room,
    // with a dead-end branch off the middle and a sealed vault nobody may enter.
    //
    //   (1,1)..(7,7)  entry        corridor y=4        (13,1)..(19,7) middle
    //   (25,1)..(31,7) exit        dead end (13,12)..(17,16)
    //   vault (25,12)..(29,16), reachable but sealed
    private static (GameMap Map, List<Room> Rooms, (int X, int Y) Entry, (int X, int Y) Exit)
        BuildLevel()
    {
        var map = new GameMap(40, 20, allWalls: true);
        void Carve(int x0, int y0, int x1, int y1, TileKind k = TileKind.Floor)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    map.SetTile(x, y, k);
        }

        Carve(1, 1, 7, 7);        // entry
        Carve(13, 1, 19, 7);      // middle
        Carve(25, 1, 31, 7);      // exit
        Carve(8, 4, 12, 4, TileKind.Corridor);    // entry -> middle
        Carve(20, 4, 24, 4, TileKind.Corridor);   // middle -> exit
        Carve(16, 8, 16, 11, TileKind.Corridor);  // middle -> dead end
        Carve(13, 12, 17, 16);    // dead end
        Carve(28, 8, 28, 11, TileKind.Corridor);  // exit -> vault
        Carve(25, 12, 29, 16);    // vault

        var rooms = new List<Room>
        {
            new Room(1, 1, 7, 7) { Archetype = RoomArchetype.Armory },
            new Room(13, 1, 7, 7) { Archetype = RoomArchetype.Armory },
            new Room(25, 1, 7, 7) { Archetype = RoomArchetype.Armory },
            new Room(13, 12, 5, 5) { IsDeadEnd = true },
            new Room(25, 12, 5, 5) { IsVault = true },
        };
        return (map, rooms, (4, 4), (28, 4));
    }

    private static double Mean(byte[,] f, int x0, int y0, int x1, int y1)
    {
        double s = 0; int n = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) { s += f[x, y]; n++; }
        return n > 0 ? s / n : 0;
    }

    [Test]
    public void TheWeightHierarchyIsOrdered()
    {
        // The ordering is the ruling, stated as a chain. If someone retunes a constant, this is
        // the line that objects.
        Assert.That(TrafficField.SpineWeight, Is.GreaterThan(TrafficField.MajorWeight));
        Assert.That(TrafficField.MajorWeight, Is.GreaterThan(TrafficField.SecondaryWeight));
        Assert.That(TrafficField.SecondaryWeight, Is.GreaterThan(TrafficField.RemoteWeight));
        Assert.That(TrafficField.RemoteWeight, Is.GreaterThan(TrafficField.SealedWeight));
        Assert.That(TrafficField.SealedWeight, Is.EqualTo(0.0),
            "A sealed room must deposit NOTHING. An unworn threshold is how the floor says " +
            "nobody comes here, and that only works if the value is exactly zero.");
    }

    [Test]
    public void TheSpineIsBusierThanAnOffRouteCorner()
    {
        var (map, rooms, entry, exit) = BuildLevel();
        var r = TrafficField.Compute(map, rooms, entry, exit);

        double spine = Mean(r.Field, 9, 4, 12, 4);      // the corridor every run walks
        double corner = Mean(r.Field, 1, 1, 2, 2);      // a corner of the entry room, off-route

        Assert.That(spine, Is.GreaterThan(corner * 1.5),
            $"the spine ({spine:F1}) must be markedly busier than an off-route corner ({corner:F1})");
    }

    [Test]
    public void ASealedRoomIsUnwalked()
    {
        var (map, rooms, entry, exit) = BuildLevel();
        var r = TrafficField.Compute(map, rooms, entry, exit);

        double vault = Mean(r.Field, 26, 13, 28, 15);
        double deadEnd = Mean(r.Field, 14, 13, 16, 15);

        Assert.That(r.TierCounts["sealed"], Is.EqualTo(1));
        Assert.That(vault, Is.LessThan(deadEnd),
            $"the vault ({vault:F1}) must be less walked than even a dead end ({deadEnd:F1})");
    }

    [Test]
    public void ARemoteBranchIsWalkedLessThanTheRouteItLeaves()
    {
        var (map, rooms, entry, exit) = BuildLevel();
        var r = TrafficField.Compute(map, rooms, entry, exit);

        double onSpine = Mean(r.Field, 16, 3, 16, 5);     // the middle room, on the through-route
        double branch = Mean(r.Field, 14, 14, 16, 15);    // the dead end it hangs off

        Assert.That(branch, Is.LessThan(onSpine),
            $"a dead end ({branch:F1}) must be quieter than the route it leaves ({onSpine:F1})");
    }

    [Test]
    public void ThresholdsAreBusierThanTheRoomsTheyServe()
    {
        // Every journey into a room passes its doorway, so a doorway accumulates what the room
        // spreads out. This is not boosted anywhere — it is what accumulation does, and the test
        // is here to prove the emergence rather than to assert a constant.
        var (map, rooms, entry, exit) = BuildLevel();
        var r = TrafficField.Compute(map, rooms, entry, exit);

        double threshold = Mean(r.Field, 12, 4, 13, 4);   // where the corridor meets the middle room
        double roomEdge = Mean(r.Field, 18, 1, 19, 2);    // a far corner of that same room

        Assert.That(threshold, Is.GreaterThan(roomEdge),
            $"a threshold ({threshold:F1}) must be busier than the room's far corner ({roomEdge:F1})");
    }

    [Test]
    public void TheFieldIsDeterministic()
    {
        var (map, rooms, entry, exit) = BuildLevel();
        var a = TrafficField.Compute(map, rooms, entry, exit).Field;
        var (map2, rooms2, entry2, exit2) = BuildLevel();
        var b = TrafficField.Compute(map2, rooms2, entry2, exit2).Field;

        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 40; x++)
                Assert.That(b[x, y], Is.EqualTo(a[x, y]), $"field differs at ({x},{y})");
    }

    [Test]
    public void AnUnreachableExitStillProducesASpine()
    {
        // A level with no way to its stairs must not come back blank — a floor with no route on
        // it would be the uniform-wear defect returning through the back door.
        var (map, rooms, entry, _) = BuildLevel();
        var r = TrafficField.Compute(map, rooms, entry, exit: null);

        Assert.That(r.SpineLength, Is.GreaterThan(0));
        double max = 0;
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 40; x++) max = System.Math.Max(max, r.Field[x, y]);
        Assert.That(max, Is.GreaterThan(0));
    }
}
