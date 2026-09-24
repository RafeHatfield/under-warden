using Godot;

namespace UnderWarden.Presentation.Map;

/// <summary>
/// Top-down 2D coordinate renderer. Grid coordinates map directly to screen pixels:
/// GridToScreen(x, y) = (x*TileWidth, y*TileHeight). Z-order is row-major.
///
/// TILE SIZE IS A PARAMETER, NOT A CONSTANT. ART-BIBLE-v0 §4.3 marks canvas and tile sizes
/// PLACEHOLDER — "native canvas per layer, and the integer scale factor to logical pixels, are
/// derived at Phase 5 against the reference device". A hard-coded 24 asserted a derived value
/// that does not exist yet, and tools/tier0_harness/README.md already carried it as a known
/// limit: the harness could declare tile.size in config and the renderer would draw on a 24px
/// grid regardless, so a capture could disagree with its own manifest without saying so.
///
/// The defaults reproduce the previous hard-coded behaviour EXACTLY — 24px tiles, zoom
/// 3.0/1.5/6.0 — so every existing call site is unchanged by construction. 24 was never a
/// derived value either; it is the Oryx 16bf world tile size inherited from the closed track.
///
/// Zoom scales with the tile so the on-screen tile pitch is what the caller declared:
/// MinZoom = scale/2 and MaxZoom = scale*2, which yields exactly 1.5 and 6.0 at the default
/// scale of 3.0. At 24px x3 that is 72 on-screen px per tile; at 32px x2 it is 64, or ~11.7
/// tiles across the reference device's 750px width.
/// </summary>
public sealed class TopDownRenderer : IMapRenderer
{
    /// <summary>The previous hard-coded tile size. NOT a derived value (§4.3 is PLACEHOLDER) —
    /// it is the Oryx 16bf world tile size inherited from the closed track.</summary>
    public const int DefaultTileSize = 24;

    /// <summary>The previous hard-coded DefaultZoom. Calibrated for 24px tiles on a 720x1280
    /// viewport: ~10 tiles wide x ~14 tall, the Shattered Pixel Dungeon density target.</summary>
    public const float DefaultScale = 3.0f;

    private readonly int _tileSize;
    private readonly float _scale;

    /// <param name="tileSize">Native tile size in pixels. Defaults to the previous constant.</param>
    /// <param name="scale">Integer scale to logical pixels; becomes DefaultZoom.</param>
    public TopDownRenderer(int tileSize = DefaultTileSize, float scale = DefaultScale)
    {
        if (tileSize <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(tileSize), tileSize,
                "Tile size must be positive.");
        if (scale <= 0f)
            throw new System.ArgumentOutOfRangeException(nameof(scale), scale,
                "Scale must be positive.");
        _tileSize = tileSize;
        _scale = scale;
    }

    public int TileWidth  => _tileSize;
    public int TileHeight => _tileSize;

    // Zoom tracks the tile so on-screen pitch is the caller's declared scale, and the readable
    // range around it is preserved rather than re-tuned per tile size. At the defaults these
    // evaluate to exactly the previous 3.0 / 1.5 / 6.0.
    public float DefaultZoom => _scale;
    public float MinZoom     => _scale * 0.5f;
    public float MaxZoom     => _scale * 2.0f;

    /// <summary>Screen position of tile top-left corner. Simple grid multiplication.</summary>
    public Vector2 GridToScreen(int gridX, int gridY)
        => new Vector2(gridX * TileWidth, gridY * TileHeight);

    /// <summary>Screen position of tile center — offset by half tile size in each axis.</summary>
    public Vector2 GridToScreenCenter(int gridX, int gridY)
    {
        var topLeft = GridToScreen(gridX, gridY);
        return new Vector2(topLeft.X + TileWidth / 2f, topLeft.Y + TileHeight / 2f);
    }

    /// <summary>
    /// Convert screen position to nearest grid coordinate.
    /// Inverse of GridToScreen: divide by tile size and round.
    /// ScreenToGrid(GridToScreenCenter(x, y)) == (x, y) for all valid positions.
    /// </summary>
    public (int gridX, int gridY) ScreenToGrid(Vector2 screenPos)
    {
        int gridX = (int)Mathf.Round((screenPos.X - TileWidth  / 2f) / TileWidth);
        int gridY = (int)Mathf.Round((screenPos.Y - TileHeight / 2f) / TileHeight);
        return (gridX, gridY);
    }

    /// <summary>Z-index for tiles. Row-major — higher Y sorts in front. Even values.</summary>
    public int GetTileSortOrder(int gridX, int gridY)
        => gridY * 2;

    /// <summary>Z-index for entities. Odd — always in front of tiles at the same row.</summary>
    public int GetEntitySortOrder(int gridX, int gridY)
        => gridY * 2 + 1;
}
