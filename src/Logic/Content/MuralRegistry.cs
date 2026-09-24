using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Logic.Content;

/// <summary>
/// One entry in the mural pool.
/// </summary>
public sealed class MuralEntry
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "text")]
    public string Text { get; set; } = "";

    [YamlMember(Alias = "min_depth")]
    public int MinDepth { get; set; } = 1;

    [YamlMember(Alias = "max_depth")]
    public int MaxDepth { get; set; } = 99;
}

/// <summary>
/// Root YAML structure for murals_inscriptions.yaml.
/// </summary>
internal sealed class MuralsFile
{
    [YamlMember(Alias = "murals")]
    public List<MuralEntry> Murals { get; set; } = new();
}

/// <summary>
/// Loads mural definitions from config/murals_inscriptions.yaml.
/// Exposes a depth-filtered view for MuralTracker selection.
/// Immutable after construction.
/// </summary>
public sealed class MuralRegistry
{
    private readonly IReadOnlyList<MuralEntry> _murals;

    private MuralRegistry(IReadOnlyList<MuralEntry> murals)
    {
        _murals = murals;
    }

    /// <summary>
    /// Load from a YAML string.
    /// </summary>
    public static MuralRegistry FromYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var file = deserializer.Deserialize<MuralsFile>(yaml);
        return new MuralRegistry(file?.Murals ?? new List<MuralEntry>());
    }

    /// <summary>
    /// Load from a file path. Convenience wrapper for tests and harness.
    /// </summary>
    public static MuralRegistry FromFile(string path)
        => FromYaml(File.ReadAllText(path));

    /// <summary>
    /// Return all murals eligible at the given depth.
    /// Depth filtering: min_depth &lt;= depth &lt;= max_depth.
    /// </summary>
    public IReadOnlyList<(string Id, string Text)> GetAllForDepth(int depth)
    {
        return _murals
            .Where(m => m.MinDepth <= depth && m.MaxDepth >= depth)
            .Select(m => (m.Id, m.Text))
            .ToList();
    }

    /// <summary>Total number of mural entries in the registry.</summary>
    public int Count => _murals.Count;
}
