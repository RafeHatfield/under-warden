using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.Persistence.Namespaces;
using NUnit.Framework;
using System.IO;
using System.Reflection;

namespace UnderWarden.Tests.Possession;

/// <summary>
/// Phase 6 tests: VoiceLineRegistry + CatalogEntryRenderer.
/// </summary>
[TestFixture]
public class VoiceLineTests
{
    // ─── VoiceLineRegistry ────────────────────────────────────────────────────

    private static VoiceLineRegistry MakeRegistry(string yaml)
        => VoiceLineRegistry.LoadFromYaml(yaml, new AotObjectFactory());

    [Test]
    public void Registry_LoadsPool_ReturnsLine()
    {
        var registry = MakeRegistry("""
            oil_slick_fire:
              - "line one"
              - "line two"
            """);
        var rng = new SeededRandom(1337);
        var line = registry.GetLine("oil_slick_fire", rng);
        Assert.That(line, Is.EqualTo("line one").Or.EqualTo("line two"));
    }

    [Test]
    public void Registry_UnknownTrigger_ReturnsNull()
    {
        var registry = MakeRegistry("""
            oil_slick_fire:
              - "line one"
            """);
        var rng = new SeededRandom(1337);
        Assert.That(registry.GetLine("does_not_exist", rng), Is.Null);
    }

    [Test]
    public void Registry_CompoundKeyFallback_ShortestMatchWins()
    {
        // "a.b" exists, "a.b.c" does not — should fallback to "a.b"
        var registry = MakeRegistry("""
            a.b:
              - "base line"
            """);
        var rng = new SeededRandom(1337);
        Assert.That(registry.GetLine("a.b.c", rng), Is.EqualTo("base line"));
    }

    [Test]
    public void Registry_CompoundKeyExact_TakesPriorityOverFallback()
    {
        var registry = MakeRegistry("""
            a.b:
              - "base line"
            a.b.c:
              - "specific line"
            """);
        var rng = new SeededRandom(1337);
        Assert.That(registry.GetLine("a.b.c", rng), Is.EqualTo("specific line"));
    }

    [Test]
    public void Registry_FirstFireSemantics_CanonicalLineFiresFirst()
    {
        var registry = MakeRegistry("""
            my_trigger:
              - "canonical line"
              - "alternate line"
            """);
        var rng = new SeededRandom(1337);
        var fired = new HashSet<string>();

        var first = registry.GetLine("my_trigger", rng, fired);
        Assert.That(first, Is.EqualTo("canonical line"), "First fire should return pool[0].");
        Assert.That(fired, Contains.Item("my_trigger"), "Trigger added to fired set.");
    }

    [Test]
    public void Registry_FirstFireSemantics_SubsequentCallsRotate()
    {
        var registry = MakeRegistry("""
            my_trigger:
              - "canonical line"
              - "alternate a"
              - "alternate b"
            """);
        var fired = new HashSet<string> { "my_trigger" }; // already fired once
        var rng = new SeededRandom(42);

        // With trigger already in firedSet, should pick from full pool randomly.
        // Run multiple times to confirm it doesn't always return canonical.
        var lines = Enumerable.Range(0, 20)
            .Select(_ => registry.GetLine("my_trigger", new SeededRandom(rng.Next(10000)), fired))
            .ToHashSet();
        Assert.That(lines.Count, Is.GreaterThan(1), "Multiple calls with firedSet set should vary.");
    }

    [Test]
    public void Registry_SingleLine_AlwaysReturnsIt()
    {
        var registry = MakeRegistry("""
            sole_trigger:
              - "only line"
            """);
        var rng = new SeededRandom(1337);
        Assert.That(registry.GetLine("sole_trigger", rng), Is.EqualTo("only line"));
    }

    [Test]
    public void Registry_HasTrigger_ReturnsTrueForKnownKey()
    {
        var registry = MakeRegistry("""
            oil_slick_fire:
              - "line"
            """);
        Assert.That(registry.HasTrigger("oil_slick_fire"), Is.True);
        Assert.That(registry.HasTrigger("oil_slick_fire.extra"), Is.True, "Compound key fallback.");
        Assert.That(registry.HasTrigger("unknown"), Is.False);
    }

    [Test]
    public void Registry_Merge_LaterPoolsWin()
    {
        var a = MakeRegistry("""
            key:
              - "from A"
            """);
        var b = MakeRegistry("""
            key:
              - "from B"
            """);
        a.Merge(b);
        var line = a.GetLine("key", new SeededRandom(1337));
        Assert.That(line, Is.EqualTo("from B"));
    }
}

