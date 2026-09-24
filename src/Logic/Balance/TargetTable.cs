namespace UnderWarden.Logic.Balance;

/// <summary>
/// One region's balance targets: the death-rate band (THE balance verdict) plus the per-archetype
/// hits-to-down diagnostic anchors (subordinate — attribution only). Spans an inclusive depth range.
/// </summary>
public sealed record TargetRegion(
    string Name,
    int DepthMin,
    int DepthMax,
    TargetBand DeathPct,
    IReadOnlyDictionary<ThreatArchetype, ArchetypeTarget> ByArchetype,
    LeverExpectation? LeverExpectation = null)
{
    public bool ContainsDepth(int depth) => depth >= DepthMin && depth <= DepthMax;

    /// <summary>Project this region to the FloorTarget the FloorHealthClassifier consumes.</summary>
    public FloorTarget ToFloorTarget() => new(DeathPct, ByArchetype);
}

/// <summary>
/// The canonical per-region balance targets, hydrated from config/balance/target_table.yaml by
/// <see cref="TargetTableLoader"/>. Resolves a floor's depth to the FloorTarget the classifier checks
/// observed reality against. Numbers are authored during B-region tuning; this type only stores +
/// resolves them, it holds no balance opinion of its own.
/// </summary>
public sealed class TargetTable
{
    private readonly IReadOnlyList<TargetRegion> _regions;

    public TargetTable(IReadOnlyList<TargetRegion> regions)
    {
        if (regions.Count == 0)
            throw new ArgumentException("TargetTable must have at least one region.", nameof(regions));
        // Keep regions depth-ordered so ForDepth's clamp picks the right edge.
        _regions = regions.OrderBy(r => r.DepthMin).ToList();
    }

    public IReadOnlyList<TargetRegion> Regions => _regions;

    /// <summary>The region whose depth range contains <paramref name="depth"/>.</summary>
    /// <remarks>
    /// Depths below the first region clamp to it; depths above the last clamp to it. Gaps between
    /// authored ranges resolve to the nearest lower region. Placeholder-friendly: never throws on depth.
    /// </remarks>
    public TargetRegion RegionForDepth(int depth)
    {
        if (depth <= _regions[0].DepthMin) return _regions[0];
        if (depth >= _regions[^1].DepthMax) return _regions[^1];

        TargetRegion best = _regions[0];
        foreach (var r in _regions)
        {
            if (r.ContainsDepth(depth)) return r;
            if (r.DepthMin <= depth) best = r; // nearest lower region for gaps
        }
        return best;
    }

    /// <summary>The FloorTarget the classifier consumes for a floor at the given depth.</summary>
    public FloorTarget ForDepth(int depth) => RegionForDepth(depth).ToFloorTarget();

    /// <summary>The per-lever expectations for a floor at the given depth, or null if the region has none.</summary>
    public LeverExpectation? LeverExpectationForDepth(int depth) => RegionForDepth(depth).LeverExpectation;
}
