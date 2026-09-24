using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;

namespace UnderWarden.Logic.Voice;

/// <summary>
/// Presentation-agnostic voice delivery scheduler (docs/systems/voice_delivery.md §Scheduler). Given
/// the trigger families raised on a turn, it picks at most one line to deliver under shuffle-bag,
/// one-shot, priority, cooldown, and silence rules, and returns it for the ribbon (5b) to render.
///
/// TWO RECONSTRUCT-class dependencies, caller-provided and never serialized (same pattern as
/// GameState.BoonTable): <see cref="VoiceLineRegistry"/> (the line pools) and
/// <see cref="VoiceTierMetadata"/> (per-family tiers). The mutable run state — bags, fired-set,
/// floor-silence flag, cooldown counter, ribbon history, and the dedicated voice Rng — is the
/// SERIALIZE-class state the mid-run save persists.
///
/// THE ONE RULE (invariant, verified by the non-consumption tests): every non-render path returns null
/// WITHOUT mutating any state. Consumption — the single bag draw, one-shot burn, cooldown stamp, and
/// history push — happens only on the delivery path, after every gate has passed.
///
/// STOP-FLAG (5a → Voice thread): <see cref="VoiceTierMetadata"/> has no authored content yet. The
/// Voice thread must author the tier YAML (fields: ambient_cutoff_tier; per family key/tier and the
/// optional once_per_run + cooldown_exempt flags; family order = tie-break). Until then the scheduler
/// is exercised by fixtures. Line-pool authoring (VoiceLineRegistry) is the Voice thread's existing job.
///
/// GAMEPLAY ISOLATION: the scheduler takes no GameState and never touches the gameplay Rng — all
/// randomness is its own <see cref="_rng"/>. Gameplay hash sequences are identical whether or not the
/// scheduler runs (proven by the isolation soak).
/// </summary>
public sealed class VoiceScheduler
{
    /// <summary>Mixed into the run seed so the voice stream is independent of the gameplay stream.</summary>
    public const int RngSeedSalt = unchecked((int)0x5643_4F49);   // "VCOI"

    /// <summary>Minimum turns between delivered lines (cooldown-exempt families bypass this).</summary>
    public const int CooldownTurns = 3;

    /// <summary>Ribbon history depth retained in the run save.</summary>
    public const int HistoryCap = 20;

    private const int NoDeliveryYet = int.MinValue;

    private readonly VoiceLineRegistry _registry;   // RECONSTRUCT — caller-provided, never serialized
    private readonly VoiceTierMetadata _meta;       // RECONSTRUCT — caller-provided, never serialized
    private readonly SeededRandom _rng;             // SERIALIZE via (Seed, CallCount)
    private readonly Dictionary<string, List<int>> _bags;   // SERIALIZE — remaining draw order per family
    private readonly HashSet<string> _fired;        // SERIALIZE — one-shot families already fired this run
    private readonly List<VoiceHistoryEntry> _history;      // SERIALIZE — last HistoryCap delivered lines
    private bool _currentFloorSilenced;             // SERIALIZE — post-Marya / shut-up mute of this floor
    private int _lastDeliveredTurn;                 // SERIALIZE — cooldown counter (turn of last delivery)

    /// <summary>Fresh scheduler for a new run. The voice Rng seed is runSeed XOR the salt.</summary>
    public VoiceScheduler(VoiceLineRegistry registry, VoiceTierMetadata meta, int runSeed)
        : this(registry, meta, new SeededRandom(runSeed ^ RngSeedSalt),
               new Dictionary<string, List<int>>(StringComparer.Ordinal),
               new HashSet<string>(StringComparer.Ordinal),
               currentFloorSilenced: false, lastDeliveredTurn: NoDeliveryYet,
               history: new List<VoiceHistoryEntry>())
    {
    }

    private VoiceScheduler(VoiceLineRegistry registry, VoiceTierMetadata meta, SeededRandom rng,
        Dictionary<string, List<int>> bags, HashSet<string> fired, bool currentFloorSilenced,
        int lastDeliveredTurn, List<VoiceHistoryEntry> history)
    {
        _registry = registry;
        _meta = meta;
        _rng = rng;
        _bags = bags;
        _fired = fired;
        _currentFloorSilenced = currentFloorSilenced;
        _lastDeliveredTurn = lastDeliveredTurn;
        _history = history;
    }