/// <summary>
/// Phase 6 tests: CatalogEntryRenderer category selection and slot filling.
/// </summary>
[TestFixture]
public class CatalogEntryRendererTests
{
    private static PastSashasData MakeData(params PastSashaRecord[] records)
    {
        var data = new PastSashasData();
        foreach (var r in records)
            data.Records.Add(r);
        return data;
    }

    private static PastSashaRecord MakeRecord(int id, int floor, string cause = "monster",
        int bestFloor = 0, bool clean = false, bool killerFirst = false,
        bool hasNotableGear = false, string? killerSpecies = null)
    {
        var rec = new PastSashaRecord
        {
            Id = id,
            DiedRun = id,
            DiedFloor = floor,
            CauseOfDeath = cause,
            KillerSpecies = killerSpecies,
            BestFloorReachedAtDeath = bestFloor,
            PreviousRunWasClean = clean,
            KillerWasFirstEncounter = killerFirst,
        };
        if (hasNotableGear)
            rec.GearCarried.Add(new GearItemRecord { TypeId = "shortsword", Enchantment = 2, IsNotable = true });
        else
            rec.GearCarried.Add(new GearItemRecord { TypeId = "dagger", Enchantment = 0 });
        return rec;
    }

    // ─── Category selection ───────────────────────────────────────────────────

    [Test]
    public void SelectCategory_MostRecent_GetsTheRecentOne()
    {
        var r1 = MakeRecord(1, floor: 3);
        var r2 = MakeRecord(2, floor: 5);
        var data = MakeData(r1, r2);

        Assert.That(CatalogEntryRenderer.SelectTemplateCategory(r2, data), Is.EqualTo("the_recent_one"));
    }

    [Test]
    public void SelectCategory_OlderRecord_DoesNotGetTheRecentOne()
    {
        var r1 = MakeRecord(1, floor: 3);
        var r2 = MakeRecord(2, floor: 5);
        var data = MakeData(r1, r2);

        Assert.That(CatalogEntryRenderer.SelectTemplateCategory(r1, data),
            Is.Not.EqualTo("the_recent_one"));
    }

    [Test]
    public void SelectCategory_BestRun_GetsTheOneWeKept()
    {
        // DiedFloor == BestFloorReachedAtDeath → milestone run
        var r1 = MakeRecord(1, floor: 12, bestFloor: 12);
        var r2 = MakeRecord(2, floor: 15); // most recent — claimed first
        var data = MakeData(r1, r2);

        Assert.That(CatalogEntryRenderer.SelectTemplateCategory(r1, data), Is.EqualTo("the_one_we_kept"));
    }

    [Test]
    public void SelectCategory_NearFinal_GetsTheAlmost()
    {
        var r1 = MakeRecord(1, floor: 22);
        var r2 = MakeRecord(2, floor: 5); // most recent
        var data = MakeData(r1, r2);

        Assert.That(CatalogEntryRenderer.SelectTemplateCategory(r1, data), Is.EqualTo("the_almost"));
    }

    [Test]
    public void SelectCategory_AlmostDoesNotApply_AtFloor25_WardenCause()
    {
        var r1 = MakeRecord(1, floor: 25, cause: "under_warden");
        var r2 = MakeRecord(2, floor: 5);
        var data = MakeData(r1, r2);

        Assert.That(CatalogEntryRenderer.SelectTemplateCategory(r1, data),
            Is.Not.EqualTo("the_almost"));
    }

    [Test]
    public void SelectCategory_NotableGear_GetsGoodGear()
    {
        var r1 = MakeRecord(1, floor: 6, hasNotableGear: true);
        var r2 = MakeRecord(2, floor: 2);
        var data = MakeData(r1, r2);

        Assert.That(CatalogEntryRenderer.SelectTemplateCategory(r1, data), Is.EqualTo("good_gear"));
    }

    [Test]
    public void SelectCategory_SelfInflicted_GetsTheStupidOne()
    {
        foreach (var cause in new[] { "oil_slick_fire", "own_poison", "own_trap", "possessed_wrong_host" })
        {
            var r1 = MakeRecord(1, floor: 4, cause: cause);
            var r2 = MakeRecord(2, floor: 2);
            var data = MakeData(r1, r2);

            Assert.That(CatalogEntryRenderer.SelectTemplateCategory(r1, data), Is.EqualTo("the_stupid_one"),
                $"cause={cause}");
        }
    }

    [Test]
    public void SelectCategory_MonsterFirstEncounterDeepFloor_GetsNoWarning()
    {
        var r1 = MakeRecord(1, floor: 10, cause: "monster", killerFirst: true, killerSpecies: "troll");
        var r2 = MakeRecord(2, floor: 2);
        var data = MakeData(r1, r2);

        Assert.That(CatalogEntryRenderer.SelectTemplateCategory(r1, data), Is.EqualTo("no_warning"));
    }

