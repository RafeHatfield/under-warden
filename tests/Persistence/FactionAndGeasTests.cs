using UnderWarden.Logic.Persistence;
using UnderWarden.Logic.Persistence.Namespaces;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

[TestFixture]
public class FactionAndGeasTests
{
    private string _tempDir = "";

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "yarl_fac_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FakePersistencePathProvider Provider() => new(_tempDir);

    // ── FactionsData defaults ─────────────────────────────────────────────────

    [Test]
    public void Factions_DefaultState_OrcIsNeutral()
    {
        var data = new FactionsData();
        Assert.That(data.GetState("orc"), Is.EqualTo("neutral"));
    }

    [Test]
    public void Factions_UnknownFaction_DefaultsToNeutral()
    {
        var data = new FactionsData();
        Assert.That(data.GetState("hall_wardens"), Is.EqualTo("neutral"));
    }

    // ── ApplyNegativeAction ───────────────────────────────────────────────────

    [Test]
    public void ApplyNegativeAction_SetsHostile()
    {
        var data = new FactionsData();
        data.ApplyNegativeAction("orc");
        Assert.That(data.GetState("orc"), Is.EqualTo("hostile"));
    }

    [Test]
    public void ApplyNegativeAction_ResetsDecayCounter()
    {
        var data = new FactionsData();
        // Accumulate some decay first
        data.OnRunEndNoNegativeAction("orc");
        data.OnRunEndNoNegativeAction("orc");
        Assert.That(data.GetOrCreate("orc").RunsSinceNegativeAction, Is.EqualTo(2));

        data.ApplyNegativeAction("orc");
        Assert.That(data.GetOrCreate("orc").RunsSinceNegativeAction, Is.EqualTo(0));
    }

    // ── ApplyAllied ───────────────────────────────────────────────────────────

    [Test]
    public void ApplyAllied_SetsAllied_ResetsDecay()
    {
        var data = new FactionsData();
        data.ApplyNegativeAction("orc");
        data.OnRunEndNoNegativeAction("orc"); // decay starts
        Assert.That(data.GetOrCreate("orc").RunsSinceNegativeAction, Is.EqualTo(1));

        data.ApplyAllied("orc");
        Assert.That(data.GetState("orc"), Is.EqualTo("allied"));
        Assert.That(data.GetOrCreate("orc").RunsSinceNegativeAction, Is.EqualTo(0));
    }

    // ── OnRunEndNoNegativeAction (soft decay) ─────────────────────────────────

    [Test]
    public void OnRunEndNoNegativeAction_IncrementsDecayCounter()
    {
        var data = new FactionsData();
        data.OnRunEndNoNegativeAction("orc");
        Assert.That(data.GetOrCreate("orc").RunsSinceNegativeAction, Is.EqualTo(1));
    }

    [Test]
    public void SoftDecay_NeutralState_CounterIncrements_NoStateChange()
    {
        var data = new FactionsData();
        // neutral → counter increments but state stays neutral (nothing to decay to)
        bool changed = data.OnRunEndNoNegativeAction("orc");
        Assert.That(changed, Is.False);
        Assert.That(data.GetState("orc"), Is.EqualTo("neutral"));
    }

    [Test]
    public void SoftDecay_HostileBeforeThreshold_StaysHostile()
    {
        var data = new FactionsData();
        data.ApplyNegativeAction("orc");

        // 4 runs without negative actions — threshold is 5
        for (int i = 0; i < 4; i++)
            data.OnRunEndNoNegativeAction("orc");

        Assert.That(data.GetState("orc"), Is.EqualTo("hostile"));
        Assert.That(data.GetOrCreate("orc").RunsSinceNegativeAction, Is.EqualTo(4));
    }

    [Test]
    public void SoftDecay_HostileAtThreshold_DecaysToNeutral()
    {
        var data = new FactionsData();
        data.ApplyNegativeAction("orc");

        // 5 runs without negative actions — hits threshold
        for (int i = 0; i < FactionsData.DecayThreshold; i++)
            data.OnRunEndNoNegativeAction("orc");

        Assert.That(data.GetState("orc"), Is.EqualTo("neutral"),
            "Hostile should decay to Neutral after DecayThreshold clean runs.");
    }

