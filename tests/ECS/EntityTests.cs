using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.ECS;

// Minimal test components
public class HealthComponent : IComponent
{
    public Entity? Owner { get; set; }
    public int MaxHp { get; set; }
    public int CurrentHp { get; set; }

    public HealthComponent(int maxHp)
    {
        MaxHp = maxHp;
        CurrentHp = maxHp;
    }
}

public class NameTagComponent : IComponent
{
    public Entity? Owner { get; set; }
    public string Tag { get; set; }

    public NameTagComponent(string tag) => Tag = tag;
}

[TestFixture]
public class EntityTests
{
    [Test]
    public void Add_SetsOwnership()
    {
        var entity = new Entity(1, "Orc", 5, 3);
        var health = new HealthComponent(28);

        entity.Add(health);

        Assert.That(health.Owner, Is.SameAs(entity));
    }

    [Test]
    public void Get_ReturnsComponent()
    {
        var entity = new Entity(1, "Orc");
        entity.Add(new HealthComponent(28));

        var health = entity.Get<HealthComponent>();

        Assert.That(health, Is.Not.Null);
        Assert.That(health!.MaxHp, Is.EqualTo(28));
    }

    [Test]
    public void Get_ReturnsNull_WhenMissing()
    {
        var entity = new Entity(1, "Orc");

        Assert.That(entity.Get<HealthComponent>(), Is.Null);
    }

    [Test]
    public void Require_ReturnsComponent()
    {
        var entity = new Entity(1, "Orc");
        entity.Add(new HealthComponent(28));

        var health = entity.Require<HealthComponent>();

        Assert.That(health.MaxHp, Is.EqualTo(28));
    }

    [Test]
    public void Require_Throws_WhenMissing()
    {
        var entity = new Entity(1, "Orc");

        Assert.Throws<InvalidOperationException>(() => entity.Require<HealthComponent>());
    }

    [Test]
    public void Has_ReturnsTrueWhenPresent()
    {
        var entity = new Entity(1, "Orc");
        entity.Add(new HealthComponent(28));

        Assert.That(entity.Has<HealthComponent>(), Is.True);
        Assert.That(entity.Has<NameTagComponent>(), Is.False);
    }

    [Test]
    public void Remove_ClearsOwnership()
    {
        var entity = new Entity(1, "Orc");
        var health = new HealthComponent(28);
        entity.Add(health);

        bool removed = entity.Remove<HealthComponent>();

        Assert.That(removed, Is.True);
        Assert.That(health.Owner, Is.Null);
        Assert.That(entity.Has<HealthComponent>(), Is.False);
    }

    [Test]
    public void Remove_ReturnsFalse_WhenMissing()
    {
        var entity = new Entity(1, "Orc");

        Assert.That(entity.Remove<HealthComponent>(), Is.False);
    }

    [Test]
    public void Add_ReplacesExisting_ClearsOldOwnership()
    {
        var entity = new Entity(1, "Orc");
        var old = new HealthComponent(28);
        var replacement = new HealthComponent(50);

        entity.Add(old);
        entity.Add(replacement);

        Assert.That(old.Owner, Is.Null);
        Assert.That(replacement.Owner, Is.SameAs(entity));
        Assert.That(entity.Require<HealthComponent>().MaxHp, Is.EqualTo(50));
    }

    [Test]
    public void MultipleComponentTypes()
    {
        var entity = new Entity(1, "Orc");
        entity.Add(new HealthComponent(28));
        entity.Add(new NameTagComponent("Elite"));

        Assert.That(entity.ComponentCount, Is.EqualTo(2));
        Assert.That(entity.Require<HealthComponent>().MaxHp, Is.EqualTo(28));
        Assert.That(entity.Require<NameTagComponent>().Tag, Is.EqualTo("Elite"));
    }

    [Test]
    public void ChebyshevDistance_MeleeRange()
    {
        var entity = new Entity(1, "Player", 5, 5);

        // Adjacent (melee range = 1)
        Assert.That(entity.ChebyshevDistanceTo(6, 5), Is.EqualTo(1));
        Assert.That(entity.ChebyshevDistanceTo(6, 6), Is.EqualTo(1)); // diagonal
        Assert.That(entity.ChebyshevDistanceTo(5, 5), Is.EqualTo(0)); // same tile
        Assert.That(entity.ChebyshevDistanceTo(7, 5), Is.EqualTo(2)); // out of melee
    }

    [Test]
    public void FluentAdd()
    {
        var entity = new Entity(1, "Orc");
        var health = entity.Add(new HealthComponent(28));

        Assert.That(health.MaxHp, Is.EqualTo(28));
        Assert.That(health.Owner, Is.SameAs(entity));
    }
}

[TestFixture]
public class EntityFactoryTests
{
    [Test]
    public void Create_AssignsSequentialIds()
    {
        var factory = new EntityFactory();

        var e1 = factory.Create("Orc", 1, 2);
        var e2 = factory.Create("Zombie", 3, 4);

        Assert.That(e1.Id, Is.EqualTo(1));
        Assert.That(e2.Id, Is.EqualTo(2));
        Assert.That(e1.Name, Is.EqualTo("Orc"));
        Assert.That(e1.X, Is.EqualTo(1));
        Assert.That(e1.Y, Is.EqualTo(2));
    }

    [Test]
    public void Create_SetsBlocksMovement()
    {
        var factory = new EntityFactory();

        var monster = factory.Create("Orc", blocksMovement: true);
        var item = factory.Create("Potion");

        Assert.That(monster.BlocksMovement, Is.True);
        Assert.That(item.BlocksMovement, Is.False);
    }
}
