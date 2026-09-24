using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

[TestFixture]
public class LifeDrainTests
{
    private static Entity CreateWraith(int hp = 20, double drainPct = 0.50)
    {
        var wraith = new Entity(1, "Wraith", 5, 5, blocksMovement: true);
        wraith.Add(new Fighter(hp: hp, damageMin: 5, damageMax: 9));
        wraith.Add(new LifeDrainComponent(drainPct));
        return wraith;
    }

    [Test]
    public void LifeDrain_HealsFor50PercentOfDamage()
    {
        var wraith = CreateWraith(hp: 20);
        var fighter = wraith.Require<Fighter>();
        fighter.Hp = 10; // wounded

        // Simulate: wraith dealt 8 damage, drain = ceil(0.50 * 8) = 4
        var drain = wraith.Get<LifeDrainComponent>()!;
        int drainAmount = (int)Math.Ceiling(drain.DrainPct * 8);
        int healed = fighter.Heal(drainAmount);

        Assert.That(drainAmount, Is.EqualTo(4));
        Assert.That(healed, Is.EqualTo(4));
        Assert.That(fighter.Hp, Is.EqualTo(14));
    }

    [Test]
    public void LifeDrain_CapsAtMissingHp()
    {
        var wraith = CreateWraith(hp: 20);
        var fighter = wraith.Require<Fighter>();
        fighter.Hp = 19; // only 1 HP missing

        int drainAmount = (int)Math.Ceiling(0.50 * 10); // 5
        int healed = fighter.Heal(drainAmount);

        Assert.That(healed, Is.EqualTo(1)); // capped at missing HP
        Assert.That(fighter.Hp, Is.EqualTo(20));
    }

    [Test]
    public void LifeDrain_ZeroDamage_NoHeal()
    {
        var wraith = CreateWraith(hp: 20);
        var fighter = wraith.Require<Fighter>();
        fighter.Hp = 10;

        int drainAmount = (int)Math.Ceiling(0.50 * 0);
        int healed = fighter.Heal(drainAmount);

        Assert.That(healed, Is.EqualTo(0));
        Assert.That(fighter.Hp, Is.EqualTo(10));
    }

    [Test]
    public void LifeDrain_ZeroPct_NoHeal()
    {
        var entity = new Entity(1, "NoLifeDrain", 5, 5, blocksMovement: true);
        entity.Add(new Fighter(hp: 20));
        entity.Add(new LifeDrainComponent(0.0));
        var fighter = entity.Require<Fighter>();
        fighter.Hp = 10;

        int drainAmount = (int)Math.Ceiling(0.0 * 8);
        Assert.That(drainAmount, Is.EqualTo(0));
    }

    [Test]
    public void LifeDrainComponent_StoresDrainPct()
    {
        var comp = new LifeDrainComponent(0.50);
        Assert.That(comp.DrainPct, Is.EqualTo(0.50));
    }
}
