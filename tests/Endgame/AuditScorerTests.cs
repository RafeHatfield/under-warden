using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Endgame;
using UnderWarden.Logic.Persistence;
using UnderWarden.Logic.Persistence.Namespaces;
using UnderWarden.Tests.Persistence;
using NUnit.Framework;

namespace UnderWarden.Tests.Endgame;

/// <summary>
/// TASK-002: the audit scoring function — the convergence point of plan_end_game.
/// Tier thresholds are strawman (tuned in TASK-011); these tests pin the STRUCTURE
/// (boundaries, tone override, ending branch), not the final balance numbers.
/// </summary>
[TestFixture]
public class AuditScorerTests
{
    // ── Guardian 1: Warden-of-Wardens (possession count + memo tone) ──────────

    [TestCase(0, "polite", GuardianTier.Allied)]
    [TestCase(1, "polite", GuardianTier.Diminished)]
    [TestCase(2, "polite", GuardianTier.Diminished)]
    [TestCase(3, "procedural_notice", GuardianTier.Neutral)]
    [TestCase(5, "procedural_notice", GuardianTier.Neutral)]
    [TestCase(6, "polite", GuardianTier.Savage)]
    public void ScoreWarden_ByCountAndTone(int possessions, string tone, GuardianTier expected)
    {
        Assert.That(AuditScorer.ScoreWarden(possessions, tone), Is.EqualTo(expected));
    }

    [TestCase("formal_complaint")]
    [TestCase("final_audit")]
    public void ScoreWarden_FormalToneIsSavage_RegardlessOfCount(string tone)
    {
        // Even zero possessions: an escalated tone means the institution already filed against you.
        Assert.That(AuditScorer.ScoreWarden(0, tone), Is.EqualTo(GuardianTier.Savage));
    }

    // ── Guardian 2: Oathkeeper (orc rep + unprovoked orc kills) ───────────────

    [Test]
    public void ScoreOathkeeper_AlliedRep_IsAlly()
    {
        Assert.That(AuditScorer.ScoreOathkeeper("allied", 99), Is.EqualTo(GuardianTier.Allied));
    }

    [Test]
    public void ScoreOathkeeper_HostileRep_IsSavage()
    {
        Assert.That(AuditScorer.ScoreOathkeeper("hostile", 0), Is.EqualTo(GuardianTier.Savage));
    }

    [Test]
    public void ScoreOathkeeper_NeutralRep_SplitsOnUnprovokedKills()
    {
        Assert.That(AuditScorer.ScoreOathkeeper("neutral", 0), Is.EqualTo(GuardianTier.Diminished));
        Assert.That(AuditScorer.ScoreOathkeeper("neutral", 1), Is.EqualTo(GuardianTier.Neutral));
    }

    // ── Guardian 3: Assembly of the Lost (cumulative deaths) ──────────────────

    [TestCase(0, GuardianTier.Allied)]
    [TestCase(2, GuardianTier.Allied)]
    [TestCase(3, GuardianTier.Diminished)]
    [TestCase(5, GuardianTier.Diminished)]
    [TestCase(6, GuardianTier.Neutral)]
    [TestCase(10, GuardianTier.Neutral)]
    [TestCase(11, GuardianTier.Savage)]
    [TestCase(50, GuardianTier.Savage)]
    public void ScoreAssembly_ByDeaths(int deaths, GuardianTier expected)
    {
        Assert.That(AuditScorer.ScoreAssembly(deaths), Is.EqualTo(expected));
    }

    // ── Guardian 4: Auditor's Own (excess = unprovoked kills / floors) ────────

    [Test]
    public void ScoreAuditor_RateBoundaries()
    {
        // 25 floors reached: rate = kills / 25.
        Assert.That(AuditScorer.ScoreAuditor(0, 25), Is.EqualTo(GuardianTier.Allied));   // 0.0
        Assert.That(AuditScorer.ScoreAuditor(2, 25), Is.EqualTo(GuardianTier.Allied));   // 0.08
        Assert.That(AuditScorer.ScoreAuditor(5, 25), Is.EqualTo(GuardianTier.Diminished)); // 0.20
        Assert.That(AuditScorer.ScoreAuditor(10, 25), Is.EqualTo(GuardianTier.Neutral));   // 0.40
        Assert.That(AuditScorer.ScoreAuditor(20, 25), Is.EqualTo(GuardianTier.Savage));    // 0.80
    }

