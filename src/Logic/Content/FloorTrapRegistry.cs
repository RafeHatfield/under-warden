namespace UnderWarden.Logic.Content;

/// <summary>
/// In-memory registry of floor trap definitions (spike_trap, web_trap, etc.).
/// Loaded from config/floor_traps.yaml by ContentLoader.
/// Provides O(1) lookup by trap type ID.
/// Immutable after construction.
/// </summary>
public sealed class FloorTrapRegistry
{
    private readonly Dictionary<string, FloorTrapDefinition> _traps;

    public FloorTrapRegistry(Dictionary<string, FloorTrapDefinition> traps)
    {
        _traps = traps;
    }

    /// <summary>
    /// Get a trap definition by type ID. Throws if not found — missing trap ID is a content error.
    /// </summary>
    public FloorTrapDefinition Get(string id)
    {
        if (_traps.TryGetValue(id, out var def))
            return def;
        throw new KeyNotFoundException(
            $"FloorTrapRegistry: unknown trap type '{id}'. " +
            $"Available: {string.Join(", ", _traps.Keys)}");
    }

    public bool TryGet(string id, out FloorTrapDefinition def)
        => _traps.TryGetValue(id, out def!);

    public IReadOnlyDictionary<string, FloorTrapDefinition> All => _traps;
}
