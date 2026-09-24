namespace UnderWarden.Logic.ECS;

/// <summary>
/// Tag component: this monster applies EngulfedEffect to the player on any successful melee hit.
/// Attached at spawn by MonsterFactory when the monster definition has engulfs_on_hit: true.
///
/// Used by StatusEffectProcessor.ProcessTurnStart for the adjacency refresh check (is any
/// monster with this tag adjacent to the engulfed entity?), and by TurnController.ResolveMonsterAttack
/// to apply the effect on hit.
///
/// Slime hierarchy: slime, large_slime, greater_slime all carry this tag.
/// </summary>
public sealed class EngulfsOnHitTag : IComponent
{
    public Entity? Owner { get; set; }
}
