using Godot;

namespace UnderWarden.Presentation.Map;

/// <summary>
/// Converts between grid coordinates and screen positions.
/// Two implementations: IsometricRenderer and TopDownRenderer.
/// Injected into all presentation-layer consumers.
///
/// The interface is stateless — all methods are pure coordinate functions.
/// This means IMapRenderer instances are safe to share and the architecture
/// supports per-floor mode switching without structural changes.
/// </summary>
public interface IMapRenderer
{
    /// <summary>Screen position of tile top-left corner.</summary>
    Vector2 GridToScreen(int gridX, int gridY);

    /// <summary>Screen position of tile center (for entity/item placement).</summary>
    Vector2 GridToScreenCenter(int gridX, int gridY);

    /// <summary>Convert screen position to nearest grid coordinate.</summary>
    (int gridX, int gridY) ScreenToGrid(Vector2 screenPos);

    /// <summary>Z-index for floor/wall tiles at this position.</summary>
    int GetTileSortOrder(int gridX, int gridY);

    /// <summary>Z-index for entities at this position (always above tiles).</summary>
    int GetEntitySortOrder(int gridX, int gridY);

    /// <summary>Tile image width in pixels.</summary>
    int TileWidth { get; }

    /// <summary>Tile image height in pixels.</summary>
    int TileHeight { get; }

    /// <summary>Default camera zoom for this renderer mode.</summary>
    float DefaultZoom { get; }

    /// <summary>Minimum camera zoom for this renderer mode.</summary>
    float MinZoom { get; }

    /// <summary>Maximum camera zoom for this renderer mode.</summary>
    float MaxZoom { get; }
}
