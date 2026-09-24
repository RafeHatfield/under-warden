namespace UnderWarden.Logic.ECS;

/// <summary>
/// Tracks monster aggro state. Added to a monster when it first spots the player.
/// Alerted monsters continue pursuing toward the player's last known position even
/// after losing line of sight — they de-aggro after a fixed number of turns without
/// re-sighting the player.
/// </summary>
public sealed class AlertedState : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>Last known player position this monster is moving toward.</summary>
    public int LastKnownPlayerX { get; set; }
    public int LastKnownPlayerY { get; set; }

    /// <summary>Turns remaining before this monster gives up the chase.</summary>
    public int TurnsUntilDeaggro { get; set; }

    /// <summary>How many turns a monster pursues after losing sight of the player.</summary>
    public const int DeaggroTurns = 8;
}
