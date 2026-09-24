using UnderWarden.Logic.Persistence;
using UnderWarden.Logic.Persistence.Namespaces;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

/// <summary>
/// Phase 6: Borrek/Vesh/Hael arcs, Marya fragments catalog, Hael hints,
/// Hollowmark meta-unlock, encounters, Under-Warden memos.
/// </summary>
[TestFixture]
public class ArcAndCatalogTests
{
    private string _tempDir = "";

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "yarl_arc_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FakePersistencePathProvider Provider() => new(_tempDir);

    // ── BorrekData ────────────────────────────────────────────────────────────

    [Test]
    public void Borrek_DefaultState_IsWary()
    {
        var data = new BorrekData();
        Assert.That(data.ArcState, Is.EqualTo("wary"));
        Assert.That(data.OrcPositiveActions, Is.EqualTo(0));
    }

    [Test]
    public void Borrek_FirstPositiveAction_TransitionsToCurious()
    {
        var data = new BorrekData();
        bool changed = data.RecordPositiveAction();

        Assert.That(data.ArcState, Is.EqualTo("curious"));
        Assert.That(changed, Is.True);
    }

    [Test]
    public void Borrek_TwoActions_StaysCurious()
    {
        var data = new BorrekData();
        data.RecordPositiveAction();
        bool changed = data.RecordPositiveAction();

        Assert.That(data.ArcState, Is.EqualTo("curious"));
        Assert.That(changed, Is.False);
        Assert.That(data.OrcPositiveActions, Is.EqualTo(2));
    }

    [Test]
    public void Borrek_ThreeActions_TransitionsToAllied()
    {
        var data = new BorrekData();
        data.RecordPositiveAction();
        data.RecordPositiveAction();
        bool changed = data.RecordPositiveAction();

        Assert.That(data.ArcState, Is.EqualTo("allied"));
        Assert.That(changed, Is.True);
    }

    [Test]
    public void Borrek_AlliedStateIsSticky()
    {
        var data = new BorrekData();
        data.RecordPositiveAction(); data.RecordPositiveAction(); data.RecordPositiveAction();
        // More actions don't regress it
        data.RecordPositiveAction();
        Assert.That(data.ArcState, Is.EqualTo("allied"));
    }

    [Test]
    public void Borrek_Sentinels_SetCorrectly()
    {
        var data = new BorrekData();
        data.RecordKnifeReceived();
        data.RecordDaughterNewsDelivered();

        Assert.That(data.KnifeReceived, Is.True);
        Assert.That(data.DaughterBloodlineNewsDelivered, Is.True);
    }

