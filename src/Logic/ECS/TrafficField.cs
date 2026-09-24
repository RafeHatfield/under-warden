using UnderWarden.Logic.Map;

namespace UnderWarden.Logic.ECS;

/// <summary>
/// WHERE PEOPLE ACTUALLY WALK, derived from the level graph and never painted.
///
/// THE DEVICE GATE, third walk: *"the worn path should be on a walking path — down the hallway,
/// through the room, into the next hallway… worn tiles in the middle of the room and no flow."*
/// Ruled: **wear is a property of TRAFFIC, not of rooms.** The field it replaces was two octaves
/// of value noise — it varied, and a blind seat measured that variation at nearly a full ladder
/// rung, but it varied with nothing. A corner nothing crosses looked exactly like a threshold
/// everything does, and the seat said so: *"same hatch density, same edge condition, same joint
/// depth."*
///
/// So the scalar is now accumulated TRAVERSAL. Every route the level implies is walked and drops
/// wear along it; where routes converge — and they converge at doorways, because a doorway is the
/// only way into a room — the deposit piles up. Nothing here draws a path. The path is what is
/// left when every journey has been taken.
///
/// WEIGHTED BY STRUCTURAL IMPORTANCE, from the generator's own semantics and nothing else:
///
///     the spine        entry to exit                     full weight
///     major rooms      on or adjacent to the spine       full
///     secondary        reachable, ordinary               moderate
///     remote branches  dead ends, decaying with distance light
///     vaults, shrines  nobody is admitted                ZERO
///
/// The zero is deliberate and is the most useful value in the table: **an unworn threshold is
/// readable as "nobody comes here"**, which is a thing the floor can say that no amount of
/// uniform aging can.
///
/// PURE LOGIC, NO GODOT. The renderer consumes the result; this computes it, and the test suite
/// can check the hierarchy without a scene, a device, or a capture.
/// </summary>
public static class TrafficField
{
    /// <summary>Weights per structural tier. Named so a test can assert the ordering.</summary>
    public const double SpineWeight = 1.0;
    public const double MajorWeight = 0.75;
    public const double SecondaryWeight = 0.45;
    public const double RemoteWeight = 0.18;
    public const double SealedWeight = 0.0;

    /// <summary>How much a route's deposit decays per tile of graph distance from the spine.</summary>
    public const double DecayPerTile = 0.010;

    public sealed record Result(byte[,] Field, int Routes, double SpineLength,
                                Dictionary<string, int> TierCounts,
                                IReadOnlyList<RoutePolyline.Line> Lines);

    /// <summary>
    /// The routes as LINES, smoothed to walking curvature and nudged off centre.
    ///
    /// The scalar field above is kept — it is what the age layer's magnitude has always read and
    /// it is verified by seven tests — but round 21 measured that a per-tile scalar cannot supply
    /// a DIRECTION a viewer can follow: its derived axis agreed between neighbours only 34% of the
    /// time. The same routes, emitted as lines, are coherent by construction.
    /// </summary>
    private static List<RoutePolyline.Line> MakeLines(
        GameMap map, IEnumerable<(List<(int X, int Y)> Path, double Weight)> routes, int seed)
    {
        var outp = new List<RoutePolyline.Line>();
        foreach (var (path, weight) in routes)
        {
            if (path == null || path.Count < 2) continue;
            // ⚠ SPINE AND REAL ROUTES ONLY, not every leaf. The scalar field is fed by every
            // branch the graph finds, which is right for a field — but as LINES, nineteen of them
            // flooded the review scene: every tile was near some route, adjacent tiles were
            // nearest to DIFFERENT routes, and the axis flipped between them. Coherence read 70%
            // where it should read ~100% along a route.
            //
            // A remote branch is walked lightly and should show as lightly-worn ground, which the
            // scalar already does. It is not a LINE the floor states, and the ruling says as much:
            // portal-to-portal plus spine.
            if (weight < SecondaryWeight) continue;
            var pts = RoutePolyline.Jitter(RoutePolyline.Smooth(path),
                                           (x, y) => map.IsWalkable(x, y), seed);
            outp.Add(new RoutePolyline.Line(pts, weight));
        }
        return outp;
    }

