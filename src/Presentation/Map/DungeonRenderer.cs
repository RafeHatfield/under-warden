using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;
using Godot;

using UnderWarden.Presentation;

namespace UnderWarden.Presentation.Map;

/// <summary>
/// Holds references to all tile sprite nodes created by DungeonRenderer.Render.
/// Used by UpdateVisibility to apply fog-of-war modulation each turn without
/// re-creating nodes. Key is grid position (X, Y).
/// </summary>
public sealed class TileLayer
{
    public Dictionary<(int X, int Y), Node2D> TileSprites { get; } = new();

    /// <summary>
    /// Base tile keys where the floor type is Dark (wall-adjacent shadow tiles).
    /// UpdateVisibility applies a slightly dimmed, cool-tinted modulate to these
    /// sprites when visible, giving rooms natural shadow depth near walls without
    /// requiring a separate tile asset.
    /// Only contains base tile keys (x, y) — never offset bones overlay keys.
    /// </summary>
    public HashSet<(int X, int Y)> DarkTileKeys { get; } = new();

    /// <summary>
    /// Door overlay sprites keyed by grid position (X, Y).
    /// Tracked separately from TileSprites so UpdateVisibility can apply FOV without
    /// colliding with the base tile key or the bones overlay offset key.
    /// Doors respect FOV: visible when seen, dimmed when explored, hidden when unknown.
    /// </summary>
    public Dictionary<(int X, int Y), Node2D> DoorOverlaySprites { get; } = new();

    /// <summary>
    /// Prop sprite nodes keyed by (PropIndex, CellOffset).
    /// PropIndex is the index of the prop in the props list passed to Render.
    /// CellOffset is 0 for single-tile props; 0..(FootprintW*FootprintH-1) for multi-tile.
    /// CellOffset -1 is reserved for the overlay tile of a 1x1 prop (e.g. brazier flame on base).
    /// UpdateVisibility modulates these via their stored grid position (GridX, GridY).
    /// </summary>
    public Dictionary<(int PropIndex, int CellOffset), (Node2D Sprite, int GridX, int GridY)> PropSprites { get; } = new();

    /// <summary>
    /// #212 — PROPS SIT AGAINST WALLS UNDER THE TOP BAND (overnight queue, 2026-09-13).
    /// A prop whose north neighbours are wall stands AGAINST that wall: its sprites are shifted
    /// north so its base line sits on the shared edge (the reveal's foot), and it draws over the
    /// face. The shift per prop index, in screen px, so the occluder pass can follow it.
    /// </summary>
    public Dictionary<int, float> PropShift { get; } = new();

    /// <summary>The wall cells whose CAP BAND is re-drawn over an against-wall prop's top, with
    /// the z it must draw at (the prop's own, added later so it wins the tie). Consumed by
    /// Tier1BoundaryWall when it lays the cap.</summary>
    public Dictionary<(int X, int Y), int> CapBandCells { get; } = new();

    /// <summary>
    /// Feature overlay sprites keyed by grid position (X, Y).
    /// Covers chests (closed/open), signposts, and murals placed by EntityPlacer.PlaceFloorFeatures.
    /// Tracked separately so UpdateVisibility can apply FOV and SwapFeatureSprite can swap
    /// chest textures on open without reconstructing the scene tree.
    /// Features respect FOV: visible when seen, dimmed when explored, hidden when unknown.
    /// </summary>
    public Dictionary<(int X, int Y), Sprite2D> FeatureOverlaySprites { get; } = new();

    /// <summary>
    /// Small key icon overlay sprites for locked chests, keyed by grid position (X, Y).
    /// Each locked chest gets a second Sprite2D at ~40% scale in the top-right corner
    /// to visually indicate a lock exists. The icon is removed when the chest is unlocked
    /// (ChestUnlockedEvent or when IsLocked becomes false).
    /// Visibility follows FeatureOverlaySprites exactly.
    /// </summary>
    public Dictionary<(int X, int Y), Sprite2D> LockKeyOverlaySprites { get; } = new();
}

