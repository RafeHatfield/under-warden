using System.IO;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Knowledge;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

/// <summary>
/// Ruling execution (Rafe, 2026-07-21): the spell-break wand is reflavored to "Sasha's Sunder", an
/// innocuous keepsake — NOT a named wand — while its wand-shaped mechanics (persistent dispel) stay
/// intact. These tests lock the reflavor against the REAL config/entities.yaml: authored name, no
/// player-facing "wand" string, mechanics preserved, and the reflavor scoped to this one item.
/// </summary>
[TestFixture]
public class SashaSunderReflavorTests
{
    private static string EntitiesPath()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        return Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", "entities.yaml"));
    }

    private static Entity CreateWand(string key)
    {
        var bundle = new ContentLoader().LoadAllFromFile(EntitiesPath());
        var factory = new SpellItemFactory(bundle.SpellItems, new EntityFactory(startId: 1));
        return factory.CreateWand(key, new SeededRandom(1))!;
    }

    [Test]
    public void SashasSunder_IsReflavored_WithNoPlayerFacingWandString()
    {
        var item = CreateWand("wand_of_spell_break");
        Assert.That(item.Name, Is.EqualTo("Sasha's Sunder"), "authored name from the ruling.");

        var view = ItemInspectView.From(item);   // registry/pool null → identified view
        Assert.Multiple(() =>
        {
            Assert.That(view.Name, Is.EqualTo("Sasha's Sunder"));
            Assert.That(view.Category, Is.EqualTo("Keepsake"), "the type label must NOT read 'Wand'.");
            foreach (var line in view.StatLines)
                Assert.That(line.ToLowerInvariant(), Does.Not.Contain("wand"),
                    "no player-facing inspect line may call it a wand.");
        });
    }

    [Test]
    public void SashasSunder_KeepsWandShapedDispelMechanics()
    {
        var item = CreateWand("wand_of_spell_break");
        Assert.Multiple(() =>
        {
            Assert.That(item.Get<WandComponent>(), Is.Not.Null, "mechanics stay wand-shaped (persistent use).");
            Assert.That(item.Get<WandComponent>()!.Infinite, Is.True, "load-bearing: never depletes.");
            Assert.That(item.Get<SpellEffect>()?.SpellId, Is.EqualTo("dispel"), "dispel pipeline untouched.");
        });
    }

    [Test]
    public void Reflavor_IsScoped_OtherWandsStillReadAsWand()
    {
        Assert.That(ItemInspectView.From(CreateWand("wand_of_portals")).Category, Is.EqualTo("Wand"),
            "the reflavor must be scoped to Sasha's Sunder — every other wand still reads 'Wand'.");
    }
}
