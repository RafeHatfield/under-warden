namespace UnderWarden.Logic.ECS;

/// <summary>
/// Base interface for all components. Components hold data and are owned by an Entity.
/// The type system enforces component identity — each concrete type is its own key.
/// </summary>
public interface IComponent
{
    /// <summary>The entity that owns this component. Set automatically on Add, cleared on Remove.</summary>
    Entity? Owner { get; set; }
}
