namespace UnderWarden.Logic.ECS;

/// <summary>
/// An entity is a named bag of components with a position.
/// Component access is type-safe and O(1) via generics — the concrete type IS the key.
/// </summary>
public sealed class Entity
{
    private readonly Dictionary<Type, IComponent> _components = new();

    public int Id { get; }
    public int X { get; set; }
    public int Y { get; set; }
    public string Name { get; }
    public bool BlocksMovement { get; set; }

    public Entity(int id, string name, int x = 0, int y = 0, bool blocksMovement = false)
    {
        Id = id;
        Name = name;
        X = x;
        Y = y;
        BlocksMovement = blocksMovement;
    }

    /// <summary>
    /// Add a component. Establishes ownership. Replaces any existing component of the same type.
    /// Returns the component for fluent chaining.
    /// Uses the concrete runtime type as the dictionary key (not the generic parameter T).
    /// This means AddRuntime(component) and Add&lt;ConcreteType&gt;(component) are equivalent.
    /// </summary>
    public T Add<T>(T component) where T : class, IComponent
    {
        // Use runtime type so that CopyComponents can add by IComponent without losing type info
        var key = component.GetType();
        if (_components.TryGetValue(key, out var existing))
        {
            existing.Owner = null;
        }
        _components[key] = component;
        component.Owner = this;
        return component;
    }

    /// <summary>
    /// Returns existing component of type T, or creates and adds a new default instance.
    /// </summary>
    public T GetOrAdd<T>() where T : class, IComponent, new()
    {
        var existing = Get<T>();
        if (existing != null) return existing;
        return Add(new T());
    }

    /// <summary>Returns the component of type T, or null if not present.</summary>
    public T? Get<T>() where T : class, IComponent
    {
        return _components.TryGetValue(typeof(T), out var c) ? (T)c : null;
    }

    /// <summary>Returns the component of type T, or throws if not present.</summary>
    public T Require<T>() where T : class, IComponent
    {
        return Get<T>()
            ?? throw new InvalidOperationException(
                $"Entity '{Name}' (id={Id}) missing required component {typeof(T).Name}");
    }

    /// <summary>Returns true if the entity has a component of type T.</summary>
    public bool Has<T>() where T : class, IComponent
    {
        return _components.ContainsKey(typeof(T));
    }

    /// <summary>Remove a component. Clears ownership. Returns true if removed.</summary>
    public bool Remove<T>() where T : class, IComponent
    {
        return RemoveByType(typeof(T));
    }

    /// <summary>
    /// Remove a component by its runtime Type, clearing ownership.
    /// Used by StatusEffectProcessor.RemoveEffect to avoid a per-type switch statement —
    /// the caller has an IStatusEffect interface reference, and GetType() gives the exact key.
    /// Returns true if a component with that type was present and removed.
    /// </summary>
    public bool RemoveByType(Type t)
    {
        if (_components.Remove(t, out var c))
        {
            c.Owner = null;
            return true;
        }
        return false;
    }

    /// <summary>Number of components attached to this entity.</summary>
    public int ComponentCount => _components.Count;

    /// <summary>
    /// Returns all components attached to this entity.
    /// Used by EntityPlacer when re-wrapping factory-created entities under a new ID.
    /// </summary>
    public IEnumerable<IComponent> GetAllComponents() => _components.Values;

    /// <summary>Euclidean distance to a coordinate.</summary>
    public double DistanceTo(int targetX, int targetY)
    {
        int dx = X - targetX;
        int dy = Y - targetY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Chebyshev (king/chessboard) distance — used for melee range checks.</summary>
    public int ChebyshevDistanceTo(int targetX, int targetY)
    {
        return Math.Max(Math.Abs(X - targetX), Math.Abs(Y - targetY));
    }

    public override string ToString() => $"{Name} (id={Id}, {X},{Y})";
}