/// <summary>
/// Renders a GameMap as tile sprites. Creates Sprite2D nodes for each
/// floor and wall tile under the TileMapLayer node.
///
/// Renders back-to-front (by grid row/column sum) for correct depth sorting.
/// Floor tiles get random variation seeded by position for visual interest.
///
/// Second pass: overlays stair sprites on top of floor tiles where TileKind.StairDown
/// is set. This only applies to dungeon-generated maps — scenario maps use CreateArena
/// and never set StairDown tile kinds.
///
/// Tile textures are sourced entirely from TileThemeConfig (loaded from tile_themes.yaml).
/// No tile paths are hardcoded here — the renderer is content-agnostic.
/// </summary>
public sealed class DungeonRenderer
{
    /// <summary>
    /// Render the entire GameMap as tile sprites under the given parent node.
    /// Pass 1: floor/wall tiles for every cell.
    /// Pass 2: stair overlays on top of StairDown/StairUp cells.
    /// Pass 3: bones decorations on ~2.5% of room floor tiles.
    ///
    /// Returns a TileLayer containing references to all base tile sprites so that
    /// UpdateVisibility can apply fog-of-war modulation each turn.
    ///
    /// If themeConfig is null, logs an error and returns an empty TileLayer without crashing.
    /// </summary>
    public static TileLayer Render(GameMap map, Node2D parent, IMapRenderer? renderer = null, TileThemeConfig? themeConfig = null, int seed = 0, IReadOnlyList<PlacedProp>? props = null, IReadOnlyList<Entity>? features = null, IReadOnlyDictionary<(int X, int Y), int>? lockedDoors = null)
    {
        if (themeConfig == null)
        {
            GD.PrintErr("[DungeonRenderer] No TileThemeConfig provided — dungeon floor will not render.");
            return new TileLayer();
        }

        // Use provided renderer, or fall back to TopDownRenderer as the active default.
        renderer ??= new TopDownRenderer();
        var tileLayer = new TileLayer();

        // Pre-compute floor tile types for the entire map.
        // FloorComposer runs the multi-pass pipeline: edge darkening + noise variation.
        // This is computed once here and referenced in Pass 1 and Pass 3.
        var floorMap = FloorComposer.Compose(map, seed);

        // Pre-compute prop footprint positions so Pass 1 can fall back to Standard floor
        // under blocking props. Worn and accent tiles create an unintended "pedestal" look when
        // visible behind a fountain, bookcase, or other furniture — the prop should sit on plain floor.
        // Dark (shadow) tiles are kept even under props: a bookshelf against a wall still casts shadow.
        var propFootprint = new HashSet<(int, int)>();
        if (props != null)
            foreach (var p in props)
                if (p.BlocksMovement)
                    for (int fx = p.X; fx < p.X + p.FootprintW; fx++)
                        for (int fy = p.Y; fy < p.Y + p.FootprintH; fy++)
                            propFootprint.Add((fx, fy));

        // Clear any existing tiles
        foreach (var child in parent.GetChildren())
        {
            child.SafeFree();
        }

        // Pass 1: floor and wall base tiles
        for (int gy = 0; gy < map.Height; gy++)
        {
            for (int gx = 0; gx < map.Width; gx++)
            {
                // IsWalkable returns false for prop-occupied tiles (blocking props register
                // cells as impassable for pathfinding). For rendering the FLOOR beneath a prop,
                // those cells must still render as floor so transparent prop pixels show floor
                // instead of the scene background. propFootprint already contains all blocking
                // prop positions computed before the tile loop.
                bool walkable = map.IsWalkable(gx, gy) || propFootprint.Contains((gx, gy));
                var screenPos = renderer.GridToScreen(gx, gy);

                var tileTheme = map.GetTileTheme(gx, gy);
                string themeName = ThemeToConfigName(tileTheme);

                string? tilePath;
                bool isDarkFloor = false;
                bool isAccentFloor = false;
                if (walkable)
                {
                    // Use FloorComposer variant to select the appropriate tile type.
                    // Dark tiles get programmatic shadow modulation in UpdateVisibility.
                    // Accent tiles use FlipH/FlipV mirroring for sprite variety (4 orientations
                    // from one asset) — better than a separate tile for dynamic light compatibility.
                    var tileType = floorMap.TryGetValue((gx, gy), out var ft) ? ft : FloorTileType.Standard;
                    // Suppress Worn and Accent under blocking props — keeps floor visually neutral
                    // so the prop doesn't appear to sit on a pedestal. Dark shadow is kept.
                    if (propFootprint.Contains((gx, gy)) && tileType is FloorTileType.Worn or FloorTileType.Accent)
                        tileType = FloorTileType.Standard;
                    isDarkFloor   = tileType == FloorTileType.Dark;
                    isAccentFloor = tileType == FloorTileType.Accent;
                    tilePath = tileType switch
                    {
                        FloorTileType.Dark   => themeConfig.GetFloorDark(themeName, gx, gy),
                        FloorTileType.Accent => themeConfig.GetFloorAccent(themeName, gx, gy),
                        FloorTileType.Worn   => themeConfig.GetFloorWorn(themeName, gx, gy),
                        _                    => themeConfig.GetFloorTile(themeName, gx, gy),
                    };
                    // Belt-and-suspenders fallback in case a variant returns null
                    tilePath ??= themeConfig.GetFloorTile(themeName, gx, gy);
                }
                else
                {
                    var (cardinal, diagonal) = ComputeWallMasks(map, gx, gy);

                    // Collapse 3-wall masks to plain horizontal/vertical edges.
                    // Masks 13/14 (one open cardinal E or W) → mask 12 (tile 187, vertical edge).
                    // Mask 11 (open to the SOUTH) → mask 3 (tile 184, horizontal edge).
                    // Tiles 194/195/196/197 in this set render as directional faces/T-junctions
                    // with the far side showing rock/stone texture — correct for a wall that
                    // has an external wall structure meeting it, but WRONG for plain room
                    // edges and corridor edges, where the far side is just more interior fill.
                    // Room and corridor walls look consistent when all edges use 184/187.
                    //
                    // ⚠ MASK 7 USED TO COLLAPSE TO 3 AS WELL, AND THAT WAS A §3 VIOLATION.
                    // Mask 7 has WALL to its south — it is a room's SOUTH wall, seen from the room
                    // to its north — so a face tile cut a reveal into the middle of a solid mass on
                    // 13 in-map cells per review scene. The rule now lives in the LOGIC layer, in
                    // WallMaskPolicy, where the test suite can assert it for all sixteen masks;
                    // this was presentation-only arithmetic and nothing could reach it.
                    int effectiveCardinal = WallMaskPolicy.Collapse(cardinal);

                    tilePath = themeConfig.GetWallTile(themeName, effectiveCardinal, diagonal, gx, gy);
                }

                if (tilePath == null)
                {
                    GD.PrintErr($"[DungeonRenderer] No tile path for {(walkable ? "floor" : "wall")} at ({gx},{gy}) theme='{themeName}'");
                    continue;
                }

                var texture = GD.Load<Texture2D>(tilePath);
                if (texture == null && walkable)
                {
                    // Variant tile missing (e.g. worn tile not yet imported by Godot editor).
                    // Fall back to standard floor so the tile is visible instead of a hole.
                    var fallbackPath = themeConfig.GetFloorTile(themeName, gx, gy);
                    if (fallbackPath != null) texture = GD.Load<Texture2D>(fallbackPath);
                    if (texture != null)
                        GD.PrintErr($"[DungeonRenderer] Variant tile missing, using fallback: {tilePath}");
                }
                if (texture == null)
                {
                    GD.PrintErr($"[DungeonRenderer] Missing tile texture: {tilePath}");
                    continue;
                }

                // Accent tiles get per-position flip flags — 4 orientations from one asset,
                // compatible with future dynamic lighting (modulate-based, not asset-based).
                int posHash = Math.Abs((gx * 7919 + gy * 104729) & 0x7FFFFFFF);
                var sprite = new Sprite2D
                {
                    Texture = texture,
                    Position = screenPos,
                    Centered = false, // Position is top-left of tile image
                    ZIndex = renderer.GetTileSortOrder(gx, gy),
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                    FlipH = isAccentFloor && (posHash & 1) != 0,
                    FlipV = isAccentFloor && (posHash & 2) != 0,
                };

                parent.AddChild(sprite);
                // Track in TileLayer so UpdateVisibility can modulate this sprite
                tileLayer.TileSprites[(gx, gy)] = sprite;
                if (isDarkFloor) tileLayer.DarkTileKeys.Add((gx, gy));
            }
        }

        // Pass 2: stair overlays — placed on top of the floor tile at the same screen position.
        // Only StairDown and StairUp get overlays; corridors/regular floors need nothing extra.
        // Skip silently if the texture file is missing — never crash.
        // Stair overlays are NOT added to tileLayer — they share visibility with their base tile
        // and are handled separately in UpdateVisibility via the stair overlay list.
        for (int gy = 0; gy < map.Height; gy++)
        {
            for (int gx = 0; gx < map.Width; gx++)
            {
                var kind = map.GetTileKind(gx, gy);
                var tileTheme = map.GetTileTheme(gx, gy);
                string themeName = ThemeToConfigName(tileTheme);

                string? overlayPath = kind switch
                {
                    TileKind.StairDown   => themeConfig.GetStairDown(themeName),
                    TileKind.StairUp     => themeConfig.GetStairUp(themeName),
                    TileKind.Door        => themeConfig.GetDoor(themeName),
                    TileKind.DoorOpen    => themeConfig.GetDoorOpen(themeName),
                    TileKind.LockedDoor  => themeConfig.GetDoorLocked(themeName),
                    _                    => null,
                };

                if (overlayPath == null) continue;

                var overlayTexture = ResourceLoader.Load<Texture2D>(overlayPath);
                if (overlayTexture == null)
                {
                    // Asset missing — skip silently rather than crashing.
                    // The floor tile still renders; the stair is just invisible.
                    GD.PrintErr($"[DungeonRenderer] Missing stair overlay texture: {overlayPath} — skipping.");
                    continue;
                }

                var screenPos = renderer.GridToScreen(gx, gy);

                // Doors in horizontal corridors (walls N+S, passage runs E-W) need 90° rotation.
                // The base sprite is oriented for a vertical corridor (walls E+W, passage N-S).
                // Rotation pivots around the tile center (Centered=true + center position) so the
                // sprite stays within its tile regardless of orientation.
                // DoorOpen uses the same orientation logic as closed Door.
                bool isDoorHorizontal = false;
                if (kind == TileKind.Door || kind == TileKind.DoorOpen || kind == TileKind.LockedDoor)
                {
                    // SecretDoors count as wall-like for orientation purposes — an unrevealed
                    // secret door adjacent to a revealed door must still orient the door sprite
                    // correctly (otherwise a door flanked by secret doors would render unrotated).
                    bool wallN = !map.InBounds(gx, gy - 1) || map.IsWallTile(gx, gy - 1);
                    bool wallS = !map.InBounds(gx, gy + 1) || map.IsWallTile(gx, gy + 1);
                    isDoorHorizontal = wallN && wallS;
                }

                var overlay = new Sprite2D
                {
                    Texture = overlayTexture,
                    Position = isDoorHorizontal ? renderer.GridToScreenCenter(gx, gy) : screenPos,
                    Centered = isDoorHorizontal,
                    RotationDegrees = isDoorHorizontal ? 90f : 0f,
                    // +1 above the floor tile (even) at this grid position — same band as entities, which is fine
                    ZIndex = renderer.GetTileSortOrder(gx, gy) + 1,
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                };

                parent.AddChild(overlay);

                // Stair overlays are always visible (player always knows where the exit is).
                // Door overlays (both closed, open, and locked) respect FOV — track for UpdateVisibility.
                if (kind == TileKind.Door || kind == TileKind.DoorOpen || kind == TileKind.LockedDoor)
                    tileLayer.DoorOverlaySprites[(gx, gy)] = overlay;

                // Locked doors: apply the lock color tint and add a key icon overlay.
                // The lock color comes from the lockedDoors lookup (populated by DungeonFloorBuilder).
                // Color tint distinguishes locked doors from locked chests in the same color family.
                if (kind == TileKind.LockedDoor && lockedDoors != null
                    && lockedDoors.TryGetValue((gx, gy), out int doorColorId))
                {
                    overlay.Modulate = GetLockColor(doorColorId);

                    // Small key icon overlay in the top-right corner — same pattern as locked chests.
                    var keyIconPath = themeConfig.GetTexturePath(5039);
                    var keyIconTex = GD.Load<Texture2D>(keyIconPath);
                    if (keyIconTex != null)
                    {
                        var keyIconPos = isDoorHorizontal
                            ? renderer.GridToScreenCenter(gx, gy) + new Vector2(4f, -10f)
                            : renderer.GridToScreen(gx, gy) + new Vector2(13f, 1f);
                        var keyIcon = new Sprite2D
                        {
                            Texture = keyIconTex,
                            Position = keyIconPos,
                            Centered = false,
                            Scale = new Vector2(0.4f, 0.4f),
                            ZIndex = renderer.GetTileSortOrder(gx, gy) + 2,
                            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                            Modulate = Colors.White,
                        };
                        parent.AddChild(keyIcon);
                        tileLayer.LockKeyOverlaySprites[(gx, gy)] = keyIcon;
                    }
                }
            }
        }

        // Pass 3: decorative details — bones on room floors (not corridors, not walls).
        // ~2.5% of room tiles get a bones overlay (controlled inside TileThemeConfig.GetBones).
        // Purely atmospheric, never gameplay-affecting.
        // Overlays are tracked in tileLayer with an offset key so UpdateVisibility auto-modulates them.
        for (int gy = 0; gy < map.Height; gy++)
        {
            for (int gx = 0; gx < map.Width; gx++)
            {
                if (!map.IsWalkable(gx, gy)) continue;
                var boneTheme = map.GetTileTheme(gx, gy);
                if (boneTheme == TileTheme.Dirt) continue; // Corridors don't get bones
                // Skip bones on Dark (wall-adjacent) tiles — shadows belong to the walls,
                // not decoration. Bones look odd crammed against wall edges anyway.
                if (floorMap.TryGetValue((gx, gy), out var boneFloorType) && boneFloorType == FloorTileType.Dark) continue;

                string themeName = ThemeToConfigName(boneTheme);
                string? bonesPath = themeConfig.GetBones(themeName, gx, gy);
                if (bonesPath == null) continue; // ~97.5% of tiles — no bones

                var bonesTexture = ResourceLoader.Load<Texture2D>(bonesPath);
                if (bonesTexture == null) continue; // Skip silently if asset missing

                var screenPos = renderer.GridToScreen(gx, gy);
                var overlay = new Sprite2D
                {
                    Texture = bonesTexture,
                    Position = screenPos,
                    Centered = false,
                    // Bones are floor decoration — stay in tile band, above plain floor tile
                    ZIndex = renderer.GetTileSortOrder(gx, gy) + 1,
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                    Modulate = new Color(1f, 1f, 1f, 0.7f), // slightly transparent — subtle
                };
                parent.AddChild(overlay);
                // Store under offset key (gx, gy + Height) — no collision with base tiles at (gx, gy).
                // UpdateVisibility iterates all TileSprites entries, so these get FOV modulation for free.
                tileLayer.TileSprites[(gx, gy + map.Height)] = overlay;
            }
        }

        // Pass 4: room props — furniture, overlays, and decorative scenery placed by RoomPropPlacer.
        // Props are tracked in TileLayer.PropSprites for per-turn FOV modulation.
        //
        // Non-blocking props (floor overlays: puddles, grates, moss) render at 0.7 alpha like
        // bones — present but subordinate to entity movement above them.
        //
        // Multi-tile props: TileLayout is a flat row-major list (FootprintW * FootprintH entries).
        //   Index i → grid offset dx=(i % FootprintW), dy=(i / FootprintW).
        //
        // Overlay tile: composite prop with two sprites at the same cell (e.g. brazier base + flame).
        //   Stored under CellOffset=-1 so it never collides with layout cell indices.
        if (props != null)
        {
            for (int propIdx = 0; propIdx < props.Count; propIdx++)
            {
                var prop = props[propIdx];
                if (prop.TileId == 0 && prop.TileLayout == null) continue; // no tile assigned — skip

                bool isOverlay = !prop.BlocksMovement;

                if (prop.TileLayout != null && (prop.FootprintW > 1 || prop.FootprintH > 1))
                {
                    // Multi-tile prop: render each cell in the layout grid
                    for (int cellIdx = 0; cellIdx < prop.TileLayout.Count; cellIdx++)
                    {
                        int tileId = prop.TileLayout[cellIdx];
                        if (tileId == 0) continue; // sparse layout gap — no sprite for this cell

                        int dx = cellIdx % prop.FootprintW;
                        int dy = cellIdx / prop.FootprintW;
                        int gx = prop.X + dx;
                        int gy = prop.Y + dy;

                        var sprite = CreatePropSprite(themeConfig, renderer, gx, gy, tileId, isOverlay,
                                                      onWallTop: prop.OnWallTop);
                        if (sprite == null) continue;

                        parent.AddChild(sprite);
                        tileLayer.PropSprites[(propIdx, cellIdx)] = (sprite, gx, gy);
                    }
                }
                else
                {
                    // 1x1 prop: single sprite at prop anchor. Pass FlipH for flippable props (e.g. chairs).
                    var sprite = CreatePropSprite(themeConfig, renderer, prop.X, prop.Y, prop.TileId,
                                              isOverlay, prop.FlipH, prop.OnWallTop);
                    if (sprite != null)
                    {
                        parent.AddChild(sprite);
                        tileLayer.PropSprites[(propIdx, 0)] = (sprite, prop.X, prop.Y);
                    }

                    // Optional overlay tile (e.g. brazier flame on top of base sprite)
                    if (prop.OverlayTileId.HasValue && prop.OverlayTileId.Value != 0)
                    {
                        var overlaySprite = CreatePropSprite(themeConfig, renderer, prop.X, prop.Y, prop.OverlayTileId.Value, isOverlay);
                        if (overlaySprite != null)
                        {
                            // Bump ZIndex +1 so the overlay renders above the base at the same cell
                            overlaySprite.ZIndex = renderer.GetTileSortOrder(prop.X, prop.Y) + 3;
                            parent.AddChild(overlaySprite);
                            tileLayer.PropSprites[(propIdx, -1)] = (overlaySprite, prop.X, prop.Y); // -1 = overlay slot
                        }
                    }
                }
            }
        }

        // ── #212: A PROP AGAINST A WALL SITS AT THE WALL'S FOOT, UNDER THE TOP BAND ──────────
        //
        // RULED-SHAPED by the cast-shadows walk (Rafe, 2026-09-13: "props sit against walls under
        // the top band"). Before this, a prop one cell south of a wall stood a full cell away from
        // the reveal — its base at its own cell's bottom edge, the wall's face beginning a cell
        // north — and read as placed in the room rather than against anything. The face's foot
        // IS the shared edge (§3: the reveal is the wall's south surface, rising from the cell
        // boundary), so a prop against the wall has its base there. The shift is measured off the
        // sprite's own bottom transparent rows, not typed. The wall's cap band is re-laid over the
        // prop's top by Tier1BoundaryWall (CapBandCells) so the top surface stays in front:
        // face < prop < cap band. Floor props only; a wall-top prop already sorts above its wall.
        if (props != null)
        {
            for (int propIdx = 0; propIdx < props.Count; propIdx++)
            {
                var prop = props[propIdx];
                if (prop.OnWallTop || !prop.BlocksMovement) continue;
                bool against = true;
                for (int dx = 0; dx < prop.FootprintW && against; dx++)
                    against = map.InBounds(prop.X + dx, prop.Y - 1) && map.IsWallTile(prop.X + dx, prop.Y - 1);
                if (!against) continue;

                // the base margin: the bottom-row cells' lowest opaque row, in screen px
                int tileH = renderer.TileHeight;
                float margin = float.MaxValue;
                for (int dx = 0; dx < prop.FootprintW; dx++)
                {
                    int cellIdx = (prop.FootprintH - 1) * prop.FootprintW + dx;
                    if (!tileLayer.PropSprites.TryGetValue((propIdx, cellIdx), out var c)
                        && !tileLayer.PropSprites.TryGetValue((propIdx, 0), out c)) continue;
                    if (c.Sprite is not Sprite2D sp || sp.Texture == null) continue;
                    var img = sp.Texture.GetImage();
                    int th = img.GetHeight(), tw = img.GetWidth(), last = -1;
                    for (int y = th - 1; y >= 0 && last < 0; y--)
                        for (int x = 0; x < tw; x++)
                            if (img.GetPixel(x, y).A > 0.01f) { last = y; break; }
                    if (last >= 0) margin = Mathf.Min(margin, (th - 1 - last) * (tileH / (float)th));
                }
                if (margin == float.MaxValue) margin = 0f;
                float shift = tileH - margin;
                foreach (var key in tileLayer.PropSprites.Keys)
                {
                    if (key.PropIndex != propIdx) continue;
                    var e = tileLayer.PropSprites[key];
                    e.Sprite.Position += new Vector2(0, -shift);
                }
                tileLayer.PropShift[propIdx] = shift;
                for (int dx = 0; dx < prop.FootprintW; dx++)
                    tileLayer.CapBandCells[(prop.X + dx, prop.Y - 1)] = renderer.GetTileSortOrder(prop.X, prop.Y) + 2;
            }
        }

        // Pass 5: interactive features — chests (closed/open), signposts, murals.
        // Rendered as Sprite2D overlays on top of the floor tile at the same cell.
        // Tracked in FeatureOverlaySprites so UpdateVisibility applies FOV and
        // SwapFeatureSprite can swap chest textures when the chest is opened.
        //
        // Chest and sign tile IDs come from tile_themes.yaml via themeConfig — never hardcoded.
        // MuralComponent.TileId stores the chosen variant at placement time.
        //
        // Locked chests: same chest_closed sprite with a color tint applied, plus a
        // small key icon overlay (tile 5039) in the top-right corner at ~40% scale.
        if (features != null)
        {
            foreach (var feature in features)
            {
                var featureTheme = ThemeToConfigName(map.GetTileTheme(feature.X, feature.Y));
                int tileId = ResolveFeaturedTileId(feature, themeConfig, featureTheme);
                if (tileId == 0) continue; // no tile assigned — unknown feature type

                var texPath = themeConfig.GetTexturePath(tileId);
                var tex = GD.Load<Texture2D>(texPath);
                if (tex == null)
                {
                    GD.PrintErr($"[DungeonRenderer] Missing feature texture: {texPath} (tileId={tileId})");
                    continue;
                }

                var screenPos = renderer.GridToScreen(feature.X, feature.Y);
                int featureZIndex = renderer.GetTileSortOrder(feature.X, feature.Y) + 2;

                // Apply lock color tint for locked chests (LockableComponent present and IsLocked).
                var lockable = feature.Get<LockableComponent>();
                bool isLocked = lockable != null && lockable.IsLocked;
                var chestTint = isLocked ? GetLockColor(lockable!.LockColorId) : Colors.White;

                var sprite = new Sprite2D
                {
                    Texture = tex,
                    Position = screenPos,
                    Centered = false,
                    // Features sit at the same Z-level as props (+2) so they appear above
                    // floor and bones overlays, but below entities.
                    ZIndex = featureZIndex,
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                    Modulate = chestTint,
                };
                parent.AddChild(sprite);
                tileLayer.FeatureOverlaySprites[(feature.X, feature.Y)] = sprite;

                // Locked chest: add a small key icon in the top-right corner as a visual indicator.
                // Scale 0.4 makes the 24px key icon appear as ~9px — recognisable but not dominant.
                if (isLocked)
                {
                    var keyIconPath = themeConfig.GetTexturePath(5039);
                    var keyIconTex = GD.Load<Texture2D>(keyIconPath);
                    if (keyIconTex != null)
                    {
                        // Position: top-right quadrant of the 24px tile.
                        // Offset (13, 1) puts the scaled icon in the top-right without clipping.
                        var keyIconPos = screenPos + new Vector2(13f, 1f);
                        var keyIcon = new Sprite2D
                        {
                            Texture = keyIconTex,
                            Position = keyIconPos,
                            Centered = false,
                            Scale = new Vector2(0.4f, 0.4f),
                            ZIndex = featureZIndex + 1,
                            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                            Modulate = Colors.White,
                        };
                        parent.AddChild(keyIcon);
                        tileLayer.LockKeyOverlaySprites[(feature.X, feature.Y)] = keyIcon;
                    }
                    else
                    {
                        GD.PrintErr($"[DungeonRenderer] Missing key icon texture: {keyIconPath} — locked chest will have no key overlay.");
                    }
                }
            }
        }

        return tileLayer;
    }

