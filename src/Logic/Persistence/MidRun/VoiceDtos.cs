namespace UnderWarden.Logic.Persistence.MidRun;

/// <summary>
/// SERIALIZE-class run state for the Hollowmark voice scheduler (docs/systems/voice_delivery.md
/// §Save boundary). The registry + tier metadata are RECONSTRUCT (caller-provided on load), never here.
/// The voice Rng is persisted as (Seed, CallCount) exactly like the gameplay stream.
/// </summary>
public sealed record VoiceSchedulerStateDto(
    int RngSeed,
    long RngCallCount,
    VoiceBagDto[] Bags,
    string[] Fired,
    bool CurrentFloorSilenced,
    int LastDeliveredTurn,
    VoiceHistoryDto[] History);

/// <summary>A family's shuffle bag: the remaining draw order (indices into the family's line pool).</summary>
public sealed record VoiceBagDto(string Family, int[] Remaining);

/// <summary>One ribbon-history entry: the family, the delivered line text, and the turn it fired.</summary>
public sealed record VoiceHistoryDto(string Family, string Line, int Turn);
