using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// 0c step 3: every combat monster carries the threat archetype docs/balance/threat_archetypes.md §2
/// assigns, resolved through extends-inheritance, and MonsterFactory attaches it as a ThreatArchetypeTag.
/// This is the attribution backbone for role-aware floor health — a misclassified killer would make the
/// report blame the wrong archetype, so the assignment is pinned here (outcome, not attachment).
/// </summary>
[TestFixture]
public class ThreatArchetypeTaggingTests
{
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

    private static ThreatArchetype? ArchetypeOf(MonsterFactory factory, string typeId)
    {
        var entity = factory.Create(typeId)
            ?? throw new InvalidOperationException($"factory could not create '{typeId}'");
        return entity.Get<ThreatArchetypeTag>()?.Archetype;
    }

    // ── Direct assignments (§2, with the tightened spike definition) ────────────────────────
    // SPIKE is reserved for can't-tank-must-change-approach (troll regen, wraith drain+speed, lich).
    // Status-on-a-weak-beast (spiders' poison/slow, fire_beetle's burn) is TEXTURE → textured baseline.
    [TestCase("orc",                 ThreatArchetype.Baseline)]
    [TestCase("skeleton",            ThreatArchetype.Baseline)]
    [TestCase("zombie",              ThreatArchetype.Baseline)]
    [TestCase("cultist_blademaster", ThreatArchetype.Baseline)]
    [TestCase("slime",               ThreatArchetype.Baseline)]
    [TestCase("fire_beetle",         ThreatArchetype.Baseline)]   // fragile + burn wrinkle, not a spike
    [TestCase("cave_spider",         ThreatArchetype.Baseline)]   // weak beast + poison wrinkle
    [TestCase("giant_spider",        ThreatArchetype.Baseline)]   // hard-hitting but low-HP/tankable (cf. orc_brute)
    [TestCase("troll",               ThreatArchetype.Spike)]
    [TestCase("wraith",              ThreatArchetype.Spike)]
    [TestCase("orc_shaman",          ThreatArchetype.Escalator)]
    [TestCase("orc_chieftain",       ThreatArchetype.Escalator)]
    [TestCase("necromancer",         ThreatArchetype.Escalator)]
    [TestCase("large_slime",         ThreatArchetype.Escalator)]
    [TestCase("lich",                ThreatArchetype.Fused)]
    public void DirectArchetype_IsTaggedAsSpecified(string typeId, ThreatArchetype expected)
        => Assert.That(ArchetypeOf(CreateFactory(), typeId), Is.EqualTo(expected));

    // ── Inheritance via extends (child inherits unless it overrides) ────────────────────────
    [TestCase("orc_grunt",          ThreatArchetype.Baseline)]   // ← orc
    [TestCase("orc_brute",          ThreatArchetype.Baseline)]   // ← orc
    [TestCase("orc_scout",          ThreatArchetype.Baseline)]   // ← orc
    [TestCase("orc_veteran",        ThreatArchetype.Baseline)]   // ← orc
    [TestCase("plague_zombie",      ThreatArchetype.Baseline)]   // ← zombie
    [TestCase("troll_ancient",      ThreatArchetype.Spike)]      // ← troll
    [TestCase("web_spider",         ThreatArchetype.Baseline)]   // ← cave_spider (textured baseline)
    [TestCase("plague_necromancer", ThreatArchetype.Escalator)] // ← necromancer
    [TestCase("greater_slime",      ThreatArchetype.Escalator)] // ← large_slime ← slime (override holds)
    public void InheritedArchetype_PropagatesThroughExtends(string typeId, ThreatArchetype expected)
        => Assert.That(ArchetypeOf(CreateFactory(), typeId), Is.EqualTo(expected));

    [Test]
    public void OrcSkirmisher_IsBaseline_DespiteNotExtendingOrc()
        => Assert.That(ArchetypeOf(CreateFactory(), "orc_skirmisher"), Is.EqualTo(ThreatArchetype.Baseline));

    // ── Unclassified path: a monster with no threat_archetype gets no tag (no silent default) ──
    [Test]
    public void UnclassifiedMonster_GetsNoTag()
    {
        // Every combat monster is now classified; this pins the fallback so a future untagged monster
        // carries no archetype (its kills attribute to nothing) rather than silently defaulting.
        var entityFactory = new EntityFactory();
        var def = new MonsterDefinition { Name = "Test Dummy", ThreatArchetype = null };
        var entity = new MonsterFactory(new Dictionary<string, MonsterDefinition> { ["dummy"] = def }, entityFactory)
            .Create("dummy");
        Assert.That(entity, Is.Not.Null);
        Assert.That(entity!.Get<ThreatArchetypeTag>(), Is.Null,
            "a monster with no threat_archetype must stay untagged, not default to an archetype.");
    }

    // ── Parser unit cases ───────────────────────────────────────────────────────────────────
    [TestCase("baseline",  ThreatArchetype.Baseline)]
    [TestCase("SPIKE",     ThreatArchetype.Spike)]
    [TestCase("Escalator", ThreatArchetype.Escalator)]
    [TestCase("fused",     ThreatArchetype.Fused)]
    public void Parse_AcceptsKnownValues_CaseInsensitive(string raw, ThreatArchetype expected)
        => Assert.That(ThreatArchetypeTag.Parse(raw), Is.EqualTo(expected));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("baseliner")]
    [TestCase("tank")]
    public void Parse_RejectsUnknownOrEmpty_ReturnsNull(string? raw)
        => Assert.That(ThreatArchetypeTag.Parse(raw), Is.Null);
}
