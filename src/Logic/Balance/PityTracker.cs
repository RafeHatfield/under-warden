using UnderWarden.Logic.Content;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Room-based pity system for critical loot categories.
///
/// Tracks "rooms since last appearance" per tracked category across an entire run.
/// Persists across floor transitions (passed into DungeonFloorBuilder.Build and carried forward).
/// Reset only on a new run.
///
/// Tracked categories (PoC-exact — offensive and utility are intentionally NOT tracked):
///   healing, panic, upgrade_weapon, upgrade_armor
///
/// Soft pity: when rooms-since-last exceeds the soft threshold, the category's selection
///   weight is multiplied by SoftBiasFactor (2.0x by default). The item still competes
///   against other categories — it's just more likely to win.
///
/// Hard pity: when rooms-since-last reaches the hard threshold, the category is guaranteed
///   to be selected for the next eligible room (density roll skipped if needed).
///   Hard threshold = soft threshold + 2 per PoC design.
/// </summary>
public sealed class PityTracker
{
    // Per-category counter: rooms processed since last item of that category appeared
    private readonly Dictionary<string, int> _counters = new(StringComparer.OrdinalIgnoreCase);

    // Flags for pending hard inject — set when hard threshold is exceeded,
    // consumed by LootController when it forces the category.
    private readonly HashSet<string> _pendingHardInjects = new(StringComparer.OrdinalIgnoreCase);

    // Per-floor telemetry — reset on SnapshotAndResetFloorTelemetry(), NOT on floor transition.
    // Tracks all generated items (tracked and untracked categories) for harness analysis.
    private readonly Dictionary<string, int> _lootItemCounts = new(StringComparer.OrdinalIgnoreCase);
    private int _hardPityFireCount;

    // ── Mid-run serialization (M1.4) ──────────────────────────────────────────
    public IReadOnlyDictionary<string, int> Counters => _counters;
    public IReadOnlyCollection<string> PendingHardInjects => _pendingHardInjects;
    public IReadOnlyDictionary<string, int> LootItemCounts => _lootItemCounts;
    public int HardPityFireCount => _hardPityFireCount;

    /// <summary>Restore pity state after a mid-run load. Serializer-only.</summary>
    public void RestoreState(IEnumerable<KeyValuePair<string, int>> counters, IEnumerable<string> pendingHardInjects,
        IEnumerable<KeyValuePair<string, int>> lootItemCounts, int hardPityFireCount)
    {
        _counters.Clear();
        foreach (var kv in counters) _counters[kv.Key] = kv.Value;
        _pendingHardInjects.Clear();
        foreach (var c in pendingHardInjects) _pendingHardInjects.Add(c);
        _lootItemCounts.Clear();
        foreach (var kv in lootItemCounts) _lootItemCounts[kv.Key] = kv.Value;
        _hardPityFireCount = hardPityFireCount;
    }

    /// <summary>
    /// Called for every room processed (including empty rooms and rooms with no item).
    /// Increments all tracked counters to advance the pity window.
    /// Call BEFORE generating an item for the room so the counter reflects "rooms without X".
    /// </summary>
    public void AdvanceRoom()
    {
        foreach (var key in _counters.Keys.ToList())
            _counters[key]++;
    }

    /// <summary>
    /// Called after an item of the given category is placed in a room.
    /// Resets that category's drought counter to 0, clears any pending hard inject,
    /// and increments the per-floor loot telemetry count.
    /// </summary>
    public void RecordRoomItem(string category)
    {
        _counters[category] = 0;
        _pendingHardInjects.Remove(category);
        _lootItemCounts.TryGetValue(category, out int c);
        _lootItemCounts[category] = c + 1;
    }

    /// <summary>
    /// Get the soft-pity weight multiplier for a category in the current band.
    ///
    /// Returns SoftBiasFactor (2.0x) when rooms-since-last > soft threshold.
    /// Returns 1.0 otherwise (no soft bias active).
    ///
    /// If the category is not tracked, returns 1.0 (no effect).
    /// </summary>
    public double GetSoftBiasMultiplier(string category, LootBand band, LootPolicyConfig policy)
    {
        if (!_counters.TryGetValue(category, out int roomsSinceLast))
            return 1.0; // untracked category

        var (soft, _) = policy.GetPityThreshold(category, band);
        return roomsSinceLast > soft ? policy.SoftBiasFactor : 1.0;
    }

    /// <summary>
    /// Returns true when the hard pity threshold has been reached for this category.
    ///
    /// Hard inject: the counter has hit or exceeded the hard threshold, meaning the
    /// category MUST appear in the next eligible room regardless of density/weight rolls.
    ///
    /// Call ConsumeHardInject after forcing the category so the flag is cleared.
    /// </summary>
    public bool IsHardInjectDue(string category, LootBand band, LootPolicyConfig policy)
    {
        // Already flagged from a previous room check
        if (_pendingHardInjects.Contains(category))
            return true;

        if (!_counters.TryGetValue(category, out int roomsSinceLast))
            return false;

        var (_, hard) = policy.GetPityThreshold(category, band);
        if (roomsSinceLast >= hard)
        {
            // Flag it so subsequent rooms know too (until consumed)
            _pendingHardInjects.Add(category);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Mark a hard inject as consumed. Call after LootController forces the category.
    /// The counter will be reset by RecordRoomItem separately.
    /// </summary>
    public void ConsumeHardInject(string category)
    {
        _pendingHardInjects.Remove(category);
        _hardPityFireCount++;
    }

    /// <summary>
    /// Returns a snapshot of per-floor loot telemetry (category counts + hard pity fires)
    /// and resets the floor-level counters. Does NOT reset drought counters — those persist
    /// across floors for the lifetime of the run.
    ///
    /// Call once after each floor completes to collect per-floor loot stats.
    /// </summary>
    public (IReadOnlyDictionary<string, int> ItemCounts, int HardPityFires) SnapshotAndResetFloorTelemetry()
    {
        var snapshot = new Dictionary<string, int>(_lootItemCounts, StringComparer.OrdinalIgnoreCase);
        int fires = _hardPityFireCount;
        _lootItemCounts.Clear();
        _hardPityFireCount = 0;
        return (snapshot, fires);
    }

    /// <summary>
    /// Initialize tracking for a set of categories. Called when the tracker is wired to a policy.
    /// Categories not in this set are not tracked and return 1.0 multiplier.
    /// </summary>
    public void InitializeTrackedCategories(IEnumerable<string> categories)
    {
        foreach (var cat in categories)
        {
            if (!_counters.ContainsKey(cat))
                _counters[cat] = 0;
        }
    }

    /// <summary>
    /// Reset all counters. Call for a new run (not a floor transition — pity persists across floors).
    /// </summary>
    public void Reset()
    {
        _counters.Clear();
        _pendingHardInjects.Clear();
        _lootItemCounts.Clear();
        _hardPityFireCount = 0;
    }

    /// <summary>Rooms since last item of the given category. Returns -1 if category is not tracked.</summary>
    public int RoomsSinceLast(string category)
        => _counters.TryGetValue(category, out int v) ? v : -1;
}