    [Test]
    public void SoftDecay_ThresholdRunReturnsTrue()
    {
        var data = new FactionsData();
        data.ApplyNegativeAction("orc");

        for (int i = 0; i < FactionsData.DecayThreshold - 1; i++)
            data.OnRunEndNoNegativeAction("orc");

        bool changed = data.OnRunEndNoNegativeAction("orc");
        Assert.That(changed, Is.True, "Final decay step should return true.");
    }

    [Test]
    public void SoftDecay_AlliedState_NeverDecays()
    {
        var data = new FactionsData();
        data.ApplyAllied("orc");

        for (int i = 0; i < FactionsData.DecayThreshold * 2; i++)
            data.OnRunEndNoNegativeAction("orc");

        Assert.That(data.GetState("orc"), Is.EqualTo("allied"),
            "Allied faction must never decay.");
    }

    [Test]
    public void SoftDecay_CustomThreshold_IsRespected()
    {
        var data = new FactionsData();
        data.ApplyNegativeAction("orc");

        // decay with threshold=2
        data.OnRunEndNoNegativeAction("orc", threshold: 2);
        data.OnRunEndNoNegativeAction("orc", threshold: 2);

        Assert.That(data.GetState("orc"), Is.EqualTo("neutral"));
    }

    // ── Multiple factions independent ────────────────────────────────────────

    [Test]
    public void MultipleFactions_IndependentState()
    {
        var data = new FactionsData();
        data.ApplyNegativeAction("orc");
        data.ApplyAllied("hall_wardens");

        Assert.That(data.GetState("orc"), Is.EqualTo("hostile"));
        Assert.That(data.GetState("hall_wardens"), Is.EqualTo("allied"));
    }

    // ── Round-trip serialization ──────────────────────────────────────────────

    [Test]
    public void Factions_RoundTrips_StateAndCounter()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.Factions.ApplyNegativeAction("orc");
        state.Factions.OnRunEndNoNegativeAction("orc");
        state.Factions.OnRunEndNoNegativeAction("orc");
        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.Factions.GetState("orc"), Is.EqualTo("hostile"));
        Assert.That(loaded.Factions.GetOrCreate("orc").RunsSinceNegativeAction, Is.EqualTo(2));
    }

    [Test]
    public void Factions_DefaultKey_OrcPresentAfterLoad()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);
        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.Factions.Factions, Contains.Key("orc"));
    }

    // ── UnshrivenGeas ─────────────────────────────────────────────────────────

    [Test]
    public void UnshrivenGeas_DefaultState_NotPushed()
    {
        var data = new UnshrivenGeasData();
        Assert.That(data.MarkerPushed, Is.False);
        Assert.That(data.MarkerPushedRun, Is.Null);
        Assert.That(data.MarkerPushedAt, Is.Null);
    }

    [Test]
    public void UnshrivenGeas_PushMarker_SetsAllFields()
    {
        var data = new UnshrivenGeasData();
        data.PushMarker(currentRun: 5);

        Assert.That(data.MarkerPushed, Is.True);
        Assert.That(data.MarkerPushedRun, Is.EqualTo(5));
        Assert.That(data.MarkerPushedAt, Is.Not.Null);
    }

    [Test]
    public void UnshrivenGeas_PushMarker_Idempotent()
    {
        var data = new UnshrivenGeasData();
        data.PushMarker(currentRun: 3);
        data.PushMarker(currentRun: 7); // second call is a no-op

        Assert.That(data.MarkerPushedRun, Is.EqualTo(3),
            "Second PushMarker call must not overwrite the first.");
    }

    [Test]
    public void UnshrivenGeas_RoundTrips_AllFields()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.UnshrivenGeas.PushMarker(currentRun: 4);
        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.UnshrivenGeas.MarkerPushed, Is.True);
        Assert.That(loaded.UnshrivenGeas.MarkerPushedRun, Is.EqualTo(4));
        Assert.That(loaded.UnshrivenGeas.MarkerPushedAt, Is.Not.Null);
    }
}