    [Test]
    public void ScoreAuditor_GuardsAgainstZeroFloors()
    {
        // Should not divide by zero; treats floors as at least 1.
        Assert.That(AuditScorer.ScoreAuditor(5, 0), Is.EqualTo(GuardianTier.Savage)); // rate 5.0
    }

    // ── Ending determination (decision 6) ─────────────────────────────────────

    private static AuditScorer.AuditResult CleanAudit() => new(
        GuardianTier.Allied, GuardianTier.Allied, GuardianTier.Allied, GuardianTier.Allied);

    private static AuditScorer.AuditResult HeavyAudit() => new(
        GuardianTier.Savage, GuardianTier.Neutral, GuardianTier.Diminished, GuardianTier.Allied);

    [Test]
    public void DetermineEnding_Refused_IsLossRefused()
    {
        Assert.That(
            AuditScorer.DetermineEnding(WeighingOutcome.Refused, CleanAudit(), false, true, "allied", 0),
            Is.EqualTo(EndingType.LossRefused));
    }

    [Test]
    public void DetermineEnding_DiedToGuardians_And_DiedToDebt_AreDistinct()
    {
        Assert.That(
            AuditScorer.DetermineEnding(WeighingOutcome.DiedToGuardians, HeavyAudit(), false, false, "hostile", 12),
            Is.EqualTo(EndingType.LossGuardians));
        Assert.That(
            AuditScorer.DetermineEnding(WeighingOutcome.DiedToDebt, CleanAudit(), false, false, "allied", 0),
            Is.EqualTo(EndingType.LossDebt));
    }

    [Test]
    public void DetermineEnding_InProgress_IsNone()
    {
        Assert.That(
            AuditScorer.DetermineEnding(WeighingOutcome.InProgress, CleanAudit(), false, false, "neutral", 0),
            Is.EqualTo(EndingType.None));
    }

    [Test]
    public void DetermineEnding_Survived_Clean_IsCleanAudit()
    {
        Assert.That(
            AuditScorer.DetermineEnding(WeighingOutcome.Survived, CleanAudit(), false, false, "allied", 1),
            Is.EqualTo(EndingType.CleanAudit));
    }

    [Test]
    public void DetermineEnding_Survived_Heavy_IsTheft()
    {
        // Heavy because a Guardian is Savage.
        Assert.That(
            AuditScorer.DetermineEnding(WeighingOutcome.Survived, HeavyAudit(), false, false, "neutral", 1),
            Is.EqualTo(EndingType.Theft));
    }

    [Test]
    public void DetermineEnding_Survived_HostileRep_IsHeavy_EvenIfNoSavageGuardian()
    {
        Assert.That(
            AuditScorer.DetermineEnding(WeighingOutcome.Survived, CleanAudit(), false, false, "hostile", 0),
            Is.EqualTo(EndingType.Theft));
    }

    [Test]
    public void DetermineEnding_Survived_HighDeaths_IsHeavy()
    {
        Assert.That(
            AuditScorer.DetermineEnding(WeighingOutcome.Survived, CleanAudit(), false, false, "allied", 11),
            Is.EqualTo(EndingType.Theft));
    }

    [Test]
    public void DetermineEnding_Swap_RequiresChosenAndAvailable()
    {
        // Chosen + available → Swap (overrides clean/heavy).
        Assert.That(
            AuditScorer.DetermineEnding(WeighingOutcome.Survived, HeavyAudit(), true, true, "hostile", 12),
            Is.EqualTo(EndingType.Swap));
        // Chosen but not available → falls through to clean/heavy.
        Assert.That(
            AuditScorer.DetermineEnding(WeighingOutcome.Survived, CleanAudit(), true, false, "allied", 0),
            Is.EqualTo(EndingType.CleanAudit));
    }

    // ── AuditResult helpers ───────────────────────────────────────────────────

    [Test]
    public void AuditResult_AnySavage_DetectsAnyAxis()
    {
        Assert.That(CleanAudit().AnySavage, Is.False);
        Assert.That(HeavyAudit().AnySavage, Is.True);
    }

    [Test]
    public void AuditResult_TierFor_DebtThrows()
    {
        Assert.That(() => CleanAudit().TierFor(GuardianId.Debt), Throws.InstanceOf<System.ArgumentOutOfRangeException>());
    }

    // ── Integration: Score over a real PersistentRunState ─────────────────────

