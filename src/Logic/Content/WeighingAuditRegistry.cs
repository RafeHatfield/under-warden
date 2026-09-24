using UnderWarden.Logic.Core;
using UnderWarden.Logic.Endgame;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Loads and resolves the Weighing audit dialogue from weighing_audit.yaml.
///
/// YAML format: each key maps to a list of {speaker, text} objects. This is structured
/// sequential content (not random pools), so each key produces exactly one ordered sequence.
/// </summary>
public sealed class WeighingAuditRegistry
{
    private readonly Dictionary<string, List<WeighingDialoguePage>> _sequences;

    public WeighingAuditRegistry(Dictionary<string, List<WeighingDialoguePage>> sequences)
    {
        _sequences = sequences;
    }

    /// <summary>The loaded dialogue sequences, exposed read-only for the mid-run serializer. The
    /// registry is config-shaped; it is serialized as captured so an in-flight run keeps the dialogue
    /// it was dealt across a content patch (self-contained-snapshot ruling).</summary>
    public IReadOnlyDictionary<string, List<WeighingDialoguePage>> Sequences => _sequences;

    /// <summary>Load from a YAML string.</summary>
    public static WeighingAuditRegistry LoadFromYaml(string yaml, IObjectFactory? factory = null)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .WithObjectFactory(factory ?? new AotObjectFactory())
            .Build();

        var raw = deserializer.Deserialize<Dictionary<string, List<RawPage>>>(yaml)
                  ?? new Dictionary<string, List<RawPage>>();

        var sequences = raw.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Select(p => new WeighingDialoguePage(p.Speaker, p.Text)).ToList());

        return new WeighingAuditRegistry(sequences);
    }

    /// <summary>Resolve the audit opening (fires before the first Guardian rises).</summary>
    public IReadOnlyList<WeighingDialoguePage> GetOpening() =>
        GetSequence("opening");

    /// <summary>
    /// Resolve the beat for a given Guardian at a given tier. Returns an empty list if no
    /// content is registered (e.g. placeholder run before content lands).
    /// </summary>
    public IReadOnlyList<WeighingDialoguePage> GetGuardianBeat(GuardianId guardian, GuardianTier tier)
    {
        string guardianKey = guardian switch
        {
            GuardianId.WardenOfWardens => "warden",
            GuardianId.Oathkeeper => "oathkeeper",
            GuardianId.AuditorsOwn => "auditor",
            GuardianId.AssemblyOfTheLost => "assembly",
            GuardianId.Debt => "debt",
            _ => "unknown",
        };
        string tierKey = tier switch
        {
            GuardianTier.Allied => "allied",
            GuardianTier.Diminished => "diminished",
            GuardianTier.Neutral => "neutral",
            GuardianTier.Savage => "savage",
            _ => "neutral",
        };
        return GetSequence($"{guardianKey}.{tierKey}");
    }

    /// <summary>Resolve the Debt sequence — unscaled, no tiers. Fires before the choice gate.</summary>
    public IReadOnlyList<WeighingDialoguePage> GetDebt() => GetSequence("debt");

    /// <summary>Resolve the resolution text for the given ending (fires on WeighingResolvedEvent).</summary>
    public IReadOnlyList<WeighingDialoguePage> GetResolution(Logic.Endgame.EndingType ending)
    {
        string key = ending switch
        {
            Logic.Endgame.EndingType.CleanAudit => "resolution.clean_audit",
            Logic.Endgame.EndingType.Swap => "resolution.swap",
            Logic.Endgame.EndingType.Theft => "resolution.theft",
            Logic.Endgame.EndingType.LossRefused => "resolution.refused",
            Logic.Endgame.EndingType.LossGuardians => "resolution.loss_guardians",
            Logic.Endgame.EndingType.LossDebt => "resolution.loss_debt",
            _ => "",
        };
        return GetSequence(key);
    }

    /// <summary>Framing narration for the ally fall-back beat (fires once if at least one ally present).</summary>
    public IReadOnlyList<WeighingDialoguePage> GetAllyFallbackFraming() =>
        GetSequence("ally_fallback.framing");

    /// <summary>Per-Guardian fallback line as the ally steps back before the Debt.</summary>
    public IReadOnlyList<WeighingDialoguePage> GetAllyFallback(Logic.Endgame.GuardianId guardian)
    {
        string key = guardian switch
        {
            Logic.Endgame.GuardianId.WardenOfWardens => "ally_fallback.warden",
            Logic.Endgame.GuardianId.Oathkeeper => "ally_fallback.oathkeeper",
            Logic.Endgame.GuardianId.AssemblyOfTheLost => "ally_fallback.assembly",
            Logic.Endgame.GuardianId.AuditorsOwn => "ally_fallback.auditor",
            _ => "",
        };
        return GetSequence(key);
    }

    /// <summary>Single text string for UI copy (button labels, confirmation text).</summary>
    public string? GetUiText(string key) =>
        _sequences.TryGetValue(key, out var seq) && seq.Count > 0 ? seq[0].Text : null;

    public bool HasSequence(string key) => _sequences.ContainsKey(key);

    private IReadOnlyList<WeighingDialoguePage> GetSequence(string key) =>
        _sequences.TryGetValue(key, out var seq) ? seq : Array.Empty<WeighingDialoguePage>();

    private sealed class RawPage
    {
        public string Speaker { get; set; } = "under_warden";
        public string Text { get; set; } = "";
    }
}
