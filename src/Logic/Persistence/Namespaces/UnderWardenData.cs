using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.Namespaces;

/// <summary>
/// A formatted memo queued for display in the inbox UI after a run ends.
/// Produced by MemoDeliveryEvaluator (Phase 3) and consumed by MemoInboxPanel (Phase 4).
/// DeliveredRun is the run number on which this memo was generated.
/// </summary>
public sealed record PendingMemo(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("delivered_run")] int DeliveredRun
);

public sealed class UnderWardenData
{
    // Monotonic counter. INTENTIONALLY not derivable from procedural_grievances_logged.
    // Content YAML uses this directly for tone-progression rules ("after 8 memos ever,
    // escalate regardless of grievance state"). Future memo variants fire without logging
    // a new grievance. Treat as load-bearing primary state — do not remove during refactors.
    [JsonPropertyName("total_memos_sent_ever")]
    public int TotalMemosSentEver { get; set; }

    // "polite" | "procedural_notice" | "formal_complaint" | "final_audit"
    // Monotonic forward — never regresses. See spec §6.15 tone-progression rule.
    [JsonPropertyName("last_memo_tone")]
    public string LastMemoTone { get; set; } = "polite";

    // Each grievance ID fires once ever. Subsequent runs skip already-logged grievances.
    [JsonPropertyName("procedural_grievances_logged")]
    public List<string> ProceduralGrievancesLogged { get; set; } = new();

    [JsonPropertyName("audit_attempted_runs")]
    public int AuditAttemptedRuns { get; set; }

    // Sticky once true — Clean Audit ending. Drives different memo content forever after.
    [JsonPropertyName("audit_completed")]
    public bool AuditCompleted { get; set; }

    // Times the player declined the Weighing (weighing_loss_refused). A refusal is NOT a death —
    // it records no past-Sasha corpse. Instead the Under-Warden files it: future audit/memo content
    // can reference prior refusals ("the visiting party has declined before; the case remains open").
    [JsonPropertyName("weighing_refusals")]
    public int WeighingRefusals { get; set; }

    // Monotonic counter. Incremented on PossessionExitedEvent where host is hall_warden.
    // Thresholds: 1 → polite.hall_warden_possession, 3 → procedural_notice, 6+ → formal_complaint.
    [JsonPropertyName("hall_warden_possessions_total")]
    public int HallWardenPossessionsTotal { get; set; }

    // Total deaths across all runs. Used for death_repeat and audit_warning thresholds.
    // Incremented by MemoDeliveryEvaluator on every death run.
    [JsonPropertyName("cumulative_deaths")]
    public int CumulativeDeaths { get; set; }

    // True if the most recently completed run had no death (survived to exit/win).
    // Used to detect two consecutive clean runs for the run_clean memo.
    [JsonPropertyName("last_run_was_clean")]
    public bool LastRunWasClean { get; set; }

    // Tracks how many times each memo key has fired (first fire and repeats).
    // Used by MemoFormatter to select the correct body variant index.
    [JsonPropertyName("grievance_fire_counts")]
    public Dictionary<string, int> GrievanceFireCounts { get; set; } = new();

    // Queue of formatted memos waiting to surface in the inbox UI.
    // PendingMemos are added after each run by MemoDeliveryEvaluator (Phase 3).
    // Consumed by MemoInboxPanel (Phase 4).
    [JsonPropertyName("pending_memos")]
    public List<PendingMemo> PendingMemos { get; set; } = new();

    // Lifetime unprovoked cross-faction kills across all runs, keyed by victim faction. Still
    // written (flushed at run end — plan_end_game_impl TASK-003), but INTENTIONALLY no longer read.
    //
    // The Weighing audit now reads THIS DESCENT's kills from RunAggressionTally, not this lifetime
    // cumulative (per-run decision, 2026-06-06): the audit weighs who Sasha was on the descent that
    // reached the bottom, not his career. See AuditScorer.Score and tech_debt_2026_06_06 DEBT-014.
    //
    // KEPT (not dead) as a deliberate seam: a future Under-Warden career-cruelty memo/audit line —
    // the bureaucrat noting the whole record even on a clean run ("across all descents, ended a
    // great many lives that posed no threat"). Same lifetime-record pattern as WeighingRefusals.
    // Remove only in a future persistence-cleanup pass IF we commit to never writing that content.
    [JsonPropertyName("cumulative_unprovoked_kills")]
    public Dictionary<string, int> CumulativeUnprovokedKills { get; set; } = new();

    /// <summary>Total unprovoked cross-faction kills across all runs (sum over factions).</summary>
    public int TotalUnprovokedKills()
    {
        int sum = 0;
        foreach (var v in CumulativeUnprovokedKills.Values) sum += v;
        return sum;
    }

    /// <summary>Unprovoked kills against a specific victim faction (0 if none).</summary>
    public int UnprovokedKillsFor(string factionId) =>
        CumulativeUnprovokedKills.TryGetValue(factionId, out var c) ? c : 0;

    /// <summary>Record an unprovoked cross-faction kill against the given victim faction.</summary>
    public void AddUnprovokedKill(string factionId, int count = 1)
    {
        CumulativeUnprovokedKills[factionId] =
            (CumulativeUnprovokedKills.TryGetValue(factionId, out var prev) ? prev : 0) + count;
    }

    public bool HasLoggedGrievance(string grievanceId) =>
        ProceduralGrievancesLogged.Contains(grievanceId);

    /// <summary>
    /// Returns how many times the given memo key has fired (0 if never).
    /// Used by MemoDeliveryEvaluator to pass the correct fireIndex to MemoFormatter.
    /// </summary>
    public int GetFireCount(string key) =>
        GrievanceFireCounts.TryGetValue(key, out var c) ? c : 0;

    public void RecordMemoSent(string? newTone = null, string? newGrievanceId = null)
    {
        TotalMemosSentEver++;
        if (newTone != null) LastMemoTone = newTone;
        if (newGrievanceId != null)
        {
            // Track in single-fire dedup list (first fire only)
            if (!HasLoggedGrievance(newGrievanceId))
                ProceduralGrievancesLogged.Add(newGrievanceId);

            // Track fire count for every fire (first and repeat), so MemoFormatter
            // can select the correct body variant on each call.
            GrievanceFireCounts[newGrievanceId] =
                (GrievanceFireCounts.TryGetValue(newGrievanceId, out var prev) ? prev : 0) + 1;
        }
    }
}
