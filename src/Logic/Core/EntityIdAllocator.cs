namespace UnderWarden.Logic.Core;

/// <summary>
/// Simple auto-incrementing ID source for dungeon generation.
///
/// Scenarios use explicit IDs: 0 = player, 1+ = monsters (defined in YAML).
/// Dungeon generation uses this allocator to produce IDs that cannot collide with
/// each other within a floor. Start from a value above the expected scenario range
/// (default 1000) if mixing with scenario entities; or start from 1 for pure dungeon floors.
/// </summary>
public sealed class EntityIdAllocator
{
    private int _next;

    public EntityIdAllocator(int startFrom = 1)
    {
        _next = startFrom;
    }

    /// <summary>Returns the next unique ID and advances the counter.</summary>
    public int Next() => _next++;

    /// <summary>The next ID that will be returned by Next() (for diagnostics).</summary>
    public int Peek => _next;
}
