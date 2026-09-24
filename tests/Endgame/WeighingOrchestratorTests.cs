using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnderWarden.Logic.AI;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Endgame;
using UnderWarden.Logic.Persistence;
using UnderWarden.Tests.Persistence;
using NUnit.Framework;

namespace UnderWarden.Tests.Endgame;

/// <summary>
/// TASK-009: the Weighing gauntlet orchestration. Drives Begin/Advance over a hand-built arena
/// GameState and asserts the phase machine: sequential rises in the finalized order, allied vs
/// hostile handling, allies-fall-back, the Debt-alone phase, and resolution into the right ending.
/// </summary>
[TestFixture]
public class WeighingOrchestratorTests
{
    private static GameState ArenaState(PersistentRunState? persistent = null)
    {
        var arena = WeighingArenaDefinition.Build();
        var start = arena.FirstAnchor("player_start")!.Value;
        var player = new Entity(0, "Player", start.X, start.Y, blocksMovement: true);
        player.Add(new Fighter(hp: 500, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 14, evasion: 0, damageMin: 5, damageMax: 8));
        arena.Map.RegisterEntity(player);
        return new GameState(player, new List<Entity>(), arena.Map, new SeededRandom(1337), turnLimit: 10_000)
        {
            IsDungeonMode = true,
            CurrentDepth = WeighingConstants.FinalFloorDepth,
            WeighingArena = arena,
            PersistentState = persistent,
        };
    }

    private static AuditScorer.AuditResult AllSavage() => new(
        GuardianTier.Savage, GuardianTier.Savage, GuardianTier.Savage, GuardianTier.Savage);

    private static AuditScorer.AuditResult AllAllied() => new(
        GuardianTier.Allied, GuardianTier.Allied, GuardianTier.Allied, GuardianTier.Allied);

    private static void KillActiveGuardianAndAdvance(GameState s)
    {
        var ws = s.Weighing!;
        var g = s.Monsters.First(m => m.Id == ws.ActiveGuardianId);
        g.Require<Fighter>().TakeDamage(99999);
        WeighingOrchestrator.Advance(s, new List<TurnEvent>());
    }

    // ── Sequential rises in the finalized order ───────────────────────────────

    [Test]
    public void Begin_AllSavage_RisesWardenFirst_AsHostile()
    {
        var s = ArenaState();
        WeighingOrchestrator.Begin(s, AllSavage(), swapAvailable: false, "hostile", 12, new List<TurnEvent>());

        Assert.That(s.Weighing!.Phase, Is.EqualTo(WeighingPhase.Guardians));
        var active = s.Monsters.First(m => m.Id == s.Weighing.ActiveGuardianId);
        Assert.That(active.Get<SpeciesTag>()!.TypeId, Is.EqualTo("guardian_warden_of_wardens"));
        Assert.That(active.Get<AiComponent>()!.Faction, Is.EqualTo(WeighingOrchestrator.WeighingFaction));
        // Warden rises at its south-rank anchor (4,9).
        Assert.That((active.X, active.Y), Is.EqualTo((4, 9)));
    }

    [Test]
    public void AllSavage_GuardiansRiseInFinalizedOrder_W_O_U_A_thenDebt()
    {
        var s = ArenaState();
        WeighingOrchestrator.Begin(s, AllSavage(), swapAvailable: false, "hostile", 12, new List<TurnEvent>());

        var order = new List<string>();
        while (s.Weighing!.Phase == WeighingPhase.Guardians)
        {
            order.Add(s.Monsters.First(m => m.Id == s.Weighing.ActiveGuardianId).Get<SpeciesTag>()!.TypeId);
            KillActiveGuardianAndAdvance(s);
        }

        Assert.That(order, Is.EqualTo(new[]
        {
            "guardian_warden_of_wardens",
            "guardian_oathkeeper",
            "guardian_auditors_own",     // Auditor's Own before Assembly (finalized swap)
            "guardian_assembly_of_the_lost",
        }));
        // After the four faction Guardians, the Debt threshold is reached — the choice gate.
        // (All-Savage + hostile rep = heavy record, no swap → Force/Refuse gate, no auto-resolve.)
        Assert.That(s.Weighing.Phase, Is.EqualTo(WeighingPhase.DebtChoiceGate));
        // No combatant yet — it's spawned only if Force is chosen.
        Assert.That(s.Weighing.DebtId, Is.Null, "Debt combatant not spawned until Force is chosen");
    }