    [Test]
    public void Score_OverPersistence_WiresAllFourGuardians()
    {
        var state = PersistentRunState.LoadFromDisk(new FakePersistencePathProvider(
            Path.Combine(Path.GetTempPath(), "yarl_audit_" + System.Guid.NewGuid())));

        // A heavy record across the board. Possession + deaths are cross-run (persistence); the
        // excess axes are THIS DESCENT's, carried on the run tally.
        state.UnderWarden.HallWardenPossessionsTotal = 7;          // Warden → Savage
        state.UnderWarden.LastMemoTone = "polite";
        state.Factions.Factions["orc"].State = "hostile";          // Oathkeeper → Savage (rep)
        state.UnderWarden.CumulativeDeaths = 12;                   // Assembly → Savage

        var tally = new RunAggressionTally();
        tally.UnprovokedKillsByFaction["orc"] = 8;                 // contributes to excess + orc subset
        tally.UnprovokedKillsByFaction["undead"] = 8;             // 16 total / 25 floors = 0.64 → Savage

        var audit = AuditScorer.Score(state, tally, WeighingConstants.FinalFloorDepth);

        Assert.That(audit.WardenOfWardens, Is.EqualTo(GuardianTier.Savage));
        Assert.That(audit.Oathkeeper, Is.EqualTo(GuardianTier.Savage));
        Assert.That(audit.AssemblyOfTheLost, Is.EqualTo(GuardianTier.Savage));
        Assert.That(audit.AuditorsOwn, Is.EqualTo(GuardianTier.Savage));
        Assert.That(audit.AnySavage, Is.True);
    }

    [Test]
    public void Score_FreshState_IsAllAllied()
    {
        var state = PersistentRunState.LoadFromDisk(new FakePersistencePathProvider(
            Path.Combine(Path.GetTempPath(), "yarl_audit_" + System.Guid.NewGuid())));

        // A fresh save with no kills this run (null tally): no possessions, neutral orc rep, no deaths.
        // Neutral orc rep with zero unprovoked kills → Oathkeeper Diminished (not Allied).
        var audit = AuditScorer.Score(state, runTally: null, WeighingConstants.FinalFloorDepth);

        Assert.That(audit.WardenOfWardens, Is.EqualTo(GuardianTier.Allied));
        Assert.That(audit.Oathkeeper, Is.EqualTo(GuardianTier.Diminished));
        Assert.That(audit.AssemblyOfTheLost, Is.EqualTo(GuardianTier.Allied));
        Assert.That(audit.AuditorsOwn, Is.EqualTo(GuardianTier.Allied));
        Assert.That(audit.AnySavage, Is.False);
    }

    [Test]
    public void Score_Excess_ReadsThisDescent_NotLifetimeCumulative()
    {
        // The decision: the Auditor's Own and the Oathkeeper's orc-subset fine scaling judge THIS
        // descent, not a career. A player with a heavy lifetime cumulative who descended CLEANLY this
        // run must read clean — otherwise the metric measures playtime, the flaw we rejected.
        var state = PersistentRunState.LoadFromDisk(new FakePersistencePathProvider(
            Path.Combine(Path.GetTempPath(), "yarl_audit_" + System.Guid.NewGuid())));

        // A butcher's career in the lifetime cumulative...
        state.UnderWarden.AddUnprovokedKill("orc", 100);
        state.UnderWarden.AddUnprovokedKill("undead", 100);

        // ...but this descent was clean (empty run tally).
        var cleanRun = new RunAggressionTally();

        var audit = AuditScorer.Score(state, cleanRun, WeighingConstants.FinalFloorDepth);

        Assert.That(audit.AuditorsOwn, Is.EqualTo(GuardianTier.Allied),
            "a clean descent reads clean on the excess axis regardless of lifetime kills");
        Assert.That(audit.Oathkeeper, Is.EqualTo(GuardianTier.Diminished),
            "neutral rep + no orc blood THIS run → Diminished, not sharpened by the lifetime cumulative");
    }

    [Test]
    public void UnderWardenData_UnprovokedKills_AccumulateByFaction()
    {
        var uw = new UnderWardenData();
        uw.AddUnprovokedKill("orc");
        uw.AddUnprovokedKill("orc", 2);
        uw.AddUnprovokedKill("undead");

        Assert.That(uw.UnprovokedKillsFor("orc"), Is.EqualTo(3));
        Assert.That(uw.UnprovokedKillsFor("undead"), Is.EqualTo(1));
        Assert.That(uw.UnprovokedKillsFor("cultist"), Is.EqualTo(0));
        Assert.That(uw.TotalUnprovokedKills(), Is.EqualTo(4));
    }
}
