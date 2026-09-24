using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;

namespace UnderWarden.Logic.ECS;

/// <summary>
/// Constraint-based prop placement engine. Reads a room's archetype, applies a recipe of
/// required and optional props, then places them according to spatial rules (wall-adjacent,
/// center, corner, free-standing, floor-overlay). Connectivity is validated after placement
/// so that props never make a room impassable.
///
/// Called once per room during dungeon generation. Always deterministic for the same seed.
/// </summary>
public static class RoomPropPlacer
{
    // -------------------------------------------------------------------------
    // Placement grid cell classification
    // -------------------------------------------------------------------------

    private enum CellClass { Open, WallAdjacent, Corner, Forbidden }

    // -------------------------------------------------------------------------
    // Recipe structure
    // -------------------------------------------------------------------------

    /// <summary>
    /// One line in an archetype's placement recipe.
    /// Chance == 1.0 = required (always attempted); Chance < 1.0 = optional (rolled per rule).
    /// </summary>
    private sealed record PropRule(
        string PropId,
        PropPlacement Placement,
        int CountMin,
        int CountMax,
        float Chance = 1.0f);

    // -------------------------------------------------------------------------
    // Density: archetypes with lots of props vs sparse ones
    // -------------------------------------------------------------------------

    private static readonly HashSet<RoomArchetype> DenseArchetypes =
    [
        RoomArchetype.Storage,
        RoomArchetype.Library,
        RoomArchetype.Crypt,
        RoomArchetype.Prison,
        RoomArchetype.MushroomGarden,
    ];

    // -------------------------------------------------------------------------
    // Symmetry: archetypes that mirror optional props after Pass 2
    // -------------------------------------------------------------------------

    private enum SymmetryType { None, Bilateral, Radial }

    private static readonly Dictionary<RoomArchetype, SymmetryType> ArchetypeSymmetry = new()
    {
        [RoomArchetype.ThroneRoom]   = SymmetryType.Bilateral,
        [RoomArchetype.Armory]       = SymmetryType.Bilateral,
        [RoomArchetype.Crypt]        = SymmetryType.Bilateral,
        [RoomArchetype.FountainRoom] = SymmetryType.Radial,
    };

    // -------------------------------------------------------------------------
    // Recipe table — one entry per non-Generic archetype
    // -------------------------------------------------------------------------

    private static readonly Dictionary<RoomArchetype, List<PropRule>> Recipes = new()
    {
        [RoomArchetype.Library] =
        [
            new("table",       PropPlacement.Center,         1, 1),
            new("chair",       PropPlacement.PropAdjacent,   1, 2, 0.70f),
            new("desk",        PropPlacement.WallAdjacent,   1, 1, 0.50f),
            new("candelabra",  PropPlacement.FreeStanding,   1, 1, 0.40f),
        ],

        [RoomArchetype.Armory] =
        [
            new("weapon_rack",     PropPlacement.WallAdjacent,  2, 3),
            new("armor_stand",     PropPlacement.FreeStanding,   1, 2, 0.50f),
            new("training_dummy",  PropPlacement.FreeStanding,   1, 1, 0.30f),
            new("crate",           PropPlacement.Corner,          1, 2, 0.40f),
        ],

        [RoomArchetype.Kitchen] =
        [
            new("fireplace",  PropPlacement.WallAdjacent,  1, 1),
            new("table",      PropPlacement.Center,         1, 1),
            new("chair",      PropPlacement.PropAdjacent,   1, 3, 0.60f),
            new("cauldron",   PropPlacement.Center,          1, 1, 0.40f),
        ],

        [RoomArchetype.ThroneRoom] =
        [
            new("throne",      PropPlacement.WallAdjacent,  1, 1),
            new("pillar",      PropPlacement.FreeStanding,   1, 2, 0.50f),
            new("brazier",     PropPlacement.FreeStanding,   1, 2, 0.40f),
            new("banner",      PropPlacement.WallAdjacent,   1, 3, 0.60f),
            new("statue",      PropPlacement.FreeStanding,   1, 2, 0.30f),
        ],

        [RoomArchetype.Prison] =
        [
            new("chain",       PropPlacement.WallAdjacent,  2, 4),
            new("iron_bars",   PropPlacement.WallAdjacent,   1, 2),
            new("cage",        PropPlacement.FreeStanding,   1, 1, 0.30f),
            new("bucket",      PropPlacement.FreeStanding,   1, 2, 0.40f),
            new("straw_pile",  PropPlacement.FloorOverlay,   1, 2, 0.40f),
        ],

        [RoomArchetype.Laboratory] =
        [
            new("alchemy_table",  PropPlacement.WallAdjacent,  1, 1),
            new("cauldron",       PropPlacement.Center,          1, 1),
            new("shelf_bottles",  PropPlacement.WallAdjacent,   1, 2),
            new("globe",          PropPlacement.FreeStanding,   1, 1, 0.35f),
            new("candelabra",     PropPlacement.FreeStanding,   1, 1, 0.30f),
        ],

        [RoomArchetype.Shrine] =
        [
            new("altar",        PropPlacement.Center,         1, 1),
            new("candle",       PropPlacement.FloorOverlay,   1, 4, 0.60f),
            new("statue",       PropPlacement.FreeStanding,   1, 2, 0.30f),
            new("prayer_mat",   PropPlacement.FloorOverlay,   1, 1, 0.40f),
            new("brazier",      PropPlacement.FreeStanding,   1, 2, 0.50f),
        ],

        [RoomArchetype.Storage] =
        [
            new("crate",        PropPlacement.Corner,        2, 3),
            new("shelf",        PropPlacement.WallAdjacent,  1, 2, 0.50f),
            new("chest_closed", PropPlacement.Corner,        1, 1, 0.30f),
            new("sack",         PropPlacement.FreeStanding,  1, 2, 0.40f),
        ],

        [RoomArchetype.Bedroom] =
        [
            new("bed",          PropPlacement.WallAdjacent,  1, 1),
            new("nightstand",   PropPlacement.WallAdjacent,   1, 1, 0.60f),
            new("chest_closed", PropPlacement.Corner,          1, 1, 0.40f),
            new("wardrobe",     PropPlacement.WallAdjacent,   1, 1, 0.35f),
        ],

        [RoomArchetype.Crypt] =
        [
            new("sarcophagus",  PropPlacement.Center,         1, 2),
            new("tombstone",    PropPlacement.FreeStanding,   1, 3, 0.50f),
            new("urn",          PropPlacement.FreeStanding,   1, 4, 0.60f),
            new("candelabra",   PropPlacement.FreeStanding,   1, 2, 0.40f),
            new("cobweb",       PropPlacement.Corner,          1, 3, 0.50f),
            new("stalagmite",   PropPlacement.FreeStanding,   1, 2, 0.35f),
        ],

        [RoomArchetype.FountainRoom] =
        [
            new("fountain",    PropPlacement.Center,         1, 1),
            new("pillar",      PropPlacement.FreeStanding,   1, 4, 0.40f),
            new("bench",       PropPlacement.WallAdjacent,   1, 4, 0.40f),
            new("planter",     PropPlacement.WallAdjacent,   1, 2, 0.30f),
        ],

        [RoomArchetype.Forge] =
        [
            new("forge",        PropPlacement.WallAdjacent,  1, 1),
            new("anvil",        PropPlacement.FreeStanding,   1, 1),
            new("tool_rack",    PropPlacement.WallAdjacent,   1, 1, 0.55f),
            new("workbench",    PropPlacement.WallAdjacent,   1, 1, 0.45f),
            new("weapon_rack",  PropPlacement.WallAdjacent,   1, 2, 0.40f),
            new("water_barrel", PropPlacement.Corner,          1, 1, 0.35f),
            new("coal_pile",    PropPlacement.FloorOverlay,   1, 2, 0.50f),
        ],

        [RoomArchetype.Sewer] =
        [
            new("grate",            PropPlacement.FloorOverlay,  1, 2),
            new("puddle",           PropPlacement.FloorOverlay,   1, 4, 0.60f),
            new("pipe_horizontal",  PropPlacement.WallAdjacent,   1, 2, 0.50f),
            new("pipe_vertical",    PropPlacement.WallAdjacent,   1, 2, 0.40f),
            new("moss_patch",       PropPlacement.FloorOverlay,   1, 3, 0.40f),
            new("drain",            PropPlacement.FloorOverlay,   1, 2, 0.35f),
            new("cobweb",           PropPlacement.Corner,          1, 2, 0.30f),
        ],

        [RoomArchetype.MushroomGarden] =
        [
            new("mushroom_cluster",  PropPlacement.FreeStanding,  2, 3),
            new("moss_patch",        PropPlacement.FloorOverlay,   2, 4, 0.60f),
            new("puddle",            PropPlacement.FloorOverlay,   1, 3, 0.40f),
            new("vine",              PropPlacement.WallAdjacent,   1, 3, 0.40f),
            new("glowing_mushroom",  PropPlacement.FreeStanding,   1, 2, 0.40f),
            new("stalagmite",        PropPlacement.FreeStanding,   1, 2, 0.30f),
        ],

        // Generic scatter: all optional, all non-blocking. Ensures rooms that fall
        // through to Generic archetype still feel atmospheric rather than sterile.
        // Low density by design — corridors and small rooms stay sparse.
        // rock/stalagmite are intentionally excluded: they block movement and are unsafe
        // in corridor-adjacent or spawn rooms that use Generic archetype.
        [RoomArchetype.Generic] =
        [
            new("cobweb",     PropPlacement.Corner,       1, 2, 0.55f),  // 55%: cobwebs in dark corners
            new("rubble",     PropPlacement.FloorOverlay, 1, 2, 0.40f),  // 40%: rubble on floor
        ],
    };

