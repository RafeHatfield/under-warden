using System.Text.Json;
using UnderWarden.Logic.Persistence;
using UnderWarden.Logic.Persistence.Namespaces;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

/// <summary>
/// Phase 1 smoke tests: round-trip serialization, atomic write mechanics, and missing-file
/// bootstrapping. These are the tests that must pass before any consumer code is written.
/// The iOS NativeAOT smoke test (export round-trip on a real device) is a separate manual step.
/// </summary>
[TestFixture]
public class PersistenceRoundTripTests
{
    private string _tempDir = "";

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "yarl_tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FakePersistencePathProvider Provider() => new(_tempDir);

    // ── Source-generation smoke test ─────────────────────────────────────────

    [Test]
    public void JsonContext_Serializes_PersistenceFile_Without_Reflection()
    {
        var file = new PersistenceFile();
        // If source generation is broken, this will throw NotSupportedException at runtime.
        var json = JsonSerializer.Serialize(file, PersistenceJsonContext.Default.PersistenceFile);
        Assert.That(json, Does.Contain("schema_version"));
        Assert.That(json, Does.Contain("namespaces"));
    }

    [Test]
    public void JsonContext_Serializes_DailySeedsFile_Without_Reflection()
    {
        var seeds = new DailySeedsFile();
        var json = JsonSerializer.Serialize(seeds, PersistenceJsonContext.Default.DailySeedsFile);
        Assert.That(json, Does.Contain("schema_version"));
        Assert.That(json, Does.Contain("records"));
    }

    // ── Round-trip: all 15 namespaces with non-trivial data ──────────────────

    [Test]
    public void RoundTrip_AllNamespaces_PreservesData()
    {
        var state = BuildNonTrivialState();
        state.MarkDirty();
        var provider = Provider();
        state.Flush(provider);

        var loaded = PersistentRunState.LoadFromDisk(provider);

        // run_counter
        Assert.That(loaded.RunCounter.TotalRuns, Is.EqualTo(5));
        Assert.That(loaded.RunCounter.FirstRunStartedAt, Is.Not.Null);

        // past_sashas
        Assert.That(loaded.PastSashas.Records, Has.Count.EqualTo(1));
        var sasha = loaded.PastSashas.Records[0];
        Assert.That(sasha.DiedRun, Is.EqualTo(3));
        Assert.That(sasha.DiedFloor, Is.EqualTo(9));
        Assert.That(sasha.CauseOfDeath, Is.EqualTo("monster"));
        Assert.That(sasha.KillerSpecies, Is.EqualTo("orc_brute"));
        Assert.That(sasha.GearCarried, Has.Count.EqualTo(2));
        Assert.That(sasha.GearCarried[0].TypeId, Is.EqualTo("shortsword"));
        Assert.That(sasha.GearCarried[0].Enchantment, Is.EqualTo(1));
        Assert.That(sasha.GearCarried[1].Condition, Is.EqualTo("corroded"));

        // factions
        var orcFaction = loaded.Factions.Factions["orc"];
        Assert.That(orcFaction.State, Is.EqualTo("hostile"));
        Assert.That(orcFaction.RunsSinceNegativeAction, Is.EqualTo(2));

        // borrek
        Assert.That(loaded.Borrek.ArcState, Is.EqualTo("curious"));
        Assert.That(loaded.Borrek.OrcPositiveActions, Is.EqualTo(1));

        // vesh
        Assert.That(loaded.Vesh.Met, Is.True);
        Assert.That(loaded.Vesh.JobsCompleted, Is.EqualTo(2));

        // hael
        Assert.That(loaded.Hael.Relationship, Is.EqualTo("trusted"));
        Assert.That(loaded.Hael.HintsUnlocked, Has.Count.EqualTo(2));
        Assert.That(loaded.Hael.BranchOfPassageUnlocked, Is.False); // needs "allied" + 4 hints

        // marya_fragments
        Assert.That(loaded.MaryaFragments.Unlocked, Has.Count.EqualTo(1));
        Assert.That(loaded.MaryaFragments.Unlocked[0].Id, Is.EqualTo("marya_taught_by_artificer"));

        // hael_hints
        Assert.That(loaded.HaelHints.UnlockedHints, Has.Count.EqualTo(1));
        Assert.That(loaded.HaelHints.BranchOfPassageUnlockMarker, Is.False);

        // freed_past_selves
        Assert.That(loaded.FreedPastSelves.Records, Has.Count.EqualTo(1));
        Assert.That(loaded.FreedPastSelves.Records[0].FreedFloor, Is.EqualTo(14));

        // unshriven_geas
        Assert.That(loaded.UnshrivenGeas.MarkerPushed, Is.True);
        Assert.That(loaded.UnshrivenGeas.MarkerPushedRun, Is.EqualTo(4));

        // hollowmark_meta
        Assert.That(loaded.HollowmarkMeta.FloorUnlockLevels.ContainsKey("1"), Is.True);
        Assert.That(loaded.HollowmarkMeta.BetweenRunsLinesFired, Does.Contain("br_intro_001"));

        // achievements
        Assert.That(loaded.Achievements.Unlocked, Has.Count.EqualTo(1));
        Assert.That(loaded.Achievements.Unlocked[0].Id, Is.EqualTo("first_run_complete"));

        // encounters
        Assert.That(loaded.Encounters.MetBorrek, Is.True);
        Assert.That(loaded.Encounters.MetVesh, Is.True);
        Assert.That(loaded.Encounters.MetHael, Is.False);

        // hollowmark_span (reserved)
        Assert.That(loaded.HollowmarkSpan.RemainingSpan, Is.Null);

        // under_warden
        Assert.That(loaded.UnderWarden.TotalMemosSentEver, Is.EqualTo(3));
        Assert.That(loaded.UnderWarden.LastMemoTone, Is.EqualTo("procedural_notice"));
        Assert.That(loaded.UnderWarden.ProceduralGrievancesLogged, Has.Count.EqualTo(1));
        Assert.That(loaded.UnderWarden.AuditCompleted, Is.False);
    }