    [Test]
    public void AllSavage_ForceChosenAndDebtDefeated_ResolvesToTheft()
    {
        var s = ArenaState();
        WeighingOrchestrator.Begin(s, AllSavage(), swapAvailable: false, "hostile", 12, new List<TurnEvent>());
        while (s.Weighing!.Phase == WeighingPhase.Guardians) KillActiveGuardianAndAdvance(s);

        // Heavy record → choice gate. Choose Force → spawns the combatant.
        Assert.That(s.Weighing.Phase, Is.EqualTo(WeighingPhase.DebtChoiceGate));
        WeighingOrchestrator.ChooseForce(s, new List<TurnEvent>());
        Assert.That(s.Weighing.Phase, Is.EqualTo(WeighingPhase.DebtCombat));
        Assert.That(s.Weighing.DebtId, Is.Not.Null, "Debt combatant spawned on Force");

        // Kill the Debt.
        s.Monsters.First(m => m.Id == s.Weighing.DebtId).Require<Fighter>().TakeDamage(99999);
        WeighingOrchestrator.Advance(s, new List<TurnEvent>());

        Assert.That(s.Weighing.Phase, Is.EqualTo(WeighingPhase.Resolved));
        Assert.That(s.Ending, Is.EqualTo(EndingType.Theft), "heavy record Force path resolves to Theft");
        Assert.That(s.IsDungeonVictory, Is.True);
        Assert.That(s.IsGameOver, Is.True);
    }

    // ── Allied Guardians: no blocking, fall back before the Debt ───────────────

    [Test]
    public void AllAllied_CleanRecord_AutoResolvesToCleanAudit_NoGate()
    {
        // Clean record + no Swap available → auto-CleanAudit, no choice gate, no combatant.
        var s = ArenaState();
        WeighingOrchestrator.Begin(s, AllAllied(), swapAvailable: false, "allied", 0, new List<TurnEvent>());

        Assert.That(s.Weighing!.AlliedGuardianIds, Has.Count.EqualTo(4), "all four joined as allies");
        Assert.That(s.Weighing.AlliesFellBack, Is.True);
        // Clean record auto-resolved — already at Resolved, no gate, no combatant.
        Assert.That(s.Weighing.Phase, Is.EqualTo(WeighingPhase.Resolved));
        Assert.That(s.Ending, Is.EqualTo(EndingType.CleanAudit));
        Assert.That(s.Weighing.DebtId, Is.Null, "no combatant spawned on auto-CleanAudit");
        Assert.That(s.IsDungeonVictory, Is.True);
    }

    [Test]
    public void AllAllied_WithSwapAvailable_ShowsChoiceGate()
    {
        // Clean record + Swap available → choice gate (Swap is a choice, not an outcome).
        var s = ArenaState();
        WeighingOrchestrator.Begin(s, AllAllied(), swapAvailable: true, "allied", 0, new List<TurnEvent>());

        Assert.That(s.Weighing!.Phase, Is.EqualTo(WeighingPhase.DebtChoiceGate),
            "Swap always shows the gate — the most thoughtful player gets the most thoughtful dilemma");
        Assert.That(s.Weighing.DebtId, Is.Null, "no combatant yet");
    }

    [Test]
    public void AlliedGuardian_SpawnsOnPlayerSide()
    {
        // A single allied Guardian (Warden) should be player_ally faction while it's up.
        var s = ArenaState();
        var audit = new AuditScorer.AuditResult(
            GuardianTier.Allied, GuardianTier.Savage, GuardianTier.Savage, GuardianTier.Savage);
        WeighingOrchestrator.Begin(s, audit, swapAvailable: false, "neutral", 0, new List<TurnEvent>());

        // Warden allied → joined; Oathkeeper savage → now the blocker.
        var ally = s.Monsters.First(m => m.Get<SpeciesTag>()!.TypeId == "guardian_warden_of_wardens");
        Assert.That(ally.Get<AiComponent>()!.Faction, Is.EqualTo(FactionRegistry.PlayerAllyFaction));
        var active = s.Monsters.First(m => m.Id == s.Weighing!.ActiveGuardianId);
        Assert.That(active.Get<SpeciesTag>()!.TypeId, Is.EqualTo("guardian_oathkeeper"));
    }

    // ── Loss states ───────────────────────────────────────────────────────────

