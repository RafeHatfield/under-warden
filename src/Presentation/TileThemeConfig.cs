using Godot;

namespace UnderWarden.Presentation;

/// <summary>
/// Data class representing the tile theme configuration loaded from config/tile_themes.yaml.
///
/// Maps theme names to Oryx 16bf world tile IDs (24x24px). Provides deterministic
/// tile selection methods that DungeonRenderer calls instead of its old hardcoded
/// switch statements.
///
/// Tile variation is always deterministic by position — same (x, y) always resolves
/// to the same tile. This keeps the dungeon visually stable across re-renders.
///
/// Note: This covers dungeon tiles only (floors, walls, stairs, decorations).
/// Entity and item sprites belong to TilesetConfig, not here.
/// </summary>
public sealed class TileThemeConfig
{
    /// <summary>
    /// res:// root path for world tile images.
    /// e.g. "res://src/Presentation/assets/sprites_16bf/world_24x24"
    /// </summary>
    public string TileRoot { get; set; } = "";

    /// <summary>
    /// Filename template with {id} placeholder.
    /// e.g. "oryx_16bit_fantasy_world_{id}.png"
    /// </summary>
    public string TilePattern { get; set; } = "";

    /// <summary>
    /// Name of the fallback theme to use when a requested theme is not defined.
    /// </summary>
    public string DefaultTheme { get; set; } = "sandstone";

    /// <summary>
    /// Map of theme name → per-role tile ID lists.
    /// Roles: floor_primary, floor_accent, wall_autotile (bitmask dict),
    ///        stair_down, stair_up, bones.
    /// </summary>
    public Dictionary<string, TileThemeData> Themes { get; set; } = new();

    // -------------------------------------------------------------------------
    // Path resolution
    // -------------------------------------------------------------------------

    /// <summary>
    /// Convert a tile ID integer to a full res:// texture path.
    /// e.g. 1091 → "res://.../oryx_16bit_fantasy_world_1091.png"
    /// </summary>
    public string GetTexturePath(int tileId)
        => $"{TileRoot}/{TilePattern.Replace("{id}", tileId.ToString())}";

    // -------------------------------------------------------------------------
    // Theme-aware tile selection — deterministic by position
    // -------------------------------------------------------------------------

    /// <summary>
    /// Return a floor texture path for the given theme and position.
    /// 85% primary, 15% accent (random accent chosen deterministically).
    /// Falls back to default_theme if the requested theme is missing.
    /// Returns null if no floor tiles are configured (logs a warning).
    /// </summary>
    public string? GetFloorTile(string theme, int x, int y)
    {
        var data = ResolveTheme(theme);
        if (data == null) return null;

        if (data.FloorPrimary.Count == 0)
        {
            GD.PrintErr($"[TileThemeConfig] Theme '{theme}' has no floor_primary tiles.");
            return null;
        }

        int hash = PositionHash(x, y);
        bool useAccent = data.FloorAccent.Count > 0 && (hash % 20) < 3; // 15%

        int tileId = useAccent
            ? data.FloorAccent[hash % data.FloorAccent.Count]
            : data.FloorPrimary[hash % data.FloorPrimary.Count];

        return GetTexturePath(tileId);
    }

    /// <summary>
    /// Return a dark floor tile path for wall-adjacent edge shadowing.
    /// Falls back to GetFloorTile if FloorDark is empty.
    /// </summary>
    public string? GetFloorDark(string theme, int x, int y)
    {
        var data = ResolveTheme(theme);
        if (data == null) return null;
        if (data.FloorDark.Count == 0) return GetFloorTile(theme, x, y);
        int hash = PositionHash(x, y);
        int tileId = data.FloorDark[hash % data.FloorDark.Count];
        return GetTexturePath(tileId);
    }

    /// <summary>
    /// Return an accent floor tile path for noise-driven variation clusters.
    /// Falls back to GetFloorTile if FloorAccent is empty.
    /// </summary>
    public string? GetFloorAccent(string theme, int x, int y)
    {
        var data = ResolveTheme(theme);
        if (data == null) return null;
        if (data.FloorAccent.Count == 0) return GetFloorTile(theme, x, y);
        int hash = PositionHash(x, y);
        int tileId = data.FloorAccent[hash % data.FloorAccent.Count];
        return GetTexturePath(tileId);
    }

