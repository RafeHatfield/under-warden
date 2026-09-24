using Godot;

namespace UnderWarden.Presentation.Map;

/// <summary>
/// Isometric coordinate renderer. Wraps the math from IsometricMapper exactly —
/// all results are identical to the original static calls.
///
/// Oryx iso tiles are 32×48 pixels. The diamond footprint occupies the lower half.
/// HalfTileHeight is TileHeight/4 = 12 (iso step height), not TileHeight/2.
///
/// Iso coordinate system:
///   screenX = (gridX - gridY) * HalfTileWidth   (16)
///   screenY = (gridX + gridY) * HalfTileHeight  (12)
/// </summary>
public sealed class IsometricRenderer : IMapRenderer
{
    public int TileWidth  => 32;
    public int TileHeight => 48;

    // HalfTileHeight = TileHeight/4 = 12 — this is the iso step height, not half the tile.
    // The original IsometricMapper uses TileHeight/4 here; preserve exactly.
    private int HalfTileWidth  => TileWidth  / 2;  // 16
    private int HalfTileHeight => TileHeight / 4;  // 12

    // Zoom values match the current hardcoded constants in PlayerCamera / Main.
    public float DefaultZoom => 4.0f;
    public float MinZoom     => 1.5f;
    public float MaxZoom     => 6.0f;

    /// <summary>Screen position of tile top-left corner.</summary>
    public Vector2 GridToScreen(int gridX, int gridY)
    {
        float screenX = (gridX - gridY) * HalfTileWidth;
        float screenY = (gridX + gridY) * HalfTileHeight;
        return new Vector2(screenX, screenY);
    }

    /// <summary>
    /// Screen position of tile diamond center — where entities stand.
    /// top-left + (HalfTileWidth, TileHeight * 0.5f)
    /// </summary>
    public Vector2 GridToScreenCenter(int gridX, int gridY)
    {
        var topLeft = GridToScreen(gridX, gridY);
        return new Vector2(topLeft.X + HalfTileWidth, topLeft.Y + TileHeight * 0.5f);
    }

    /// <summary>
    /// Convert screen position to nearest grid coordinate.
    /// Inverse of GridToScreen; used for tap-to-target.
    /// </summary>
    public (int gridX, int gridY) ScreenToGrid(Vector2 screenPos)
    {
        // Adjust for the visual diamond center within the 32×48 tile.
        // The diamond midpoint sits at (HalfTileWidth, TileHeight * 0.75f) from tile origin.
        float adjustedX = screenPos.X - HalfTileWidth;
        float adjustedY = screenPos.Y - TileHeight * 0.75f;

        float sum  = adjustedY / HalfTileHeight;   // gx + gy
        float diff = adjustedX / HalfTileWidth;    // gx - gy
        float gx = (sum + diff) / 2f;
        float gy = (sum - diff) / 2f;
        return ((int)Mathf.Round(gx), (int)Mathf.Round(gy));
    }

    /// <summary>Z-index for tiles. Even — tiles always sort behind entities at the same depth.</summary>
    public int GetTileSortOrder(int gridX, int gridY)
        => (gridX + gridY) * 2;

    /// <summary>Z-index for entities. Odd — always in front of tiles at the same depth.</summary>
    public int GetEntitySortOrder(int gridX, int gridY)
        => (gridX + gridY) * 2 + 1;
}
