using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Balance.LlmPlayer;

/// <summary>Deserialized response from the LLM player API call.</summary>
public sealed class StructuredOutput
{
    [JsonPropertyName("action_index")]
    public int ActionIndex { get; set; }        // 1-based

    [JsonPropertyName("action_label")]
    public string ActionLabel { get; set; } = "";

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = "";

    /// <summary>Null when no structural assessment is warranted (most turns).</summary>
    [JsonPropertyName("structural_assessment")]
    public StructuredOutputAssessment? StructuralAssessment { get; set; }

    /// <summary>Non-null only when the prompt contained a FLOOR COMPLETE block.</summary>
    [JsonPropertyName("floor_summary")]
    public string? FloorSummary { get; set; }

    /// <summary>Non-null only when the prompt contained a REFLECTION block.</summary>
    [JsonPropertyName("reflection")]
    public string? Reflection { get; set; }
}

public sealed class StructuredOutputAssessment
{
    [JsonPropertyName("judgment")]
    public string Judgment { get; set; } = "";

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";
}

public static class LlmResponseParser
{
    // Valid structural assessment vocabulary — must exactly match the rubric.
    // "floor_summary" and "reflection" are reserved labels used internally by
    // LlmBotBrain to store per-floor and per-hook answers; they are NOT valid
    // judgment values the LLM is permitted to emit.
    private static readonly HashSet<string> ValidJudgments = new(StringComparer.Ordinal)
    {
        "dead_action_space",
        "forced_move",
        "novel_encounter",
        "system_unreachable",
    };

    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Parse the raw LLM text response into a StructuredOutput.
    /// Strips markdown code fences if present.
    /// Returns false on any failure (malformed JSON, out-of-range action_index,
    /// invalid judgment vocabulary).
    /// Never throws.
    /// </summary>
    public static bool TryParse(string raw, int actionCount, out StructuredOutput result, out string error)
    {
        result = null!;
        error = "";

        try
        {
            string text = StripCodeFences(raw.Trim());

            var parsed = JsonSerializer.Deserialize<StructuredOutput>(text, _options);
            if (parsed == null)
            {
                error = "Deserialized to null";
                return false;
            }

            if (parsed.ActionIndex < 1 || parsed.ActionIndex > actionCount)
            {
                error = $"action_index out of range: {parsed.ActionIndex} (valid: 1–{actionCount})";
                return false;
            }

            if (parsed.StructuralAssessment != null &&
                !ValidJudgments.Contains(parsed.StructuralAssessment.Judgment))
            {
                error = $"invalid judgment: {parsed.StructuralAssessment.Judgment}";
                return false;
            }

            result = parsed;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Strip markdown code fences: handles ```json\n...\n``` and ```\n...\n```.
    /// Returns the original string unchanged if no fences are found.
    /// </summary>
    private static string StripCodeFences(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;

        // Find the end of the opening fence line (may be "```" or "```json" etc.)
        int newlinePos = text.IndexOf('\n');
        if (newlinePos < 0)
            return text; // malformed fence — let JSON parse fail

        // Find the closing fence
        int closingFence = text.LastIndexOf("```", StringComparison.Ordinal);
        if (closingFence <= newlinePos)
            return text; // no distinct closing fence

        // Extract content between opening-fence-newline and closing-fence
        string inner = text.Substring(newlinePos + 1, closingFence - newlinePos - 1);
        return inner.Trim();
    }
}
