using UnderWarden.Logic.Core;
using UnderWarden.Logic.Persistence.Namespaces;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Selects a catalog entry template for a past-Sasha record and fills its slots.
///
/// Priority logic (first match wins):
///   1. the_recent_one  — most recent record in the namespace
///   2. the_one_we_kept — DiedFloor >= BestFloorReachedAtDeath (milestone run)
///   3. the_almost      — DiedFloor 20–24, not warden-initiated
///   4. good_gear       — GearCarried has at least one notable item
///   5. the_stupid_one  — self-inflicted cause of death
///   6. no_warning      — monster kill + first encounter + DiedFloor >= 8
///   7. the_patient_one — PreviousRunWasClean
///   8. early_disaster  — catch-all
/// </summary>
public static class CatalogEntryRenderer
{
    private static readonly HashSet<string> SelfInflictedCauses = new(StringComparer.Ordinal)
    {
        "oil_slick_fire", "own_poison", "own_trap", "possessed_wrong_host",
    };

    // Floor milestone phrases (the_one_we_kept slot: {floor_milestone})
    private static string FloorMilestone(int floor) => floor switch
    {
        >= 3 and <= 5 => "the Crypt",
        >= 6 and <= 8 => "the Boundary",
        >= 9 and <= 12 => "the Dimhalls",
        >= 13 and <= 17 => "the Weighing",
        _ => "the Inner Court",
    };

    // Region names (the_one_we_kept slot: {region_name})
    private static string RegionName(int floor) => floor switch
    {
        >= 1 and <= 5 => "Reven Crypt",
        >= 6 and <= 8 => "the Boundary",
        >= 9 and <= 12 => "the Dimhalls",
        >= 13 and <= 17 => "the Weighing",
        _ => "the Inner Court",
    };

    // Hazard readable names (the_stupid_one slot: {hazard})
    private static string HazardName(string causeOfDeath) => causeOfDeath switch
    {
        "oil_slick_fire" => "oil",
        "own_poison" => "poison cloud",
        "own_trap" => "trap",
        "possessed_wrong_host" => "body",
        _ => "hazard",
    };

    // Primary gear display name: prefer weapon, fallback to "kit"
    private static string GearItemName(PastSashaRecord record)
    {
        // Notable items take priority
        var notable = record.GearCarried.FirstOrDefault(g => g.IsNotable);
        if (notable != null)
            return FormatItem(notable);

        // Otherwise use any gear, preferring non-ring slots (TypeId heuristics)
        var primary = record.GearCarried
            .FirstOrDefault(g => !g.TypeId.Contains("ring", StringComparison.OrdinalIgnoreCase));
        if (primary != null)
            return FormatItem(primary);

        var any = record.GearCarried.FirstOrDefault();
        return any != null ? FormatItem(any) : "kit";
    }

    private static string FormatItem(GearItemRecord item)
    {
        // e.g. "shortsword" → "shortsword", +1 → "shortsword +1"
        var name = item.TypeId.Replace('_', ' ');
        return item.Enchantment > 0 ? $"{name} +{item.Enchantment}" : name;
    }

    /// <summary>
    /// Determines which template category applies to this record.
    /// <paramref name="allRecords"/> is the full PastSashasData for the_recent_one check.
    /// </summary>
    public static string SelectTemplateCategory(PastSashaRecord record, PastSashasData data)
    {
        // 1. Most recent record wins the_recent_one slot.
        var mostRecentId = data.Records.Count > 0 ? data.Records.Max(r => r.Id) : -1;
        if (record.Id == mostRecentId)
            return "the_recent_one";

        // 2. Best-run-so-far milestone.
        if (record.BestFloorReachedAtDeath > 0 && record.DiedFloor >= record.BestFloorReachedAtDeath)
            return "the_one_we_kept";

        // 3. Near-final (floors 20–24), not warden-caught at the final audit.
        if (record.DiedFloor >= 20 && record.DiedFloor <= 24
            && !string.Equals(record.CauseOfDeath, "under_warden", StringComparison.Ordinal))
            return "the_almost";

        // 4. Notable gear.
        if (record.GearCarried.Any(g => g.IsNotable))
            return "good_gear";

        // 5. Self-inflicted death.
        if (SelfInflictedCauses.Contains(record.CauseOfDeath))
            return "the_stupid_one";

        // 6. Monster kill, first encounter, deep enough to be a real surprise.
        if (string.Equals(record.CauseOfDeath, "monster", StringComparison.Ordinal)
            && record.KillerWasFirstEncounter
            && record.DiedFloor >= 8)
            return "no_warning";

        // 7. Clean previous run heuristic.
        if (record.PreviousRunWasClean)
            return "the_patient_one";

        // 8. Catch-all.
        return "early_disaster";
    }

    /// <summary>
    /// Render a catalog entry for the given record.
    /// Returns the header + empty_state strings when the catalog YAML is provided
    /// but does no line resolution (the UI layer renders those from the registry).
    /// Template line slot-filling is done here.
    /// </summary>
    public static string RenderEntry(PastSashaRecord record, PastSashasData data,
        VoiceLineRegistry registry, SeededRandom rng)
    {
        var category = SelectTemplateCategory(record, data);
        var triggerId = $"entry_templates.{category}";
        var template = registry.GetLine(triggerId, rng) ?? FallbackTemplate(record);
        return FillSlots(template, record, category);
    }

    private static string FillSlots(string template, PastSashaRecord record, string category)
    {
        var result = template
            .Replace("{run_number}", record.DiedRun.ToString())
            .Replace("{floor}", record.DiedFloor.ToString())
            .Replace("{gear_item}", GearItemName(record));

        if (category == "the_stupid_one")
            result = result.Replace("{hazard}", HazardName(record.CauseOfDeath));

        if (category == "the_one_we_kept")
        {
            result = result
                .Replace("{floor_milestone}", FloorMilestone(record.DiedFloor))
                .Replace("{region_name}", RegionName(record.DiedFloor));
        }

        return result;
    }

    private static string FallbackTemplate(PastSashaRecord record) =>
        $"#{record.DiedRun}. Floor {record.DiedFloor}. He had ambitions on this one. The {{gear_item}} is yours now.";
}