    /// <summary>
    /// Return a worn floor tile path for high-traffic path appearance.
    /// Falls back to GetFloorTile if FloorWorn is empty.
    /// </summary>
    public string? GetFloorWorn(string theme, int x, int y)
    {
        var data = ResolveTheme(theme);
        if (data == null) return null;
        if (data.FloorWorn.Count == 0) return GetFloorTile(theme, x, y);
        int hash = PositionHash(x, y);
        int tileId = data.FloorWorn[hash % data.FloorWorn.Count];
        return GetTexturePath(tileId);
    }

    /// <summary>
    /// Return a wall texture path for the given theme using the hybrid cardinal+diagonal
    /// autotile algorithm.
    ///
    /// Algorithm:
    ///   1. cardinalMask 0–14: look up WallAutotile[cardinalMask] directly.
    ///   2. cardinalMask == 15 AND diagonalFloorMask > 0: check diagonal bits for outer corners.
    ///      The diagonal mask encodes which diagonal neighbors are floor (walkable):
    ///        bit3(8) = NE diagonal is floor → this wall is SW outer corner
    ///        bit2(4) = NW diagonal is floor → this wall is SE outer corner
    ///        bit1(2) = SE diagonal is floor → this wall is NW outer corner
    ///        bit0(1) = SW diagonal is floor → this wall is NE outer corner
    ///      Priority when multiple bits set: NW > NE > SW > SE.
    ///   3. cardinalMask == 15 AND diagonalFloorMask == 0: interior fill.
    ///
    /// Falls back gracefully if WallDiagonal is empty or a key is missing.
    /// Falls back to default_theme if the theme itself is missing.
    /// Returns null only if the theme AND default are both unconfigured.
    /// </summary>
    public string? GetWallTile(string theme, int cardinalMask, int diagonalFloorMask,
                               int x = 0, int y = 0)
    {
        var data = ResolveTheme(theme);
        if (data == null) return null;

        if (data.WallAutotile.Count == 0)
        {
            GD.PrintErr($"[TileThemeConfig] Theme '{theme}' has no wall_autotile entries.");
            return null;
        }

        // For cardinal mask < 15, the autotile table is authoritative.
        if (cardinalMask < 15)
        {
            if (!data.WallAutotile.TryGetValue(cardinalMask, out var variants) || variants.Count == 0)
            {
                // Missing entry — fall back to interior fill (mask 15).
                if (!data.WallAutotile.TryGetValue(15, out variants) || variants.Count == 0)
                {
                    GD.PrintErr($"[TileThemeConfig] Theme '{theme}' missing bitmask {cardinalMask} and fallback 15.");
                    return null;
                }
            }
            return GetTexturePath(PickVariant(variants, x, y));
        }

        // cardinalMask == 15: all four cardinal neighbors are walls.
        // Check diagonal floor bits to determine if this is an outer corner or true interior.
        if (diagonalFloorMask > 0 && data.WallDiagonal.Count > 0)
        {
            // Priority: NW outer corner > NE outer corner > SW outer corner > SE outer corner.
            // A diagonal floor in direction D means THIS tile is the outer corner facing D.
            // bit1(2) = SE diagonal is floor → this wall is NW outer corner
            if ((diagonalFloorMask & 2) != 0 && data.WallDiagonal.TryGetValue("corner_outer_nw", out var nwIds) && nwIds.Count > 0)
                return GetTexturePath(PickVariant(nwIds, x, y));
            // bit0(1) = SW diagonal is floor → this wall is NE outer corner
            if ((diagonalFloorMask & 1) != 0 && data.WallDiagonal.TryGetValue("corner_outer_ne", out var neIds) && neIds.Count > 0)
                return GetTexturePath(PickVariant(neIds, x, y));
            // bit3(8) = NE diagonal is floor → this wall is SW outer corner
            if ((diagonalFloorMask & 8) != 0 && data.WallDiagonal.TryGetValue("corner_outer_sw", out var swIds) && swIds.Count > 0)
                return GetTexturePath(PickVariant(swIds, x, y));
            // bit2(4) = NW diagonal is floor → this wall is SE outer corner
            if ((diagonalFloorMask & 4) != 0 && data.WallDiagonal.TryGetValue("corner_outer_se", out var seIds) && seIds.Count > 0)
                return GetTexturePath(PickVariant(seIds, x, y));
        }

        // No diagonal floor, or WallDiagonal not configured: interior fill.
        //
        // THIS IS THE ONE THAT MATTERS. In any ordinary map interior_fill is the overwhelming
        // majority of wall cells - 267 of ~300 in the tier-0 review corridor - so a scalar here
        // stamps one tile across nearly the whole solid mass however many variants the autotile
        // masks declare. The blind critic measured exactly that and culled on it: "the solid
        // field is one 32px tile stamped ~150 times with no variation (median tile-to-mean
        // correlation 0.94)". Making the masks list-valued and leaving this scalar fixed the
        // visible 6% and left the invisible 94% alone.
        if (data.WallDiagonal.TryGetValue("interior_fill", out var fillIds) && fillIds.Count > 0)
            return GetTexturePath(PickVariant(fillIds, x, y));

        // Final fallback: autotile mask 15 entry.
        if (data.WallAutotile.TryGetValue(15, out var fillVariants) && fillVariants.Count > 0)
            return GetTexturePath(PickVariant(fillVariants, x, y));

        GD.PrintErr($"[TileThemeConfig] Theme '{theme}' has no interior_fill or mask-15 fallback.");
        return null;
    }

