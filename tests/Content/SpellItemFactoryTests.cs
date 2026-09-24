using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

/// <summary>
/// Tests for SpellItemFactory — YAML loading, entity creation, component verification.
/// </summary>
[TestFixture]
public class SpellItemFactoryTests
{
    private const string MinimalScrollYaml = """
        scrolls:
          scroll_of_lightning:
            name: "Scroll of Lightning"
            spell_id: "lightning"
            targeting: "auto_closest"
            damage: 40
            range: 5
            char: "~"
            color: [255, 255, 100]
        """;

    private const string MinimalWandYaml = """
        wands:
          wand_of_lightning:
            name: "Wand of Lightning"
            spell_id: "lightning"
            targeting: "auto_closest"
            damage: 40
            range: 5
            is_wand: true
            min_charges: 3
            max_charges: 6
            charge_cap: 10
            recharge_scroll: "lightning"
            char: "/"
            color: [255, 255, 100]
          wand_of_portals:
            name: "Wand of Portals"
            spell_id: "portal"
            targeting: "portal"
            is_wand: true
            infinite: true
            char: "/"
            color: [0, 200, 200]
        """;

    private static SpellItemFactory BuildFactory(string yaml)
    {
        var loader = new ContentLoader();
        var defs = loader.LoadSpellItems(yaml);
        var entityFactory = new EntityFactory(startId: 1);
        return new SpellItemFactory(defs, entityFactory);
    }

    // ─── Scroll Creation ─────────────────────────────────────────────────────

    [Test]
    public void CreateScroll_ValidId_ReturnsEntityWithConsumableAndSpellEffect()
    {
        var factory = BuildFactory(MinimalScrollYaml);

        var entity = factory.CreateScroll("scroll_of_lightning");

        Assert.That(entity, Is.Not.Null);
        Assert.That(entity!.Has<Consumable>(), Is.True);
        Assert.That(entity.Has<SpellEffect>(), Is.True);
    }

    [Test]
    public void CreateScroll_ValidId_ConsumableHealAmountIsZero()
    {
        var factory = BuildFactory(MinimalScrollYaml);

        var entity = factory.CreateScroll("scroll_of_lightning")!;

        Assert.That(entity.Require<Consumable>().HealAmount, Is.EqualTo(0));
    }

    [Test]
    public void CreateScroll_ValidId_SpellEffectHasCorrectSpellId()
    {
        var factory = BuildFactory(MinimalScrollYaml);

        var entity = factory.CreateScroll("scroll_of_lightning")!;

        Assert.That(entity.Require<SpellEffect>().SpellId, Is.EqualTo("lightning"));
    }

    [Test]
    public void CreateScroll_ValidId_SpellEffectHasCorrectTargeting()
    {
        var factory = BuildFactory(MinimalScrollYaml);

        var entity = factory.CreateScroll("scroll_of_lightning")!;

        Assert.That(entity.Require<SpellEffect>().Targeting, Is.EqualTo(TargetingMode.AutoClosest));
    }

    [Test]
    public void CreateScroll_UnknownId_ReturnsNull()
    {
        var factory = BuildFactory(MinimalScrollYaml);

        var entity = factory.CreateScroll("nonexistent_scroll");

        Assert.That(entity, Is.Null);
    }

    [Test]
    public void CreateScroll_WandId_ReturnsNull()
    {
        var factory = BuildFactory(MinimalScrollYaml + "\n" + MinimalWandYaml);

        // Wand IDs should not be created via CreateScroll
        var entity = factory.CreateScroll("wand_of_lightning");

        Assert.That(entity, Is.Null);
    }

    // ─── Wand Creation ───────────────────────────────────────────────────────

    [Test]
    public void CreateWand_ValidId_ReturnsEntityWithWandComponentAndSpellEffect()
    {
        var factory = BuildFactory(MinimalWandYaml);
        var rng = new SeededRandom(1337);

        var entity = factory.CreateWand("wand_of_lightning", rng);

        Assert.That(entity, Is.Not.Null);
        Assert.That(entity!.Has<WandComponent>(), Is.True);
        Assert.That(entity.Has<SpellEffect>(), Is.True);
    }

    [Test]
    public void CreateWand_ValidId_ChargesWithinMinMaxRange()
    {
        var factory = BuildFactory(MinimalWandYaml);
        var rng = new SeededRandom(1337);

        var entity = factory.CreateWand("wand_of_lightning", rng)!;
        var wand = entity.Require<WandComponent>();

        Assert.That(wand.Charges, Is.InRange(3, 6), "Charges should be between min(3) and max(6)");
    }

    [Test]
    public void CreateWand_InfiniteWand_IsInfiniteAndChargesAreZero()
    {
        var factory = BuildFactory(MinimalWandYaml);
        var rng = new SeededRandom(1337);

        var entity = factory.CreateWand("wand_of_portals", rng)!;
        var wand = entity.Require<WandComponent>();

        Assert.That(wand.Infinite, Is.True);
        Assert.That(wand.HasCharges, Is.True);
    }

    [Test]
    public void CreateWand_ValidId_RechargeScrollIdSet()
    {
        var factory = BuildFactory(MinimalWandYaml);
        var rng = new SeededRandom(1337);

        var entity = factory.CreateWand("wand_of_lightning", rng)!;
        var wand = entity.Require<WandComponent>();

        Assert.That(wand.RechargeScrollId, Is.EqualTo("lightning"));
    }

    [Test]
    public void CreateWand_UnknownId_ReturnsNull()
    {
        var factory = BuildFactory(MinimalWandYaml);
        var rng = new SeededRandom(1337);

        var entity = factory.CreateWand("nonexistent_wand", rng);

        Assert.That(entity, Is.Null);
    }

    [Test]
    public void CreateWand_ScrollId_ReturnsNull()
    {
        var factory = BuildFactory(MinimalScrollYaml + "\n" + MinimalWandYaml);
        var rng = new SeededRandom(1337);

        // Scroll IDs should not be created via CreateWand
        var entity = factory.CreateWand("scroll_of_lightning", rng);

        Assert.That(entity, Is.Null);
    }

    // ─── ContentLoader Integration ────────────────────────────────────────────

    [Test]
    public void ContentLoader_LoadSpellItems_LoadsBothScrollsAndWands()
    {
        var loader = new ContentLoader();
        string yaml = MinimalScrollYaml + "\n" + MinimalWandYaml;

        var spellItems = loader.LoadSpellItems(yaml);

        Assert.That(spellItems.ContainsKey("scroll_of_lightning"), Is.True);
        Assert.That(spellItems.ContainsKey("wand_of_lightning"), Is.True);
        Assert.That(spellItems.ContainsKey("wand_of_portals"), Is.True);
    }

    [Test]
    public void ContentLoader_LoadSpellItems_ScrollHasIsWandFalse()
    {
        var loader = new ContentLoader();

        var spellItems = loader.LoadSpellItems(MinimalScrollYaml);

        Assert.That(spellItems["scroll_of_lightning"].IsWand, Is.False);
    }

    [Test]
    public void ContentLoader_LoadSpellItems_WandHasIsWandTrue()
    {
        var loader = new ContentLoader();

        var spellItems = loader.LoadSpellItems(MinimalWandYaml);

        Assert.That(spellItems["wand_of_lightning"].IsWand, Is.True);
    }
}
