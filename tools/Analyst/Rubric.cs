using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Analyst;

/// <summary>Raised when the rubric is malformed or violates the Analyst's interface contract.</summary>
public sealed class RubricLoadException(string message) : Exception(message);

/// <summary>
/// The mechanisms the BugDetector dispatch RECOGNIZES. `predicate` is implemented; the other
/// three are recognized-but-stubbed (log-and-skip) this phase. A mechanism NOT in this set is a
/// fatal load error — an unknown mechanism that silently did nothing is exactly the
/// "reports-clean-but-cannot-fire" failure this system exists to prevent.
/// </summary>
public static class Mechanisms
{
    public const string Predicate = "predicate";
    public const string TextPattern = "text_pattern";
    public const string LlmJudged = "llm_judged";
    public const string TriggerConsequence = "trigger_consequence";

    public static readonly IReadOnlySet<string> Known =
        new HashSet<string> { Predicate, TextPattern, LlmJudged, TriggerConsequence };

    public static readonly IReadOnlySet<string> Implemented =
        new HashSet<string> { Predicate };
}

/// <summary>One bug category from the rubric, with its dispatch mechanism.</summary>
public sealed class BugCategory
{
    public required string Name { get; init; }
    public required string Mechanism { get; init; }
    public string Description { get; init; } = "";
    public string Scope { get; init; } = "runtime";

    /// <summary>Parsed predicate (predicate mechanism only). Null for other mechanisms.</summary>
    public PredicateExpression? Predicate { get; init; }
}

/// <summary>The loaded, validated rubric. v1 reads bug_categories + structural_judgment_schema
/// + coverage_semantics (for the Phase-4 aggregate heatmap interpretation).</summary>
public sealed class Rubric
{
    public required int SchemaVersion { get; init; }
    public required IReadOnlyList<BugCategory> BugCategories { get; init; }
    public required IReadOnlyList<string> StructuralJudgmentSchema { get; init; }

    /// <summary>Heatmap interpretation rules. Defaults (empty examples) if the rubric omits the block.</summary>
    public required CoverageSemantics CoverageSemantics { get; init; }
}

// ── YAML DTOs (UnderscoredNamingConvention + IgnoreUnmatchedProperties drops the rest) ──

internal sealed class RubricDto
{
    public int SchemaVersion { get; set; } = -1;
    public List<string>? StructuralJudgmentSchema { get; set; }
    public Dictionary<string, BugCategoryDto>? BugCategories { get; set; }
    public CoverageSemanticsDto? CoverageSemantics { get; set; }
}

internal sealed class CoverageSemanticsDto
{
    public CoverageClassDto? ContentTriggers { get; set; }
    public CoverageClassDto? MechanismTriggers { get; set; }
}

internal sealed class CoverageClassDto
{
    public bool ZeroRateIsEvidenceGap { get; set; }
    public bool LowNonzeroIsCoherent { get; set; }
    public List<string>? Examples { get; set; }
}

internal sealed class BugCategoryDto
{
    public string? Mechanism { get; set; }
    public string? Description { get; set; }
    public string? Predicate { get; set; }
    // scope may be a scalar ("runtime") or a sequence (["runtime","static"]); accept either as object.
    public object? Scope { get; set; }
}

/// <summary>
/// Reads and validates the rubric YAML against the Analyst's interface. Validation is strict by
/// design (plan-analyst §1): a missing mechanism, an unknown mechanism, a missing predicate, or
/// a malformed predicate are all FATAL — never silently skipped — because a check that cannot run
/// is worse than no check. Unknown TOP-LEVEL keys (coherence_dimensions, coverage_semantics) are
/// ignored gracefully so the rubric can carry later-phase content without breaking v1 loading.
/// </summary>
public static class RubricLoader
{
    public const int SupportedSchemaVersion = 1;

    public static Rubric LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new RubricLoadException($"Rubric not found: {path}");
        return LoadFromText(File.ReadAllText(path), path);
    }

    public static Rubric LoadFromText(string yaml, string source = "<text>")
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        RubricDto dto;
        try
        {
            dto = deserializer.Deserialize<RubricDto>(yaml) ?? throw new RubricLoadException($"{source}: empty rubric.");
        }
        catch (RubricLoadException) { throw; }
        catch (Exception ex)
        {
            throw new RubricLoadException($"{source}: YAML parse error — {ex.Message}");
        }

        if (dto.SchemaVersion != SupportedSchemaVersion)
            throw new RubricLoadException(
                $"{source}: schema_version {dto.SchemaVersion} unsupported (this Analyst supports v{SupportedSchemaVersion}).");

        var categories = new List<BugCategory>();
        foreach (var (name, c) in dto.BugCategories ?? new())
        {
            if (string.IsNullOrWhiteSpace(c.Mechanism))
                throw new RubricLoadException($"{source}: bug_category '{name}' is missing required field 'mechanism'.");

            if (!Mechanisms.Known.Contains(c.Mechanism))
                throw new RubricLoadException(
                    $"{source}: bug_category '{name}' has unknown mechanism '{c.Mechanism}'. " +
                    $"Known: {string.Join(", ", Mechanisms.Known)}.");

            PredicateExpression? predicate = null;
            if (c.Mechanism == Mechanisms.Predicate)
            {
                if (string.IsNullOrWhiteSpace(c.Predicate))
                    throw new RubricLoadException($"{source}: predicate category '{name}' is missing required field 'predicate'.");
                try
                {
                    predicate = PredicateExpression.Parse(c.Predicate);
                }
                catch (PredicateException ex)
                {
                    throw new RubricLoadException($"{source}: category '{name}' has a malformed predicate — {ex.Message}");
                }
            }

            categories.Add(new BugCategory
            {
                Name        = name,
                Mechanism   = c.Mechanism,
                Description = c.Description?.Trim() ?? "",
                Scope       = ScopeToString(c.Scope),
                Predicate   = predicate,
            });
        }

        return new Rubric
        {
            SchemaVersion            = dto.SchemaVersion,
            BugCategories            = categories,
            StructuralJudgmentSchema = dto.StructuralJudgmentSchema ?? new(),
            CoverageSemantics        = new CoverageSemantics
            {
                ContentExamples = dto.CoverageSemantics?.ContentTriggers?.Examples ?? new(),
                MechanismExamples = dto.CoverageSemantics?.MechanismTriggers?.Examples ?? new(),
                MechanismZeroRateIsEvidenceGap =
                    dto.CoverageSemantics?.MechanismTriggers?.ZeroRateIsEvidenceGap ?? true,
            },
        };
    }

    private static string ScopeToString(object? scope) => scope switch
    {
        null => "runtime",
        string s => s,
        IEnumerable<object> seq => string.Join(",", seq),
        _ => scope.ToString() ?? "runtime",
    };
}
