namespace UnderWarden.Logic.Balance;

/// <summary>
/// A single captured event in a run transcript (D1 narrative testing).
/// </summary>
public sealed class TranscriptEntry
{
    /// <summary>Dungeon depth (1-based) this event occurred on.</summary>
    public int Depth { get; init; }

    /// <summary>Turn number within the current floor (1-based at turn completion).</summary>
    public int FloorTurn { get; init; }

    /// <summary>
    /// Category label for the event.
    /// Values: "floor_enter", "voice", "monster_killed", "player_died",
    ///         "descended", "trap_triggered"
    /// </summary>
    public string EventType { get; init; } = "";

    /// <summary>Human-readable detail string (trigger ID, entity name, cause).</summary>
    public string Detail { get; init; } = "";
}
