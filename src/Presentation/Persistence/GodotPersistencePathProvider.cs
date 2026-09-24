using UnderWarden.Logic.Persistence;
using Godot;

namespace UnderWarden.Presentation.Persistence;

/// <summary>
/// Resolves persistence file paths using Godot's user:// data directory.
/// Platform-appropriate per Godot conventions:
///   macOS  — ~/Library/Application Support/Godot/app_userdata/&lt;game-name&gt;/
///   iOS    — app sandbox Documents/
///   Windows — %APPDATA%/Godot/app_userdata/&lt;game-name&gt;/
///   Linux  — ~/.local/share/godot/app_userdata/&lt;game-name&gt;/
/// </summary>
public sealed class GodotPersistencePathProvider : IPersistencePathProvider
{
    private readonly string _userDir;

    public GodotPersistencePathProvider()
    {
        _userDir = ProjectSettings.GlobalizePath("user://");
    }

    public string GetMainSaveFilePath() =>
        Path.Combine(_userDir, "yarl_persistence.json");

    public string GetDailySeedsFilePath() =>
        Path.Combine(_userDir, "yarl_daily_seeds.json");

    public string GetSettingsFilePath() =>
        Path.Combine(_userDir, "yarl_settings.json");

    public string GetBackupDirectory() =>
        Path.Combine(_userDir, "backups");

    public string GetMidRunSaveFilePath() =>
        Path.Combine(_userDir, "yarl_midrun.json");
}
