namespace UnderWarden.Logic.Voice;

/// <summary>
/// Silence mode for voice delivery (docs/systems/voice_delivery.md §Silence rules). This is a DEVICE
/// SETTING owned by the presentation layer (5b) and MUST survive across runs — it is passed into
/// <see cref="VoiceScheduler.TryDeliver"/> per call and is deliberately NOT part of the run save.
/// </summary>
public enum VoiceMode
{
    /// <summary>All tiers render.</summary>
    Verbose,

    /// <summary>Only families with tier strictly above the metadata's ambient cutoff render.</summary>
    Tactical,

    /// <summary>Nothing renders (and, per THE ONE RULE, nothing is consumed).</summary>
    Silent,
}