    /// <summary>
    /// Accumulate traversal over the walkable grid. Returns a per-TILE scalar, 0..255.
    ///
    /// Per tile rather than per pixel on purpose: the renderer interpolates between tile centres,
    /// so the field arrives smooth and never as a grid of blocks. A per-tile scalar consumed
    /// directly would be §8.3.1's lattice with a traffic model behind it.
    /// </summary>
    public static Result Compute(GameMap map, IReadOnlyList<Room> rooms,
                                 (int X, int Y) entry, (int X, int Y)? exit)
    {
        int w = map.Width, h = map.Height;
        var acc = new double[w, h];
        int routes = 0;

        // ---- THE SPINE: the one journey every run makes.
        var spine = exit is { } e
            ? Pathfinder.AStar(map, entry.X, entry.Y, e.X, e.Y, canPassDoors: true, terrainOnly: true)
            : null;
        if (spine == null || spine.Count == 0)
        {
            // No exit, or no way to it. Fall back to the longest journey the level affords, so a
            // level without stairs still has a spine rather than a blank field.
            var far = FarthestWalkable(map, entry);
            spine = far is { } f
                ? Pathfinder.AStar(map, entry.X, entry.Y, f.X, f.Y, canPassDoors: true, terrainOnly: true)
                : new List<(int X, int Y)>();
        }
        if (spine.Count > 0) routes++;
        foreach (var (x, y) in spine) Deposit(acc, w, h, x, y, SpineWeight);
        var lineSrc = new List<(List<(int X, int Y)> Path, double Weight)>();
        if (spine.Count >= 2) lineSrc.Add((spine, SpineWeight));

        // Distance from the spine, in tiles, for the decay term.
        var spineSet = new HashSet<(int, int)>(spine);
        var tiers = new Dictionary<string, int>
        {
            ["spine"] = spine.Count, ["major"] = 0, ["secondary"] = 0,
            ["remote"] = 0, ["sealed"] = 0,
        };

        // ---- EVERY ROOM IS VISITED FROM THE SPINE, at the weight its role earns.
        foreach (var room in rooms)
        {
            double weight = WeightFor(room);
            string tier = TierFor(room);

            if (weight <= 0.0)
            {
                tiers["sealed"]++;
                continue;   // a sealed room deposits nothing — and that silence is the signal
            }

            var target = NearestWalkable(map, room.CenterX, room.CenterY);
            if (target is not { } t) continue;

            var from = NearestOf(spineSet, t) ?? entry;
            var route = Pathfinder.AStar(map, from.X, from.Y, t.X, t.Y, canPassDoors: true, terrainOnly: true);
            if (route == null) continue;
            routes++;
            tiers[tier]++;
            lineSrc.Add((route, weight));

            // Decay with distance travelled off the spine: a remote branch is walked, but less,
            // and less the further out it goes.
            for (int i = 0; i < route.Count; i++)
            {
                double decayed = weight * System.Math.Max(0.15, 1.0 - DecayPerTile * i);
                Deposit(acc, w, h, route[i].X, route[i].Y, decayed);
            }
        }

        // ---- SMOOTH, so the path has width and its edges fray rather than ending on a pixel.
        var sm = Smooth(acc, w, h, 2);

        // ---- NORMALISE. A level whose busiest tile is lightly walked should still read as a
        // level with a route through it, so the scale is relative to the level's own maximum.
        double max = 0.0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (sm[x, y] > max) max = sm[x, y];

        var field = new byte[w, h];
        if (max > 0)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    field[x, y] = (byte)System.Math.Clamp((int)System.Math.Round(sm[x, y] / max * 255.0), 0, 255);

        return new Result(field, routes, spine.Count, tiers, MakeLines(map, lineSrc, 1337));
    }

