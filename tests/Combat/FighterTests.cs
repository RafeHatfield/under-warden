using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Combat;

[TestFixture]
public class FighterTests
{
    private static Entity CreateOrc()
    {
        var entity = new Entity(1, "Orc", 5, 3, blocksMovement: true);
        entity.Add(new Fighter(
            hp: 28,
            defense: 0,
            power: 0,
            xp: 35,
            damageMin: 4,
            damageMax: 6,
            strength: 14,
            dexterity: 10,
            constitution: 12,
            accuracy: 4,
            evasion: 1));
        return entity;
    }

    private static Entity CreatePlayer()
    {
        var entity = new Entity(0, "Player", 1, 1, blocksMovement: true);
        entity.Add(new Fighter(
            hp: 54,
            strength: 12,
            dexterity: 14,
            constitution: 12,
            accuracy: 2,
            evasion: 1));
        return entity;
    }

    [Test]
    public void Orc_BaseStats()
    {
        var orc = CreateOrc();
        var fighter = orc.Require<Fighter>();

        Assert.That(fighter.BaseMaxHp, Is.EqualTo(28));
        Assert.That(fighter.Hp, Is.EqualTo(28));
        Assert.That(fighter.Strength, Is.EqualTo(14));
        Assert.That(fighter.StrengthMod, Is.EqualTo(2));   // (14-10)/2
        Assert.That(fighter.DexterityMod, Is.EqualTo(0));   // (10-10)/2
        Assert.That(fighter.ConstitutionMod, Is.EqualTo(1)); // (12-10)/2
        Assert.That(fighter.Accuracy, Is.EqualTo(4));
        Assert.That(fighter.Evasion, Is.EqualTo(1));
        Assert.That(fighter.Xp, Is.EqualTo(35));
    }

    [Test]
    public void MaxHp_IncludesConstitutionMod()
    {
        var orc = CreateOrc();
        var fighter = orc.Require<Fighter>();

        // base 28 + CON mod 1 (CON=12) = 29 — matches PoC fighter.max_hp
        Assert.That(fighter.MaxHp, Is.EqualTo(29));
    }

    [Test]
    public void Player_MaxHp()
    {
        var player = CreatePlayer();
        var fighter = player.Require<Fighter>();

        // Player in test has CON=12 → mod +1. base 54 + 1 = 55.
        Assert.That(fighter.MaxHp, Is.EqualTo(55));
    }

    [Test]
    public void BaseArmorClass()
    {
        var orc = CreateOrc();
        var fighter = orc.Require<Fighter>();

        // 10 + DEX mod 0 = 10
        Assert.That(fighter.BaseArmorClass, Is.EqualTo(10));

        var player = CreatePlayer();
        var pf = player.Require<Fighter>();

        // 10 + DEX mod 2 = 12
        Assert.That(pf.BaseArmorClass, Is.EqualTo(12));
    }

    [Test]
    public void TakeDamage_ReducesHp()
    {
        var orc = CreateOrc();
        var fighter = orc.Require<Fighter>();

        int dealt = fighter.TakeDamage(10);

        Assert.That(dealt, Is.EqualTo(10));
        Assert.That(fighter.Hp, Is.EqualTo(18));
        Assert.That(fighter.IsAlive, Is.True);
    }

    [Test]
    public void TakeDamage_CanKill()
    {
        var orc = CreateOrc();
        var fighter = orc.Require<Fighter>();

        fighter.TakeDamage(30);

        Assert.That(fighter.Hp, Is.LessThanOrEqualTo(0));
        Assert.That(fighter.IsAlive, Is.False);
    }

    [Test]
    public void TakeDamage_NegativeIsClamped()
    {
        var orc = CreateOrc();
        var fighter = orc.Require<Fighter>();

        int dealt = fighter.TakeDamage(-5);

        Assert.That(dealt, Is.EqualTo(0));
        Assert.That(fighter.Hp, Is.EqualTo(28));
    }

    [Test]
    public void Heal_RestoresHp()
    {
        var orc = CreateOrc();
        var fighter = orc.Require<Fighter>();
        fighter.TakeDamage(10);

        int healed = fighter.Heal(5);

        Assert.That(healed, Is.EqualTo(5));
        Assert.That(fighter.Hp, Is.EqualTo(23));
    }

    [Test]
    public void Heal_CapsAtMaxHp()
    {
        var orc = CreateOrc();
        var fighter = orc.Require<Fighter>();
        // Orc: BaseMaxHp=28, CON=12 (mod +1), MaxHp=29. Hp starts at 28.
        fighter.TakeDamage(5); // Hp = 23

        int healed = fighter.Heal(100);

        Assert.That(fighter.Hp, Is.EqualTo(fighter.MaxHp)); // 29
        Assert.That(healed, Is.EqualTo(6)); // 29 - 23 = 6
    }

    [Test]
    public void Heal_AtFullHp_ReturnsZero()
    {
        var orc = CreateOrc();
        var fighter = orc.Require<Fighter>();

        int healed = fighter.Heal(10);

        // Hp starts at BaseMaxHp (28), MaxHp is 29 (with CON mod)
        // So can heal 1 point
        Assert.That(healed, Is.EqualTo(1));
    }

    [Test]
    public void Heal_NegativeAmount_ReturnsZero()
    {
        var orc = CreateOrc();
        var fighter = orc.Require<Fighter>();
        fighter.TakeDamage(10);

        int healed = fighter.Heal(-5);

        Assert.That(healed, Is.EqualTo(0));
        Assert.That(fighter.Hp, Is.EqualTo(18));
    }

    [Test]
    public void HitChance_PlayerVsOrc()
    {
        var player = CreatePlayer();
        var orc = CreateOrc();
        var pf = player.Require<Fighter>();
        var of = orc.Require<Fighter>();

        // Player acc=2 vs Orc eva=1 → 80%
        double playerChance = HitModel.ComputeHitChance(pf.Accuracy, of.Evasion);
        Assert.That(playerChance, Is.EqualTo(0.80).Within(0.001));

        // Orc acc=4 vs Player eva=1 → 90%
        double orcChance = HitModel.ComputeHitChance(of.Accuracy, pf.Evasion);
        Assert.That(orcChance, Is.EqualTo(0.90).Within(0.001));
    }

    [Test]
    public void DefaultStats_MatchPrototype()
    {
        // Default fighter should use HitModel defaults
        var fighter = new Fighter(hp: 20);

        Assert.That(fighter.Accuracy, Is.EqualTo(2));
        Assert.That(fighter.Evasion, Is.EqualTo(1));
        Assert.That(fighter.Strength, Is.EqualTo(10));
        Assert.That(fighter.Dexterity, Is.EqualTo(10));
        Assert.That(fighter.Constitution, Is.EqualTo(10));
        Assert.That(fighter.StrengthMod, Is.EqualTo(0));
        Assert.That(fighter.MaxHp, Is.EqualTo(20)); // no CON bonus
    }
}
