using UnderWarden.Logic.Core;

namespace UnderWarden.Logic.ECS;

/// <summary>
/// Selects and carves non-rectangular room shapes into a GameMap.
/// All operations are confined to the Room's bounding box — the bounding box
/// still governs overlap detection and entity placement (EntityPlacer filters
/// by IsWalkable, so non-rectangular shapes work automatically).
///
/// Invariant: after any CarveRoom call, the room's center tile (CenterX, CenterY)
/// is always Floor. This guarantees corridor L-tunnels can always connect to it.
/// </summary>
public static class RoomShapeGenerator
{
    // ---------------------------------------------------------------------------
    // Shape selection
    // ---------------------------------------------------------------------------

    // Weights and minimum sizes for each shape. Only shapes whose minimum is met
    // are eligible; the remainder of the weight goes proportionally to eligible shapes.
    private static readonly (RoomShape Shape, int Weight, int MinW, int MinH)[] ShapeWeights =
    [
        (RoomShape.Rectangle,    30, 3, 3),
        (RoomShape.Union,        30, 5, 5),
        (RoomShape.Cave,         15, 7, 7),
        (RoomShape.Circle,        8, 7, 7),
        (RoomShape.Alcove,       10, 8, 8),
        (RoomShape.CorridorRoom,  7, 3, 3), // one dim >= 8, other >= 3 — checked inside
    ];

    /// <summary>
    /// Pick a room shape based on weighted probability. Only shapes whose minimum
    /// size requirement is met are eligible. Rectangle is always eligible.
    /// </summary>
    public static RoomShape SelectShape(int w, int h, SeededRandom rng)
    {
        // Build the eligible set
        var eligible = new List<(RoomShape Shape, int Weight)>();
        foreach (var (shape, weight, minW, minH) in ShapeWeights)
        {
            bool meetsSize = shape switch
            {
                RoomShape.CorridorRoom => (w >= 8 && h >= 3) || (h >= 8 && w >= 3),
                _                     => w >= minW && h >= minH,
            };
            if (meetsSize)
                eligible.Add((shape, weight));
        }

        // Should never be empty (Rectangle is 3x3 and always eligible), but guard anyway
        if (eligible.Count == 0)
            return RoomShape.Rectangle;

        int total = eligible.Sum(e => e.Weight);
        int roll = rng.Next(total);
        int cumulative = 0;
        foreach (var (shape, weight) in eligible)
        {
            cumulative += weight;
            if (roll < cumulative)
                return shape;
        }
        return RoomShape.Rectangle; // unreachable
    }

    // ---------------------------------------------------------------------------
    // Dispatch
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Carve a room using the specified shape algorithm.
    /// After carving, the center tile is guaranteed to be Floor.
    /// </summary>
    public static void CarveRoom(GameMap map, Room room, RoomShape shape, SeededRandom rng)
    {
        switch (shape)
        {
            case RoomShape.Rectangle:
                CarveRectangle(map, room);
                break;
            case RoomShape.Union:
                CarveUnion(map, room, rng);
                break;
            case RoomShape.Cave:
                CarveCave(map, room, rng);
                break;
            case RoomShape.Circle:
                CarveCircle(map, room, rng);
                break;
            case RoomShape.Alcove:
                CarveAlcove(map, room, rng);
                break;
            case RoomShape.CorridorRoom:
                CarveCorridorRoom(map, room, rng);
                break;
        }

        // Invariant: center tile is always floor so corridor tunnels connect correctly.
        map.SetTile(room.CenterX, room.CenterY, TileKind.Floor);

        // Post-carve: remove dangling inner-corner wall tiles that appear as random
        // wall pieces inside non-rectangular rooms. Any wall tile within the bounding
        // box with ≥ 2 walkable cardinal neighbors is a concave corner artifact
        // (e.g. the inner corner of an L/T/plus shape, or a cave boundary pocket).
        // Safe: only operates within this room's bounding box; the 1-tile gap between
        // room bounding boxes ensures no adjacent room's tiles are modified.
        RemoveInnerCorners(map, room);
    }

