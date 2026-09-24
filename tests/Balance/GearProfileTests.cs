using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Step 8 staged-start: gear-profile loading + the geared starting player DungeonFloorBuilder builds
/// from it. These pin that --gear actually equips the profile's gear and applies its stat bumps — the
/// thing that lets a region be soaked at its own power level instead of grinding up from floor 1.
/// </summary>
[TestFixture]
public class GearProfileTests
{
    private const string Yaml = """
        profiles:
          b1:
            main_hand: dagger
            chest: leather_armor
            healing_potions: 3
          b3:
            main_hand: shortsword
            chest: chain_mail
            healing_potions: 4
            bonus_hp: 26
            strength: 16
            constitution: 15
        """;

    [Test]
    public void Loader_ParsesProfiles_AndStampsNameFromKey()
    {
        var profiles = GearProfileLoader.FromYaml(Yaml);
        Assert.That(profiles.Keys, Is.EquivalentTo(new[] { "b1", "b3" }));

        var b1 = profiles["b1"];
        Assert.That(b1.Name, Is.EqualTo("b1"));
        Assert.That(b1.MainHand, Is.EqualTo("dagger"));
        Assert.That(b1.HealingPotions, Is.EqualTo(3));
        Assert.That(b1.BonusHp, Is.EqualTo(0));

        var b3 = profiles["b3"];
        Assert.That(b3.Chest, Is.EqualTo("chain_mail"));
        Assert.That(b3.BonusHp, Is.EqualTo(26));
        Assert.That(b3.Strength, Is.EqualTo(16));
        Assert.That(b3.Constitution, Is.EqualTo(15));
        Assert.That(b3.Dexterity, Is.Null, "unset stat stays null (keeps the default)");
    }

    [Test]
    public void ShippedConfig_Loads_AllRegionsB1ThroughB5()
    {
        var path = FindConfig();
        var profiles = GearProfileLoader.FromFile(path);
        Assert.That(profiles.Keys, Is.SupersetOf(new[] { "b1", "b2", "b3", "b4", "b5" }));
        Assert.That(profiles["b1"].MainHand, Is.EqualTo("dagger"), "b1 == the floor-1 default loadout");
    }

    [Test]
    public void CreateGearedPlayer_EquipsProfileGear_AndAppliesBonusHpAndPotions()
    {
        var builder = CreateFloorBuilder();
        var profile = new GearProfile
        {
            Name = "test", MainHand = "battleaxe", Chest = "scale_mail",
            HealingPotions = 4, BonusHp = 40, Strength = 18,
        };

        var player = builder.CreateGearedPlayer(profile);
        var equipment = player.Get<Equipment>()!;

        Assert.That(equipment.MainHand?.Get<ItemTag>()?.TypeId, Is.EqualTo("battleaxe"));
        Assert.That(equipment.Chest?.Get<ItemTag>()?.TypeId, Is.EqualTo("scale_mail"));

        var fighter = player.Get<Fighter>()!;
        Assert.That(fighter.Strength, Is.EqualTo(18));

        // bonus_hp adds exactly its amount on top of base + CON modifier — assert the DELTA vs an
        // otherwise-identical zero-bonus profile, so the assertion doesn't couple to the CON→HP formula.
        var noBonus = builder.CreateGearedPlayer(new GearProfile
        {
            Name = "test0", MainHand = "battleaxe", Chest = "scale_mail",
            HealingPotions = 4, BonusHp = 0, Strength = 18,
        });
        Assert.That(fighter.MaxHp - noBonus.Get<Fighter>()!.MaxHp, Is.EqualTo(40), "bonus_hp 40 → +40 MaxHp");

        // Healing potions stack into one inventory entry — sum StackSize, don't count items.
        int potions = player.Get<Inventory>()!.Items
            .Where(i => i.Get<Consumable>()?.IsHealing == true)
            .Sum(i => i.Get<Consumable>()!.StackSize);
        Assert.That(potions, Is.EqualTo(4));
    }

    [Test]
    public void CreateGearedPlayer_UnsetStats_KeepDefault14()
    {
        var builder = CreateFloorBuilder();
        var player = builder.CreateGearedPlayer(new GearProfile { Name = "bare", MainHand = "dagger" });
        var fighter = player.Get<Fighter>()!;
        Assert.That(fighter.Strength, Is.EqualTo(14));
        Assert.That(fighter.Dexterity, Is.EqualTo(14));
        Assert.That(fighter.Constitution, Is.EqualTo(14));
    }

    // ── helpers ─────────────────────────────────────────────────────────────
    private static string FindConfig() => FindUnder("config", "balance", "gear_profiles.yaml");
    private static string EntitiesYaml() => FindUnder("config", "entities.yaml");
    private static string LevelTemplatesYaml() => FindUnder("config", "level_templates.yaml");

    private static string FindUnder(params string[] parts)
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var rel = Path.Combine(parts);
        var path = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", rel));
        if (File.Exists(path)) return path;
        path = Path.GetFullPath(Path.Combine(testDir, rel));
        if (File.Exists(path)) return path;
        throw new FileNotFoundException($"{rel} not found. Tried: {path}");
    }

    private static DungeonFloorBuilder CreateFloorBuilder()
    {
        var loader = new ContentLoader();
        var content = loader.LoadAllFromFile(EntitiesYaml());
        var entityFactory = new EntityFactory();
        var itemFactory = new ItemFactory(content.Items, entityFactory);
        var monsterFactory = new MonsterFactory(content.Monsters, entityFactory, itemFactory);
        var consumableFactory = new ConsumableFactory(content.Consumables, entityFactory);
        var templates = LevelTemplateRegistry.FromFile(LevelTemplatesYaml());
        return new DungeonFloorBuilder(templates, monsterFactory, itemFactory, consumableFactory);
    }
}