    /// <summary>
    /// Override recipe for Grand Shrine rooms (Shrine archetype with ≥ 36 walkable tiles).
    /// More dramatic than the standard Shrine recipe — altar is required, flanked by braziers
    /// and a ring of candles, with optional statues. Radial symmetry is applied around the altar.
    /// </summary>
    private static readonly List<PropRule> GrandShrineRecipe =
    [
        new("altar",   PropPlacement.Center,        1, 1),          // required — center anchor for radial symmetry
        new("brazier", PropPlacement.WallAdjacent,  1, 1, 0.85f),  // wall-adjacent: radial symmetry produces 4 braziers at perimeter
        new("candle",  PropPlacement.FloorOverlay,  2, 4, 0.80f),  // likely — candle ring
        new("statue",  PropPlacement.FreeStanding,  1, 1, 0.45f),  // optional — symmetrically placed
    ];

    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    /// <summary>
    /// Place props into <paramref name="room"/> according to its archetype recipe.
    /// Returns an empty list if no recipe is defined for the room's archetype.
    /// Calls <see cref="GameMap.MarkPropCell"/> for every blocking prop cell placed.
    ///
    /// Uses a best-of-N approach: generates <paramref name="candidates"/> independent layouts
    /// using child RNGs forked from the parent, then keeps the highest-scoring one.
    /// The parent RNG advances by exactly <paramref name="candidates"/> steps regardless of
    /// which candidate wins, preserving determinism for downstream systems.
    /// </summary>
    public static List<PlacedProp> PlaceProps(
        Room room,
        GameMap map,
        int depth,
        PropRegistry registry,
        SeededRandom rng,
        int candidates = 5)
    {

        // Step A: entrance detection and placement grid are shared across all candidates —
        // these depend only on the map geometry, not on the random layout choices.
        // Computed before recipe selection so walkable count is available for Grand Shrine detection.
        var (entranceTiles, marginTiles) = FindEntrancesAndMargins(room, map);
        var baseGrid = BuildPlacementGrid(room, map, entranceTiles, marginTiles);
        int walkable = CountWalkable(room, map);

        // Grand Shrine: Shrine room with large walkable area gets a dramatic override recipe
        // and radial symmetry anchored on the altar instead of the standard recipe.
        bool isGrandShrine = room.Archetype == RoomArchetype.Shrine && walkable >= 36;

        List<PropRule> recipe;
        string? radialAnchorOverride = null;

        if (isGrandShrine)
        {
            recipe = GrandShrineRecipe;
            radialAnchorOverride = "altar";
        }
        else if (!Recipes.TryGetValue(room.Archetype, out recipe!))
        {
            return [];
        }

        int maxProps = ComputeMaxProps(walkable, room.Archetype);

        // Advance the parent RNG by exactly `candidates` steps to generate child seeds.
        // This ensures the parent RNG state is deterministic regardless of which candidate wins.
        var childSeeds = new int[candidates];
        for (int i = 0; i < candidates; i++)
            childSeeds[i] = rng.Next(int.MaxValue);

        List<PlacedProp>? bestLayout = null;
        int bestScore = int.MinValue;

        for (int c = 0; c < candidates; c++)
        {
            var childRng = new SeededRandom(childSeeds[c]);
            var candidateLayout = RunPlacement(room, map, recipe, entranceTiles, marginTiles, baseGrid, maxProps, registry, childRng, room.MaintenanceState, radialAnchorOverride);
            int score = ScoreLayout(candidateLayout);
            if (score > bestScore)
            {
                bestScore = score;
                bestLayout = candidateLayout;
            }
        }

        // Apply the winning layout: mark prop cells in GameMap, then run real connectivity validation
        var result = bestLayout ?? new List<PlacedProp>();
        foreach (var prop in result)
            MarkFootprint(map, prop);

        // Final connectivity validation on the actual marked map — the winner's footprints are
        // now marked, so EntrancesConnected will correctly detect blocked passages.
        ValidateConnectivity(map, result, entranceTiles);

        return result;
    }

