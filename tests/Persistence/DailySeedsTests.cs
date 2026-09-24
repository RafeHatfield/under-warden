using UnderWarden.Logic.Persistence;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

[TestFixture]
public class DailySeedsTests
{
    private string _tempDir = "";

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "yarl_ds_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FakePersistencePathProvider Provider() => new(_tempDir);

    // ── RecordRun ─────────────────────────────────────────────────────────────

    [Test]
    public void RecordRun_SetsAllFields_OnFirstRun()
    {
        var file = new DailySeedsFile();
        file.RecordRun("2026-05-04", seed: "seed_abc", score: 1200, floor: 8);

        var rec = file.Records["2026-05-04"];
        Assert.That(rec.Seed, Is.EqualTo("seed_abc"));
        Assert.That(rec.BestScore, Is.EqualTo(1200));
        Assert.That(rec.BestFloor, Is.EqualTo(8));
        Assert.That(rec.RunsCompleted, Is.EqualTo(1));
        Assert.That(rec.FirstRunCompletedAt, Is.Not.Null);
    }

    [Test]
    public void RecordRun_UpdatesBestScore_IfHigher()
    {
        var file = new DailySeedsFile();
        file.RecordRun("2026-05-04", "seed", 800, 5);
        file.RecordRun("2026-05-04", "seed", 1500, 3);

        var rec = file.Records["2026-05-04"];
        Assert.That(rec.BestScore, Is.EqualTo(1500));
        Assert.That(rec.BestFloor, Is.EqualTo(5), "BestFloor should not regress.");
        Assert.That(rec.RunsCompleted, Is.EqualTo(2));
    }

    [Test]
    public void RecordRun_UpdatesBestFloor_IfHigher()
    {
        var file = new DailySeedsFile();
        file.RecordRun("2026-05-04", "seed", 1000, 6);
        file.RecordRun("2026-05-04", "seed", 500, 12);

        var rec = file.Records["2026-05-04"];
        Assert.That(rec.BestFloor, Is.EqualTo(12));
        Assert.That(rec.BestScore, Is.EqualTo(1000), "BestScore should not regress.");
    }

    [Test]
    public void RecordRun_FirstRunTimestamp_NotOverwrittenOnSubsequentRuns()
    {
        var file = new DailySeedsFile();
        file.RecordRun("2026-05-04", "seed", 100, 1);
        var firstTimestamp = file.Records["2026-05-04"].FirstRunCompletedAt;

        System.Threading.Thread.Sleep(5); // ensure clock tick
        file.RecordRun("2026-05-04", "seed", 200, 2);

        Assert.That(file.Records["2026-05-04"].FirstRunCompletedAt, Is.EqualTo(firstTimestamp),
            "FirstRunCompletedAt must not be overwritten by subsequent runs.");
    }

    // ── Multiple dates independent ────────────────────────────────────────────

    [Test]
    public void MultipleDates_StoreIndependently()
    {
        var file = new DailySeedsFile();
        file.RecordRun("2026-05-03", "seed_a", 500, 4);
        file.RecordRun("2026-05-04", "seed_b", 1200, 9);

        Assert.That(file.Records, Has.Count.EqualTo(2));
        Assert.That(file.Records["2026-05-03"].BestScore, Is.EqualTo(500));
        Assert.That(file.Records["2026-05-04"].BestScore, Is.EqualTo(1200));
    }

    // ── Load/Save ─────────────────────────────────────────────────────────────

    [Test]
    public void LoadDailySeeds_MissingFile_ReturnsEmptyFile()
    {
        var provider = Provider();
        var loaded = PersistentRunState.LoadDailySeedsFromDisk(provider);

        Assert.That(loaded.Records, Is.Empty);
    }

    [Test]
    public void DailySeeds_RoundTrips_MultipleRecords()
    {
        var provider = Provider();
        var seeds = new DailySeedsFile();
        seeds.RecordRun("2026-05-03", "seed_a", 400, 5);
        seeds.RecordRun("2026-05-04", "seed_b", 1100, 10);
        seeds.RecordRun("2026-05-04", "seed_b", 1500, 7); // second run same day

        PersistentRunState.FlushDailySeeds(seeds, provider);
        var loaded = PersistentRunState.LoadDailySeedsFromDisk(provider);

        Assert.That(loaded.Records, Has.Count.EqualTo(2));
        Assert.That(loaded.Records["2026-05-04"].RunsCompleted, Is.EqualTo(2));
        Assert.That(loaded.Records["2026-05-04"].BestScore, Is.EqualTo(1500));
        Assert.That(loaded.Records["2026-05-03"].BestScore, Is.EqualTo(400));
    }

    [Test]
    public void DailySeeds_IndependentOfMainFile()
    {
        var provider = Provider();

        // Write main file
        var state = PersistentRunState.LoadFromDisk(provider);
        state.RunCounter.TotalRuns = 5;
        state.MarkDirty();
        state.Flush(provider);

        // Write daily seeds file
        var seeds = new DailySeedsFile();
        seeds.RecordRun("2026-05-04", "seed", 999, 7);
        PersistentRunState.FlushDailySeeds(seeds, provider);

        // Reload both and verify they don't interfere
        var loadedState = PersistentRunState.LoadFromDisk(provider);
        var loadedSeeds = PersistentRunState.LoadDailySeedsFromDisk(provider);

        Assert.That(loadedState.RunCounter.TotalRuns, Is.EqualTo(5));
        Assert.That(loadedSeeds.Records["2026-05-04"].BestScore, Is.EqualTo(999));
    }

    [Test]
    public void DailySeeds_CorruptedFile_FallsBackToEmpty_DoesNotThrow()
    {
        var provider = Provider();
        File.WriteAllText(provider.GetDailySeedsFilePath(), "{ not valid json }}}");

        DailySeedsFile? result = null;
        var errors = new List<string>();
        Assert.DoesNotThrow(() =>
            result = PersistentRunState.LoadDailySeedsFromDisk(provider, err => errors.Add(err)));

        Assert.That(result!.Records, Is.Empty, "Corrupted daily seeds should fall back to empty.");
        Assert.That(errors, Has.Count.GreaterThan(0), "Corruption should log an error.");
    }

    [Test]
    public void DailySeeds_LeaderboardSynced_DefaultsFalse()
    {
        var file = new DailySeedsFile();
        file.RecordRun("2026-05-04", "seed", 100, 1);
        Assert.That(file.Records["2026-05-04"].LeaderboardSynced, Is.False,
            "Reserved leaderboard flag must default to false.");
    }
}
