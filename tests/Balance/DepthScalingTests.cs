using UnderWarden.Logic.Balance;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

[TestFixture]
public class DepthScalingTests
{
    [TestCase(1, 0)]
    [TestCase(2, 0)]
    [TestCase(3, 1)]
    [TestCase(4, 1)]
    [TestCase(5, 2)]
    [TestCase(6, 2)]
    [TestCase(7, 3)]
    [TestCase(8, 3)]
    [TestCase(9, 4)]
    [TestCase(15, 4)]
    public void GetBand_CorrectMapping(int depth, int expectedBand)
    {
        Assert.That(DepthScaling.GetBand(depth), Is.EqualTo(expectedBand));
    }

    [Test]
    public void DefaultCurve_Depth1_NoScaling()
    {
        var m = DepthScaling.GetDefault(1);
        Assert.That(m.Hp, Is.EqualTo(1.0));
        Assert.That(m.ToHit, Is.EqualTo(1.0));
        Assert.That(m.Damage, Is.EqualTo(1.0));
    }

    [Test]
    public void DefaultCurve_Depth5_MidgameScaling()
    {
        var m = DepthScaling.GetDefault(5);
        Assert.That(m.Hp, Is.EqualTo(1.25));
        Assert.That(m.ToHit, Is.EqualTo(1.12));
        Assert.That(m.Damage, Is.EqualTo(1.05));
    }

    [Test]
    public void DefaultCurve_Depth9_EndgameScaling()
    {
        var m = DepthScaling.GetDefault(9);
        Assert.That(m.Hp, Is.EqualTo(1.45));
        Assert.That(m.ToHit, Is.EqualTo(1.22));
        Assert.That(m.Damage, Is.EqualTo(1.15));
    }

    [Test]
    public void ZombieCurve_Depth5_NoScaling()
    {
        var m = DepthScaling.GetZombie(5);
        Assert.That(m.Hp, Is.EqualTo(1.0));
        Assert.That(m.ToHit, Is.EqualTo(1.0));
        Assert.That(m.Damage, Is.EqualTo(1.0));
    }

    [Test]
    public void ZombieCurve_Depth9_ConservativeScaling()
    {
        var m = DepthScaling.GetZombie(9);
        Assert.That(m.Hp, Is.EqualTo(1.20));
        Assert.That(m.ToHit, Is.EqualTo(1.10));
        Assert.That(m.Damage, Is.EqualTo(1.05));
    }

    [Test]
    public void GetForTags_ZombieTag_UsesZombieCurve()
    {
        var tags = new[] { "undead", "zombie" };
        var m = DepthScaling.GetForTags(5, tags);
        Assert.That(m.Hp, Is.EqualTo(1.0)); // zombie curve: no scaling at depth 5
    }

    [Test]
    public void GetForTags_NoZombieTag_UsesDefaultCurve()
    {
        var tags = new[] { "humanoid", "living" };
        var m = DepthScaling.GetForTags(5, tags);
        Assert.That(m.Hp, Is.EqualTo(1.25)); // default curve
    }

    [Test]
    public void GetForTags_NullTags_UsesDefaultCurve()
    {
        var m = DepthScaling.GetForTags(5, null);
        Assert.That(m.Hp, Is.EqualTo(1.25));
    }

    [Test]
    public void ScaleHp_RoundsUp()
    {
        // 28 * 1.08 = 30.24 → ceil = 31
        Assert.That(DepthScaling.ScaleHp(28, 1.08), Is.EqualTo(31));
    }

    [Test]
    public void ScaleStat_RoundsHalfUp()
    {
        // 4 * 1.06 = 4.24 → round = 4
        Assert.That(DepthScaling.ScaleStat(4, 1.06), Is.EqualTo(4));

        // 4 * 1.12 = 4.48 → round = 4
        Assert.That(DepthScaling.ScaleStat(4, 1.12), Is.EqualTo(4));

        // 4 * 1.25 = 5.0 → round = 5
        Assert.That(DepthScaling.ScaleStat(4, 1.25), Is.EqualTo(5));
    }

    [Test]
    public void OrcAtDepth5_MatchesPrototype()
    {
        // Orc base: HP 28, damage 4-6, accuracy 4
        // Depth 5 default: HP 1.25x, DMG 1.05x, ToHit 1.12x
        var m = DepthScaling.GetDefault(5);

        int hp = DepthScaling.ScaleHp(28, m.Hp);       // 28 * 1.25 = 35.0 → ceil = 35
        int dmgMin = DepthScaling.ScaleStat(4, m.Damage); // 4 * 1.05 = 4.2 → round = 4
        int dmgMax = DepthScaling.ScaleStat(6, m.Damage); // 6 * 1.05 = 6.3 → round = 6
        int acc = DepthScaling.ScaleStat(4, m.ToHit);   // 4 * 1.12 = 4.48 → round = 4

        Assert.That(hp, Is.EqualTo(35));
        Assert.That(dmgMin, Is.EqualTo(4));
        Assert.That(dmgMax, Is.EqualTo(6));
        Assert.That(acc, Is.EqualTo(4));
    }
}
