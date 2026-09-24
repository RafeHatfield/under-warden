using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for status immunity enforcement.
/// Wraith is immune to confusion/slow/fear. Lich adds poison/bleed immunity.
/// StatusImmunityComponent blocks ApplyEffect for matching effect types.
/// </summary>
[TestFixture]
public class StatusImmunityTests
{
    private static Entity CreateEntityWithImmunities(params string[] immunities)
    {
        var entity = new Entity(1, "ImmuneMob", 5, 5, blocksMovement: true);
        entity.Add(new Fighter(hp: 30));
        entity.Add(new StatusImmunityComponent(immunities));
        return entity;
    }

    // ─── Component basics ────────────────────────────────────────────────

    [Test]
    public void StatusImmunityComponent_IsImmuneTo_MatchesRegistered()
    {
        var comp = new StatusImmunityComponent(["confusion", "slow", "fear"]);
        Assert.That(comp.IsImmuneTo("confusion"), Is.True);
        Assert.That(comp.IsImmuneTo("slow"), Is.True);
        Assert.That(comp.IsImmuneTo("fear"), Is.True);
        Assert.That(comp.IsImmuneTo("poison"), Is.False);
    }

    [Test]
    public void StatusImmunityComponent_CaseInsensitive()
    {
        var comp = new StatusImmunityComponent(["confusion"]);
        Assert.That(comp.IsImmuneTo("Confusion"), Is.True);
        Assert.That(comp.IsImmuneTo("CONFUSION"), Is.True);
    }

    // ─── ApplyEffect blocked by immunity ─────────────────────────────────

    [Test]
    public void ApplyEffect_SlowImmune_BlocksSlowed()
    {
        var entity = CreateEntityWithImmunities("slow");
        var result = StatusEffectProcessor.ApplyEffect<SlowedEffect>(entity, 10);

        Assert.That(result, Is.Null, "SlowedEffect should be blocked by 'slow' immunity");
        Assert.That(entity.Get<SlowedEffect>(), Is.Null);
    }

    [Test]
    public void ApplyEffect_FearImmune_BlocksFear()
    {
        var entity = CreateEntityWithImmunities("fear");
        var result = StatusEffectProcessor.ApplyEffect<FearEffect>(entity, 10);

        Assert.That(result, Is.Null);
        Assert.That(entity.Get<FearEffect>(), Is.Null);
    }

    [Test]
    public void ApplyEffect_ConfusionImmune_BlocksDisorientation()
    {
        // "confusion" in YAML maps to DisorientationEffect in C#
        var entity = CreateEntityWithImmunities("confusion");
        var result = StatusEffectProcessor.ApplyEffect<DisorientationEffect>(entity, 10);

        Assert.That(result, Is.Null);
        Assert.That(entity.Get<DisorientationEffect>(), Is.Null);
    }

    [Test]
    public void ApplyEffect_PoisonImmune_BlocksPoison()
    {
        var entity = CreateEntityWithImmunities("poison");
        var result = StatusEffectProcessor.ApplyEffect<PoisonEffect>(entity, 10);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ApplyEffect_NoImmunity_Applies()
    {
        var entity = CreateEntityWithImmunities("slow"); // only slow immunity
        var result = StatusEffectProcessor.ApplyEffect<PoisonEffect>(entity, 10);

        Assert.That(result, Is.Not.Null, "PoisonEffect should NOT be blocked by 'slow' immunity");
        Assert.That(entity.Get<PoisonEffect>(), Is.Not.Null);
    }

    [Test]
    public void ApplyEffect_NoComponent_Applies()
    {
        var entity = new Entity(1, "NormalMob", 5, 5, blocksMovement: true);
        entity.Add(new Fighter(hp: 30));
        // No StatusImmunityComponent
        var result = StatusEffectProcessor.ApplyEffect<SlowedEffect>(entity, 10);

        Assert.That(result, Is.Not.Null);
    }

    // ─── Factory wiring ──────────────────────────────────────────────────

    [Test]
    public void Wraith_Factory_HasImmunityComponent()
    {
        var factory = CreateFactory();
        var wraith = factory.Create("wraith")!;
        var immunities = wraith.Get<StatusImmunityComponent>();

        Assert.That(immunities, Is.Not.Null, "Wraith must have StatusImmunityComponent");
        Assert.That(immunities!.IsImmuneTo("confusion"), Is.True);
        Assert.That(immunities.IsImmuneTo("slow"), Is.True);
        Assert.That(immunities.IsImmuneTo("fear"), Is.True);
        Assert.That(immunities.IsImmuneTo("poison"), Is.False);
    }

    [Test]
    public void Lich_Factory_HasImmunityComponent()
    {
        var factory = CreateFactory();
        var lich = factory.Create("lich")!;
        var immunities = lich.Get<StatusImmunityComponent>();

        Assert.That(immunities, Is.Not.Null, "Lich must have StatusImmunityComponent");
        Assert.That(immunities!.IsImmuneTo("confusion"), Is.True);
        Assert.That(immunities.IsImmuneTo("slow"), Is.True);
        Assert.That(immunities.IsImmuneTo("fear"), Is.True);
        Assert.That(immunities.IsImmuneTo("poison"), Is.True);
        Assert.That(immunities.IsImmuneTo("bleed"), Is.True);
    }

    [Test]
    public void Wraith_Factory_SlowEffectBlocked()
    {
        var factory = CreateFactory();
        var wraith = factory.Create("wraith")!;

        var result = StatusEffectProcessor.ApplyEffect<SlowedEffect>(wraith, 10);

        Assert.That(result, Is.Null, "Wraith should be immune to SlowedEffect");
        Assert.That(wraith.Get<SlowedEffect>(), Is.Null);
    }

    [Test]
    public void Orc_NoImmunity_SlowEffectApplies()
    {
        var factory = CreateFactory();
        var orc = factory.Create("orc")!;

        var result = StatusEffectProcessor.ApplyEffect<SlowedEffect>(orc, 10);

        Assert.That(result, Is.Not.Null, "Orc should NOT be immune to SlowedEffect");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

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
}
