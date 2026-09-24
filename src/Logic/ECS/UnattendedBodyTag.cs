namespace UnderWarden.Logic.ECS;

/// <summary>
/// Marker tag applied to the home body when possession enters Active state.
/// Signals that the soul has left — the body remains on the map, can take damage,
/// and monsters still target it, but it takes no actions on its own turn.
///
/// Removed by PossessionSystem on any exit transition.
/// Sibling pattern: FreeActionTag.
/// </summary>
public sealed class UnattendedBodyTag : IComponent
{
    public Entity? Owner { get; set; }
}
