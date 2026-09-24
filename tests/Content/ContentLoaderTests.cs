using UnderWarden.Logic.Content;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

[TestFixture]
public class ContentLoaderTests
{
    private const string TestYaml = @"
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
    ai_type: ""basic""
    blocks: true
    faction: ""orc""
    tags: [""humanoid"", ""living""]
    etp_base: 27

  orc_grunt:
    name: ""Orc""
    extends: orc

  orc_brute:
    extends: orc
    stats:
      hp: 42
      damage_min: 5
      damage_max: 8
      strength: 16
      xp: 55
    char: ""O""
    color: [90, 160, 90]
    etp_base: 45
    tags: [""humanoid"", ""living"", ""brute""]

  zombie:
    stats:
      hp: 24
      xp: 30
      damage_min: 3
      damage_max: 6
      strength: 12
      dexterity: 6
      constitution: 14
      accuracy: 1
      evasion: 0
    char: ""z""
    color: [128, 128, 96]
    blocks: true
    faction: ""undead""
    tags: [""undead"", ""zombie""]
    damage_resistance: ""piercing""
    etp_base: 31
";

    private Dictionary<string, MonsterDefinition> _monsters = null!;

    [OneTimeSetUp]
    public void LoadMonsters()
    {
        var loader = new ContentLoader();
        _monsters = loader.LoadMonsters(TestYaml);
    }

    [Test]
    public void LoadsAllMonsters()
    {
        Assert.That(_monsters, Has.Count.EqualTo(4));
        Assert.That(_monsters.ContainsKey("orc"), Is.True);
        Assert.That(_monsters.ContainsKey("orc_grunt"), Is.True);
        Assert.That(_monsters.ContainsKey("orc_brute"), Is.True);
        Assert.That(_monsters.ContainsKey("zombie"), Is.True);
    }

    [Test]
    public void BaseOrc_Stats()
    {
        var orc = _monsters["orc"];
        Assert.That(orc.Stats!.Hp, Is.EqualTo(28));
        Assert.That(orc.Stats.Strength, Is.EqualTo(14));
        Assert.That(orc.Stats.DamageMin, Is.EqualTo(4));
        Assert.That(orc.Stats.DamageMax, Is.EqualTo(6));
        Assert.That(orc.Stats.Accuracy, Is.EqualTo(4));
        Assert.That(orc.Stats.Evasion, Is.EqualTo(1));
        Assert.That(orc.Stats.Xp, Is.EqualTo(35));
    }

    [Test]
    public void BaseOrc_Fields()
    {
        var orc = _monsters["orc"];
        Assert.That(orc.Char, Is.EqualTo("o"));
        Assert.That(orc.Color, Is.EqualTo(new[] { 63, 127, 63 }));
        Assert.That(orc.Faction, Is.EqualTo("orc"));
        Assert.That(orc.EtpBase, Is.EqualTo(27));
        Assert.That(orc.Tags, Contains.Item("humanoid"));
    }

    [Test]
    public void OrcGrunt_InheritsFromOrc()
    {
        var grunt = _monsters["orc_grunt"];

        // Explicit name
        Assert.That(grunt.Name, Is.EqualTo("Orc"));

        // Inherited stats
        Assert.That(grunt.Stats!.Hp, Is.EqualTo(28));
        Assert.That(grunt.Stats.Strength, Is.EqualTo(14));
        Assert.That(grunt.Stats.Accuracy, Is.EqualTo(4));

        // Inherited fields
        Assert.That(grunt.Char, Is.EqualTo("o"));
        Assert.That(grunt.Faction, Is.EqualTo("orc"));
        Assert.That(grunt.EtpBase, Is.EqualTo(27));

        // Inheritance resolved
        Assert.That(grunt.Extends, Is.Null);
    }

    [Test]
    public void OrcBrute_OverridesParentStats()
    {
        var brute = _monsters["orc_brute"];

        // Overridden stats
        Assert.That(brute.Stats!.Hp, Is.EqualTo(42));
        Assert.That(brute.Stats.DamageMin, Is.EqualTo(5));
        Assert.That(brute.Stats.DamageMax, Is.EqualTo(8));
        Assert.That(brute.Stats.Strength, Is.EqualTo(16));
        Assert.That(brute.Stats.Xp, Is.EqualTo(55));

        // Inherited from parent (not overridden)
        Assert.That(brute.Stats.Constitution, Is.EqualTo(12));
        Assert.That(brute.Stats.Accuracy, Is.EqualTo(4));
        Assert.That(brute.Stats.Evasion, Is.EqualTo(1));

        // Overridden fields
        Assert.That(brute.Char, Is.EqualTo("O"));
        Assert.That(brute.EtpBase, Is.EqualTo(45));

        // Inherited faction
        Assert.That(brute.Faction, Is.EqualTo("orc"));
    }