    [Test]
    public void DeathDuringGuardians_IsLossGuardians()
    {
        var s = ArenaState();
        WeighingOrchestrator.Begin(s, AllSavage(), swapAvailable: false, "hostile", 12, new List<TurnEvent>());

        s.PlayerFighter.TakeDamage(99999);
        WeighingOrchestrator.Advance(s, new List<TurnEvent>());

        Assert.That(s.Ending, Is.EqualTo(EndingType.LossGuardians));
        Assert.That(s.PlayerDeathCause, Is.EqualTo(WeighingConstants.LossGuardiansCause));
    }

    [Test]
    public void DeathAtTheDebt_IsLossDebt()
    {
        // Force must be chosen to enter combat; then dying is LossDebt.
        var s = ArenaState();
        WeighingOrchestrator.Begin(s, AllSavage(), swapAvailable: false, "hostile", 12, new List<TurnEvent>());
        while (s.Weighing!.Phase == WeighingPhase.Guardians) KillActiveGuardianAndAdvance(s);
        WeighingOrchestrator.ChooseForce(s, new List<TurnEvent>());
        Assert.That(s.Weighing.Phase, Is.EqualTo(WeighingPhase.DebtCombat));

        s.PlayerFighter.TakeDamage(99999);
        WeighingOrchestrator.Advance(s, new List<TurnEvent>());

        Assert.That(s.Ending, Is.EqualTo(EndingType.LossDebt));
        Assert.That(s.PlayerDeathCause, Is.EqualTo(WeighingConstants.LossDebtCause));
    }

    [Test]
    public void Refuse_IsChosenNonDeathLoss()
    {
        var s = ArenaState();
        WeighingOrchestrator.Begin(s, AllSavage(), swapAvailable: false, "hostile", 12, new List<TurnEvent>());

        WeighingOrchestrator.Refuse(s, new List<TurnEvent>());

        Assert.That(s.Ending, Is.EqualTo(EndingType.LossRefused));
        Assert.That(s.PlayerDeathCause, Is.EqualTo(WeighingConstants.LossRefusedCause));
        Assert.That(s.PlayerFighter.IsAlive, Is.True, "refusal is a choice, not a death");
        Assert.That(s.IsGameOver, Is.True);
    }

    // ── Swap (hidden ending) ──────────────────────────────────────────────────

    // ── Integration: the turn loop begins the Weighing on the first arena turn ─

    [Test]
    public void ProcessTurn_OnArenaFloor_BeginsTheWeighing()
    {
        var persistent = PersistentRunState.LoadFromDisk(new FakePersistencePathProvider(
            Path.Combine(Path.GetTempPath(), "yarl_weigh_" + System.Guid.NewGuid())));
        var s = ArenaState(persistent);

        Assert.That(s.Weighing, Is.Null, "not yet begun");

        TurnController.ProcessTurn(s, PlayerAction.Wait);

        Assert.That(s.Weighing, Is.Not.Null, "the turn loop begins the Weighing on the first arena turn");
        // A fresh save scores Warden Allied (0 possessions) + Oathkeeper Diminished (neutral rep, no
        // orc blood this run) → the Warden joins as an ally and the Oathkeeper rises as the blocker.
        Assert.That(s.Weighing!.Phase, Is.EqualTo(WeighingPhase.Guardians));
        Assert.That(s.Weighing.ActiveGuardianId, Is.Not.Null, "a hostile Guardian blocks");
        var blocker = s.Monsters.First(m => m.Id == s.Weighing.ActiveGuardianId);
        Assert.That(blocker.Get<Fighter>()!.IsAlive, Is.True, "the blocking Guardian is live on the field");
        Assert.That(blocker.Get<SpeciesTag>()!.TypeId, Is.EqualTo("guardian_oathkeeper"));
    }

    // ── Headless enablement: null persistence + gate policy + audit override (TASK-009) ─

    [Test]
    public void ProcessTurn_NullPersistence_BeginsTheWeighing_NoSoftLock()
    {
        // Regression for the soft-lock: before the fix, BeginFromPersistence bailed on null
        // persistence → the gauntlet never began, and floor 25 has no stair to leave by. The harness
        // (which passes no persistence) would hang forever. Now it falls back to the default audit.
        var s = ArenaState(persistent: null);
        Assert.That(s.Weighing, Is.Null, "not yet begun");

        TurnController.ProcessTurn(s, PlayerAction.Wait);

        Assert.That(s.Weighing, Is.Not.Null, "the Weighing begins even without persistence");
        // Default headless audit is all-Neutral (hostile) → the Warden blocks first.
        Assert.That(s.Weighing!.Phase, Is.EqualTo(WeighingPhase.Guardians));
        Assert.That(s.Weighing.ActiveGuardianId, Is.Not.Null, "a Guardian is up — no soft-lock");
    }

