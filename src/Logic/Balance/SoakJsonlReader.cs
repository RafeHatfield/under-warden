using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Reads JSONL files produced by the dungeon soak harness (--jsonl flag) and
/// reconstructs DungeonSoakRunResult objects for offline analysis.
///
/// The JSONL format uses snake_case property names (PropertyNamingPolicy.SnakeCaseLower),
/// matching the serialization options in Program.cs RunSoakWithStreaming().
/// Dictionary keys (ActionCounts, ReasonCounts, ContextCounts) are NOT snake_cased —
/// they are data values that analysis tools key on.
/// </summary>
public static class SoakJsonlReader
{
    // Must match the options used in RunSoakWithStreaming() in Program.cs.
    // SnakeCaseLower for properties, null for dictionary keys (data values, not property names),
    // WhenWritingNull for omit-null fields (e.g. BotSummary when telemetry is off).
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        // Dictionary keys: null policy — keys are not transformed, matching the writer.
        DictionaryKeyPolicy         = null,
        // Tolerate unknown fields gracefully (forward-compatible with schema additions).
        // System.Text.Json ignores unknown fields by default — no explicit option needed.
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Read a JSONL file and aggregate all runs into a DungeonSoakSummary.
    ///
    /// Each valid line is deserialized as a DungeonSoakRunResult. Malformed lines
    /// are skipped with a warning to stderr; they do not abort the read.
    ///
    /// Returns a 0-run summary if the file is empty or all lines are malformed.
    /// </summary>
    public static DungeonSoakSummary ReadFromFile(string path)
    {
        var runs = ReadRunsFromFile(path).ToList();
        return DungeonSoakSummary.ComputeFrom(runs);
    }

    /// <summary>
    /// Read a JSONL file line by line, yielding one DungeonSoakRunResult per valid line.
    /// Malformed lines are skipped with a warning to stderr.
    ///
    /// Streaming: does not load the entire file into memory at once.
    /// </summary>
    public static IEnumerable<DungeonSoakRunResult> ReadRunsFromFile(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"[SoakJsonlReader] File not found: {path}");
            yield break;
        }

        int lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;

            // Skip blank lines silently — they're harmless (e.g. trailing newline)
            if (string.IsNullOrWhiteSpace(line))
                continue;

            DungeonSoakRunResult? result = null;
            try
            {
                result = JsonSerializer.Deserialize<DungeonSoakRunResult>(line, ReadOptions);
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine(
                    $"[SoakJsonlReader] Skipping malformed line {lineNumber}: {ex.Message}");
            }

            if (result != null)
                yield return result;
        }
    }
}
