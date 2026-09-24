using System.Text.Json;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Persistence.MidRun;

/// <summary>
/// Serializes an entity graph to a flat, reference-resolved <see cref="EntityTableDto"/> and back
/// (M1.4 §Entity-table serialization). This is the library layer only — GameState-level save/load
/// and file I/O are 4a.3.
///
/// Serialize walks the transitive closure of the given roots (descending into container components
/// so equipped/held item entities are captured even though they sit in no top-level list). Load
/// rebuilds all entities first, then resolves every reference against the table, so identity and
/// cycles are preserved.
/// </summary>
public static class EntitySerializer
{
    /// <summary>Current on-disk schema version for the entity table.</summary>
    public const int SchemaVersion = 1;

    public static EntityTableDto Serialize(IEnumerable<Entity> roots)
    {
        var all = CollectClosure(roots);

        var table = new EntityTableDto { SchemaVersion = SchemaVersion };
        foreach (var entity in all.OrderBy(e => e.Id))
        {
            var record = new EntityRecord
            {
                Id = entity.Id,
                Name = entity.Name,
                X = entity.X,
                Y = entity.Y,
                BlocksMovement = entity.BlocksMovement,
            };

            // Stable component order (by discriminator) so the same state serializes byte-identically.
            foreach (var component in entity.GetAllComponents().OrderBy(c => c.GetType().Name, StringComparer.Ordinal))
            {
                var codec = MidRunComponentRegistry.Get(component.GetType());
                record.Components.Add(new ComponentEnvelope
                {
                    Type = codec.TypeName,
                    Data = codec.Serialize(component),
                });
            }
            table.Entities.Add(record);
        }
        return table;
    }

    /// <summary>
    /// Rebuild the entity graph from a table. Returns the Id → Entity map (identity preserved:
    /// an Id referenced from many places resolves to ONE object).
    /// </summary>
    public static IReadOnlyDictionary<int, Entity> Deserialize(EntityTableDto table)
    {
        // Phase 1 — materialize bare entities so every reference target exists before resolving.
        var byId = new Dictionary<int, Entity>(table.Entities.Count);
        foreach (var record in table.Entities)
        {
            if (byId.ContainsKey(record.Id))
                throw new InvalidOperationException($"Duplicate entity Id {record.Id} in mid-run entity table.");
            byId[record.Id] = new Entity(record.Id, record.Name, record.X, record.Y, record.BlocksMovement);
        }

        // Phase 2 — rebuild components, resolving references against the completed table.
        var resolver = new DictionaryResolver(byId);
        foreach (var record in table.Entities)
        {
            var entity = byId[record.Id];
            foreach (var env in record.Components)
            {
                if (!MidRunComponentRegistry.TryGet(env.Type, out var codec))
                    throw new InvalidOperationException(
                        $"Unknown mid-run component discriminator '{env.Type}' on entity {record.Id}. " +
                        "The save was written by a newer/other build, or a codec was removed.");
                var component = codec.Deserialize(env.Data, resolver);
                entity.Add(component);   // establishes Owner (RECONSTRUCT-class) automatically
            }
        }
        return byId;
    }

    // Transitive closure: roots + every entity reachable through container components.
    private static List<Entity> CollectClosure(IEnumerable<Entity> roots)
    {
        var seen = new Dictionary<int, Entity>();
        var stack = new Stack<Entity>();
        foreach (var r in roots)
            if (r is not null && seen.TryAdd(r.Id, r))
                stack.Push(r);

        while (stack.Count > 0)
        {
            var entity = stack.Pop();
            foreach (var component in entity.GetAllComponents())
            {
                if (!MidRunComponentRegistry.TryGet(component.GetType(), out var codec))
                    throw new InvalidOperationException(
                        $"No mid-run codec for component '{component.GetType().FullName}' on entity {entity.Id}.");
                foreach (var child in codec.Children(component))
                    if (child is not null && seen.TryAdd(child.Id, child))
                        stack.Push(child);
            }
        }
        return seen.Values.ToList();
    }

    private sealed class DictionaryResolver(IReadOnlyDictionary<int, Entity> byId) : IEntityResolver
    {
        public Entity Resolve(int id) =>
            byId.TryGetValue(id, out var e)
                ? e
                : throw new InvalidOperationException(
                    $"Mid-run load: entity reference to Id {id} not found in the table (incomplete save).");

        public Entity? ResolveOptional(int? id) => id is null ? null : Resolve(id.Value);
    }
}
