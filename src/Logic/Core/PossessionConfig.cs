namespace UnderWarden.Logic.Core;

/// <summary>
/// Tunable knobs for the possession system. All values in one place.
/// Harness sweep targets: drain rate, range, knowledge threshold.
/// </summary>
public static class PossessionConfig
{
    /// <summary>
    /// Maximum Chebyshev distance between host and home body before possession breaks.
    /// v3 spec: 4. Phone-screen-legibility constraint.
    /// </summary>
    public const int MaxPossessionDistance = 4;

    /// <summary>
    /// Minimum turns of voluntary possession required to unlock the Tier-3 species-knowledge trait.
    /// Voluntary exit after this many turns records RecordTrait("possessed_by_player").
    /// Below this threshold, only RecordEngaged fires.
    /// </summary>
    public const int KnowledgeUnlockTurnThreshold = 5;

    /// <summary>
    /// Sentinel entity ID used when the Under-Warden (a non-tile-resident entity) is the possessor.
    /// </summary>
    public const int WardenPossessorSentinelId = -2;

    /// <summary>
    /// Maximum Chebyshev distance the phantom wand position can be from the host entity
    /// before Hollowmark abilities (portals, spell-break) are suppressed.
    /// Separate from MaxPossessionDistance (which is the host-to-home-body constraint).
    /// </summary>
    public const int MaxWandDistance = 4;

    /// <summary>
    /// 1-in-N chance per possession for Hollowmark to voice enter commentary.
    /// Denominator of 4 gives a 25% chance — mostly silent, occasionally speaks.
    /// </summary>
    public const int PossessionEnterVoiceChanceDenominator = 4;

    /// <summary>
    /// 1-in-N chance per adjacent monster per turn to kick the phantom wand.
    /// Denominator of 4 gives a 25% chance, generating average kick rate of ~1.4 kicks
    /// per 5-turn encounter against a typical adjacent pack.
    /// </summary>
    public const int WandKickChanceDenominator = 4;

    /// <summary>
    /// HP/turn drained from the home body while player-initiated possession is active.
    /// Applied once per player turn (host turn), not per monster turn.
    /// Drain cannot kill the home body by default — see safety rail in PossessionSystem.ApplyDrainTick.
    /// </summary>
    public static int DrainPerTurnByDepth(int depth) => depth switch
    {
        <= 8 => 1,
        <= 16 => 2,
        _ => 3,
    };
}
