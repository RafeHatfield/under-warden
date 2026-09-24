using UnderWarden.Logic.Persistence;
using UnderWarden.Logic.Persistence.Namespaces;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Evaluates post-run and mid-run events to decide which Under-Warden memos should be
/// queued for the player to read in the inbox.
///
/// Called from two entry points:
///   - EvaluateRunEnd: after each game run ends (death or survival)
///   - EvaluateHallWardenPossession: when a Hall Warden possession ends
///
/// Both methods append formatted PendingMemo entries to state.UnderWarden.PendingMemos
/// and call state.MarkDirty() if any memos were added.
///
/// Fire semantics:
///   - body[0] fires on the first fire of a key (fireIndex=0)
///   - body[1], body[2], ... fire on subsequent fires (fireIndex=1, 2, ...)
///   - If fireIndex exceeds the body list, MemoFormatter clamps to the last variant
///   - Single-shot memos (only body[0]) never re-fire — caller guards via HasLoggedGrievance
///
/// Fire order within EvaluateRunEnd is deterministic:
///   death_first → floor_low → cause_trap → cause_acid → cause_possession_neglect
///   → death_repeat → audit_warning → run_clean
/// </summary>
public sealed class MemoDeliveryEvaluator
{
    // Engine cause strings that map to the "trap" incident type.
    private static readonly HashSet<string> TrapCauses = new(StringComparer.OrdinalIgnoreCase)
    {
        "spike_trap", "dart_trap", "flame_trap", "own_trap",
    };

    // Engine cause strings that map to the "acid" incident type.
    private static readonly HashSet<string> AcidCauses = new(StringComparer.OrdinalIgnoreCase)
    {
        "acid_pool", "acid_spray",
    };

    // Engine cause strings that map to the possession-neglect incident type.
    // Fires when the home vessel is killed while the player inhabits a host body.
    private static readonly HashSet<string> PossessionNeglectCauses = new(StringComparer.OrdinalIgnoreCase)
    {
        "possession_neglect",
    };

