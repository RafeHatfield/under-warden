using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Minimap overlay — draws one pixel per map tile using _Draw().
/// Call Refresh(state) after each turn and after floor loads.
/// Anchored to top-right of UILayer; size is set to map dimensions × TilePixels.
/// </summary>
public sealed partial class MiniMap : Control
{
    private const int TilePixels = 2;
    private const int Padding = 6;  // gap from screen edge

    // colors
    private static readonly Color ColWall        = new(0.12f, 0.12f, 0.14f, 0.85f);
    private static readonly Color ColFloor       = new(0.35f, 0.35f, 0.38f, 0.90f);
    private static readonly Color ColFloorVis    = new(0.60f, 0.60f, 0.65f, 0.95f);
    private static readonly Color ColCorridor    = new(0.28f, 0.28f, 0.30f, 0.90f);
    private static readonly Color ColCorridorVis = new(0.50f, 0.50f, 0.55f, 0.95f);
    private static readonly Color ColStair       = new(0.30f, 0.55f, 1.00f, 1.00f);
    private static readonly Color ColPlayer      = new(1.00f, 1.00f, 0.20f, 1.00f);
    private static readonly Color ColMonster     = new(1.00f, 0.25f, 0.25f, 1.00f);
    // StyleBoxFlat for rounded-corner background + subtle border.
    // Created once and reused across redraws to avoid per-frame allocations.
    private readonly StyleBoxFlat _bgStyle = BuildBgStyle();

    private static StyleBoxFlat BuildBgStyle()
    {
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.05f, 0.05f, 0.08f, 0.82f);

        // 4px rounded corners
        style.CornerRadiusTopLeft     = 4;
        style.CornerRadiusTopRight    = 4;
        style.CornerRadiusBottomLeft  = 4;
        style.CornerRadiusBottomRight = 4;

        // Subtle 1px border (0.5px isn't representable; 1px at minimap scale reads as subtle)
        style.BorderColor       = new Color(0.60f, 0.60f, 0.70f, 0.45f);
        style.BorderWidthTop    = 1;
        style.BorderWidthRight  = 1;
        style.BorderWidthBottom = 1;
        style.BorderWidthLeft   = 1;

        return style;
    }

    private GameState? _state;

    public void Refresh(GameState state)
    {
        _state = state;

        // Resize to match map dimensions
        var w = state.Map.Width  * TilePixels;
        var h = state.Map.Height * TilePixels;
        CustomMinimumSize = new Vector2(w, h);
        Size = new Vector2(w, h);

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_state == null) return;

        var map = _state.Map;
        var mw = map.Width;
        var mh = map.Height;
        int pw = mw * TilePixels;
        int ph = mh * TilePixels;

        // Background — rounded corners + border via StyleBoxFlat.
        // TODO: tap-to-expand — intercept _GuiInput, scale up to full-screen map view, add close gesture.
        DrawStyleBox(_bgStyle, new Rect2(0, 0, pw, ph));

        // Tiles
        for (int x = 0; x < mw; x++)
        for (int y = 0; y < mh; y++)
        {
            if (!map.IsExplored(x, y)) continue;

            bool vis = map.IsVisible(x, y);
            var kind = map.GetTileKind(x, y);
            Color col = kind switch
            {
                TileKind.Floor     => vis ? ColFloorVis    : ColFloor,
                TileKind.Corridor  => vis ? ColCorridorVis : ColCorridor,
                TileKind.StairDown => ColStair,
                TileKind.StairUp   => ColStair,
                _                  => ColWall,
            };

            DrawRect(new Rect2(x * TilePixels, y * TilePixels, TilePixels, TilePixels), col);
        }

        // Monsters (visible only)
        foreach (var m in _state.AliveMonsters)
            if (map.IsVisible(m.X, m.Y))
                DrawRect(new Rect2(m.X * TilePixels, m.Y * TilePixels, TilePixels, TilePixels), ColMonster);

        // Player
        int px = _state.Player.X * TilePixels;
        int py = _state.Player.Y * TilePixels;
        DrawRect(new Rect2(px - 1, py - 1, TilePixels + 2, TilePixels + 2), ColPlayer);
    }
}
