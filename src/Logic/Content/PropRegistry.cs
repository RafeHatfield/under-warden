namespace UnderWarden.Logic.Content;

/// <summary>
/// In-memory registry of all prop definitions loaded from config/props.yaml.
/// Provides O(1) lookup by prop ID and an enumerable view of all props.
/// Immutable after construction — props are static content data.
/// </summary>
public sealed class PropRegistry
{
    private readonly Dictionary<string, PropDefinition> _props;

    public PropRegistry(Dictionary<string, PropDefinition> props)
    {
        _props = props;
    }

    public bool TryGet(string id, out PropDefinition def)
        => _props.TryGetValue(id, out def!);

    public PropDefinition? Get(string id)
        => _props.TryGetValue(id, out var def) ? def : null;

    public IReadOnlyDictionary<string, PropDefinition> All => _props;
}