    [Test]
    public void Borrek_RoundTrips_ArcStateAndActions()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.Borrek.RecordPositiveAction();
        state.Borrek.RecordPositiveAction();
        state.Borrek.RecordKnifeReceived();
        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.Borrek.ArcState, Is.EqualTo("curious"));
        Assert.That(loaded.Borrek.OrcPositiveActions, Is.EqualTo(2));
        Assert.That(loaded.Borrek.KnifeReceived, Is.True);
        Assert.That(loaded.Borrek.DaughterBloodlineNewsDelivered, Is.False);
    }

    // ── VeshData ──────────────────────────────────────────────────────────────

    [Test]
    public void Vesh_DefaultState_NotMet()
    {
        var data = new VeshData();
        Assert.That(data.Met, Is.False);
        Assert.That(data.JobsCompleted, Is.EqualTo(0));
    }

    [Test]
    public void Vesh_RecordMet_SetsMet()
    {
        var data = new VeshData();
        data.RecordMet();
        Assert.That(data.Met, Is.True);
    }

    [Test]
    public void Vesh_RecordJobCompleted_Increments()
    {
        var data = new VeshData();
        data.RecordJobCompleted();
        data.RecordJobCompleted();
        Assert.That(data.JobsCompleted, Is.EqualTo(2));
    }

    [Test]
    public void Vesh_SpiritTransaction_SetsFlags()
    {
        var data = new VeshData();
        data.RecordSpiritReceived();
        data.RecordSpiritStoryHeard();

        Assert.That(data.SpiritReceived, Is.True);
        Assert.That(data.SpiritStoryHeard, Is.True);
    }

    [Test]
    public void Vesh_RoundTrips_AllFields()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.Vesh.RecordMet();
        state.Vesh.RecordJobCompleted();
        state.Vesh.RecordJobCompleted();
        state.Vesh.RecordSpiritReceived();
        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.Vesh.Met, Is.True);
        Assert.That(loaded.Vesh.JobsCompleted, Is.EqualTo(2));
        Assert.That(loaded.Vesh.SpiritReceived, Is.True);
        Assert.That(loaded.Vesh.SpiritStoryHeard, Is.False);
    }

    // ── HaelData ──────────────────────────────────────────────────────────────

    [Test]
    public void Hael_BranchOfPassageUnlocked_ComputedCorrectly()
    {
        var data = new HaelData();
        Assert.That(data.BranchOfPassageUnlocked, Is.False);

        data.Relationship = "allied";
        data.HintsUnlocked.AddRange(new[] { "h1", "h2", "h3", "h4" });
        Assert.That(data.BranchOfPassageUnlocked, Is.True);
    }

    [Test]
    public void Hael_BranchOfPassage_RequiresAllied_NotJustHints()
    {
        var data = new HaelData { Relationship = "trusted" };
        data.HintsUnlocked.AddRange(new[] { "h1", "h2", "h3", "h4" });
        Assert.That(data.BranchOfPassageUnlocked, Is.False);
    }

    [Test]
    public void Hael_BranchOfPassage_RequiresFourHints()
    {
        var data = new HaelData { Relationship = "allied" };
        data.HintsUnlocked.AddRange(new[] { "h1", "h2", "h3" }); // only 3
        Assert.That(data.BranchOfPassageUnlocked, Is.False);
    }

    [Test]
    public void Hael_UnlockHint_Idempotent()
    {
        var data = new HaelData();
        Assert.That(data.UnlockHint("hint_a"), Is.True);
        Assert.That(data.UnlockHint("hint_a"), Is.False);
        Assert.That(data.HintsUnlocked, Has.Count.EqualTo(1));
    }

    [Test]
    public void Hael_RoundTrips_ComputedPropertyNotStored()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.Hael.Relationship = "allied";
        state.Hael.UnlockHint("hint_a");
        state.Hael.UnlockHint("hint_b");
        state.Hael.UnlockHint("hint_c");
        state.Hael.UnlockHint("hint_d");
        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.Hael.BranchOfPassageUnlocked, Is.True);
        Assert.That(loaded.Hael.HintsUnlocked, Has.Count.EqualTo(4));

        // Verify the computed property is NOT stored as a JSON field
        var raw = System.IO.File.ReadAllText(provider.GetMainSaveFilePath());
        Assert.That(raw, Does.Not.Contain("branch_of_passage_unlocked"),
            "Computed BranchOfPassageUnlocked must not be serialized to disk.");
    }

    // ── HaelHintsData ─────────────────────────────────────────────────────────

    [Test]
    public void HaelHints_TryUnlock_StoresRecord()
    {
        var data = new HaelHintsData();
        var rec = data.TryUnlock("wend_buried_under_paths", unlockedRun: 3, hintTextRef: "hael_hint_001");

        Assert.That(rec, Is.Not.Null);
        Assert.That(rec!.Id, Is.EqualTo("wend_buried_under_paths"));
        Assert.That(rec.UnlockedRun, Is.EqualTo(3));
        Assert.That(rec.HintTextRef, Is.EqualTo("hael_hint_001"));
    }

    [Test]
    public void HaelHints_TryUnlock_Idempotent()
    {
        var data = new HaelHintsData();
        data.TryUnlock("hint_x", 1, "ref_1");
        var second = data.TryUnlock("hint_x", 2, "ref_1");

        Assert.That(second, Is.Null);
        Assert.That(data.UnlockedHints, Has.Count.EqualTo(1));
    }

    [Test]
    public void HaelHints_RoundTrips()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.HaelHints.TryUnlock("hint_passage", unlockedRun: 5, hintTextRef: "hael_hint_002");
        state.HaelHints.BranchOfPassageUnlockMarker = true;
        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.HaelHints.HasHint("hint_passage"), Is.True);
        Assert.That(loaded.HaelHints.BranchOfPassageUnlockMarker, Is.True);
        Assert.That(loaded.HaelHints.UnlockedHints[0].UnlockedRun, Is.EqualTo(5));
    }

    // ── MaryaFragmentsData ────────────────────────────────────────────────────

    [Test]
    public void MaryaFragments_TryUnlock_StoresRecord()
    {
        var data = new MaryaFragmentsData();
        var rec = data.TryUnlock("marya_001", unlockedRun: 2, place: "Wend", fragmentTextRef: "marya_001");

        Assert.That(rec, Is.Not.Null);
        Assert.That(rec!.Id, Is.EqualTo("marya_001"));
        Assert.That(rec.Place, Is.EqualTo("Wend"));
        Assert.That(rec.UnlockedRun, Is.EqualTo(2));
        Assert.That(rec.UnlockedAt, Is.Not.EqualTo(default(DateTimeOffset)));
    }

    [Test]
    public void MaryaFragments_TryUnlock_Idempotent()
    {
        var data = new MaryaFragmentsData();
        data.TryUnlock("marya_001", 1, "Wend", "marya_001");
        var second = data.TryUnlock("marya_001", 2, "Wend", "marya_001");

        Assert.That(second, Is.Null);
        Assert.That(data.Unlocked, Has.Count.EqualTo(1));
    }

    [Test]
    public void MaryaFragments_MultipleFragments_AllPersist()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.MaryaFragments.TryUnlock("marya_001", 2, "Wend", "marya_001");
        state.MaryaFragments.TryUnlock("marya_002", 3, "Crypt", "marya_002");
        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.MaryaFragments.HasUnlocked("marya_001"), Is.True);
        Assert.That(loaded.MaryaFragments.HasUnlocked("marya_002"), Is.True);
        Assert.That(loaded.MaryaFragments.Unlocked, Has.Count.EqualTo(2));
    }

    // ── HollowmarkMetaData ────────────────────────────────────────────────────

    [Test]
    public void HollowmarkMeta_DefaultFloorLevel_IsOne()
    {
        var data = new HollowmarkMetaData();
        Assert.That(data.GetFloorUnlockLevel(1), Is.EqualTo(1));
        Assert.That(data.GetFloorUnlockLevel(99), Is.EqualTo(1)); // unknown floor defaults to 1
    }

    [Test]
    public void HollowmarkMeta_AdvanceFloorLevel_IncrementsUpToMax()
    {
        var data = new HollowmarkMetaData();
        data.AdvanceFloorUnlockLevel(floor: 1, maxLevel: 3);
        Assert.That(data.GetFloorUnlockLevel(1), Is.EqualTo(2));

        data.AdvanceFloorUnlockLevel(floor: 1, maxLevel: 3);
        Assert.That(data.GetFloorUnlockLevel(1), Is.EqualTo(3));

        // At max — no further advance
        data.AdvanceFloorUnlockLevel(floor: 1, maxLevel: 3);
        Assert.That(data.GetFloorUnlockLevel(1), Is.EqualTo(3));
    }

    [Test]
    public void HollowmarkMeta_FloorLevels_IndependentPerFloor()
    {
        var data = new HollowmarkMetaData();
        data.AdvanceFloorUnlockLevel(1, 5);
        data.AdvanceFloorUnlockLevel(1, 5);
        // floor 2 is untouched
        Assert.That(data.GetFloorUnlockLevel(1), Is.EqualTo(3));
        Assert.That(data.GetFloorUnlockLevel(2), Is.EqualTo(1));
    }

    [Test]
    public void HollowmarkMeta_BetweenRunsLines_TrackCorrectly()
    {
        var data = new HollowmarkMetaData();
        data.RecordBetweenRunsLineFired("br_intro_001");
        data.RecordBetweenRunsLineFired("br_consolation_004");

        Assert.That(data.HasFiredBetweenRunsLine("br_intro_001"), Is.True);
        Assert.That(data.HasFiredBetweenRunsLine("br_consolation_004"), Is.True);
        Assert.That(data.HasFiredBetweenRunsLine("br_intro_002"), Is.False);
    }

    [Test]
    public void HollowmarkMeta_RoundTrips_FloorLevelsAndLinesFired()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.HollowmarkMeta.AdvanceFloorUnlockLevel(1, 5);
        state.HollowmarkMeta.AdvanceFloorUnlockLevel(2, 5);
        state.HollowmarkMeta.AdvanceFloorUnlockLevel(2, 5); // floor 2 at level 3
        state.HollowmarkMeta.RecordBetweenRunsLineFired("br_line_001");
        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.HollowmarkMeta.GetFloorUnlockLevel(1), Is.EqualTo(2));
        Assert.That(loaded.HollowmarkMeta.GetFloorUnlockLevel(2), Is.EqualTo(3));
        Assert.That(loaded.HollowmarkMeta.HasFiredBetweenRunsLine("br_line_001"), Is.True);
    }

    // ── EncountersData ────────────────────────────────────────────────────────

    [Test]
    public void Encounters_DefaultState_NoneRecorded()
    {
        var data = new EncountersData();
        Assert.That(data.MetBorrek, Is.False);
        Assert.That(data.MetVesh, Is.False);
        Assert.That(data.MetHael, Is.False);
        Assert.That(data.MetUnderWarden, Is.False);
    }

    [Test]
    public void Encounters_RecordMet_SetsIndividualFlags()
    {
        var data = new EncountersData();
        data.RecordMetBorrek();
        data.RecordMetHael();

        Assert.That(data.MetBorrek, Is.True);
        Assert.That(data.MetVesh, Is.False);
        Assert.That(data.MetHael, Is.True);
    }

    [Test]
    public void Encounters_RoundTrips_AllFlags()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.Encounters.RecordMetBorrek();
        state.Encounters.RecordMetVesh();
        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.Encounters.MetBorrek, Is.True);
        Assert.That(loaded.Encounters.MetVesh, Is.True);
        Assert.That(loaded.Encounters.MetHael, Is.False);
    }

    // ── UnderWardenData ───────────────────────────────────────────────────────

    [Test]
    public void UnderWarden_RecordMemoSent_IncrementsCounter()
    {
        var data = new UnderWardenData();
        data.RecordMemoSent();
        data.RecordMemoSent(newTone: "procedural_notice");

        Assert.That(data.TotalMemosSentEver, Is.EqualTo(2));
        Assert.That(data.LastMemoTone, Is.EqualTo("procedural_notice"));
    }

    [Test]
    public void UnderWarden_GrievanceLogging_Idempotent()
    {
        var data = new UnderWardenData();
        data.RecordMemoSent(newGrievanceId: "unauthorized_descent");
        data.RecordMemoSent(newGrievanceId: "unauthorized_descent"); // second fire — same grievance

        Assert.That(data.ProceduralGrievancesLogged, Has.Count.EqualTo(1),
            "Same grievance must not be logged twice.");
        Assert.That(data.TotalMemosSentEver, Is.EqualTo(2),
            "Memo counter increments regardless; grievance deduplication is separate.");
    }

    [Test]
    public void UnderWarden_RoundTrips_AllFields()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.UnderWarden.RecordMemoSent(newTone: "formal_complaint", newGrievanceId: "soul_transfer_threshold");
        state.UnderWarden.AuditAttemptedRuns = 2;
        state.MarkDirty();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.UnderWarden.TotalMemosSentEver, Is.EqualTo(1));
        Assert.That(loaded.UnderWarden.LastMemoTone, Is.EqualTo("formal_complaint"));
        Assert.That(loaded.UnderWarden.HasLoggedGrievance("soul_transfer_threshold"), Is.True);
        Assert.That(loaded.UnderWarden.AuditAttemptedRuns, Is.EqualTo(2));
        Assert.That(loaded.UnderWarden.AuditCompleted, Is.False);
    }
}
