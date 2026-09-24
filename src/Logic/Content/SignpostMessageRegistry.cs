using UnderWarden.Logic.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Logic.Content;

/// <summary>
/// One entry in the signpost message pool.
/// </summary>
public sealed class SignpostMessageEntry
{
    [YamlMember(Alias = "text")]
    public string Text { get; set; } = "";

    [YamlMember(Alias = "min_depth")]
    public int MinDepth { get; set; } = 1;

    [YamlMember(Alias = "max_depth")]
    public int MaxDepth { get; set; } = 99;
}

/// <summary>
/// Root YAML structure for signpost_messages.yaml.
/// </summary>
internal sealed class SignpostMessagesFile
{
    [YamlMember(Alias = "messages")]
    public SignpostMessagesByType Messages { get; set; } = new();
}

/// <summary>
/// Messages grouped by sign type.
/// </summary>
public sealed class SignpostMessagesByType
{
    [YamlMember(Alias = "lore")]
    public List<SignpostMessageEntry> Lore { get; set; } = new();

    [YamlMember(Alias = "warning")]
    public List<SignpostMessageEntry> Warning { get; set; } = new();

    [YamlMember(Alias = "humor")]
    public List<SignpostMessageEntry> Humor { get; set; } = new();

    [YamlMember(Alias = "hint")]
    public List<SignpostMessageEntry> Hint { get; set; } = new();

    [YamlMember(Alias = "directional")]
    public List<SignpostMessageEntry> Directional { get; set; } = new();
}

/// <summary>
/// Loads signpost messages from config/signpost_messages.yaml and selects
/// depth-appropriate messages by type.
///
/// Fallback: when no messages match the depth/type, returns a hardcoded neutral string
/// instead of throwing. This prevents empty signposts from crashing in edge cases.
/// </summary>
public sealed class SignpostMessageRegistry
{
    private const string FallbackMessage = "The signpost is worn smooth.";

    private readonly Dictionary<string, List<SignpostMessageEntry>> _messagesByType;

    private SignpostMessageRegistry(Dictionary<string, List<SignpostMessageEntry>> messagesByType)
    {
        _messagesByType = messagesByType;
    }

    /// <summary>
    /// Load from a YAML string. Uses YamlDotNet with underscore naming convention.
    /// </summary>
    public static SignpostMessageRegistry FromYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var file = deserializer.Deserialize<SignpostMessagesFile>(yaml);
        var msgs = file?.Messages ?? new SignpostMessagesByType();

        var dict = new Dictionary<string, List<SignpostMessageEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            ["lore"]        = msgs.Lore,
            ["warning"]     = msgs.Warning,
            ["humor"]       = msgs.Humor,
            ["hint"]        = msgs.Hint,
            ["directional"] = msgs.Directional,
        };

        return new SignpostMessageRegistry(dict);
    }

    /// <summary>
    /// Load from a file path. Convenience wrapper for tests and harness.
    /// </summary>
    public static SignpostMessageRegistry FromFile(string path)
        => FromYaml(File.ReadAllText(path));

    /// <summary>
    /// Select a random message for the given sign type and depth.
    /// Depth filtering: min_depth &lt;= depth &lt;= max_depth.
    /// Returns (message, signType). If no candidates exist, returns the fallback and the requested type.
    /// </summary>
    public (string Message, string SignType) GetRandomMessage(string signType, int depth, SeededRandom rng)
    {
        if (!_messagesByType.TryGetValue(signType, out var pool))
            return (FallbackMessage, signType);

        var candidates = pool
            .Where(e => e.MinDepth <= depth && e.MaxDepth >= depth)
            .ToList();

        if (candidates.Count == 0)
            return (FallbackMessage, signType);

        var chosen = candidates[rng.Next(candidates.Count)];
        return (chosen.Text, signType);
    }

    /// <summary>
    /// All valid sign types supported by this registry.
    /// </summary>
    public IReadOnlyList<string> SignTypes => new[] { "lore", "warning", "humor", "hint", "directional" };
}
