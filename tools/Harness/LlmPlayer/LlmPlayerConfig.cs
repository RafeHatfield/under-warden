namespace UnderWarden.Harness.LlmPlayer;

/// <summary>
/// Configuration for an LLM Player run. Loaded from YAML; defaults used when file is missing.
/// YAML files live at: config/llm_player/reader.yaml, config/llm_player/system_explorer.yaml
/// </summary>
public sealed class LlmPlayerConfig
{
    public string Persona { get; set; } = "reader";
    public string Model { get; set; } = "claude-haiku-4-5-20251001";
    public int MaxTurns { get; set; } = 1500;
    public string FallbackPersona { get; set; } = "balanced";
    public List<string> SignificantEventHooks { get; set; } = new();

    /// <summary>
    /// Load config from a YAML file. Returns defaults if the file is missing or unparsable.
    /// Uses manual key:value parsing to avoid adding a dependency on YamlDotNet from
    /// the Harness project at startup (the logic layer YAML loaders use YamlDotNet but
    /// live in a separate context).
    /// </summary>
    public static LlmPlayerConfig FromYaml(string path)
    {
        var config = new LlmPlayerConfig();

        if (!File.Exists(path))
            return config;

        try
        {
            var lines = File.ReadAllLines(path);
            bool inHooks = false;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();

                if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                    continue;

                // Detect the significant_event_hooks list block
                if (line.StartsWith("significant_event_hooks:", StringComparison.OrdinalIgnoreCase))
                {
                    inHooks = true;
                    continue;
                }

                // YAML list item (- value)
                if (inHooks && line.StartsWith("- ", StringComparison.Ordinal))
                {
                    config.SignificantEventHooks.Add(line[2..].Trim());
                    continue;
                }

                // Any non-list line exits the hook block
                if (inHooks && !line.StartsWith("- ", StringComparison.Ordinal))
                    inHooks = false;

                // Key: value pairs
                int colonIdx = line.IndexOf(':', StringComparison.Ordinal);
                if (colonIdx < 0) continue;

                string key = line[..colonIdx].Trim().ToLowerInvariant();
                string value = line[(colonIdx + 1)..].Trim();

                // Strip inline comments
                int commentIdx = value.IndexOf('#');
                if (commentIdx >= 0)
                    value = value[..commentIdx].Trim();

                switch (key)
                {
                    case "persona":
                        config.Persona = value;
                        break;
                    case "model":
                        config.Model = value;
                        break;
                    case "max_turns" when int.TryParse(value, out int turns):
                        config.MaxTurns = turns;
                        break;
                    case "fallback_persona":
                        config.FallbackPersona = value;
                        break;
                }
            }
        }
        catch
        {
            // Parsing failed — fall back to defaults already set
        }

        return config;
    }
}