    /// <summary>
    /// Backwards-compatible wall tile lookup using only the 4-bit cardinal bitmask.
    /// Delegates to GetWallTile with diagonalFloorMask=0 (no diagonal discrimination).
    /// Use GetWallTile directly when the renderer has diagonal information available.
    /// </summary>
    public string? GetAutoWallTile(string theme, int bitmask)
        => GetWallTile(theme, bitmask, 0);

    /// <summary>
    /// Return the stair-down texture path for the given theme.
    /// Falls back to default_theme if missing. Returns null if unconfigured.
    /// </summary>
    public string? GetStairDown(string theme)
    {
        var data = ResolveTheme(theme);
        if (data == null) return null;

        if (data.StairDown.Count == 0)
        {
            GD.PrintErr($"[TileThemeConfig] Theme '{theme}' has no stair_down tiles.");
            return null;
        }

        return GetTexturePath(data.StairDown[0]);
    }

    /// <summary>
    /// Return the stair-up texture path for the given theme.
    /// Falls back to default_theme if missing. Returns null if unconfigured.
    /// </summary>
    public string? GetStairUp(string theme)
    {
        var data = ResolveTheme(theme);
        if (data == null) return null;

        if (data.StairUp.Count == 0)
        {
            GD.PrintErr($"[TileThemeConfig] Theme '{theme}' has no stair_up tiles.");
            return null;
        }

        return GetTexturePath(data.StairUp[0]);
    }

    /// <summary>
    /// Return a door texture path for the given theme and variant.
    /// Returns null if the tile ID is 0 (not configured) — door renders invisibly but still functions.
    /// No error log on missing variant — themes only configure the variants they use.
    /// </summary>
    public string? GetDoor(string theme)               => GetDoorVariant(theme, d => d.Door);
    public string? GetDoorOpen(string theme)           => GetDoorVariant(theme, d => d.DoorOpen);
    public string? GetDoorLocked(string theme)         => GetDoorVariant(theme, d => d.DoorLocked);
    public string? GetDoorShut(string theme)           => GetDoorVariant(theme, d => d.DoorShut);
    public string? GetDoorBarred(string theme)         => GetDoorVariant(theme, d => d.DoorBarred);
    public string? GetDoorBroken(string theme)         => GetDoorVariant(theme, d => d.DoorBroken);
    public string? GetDoorAjar(string theme)           => GetDoorVariant(theme, d => d.DoorAjar);
    public string? GetDoorIron(string theme)           => GetDoorVariant(theme, d => d.DoorIron);
    public string? GetDoorIronOpen(string theme)       => GetDoorVariant(theme, d => d.DoorIronOpen);
    public string? GetDoorMagic(string theme)          => GetDoorVariant(theme, d => d.DoorMagic);
    public string? GetDoorMagicOpen(string theme)      => GetDoorVariant(theme, d => d.DoorMagicOpen);
    public string? GetDoorBarricaded(string theme)     => GetDoorVariant(theme, d => d.DoorBarricaded);
    public string? GetDoorBarricadedOpen(string theme) => GetDoorVariant(theme, d => d.DoorBarricadedOpen);
    public string? GetDoorPortal(string theme)         => GetDoorVariant(theme, d => d.DoorPortal);

    private string? GetDoorVariant(string theme, Func<TileThemeData, int> selector)
    {
        var data = ResolveTheme(theme);
        if (data == null) return null;
        int id = selector(data);
        return id == 0 ? null : GetTexturePath(id);
    }

    // -------------------------------------------------------------------------
    // Chest and sign tile IDs
    // -------------------------------------------------------------------------

