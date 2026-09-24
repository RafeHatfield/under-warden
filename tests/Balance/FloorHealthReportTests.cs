using UnderWarden.Logic.Balance;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// READ-LEVEL tests for the role-aware Floor Health report section. Same outcome-testing discipline that
/// protects the classifiers, applied to the formatter: a synthetic floor with a KNOWN health verdict and
/// a KNOWN lever attribution must produce a report that SAYS that verdict and that lever — so the screen
/// the balance pass reads can't silently start mislabeling. Pure data; no engine.
/// </summary>
[TestFixture]
public class FloorHealthReportTests
{
    // B1 targets: death band 5–15%; baseline ttd 9 hits, spike 3; lever expectations all "normal".
    private static TargetTable Targets() => new(new[]
    {
        new TargetRegion("B1", 1, 5, new TargetBand(0.05, 0.15),
            new Dictionary<ThreatArchetype, ArchetypeTarget>
            {
                [ThreatArchetype.Baseline]  = new(9),
                [ThreatArchetype.Spike]     = new(3),
                [ThreatArchetype.Escalator] = new(4),
                [ThreatArchetype.Fused]     = new(3),
            },
            new LeverExpectation(DamagePerHit: 5.0, KillerHitRate: 0.5,
                CounterattacksLanded: 4.0, DistinctAttackers: 2.0, AttackFrequency: 1.0)),
    });

    private static PlayerDeathRecord Death(
        ThreatArchetype arch, int hits = 9, double dmgPerHit = 5.0, double hitRate = 0.5,
        int counters = 4, int attackers = 2, int engagementTurns = 9) => new()
        {
            Depth = 1,
            KillerArchetype = arch,
            HitsToDown = hits,
            DamagePerHit = dmgPerHit,
            KillerHitRate = hitRate,
            CounterattacksLanded = counters,
            DistinctAttackers = attackers,
            EngagementTurns = engagementTurns,
        };

    private static DungeonSoakRunResult Floor1Run(int seed, PlayerDeathRecord? death)
    {
        bool died = death != null;
        var floor = new FloorRunMetrics
        {
            Depth = 1,
            TurnsTaken = 40,
            MonstersKilled = 3,
            PlayerHpAtEnd = died ? 0 : 30,
            PlayerMaxHp = 55,
            PlayerDied = died,
            Descended = !died,
            Death = death,
            SpikePresent = death?.KillerArchetype is ThreatArchetype.Spike or ThreatArchetype.Fused,
            EscalatorPresent = death?.KillerArchetype is ThreatArchetype.Escalator or ThreatArchetype.Fused,
        };
        return new DungeonSoakRunResult
        {
            Seed = seed,
            Outcome = died ? OutcomeClassifier.Died : OutcomeClassifier.Survived,
            FailureType = died ? OutcomeClassifier.FailureDeath : OutcomeClassifier.FailureNone,
            FailureDetail = died ? (death!.KillerTypeId ?? "monster") : "",
            DeepestFloorReached = 1,
            FloorsCompleted = died ? 0 : 1,
            TotalTurns = 40,
            TotalKills = 3,
            FinalHp = died ? 0 : 30,
            FinalMaxHp = 55,
            FinalHpFraction = died ? 0.0 : 30.0 / 55.0,
            PerFloor = new[] { floor },
        };
    }

    /// <summary>Build a depth-1 soak: <paramref name="deaths"/> dying runs + survivors to total <paramref name="runs"/>.</summary>
    private static string Render(int runs, IReadOnlyList<PlayerDeathRecord> deaths)
    {
        var list = new List<DungeonSoakRunResult>();
        for (int i = 0; i < deaths.Count; i++) list.Add(Floor1Run(seed: i, death: deaths[i]));
        for (int i = deaths.Count; i < runs; i++) list.Add(Floor1Run(seed: i, death: null));
        var summary = DungeonSoakSummary.ComputeFrom(list, configuredFloors: 1);
        return DungeonSoakReport.Generate(summary, Targets());
    }

    [Test]
    public void HealthyFloor_SaysHealthy_AndShowsNoLeverLine()
    {
        // 1/10 deaths = 10% (in band 5–15%); the death is a SLOW baseline (attrition) → Healthy.
        var report = Render(runs: 10, deaths: new[] { Death(ThreatArchetype.Baseline, hits: 9) });
        Assert.That(report, Does.Contain("Floor Health (role-aware):"));
        Assert.That(report, Does.Contain("Healthy"));
        Assert.That(report, Does.Not.Contain("levers:"), "a healthy floor must not print a lever attribution");
    }

    [Test]
    public void TooHardFloor_NormalDamage_HighHitRate_SaysTooHard_AndArmor()
    {
        // 4/10 = 40% (above band). Slow baseline deaths (not fast → not BaselineBroken), normal per-hit
        // damage but high hit-rate → the report must read TooHard and attribute to Armor, not MonsterDamage.
        var deaths = Enumerable.Range(0, 4)
            .Select(_ => Death(ThreatArchetype.Baseline, hits: 9, dmgPerHit: 5.0, hitRate: 0.9))
            .ToList();
        var report = Render(runs: 10, deaths: deaths);
        Assert.That(report, Does.Contain("TooHard"));
        Assert.That(report, Does.Contain("Armor"));
        Assert.That(report, Does.Not.Contain("MonsterDamage"),
            "normal per-hit damage must NOT implicate the monster-damage lever");
    }

    [Test]
    public void TooHardFloor_NormalDamage_HighFrequency_SaysAttackFrequency_NotMonsterDamage()
    {
        // The headline proof at the report level: a wraith-shaped death (normal damage, two blows/turn)
        // attributes to AttackFrequency, not MonsterDamage — one-signal-per-lever holds end to end.
        var deaths = Enumerable.Range(0, 4)
            .Select(_ => Death(ThreatArchetype.Spike, hits: 4, dmgPerHit: 5.0, engagementTurns: 2)) // freq 2.0
            .ToList();
        var report = Render(runs: 10, deaths: deaths);
        Assert.That(report, Does.Contain("TooHard"));
        Assert.That(report, Does.Contain("AttackFrequency"));
        Assert.That(report, Does.Not.Contain("MonsterDamage"));
    }

    [Test]
    public void FastBaselineDeaths_SayBaselineBroken_EvenInBand()
    {
        // 1/10 = 10% (IN band) but the death is a FAST baseline (2 hits ≪ 9) → secretly lethal.
        // Verdict must read BaselineBroken, and a lever line must accompany it.
        var report = Render(runs: 10, deaths: new[]
        {
            Death(ThreatArchetype.Baseline, hits: 2, dmgPerHit: 12.0), // fast + hits hard → MonsterDamage
        });
        Assert.That(report, Does.Contain("BaselineBroken"));
        Assert.That(report, Does.Contain("MonsterDamage"));
    }

    [Test]
    public void BackCompat_NoTargets_OmitsFloorHealthSection()
    {
        var summary = DungeonSoakSummary.ComputeFrom(new List<DungeonSoakRunResult> { Floor1Run(0, null) }, 1);
        var report = DungeonSoakReport.Generate(summary); // no targets
        Assert.That(report, Does.Not.Contain("Floor Health (role-aware):"));
        Assert.That(report, Does.Contain("Floor Efficiency:"), "the plain vitals section still renders");
    }
}