    [Test]
    public void Zombie_IndependentDefinition()
    {
        var zombie = _monsters["zombie"];

        Assert.That(zombie.Stats!.Hp, Is.EqualTo(24));
        Assert.That(zombie.Stats.Dexterity, Is.EqualTo(6));
        Assert.That(zombie.Stats.Constitution, Is.EqualTo(14));
        Assert.That(zombie.Stats.Accuracy, Is.EqualTo(1));
        Assert.That(zombie.Stats.Evasion, Is.EqualTo(0));
        Assert.That(zombie.Faction, Is.EqualTo("undead"));
        Assert.That(zombie.DamageResistance, Is.EqualTo("piercing"));
        Assert.That(zombie.Tags, Contains.Item("zombie"));
    }

    [Test]
    public void Name_DefaultsToTitleCasedId()
    {
        var brute = _monsters["orc_brute"];
        Assert.That(brute.Name, Is.EqualTo("Orc Brute"));
    }

    [Test]
    public void CircularInheritance_Throws()
    {
        const string circular = @"
monsters:
  a:
    extends: b
    stats:
      hp: 10
    char: ""a""
  b:
    extends: a
    stats:
      hp: 20
    char: ""b""
";
        var loader = new ContentLoader();
        Assert.Throws<InvalidOperationException>(() => loader.LoadMonsters(circular));
    }

    [Test]
    public void EmptyYaml_ReturnsEmpty()
    {
        var loader = new ContentLoader();
        var result = loader.LoadMonsters("monsters: {}");
        Assert.That(result, Is.Empty);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// Potion ContentLoader / ConsumableFactory tests (TASK-001 + TASK-004/006/008)
// ────────────────────────────────────────────────────────────────────────────────

[TestFixture]
public class ConsumableFactoryPotionTests
{
    private const string PotionYaml = """
        consumables:
          healing_potion:
            name: "Healing Potion"
            heal_amount: 40
            char: "!"
            color: [127, 0, 255]

          potion_of_speed:
            name: "Potion of Speed"
            is_potion: true
            spell_id: "haste"
            targeting: "self"
            duration: 20
            char: "!"
            color: [255, 220, 50]

          potion_of_weakness:
            name: "Potion of Weakness"
            is_potion: true
            spell_id: "drink_weakness"
            targeting: "self"
            duration: 30
            throw_spell_id: "throw_weakness"
            range: 10
            char: "!"
            color: [180, 100, 180]

          fire_potion:
            name: "Fire Potion"
            is_potion: true
            spell_id: "throw_fire"
            targeting: "single_target"
            duration: 4
            range: 10
            char: "!"
            color: [255, 80, 0]
        """;

    private Dictionary<string, UnderWarden.Logic.Content.ConsumableDefinition> _defs = null!;
    private UnderWarden.Logic.Content.ConsumableFactory _factory = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var loader = new UnderWarden.Logic.Content.ContentLoader();
        _defs = loader.LoadConsumables(PotionYaml);
        _factory = new UnderWarden.Logic.Content.ConsumableFactory(
            _defs, new UnderWarden.Logic.ECS.EntityFactory());
    }

    [Test]
    public void ContentLoader_LoadConsumables_IncludesPotions()
    {
        Assert.That(_defs.ContainsKey("potion_of_speed"), Is.True);
        Assert.That(_defs.ContainsKey("potion_of_weakness"), Is.True);
        Assert.That(_defs.ContainsKey("fire_potion"), Is.True);
    }

    [Test]
    public void ContentLoader_BuffPotion_HasIsPotion_True()
    {
        Assert.That(_defs["potion_of_speed"].IsPotion, Is.True, "is_potion: true should be deserialized.");
    }

    [Test]
    public void ContentLoader_HealingPotion_IsPotion_DefaultFalse()
    {
        // healing_potion uses the legacy heal_amount path — is_potion defaults to false.
        Assert.That(_defs["healing_potion"].IsPotion, Is.False,
            "healing_potion should not have is_potion: true.");
    }

    [Test]
    public void ConsumableFactory_CreatesSpellEffect_WhenSpellIdPresent()
    {
        var entity = _factory.Create("potion_of_speed")!;
        var spell = entity.Get<UnderWarden.Logic.Combat.SpellEffect>();
        Assert.That(spell, Is.Not.Null, "SpellEffect should be created when spell_id is present.");
        Assert.That(spell!.SpellId, Is.EqualTo("haste"));
    }

    [Test]
    public void ConsumableFactory_SetsIsPotion_FromDefinition()
    {
        var entity = _factory.Create("potion_of_speed")!;
        var consumable = entity.Get<UnderWarden.Logic.Combat.Consumable>();
        Assert.That(consumable, Is.Not.Null);
        Assert.That(consumable!.IsPotion, Is.True, "Consumable.IsPotion should be true for potions.");
    }

    [Test]
    public void ConsumableFactory_SetsThrowSpellId_WhenPresent()
    {
        var entity = _factory.Create("potion_of_weakness")!;
        var spell = entity.Get<UnderWarden.Logic.Combat.SpellEffect>();
        Assert.That(spell, Is.Not.Null);
        Assert.That(spell!.ThrowSpellId, Is.EqualTo("throw_weakness"),
            "SpellEffect.ThrowSpellId should be set from throw_spell_id YAML field.");
    }

    [Test]
    public void ConsumableFactory_NoSpellEffect_WhenNoSpellId()
    {
        // healing_potion has no spell_id — no SpellEffect should be created.
        var entity = _factory.Create("healing_potion")!;
        Assert.That(entity.Get<UnderWarden.Logic.Combat.SpellEffect>(), Is.Null,
            "healing_potion should NOT have SpellEffect — it uses the legacy heal path.");
    }

    [Test]
    public void ConsumableFactory_FirePotion_HasSingleTargetMode()
    {
        var entity = _factory.Create("fire_potion")!;
        var spell = entity.Get<UnderWarden.Logic.Combat.SpellEffect>();
        Assert.That(spell, Is.Not.Null);
        Assert.That(spell!.Targeting, Is.EqualTo(UnderWarden.Logic.Combat.TargetingMode.SingleTarget),
            "Fire potion is throw-only and should have SingleTarget targeting.");
        Assert.That(spell.ThrowSpellId, Is.Null,
            "Fire potion is a direct single_target spell, not a throw_spell_id bifurcation.");
    }

    [Test]
    public void ContentLoader_EntitiesYaml_LoadsAllPotions()
    {
        // Round-trip test against the actual entities.yaml to verify all 14 potion definitions load.
        // Use LoadAll (not LoadConsumables directly) — LoadAll strips floor_item_pool sequences first.
        // Path pattern: TestContext.CurrentContext.TestDirectory (same as Wave2MonsterTests).
        var testDir = NUnit.Framework.TestContext.CurrentContext.TestDirectory;
        var entitiesPath = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(testDir, "..", "..", "..", "..", "config", "entities.yaml"));
        if (!System.IO.File.Exists(entitiesPath))
            entitiesPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(testDir, "config", "entities.yaml"));

