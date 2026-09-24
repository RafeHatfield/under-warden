using System.Globalization;
using System.Text.RegularExpressions;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Formats a MemoDefinition into a rendered (Subject, Body) pair by applying
/// slot substitution and cause-of-death display name resolution.
///
/// Slot syntax: {slot_name} in both subject and body strings.
///
/// Special slot — {cause_of_death}:
///   If the slots dict contains "cause_of_death" AND the registry has a display name
///   for that value, the display name is substituted.
///   If no display name is registered, the fallback is: underscores → spaces, title-case
///   (e.g. "orc_brute" → "Orc Brute").
///
/// Slots not present in the dict are left as-is (no exception).
/// </summary>
public sealed class MemoFormatter
{
    // Matches {slot_name} — slot names are lowercase letters, digits, underscores.
    private static readonly Regex SlotPattern = new(@"\{([a-z0-9_]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Format a memo with the given slots dictionary.
    ///
    /// fireIndex=0: uses body[0] (canonical first-fire text).
    /// fireIndex=1+: uses body[fireIndex] if it exists; falls back to body[0] if not.
    /// </summary>
    public (string Subject, string Body) Format(
        MemoDefinition memo,
        int fireIndex,
        Dictionary<string, string> slots,
        MemoRegistry registry)
    {
        // Select body variant. Body list is always non-null per MemoDefinition contract.
        // fireIndex=0 → canonical first-fire body[0]
        // fireIndex>0 → body[fireIndex] if it exists; clamp to body[last] rather than
        //   falling back to body[0], so the Under-Warden's weariness progression (shortest,
        //   most exhausted variant) is the steady state for repeat offenders, not the full
        //   explanation given on a first encounter.
        var bodyList = memo.Body;
        string rawBody;
        if (bodyList.Count == 0)
            rawBody = "";
        else if (fireIndex <= 0)
            rawBody = bodyList[0];
        else
            rawBody = bodyList[Math.Min(fireIndex, bodyList.Count - 1)];

        var subject = ApplySlots(memo.Subject, slots, registry);
        var body    = ApplySlots(rawBody, slots, registry);

        return (subject, body);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static string ApplySlots(string text, Dictionary<string, string> slots, MemoRegistry registry)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('{'))
            return text;

        return SlotPattern.Replace(text, match =>
        {
            var slotName = match.Groups[1].Value;

            if (!slots.TryGetValue(slotName, out var rawValue))
                // Slot key not in dict — leave the placeholder as-is
                return match.Value;

            // Special handling for cause_of_death: resolve through registry first
            if (slotName == "cause_of_death")
            {
                var displayName = registry.GetCauseDisplayName(rawValue);
                if (displayName != null)
                    return displayName;

                // Fallback: underscore → space, title-case
                return TitleCase(rawValue.Replace('_', ' '));
            }

            return rawValue;
        });
    }

    /// <summary>
    /// Convert a space-separated string to title case using invariant culture.
    /// "some unknown cause" → "Some Unknown Cause"
    /// </summary>
    private static string TitleCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // TextInfo.ToTitleCase requires lower-case input to work correctly on all words.
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(input.ToLowerInvariant());
    }
}
