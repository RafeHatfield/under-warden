namespace UnderWarden.Logic.ECS;

/// <summary>
/// Marks an entity as having intrinsic regeneration (e.g. troll, troll_ancient).
/// Unlike RegenerationEffect (which is a timed status from rings/potions), this component
/// is permanent and set at monster spawn time.
///
/// AcidEffect specifically suppresses InnateRegenComponent — it does NOT affect
/// RegenerationEffect so player regeneration from rings and potions is unaffected.
///
/// ProcessTurnStart reads this component (when no AcidEffect is present) and heals
/// HealPerTurn, emitting a HotHealEvent with EffectName="innate_regen".
/// </summary>
public sealed class InnateRegenComponent : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>HP healed per turn. Matches regeneration_amount from monster YAML.</summary>
    public int HealPerTurn { get; set; } = 2;
}
