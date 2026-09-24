namespace UnderWarden.Presentation;

/// <summary>
/// Instance-based sprite mapping backed by a loaded TilesetConfig.
///
/// Replaces the former static class that had hardcoded dictionaries.
/// Now driven entirely by YAML — swap the tileset config, get a different sprite set,
/// no C# changes required.
///
/// Two addressing models:
///   Path-based (FrameStride == 0, UF):
///     {SpritesRoot}/{value}_{frame}.png
///   Index-based (FrameStride > 0, 16bf):
///     sprite_index = int(value) * FrameStride + frame + FrameOffset
///     filename = FramePattern with "{index}" substituted (D2 padding)
///
/// Thread safety: read-only after construction. Safe to share across callers.
/// </summary>
public sealed class SpriteMapping
{
    private readonly TilesetConfig _config;

    public SpriteMapping(TilesetConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Sprite path component for the player entity.
    /// In path-based mode: full subfolder/base, e.g. "heroes/knight".
    /// In index-based mode: creature key string, e.g. "1".
    /// </summary>
    public string PlayerSprite => _config.PlayerSprite;

    /// <summary>Tileset identifier (e.g. "ultimate_fantasy"). Used by UI to reflect current selection.</summary>
    public string TilesetId => _config.Id;

    /// <summary>Tileset display name (e.g. "Ultimate Fantasy"). Used by UI labels.</summary>
    public string TilesetName => _config.Name;

    /// <summary>Number of animation frames per entity sprite.</summary>
    public int FrameCount => _config.FrameCount;

    /// <summary>True for index-based tilesets (e.g. 16bf). False for path-based (e.g. UF).</summary>
    public bool IsIndexBased => _config.FrameStride > 0;

    /// <summary>res:// root path for entity sprites. Used by SpriteBrowser to load raw frames.</summary>
    public string SpritesRoot => _config.SpritesRoot;

    /// <summary>
    /// Native sprite size in pixels (48 for UF, 24 for 16bf).
    /// Used by EntitySpriteManager for scale compensation.
    /// </summary>
    public int SpriteSize => _config.SpriteSize;

    /// <summary>
    /// Get the sprite base value for a monster type ID.
    /// Returns null if no mapping exists for this type ID.
    /// In path-based mode: a subfolder/name string (e.g. "heroes/goblin").
    /// In index-based mode: a creature key string (e.g. "137").
    /// </summary>
    public string? GetSpriteBase(string typeId)
    {
        return _config.Entities.GetValueOrDefault(typeId);
    }

    /// <summary>
    /// Get the full resource path for an entity sprite at a given animation frame.
    /// spriteBase is the value returned by GetSpriteBase (or PlayerSprite).
    /// Frame is 1-based.
    /// </summary>
    public string GetFramePath(string spriteBase, int animFrame)
    {
        if (_config.FrameStride == 0)
        {
            // Path-based (UF): {SpritesRoot}/{base}_{frame}.png
            return $"{_config.SpritesRoot}/{spriteBase}_{animFrame}.png";
        }
        else
        {
            // Index-based (16bf): compute sprite index from creature key
            // index = key * stride + frame + offset
            // Frame is 1-based here; the offset accounts for 1-indexed sprite files.
            if (!int.TryParse(spriteBase, out int creatureKey))
            {
                // Defensive: if the value isn't a valid integer, fall through to a safe path.
                // This should never happen if the tileset YAML is correct.
                Godot.GD.PrintErr($"[SpriteMapping] Expected integer creature key, got: '{spriteBase}'");
                return $"{_config.SpritesRoot}/{spriteBase}_{animFrame}.png";
            }

            int spriteIndex = creatureKey * _config.FrameStride + animFrame + _config.FrameOffset;
            // FramePattern uses {index:D2} placeholder (D2 = minimum 2-digit zero padding).
            // Simple string replacement — avoids string.Format to keep it explicit.
            var filename = _config.FramePattern.Replace("{index:D2}", spriteIndex.ToString("D2"));
            return $"{_config.SpritesRoot}/{filename}";
        }
    }

    /// <summary>
    /// Get the Offset.Y value for entity sprites (positive = down, negative = up).
    /// Uses entity_y_offset from the tileset config if set; otherwise falls back to
    /// the default formula: -(textureHeight * scale * 0.15f).
    /// </summary>
    public float GetEntityYOffset(float textureHeight, float scale)
    {
        return _config.EntityYOffset ?? -(textureHeight * scale * 0.15f);
    }

    /// <summary>
    /// Get the full resource path for an item sprite.
    /// Returns null if no mapping exists or the value is empty (caller falls back to placeholder).
    ///
    /// Path-based (UF, ItemsPattern == ""):  {ItemsRoot}/{value}.png
    /// Index-based (16bf, ItemsPattern set): value is a file number; ItemsPattern replaces {index:D2}
    /// </summary>
    public string? GetItemSpritePath(string itemTypeId)
    {
        if (!_config.Items.TryGetValue(itemTypeId, out var spriteValue))
            return null;
        if (string.IsNullOrEmpty(spriteValue))
            return null;

        if (string.IsNullOrEmpty(_config.ItemsPattern))
        {
            // Path-based (UF): value is a filename stem
            return $"{_config.ItemsRoot}/{spriteValue}.png";
        }
        else
        {
            // Index-based (16bf): value is a file number
            if (!int.TryParse(spriteValue, out int fileNum))
            {
                Godot.GD.PrintErr($"[SpriteMapping] Expected integer item file number, got: '{spriteValue}' for '{itemTypeId}'");
                return null;
            }
            var filename = _config.ItemsPattern.Replace("{index:D2}", fileNum.ToString("D2"));
            return $"{_config.ItemsRoot}/{filename}";
        }
    }
}