    private readonly MemoFormatter _formatter = new();

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluate which memos should fire after a game run ends. Adds formatted memos
    /// to the pending queue and marks state dirty if any were added.
    ///
    /// Handles both death runs and survival runs:
    ///   - Death: increments CumulativeDeaths, evaluates cause-specific and pattern memos
    ///   - Survival: evaluates run_clean (requires two consecutive clean runs)
    ///
    /// Always updates LastRunWasClean to reflect this run's outcome.
    /// </summary>
    public void EvaluateRunEnd(PostRunContext ctx, PersistentRunState state, MemoRegistry registry)
    {
        var addedAny = false;

        if (ctx.Died)
        {
            // Increment the cumulative death counter before evaluating thresholds so
            // that death_repeat and audit_warning see the updated count.
            state.UnderWarden.CumulativeDeaths++;

            // Track whether a cause-specific memo fired this run. death_repeat is suppressed
            // when a cause-specific memo fires on the same run — the specific cause is more
            // characterful than the general pattern notice.
            var causeSpecificFired = false;

            // ── polite.death_first ─────────────────────────────────────────────
            // Single-shot. Only fires on the first death ever.
            if (!state.UnderWarden.HasLoggedGrievance("polite.death_first"))
            {
                AddMemo("polite.death_first", ctx, state, registry, ctx.RunNumber);
                addedAny = true;
            }

            // ── polite.floor_low ───────────────────────────────────────────────
            // Multi-fire. Fires whenever a death happens on floor 3 or below.
            if (ctx.FloorReached <= 3)
            {
                AddMemo("polite.floor_low", ctx, state, registry, ctx.RunNumber);
                addedAny = true;
            }

            // ── polite.cause_trap ──────────────────────────────────────────────
            // Multi-fire. Fires whenever death cause is a trap type.
            if (ctx.CauseOfDeath != null && TrapCauses.Contains(ctx.CauseOfDeath))
            {
                AddMemo("polite.cause_trap", ctx, state, registry, ctx.RunNumber);
                addedAny = true;
                causeSpecificFired = true;
            }

            // ── polite.cause_acid ──────────────────────────────────────────────
            // Multi-fire. Fires whenever death cause is an acid type.
            if (ctx.CauseOfDeath != null && AcidCauses.Contains(ctx.CauseOfDeath))
            {
                AddMemo("polite.cause_acid", ctx, state, registry, ctx.RunNumber);
                addedAny = true;
                causeSpecificFired = true;
            }

            // ── procedural_notice.cause_possession_neglect ─────────────────────
            // Multi-fire. Fires when the home vessel is killed during active possession.
            if (ctx.CauseOfDeath != null && PossessionNeglectCauses.Contains(ctx.CauseOfDeath))
            {
                AddMemo("procedural_notice.cause_possession_neglect", ctx, state, registry, ctx.RunNumber);
                addedAny = true;
                causeSpecificFired = true;
            }

            // ── procedural_notice.death_repeat ─────────────────────────────────
            // Multi-fire, but suppressed when a cause-specific memo fires on the same run.
            // Fires on the 3rd death and every subsequent death without a specific cause.
            // CumulativeDeaths was already incremented above, so >= 3 means "third or later".
            if (state.UnderWarden.CumulativeDeaths >= 3 && !causeSpecificFired)
            {
                AddMemo("procedural_notice.death_repeat", ctx, state, registry, ctx.RunNumber);
                addedAny = true;
            }

            // ── procedural_notice.audit_warning ────────────────────────────────
            // Single-shot. Fires once when cumulative deaths cross the audit threshold.
            // Threshold is tunable here; memo text is threshold-agnostic.
            if (state.UnderWarden.CumulativeDeaths >= 10
                && !state.UnderWarden.HasLoggedGrievance("procedural_notice.audit_warning"))
            {
                AddMemo("procedural_notice.audit_warning", ctx, state, registry, ctx.RunNumber);
                addedAny = true;
            }
        }
        else
        {
            // ── procedural_notice.run_clean ────────────────────────────────────
            // Fires only when BOTH the current run and the previous run were clean.
            // Single clean runs do not trigger it; the memo text's "two consecutive
            // descents" framing depends on the two-in-a-row condition.
            if (state.UnderWarden.LastRunWasClean)
            {
                AddMemo("procedural_notice.run_clean", ctx, state, registry, ctx.RunNumber);
                addedAny = true;
            }
        }

        // Update the clean-run flag for the next run to consult.
        // Must happen after the run_clean check above.
        state.UnderWarden.LastRunWasClean = !ctx.Died;

        if (addedAny)
            state.MarkDirty();
    }

    /// <summary>
    /// Called the first time a past-self is freed via the Possessed-Corpse spell-break encounter.
    /// Queues the formal_complaint.catalog_referenced memo with the rendered catalog entry quoted
    /// verbatim inside the memo body. Single-shot — never re-fires.
    ///
    /// <paramref name="catalogEntry"/> is the pre-rendered Hollowmark catalog line for the freed
    /// past-self (from CatalogEntryRenderer). If null, the {catalog_entry} slot is left unfilled.
    /// </summary>
    public void EvaluateCatalogReferenced(
        PersistentRunState state, MemoRegistry registry, int runNumber, string? catalogEntry)
    {
        if (state.UnderWarden.HasLoggedGrievance("formal_complaint.catalog_referenced")) return;

        // No authoritative EndingType source in scope: this is a synthetic mid-run context for a
        // catalog-referenced possession event, not an end-of-run flush. Only PersistentRunState is
        // available here, not the live GameState that carries Ending. None is honest, not a guess.
        var ctx = new PostRunContext(Died: false, CauseOfDeath: null, KillerSpecies: null, FloorReached: 0, RunNumber: runNumber, Ending: Endgame.EndingType.None);
        var extraSlots = catalogEntry != null
            ? new Dictionary<string, string> { ["catalog_entry"] = catalogEntry }
            : null;
        AddMemo("formal_complaint.catalog_referenced", ctx, state, registry, runNumber, extraSlots);
        state.MarkDirty();
    }

