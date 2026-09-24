using UnderWarden.Logic.Voice;
using Godot;

namespace UnderWarden.Presentation.Persistence;

/// <summary>
/// DEVICE-settings store for voice delivery (silence mode + ribbon duration). Persisted to
/// <c>user://voice_settings.json</c> — SEPARATE from the run save, so settings survive across runs and
/// run state never leaks into settings (docs/systems/voice_delivery.md §Save boundary). This is the
/// minimal device-settings store the project needed; no general settings store existed.
///
/// The pure round-trip lives in <see cref="VoiceSettings"/> (headlessly tested); this is only the
/// Godot file IO. Load is best-effort: a missing/garbled file yields defaults (Verbose / Normal).
/// </summary>
public static class VoiceSettingsStore
{
    private const string Path = "user://voice_settings.json";

    public static VoiceSettings Load()
    {
        if (!Godot.FileAccess.FileExists(Path)) return new VoiceSettings();
        using var f = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Read);
        if (f == null)
        {
            GD.PrintErr($"[VoiceSettings] open-for-read failed: {Godot.FileAccess.GetOpenError()}");
            return new VoiceSettings();
        }
        return VoiceSettings.FromJson(f.GetAsText());
    }

    public static void Save(VoiceSettings settings)
    {
        using var f = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
        if (f == null)
        {
            GD.PrintErr($"[VoiceSettings] open-for-write failed: {Godot.FileAccess.GetOpenError()}");
            return;
        }
        f.StoreString(settings.ToJson());
    }
}
