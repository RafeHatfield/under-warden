using Godot;

namespace UnderWarden.Presentation;

/// <summary>
/// Extension methods for safe Godot node lifecycle management.
/// Always use SafeFree() instead of bare QueueFree() when removing a node
/// that is a child of a layout container. QueueFree alone leaves the node
/// in the tree until end-of-frame, causing ghost nodes in layout.
/// </summary>
public static class NodeExtensions
{
    /// <summary>
    /// Remove from parent immediately, then queue for deletion.
    /// Safe to call even if the node has no parent or has already been freed.
    /// </summary>
    public static void SafeFree(this Node node)
    {
        if (!GodotObject.IsInstanceValid(node)) return;
        node.GetParent()?.RemoveChild(node);
        node.QueueFree();
    }
}
