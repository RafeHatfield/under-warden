using UnderWarden.Logic.Balance;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Loads bot persona configurations from a YAML file.
///
/// YAML format:
///   personas:
///     balanced:
///       retreat_hp_threshold: 0.25
///       base_heal_threshold: 0.30
///       ...
///
/// When the YAML file is absent, the hardcoded table in BotPersonaRegistry.Defaults
/// serves as the fallback — callers should catch exceptions and handle gracefully.
///
/// Uses YamlDotNet with underscore naming convention, matching all other content loaders.
/// IgnoreUnmatchedProperties allows forward-compatible YAML evolution.
/// </summary>
public static class BotPersonaLoader
{
    /// <summary>
    /// Load persona configs from a YAML file. Returns an immutable dictionary keyed by persona name.
    /// Throws if the file does not exist or is malformed.
    /// </summary>
    public static IReadOnlyDictionary<string, BotPersonaConfig> LoadFromFile(string path)
        => LoadFromYaml(File.ReadAllText(path));

    /// <summary>
    /// Parse persona configs from a YAML string.
    /// </summary>
    public static IReadOnlyDictionary<string, BotPersonaConfig> LoadFromYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithObjectFactory(new AotObjectFactory())
            .IgnoreUnmatchedProperties()
            .Build();

        var file = deserializer.Deserialize<BotPersonasFile>(yaml);
        if (file?.Personas == null || file.Personas.Count == 0)
            return new Dictionary<string, BotPersonaConfig>();

        var result = new Dictionary<string, BotPersonaConfig>();
        foreach (var (name, dto) in file.Personas)
        {
            result[name] = new BotPersonaConfig(
                Name:                     name,
                RetreatHpThreshold:       dto.RetreatHpThreshold,
                BaseHealThreshold:        dto.BaseHealThreshold,
                PanicHpThreshold:         dto.PanicHpThreshold,
                PanicMultiEnemyCount:     dto.PanicMultiEnemyCount,
                CombatEngagementDistance: dto.CombatEngagementDistance,
                LootPriority:             dto.LootPriority,
                PreferStairs:             dto.PreferStairs,
                AvoidCombat:              dto.AvoidCombat,
                AllowCombatHealing:       dto.AllowCombatHealing);
        }
        return result;
    }

    // ── YAML deserialization DTOs ──────────────────────────────────────────────

    /// <summary>Top-level YAML wrapper: personas: { name: {fields...} }</summary>
    private sealed class BotPersonasFile
    {
        public Dictionary<string, BotPersonaDto>? Personas { get; set; }
    }

    /// <summary>
    /// Per-persona YAML fields. All fields have defaults matching the "balanced" persona
    /// so partial YAML entries are safe — missing fields inherit balanced values.
    /// </summary>
    private sealed class BotPersonaDto
    {
        public double RetreatHpThreshold       { get; set; } = 0.25;
        public double BaseHealThreshold        { get; set; } = 0.30;
        public double PanicHpThreshold         { get; set; } = 0.15;
        public int    PanicMultiEnemyCount      { get; set; } = 2;
        public int    CombatEngagementDistance  { get; set; } = 8;
        public int    LootPriority              { get; set; } = 1;
        public bool   PreferStairs              { get; set; } = false;
        public bool   AvoidCombat               { get; set; } = false;
        public bool   AllowCombatHealing        { get; set; } = true;
    }
}
