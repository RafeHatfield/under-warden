using UnderWarden.Logic.Content;

namespace UnderWarden.Logic.Core;

/// <summary>
/// Per-run tracker that prevents the same mural from appearing twice on the same floor.
/// Carried forward across floors — murals placed on floor 1 are excluded from floor 2+
/// until the pool exhausts, at which point all IDs are eligible again.
///
/// Floor-level tracking: before placing murals on a new floor, call ResetForFloor().
/// Run-level uniqueness: if all available murals have been used across all floors,
/// MarkUsedForRun(id) records the global used set; the pool resets when exhausted.
/// </summary>
public sealed class MuralTracker
{
    // IDs used on the CURRENT floor this session — reset when a new floor is started.
    private readonly HashSet<string> _usedThisFloor = new();

    // IDs used across all floors this run — resets when the pool is exhausted.
    private readonly HashSet<string> _usedThisRun = new();

    // ── Mid-run serialization (M1.4) ──────────────────────────────────────────
    public IReadOnlyCollection<string> UsedThisFloor => _usedThisFloor;
    public IReadOnlyCollection<string> UsedThisRun => _usedThisRun;

    /// <summary>Restore both used-sets after a mid-run load. Serializer-only.</summary>
    public void RestoreState(IEnumerable<string> usedThisFloor, IEnumerable<string> usedThisRun)
    {
        _usedThisFloor.Clear();
        foreach (var m in usedThisFloor) _usedThisFloor.Add(m);
        _usedThisRun.Clear();
        foreach (var m in usedThisRun) _usedThisRun.Add(m);
    }

    /// <summary>True if the given mural ID has been placed on the current floor.</summary>
    public bool IsUsedThisFloor(string muralId) => _usedThisFloor.Contains(muralId);

    /// <summary>True if the mural has been used somewhere this run (cross-floor uniqueness).</summary>
    public bool IsUsedThisRun(string muralId) => _usedThisRun.Contains(muralId);

    /// <summary>
    /// Record that a mural was placed on the current floor and mark it used for the run.
    /// </summary>
    public void MarkUsed(string muralId)
    {
        _usedThisFloor.Add(muralId);
        _usedThisRun.Add(muralId);
    }

    /// <summary>
    /// Clear per-floor state before placing murals on a new floor.
    /// Does NOT reset the run-level set — cross-floor uniqueness persists until pool exhaustion.
    /// </summary>
    public void ResetForFloor() => _usedThisFloor.Clear();

    /// <summary>
    /// Reset the entire run-level used set (called when the pool is exhausted).
    /// After reset, all murals become eligible for placement again.
    /// </summary>
    public void ResetRunPool()
    {
        _usedThisRun.Clear();
        _usedThisFloor.Clear();
    }

    /// <summary>
    /// Pick a unique mural for this floor from the registry.
    /// Priority: not used on current floor AND not used this run.
    /// If all murals have been used this run, reset the run pool and pick again.
    /// Returns null only if the registry is empty.
    /// </summary>
    public (string Id, string Text)? GetUniqueMuralForFloor(
        int depth, MuralRegistry registry, SeededRandom rng)
    {
        var candidates = registry.GetAllForDepth(depth);
        if (candidates.Count == 0) return null;

        // Filter out murals already placed this floor and this run.
        var available = candidates
            .Where(m => !IsUsedThisFloor(m.Id) && !IsUsedThisRun(m.Id))
            .ToList();

        // Pool exhausted — reset and allow reuse.
        if (available.Count == 0)
        {
            ResetRunPool();
            available = candidates
                .Where(m => !IsUsedThisFloor(m.Id))
                .ToList();
        }

        if (available.Count == 0) return null;

        var chosen = available[rng.Next(available.Count)];
        MarkUsed(chosen.Id);
        return (chosen.Id, chosen.Text);
    }
}
