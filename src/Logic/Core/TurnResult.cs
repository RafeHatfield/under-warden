namespace UnderWarden.Logic.Core;

/// <summary>
/// The complete result of processing one turn. Contains all events in order.
/// The presentation layer animates these; the harness derives metrics from them.
/// </summary>
public sealed class TurnResult
{
    public int TurnNumber { get; init; }
    public List<TurnEvent> Events { get; init; } = new();
    public bool GameOver { get; init; }
    public bool PlayerDied { get; init; }
    public bool AllMonstersDefeated { get; init; }
}
