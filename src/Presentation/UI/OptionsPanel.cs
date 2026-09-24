using System;
using UnderWarden.Logic.Voice;
using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Options panel shown from the main menu.
///
/// Contains a tileset selector (requires restart) and, in debug builds,
/// a hot-toggle for the debug overlay.
/// </summary>
public sealed partial class OptionsPanel : Control
{
    [Signal] public delegate void BackRequestedEventHandler();

    // The ID that was actually loaded at boot (passed in from Main so we can detect changes).
    private readonly string _loadedTilesetId;

    // Debug overlay reference — null in release builds or when not passed.
    private readonly DebugOverlay? _debugOverlay;

    // Ordered list of (id, display name) pairs. Small and fixed — no need to scan disk.
    private static readonly (string Id, string DisplayName)[] KnownTilesets =
    [
        ("ultimate_fantasy", "Ultimate Fantasy"),
        ("16bit_fantasy",    "16-Bit Fantasy"),
    ];

    // Current selection index into KnownTilesets
    private int _selectedIndex;

    // Live UI references so we can update text after _Ready
    private TouchButton? _cycleBtn;
    private Label?       _restartLabel;
    private TouchButton? _debugOverlayBtn;

    // Voice device settings (M1.5b) — persisted immediately via the callback so they survive across
    // runs, separate from the run save. Null callback → the Voice section is not shown.
    private VoiceSettings _voiceSettings;
    private readonly Action<VoiceSettings>? _onVoiceChanged;
    private TouchButton? _voiceModeBtn;
    private TouchButton? _voiceDurationBtn;
    private TouchButton? _voiceDismissBtn;

    // Dev instrumentation: export the on-disk + in-memory mid-run save. Null (and hidden) in release.
    private readonly Action? _onExportSave;

    // Dev instrumentation: toggle the per-turn voice diagnostic (Phase 1, symptom E). Null in release.
    private readonly Action<bool>? _onVoiceDiagToggle;
    private bool _voiceDiagOn;
    private TouchButton? _voiceDiagBtn;

    public OptionsPanel(string loadedTilesetId, DebugOverlay? debugOverlay = null,
        VoiceSettings? voiceSettings = null, Action<VoiceSettings>? onVoiceChanged = null,
        Action? onExportSave = null, bool voiceDiagOn = false, Action<bool>? onVoiceDiagToggle = null)
    {
        _loadedTilesetId = loadedTilesetId;
        _debugOverlay    = debugOverlay;
        _voiceSettings   = voiceSettings ?? new VoiceSettings();
        _onVoiceChanged  = onVoiceChanged;
        _onExportSave    = onExportSave;
        _voiceDiagOn     = voiceDiagOn;
        _onVoiceDiagToggle = onVoiceDiagToggle;
        // Find the starting selection — default to index 0 if ID not recognised.
        _selectedIndex = FindIndex(loadedTilesetId);
    }

    public override void _Ready()
    {
        BuildLayout();
    }

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        // Semi-transparent dark backdrop — matches MainMenuPanel / other menu panels.
        var bg = new ColorRect
        {
            Color       = new Color(0f, 0f, 0f, 0.85f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Centered content column
        var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        vbox.GrowHorizontal    = GrowDirection.Both;
        vbox.GrowVertical      = GrowDirection.Both;
        vbox.CustomMinimumSize = new Vector2(320, 0);
        vbox.AddThemeConstantOverride("separation", 24);
        AddChild(vbox);

        // Title
        var title = new Label
        {
            Text                = "Options",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        title.AddThemeFontSizeOverride("font_size", 36);
        vbox.AddChild(title);

        // Section label
        var sectionLabel = new Label
        {
            Text                = "Tileset",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        sectionLabel.AddThemeFontSizeOverride("font_size", 18);
        sectionLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f, 1f));
        vbox.AddChild(sectionLabel);

        // Cycle button — label shows current selection, press advances to next.
        _cycleBtn = new TouchButton
        {
            Text              = CycleBtnText(),
            FontSize          = 22,
            BackgroundColor   = new Color(0.25f, 0.25f, 0.35f, 0.95f),
            CustomMinimumSize = new Vector2(280, 56),
        };
        _cycleBtn.Pressed += OnCyclePressed;
        vbox.AddChild(_cycleBtn);

        // "Restart to apply" notice — only visible when a change has been made this session.
        _restartLabel = new Label
        {
            Text                = "Restart to apply",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter         = MouseFilterEnum.Ignore,
            Visible             = false,
        };
        _restartLabel.AddThemeFontSizeOverride("font_size", 16);
        _restartLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.75f, 0.3f, 1f));
        vbox.AddChild(_restartLabel);

