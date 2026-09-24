namespace UnderWarden.Logic.ECS;

/// <summary>
/// Tracks state for the Orc Shaman's Crippling Hex, Chant of Dissonance, and hang-back behavior.
/// Attached by MonsterFactory when ai_type is "orc_shaman".
/// </summary>
public sealed class OrcShamanComponent : IComponent
{
    public Entity? Owner { get; set; }

    // ── Crippling Hex ────────────────────────────────────────────────────────

    /// <summary>Turns remaining before the next hex can fire. 0 = ready.</summary>
    public int HexCooldownRemaining { get; set; } = 0;

    /// <summary>Full cooldown applied after a successful hex. PoC value: 10.</summary>
    public int HexCooldownTurns { get; set; } = 10;

    /// <summary>Maximum range (Chebyshev) for Crippling Hex. PoC value: 6.</summary>
    public int HexRange { get; set; } = 6;

    /// <summary>Duration of the CrippledEffect applied to the player. PoC value: 5 turns.</summary>
    public int HexDuration { get; set; } = 5;

    // ── Chant of Dissonance ─────────────────────────────────────────────────

    /// <summary>True while the shaman is actively channeling the chant.</summary>
    public bool IsChanneling { get; set; } = false;

    /// <summary>Turns remaining in the current channel. 0 when not channeling.</summary>
    public int ChantTurnsRemaining { get; set; } = 0;

    /// <summary>Turns remaining before the next chant can start. 0 = ready.</summary>
    public int ChantCooldownRemaining { get; set; } = 0;

    /// <summary>Full cooldown applied after a chant ends (natural or interrupted). PoC value: 15.</summary>
    public int ChantCooldownTurns { get; set; } = 15;

    /// <summary>Maximum range (Chebyshev) to target the player with Chant. PoC value: 5.</summary>
    public int ChantRange { get; set; } = 5;

    /// <summary>Number of turns the channel lasts. PoC value: 3.</summary>
    public int ChantDuration { get; set; } = 3;

    /// <summary>Entity ID of the current chant target. Null when not channeling.</summary>
    public int? ChantTargetEntityId { get; set; } = null;

    // ── Positioning ──────────────────────────────────────────────────────────

    /// <summary>Minimum preferred distance from player (retreat if closer). PoC value: 4.</summary>
    public int PreferredDistanceMin { get; set; } = 4;

    /// <summary>Maximum preferred distance from player (close if farther). PoC value: 7.</summary>
    public int PreferredDistanceMax { get; set; } = 7;

    /// <summary>Panic radius: if player is this close, always retreat. PoC value: 2.</summary>
    public int DangerRadius { get; set; } = 2;
}
