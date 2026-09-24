using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

[TestFixture]
public class MonsterFactoryTests
{
    private MonsterFactory _factory = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        const string yaml = @"
monsters:
  orc:
    stats:
      hp: 28
      power: 0
      defense: 0
      xp: 35
      damage_min: 4
      damage_max: 6
      strength: 14
      dexterity: 10
      constitution: 12
      accuracy: 4
      evasion: 1
    char: ""o""
    color: [63, 127, 63]
    blocks: true
    faction: ""orc""
    etp_base: 27

  orc_grunt:
    name: ""Orc""
    extends: orc
";
        var loader = new ContentLoader();
        var defs = loader.LoadMonsters(yaml);
        _factory = new MonsterFactory(defs, new EntityFactory());
    }

    [Test]
    public void Create_ReturnsEntityWithFighter()
    {
        var orc = _factory.Create("orc");

        Assert.That(orc, Is.Not.Null);
        Assert.That(orc!.Has<Fighter>(), Is.True);
    }

    [Test]
    public void Create_SetsPosition()
    {
        var orc = _factory.Create("orc", x: 5, y: 3);

        Assert.That(orc!.X, Is.EqualTo(5));
        Assert.That(orc.Y, Is.EqualTo(3));
    }

    [Test]
    public void Create_FighterHasCorrectStats()
    {
        var orc = _factory.Create("orc")!;
        var f = orc.Require<Fighter>();

        Assert.That(f.BaseMaxHp, Is.EqualTo(28));
        Assert.That(f.Hp, Is.EqualTo(28));
        Assert.That(f.Strength, Is.EqualTo(14));
        Assert.That(f.DamageMin, Is.EqualTo(4));
        Assert.That(f.DamageMax, Is.EqualTo(6));
        Assert.That(f.Accuracy, Is.EqualTo(4));
        Assert.That(f.Evasion, Is.EqualTo(1));
        Assert.That(f.Xp, Is.EqualTo(35));
    }

    [Test]
    public void Create_InheritedMonster()
    {
        var grunt = _factory.Create("orc_grunt")!;

        Assert.That(grunt.Name, Is.EqualTo("Orc"));
        Assert.That(grunt.Require<Fighter>().BaseMaxHp, Is.EqualTo(28));
    }

    [Test]
    public void Create_BlocksMovement()
    {
        var orc = _factory.Create("orc")!;
        Assert.That(orc.BlocksMovement, Is.True);
    }

    [Test]
    public void Create_UnknownId_ReturnsNull()
    {
        Assert.That(_factory.Create("dragon"), Is.Null);
    }

    [Test]
    public void Create_SequentialIds()
    {
        var a = _factory.Create("orc")!;
        var b = _factory.Create("orc")!;

        Assert.That(a.Id, Is.Not.EqualTo(b.Id));
    }
}
