using System.Text.Json;
using UnderWarden.Logic.Persistence;
using UnderWarden.Logic.Persistence.Namespaces;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

[TestFixture]
public class PastSashasTests
{
    private string _tempDir = "";

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "yarl_past_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FakePersistencePathProvider Provider() => new(_tempDir);

    // ── AddRecord ────────────────────────────────────────────────────────────

    [Test]
    public void AddRecord_SetsAllFields_AndIncrementsNextId()
    {
        var data = new PastSashasData();
        var gear = new List<GearItemRecord>
        {
            new() { TypeId = "shortsword", Enchantment = 1, Condition = "normal" },
        };

        var record = data.AddRecord(
            diedRun: 3, diedFloor: 7, causeOfDeath: "monster",
            killerSpecies: "orc_brute", gearCarried: gear);

        Assert.That(record.Id, Is.EqualTo(1));
        Assert.That(record.DiedRun, Is.EqualTo(3));
        Assert.That(record.DiedFloor, Is.EqualTo(7));
        Assert.That(record.CauseOfDeath, Is.EqualTo("monster"));
        Assert.That(record.KillerSpecies, Is.EqualTo("orc_brute"));
        Assert.That(record.GearCarried, Has.Count.EqualTo(1));
        Assert.That(record.GearCarried[0].TypeId, Is.EqualTo("shortsword"));
        Assert.That(data.NextId, Is.EqualTo(2));
    }

    [Test]
    public void AddRecord_MultipleRecords_UniqueIds()
    {
        var data = new PastSashasData();

        var r1 = data.AddRecord(1, 4, "monster", null, new List<GearItemRecord>());
        var r2 = data.AddRecord(2, 6, "hazard", null, new List<GearItemRecord>());
        var r3 = data.AddRecord(3, 9, "monster", "orc", new List<GearItemRecord>());

        Assert.That(r1.Id, Is.EqualTo(1));
        Assert.That(r2.Id, Is.EqualTo(2));
        Assert.That(r3.Id, Is.EqualTo(3));
        Assert.That(data.Records, Has.Count.EqualTo(3));
    }

    [Test]
    public void AddRecord_NullKillerSpecies_IsAllowed()
    {
        var data = new PastSashasData();
        var record = data.AddRecord(1, 1, "hazard", null, new List<GearItemRecord>());

        Assert.That(record.KillerSpecies, Is.Null);
        Assert.That(record.CauseOfDeath, Is.EqualTo("hazard"));
    }

    // ── GearItemRecord ────────────────────────────────────────────────────────

    [Test]
    public void GearItemRecord_CorrodedCondition_StoredCorrectly()
    {
        var gear = new GearItemRecord { TypeId = "longsword", Enchantment = 0, Condition = "corroded" };

        Assert.That(gear.TypeId, Is.EqualTo("longsword"));
        Assert.That(gear.Condition, Is.EqualTo("corroded"));
    }

    [Test]
    public void GearItemRecord_DefaultCondition_IsNormal()
    {
        var gear = new GearItemRecord { TypeId = "dagger" };
        Assert.That(gear.Condition, Is.EqualTo("normal"));
        Assert.That(gear.Enchantment, Is.EqualTo(0));
    }

    // ── GetEligibleRecords ────────────────────────────────────────────────────

