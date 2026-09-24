using UnderWarden.Logic.Core;
using Godot;

namespace UnderWarden.Presentation.Map;

/// <summary>
/// Renders active ground hazards as sprite overlays that cycle through animation frames
/// each turn, fading out as the hazard ages.
///
/// Fire: orange flame sprites (141→142→143), alpha decays with remaining turns.
/// Poison gas: green wisp sprites (125→126→127), same decay logic.
///
/// One Sprite2D per tile, created lazily and reused. ZIndex sits above the dungeon
/// floor but below entity sprites. Call Refresh() after every turn. Call Clear() on
/// floor transition.
/// </summary>
public sealed class GroundHazardOverlay
{
    // Animation frame paths — static so textures are cached across floor transitions.
    // 16bf FX sheet (32×32): flame sprites 07–09 for fire, 22–24 for poison gas.
    private static string FxPath16(int n) =>
        $"res://src/Presentation/assets/sprites_16bf/fx_32x32/oryx_16bit_fantasy_fx_{n:D2}.png";

    private static readonly string[] FireFrames = { FxPath16(7), FxPath16(8) };
    private static readonly string[] GasFrames  = { FxPath16(23), FxPath16(24) };

    // Texture cache — ResourceLoader.Load is expensive; never call it twice for the same path.
    private static readonly Dictionary<string, Texture2D> _texCache = new();

    private static Texture2D GetTexture(string path)
    {
        if (!_texCache.TryGetValue(path, out var tex))
            _texCache[path] = tex = ResourceLoader.Load<Texture2D>(path);
        return tex;
    }

    // Alpha envelope — intense at full duration, fades as hazard ages.
    private const float FireAlpha      = 0.90f;
    private const float PoisonGasAlpha = 0.85f;

    private readonly Node2D _layer;
    private readonly IMapRenderer _renderer;

    // Persistent sprites keyed by tile — created once, reused each turn.
    private readonly Dictionary<(int X, int Y), Sprite2D> _tileSprites = new();

    // Incremented each Refresh() so frame selection advances each turn.
    private int _turnCounter;

    public GroundHazardOverlay(Node2D layer, IMapRenderer renderer)
    {
        _layer    = layer;
        _renderer = renderer;
    }

    /// <summary>
    /// Sync overlay with current hazard state. Advances the animation frame counter.
    /// Call after every turn completion.
    /// </summary>
    public void Refresh(GameState state)
    {
        _turnCounter++;

        // Hide all first — active hazards are re-shown below.
        foreach (var sprite in _tileSprites.Values)
            sprite.Visible = false;

        foreach (var (pos, hazard) in state.GroundHazards.Hazards)
        {
            float intensity = hazard.RemainingTurns / (float)hazard.MaxDuration;
            bool isFire     = hazard.Type == HazardType.Fire;
            string[] frames = isFire ? FireFrames : GasFrames;
            float baseAlpha = isFire ? FireAlpha : PoisonGasAlpha;

            string texPath = frames[_turnCounter % frames.Length];

            if (!_tileSprites.TryGetValue(pos, out var sprite))
            {
                sprite = new Sprite2D
                {
                    Position    = _renderer.GridToScreenCenter(pos.X, pos.Y),
                    ZIndex      = _renderer.GetTileSortOrder(pos.X, pos.Y) + 1,
                    ZAsRelative = false,
                    Centered    = true,
                };
                _layer.AddChild(sprite);
                _tileSprites[pos] = sprite;
            }

            sprite.Texture  = GetTexture(texPath);
            sprite.Modulate = new Color(1f, 1f, 1f, baseAlpha * intensity);
            sprite.Visible  = true;
        }
    }

    /// <summary>Free all nodes. Call on floor transition.</summary>
    public void Clear()
    {
        foreach (var sprite in _tileSprites.Values)
            sprite.SafeFree();
        _tileSprites.Clear();
    }
}
