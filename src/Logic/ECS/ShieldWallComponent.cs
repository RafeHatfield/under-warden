namespace UnderWarden.Logic.ECS;

/// <summary>
/// Grants a skeleton bonus AC for each adjacent (4-way) skeleton ally.
/// SkeletonAI.Decide() scans the four cardinal tiles each turn and writes
/// CurrentAcBonus = adjacentAllies * AcBonusPerAlly.
/// CombatResolver.ResolveAttack() reads CurrentAcBonus when computing targetAc.
/// No state mutation in CombatResolver — SkeletonAI owns the update.
/// </summary>
public sealed class ShieldWallComponent : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>AC granted per adjacent skeleton ally. PoC value: 1.</summary>
    public int AcBonusPerAlly { get; init; } = 1;

    /// <summary>
    /// Cached bonus from the last AI turn. Written by SkeletonAI, read by CombatResolver.
    /// Starts at 0 (no allies adjacent at spawn).
    /// </summary>
    public int CurrentAcBonus { get; set; }
}
