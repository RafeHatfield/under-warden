using System.Threading;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Persistence.MidRun;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

/// <summary>
/// Resume-correctness for the M1.4 4b device path — exercised headlessly through the REAL file layer
/// (MidRunFile.SaveMidRunToFile/LoadMidRunFromFile + MidRunAutosaveWriter), not just the serializer API.
/// The on-device wiring (Main.cs seams) is covered by the manual acceptance checklist in the PR.
/// </summary>
[TestFixture]
public class MidRunResumeFileTests
{
    private const string ContentYaml = @"
monsters:
  orc_grunt:
    name: Orc
    stats: { hp: 20, xp: 25, damage_min: 3, damage_max: 5, strength: 12, dexterity: 10, constitution: 10, accuracy: 3, evasion: 1 }
    char: o
    ai_type: basic
    blocks: true
    faction: orc
consumables:
  healing_potion: { name: Healing Potion, heal_amount: 20 }
";

    private static GameState DungeonFloor(int seed)
    {
        var b = new ContentLoader().LoadAll(ContentYaml);
        var ef = new EntityFactory(startId: 1);
        var builder = new DungeonFloorBuilder(
            LevelTemplateRegistry.FromYaml("levels: {}"),
            new MonsterFactory(b.Monsters, ef), new ItemFactory(b.Items, ef), new ConsumableFactory(b.Consumables, ef));
        return builder.Build(3, new SeededRandom(seed));
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"midrun_{Guid.NewGuid():N}.json");

    // (1) Save→load→save byte-identity through the REAL device file path (proves the file layer adds no drift).
    [Test]
    public void FilePath_SaveLoadSave_IsByteIdentical()
    {
        var state = DungeonFloor(1337);
        string p1 = TempPath(), p2 = TempPath();
        try
        {
            MidRunFile.SaveMidRunToFile(MidRunSerializer.SaveMidRun(state), p1);
            var loaded = MidRunFile.LoadMidRunFromFile(p1);
            Assert.That(loaded.IsOk, Is.True);
            var rebuilt = MidRunSerializer.LoadMidRun(loaded.Save!);
            MidRunFile.SaveMidRunToFile(MidRunSerializer.SaveMidRun(rebuilt), p2);
            Assert.That(File.ReadAllBytes(p2), Is.EqualTo(File.ReadAllBytes(p1)),
                "the real device save→load→save must be byte-identical.");
        }
        finally { File.Delete(p1); File.Delete(p2); }
    }

    // (2) IdAllocator watermark is LIVE in a real dungeon-path save; post-resume ids never collide.
    [Test]
    public void IdAllocatorWatermark_IsLive_AndPostResumeIdsNeverCollide()
    {
        var state = DungeonFloor(2024);
        Assert.That(state.IdAllocator, Is.Not.Null, "real dungeon Build must expose a live IdAllocator.");

        string p = TempPath();
        try
        {
            MidRunFile.SaveMidRunToFile(MidRunSerializer.SaveMidRun(state), p);
            var loaded = MidRunSerializer.LoadMidRun(MidRunFile.LoadMidRunFromFile(p).Save!);
            Assert.That(loaded.IdAllocator, Is.Not.Null, "watermark must survive the round-trip.");

            var savedIds = MidRunSerializer.SaveMidRun(loaded).Entities.Entities.Select(e => e.Id).ToHashSet();
            for (int i = 0; i < 25; i++)
            {
                int fresh = loaded.IdAllocator!.Next();
                Assert.That(savedIds.Contains(fresh), Is.False, $"allocated id {fresh} collides with a saved id.");
            }
        }
        finally { File.Delete(p); }
    }

