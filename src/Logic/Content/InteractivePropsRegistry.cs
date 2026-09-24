namespace UnderWarden.Logic.Content;

/// <summary>
/// In-memory registry of interactive prop definitions (barrel, bookshelf, bone_pile).
/// Loaded from config/interactive_props.yaml by ContentLoader.
/// Provides O(1) lookup by prop kind ID and an enumerable view of all definitions.
/// Immutable after construction.
/// </summary>
public sealed class InteractivePropsRegistry
{
    private readonly Dictionary<string, InteractivePropDefinition> _props;
    private readonly Dictionary<string, TrapPayloadDefinition> _payloads;

    public InteractivePropsRegistry(
        Dictionary<string, InteractivePropDefinition> props,
        Dictionary<string, TrapPayloadDefinition> payloads)
    {
        _props    = props;
        _payloads = payloads;
    }

    /// <summary>
    /// Get a prop definition by ID. Throws if not found — missing prop ID is a content error.
    /// </summary>
    public InteractivePropDefinition Get(string id)
    {
        if (_props.TryGetValue(id, out var def))
            return def;
        throw new KeyNotFoundException(
            $"InteractivePropsRegistry: unknown prop kind '{id}'. " +
            $"Available: {string.Join(", ", _props.Keys)}");
    }

    public bool TryGet(string id, out InteractivePropDefinition def)
        => _props.TryGetValue(id, out def!);

    /// <summary>
    /// Get a named trap payload definition. Throws if not found.
    /// </summary>
    public TrapPayloadDefinition GetPayload(string id)
    {
        if (_payloads.TryGetValue(id, out var def))
            return def;
        throw new KeyNotFoundException(
            $"InteractivePropsRegistry: unknown trap payload '{id}'. " +
            $"Available: {string.Join(", ", _payloads.Keys)}");
    }

    public bool TryGetPayload(string id, out TrapPayloadDefinition def)
        => _payloads.TryGetValue(id, out def!);

    public IReadOnlyDictionary<string, InteractivePropDefinition> AllProps => _props;
    public IReadOnlyDictionary<string, TrapPayloadDefinition> AllPayloads => _payloads;
}
