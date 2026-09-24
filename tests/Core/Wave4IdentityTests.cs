using UnderWarden.Logic.AI;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Wave 4 monster identity tests — validates that each deep-dungeon monster's
/// signature mechanics work correctly via factory creation and ECS behavior.
///
/// Covers: wraith (life drain, speed, no corpse, status immunities),
/// lich (soul bolt, command the dead, death siphon, necromancer fallback, no corpse),
/// plague_zombie (plague on-hit, extends zombie), troll_ancient (regen 3, extends troll),
/// greater_slime (splits into large_slimes at 35%).
/// </summary>
[TestFixture]
public class Wave4IdentityTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string FindEntitiesYaml()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var path = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", "entities.yaml"));
        if (File.Exists(path)) return path;
        path = Path.GetFullPath(Path.Combine(testDir, "config", "entities.yaml"));
        if (File.Exists(path)) return path;
        throw new FileNotFoundException($"entities.yaml not found. Tried: {path}");
    }

    private static MonsterFactory CreateFactory()
    {
        var loader = new ContentLoader();
        var bundle = loader.LoadAllFromFile(FindEntitiesYaml());
        var entityFactory = new EntityFactory();
        var itemFactory = new ItemFactory(bundle.Items, entityFactory);
        return new MonsterFactory(bundle.Monsters, entityFactory, itemFactory);
    }

    // ─── Content loading: all wave 4 monsters parse ──────────────────────────

    [Test]
    public void ContentLoader_AllWave4MonstersLoad()
    {
        var factory = CreateFactory();
        foreach (var id in new[] { "plague_zombie", "troll_ancient", "greater_slime", "wraith", "lich" })
        {
            var entity = factory.Create(id);
            Assert.That(entity, Is.Not.Null, $"'{id}' must be registered in entities.yaml");
            Assert.That(entity!.Require<Fighter>().IsAlive, Is.True, $"'{id}' must have a living Fighter");
        }
    }

    // ─── Wraith ──────────────────────────────────────────────────────────────

    [Test]
    public void Wraith_HasLifeDrainComponent()
    {
        var factory = CreateFactory();
        var wraith = factory.Create("wraith")!;
        var drain = wraith.Get<LifeDrainComponent>();

        Assert.That(drain, Is.Not.Null, "Wraith must have LifeDrainComponent");
        Assert.That(drain!.DrainPct, Is.EqualTo(0.50));
    }

    [Test]
    public void Wraith_HasSpeed2x()
    {
        var factory = CreateFactory();
        var wraith = factory.Create("wraith")!;
        var speed = wraith.Get<SpeedBonusTracker>();

        Assert.That(speed, Is.Not.Null, "Wraith must have SpeedBonusTracker");
        Assert.That(speed!.BaseRatio, Is.EqualTo(2.0));
    }

    [Test]
    public void Wraith_LeavesNoCorpse()
    {
        var factory = CreateFactory();
        var def = factory.GetDefinition("wraith")!;
        Assert.That(def.LeavesCorpse, Is.False, "Wraith must not leave a corpse");
    }

    [Test]
    public void Wraith_CorrectStats()
    {
        var factory = CreateFactory();
        var wraith = factory.Create("wraith")!;
        var f = wraith.Require<Fighter>();

        Assert.That(f.BaseMaxHp, Is.EqualTo(20));
        Assert.That(f.DamageMin, Is.EqualTo(5));
        Assert.That(f.DamageMax, Is.EqualTo(9));
        Assert.That(f.Xp, Is.EqualTo(100));
    }

    [Test]
    public void Wraith_StatusImmunities()
    {
        var factory = CreateFactory();
        var def = factory.GetDefinition("wraith")!;

        Assert.That(def.StatusImmunities, Is.Not.Null);
        Assert.That(def.StatusImmunities, Contains.Item("confusion"));
        Assert.That(def.StatusImmunities, Contains.Item("slow"));
        Assert.That(def.StatusImmunities, Contains.Item("fear"));
    }

    // ─── Lich ────────────────────────────────────────────────────────────────

    [Test]
    public void Lich_HasLichAiComponent()
    {
        var factory = CreateFactory();
        var lich = factory.Create("lich")!;
        var lichComp = lich.Get<LichAiComponent>();

        Assert.That(lichComp, Is.Not.Null);
        Assert.That(lichComp!.SoulBoltRange, Is.EqualTo(7));
        Assert.That(lichComp.SoulBoltDamagePct, Is.EqualTo(0.18));
        Assert.That(lichComp.SoulBoltCooldownTurns, Is.EqualTo(8));
        Assert.That(lichComp.CommandTheDeadRadius, Is.EqualTo(6));
        Assert.That(lichComp.DeathSiphonRadius, Is.EqualTo(6));
    }

    [Test]
    public void Lich_HasNecromancerAiComponent()
    {
        var factory = CreateFactory();
        var lich = factory.Create("lich")!;
        var necComp = lich.Get<NecromancerAiComponent>();

        Assert.That(necComp, Is.Not.Null, "Lich must also have NecromancerAiComponent");
        Assert.That(necComp!.RaiseRange, Is.EqualTo(5));
    }

    [Test]
    public void Lich_LeavesNoCorpse()
    {
        var factory = CreateFactory();
        var def = factory.GetDefinition("lich")!;
        Assert.That(def.LeavesCorpse, Is.False);
    }

    [Test]
    public void Lich_AiTypeIsLich()
    {
        var factory = CreateFactory();
        var lich = factory.Create("lich")!;
        var ai = lich.Get<AiComponent>();

        Assert.That(ai, Is.Not.Null);
        Assert.That(ai!.AiType, Is.EqualTo("lich"));
    }

    [Test]
    public void Lich_StatusImmunities()
    {
        var factory = CreateFactory();
        var def = factory.GetDefinition("lich")!;

        Assert.That(def.StatusImmunities, Is.Not.Null);
        Assert.That(def.StatusImmunities, Contains.Item("confusion"));
        Assert.That(def.StatusImmunities, Contains.Item("slow"));
        Assert.That(def.StatusImmunities, Contains.Item("fear"));
        Assert.That(def.StatusImmunities, Contains.Item("poison"));
        Assert.That(def.StatusImmunities, Contains.Item("bleed"));
    }

    [Test]
    public void Lich_CorrectStats()
    {
        var factory = CreateFactory();
        var lich = factory.Create("lich")!;
        var f = lich.Require<Fighter>();

        Assert.That(f.BaseMaxHp, Is.EqualTo(60));
        Assert.That(f.DamageMin, Is.EqualTo(3));
        Assert.That(f.DamageMax, Is.EqualTo(6));
        Assert.That(f.Xp, Is.EqualTo(150));
    }

    // ─── Plague Zombie ───────────────────────────────────────────────────────

    [Test]
    public void PlagueZombie_ExtendsZombie()
    {
        var factory = CreateFactory();
        var def = factory.GetDefinition("plague_zombie")!;

        // Should inherit zombie's damage_resistance: piercing
        Assert.That(def.DamageResistance, Is.EqualTo("piercing"));
        Assert.That(def.DamageVulnerability, Is.EqualTo("bludgeoning"));
    }

    [Test]
    public void PlagueZombie_HasPlagueOnHit()
    {
        var factory = CreateFactory();
        var zombie = factory.Create("plague_zombie")!;
        var onHit = zombie.Get<OnHitEffectComponent>();

        Assert.That(onHit, Is.Not.Null, "Plague zombie must have OnHitEffectComponent");
        Assert.That(onHit!.EffectType, Is.EqualTo("plague"));
        Assert.That(onHit.Duration, Is.EqualTo(20));
    }

    [Test]
    public void PlagueZombie_CorrectStats()
    {
        var factory = CreateFactory();
        var zombie = factory.Create("plague_zombie")!;
        var f = zombie.Require<Fighter>();

        Assert.That(f.BaseMaxHp, Is.EqualTo(30));
        Assert.That(f.DamageMin, Is.EqualTo(4));
        Assert.That(f.DamageMax, Is.EqualTo(7));
        Assert.That(f.Xp, Is.EqualTo(45));
    }

    // ─── Ancient Troll ───────────────────────────────────────────────────────

    [Test]
    public void TrollAncient_ExtendsTroll()
    {
        var factory = CreateFactory();
        var def = factory.GetDefinition("troll_ancient")!;

        // Troll base has can_seek_items: true and inventory — ancient troll should inherit
        Assert.That(def.CanSeekItems, Is.True);
    }

    [Test]
    public void TrollAncient_Regen3PerTurn()
    {
        // After Phase 6: troll_ancient uses InnateRegenComponent (permanent, not a timed status).
        var factory = CreateFactory();
        var troll = factory.Create("troll_ancient")!;
        var innateRegen = troll.Get<InnateRegenComponent>();

        Assert.That(innateRegen, Is.Not.Null, "Ancient troll must have InnateRegenComponent");
        Assert.That(innateRegen!.HealPerTurn, Is.EqualTo(3));
    }

    [Test]
    public void TrollAncient_CorrectStats()
    {
        var factory = CreateFactory();
        var troll = factory.Create("troll_ancient")!;
        var f = troll.Require<Fighter>();

        Assert.That(f.BaseMaxHp, Is.EqualTo(50));
        Assert.That(f.Xp, Is.EqualTo(200));
    }

    // ─── Greater Slime ───────────────────────────────────────────────────────

    [Test]
    public void GreaterSlime_SplitsIntoLargeSlimes()
    {
        var factory = CreateFactory();
        var def = factory.GetDefinition("greater_slime")!;

        Assert.That(def.SplitChildType, Is.EqualTo("large_slime"));
    }

    [Test]
    public void GreaterSlime_SplitAt35Percent()
    {
        var factory = CreateFactory();
        var def = factory.GetDefinition("greater_slime")!;

        Assert.That(def.SplitTriggerHpPct, Is.EqualTo(0.35));
        Assert.That(def.SplitMinChildren, Is.EqualTo(2));
        Assert.That(def.SplitMaxChildren, Is.EqualTo(2));
    }

    [Test]
    public void GreaterSlime_HasCorrosion()
    {
        var factory = CreateFactory();
        var slime = factory.Create("greater_slime")!;
        var corrosion = slime.Get<CorrosionComponent>();

        Assert.That(corrosion, Is.Not.Null);
        Assert.That(corrosion!.Chance, Is.EqualTo(0.15));
    }

    [Test]
    public void GreaterSlime_CorrectStats()
    {
        var factory = CreateFactory();
        var slime = factory.Create("greater_slime")!;
        var f = slime.Require<Fighter>();

        Assert.That(f.BaseMaxHp, Is.EqualTo(80));
        Assert.That(f.Xp, Is.EqualTo(150));
    }
}
