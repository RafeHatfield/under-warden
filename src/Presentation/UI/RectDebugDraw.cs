using Godot;

namespace UnderWarden.Presentation.UI;

/// <summary>
/// Debug overlay that draws colored outlines around interactive UI element rects.
/// Toggle via F3 in debug builds. Helps diagnose hit-test offset issues caused
/// by Godot's integer stretch scale mode + CanvasLayer coordinate bugs.
/// </summary>
public sealed partial class RectDebugDraw : Control
{
    private QuickSlotBar? _quickSlotBar;

    public void SetQuickSlotBar(QuickSlotBar? bar) => _quickSlotBar = bar;

    public override void _Draw()
    {
        if (_quickSlotBar == null) return;

        var panelGlobalPos = _quickSlotBar.GetGlobalRect().Position;
        var myGlobalPos = GetGlobalRect().Position;

        // Draw item slot rects
        foreach (var (itemId, localRect) in _quickSlotBar.SlotRects)
        {
            // Convert from QuickSlotBar-local to our local coords
            var globalRect = new Rect2(localRect.Position + panelGlobalPos, localRect.Size);
            var drawRect = new Rect2(globalRect.Position - myGlobalPos, globalRect.Size);
            DrawRect(drawRect, Colors.Lime, false, 2.0f);
        }
    }

    public override void _Process(double delta)
    {
        if (Visible) QueueRedraw();
    }
}
