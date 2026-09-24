using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Loads and provides access to Under-Warden memo definitions and cause-of-death display names.
///
/// Memos are keyed by "{tone}.{incident_type}" (e.g. "polite.death_first").
/// Cause display names are keyed by engine cause strings (e.g. "spike_trap").
///
/// YAML loading uses AotObjectFactory for NativeAOT/iOS safety — no reflection at runtime.
/// </summary>
public sealed class MemoRegistry
{
    private readonly Dictionary<string, MemoDefinition> _memos;
    private readonly Dictionary<string, string> _causeDisplayNames;

    private MemoRegistry(
        Dictionary<string, MemoDefinition> memos,
        Dictionary<string, string> causeDisplayNames)
    {
        _memos = memos;
        _causeDisplayNames = causeDisplayNames;
    }

    /// <summary>
    /// Load from YAML strings. Uses AotObjectFactory for NativeAOT safety.
    /// </summary>
    public static MemoRegistry LoadFromYaml(string memosYaml, string causeDisplayNamesYaml,
        IObjectFactory? factory = null)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .WithObjectFactory(factory ?? new AotObjectFactory())
            .Build();

        // Memos file: flat key → MemoDtoEntry
        var rawMemos = deserializer.Deserialize<Dictionary<string, MemoDtoEntry>>(memosYaml)
                       ?? new Dictionary<string, MemoDtoEntry>();

        // Cause display names: flat key → string
        var causeNames = deserializer.Deserialize<Dictionary<string, string>>(causeDisplayNamesYaml)
                         ?? new Dictionary<string, string>();

        // Convert DTOs to public MemoDefinition records
        var memos = new Dictionary<string, MemoDefinition>(rawMemos.Count);
        foreach (var (key, dto) in rawMemos)
        {
            var register = dto.Register?.ToLowerInvariant() switch
            {
                "internal_cc" => MemoRegister.InternalCc,
                _ => MemoRegister.Direct,
            };

            memos[key] = new MemoDefinition
            {
                Register = register,
                To = dto.To,
                Subject = dto.Subject ?? "",
                Body = dto.Body ?? new List<string>(),
            };
        }

        return new MemoRegistry(memos, causeNames);
    }

    /// <summary>
    /// Retrieve a memo by key (e.g. "polite.death_first").
    /// Returns null for unknown keys — never throws.
    ///
    /// fireIndex=0 returns the canonical first-fire body.
    /// fireIndex=1+ returns a repeat variant if one exists; falls back to body[0].
    /// The body selection is done by <see cref="MemoFormatter"/>; this method
    /// returns the full MemoDefinition so the formatter can apply slot substitution.
    /// </summary>
    public MemoDefinition? GetMemo(string key)
    {
        return _memos.TryGetValue(key, out var memo) ? memo : null;
    }

    /// <summary>
    /// Look up the bureaucratic display phrase for an engine cause-of-death string.
    /// Returns null if the cause is not mapped — callers should apply their own fallback
    /// (e.g. underscore-to-space + title-case).
    /// </summary>
    public string? GetCauseDisplayName(string causeKey)
    {
        return _causeDisplayNames.TryGetValue(causeKey, out var name) ? name : null;
    }

    // ── Internal YAML DTO ──────────────────────────────────────────────────────

    /// <summary>
    /// Private DTO that mirrors the snake_case YAML shape of each memo entry.
    /// YamlDotNet deserializes into this; it is then converted to MemoDefinition.
    /// </summary>
    internal sealed class MemoDtoEntry
    {
        public string? Register { get; set; }
        public string? To { get; set; }
        public string? Subject { get; set; }
        public List<string>? Body { get; set; }
    }
}
