using UnderWarden.Logic.Combat;
using NUnit.Framework;

namespace UnderWarden.Tests.Combat;

[TestFixture]
public class HitModelTests
{
    [Test]
    public void EqualAccuracyAndEvasion_ReturnsBaseHit()
    {
        double chance = HitModel.ComputeHitChance(3, 3);
        Assert.That(chance, Is.EqualTo(0.75).Within(0.001));
    }

    [Test]
    public void DefaultStats_PlayerVsOrc()
    {
        // Player accuracy=2, Orc evasion=1 → 75% + (2-1)*5% = 80%
        double chance = HitModel.ComputeHitChance(2, 1);
        Assert.That(chance, Is.EqualTo(0.80).Within(0.001));
    }

    [Test]
    public void HighAccuracy_CapsAtMax()
    {
        // +20 accuracy advantage → 75% + 100% = 175%, capped at 95%
        double chance = HitModel.ComputeHitChance(25, 5);
        Assert.That(chance, Is.EqualTo(HitModel.MaxHit).Within(0.001));
    }

    [Test]
    public void HighEvasion_CapsAtMin()
    {
        // -20 accuracy disadvantage → 75% - 100% = -25%, capped at 5%
        double chance = HitModel.ComputeHitChance(0, 20);
        Assert.That(chance, Is.EqualTo(HitModel.MinHit).Within(0.001));
    }

    [Test]
    public void EachPointIsFivePercent()
    {
        double base_ = HitModel.ComputeHitChance(5, 5);    // 75%
        double plus1 = HitModel.ComputeHitChance(6, 5);     // 80%
        double minus1 = HitModel.ComputeHitChance(4, 5);    // 70%

        Assert.That(plus1 - base_, Is.EqualTo(0.05).Within(0.001));
        Assert.That(base_ - minus1, Is.EqualTo(0.05).Within(0.001));
    }

    [TestCase(4, 1, 0.90)]  // Orc accuracy=4, player evasion=1
    [TestCase(1, 4, 0.60)]  // Low accuracy vs high evasion
    [TestCase(0, 0, 0.75)]  // Zero stats = base
    public void KnownCombinations(int accuracy, int evasion, double expected)
    {
        double chance = HitModel.ComputeHitChance(accuracy, evasion);
        Assert.That(chance, Is.EqualTo(expected).Within(0.001));
    }
}