    /// <summary>
    /// The same accumulation for a map with no Room records — the review scenes, and anything
    /// else built by carving rather than by the generator.
    ///
    /// Entry and exit are the two ends of the level's own longest walk, found by a double sweep;
    /// every remaining leaf — a cell that is a local maximum of distance from the spine — is a
    /// branch, walked at the remote weight and decaying as it goes. The hierarchy it can express
    /// is therefore SPINE and BRANCH and nothing between, which is a limit of the map rather than
    /// of the model: a scene with one room and one alcove has no secondary tier to show.
    /// </summary>
    public static Result ComputeFromMap(GameMap map)
    {
        var start = FirstWalkable(map);
        if (start is not { } s0)
            return new Result(new byte[map.Width, map.Height], 0, 0, new(),
                              new List<RoutePolyline.Line>());

        var a = FarthestWalkable(map, s0) ?? s0;
        var b = FarthestWalkable(map, a) ?? a;

        int w = map.Width, h = map.Height;
        var acc = new double[w, h];
        var spine = Pathfinder.AStar(map, a.X, a.Y, b.X, b.Y, canPassDoors: true, terrainOnly: true)
                    ?? new List<(int X, int Y)>();
        foreach (var (x, y) in spine) Deposit(acc, w, h, x, y, SpineWeight);

        // Distance from the spine, so leaves can be found and branches can decay along their run.
        var dist = DistanceFrom(map, spine);
        var leaves = new List<(int X, int Y)>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!map.IsWalkable(x, y) || dist[x, y] <= 1 || dist[x, y] == int.MaxValue) continue;
                bool localMax = true;
                for (int dy = -1; dy <= 1 && localMax; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                        if (dist[nx, ny] != int.MaxValue && dist[nx, ny] > dist[x, y])
                        { localMax = false; break; }
                    }
                if (localMax) leaves.Add((x, y));
            }

        var spineSet = new HashSet<(int, int)>(spine);
        int routes = spine.Count > 0 ? 1 : 0;
        var lineSrc = new List<(List<(int X, int Y)> Path, double Weight)>();
        if (spine.Count >= 2) lineSrc.Add((spine, SpineWeight));
        foreach (var leaf in leaves)
        {
            var from = NearestOf(spineSet, leaf);
            if (from is not { } f) continue;
            var route = Pathfinder.AStar(map, f.X, f.Y, leaf.X, leaf.Y, canPassDoors: true, terrainOnly: true);
            if (route == null) continue;
            routes++;
            lineSrc.Add((route, RemoteWeight));
            for (int i = 0; i < route.Count; i++)
                Deposit(acc, w, h, route[i].X, route[i].Y,
                        RemoteWeight * System.Math.Max(0.15, 1.0 - DecayPerTile * i));
        }

        var sm = Smooth(acc, w, h, 2);
        double max = 0.0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) if (sm[x, y] > max) max = sm[x, y];

        var field = new byte[w, h];
        if (max > 0)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    field[x, y] = (byte)System.Math.Clamp(
                        (int)System.Math.Round(sm[x, y] / max * 255.0), 0, 255);

        return new Result(field, routes, spine.Count,
                          new Dictionary<string, int> { ["spine"] = spine.Count,
                                                        ["remote"] = leaves.Count },
                          MakeLines(map, lineSrc, 1337));
    }

    private static (int X, int Y)? FirstWalkable(GameMap map)
    {
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                if (map.IsWalkable(x, y)) return (x, y);
        return null;
    }

    private static int[,] DistanceFrom(GameMap map, List<(int X, int Y)> seeds)
    {
        int w = map.Width, h = map.Height;
        var d = new int[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) d[x, y] = int.MaxValue;
        var q = new Queue<(int X, int Y)>();
        foreach (var (x, y) in seeds) { if (d[x, y] != 0) { d[x, y] = 0; q.Enqueue((x, y)); } }
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    if (!map.IsWalkable(nx, ny) || d[nx, ny] != int.MaxValue) continue;
                    d[nx, ny] = d[x, y] + 1;
                    q.Enqueue((nx, ny));
                }
        }
        return d;
    }

    /// <summary>The tier a room earns, from the generator's semantics only.</summary>
    public static string TierFor(Room room)
    {
        if (room.IsVault || room.IsGrandShrine) return "sealed";
        if (room.IsDeadEnd) return "remote";
        return room.Archetype == RoomArchetype.Generic ? "secondary" : "major";
    }

    public static double WeightFor(Room room) => TierFor(room) switch
    {
        "sealed" => SealedWeight,
        "remote" => RemoteWeight,
        "secondary" => SecondaryWeight,
        _ => MajorWeight,
    };

    /// <summary>
    /// A deposit is a small brush, not a pixel. A single-cell trail would read as a drawn line —
    /// the register guardrail is that the path is discovered, never staged — so each step also
    /// touches its neighbours at a third, and the accumulated width varies with how many routes
    /// happened to pass.
    /// </summary>
    private static void Deposit(double[,] acc, int w, int h, int x, int y, double amount)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return;
        acc[x, y] += amount;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && nx < w && ny >= 0 && ny < h) acc[nx, ny] += amount * 0.33;
            }
    }

    private static double[,] Smooth(double[,] a, int w, int h, int r)
    {
        var outp = new double[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double s = 0; int n = 0;
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                        s += a[nx, ny]; n++;
                    }
                outp[x, y] = n > 0 ? s / n : 0;
            }
        return outp;
    }

    private static (int X, int Y)? NearestWalkable(GameMap map, int x, int y)
    {
        if (map.IsWalkable(x, y)) return (x, y);
        for (int r = 1; r < 8; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (map.IsWalkable(nx, ny)) return (nx, ny);
                }
        return null;
    }

    private static (int X, int Y)? NearestOf(HashSet<(int, int)> set, (int X, int Y) to)
    {
        (int X, int Y)? best = null;
        int bestD = int.MaxValue;
        foreach (var (x, y) in set)
        {
            int d = System.Math.Abs(x - to.X) + System.Math.Abs(y - to.Y);
            if (d < bestD) { bestD = d; best = (x, y); }
        }
        return best;
    }

    private static (int X, int Y)? FarthestWalkable(GameMap map, (int X, int Y) from)
    {
        var d = Pathfinder.DijkstraMap(map, from.X, from.Y, canPassDoors: true);
        (int X, int Y)? best = null;
        int bestD = -1;
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                if (d[x, y] > bestD && d[x, y] < int.MaxValue && map.IsWalkable(x, y))
                { bestD = d[x, y]; best = (x, y); }
        return best;
    }
}
