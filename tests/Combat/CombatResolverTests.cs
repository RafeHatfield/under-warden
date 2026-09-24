using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Combat;

[TestFixture]
public class CombatResolverTests
{
    private static Entity MakeEntity(string name, int hp, int str = 10, int dex = 10,
        int dmgMin = 1, int dmgMax = 4, int accuracy = 2, int evasion = 1)
    {
        var e = new Entity(0, name);
        e.Add(new Fighter(hp: hp, strength: str, dexterity: dex,
            damageMin: dmgMin, damageMax: dmgMax, accuracy: accuracy, evasion: evasion));
        return e;
    }

    [Test]
    public void ResolveAttack_Deterministic()
    {
        var attacker = MakeEntity("A", 20, str: 14, dmgMin: 4, dmgMax: 6);
        var defender1 = MakeEntity("D1", 50);
        var defender2 = MakeEntity("D2", 50);

        var rng1 = new SeededRandom(1337);
        var rng2 = new SeededRandom(1337);

        var r1 = CombatResolver.ResolveAttack(attacker, defender1, rng1);

        // Reset attacker (it's stateless for attacking)
        var r2 = CombatResolver.ResolveAttack(attacker, defender2, rng2);

        Assert.That(r1.Hit, Is.EqualTo(r2.Hit));
        Assert.That(r1.Damage, Is.EqualTo(r2.Damage));
        Assert.That(r1.D20Roll, Is.EqualTo(r2.D20Roll));
    }

    [Test]
    public void ResolveAttack_CanKill()
    {
        var attacker = MakeEntity("A", 20, str: 18, dmgMin: 10, dmgMax: 10);
        var defender = MakeEntity("D", 1);
        var rng = new SeededRandom(42);

        // Run until we get a hit
        AttackResult? killingBlow = null;
        for (int i = 0; i < 100; i++)
        {
            defender = MakeEntity("D", 1); // fresh each time
            var result = CombatResolver.ResolveAttack(attacker, defender, rng);
            if (result.Hit)
            {
                killingBlow = result;
                break;
            }
        }

        Assert.That(killingBlow, Is.Not.Null);
        Assert.That(killingBlow!.TargetKilled, Is.True);
    }

    [Test]
    public void ResolveAttack_MinimumOneDamageOnHit()
    {
        // Even with 0 damage range and negative STR, a hit does at least 1
        var attacker = MakeEntity("A", 20, str: 6, dmgMin: 0, dmgMax: 0); // STR mod = -2
        var defender = MakeEntity("D", 100);
        var rng = new SeededRandom(1337);

        for (int i = 0; i < 100; i++)
        {
            var result = CombatResolver.ResolveAttack(attacker, defender, rng);
            if (result.Hit)
            {
                Assert.That(result.Damage, Is.GreaterThanOrEqualTo(1));
                return;
            }
        }
        Assert.Fail("Never got a hit in 100 attempts");
    }

    [Test]
    public void ManyAttacks_ProduceReasonableHitRate()
    {
        // Player (DEX 14, mod +2) vs target (DEX 10, AC 10)
        // Attack roll = d20 + 2, needs >= 10 → hits on 8+ = 65%
        // Plus nat20 always hits, nat1 always misses
        var attacker = MakeEntity("A", 100, dex: 14, dmgMin: 1, dmgMax: 4);
        var defender = MakeEntity("D", 100000); // won't die
        var rng = new SeededRandom(1337);

        int hits = 0;
        int total = 1000;
        for (int i = 0; i < total; i++)
        {
            var result = CombatResolver.ResolveAttack(attacker, defender, rng);
            if (result.Hit) hits++;
        }

        double hitRate = (double)hits / total;
        // Expected ~65% (d20+2 vs AC 10), allow wide margin for small sample
        Assert.That(hitRate, Is.InRange(0.55, 0.75));
    }
}
