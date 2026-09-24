using System.IO;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Knowledge;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

/// <summary>
/// Ruling execution (Rafe, 2026-08-22): the wand of portals is reflavored to "Hollowmark" — CANON, the
/// bound artificer whose body is the wand and whose voice is the ribbon. Unlike Sasha's Sunder (disguised
/// as a keepsake), Hollowmark is genuinely, openly a wand — so it still reads "Wand". This is a
/// presentation-only change: the blessed name + description against the REAL config/entities.yaml, with
/// the portal traversal mechanics preserved and the entity key kept (the run-start grant depends on it).
/// </summary>
[TestFixture]
public class HollowmarkWandReflavorTests
{
    // Blessed text, verbatim (Rafe, 2026-08-22) — do not edit.
    private const string BlessedName = "Hollowmark";
    private const string BlessedDescription =
        "Pale wood, light grip, warm. It opens passages; it decides where they close. "
        + "It points better than it is pointed. Sasha has stopped arguing with it.";

    private static string EntitiesPath()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        return Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", "entities.yaml"));
    }

    private static ContentBundle Bundle() => new ContentLoader().LoadAllFromFile(EntitiesPath());

    private static Entity CreateWand(string key)
    {
        var bundle = Bundle();
        var factory = new SpellItemFactory(bundle.SpellItems, new EntityFactory(startId: 1));
        return factory.CreateWand(key, new SeededRandom(1))!;
    }

    [Test]
    public void Hollowmark_HasBlessedNameAndDescription()
    {
        var def = Bundle().SpellItems["wand_of_portals"];
        Assert.Multiple(() =>
        {
            Assert.That(def.Name, Is.EqualTo(BlessedName), "authored name from the ruling.");
            Assert.That(def.Description, Is.EqualTo(BlessedDescription), "blessed description, verbatim.");
            Assert.That(def.Description, Does.Not.Contain("Marya"), "the blessed text names no 'Marya'.");
        });
    }

    [Test]
    public void Hollowmark_ReadsAsHollowmark_StillAWand()
    {
        var item = CreateWand("wand_of_portals");
        Assert.That(item.Name, Is.EqualTo(BlessedName));

        var view = ItemInspectView.From(item);   // registry/pool null → identified view
        Assert.Multiple(() =>
        {
            Assert.That(view.Name, Is.EqualTo(BlessedName));
            Assert.That(view.Category, Is.EqualTo("Wand"),
                "Hollowmark IS the wand of portals (canon) — unlike Sasha's Sunder it does not hide its wand-ness.");
        });
    }

    [Test]
    public void Hollowmark_KeepsPortalTraversalMechanics()
    {
        var item = CreateWand("wand_of_portals");
        Assert.Multiple(() =>
        {
            Assert.That(item.Get<WandComponent>(), Is.Not.Null, "still wand-shaped (persistent use).");
            Assert.That(item.Get<WandComponent>()!.Infinite, Is.True, "load-bearing: the traversal tool never depletes.");
            Assert.That(item.Get<SpellEffect>()?.SpellId, Is.EqualTo("portal"), "portal pipeline untouched.");
        });
    }
}