    [Test]
    public void HeadlessGatePolicy_Force_FightsTheDebt_OnANeutralRecord()
    {
        // The default neutral audit is not heavy and offers no swap, so the Debt would auto-resolve
        // to CleanAudit. A Force policy makes the harness drive the Debt fight instead.
        var s = ArenaState(persistent: null);
        s.WeighingHeadlessGatePolicy = WeighingGateDecision.Force;
        WeighingOrchestrator.BeginFromPersistence(s, new List<TurnEvent>());

        while (s.Weighing!.Phase == WeighingPhase.Guardians) KillActiveGuardianAndAdvance(s);

        Assert.That(s.Weighing.Phase, Is.EqualTo(WeighingPhase.DebtCombat),
            "Force policy bypassed the clean auto-resolve and spawned the Debt");
        Assert.That(s.Weighing.DebtId, Is.Not.Null);

        s.Monsters.First(m => m.Id == s.Weighing.DebtId).Require<Fighter>().TakeDamage(99999);
        WeighingOrchestrator.Advance(s, new List<TurnEvent>());

        Assert.That(s.Weighing.Phase, Is.EqualTo(WeighingPhase.Resolved));
        Assert.That(s.Ending, Is.EqualTo(EndingType.CleanAudit), "neutral record + survived → CleanAudit");
        Assert.That(s.IsDungeonVictory, Is.True);
    }

    [Test]
    public void HeadlessGatePolicy_Refuse_ResolvesToLossRefused()
    {
        var s = ArenaState(persistent: null);
        s.WeighingHeadlessGatePolicy = WeighingGateDecision.Refuse;
        WeighingOrchestrator.BeginFromPersistence(s, new List<TurnEvent>());

        while (s.Weighing!.Phase == WeighingPhase.Guardians) KillActiveGuardianAndAdvance(s);

        Assert.That(s.Weighing.Phase, Is.EqualTo(WeighingPhase.Resolved));
        Assert.That(s.Ending, Is.EqualTo(EndingType.LossRefused));
        Assert.That(s.PlayerFighter.IsAlive, Is.True, "refusal is a chosen loss, not a death");
    }

    // ── Savage Warden lingering curse (decision C) ────────────────────────────

    [Test]
    public void SavageWarden_CursesTheNextAllyToRise()
    {
        // Warden Savage lays the curse; it dies first, but the curse lands on the next ally to rise.
        var s = ArenaState();
        var audit = new AuditScorer.AuditResult(
            GuardianTier.Savage,   // Warden — lays the curse, blocks first
            GuardianTier.Allied,   // Oathkeeper — the next ally to rise → gets turned
            GuardianTier.Savage,
            GuardianTier.Savage);
        WeighingOrchestrator.Begin(s, audit, swapAvailable: false, "neutral", 0, new List<TurnEvent>());

        var warden = s.Monsters.First(m => m.Id == s.Weighing!.ActiveGuardianId);
        Assert.That(warden.Get<SpeciesTag>()!.TypeId, Is.EqualTo("guardian_warden_of_wardens"));
        Assert.That(s.Weighing!.WardenCursePending, Is.True, "the savage Warden armed the curse");

        // Kill the Warden → the Oathkeeper rises Allied, and the lingering curse turns it.
        KillActiveGuardianAndAdvance(s);

        var oathkeeper = s.Monsters.First(m => m.Get<SpeciesTag>()!.TypeId == "guardian_oathkeeper");
        Assert.That(oathkeeper.Get<AiComponent>()!.Faction, Is.EqualTo(FactionRegistry.PlayerAllyFaction),
            "still player_ally faction so Hollowmark's Dispel can revert it");
        Assert.That(oathkeeper.Has<EnragedEffect>(), Is.True, "the curse enraged the risen ally");
        Assert.That(s.Weighing.WardenCursePending, Is.False, "curse consumed once");
        // A turned Guardian earns no loyal fall-back line.
        Assert.That(s.Weighing.AlliedGuardianTypes, Does.Not.Contain(GuardianId.Oathkeeper));
    }

