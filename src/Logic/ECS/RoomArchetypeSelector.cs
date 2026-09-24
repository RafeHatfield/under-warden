using UnderWarden.Logic.Core;

namespace UnderWarden.Logic.ECS;

/// <summary>
/// Assigns a semantic <see cref="RoomArchetype"/> to a room during dungeon generation.
///
/// The selector applies hard rules first (spawn and exit rooms are always Generic),
/// then filters the full archetype table by walkable area, depth range, and allowed
/// shapes, and finally picks from eligible candidates by weighted random.
///
/// Constraint data is hardcoded here as a table — moving it to YAML is a follow-up task
/// once the schema and prop-placement systems are defined.
/// </summary>
public static class RoomArchetypeSelector
{
    // -------------------------------------------------------------------------
    // Constraint table — one entry per archetype.
    // AllowedShapes: null means any shape is allowed.
    // -------------------------------------------------------------------------

    private sealed record ArchetypeConstraint(
        RoomArchetype Archetype,
        int MinWalkable,
        RoomShape[]? AllowedShapes,
        int DepthMin,
        int DepthMax,
        int Weight);

    private static readonly ArchetypeConstraint[] Constraints =
    [
        new(RoomArchetype.Generic,        9,  null,                                                                      1, 99, 30),
        new(RoomArchetype.Library,        25, [RoomShape.Rectangle, RoomShape.Union, RoomShape.Alcove],                 1,  4, 10),
        new(RoomArchetype.Armory,         25, [RoomShape.Rectangle, RoomShape.Union],                                   2,  6,  8),
        new(RoomArchetype.Kitchen,        30, [RoomShape.Rectangle, RoomShape.Union],                                   1,  4,  8),
        new(RoomArchetype.ThroneRoom,     64, [RoomShape.Rectangle, RoomShape.Circle],                                  3,  6,  4),
        new(RoomArchetype.Prison,         25, [RoomShape.Rectangle, RoomShape.Union, RoomShape.Alcove],                 2,  6,  7),
        new(RoomArchetype.Laboratory,     25, [RoomShape.Rectangle, RoomShape.Union, RoomShape.Cave],                   2,  5,  8),
        new(RoomArchetype.Shrine,         25, [RoomShape.Rectangle, RoomShape.Circle],                                  1, 99, 10),
        new(RoomArchetype.Storage,        16, null,                                                                      1, 99, 12),
        new(RoomArchetype.Bedroom,        16, [RoomShape.Rectangle, RoomShape.Alcove],                                  1,  3, 10),
        new(RoomArchetype.Crypt,          30, [RoomShape.Rectangle, RoomShape.Alcove],                                  2,  6,  8),
        new(RoomArchetype.FountainRoom,   36, [RoomShape.Circle, RoomShape.Rectangle],                                  1, 99,  6),
        new(RoomArchetype.Forge,          30, [RoomShape.Rectangle, RoomShape.Union],                                   3,  6,  6),
        new(RoomArchetype.Sewer,          16, [RoomShape.Rectangle, RoomShape.CorridorRoom, RoomShape.Cave],            1,  3,  8),
        new(RoomArchetype.MushroomGarden, 25, [RoomShape.Cave, RoomShape.Circle],                                       3,  6,  5),
    ];

    // Archetypes that are excluded from Cave-shaped rooms regardless of the AllowedShapes list.
    // Cave geometry has irregular walls that clash with structured prop placement for these types.
    private static readonly HashSet<RoomArchetype> CaveExclusions =
    [
        RoomArchetype.Library,
        RoomArchetype.Armory,
        RoomArchetype.Kitchen,
        RoomArchetype.ThroneRoom,
        RoomArchetype.Prison,
    ];

    // CorridorRoom-shaped rooms only qualify for Generic or Sewer — the narrow strip
    // doesn't have enough floor area for most themed prop layouts.
    private static readonly HashSet<RoomArchetype> CorridorRoomAllowed =
    [
        RoomArchetype.Generic,
        RoomArchetype.Sewer,
    ];

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Select an archetype for <paramref name="room"/>.
    ///
    /// Hard rules:
    ///   - roomIndex 0 (player spawn) → always Generic
    ///   - roomIndex totalRooms-1 (last room, stair exit) → always Generic
    ///
    /// Otherwise: filter <see cref="Constraints"/> by walkable area, depth, shape,
    /// and additional shape-class restrictions, then pick by weighted random.
    /// Falls back to Generic if the filter somehow eliminates everything.
    /// </summary>
    public static RoomArchetype Select(
        Room room,
        GameMap map,
        int depth,
        int roomIndex,
        int totalRooms,
        SeededRandom rng)
    {
        // Hard rule: spawn and exit rooms are always Generic — no props that could
        // block entry or complicate orientation for the player.
        if (roomIndex == 0 || roomIndex == totalRooms - 1)
            return RoomArchetype.Generic;

        int walkable = CountWalkableArea(room, map);

        var eligible = new List<(RoomArchetype Archetype, int Weight)>();

        foreach (var c in Constraints)
        {
            if (!IsEligible(c, room.Shape, walkable, depth))
                continue;

            eligible.Add((c.Archetype, c.Weight));
        }

        // Failsafe: this should never be empty because Generic passes for walkable >= 9,
        // but if every archetype was filtered (pathological tiny room), default safely.
        if (eligible.Count == 0)
            return RoomArchetype.Generic;

        return WeightedPick(eligible, rng);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Count tiles that are walkable on the <paramref name="map"/> AND fall within
    /// <paramref name="room"/>'s bounding box. This gives the actual carved floor area
    /// rather than the full Width*Height bounding box, which matters for non-rectangular
    /// shapes (cave, circle, alcove) where carved area is significantly smaller.
    /// </summary>
    private static int CountWalkableArea(Room room, GameMap map)
    {
        int count = 0;
        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
                if (room.Contains(x, y) && map.IsWalkable(x, y))
                    count++;
        return count;
    }

    private static bool IsEligible(
        ArchetypeConstraint c,
        RoomShape shape,
        int walkable,
        int depth)
    {
        // Walkable area threshold
        if (walkable < c.MinWalkable)
            return false;

        // Depth range
        if (depth < c.DepthMin || depth > c.DepthMax)
            return false;

        // CorridorRoom shape — only Generic and Sewer are allowed
        if (shape == RoomShape.CorridorRoom && !CorridorRoomAllowed.Contains(c.Archetype))
            return false;

        // Cave exclusion list — structured archetypes don't work in organic cave shapes
        if (shape == RoomShape.Cave && CaveExclusions.Contains(c.Archetype))
            return false;

        // Shape whitelist (null = any shape allowed)
        if (c.AllowedShapes != null && !Array.Exists(c.AllowedShapes, s => s == shape))
            return false;

        return true;
    }

    /// <summary>
    /// Weighted random selection from an eligible list.
    /// Uses rng.Next(0, totalWeight) and walks the list — same pattern as
    /// RoomShapeGenerator.SelectShape so the behavior is consistent across selectors.
    /// </summary>
    private static RoomArchetype WeightedPick(
        List<(RoomArchetype Archetype, int Weight)> candidates,
        SeededRandom rng)
    {
        int total = 0;
        foreach (var (_, w) in candidates) total += w;

        int roll = rng.Next(0, total);
        int cumulative = 0;
        foreach (var (archetype, weight) in candidates)
        {
            cumulative += weight;
            if (roll < cumulative)
                return archetype;
        }

        // Unreachable if candidates is non-empty, but satisfy the compiler
        return candidates[0].Archetype;
    }
}
