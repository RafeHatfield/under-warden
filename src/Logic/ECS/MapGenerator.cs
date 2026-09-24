using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Core;

namespace UnderWarden.Logic.ECS;

/// <summary>
/// Procedural dungeon generator using random room placement + L-corridor connections.
/// Algorithm matches the Python prototype (map_objects/game_map.py lines 198-302) so that
/// cross-prototype validation is possible with the same seed.
///
/// NOT BSP — BSP produces different room distributions for the same seed. Random placement
/// with intersection rejection preserves the probability distribution of the prototype.
/// </summary>
public static class MapGenerator
{
    // Defaults used when no level template parameters are present.
    // Room sizes tuned for mobile: SPD-style small rooms that fit on screen.
    public const int DefaultWidth = 120;
    public const int DefaultHeight = 80;
    public const int DefaultMaxRooms = 25; // attempt count; ~15-18 rooms actually placed
    public const int DefaultMinRoomSize = 5;
    public const int DefaultMaxRoomSize = 10;

    /// <summary>
    /// Generate a dungeon floor.
    /// </summary>
    /// <param name="width">Map width in tiles.</param>
    /// <param name="height">Map height in tiles.</param>
    /// <param name="maxRooms">Maximum number of rooms to attempt placing.</param>
    /// <param name="minRoomSize">Minimum room side length (inclusive).</param>
    /// <param name="maxRoomSize">Maximum room side length (inclusive).</param>
    /// <param name="rng">Seeded RNG — deterministic for the same seed.</param>
    /// <param name="stairs">Optional stair placement rules. Null = default (place stair in last room).</param>
    /// <param name="depth">Dungeon depth (1-based). Used by archetype selector to restrict themed rooms
    /// to appropriate floors. Defaults to 1 so all existing callers compile unchanged.</param>
    /// <param name="propRegistry">Optional prop registry. When provided, props are placed in each room
    /// according to its archetype recipe. When null, no props are placed (Props will be empty).</param>
    public static GeneratedMap Generate(
        int width, int height,
        int maxRooms, int minRoomSize, int maxRoomSize,
        SeededRandom rng,
        StairRules? stairs = null,
        int depth = 1,
        Content.PropRegistry? propRegistry = null)
    {
        // Phase 1 of Python algorithm: all walls
        var map = new GameMap(width, height, allWalls: true);

        var rooms = new List<Room>();
        var corridors = new List<CorridorSegment>();

        // Track how many corridor connections each room has (by index in rooms list).
        // Used after placement to tag dead-end rooms (exactly 1 connection, small area).
        var connectionCount = new Dictionary<int, int>();

        // Attempt to place up to maxRooms rooms
        for (int attempt = 0; attempt < maxRooms; attempt++)
        {
            // Random width and height (minRoomSize..maxRoomSize inclusive, matching randint)
            int w = rng.Next(minRoomSize, maxRoomSize + 1);
            int h = rng.Next(minRoomSize, maxRoomSize + 1);

            // Random position — keep at least 1 wall tile on every map edge so the
            // isometric renderer always has a wall tile behind every floor tile.
            // PoC equivalent: same constraint is implicit because rooms carve x1+1..x2-1
            // (leaving a wall border). C# carves the full rectangle, so we enforce the
            // border here instead.
            int x = rng.Next(1, Math.Max(2, width - w));    // [1, width-w-1]
            int y = rng.Next(1, Math.Max(2, height - h));   // [1, height-h-1]

            var newRoom = new Room(x, y, w, h);

            // Reject if intersects any existing room (with 1-tile padding)
            bool overlaps = false;
            foreach (var existingRoom in rooms)
            {
                if (newRoom.Intersects(existingRoom))
                {
                    overlaps = true;
                    break;
                }
            }
            if (overlaps) continue;

            // Select shape and carve.
            // Room 0 (player spawn) is always Rectangle — the player should start in a clean,
            // familiar space, not a cave or alcove room. All other rooms get random shapes.
            var shape = rooms.Count == 0
                ? RoomShape.Rectangle
                : RoomShapeGenerator.SelectShape(w, h, rng);
            RoomShapeGenerator.CarveRoom(map, newRoom, shape, rng);
            newRoom = newRoom with { Shape = shape };

            // Assign room archetype based on walkable area, shape, and dungeon depth.
            // Room 0 is always Generic (player spawn — no blocking props at entry).
            // We pass int.MaxValue as totalRooms so the "last room" guard never fires
            // during placement; the actual last room is fixed to Generic in a post-pass
            // once the full room list is known (see below).
            var archetype = RoomArchetypeSelector.Select(
                newRoom, map, depth,
                roomIndex: rooms.Count,
                totalRooms: int.MaxValue,
                rng);
            newRoom = newRoom with { Archetype = archetype };

            // Assign maintenance state — depth-weighted random roll.
            // First room (player spawn) is always WellMaintained so the player doesn't start in rubble.
            var maintenance = rooms.Count == 0
                ? RoomMaintenanceState.WellMaintained
                : RollMaintenanceState(depth, rng);
            newRoom = newRoom with { MaintenanceState = maintenance };

            if (rooms.Count > 0)
            {
                // Connect to the spatially nearest existing room (Prim's MST heuristic).
                // Connecting by order of placement produces long crossing corridors because
                // room placement is random — rooms placed consecutively are rarely adjacent.
                // Nearest-room produces shorter corridors and a more structured layout.
                var (connectRoom, connectIdx) = FindNearestRoom(rooms, newRoom);
                ConnectRooms(map, corridors, connectRoom, newRoom, rng);

                int newIdx = rooms.Count;
                connectionCount[connectIdx] = connectionCount.GetValueOrDefault(connectIdx) + 1;
                connectionCount[newIdx] = connectionCount.GetValueOrDefault(newIdx) + 1;
            }

            rooms.Add(newRoom);
        }

        // If no rooms were placed (pathological case), create a single minimal room
        if (rooms.Count == 0)
        {
            var fallback = new Room(1, 1, minRoomSize, minRoomSize);
            CarveRoom(map, fallback);
            rooms.Add(fallback);
        }

        // Post-pass: force the last room to Generic so no archetype props block the stair exit.
        // This is done after placement because we don't know which attempt produces the final
        // room until the placement loop ends.
        if (rooms.Count > 0 && rooms[^1].Archetype != RoomArchetype.Generic)
            rooms[^1] = rooms[^1] with { Archetype = RoomArchetype.Generic };

        // Post-pass: tag dead-end rooms (exactly 1 corridor connection, small walkable area).
        // Skip first (index 0 = player spawn) and last (stair exit). Dead-end rooms get a
        // loot bias in EntityPlacer — they reward exploration of branching dead-end passages.
        for (int i = 1; i < rooms.Count - 1; i++)
        {
            int connections = connectionCount.GetValueOrDefault(i, 0);
            if (connections != 1) continue;
            int walkable = CountWalkableTiles(map, rooms[i]);
            if (walkable <= 16)
                rooms[i] = rooms[i] with { IsDeadEnd = true };
        }

        var playerRoom = rooms[0];
        var playerSpawn = (playerRoom.CenterX, playerRoom.CenterY);

        // Belt-and-suspenders: ensure every room center is reachable from player spawn.
        // Non-rectangular shapes are designed to include their center, but cave/circle shapes
        // can occasionally produce a corner case where the corridor L-tunnel joins at a wall.
        EnsureConnectivity(map, rooms, playerSpawn);

        // Place doors at corridor-room chokepoints — after all carving is complete
        var doorPositions = DoorPlacer.PlaceDoors(map); // DOOR-001

        // Stair down goes in the last room placed
        (int X, int Y)? stairDownPos = null;
        if (stairs == null || stairs.Down)
        {
            var lastRoom = rooms[^1];
            stairDownPos = (lastRoom.CenterX, lastRoom.CenterY);
            map.SetTile(stairDownPos.Value.X, stairDownPos.Value.Y, TileKind.StairDown);
        }

        // Stair up goes in player room on depth > 1 (controlled by caller via StairRules)
        (int X, int Y)? stairUpPos = null;
        if (stairs != null && stairs.Up)
        {
            // Place stair up near-but-not-at the player spawn to avoid spawn-on-stair overlap
            // Use a tile adjacent to center if available, else center
            stairUpPos = FindStairUpPosition(map, playerRoom, playerSpawn);
            if (stairUpPos.HasValue)
                map.SetTile(stairUpPos.Value.X, stairUpPos.Value.Y, TileKind.StairUp);
        }

        // Vault designation: depth 3+, at most 1 vault per floor, 15-25% depth-scaled chance.
        // Runs BEFORE prop placement so vault rooms have their archetype overridden to Generic
        // before RoomPropPlacer runs — ensuring no themed props are placed in vault rooms.
        if (depth >= 3)
            DesignateVault(rooms, map, depth, rng);

        // Prop placement pass — runs after stair placement so stair cells are already set.
        // Runs only when a registry is provided; skipped for scenario harness and tests that
        // don't need props (keeps generation fast and avoids side effects in those contexts).
        var allProps = new List<PlacedProp>();
        if (propRegistry != null)
        {
            foreach (var room in rooms)
            {
                var props = RoomPropPlacer.PlaceProps(room, map, depth, propRegistry, rng);
                allProps.AddRange(props);
            }
        }

        // Tag Grand Shrine rooms and collect altar positions for EntityPlacer item rewards.
        // Must run after prop placement so we can find the altar prop placed in each Shrine room.
        var grandShrineAltarPositions = new List<(int X, int Y)>();
        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i].Archetype != RoomArchetype.Shrine) continue;
            if (CountWalkableTiles(map, rooms[i]) < 36) continue;

            rooms[i] = rooms[i] with { IsGrandShrine = true };

            // Find the altar prop placed in this room by the prop placement pass above
            foreach (var prop in allProps)
            {
                if (prop.PropId == "altar" && rooms[i].Contains(prop.X, prop.Y))
                {
                    grandShrineAltarPositions.Add((prop.X, prop.Y));
                    break; // one altar per room
                }
            }
        }

        return new GeneratedMap(
            map: map,
            rooms: rooms,
            corridors: corridors,
            playerRoom: playerRoom,
            playerSpawn: playerSpawn,
            stairDownPos: stairDownPos,
            stairUpPos: stairUpPos,
            props: allProps,
            doorPositions: doorPositions,
            grandShrineAltarPositions: grandShrineAltarPositions);
    }

    /// <summary>Carve all interior tiles of a room as Floor (fallback for pathological case).</summary>
    private static void CarveRoom(GameMap map, Room room)
    {
        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
                map.SetTile(x, y, TileKind.Floor);
    }

    /// <summary>
    /// Post-generation connectivity check. Flood fills from playerSpawn and verifies
    /// every room center is reachable. If a room is unreachable, carves an emergency
    /// L-corridor from the nearest reachable room center to reconnect it.
    /// Runs up to 3 passes to handle cascading disconnections.
    ///
    /// This is belt-and-suspenders: shape carvers are designed to include the room center
    /// as floor, but edge cases in cave/circle shapes with small bounding boxes can
    /// occasionally produce a center that the corridor tunnel lands on a wall tile.
    /// </summary>
    private static void EnsureConnectivity(
        GameMap map,
        List<Room> rooms,
        (int X, int Y) playerSpawn)
    {
        const int MaxPasses = 3;

        for (int pass = 0; pass < MaxPasses; pass++)
        {
            // Flood fill from player spawn across all walkable tiles
            var reachable = FloodFillMap(map, playerSpawn.X, playerSpawn.Y);

            // Find the first unreachable room
            Room? unreachable = null;
            foreach (var room in rooms)
            {
                if (!reachable.Contains((room.CenterX, room.CenterY)))
                {
                    unreachable = room;
                    break;
                }
            }

            if (unreachable == null) return; // all rooms connected

            // Find nearest reachable room to connect from
            Room? nearest = null;
            int bestDist = int.MaxValue;
            foreach (var room in rooms)
            {
                if (!reachable.Contains((room.CenterX, room.CenterY))) continue;
                int dist = Math.Abs(room.CenterX - unreachable.CenterX)
                         + Math.Abs(room.CenterY - unreachable.CenterY);
                if (dist < bestDist) { bestDist = dist; nearest = room; }
            }

            if (nearest == null)
            {
                // No reachable room at all — just ensure the first room's center is floor
                map.SetTile(rooms[0].CenterX, rooms[0].CenterY, TileKind.Floor);
                continue;
            }

            // Carve emergency L-corridor (H then V, no random — just fix the gap)
            int x1 = nearest.CenterX, y1 = nearest.CenterY;
            int x2 = unreachable.CenterX, y2 = unreachable.CenterY;
            CarveHTunnel(map, x1, x2, y1);
            CarveVTunnel(map, y1, y2, x2);
            // Ensure the unreachable room center is floor (CarveVTunnel only sets Corridor on Wall)
            map.SetTile(x2, y2, TileKind.Floor);
        }
    }

    /// <summary>
    /// BFS flood fill from (startX, startY) across all walkable tiles on the full map.
    /// Returns the set of (x,y) positions reachable.
    /// </summary>
    private static HashSet<(int X, int Y)> FloodFillMap(GameMap map, int startX, int startY)
    {
        var visited = new HashSet<(int, int)>();
        if (!map.IsWalkable(startX, startY)) return visited;

        var queue = new Queue<(int, int)>();
        queue.Enqueue((startX, startY));
        visited.Add((startX, startY));

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (visited.Contains((nx, ny))) continue;
                if (!map.IsWalkable(nx, ny)) continue;
                visited.Add((nx, ny));
                queue.Enqueue((nx, ny));
            }
        }
        return visited;
    }

    /// <summary>
    /// Connect two rooms with an L-shaped corridor.
    /// Randomly chooses horizontal-then-vertical or vertical-then-horizontal,
    /// matching the Python coin-flip. Records both segments in corridors list.
    /// 30% of connections are 2 tiles wide for visual variety (CORR-001).
    /// </summary>
    private static void ConnectRooms(
        GameMap map,
        List<CorridorSegment> corridors,
        Room from,
        Room to,
        SeededRandom rng)
    {
        int x1 = from.CenterX, y1 = from.CenterY;
        int x2 = to.CenterX, y2 = to.CenterY;

        // Python: if randint(0, 1) == 1: H then V; else: V then H
        if (rng.Next(2) == 1)
        {
            // Horizontal then vertical
            CarveHTunnel(map, x1, x2, y1);
            CarveVTunnel(map, y1, y2, x2);
            corridors.Add(new CorridorSegment(x1, y1, x2, y1)); // H segment
            corridors.Add(new CorridorSegment(x2, y1, x2, y2)); // V segment
        }
        else
        {
            // Vertical then horizontal
            CarveVTunnel(map, y1, y2, x1);
            CarveHTunnel(map, x1, x2, y2);
            corridors.Add(new CorridorSegment(x1, y1, x1, y2)); // V segment
            corridors.Add(new CorridorSegment(x1, y2, x2, y2)); // H segment
        }
    }

    /// <summary>
    /// Find the existing room whose center is closest to newRoom's center (Euclidean² distance).
    /// Returns the room and its index in the rooms list.
    /// </summary>
    private static (Room Room, int Index) FindNearestRoom(List<Room> rooms, Room newRoom)
    {
        int nearestIdx = 0;
        long minDist = long.MaxValue;
        for (int i = 0; i < rooms.Count; i++)
        {
            long dx = newRoom.CenterX - rooms[i].CenterX;
            long dy = newRoom.CenterY - rooms[i].CenterY;
            long dist = dx * dx + dy * dy;
            if (dist < minDist) { minDist = dist; nearestIdx = i; }
        }
        return (rooms[nearestIdx], nearestIdx);
    }

    private static void CarveHTunnel(GameMap map, int x1, int x2, int y)
    {
        int xMin = Math.Min(x1, x2);
        int xMax = Math.Max(x1, x2);
        for (int x = xMin; x <= xMax; x++)
        {
            // Only overwrite Wall tiles — don't downgrade Floor to Corridor
            if (map.GetTileKind(x, y) == TileKind.Wall)
                map.SetTile(x, y, TileKind.Corridor);
        }
    }

    private static void CarveVTunnel(GameMap map, int y1, int y2, int x)
    {
        int yMin = Math.Min(y1, y2);
        int yMax = Math.Max(y1, y2);
        for (int y = yMin; y <= yMax; y++)
        {
            if (map.GetTileKind(x, y) == TileKind.Wall)
                map.SetTile(x, y, TileKind.Corridor);
        }
    }

    /// <summary>
    /// Find a suitable position for the up stair within the player room,
    /// distinct from the player spawn center. Falls back to center if nothing else available.
    /// </summary>
    private static (int X, int Y)? FindStairUpPosition(
        GameMap map,
        Room room,
        (int X, int Y) avoidPos)
    {
        // Try center + small offset first
        int[] offsets = [-1, 1, -2, 2];
        foreach (int dx in offsets)
        {
            int nx = room.CenterX + dx;
            if (room.Contains(nx, room.CenterY) && map.IsWalkable(nx, room.CenterY)
                && (nx != avoidPos.X || room.CenterY != avoidPos.Y))
                return (nx, room.CenterY);
        }
        foreach (int dy in offsets)
        {
            int ny = room.CenterY + dy;
            if (room.Contains(room.CenterX, ny) && map.IsWalkable(room.CenterX, ny)
                && (room.CenterX != avoidPos.X || ny != avoidPos.Y))
                return (room.CenterX, ny);
        }
        // Fallback: use center (stair and player will overlap — acceptable edge case)
        return (room.CenterX, room.CenterY);
    }

    /// <summary>
    /// Designate one room as a vault (depth 3+, 15-25% depth-scaled chance).
    /// Vault rooms: not first, not last, not Grand Shrine candidate, walkable >= 25 tiles.
    /// Overrides the vault room's archetype to Generic so prop placement produces no themed
    /// props for vault rooms — vault rooms are meant to feel distinct and uncluttered.
    /// IsVault is set here; EntityPlacer reads it to place guaranteed items and a guardian.
    ///
    /// At most 1 vault per floor. Called before the prop placement loop.
    /// </summary>
    private static void DesignateVault(
        List<Room> rooms, GameMap map, int depth, SeededRandom rng)
    {
        // Depth-scaled chance: 15% at depth 3-4, 20% at depth 5-6, 25% at depth 7+
        int chancePct = depth switch
        {
            <= 4 => 15,
            <= 6 => 20,
            _    => 25,
        };

        if (rng.Next(100) >= chancePct) return; // no vault this floor

        // Eligible rooms: not first (player spawn), not last (stair exit), walkable >= 25 tiles.
        // We also exclude rooms that would qualify as Grand Shrines (Shrine archetype + large area)
        // since those have their own special reward system.
        var eligible = new List<int>();
        for (int i = 1; i < rooms.Count - 1; i++)
        {
            // Skip Grand Shrine candidates (Shrine archetype with >= 36 walkable tiles)
            if (rooms[i].Archetype == RoomArchetype.Shrine &&
                CountWalkableTiles(map, rooms[i]) >= 36) continue;

            if (CountWalkableTiles(map, rooms[i]) < 25) continue;
            eligible.Add(i);
        }

        if (eligible.Count == 0) return;

        int chosen = eligible[rng.Next(eligible.Count)];
        // Override archetype to Generic — vault rooms skip themed prop placement
        rooms[chosen] = rooms[chosen] with { IsVault = true, Archetype = RoomArchetype.Generic };
    }

    /// <summary>
    /// Count walkable tiles within a room's bounding box.
    /// Used to determine whether a room qualifies as a dead-end (small area + 1 connection).
    /// </summary>
    private static int CountWalkableTiles(GameMap map, Room room)
    {
        int count = 0;
        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
                if (map.IsWalkable(x, y)) count++;
        return count;
    }

    /// <summary>
    /// CORR-003: 25% chance per floor of placing 1-2 dead-end corridor stubs.
    /// Each stub branches perpendicularly from an interior corridor tile into wall space,
    /// 3-5 tiles long. These create branching passages that reward exploration without
    /// adding navigable routes (no rooms at the end).
    /// Called after EnsureConnectivity — can only add tiles, never disconnect.
    /// </summary>
    private static void AddCorridorStubs(GameMap map, List<CorridorSegment> corridors, SeededRandom rng)
    {
        // 25% chance to add any stubs this floor
        if (rng.Next(4) != 0) return;

        int stubCount = rng.Next(1, 3); // 1 or 2 stubs

        // Collect interior corridor tiles not adjacent to any Floor tile (away from room entrances)
        var candidates = new List<(int X, int Y)>();
        for (int x = 1; x < map.Width - 1; x++)
            for (int y = 1; y < map.Height - 1; y++)
            {
                if (map.GetTileKind(x, y) != TileKind.Corridor) continue;
                // Skip if adjacent to any Floor tile (at room boundary)
                if (map.GetTileKind(x - 1, y) == TileKind.Floor || map.GetTileKind(x + 1, y) == TileKind.Floor ||
                    map.GetTileKind(x, y - 1) == TileKind.Floor || map.GetTileKind(x, y + 1) == TileKind.Floor)
                    continue;
                candidates.Add((x, y));
            }

        if (candidates.Count == 0) return;

        for (int s = 0; s < stubCount; s++)
        {
            var (cx, cy) = candidates[rng.Next(candidates.Count)];

            // Shuffle cardinal directions and try each until one leads into clear wall space
            (int dx, int dy)[] dirs = [(-1, 0), (1, 0), (0, -1), (0, 1)];
            for (int i = dirs.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
            }

            foreach (var (dx, dy) in dirs)
            {
                // First tile in the direction must be a Wall (not already carved)
                int nx = cx + dx, ny = cy + dy;
                if (!map.InBounds(nx, ny) || map.GetTileKind(nx, ny) != TileKind.Wall) continue;

                // Verify all tiles in the stub length are Wall before carving
                int length = rng.Next(3, 6); // 3, 4, or 5 tiles
                bool ok = true;
                for (int i = 1; i <= length; i++)
                {
                    int tx = cx + dx * i, ty = cy + dy * i;
                    if (!map.InBounds(tx, ty) || map.GetTileKind(tx, ty) != TileKind.Wall)
                    { ok = false; break; }
                }
                if (!ok) continue;

                for (int i = 1; i <= length; i++)
                    map.SetTile(cx + dx * i, cy + dy * i, TileKind.Corridor);
                break; // one direction per stub
            }
        }
    }

    /// <summary>
    /// Roll a maintenance state for a room based on dungeon depth.
    /// Deeper floors skew toward Neglected / Abandoned / Ruined.
    /// </summary>
    public static RoomMaintenanceState RollMaintenanceState(int depth, SeededRandom rng)
    {
        // Weights: [WellMaintained, Normal, Neglected, Abandoned, Ruined]
        int[] weights = depth switch
        {
            <= 2 => [10, 70, 20, 0, 0],
            <= 4 => [0, 50, 30, 20, 0],
            <= 6 => [0, 20, 30, 30, 20],
            _    => [0, 10, 20, 30, 40],
        };

        int total = 0;
        foreach (var w in weights) total += w;

        int roll = rng.Next(total);
        int cumulative = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return (RoomMaintenanceState)i;
        }
        return RoomMaintenanceState.Normal;
    }

    /// <summary>
    /// CORR-002: 20% of corridor segments longer than 6 tiles get a 2-tile perpendicular
    /// widening at the midpoint, creating alcove-like niches that break up the narrow monotony
    /// of long corridors.
    /// Called after AddCorridorStubs — only widens existing segments, no new routing created.
    /// </summary>
    private static void AddCorridorAlcoves(GameMap map, List<CorridorSegment> corridors, SeededRandom rng)
    {
        foreach (var seg in corridors)
        {
            bool isHorizontal = seg.Y1 == seg.Y2;
            bool isVertical   = seg.X1 == seg.X2;
            if (!isHorizontal && !isVertical) continue; // diagonal — skip (shouldn't happen with L-tunnels)

            int length = isHorizontal
                ? Math.Abs(seg.X2 - seg.X1) + 1
                : Math.Abs(seg.Y2 - seg.Y1) + 1;

            if (length <= 6) continue;
            if (rng.Next(10) >= 2) continue; // 20% chance

            int midX = (seg.X1 + seg.X2) / 2;
            int midY = (seg.Y1 + seg.Y2) / 2;

            if (isHorizontal)
            {
                // Horizontal corridor: widen N (y-1) and S (y+1)
                if (map.InBounds(midX, midY - 1) && map.GetTileKind(midX, midY - 1) == TileKind.Wall)
                    map.SetTile(midX, midY - 1, TileKind.Corridor);
                if (map.InBounds(midX, midY + 1) && map.GetTileKind(midX, midY + 1) == TileKind.Wall)
                    map.SetTile(midX, midY + 1, TileKind.Corridor);
            }
            else
            {
                // Vertical corridor: widen E (x+1) and W (x-1)
                if (map.InBounds(midX + 1, midY) && map.GetTileKind(midX + 1, midY) == TileKind.Wall)
                    map.SetTile(midX + 1, midY, TileKind.Corridor);
                if (map.InBounds(midX - 1, midY) && map.GetTileKind(midX - 1, midY) == TileKind.Wall)
                    map.SetTile(midX - 1, midY, TileKind.Corridor);
            }
        }
    }
}
