using System.Text.Json;
using UnderWarden.Logic.Persistence;
using UnderWarden.Logic.Persistence.Namespaces;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

/// <summary>
/// Phase 2 persistence tests: HallWardenPossessionsTotal, PendingMemos list,
/// PendingMemo record shape, and serialization round-trips.
/// Phase 3 (MemoDeliveryEvaluator) and Phase 4 (MemoInboxPanel) are not yet built;
/// these tests cover only the data layer.
/// </summary>
[TestFixture]
public class UnderWardenDataTests
{
    private string _tempDir = "";

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "yarl_uwdata_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FakePersistencePathProvider Provider() => new(_tempDir);

    // ── HallWardenPossessionsTotal ────────────────────────────────────────────

    [Test]
    public void HallWardenPossessionsTotal_DefaultsToZero()
    {
        var data = new UnderWardenData();
        Assert.That(data.HallWardenPossessionsTotal, Is.EqualTo(0));
    }

    [Test]
    public void HallWardenPossessionsTotal_IncrementsCorrectly()
    {
        var data = new UnderWardenData();
        data.HallWardenPossessionsTotal++;
        data.HallWardenPossessionsTotal++;
        data.HallWardenPossessionsTotal++;

        Assert.That(data.HallWardenPossessionsTotal, Is.EqualTo(3));
    }

