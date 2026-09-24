using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using NUnit.Framework;

namespace UnderWarden.Tests.Combat;

[TestFixture]
public class CombatMathTests
{
    [TestCase(8, -1)]
    [TestCase(9, -1)]
    [TestCase(10, 0)]
    [TestCase(11, 0)]
    [TestCase(12, 1)]
    [TestCase(13, 1)]
    [TestCase(14, 2)]
    [TestCase(15, 2)]
    [TestCase(16, 3)]
    [TestCase(17, 3)]
    [TestCase(18, 4)]
    public void StatModifier_DnDTable(int stat, int expected)
    {
        Assert.That(CombatMath.StatModifier(stat), Is.EqualTo(expected));
    }

    [Test]
    public void StatModifier_BelowMinimum()
    {
        // stat=6 → (6-10)/2 = -2
        Assert.That(CombatMath.StatModifier(6), Is.EqualTo(-2));
    }

    [Test]
    public void RollDamage_WithinRange()
    {
        var rng = new SeededRandom(1337);
        for (int i = 0; i < 100; i++)
        {
            int dmg = CombatMath.RollDamage(rng, 4, 6);
            Assert.That(dmg, Is.InRange(4, 6));
        }
    }

    [Test]
    public void RollDamage_ZeroRange_ReturnsZero()
    {
        var rng = new SeededRandom(1337);
        Assert.That(CombatMath.RollDamage(rng, 0, 0), Is.EqualTo(0));
    }

    [Test]
    public void RollDamage_InvalidRange_ReturnsZero()
    {
        var rng = new SeededRandom(1337);
        Assert.That(CombatMath.RollDamage(rng, 6, 4), Is.EqualTo(0));
    }

    [Test]
    public void RollDamage_Deterministic()
    {
        var rng1 = new SeededRandom(42);
        var rng2 = new SeededRandom(42);

        for (int i = 0; i < 50; i++)
        {
            Assert.That(
                CombatMath.RollDamage(rng1, 1, 10),
                Is.EqualTo(CombatMath.RollDamage(rng2, 1, 10)));
        }
    }
}