    /// <summary>
    /// Convert inner-corner and isolated wall tiles within the room bounding box
    /// to floor. A wall tile with ≥ 2 walkable cardinal neighbors is a concave
    /// corner artifact from non-rectangular room shapes — it renders as a corner
    /// tile floating inside the room area, looking like a random wall piece.
    /// </summary>
    private static void RemoveInnerCorners(GameMap map, Room room)
    {
        for (int x = room.X; x < room.X + room.Width; x++)
        {
            for (int y = room.Y; y < room.Y + room.Height; y++)
            {
                if (map.IsWalkable(x, y)) continue; // already floor

                int walkableNeighbors = 0;
                if (map.IsWalkable(x,     y - 1)) walkableNeighbors++;
                if (map.IsWalkable(x,     y + 1)) walkableNeighbors++;
                if (map.IsWalkable(x - 1, y    )) walkableNeighbors++;
                if (map.IsWalkable(x + 1, y    )) walkableNeighbors++;

                if (walkableNeighbors >= 2)
                    map.SetTile(x, y, TileKind.Floor);
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Rectangle (baseline — reproduces existing MapGenerator behavior)
    // ---------------------------------------------------------------------------

    /// <summary>Carve every tile in the bounding box as Floor.</summary>
    public static void CarveRectangle(GameMap map, Room room)
    {
        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
                map.SetTile(x, y, TileKind.Floor);
    }

    // ---------------------------------------------------------------------------
    // Union (two overlapping rectangles → L/T/cross shapes)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Carve a clean geometric compound shape — one of three variants chosen randomly:
    ///
    ///   0. Plus/cross: full-width horizontal bar + full-height vertical bar, both centered.
    ///      Bilaterally symmetric. Center tile (CenterX,CenterY) is always carved.
    ///
    ///   1. T-shape (4 orientations ⊤⊥⊣⊢): one full-span bar centered on an axis;
    ///      one half-span stem extending from the bar to one edge.
    ///      Center tile is always in the full-span bar.
    ///
    ///   2. L-shape (4 corners): one half of the room carved fully, one adjacent quarter
    ///      carved as the "foot" of the L. The boundary row/column includes the center.
    ///      Guarantees center tile is always in the carved region.
    ///
    /// All three variants guarantee the center tile is carved and all floor tiles are
    /// connected (no isolated pockets). No random protrusions — every shape uses only
    /// clean rectilinear regions.
    /// </summary>
    public static void CarveUnion(GameMap map, Room room, SeededRandom rng)
    {
        int barH = rng.Next(2, 4); // horizontal bar thickness: 2 or 3 tiles tall
        int barV = rng.Next(2, 4); // vertical bar thickness: 2 or 3 tiles wide

        // Pre-compute centered bar bounds — used by Plus and T-shape.
        int hY1 = Math.Max(room.Y, room.CenterY - barH / 2);
        int hY2 = Math.Min(room.Y + room.Height, hY1 + barH);
        int vX1 = Math.Max(room.X, room.CenterX - barV / 2);
        int vX2 = Math.Min(room.X + room.Width, vX1 + barV);

        int variant = rng.Next(3);

        if (variant == 0)
        {
            // ─── Plus/cross ───
            // Horizontal bar: full width, centered on Y
            for (int x = room.X; x < room.X + room.Width; x++)
                for (int y = hY1; y < hY2; y++)
                    map.SetTile(x, y, TileKind.Floor);
            // Vertical bar: full height, centered on X
            for (int x = vX1; x < vX2; x++)
                for (int y = room.Y; y < room.Y + room.Height; y++)
                    map.SetTile(x, y, TileKind.Floor);
        }
        else if (variant == 1)
        {
            // ─── T-shape ───
            // 4 orientations: stem below (⊥), above (⊤), left (⊣), right (⊢) the crossbar.
            // The full-span bar always passes through (CenterX, CenterY) — center guaranteed.
            int orient = rng.Next(4);

            if (orient <= 1) // horizontal crossbar, vertical stem
            {
                // Crossbar: full width, centered on Y — center tile IS in this bar
                for (int x = room.X; x < room.X + room.Width; x++)
                    for (int y = hY1; y < hY2; y++)
                        map.SetTile(x, y, TileKind.Floor);
                // Stem: centered on X, extends to the edge opposite the stem side
                // orient 0 (⊥): stem BELOW crossbar; orient 1 (⊤): stem ABOVE crossbar
                int stemY1 = orient == 0 ? hY2 : room.Y;
                int stemY2 = orient == 0 ? room.Y + room.Height : hY1;
                for (int x = vX1; x < vX2; x++)
                    for (int y = stemY1; y < stemY2; y++)
                        map.SetTile(x, y, TileKind.Floor);
            }
            else // vertical crossbar, horizontal stem
            {
                // Crossbar: full height, centered on X — center tile IS in this bar
                for (int x = vX1; x < vX2; x++)
                    for (int y = room.Y; y < room.Y + room.Height; y++)
                        map.SetTile(x, y, TileKind.Floor);
                // Stem: centered on Y, extends to the edge opposite the stem side
                // orient 2 (⊢): stem RIGHT of bar; orient 3 (⊣): stem LEFT of bar
                int stemX1 = orient == 2 ? vX2 : room.X;
                int stemX2 = orient == 2 ? room.X + room.Width : vX1;
                for (int x = stemX1; x < stemX2; x++)
                    for (int y = hY1; y < hY2; y++)
                        map.SetTile(x, y, TileKind.Floor);
            }
        }
        else
        {
            // ─── L-shape ───
            // Divide the room at (CenterX, CenterY). Three of the four quadrant-halves are carved:
            // the full "main half" (row or column) + one adjacent "foot" quadrant.
            // The boundary row/column at the center is INCLUSIVE in both halves, so the center
            // tile is always carved and all floor tiles are connected via the shared boundary.
            //
            // 4 corners (which half is "main", which quadrant is the "foot"):
            //   0 = TL: top half (full width) + bottom-left quadrant
            //   1 = TR: top half (full width) + bottom-right quadrant
            //   2 = BL: bottom half (full width) + top-left quadrant
            //   3 = BR: bottom half (full width) + top-right quadrant
            int corner = rng.Next(4);

            bool topMain = corner <= 1; // main half is top (corners 0,1) or bottom (2,3)
            bool leftFoot = (corner == 0 || corner == 2); // foot quadrant on left or right

            // Main half: full room width, top or bottom — includes the center row (CenterY)
            int mainY1 = topMain ? room.Y      : room.CenterY; // top: from top; bottom: from center
            int mainY2 = topMain ? room.CenterY + 1 : room.Y + room.Height; // +1 to include center row
            for (int x = room.X; x < room.X + room.Width; x++)
                for (int y = mainY1; y < mainY2; y++)
                    map.SetTile(x, y, TileKind.Floor);

            // Foot quadrant: one side, the other half of the room height — includes center col (CenterX)
            int footX1 = leftFoot ? room.X : room.CenterX;
            int footX2 = leftFoot ? room.CenterX + 1 : room.X + room.Width; // +1 to include center col
            int footY1 = topMain ? room.CenterY : room.Y;
            int footY2 = topMain ? room.Y + room.Height : room.CenterY + 1;
            for (int x = footX1; x < footX2; x++)
                for (int y = footY1; y < footY2; y++)
                    map.SetTile(x, y, TileKind.Floor);
        }
    }

    // ---------------------------------------------------------------------------
    // Cave (cellular automata 4-5 rule)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Generate an organic cave shape using cellular automata.
    /// Minimum bounding box 7×7. Falls back to Rectangle for smaller rooms.
    ///
    /// Algorithm:
    ///   1. Initialize local bool grid: border = wall, interior 45% wall
    ///   2. Pin: center + 3×3 neighborhood always floor (guarantees connectivity)
    ///   3. Run 4 CA iterations with 4-5 rule
    ///   4. Flood fill from center; discard disconnected pockets
    ///   5. Write surviving floor tiles to GameMap
    /// </summary>
    public static void CarveCave(GameMap map, Room room, SeededRandom rng)
    {
        if (room.Width < 7 || room.Height < 7)
        {
            CarveRectangle(map, room);
            return;
        }

        int w = room.Width;
        int h = room.Height;
        bool[,] wall = new bool[w, h]; // true = wall

        // Step 1: Initialize — border always wall, interior 45% wall
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                wall[x, y] = (x == 0 || x == w - 1 || y == 0 || y == h - 1)
                    || rng.Next(100) < 45;

        // Step 2: Pin center 3×3 as definitively floor
        int cx = w / 2, cy = h / 2;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int px = cx + dx, py = cy + dy;
                if (px >= 0 && px < w && py >= 0 && py < h)
                    wall[px, py] = false;
            }

        // Step 3: Run 4 CA iterations (4-5 rule), keeping pinned region floor
        bool[,] next = new bool[w, h];
        for (int iter = 0; iter < 4; iter++)
        {
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    // Pinned region never changes
                    if (Math.Abs(x - cx) <= 1 && Math.Abs(y - cy) <= 1)
                    {
                        next[x, y] = false;
                        continue;
                    }
                    // Border is always wall
                    if (x == 0 || x == w - 1 || y == 0 || y == h - 1)
                    {
                        next[x, y] = true;
                        continue;
                    }

                    int wallCount = CountWallNeighbors(wall, x, y, w, h);
                    next[x, y] = wall[x, y]
                        ? wallCount >= 4   // wall stays wall if >= 4 wall neighbors
                        : wallCount >= 5;  // floor becomes wall if >= 5 wall neighbors
                }
            }
            // Swap
            Array.Copy(next, wall, w * h);
        }

        // Step 4: Flood fill from center to find connected floor region
        bool[,] reachable = FloodFillLocal(wall, cx, cy, w, h);

        // Step 5: Write to map — only reachable floor tiles
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (reachable[x, y]) // reachable implies floor (not wall)
                    map.SetTile(room.X + x, room.Y + y, TileKind.Floor);
    }

