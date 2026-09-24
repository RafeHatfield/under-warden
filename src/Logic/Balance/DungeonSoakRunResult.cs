namespace UnderWarden.Logic.Balance;

/// <summary>
/// Per-run result from a dungeon soak session. Captures everything needed for
/// failure classification, survival-curve construction, and JSONL analysis.
///
/// Mutable (not a record) so it can be built incrementally during a run.
/// BotSummary is reserved for Phase 2 telemetry and is always null here.
///
/// Port of PoC's SoakRunResult dataclass (engine/soak_harness.py).
/// </summary>
public sealed class DungeonSoakRunResult
{
    /// <summary>Base seed used for this run (baseSeed + runIndex).</summary>
    public int Seed { get; set; }

    /// <summary>
    /// Final outcome for this run. String constant rather than enum — avoids
    /// serialization friction when writing JSONL and matches PoC convention.
    /// Values: "survived", "died", "max_turns", "stuck", "exception"
    /// </summary>
    public string Outcome { get; set; } = "exception";

    /// <summary>
    /// Structured failure type for bucketed analysis.
    /// Values: "none", "death", "max_turns", "stuck", "exception"
    /// </summary>
    public string FailureType { get; set; } = "exception";

    /// <summary>
    /// Human-readable detail about the failure.
    /// Death: killer entity name (e.g. "orc_brute")
    /// Max_turns: "Floor {depth}: hit {N} turn limit"
    /// Exception: exception message
    /// None: ""
    /// </summary>
    public string FailureDetail { get; set; } = "";

    /// <summary>Deepest floor the player reached this run (1-based).</summary>
    public int DeepestFloorReached { get; set; }

    /// <summary>Number of floors the player cleared and descended from.</summary>
    public int FloorsCompleted { get; set; }

    /// <summary>Total turns taken across all floors.</summary>
    public int TotalTurns { get; set; }

    /// <summary>Total monsters killed across all floors.</summary>
    public int TotalKills { get; set; }

    /// <summary>Player HP at end of run (0 if player died).</summary>
    public int FinalHp { get; set; }

    /// <summary>Player max HP at end of run.</summary>
    public int FinalMaxHp { get; set; }

    /// <summary>FinalHp / FinalMaxHp. 0.0 if player died or MaxHp is zero.</summary>
    public double FinalHpFraction { get; set; }

    /// <summary>
    /// Count of HealEvent instances where ActorId == player.Id across all floors.
    /// Filtered to player only — monster item-use that accidentally heals the player
    /// does NOT count here.
    /// </summary>
    public int PotionsUsed { get; set; }

    /// <summary>Healing potions remaining in player inventory at run end.</summary>
    public int PotionsRemaining { get; set; }

    /// <summary>Number of depth boons applied during this run.</summary>
    public int BoonsAcquired { get; set; }

    /// <summary>
    /// Wall-clock duration of the run in seconds. Useful for detecting performance
    /// regressions (e.g., floor generation time doubling after adding a monster type).
    /// </summary>
    public double DurationSeconds { get; set; }

    /// <summary>Per-floor breakdown. Length == number of floors attempted.</summary>
    public IReadOnlyList<FloorRunMetrics> PerFloor { get; set; } = Array.Empty<FloorRunMetrics>();

    /// <summary>
    /// Bot telemetry summary for this run: action distribution, heal behavior, decision reasons.
    /// Non-null when telemetry was enabled for the run (DungeonRunHarness soak mode).
    /// Null when running without telemetry (legacy Run() calls, tests that don't opt in).
    ///
    /// Serializes as "bot_summary" in JSONL output and is omitted (not null-literal) when absent.
    /// </summary>
    public BotRunSummary? BotSummary { get; set; }

    /// <summary>
    /// Voice line trigger IDs emitted during this run, with emission counts.
    /// Null when no voice lines fired. Serializes as snake_case via harness options.
    /// </summary>
    public Dictionary<string, int>? VoiceLineHits { get; set; }

    /// <summary>
    /// True when BotBrain's stuck detection aborted this run (stuck counter >= 15 turns).
    /// Aborted runs are classified as Outcome="aborted", FailureType="aborted", and count
    /// as death-equivalent in Death% and PressureModel calculations.
    /// </summary>
    public bool WasAborted { get; set; }

    /// <summary>
    /// Persona name used for this run (e.g. "balanced", "aggressive").
    /// Defaults to "balanced" for JSONL files missing this field (forward-compatible reads).
    /// </summary>
    public string Persona { get; set; } = "balanced";
}
