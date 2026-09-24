using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Persistence;
using UnderWarden.Logic.Persistence.Namespaces;

namespace UnderWarden.Logic.Endgame;

/// <summary>
/// The audit. Deterministic scoring of Sasha's whole record into per-Guardian dispositions
/// and a final ending. This is the convergence point of plan_end_game: possession, memo tone,
/// faction reputation, deaths/past-Sashas, and the excess metric all resolve here.
///
/// Inputs: cross-run persistence fields (possession count, memo tone, orc rep, deaths) for the
/// axes the institution remembers across the whole save, plus THIS DESCENT's unprovoked-kill tally
/// for the excess axes (decision 2 / per-run, 2026-06-06). Thresholds are STRAWMAN per decisions
/// 5/6 — the structure is final; the numbers move in the harness balance pass (TASK-011). All
/// scoring is pure and deterministic (no RNG, no time).
/// </summary>
public static class AuditScorer
{
    /// <summary>The four faction-Guardian tiers for a run. The Debt has no tier.</summary>
    public readonly record struct AuditResult(
        GuardianTier WardenOfWardens,
        GuardianTier Oathkeeper,
        GuardianTier AssemblyOfTheLost,
        GuardianTier AuditorsOwn)
    {
        /// <summary>True if any faction Guardian came up Savage — the primary "heavy record" signal.</summary>
        public bool AnySavage =>
            WardenOfWardens == GuardianTier.Savage
            || Oathkeeper == GuardianTier.Savage
            || AssemblyOfTheLost == GuardianTier.Savage
            || AuditorsOwn == GuardianTier.Savage;

        /// <summary>Tier for a given faction Guardian. Throws for <see cref="GuardianId.Debt"/> (unscaled).</summary>
        public GuardianTier TierFor(GuardianId id) => id switch
        {
            GuardianId.WardenOfWardens => WardenOfWardens,
            GuardianId.Oathkeeper => Oathkeeper,
            GuardianId.AssemblyOfTheLost => AssemblyOfTheLost,
            GuardianId.AuditorsOwn => AuditorsOwn,
            _ => throw new System.ArgumentOutOfRangeException(nameof(id), "The Debt does not scale and has no tier."),
        };
    }

    // --- Strawman thresholds (tunable in TASK-011) ---
    private const int AssemblyAlliedMaxDeaths = 2;     // 0-2 deaths
    private const int AssemblyDiminishedMaxDeaths = 5; // 3-5
    private const int AssemblyNeutralMaxDeaths = 10;   // 6-10; 11+ Savage
    private const int WardenDiminishedMaxPossessions = 2; // 1-2
    private const int WardenNeutralMaxPossessions = 5;    // 3-5; 6+ Savage
    private const double AuditorAlliedMaxRate = 0.10;     // excess kills per floor reached
    private const double AuditorDiminishedMaxRate = 0.30;
    private const double AuditorNeutralMaxRate = 0.60;    // >= 0.60 Savage

    /// <summary>
    /// Guardian 1 — Warden-of-Wardens. Possession count + memo tone.
    /// A formal_complaint / final_audit tone is itself savage regardless of count.
    /// </summary>
    public static GuardianTier ScoreWarden(int hallWardenPossessions, string memoTone)
    {
        if (memoTone is "formal_complaint" or "final_audit")
            return GuardianTier.Savage;
        if (hallWardenPossessions <= 0) return GuardianTier.Allied;
        if (hallWardenPossessions <= WardenDiminishedMaxPossessions) return GuardianTier.Diminished;
        if (hallWardenPossessions <= WardenNeutralMaxPossessions) return GuardianTier.Neutral;
        return GuardianTier.Savage;
    }

    /// <summary>
    /// Guardian 2 — Oathkeeper. Orc reputation is the coarse lever; unprovoked orc kills sharpen
    /// the middle. Allied rep → ally; Hostile rep → savage; Neutral splits on whether blood was spilled.
    /// <paramref name="unprovokedOrcKills"/> is THIS DESCENT's count (per-run), not lifetime — the
    /// audit weighs who Sasha was on the run that reached the bottom (see <see cref="Score"/>).
    /// </summary>
    public static GuardianTier ScoreOathkeeper(string orcRepState, int unprovokedOrcKills)
    {
        switch (orcRepState)
        {
            case "allied": return GuardianTier.Allied;
            case "hostile": return GuardianTier.Savage;
            default: // neutral
                return unprovokedOrcKills <= 0 ? GuardianTier.Diminished : GuardianTier.Neutral;
        }
    }

    /// <summary>
    /// Guardian 3 — Assembly of the Lost. Cumulative deaths (the past-Sasha catalog tracks the same
    /// qualifying deaths; deaths is the primary lever).
    /// </summary>
    public static GuardianTier ScoreAssembly(int cumulativeDeaths)
    {
        if (cumulativeDeaths <= AssemblyAlliedMaxDeaths) return GuardianTier.Allied;
        if (cumulativeDeaths <= AssemblyDiminishedMaxDeaths) return GuardianTier.Diminished;
        if (cumulativeDeaths <= AssemblyNeutralMaxDeaths) return GuardianTier.Neutral;
        return GuardianTier.Savage;
    }

