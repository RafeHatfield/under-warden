namespace UnderWarden.Logic.Balance;

/// <summary>
/// Classifies the outcome of a single dungeon run into a structured triple:
/// (outcome, failureType, failureDetail).
///
/// Port of PoC's SoakRunResult.classify_failure() (engine/soak_harness.py).
/// Simplified: no libtcod concerns, no auto-explore distinction.
/// Exception outcomes are handled by RunSoak() directly — Classify() only
/// handles the four in-game outcomes.
///
/// Pure static function — no side effects, no state.
/// </summary>
public static class OutcomeClassifier
{
    // Outcome string constants — matches PoC convention
    public const string Survived   = "survived";
    public const string Died       = "died";
    public const string MaxTurns   = "max_turns";
    public const string Stuck      = "stuck";
    public const string Exception  = "exception";

    /// <summary>
    /// Outcome when BotBrain's stuck detection fired (stuck counter >= 15 turns).
    /// Treated as death-equivalent for Death% and PressureModel calculations.
    /// Downstream consumers: treat "aborted" the same as "died" for aggregate metrics.
    /// </summary>
    public const string Aborted = "aborted";

    // FailureType string constants
    public const string FailureNone      = "none";
    public const string FailureDeath     = "death";
    public const string FailureMaxTurns  = "max_turns";
    public const string FailureStuck     = "stuck";
    public const string FailureException = "exception";

    /// <summary>FailureType when BotBrain aborted the run via stuck detection.</summary>
    public const string FailureAborted = "aborted";

    /// <summary>
    /// Classify the outcome of a dungeon run.
    ///
    /// Classification order (highest priority first):
    ///   1. Player died → outcome="died", failure_type="death", detail=killerName
    ///   2. All floors completed → outcome="survived", failure_type="none"
    ///   3. Max turns hit on a floor → outcome="max_turns" (and "stuck" for now —
    ///      Phase 2 will refine once bot telemetry can distinguish the two)
    ///
    /// Note: Exception outcomes are classified by RunSoak() before calling this.
    /// killerName may be null if the killer entity was not found at death time.
    /// </summary>
    /// <param name="result">The completed dungeon run result.</param>
    /// <param name="killerName">Name of the entity that killed the player, or null if unknown.</param>
    /// <returns>Tuple of (outcome, failureType, failureDetail).</returns>
    public static (string outcome, string failureType, string failureDetail) Classify(
        DungeonRunResult result, string? killerName)
    {
        // Priority 1: player died
        if (result.PlayerDied)
        {
            string detail = killerName ?? "unknown";
            return (Died, FailureDeath, detail);
        }

        // Priority 2: all floors completed — player survived the campaign
        if (result.FloorsCompleted >= result.FloorsAttempted)
        {
            return (Survived, FailureNone, "");
        }

        // Priority 3: max turns hit on a floor — either stuck or slow
        // Phase 2 note: without bot telemetry we cannot distinguish "bot is stuck in a loop"
        // from "bot is moving but making very slow progress." Both surface as HitMaxTurns=true.
        // Classify as "max_turns" for now; "stuck" is reserved for a distinct Phase 2 detector.
        var maxTurnsFloor = result.PerFloor.FirstOrDefault(f => f.HitMaxTurns);
        if (maxTurnsFloor != null)
        {
            string detail = $"Floor {maxTurnsFloor.Depth}: hit turn limit";
            return (MaxTurns, FailureMaxTurns, detail);
        }

        // Fallback: run ended early without a clear cause — treat as stuck
        // This path should be rare but is here for safety.
        return (Stuck, FailureStuck, "run ended without completing floors");
    }
}
