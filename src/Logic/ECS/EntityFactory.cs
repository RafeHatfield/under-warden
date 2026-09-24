namespace UnderWarden.Logic.ECS;

/// <summary>
/// Creates entities with sequential IDs. Single source of truth for entity creation.
/// </summary>
public sealed class EntityFactory
{
    private int _nextId;

    public EntityFactory(int startId = 1)
    {
        _nextId = startId;
    }

    /// <summary>Create a bare entity with a name and position.</summary>
    public Entity Create(string name, int x = 0, int y = 0, bool blocksMovement = false)
    {
        return new Entity(_nextId++, name, x, y, blocksMovement);
    }

    /// <summary>The next ID that will be assigned.</summary>
    public int NextId => _nextId;
}
