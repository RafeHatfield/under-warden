namespace UnderWarden.Logic.ECS;

/// <summary>
/// Marker component. Entity is immune to slow (SlowedEffect) and paralysis (ImmobilizedEffect)
/// while this component is present.
///
/// Added/removed by TurnController.ApplyRingEffect when the ring_of_free_action is equipped
/// or unequipped. StatusEffectProcessor checks for this tag before applying slow/paralysis.
/// </summary>
public sealed class FreeActionTag : IComponent
{
    public Entity? Owner { get; set; }
}
