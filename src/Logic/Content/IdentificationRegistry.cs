namespace UnderWarden.Logic.Content;

/// <summary>
/// Per-run identification state. Tracks which item types the player has identified this run.
///
/// Design: identification is binary per type — either the player knows what a type is, or
/// they don't. Once identified, all instances of that type immediately show their true name.
///
/// Pre-identification decisions are also cached here so the same type doesn't re-roll if
/// a second instance spawns later in the run. The first decision made for a type (identified
/// or explicitly kept unidentified) is the permanent decision for that run.
///
/// Thread safety: single-threaded (all game logic is deterministic single-threaded).
/// TODO: wire to save/load system when that lands.
/// </summary>
public sealed class IdentificationRegistry
{
    private readonly HashSet<string> _identified = new();
    private readonly HashSet<string> _decidedUnidentified = new();

    /// <summary>
    /// When true, all items are treated as identified regardless of what's in the set.
    /// Used by testing-mode scenarios with all_items_identified: true.
    /// </summary>
    public bool AlwaysIdentified { get; set; } = false;

    // ── Mid-run serialization (M1.4) ──────────────────────────────────────────
    public IReadOnlyCollection<string> IdentifiedTypeIds => _identified;
    public IReadOnlyCollection<string> DecidedUnidentifiedTypeIds => _decidedUnidentified;

    /// <summary>Restore identification state after a mid-run load. Serializer-only.</summary>
    public void RestoreState(IEnumerable<string> identified, IEnumerable<string> decidedUnidentified, bool alwaysIdentified)
    {
        AlwaysIdentified = alwaysIdentified;
        _identified.Clear();
        foreach (var t in identified) _identified.Add(t);
        _decidedUnidentified.Clear();
        foreach (var t in decidedUnidentified) _decidedUnidentified.Add(t);
    }

    /// <summary>Returns true if the given item type is identified this run.</summary>
    public bool IsIdentified(string typeId) => AlwaysIdentified || _identified.Contains(typeId);

    /// <summary>
    /// Returns true if a pre-identification decision has already been made for this type —
    /// either identified or explicitly marked as unidentified. Prevents re-rolling on
    /// second and subsequent instances of the same type within a run.
    /// </summary>
    public bool HasDecision(string typeId) =>
        _identified.Contains(typeId) || _decidedUnidentified.Contains(typeId);

    /// <summary>
    /// Mark a type as identified.
    /// Returns true if newly identified (first time), false if already known.
    /// The return value drives the "You realize this was..." toast — only shown on first ID.
    /// </summary>
    public bool Identify(string typeId)
    {
        // Remove from unidentified set if it was there (pre-ID decision may be overridden by use)
        _decidedUnidentified.Remove(typeId);
        return _identified.Add(typeId);
    }

    /// <summary>
    /// Record that this type was rolled as unidentified at run start.
    /// Subsequent instances of the same type inherit this decision without re-rolling.
    /// Has no effect if the type is already identified.
    /// </summary>
    public void MarkUnidentified(string typeId)
    {
        if (!_identified.Contains(typeId))
            _decidedUnidentified.Add(typeId);
    }

    /// <summary>Count of currently identified types. Useful for tests and debug.</summary>
    public int IdentifiedCount => _identified.Count;

    /// <summary>Count of types that have a cached "unidentified" decision.</summary>
    public int UnidentifiedDecisionCount => _decidedUnidentified.Count;
}