    /// <summary>
    /// Rebuild a scheduler at a saved position (construct-then-restore; registry + meta are RECONSTRUCT,
    /// caller-provided). Mirrors <see cref="SeededRandom.Restore"/> for the voice Rng.
    /// </summary>
    public static VoiceScheduler Restore(VoiceLineRegistry registry, VoiceTierMetadata meta,
        int rngSeed, long rngCallCount, IEnumerable<(string Family, int[] Remaining)> bags,
        IEnumerable<string> fired, bool currentFloorSilenced, int lastDeliveredTurn,
        IEnumerable<VoiceHistoryEntry> history)
    {
        var bagMap = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var (family, remaining) in bags)
            if (remaining.Length > 0) bagMap[family] = remaining.ToList();

        return new VoiceScheduler(registry, meta, SeededRandom.Restore(rngSeed, rngCallCount),
            bagMap, new HashSet<string>(fired, StringComparer.Ordinal),
            currentFloorSilenced, lastDeliveredTurn, history.ToList());
    }

    // ── delivery ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Consider the trigger families raised this turn and deliver at most one line. Returns null (and
    /// consumes NOTHING) whenever no line renders. All probes are supplied by the caller (5b): the mode
    /// setting, the tier of the line currently on the ribbon (for the strictly-higher supersede rule),
    /// and whether the ribbon surface is available.
    /// </summary>
    /// <param name="triggerFamilies">Family keys raised this turn (duplicates and unknowns are ignored).</param>
    /// <param name="mode">Verbose / Tactical / Silent (device setting, passed in — never stored).</param>
    /// <param name="currentTurn">The run turn count, for cooldown + history.</param>
    /// <param name="currentRibbonTier">Tier of the line currently displayed, or null if the ribbon is empty.</param>
    /// <param name="surfaceAvailable">False when 5b cannot render right now (probe).</param>
    public VoiceDelivery? TryDeliver(IReadOnlyList<string> triggerFamilies, VoiceMode mode,
        int currentTurn, int? currentRibbonTier = null, bool surfaceAvailable = true)
        => TryDeliver(triggerFamilies, mode, currentTurn, currentRibbonTier, surfaceAvailable, out _);

    /// <summary>
    /// Diagnostic overload — identical behaviour and RNG to the parameterless-reason overload, but also
    /// reports WHY nothing was delivered (Phase 1 device diagnostic for symptom E). The reason is
    /// observed at each existing gate; no extra draws, so gameplay/voice-stream isolation is unchanged.
    /// </summary>
    public VoiceDelivery? TryDeliver(IReadOnlyList<string> triggerFamilies, VoiceMode mode,
        int currentTurn, int? currentRibbonTier, bool surfaceAvailable, out VoiceDeliverReason reason)
    {
        // Global silence gates — nothing renders, nothing consumes.
        if (mode == VoiceMode.Silent) { reason = VoiceDeliverReason.SilentMode; return null; }
        if (_currentFloorSilenced) { reason = VoiceDeliverReason.FloorSilenced; return null; }
        if (!surfaceAvailable) { reason = VoiceDeliverReason.SurfaceUnavailable; return null; }

        // Candidates: each trigger key is a SPECIFIC pool key (e.g. "species_first_sight.orc_grunt").
        // Tier/flags come from the family the key resolves to (separator-exact prefix); the pool + bag are
        // keyed by the specific key. An unknown key resolves to no family → skipped, never consumed.
        VoiceFamilyMeta? best = null;
        string? bestKey = null;                                                // the winner's specific pool key
        foreach (var triggerKey in triggerFamilies)
        {
            var meta = _meta.ResolveFamily(triggerKey);
            if (meta == null) continue;                                        // unknown key — skip, no consume
            if (mode == VoiceMode.Tactical && meta.Tier <= _meta.AmbientCutoffTier) continue;  // ambient, hidden
            if (meta.OncePerRun && _fired.Contains(meta.Key)) continue;        // one-shot family already spent
            var pool = _registry.GetPool(triggerKey);
            if (pool == null || pool.Count == 0) continue;                     // no lines to draw for this key

            // Highest tier wins; ties broken by family declaration order (lower Order wins).
            if (best == null || meta.Tier > best.Tier || (meta.Tier == best.Tier && meta.Order < best.Order))
            {
                best = meta;
                bestKey = triggerKey;
            }
        }
        if (best == null) { reason = VoiceDeliverReason.NoEligibleFamily; return null; }  // empty pool / unknown / filtered

        // Cooldown — the winner must clear it unless its family is exempt. (long math avoids sentinel overflow.)
        if (!best.CooldownExempt && (long)currentTurn - _lastDeliveredTurn < CooldownTurns) { reason = VoiceDeliverReason.Cooldown; return null; }

        // Ribbon supersede — a new line only lands if strictly higher than what is already showing.
        if (currentRibbonTier is int shown && best.Tier <= shown) { reason = VoiceDeliverReason.SupersededByCurrent; return null; }

        // ── every gate passed: CONSUME exactly once ──
        string line = DrawFromBag(bestKey!);                 // bag + pool keyed by the SPECIFIC key
        if (best.OncePerRun) _fired.Add(best.Key);           // one-shot tracked per FAMILY
        _lastDeliveredTurn = currentTurn;
        _history.Add(new VoiceHistoryEntry(best.Key, line, currentTurn));
        if (_history.Count > HistoryCap) _history.RemoveRange(0, _history.Count - HistoryCap);

        reason = VoiceDeliverReason.Delivered;
        return new VoiceDelivery(best.Key, line, best.Tier);
    }

    /// <summary>Mute the current floor (post-Marya flag, or the shut-up action). Cleared on floor entry.</summary>
    public void SilenceCurrentFloor() => _currentFloorSilenced = true;

    /// <summary>Clear the floor-silence flag on entering a new floor (the game is a one-way descent).</summary>
    public void OnFloorEntered() => _currentFloorSilenced = false;

    // ── shuffle bag ──────────────────────────────────────────────────────────────

    private string DrawFromBag(string poolKey)
    {
        var pool = _registry.GetPool(poolKey)!;    // guaranteed non-empty by the eligibility filter
        if (!_bags.TryGetValue(poolKey, out var bag) || bag.Count == 0)
        {
            bag = ShuffledIndices(pool.Count);     // reshuffle only on exhaustion
            _bags[poolKey] = bag;
        }
        int idx = bag[0];
        bag.RemoveAt(0);
        return pool[idx];
    }

    /// <summary>Fisher–Yates permutation of [0, n) drawing ONLY from the dedicated voice Rng.</summary>
    private List<int> ShuffledIndices(int n)
    {
        var order = new List<int>(n);
        for (int i = 0; i < n; i++) order.Add(i);
        for (int i = n - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }

    // ── serialization accessors (read-only snapshots for MidRunSerializer) ─────────

    public int RngSeed => _rng.Seed;
    public long RngCallCount => _rng.CallCount;
    public bool CurrentFloorSilenced => _currentFloorSilenced;
    public int LastDeliveredTurn => _lastDeliveredTurn;

    /// <summary>Non-empty bags in canonical (ordinal-key) order; each bag's remaining draw order intact.</summary>
    public IEnumerable<(string Family, int[] Remaining)> BagsSnapshot() =>
        _bags.Where(kv => kv.Value.Count > 0)
             .OrderBy(kv => kv.Key, StringComparer.Ordinal)
             .Select(kv => (kv.Key, kv.Value.ToArray()));

    /// <summary>Fired one-shot family keys in canonical order.</summary>
    public IEnumerable<string> FiredSnapshot() => _fired.OrderBy(k => k, StringComparer.Ordinal);

    /// <summary>Ribbon history in chronological (delivery) order — order is significant.</summary>
    public IReadOnlyList<VoiceHistoryEntry> HistorySnapshot() => _history;
}

/// <summary>A line the scheduler chose to deliver this turn: its family, resolved text, and tier.</summary>
public sealed record VoiceDelivery(string Family, string Line, int Tier);

/// <summary>
/// Why <see cref="VoiceScheduler.TryDeliver"/> did or didn't deliver — the reason codes the Phase 1
/// device diagnostic logs per turn to localize symptom E without guessing.
/// </summary>
public enum VoiceDeliverReason
{
    Delivered,            // a line rendered
    SilentMode,           // mode == Silent
    FloorSilenced,        // shut-up / post-Marya floor mute
    SurfaceUnavailable,   // 5b probe said the ribbon can't render (HUD inactive / modal up)
    NoEligibleFamily,     // no trigger family survived (empty pool, unknown key, mode-filtered, one-shot spent)
    Cooldown,             // winner within the 3-turn cooldown and not exempt
    SupersededByCurrent,  // a same-or-higher-tier line already on the ribbon
}

/// <summary>One entry of the run-scoped ribbon history (delivered line text + the turn it fired).</summary>
public sealed record VoiceHistoryEntry(string Family, string Line, int Turn);