    [Test]
    public void SelectCategory_NoWarningDoesNotApply_ShallowFloor()
    {
        var r1 = MakeRecord(1, floor: 5, cause: "monster", killerFirst: true);
        var r2 = MakeRecord(2, floor: 2);
        var data = MakeData(r1, r2);

        Assert.That(CatalogEntryRenderer.SelectTemplateCategory(r1, data),
            Is.Not.EqualTo("no_warning"), "Floor 5 is too shallow for no_warning.");
    }

    [Test]
    public void SelectCategory_CleanRun_GetsThePatientOne()
    {
        var r1 = MakeRecord(1, floor: 11, clean: true);
        var r2 = MakeRecord(2, floor: 2);
        var data = MakeData(r1, r2);

        Assert.That(CatalogEntryRenderer.SelectTemplateCategory(r1, data), Is.EqualTo("the_patient_one"));
    }

    [Test]
    public void SelectCategory_Fallback_GetsEarlyDisaster()
    {
        var r1 = MakeRecord(1, floor: 3, cause: "monster");
        var r2 = MakeRecord(2, floor: 2);
        var data = MakeData(r1, r2);

        Assert.That(CatalogEntryRenderer.SelectTemplateCategory(r1, data), Is.EqualTo("early_disaster"));
    }

    [Test]
    public void SelectCategory_SingleRecord_IsRecentOne()
    {
        var r1 = MakeRecord(1, floor: 3);
        var data = MakeData(r1);

        Assert.That(CatalogEntryRenderer.SelectTemplateCategory(r1, data), Is.EqualTo("the_recent_one"));
    }

    // ─── Slot filling ─────────────────────────────────────────────────────────

    [Test]
    public void RenderEntry_FillsRunNumberAndFloor()
    {
        var yaml = """
            entry_templates.early_disaster:
              - "#{run_number}. Floor {floor}. Generic."
            """;
        var registry = VoiceLineRegistry.LoadFromYaml(yaml, new AotObjectFactory());
        var r1 = MakeRecord(3, floor: 7);
        var r2 = MakeRecord(4, floor: 2);
        var data = MakeData(r1, r2);

        var rendered = CatalogEntryRenderer.RenderEntry(r1, data, registry, new SeededRandom(1337));
        Assert.That(rendered, Does.Contain("#3"));
        Assert.That(rendered, Does.Contain("Floor 7"));
    }

    [Test]
    public void RenderEntry_FillsGearItem_ForEarlyDisaster()
    {
        var yaml = """
            entry_templates.early_disaster:
              - "The {gear_item} is yours."
            """;
        var registry = VoiceLineRegistry.LoadFromYaml(yaml, new AotObjectFactory());
        var r1 = MakeRecord(1, floor: 3); // dagger, no enchantment
        var r2 = MakeRecord(2, floor: 2);
        var data = MakeData(r1, r2);

        var rendered = CatalogEntryRenderer.RenderEntry(r1, data, registry, new SeededRandom(1337));
        Assert.That(rendered, Does.Contain("dagger"));
    }

    [Test]
    public void RenderEntry_NotableGear_ShowsEnchantment()
    {
        var yaml = """
            entry_templates.good_gear:
              - "The {gear_item} was on him."
            """;
        var registry = VoiceLineRegistry.LoadFromYaml(yaml, new AotObjectFactory());
        var r1 = MakeRecord(1, floor: 6, hasNotableGear: true); // shortsword +2
        var r2 = MakeRecord(2, floor: 2);
        var data = MakeData(r1, r2);

        var rendered = CatalogEntryRenderer.RenderEntry(r1, data, registry, new SeededRandom(1337));
        Assert.That(rendered, Does.Contain("shortsword +2"));
    }

    [Test]
    public void RenderEntry_NoGear_FallsBackToKit()
    {
        var yaml = """
            entry_templates.the_recent_one:
              - "He had the {gear_item}."
            """;
        var registry = VoiceLineRegistry.LoadFromYaml(yaml, new AotObjectFactory());
        var r1 = new PastSashaRecord { Id = 1, DiedRun = 1, DiedFloor = 3, CauseOfDeath = "monster" };
        var data = MakeData(r1);

        var rendered = CatalogEntryRenderer.RenderEntry(r1, data, registry, new SeededRandom(1337));
        Assert.That(rendered, Does.Contain("kit"));
    }