    /// <summary>
    /// Resolve the tile ID for a feature entity based on its component type.
    /// Returns 0 for unknown feature types — caller should skip those.
    ///
    /// Locked chests use the same tile as closed chests; the tint is applied separately.
    /// </summary>
    private static int ResolveFeaturedTileId(Entity feature, TileThemeConfig themeConfig, string theme)
    {
        var chest = feature.Get<ChestComponent>();
        if (chest != null)
        {
            if (chest.IsLooted) return themeConfig.GetChestEmpty(theme);
            if (chest.IsOpen)   return themeConfig.GetChestOpen(theme);
            var trapped = feature.Get<TrapPayloadComponent>();
            if (trapped != null) return themeConfig.GetChestTrapped(theme);
            return themeConfig.GetChestClosed(theme);
        }

        var sign = feature.Get<SignpostComponent>();
        if (sign != null) return themeConfig.GetSign(theme);

        var mural = feature.Get<MuralComponent>();
        if (mural != null) return mural.TileId;

        return 0;
    }

    /// <summary>
    /// Map a LockColorId (0–4) to a Color for chest tinting and key item modulation.
    /// Colors are saturated enough to read clearly on the 24px chest sprite.
    /// Fallback to White for unknown IDs — safe, looks like an unlocked chest.
    /// </summary>
    internal static Color GetLockColor(int colorId) => colorId switch
    {
        0 => new Color(0.88f, 0.31f, 0.31f),  // red
        1 => new Color(0.31f, 0.50f, 0.88f),  // blue
        2 => new Color(0.25f, 0.75f, 0.38f),  // green
        3 => new Color(0.83f, 0.63f, 0.13f),  // gold
        4 => new Color(0.56f, 0.31f, 0.75f),  // purple
        _ => Colors.White,
    };

