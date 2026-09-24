using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Combat;

[TestFixture]
public class DamageModifiersTests
{
    [Test]
    public void Resistance_HalvesDamage()
    {
        var mods = new DamageModifiers { Resistance = "piercing" };
        Assert.That(mods.ApplyTo(10, "piercing"), Is.EqualTo(5));
    }

    [Test]
    public void Vulnerability_DoublesDamage()
    {
        var mods = new DamageModifiers { Vulnerability = "bludgeoning" };
        Assert.That(mods.ApplyTo(10, "bludgeoning"), Is.EqualTo(20));
    }

    [Test]
    public void NeutralType_NoDamageChange()
    {
        var mods = new DamageModifiers { Resistance = "piercing", Vulnerability = "bludgeoning" };
        Assert.That(mods.ApplyTo(10, "slashing"), Is.EqualTo(10));
    }

    [Test]
    public void NullDamageType_NoDamageChange()
    {
        var mods = new DamageModifiers { Resistance = "piercing" };
        Assert.That(mods.ApplyTo(10, null), Is.EqualTo(10));
    }

    [Test]
    public void Resistance_MinimumOneDamage()
    {
        var mods = new DamageModifiers { Resistance = "piercing" };
        Assert.That(mods.ApplyTo(1, "piercing"), Is.EqualTo(1)); // 1/2 = 0, floored to 1
    }

    [Test]
    public void CaseInsensitive()
    {
        var mods = new DamageModifiers { Resistance = "Piercing" };
        Assert.That(mods.ApplyTo(10, "piercing"), Is.EqualTo(5));
    }

    [Test]
    public void CritThreshold_KeenWeapon_CritsOn19()
    {
        var attacker = new Entity(1, "Player");
        attacker.Add(new Fighter(hp: 50, damageMin: 5, damageMax: 5)); // fixed damage
        var keenDagger = new Entity(10, "Keen Dagger");
        keenDagger.Add(new Equippable(EquipmentSlot.MainHand)
        {
            DamageMin = 5, DamageMax = 5, CritThreshold = 19,
        });
        var equipment = attacker.Add(new Equipment());
        equipment.MainHand = keenDagger;

        var defender = new Entity(2, "Dummy", blocksMovement: true);
        defender.Add(new Fighter(hp: 1000));

        // Run many attacks, count crits
        var rng = new SeededRandom(1337);
        int crits = 0;
        int total = 2000;
        for (int i = 0; i < total; i++)
        {
            // Reset defender HP
            defender.Require<Fighter>().Hp = 1000;
            var result = CombatResolver.ResolveAttack(attacker, defender, rng);
            if (result.IsCritical) crits++;
        }

        double critRate = (double)crits / total;
        // Keen: crits on 19 and 20 = 10% expected (was 5% on 20 only)
        Assert.That(critRate, Is.InRange(0.07, 0.13),
            $"Keen crit rate {critRate:P1} should be ~10% (19-20 on d20)");
    }

    [Test]
    public void Zombie_TakesHalfFromDagger_DoubleFromClub()
    {
        // Zombie with piercing resistance, bludgeoning vulnerability
        var zombie = new Entity(1, "Zombie");
        zombie.Add(new Fighter(hp: 200));
        zombie.Add(new DamageModifiers { Resistance = "piercing", Vulnerability = "bludgeoning" });

        var player = new Entity(0, "Player");
        player.Add(new Fighter(hp: 50, strength: 12)); // STR mod +1

        // Dagger (piercing) — should do less damage
        var dagger = new Entity(10, "Dagger");
        dagger.Add(new Equippable(EquipmentSlot.MainHand)
        {
            DamageMin = 1, DamageMax = 4, DamageType = "piercing",
        });
        var equipD = player.Add(new Equipment());
        equipD.MainHand = dagger;

        var rng1 = new SeededRandom(1337);
        int daggerDmg = 0;
        for (int i = 0; i < 500; i++)
        {
            zombie.Require<Fighter>().Hp = 200;
            var result = CombatResolver.ResolveAttack(player, zombie, rng1);
            daggerDmg += result.Damage;
        }

        // Club (bludgeoning) — should do more damage
        var club = new Entity(11, "Club");
        club.Add(new Equippable(EquipmentSlot.MainHand)
        {
            DamageMin = 1, DamageMax = 6, DamageType = "bludgeoning",
        });
        equipD.MainHand = club;

        var rng2 = new SeededRandom(1337);
        int clubDmg = 0;
        for (int i = 0; i < 500; i++)
        {
            zombie.Require<Fighter>().Hp = 200;
            var result = CombatResolver.ResolveAttack(player, zombie, rng2);
            clubDmg += result.Damage;
        }

        // Club should deal significantly more total damage than dagger vs zombie
        Assert.That(clubDmg, Is.GreaterThan(daggerDmg * 2),
            $"Club ({clubDmg}) should deal >2x dagger ({daggerDmg}) vs zombie (resist piercing, vuln bludgeoning)");
    }
}