    [Test]
    public void HallWardenPossessionsTotal_RoundTrips()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.UnderWarden.HallWardenPossessionsTotal = 4;
        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.UnderWarden.HallWardenPossessionsTotal, Is.EqualTo(4));
    }

    // ── PendingMemos ──────────────────────────────────────────────────────────

    [Test]
    public void PendingMemos_DefaultsToEmpty()
    {
        var data = new UnderWardenData();
        Assert.That(data.PendingMemos, Is.Empty);
    }

    [Test]
    public void PendingMemos_AddingMemo_Works()
    {
        var data = new UnderWardenData();
        var memo = new PendingMemo(
            Key: "polite.hall_warden_possession",
            Subject: "RE: Unsanctioned Bodily Occupation",
            Body: "It has come to our attention that you have occupied the warden.",
            DeliveredRun: 1
        );

        data.PendingMemos.Add(memo);

        Assert.That(data.PendingMemos, Has.Count.EqualTo(1));
        Assert.That(data.PendingMemos[0].Key, Is.EqualTo("polite.hall_warden_possession"));
        Assert.That(data.PendingMemos[0].DeliveredRun, Is.EqualTo(1));
    }

    [Test]
    public void PendingMemos_RoundTrips_PreservesAll()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.UnderWarden.PendingMemos.Add(new PendingMemo(
            Key: "polite.death_first",
            Subject: "Condolences on your recent fatality",
            Body: "We note with bureaucratic sympathy your untimely demise on floor 3.",
            DeliveredRun: 2
        ));
        state.UnderWarden.PendingMemos.Add(new PendingMemo(
            Key: "procedural_notice.hall_warden_possession",
            Subject: "NOTICE: Repeated possession offence",
            Body: "This constitutes your second infraction. Disciplinary review is pending.",
            DeliveredRun: 5
        ));

        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.UnderWarden.PendingMemos, Has.Count.EqualTo(2));
        Assert.That(loaded.UnderWarden.PendingMemos[0].Key, Is.EqualTo("polite.death_first"));
        Assert.That(loaded.UnderWarden.PendingMemos[0].DeliveredRun, Is.EqualTo(2));
        Assert.That(loaded.UnderWarden.PendingMemos[1].Key, Is.EqualTo("procedural_notice.hall_warden_possession"));
        Assert.That(loaded.UnderWarden.PendingMemos[1].DeliveredRun, Is.EqualTo(5));
    }

    // ── PendingMemo record JSON shape ─────────────────────────────────────────

    [Test]
    public void PendingMemo_SerializesToExpectedJsonShape()
    {
        var memo = new PendingMemo(
            Key: "polite.cause_acid",
            Subject: "Re: Acid Incident",
            Body: "Please avoid corrosive hazards.",
            DeliveredRun: 5
        );

        var json = JsonSerializer.Serialize(memo, PersistenceJsonContext.Default.PendingMemo);

        Assert.That(json, Does.Contain("\"key\""));
        Assert.That(json, Does.Contain("\"subject\""));
        Assert.That(json, Does.Contain("\"body\""));
        Assert.That(json, Does.Contain("\"delivered_run\""));
        Assert.That(json, Does.Contain("\"polite.cause_acid\""));
        Assert.That(json, Does.Contain("\"Re: Acid Incident\""));
        Assert.That(json, Does.Contain("5"));
        // Ensure PascalCase properties are NOT present (i.e. source-gen uses JsonPropertyName)
        Assert.That(json, Does.Not.Contain("\"Key\""));
        Assert.That(json, Does.Not.Contain("\"DeliveredRun\""));
    }

    [Test]
    public void PendingMemo_DeserializesFromExpectedJsonShape()
    {
        var json = """{"key":"formal_complaint.hall_warden_possession","subject":"FINAL NOTICE","body":"Your file has been escalated.","delivered_run":7}""";

        var memo = JsonSerializer.Deserialize(json, PersistenceJsonContext.Default.PendingMemo);

        Assert.That(memo, Is.Not.Null);
        Assert.That(memo!.Key, Is.EqualTo("formal_complaint.hall_warden_possession"));
        Assert.That(memo.Subject, Is.EqualTo("FINAL NOTICE"));
        Assert.That(memo.Body, Is.EqualTo("Your file has been escalated."));
        Assert.That(memo.DeliveredRun, Is.EqualTo(7));
    }

    [Test]
    public void PendingMemo_RoundTrip_ViaSourceGeneratedContext()
    {
        var original = new PendingMemo(
            Key: "polite.floor_low",
            Subject: "Introductory Correspondence",
            Body: "Welcome to the catacombs. We are watching.",
            DeliveredRun: 1
        );

        var json = JsonSerializer.Serialize(original, PersistenceJsonContext.Default.PendingMemo);
        var deserialized = JsonSerializer.Deserialize(json, PersistenceJsonContext.Default.PendingMemo);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Key, Is.EqualTo(original.Key));
        Assert.That(deserialized.Subject, Is.EqualTo(original.Subject));
        Assert.That(deserialized.Body, Is.EqualTo(original.Body));
        Assert.That(deserialized.DeliveredRun, Is.EqualTo(original.DeliveredRun));
    }

    // ── Existing RecordMemoSent still works ───────────────────────────────────

    [Test]
    public void RecordMemoSent_StillWorks_WithNewFields_Present()
    {
        var data = new UnderWardenData();

        // New fields at defaults
        Assert.That(data.HallWardenPossessionsTotal, Is.EqualTo(0));
        Assert.That(data.PendingMemos, Is.Empty);

        // Existing method unaffected
        data.RecordMemoSent("polite", "unauthorized_descent");
        data.RecordMemoSent("procedural_notice");

        Assert.That(data.TotalMemosSentEver, Is.EqualTo(2));
        Assert.That(data.LastMemoTone, Is.EqualTo("procedural_notice"));
        Assert.That(data.HasLoggedGrievance("unauthorized_descent"), Is.True);

        // New fields still at defaults (RecordMemoSent does not touch them)
        Assert.That(data.HallWardenPossessionsTotal, Is.EqualTo(0));
        Assert.That(data.PendingMemos, Is.Empty);
    }

    [Test]
    public void RecordMemoSent_RoundTrips_WithNewFieldsCoexisting()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.UnderWarden.RecordMemoSent("polite", "unauthorized_descent");
        state.UnderWarden.HallWardenPossessionsTotal = 2;
        state.UnderWarden.PendingMemos.Add(new PendingMemo(
            Key: "polite.hall_warden_possession",
            Subject: "RE: Possession",
            Body: "We noticed you occupied the warden.",
            DeliveredRun: 3
        ));

        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.UnderWarden.TotalMemosSentEver, Is.EqualTo(1));
        Assert.That(loaded.UnderWarden.LastMemoTone, Is.EqualTo("polite"));
        Assert.That(loaded.UnderWarden.HasLoggedGrievance("unauthorized_descent"), Is.True);
        Assert.That(loaded.UnderWarden.HallWardenPossessionsTotal, Is.EqualTo(2));
        Assert.That(loaded.UnderWarden.PendingMemos, Has.Count.EqualTo(1));
        Assert.That(loaded.UnderWarden.PendingMemos[0].Key, Is.EqualTo("polite.hall_warden_possession"));
    }
}
