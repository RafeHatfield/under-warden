using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Map;

/// <summary>
/// Field of view computation using 8-octant recursive shadowcasting.
/// Algorithm by Björn Bergström (http://roguebasin.com/index.php/FOV_using_recursive_shadowcasting).
///
/// Pure C# — no Godot dependencies. Results written directly to GameMap via SetVisible.
/// </summary>
public static class FovComputer
{
    // Octant transform table: maps (dx, dy) in octant-0 space to map coordinates.
    // Each row is one octant: [xx, xy, yx, yy] multipliers.
    // mapX = cx + dx*xx + dy*yx
    // mapY = cy + dx*xy + dy*yy
    private static readonly int[,] Mult =
    {
        {  1,  0,  0,  1 },
        {  0,  1,  1,  0 },
        {  0, -1,  1,  0 },
        { -1,  0,  0,  1 },
        { -1,  0,  0, -1 },
        {  0, -1, -1,  0 },
        {  0,  1, -1,  0 },
        {  1,  0,  0, -1 },
    };

    /// <summary>
    /// Compute FOV from (centerX, centerY) with the given radius (Chebyshev tiles).
    ///
    /// Clears current visibility, then marks all tiles within radius and line-of-sight
    /// as visible. Explored state is set as a permanent side effect — tiles that have
    /// been seen are never un-explored.
    /// </summary>
    public static void Compute(GameMap map, int centerX, int centerY, int radius = 8)
    {
        map.ClearAllVisible();
        map.SetVisible(centerX, centerY); // player's own tile is always visible

        for (int octant = 0; octant < 8; octant++)
        {
            CastLight(map, centerX, centerY, radius, 1, 1.0f, 0.0f,
                Mult[octant, 0], Mult[octant, 1],
                Mult[octant, 2], Mult[octant, 3]);
        }
    }

    /// <summary>
    /// Recursive shadowcasting for one octant. Traces shadow segments across each
    /// row (distance from origin), splitting on obstacle boundaries.
    ///
    /// Parameters:
    ///   row        — current row distance from origin (starts at 1, recurse deeper)
    ///   startSlope — left (wider) shadow boundary (1.0 = full open)
    ///   endSlope   — right (narrower) shadow boundary (0.0 = edge of octant)
    ///   xx,xy      — row/column transformation for this octant
    ///   yx,yy      — row/column transformation for this octant
    /// </summary>
    private static void CastLight(
        GameMap map, int cx, int cy, int radius,
        int row, float startSlope, float endSlope,
        int xx, int xy, int yx, int yy)
    {
        if (startSlope < endSlope) return;

        float nextStartSlope = startSlope;

        for (int i = row; i <= radius; i++)
        {
            bool blocked = false;
            int dx = -i;

            for (; dx <= 0; dx++)
            {
                int dy = -i;

                // Translate octant-local (dx, dy) to actual map coordinates
                int mapX = cx + dx * xx + dy * yx;
                int mapY = cy + dx * xy + dy * yy;

                // Left and right slopes for this cell
                float lSlope = (dx - 0.5f) / (dy + 0.5f);
                float rSlope = (dx + 0.5f) / (dy - 0.5f);

                if (startSlope < rSlope) continue;
                if (endSlope > lSlope) break;

                // Within FOV cone — mark visible if within radius
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist <= radius)
                    map.SetVisible(mapX, mapY);

                if (blocked)
                {
                    // We're inside an obstacle run. Did this cell clear it?
                    if (BlocksSight(map, mapX, mapY))
                    {
                        // Still blocked — advance the shadow boundary
                        nextStartSlope = rSlope;
                        continue;
                    }
                    // Clear cell after obstacle — end the blocked run, restore slope
                    blocked = false;
                    startSlope = nextStartSlope;
                }
                else if (BlocksSight(map, mapX, mapY))
                {
                    // New obstacle — recurse for the lit region before this obstacle,
                    // then start tracking the new shadow
                    blocked = true;
                    nextStartSlope = rSlope;
                    CastLight(map, cx, cy, radius, i + 1, startSlope, lSlope,
                        xx, xy, yx, yy);
                }
            }

            // If the entire row was blocked (obstacle ran to the edge), stop.
            if (blocked) break;
        }
    }

    /// <summary>
    /// Returns true if the tile at (x, y) blocks line of sight.
    /// Out-of-bounds tiles are treated as opaque walls.
    /// </summary>
    private static bool BlocksSight(GameMap map, int x, int y)
    {
        if (!map.InBounds(x, y)) return true;
        var kind = map.GetTileKind(x, y);
        return kind == TileKind.Wall || kind == TileKind.Door || kind == TileKind.SecretDoor;
    }
}