    [Test]
    public void GetEligibleRecords_EmptyEncounteredSet_ReturnsAllRecords()
    {
        var data = new PastSashasData();
        data.AddRecord(1, 3, "monster", "orc", new List<GearItemRecord>());
        data.AddRecord(2, 5, "monster", "troll", new List<GearItemRecord>());

        var eligible = data.GetEligibleRecords(new HashSet<int>()).ToList();

        Assert.That(eligible, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetEligibleRecords_ExcludesEncounteredIds()
    {
        var data = new PastSashasData();
        var r1 = data.AddRecord(1, 3, "monster", "orc", new List<GearItemRecord>());
        var r2 = data.AddRecord(2, 5, "monster", "troll", new List<GearItemRecord>());

        var encountered = new HashSet<int> { r1.Id };
        var eligible = data.GetEligibleRecords(encountered).ToList();

        Assert.That(eligible, Has.Count.EqualTo(1));
        Assert.That(eligible[0].Id, Is.EqualTo(r2.Id));
    }

    [Test]
    public void GetEligibleRecords_AllEncountered_ReturnsEmpty()
    {
        var data = new PastSashasData();
        var r1 = data.AddRecord(1, 3, "monster", "orc", new List<GearItemRecord>());
        var r2 = data.AddRecord(2, 5, "monster", "troll", new List<GearItemRecord>());

        var encountered = new HashSet<int> { r1.Id, r2.Id };
        var eligible = data.GetEligibleRecords(encountered).ToList();

        Assert.That(eligible, Is.Empty);
    }

    [Test]
    public void GetEligibleRecords_OrderedByIdDescending_MostRecentFirst()
    {
        var data = new PastSashasData();
        data.AddRecord(1, 3, "monster", "orc", new List<GearItemRecord>());
        data.AddRecord(2, 5, "monster", "troll", new List<GearItemRecord>());
        data.AddRecord(3, 8, "monster", "lich", new List<GearItemRecord>());

        var eligible = data.GetEligibleRecords(new HashSet<int>()).ToList();

        // Most-recent (highest Id) first — preferred Variant 3 candidate.
        Assert.That(eligible[0].Id, Is.EqualTo(3));
        Assert.That(eligible[1].Id, Is.EqualTo(2));
        Assert.That(eligible[2].Id, Is.EqualTo(1));
    }

    [Test]
    public void GetEligibleRecords_EmptyData_ReturnsEmpty()
    {
        var data = new PastSashasData();
        Assert.That(data.GetEligibleRecords(new HashSet<int>()), Is.Empty);
    }

    // ── Round-trip serialization ──────────────────────────────────────────────

    [Test]
    public void PastSashaRecord_RoundTrips_AllFields()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.PastSashas.AddRecord(
            diedRun: 5,
            diedFloor: 12,
            causeOfDeath: "monster",
            killerSpecies: "troll_ancient",
            gearCarried: new List<GearItemRecord>
            {
                new() { TypeId = "shortsword", Enchantment = 2, Condition = "corroded" },
                new() { TypeId = "chain_mail", Enchantment = 0, Condition = "normal" },
            });
        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);

        Assert.That(loaded.PastSashas.Records, Has.Count.EqualTo(1));
        var r = loaded.PastSashas.Records[0];
        Assert.That(r.Id, Is.EqualTo(1));
        Assert.That(r.DiedRun, Is.EqualTo(5));
        Assert.That(r.DiedFloor, Is.EqualTo(12));
        Assert.That(r.CauseOfDeath, Is.EqualTo("monster"));
        Assert.That(r.KillerSpecies, Is.EqualTo("troll_ancient"));
        Assert.That(r.GearCarried, Has.Count.EqualTo(2));
        Assert.That(r.GearCarried[0].TypeId, Is.EqualTo("shortsword"));
        Assert.That(r.GearCarried[0].Enchantment, Is.EqualTo(2));
        Assert.That(r.GearCarried[0].Condition, Is.EqualTo("corroded"));
        Assert.That(r.GearCarried[1].TypeId, Is.EqualTo("chain_mail"));
        Assert.That(loaded.PastSashas.NextId, Is.EqualTo(2));
    }

    [Test]
    public void PastSashas_MultipleRunDeaths_AccumulateAcrossRuns()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.PastSashas.AddRecord(1, 3, "monster", "orc", new List<GearItemRecord>());
        state.PastSashas.AddRecord(2, 7, "monster", "troll", new List<GearItemRecord>());
        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);

        Assert.That(loaded.PastSashas.Records, Has.Count.EqualTo(2));
        Assert.That(loaded.PastSashas.NextId, Is.EqualTo(3));
    }

    // ── FreedPastSelvesData ───────────────────────────────────────────────────

    [Test]
    public void FreedPastSelf_AddRecord_SetsAllFields()
    {
        var data = new FreedPastSelvesData();
        var record = data.AddRecord(freedPastSashaId: 7, freedRun: 9, freedFloor: 14);

        Assert.That(record.FreedPastSashaId, Is.EqualTo(7));
        Assert.That(record.FreedRun, Is.EqualTo(9));
        Assert.That(record.FreedFloor, Is.EqualTo(14));
        Assert.That(record.FreedAt, Is.Not.EqualTo(default(DateTimeOffset)));
    }

    [Test]
    public void FreedPastSelf_RoundTrips_PastSashaRef()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.PastSashas.AddRecord(1, 5, "monster", "orc", new List<GearItemRecord>());
        state.FreedPastSelves.AddRecord(freedPastSashaId: 1, freedRun: 2, freedFloor: 11);
        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);

        Assert.That(loaded.FreedPastSelves.Records, Has.Count.EqualTo(1));
        Assert.That(loaded.FreedPastSelves.Records[0].FreedPastSashaId, Is.EqualTo(1));
        Assert.That(loaded.FreedPastSelves.Records[0].FreedRun, Is.EqualTo(2));
        Assert.That(loaded.FreedPastSelves.Records[0].FreedFloor, Is.EqualTo(11));
    }
}
