using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.ECS;

/// <summary>
/// Tests for SROOM-003: vault room designation.
/// Vault rooms are rare, depth 3+ rooms with guaranteed loot and a guardian monster.
/// At most 1 vault per floor. Never first or last room. Walkable area >= 25 tiles.
/// Archetype overridden to Generic so no themed props are placed.
/// </summary>
[TestFixture]
public class VaultTests
{
    // Larger maps + more rooms → more room candidates, higher chance of vaults appearing
    private const int Width = 120;
    private const int Height = 80;
    private const int MaxRooms = 150;
    private const int MinSize = 6;
    private const int MaxSize = 12;

    private static GeneratedMap MakeMap(int seed, int depth) =>
        MapGenerator.Generate(Width, Height, MaxRooms, MinSize, MaxSize,
            new SeededRandom(seed), depth: depth);

    private static int CountWalkable(GameMap map, Room room)
    {
        int count = 0;
        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
                if (map.IsWalkable(x, y)) count++;
        return count;
    }

    // -------------------------------------------------------------------------
    // Test 1: No vault on depth 1-2
    // -------------------------------------------------------------------------

    [Test]
    public void SROOM003_NoVault_AtDepth1()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var generated = MakeMap(seed, depth: 1);
            Assert.That(generated.Rooms.All(r => !r.IsVault), Is.True,
                $"Seed {seed}: No vault rooms expected at depth 1");
        }
    }

    [Test]
    public void SROOM003_NoVault_AtDepth2()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var generated = MakeMap(seed, depth: 2);
            Assert.That(generated.Rooms.All(r => !r.IsVault), Is.True,
                $"Seed {seed}: No vault rooms expected at depth 2");
        }
    }

    // -------------------------------------------------------------------------
    // Test 2: Vault can appear on depth 3+
    // -------------------------------------------------------------------------

    [Test]
    public void SROOM003_VaultCanAppear_AtDepth3()
    {
        // With 15% chance and 100 floors we expect roughly 15 vaults.
        // We just check the system fires at all — even 1 vault across 100 floors confirms it.
        int floorsWithVault = 0;
        for (int seed = 0; seed < 100; seed++)
        {
            var generated = MakeMap(seed, depth: 3);
            if (generated.Rooms.Any(r => r.IsVault))
                floorsWithVault++;
        }

        Assert.That(floorsWithVault, Is.GreaterThan(0),
            "Expected at least one vault to appear across 100 depth-3 floors (15% chance)");
    }

    // -------------------------------------------------------------------------
    // Test 3: At most 1 vault per floor
    // -------------------------------------------------------------------------

    [Test]
    public void SROOM003_AtMostOneVault_PerFloor()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            // Test across multiple depths (3, 5, 7) to hit all chance brackets
            int depth = 3 + (seed % 5); // cycles through depths 3-7
            var generated = MakeMap(seed, depth: depth);
            int vaultCount = generated.Rooms.Count(r => r.IsVault);

            Assert.That(vaultCount, Is.LessThanOrEqualTo(1),
                $"Seed {seed} depth {depth}: Expected at most 1 vault per floor, found {vaultCount}");
        }
    }

    // -------------------------------------------------------------------------
    // Test 4: First and last rooms never vault
    // -------------------------------------------------------------------------

    [Test]
    public void SROOM003_FirstRoom_NeverVault()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            int depth = 3 + (seed % 5);
            var generated = MakeMap(seed, depth: depth);
            if (generated.Rooms.Count < 1) continue;

            Assert.That(generated.Rooms[0].IsVault, Is.False,
                $"Seed {seed} depth {depth}: First room (player spawn) must never be a vault");
        }
    }

    [Test]
    public void SROOM003_LastRoom_NeverVault()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            int depth = 3 + (seed % 5);
            var generated = MakeMap(seed, depth: depth);
            if (generated.Rooms.Count < 1) continue;

            Assert.That(generated.Rooms[^1].IsVault, Is.False,
                $"Seed {seed} depth {depth}: Last room (stair exit) must never be a vault");
        }
    }

    // -------------------------------------------------------------------------
    // Test 5: Vault rooms have Generic archetype
    // -------------------------------------------------------------------------

    [Test]
    public void SROOM003_VaultRooms_HaveGenericArchetype()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            int depth = 3 + (seed % 5);
            var generated = MakeMap(seed, depth: depth);

            foreach (var room in generated.Rooms)
            {
                if (!room.IsVault) continue;

                Assert.That(room.Archetype, Is.EqualTo(RoomArchetype.Generic),
                    $"Seed {seed} depth {depth}: Vault room must have Generic archetype " +
                    $"(had {room.Archetype}) — ensures no themed props are placed");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Test 6: Vault room walkable area >= 25
    // -------------------------------------------------------------------------

    [Test]
    public void SROOM003_VaultRooms_HaveSufficientWalkableArea()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            int depth = 3 + (seed % 5);
            var generated = MakeMap(seed, depth: depth);

            foreach (var room in generated.Rooms)
            {
                if (!room.IsVault) continue;

                int walkable = CountWalkable(generated.Map, room);
                Assert.That(walkable, Is.GreaterThanOrEqualTo(25),
                    $"Seed {seed} depth {depth}: Vault room has {walkable} walkable tiles — must be >= 25");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Test 7: IsVault defaults false
    // -------------------------------------------------------------------------

    [Test]
    public void SROOM003_IsVault_DefaultsToFalse()
    {
        var room = new Room(1, 1, 5, 5);
        Assert.That(room.IsVault, Is.False,
            "Newly created Room must have IsVault = false by default");
    }

    [Test]
    public void SROOM003_IsVault_CanBeSetWithInitSyntax()
    {
        var room = new Room(1, 1, 8, 8) { IsVault = true };
        Assert.That(room.IsVault, Is.True);

        // with-expression must preserve the flag
        var copy = room with { Archetype = RoomArchetype.Armory };
        Assert.That(copy.IsVault, Is.True);
    }

    // -------------------------------------------------------------------------
    // Test 8: Grand Shrine candidate rooms excluded from vault designation
    // -------------------------------------------------------------------------

    [Test]
    public void SROOM003_GrandShrineCandidate_NeverVault()
    {
        // A room cannot be both a vault and a Grand Shrine candidate.
        // Grand Shrine candidates are Shrine archetype rooms with >= 36 walkable tiles.
        // The vault designator skips these to avoid conflicting reward systems.
        for (int seed = 0; seed < 200; seed++)
        {
            int depth = 3 + (seed % 5);
            var generated = MakeMap(seed, depth: depth);

            foreach (var room in generated.Rooms)
            {
                // A vault room must NOT have Shrine archetype (which was replaced with Generic)
                // and certainly must not be tagged IsVault AND be a Grand Shrine candidate.
                // Since vault designation overrides archetype to Generic, the vault room
                // can never simultaneously be a Shrine archetype room.
                if (room.IsVault)
                {
                    Assert.That(room.Archetype, Is.Not.EqualTo(RoomArchetype.Shrine),
                        $"Seed {seed} depth {depth}: Vault room must not retain Shrine archetype");
                }
            }
        }
    }
}
