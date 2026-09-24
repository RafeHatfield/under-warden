namespace UnderWarden.Logic.ECS;

/// <summary>
/// Component marking an entity as a staircase.
/// Used by EntityPlacer and (in Phase 5) by GameState.PlayerOnStairDown.
/// </summary>
public sealed class Stair : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>True = stair leads down (deeper); false = stair leads up (shallower).</summary>
    public bool IsDown { get; }

    /// <summary>The depth the player will arrive at after taking this stair.</summary>
    public int TargetDepth { get; }

    public Stair(bool isDown, int targetDepth)
    {
        IsDown = isDown;
        TargetDepth = targetDepth;
    }
}
