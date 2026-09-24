using YamlDotNet.Serialization;

namespace UnderWarden.Logic.Content;

public enum PropPlacement
{
    WallAdjacent,
    Center,
    Corner,
    FreeStanding,
    FloorOverlay,
    /// <summary>
    /// Place adjacent (cardinal) to any already-placed blocking prop.
    /// Chairs next to tables, stools next to fireplaces, etc.
    /// Falls back to FreeStanding when no prior props have been placed.
    /// </summary>
    PropAdjacent,
}

/// <summary>
/// Deserialized prop definition from config/props.yaml.
/// Props are purely presentational/layout data — they describe visual furniture and
/// obstacles placed in rooms. No game mechanics live here; those belong in the ECS layer.
/// </summary>
public sealed class PropDefinition
{
    [YamlMember(Alias = "tile_ids")]
    public List<int> TileIds { get; set; } = new();

    [YamlMember(Alias = "footprint")]
    public List<int> Footprint { get; set; } = new() { 1, 1 };

    [YamlMember(Alias = "blocks_movement")]
    public bool BlocksMovement { get; set; } = true;

    [YamlMember(Alias = "placement")]
    public string PlacementRaw { get; set; } = "free_standing";

    [YamlMember(Alias = "tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Optional: one or more tile layout variants for multi-tile props.
    /// Each inner list has FootprintW * FootprintH tile IDs in row-major order.
    /// When non-null, TileIds is ignored.
    /// </summary>
    [YamlMember(Alias = "tile_layouts")]
    public List<List<int>>? TileLayouts { get; set; }

    /// <summary>
    /// Optional: a second tile ID rendered on top of TileIds[0] at the same cell.
    /// Used for composite props like brazier (bowl + flame).
    /// </summary>
    [YamlMember(Alias = "overlay_tile_id")]
    public int? OverlayTileId { get; set; }

    public int FootprintW => Footprint.Count > 0 ? Footprint[0] : 1;
    public int FootprintH => Footprint.Count > 1 ? Footprint[1] : 1;

    public bool IsMultiTile => FootprintW > 1 || FootprintH > 1;

    public PropPlacement Placement => PlacementRaw switch
    {
        "wall_adjacent"  => PropPlacement.WallAdjacent,
        "center"         => PropPlacement.Center,
        "corner"         => PropPlacement.Corner,
        "free_standing"  => PropPlacement.FreeStanding,
        "floor_overlay"  => PropPlacement.FloorOverlay,
        "prop_adjacent"  => PropPlacement.PropAdjacent,
        _                => PropPlacement.FreeStanding,
    };
}

/// <summary>
/// Root YAML structure for config/props.yaml.
/// </summary>
public sealed class PropsFile
{
    [YamlMember(Alias = "props")]
    public Dictionary<string, PropDefinition> Props { get; set; } = new();
}
