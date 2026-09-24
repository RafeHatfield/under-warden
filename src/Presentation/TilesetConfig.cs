namespace UnderWarden.Presentation;

/// <summary>
/// Data class representing one tileset definition loaded from config/tilesets/{id}.yaml.
///
/// Supports two sprite addressing models:
///   - Path-based (FrameStride == 0): entity value is a path component — {SpritesRoot}/{value}_{frame}.png
///   - Index-based (FrameStride > 0): entity value is a creature key number.
///     sprite_index = int(value) * FrameStride + animation_frame + FrameOffset
///     filename = FramePattern with {index} replaced (zero-padded to 3 digits).
///
/// The UF tileset uses path-based; the 16bf tileset uses index-based.
/// </summary>
public sealed class TilesetConfig
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Native sprite size in pixels (48 for UF, 24 for 16bf creatures).</summary>
    public int SpriteSize { get; set; } = 48;

    /// <summary>Number of animation frames per entity.</summary>
    public int FrameCount { get; set; } = 4;

    /// <summary>
    /// Sprite slots per creature key. 0 = path-based addressing (no stride calculation).
    /// Positive value = index-based: index = key * stride + frame + offset.
    /// </summary>
    public int FrameStride { get; set; } = 0;

    /// <summary>Added to the computed sprite index in index-based mode. See FrameStride.</summary>
    public int FrameOffset { get; set; } = 0;

    /// <summary>
    /// Filename template for index-based mode. {base} is replaced with the entity value;
    /// {frame} or {index} are replaced with the computed value (zero-padded as needed).
    /// Unused in path-based mode (FrameStride == 0).
    /// </summary>
    public string FramePattern { get; set; } = "{base}_{frame}.png";

    /// <summary>res:// root path for entity sprites (player, monsters).</summary>
    public string SpritesRoot { get; set; } = "";

    /// <summary>res:// root path for item sprites.</summary>
    public string ItemsRoot { get; set; } = "";

    /// <summary>
    /// Filename template for index-based item sprites. {index:D2} is replaced with
    /// the zero-padded file number from the YAML items value.
    /// Empty string = path-based mode (UF): value is the filename stem directly.
    /// </summary>
    public string ItemsPattern { get; set; } = "";

    /// <summary>
    /// Path component (path-based) or creature key (index-based) for the player sprite.
    /// In path-based mode: SpritesRoot/PlayerSprite_{frame}.png
    /// In index-based mode: computed from int(PlayerSprite) * FrameStride + frame + FrameOffset
    /// </summary>
    public string PlayerSprite { get; set; } = "";

    /// <summary>
    /// Monster type ID → sprite value. Interpretation depends on FrameStride:
    ///   path-based:  value is a subfolder/base path component (e.g. "heroes/goblin")
    ///   index-based: value is a creature key string (e.g. "137")
    /// </summary>
    public Dictionary<string, string> Entities { get; set; } = new();

    /// <summary>
    /// Item type ID → sprite value. In path-based mode, value is the filename stem
    /// (e.g. "potion_red") under ItemsRoot. In index-based mode, value is an item key.
    /// </summary>
    public Dictionary<string, string> Items { get; set; } = new();

    /// <summary>
    /// Explicit Offset.Y value for entity sprites (positive = down, negative = up).
    /// When null, defaults to the formula: -(textureHeight * scale * 0.15f).
    /// Use this to ground entity feet on the tile surface when the default formula
    /// gives wrong results (e.g. small sprites like 16bf at native 24px).
    /// </summary>
    public float? EntityYOffset { get; set; } = null;
}
