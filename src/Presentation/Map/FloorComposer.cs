using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;

namespace UnderWarden.Presentation.Map;

/// <summary>
/// The tile type assigned to each walkable floor cell by FloorComposer.
///
/// Passes run in priority order: later passes override earlier ones.
/// Dark always wins — a wall-adjacent tile never becomes Accent.
/// </summary>
public enum FloorTileType
{
    /// Standard floor tile (85%+ of walkable cells in open rooms).
    Standard,

    /// Wall-adjacent shadow tile. Applied in the edge-darkening pass.
    /// Tiles with any wall in their 8-cell neighborhood receive this type.
    /// Rendered with a subtle colour modulate (not a separate asset) for edge depth.
    Dark,

    /// Noise-driven accent cluster. Applied to Standard tiles only (never Dark).
    /// Simplex noise produces organic blobs (~10% of standard tiles) rather than
    /// salt-and-pepper scatter.
    Accent,

    /// Worn path tile. Reserved for future use (e.g., worn pathways through rooms).
    /// Currently unused by the pipeline but available to callers.
    Worn,
}

/// <summary>
/// Multi-pass floor tile compositor. Runs purely on GameMap data — no Godot APIs.
///
/// The pipeline has three passes:
///   1. Base fill — all walkable tiles start as Standard.
///   2. Edge darkening — tiles adjacent to walls become Dark for shadow effect.
///   3. Noise variation — remaining Standard tiles may become Accent via simplex noise.
///
/// Results are deterministic: same map + seed → same output every time.
/// </summary>
public static class FloorComposer
{
    /// <summary>
    /// Pre-compute the floor tile variant for every walkable tile in the map.
    /// Returns a dictionary keyed by (x, y) for all walkable tiles.
    /// Non-walkable (wall) tiles are not included.
    ///
    /// The seed offsets the simplex noise pattern so each map floor looks different
    /// even with the same geometry. Pass 0 for a consistent baseline appearance.
    /// </summary>
    public static Dictionary<(int X, int Y), FloorTileType> Compose(GameMap map, int seed)
    {
        var result = new Dictionary<(int X, int Y), FloorTileType>();

        // Pass 1: base fill — all walkable tiles start as Standard
        for (int x = 0; x < map.Width; x++)
            for (int y = 0; y < map.Height; y++)
                if (map.IsWalkable(x, y))
                    result[(x, y)] = FloorTileType.Standard;

        // Pass 2: edge darkening — tiles next to walls become Dark
        ApplyEdgeDarkening(map, result);

        // Pass 3a: worn patch variation — large low-frequency blobs (~6% of Standard tiles).
        // Represents well-travelled floor: stone worn smooth by foot traffic.
        // Runs before Accent so worn and accent pools are mutually exclusive.
        ApplyWornVariation(result, seed);

        // Pass 3b: noise-driven accent variation — only affects remaining Standard tiles.
        // Dark tiles are never overridden to keep wall shadows intact.
        ApplyNoiseVariation(result, seed);

        return result;
    }

    // -------------------------------------------------------------------------
    // Pass 2: Edge darkening
    // -------------------------------------------------------------------------

    private static void ApplyEdgeDarkening(GameMap map, Dictionary<(int X, int Y), FloorTileType> result)
    {
        // All 8 neighbor directions (cardinal + diagonal)
        int[] dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
        int[] dy = { -1, -1, -1, 0, 0, 1, 1, 1 };

        foreach (var (pos, _) in result.ToList()) // ToList: avoid modifying dict while iterating
        {
            int x = pos.X, y = pos.Y;

            // Distance 0: any of the 8 immediate neighbors is a wall → always Dark.
            // This catches all room edges including diagonal corners.
            //
            // Wall-ness is IsWallTile, NOT !IsWalkable. Walkability also excludes blocking
            // props (and closed doors), so borrowing it here made prop-occupied cells cast
            // wall-shadow — a "pedestal" halo around every blocking prop, mid-room, with no
            // wall to justify it. Never designed; an accident of predicate reuse (ruled a
            // render bug, Rafe 2026-08). Edge darkening is a WALL-shadow pass, so it must
            // test rendered walls (Wall/SecretDoor) — the same predicate the wall autotiler
            // uses — and props must not participate.
            bool adjacentToWall = false;
            for (int d = 0; d < 8; d++)
            {
                if (map.IsWallTile(x + dx[d], y + dy[d]))
                {
                    adjacentToWall = true;
                    break;
                }
            }

            if (adjacentToWall)
                result[pos] = FloorTileType.Dark;
        }
    }

    // -------------------------------------------------------------------------
    // Pass 3a: Worn variation
    // -------------------------------------------------------------------------

    private static void ApplyWornVariation(Dictionary<(int X, int Y), FloorTileType> result, int seed)
    {
        // Medium-frequency noise (scale 0.22) → patches 4-5 tiles across — smaller than a typical room,
        // so worn areas read as a path through the room rather than covering it entirely.
        // Scale 0.10 was too low: a single noise peak could span an entire small room.
        // Seed offset is intentionally different from the Accent pass so the two patterns don't align.
        float offsetX = (seed * 0.53f + 317f) % 1000f;
        float offsetY = (seed * 0.89f + 431f) % 1000f;

        foreach (var (pos, type) in result.ToList())
        {
            if (type != FloorTileType.Standard) continue; // Dark tiles never overridden

            // Scale 0.22 → medium frequency. Threshold 0.72 ≈ 3-4% of standard tiles.
            float noise = SimplexNoise.Evaluate(
                pos.X * 0.22f + offsetX,
                pos.Y * 0.22f + offsetY);

            if (noise > 0.72f)
                result[pos] = FloorTileType.Worn;
        }
    }

    // -------------------------------------------------------------------------
    // Pass 3b: Noise variation
    // -------------------------------------------------------------------------

    private static void ApplyNoiseVariation(Dictionary<(int X, int Y), FloorTileType> result, int seed)
    {
        // Per-map offset derived from seed so each floor looks distinct.
        // The modulo keeps offsets in a sane range to avoid float precision issues.
        float offsetX = (seed * 0.37f) % 1000f;
        float offsetY = (seed * 0.73f) % 1000f;

        foreach (var (pos, type) in result.ToList())
        {
            if (type != FloorTileType.Standard) continue; // Dark tiles are never overridden

            // Scale of 0.25 → low-frequency blobs, not fine grain.
            // Threshold 0.6 → ~10% of standard tiles become Accent.
            float noise = SimplexNoise.Evaluate(
                pos.X * 0.25f + offsetX,
                pos.Y * 0.25f + offsetY);

            if (noise > 0.6f)
                result[pos] = FloorTileType.Accent;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Deterministic position hash for tile-level decisions.
    /// Same (x, y) always produces the same value — the floor looks stable across re-renders.
    /// </summary>
    private static int PositionHash(int x, int y)
        => Math.Abs((x * 7919 + y * 104729) & 0x7FFFFFFF);
}
