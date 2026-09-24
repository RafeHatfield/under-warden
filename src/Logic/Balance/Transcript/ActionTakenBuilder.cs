using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Balance.Transcript;

/// <summary>
/// Flattens a resolved <see cref="PlayerAction"/> into a serializable
/// <see cref="ActionTaken"/> — entity references become deterministic-under-seed
/// IDs plus stable type strings. This is the replay prerequisite: game state at
/// turn N is reproducible from (seed + the action_taken sequence for turns 0..N-1).
/// </summary>
public static class ActionTakenBuilder
{
    public static ActionTaken From(PlayerAction action)
    {
        return new ActionTaken
        {
            Kind             = action.Kind.ToString(),
            TargetEntityId   = action.Target?.Id,
            TargetEntityType = TypeIdOf(action.Target),
            TargetX          = action.TargetX,
            TargetY          = action.TargetY,
            TargetX2         = action.TargetX2,
            TargetY2         = action.TargetY2,
            ItemEntityId     = action.Item?.Id,
            ItemTypeId       = TypeIdOf(action.Item),
            Slot             = action.Slot?.ToString(),
            AbilityId        = action.AbilityId,
        };
    }

    /// <summary>
    /// Stable type string for an entity: species TypeId for monsters, item TypeId
    /// for items. Falls back to the entity Name when no tag is present (e.g. the
    /// player or a bare entity). Null for a null entity.
    /// </summary>
    private static string? TypeIdOf(Entity? entity)
    {
        if (entity == null) return null;
        var species = entity.Get<SpeciesTag>();
        if (species != null) return species.TypeId;
        var item = entity.Get<ItemTag>();
        if (item != null) return item.TypeId;
        return entity.Name;
    }
}
