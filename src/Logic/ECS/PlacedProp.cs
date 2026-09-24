namespace UnderWarden.Logic.ECS;

/// <summary>
/// A prop that has been placed in a generated dungeon room.
/// Immutable snapshot: position and display info resolved at generation time.
/// </summary>
public sealed record PlacedProp(
    string PropId,        // References props.yaml definition (e.g. "barrel", "bookshelf")
    int X,                // Grid position (top-left of footprint)
    int Y,
    int FootprintW,       // Tile footprint width (1 for 1x1, 3 for 3x1, etc.)
    int FootprintH,       // Tile footprint height (1 for 1x1, 3 for 1x3, etc.)
    bool BlocksMovement,  // True for furniture; false for floor overlays (puddles, grates)
    int TileId,           // Anchor tile ID — used for 1x1 props and as the [0] tile for multi-tile
    int? OverlayTileId = null,              // Second tile rendered on top at same cell (e.g. brazier flame)
    IReadOnlyList<int>? TileLayout = null,  // For multi-tile props: flat row-major list of tile IDs
                                            // (FootprintW * FootprintH entries). Null = use TileId only.
    bool FlipH = false,   // Mirror the sprite horizontally. Applied to 1x1 props only (flippable tag).

    // ── #167: THIS PROP STANDS ON A WALL TOP, NOT ON THE FLOOR ───────────────────────────────
    // "The prop/overlay pass gives wall tops world-placed OBJECTS standing on them — a brazier,
    // a bundle, a driven post, salvage — which are objects rather than tile incident, so §8.3.1
    // does not reach them."
    //
    // It changes two things and nothing else. The cell is WALL rather than floor, so the scene
    // builder's floor test inverts; and the sprite must sort ABOVE the wall and its cap, because
    // from directly overhead a thing standing on a wall top is nearer the camera than the top is.
    // A floor prop keeps sorting BELOW the wall in front of it, which is why this is a per-prop
    // fact and not a global z bump.
    bool OnWallTop = false,
    // ── CAST SHADOWS (bible §6.3 receive-light; §12.1a occlusion) ──────────────────────────
    // The occluder is shaped from the FOOTPRINT, never the sprite: "box" is the oblique base
    // parallelogram of §3.2, "round" the plan circle of the round exception. Presentation reads
    // it; nothing in the logic layer acts on it.
    string Footprint = "box",
    // A prop that EMITS. The orc fire is the game's one tended light (§5.4, B-PROP-003): the
    // sprite receives and the engine emits — a painted glow is §6.3's outlawed rim by another
    // name (#205). Null for every other prop.
    PropLight? Light = null
);

/// <summary>A prop's own light: colour as hex, energy, reach in tiles. Values from the scene
/// spec or the props manifest; PLACEHOLDER until Rafe rules them at the walk.</summary>
public sealed record PropLight(string Color, float Energy, float RadiusTiles);
