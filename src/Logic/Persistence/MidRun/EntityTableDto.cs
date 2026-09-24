using System.Text.Json;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Persistence.MidRun;

/// <summary>
/// Flat, reference-resolved serialization of an entity graph (mid-run save, M1.4 §Entity-table).
/// Entities are stored one record per Id; every entity reference anywhere in the graph is an Id,
/// never a nested object. Load rebuilds the table first, then resolves references — so identity is
/// preserved (an entity referenced twice deserializes to ONE object) and cycles are legal.
///
/// Scope (4a.2): this is the entity+component serialization layer only. GameState-level save/load,
/// the map/knowledge/etc. subsystems, file I/O and the S1/S2 soak are 4a.3. Nothing in game code
/// calls this yet.
/// </summary>
public sealed class EntityTableDto
{
    /// <summary>Bumped when the on-disk shape changes. Present from day one per the spec.</summary>
    public int SchemaVersion { get; set; } = 1;

    public List<EntityRecord> Entities { get; set; } = new();
}

/// <summary>One serialized entity: its identity fields plus its components as a typed envelope list.</summary>
public sealed class EntityRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public bool BlocksMovement { get; set; }
    public List<ComponentEnvelope> Components { get; set; } = new();
}

/// <summary>
/// A single component, tagged by its stable type name (the discriminator) with its data payload.
/// The discriminator is the concrete type's short name — NOT assembly-qualified (moves rot) and NOT
/// an enum ordinal (reorders rot). The registry maps the name to the concrete DTO codec.
/// </summary>
public sealed class ComponentEnvelope
{
    public string Type { get; set; } = "";
    public JsonElement Data { get; set; }
}

/// <summary>
/// Resolves an entity Id to the live <see cref="Entity"/> during load. Only the container components
/// (Inventory, Equipment, ChestLootStash, PortalCastState) that hold Entity *objects* use this;
/// plain int-Id fields (e.g. PossessorEntityId) stay as ints and are not resolved here.
/// </summary>
public interface IEntityResolver
{
    /// <summary>Resolve a required reference. Throws if the Id is not in the table (fail-loud on an incomplete save).</summary>
    Entity Resolve(int id);

    /// <summary>Resolve an optional reference; null Id → null.</summary>
    Entity? ResolveOptional(int? id);
}
