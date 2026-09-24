using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Persistence.MidRun;

/// <summary>
/// Per-type serialization codec for one concrete <see cref="IComponent"/>. Holds the type-erased
/// map between the live component and its DTO. All mapping code lives here (per-type), never in a
/// reflection serializer — reflection is reserved for the completeness GATE, not this hot path.
/// </summary>
public sealed class ComponentCodec
{
    public required Type ComponentType { get; init; }

    /// <summary>Stable discriminator = the concrete component type's short name.</summary>
    public required string TypeName { get; init; }

    /// <summary>Live component → its serialized data payload.</summary>
    public required Func<IComponent, JsonElement> Serialize { get; init; }

    /// <summary>Data payload + resolver → a fresh live component (Owner unset; set on Entity.Add).</summary>
    public required Func<JsonElement, IEntityResolver, IComponent> Deserialize { get; init; }

    /// <summary>
    /// The child entity *objects* this component owns by reference (Inventory contents, Equipment
    /// slots, chest stash, portal-cast pending entrance). Used by the graph walk to reach entities
    /// that live inside containers rather than in a top-level list. Empty for the vast majority.
    /// </summary>
    public required Func<IComponent, IEnumerable<Entity>> Children { get; init; }
}

/// <summary>
/// The explicit component ↔ DTO registry (M1.4 §Component registry). One codec per concrete
/// IComponent type, keyed by stable type name. Registration is a hand-written, per-type call —
/// the completeness gate (reflection) proves the hand list covers every concrete IComponent.
/// </summary>
public static class MidRunComponentRegistry
{
    private static readonly Dictionary<string, ComponentCodec> ByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, ComponentCodec> ByType = new();

    static MidRunComponentRegistry() => MidRunComponentRegistrations.RegisterAll();

    public static IReadOnlyCollection<ComponentCodec> All => ByName.Values;

    public static bool TryGet(Type componentType, out ComponentCodec codec) =>
        ByType.TryGetValue(componentType, out codec!);

    public static bool TryGet(string typeName, out ComponentCodec codec) =>
        ByName.TryGetValue(typeName, out codec!);

    public static ComponentCodec Get(Type componentType) =>
        TryGet(componentType, out var c)
            ? c
            : throw new InvalidOperationException(
                $"No mid-run serialization codec registered for component type '{componentType.FullName}'. " +
                "Add a registration in MidRunComponentRegistrations (the completeness gate should have caught this).");

    /// <summary>
    /// Register one component type. <paramref name="toDto"/>/<paramref name="fromDto"/> are the only
    /// place the mapping lives; <paramref name="typeInfo"/> is the source-generated serializer for the DTO.
    /// </summary>
    internal static void Register<TComponent, TDto>(
        string typeName,
        JsonTypeInfo<TDto> typeInfo,
        Func<TComponent, TDto> toDto,
        Func<TDto, IEntityResolver, TComponent> fromDto,
        Func<TComponent, IEnumerable<Entity>>? children = null)
        where TComponent : class, IComponent
    {
        if (ByName.ContainsKey(typeName))
            throw new InvalidOperationException($"Duplicate mid-run component discriminator '{typeName}'.");

        var codec = new ComponentCodec
        {
            ComponentType = typeof(TComponent),
            TypeName = typeName,
            Serialize = c => JsonSerializer.SerializeToElement(toDto((TComponent)c), typeInfo),
            Deserialize = (el, resolver) => fromDto(el.Deserialize(typeInfo)!, resolver),
            Children = children is null
                ? _ => Array.Empty<Entity>()
                : c => children((TComponent)c),
        };
        ByName[typeName] = codec;
        ByType[typeof(TComponent)] = codec;
    }
}
