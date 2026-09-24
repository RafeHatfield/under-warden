namespace UnderWarden.Logic.ECS;

/// <summary>
/// Lifecycle state of a corpse entity.
/// Fresh corpses can be raised; Spent cannot (already-raised zombie that died again);
/// Consumed is the terminal inert state after all possible interactions are exhausted.
/// </summary>
public enum CorpseState
{
    /// <summary>Newly dead. Can be raised by a necromancer or Raise Dead scroll.</summary>
    Fresh,

    /// <summary>
    /// A previously-raised zombie that died again. Cannot be raised.
    /// Future: can be consumed by exploder necromancer (deferred).
    /// </summary>
    Spent,

    /// <summary>Terminal inert state. No further interactions possible.</summary>
    Consumed,
}