    /// <summary>
    /// Called when a Hall Warden possession ends. Increments the possession counter and
    /// queues the appropriate memo at each threshold (1, 3, 6+).
    ///
    /// The formal_complaint fires once only (guarded by ProceduralGrievancesLogged).
    /// Always marks state dirty since the counter always increments.
    /// </summary>
    public void EvaluateHallWardenPossession(PersistentRunState state, MemoRegistry registry, int runNumber)
    {
        state.UnderWarden.HallWardenPossessionsTotal++;
        var total = state.UnderWarden.HallWardenPossessionsTotal;

        // Use a null context — possession memos don't have per-run death data.
        // Pass a minimal ctx with only the run number relevant field.
        // No authoritative EndingType source in scope here either (same reasoning as
        // EvaluateCatalogReferenced above): only PersistentRunState is available, not the live
        // GameState that carries Ending. None is honest, not a guess.
        var ctx = new PostRunContext(
            Died: false,
            CauseOfDeath: null,
            KillerSpecies: null,
            FloorReached: 0,
            RunNumber: runNumber,
            Ending: Endgame.EndingType.None
        );

        if (total == 1)
        {
            AddMemo("polite.hall_warden_possession", ctx, state, registry, runNumber);
        }
        else if (total == 3)
        {
            AddMemo("procedural_notice.hall_warden_possession", ctx, state, registry, runNumber);
        }
        else if (total >= 6 && !state.UnderWarden.HasLoggedGrievance("formal_complaint.hall_warden_possession"))
        {
            AddMemo("formal_complaint.hall_warden_possession", ctx, state, registry, runNumber);
        }

        state.MarkDirty();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Look up the memo definition, determine the correct fire index (from GrievanceFireCounts),
    /// format the memo, and append it to the pending queue. Also calls RecordMemoSent to
    /// track the grievance key, increment TotalMemosSentEver, and update GrievanceFireCounts.
    ///
    /// If the key is unknown in the registry, the call is a no-op.
    /// </summary>
    private void AddMemo(
        string memoKey,
        PostRunContext ctx,
        PersistentRunState state,
        MemoRegistry registry,
        int runNumber,
        Dictionary<string, string>? extraSlots = null)
    {
        var memo = registry.GetMemo(memoKey);
        if (memo is null)
            return;

        // Use GrievanceFireCounts for the fire index so MemoFormatter selects the correct
        // body variant. GetFireCount returns 0 before first fire, 1 on second, etc.
        var fireIndex = state.UnderWarden.GetFireCount(memoKey);

        var slots = BuildSlots(ctx, state);
        if (extraSlots != null)
            foreach (var (k, v) in extraSlots)
                slots[k] = v;

        var (subject, body) = _formatter.Format(memo, fireIndex, slots, registry);

        state.UnderWarden.PendingMemos.Add(new PendingMemo(
            Key: memoKey,
            Subject: subject,
            Body: body,
            DeliveredRun: runNumber
        ));

        // RecordMemoSent increments TotalMemosSentEver, dedup-adds to ProceduralGrievancesLogged,
        // and increments GrievanceFireCounts[memoKey] for the variant index on the next fire.
        state.UnderWarden.RecordMemoSent(newGrievanceId: memoKey);
    }

    /// <summary>
    /// Build the slot dictionary from the run context and persistent state.
    /// All slots are strings; unknown slots are left as-is by MemoFormatter.
    /// </summary>
    private static Dictionary<string, string> BuildSlots(PostRunContext ctx, PersistentRunState state)
    {
        return new Dictionary<string, string>
        {
            ["run_number"]    = ctx.RunNumber.ToString(),
            ["floor"]         = ctx.FloorReached.ToString(),
            ["cause_of_death"] = ctx.CauseOfDeath ?? "unknown",
            ["killer_species"] = ctx.KillerSpecies ?? "",
            ["run_count"]     = state.RunCounter.TotalRuns.ToString(),
            ["memo_count"]    = state.UnderWarden.TotalMemosSentEver.ToString(),
            ["floor_best"]    = state.RunCounter.BestFloorReached.ToString(),
            ["offense_summary"] = "", // deferred — empty string for now
        };
    }
}
