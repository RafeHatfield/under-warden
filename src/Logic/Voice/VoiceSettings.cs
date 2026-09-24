namespace UnderWarden.Logic.Voice;

/// <summary>Ribbon display duration (docs/systems/voice_delivery.md §Ribbon contract).</summary>
public enum VoiceDuration { Short, Normal, Long }

/// <summary>
/// How a ribbon line leaves the screen. <see cref="Manual"/> (default) keeps each line until the player
/// taps it — so nothing is missed mid-fight; <see cref="Timed"/> auto-fades after <c>DurationSeconds</c>.
/// </summary>
public enum VoiceDismiss { Manual, Timed }

/// <summary>
/// DEVICE settings for voice delivery (silence mode + ribbon duration + dismiss mode). These survive
/// across runs and are stored OUTSIDE the run save (see the presentation VoiceSettingsStore). Pure +
/// Godot-free so the round-trip is headlessly testable; serialization is hand-rolled (enum fields) to
/// stay NativeAOT safe with no reflection.
/// </summary>
public sealed record VoiceSettings(
    VoiceMode Mode = VoiceMode.Verbose,
    VoiceDuration Duration = VoiceDuration.Normal,
    VoiceDismiss Dismiss = VoiceDismiss.Manual)
{
    /// <summary>Seconds the ribbon holds a line before auto-dismiss: Short 2.5 / Normal 4 / Long 6.</summary>
    public float DurationSeconds => Duration switch
    {
        VoiceDuration.Short => 2.5f,
        VoiceDuration.Long => 6f,
        _ => 4f,
    };

    public VoiceMode CycleMode() => Mode switch
    {
        VoiceMode.Verbose => VoiceMode.Tactical,
        VoiceMode.Tactical => VoiceMode.Silent,
        _ => VoiceMode.Verbose,
    };

    public VoiceDuration CycleDuration() => Duration switch
    {
        VoiceDuration.Short => VoiceDuration.Normal,
        VoiceDuration.Normal => VoiceDuration.Long,
        _ => VoiceDuration.Short,
    };

    public VoiceDismiss CycleDismiss() => Dismiss == VoiceDismiss.Manual ? VoiceDismiss.Timed : VoiceDismiss.Manual;

    /// <summary>Compact, stable JSON (field order fixed) — hand-emitted, no serializer dependency.</summary>
    public string ToJson() => $"{{\"mode\":\"{Mode}\",\"duration\":\"{Duration}\",\"dismiss\":\"{Dismiss}\"}}";

    /// <summary>Parse; unknown/empty input falls back to defaults rather than throwing (settings are best-effort).</summary>
    public static VoiceSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new VoiceSettings();
        var mode = Extract(json, "mode") is { } m && Enum.TryParse<VoiceMode>(m, out var vm) ? vm : VoiceMode.Verbose;
        var dur = Extract(json, "duration") is { } d && Enum.TryParse<VoiceDuration>(d, out var vd) ? vd : VoiceDuration.Normal;
        var dis = Extract(json, "dismiss") is { } s && Enum.TryParse<VoiceDismiss>(s, out var vs) ? vs : VoiceDismiss.Manual;
        return new VoiceSettings(mode, dur, dis);
    }

    // Minimal value extractor for "\"key\":\"value\"" — avoids a JSON dependency for two fields.
    private static string? Extract(string json, string key)
    {
        int k = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (k < 0) return null;
        int colon = json.IndexOf(':', k);
        if (colon < 0) return null;
        int q1 = json.IndexOf('"', colon + 1);
        if (q1 < 0) return null;
        int q2 = json.IndexOf('"', q1 + 1);
        if (q2 < 0) return null;
        return json.Substring(q1 + 1, q2 - q1 - 1);
    }
}
