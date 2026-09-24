using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Map;

/// <summary>
/// Pure pathfinding algorithms. No Godot dependencies.
/// A* for point-to-point pathing; Dijkstra flood-fill for nearest-target queries.
/// </summary>
public static class Pathfinder
{
    /// <summary>
    /// A* pathfinding on the logical grid, 8-directional movement.
    /// Returns path as (X,Y) positions NOT including the start, inclusive of goal.
    /// Returns null if no path exists. Returns empty list if fromX/Y == toX/Y.
    ///
    /// movingEntity: excluded from blocking checks (can't block itself).
    /// Destination tile is always pathable even if occupied by another entity.
    /// Diagonal moves are blocked when either adjacent cardinal tile is a wall (no corner-cutting).
    /// </summary>
    public static List<(int X, int Y)>? AStar(
        GameMap map, int fromX, int fromY, int toX, int toY,
        Entity? movingEntity = null, bool canPassDoors = false,
        HashSet<(int, int)>? avoidTiles = null, bool terrainOnly = false)
    {
        if (fromX == toX && fromY == toY) return new List<(int, int)>();

        if (!map.InBounds(toX, toY)) return null;

        // g costs: cardinal = 10, diagonal = 14
        const int CardinalCost = 10;
        const int DiagonalCost = 14;

        var openSet = new PriorityQueue<(int X, int Y), int>();
        var gCost = new Dictionary<(int, int), int>();
        var cameFrom = new Dictionary<(int, int), (int, int)>();

        var start = (fromX, fromY);
        var goal = (toX, toY);

        gCost[start] = 0;
        openSet.Enqueue(start, Heuristic(fromX, fromY, toX, toY));

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();

            if (current == goal)
                return ReconstructPath(cameFrom, current);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;

                    int nx = current.X + dx;
                    int ny = current.Y + dy;
                    var neighbor = (nx, ny);

                    bool isDiagonal = dx != 0 && dy != 0;

                    // Check passability — destination is always allowed even if occupied
                    bool isDestination = nx == toX && ny == toY;
                    // TERRAIN ONLY, for callers asking about the LEVEL rather than about this
                    // turn. A route is a property of the map's shape and its graph, not of who
                    // happens to be standing where — and with occupancy on, ONE CREATURE IN A
                    // ONE-TILE CORRIDOR SEVERS THE GRAPH. Measured: with the player at (8,11) in
                    // the review scene's chokepoint, the whole level returned spine:0/routes:0
                    // and the floor's entire route model vanished.
                    bool passable = terrainOnly
                        ? (map.IsWalkable(nx, ny)
                           || (canPassDoors && map.GetTileKind(nx, ny) == TileKind.Door))
                        : map.CanMoveToWith(nx, ny, movingEntity,
                                            ignoreEntityAtDest: isDestination,
                                            canPassDoors: canPassDoors);
                    if (!passable)
                        continue;

                    // Skip avoid-tiles unless we're already standing on one (start tile)
                    // or the destination itself is the goal (can't route elsewhere).
                    if (avoidTiles != null && avoidTiles.Contains(neighbor) && !isDestination)
                        continue;

                    int moveCost = isDiagonal ? DiagonalCost : CardinalCost;
                    int tentativeG = gCost[current] + moveCost;

                    if (!gCost.TryGetValue(neighbor, out int existingG) || tentativeG < existingG)
                    {
                        gCost[neighbor] = tentativeG;
                        cameFrom[neighbor] = current;
                        int f = tentativeG + Heuristic(nx, ny, toX, toY);
                        openSet.Enqueue(neighbor, f);
                    }
                }
            }
        }

        return null; // No path found
    }

    /// <summary>
    /// Build a set of detected, non-spent floor trap positions from the features list.
    /// Used by the harness bot to route around known traps via trap-aware A*.
    /// </summary>
    public static HashSet<(int, int)> DetectedTrapTiles(IReadOnlyList<Entity> features)
    {
        var tiles = new HashSet<(int, int)>();
        foreach (var f in features)
        {
            var trap = f.Get<FloorTrapComponent>();
            if (trap != null && trap.IsDetected && !trap.IsSpent)
                tiles.Add((f.X, f.Y));
        }
        return tiles;
    }

    /// <summary>
    /// Dijkstra flood-fill from source point.
    /// Returns int[,] distance array indexed [x, y]. int.MaxValue = unreachable.
    /// Traverses walkable tiles only (ignores entity blocking — targets tiles, not paths through entities).
    /// 8-directional.
    /// </summary>
    public static int[,] DijkstraMap(GameMap map, int fromX, int fromY, bool canPassDoors = false)
    {
        var dist = new int[map.Width, map.Height];
        for (int x = 0; x < map.Width; x++)
            for (int y = 0; y < map.Height; y++)
                dist[x, y] = int.MaxValue;

        if (!map.InBounds(fromX, fromY)) return dist;

        dist[fromX, fromY] = 0;
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((fromX, fromY));

        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            int nextDist = dist[cx, cy] + 1;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = cx + dx, ny = cy + dy;
                    if (!map.InBounds(nx, ny)) continue;
                    bool passable = map.IsWalkable(nx, ny) || (canPassDoors && map.GetTileKind(nx, ny) == TileKind.Door);
                    if (!passable) continue;

                    if (dist[nx, ny] != int.MaxValue) continue;

                    dist[nx, ny] = nextDist;
                    queue.Enqueue((nx, ny));
                }
            }
        }

        return dist;
    }

    /// <summary>
    /// Find the (x,y) with the minimum Dijkstra distance where predicate holds.
    /// Returns null if no matching reachable tile exists.
    /// </summary>
    public static (int X, int Y)? NearestWhere(
        int[,] dijkstra, int width, int height, Func<int, int, bool> predicate)
    {
        int bestDist = int.MaxValue;
        (int X, int Y)? best = null;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int d = dijkstra[x, y];
                if (d == int.MaxValue) continue;
                if (!predicate(x, y)) continue;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = (x, y);
                }
            }
        }

        return best;
    }

    // Octile distance heuristic (scaled integers, no floats).
    // h = 10*max(dx,dy) + 4*min(dx,dy)  ≡  14*min + 10*(max-min) — consistent with g costs.
    private static int Heuristic(int x1, int y1, int x2, int y2)
    {
        int dx = Math.Abs(x2 - x1);
        int dy = Math.Abs(y2 - y1);
        return 10 * Math.Max(dx, dy) + 4 * Math.Min(dx, dy);
    }

    private static List<(int X, int Y)> ReconstructPath(
        Dictionary<(int, int), (int, int)> cameFrom, (int X, int Y) current)
    {
        var path = new List<(int X, int Y)>();
        while (cameFrom.ContainsKey(current))
        {
            path.Add(current);
            current = cameFrom[current];
        }
        path.Reverse();
        return path;
    }
}