    /// <summary>
    /// Create a single Sprite2D for one prop tile (or multi-tile cell).
    /// Returns null and logs an error if the texture is missing — never crashes.
    /// isOverlay=true renders at 0.7 alpha (floor overlay: puddles, grates, moss) to stay
    /// visually subordinate to entities moving over them.
    /// </summary>
    private static Sprite2D? CreatePropSprite(
        TileThemeConfig themeConfig, IMapRenderer renderer,
        int gx, int gy, int tileId, bool isOverlay, bool flipH = false, bool onWallTop = false)
    {
        var texturePath = themeConfig.GetTexturePath(tileId);
        var texture = GD.Load<Texture2D>(texturePath);
        if (texture == null)
        {
            GD.PrintErr($"[DungeonRenderer] Missing prop texture: {texturePath} (tileId={tileId})");
            return null;
        }

        var screenPos = renderer.GridToScreen(gx, gy);

        return new Sprite2D
        {
            Texture = texture,
            Position = screenPos,
            Centered = false,
            // Props sit above floor (+0) and bones (+1) but below entity layer.
            // +2 keeps them visually above floor decoration without competing with entities.
            //
            // ── #167: A WALL-TOP PROP SORTS ABOVE THE WALL IT STANDS ON ──────────────────────
            //
            // From directly overhead, a thing standing on a wall top is NEARER THE CAMERA than
            // the top is, so it must draw over it. A floor prop must keep doing the opposite —
            // it stands in the room and the wall in front of it occludes it — which is why this
            // is a per-prop fact and not a global bump. Measured before it existed: a prop on a
            // wall cell changed 98 device pixels where a floor prop changes about 900. The
            // renderer seated it and the wall drew straight over it.
            //
            // +5 clears the tallest thing at the cell. The wall's own stack is the base sprite at
            // GetTileSortOrder, its face child at +1 and its binding child at +1; the floor
            // overlay family reaches +4 (channel 1, occlusion 2, event 3, grit 4). Five is one
            // above the highest of those, and it stays below the entity layer, which is odd-
            // numbered by GetEntitySortOrder and therefore always in front.
            ZIndex = renderer.GetTileSortOrder(gx, gy) + (onWallTop ? 5 : 2),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            // Non-blocking overlays (puddles, grates) are 0.7 alpha like bones — subtle atmosphere
            Modulate = isOverlay ? new Color(1f, 1f, 1f, 0.7f) : Colors.White,
            FlipH = flipH,
        };
    }