    // ── Missing file → fresh defaults ────────────────────────────────────────

    [Test]
    public void LoadFromDisk_MissingFile_ReturnsFreshDefaults()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        Assert.That(state.RunCounter.TotalRuns, Is.EqualTo(0));
        Assert.That(state.PastSashas.Records, Is.Empty);
        Assert.That(state.Achievements.Unlocked, Is.Empty);
        Assert.That(File.Exists(provider.GetMainSaveFilePath()), Is.False,
            "Fresh state must not write to disk until MarkDirty + Flush.");
    }

    // ── Atomic write: original intact if .tmp exists but move hasn't fired ───

    [Test]
    public void AtomicWrite_OriginalIntact_If_TmpExistsFromPreviousCrash()
    {
        // Simulate a prior crash: .tmp file left on disk.
        var provider = Provider();
        var tmpPath = provider.GetMainSaveFilePath() + ".tmp";
        File.WriteAllText(tmpPath, "{ corrupted }");

        // Save a valid state.
        var state = BuildNonTrivialState();
        state.MarkDirty();
        state.Flush(provider);

        // Both the main file and the .tmp cleanup should succeed.
        Assert.That(File.Exists(provider.GetMainSaveFilePath()), Is.True);
        // .tmp should be gone (overwritten by the move).
        Assert.That(File.Exists(tmpPath), Is.False);

        // Main file should be valid JSON with expected data.
        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.RunCounter.TotalRuns, Is.EqualTo(5));
    }

    // ── Dirty tracking ───────────────────────────────────────────────────────

    [Test]
    public void MarkDirty_IsDirty_ClearedByFlush()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        Assert.That(state.IsDirty, Is.False);
        state.MarkDirty();
        Assert.That(state.IsDirty, Is.True);
        state.Flush(provider);
        Assert.That(state.IsDirty, Is.False);
    }

    // ── Backup rotation ──────────────────────────────────────────────────────

    [Test]
    public void BackupRotation_KeepsAtMost5Backups()
    {
        var provider = Provider();

        // Write 7 successive saves — should keep only 5 backups.
        for (int i = 0; i < 7; i++)
        {
            var state = PersistentRunState.LoadFromDisk(provider);
            state.RunCounter.TotalRuns = i + 1;
            state.MarkDirty();
            state.Flush(provider);
            // Small sleep to ensure distinct timestamps in backup filenames.
            Thread.Sleep(10);
        }

        var backups = Directory.GetFiles(provider.GetBackupDirectory(), "yarl_persistence.*.json");
        Assert.That(backups.Length, Is.LessThanOrEqualTo(5));
    }

    // ── Corrupted file → graceful fallback ───────────────────────────────────

    [Test]
    public void LoadFromDisk_CorruptedFile_FallsBackToDefaults()
    {
        var provider = Provider();
        File.WriteAllText(provider.GetMainSaveFilePath(), "{ this is not valid json ]]]");

        string? loggedError = null;
        var state = PersistentRunState.LoadFromDisk(provider, err => loggedError = err);

        Assert.That(state.RunCounter.TotalRuns, Is.EqualTo(0));
        Assert.That(loggedError, Is.Not.Null, "Should log the parse error.");
    }

    // ── Daily seeds round-trip ───────────────────────────────────────────────

    [Test]
    public void DailySeeds_RoundTrip_PreservesRecords()
    {
        var provider = Provider();
        var seeds = new DailySeedsFile();
        var record = seeds.GetOrCreate("2026-04-22");
        record.Seed = "abc123-pinned";
        record.BestScore = 4250;
        record.BestFloor = 17;
        record.RunsCompleted = 2;
        record.FirstRunCompletedAt = DateTimeOffset.UtcNow;

        PersistentRunState.FlushDailySeeds(seeds, provider);
        var loaded = PersistentRunState.LoadDailySeedsFromDisk(provider);

        Assert.That(loaded.Records.ContainsKey("2026-04-22"), Is.True);
        var r = loaded.Records["2026-04-22"];
        Assert.That(r.Seed, Is.EqualTo("abc123-pinned"));
        Assert.That(r.BestScore, Is.EqualTo(4250));
        Assert.That(r.BestFloor, Is.EqualTo(17));
        Assert.That(r.RunsCompleted, Is.EqualTo(2));
    }

    // ── Hael computed property ────────────────────────────────────────────────

    [Test]
    public void Hael_BranchOfPassageUnlocked_IsComputedNotStored()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.Hael.Met = true;
        state.Hael.Relationship = "allied";
        // Fewer than 4 hints — not unlocked yet.
        state.Hael.UnlockHint("hint_a");
        state.Hael.UnlockHint("hint_b");
        state.Hael.UnlockHint("hint_c");
        Assert.That(state.Hael.BranchOfPassageUnlocked, Is.False);

        state.Hael.UnlockHint("hint_d");
        Assert.That(state.Hael.BranchOfPassageUnlocked, Is.True);

        // Persisting and reloading should still have the same derived result.
        state.MarkDirty();
        state.Flush(provider);
        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.Hael.BranchOfPassageUnlocked, Is.True);
    }

    // ── HollowmarkMeta: stable IDs not broken by repeated calls ─────────────

    [Test]
    public void HollowmarkMeta_BetweenRunsLinesFired_NoDuplicates()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);

        state.HollowmarkMeta.RecordBetweenRunsLineFired("br_001");
        state.HollowmarkMeta.RecordBetweenRunsLineFired("br_001"); // duplicate
        state.HollowmarkMeta.RecordBetweenRunsLineFired("br_002");

        Assert.That(state.HollowmarkMeta.BetweenRunsLinesFired.Count, Is.EqualTo(2));

        state.MarkDirty();
        state.Flush(provider);
        var loaded = PersistentRunState.LoadFromDisk(provider);
        Assert.That(loaded.HollowmarkMeta.BetweenRunsLinesFired.Count, Is.EqualTo(2));
    }

    // ── UnderWarden: total_memos_sent_ever is independently tracked ──────────

    [Test]
    public void UnderWarden_TotalMemosSentEver_IndependentOfGrievanceCount()
    {
        var state = PersistentRunState.LoadFromDisk(Provider());

        // Memo with a grievance
        state.UnderWarden.RecordMemoSent("procedural_notice", "unauthorized_descent");
        // Memo without a new grievance (follow-up / acknowledgement memo)
        state.UnderWarden.RecordMemoSent();
        state.UnderWarden.RecordMemoSent();

        Assert.That(state.UnderWarden.TotalMemosSentEver, Is.EqualTo(3));
        Assert.That(state.UnderWarden.ProceduralGrievancesLogged.Count, Is.EqualTo(1),
            "Only one grievance logged even though 3 memos sent.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PersistentRunState BuildNonTrivialState()
    {
        var state = PersistentRunState.LoadFromDisk(new FakePersistencePathProvider(
            Path.Combine(Path.GetTempPath(), "yarl_build_" + Guid.NewGuid())));

        state.RunCounter.TotalRuns = 5;
        state.RunCounter.FirstRunStartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        state.PastSashas.AddRecord(3, 9, "monster", "orc_brute", new List<GearItemRecord>
        {
            new() { TypeId = "shortsword", Enchantment = 1, Condition = "normal" },
            new() { TypeId = "leather_armor", Enchantment = 0, Condition = "corroded" },
        });

        state.Factions.Factions["orc"].State = "hostile";
        state.Factions.Factions["orc"].RunsSinceNegativeAction = 2;

        state.Borrek.ArcState = "curious";
        state.Borrek.OrcPositiveActions = 1;

        state.Vesh.Met = true;
        state.Vesh.JobsCompleted = 2;

        state.Hael.Relationship = "trusted";
        state.Hael.UnlockHint("wend_buried_under_paths");
        state.Hael.UnlockHint("branch_of_passage_clue");

        state.MaryaFragments.TryUnlock("marya_taught_by_artificer", 2, "Wend", "marya_001");

        state.HaelHints.TryUnlock("wend_buried_under_paths", 2, "hael_hint_001");

        state.FreedPastSelves.AddRecord(7, 5, 14);

        state.UnshrivenGeas.PushMarker(4);

        state.HollowmarkMeta.FloorUnlockLevels["1"] = 2;
        state.HollowmarkMeta.RecordBetweenRunsLineFired("br_intro_001");

        state.Achievements.TryUnlock("first_run_complete");

        state.Encounters.MetBorrek = true;
        state.Encounters.MetVesh = true;

        state.UnderWarden.RecordMemoSent("polite");
        state.UnderWarden.RecordMemoSent("procedural_notice", "unauthorized_descent");
        state.UnderWarden.RecordMemoSent();

        return state;
    }
}

// ── Test helper ──────────────────────────────────────────────────────────────

internal sealed class FakePersistencePathProvider : IPersistencePathProvider
{
    private readonly string _dir;

    public FakePersistencePathProvider(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
    }

    public string GetMainSaveFilePath()    => Path.Combine(_dir, "yarl_persistence.json");
    public string GetDailySeedsFilePath()  => Path.Combine(_dir, "yarl_daily_seeds.json");
    public string GetSettingsFilePath()    => Path.Combine(_dir, "yarl_settings.json");
    public string GetBackupDirectory()     => Path.Combine(_dir, "backups");
}
