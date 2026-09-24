using Godot;
using System.Globalization;
// Alias to resolve ambiguity with System.IO.FileAccess
using GodotFileAccess = Godot.FileAccess;

namespace UnderWarden.Presentation;

/// <summary>
/// Loads tileset configuration from config/tilesets/{id}.yaml.
///
/// Uses manual line-by-line YAML parsing instead of YamlDotNet because
/// InvariantGlobalization=true (required for iOS NativeAOT — Godot's experimental
/// iOS C# export doesn't bundle ICU data) breaks YamlDotNet's deserialization.
///
/// The tileset YAML is simple enough (flat keys + two dictionaries) that
/// a full YAML library isn't needed.
///
/// Uses Godot's FileAccess for res:// path resolution — on iOS/Android, res:// files are
/// packed inside the .pck bundle and cannot be read via System.IO.File.
/// </summary>
public static class TilesetLoader
{
    private const string TilesetsDir = "res://config/tilesets/";
    private const string FallbackId = "ultimate_fantasy";

    /// <summary>
    /// Load a tileset by ID from config/tilesets/{id}.yaml.
    /// Throws FileNotFoundException with a clear path message if the file doesn't exist.
    /// </summary>
    public static TilesetConfig Load(string tilesetId)
    {
        var resPath = $"{TilesetsDir}{tilesetId}.yaml";

        using var file = GodotFileAccess.Open(resPath, GodotFileAccess.ModeFlags.Read);
        if (file == null)
        {
            throw new System.IO.FileNotFoundException(
                $"Tileset file not found: {resPath}. " +
                $"Ensure config/tilesets/{tilesetId}.yaml exists and is included in the Godot export.");
        }

        var yaml = file.GetAsText();
        var config = ParseTilesetYaml(yaml);

        if (config == null)
            throw new System.InvalidOperationException($"Failed to parse tileset: {resPath}");

        // Normalize: if Id wasn't set in the YAML, use the requested id.
        if (string.IsNullOrEmpty(config.Id))
            config.Id = tilesetId;

#if DEBUG
        ValidateSpritePaths(config);
#endif

        return config;
    }

    /// <summary>
    /// Load a tileset by ID, falling back to ultimate_fantasy on any error.
    /// Logs a clear error before falling back so the failure is visible.
    /// </summary>
    public static TilesetConfig LoadWithFallback(string tilesetId)
    {
        try
        {
            return Load(tilesetId);
        }
        catch (System.IO.FileNotFoundException ex)
        {
            GD.PrintErr($"[TilesetLoader] {ex.Message} — falling back to '{FallbackId}'.");
            return Load(FallbackId);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[TilesetLoader] Failed to load tileset '{tilesetId}': {ex.Message} — falling back to '{FallbackId}'.");
            return Load(FallbackId);
        }
    }

    // -------------------------------------------------------------------------
    // Manual YAML parser — handles the flat key-value + dictionary structure
    // of tileset YAML files without depending on YamlDotNet.
    // -------------------------------------------------------------------------

    private static TilesetConfig? ParseTilesetYaml(string yaml)
    {
        var config = new TilesetConfig();
        string? currentDict = null; // "entities" or "items" when inside a mapping

        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            // Skip blank lines and comments
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#')
                continue;

            // Strip inline comments: find # preceded by whitespace (not inside quotes)
            var contentPart = StripInlineComment(trimmed);
            if (contentPart.Length == 0)
                continue;

            // Detect indentation: indented lines are dictionary entries
            bool isIndented = line.Length > 0 && (line[0] == ' ' || line[0] == '\t');

            // Parse key: value
            int colonIdx = contentPart.IndexOf(':');
            if (colonIdx < 0) continue;

            var key = contentPart[..colonIdx].Trim();
            var value = colonIdx + 1 < contentPart.Length
                ? contentPart[(colonIdx + 1)..].Trim()
                : "";

            // Strip surrounding quotes
            value = Unquote(value);

            if (isIndented && currentDict != null)
            {
                // Inside entities: or items: mapping
                if (currentDict == "entities")
                    config.Entities[key] = value;
                else if (currentDict == "items")
                    config.Items[key] = value;
                continue;
            }

            // Top-level key — exit any current dictionary
            currentDict = null;

            switch (key)
            {
                case "id":              config.Id = value; break;
                case "name":            config.Name = value; break;
                case "sprite_size":     config.SpriteSize = ParseInt(value, 48); break;
                case "frame_count":     config.FrameCount = ParseInt(value, 4); break;
                case "frame_stride":    config.FrameStride = ParseInt(value, 0); break;
                case "frame_offset":    config.FrameOffset = ParseInt(value, 0); break;
                case "frame_pattern":   config.FramePattern = value; break;
                case "sprites_root":    config.SpritesRoot = value; break;
                case "items_root":      config.ItemsRoot = value; break;
                case "items_pattern":   config.ItemsPattern = value; break;
                case "player_sprite":   config.PlayerSprite = value; break;
                case "entity_y_offset":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float yOff))
                        config.EntityYOffset = yOff;
                    break;
                case "entities":
                    currentDict = "entities";
                    break;
                case "items":
                    currentDict = "items";
                    break;
            }
        }

        return config;
    }

    private static string StripInlineComment(string s)
    {
        bool inQuote = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') inQuote = !inQuote;
            if (!inQuote && s[i] == '#' && i > 0 && s[i - 1] == ' ')
                return s[..i].TrimEnd();
        }
        return s;
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            return s[1..^1];
        return s;
    }

    private static int ParseInt(string s, int fallback)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    // -------------------------------------------------------------------------
    // Debug validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Debug-only: check that the frame-1 resource path exists for each entity mapping.
    /// GD.PrintErr on any missing file so YAML typos surface at boot, not at first spawn.
    /// </summary>
    private static void ValidateSpritePaths(TilesetConfig config)
    {
#if DEBUG
        foreach (var (typeId, spriteValue) in config.Entities)
        {
            var path = ResolveFrame1Path(config, spriteValue);
            if (!ResourceLoader.Exists(path))
                GD.PrintErr($"[TilesetLoader] Missing entity sprite for '{typeId}': {path}");
        }
        foreach (var (typeId, spriteValue) in config.Items)
        {
            string path;
            if (string.IsNullOrEmpty(config.ItemsPattern))
            {
                path = $"{config.ItemsRoot}/{spriteValue}.png";
            }
            else
            {
                if (!int.TryParse(spriteValue, out int fileNum))
                {
                    GD.PrintErr($"[TilesetLoader] Item sprite value not an integer: '{spriteValue}' for '{typeId}'");
                    continue;
                }
                var filename = config.ItemsPattern.Replace("{index:D2}", fileNum.ToString("D2"));
                path = $"{config.ItemsRoot}/{filename}";
            }
            if (!ResourceLoader.Exists(path))
                GD.PrintErr($"[TilesetLoader] Missing item sprite for '{typeId}': {path}");
        }
#endif
    }

    private static string ResolveFrame1Path(TilesetConfig config, string spriteValue)
    {
        if (config.FrameStride == 0)
        {
            return $"{config.SpritesRoot}/{spriteValue}_1.png";
        }
        else
        {
            if (!int.TryParse(spriteValue, out int creatureKey))
            {
                GD.PrintErr($"[TilesetLoader] Index-based tileset has non-integer creature key: '{spriteValue}'");
                return "";
            }
            int spriteIndex = creatureKey * config.FrameStride + 1 + config.FrameOffset;
            var filename = config.FramePattern.Replace("{index:D2}", spriteIndex.ToString("D2"));
            return $"{config.SpritesRoot}/{filename}";
        }
    }
}