    public int GetChestClosed(string theme)  => ResolveTheme(theme)?.ChestClosed  ?? 0;
    public int GetChestOpen(string theme)    => ResolveTheme(theme)?.ChestOpen    ?? 0;
    public int GetChestEmpty(string theme)   => ResolveTheme(theme)?.ChestEmpty   ?? 0;
    public int GetChestTrapped(string theme) => ResolveTheme(theme)?.ChestTrapped ?? 0;
    public int GetSign(string theme)         => ResolveTheme(theme)?.Sign         ?? 0;

    /// <summary>
    /// Return a bones decoration texture path for the given theme and position,
    /// or null if the position doesn't receive a bones overlay (~2.5% chance).
    ///
    /// Deterministic by position — the same tile always either has bones or doesn't,
    /// and always uses the same bones variant. Purely atmospheric.
    /// </summary>
    public string? GetBones(string theme, int x, int y)
    {
        var data = ResolveTheme(theme);
        if (data == null || data.Bones.Count == 0) return null;

        int hash = PositionHash(x, y);
        // ~2.5%: 1 in 40
        if (hash % 40 != 0) return null;

        int tileId = data.Bones[hash % data.Bones.Count];
        return GetTexturePath(tileId);
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolve a theme name to its data, falling back to DefaultTheme if missing.
    /// Returns null and logs an error if even the default theme is missing.
    /// </summary>
    private TileThemeData? ResolveTheme(string theme)
    {
        if (Themes.TryGetValue(theme, out var data))
            return data;

        // Theme not found — fall back to the configured default
        if (theme != DefaultTheme)
        {
            GD.PrintErr($"[TileThemeConfig] Theme '{theme}' not found — falling back to '{DefaultTheme}'.");
            if (Themes.TryGetValue(DefaultTheme, out var fallback))
                return fallback;
        }

        GD.PrintErr($"[TileThemeConfig] Default theme '{DefaultTheme}' not found. Check tile_themes.yaml.");
        return null;
    }

    /// <summary>
    /// Deterministic position hash for tile variation.
    /// Same (x, y) always produces the same hash — dungeon looks stable across re-renders.
    ///
    /// THE PREVIOUS VERSION WAS LINEAR AND THEREFORE DREW A LATTICE. It was
    /// <c>|(x*7919 + y*104729) &amp; 0x7FFFFFFF|</c>, and a linear function has a constant
    /// difference along any straight line: stepping (+1,+1) always changes the hash by exactly
    /// 7919 + 104729 = 112648. Taken modulo a variant count that becomes a fixed ADDITIVE STEP,
    /// so the chosen variant cycles with period <c>N / gcd(step, N)</c> along every row, column
    /// and diagonal. It is not a weak hash; it is periodic by construction, at every N.
    ///
    /// With this session's 24 floor variants the diagonal step is 112648 mod 24 = 16, whose
    /// additive order mod 24 is 3 — **the same tile recurs every third cell down the diagonal**,
    /// out of a pool of twenty-four. A blind seat measured it before the arithmetic was checked:
    ///
    ///     "eight pairs at exactly (+3 rows, +3 cols), six at (+4 rows, -4 cols). That is a
    ///      lattice ... 24% of the tiles on this single screen have a visible twin also on this
    ///      screen, arranged in a regular diagonal rhythm the eye picks up even before it
    ///      identifies why."
    ///
    /// ART-BIBLE-v0 §8.3 is the clause it breaks, and it breaks it in the worst possible place:
    /// the variant system is the ONLY mechanism the bible gives for keeping a tiled field off
    /// the motif trap, and this function was quietly undoing it. A twenty-four tile family was
    /// delivering three.
    ///
    /// The fix is a bit-mixing finalizer (xor-shift / multiply / xor-shift), which destroys the
    /// linear relationship so no straight line through the map carries a constant step. It is
    /// deterministic, cheap, and the same construction <c>FloorIncidentPlanner.Hash</c> uses.
    ///
    /// ⚠ IT CHANGES WHICH VARIANT LANDS ON WHICH CELL for every existing theme. Nothing looks
    /// worse for it — a role with one variant is untouched, and a role with several was drawing
    /// a periodic pattern it should not have — but captures taken before this commit will not
    /// reproduce byte-for-byte, and that is a real cost recorded rather than discovered.
    /// </summary>
    private static int PositionHash(int x, int y)
    {
        unchecked
        {
            int h = x * 7919 + y * 104729;
            h ^= h >> 13;
            h *= 1274126177;
            h ^= h >> 16;
            return h & 0x7FFFFFFF;
        }
    }

    /// <summary>
    /// Choose one of a role's tile-ID variants for a grid cell. Single-entry lists resolve to
    /// their one member without consulting the position, so a scalar-configured mask behaves
    /// exactly as it did before variants existed.
    /// </summary>
    private static int PickVariant(List<int> variants, int x, int y)
        => variants.Count == 1 ? variants[0] : variants[PositionHash(x, y) % variants.Count];
}

/// <summary>
/// Per-theme tile ID lists for each dungeon surface role.
/// All lists may be empty if a role is not configured for this theme.
///
/// WallAutotile maps 4-bit cardinal bitmask (0–15) → tile ID.
/// WallDiagonal maps named corner roles → tile ID for outer corner detection
/// when all four cardinal neighbors are walls (mask 15).
/// </summary>
public sealed class TileThemeData
{
    public List<int> FloorPrimary  { get; set; } = new();
    public List<int> FloorAccent   { get; set; } = new();

