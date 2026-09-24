namespace UnderWarden.Logic.ECS;

/// <summary>
/// Sticky marker. Added to a monster the moment it attacks the player (hit or miss) — it chose
/// violence. Never removed for the monster's lifetime: it survives deaggro and loss of line of
/// sight (a monster that chased Sasha and then fled still chose violence first).
///
/// This is the predicate for an "unprovoked" kill (plan_end_game decision 2 / TASK-003): a player
/// kill of a monster WITHOUT this tag is unprovoked — Sasha killed something that would have let
/// him pass. The signal measures the monster's choice, not the player's tactics or the kill
/// circumstances, which is why awareness- or first-strike-based predicates were rejected.
///
/// One predicate, three consumers: the Auditor's Own Guardian (all factions), the Oathkeeper
/// Guardian (orc subset), and the orc faction-reputation Hostile transition.
/// </summary>
public sealed class HasAttackedPlayerTag : IComponent
{
    public Entity? Owner { get; set; }
}
