namespace UnderWarden.Logic.ECS;

/// <summary>
/// Tracks the 3-step portal casting cycle for the Wand of Portals.
/// Attached to the wand entity so the state travels with the item.
///
/// Cycle:
///   Ready          — no portals placed; casting places entrance at caster's feet
///   EntrancePlaced — entrance exists on the map (not yet in state.Portals);
///                    casting requires a location target for the exit
///   BothPlaced     — both portals linked and in state.Portals; casting resets everything
/// </summary>
public enum PortalCastStep { Ready, EntrancePlaced, BothPlaced }

public sealed class PortalCastStateComponent : IComponent
{
    public Entity? Owner { get; set; }

    public PortalCastStep Step { get; set; } = PortalCastStep.Ready;

    /// <summary>
    /// The pending entrance entity when Step = EntrancePlaced.
    /// Registered on the map but NOT in state.Portals until the exit is placed.
    /// Null when not active.
    /// </summary>
    public Entity? PendingEntrance { get; set; }
}
