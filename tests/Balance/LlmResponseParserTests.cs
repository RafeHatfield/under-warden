using UnderWarden.Logic.Balance.LlmPlayer;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

[TestFixture]
[Category("Balance")]
[Description("LlmResponseParser: structured output parsing from LLM text responses")]
public class LlmResponseParserTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ValidJson(int actionIndex = 3, string reasoning = "Moving toward the staircase.",
        string? judgment = null, string? floorSummary = null, string? reflection = null)
    {
        string assessmentJson = judgment != null
            ? $@"{{""judgment"": ""{judgment}"", ""note"": ""test note""}}"
            : "null";

        string floorJson = floorSummary != null ? $@"""{floorSummary}""" : "null";
        string reflectionJson = reflection != null ? $@"""{reflection}""" : "null";

        return $@"{{
  ""action_index"": {actionIndex},
  ""action_label"": ""Move north"",
  ""reasoning"": ""{reasoning}"",
  ""structural_assessment"": {assessmentJson},
  ""floor_summary"": {floorJson},
  ""reflection"": {reflectionJson}
}}";
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Test]
    [Description("Valid JSON with action_index 3, reasoning, null structural_assessment parses correctly")]
    public void ValidJson_ParsesCorrectly()
    {
        string json = ValidJson(actionIndex: 3);

        bool ok = LlmResponseParser.TryParse(json, actionCount: 5, out var result, out var error);

        Assert.That(ok, Is.True, $"Expected parse success but got error: {error}");
        Assert.That(result.ActionIndex, Is.EqualTo(3));
        Assert.That(result.Reasoning, Is.EqualTo("Moving toward the staircase."));
        Assert.That(result.StructuralAssessment, Is.Null);
    }

    [Test]
    [Description("JSON wrapped in ```json code fences is unwrapped and parsed successfully")]
    public void JsonWithCodeFences_StripsFencesAndParses()
    {
        string inner = ValidJson(actionIndex: 2);
        string fenced = $"```json\n{inner}\n```";

        bool ok = LlmResponseParser.TryParse(fenced, actionCount: 5, out var result, out var error);

        Assert.That(ok, Is.True, $"Expected parse success but got error: {error}");
        Assert.That(result.ActionIndex, Is.EqualTo(2));
    }

    [Test]
    [Description("JSON with structural_assessment judgment 'forced_move' parses to non-null assessment")]
    public void ValidJson_WithStructuralAssessment_ParsesJudgment()
    {
        string json = ValidJson(judgment: "forced_move");

        bool ok = LlmResponseParser.TryParse(json, actionCount: 5, out var result, out var error);

        Assert.That(ok, Is.True, $"Expected parse success but got error: {error}");
        Assert.That(result.StructuralAssessment, Is.Not.Null);
        Assert.That(result.StructuralAssessment!.Judgment, Is.EqualTo("forced_move"));
        Assert.That(result.StructuralAssessment.Note, Is.EqualTo("test note"));
    }

    [Test]
    [Description("Structural assessment with out-of-vocabulary judgment returns false")]
    public void InvalidJudgment_ReturnsFalse()
    {
        string json = ValidJson(judgment: "feeling_bad");

        bool ok = LlmResponseParser.TryParse(json, actionCount: 5, out _, out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("feeling_bad"));
    }

    [Test]
    [Description("action_index of 0 with actionCount 5 returns false")]
    public void ActionIndexZero_ReturnsFalse()
    {
        string json = ValidJson(actionIndex: 0);

        bool ok = LlmResponseParser.TryParse(json, actionCount: 5, out _, out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("action_index out of range"));
    }

    [Test]
    [Description("action_index exceeding actionCount returns false")]
    public void ActionIndexOverCount_ReturnsFalse()
    {
        string json = ValidJson(actionIndex: 6);

        bool ok = LlmResponseParser.TryParse(json, actionCount: 5, out _, out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("action_index out of range"));
    }

    [Test]
    [Description("Malformed JSON returns false with an error message")]
    public void MalformedJson_ReturnsFalse()
    {
        bool ok = LlmResponseParser.TryParse("not json", actionCount: 5, out _, out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    [Description("floor_summary field round-trips through parse")]
    public void FloorSummary_RoundTrips()
    {
        string json = ValidJson(actionIndex: 1, floorSummary: "Floor done");

        bool ok = LlmResponseParser.TryParse(json, actionCount: 5, out var result, out var error);

        Assert.That(ok, Is.True, $"Expected parse success but got error: {error}");
        Assert.That(result.FloorSummary, Is.EqualTo("Floor done"));
    }

    [Test]
    [Description("reflection field round-trips through parse")]
    public void Reflection_RoundTrips()
    {
        string json = ValidJson(actionIndex: 1, reflection: "near death reflection");

        bool ok = LlmResponseParser.TryParse(json, actionCount: 5, out var result, out var error);

        Assert.That(ok, Is.True, $"Expected parse success but got error: {error}");
        Assert.That(result.Reflection, Is.EqualTo("near death reflection"));
    }

    [Test]
    [Description("structural_assessment:null parses to StructuredOutput.StructuralAssessment == null")]
    public void NullStructuralAssessment_ParsesCorrectly()
    {
        string json = ValidJson(actionIndex: 1, judgment: null);

        bool ok = LlmResponseParser.TryParse(json, actionCount: 5, out var result, out var error);

        Assert.That(ok, Is.True, $"Expected parse success but got error: {error}");
        Assert.That(result.StructuralAssessment, Is.Null);
    }

    [Test]
    [Description("All four valid judgment values are accepted")]
    public void AllValidJudgments_AreAccepted()
    {
        string[] validJudgments = { "dead_action_space", "forced_move", "novel_encounter", "system_unreachable" };

        foreach (var judgment in validJudgments)
        {
            string json = ValidJson(judgment: judgment);
            bool ok = LlmResponseParser.TryParse(json, actionCount: 5, out _, out var error);
            Assert.That(ok, Is.True, $"Judgment '{judgment}' should be valid but got: {error}");
        }
    }

    [Test]
    [Description("Plain (unfenced) code fence variation with just triple backticks works")]
    public void JsonWithPlainCodeFences_StripsFencesAndParses()
    {
        string inner = ValidJson(actionIndex: 1);
        string fenced = $"```\n{inner}\n```";

        bool ok = LlmResponseParser.TryParse(fenced, actionCount: 5, out var result, out var error);

        Assert.That(ok, Is.True, $"Expected parse success but got error: {error}");
        Assert.That(result.ActionIndex, Is.EqualTo(1));
    }

    [Test]
    [Description("action_index exactly equal to actionCount is valid")]
    public void ActionIndexAtBoundary_IsValid()
    {
        string json = ValidJson(actionIndex: 5);

        bool ok = LlmResponseParser.TryParse(json, actionCount: 5, out var result, out var error);

        Assert.That(ok, Is.True, $"Expected parse success but got error: {error}");
        Assert.That(result.ActionIndex, Is.EqualTo(5));
    }
}
