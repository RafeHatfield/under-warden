namespace UnderWarden.Logic.ECS;

/// <summary>
/// Marks a weapon entity as coated in acid (from acid_trap walk-over).
/// Each successful hit with this weapon applies AcidEffect(duration:6) to the target
/// and decrements HitsRemaining. Component is removed at 0.
///
/// Does NOT interact with slime corrosion — slime corrosion degrades DamageMax,
/// which is a different mechanic. Acid coating is a purely offensive buff.
/// </summary>
public sealed class WeaponAcidCoatingComponent : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>Hits remaining before the coating wears off.</summary>
    public int HitsRemaining { get; set; } = 4;

    /// <summary>Duration of AcidEffect applied to the target per coated hit (turns).</summary>
    public int EffectDuration { get; set; } = 6;
}
