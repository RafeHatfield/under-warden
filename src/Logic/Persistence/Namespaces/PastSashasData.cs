using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.Namespaces;

public sealed class PastSashasData
{
    [JsonPropertyName("records")]
    public List<PastSashaRecord> Records { get; set; } = new();

    [JsonPropertyName("next_id")]
    public int NextId { get; set; } = 1;

    /// <summary>
    /// Returns records eligible for Variant 3 possession spawn — those not already encountered
    /// this run. The floor builder calls this to find past-Sasha candidates; it should prefer
    /// the most-recent eligible record (highest Id), falling back to older ones.
    /// </summary>
    public IEnumerable<PastSashaRecord> GetEligibleRecords(IReadOnlySet<int> encounteredThisRun) =>
        Records.Where(r => !encounteredThisRun.Contains(r.Id))
               .OrderByDescending(r => r.Id);

    public PastSashaRecord AddRecord(
        int diedRun, int diedFloor, string causeOfDeath,
        string? killerSpecies, List<GearItemRecord> gearCarried,
        int bestFloorReachedAtDeath = 0, bool previousRunWasClean = false,
        bool killerWasFirstEncounter = false)
    {
        var record = new PastSashaRecord
        {
            Id = NextId++,
            DiedRun = diedRun,
            DiedFloor = diedFloor,
            DiedAt = DateTimeOffset.UtcNow,
            CauseOfDeath = causeOfDeath,
            KillerSpecies = killerSpecies,
            GearCarried = gearCarried,
            BestFloorReachedAtDeath = bestFloorReachedAtDeath,
            PreviousRunWasClean = previousRunWasClean,
            KillerWasFirstEncounter = killerWasFirstEncounter,
        };
        Records.Add(record);
        return record;
    }
}

public sealed class PastSashaRecord
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("died_run")]
    public int DiedRun { get; set; }

    [JsonPropertyName("died_floor")]
    public int DiedFloor { get; set; }

    [JsonPropertyName("died_at")]
    public DateTimeOffset DiedAt { get; set; }

    // "monster" | "self_inflicted" | "under_warden"
    [JsonPropertyName("cause_of_death")]
    public string CauseOfDeath { get; set; } = "monster";

    [JsonPropertyName("killer_species")]
    public string? KillerSpecies { get; set; }

    [JsonPropertyName("gear_carried")]
    public List<GearItemRecord> GearCarried { get; set; } = new();

    /// <summary>
    /// The player's all-time best floor reached up to and including this run, at death time.
    /// Used by CatalogEntryRenderer to evaluate the_one_we_kept category.
    /// Snapshotted at death so the check doesn't drift as the player later surpasses this depth.
    /// </summary>
    [JsonPropertyName("best_floor_reached_at_death")]
    public int BestFloorReachedAtDeath { get; set; }

    /// <summary>
    /// True if this run was "clean" — no self-inflicted death, floor ≥ 10.
    /// Drives the_patient_one catalog category.
    /// </summary>
    [JsonPropertyName("previous_run_was_clean")]
    public bool PreviousRunWasClean { get; set; }

    /// <summary>
    /// True if the killing monster species had not been previously engaged in this run.
    /// Drives the no_warning catalog category.
    /// </summary>
    [JsonPropertyName("killer_was_first_encounter")]
    public bool KillerWasFirstEncounter { get; set; }
}

public sealed class GearItemRecord
{
    [JsonPropertyName("type_id")]
    public string TypeId { get; set; } = "";

    [JsonPropertyName("enchantment")]
    public int Enchantment { get; set; }

    // "normal" | "corroded" | etc.
    [JsonPropertyName("condition")]
    public string Condition { get; set; } = "normal";

    /// <summary>
    /// True for named, identified-rare, or NPC-gifted items (e.g., Borrek knife).
    /// Used by the good_gear catalog category. Computed at death time.
    /// </summary>
    [JsonPropertyName("is_notable")]
    public bool IsNotable { get; set; }
}