    /// <summary>
    /// Guardian 4 — Auditor's Own. The excess metric: unprovoked cross-faction kills normalized by
    /// floors reached (rate, not raw count — descending deeper does not by itself count as cruelty).
    /// Both inputs are THIS DESCENT's: per-run kills over this run's floors. A careful descent reads
    /// clean no matter how many prior runs were played — the metric measures cruelty, not playtime.
    /// </summary>
    public static GuardianTier ScoreAuditor(int totalUnprovokedKills, int floorsReached)
    {
        double rate = totalUnprovokedKills / (double)System.Math.Max(1, floorsReached);
        if (rate < AuditorAlliedMaxRate) return GuardianTier.Allied;
        if (rate < AuditorDiminishedMaxRate) return GuardianTier.Diminished;
        if (rate < AuditorNeutralMaxRate) return GuardianTier.Neutral;
        return GuardianTier.Savage;
    }

    /// <summary>
    /// Score the full audit from persistence + this run's aggression tally.
    ///
    /// The excess axes (Auditor's Own, all factions; and the Oathkeeper's orc-subset fine scaling)
    /// read THIS DESCENT's unprovoked kills from <paramref name="runTally"/> — the run-scoped tally
    /// live on the player at the Weighing, before its run-end flush into the lifetime cumulative.
    /// This is deliberate: the audit weighs who Sasha was on the descent that reached the bottom, not
    /// his career. A clean descent reads clean regardless of how many prior runs accrued kills.
    ///
    /// The possession (Warden) and death (Assembly) axes remain cross-run by design — the institution
    /// remembers every possession and every death across the whole save.
    ///
    /// <paramref name="floorsReached"/> is this run's floors descended (≈ <see
    /// cref="WeighingConstants.FinalFloorDepth"/> at the Weighing). <paramref name="runTally"/> may be
    /// null (treated as zero kills).
    /// </summary>
    public static AuditResult Score(PersistentRunState persistent, RunAggressionTally? runTally, int floorsReached)
    {
        UnderWardenData uw = persistent.UnderWarden;
        string orcRep = persistent.Factions.GetState(FactionsData.OrcFactionId);

        int orcUnprovokedThisRun = runTally?.UnprovokedKillsFor(FactionsData.OrcFactionId) ?? 0;
        int totalUnprovokedThisRun = runTally?.Total() ?? 0;

        return new AuditResult(
            WardenOfWardens: ScoreWarden(uw.HallWardenPossessionsTotal, uw.LastMemoTone),
            Oathkeeper: ScoreOathkeeper(orcRep, orcUnprovokedThisRun),
            AssemblyOfTheLost: ScoreAssembly(uw.CumulativeDeaths),
            AuditorsOwn: ScoreAuditor(totalUnprovokedThisRun, floorsReached));
    }

    /// <summary>
    /// The ending branch (decision 6). Refusal and the two death states resolve directly from the
    /// outcome; survival splits into Swap (chosen, gated on the Hael catalog), Theft (heavy record),
    /// or Clean Audit. "Heavy" strawman: any Savage Guardian, or orc rep Hostile, or high deaths.
    /// </summary>
    public static EndingType DetermineEnding(
        WeighingOutcome outcome,
        AuditResult audit,
        bool swapChosen,
        bool swapAvailable,
        string orcRepState,
        int cumulativeDeaths)
    {
        switch (outcome)
        {
            case WeighingOutcome.Refused:
                return EndingType.LossRefused;
            case WeighingOutcome.DiedToGuardians:
                return EndingType.LossGuardians;
            case WeighingOutcome.DiedToDebt:
                return EndingType.LossDebt;
            case WeighingOutcome.InProgress:
                return EndingType.None;
            case WeighingOutcome.Survived:
                // Swap is the hidden terminus — only when the catalog gate is open and the player chose it.
                if (swapChosen && swapAvailable) return EndingType.Swap;
                return IsHeavyRecord(audit, orcRepState, cumulativeDeaths)
                    ? EndingType.Theft : EndingType.CleanAudit;
            default:
                return EndingType.None;
        }
    }

    /// <summary>
    /// True when the record is heavy enough that the claim cannot be auto-satisfied.
    /// "Heavy" = any faction Guardian came up Savage, orc rep Hostile, or death count very high.
    /// Used by the orchestrator to decide whether the Debt threshold auto-resolves (clean + no Swap)
    /// or presents a choice gate.
    /// </summary>
    public static bool IsHeavyRecord(AuditResult audit, string orcRepState, int cumulativeDeaths)
        => audit.AnySavage
           || orcRepState == "hostile"
           || cumulativeDeaths > AssemblyNeutralMaxDeaths;
}
