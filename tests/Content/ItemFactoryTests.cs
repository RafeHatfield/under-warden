using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

[TestFixture]
public class ItemFactoryTests
{
    private ItemFactory _factory = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        const string yaml = @"
weapons:
  dagger:
    slot: ""main_hand""
    damage_min: 1
    damage_max: 4
    to_hit_bonus: 1
    damage_type: ""piercing""

  keen_dagger:
    name: ""Keen Dagger""
    slot: ""main_hand""
    damage_min: 1
    damage_max: 4
    to_hit_bonus: 1
    damage_type: ""piercing""
    crit_threshold: 19

  quickfang_dagger:
    name: ""Quickfang Dagger""
    slot: ""main_hand""
    damage_min: 1
    damage_max: 4
    to_hit_bonus: 1
    damage_type: ""piercing""
    speed_bonus: 0.18

  masterwork_longsword:
    name: ""Masterwork Longsword""
    slot: ""main_hand""
    damage_min: 2
    damage_max: 9
    to_hit_bonus: 1
    damage_type: ""slashing""
";
        var loader = new ContentLoader();
        _factory = new ItemFactory(loader.LoadItems(yaml), new EntityFactory());
    }

    [Test]
    public void KeenDagger_HasCritThreshold19()
    {
        var item = _factory.Create("keen_dagger")!;
        var eq = item.Require<Equippable>();

        Assert.That(eq.CritThreshold, Is.EqualTo(19));
        Assert.That(eq.DamageType, Is.EqualTo("piercing"));
    }

    [Test]
    public void RegularDagger_HasDefaultCritThreshold()
    {
        var item = _factory.Create("dagger")!;
        Assert.That(item.Require<Equippable>().CritThreshold, Is.EqualTo(20));
    }

    [Test]
    public void QuickfangDagger_HasSpeedBonus()
    {
        var item = _factory.Create("quickfang_dagger")!;
        var tracker = item.Get<SpeedBonusTracker>();

        Assert.That(tracker, Is.Not.Null);
        Assert.That(tracker!.EquipmentRatio, Is.EqualTo(0.18).Within(0.001));
    }

    [Test]
    public void MasterworkLongsword_HasBonuses()
    {
        var item = _factory.Create("masterwork_longsword")!;
        var eq = item.Require<Equippable>();

        Assert.That(eq.DamageMin, Is.EqualTo(2));
        Assert.That(eq.DamageMax, Is.EqualTo(9));
        Assert.That(eq.ToHitBonus, Is.EqualTo(1));
    }

    [Test]
    public void UnknownItem_ReturnsNull()
    {
        Assert.That(_factory.Create("excalibur"), Is.Null);
    }
}
