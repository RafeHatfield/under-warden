namespace UnderWarden.Logic.ECS;

/// <summary>
/// Simple tile grid for scenario arenas and generated dungeons.
/// Tracks walkable tiles, tile kinds, and entity positions.
/// No rendering, no Godot — pure logic.
/// </summary>
public sealed class GameMap
{
    public int Width { get; }
    public int Height { get; }

    private readonly bool[,] _walkable;
    private readonly TileKind[,] _tiles;
    private readonly bool[,] _visible;
    private readonly bool[,] _explored;
    private readonly TileTheme[,] _theme;
    private readonly List<Entity> _entities = new();
    private readonly HashSet<(int, int)> _propCells = new();

    /// <summary>Default constructor: all tiles walkable (existing behavior for scenarios).</summary>
    public GameMap(int width, int height)
    {
        Width = width;
        Height = height;
        _walkable = new bool[width, height];
        _tiles = new TileKind[width, height];
        _visible = new bool[width, height];
        _explored = new bool[width, height];
        _theme = new TileTheme[width, height]; // zero-initialises to TileTheme.Grey

        // Default: all tiles walkable (existing scenario behavior preserved)
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                _walkable[x, y] = true;
                _tiles[x, y] = TileKind.Floor;
            }
        // _visible and _explored default to false (not yet seen)
    }

    /// <summary>
    /// Dungeon-generation constructor: all tiles initialised as Wall (non-walkable).
    /// Use SetTile to carve rooms and corridors.
    /// </summary>
    public GameMap(int width, int height, bool allWalls)
    {
        Width = width;
        Height = height;
        _walkable = new bool[width, height];
        _tiles = new TileKind[width, height];
        _visible = new bool[width, height];
        _explored = new bool[width, height];
        _theme = new TileTheme[width, height]; // zero-initialises to TileTheme.Grey

        if (allWalls)
        {
            // All tiles start as Wall / non-walkable — generator carves from here
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    _walkable[x, y] = false;
                    _tiles[x, y] = TileKind.Wall;
                }
        }
        else
        {
            // Treat same as default constructor
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    _walkable[x, y] = true;
                    _tiles[x, y] = TileKind.Floor;
                }
        }
        // _visible and _explored default to false (not yet seen)
    }

    /// <summary>
    /// Create an enclosed arena (walls on edges, floor inside).
    /// Used by scenario harness — behavior is identical to before TileKind was added.
    /// Writes _walkable directly (not via SetTile) to preserve the exact prior logic.
    /// _tiles is kept in sync manually.
    /// </summary>
    public static GameMap CreateArena(int width, int height)
    {
        var map = new GameMap(width, height);
        for (int x = 0; x < width; x++)
        {
            map._walkable[x, 0] = false;
            map._tiles[x, 0] = TileKind.Wall;
            map._walkable[x, height - 1] = false;
            map._tiles[x, height - 1] = TileKind.Wall;
        }
        for (int y = 0; y < height; y++)
        {
            map._walkable[0, y] = false;
            map._tiles[0, y] = TileKind.Wall;
            map._walkable[width - 1, y] = false;
            map._tiles[width - 1, y] = TileKind.Wall;
        }
        return map;
    }

    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    // --- Line of sight ---

    /// <summary>
    /// Bresenham LOS trace from (x1,y1) to (x2,y2).
    /// Returns true if no opaque tile (Wall/Door/SecretDoor) lies strictly between the two points.
    /// Start and destination tiles are excluded from the opacity check —
    /// both are assumed to be entity positions (walkable or otherwise relevant).
    /// Used by PossessionSystem.CheckVisibilityConstraint.
    /// </summary>
    public bool HasLineOfSight(int x1, int y1, int x2, int y2)
    {
        if (x1 == x2 && y1 == y2) return true;

        int dx = Math.Abs(x2 - x1), dy = Math.Abs(y2 - y1);
        int sx = x1 < x2 ? 1 : -1, sy = y1 < y2 ? 1 : -1;
        int err = dx - dy, cx = x1, cy = y1;

        while (true)
        {
            if (cx == x2 && cy == y2) return true;

            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; cx += sx; }
            if (e2 < dx) { err += dx; cy += sy; }

            // Check intermediate tile (skip source; destination is already the loop exit)
            if (cx != x2 || cy != y2)
            {
                if (!InBounds(cx, cy)) return false;
                var kind = GetTileKind(cx, cy);
                if (kind == TileKind.Wall || kind == TileKind.Door || kind == TileKind.SecretDoor)
                    return false;
            }
        }
    }

    // --- Visibility / FOV ---

    public bool IsVisible(int x, int y) => InBounds(x, y) && _visible[x, y];
    public bool IsExplored(int x, int y) => InBounds(x, y) && _explored[x, y];

    /// <summary>
    /// Mark (x, y) as visible this turn and explored permanently.
    /// Called by FovComputer for each tile in line-of-sight.
    /// </summary>
    public void SetVisible(int x, int y)
    {
        if (!InBounds(x, y)) return;
        _visible[x, y] = true;
        _explored[x, y] = true; // seeing a tile = explored, forever
    }

    /// <summary>
    /// Clear per-turn visibility. Called at the start of each FOV recompute.
    /// Explored state is unaffected — explored tiles are never un-explored.
    /// </summary>
    public void ClearAllVisible()
    {
        Array.Clear(_visible, 0, _visible.Length);
    }

    /// <summary>
    /// Mark all tiles visible and explored. Used for scenarios (no fog of war).
    /// Called by GameStateFactory.FromScenario after map creation.
    /// The policy "scenarios have no fog" lives at the factory level, not here.
    /// </summary>
    public void RevealAll()
    {
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                _visible[x, y] = true;
                _explored[x, y] = true;
            }
    }

    public bool IsWalkable(int x, int y) => InBounds(x, y) && _walkable[x, y] && !_propCells.Contains((x, y));

    /// <summary>
    /// Mark a grid cell as occupied by a blocking prop.
    /// Called during dungeon generation after prop placement.
    /// </summary>
    public void MarkPropCell(int x, int y)
    {
        if (InBounds(x, y)) _propCells.Add((x, y));
    }

    /// <summary>Returns true if a blocking prop occupies this cell.</summary>
    public bool IsPropCell(int x, int y) => _propCells.Contains((x, y));

    /// <summary>
    /// Remove a prop cell marking. Used during connectivity repair when a
    /// previously placed prop is removed to restore passage.
    /// </summary>
    public void UnmarkPropCell(int x, int y) => _propCells.Remove((x, y));

    /// <summary>
    /// Returns true for Wall or SecretDoor tiles.
    /// Used by the wall autotile renderer — does NOT include prop cells or closed doors.
    /// Closed doors are non-walkable but must not be treated as walls for autotile purposes.
    /// SecretDoors render identically to walls until discovered, so they must contribute to
    /// the autotile bitmask the same way walls do — otherwise adjacent wall tiles would have
    /// incorrect corner fills where the secret door sits.
    /// </summary>
    public bool IsWallTile(int x, int y) => InBounds(x, y)
        && (_tiles[x, y] == TileKind.Wall || _tiles[x, y] == TileKind.SecretDoor);

    /// <summary>Returns true for Door (closed), DoorOpen, or LockedDoor.</summary>
    public bool IsDoorTile(int x, int y) =>
        InBounds(x, y) && (_tiles[x, y] == TileKind.Door
            || _tiles[x, y] == TileKind.DoorOpen
            || _tiles[x, y] == TileKind.LockedDoor);

    /// <summary>
    /// Open a closed door at (x, y). Changes Door → DoorOpen (walkable, LOS-transparent).
    /// No-op if the tile is not a closed Door. Returns true if a door was opened.
    /// </summary>
    public bool OpenDoor(int x, int y)
    {
        if (!InBounds(x, y) || _tiles[x, y] != TileKind.Door) return false;
        SetTile(x, y, TileKind.DoorOpen);
        return true;
    }

    /// <summary>Returns the tile kind at (x, y). Returns Wall if out of bounds.</summary>
    public TileKind GetTileKind(int x, int y)
    {
        if (!InBounds(x, y)) return TileKind.Wall;
        return _tiles[x, y];
    }

    /// <summary>
    /// Set the tile kind and keep _walkable in sync.
    /// Floor, Corridor, StairDown, StairUp, Door = walkable.
    /// Wall, Trap = not walkable.
    /// NOTE: CreateArena writes _walkable directly and bypasses this — that is intentional.
    /// </summary>
    public void SetTile(int x, int y, TileKind kind)
    {
        if (!InBounds(x, y)) return;
        _tiles[x, y] = kind;
        _walkable[x, y] = kind switch
        {
            TileKind.Floor => true,
            TileKind.Corridor => true,
            TileKind.StairDown => true,
            TileKind.StairUp => true,
            TileKind.Door => false,        // closed door blocks movement until opened
            TileKind.DoorOpen => true,     // open door is walkable
            TileKind.Wall => false,
            TileKind.Trap => false,
            TileKind.LockedDoor => false,  // locked door: impassable until matching key used; never in canPassDoors
            TileKind.SecretDoor => false,  // secret door: looks and behaves like a wall until discovered
            _ => false,
        };
    }

    // --- Tile theme ---

    /// <summary>Returns the visual theme for a tile. Defaults to Grey if out of bounds.</summary>
    public TileTheme GetTileTheme(int x, int y) =>
        InBounds(x, y) ? _theme[x, y] : TileTheme.Grey;

    /// <summary>Set the visual theme for a single tile.</summary>
    public void SetTileTheme(int x, int y, TileTheme theme)
    {
        if (InBounds(x, y)) _theme[x, y] = theme;
    }

    /// <summary>Set theme for a rectangular region (inclusive bounds).</summary>
    public void SetTileThemeRect(int x1, int y1, int x2, int y2, TileTheme theme)
    {
        for (int x = x1; x <= x2; x++)
            for (int y = y1; y <= y2; y++)
                SetTileTheme(x, y, theme);
    }

    /// <summary>All entities currently registered on this map (players, monsters, floor items, stairs).</summary>
    public IReadOnlyList<Entity> Entities => _entities;

    public void RegisterEntity(Entity entity) => _entities.Add(entity);
    public void UnregisterEntity(Entity entity) => _entities.Remove(entity);

    // ── Mid-run serialization (M1.4) ──────────────────────────────────────────
    // The five per-cell grids are private; the serializer exports/restores them row-major
    // (x outer, y inner) for byte-stable output. _entities and _propCells are DERIVED — the
    // loader rebuilds them by re-registering entities / re-marking prop cells.

    /// <summary>Flattened row-major snapshot of the five per-cell grids for the mid-run save.</summary>
    public readonly record struct GridSnapshot(
        TileKind[] Tiles, bool[] Walkable, bool[] Visible, bool[] Explored, TileTheme[] Theme);

    public GridSnapshot ExportGrids()
    {
        int n = Width * Height;
        var tiles = new TileKind[n];
        var walk = new bool[n];
        var vis = new bool[n];
        var expl = new bool[n];
        var theme = new TileTheme[n];
        int i = 0;
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++, i++)
            {
                tiles[i] = _tiles[x, y];
                walk[i] = _walkable[x, y];
                vis[i] = _visible[x, y];
                expl[i] = _explored[x, y];
                theme[i] = _theme[x, y];
            }
        return new GridSnapshot(tiles, walk, vis, expl, theme);
    }

    /// <summary>Ids of entities registered on the map for occupancy (blocking) checks. This set is
    /// dynamic — dropped items, split children, portals etc. register/unregister during play, so it
    /// is NOT derivable from GameState list membership and must be serialized. Sorted for byte-stability;
    /// occupancy tests are order-independent.</summary>
    public IReadOnlyList<int> RegisteredEntityIds => _entities.Select(e => e.Id).OrderBy(i => i).ToList();

    /// <summary>Prop-blocked cells (barrels/bookshelves), sorted. Empty in scenario mode.</summary>
    public IReadOnlyList<(int X, int Y)> PropCells => _propCells.OrderBy(c => c.Item1).ThenBy(c => c.Item2).ToList();

    public void RestoreGrids(GridSnapshot g)
    {
        int n = Width * Height;
        if (g.Tiles.Length != n || g.Walkable.Length != n || g.Visible.Length != n
            || g.Explored.Length != n || g.Theme.Length != n)
            throw new InvalidOperationException(
                $"Mid-run map restore: grid length mismatch for a {Width}x{Height} map.");
        int i = 0;
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++, i++)
            {
                _tiles[x, y] = g.Tiles[i];
                _walkable[x, y] = g.Walkable[i];
                _visible[x, y] = g.Visible[i];
                _explored[x, y] = g.Explored[i];
                _theme[x, y] = g.Theme[i];
            }
    }

    /// <summary>Check if a tile is blocked by a living, movement-blocking entity.</summary>
    public bool IsBlocked(int x, int y)
    {
        if (!IsWalkable(x, y)) return true;
        return _entities.Any(e => e.X == x && e.Y == y && e.BlocksMovement && IsEntityAlive(e));
    }

    /// <summary>Check if a tile is free to move into.</summary>
    public bool CanMoveTo(int x, int y) => IsWalkable(x, y) &&
        !_entities.Any(e => e.X == x && e.Y == y && e.BlocksMovement && IsEntityAlive(e));

    /// <summary>
    /// Check if a tile can be moved into, with optional exclusions.
    /// excludeEntity: this entity is not counted as a blocker (an entity can't block itself).
    /// ignoreEntityAtDest: when true, entity blocking at (x,y) is ignored — allows pathing TO an occupied tile.
    /// canPassDoors: when true, closed Door tiles count as passable for pathfinding purposes.
    ///   The door is NOT opened by this check — callers must call OpenDoor before actually moving through.
    /// </summary>
    public bool CanMoveToWith(int x, int y, Entity? excludeEntity, bool ignoreEntityAtDest = false, bool canPassDoors = false)
    {
        bool passable = IsWalkable(x, y) || (canPassDoors && InBounds(x, y) && _tiles[x, y] == TileKind.Door);
        if (!passable) return false;
        return !_entities.Any(e =>
            e.X == x && e.Y == y
            && e.BlocksMovement
            && IsEntityAlive(e)
            && (excludeEntity == null || e.Id != excludeEntity.Id)
            && !ignoreEntityAtDest);
    }

    private static bool IsEntityAlive(Entity e)
    {
        var fighter = e.Get<Combat.Fighter>();
        return fighter == null || fighter.IsAlive; // non-fighters don't block based on alive status
    }

    /// <summary>
    /// Move entity one step toward target using simple greedy pathfinding.
    /// Tries direct path first, then axis-aligned alternatives.
    /// Returns true if the entity moved.
    /// </summary>
    public bool MoveToward(Entity mover, int targetX, int targetY)
    {
        int dx = Math.Sign(targetX - mover.X);
        int dy = Math.Sign(targetY - mover.Y);

        // Try diagonal first
        if (dx != 0 && dy != 0 && CanMoveTo(mover.X + dx, mover.Y + dy))
        {
            mover.X += dx;
            mover.Y += dy;
            return true;
        }

        // Try horizontal
        if (dx != 0 && CanMoveTo(mover.X + dx, mover.Y))
        {
            mover.X += dx;
            return true;
        }

        // Try vertical
        if (dy != 0 && CanMoveTo(mover.X, mover.Y + dy))
        {
            mover.Y += dy;
            return true;
        }

        return false;
    }
}