        // Voice section (M1.5b) — mode + ribbon duration. Device settings, persisted immediately.
        if (_onVoiceChanged != null)
        {
            var voiceSection = new Label
            {
                Text = "Hollowmark voice",
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            voiceSection.AddThemeFontSizeOverride("font_size", 18);
            voiceSection.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f, 1f));
            vbox.AddChild(voiceSection);

            _voiceModeBtn = new TouchButton
            {
                Text = VoiceModeBtnText(), FontSize = 22,
                BackgroundColor = new Color(0.25f, 0.25f, 0.35f, 0.95f),
                CustomMinimumSize = new Vector2(280, 56),
            };
            _voiceModeBtn.Pressed += OnVoiceModeCycle;
            vbox.AddChild(_voiceModeBtn);

            _voiceDurationBtn = new TouchButton
            {
                Text = VoiceDurationBtnText(), FontSize = 22,
                BackgroundColor = new Color(0.25f, 0.25f, 0.35f, 0.95f),
                CustomMinimumSize = new Vector2(280, 56),
            };
            _voiceDurationBtn.Pressed += OnVoiceDurationCycle;
            vbox.AddChild(_voiceDurationBtn);

            _voiceDismissBtn = new TouchButton
            {
                Text = VoiceDismissBtnText(), FontSize = 22,
                BackgroundColor = new Color(0.25f, 0.25f, 0.35f, 0.95f),
                CustomMinimumSize = new Vector2(280, 56),
            };
            _voiceDismissBtn.Pressed += OnVoiceDismissCycle;
            vbox.AddChild(_voiceDismissBtn);
        }

        // Debug overlay toggle — only in debug builds where the overlay exists.
        if (_debugOverlay != null)
        {
            var debugSection = new Label
            {
                Text                = "Debug",
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter         = MouseFilterEnum.Ignore,
            };
            debugSection.AddThemeFontSizeOverride("font_size", 18);
            debugSection.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f, 1f));
            vbox.AddChild(debugSection);

            _debugOverlayBtn = new TouchButton
            {
                Text              = DebugOverlayBtnText(),
                FontSize          = 22,
                BackgroundColor   = new Color(0.25f, 0.25f, 0.35f, 0.95f),
                CustomMinimumSize = new Vector2(280, 56),
            };
            _debugOverlayBtn.Pressed += OnDebugOverlayToggle;
            vbox.AddChild(_debugOverlayBtn);
        }

        // Export Save (dev instrumentation) — DEBUG BUILDS ONLY. Exports the on-disk save byte-verbatim
        // plus a fresh serialization of the current in-memory state, for save-side vs load-side diffing.
        if (_onExportSave != null && OS.IsDebugBuild())
        {
            var exportBtn = new TouchButton
            {
                Text              = "Export Save (dev)",
                FontSize          = 22,
                BackgroundColor   = new Color(0.2f, 0.3f, 0.25f, 0.95f),
                CustomMinimumSize = new Vector2(280, 56),
            };
            exportBtn.Pressed += () => _onExportSave();
            vbox.AddChild(exportBtn);
        }

        // Voice diagnostic toggle (Phase 1, symptom E) — DEBUG BUILDS ONLY. Writes per-turn ribbon
        // reason codes to diag_structured.jsonl so a device play localizes why the ribbon stays blank.
        if (_onVoiceDiagToggle != null && OS.IsDebugBuild())
        {
            _voiceDiagBtn = new TouchButton
            {
                Text              = VoiceDiagBtnText(),
                FontSize          = 22,
                BackgroundColor   = new Color(0.2f, 0.3f, 0.25f, 0.95f),
                CustomMinimumSize = new Vector2(280, 56),
            };
            _voiceDiagBtn.Pressed += () =>
            {
                _voiceDiagOn = !_voiceDiagOn;
                if (_voiceDiagBtn != null) _voiceDiagBtn.Text = VoiceDiagBtnText();
                _onVoiceDiagToggle(_voiceDiagOn);
            };
            vbox.AddChild(_voiceDiagBtn);
        }

        // Back button
        var backBtn = new TouchButton
        {
            Text              = "← Back",
            FontSize          = 22,
            BackgroundColor   = new Color(0.35f, 0.2f, 0.2f, 0.95f),
            CustomMinimumSize = new Vector2(280, 56),
        };
        backBtn.Pressed += () => EmitSignal(SignalName.BackRequested);
        vbox.AddChild(backBtn);
    }

    // -------------------------------------------------------------------------
    // Voice settings (M1.5b) — cycle + persist immediately via the callback.
    // -------------------------------------------------------------------------

    private void OnVoiceModeCycle()
    {
        _voiceSettings = _voiceSettings with { Mode = _voiceSettings.CycleMode() };
        if (_voiceModeBtn != null) _voiceModeBtn.Text = VoiceModeBtnText();
        _onVoiceChanged?.Invoke(_voiceSettings);
    }

    private void OnVoiceDurationCycle()
    {
        _voiceSettings = _voiceSettings with { Duration = _voiceSettings.CycleDuration() };
        if (_voiceDurationBtn != null) _voiceDurationBtn.Text = VoiceDurationBtnText();
        _onVoiceChanged?.Invoke(_voiceSettings);
    }

    private void OnVoiceDismissCycle()
    {
        _voiceSettings = _voiceSettings with { Dismiss = _voiceSettings.CycleDismiss() };
        if (_voiceDismissBtn != null) _voiceDismissBtn.Text = VoiceDismissBtnText();
        _onVoiceChanged?.Invoke(_voiceSettings);
    }

    private string VoiceModeBtnText() => $"Voice: {_voiceSettings.Mode}";
    private string VoiceDurationBtnText() => $"Duration: {_voiceSettings.Duration}";
    // "Tap to clear" (Manual) keeps lines until tapped; "Auto-fade" (Timed) uses the Duration above.
    private string VoiceDismissBtnText() =>
        $"Dismiss: {(_voiceSettings.Dismiss == VoiceDismiss.Manual ? "Tap to clear" : "Auto-fade")}";
    private string VoiceDiagBtnText() => $"Voice Diag (dev): {(_voiceDiagOn ? "ON" : "OFF")}";

    // -------------------------------------------------------------------------
    // Tileset cycling
    // -------------------------------------------------------------------------

    private void OnCyclePressed()
    {
        // Advance to next tileset, wrapping around.
        _selectedIndex = (_selectedIndex + 1) % KnownTilesets.Length;

        var (newId, _) = KnownTilesets[_selectedIndex];

        // Update button label
        if (_cycleBtn != null)
            _cycleBtn.Text = CycleBtnText();

        // Show or hide the "restart" notice based on whether the selection differs from boot.
        if (_restartLabel != null)
            _restartLabel.Visible = newId != _loadedTilesetId;

        // Persist to game_settings.yaml immediately so the value survives if the user
        // closes without returning to the main menu.
        WriteTilesetSetting(newId);
    }

    private string CycleBtnText()
    {
        var (_, displayName) = KnownTilesets[_selectedIndex];
        return $"Tileset: {displayName}  →";
    }

    // -------------------------------------------------------------------------
    // Debug overlay toggle
    // -------------------------------------------------------------------------

    private void OnDebugOverlayToggle()
    {
        if (_debugOverlay == null) return;
        _debugOverlay.Visible = !_debugOverlay.Visible;
        if (_debugOverlayBtn != null)
            _debugOverlayBtn.Text = DebugOverlayBtnText();
        WriteDebugOverlaySetting(_debugOverlay.Visible);
    }

    private string DebugOverlayBtnText()
    {
        var on = _debugOverlay?.Visible ?? false;
        return $"Debug Overlay: {(on ? "ON" : "OFF")}";
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static int FindIndex(string id)
    {
        for (int i = 0; i < KnownTilesets.Length; i++)
        {
            if (KnownTilesets[i].Id == id) return i;
        }
        return 0;
    }

    /// <summary>
    /// Write the new tileset ID into config/game_settings.yaml.
    ///
    /// Strategy: read the current file text, replace the `tileset:` line, write back.
    /// Simple string replacement is intentional — YamlDotNet round-trips would strip
    /// comments and reformat the file. We preserve the file exactly except for that one line.
    ///
    /// Uses ProjectSettings.GlobalizePath to get the real filesystem path. This is
    /// correct on desktop/editor. On mobile, res:// is read-only inside the .pck — a
    /// proper settings migration to user:// would be needed there, but that's deferred.
    /// </summary>
    private static void WriteTilesetSetting(string tilesetId)
    {
        const string resPath = "res://config/game_settings.yaml";

        try
        {
            // Read current contents
            string currentText;
            using (var readFile = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read))
            {
                if (readFile == null)
                {
                    GD.PrintErr("[OptionsPanel] Cannot open game_settings.yaml for reading.");
                    return;
                }
                currentText = readFile.GetAsText();
            }

            // Replace the tileset: line, preserving the rest of the file verbatim.
            // Handles both quoted ("value") and unquoted (value) forms on the RHS.
            var lines = currentText.Split('\n');
            bool replaced = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("tileset:", System.StringComparison.Ordinal))
                {
                    lines[i] = $"tileset: \"{tilesetId}\"";
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                // Field not found — append it so the file stays valid.
                var list = new System.Collections.Generic.List<string>(lines) { $"tileset: \"{tilesetId}\"" };
                lines = list.ToArray();
            }

            var newText = string.Join('\n', lines);

            // Write back via Godot FileAccess (handles res:// correctly on desktop/editor).
            using var writeFile = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Write);
            if (writeFile == null)
            {
                GD.PrintErr("[OptionsPanel] Cannot open game_settings.yaml for writing. " +
                            "On mobile, res:// is read-only — settings changes won't persist.");
                return;
            }
            writeFile.StoreString(newText);
            GD.Print($"[OptionsPanel] Tileset setting saved: {tilesetId}");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[OptionsPanel] Failed to write game_settings.yaml: {ex.Message}");
        }
    }

    private static void WriteDebugOverlaySetting(bool visible)
    {
        const string resPath = "res://config/game_settings.yaml";
        try
        {
            string currentText;
            using (var readFile = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read))
            {
                if (readFile == null) return;
                currentText = readFile.GetAsText();
            }

            var lines = currentText.Split('\n');
            bool replaced = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("show_debug_overlay:", System.StringComparison.Ordinal))
                {
                    lines[i] = $"show_debug_overlay: {(visible ? "true" : "false")}";
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                var list = new System.Collections.Generic.List<string>(lines)
                    { $"show_debug_overlay: {(visible ? "true" : "false")}" };
                lines = list.ToArray();
            }

            using var writeFile = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Write);
            if (writeFile == null) return;
            writeFile.StoreString(string.Join('\n', lines));
            GD.Print($"[OptionsPanel] Debug overlay setting saved: {visible}");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[OptionsPanel] Failed to write debug overlay setting: {ex.Message}");
        }
    }
}
