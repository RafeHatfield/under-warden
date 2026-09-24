namespace UnderWarden.Logic.Balance.Etp;

/// <summary>
/// Room and floor ETP budget validation.
/// Ported from ~/development/rlike/balance/etp.py:617-673 (check_room_budget).
///
/// Status taxonomy:
///   OK    — within [budget_min, budget_max] (with tolerance)
///   UNDER — total below min × (1 - tolerance)
///   OVER  — total above max × (1 + tolerance)
/// </summary>
public static class EtpBudgetChecker
{
    public sealed record RoomCheckResult(
        bool IsValid,
        string Status,           // "OK" | "UNDER" | "OVER"
        double TotalEtp,
        double BudgetMin,
        double BudgetMax,
        double DeviationPct,
        string Message);

    /// <summary>
    /// Check whether totalEtp is within the room budget for this depth.
    ///
    /// Tolerance is applied: budgets are treated as soft targets.
    ///   effective_min = budget_min × (1 - room_tolerance)  [but 0 stays 0]
    ///   effective_max = budget_max × (1 + room_tolerance)
    ///
    /// allowSpike extends the max by spike_multiplier (default 1.5) BEFORE
    /// tolerance is applied.
    ///
    /// PoC: check_room_budget() in etp.py:617-673.
    /// </summary>
    public static RoomCheckResult CheckRoom(
        EtpConfig cfg,
        double totalEtp,
        int depth,
        string roomId = "",
        bool allowSpike = false)
    {
        var (budgetMin, budgetMax) = EtpCalculator.GetRoomEtpBudget(cfg, depth, allowSpike);
        double tolerance   = cfg.Tolerance.RoomTolerance;

        // Empty-room case: budget_min is 0 and no monsters
        if (budgetMin <= 0 && totalEtp <= 0)
        {
            return new RoomCheckResult(
                true, "OK", 0, budgetMin, budgetMax, 0,
                "Empty room (no monsters)");
        }

        // Apply tolerance to get effective boundaries
        double effectiveMin = budgetMin > 0 ? budgetMin * (1.0 - tolerance) : 0;
        double effectiveMax = budgetMax * (1.0 + tolerance);

        // Deviation from midpoint (for diagnostic output)
        double midpoint = (budgetMin + budgetMax) / 2.0;
        double deviationPct = midpoint > 0 ? (totalEtp - midpoint) / midpoint : 0;

        if (totalEtp < effectiveMin && budgetMin > 0)
        {
            return new RoomCheckResult(
                false, "UNDER", totalEtp, budgetMin, budgetMax, deviationPct,
                $"Room {roomId}: ETP {totalEtp:F1} below minimum {effectiveMin:F1} (budget: [{budgetMin},{budgetMax}])");
        }

        if (totalEtp > effectiveMax)
        {
            return new RoomCheckResult(
                false, "OVER", totalEtp, budgetMin, budgetMax, deviationPct,
                $"Room {roomId}: ETP {totalEtp:F1} exceeds maximum {effectiveMax:F1} (budget: [{budgetMin},{budgetMax}])");
        }

        return new RoomCheckResult(
            true, "OK", totalEtp, budgetMin, budgetMax, deviationPct,
            $"Room {roomId}: ETP {totalEtp:F1} within budget [{budgetMin},{budgetMax}]");
    }
}
