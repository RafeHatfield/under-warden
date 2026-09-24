using UnderWarden.Logic.Balance;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// OUTCOME-LEVEL tests for FloorHealthClassifier — the operational definition of "balanced".
///
/// Per the hard requirement (docs/balance/threat_archetypes.md, tasks/0c_balance_report.md): the
/// report is the instrument of truth, so its verdicts must be tested at the OUTCOME level — a
/// known-broken composition must classify as broken, never merely that inputs were attached.
/// These tests ARE the reviewable spec; read the verdicts here to review what "balanced" means.
///
/// Pure data; no engine, no Godot.
/// </summary>
[TestFixture]
[Description("Role-aware floor health classification (the operational definition of balanced)")]
public class FloorHealthClassifierTests
{
    private static readonly ClassifierConfig Cfg = new();

    // Default floor: death% target band 5–15%; baseline hits-to-down HIGH (9), spike LOW (3).
    // Units are HITS (the killer's landed blows the player absorbs), not turns — see ArchetypeTarget.
    private static FloorTarget Target(double min = 0.05, double max = 0.15,
        double baselineHits = 9, double spikeHits = 3, double escalatorHits = 4, double fusedHits = 3)
        => new(new TargetBand(min, max), new Dictionary<ThreatArchetype, ArchetypeTarget>
        {
            [ThreatArchetype.Baseline]  = new(baselineHits),
            [ThreatArchetype.Spike]     = new(spikeHits),
            [ThreatArchetype.Escalator] = new(escalatorHits),
            [ThreatArchetype.Fused]     = new(fusedHits),
        });

    private static List<DeathRecord> Deaths(params DeathRecord[] d) => d.ToList();
    // hits = the killer's landed blows the player absorbed before going down (HitsToDown).
    private static DeathRecord Death(ThreatArchetype a, double hits) => new(a, hits);

    // ── Refinement 1: "fast" is relative to the KILLER's archetype hits-to-down ──────────

    [Test]
    public void FastDeathToBaseline_IsBaselineBroken()
    {
        // Baseline target 9 hits → fast = <3 hits. Going down in 2 hits to a baseline = secretly lethal.
        var o = new FloorObserved(
            DeathPct: 0.20,
            Deaths: Deaths(Death(ThreatArchetype.Baseline, 2), Death(ThreatArchetype.Baseline, 2)),
            HasSpike: false, HasEscalator: false, EscalatorReachable: false, Escalator: null);

        Assert.That(FloorHealthClassifier.Classify(o, Target(), Cfg),
            Is.EqualTo(FloorHealth.BaselineBroken));
    }

    [Test]
    public void FastDeathToSpike_IsHealthy_NotBroken()
    {
        // The spike doing its job. A fast death to a spike must NOT read as broken. (spike-OK)
        var o = new FloorObserved(
            DeathPct: 0.10, // in band
            Deaths: Deaths(Death(ThreatArchetype.Spike, 1), Death(ThreatArchetype.Spike, 2)),
            HasSpike: true, HasEscalator: false, EscalatorReachable: false, Escalator: null);

        Assert.That(FloorHealthClassifier.Classify(o, Target(), Cfg),
            Is.EqualTo(FloorHealth.Healthy));
    }

    [Test]
    public void SlowDeathToBaseline_IsHealthy()
    {
        // Died grinding (10 hits vs target 9) — attrition, the baseline working. (baseline-OK)
        var o = new FloorObserved(
            DeathPct: 0.10,
            Deaths: Deaths(Death(ThreatArchetype.Baseline, 10), Death(ThreatArchetype.Baseline, 11)),
            HasSpike: false, HasEscalator: false, EscalatorReachable: false, Escalator: null);

        Assert.That(FloorHealthClassifier.Classify(o, Target(), Cfg),
            Is.EqualTo(FloorHealth.Healthy));
    }

    // ── Spike pushover ──────────────────────────────────────────────────────────────────

    [Test]
    public void SpikePresentButNoDeaths_AndTooEasy_IsSpikeBroken()
    {
        // A spike on the floor, death% well below band, and it killed no one → pushover.
        var o = new FloorObserved(
            DeathPct: 0.01,
            Deaths: Deaths(), // nobody died
            HasSpike: true, HasEscalator: false, EscalatorReachable: false, Escalator: null);

        Assert.That(FloorHealthClassifier.Classify(o, Target(), Cfg),
            Is.EqualTo(FloorHealth.SpikeBroken));
    }

    // ── Refinement 2: the three escalator signals, each a distinct failure ────────────────

    [Test]
    public void EscalatorIgnorableWithNoConsequence_IsEscalatorBroken()
    {
        // Leaving it alive isn't even hard (alive death% in band) → it isn't escalating.
        var o = new FloorObserved(
            DeathPct: 0.10,
            Deaths: Deaths(Death(ThreatArchetype.Escalator, 5)),
            HasSpike: false, HasEscalator: true, EscalatorReachable: true,
            Escalator: new EscalatorComparison(DeathPctAlive: 0.10, DeathPctKilledEarly: 0.09));

        Assert.That(FloorHealthClassifier.Classify(o, Target(), Cfg),
            Is.EqualTo(FloorHealth.EscalatorBroken));
    }