        var loader = new UnderWarden.Logic.Content.ContentLoader();
        var bundle = loader.LoadAllFromFile(entitiesPath);
        var defs = bundle.Consumables;

        // Buff potions
        Assert.That(defs.ContainsKey("potion_of_speed"), Is.True, "Missing potion_of_speed");
        Assert.That(defs.ContainsKey("potion_of_protection"), Is.True, "Missing potion_of_protection");
        Assert.That(defs.ContainsKey("potion_of_regeneration"), Is.True, "Missing potion_of_regeneration");
        Assert.That(defs.ContainsKey("potion_of_invisibility"), Is.True, "Missing potion_of_invisibility");
        Assert.That(defs.ContainsKey("potion_of_heroism"), Is.True, "Missing potion_of_heroism");
        // Debuff potions
        Assert.That(defs.ContainsKey("potion_of_weakness"), Is.True, "Missing potion_of_weakness");
        Assert.That(defs.ContainsKey("potion_of_slowness"), Is.True, "Missing potion_of_slowness");
        Assert.That(defs.ContainsKey("potion_of_blindness"), Is.True, "Missing potion_of_blindness");
        Assert.That(defs.ContainsKey("potion_of_paralysis"), Is.True, "Missing potion_of_paralysis");
        Assert.That(defs.ContainsKey("tar_potion"), Is.True, "Missing tar_potion");
        // Special potions
        Assert.That(defs.ContainsKey("root_potion"), Is.True, "Missing root_potion");
        Assert.That(defs.ContainsKey("sunburst_potion"), Is.True, "Missing sunburst_potion");
        Assert.That(defs.ContainsKey("fire_potion"), Is.True, "Missing fire_potion");
        Assert.That(defs.ContainsKey("antidote_potion"), Is.True, "Missing antidote_potion");
    }
}