    [Test]
    public void RenderEntry_StupidOne_FillsHazard()
    {
        var yaml = """
            entry_templates.the_stupid_one:
              - "The {hazard} was visible."
            """;
        var registry = VoiceLineRegistry.LoadFromYaml(yaml, new AotObjectFactory());
        var r1 = MakeRecord(1, floor: 4, cause: "own_trap");
        var r2 = MakeRecord(2, floor: 2);
        var data = MakeData(r1, r2);

        var rendered = CatalogEntryRenderer.RenderEntry(r1, data, registry, new SeededRandom(1337));
        Assert.That(rendered, Does.Contain("trap"));
    }

    [Test]
    public void RenderEntry_TheOneWeKept_FillsFloorMilestoneAndRegion()
    {
        var yaml = """
            entry_templates.the_one_we_kept:
              - "The {floor_milestone} business, {region_name}."
            """;
        var registry = VoiceLineRegistry.LoadFromYaml(yaml, new AotObjectFactory());
        var r1 = MakeRecord(1, floor: 7, bestFloor: 7); // floor 7 = the Boundary region
        var r2 = MakeRecord(2, floor: 2);
        var data = MakeData(r1, r2);

        var rendered = CatalogEntryRenderer.RenderEntry(r1, data, registry, new SeededRandom(1337));
        Assert.That(rendered, Does.Contain("the Boundary"));
    }
}

/// <summary>
/// Phase 6 integration: real voice line YAML files load correctly with AotObjectFactory.
/// </summary>
[TestFixture]
public class VoiceLineYamlIntegrationTests
{
    private static string VoiceLinePath(string fileName) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", "voice_lines", fileName);

    [Test]
    public void HollowmarkYaml_LoadsWithStrictFactory()
    {
        var yaml = File.ReadAllText(VoiceLinePath("hollowmark.yaml"));
        Assert.DoesNotThrow(() => VoiceLineRegistry.LoadFromYaml(yaml, new AotObjectFactory(strict: true)));
    }

    [Test]
    public void QuippingShadeYaml_LoadsWithStrictFactory()
    {
        var yaml = File.ReadAllText(VoiceLinePath("quipping_shade.yaml"));
        Assert.DoesNotThrow(() => VoiceLineRegistry.LoadFromYaml(yaml, new AotObjectFactory(strict: true)));
    }

    [Test]
    public void CatalogPastSelvesYaml_LoadsWithStrictFactory()
    {
        var yaml = File.ReadAllText(VoiceLinePath("catalog_past_selves.yaml"));
        Assert.DoesNotThrow(() => VoiceLineRegistry.LoadFromYaml(yaml, new AotObjectFactory(strict: true)));
    }

    [Test]
    public void AllVoiceLineFiles_Merged_HaveExpectedTriggers()
    {
        var hollowmark = File.ReadAllText(VoiceLinePath("hollowmark.yaml"));
        var quipping = File.ReadAllText(VoiceLinePath("quipping_shade.yaml"));
        var catalog = File.ReadAllText(VoiceLinePath("catalog_past_selves.yaml"));

        var registry = VoiceLineRegistry.LoadFromYaml(hollowmark, new AotObjectFactory());
        registry.Merge(VoiceLineRegistry.LoadFromYaml(quipping, new AotObjectFactory()));
        registry.Merge(VoiceLineRegistry.LoadFromYaml(catalog, new AotObjectFactory()));

        // Spot-check key triggers from each file.
        Assert.That(registry.HasTrigger("past_sasha_encounter.looted_body"), Is.True);
        Assert.That(registry.HasTrigger("past_sasha_encounter.possessed_corpse.post_spell_break"), Is.True);
        Assert.That(registry.HasTrigger("oil_slick_fire"), Is.True);
        Assert.That(registry.HasTrigger("hollowmark_out_of_range"), Is.True);
        Assert.That(registry.HasTrigger("entry_templates.early_disaster"), Is.True);
        Assert.That(registry.HasTrigger("entry_templates.the_recent_one"), Is.True);
    }

    [Test]
    public void PostSpellBreakTrigger_CanonicalLineFirst()
    {
        var yaml = File.ReadAllText(VoiceLinePath("hollowmark.yaml"));
        var registry = VoiceLineRegistry.LoadFromYaml(yaml, new AotObjectFactory());
        var fired = new HashSet<string>();
        var line = registry.GetLine("past_sasha_encounter.possessed_corpse.post_spell_break",
            new SeededRandom(1337), fired);
        Assert.That(line, Is.EqualTo("That wasn't you anymore, Boss. You can have the gear back now."),
            "Canonical (first) line should fire on first encounter.");
    }
}