    [Test]
    public void EscalatorLethalButUnreachable_IsEscalatorUnfair()
    {
        // Alive = too-much (0.40), killing it early recovers (0.08), but no window to reach it.
        var o = new FloorObserved(
            DeathPct: 0.40,
            Deaths: Deaths(Death(ThreatArchetype.Escalator, 4)),
            HasSpike: false, HasEscalator: true, EscalatorReachable: false,
            Escalator: new EscalatorComparison(DeathPctAlive: 0.40, DeathPctKilledEarly: 0.08));

        Assert.That(FloorHealthClassifier.Classify(o, Target(), Cfg),
            Is.EqualTo(FloorHealth.EscalatorUnfair));
    }

    [Test]
    public void EscalatorKilledEarlyButStillLost_IsBaselineBroken_NotEscalatorOk()
    {
        // The refinement-2 proof: alive=0.40 (hard), but killing it early barely helps (0.38) →
        // the escalator isn't the lever; the BASELINE underneath is the real threat.
        var o = new FloorObserved(
            DeathPct: 0.40,
            Deaths: Deaths(Death(ThreatArchetype.Escalator, 4)),
            HasSpike: false, HasEscalator: true, EscalatorReachable: true,
            Escalator: new EscalatorComparison(DeathPctAlive: 0.40, DeathPctKilledEarly: 0.38));

        Assert.That(FloorHealthClassifier.Classify(o, Target(), Cfg),
            Is.EqualTo(FloorHealth.BaselineBroken));
    }

    [Test]
    public void EscalatorImprovesButFloorStillTooHardNeutralized_IsBaselineBroken()
    {
        // Killing it improves meaningfully (0.45→0.30) and it's reachable, but even neutralized the
        // floor is still above band (0.30 > 0.15) → the baseline is overtuned, not the escalator.
        var o = new FloorObserved(
            DeathPct: 0.45,
            Deaths: Deaths(Death(ThreatArchetype.Escalator, 4)),
            HasSpike: false, HasEscalator: true, EscalatorReachable: true,
            Escalator: new EscalatorComparison(DeathPctAlive: 0.45, DeathPctKilledEarly: 0.30));

        Assert.That(FloorHealthClassifier.Classify(o, Target(), Cfg),
            Is.EqualTo(FloorHealth.BaselineBroken));
    }

    [Test]
    public void EscalatorHardAliveRecoversWhenKilled_IsHealthy()
    {
        // The intended shape: alive = too-much (0.40), killed early = winnable (0.05), reachable.
        // Adding it flips the verdict; neutralizing it recovers. Escalator healthy.
        var o = new FloorObserved(
            DeathPct: 0.10, // overall in band
            Deaths: Deaths(Death(ThreatArchetype.Escalator, 5)),
            HasSpike: false, HasEscalator: true, EscalatorReachable: true,
            Escalator: new EscalatorComparison(DeathPctAlive: 0.40, DeathPctKilledEarly: 0.05));

        Assert.That(FloorHealthClassifier.Classify(o, Target(), Cfg),
            Is.EqualTo(FloorHealth.Healthy));
    }

    // ── Generic band checks (no archetype attribution) ────────────────────────────────────

    [Test]
    public void DeathPctAboveBand_NoAttribution_IsTooHard()
    {
        var o = new FloorObserved(
            DeathPct: 0.30,
            Deaths: Deaths(Death(ThreatArchetype.Baseline, 10)), // attrition, not fast
            HasSpike: false, HasEscalator: false, EscalatorReachable: false, Escalator: null);

        Assert.That(FloorHealthClassifier.Classify(o, Target(), Cfg),
            Is.EqualTo(FloorHealth.TooHard));
    }

    [Test]
    public void DeathPctBelowBand_NoSpike_IsTooEasy()
    {
        var o = new FloorObserved(
            DeathPct: 0.01,
            Deaths: Deaths(),
            HasSpike: false, HasEscalator: false, EscalatorReachable: false, Escalator: null);

        Assert.That(FloorHealthClassifier.Classify(o, Target(), Cfg),
            Is.EqualTo(FloorHealth.TooEasy));
    }

    [Test]
    public void InBandWithAttritionDeaths_IsHealthy()
    {
        var o = new FloorObserved(
            DeathPct: 0.10,
            Deaths: Deaths(Death(ThreatArchetype.Baseline, 9), Death(ThreatArchetype.Spike, 3)),
            HasSpike: true, HasEscalator: false, EscalatorReachable: false, Escalator: null);

        Assert.That(FloorHealthClassifier.Classify(o, Target(), Cfg),
            Is.EqualTo(FloorHealth.Healthy));
    }
}
