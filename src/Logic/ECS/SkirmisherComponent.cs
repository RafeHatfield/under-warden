namespace UnderWarden.Logic.ECS;

/// <summary>
/// Tracks state for the Orc Skirmisher's Pouncing Leap ability.
/// Attached by MonsterFactory when ai_type is "skirmisher".
/// The leap triggers when the skirmisher is 3-6 tiles from the player and the cooldown is 0.
/// On leap: moves 2 tiles toward player, sets cooldown = LeapCooldownTurns.
/// </summary>
public sealed class SkirmisherComponent : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>Turns remaining before the next leap. 0 = ready.</summary>
    public int LeapCooldownRemaining { get; set; } = 0;

    /// <summary>Full cooldown applied after a successful leap. PoC value: 3.</summary>
    public int LeapCooldownTurns { get; set; } = 3;

    /// <summary>Minimum Chebyshev distance to player for leap to trigger. PoC value: 3.</summary>
    public int LeapRangeMin { get; set; } = 3;

    /// <summary>Maximum Chebyshev distance to player for leap to trigger. PoC value: 6.</summary>
    public int LeapRangeMax { get; set; } = 6;
}
