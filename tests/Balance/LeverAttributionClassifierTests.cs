using UnderWarden.Logic.Balance;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// OUTCOME-level tests for LeverAttributionClassifier — the diagnostic that sits beside FloorHealth.
/// A known signal pattern must attribute to the RIGHT lever: high damage-per-hit ⇒ monster-damage,
/// normal damage but high hit-rate ⇒ armor (NOT "baseline broken"), normal damage but high frequency
/// ⇒ attack-frequency (NOT monster-damage). This is the precision the multi-signal scope exists to buy.
/// Pure data; no engine.
/// </summary>
[TestFixture]
public class LeverAttributionClassifierTests
{
    private static readonly LeverConfig Cfg = new();

    // "Everything normal" expectation at baseline gear.
    private static readonly LeverExpectation Expected = new(
        DamagePerHit: 5.0, KillerHitRate: 0.5, CounterattacksLanded: 4.0,
        DistinctAttackers: 2.0, AttackFrequency: 1.0);

    // Build a death whose signals default to exactly the expectation (AttackFrequency = hits/turns = 1.0).
    private static PlayerDeathRecord Death(
        double dmgPerHit = 5.0, double hitRate = 0.5, int counters = 4,
        int attackers = 2, int hits = 3, int engagementTurns = 3) => new()
        {
            DamagePerHit = dmgPerHit,
            KillerHitRate = hitRate,
            CounterattacksLanded = counters,
            DistinctAttackers = attackers,
            HitsToDown = hits,
            EngagementTurns = engagementTurns,
        };

    private static BalanceLever? Dominant(PlayerDeathRecord d) => LeverAttributionClassifier.Dominant(d, Expected, Cfg);

    [Test]
    public void HighDamagePerHit_ImplicatesMonsterDamage()
        => Assert.That(Dominant(Death(dmgPerHit: 10.0)), Is.EqualTo(BalanceLever.MonsterDamage));

    [Test]
    public void NormalDamage_HighHitRate_ImplicatesArmor_NotMonster()
    {
        // The motivating case: the monster's per-hit damage is fine; it just lands too often → armor/AC.
        var d = Death(dmgPerHit: 5.0, hitRate: 0.9);
        Assert.That(Dominant(d), Is.EqualTo(BalanceLever.Armor));
        Assert.That(LeverAttributionClassifier.Attribute(d, Expected, Cfg).Select(f => f.Lever),
            Does.Not.Contain(BalanceLever.MonsterDamage));
    }

    [Test]
    public void NormalDamage_HighFrequency_ImplicatesAttackFrequency_NotMonsterDamage()
    {
        // Wraith case: two NORMAL-damage blows per turn. Frequency is its own lever; damage stays clean.
        var d = Death(dmgPerHit: 5.0, hits: 4, engagementTurns: 2); // frequency 2.0 vs expected 1.0
        Assert.That(d.AttackFrequency, Is.EqualTo(2.0).Within(1e-9));
        Assert.That(Dominant(d), Is.EqualTo(BalanceLever.AttackFrequency));
        Assert.That(LeverAttributionClassifier.Attribute(d, Expected, Cfg).Select(f => f.Lever),
            Does.Not.Contain(BalanceLever.MonsterDamage));
    }

    [Test]
    public void ManyDistinctAttackers_ImplicatesDensity()
        => Assert.That(Dominant(Death(attackers: 6)), Is.EqualTo(BalanceLever.Density));

    [Test]
    public void FewCounterattacks_ImplicatesWeaponSpeedControl()
        => Assert.That(Dominant(Death(counters: 1)), Is.EqualTo(BalanceLever.WeaponSpeedControl));

    [Test]
    public void AllSignalsWithinTolerance_ImplicatesNothing()
    {
        var d = Death(); // every signal at expectation
        Assert.That(LeverAttributionClassifier.Attribute(d, Expected, Cfg), Is.Empty);
        Assert.That(Dominant(d), Is.Null);
    }

    [Test]
    public void SmallDeviationWithinTolerance_NotImplicated()
    {
        // +20% damage-per-hit is under the 25% implication threshold → not flagged.
        Assert.That(Dominant(Death(dmgPerHit: 6.0)), Is.Null);
    }

    [Test]
    public void MultipleImplicated_RankedByDeviation_WorstFirst()
    {
        // Damage +50% and frequency +100% both flag; frequency deviates more → it leads.
        var d = Death(dmgPerHit: 7.5, hits: 4, engagementTurns: 2);
        var findings = LeverAttributionClassifier.Attribute(d, Expected, Cfg);
        Assert.That(findings.Select(f => f.Lever),
            Is.EquivalentTo(new[] { BalanceLever.MonsterDamage, BalanceLever.AttackFrequency }));
        Assert.That(findings[0].Lever, Is.EqualTo(BalanceLever.AttackFrequency), "worst deviation leads");
    }

    [Test]
    public void ZeroExpectation_SignalSkipped_NoDivideByZero()
    {
        // A region with no authored expectation for a lever (0) must not crash or false-implicate it.
        var noDensityExpectation = Expected with { DistinctAttackers = 0.0 };
        Assert.That(LeverAttributionClassifier.Attribute(Death(attackers: 9), noDensityExpectation, Cfg)
            .Select(f => f.Lever), Does.Not.Contain(BalanceLever.Density));
    }
}
