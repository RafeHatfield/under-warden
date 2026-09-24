namespace UnderWarden.Analyst;

/// <summary>How a system-trigger's fire rate should be READ (rubric coverage_semantics).</summary>
public enum TriggerClass
{
    /// <summary>Murals/signs/flavor. Any rate including zero is fine — there is no mechanism to verify.</summary>
    Content,

    /// <summary>Faction-turning, possession, orc-rep. Low-but-nonzero is coherent playstyle; ZERO = unverified.</summary>
    Mechanism,

    /// <summary>Not yet classified by the rubric (classification is explicitly incomplete).</summary>
    Unclassified,
}

/// <summary>
/// The rubric's coverage_semantics, used by the AGGREGATE reporter to interpret the (neutral)
/// system-trigger heatmap. The threshold is ZERO, not "low": for a MECHANISM trigger, zero across
/// a batch means UNVERIFIED (route to a targeted run), never "broken"; low-but-nonzero is coherent
/// playstyle variation. CONTENT triggers are never flagged.
///
/// Naming bridge: the rubric's `examples` are detector names (e.g. possession_ability_grant) while
/// the transcript heatmap keys are system_triggers fields (e.g. possession_used). Classification
/// matches by exact key OR by leading stem (first underscore token), which bridges that gap for the
/// current trigger set. This is the seam to tighten as the rubric's per-trigger classification
/// completes; unmatched triggers fall to Unclassified and are reported neutrally, never flagged.
/// </summary>
public sealed class CoverageSemantics
{
    public IReadOnlyList<string> ContentExamples { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MechanismExamples { get; init; } = Array.Empty<string>();

    /// <summary>mechanism_triggers.zero_rate_is_evidence_gap (true in v1).</summary>
    public bool MechanismZeroRateIsEvidenceGap { get; init; } = true;

    public TriggerClass Classify(string trigger)
    {
        if (MatchesAny(trigger, ContentExamples)) return TriggerClass.Content;
        if (MatchesAny(trigger, MechanismExamples)) return TriggerClass.Mechanism;
        return TriggerClass.Unclassified;
    }

    private static bool MatchesAny(string trigger, IReadOnlyList<string> examples)
    {
        foreach (var e in examples)
        {
            if (string.Equals(trigger, e, StringComparison.Ordinal)) return true;
            if (string.Equals(Stem(trigger), Stem(e), StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>Leading underscore token: "possession_used" -> "possession", "orc_rep_changed" -> "orc".</summary>
    private static string Stem(string s)
    {
        int us = s.IndexOf('_');
        return us < 0 ? s : s[..us];
    }
}
