namespace UnderWarden.Logic.Balance;

/// <summary>
/// Immutable definition of a single depth boon. Loaded from config/depth_boons.yaml.
/// All numeric fields default to 0 (no effect).
/// </summary>
public sealed record BoonDefinition(
    string BoonId,
    string DisplayName,
    string Description,
    int HpBonus = 0,
    int ImmediateHeal = 0,
    int AccuracyBonus = 0,
    int DefenseBonus = 0,
    int MinDamageBonus = 0
);
