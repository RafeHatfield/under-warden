namespace UnderWarden.Logic.ECS;

/// <summary>
/// Marks a monster as having life drain on melee hit.
/// DrainPct is the fraction of damage dealt that is healed back.
/// Attached by MonsterFactory when MonsterDefinition.LifeDrainPct > 0.
/// </summary>
public sealed class LifeDrainComponent : IComponent
{
    public Entity? Owner { get; set; }
    public double DrainPct { get; }

    public LifeDrainComponent(double drainPct) => DrainPct = drainPct;
}