    // ---------------------------------------------------------------------------
    // Circle (ellipse equation)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Carve an ellipse fitted to the room's bounding box.
    /// Minimum 7×7. Falls back to Rectangle for smaller rooms.
    /// Optional single CA smoothing pass to reduce staircase jaggedness.
    /// </summary>
    public static void CarveCircle(GameMap map, Room room, SeededRandom rng)
    {
        if (room.Width < 7 || room.Height < 7)
        {
            CarveRectangle(map, room);
            return;
        }

        // Semi-axes: a along X, b along Y
        // Use (Width-1)/2 so the ellipse boundary fits inside the bounding box
        double a = (room.Width - 1) / 2.0;
        double b = (room.Height - 1) / 2.0;
        double cx = room.X + a;
        double cy = room.Y + b;

        // First pass: carve ellipse
        for (int x = room.X; x < room.X + room.Width; x++)
        {
            for (int y = room.Y; y < room.Y + room.Height; y++)
            {
                double dx = x - cx;
                double dy = y - cy;
                if (dx * dx / (a * a) + dy * dy / (b * b) <= 1.0)
                    map.SetTile(x, y, TileKind.Floor);
            }
        }

        // One CA smoothing pass: wall tile with <= 2 wall neighbors (of 8) → floor
        // This fills in single-tile staircase notches for a smoother oval appearance
        for (int x = room.X + 1; x < room.X + room.Width - 1; x++)
        {
            for (int y = room.Y + 1; y < room.Y + room.Height - 1; y++)
            {
                if (map.GetTileKind(x, y) == TileKind.Floor) continue;
                int wallNeighbors = 0;
                for (int nx = x - 1; nx <= x + 1; nx++)
                    for (int ny = y - 1; ny <= y + 1; ny++)
                        if ((nx != x || ny != y) && map.GetTileKind(nx, ny) == TileKind.Wall)
                            wallNeighbors++;
                if (wallNeighbors <= 2)
                    map.SetTile(x, y, TileKind.Floor);
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Alcove (rectangle with wall protrusions)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Carve a base rectangle with 2-tile margin, then add symmetric alcove pairs
    /// on the N/S and E/W wall pairs. Each axis pair shares the same offset so the
    /// resulting room is bilaterally symmetric on both axes.
    /// Minimum 8×8. Falls back to Rectangle for smaller rooms.
    /// </summary>
    public static void CarveAlcove(GameMap map, Room room, SeededRandom rng)
    {
        if (room.Width < 8 || room.Height < 8)
        {
            CarveRectangle(map, room);
            return;
        }

        // Base rectangle with 2-tile margin on all sides
        int bx1 = room.X + 2;
        int by1 = room.Y + 2;
        int bx2 = room.X + room.Width - 2;  // exclusive
        int by2 = room.Y + room.Height - 2; // exclusive

        // Carve the base rectangle
        for (int x = bx1; x < bx2; x++)
            for (int y = by1; y < by2; y++)
                map.SetTile(x, y, TileKind.Floor);

        // --- N/S alcove pair (horizontal walls) ---
        // Roll once; if success, place the SAME alcove on both N and S walls.
        int northLen = bx2 - bx1;
        if (northLen > 3 && rng.Next(100) < 40) // 40% chance for the pair
        {
            int depth = rng.Next(1, 3);           // 1-2 deep
            int width = rng.Next(1, Math.Min(4, northLen)); // 1-3 wide, ≤ wall length
            int posOffset = rng.Next(0, northLen - width + 1); // same position for both

            for (int w = 0; w < width; w++)
            {
                for (int d = 1; d <= depth; d++)
                {
                    int alcX = bx1 + posOffset + w;
                    // North: extrude toward room.Y (decreasing Y)
                    if (room.Contains(alcX, by1 - d))
                        map.SetTile(alcX, by1 - d, TileKind.Floor);
                    // South: extrude toward room.Y+Height (increasing Y) — mirrors North
                    if (room.Contains(alcX, by2 - 1 + d))
                        map.SetTile(alcX, by2 - 1 + d, TileKind.Floor);
                }
            }
        }

        // --- E/W alcove pair (vertical walls) ---
        int westLen = by2 - by1;
        if (westLen > 3 && rng.Next(100) < 40) // 40% chance for the pair
        {
            int depth = rng.Next(1, 3);
            int width = rng.Next(1, Math.Min(4, westLen));
            int posOffset = rng.Next(0, westLen - width + 1);

            for (int w = 0; w < width; w++)
            {
                for (int d = 1; d <= depth; d++)
                {
                    int alcY = by1 + posOffset + w;
                    // West: extrude toward room.X (decreasing X)
                    if (room.Contains(bx1 - d, alcY))
                        map.SetTile(bx1 - d, alcY, TileKind.Floor);
                    // East: extrude toward room.X+Width (increasing X) — mirrors West
                    if (room.Contains(bx2 - 1 + d, alcY))
                        map.SetTile(bx2 - 1 + d, alcY, TileKind.Floor);
                }
            }
        }
    }

    // ---------------------------------------------------------------------------
    // CorridorRoom (long thin strip)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Carve a corridor-room: a long thin strip 2-3 tiles wide, full bounding-box length.
    /// Horizontal when Width > Height, vertical when Height > Width, random when square.
    /// Minimum: one dimension >= 8 AND other >= 3. Falls back to Rectangle otherwise.
    /// </summary>
    public static void CarveCorridorRoom(GameMap map, Room room, SeededRandom rng)
    {
        bool canH = room.Width >= 8 && room.Height >= 3;
        bool canV = room.Height >= 8 && room.Width >= 3;

        if (!canH && !canV)
        {
            CarveRectangle(map, room);
            return;
        }

        bool horizontal;
        if (canH && canV)
            horizontal = room.Width >= room.Height || (room.Width == room.Height && rng.Next(2) == 0);
        else
            horizontal = canH;

        int corridorWidth = rng.Next(2, 4); // 2 or 3 tiles

        if (horizontal)
        {
            // Center on Y axis, full X length
            int midY = room.CenterY;
            int halfW = corridorWidth / 2;
            int y1 = Math.Max(room.Y, midY - halfW);
            int y2 = Math.Min(room.Y + room.Height - 1, midY + corridorWidth - 1 - halfW);
            for (int x = room.X; x < room.X + room.Width; x++)
                for (int y = y1; y <= y2; y++)
                    map.SetTile(x, y, TileKind.Floor);
        }
        else
        {
            // Center on X axis, full Y length
            int midX = room.CenterX;
            int halfW = corridorWidth / 2;
            int x1 = Math.Max(room.X, midX - halfW);
            int x2 = Math.Min(room.X + room.Width - 1, midX + corridorWidth - 1 - halfW);
            for (int y = room.Y; y < room.Y + room.Height; y++)
                for (int x = x1; x <= x2; x++)
                    map.SetTile(x, y, TileKind.Floor);
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Count wall neighbors (8-connectivity) in a local bool grid.
    /// Out-of-bounds cells count as wall.
    /// </summary>
    private static int CountWallNeighbors(bool[,] wall, int x, int y, int w, int h)
    {
        int count = 0;
        for (int nx = x - 1; nx <= x + 1; nx++)
            for (int ny = y - 1; ny <= y + 1; ny++)
            {
                if (nx == x && ny == y) continue;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h)
                    count++; // treat OOB as wall
                else if (wall[nx, ny])
                    count++;
            }
        return count;
    }

    /// <summary>
    /// Flood fill from (startX, startY) in a local bool grid (true=wall, false=floor).
    /// Returns bool[,] where true means "reachable floor tile."
    /// </summary>
    private static bool[,] FloodFillLocal(bool[,] wall, int startX, int startY, int w, int h)
    {
        var reachable = new bool[w, h];
        if (wall[startX, startY]) return reachable; // start is a wall — nothing reachable

        var queue = new Queue<(int x, int y)>();
        queue.Enqueue((startX, startY));
        reachable[startX, startY] = true;

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            // 4-connectivity flood fill (corridors are 1 tile wide — diagonal not needed)
            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (reachable[nx, ny] || wall[nx, ny]) continue;
                reachable[nx, ny] = true;
                queue.Enqueue((nx, ny));
            }
        }
        return reachable;
    }

}
