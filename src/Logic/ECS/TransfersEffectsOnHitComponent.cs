namespace UnderWarden.Logic.ECS;

/// <summary>
/// Marks a monster as able to absorb status effects from its melee target on a successful hit.
///
/// Clone semantics: the effect is copied to the attacker (original stays on target).
/// Guard: only transfers if the attacker does not already have that effect.
/// Supported effects: PoisonEffect, BleedEffect.
///
/// Canonical use case: wraith drain attack — a wraith hitting a poisoned player becomes poisoned.
/// Attached by MonsterFactory when MonsterDefinition.TransfersEffectsOnHit == true.
/// </summary>
public sealed class TransfersEffectsOnHitComponent : IComponent
{
    public Entity? Owner { get; set; }
}
