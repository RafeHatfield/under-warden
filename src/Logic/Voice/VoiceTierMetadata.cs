using UnderWarden.Logic.Content;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Logic.Voice;

/// <summary>
/// Per-trigger-family delivery metadata for the voice scheduler (docs/systems/voice_delivery.md
/// §Scheduler). This is DATA, not code: the priority tier, one-shot flag, cooldown exemption, and the
/// ambient cutoff all live in YAML authored by the Voice thread. The scheduler is content-agnostic and
/// consumes whatever this describes.
///
/// STOP-FLAG (5a → Voice thread): the tier metadata YAML does not exist yet — the existing voice line
/// files are a flat trigger→lines map (VoiceLineRegistry) with no tier information. The exact fields
/// the Voice thread must author are the shape below; see the class-level note in VoiceScheduler.
///
/// Schema:
///   ambient_cutoff_tier: &lt;int&gt;   # Tactical mode renders only families with tier STRICTLY ABOVE this
///   families:                        # ORDER IS SIGNIFICANT — it is the priority tie-break
///     - key: hp_critical             # must match a VoiceLineRegistry pool key (compound-key resolved)
///       tier: 70                     # higher = more important
///       cooldown_exempt: true        # optional (default false) — bypasses the 3-turn cooldown
///     - key: possession
///       tier: 60
///     - key: species_first_sight
///       tier: 40
///       once_per_run: true           # optional (default false) — fires at most once per run
/// </summary>
public sealed class VoiceTierMetadata
{
    private readonly List<VoiceFamilyMeta> _families;                 // declaration order = tie-break order
    private readonly Dictionary<string, VoiceFamilyMeta> _byKey;

    /// <summary>Tactical mode renders only families whose tier is strictly greater than this.</summary>
    public int AmbientCutoffTier { get; }

    /// <summary>Families in declaration order (the priority tie-break order).</summary>
    public IReadOnlyList<VoiceFamilyMeta> Families => _families;

    public VoiceTierMetadata(int ambientCutoffTier, IEnumerable<VoiceFamilyMeta> families)
    {
        AmbientCutoffTier = ambientCutoffTier;
        _families = families.ToList();
        _byKey = _families.ToDictionary(f => f.Key, StringComparer.Ordinal);
    }

    /// <summary>Metadata for a family key, or null if the family is unknown (not schedulable).</summary>
    public VoiceFamilyMeta? Get(string familyKey) =>
        _byKey.TryGetValue(familyKey, out var f) ? f : null;

    /// <summary>
    /// Resolve a SPECIFIC pool/trigger key (e.g. "species_first_sight.orc_grunt", "hp_threshold.25",
    /// "long_idle") to the family that owns it, for the tier/flags. Matching is SEPARATOR-EXACT: a family
    /// "hp_threshold" owns "hp_threshold" (exact) and "hp_threshold.25" (dot-prefixed), but never a future
    /// "hp_threshold_x". On nested families the LONGEST prefix wins, so resolution is unambiguous. Returns
    /// null for an unknown key — the caller then skips it WITHOUT consuming (the ONE RULE for malformed
    /// keys); the tiers↔pools cross-validation test turns such a key into a loud CI failure.
    /// </summary>
    public VoiceFamilyMeta? ResolveFamily(string triggerKey)
    {
        if (_byKey.TryGetValue(triggerKey, out var exact)) return exact;   // flat key == family
        VoiceFamilyMeta? best = null;
        foreach (var f in _families)
            if (triggerKey.StartsWith(f.Key + ".", StringComparison.Ordinal)
                && (best == null || f.Key.Length > best.Key.Length))
                best = f;
        return best;
    }

    /// <summary>Load from the tier-metadata YAML string. Uses AotObjectFactory for NativeAOT safety.</summary>
    public static VoiceTierMetadata LoadFromYaml(string yaml, IObjectFactory? factory = null)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .WithObjectFactory(factory ?? new AotObjectFactory())
            .Build();

        var file = deserializer.Deserialize<MetaFileDto>(yaml) ?? new MetaFileDto();
        var families = new List<VoiceFamilyMeta>(file.Families.Count);
        for (int i = 0; i < file.Families.Count; i++)
        {
            var f = file.Families[i];
            if (string.IsNullOrEmpty(f.Key))
                throw new InvalidOperationException($"voice tier metadata: family at index {i} has no 'key'.");
            families.Add(new VoiceFamilyMeta(f.Key, f.Tier, i, f.OncePerRun, f.CooldownExempt));
        }
        return new VoiceTierMetadata(file.AmbientCutoffTier, families);
    }

    // ── YAML shape (deserializer targets) ────────────────────────────────────────
    // internal (not private) so AotObjectFactory can register them — under NativeAOT (iOS) YamlDotNet
    // cannot reflect a parameterless ctor, so every deserialized type MUST have an explicit factory
    // entry or the parse throws. Missing these is exactly why the voice scheduler was null on device.
    internal sealed class MetaFileDto
    {
        public int AmbientCutoffTier { get; set; }
        public List<FamilyDto> Families { get; set; } = new();
    }

    internal sealed class FamilyDto
    {
        public string Key { get; set; } = "";
        public int Tier { get; set; }
        public bool OncePerRun { get; set; }
        public bool CooldownExempt { get; set; }
    }
}

/// <summary>
/// One trigger family's delivery metadata. <see cref="Order"/> is the family's index in the YAML
/// (the priority tie-break); lower wins a tie.
/// </summary>
public sealed record VoiceFamilyMeta(string Key, int Tier, int Order, bool OncePerRun, bool CooldownExempt);