    /// <summary>
    /// Apply fog-of-war visibility to all tile sprites based on current map state.
    /// Call after each turn completes (both player and monster turns resolve).
    ///
    /// Three states:
    ///   Visible     — full brightness; Dark floor tiles get a shadowed modulate
    ///   Explored    — dimmed blue-grey (previously seen, currently not in FOV)
    ///   Unexplored  — hidden entirely
    ///
    /// Dark floor tiles (wall-adjacent) stay slightly dimmed even when visible —
    /// the shadow modulate is applied via programmatic color rather than a separate asset.
    ///
    /// Bones overlays (offset key y >= map.Height) preserve their 0.7 alpha at all
    /// visible/explored states so they remain subtle rather than fully opaque.
    /// </summary>
    public static void UpdateVisibility(TileLayer layer, GameMap map)
    {
        foreach (var ((x, y), sprite) in layer.TileSprites)
        {
            // Bones overlays are stored under offset key (gx, gy + map.Height) to avoid
            // colliding with base tile keys. Resolve the actual grid position before lookup.
            bool isBoneOverlay = y >= map.Height;
            int realX = x;
            int realY = isBoneOverlay ? y - map.Height : y;

            if (map.IsVisible(realX, realY))
            {
                sprite.Visible = true;
                if (isBoneOverlay)
                    sprite.Modulate = new Color(1f, 1f, 1f, 0.7f);        // bones: restore subtle alpha
                else if (layer.DarkTileKeys.Contains((x, y)))
                    sprite.Modulate = DarkFloorModulate;                   // wall-shadow darkening
                else
                    sprite.Modulate = Colors.White;
            }
            else if (map.IsExplored(realX, realY))
            {
                sprite.Visible = true;
                // Bones overlays keep their 0.7 alpha in explored state too
                sprite.Modulate = isBoneOverlay
                    ? new Color(0.4f, 0.4f, 0.5f, 0.7f)
                    : new Color(0.4f, 0.4f, 0.5f);
            }
            else
            {
                sprite.Visible = false; // never seen — hidden
            }
        }

        // Door overlay sprites — respect FOV like base tiles.
        // Doors are full opacity when visible, dim when explored, hidden when unknown.
        foreach (var ((x, y), sprite) in layer.DoorOverlaySprites)
        {
            if (map.IsVisible(x, y))
            {
                sprite.Visible = true;
                sprite.Modulate = Colors.White;
            }
            else if (map.IsExplored(x, y))
            {
                sprite.Visible = true;
                sprite.Modulate = new Color(0.4f, 0.4f, 0.5f);
            }
            else
            {
                sprite.Visible = false;
            }
        }

        // Prop sprites — FOV modulation by stored grid position.
        // isOverlay is re-derived from Modulate.A because non-blocking props have 0.7 alpha
        // set at creation time; blocking props (furniture) use full white.
        foreach (var ((propIdx, cellOffset), (sprite, gx, gy)) in layer.PropSprites)
        {
            bool isPropOverlay = sprite.Modulate.A < 1f; // non-blocking overlays always have A=0.7

            if (map.IsVisible(gx, gy))
            {
                sprite.Visible = true;
                sprite.Modulate = isPropOverlay
                    ? new Color(1f, 1f, 1f, 0.7f)
                    : Colors.White;
            }
            else if (map.IsExplored(gx, gy))
            {
                sprite.Visible = true;
                // Preserve alpha for overlays; solid props get the same explored tint as tiles
                sprite.Modulate = isPropOverlay
                    ? new Color(0.4f, 0.4f, 0.5f, 0.7f)
                    : new Color(0.4f, 0.4f, 0.5f);
            }
            else
            {
                sprite.Visible = false;
            }
        }

        // Feature overlay sprites — full opacity visible, dimmed explored, hidden unknown.
        // Same FOV rules as door overlays: solid props that the player can interact with.
        // NOTE: For locked chests, the lock tint is set at creation time and re-applied after
        // this call by RefreshLockedChestTints (in Main.cs), which has access to game state.
        // UpdateVisibility sets White for visible tiles as a safe baseline; RefreshLockedChestTints
        // then overrides White with the lock tint for any chest that is still locked and visible.
        foreach (var ((x, y), sprite) in layer.FeatureOverlaySprites)
        {
            if (map.IsVisible(x, y))
            {
                sprite.Visible = true;
                sprite.Modulate = Colors.White; // baseline; lock tint re-applied by caller if needed
            }
            else if (map.IsExplored(x, y))
            {
                sprite.Visible = true;
                sprite.Modulate = new Color(0.4f, 0.4f, 0.5f);
            }
            else
            {
                sprite.Visible = false;
            }
        }

        // Lock key icon overlay sprites — same FOV rules as feature overlays.
        // Hidden when the tile is explored (icon also dims) or unseen.
        foreach (var ((x, y), sprite) in layer.LockKeyOverlaySprites)
        {
            if (map.IsVisible(x, y))
            {
                sprite.Visible = true;
                sprite.Modulate = Colors.White;
            }
            else if (map.IsExplored(x, y))
            {
                sprite.Visible = true;
                sprite.Modulate = new Color(0.4f, 0.4f, 0.5f);
            }
            else
            {
                sprite.Visible = false;
            }
        }
    }