    // -------------------------------------------------------------------------
    // RunPlacement: one candidate layout (no GameMap mutation)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs the full placement pass (required + optional + symmetry + candidate connectivity)
    /// against a COPY of the base grid. Does not call MarkFootprint — the caller applies
    /// the winning layout's footprints to the actual GameMap after selection.
    /// </summary>
    private static List<PlacedProp> RunPlacement(
        Room room, GameMap map,
        List<PropRule> recipe,
        HashSet<(int, int)> entrances,
        HashSet<(int, int)> margins,
        Dictionary<(int, int), CellClass> baseGrid,  // read-only reference — must copy
        int maxProps,
        PropRegistry registry,
        SeededRandom rng,
        RoomMaintenanceState maintenance = RoomMaintenanceState.Normal,
        string? radialAnchorOverride = null)
    {
        // Work on a copy so different candidates don't share grid state.
        // UpdateGrid marks cells Forbidden to prevent overlap within a single placement pass.
        var grid = new Dictionary<(int, int), CellClass>(baseGrid);

        var placed = new List<PlacedProp>();

        // Apply maintenance state density modifier — deeper/more neglected rooms have fewer props
        int effectiveMaxProps = maintenance switch
        {
            RoomMaintenanceState.WellMaintained => (int)Math.Ceiling(maxProps * 1.2),
            RoomMaintenanceState.Normal         => maxProps,
            RoomMaintenanceState.Neglected      => Math.Max(1, (int)(maxProps * 0.8)),
            RoomMaintenanceState.Abandoned      => Math.Max(1, (int)(maxProps * 0.6)),
            RoomMaintenanceState.Ruined         => Math.Max(1, (int)(maxProps * 0.4)),
            _                                   => maxProps,
        };

        // Pass 1: required props (Chance >= 1.0)
        foreach (var rule in recipe)
        {
            if (placed.Count >= effectiveMaxProps) break;
            if (rule.Chance < 1.0f) continue;

            int count = rng.Next(rule.CountMin, rule.CountMax + 1);
            for (int i = 0; i < count && placed.Count < effectiveMaxProps; i++)
            {
                if (TryPlaceProp(room, map, rule.PropId, rule.Placement, placed,
                    entrances, margins, grid, registry, rng, out var prop) && prop != null)
                {
                    placed.Add(prop);
                    // NOTE: No MarkFootprint here — candidate layouts don't mutate GameMap.
                    // The grid copy handles within-candidate occupation tracking instead.
                    UpdateGrid(grid, prop, room);
                }
            }
        }

        // Ruined rooms: required props may be removed (50% chance each).
        // This represents years of neglect collapsing furniture — a sarcophagus might be
        // buried under rubble, a weapon rack broken beyond recognition.
        if (maintenance == RoomMaintenanceState.Ruined)
        {
            for (int i = placed.Count - 1; i >= 0; i--)
            {
                if (rng.Next(2) == 0) // 50%
                    placed.RemoveAt(i);
                // Note: no UnmarkFootprint — candidate runs don't mark cells
            }
        }

        int requiredCount = placed.Count;

        // Pass 2: optional props (Chance < 1.0)
        foreach (var rule in recipe)
        {
            if (placed.Count >= effectiveMaxProps) break;
            if (rule.Chance >= 1.0f) continue;

            if (rng.NextDouble() > rule.Chance) continue;

            int count = rng.Next(rule.CountMin, rule.CountMax + 1);
            for (int i = 0; i < count && placed.Count < effectiveMaxProps; i++)
            {
                if (TryPlaceProp(room, map, rule.PropId, rule.Placement, placed,
                    entrances, margins, grid, registry, rng, out var prop) && prop != null)
                {
                    placed.Add(prop);
                    UpdateGrid(grid, prop, room);
                }
            }
        }

        // Pass 2b: symmetry — mirror optional props for bilateral/radial archetypes.
        // ApplySymmetry calls MarkFootprint internally, but since candidate runs don't have
        // any footprints marked yet, we pass the grid copy for collision tracking.
        // We use a candidate-safe overload that doesn't touch GameMap footprints.
        // Grand Shrine passes radialAnchorOverride="altar" to force radial symmetry around the altar.
        ApplySymmetryCandidate(room, map, placed, entrances, margins, grid, requiredCount, effectiveMaxProps, radialAnchorOverride);

        // Jitter: Neglected/Abandoned optional props shift 1 tile (40% chance each).
        // Applied after symmetry so mirrored props can also jitter.
        if (maintenance == RoomMaintenanceState.Neglected || maintenance == RoomMaintenanceState.Abandoned)
            ApplyJitter(room, map, placed, entrances, margins, grid, requiredCount, rng);

        // Scatter overlays for Abandoned/Ruined rooms (rubble, cobweb, bones).
        // These ignore the effectiveMaxProps cap — scatter is additive atmospheric dressing.
        if (maintenance == RoomMaintenanceState.Abandoned || maintenance == RoomMaintenanceState.Ruined)
            AddScatterOverlays(room, map, placed, entrances, grid, maintenance, registry, rng);

        // Pass 3: connectivity validation without UnmarkFootprint (nothing was marked).
        // Since no prop cells are marked in GameMap, EntrancesConnected sees all tiles as
        // walkable — the removal pass here is a best-effort heuristic to trim obviously
        // bad layouts. The winning layout gets real validation after MarkFootprint in PlaceProps.
        ValidateConnectivityCandidate(placed);

        return placed;
    }

