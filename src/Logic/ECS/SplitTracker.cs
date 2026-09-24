namespace UnderWarden.Logic.ECS;

/// <summary>
/// Tracks split-under-pressure config and one-time-split guard.
/// Set on large_slime (and any future splitting monster) at spawn time.
/// Split fires when HP drops below TriggerHpPct — one time only, not on death.
/// </summary>
public sealed class SplitTracker : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>HP fraction below which split triggers. E.g. 0.40 = triggers below 40% HP.</summary>
    public double TriggerHpPct { get; }

    /// <summary>Monster type ID for spawned children (e.g. "slime").</summary>
    public string ChildType { get; }

    /// <summary>Minimum number of children to spawn.</summary>
    public int MinChildren { get; }

    /// <summary>Maximum number of children to spawn.</summary>
    public int MaxChildren { get; }

    /// <summary>
    /// Optional weighted distribution over [MinChildren, MaxChildren].
    /// Length must equal MaxChildren - MinChildren + 1.
    /// Null = uniform random in [MinChildren, MaxChildren].
    /// </summary>
    public int[]? Weights { get; }

    /// <summary>True once split has fired. Prevents double-splitting on subsequent hits.</summary>
    public bool HasSplit { get; set; }

    public SplitTracker(double triggerHpPct, string childType,
        int minChildren, int maxChildren, int[]? weights)
    {
        TriggerHpPct = triggerHpPct;
        ChildType = childType;
        MinChildren = minChildren;
        MaxChildren = maxChildren;
        Weights = weights;
    }
}
