using UnderWarden.Logic.Content;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Legacy ETP facade — thin wrapper over Balance.Etp.EtpCalculator.
/// Preserved for backward compatibility with EntityPlacer and existing callers.
/// New code should use UnderWarden.Logic.Balance.Etp.EtpCalculator directly.
/// </summary>
public static class EtpCalculator
{
    /// <summary>ETP cost of spawning one instance of this monster.</summary>
    public static int GetEtp(MonsterDefinition def) => def.EtpBase;

    /// <summary>
    /// True if adding addEtp to currentEtp stays within the room budget.
    /// When allowSpike is true, budget is relaxed to 150%.
    /// </summary>
    public static bool FitsInBudget(int currentEtp, int addEtp, int maxEtp, bool allowSpike)
        => (currentEtp + addEtp) <= (allowSpike ? (int)(maxEtp * 1.5) : maxEtp);
}