    /// <summary>
    /// Wall-adjacent dark floor tiles (distance 1 from a wall).
    /// Provides the edge-darkening effect in the floor composition pipeline.
    /// Optional — floor decoration pipeline uses these when present.
    /// </summary>
    public List<int> FloorDark     { get; set; } = new();

    /// <summary>
    /// Deep interior floor tiles (distance 2+ from any wall).
    /// Provides subtle variation for large open room centers.
    /// Optional — floor decoration pipeline uses these when present.
    /// </summary>
    public List<int> FloorInterior { get; set; } = new();

    /// <summary>
    /// Worn floor tiles for high-traffic paths (noise-driven variation cluster).
    /// Provides a subtle "walked over" look to central corridors and room paths.
    /// Optional — floor decoration pipeline uses these when present.
    /// </summary>
    public List<int> FloorWorn     { get; set; } = new();

    /// <summary>
    /// 4-bit cardinal bitmask → tile ID variants for connected wall autotiling.
    /// Keys 0–15, where the bitmask encodes cardinal wall neighbors:
    /// bit3(8)=North, bit2(4)=South, bit1(2)=East, bit0(1)=West.
    ///
    /// A mask may declare one tile ID (`3: 184`) or several (`3: [184, 185, 186]`), in which
    /// case the variant is chosen by PositionHash exactly as floor roles already are. A wall
    /// mask that resolves to a single tile stamps that tile at every cell where the mask
    /// occurs — for a corridor edge that is a repeat every 32px, which is the defect the wall
    /// gauntlet's critic named in every round it reached.
    /// </summary>
    public Dictionary<int, List<int>> WallAutotile { get; set; } = new();

    /// <summary>
    /// Named outer corner and interior fill tile ID variants.
    /// Used when cardinalMask==15 to distinguish outer corners from true interior.
    /// Keys: corner_outer_nw, corner_outer_ne, corner_outer_sw, corner_outer_se, interior_fill.
    ///
    /// Like WallAutotile, a role may declare one tile ID or a bracketed list; a list is chosen
    /// from by PositionHash. interior_fill is the role where this matters most - it is the bulk
    /// of every map's solid mass.
    /// </summary>
    public Dictionary<string, List<int>> WallDiagonal { get; set; } = new();

    public List<int> StairDown         { get; set; } = new();
    public List<int> StairUp           { get; set; } = new();

    // Door tile variants. 0 = not configured for this theme.
    public int Door               { get; set; }  // 201 closed
    public int DoorOpen           { get; set; }  // 202 open
    public int DoorLocked         { get; set; }  // 203 locked (keyed)
    public int DoorShut           { get; set; }  // 204 shut, no handle
    public int DoorBarred         { get; set; }  // 205 barred
    public int DoorBroken         { get; set; }  // 206 broken open
    public int DoorAjar           { get; set; }  // 207 slightly ajar
    public int DoorIron           { get; set; }  // 208 iron, closed
    public int DoorIronOpen       { get; set; }  // 209 iron, open
    public int DoorMagic          { get; set; }  // 210 magic, closed
    public int DoorMagicOpen      { get; set; }  // 211 magic, open
    public int DoorBarricaded     { get; set; }  // 212 barricaded
    public int DoorBarricadedOpen { get; set; }  // 213 barricaded, broken open
    public int DoorPortal         { get; set; }  // 214 door with magic portal

    public List<int> Bones             { get; set; } = new();

    // Chest variants (0 = not configured for this theme)
    public int ChestClosed  { get; set; }
    public int ChestOpen    { get; set; }
    public int ChestTrapped { get; set; }
    public int ChestEmpty   { get; set; }

    // Decorative prop tiles (0 = not configured for this theme)
    public int Sign               { get; set; }
    public int MuralGoldLandscape { get; set; }
    public int MuralGoldWarm      { get; set; }
    public int MuralWoodCool      { get; set; }
}
