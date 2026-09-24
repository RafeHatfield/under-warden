using UnderWarden.Logic.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Loads voice line YAML pool files and resolves trigger IDs to individual lines.
///
/// YAML format: flat key → string[] mapping.
/// Keys are trigger IDs such as "past_sasha_encounter.looted_body" or "oil_slick_fire".
///
/// Compound-key fallback: if "a.b.c" is not found, tries "a.b", then "a".
/// First-fire semantics: pass a caller-managed HashSet to ensure the first line
/// in a pool fires exactly once per logical session before rotating to random picks.
/// </summary>
public sealed class VoiceLineRegistry
{
    private readonly Dictionary<string, List<string>> _pools;

    public VoiceLineRegistry(Dictionary<string, List<string>> pools)
    {
        _pools = pools;
    }

    /// <summary>Load from a YAML string. Uses AotObjectFactory for NativeAOT safety.</summary>
    public static VoiceLineRegistry LoadFromYaml(string yaml, IObjectFactory? factory = null)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .WithObjectFactory(factory ?? new AotObjectFactory())
            .Build();

        var pools = deserializer.Deserialize<Dictionary<string, List<string>>>(yaml)
                    ?? new Dictionary<string, List<string>>();
        return new VoiceLineRegistry(pools);
    }

    /// <summary>Merge a second registry into this one. Later-loaded pools win on key collision.</summary>
    public void Merge(VoiceLineRegistry other)
    {
        foreach (var (key, lines) in other._pools)
            _pools[key] = lines;
    }

    /// <summary>
    /// Resolve a trigger ID to a random line. Returns null if no pool matches.
    /// Compound-key fallback: "a.b.c" → "a.b" → "a".
    /// </summary>
    public string? GetLine(string triggerId, SeededRandom rng)
        => GetLine(triggerId, rng, firedSet: null);

    /// <summary>
    /// Resolve with first-fire semantics. The first line in the pool fires the first time
    /// this triggerId appears in <paramref name="firedSet"/>; subsequent calls pick randomly.
    /// The triggerId is added to firedSet on the first call.
    /// </summary>
    public string? GetLine(string triggerId, SeededRandom rng, HashSet<string>? firedSet)
    {
        var pool = Resolve(triggerId);
        if (pool == null || pool.Count == 0) return null;

        if (firedSet != null && !firedSet.Contains(triggerId))
        {
            firedSet.Add(triggerId);
            return pool[0];
        }

        return pool[rng.Next(pool.Count)];
    }

    /// <summary>
    /// The authored top-level pool keys, verbatim (no compound-key resolution). Used by the
    /// tiers↔pools cross-validation to assert every authored key resolves to exactly one voice family.
    /// </summary>
    public IReadOnlyCollection<string> PoolKeys => _pools.Keys;

    /// <summary>True if any pool is registered for this trigger (or a compound-key prefix).</summary>
    public bool HasTrigger(string triggerId) => Resolve(triggerId) != null;

    /// <summary>
    /// The full line pool for a trigger (compound-key resolved), or null if none. Read-only view —
    /// the scheduler (VoiceScheduler) needs the whole pool to build a shuffle bag, not a single pick.
    /// </summary>
    public IReadOnlyList<string>? GetPool(string triggerId) => Resolve(triggerId);

    // ── Private ────────────────────────────────────────────────────────────────

    private List<string>? Resolve(string triggerId)
    {
        if (_pools.TryGetValue(triggerId, out var pool)) return pool;

        // Compound-key fallback: strip last segment and retry.
        var key = triggerId;
        while (key.Contains('.'))
        {
            key = key[..key.LastIndexOf('.')];
            if (_pools.TryGetValue(key, out pool)) return pool;
        }

        return null;
    }
}
