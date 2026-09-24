using UnderWarden.Logic.Balance;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Root structure for level_templates.yaml. Maps depth (as string key) to LevelOverride.
/// Depths are stored as integer keys in YAML but loaded as Dictionary&lt;string, LevelOverride&gt;
/// for YAML compatibility (YamlDotNet requires string keys for non-scalar dicts).
/// </summary>
public sealed class LevelTemplatesFile
{
    [YamlMember(Alias = "levels")]
    public Dictionary<string, LevelOverride> Levels { get; set; } = new();
}

/// <summary>
/// Loads and provides access to per-floor level template overrides from level_templates.yaml.
/// GetLevelOverride(depth) returns null for unconfigured depths — callers use defaults.
/// </summary>
public sealed class LevelTemplateRegistry
{
    private readonly Dictionary<int, LevelOverride> _levels;

    private LevelTemplateRegistry(Dictionary<int, LevelOverride> levels)
    {
        _levels = levels;
    }

    /// <summary>
    /// Load a LevelTemplateRegistry from a YAML string.
    /// Uses the same deserializer configuration as ContentLoader (IgnoreUnmatchedProperties,
    /// UnderscoredNamingConvention) plus the SpawnEntry custom converter.
    /// </summary>
    public static LevelTemplateRegistry FromYaml(string yaml) =>
        FromYaml(yaml, new AotObjectFactory());

    /// <summary>
    /// Injectable overload — pass <c>new AotObjectFactory(strict: true)</c> in tests
    /// to simulate NativeAOT behaviour and catch missing factory registrations early.
    /// </summary>
    public static LevelTemplateRegistry FromYaml(string yaml, IObjectFactory factory)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .WithObjectFactory(factory)
            .WithTypeConverter(new SpawnEntryConverter())
            .Build();

        var file = deserializer.Deserialize<LevelTemplatesFile>(yaml);
        var levels = new Dictionary<int, LevelOverride>();

        if (file?.Levels != null)
        {
            foreach (var (key, levelOverride) in file.Levels)
            {
                if (int.TryParse(key, out int depth))
                    levels[depth] = levelOverride;
            }
        }

        return new LevelTemplateRegistry(levels);
    }

    /// <summary>
    /// Load a LevelTemplateRegistry from a YAML file path.
    /// </summary>
    public static LevelTemplateRegistry FromFile(string path)
    {
        return FromYaml(File.ReadAllText(path));
    }

    /// <summary>
    /// Construct a registry containing a single depth → override entry.
    /// Used by LaunchTestScenario to build a temporary registry from a scenario's
    /// GuaranteedSpawns without writing a YAML string.
    ///
    /// GetLevelOverride(depth) returns the provided override.
    /// GetLevelOverride(any other depth) returns null.
    /// </summary>
    public static LevelTemplateRegistry FromSingleDepth(int depth, LevelOverride levelOverride)
    {
        var levels = new Dictionary<int, LevelOverride> { [depth] = levelOverride };
        return new LevelTemplateRegistry(levels);
    }

    /// <summary>
    /// Returns the LevelOverride for the given depth, or null if no override is configured.
    /// Callers should apply defaults when null is returned.
    /// </summary>
    public LevelOverride? GetLevelOverride(int depth)
    {
        return _levels.TryGetValue(depth, out var levelOverride) ? levelOverride : null;
    }

    /// <summary>All configured depths (for diagnostics / testing).</summary>
    public IEnumerable<int> ConfiguredDepths => _levels.Keys.OrderBy(d => d);
}
