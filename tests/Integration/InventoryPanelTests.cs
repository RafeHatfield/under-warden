using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace UnderWarden.Integration;

using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Presentation.UI;

// InventoryPanel is superseded by QuickSlotBar (Phase 3 mobile layout overhaul).
// These tests remain for regression coverage until QuickSlotBar integration tests
// are written to replace them. CS0618 suppressed here because the warning is expected
// and the coverage is still valid.
// TODO: migrate these tests to QuickSlotBar integration tests.
#pragma warning disable CS0618

/// <summary>
/// Integration tests for InventoryPanel.
/// Verifies slot rect computation and manual hit-test click handling
/// (which bypasses Godot Button hit-testing due to integer stretch offset bug).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class InventoryPanelTests
{
    private ISceneRunner _runner = null!;
    private InventoryPanel _panel = null!;
    private GameState _state = null!;

    [BeforeTest]
    public async Task Setup()
    {
        // Build a minimal GameState with a player that has inventory items
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 4));

        var inventory = new Inventory();
        player.Add(inventory);

        // Add a consumable to inventory
        var potion = new Entity(100, "Healing Potion", 0, 0);
        var potionConsumable = new Consumable(healAmount: 15);
        potionConsumable.StackSize = 2;
        potion.Add(potionConsumable);
        inventory.Add(potion);

        // Add a second item
        var scroll = new Entity(101, "Fire Scroll", 0, 0);
        scroll.Add(new Consumable(healAmount: 0));
        inventory.Add(scroll);

        var map = GameMap.CreateArena(20, 20);
        var rng = new SeededRandom(1337);
        _state = new GameState(player, new List<Entity>(), map, rng);

        // Create panel in scene
        var root = new Control();
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _panel = new InventoryPanel();
        _panel.Name = "InventoryPanel";
        root.AddChild(_panel);

        _runner = ISceneRunner.Load(root, true);

        // Wait for _Ready and layout
        await _runner.SimulateFrames(3);

        // Initialize with state
        _panel.Initialize(_state);

        // Wait for deferred slot rect computation
        await _runner.SimulateFrames(3);
    }

    [TestCase]
    public void SlotRects_ComputedAfterRefresh()
    {
        // Two items in inventory = two slot rects
        AssertInt(_panel.SlotRects.Count).IsEqual(2);
    }

    [TestCase]
    public void SlotRects_HaveValidSize()
    {
        foreach (var (itemId, rect) in _panel.SlotRects)
        {
            AssertFloat(rect.Size.X).IsGreater(0f);
            AssertFloat(rect.Size.Y).IsGreater(0f);
        }
    }

    [TestCase]
    public void SlotRects_NonOverlapping()
    {
        if (_panel.SlotRects.Count < 2) return;

        var r0 = _panel.SlotRects[0].LocalRect;
        var r1 = _panel.SlotRects[1].LocalRect;

        // Slots are in a horizontal strip — right edge of first should be before left edge of second
        AssertFloat(r0.End.X).IsLessEqual(r1.Position.X);
    }

    [TestCase]
    public async Task ItemTapped_FiresOnHitTestMatch()
    {
        int tappedId = -1;
        _panel.ItemTapped += id => tappedId = id;

        // Simulate a click at the center of the first slot rect
        if (_panel.SlotRects.Count == 0)
        {
            AssertBool(false).IsTrue(); // fail if no slots
            return;
        }

        var (expectedId, rect) = _panel.SlotRects[0];
        var center = rect.GetCenter();

        // Create a mouse click event at the slot center (panel-local coords)
        var clickEvent = new InputEventMouseButton
        {
            Position = center,
            ButtonIndex = MouseButton.Left,
            Pressed = true,
        };

        // Feed directly to the panel's _GuiInput
        _panel._GuiInput(clickEvent);

        AssertInt(tappedId).IsEqual(expectedId);

        await ISceneRunner.SyncProcessFrame;
    }

    [TestCase]
    public async Task ItemTapped_DoesNotFireOutsideSlots()
    {
        int tappedId = -1;
        _panel.ItemTapped += id => tappedId = id;

        // Click at (0, 0) — should be outside all slot rects (header area)
        var clickEvent = new InputEventMouseButton
        {
            Position = new Vector2(0, 0),
            ButtonIndex = MouseButton.Left,
            Pressed = true,
        };

        _panel._GuiInput(clickEvent);

        // Should NOT have fired
        AssertInt(tappedId).IsEqual(-1);

        await ISceneRunner.SyncProcessFrame;
    }
}
#pragma warning restore CS0618
