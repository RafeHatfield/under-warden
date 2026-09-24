namespace UnderWarden.Logic.ECS;

/// <summary>
/// Marks an entity as having acid-based attacks that can corrode metal weapons.
/// On each successful hit against the player, a corrosion roll is made against Chance.
/// </summary>
public sealed class CorrosionComponent : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>
    /// Probability [0.0–1.0] of corroding the player's equipped metal weapon on each hit.
    /// E.g. 0.05 = 5% per hit for slime, 0.10 = 10% for large_slime.
    /// </summary>
    public double Chance { get; }

    public CorrosionComponent(double chance) => Chance = chance;
}
