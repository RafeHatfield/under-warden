namespace UnderWarden.Logic.ECS;

/// <summary>
/// Tracks which depths the player has visited and which boons have been applied.
/// Lives on GameState as a per-run singleton. Carried forward across floor transitions.
/// Reset on new game (fresh BoonTracker created).
/// </summary>
public sealed class BoonTracker
{
    public HashSet<int> VisitedDepths { get; } = new();
    public List<string> BoonsApplied { get; } = new();

    /// <summary>
    /// When true, no depth boons are awarded. Used by scenarios that need boon-free baselines.
    /// </summary>
    public bool DisableDepthBoons { get; set; }

    public void Reset()
    {
        VisitedDepths.Clear();
        BoonsApplied.Clear();
    }
}