    [Test]
    public void SavageWarden_OnlyFirstAllyIsTurned()
    {
        // Warden Savage; Oathkeeper + Auditor both Allied. Only the FIRST ally to rise is cursed.
        var s = ArenaState();
        var audit = new AuditScorer.AuditResult(
            GuardianTier.Savage, GuardianTier.Allied, GuardianTier.Allied, GuardianTier.Allied);
        WeighingOrchestrator.Begin(s, audit, swapAvailable: false, "neutral", 0, new List<TurnEvent>());

        KillActiveGuardianAndAdvance(s); // kill the Warden → remaining allies rise, gauntlet completes

        // The remaining allies are all loyal and fall back once the gauntlet completes, so the
        // invariant under test is the consumed-once flag and the fall-back roster: exactly one turned.
        Assert.That(s.Weighing!.WardenCursePending, Is.False, "curse consumed by the first ally");
        Assert.That(s.Weighing.AlliedGuardianTypes, Does.Not.Contain(GuardianId.Oathkeeper),
            "first ally (Oathkeeper) was turned — no loyal fall-back line");
        Assert.That(s.Weighing.AlliedGuardianTypes, Does.Contain(GuardianId.AuditorsOwn),
            "second ally (Auditor) stood loyally — keeps its fall-back line");
    }

    [Test]
    public void SavageWarden_NoAllyToTurn_CurseFizzles_NoCrash()
    {
        // All-Savage: the curse arms but never finds an ally. It must fizzle harmlessly, not hang.
        var s = ArenaState();
        WeighingOrchestrator.Begin(s, AllSavage(), swapAvailable: false, "hostile", 12, new List<TurnEvent>());
        Assert.That(s.Weighing!.WardenCursePending, Is.True);

        while (s.Weighing!.Phase == WeighingPhase.Guardians) KillActiveGuardianAndAdvance(s);

        Assert.That(s.Weighing.WardenCursePending, Is.True, "fizzled — no ally ever rose to turn");
        Assert.That(s.Weighing.Phase, Is.EqualTo(WeighingPhase.DebtChoiceGate), "gauntlet still completed");
    }

    [Test]
    public void WeighingAuditOverride_DrivesTiers_WithoutPersistence()
    {
        // Override to all-Allied: every Guardian joins, allies fall back, clean record auto-resolves
        // to CleanAudit with no blocker. (Proves the override is honored — the default neutral audit
        // would instead block on the Warden.)
        var s = ArenaState(persistent: null);
        s.WeighingAuditOverride = AllAllied();
        WeighingOrchestrator.BeginFromPersistence(s, new List<TurnEvent>());

        Assert.That(s.Weighing!.Phase, Is.EqualTo(WeighingPhase.Resolved));
        Assert.That(s.Ending, Is.EqualTo(EndingType.CleanAudit));
        Assert.That(s.Weighing.ActiveGuardianId, Is.Null, "no Guardian blocks on an all-Allied override");
    }

    [Test]
    public void Swap_AtChoiceGate_ResolvesImmediately_NoCombat()
    {
        // Swap is an act of will, not a fight — choosing Self at the gate resolves immediately.
        var s = ArenaState();
        WeighingOrchestrator.Begin(s, AllAllied(), swapAvailable: true, "allied", 0, new List<TurnEvent>());
        Assert.That(s.Weighing!.Phase, Is.EqualTo(WeighingPhase.DebtChoiceGate));

        WeighingOrchestrator.ChooseSwap(s, new List<TurnEvent>());

        Assert.That(s.Ending, Is.EqualTo(EndingType.Swap), "Swap resolves immediately on choosing Self");
        Assert.That(s.Weighing.DebtId, Is.Null, "no combatant was ever spawned");
        Assert.That(s.IsDungeonVictory, Is.True);
    }

    [Test]
    public void SwapAvailable_HeavyRecord_StillShowsChoiceGate()
    {
        // Even a heavy-record player who earned the catalog gets the Swap option.
        var s = ArenaState();
        WeighingOrchestrator.Begin(s, AllSavage(), swapAvailable: true, "hostile", 12, new List<TurnEvent>());
        while (s.Weighing!.Phase == WeighingPhase.Guardians) KillActiveGuardianAndAdvance(s);

        Assert.That(s.Weighing.Phase, Is.EqualTo(WeighingPhase.DebtChoiceGate));
        // Verify the gate event carries SwapAvailable=true
        // (presentation would show Force/Self/Refuse).
        WeighingOrchestrator.ChooseSwap(s, new List<TurnEvent>());
        Assert.That(s.Ending, Is.EqualTo(EndingType.Swap));
    }
}
