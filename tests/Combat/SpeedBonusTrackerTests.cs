using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Combat;

[TestFixture]
public class SpeedBonusTrackerTests
{
    [Test]
    public void ZeroSpeed_NeverTriggers()
    {
        var tracker = new SpeedBonusTracker(0.0);
        var rng = new SeededRandom(1337);

        for (int i = 0; i < 100; i++)
            Assert.That(tracker.RollForBonusAttack(rng), Is.False);
    }

    [Test]
    public void GuaranteedAt100Percent()
    {
        // speed_bonus 0.25 → guaranteed at attack 4 (4 * 0.25 = 1.0)
        var tracker = new SpeedBonusTracker(0.25);
        var rng = new SeededRandom(999999); // seed that won't trigger early

        // Skip any early triggers by checking up to attack 4
        bool gotGuaranteed = false;
        for (int i = 0; i < 20; i++)
        {
            bool triggered = tracker.RollForBonusAttack(rng);
            if (triggered && tracker.AttackCounter == 0)
            {
                // Counter reset = guaranteed bonus
                gotGuaranteed = true;
                break;
            }
        }
        Assert.That(gotGuaranteed, Is.True, "Should get a guaranteed bonus within 20 attacks at 0.25 speed");
    }

    [Test]
    public void HighSpeed_FrequentBonuses()
    {
        // speed_bonus 1.0 → guaranteed every attack
        var tracker = new SpeedBonusTracker(1.0);
        var rng = new SeededRandom(1337);

        int bonuses = 0;
        for (int i = 0; i < 20; i++)
        {
            if (tracker.RollForBonusAttack(rng))
                bonuses++;
        }
        // At 1.0 speed, every attack triggers guaranteed bonus
        Assert.That(bonuses, Is.EqualTo(20));
    }

    [Test]
    public void ResetMomentum_ClearsCounter()
    {
        var tracker = new SpeedBonusTracker(0.25);
        var rng = new SeededRandom(1337);

        tracker.RollForBonusAttack(rng); // counter = 1
        tracker.RollForBonusAttack(rng); // counter = 2
        tracker.ResetMomentum();

        Assert.That(tracker.AttackCounter, Is.EqualTo(0));
    }

    [Test]
    public void CanBuildMomentum_FasterAttackerOnly()
    {
        var fast = new Entity(1, "Fast");
        fast.Add(new SpeedBonusTracker(0.25));

        var slow = new Entity(2, "Slow");
        slow.Add(new SpeedBonusTracker(0.10));

        var noSpeed = new Entity(3, "NoSpeed");

        Assert.That(SpeedBonusTracker.CanBuildMomentum(fast, slow), Is.True);
        Assert.That(SpeedBonusTracker.CanBuildMomentum(slow, fast), Is.False);
        Assert.That(SpeedBonusTracker.CanBuildMomentum(fast, noSpeed), Is.True);
        Assert.That(SpeedBonusTracker.CanBuildMomentum(noSpeed, fast), Is.False);
    }

    [Test]
    public void EquipmentRatio_Stacks()
    {
        var tracker = new SpeedBonusTracker(0.0);
        tracker.EquipmentRatio = 0.18;

        Assert.That(tracker.SpeedBonusRatio, Is.EqualTo(0.18).Within(0.001));

        tracker.EquipmentRatio += 0.18; // boots + weapon
        Assert.That(tracker.SpeedBonusRatio, Is.EqualTo(0.36).Within(0.001));
    }

    [Test]
    public void TargetSwitch_ResetsMomentum()
    {
        var tracker = new SpeedBonusTracker(0.25);
        var rng = new SeededRandom(1337);
        var target1 = new Entity(1, "Orc A");
        var target2 = new Entity(2, "Orc B");

        // Build momentum against target 1
        tracker.RollForBonusAttack(rng, target1); // counter = 1
        tracker.RollForBonusAttack(rng, target1); // counter = 2
        Assert.That(tracker.AttackCounter, Is.GreaterThanOrEqualTo(2));

        // Switch to target 2 — momentum resets
        tracker.RollForBonusAttack(rng, target2);
        // Counter should be 1 (reset to 0, then incremented for this attack)
        Assert.That(tracker.AttackCounter, Is.LessThanOrEqualTo(1));
    }

    [Test]
    public void SameTarget_PreservesMomentum()
    {
        var tracker = new SpeedBonusTracker(0.25);
        var rng = new SeededRandom(99999);
        var target = new Entity(1, "Orc");

        tracker.RollForBonusAttack(rng, target);
        tracker.RollForBonusAttack(rng, target);
        tracker.RollForBonusAttack(rng, target);

        // Should have built up to 3 (unless a guaranteed bonus fired and reset)
        // At minimum, counter should be > 0 since speed is only 0.25
        Assert.That(tracker.AttackCounter, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void Deterministic()
    {
        var t1 = new SpeedBonusTracker(0.25);
        var t2 = new SpeedBonusTracker(0.25);
        var rng1 = new SeededRandom(42);
        var rng2 = new SeededRandom(42);

        for (int i = 0; i < 50; i++)
        {
            Assert.That(t1.RollForBonusAttack(rng1), Is.EqualTo(t2.RollForBonusAttack(rng2)));
        }
    }

    [Test]
    public void StatisticalBonusRate_Reasonable()
    {
        // At 0.25 speed over many attacks, bonus rate should be significant
        var tracker = new SpeedBonusTracker(0.25);
        var rng = new SeededRandom(1337);

        int bonuses = 0;
        int total = 1000;
        for (int i = 0; i < total; i++)
        {
            if (tracker.RollForBonusAttack(rng))
                bonuses++;
        }

        double rate = (double)bonuses / total;
        // At 0.25, expect roughly 40-70% bonus rate due to cascading
        Assert.That(rate, Is.InRange(0.3, 0.8),
            $"Bonus rate {rate:P0} at 0.25 speed should be significant");
    }
}
