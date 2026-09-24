using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Knowledge;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for ItemInspectView.From — verifies correct stat lines for each item category.
/// </summary>
[TestFixture]
public class ItemInspectTests
{
    private static Entity MakeEntity(int id, string name) => new(id, name);

    [Test]
    public void Weapon_ShowsDamageRange()
    {
        var item = MakeEntity(1, "Short Sword");
        item.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 4, DamageMax = 8 });

        var view = ItemInspectView.From(item);

        Assert.That(view.Category, Is.EqualTo("Weapon"));
        Assert.That(view.StatLines, Has.Some.Contains("4-8"));
    }

    [Test]
    public void Weapon_ShowsAccuracyBonus()
    {
        var item = MakeEntity(2, "Enchanted Sword +2");
        item.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 4, DamageMax = 8, ToHitBonus = 2 });

        var view = ItemInspectView.From(item);

        Assert.That(view.StatLines, Has.Some.Contains("Accuracy: +2"));
    }

    [Test]
    public void Armor_ShowsAcBonus()
    {
        var item = MakeEntity(3, "Leather Armor");
        item.Add(new Equippable(EquipmentSlot.Chest) { ArmorClassBonus = 3 });

        var view = ItemInspectView.From(item);

        // Chest slot → Armor category
        Assert.That(view.Category, Is.EqualTo("Armor"));
        Assert.That(view.StatLines, Has.Some.Contains("AC Bonus: +3"));
    }

    [Test]
    public void Wand_ShowsChargesAndSpell()
    {
        var item = MakeEntity(4, "Wand of Lightning");
        item.Add(new SpellEffect { SpellId = "lightning", Targeting = TargetingMode.AutoClosest });
        item.Add(new WandComponent { Charges = 3, MaxCharges = 10 });

        var view = ItemInspectView.From(item);

        Assert.That(view.Category, Is.EqualTo("Wand"));
        Assert.That(view.StatLines, Has.Some.Contains("Spell:").And.Some.Contains("Lightning"));
        Assert.That(view.StatLines, Has.Some.Contains("3/10"));
    }

    [Test]
    public void Wand_InfiniteCharges_ShowsInfinitySymbol()
    {
        var item = MakeEntity(5, "Wand of Portals");
        item.Add(new SpellEffect { SpellId = "portal", Targeting = TargetingMode.Portal });
        item.Add(new WandComponent { Infinite = true, MaxCharges = 10 });

        var view = ItemInspectView.From(item);

        Assert.That(view.StatLines, Has.Some.Contains("\u221e"),
            "Infinite wand should show infinity symbol for charges");
    }

    [Test]
    public void Scroll_ShowsSpellName()
    {
        var item = MakeEntity(6, "Scroll of Magic Mapping");
        item.Add(new SpellEffect { SpellId = "magic_mapping", Targeting = TargetingMode.Self });
        item.Add(new Consumable(healAmount: 0) { StackSize = 1 });

        var view = ItemInspectView.From(item);

        Assert.That(view.Category, Is.EqualTo("Scroll"));
        Assert.That(view.StatLines, Has.Some.Contains("Magic Mapping"));
    }

    [Test]
    public void Potion_ShowsHealAmount()
    {
        var item = MakeEntity(7, "Healing Potion");
        item.Add(new Consumable(healAmount: 25) { StackSize = 1 });

        var view = ItemInspectView.From(item);

        Assert.That(view.Category, Is.EqualTo("Potion"));
        Assert.That(view.StatLines, Has.Some.Contains("25 HP"));
    }

    // ─── Identification gating ──────────────────────────────────────────────────
    // Potions/scrolls/wands/rings must not leak their true name or effect until
    // identified — the popup would otherwise spoil the identification system.

    private static Entity MakePotion(string typeId = "healing_potion", int healAmount = 40)
    {
        var e = MakeEntity(1, "Healing Potion");
        e.Add(new Consumable(healAmount: healAmount) { IsPotion = true });
        e.Add(new ItemTag(typeId));
        e.Add(new IdentifiableItem { IdentifiedName = "Healing Potion" });
        return e;
    }

    private static Entity MakeWand(string typeId = "wand_of_lightning")
    {
        var e = MakeEntity(2, "Wand of Lightning");
        e.Add(new SpellEffect { SpellId = "lightning", Targeting = TargetingMode.AutoClosest });
        e.Add(new WandComponent { Charges = 3, MaxCharges = 10 });
        e.Add(new ItemTag(typeId));
        e.Add(new IdentifiableItem { IdentifiedName = "Wand of Lightning" });
        return e;
    }

    private static Entity MakeScroll(string typeId = "scroll_of_magic_mapping")
    {
        var e = MakeEntity(3, "Scroll of Magic Mapping");
        e.Add(new SpellEffect { SpellId = "magic_mapping", Targeting = TargetingMode.Self });
        e.Add(new Consumable(healAmount: 0) { StackSize = 1 });
        e.Add(new ItemTag(typeId));
        e.Add(new IdentifiableItem { IdentifiedName = "Scroll of Magic Mapping" });
        return e;
    }

    private static Entity MakeRing(string typeId = "ring_of_protection")
    {
        var e = MakeEntity(4, "Ring of Protection");
        e.Add(new Equippable(EquipmentSlot.LeftRing) { ArmorClassBonus = 3 });
        e.Add(new ItemTag(typeId));
        e.Add(new IdentifiableItem { IdentifiedName = "Ring of Protection" });
        return e;
    }

    [Test]
    public void UnidentifiedPotion_ShowsMysteryName_NotTrueStats()
    {
        var pool     = new AppearancePool([("healing_potion", ItemCategory.Potion)], seed: 42);
        var registry = new IdentificationRegistry();
        registry.MarkUnidentified("healing_potion");

        var view = ItemInspectView.From(MakePotion(), registry, pool);

        Assert.That(view.Name, Is.EqualTo(pool.GetDisplayName("healing_potion")));
        Assert.That(view.Name, Is.Not.EqualTo("Healing Potion"));
        Assert.That(view.Category, Is.EqualTo("Potion"), "Broad category is not a spoiler and stays visible");
        Assert.That(view.StatLines, Has.None.Contains("40"),
            "Heal amount must not leak before identification");
        Assert.That(view.StatLines, Has.Some.Contains("Unidentified"));
    }

    [Test]
    public void UnidentifiedWand_ShowsMysteryName_NotTrueStats()
    {
        var pool     = new AppearancePool([("wand_of_lightning", ItemCategory.Wand)], seed: 42);
        var registry = new IdentificationRegistry();
        registry.MarkUnidentified("wand_of_lightning");

        var view = ItemInspectView.From(MakeWand(), registry, pool);

        Assert.That(view.Name, Is.EqualTo(pool.GetDisplayName("wand_of_lightning")));
        Assert.That(view.Category, Is.EqualTo("Wand"));
        Assert.That(view.StatLines, Has.None.Contains("Lightning").And.None.Contains("3/10"),
            "Spell and charges must not leak before identification");
        Assert.That(view.StatLines, Has.Some.Contains("Unidentified"));
    }

    [Test]
    public void UnidentifiedScroll_ShowsMysteryName_NotTrueSpell()
    {
        var pool     = new AppearancePool([("scroll_of_magic_mapping", ItemCategory.Scroll)], seed: 42);
        var registry = new IdentificationRegistry();
        registry.MarkUnidentified("scroll_of_magic_mapping");

        var view = ItemInspectView.From(MakeScroll(), registry, pool);

        Assert.That(view.Name, Is.EqualTo(pool.GetDisplayName("scroll_of_magic_mapping")));
        Assert.That(view.Category, Is.EqualTo("Scroll"));
        Assert.That(view.StatLines, Has.None.Contains("Magic Mapping"),
            "Spell name must not leak before identification");
        Assert.That(view.StatLines, Has.Some.Contains("Unidentified"));
    }

    [Test]
    public void UnidentifiedRing_ShowsMysteryName_NotTrueAcBonus()
    {
        var pool     = new AppearancePool([("ring_of_protection", ItemCategory.Ring)], seed: 42);
        var registry = new IdentificationRegistry();
        registry.MarkUnidentified("ring_of_protection");

        var view = ItemInspectView.From(MakeRing(), registry, pool);

        Assert.That(view.Name, Is.EqualTo(pool.GetDisplayName("ring_of_protection")));
        Assert.That(view.Category, Is.EqualTo("Ring"));
        Assert.That(view.StatLines, Has.None.Contains("AC Bonus"),
            "Rings are identifiable too — AC bonus must not leak before identification");
        Assert.That(view.StatLines, Has.Some.Contains("Unidentified"));
    }

    [Test]
    public void IdentifiedPotion_ShowsTrueNameAndStats()
    {
        var pool     = new AppearancePool([("healing_potion", ItemCategory.Potion)], seed: 42);
        var registry = new IdentificationRegistry();
        registry.Identify("healing_potion");

        var view = ItemInspectView.From(MakePotion(), registry, pool);

        Assert.That(view.Name, Is.EqualTo("Healing Potion"));
        Assert.That(view.StatLines, Has.Some.Contains("40 HP"));
    }

    [Test]
    public void NullRegistry_AlwaysShowsTrueStats_ScenarioHarnessMode()
    {
        // Harness/scenario mode passes no registry — must behave exactly like before this fix.
        var view = ItemInspectView.From(MakePotion());

        Assert.That(view.Name, Is.EqualTo("Healing Potion"));
        Assert.That(view.StatLines, Has.Some.Contains("40 HP"));
    }

    [Test]
    public void Weapon_HasNoIdentifiableItem_AlwaysShowsTrueStats_RegardlessOfRegistry()
    {
        // Weapons/plain armor are always identified per docs/systems/LOOT_AND_IDENTIFICATION.md —
        // an "unidentified" registry state must have no effect on them.
        var registry = new IdentificationRegistry(); // nothing identified

        var item = MakeEntity(5, "Short Sword");
        item.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 4, DamageMax = 8 });
        item.Add(new ItemTag("short_sword"));
        // No IdentifiableItem component — matches how ItemFactory builds weapons.

        var view = ItemInspectView.From(item, registry, pool: null);

        Assert.That(view.Name, Is.EqualTo("Short Sword"));
        Assert.That(view.Category, Is.EqualTo("Weapon"));
        Assert.That(view.StatLines, Has.Some.Contains("4-8"));
    }
}
