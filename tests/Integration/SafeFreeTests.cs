using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace UnderWarden.Integration;

using UnderWarden.Presentation;

/// <summary>
/// Verifies SafeFree vs bare QueueFree behavior.
/// SafeFree immediately removes from parent (GetChildCount drops),
/// while QueueFree leaves the ghost node until end-of-frame.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SafeFreeTests
{
    [TestCase]
    public async Task SafeFree_ImmediatelyRemovesFromParent()
    {
        var parent = new Node2D();
        var child1 = new Sprite2D();
        var child2 = new Sprite2D();
        parent.AddChild(child1);
        parent.AddChild(child2);

        // Baseline
        AssertInt(parent.GetChildCount()).IsEqual(2);

        // SafeFree should remove immediately
        child1.SafeFree();
        AssertInt(parent.GetChildCount()).IsEqual(1);

        // Cleanup
        child2.SafeFree();
        parent.QueueFree();
        await ISceneRunner.SyncProcessFrame;
    }

    [TestCase]
    public async Task QueueFree_DoesNotRemoveFromParentImmediately()
    {
        var parent = new Node2D();
        var child = new Sprite2D();
        parent.AddChild(child);

        AssertInt(parent.GetChildCount()).IsEqual(1);

        // Bare QueueFree does NOT remove from parent on the same call —
        // the node remains a child until Godot processes the deletion.
        // This is the root cause of the ghost node bugs SafeFree prevents.
        child.QueueFree();

        // GdUnit4's scene environment may behave differently from a live Godot scene tree:
        // in some test setups the node count drops immediately, in others it does not.
        // The assertion below documents the expected in-game behavior (ghost node still
        // present). If this fails in CI it indicates GdUnit4 is processing deletions
        // eagerly; in that case treat this test as documentation of the contract rather
        // than a strict runtime check.
        AssertInt(parent.GetChildCount()).IsEqual(1); // ghost node still present

        // Contrast: SafeFree would have already removed it from the parent.
        // With bare QueueFree, the child is still in the tree at this point.
        // (The exact frame at which Godot removes it depends on engine internals.)
        // Key assertion: SafeFree is strictly better because RemoveChild is immediate.

        // Cleanup — wait for QueueFree to process
        await ISceneRunner.SyncProcessFrame;
        await ISceneRunner.SyncProcessFrame;
        parent.QueueFree();
        await ISceneRunner.SyncProcessFrame;
    }

    [TestCase]
    public async Task SafeFree_CalledTwice_DoesNotCrash()
    {
        var parent = new Node2D();
        var child = new Sprite2D();
        parent.AddChild(child);

        // First call: removes from parent immediately and schedules deletion.
        // Second call: node has no parent, so GetParent() returns null and the
        // RemoveChild path is skipped. QueueFree on an already-queued node should
        // not cause an engine crash.
        child.SafeFree();
        child.SafeFree(); // second call — node has no parent, must not throw

        await ISceneRunner.SyncProcessFrame;
        parent.QueueFree();
        await ISceneRunner.SyncProcessFrame;
    }

    [TestCase]
    public async Task SafeFree_SafeWithNoParent()
    {
        var orphan = new Sprite2D();

        // Should not throw even without a parent
        orphan.SafeFree();
        await ISceneRunner.SyncProcessFrame;
    }
}
