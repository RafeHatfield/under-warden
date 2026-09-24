using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Logic.Balance.Etp;

/// <summary>
/// Loads etp_config.yaml into an EtpConfig instance.
/// Follows the same pattern as ContentLoader — uses YamlDotNet with
/// UnderscoredNamingConvention to match the YAML's snake_case field names.
/// </summary>
public static class EtpConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Load EtpConfig from the given file path.
    /// Throws InvalidOperationException if the file is missing or malformed.
    /// </summary>
    public static EtpConfig FromFile(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"ETP config not found: '{path}'");

        var yaml = File.ReadAllText(path);
        return FromYaml(yaml);
    }

    /// <summary>
    /// Parse EtpConfig from a YAML string. Used for testing.
    /// </summary>
    public static EtpConfig FromYaml(string yaml)
    {
        var cfg = Deserializer.Deserialize<EtpConfig>(yaml);
        if (cfg == null)
            throw new InvalidOperationException("Failed to deserialize EtpConfig.");
        return cfg;
    }
}