    // (3) A terminal-state save is detected as game-over → resume routes to the death flow, not play.
    [Test]
    public void TerminalStateSave_LoadsAsGameOver()
    {
        var state = DungeonFloor(7);
        state.PlayerFighter.TakeDamage(999999);      // kill the player → IsGameOver
        Assume.That(state.IsGameOver, Is.True);

        string p = TempPath();
        try
        {
            MidRunFile.SaveMidRunToFile(MidRunSerializer.SaveMidRun(state), p);
            var loaded = MidRunSerializer.LoadMidRun(MidRunFile.LoadMidRunFromFile(p).Save!);
            Assert.That(loaded.IsGameOver, Is.True, "a terminal save must load as game-over so it routes to the death flow.");
        }
        finally { File.Delete(p); }
    }

    // (4) A corrupt file archives (never deletes) and reports Corrupt.
    [Test]
    public void CorruptFile_ReportsCorrupt_AndArchivesInsteadOfDeleting()
    {
        string p = TempPath();
        try
        {
            File.WriteAllText(p, "not json {{{ ]]]");
            Assert.That(MidRunFile.LoadMidRunFromFile(p).Status, Is.EqualTo(MidRunLoadStatus.Corrupt));

            var archived = MidRunFile.ArchiveCorrupt(p);
            Assert.That(archived, Is.Not.Null);
            Assert.That(File.Exists(p), Is.False, "the corrupt file must be moved aside, not left in place.");
            Assert.That(File.Exists(archived!), Is.True, "the corrupt file must be archived, never deleted.");
            File.Delete(archived);
        }
        finally { if (File.Exists(p)) File.Delete(p); }
    }

    // (5) The off-critical-path writer: latest-wins on background writes, and FlushSync is durable.
    [Test]
    public void AutosaveWriter_LatestWins_AndFlushSyncIsDurable()
    {
        var s1 = DungeonFloor(11);
        var s2 = DungeonFloor(22);
        string p = TempPath();
        try
        {
            using (var w = new MidRunAutosaveWriter(p))
            {
                w.RequestWrite(MidRunSerializer.SaveMidRun(s1));
                w.RequestWrite(MidRunSerializer.SaveMidRun(s2));   // supersedes s1
                w.WaitForIdle();
                var afterBg = MidRunFile.LoadMidRunFromFile(p);
                Assert.That(afterBg.IsOk, Is.True);
                Assert.That(afterBg.Save!.RngSeed, Is.EqualTo(22), "latest queued snapshot must win.");

                w.FlushSync(MidRunSerializer.SaveMidRun(s1));       // synchronous — durable immediately
                var afterFlush = MidRunFile.LoadMidRunFromFile(p);
                Assert.That(afterFlush.Save!.RngSeed, Is.EqualTo(11), "FlushSync must be durable on return.");
            }
        }
        finally { File.Delete(p); }
    }

    // (6) BLOCKING-fix regression: a stalled background write holding an OLDER snapshot must NOT
    // overwrite a newer FlushSync. Forces the interleaving deterministically via the test seam.
    [Test]
    public void FlushSync_IsNotOvertakenByStaleBackgroundWrite()
    {
        var older = DungeonFloor(11);   // RngSeed 11 — queued first (lower seq)
        var newer = DungeonFloor(22);   // RngSeed 22 — FlushSync'd second (higher seq)
        string p = TempPath();
        try
        {
            using var dequeued = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            using var w = new MidRunAutosaveWriter(p);
            w.OnAfterDequeueForTest = _ => { dequeued.Set(); release.Wait(); };  // stall the background write pre-gate

            w.RequestWrite(MidRunSerializer.SaveMidRun(older));   // worker dequeues (older, seq=1), stalls
            Assert.That(dequeued.Wait(TimeSpan.FromSeconds(5)), Is.True, "background worker never dequeued.");

            w.FlushSync(MidRunSerializer.SaveMidRun(newer));      // seq=2 → written, lastWrittenSeq=2
            release.Set();                                        // worker resumes → tries to write older (seq=1) → SKIP
            w.WaitForIdle();

            var final = MidRunFile.LoadMidRunFromFile(p);
            Assert.That(final.Save!.RngSeed, Is.EqualTo(22),
                "the newer FlushSync snapshot must survive — a stale background write must not land after it.");
        }
        finally { File.Delete(p); }
    }
}