    /// <summary>
    /// Jitter optional props 1 tile in a random cardinal direction (40% chance each).
    /// Only blocking props jitter — floor overlays are already loosely placed.
    /// Only applies to optional props (index >= requiredCount) to preserve required-prop positions.
    /// </summary>
    private static void ApplyJitter(
        Room room, GameMap map,
        List<PlacedProp> placed,
        HashSet<(int, int)> entrances,
        HashSet<(int, int)> margins,
        Dictionary<(int, int), CellClass> grid,
        int requiredCount,
        SeededRandom rng)
    {
        (int dx, int dy)[] cardinals = [(-1, 0), (1, 0), (0, -1), (0, 1)];

        for (int i = requiredCount; i < placed.Count; i++)
        {
            if (rng.Next(10) >= 4) continue; // 40% chance

            var prop = placed[i];
            if (!prop.BlocksMovement) continue; // don't jitter floor overlays

            // Pick a random cardinal direction
            var (dx, dy) = cardinals[rng.Next(cardinals.Length)];
            int nx = prop.X + dx;
            int ny = prop.Y + dy;

            if (!CanPlaceFootprint(room, map, nx, ny, prop.FootprintW, prop.FootprintH, entrances, margins, grid))
                continue;

            // Move the prop: free old grid cells, place at new position
            for (int fx = prop.X; fx < prop.X + prop.FootprintW; fx++)
                for (int fy = prop.Y; fy < prop.Y + prop.FootprintH; fy++)
                    if (grid.TryGetValue((fx, fy), out _))
                        grid[(fx, fy)] = CellClass.Open;

            placed[i] = prop with { X = nx, Y = ny };
            UpdateGrid(grid, placed[i], room);
        }
    }

    /// <summary>
    /// Add atmospheric scatter overlays (cobweb, rubble, bones) to Abandoned/Ruined rooms.
    /// Scatter overlays never block movement — they are purely visual dressing.
    /// Placed on open floor tiles not already occupied by blocking props.
    /// Count: 1-2 for Abandoned, 2-4 for Ruined.
    /// </summary>
    private static void AddScatterOverlays(
        Room room, GameMap map,
        List<PlacedProp> placed,
        HashSet<(int, int)> entrances,
        Dictionary<(int, int), CellClass> grid,
        RoomMaintenanceState maintenance,
        PropRegistry registry,
        SeededRandom rng)
    {
        // Scatter prop candidates in priority order (use whichever exists in the registry)
        string[] scatterIds = maintenance == RoomMaintenanceState.Ruined
            ? ["rubble", "cobweb"]
            : ["cobweb", "rubble"];

        int scatterMin = maintenance == RoomMaintenanceState.Ruined ? 2 : 1;
        int scatterMax = maintenance == RoomMaintenanceState.Ruined ? 4 : 2;
        int scatterCount = rng.Next(scatterMin, scatterMax + 1);

        // Build candidate tiles: walkable floor tiles not blocked by props or entrances
        var candidates = new List<(int X, int Y)>();
        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
            {
                if (!room.Contains(x, y)) continue;
                if (!IsRawWalkable(map, x, y)) continue;
                if (entrances.Contains((x, y))) continue;
                if (map.IsPropCell(x, y)) continue;
                if (grid.TryGetValue((x, y), out var cls) && cls == CellClass.Forbidden) continue;
                candidates.Add((x, y));
            }

        if (candidates.Count == 0) return;

        for (int s = 0; s < scatterCount; s++)
        {
            // Pick a scatter prop that exists in the registry
            PropDefinition? def = null;
            string? scatterId = null;
            foreach (var id in scatterIds)
            {
                def = registry.Get(id);
                if (def != null) { scatterId = id; break; }
            }
            if (def == null || scatterId == null) break;

            if (candidates.Count == 0) break;
            int idx = rng.Next(candidates.Count);
            var (cx, cy) = candidates[idx];
            candidates.RemoveAt(idx); // don't place two scatters on same tile

            int tileId = def.TileIds.Count > 0 ? def.TileIds[rng.Next(def.TileIds.Count)] : 0;
            // BlocksMovement=false — scatter overlays never block movement
            placed.Add(new PlacedProp(scatterId, cx, cy, 1, 1, BlocksMovement: false, TileId: tileId));
        }
    }

    // -------------------------------------------------------------------------
    // Scoring
    // -------------------------------------------------------------------------

    /// <summary>
    /// Score a candidate layout. More props = better. Blocking props count more than
    /// overlays because they represent meaningful room furniture vs floor decoration.
    /// </summary>
    private static int ScoreLayout(List<PlacedProp> layout)
    {
        int blockingProps = layout.Count(p => p.BlocksMovement);
        int overlayProps  = layout.Count(p => !p.BlocksMovement);
        return blockingProps * 10 + overlayProps * 3;
    }

    // -------------------------------------------------------------------------
    // Candidate-safe symmetry (no MarkFootprint calls)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Symmetry pass that works on candidate layouts — does NOT call MarkFootprint,
    /// only UpdateGrid on the local grid copy. Mirrors the logic of ApplySymmetry but
    /// operates without mutating the shared GameMap.
    /// </summary>
    private static void ApplySymmetryCandidate(
        Room room, GameMap map,
        List<PlacedProp> placed,
        HashSet<(int, int)> entrances,
        HashSet<(int, int)> margins,
        Dictionary<(int, int), CellClass> grid,
        int requiredCount,
        int maxProps,
        string? radialAnchorOverride = null)
    {
        // If an explicit radial anchor is provided (e.g., Grand Shrine), force radial symmetry
        // regardless of what ArchetypeSymmetry says about this room's archetype.
        if (radialAnchorOverride != null)
        {
            ApplyRadialCandidate(room, map, placed, entrances, margins, grid, requiredCount, maxProps, radialAnchorOverride);
            return;
        }

        if (!ArchetypeSymmetry.TryGetValue(room.Archetype, out var symmetry))
            return;

        if (symmetry == SymmetryType.Bilateral)
            ApplyBilateralCandidate(room, map, placed, entrances, margins, grid, requiredCount, maxProps);
        else if (symmetry == SymmetryType.Radial)
            ApplyRadialCandidate(room, map, placed, entrances, margins, grid, requiredCount, maxProps);
    }