    // Programmatic shadow modulate for wall-adjacent Dark floor tiles when visible.
    // Very subtle dimming (86%) with a whisper of cool shift — just enough to read as
    // shadow at the wall edge without looking like a deliberate floor pattern.
    private static readonly Color DarkFloorModulate = new(0.92f, 0.92f, 0.95f);

    /// <summary>
    /// Map the TileTheme enum value to the YAML config theme key.
    ///
    /// All themes currently map to "sandstone" — the TMX-verified tile set from Phase 0.
    /// Additional themes (crypt, moss, dirt) will diverge once their tile IDs are verified
    /// and added to tile_themes.yaml in a later phase.
    /// </summary>
    internal static string ThemeToConfigName(TileTheme theme) => theme switch
    {
        TileTheme.Grey  => "sandstone",
        TileTheme.Crypt => "sandstone", // Fallback until crypt tile IDs are verified
        TileTheme.Moss  => "sandstone", // Fallback until moss tile IDs are verified
        TileTheme.Dirt  => "sandstone", // Fallback until dirt theme tile IDs are verified
        _               => "sandstone",
    };

    /// <summary>
    /// Compute the cardinal and diagonal wall masks for autotile selection at (x, y).
    ///
    /// Cardinal mask (0–15): each bit encodes whether the corresponding cardinal
    /// neighbor is a wall tile. Uses IsWallTile (not IsWalkable) so that prop cells
    /// — which are non-walkable but are not wall geometry — are treated as open space
    /// and don't corrupt the autotile bitmask for adjacent real walls.
    /// Out-of-bounds returns false from IsWallTile, so map borders do NOT count as
    /// walls for autotile purposes (edge tiles will use the non-wall fallback).
    ///   bit3 (8) = North (y-1)
    ///   bit2 (4) = South (y+1)
    ///   bit1 (2) = East  (x+1)
    ///   bit0 (1) = West  (x-1)
    ///
    /// Diagonal mask (0–15): only computed when cardinalMask == 15 (all four cardinal
    /// neighbors are walls). Each bit encodes whether a diagonal neighbor is NOT a wall
    /// (i.e. open space — floor or prop). A diagonal non-wall means this wall tile is
    /// an outer corner facing that diagonal direction:
    ///   bit3 (8) = NE diagonal is open
    ///   bit2 (4) = NW diagonal is open
    ///   bit1 (2) = SE diagonal is open
    ///   bit0 (1) = SW diagonal is open
    /// </summary>
    private static (int Cardinal, int Diagonal) ComputeWallMasks(GameMap map, int x, int y)
    {
        int cardinal = 0;
        if (map.IsWallTile(x,     y - 1)) cardinal |= 8; // North
        if (map.IsWallTile(x,     y + 1)) cardinal |= 4; // South
        if (map.IsWallTile(x + 1, y    )) cardinal |= 2; // East
        if (map.IsWallTile(x - 1, y    )) cardinal |= 1; // West

        // Only check diagonals when all cardinal neighbors are walls.
        // If any cardinal is floor, this can't be an outer corner — skip diagonal work.
        int diagonal = 0;
        if (cardinal == 15)
        {
            if (!map.IsWallTile(x + 1, y - 1)) diagonal |= 8; // NE is open
            if (!map.IsWallTile(x - 1, y - 1)) diagonal |= 4; // NW is open
            if (!map.IsWallTile(x + 1, y + 1)) diagonal |= 2; // SE is open
            if (!map.IsWallTile(x - 1, y + 1)) diagonal |= 1; // SW is open
        }

        return (cardinal, diagonal);
    }
}
