using UnderWarden.Logic.Content;
using UnderWarden.Logic.Persistence;
using UnderWarden.Tests.Persistence;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

/// <summary>
/// Phase 3 tests: MemoDeliveryEvaluator incident detection, memo queuing,
/// slot filling, counter management, and deduplication logic.
///
/// Uses real YAML files from config/under_warden/ loaded once in [OneTimeSetUp].
/// PersistentRunState is created as a fresh in-memory instance per test using
/// a temp directory (same pattern as UnderWardenDataTests).
/// </summary>
[TestFixture]
public class MemoDeliveryEvaluatorTests
{
    private MemoRegistry _registry = null!;
    private MemoDeliveryEvaluator _evaluator = null!;
    private string _tempDir = "";

    [OneTimeSetUp]
    public void LoadRegistry()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var configDir = Path.GetFullPath(
            Path.Combine(testDir, "..", "..", "..", "..", "config", "under_warden"));

        var memosYaml     = File.ReadAllText(Path.Combine(configDir, "memos.yaml"));
        var causeNamesYaml = File.ReadAllText(Path.Combine(configDir, "cause_display_names.yaml"));

        _registry  = MemoRegistry.LoadFromYaml(memosYaml, causeNamesYaml, new AotObjectFactory());
        _evaluator = new MemoDeliveryEvaluator();
    }

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "yarl_mde_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private PersistentRunState FreshState() =>
        PersistentRunState.LoadFromDisk(new FakePersistencePathProvider(_tempDir));

    private static PostRunContext Died(int floor, string? cause = null, string? killer = null, int run = 1) =>
        new(Died: true, CauseOfDeath: cause, KillerSpecies: killer, FloorReached: floor, RunNumber: run);

    private static PostRunContext Survived(int floor = 10, int run = 1) =>
        new(Died: false, CauseOfDeath: null, KillerSpecies: null, FloorReached: floor, RunNumber: run);

    // ── death_first ───────────────────────────────────────────────────────────

    [Test]
    public void Death_QueuesDeath_First_Memo_On_First_Death()
    {
        var state = FreshState();
        var ctx = Died(floor: 5, cause: "orc_brute", run: 1);

        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        var memos = state.UnderWarden.PendingMemos;
        Assert.That(memos.Any(m => m.Key == "polite.death_first"), Is.True,
            "Expected polite.death_first to be queued on first death");
    }

    [Test]
    public void Death_DoesNotRequeue_Death_First_Memo_On_Second_Death()
    {
        var state = FreshState();
        // Manually log the grievance so it looks like it already fired
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");

        var ctx = Died(floor: 5, cause: "orc_brute", run: 2);

        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        var deathFirstMemos = state.UnderWarden.PendingMemos
            .Where(m => m.Key == "polite.death_first")
            .ToList();

        Assert.That(deathFirstMemos, Is.Empty,
            "polite.death_first must not re-fire after it has been logged");
    }

    // ── floor_low ─────────────────────────────────────────────────────────────

    [Test]
    public void Death_QueuesFloorLow_When_DiedOnFloor3()
    {
        var state = FreshState();
        // Seed death_first so it doesn't interfere with the count assertion
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");

        var ctx = Died(floor: 3, run: 2);

        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(state.UnderWarden.PendingMemos.Any(m => m.Key == "polite.floor_low"), Is.True,
            "polite.floor_low should fire for floor <= 3");
    }

    [Test]
    public void Death_DoesNotQueueFloorLow_When_DiedOnFloor4()
    {
        var state = FreshState();
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");

        var ctx = Died(floor: 4, run: 2);

        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(state.UnderWarden.PendingMemos.Any(m => m.Key == "polite.floor_low"), Is.False,
            "polite.floor_low must not fire for floor > 3");
    }

    // ── cause_trap ────────────────────────────────────────────────────────────

    [Test]
    public void Death_QueuesCauseTrap_For_SpikeTrap()
    {
        var state = FreshState();
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");

        var ctx = Died(floor: 5, cause: "spike_trap", run: 2);

        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(state.UnderWarden.PendingMemos.Any(m => m.Key == "polite.cause_trap"), Is.True,
            "polite.cause_trap should fire for spike_trap");
    }

    [Test]
    public void Death_QueuesCauseTrap_For_OwnTrap()
    {
        var state = FreshState();
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");

        var ctx = Died(floor: 5, cause: "own_trap", run: 2);

        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(state.UnderWarden.PendingMemos.Any(m => m.Key == "polite.cause_trap"), Is.True,
            "polite.cause_trap should fire for own_trap");
    }

    [Test]
    public void Death_DoesNotQueueCauseTrap_For_OrcBrute()
    {
        var state = FreshState();
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");

        var ctx = Died(floor: 5, cause: "orc_brute", run: 2);

        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(state.UnderWarden.PendingMemos.Any(m => m.Key == "polite.cause_trap"), Is.False,
            "polite.cause_trap must not fire for non-trap causes");
    }

    // ── cause_acid ────────────────────────────────────────────────────────────

    [Test]
    public void Death_QueuesCauseAcid_For_AcidPool()
    {
        var state = FreshState();
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");

        var ctx = Died(floor: 5, cause: "acid_pool", run: 2);

        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(state.UnderWarden.PendingMemos.Any(m => m.Key == "polite.cause_acid"), Is.True,
            "polite.cause_acid should fire for acid_pool");
    }

    // ── multi-memo fire ───────────────────────────────────────────────────────

    [Test]
    public void Death_CanQueueMultipleMemos_For_FirstDeathOnFloor2ByTrap()
    {
        var state = FreshState();
        var ctx = Died(floor: 2, cause: "spike_trap", run: 1);

        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        var keys = state.UnderWarden.PendingMemos.Select(m => m.Key).ToList();

        Assert.That(keys, Contains.Item("polite.death_first"),  "death_first should fire");
        Assert.That(keys, Contains.Item("polite.floor_low"),    "floor_low should fire (floor 2)");
        Assert.That(keys, Contains.Item("polite.cause_trap"),   "cause_trap should fire (spike_trap)");
        Assert.That(state.UnderWarden.PendingMemos, Has.Count.EqualTo(3),
            "Exactly 3 memos: death_first + floor_low + cause_trap");
    }

    // ── survival ─────────────────────────────────────────────────────────────

    [Test]
    public void Survive_QueuesNoMemos()
    {
        var state = FreshState();
        var ctx = Survived(floor: 10, run: 1);

        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(state.UnderWarden.PendingMemos, Is.Empty,
            "No memos should fire on survival");
        Assert.That(state.IsDirty, Is.False,
            "State must not be marked dirty if no memos were added");
    }

    // ── Hall Warden possession thresholds ─────────────────────────────────────

    [Test]
    public void HallWarden_First_Queues_PoliteMemo()
    {
        var state = FreshState();

        _evaluator.EvaluateHallWardenPossession(state, _registry, runNumber: 1);

        Assert.That(state.UnderWarden.HallWardenPossessionsTotal, Is.EqualTo(1));
        Assert.That(state.UnderWarden.PendingMemos.Any(m => m.Key == "polite.hall_warden_possession"), Is.True,
            "polite.hall_warden_possession should fire at count=1");
    }

    [Test]
    public void HallWarden_Third_Queues_ProceduralNotice()
    {
        var state = FreshState();

        _evaluator.EvaluateHallWardenPossession(state, _registry, runNumber: 1);
        _evaluator.EvaluateHallWardenPossession(state, _registry, runNumber: 2);
        _evaluator.EvaluateHallWardenPossession(state, _registry, runNumber: 3);

        Assert.That(state.UnderWarden.HallWardenPossessionsTotal, Is.EqualTo(3));
        Assert.That(
            state.UnderWarden.PendingMemos.Any(m => m.Key == "procedural_notice.hall_warden_possession"),
            Is.True,
            "procedural_notice.hall_warden_possession should fire at count=3");
    }

    [Test]
    public void HallWarden_Sixth_Queues_FormalComplaint()
    {
        var state = FreshState();

        for (var i = 1; i <= 6; i++)
            _evaluator.EvaluateHallWardenPossession(state, _registry, runNumber: i);

        Assert.That(state.UnderWarden.HallWardenPossessionsTotal, Is.EqualTo(6));
        Assert.That(
            state.UnderWarden.PendingMemos.Any(m => m.Key == "formal_complaint.hall_warden_possession"),
            Is.True,
            "formal_complaint.hall_warden_possession should fire at count=6");
    }

    [Test]
    public void HallWarden_Seventh_DoesNotRequeue_FormalComplaint()
    {
        var state = FreshState();

        for (var i = 1; i <= 7; i++)
            _evaluator.EvaluateHallWardenPossession(state, _registry, runNumber: i);

        Assert.That(state.UnderWarden.HallWardenPossessionsTotal, Is.EqualTo(7));

        var formalComplaintCount = state.UnderWarden.PendingMemos
            .Count(m => m.Key == "formal_complaint.hall_warden_possession");

        Assert.That(formalComplaintCount, Is.EqualTo(1),
            "formal_complaint.hall_warden_possession must fire at most once (7th call should not re-queue)");
    }

    // ── Slot filling ──────────────────────────────────────────────────────────

    [Test]
    public void FormattedMemo_HasSlotsFilled()
    {
        var state = FreshState();
        // Seed death_first so we only get floor_low
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");
        // Set a known best floor
        state.RunCounter.UpdateBestFloor(12);

        var ctx = Died(floor: 2, cause: "orc_brute", run: 5);

        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        var floorLow = state.UnderWarden.PendingMemos.FirstOrDefault(m => m.Key == "polite.floor_low");
        Assert.That(floorLow, Is.Not.Null, "polite.floor_low should have been queued");

        // The formatted body should contain the floor number, not the raw slot token
        Assert.That(floorLow!.Body, Does.Contain("Floor 2").Or.Contain("floor 2"),
            "Formatted body should contain resolved floor value, not {floor} placeholder");
        Assert.That(floorLow.Body, Does.Not.Contain("{floor}"),
            "{floor} slot must have been substituted");
    }

    // ── Pending memo count ────────────────────────────────────────────────────

    [Test]
    public void PendingMemos_Count_IncreasesCorrectly()
    {
        var state = FreshState();

        // Three incidents fire: death_first, floor_low, cause_trap
        var ctx = Died(floor: 2, cause: "spike_trap", run: 1);
        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(state.UnderWarden.PendingMemos.Count, Is.EqualTo(3));
    }

    // ── TotalMemosSentEver ────────────────────────────────────────────────────

    [Test]
    public void RecordMemoSent_Called_IncrementsTotalMemosSentEver()
    {
        var state = FreshState();
        Assert.That(state.UnderWarden.TotalMemosSentEver, Is.EqualTo(0),
            "Starts at 0");

        var ctx = Died(floor: 5, cause: "orc_brute", run: 1);
        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        // Only death_first fires (floor > 3, not a trap/acid cause)
        Assert.That(state.UnderWarden.TotalMemosSentEver, Is.EqualTo(1),
            "TotalMemosSentEver should be 1 after one memo queued");
    }

    // ── Dirty flag ───────────────────────────────────────────────────────────

    [Test]
    public void EvaluateRunEnd_MarksStateDirty_WhenMemosAdded()
    {
        var state = FreshState();
        Assert.That(state.IsDirty, Is.False, "Should start clean");

        var ctx = Died(floor: 5, cause: "orc_brute", run: 1);
        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(state.IsDirty, Is.True, "Should be dirty after memo was added");
    }

    [Test]
    public void EvaluateHallWardenPossession_AlwaysMarksStateDirty()
    {
        var state = FreshState();

        _evaluator.EvaluateHallWardenPossession(state, _registry, runNumber: 1);

        Assert.That(state.IsDirty, Is.True,
            "EvaluateHallWardenPossession always increments counter, so always marks dirty");
    }

    // ── death_repeat ──────────────────────────────────────────────────────────

    [Test]
    public void Death_QueuesDeath_Repeat_On_ThirdDeath()
    {
        var state = FreshState();
        // Seed 2 prior deaths by incrementing CumulativeDeaths directly
        // (simulate prior runs without queuing their memos)
        state.UnderWarden.CumulativeDeaths = 2;
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");

        var ctx = Died(floor: 5, cause: "orc_brute", run: 3);
        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(state.UnderWarden.PendingMemos.Any(m => m.Key == "procedural_notice.death_repeat"), Is.True,
            "death_repeat must fire on the third death");
    }

    [Test]
    public void Death_DoesNotQueue_Death_Repeat_Before_ThirdDeath()
    {
        var state = FreshState();
        // Seed 1 prior death — this will be the 2nd death total
        state.UnderWarden.CumulativeDeaths = 1;
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");

        var ctx = Died(floor: 5, cause: "orc_brute", run: 2);
        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(state.UnderWarden.PendingMemos.Any(m => m.Key == "procedural_notice.death_repeat"), Is.False,
            "death_repeat must not fire before the third death");
    }

    [Test]
    public void Death_DoesNotQueue_Death_Repeat_When_CauseSpecificAlsoFires()
    {
        var state = FreshState();
        // Third death via trap — cause_trap fires, death_repeat should be suppressed
        state.UnderWarden.CumulativeDeaths = 2;
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");

        var ctx = Died(floor: 5, cause: "spike_trap", run: 3);
        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(state.UnderWarden.PendingMemos.Any(m => m.Key == "polite.cause_trap"), Is.True,
            "cause_trap should fire");
        Assert.That(state.UnderWarden.PendingMemos.Any(m => m.Key == "procedural_notice.death_repeat"), Is.False,
            "death_repeat must be suppressed when a cause-specific memo fires on the same run");
    }

    [Test]
    public void Death_QueuesDeathRepeat_Without_CauseSpecific_On_ThirdDeath()
    {
        var state = FreshState();
        // Third death via generic orc — no cause-specific suppression
        state.UnderWarden.CumulativeDeaths = 2;
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");

        var ctx = Died(floor: 5, cause: "orc_brute", run: 3);
        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(state.UnderWarden.PendingMemos.Any(m => m.Key == "procedural_notice.death_repeat"), Is.True,
            "death_repeat must fire on third death with no cause-specific memo");
    }

    // ── cause_possession_neglect ──────────────────────────────────────────────

    [Test]
    public void Death_QueuesCausePossessionNeglect()
    {
        var state = FreshState();
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");

        var ctx = Died(floor: 6, cause: "possession_neglect", run: 2);
        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(
            state.UnderWarden.PendingMemos.Any(m => m.Key == "procedural_notice.cause_possession_neglect"),
            Is.True,
            "cause_possession_neglect memo must fire for possession_neglect cause");
    }

    // ── audit_warning ─────────────────────────────────────────────────────────

    [Test]
    public void Death_QueuesAuditWarning_AtThreshold()
    {
        var state = FreshState();
        // Seed 9 prior deaths — this death will be the 10th
        state.UnderWarden.CumulativeDeaths = 9;
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");

        var ctx = Died(floor: 5, cause: "orc_brute", run: 10);
        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(
            state.UnderWarden.PendingMemos.Any(m => m.Key == "procedural_notice.audit_warning"),
            Is.True,
            "audit_warning must fire when CumulativeDeaths reaches 10");
    }

    [Test]
    public void Death_DoesNotRequeue_AuditWarning_After_First_Fire()
    {
        var state = FreshState();
        state.UnderWarden.CumulativeDeaths = 10;
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");
        // Pre-seed audit_warning as already fired
        state.UnderWarden.RecordMemoSent(newGrievanceId: "procedural_notice.audit_warning");

        var ctx = Died(floor: 5, cause: "orc_brute", run: 12);
        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        var count = state.UnderWarden.PendingMemos.Count(m => m.Key == "procedural_notice.audit_warning");
        Assert.That(count, Is.EqualTo(0),
            "audit_warning is single-shot and must not re-fire after first fire");
    }

    // ── run_clean ─────────────────────────────────────────────────────────────

    [Test]
    public void Survived_QueuesRunClean_WhenPreviousRunAlsoClean()
    {
        var state = FreshState();
        state.UnderWarden.LastRunWasClean = true; // previous run was also clean

        var ctx = Survived(floor: 10, run: 3);
        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(
            state.UnderWarden.PendingMemos.Any(m => m.Key == "procedural_notice.run_clean"),
            Is.True,
            "run_clean must fire when both the current and previous runs were clean");
    }

    [Test]
    public void Survived_DoesNotQueue_RunClean_OnFirstCleanRun()
    {
        var state = FreshState(); // LastRunWasClean defaults to false

        var ctx = Survived(floor: 10, run: 1);
        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(
            state.UnderWarden.PendingMemos.Any(m => m.Key == "procedural_notice.run_clean"),
            Is.False,
            "run_clean must not fire on the first clean run — requires two consecutive");
    }

    [Test]
    public void EvaluateRunEnd_UpdatesLastRunWasClean_True_On_Survival()
    {
        var state = FreshState();
        Assert.That(state.UnderWarden.LastRunWasClean, Is.False, "Defaults to false");

        var ctx = Survived(floor: 10, run: 1);
        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(state.UnderWarden.LastRunWasClean, Is.True,
            "LastRunWasClean must be set to true after a survival run");
    }

    [Test]
    public void EvaluateRunEnd_UpdatesLastRunWasClean_False_On_Death()
    {
        var state = FreshState();
        state.UnderWarden.LastRunWasClean = true; // previously clean

        var ctx = Died(floor: 5, cause: "orc_brute", run: 2);
        _evaluator.EvaluateRunEnd(ctx, state, _registry);

        Assert.That(state.UnderWarden.LastRunWasClean, Is.False,
            "LastRunWasClean must be set to false after a death run");
    }

    // ── Multi-fire body variant selection ────────────────────────────────────

    [Test]
    public void CauseTrap_FiresBody1_OnSecondTrapDeath()
    {
        var state = FreshState();
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");

        // First trap death (fires body[0] — contains "three centuries")
        _evaluator.EvaluateRunEnd(Died(floor: 5, cause: "spike_trap", run: 2), state, _registry);
        state.UnderWarden.PendingMemos.Clear();

        // Second trap death (fires body[1] — contains "recurrence")
        _evaluator.EvaluateRunEnd(Died(floor: 5, cause: "spike_trap", run: 3), state, _registry);

        var memo = state.UnderWarden.PendingMemos.FirstOrDefault(m => m.Key == "polite.cause_trap");
        Assert.That(memo, Is.Not.Null, "cause_trap should fire on second trap death");
        Assert.That(memo!.Body, Does.Contain("recurrence"),
            "Second fire should use body[1] which contains 'recurrence'");
        Assert.That(memo.Body, Does.Not.Contain("three centuries"),
            "Second fire must not use body[0] which contains 'three centuries'");
    }

    [Test]
    public void CauseTrap_FiresBody2_OnThirdTrapDeath()
    {
        var state = FreshState();
        state.UnderWarden.RecordMemoSent(newGrievanceId: "polite.death_first");

        // First and second trap deaths
        _evaluator.EvaluateRunEnd(Died(floor: 5, cause: "spike_trap", run: 2), state, _registry);
        _evaluator.EvaluateRunEnd(Died(floor: 5, cause: "spike_trap", run: 3), state, _registry);
        state.UnderWarden.PendingMemos.Clear();

        // Third trap death (fires body[2] — contains "No comment.")
        _evaluator.EvaluateRunEnd(Died(floor: 5, cause: "spike_trap", run: 4), state, _registry);

        var memo = state.UnderWarden.PendingMemos.FirstOrDefault(m => m.Key == "polite.cause_trap");
        Assert.That(memo, Is.Not.Null, "cause_trap should fire on third trap death");
        Assert.That(memo!.Body, Does.Contain("No comment."),
            "Third fire should use body[2] which contains 'No comment.'");
    }

    // ── MemoFormatter clamping ────────────────────────────────────────────────

    [Test]
    public void MemoFormatter_ClampsToLastBody_OnHighFireIndex()
    {
        // Use polite.cause_trap which has 3 body variants (body[0], body[1], body[2]).
        // A fireIndex of 5 should clamp to body[2] (the last), not wrap back to body[0].
        var formatter = new MemoFormatter();
        var memo = _registry.GetMemo("polite.cause_trap");
        Assert.That(memo, Is.Not.Null, "polite.cause_trap must be in the registry");

        var slots = new Dictionary<string, string>
        {
            ["floor"] = "7",
            ["cause_of_death"] = "spike_trap",
        };

        var (_, body) = formatter.Format(memo!, fireIndex: 5, slots, _registry);

        Assert.That(body, Does.Contain("No comment."),
            "fireIndex=5 should clamp to body[2] (the last variant) which contains 'No comment.'");
        Assert.That(body, Does.Not.Contain("three centuries"),
            "fireIndex=5 must not use body[0]");
    }

    // ── catalog_referenced ────────────────────────────────────────────────────

    [Test]
    public void EvaluateCatalogReferenced_QueuesMemo_FirstTime()
    {
        var state = FreshState();

        _evaluator.EvaluateCatalogReferenced(state, _registry, runNumber: 3, catalogEntry: "test entry");

        var memo = state.UnderWarden.PendingMemos.FirstOrDefault(m => m.Key == "formal_complaint.catalog_referenced");
        Assert.That(memo, Is.Not.Null, "formal_complaint.catalog_referenced should be queued");
        Assert.That(memo!.Subject, Does.Contain("irregularity"));
    }

    [Test]
    public void EvaluateCatalogReferenced_FillsCatalogEntrySlot()
    {
        var state = FreshState();
        const string entry = "He carried it for nine floors. Take what's left.";

        _evaluator.EvaluateCatalogReferenced(state, _registry, runNumber: 2, catalogEntry: entry);

        var memo = state.UnderWarden.PendingMemos.First(m => m.Key == "formal_complaint.catalog_referenced");
        Assert.That(memo.Body, Does.Contain(entry),
            "The rendered catalog entry should appear quoted in the memo body.");
    }

    [Test]
    public void EvaluateCatalogReferenced_IsSingleShot_DoesNotFireTwice()
    {
        var state = FreshState();

        _evaluator.EvaluateCatalogReferenced(state, _registry, runNumber: 1, catalogEntry: "first entry");
        _evaluator.EvaluateCatalogReferenced(state, _registry, runNumber: 2, catalogEntry: "second entry");

        var memos = state.UnderWarden.PendingMemos
            .Where(m => m.Key == "formal_complaint.catalog_referenced").ToList();
        Assert.That(memos.Count, Is.EqualTo(1), "catalog_referenced is single-shot; must not fire twice.");
    }

    [Test]
    public void EvaluateCatalogReferenced_WithNullEntry_LeavesSlotUnfilled()
    {
        var state = FreshState();

        _evaluator.EvaluateCatalogReferenced(state, _registry, runNumber: 1, catalogEntry: null);

        var memo = state.UnderWarden.PendingMemos.First(m => m.Key == "formal_complaint.catalog_referenced");
        Assert.That(memo.Body, Does.Contain("{catalog_entry}"),
            "With a null entry the slot placeholder should remain unfilled.");
    }

    [Test]
    public void EvaluateCatalogReferenced_MarksDirty()
    {
        var state = FreshState();
        Assert.That(state.IsDirty, Is.False, "pre-condition: state is clean");

        _evaluator.EvaluateCatalogReferenced(state, _registry, runNumber: 1, catalogEntry: "entry");

        Assert.That(state.IsDirty, Is.True, "EvaluateCatalogReferenced must mark state dirty.");
    }
}