    private static void ApplyBilateralCandidate(
        Room room, GameMap map,
        List<PlacedProp> placed,
        HashSet<(int, int)> entrances,
        HashSet<(int, int)> margins,
        Dictionary<(int, int), CellClass> grid,
        int requiredCount, int maxProps)
    {
        bool horizontal = room.Width >= room.Height;
        int axisX = room.X + room.Width / 2;
        int axisY = room.Y + room.Height / 2;

        var optionals = placed.Skip(requiredCount).ToList();

        foreach (var prop in optionals)
        {
            if (placed.Count >= maxProps) break;

            int mx = horizontal
                ? 2 * axisX - prop.X - (prop.FootprintW - 1)
                : prop.X;
            int my = horizontal
                ? prop.Y
                : 2 * axisY - prop.Y - (prop.FootprintH - 1);

            if (!CanPlaceFootprint(room, map, mx, my, prop.FootprintW, prop.FootprintH, entrances, margins, grid))
                continue;

            var mirror = prop with { X = mx, Y = my };
            placed.Add(mirror);
            UpdateGrid(grid, mirror, room);
        }
    }

    private static void ApplyRadialCandidate(
        Room room, GameMap map,
        List<PlacedProp> placed,
        HashSet<(int, int)> entrances,
        HashSet<(int, int)> margins,
        Dictionary<(int, int), CellClass> grid,
        int requiredCount, int maxProps,
        string anchorPropId = "fountain")
    {
        var anchor = placed.Take(requiredCount).FirstOrDefault(p => p.PropId == anchorPropId);
        if (anchor == null) return;

        int fcx = anchor.X + anchor.FootprintW / 2;
        int fcy = anchor.Y + anchor.FootprintH / 2;

        var optionals = placed.Skip(requiredCount).ToList();

        foreach (var prop in optionals)
        {
            if (placed.Count >= maxProps) break;

            int offX = (prop.X + prop.FootprintW / 2) - fcx;
            int offY = (prop.Y + prop.FootprintH / 2) - fcy;

            (int, int)[] rotations =
            [
                (-offY, offX),
                (-offX, -offY),
                (offY, -offX),
            ];

            foreach (var (rx, ry) in rotations)
            {
                if (placed.Count >= maxProps) break;

                int tx = fcx + rx - prop.FootprintW / 2;
                int ty = fcy + ry - prop.FootprintH / 2;

                if (!CanPlaceFootprint(room, map, tx, ty, prop.FootprintW, prop.FootprintH, entrances, margins, grid))
                    continue;

                var counterpart = prop with { X = tx, Y = ty };
                placed.Add(counterpart);
                UpdateGrid(grid, counterpart, room);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Candidate-safe connectivity (no UnmarkFootprint calls)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Heuristic connectivity pass for candidate layouts. Since no footprints are marked in
    /// GameMap, EntrancesConnected always sees clear tiles — so this just removes the last
    /// blocking prop (up to 3 times) as a preventive trim. The real connectivity check happens
    /// after the winner's footprints are marked in PlaceProps.
    /// </summary>
    private static void ValidateConnectivityCandidate(List<PlacedProp> placed)
    {
        // Without marked footprints, we can't flood-fill accurately.
        // This is a no-op placeholder — the winner gets real validation in PlaceProps.
        // Left as a stub to match the structure of ValidateConnectivity and to allow
        // future enhancement (e.g. simulated flood fill against a virtual prop map).
        _ = placed; // suppress unused-parameter warning
    }

    // -------------------------------------------------------------------------
    // Step A: entrance detection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Find entrance tiles (walkable room tiles that have a corridor neighbor) and
    /// margin tiles (walkable room tiles within 1 cardinal step of an entrance).
    /// Props cannot be placed on either set.
    /// </summary>
    private static (HashSet<(int, int)> Entrances, HashSet<(int, int)> Margins) FindEntrancesAndMargins(
        Room room, GameMap map)
    {
        var entrances = new HashSet<(int, int)>();

        for (int x = room.X; x < room.X + room.Width; x++)
        {
            for (int y = room.Y; y < room.Y + room.Height; y++)
            {
                if (!room.Contains(x, y)) continue;
                // Use raw walkability (ignoring prop cells) — at generation time no props are placed yet
                if (!IsRawWalkable(map, x, y)) continue;

                // Check if any cardinal neighbor is a corridor or door tile.
                // DoorPlacer converts corridor-room boundary tiles to Door — we must
                // treat Door the same as Corridor here so entrance detection still works
                // after doors are placed (DOOR-001).
                static bool IsCorridorOrDoor(TileKind k) => k == TileKind.Corridor || k == TileKind.Door || k == TileKind.DoorOpen;

                if (IsCorridorOrDoor(map.GetTileKind(x - 1, y)) ||
                    IsCorridorOrDoor(map.GetTileKind(x + 1, y)) ||
                    IsCorridorOrDoor(map.GetTileKind(x, y - 1)) ||
                    IsCorridorOrDoor(map.GetTileKind(x, y + 1)))
                {
                    entrances.Add((x, y));
                }
            }
        }

        // Margin: cardinal neighbors of entrance tiles that are walkable and inside the room
        var margins = new HashSet<(int, int)>();
        foreach (var (ex, ey) in entrances)
        {
            (int dx, int dy)[] cardinals = [(-1, 0), (1, 0), (0, -1), (0, 1)];
            foreach (var (dx, dy) in cardinals)
            {
                int nx = ex + dx, ny = ey + dy;
                if (room.Contains(nx, ny) && IsRawWalkable(map, nx, ny) && !entrances.Contains((nx, ny)))
                    margins.Add((nx, ny));
            }
        }

        return (entrances, margins);
    }

    // -------------------------------------------------------------------------
    // Step B: placement grid
    // -------------------------------------------------------------------------

    /// <summary>
    /// Classify each room tile as Forbidden, Corner, WallAdjacent, or Open.
    /// Classification uses IsWallTile which reflects only actual wall geometry, not prop cells.
    /// </summary>
    private static Dictionary<(int, int), CellClass> BuildPlacementGrid(
        Room room, GameMap map,
        HashSet<(int, int)> entrances, HashSet<(int, int)> margins)
    {
        var grid = new Dictionary<(int, int), CellClass>();

        for (int x = room.X; x < room.X + room.Width; x++)
        {
            for (int y = room.Y; y < room.Y + room.Height; y++)
            {
                if (!room.Contains(x, y)) continue;
                if (!IsRawWalkable(map, x, y)) continue;

                if (entrances.Contains((x, y)) || margins.Contains((x, y)))
                {
                    grid[(x, y)] = CellClass.Forbidden;
                    continue;
                }

                bool wallN = map.IsWallTile(x, y - 1);
                bool wallS = map.IsWallTile(x, y + 1);
                bool wallW = map.IsWallTile(x - 1, y);
                bool wallE = map.IsWallTile(x + 1, y);

                int wallCount = (wallN ? 1 : 0) + (wallS ? 1 : 0) + (wallW ? 1 : 0) + (wallE ? 1 : 0);

                if (wallCount >= 2 &&
                    ((wallN && wallW) || (wallN && wallE) || (wallS && wallW) || (wallS && wallE)))
                {
                    grid[(x, y)] = CellClass.Corner;
                }
                else if (wallCount >= 1)
                {
                    grid[(x, y)] = CellClass.WallAdjacent;
                }
                else
                {
                    grid[(x, y)] = CellClass.Open;
                }
            }
        }

        return grid;
    }

    /// <summary>
    /// After placing a blocking prop, mark its cells as Forbidden in the grid so subsequent
    /// placement attempts don't overlap. Called after each successful placement.
    /// </summary>
    private static void UpdateGrid(
        Dictionary<(int, int), CellClass> grid,
        PlacedProp prop,
        Room room)
    {
        for (int fx = prop.X; fx < prop.X + prop.FootprintW; fx++)
            for (int fy = prop.Y; fy < prop.Y + prop.FootprintH; fy++)
                if (room.Contains(fx, fy))
                    grid[(fx, fy)] = CellClass.Forbidden;
    }

    // -------------------------------------------------------------------------
    // Density / cap helpers
    // -------------------------------------------------------------------------

    private static int CountWalkable(Room room, GameMap map)
    {
        int count = 0;
        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
                if (room.Contains(x, y) && IsRawWalkable(map, x, y))
                    count++;
        return count;
    }

    private static int ComputeMaxProps(int walkable, RoomArchetype archetype)
    {
        // Hard caps for small rooms — prevent choking the entire room with furniture
        if (walkable <= 9)  return 1;
        if (walkable <= 16) return 2;
        if (walkable <= 25) return 4;

        // Larger rooms: density cap
        float densityFraction = DenseArchetypes.Contains(archetype) ? 0.55f : 0.25f;
        return Math.Max(4, (int)(walkable * densityFraction));
    }

    // -------------------------------------------------------------------------
    // Step E: TryPlaceProp
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempt to place one instance of <paramref name="propId"/> in the room.
    /// Builds a candidate list filtered by placement type, footprint bounds, and occupation.
    /// Returns false if no valid position exists.
    /// </summary>
    private static bool TryPlaceProp(
        Room room, GameMap map,
        string propId, PropPlacement placement,
        List<PlacedProp> alreadyPlaced,
        HashSet<(int, int)> entrances, HashSet<(int, int)> margins,
        Dictionary<(int, int), CellClass> grid,
        PropRegistry registry, SeededRandom rng,
        out PlacedProp? placed)
    {
        placed = null;

        var def = registry.Get(propId);
        if (def == null) return false;

        // Build set of already-occupied anchor positions from placed props (for footprint collision)
        var occupiedAnchors = new HashSet<(int, int)>();
        foreach (var p in alreadyPlaced)
            for (int fx = p.X; fx < p.X + p.FootprintW; fx++)
                for (int fy = p.Y; fy < p.Y + p.FootprintH; fy++)
                    occupiedAnchors.Add((fx, fy));

        // PropAdjacent: tiles immediately (cardinal) next to any already-placed prop.
        // Falls back to FreeStanding behaviour when no props have been placed yet.
        HashSet<(int, int)>? propAdjacentTiles = null;
        if (placement == PropPlacement.PropAdjacent && occupiedAnchors.Count > 0)
        {
            propAdjacentTiles = new HashSet<(int, int)>();
            foreach (var (ox, oy) in occupiedAnchors)
            {
                foreach (var (nx, ny) in new[] { (ox - 1, oy), (ox + 1, oy), (ox, oy - 1), (ox, oy + 1) })
                    if (!occupiedAnchors.Contains((nx, ny)))
                        propAdjacentTiles.Add((nx, ny));
            }
        }

        // Build candidate list
        var candidates = new List<(int X, int Y)>();

        // Center 60% bounds for Center placement
        double centerX1 = room.X + room.Width * 0.2;
        double centerX2 = room.X + room.Width * 0.8;
        double centerY1 = room.Y + room.Height * 0.2;
        double centerY2 = room.Y + room.Height * 0.8;

        bool isFloorOverlay = placement == PropPlacement.FloorOverlay || !def.BlocksMovement;

        for (int x = room.X; x < room.X + room.Width; x++)
        {
            for (int y = room.Y; y < room.Y + room.Height; y++)
            {
                if (!room.Contains(x, y)) continue;
                if (!IsRawWalkable(map, x, y)) continue;

                // Skip entrance tiles (margins OK for floor overlays since they don't block)
                if (entrances.Contains((x, y))) continue;
                if (!isFloorOverlay && margins.Contains((x, y))) continue;

                // Skip tiles already marked occupied in the grid
                if (grid.TryGetValue((x, y), out var cls) && cls == CellClass.Forbidden && !isFloorOverlay)
                    continue;
                // For non-overlay props, also skip occupied anchor positions
                if (!isFloorOverlay && occupiedAnchors.Contains((x, y))) continue;
                // For overlays, still skip directly occupied cells by blocking props
                if (isFloorOverlay && map.IsPropCell(x, y)) continue;

                // Footprint bounds check — all footprint cells must be within the room
                bool footprintFits = true;
                for (int fx = x; fx < x + def.FootprintW && footprintFits; fx++)
                    for (int fy = y; fy < y + def.FootprintH && footprintFits; fy++)
                    {
                        if (!room.Contains(fx, fy)) { footprintFits = false; break; }
                        if (!IsRawWalkable(map, fx, fy)) { footprintFits = false; break; }
                        if (!isFloorOverlay && occupiedAnchors.Contains((fx, fy))) { footprintFits = false; break; }
                        if (!isFloorOverlay && grid.TryGetValue((fx, fy), out var fcls) && fcls == CellClass.Forbidden) { footprintFits = false; break; }
                    }
                if (!footprintFits) continue;

                // Placement-type filter
                bool qualifies = placement switch
                {
                    PropPlacement.WallAdjacent =>
                        grid.TryGetValue((x, y), out var wc) &&
                        (wc == CellClass.WallAdjacent || wc == CellClass.Corner),

                    PropPlacement.Corner =>
                        grid.TryGetValue((x, y), out var cc) && cc == CellClass.Corner,

                    PropPlacement.Center =>
                        x >= centerX1 && x <= centerX2 && y >= centerY1 && y <= centerY2,

                    PropPlacement.FreeStanding => true, // any non-forbidden walkable tile

                    PropPlacement.FloorOverlay => true, // any non-entrance walkable tile

                    // Adjacent to a prior prop. propAdjacentTiles is null only when no props placed yet
                    // → fall through to FreeStanding (any walkable tile).
                    PropPlacement.PropAdjacent =>
                        propAdjacentTiles == null || propAdjacentTiles.Count == 0
                            ? true
                            : propAdjacentTiles.Contains((x, y)),

                    _ => true,
                };
                if (!qualifies) continue;

                candidates.Add((x, y));
            }
        }

        if (candidates.Count == 0) return false;

        // Pick a random candidate
        var (cx, cy) = candidates[rng.Next(candidates.Count)];

        // Resolve tile ID(s) — multi-tile props carry the full layout
        IReadOnlyList<int>? tileLayout = null;
        int tileId;

        if (def.TileLayouts != null && def.TileLayouts.Count > 0)
        {
            var layout = def.TileLayouts[rng.Next(def.TileLayouts.Count)];
            tileId = layout.Count > 0 ? layout[0] : 0;
            tileLayout = layout; // renderer uses the full flat list
        }
        else if (def.TileIds.Count > 0)
        {
            tileId = def.TileIds[rng.Next(def.TileIds.Count)];
        }
        else
        {
            tileId = 0;
        }

        // Flippable props (tagged "flippable" in props.yaml) get a random horizontal mirror.
        // Only meaningful for 1x1 props — multi-tile flipping requires mirroring the full layout.
        bool flipH = def.FootprintW == 1 && def.FootprintH == 1
                     && def.Tags.Contains("flippable")
                     && rng.Next(2) == 0;

        placed = new PlacedProp(
            propId, cx, cy,
            def.FootprintW, def.FootprintH,
            def.BlocksMovement,
            tileId,
            OverlayTileId: def.OverlayTileId,
            TileLayout: tileLayout,
            FlipH: flipH);
        return true;
    }

    // -------------------------------------------------------------------------
    // Pass 2b: symmetry
    // -------------------------------------------------------------------------

    private static void ApplySymmetry(
        Room room, GameMap map,
        List<PlacedProp> placed,
        HashSet<(int, int)> entrances,
        HashSet<(int, int)> margins,
        Dictionary<(int, int), CellClass> grid,
        int requiredCount,
        int maxProps,
        string? radialAnchorOverride = null)
    {
        // If an explicit radial anchor is provided (e.g., Grand Shrine), force radial symmetry
        if (radialAnchorOverride != null)
        {
            ApplyRadial(room, map, placed, entrances, margins, grid, requiredCount, maxProps, radialAnchorOverride);
            return;
        }

        if (!ArchetypeSymmetry.TryGetValue(room.Archetype, out var symmetry))
            return;

        if (symmetry == SymmetryType.Bilateral)
            ApplyBilateral(room, map, placed, entrances, margins, grid, requiredCount, maxProps);
        else if (symmetry == SymmetryType.Radial)
            ApplyRadial(room, map, placed, entrances, margins, grid, requiredCount, maxProps);
    }

    /// <summary>
    /// Mirror each optional prop across the room's long axis.
    /// Wide rooms (Width >= Height) mirror across the vertical center line.
    /// Tall rooms (Height > Width) mirror across the horizontal center line.
    /// Best-effort: if the mirror position is invalid, the original stays, nothing crashes.
    /// </summary>
    private static void ApplyBilateral(
        Room room, GameMap map,
        List<PlacedProp> placed,
        HashSet<(int, int)> entrances,
        HashSet<(int, int)> margins,
        Dictionary<(int, int), CellClass> grid,
        int requiredCount, int maxProps)
    {
        bool horizontal = room.Width >= room.Height;
        int axisX = room.X + room.Width / 2;
        int axisY = room.Y + room.Height / 2;

        // Snapshot optional props so we don't iterate while modifying placed
        var optionals = placed.Skip(requiredCount).ToList();

        foreach (var prop in optionals)
        {
            if (placed.Count >= maxProps) break;

            int mx = horizontal
                ? 2 * axisX - prop.X - (prop.FootprintW - 1)
                : prop.X;
            int my = horizontal
                ? prop.Y
                : 2 * axisY - prop.Y - (prop.FootprintH - 1);

            if (!CanPlaceFootprint(room, map, mx, my, prop.FootprintW, prop.FootprintH, entrances, margins, grid))
                continue;

            var mirror = prop with { X = mx, Y = my };
            placed.Add(mirror);
            MarkFootprint(map, mirror);
            UpdateGrid(grid, mirror, room);
        }
    }

    /// <summary>
    /// For FountainRoom: find the fountain's center, then for each optional prop already placed,
    /// try to add counterparts at the remaining cardinal positions (90°, 180°, 270° rotations)
    /// at the same offset distance from the fountain center.
    /// Best-effort: skips positions that are blocked or out of bounds.
    /// </summary>
    private static void ApplyRadial(
        Room room, GameMap map,
        List<PlacedProp> placed,
        HashSet<(int, int)> entrances,
        HashSet<(int, int)> margins,
        Dictionary<(int, int), CellClass> grid,
        int requiredCount, int maxProps,
        string anchorPropId = "fountain")
    {
        // Find the anchor prop (first required prop that matches the ID — should be at center)
        var anchor = placed.Take(requiredCount).FirstOrDefault(p => p.PropId == anchorPropId);
        if (anchor == null) return;

        int fcx = anchor.X + anchor.FootprintW / 2;
        int fcy = anchor.Y + anchor.FootprintH / 2;

        // Snapshot optional props
        var optionals = placed.Skip(requiredCount).ToList();

        foreach (var prop in optionals)
        {
            if (placed.Count >= maxProps) break;

            // Offset from fountain center to the center of the prop's footprint
            int offX = (prop.X + prop.FootprintW / 2) - fcx;
            int offY = (prop.Y + prop.FootprintH / 2) - fcy;

            // Generate 90°, 180°, 270° rotations of the offset: (x,y) → (-y,x) → (-x,-y) → (y,-x)
            (int, int)[] rotations =
            [
                (-offY, offX),
                (-offX, -offY),
                (offY, -offX),
            ];

            foreach (var (rx, ry) in rotations)
            {
                if (placed.Count >= maxProps) break;

                // Convert back to anchor position (prop center → top-left anchor)
                int tx = fcx + rx - prop.FootprintW / 2;
                int ty = fcy + ry - prop.FootprintH / 2;

                if (!CanPlaceFootprint(room, map, tx, ty, prop.FootprintW, prop.FootprintH, entrances, margins, grid))
                    continue;

                var counterpart = prop with { X = tx, Y = ty };
                placed.Add(counterpart);
                MarkFootprint(map, counterpart);
                UpdateGrid(grid, counterpart, room);
            }
        }
    }

    /// <summary>
    /// Returns true if every cell in the [x, x+fw) × [y, y+fh) footprint is:
    /// inside the room, raw-walkable, not an entrance, not a margin, and not Forbidden in the grid.
    /// Used by symmetry passes where we know the position to check rather than scanning candidates.
    /// </summary>
    private static bool CanPlaceFootprint(
        Room room, GameMap map,
        int x, int y, int fw, int fh,
        HashSet<(int, int)> entrances,
        HashSet<(int, int)> margins,
        Dictionary<(int, int), CellClass> grid)
    {
        for (int fx = x; fx < x + fw; fx++)
        for (int fy = y; fy < y + fh; fy++)
        {
            if (!room.Contains(fx, fy)) return false;
            if (!IsRawWalkable(map, fx, fy)) return false;
            if (entrances.Contains((fx, fy))) return false;
            if (margins.Contains((fx, fy))) return false;
            if (grid.TryGetValue((fx, fy), out var cls) && cls == CellClass.Forbidden) return false;
        }
        return true;
    }

    // -------------------------------------------------------------------------
    // Footprint marking
    // -------------------------------------------------------------------------

    private static void MarkFootprint(GameMap map, PlacedProp prop)
    {
        // Only blocking props mark cells — overlays don't affect walkability
        if (!prop.BlocksMovement) return;

        for (int fx = prop.X; fx < prop.X + prop.FootprintW; fx++)
            for (int fy = prop.Y; fy < prop.Y + prop.FootprintH; fy++)
                map.MarkPropCell(fx, fy);
    }

    // -------------------------------------------------------------------------
    // Step D Pass 3: connectivity validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Flood fill from each entrance tile and verify all other entrance tiles are reachable.
    /// If connectivity is broken, remove the last-placed blocking prop and retry (up to 3 times).
    /// FloorOverlay props (BlocksMovement=false) are never removed since they don't block movement.
    /// </summary>
    private static void ValidateConnectivity(
        GameMap map,
        List<PlacedProp> placed,
        HashSet<(int, int)> entrances)
    {
        if (entrances.Count < 2) return; // nothing to validate with 0 or 1 entrance

        const int MaxRemovalAttempts = 3;

        for (int attempt = 0; attempt < MaxRemovalAttempts; attempt++)
        {
            if (EntrancesConnected(map, entrances)) return;

            // Find the last placed prop that blocks movement and remove it
            int removeIdx = -1;
            for (int i = placed.Count - 1; i >= 0; i--)
            {
                if (placed[i].BlocksMovement)
                {
                    removeIdx = i;
                    break;
                }
            }

            if (removeIdx < 0) return; // no blocking props to remove

            var removedProp = placed[removeIdx];
            placed.RemoveAt(removeIdx);
            UnmarkFootprint(map, removedProp);
        }
    }

    private static void UnmarkFootprint(GameMap map, PlacedProp prop)
    {
        if (!prop.BlocksMovement) return;
        for (int fx = prop.X; fx < prop.X + prop.FootprintW; fx++)
            for (int fy = prop.Y; fy < prop.Y + prop.FootprintH; fy++)
                map.UnmarkPropCell(fx, fy);
    }

    private static bool EntrancesConnected(GameMap map, HashSet<(int, int)> entrances)
    {
        var start = entrances.First();
        var reachable = FloodFill(map, start.Item1, start.Item2);

        foreach (var entrance in entrances)
            if (!reachable.Contains(entrance))
                return false;

        return true;
    }

    private static HashSet<(int, int)> FloodFill(GameMap map, int startX, int startY)
    {
        var visited = new HashSet<(int, int)>();
        if (!map.IsWalkable(startX, startY)) return visited;

        var queue = new Queue<(int, int)>();
        queue.Enqueue((startX, startY));
        visited.Add((startX, startY));

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            (int dx, int dy)[] cardinals = [(-1, 0), (1, 0), (0, -1), (0, 1)];
            foreach (var (dx, dy) in cardinals)
            {
                int nx = x + dx, ny = y + dy;
                if (visited.Contains((nx, ny))) continue;
                if (!map.IsWalkable(nx, ny)) continue;
                visited.Add((nx, ny));
                queue.Enqueue((nx, ny));
            }
        }

        return visited;
    }

    // -------------------------------------------------------------------------
    // Raw walkability — ignores prop cells (used during placement, before marking)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true if the tile is a walkable floor tile, WITHOUT checking _propCells.
    /// Used during placement candidate selection so we evaluate the underlying tile type,
    /// not the post-placement state. Prop occupation is tracked in the grid/alreadyPlaced instead.
    /// </summary>
    private static bool IsRawWalkable(GameMap map, int x, int y)
    {
        // IsWalkable checks _propCells; we want to skip that during placement.
        // IsWallTile is the inverse of the walkable array (ignores prop cells).
        // We use !IsWallTile here since GameMap exposes no separate "tile is floor" query.
        return map.InBounds(x, y) && !map.IsWallTile(x, y);
    }
}
