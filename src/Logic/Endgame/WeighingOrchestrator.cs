using System.Collections.Generic;
using System.Linq;
using UnderWarden.Logic.AI;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Endgame;

/// <summary>The phase of the Weighing gauntlet.</summary>
public enum WeighingPhase
{
    /// <summary>Faction Guardians rising in sequence; the player clears hostile ones one at a time.</summary>
    Guardians,
    /// <summary>
    /// The claim has stated its terms; the player must choose Force / Self (Swap) / Refuse.
    /// No combat entity is present yet. Input is blocked until a choice method is called.
    /// Auto-CleanAudit skips this phase entirely (no choice to make).
    /// </summary>
    DebtChoiceGate,
    /// <summary>Force was chosen; the Debt is a live combatant. Faced alone.</summary>
    DebtCombat,
    /// <summary>The Weighing has resolved into an ending (set on GameState.Ending).</summary>
    Resolved,
}

/// <summary>
/// Live orchestration state for the Weighing. Holds the audit result (per-Guardian tiers, decided
/// once at the start), the rise cursor, and the snapshot inputs the ending branch needs. Self-
/// contained so the orchestrator is testable without re-reading persistence mid-fight.
/// </summary>
public sealed class WeighingState
{
    public WeighingPhase Phase { get; set; } = WeighingPhase.Guardians;
    public int CurrentGuardianIndex { get; set; }
    public AuditScorer.AuditResult Audit { get; set; }

    public bool SwapAvailable { get; set; }
    public bool SwapChosen { get; set; }
    public string OrcRepState { get; set; } = "neutral";
    public int CumulativeDeaths { get; set; }

    /// <summary>The hostile Guardian currently blocking progress (null while none is up).</summary>
    public int? ActiveGuardianId { get; set; }
    /// <summary>Entity IDs of allied Guardians on the field.</summary>
    public List<int> AlliedGuardianIds { get; } = new();
    /// <summary>GuardianId values that allied — needed to emit fallback lines in FallBackAllies.</summary>
    public List<GuardianId> AlliedGuardianTypes { get; } = new();
    public int? DebtId { get; set; }
    public bool AlliesFellBack { get; set; }

    /// <summary>
    /// Set when the Warden-of-Wardens rises Savage; consumed when the next allied Guardian rises,
    /// turning it against Sasha (decision C, 2026-06-06). The Warden rises first and is defeated
    /// before any other Guardian, so its "now wear this" curse must linger past its own death to
    /// land on a later ally. Fizzles (stays true, no effect) if no later Guardian allies.
    /// </summary>
    public bool WardenCursePending { get; set; }
}

// ── Events (content/presentation hooks; lines are authored separately) ──────────
public sealed class GuardianRoseEvent : TurnEvent
{
    public GuardianId Guardian { get; init; }
    public GuardianTier Tier { get; init; }
    public int GuardianEntityId { get; init; }
}
public sealed class AlliesFellBackEvent : TurnEvent { }
public sealed class DebtRoseEvent : TurnEvent { }

/// <summary>
/// Emitted when the Debt threshold is reached and a player choice is required.
/// The presentation layer renders Force / (Self if SwapAvailable) / Refuse buttons.
/// Auto-CleanAudit (clean record, no Swap available) never emits this event.
/// </summary>
public sealed class DebtChoiceGateEvent : TurnEvent
{
    /// <summary>Whether the Swap option (Self) should be offered.</summary>
    public bool SwapAvailable { get; init; }
    /// <summary>True on the heavy-record path; false on clean-record-with-Swap.</summary>
    public bool IsHeavyRecord { get; init; }
}

/// <summary>
/// Emitted once per faction Guardian when its tier is resolved at Weighing begin.
/// Carries BOTH the resolved tier AND the raw input metrics the scorer read, independently of each
/// other — the guardian_tier_correctness detector reconciles "given these inputs, was this the right
/// tier?" Capturing inputs separately from the output is the independence invariant.
///
/// WasScored=true: tier was computed from a real PersistentRunState (inputs are meaningful).
/// WasScored=false: tier came from a headless override (inputs are null — the scorer did not run).
/// Extensibility: uses the same TurnEventJsonConverter seam as all other TurnEvents; new Weighing
/// inputs are added as nullable properties here without touching the capture layer.
///
/// Input fields per guardian (others are null):
///   WardenOfWardens  → HallWardenPossessions, MemoTone
///   Oathkeeper       → OrcRepState, UnprovokedOrcKillsThisRun
///   AssemblyOfTheLost → CumulativeDeaths
///   AuditorsOwn      → TotalUnprovokedKillsThisRun, FloorsReached
/// </summary>
public sealed class GuardianTierResolvedEvent : TurnEvent
{
    public GuardianId Guardian { get; init; }
    public GuardianTier Tier { get; init; }

    /// <summary>True when the tier was computed by AuditScorer.Score(); false for a headless override.</summary>
    public bool WasScored { get; init; }

    // ── Warden-of-Wardens inputs ──
    public int? HallWardenPossessions { get; init; }
    public string? MemoTone { get; init; }

    // ── Oathkeeper inputs ──
    public string? OrcRepState { get; init; }
    public int? UnprovokedOrcKillsThisRun { get; init; }

    // ── Assembly-of-the-Lost inputs ──
    public int? CumulativeDeaths { get; init; }

    // ── Auditor's Own inputs ──
    public int? TotalUnprovokedKillsThisRun { get; init; }
    public int? FloorsReached { get; init; }
}

/// <summary>
/// Emitted when the Weighing fully resolves into an ending.
/// Carries the resolved ending type plus the full audit record and the key aggregate inputs
/// DetermineEnding keyed on — so the ending_resolved detector can reconcile without re-running
/// the scorer.
/// </summary>
public sealed class WeighingResolvedEvent : TurnEvent
{
    public EndingType Ending { get; init; }

    // ── Full audit result ──
    public GuardianTier WardenTier { get; init; }
    public GuardianTier OathkeeperTier { get; init; }
    public GuardianTier AssemblyTier { get; init; }
    public GuardianTier AuditorTier { get; init; }

    // ── Aggregate inputs DetermineEnding read ──
    public bool AnySavage { get; init; }
    public bool IsHeavyRecord { get; init; }
    public string OrcRepState { get; init; } = "";
    public int CumulativeDeaths { get; init; }
    public bool SwapChosen { get; init; }
    public bool SwapAvailable { get; init; }
}

/// <summary>
/// Drives the Weighing gauntlet (TASK-009). Sequential: the four faction Guardians rise in the
/// finalized order (Warden → Oathkeeper → Auditor's Own → Assembly), each at its arena anchor and
/// at the tier the audit assigned. Allied Guardians join Sasha's side and don't block; hostile ones
/// must be defeated before the next rises. When the faction Guardians are done, allies fall back and
/// the Debt rises — faced alone, always full strength. The outcome resolves into a GameState.Ending
/// via <see cref="AuditScorer.DetermineEnding"/>.
///
/// Guardian stats here are PLACEHOLDER (tier-scaled); the wall's height is tuned in the harness pass
/// (TASK-011). The rise order is a tunable constant per the layout↔audit-content co-design.
/// </summary>
public static class WeighingOrchestrator
{
    /// <summary>Faction string for hostile Guardians — not Sasha's side, so hostile to player + allies.</summary>
    public const string WeighingFaction = "weighing";

    /// <summary>
    /// The finalized rise order (south→north up the hall). Tunable default per the co-design with the
    /// audit dialogue: external grievance → internal reckoning, Assembly last (twinned with the Debt).
    /// </summary>
    public static readonly (GuardianId Id, string Anchor)[] RiseOrder =
    {
        (GuardianId.WardenOfWardens, "guardian_warden"),
        (GuardianId.Oathkeeper, "guardian_oathkeeper"),
        (GuardianId.AuditorsOwn, "guardian_auditor"),
        (GuardianId.AssemblyOfTheLost, "guardian_assembly"),
    };

    /// <summary>Begin the Weighing with explicit inputs (test-friendly).</summary>
    public static void Begin(GameState state, AuditScorer.AuditResult audit, bool swapAvailable,
        string orcRepState, int cumulativeDeaths, List<TurnEvent> events)
    {
        var ws = new WeighingState
        {
            Audit = audit,
            SwapAvailable = swapAvailable,
            OrcRepState = orcRepState,
            CumulativeDeaths = cumulativeDeaths,
            Phase = WeighingPhase.Guardians,
            CurrentGuardianIndex = 0,
        };
        state.Weighing = ws;

        // Emit the audit opening — fires before the first Guardian rises (the narration gate).
        EmitDialogue(state, events, "opening", state.WeighingAudit?.GetOpening());

        RiseUntilBlockedOrDone(state, ws, events);
    }

    /// <summary>The audit used headlessly when there is no persistence and no override — a neutral
    /// (full-strength, no-ally) baseline wall. The harness sets <see cref="GameState.WeighingAuditOverride"/>
    /// to test other tiers.</summary>
    public static readonly AuditScorer.AuditResult DefaultHeadlessAudit = new(
        GuardianTier.Neutral, GuardianTier.Neutral, GuardianTier.Neutral, GuardianTier.Neutral);

    /// <summary>
    /// Begin the Weighing, scoring the audit from the run's persistence (production path).
    ///
    /// Headless-safe: when there is no persistence (the harness / balance pass), it falls back to
    /// <see cref="GameState.WeighingAuditOverride"/> or <see cref="DefaultHeadlessAudit"/> instead of
    /// bailing — bailing would soft-lock the arena (no Guardians, no stair, no resolution). The audit
    /// override on <see cref="GameState"/> takes precedence even when persistence exists, so a save's
    /// real tiers can be overridden for balance runs.
    /// </summary>
    public static void BeginFromPersistence(GameState state, List<TurnEvent> events)
    {
        var persistent = state.PersistentState;
        AuditScorer.AuditResult audit;
        bool swapAvailable;
        string orcRepState;
        int cumulativeDeaths;

        if (persistent != null)
        {
            var runTally = state.Player.Get<RunAggressionTally>();
            var uw = persistent.UnderWarden;
            var factions = persistent.Factions;
            orcRepState = factions.GetState(Persistence.Namespaces.FactionsData.OrcFactionId);
            cumulativeDeaths = uw.CumulativeDeaths;

            // Capture inputs BEFORE Score() so they are independent of the scorer's output.
            // This is the independence invariant: the detector reconciles "given these inputs, was
            // this the right tier?" and cannot do so if the event merely echoes the scorer's decision.
            bool willScore = state.WeighingAuditOverride == null;
            int orcKillsThisRun = runTally?.UnprovokedKillsFor(Persistence.Namespaces.FactionsData.OrcFactionId) ?? 0;
            int totalKillsThisRun = runTally?.Total() ?? 0;
            int floors = state.CurrentDepth;

            audit = state.WeighingAuditOverride
                ?? AuditScorer.Score(persistent, runTally, floors);
            swapAvailable = persistent.Hael.BranchOfPassageUnlocked;

            // Emit one GuardianTierResolvedEvent per Guardian. Inputs are present when WasScored=true
            // (the scorer ran from real persistence) and null when WasScored=false (headless override,
            // scorer bypassed — inputs are not meaningful and the detector must skip reconciliation).
            int actor = state.Player.Id;
            events.Add(new GuardianTierResolvedEvent
            {
                ActorId = actor,
                Guardian = GuardianId.WardenOfWardens,
                Tier     = audit.WardenOfWardens,
                WasScored = willScore,
                HallWardenPossessions = willScore ? uw.HallWardenPossessionsTotal : null,
                MemoTone              = willScore ? uw.LastMemoTone : null,
            });
            events.Add(new GuardianTierResolvedEvent
            {
                ActorId = actor,
                Guardian = GuardianId.Oathkeeper,
                Tier     = audit.Oathkeeper,
                WasScored = willScore,
                OrcRepState              = willScore ? orcRepState : null,
                UnprovokedOrcKillsThisRun = willScore ? orcKillsThisRun : null,
            });
            events.Add(new GuardianTierResolvedEvent
            {
                ActorId = actor,
                Guardian = GuardianId.AssemblyOfTheLost,
                Tier     = audit.AssemblyOfTheLost,
                WasScored = willScore,
                CumulativeDeaths = willScore ? cumulativeDeaths : null,
            });
            events.Add(new GuardianTierResolvedEvent
            {
                ActorId = actor,
                Guardian = GuardianId.AuditorsOwn,
                Tier     = audit.AuditorsOwn,
                WasScored = willScore,
                TotalUnprovokedKillsThisRun = willScore ? totalKillsThisRun : null,
                FloorsReached               = willScore ? floors : null,
            });
        }
        else
        {
            // No persistence (harness / balance pass): use the injected override or the neutral baseline.
            // GuardianTierResolvedEvent still fires (with WasScored=false) so the event is always in
            // the stream — a test watching for the event can verify it fires without needing real inputs.
            audit = state.WeighingAuditOverride ?? DefaultHeadlessAudit;
            swapAvailable = false;
            orcRepState = "neutral";
            cumulativeDeaths = 0;

            int actor = state.Player.Id;
            foreach (var (id, _) in RiseOrder)
            {
                events.Add(new GuardianTierResolvedEvent
                {
                    ActorId   = actor,
                    Guardian  = id,
                    Tier      = audit.TierFor(id),
                    WasScored = false,
                });
            }
        }

        Begin(state, audit, swapAvailable, orcRepState, cumulativeDeaths, events);
    }

    /// <summary>
    /// The player declines — a chosen non-death loss. Valid at either the audit or the Debt threshold.
    /// </summary>
    public static void Refuse(GameState state, List<TurnEvent> events)
    {
        var ws = state.Weighing;
        if (ws == null || ws.Phase == WeighingPhase.Resolved) return;
        ws.SwapChosen = false;
        Resolve(state, ws, WeighingOutcome.Refused, events);
    }

    /// <summary>
    /// The player offers himself — the Swap, if the catalog gate is open. Resolves immediately
    /// (the Swap is an act of will, not a fight; no combatant is spawned).
    /// </summary>
    public static void ChooseSwap(GameState state, List<TurnEvent> events)
    {
        var ws = state.Weighing;
        if (ws == null || !ws.SwapAvailable || ws.Phase == WeighingPhase.Resolved) return;
        ws.SwapChosen = true;
        Resolve(state, ws, WeighingOutcome.Survived, events);
    }

    /// <summary>
    /// The player chooses to take Anik by force. Spawns the Debt as a combatant and enters combat.
    /// Only valid at the DebtChoiceGate phase.
    /// </summary>
    public static void ChooseForce(GameState state, List<TurnEvent> events)
    {
        var ws = state.Weighing;
        if (ws == null || ws.Phase != WeighingPhase.DebtChoiceGate) return;
        SpawnDebtCombatant(state, ws, events);
    }

    /// <summary>End-of-turn progression. Called by TurnController after all combat is resolved.</summary>
    public static void Advance(GameState state, List<TurnEvent> events)
    {
        var ws = state.Weighing;
        if (ws == null || ws.Phase == WeighingPhase.Resolved) return;

        // Death during the Weighing → the corresponding loss (distinct cause per phase).
        if (!state.PlayerFighter.IsAlive)
        {
            ResolveDeath(state, ws, events);
            return;
        }

        switch (ws.Phase)
        {
            case WeighingPhase.Guardians:
                if (ws.ActiveGuardianId is int gid && IsAlive(state, gid))
                    return; // still fighting the current hostile Guardian
                // Current Guardian resolved (defeated) — advance the cursor and rise the next.
                ws.ActiveGuardianId = null;
                ws.CurrentGuardianIndex++;
                RiseUntilBlockedOrDone(state, ws, events);
                break;

            case WeighingPhase.DebtChoiceGate:
                return; // waiting for the player to call ChooseForce / ChooseSwap / Refuse

            case WeighingPhase.DebtCombat:
                if (ws.DebtId is int did && IsAlive(state, did))
                    return; // still fighting the Debt
                // Debt defeated on the Force branch → Theft (the heavy record was already confirmed
                // before this phase was entered; clean record auto-resolved before combat started).
                Resolve(state, ws, WeighingOutcome.Survived, events);
                break;
        }
    }

    // ── Internals ───────────────────────────────────────────────────────────────

    private static void RiseUntilBlockedOrDone(GameState state, WeighingState ws, List<TurnEvent> events)
    {
        while (ws.CurrentGuardianIndex < RiseOrder.Length)
        {
            var (id, anchor) = RiseOrder[ws.CurrentGuardianIndex];
            var tier = ws.Audit.TierFor(id);
            var pos = state.WeighingArena?.FirstAnchor(anchor) ?? (state.Player.X, state.Player.Y);

            // The Savage Warden's "now wear this" — arm the curse. It lands on the next ally to rise
            // (decision C): the Warden is first and dies before any other Guardian, so the curse must
            // outlive it. Possession-themed malice corrupting Sasha's one ally — on-theme.
            if (id == GuardianId.WardenOfWardens && tier == GuardianTier.Savage)
                ws.WardenCursePending = true;

            // Emit the beat dialogue before spawning so narration precedes the rise.
            EmitDialogue(state, events,
                $"{GuardianTypeId(id)}.{tier.ToString().ToLowerInvariant()}",
                state.WeighingAudit?.GetGuardianBeat(id, tier));

            var guardian = SpawnGuardian(state, id, pos, tier, events);

            if (tier == GuardianTier.Allied)
            {
                // An ally joins Sasha's side and does not block the gauntlet — rise the next at once.
                ws.AlliedGuardianIds.Add(guardian.Id); // tracked for cleanup at the allies-fall-back beat

                if (ws.WardenCursePending)
                {
                    // The Warden's lingering curse turns this ally hostile (HostileToAll) — it now
                    // fights Sasha and the field. Still player_ally faction, so Hollowmark's Dispel
                    // reverts it. NOT added to AlliedGuardianTypes: a turned Guardian earns no loyal
                    // fall-back line. Consumed once — only the first ally after the Savage Warden turns.
                    ws.WardenCursePending = false;
                    GuardianAbilities.TurnAllyHostile(guardian, events);
                }
                else
                {
                    ws.AlliedGuardianTypes.Add(id);
                }

                ws.CurrentGuardianIndex++;
                continue;
            }

            // A hostile Guardian blocks until defeated.
            ws.ActiveGuardianId = guardian.Id;
            return;
        }

        // All four faction Guardians resolved → allies fall back, then the Debt rises alone.
        FallBackAllies(state, ws, events);
        RaiseDebt(state, ws, events);
    }

    private static void FallBackAllies(GameState state, WeighingState ws, List<TurnEvent> events)
    {
        if (ws.AlliesFellBack) return;
        ws.AlliesFellBack = true;

        // Emit fallback dialogue before removing entities so narration precedes the withdrawal.
        // Framing narration only fires when at least one ally stood with Sasha (the line
        // "those who stood with you step back" reads nonsensically with no one present).
        if (ws.AlliedGuardianTypes.Count > 0 && state.WeighingAudit != null)
        {
            EmitDialogue(state, events, "ally_fallback.framing",
                state.WeighingAudit.GetAllyFallbackFraming());
            foreach (var guardianId in ws.AlliedGuardianTypes)
                EmitDialogue(state, events, $"ally_fallback.{GuardianFallbackKey(guardianId)}",
                    state.WeighingAudit.GetAllyFallback(guardianId));
        }

        // Remove allied entities — they leave by will (the layout lets them follow; they choose not to).
        foreach (var allyId in ws.AlliedGuardianIds)
        {
            var ally = state.Monsters.FirstOrDefault(m => m.Id == allyId);
            if (ally != null)
            {
                state.Map.UnregisterEntity(ally);
                state.Monsters.Remove(ally);
            }
        }
        events.Add(new AlliesFellBackEvent());
    }

    private static string GuardianFallbackKey(GuardianId id) => id switch
    {
        GuardianId.WardenOfWardens => "warden",
        GuardianId.Oathkeeper => "oathkeeper",
        GuardianId.AssemblyOfTheLost => "assembly",
        GuardianId.AuditorsOwn => "auditor",
        _ => "unknown",
    };

    /// <summary>
    /// Called after all faction Guardians resolve. Emits the Debt's terms (dialogue), then either:
    /// - auto-resolves to CleanAudit when record is clean and Swap is unavailable (no real choice), or
    /// - emits the choice gate (Force / Self / Refuse) for the player to decide.
    /// The combatant is not spawned here; it's spawned only if ChooseForce is called.
    /// </summary>
    private static void RaiseDebt(GameState state, WeighingState ws, List<TurnEvent> events)
    {
        // Emit the claim's terms (dialogue key "debt" resolved by the audit registry).
        EmitDialogue(state, events, "debt", state.WeighingAudit?.GetDebt());
        events.Add(new DebtRoseEvent());

        bool heavy = AuditScorer.IsHeavyRecord(ws.Audit, ws.OrcRepState, ws.CumulativeDeaths);

        // Headless gate policy (harness / balance pass): drive the choice without UI. The gate event
        // still fires for any listener, then the policy applies the decision immediately. This also
        // bypasses the clean+no-swap auto-resolve, so the harness can force the Debt fight on any record.
        if (state.WeighingHeadlessGatePolicy is WeighingGateDecision decision)
        {
            ws.Phase = WeighingPhase.DebtChoiceGate;
            events.Add(new DebtChoiceGateEvent { SwapAvailable = ws.SwapAvailable, IsHeavyRecord = heavy });
            ApplyGateDecision(state, ws, decision, events);
            return;
        }

        // Swap always shows the gate — it's a choice, not an outcome.
        // Clean + no swap = the claim is satisfied; auto-resolve with no decision to make.
        if (!ws.SwapAvailable && !heavy)
        {
            Resolve(state, ws, WeighingOutcome.Survived, events);
            return;
        }

        ws.Phase = WeighingPhase.DebtChoiceGate;
        events.Add(new DebtChoiceGateEvent { SwapAvailable = ws.SwapAvailable, IsHeavyRecord = heavy });
    }

    /// <summary>
    /// Apply a headless gate decision (harness path). Reuses the same Choose* entry points the UI
    /// calls, so production and headless share one code path. A Swap requested without availability
    /// falls back to Force — a headless run must resolve, not hang; a well-formed policy never hits it.
    /// </summary>
    private static void ApplyGateDecision(GameState state, WeighingState ws,
        WeighingGateDecision decision, List<TurnEvent> events)
    {
        switch (decision)
        {
            case WeighingGateDecision.Swap when ws.SwapAvailable:
                ChooseSwap(state, events);
                break;
            case WeighingGateDecision.Refuse:
                Refuse(state, events);
                break;
            case WeighingGateDecision.Force:
            case WeighingGateDecision.Swap: // unavailable — safe non-hang fallback
                ChooseForce(state, events);
                break;
        }
    }

    /// <summary>Spawns the Debt as a live combatant (called by ChooseForce).</summary>
    private static void SpawnDebtCombatant(GameState state, WeighingState ws, List<TurnEvent> events)
    {
        var pos = state.WeighingArena?.FirstAnchor("debt") ?? (state.Player.X, state.Player.Y - 1);
        var debt = new Entity(NextId(state), "The Debt", pos.Item1, pos.Item2, blocksMovement: true);
        // Placeholder stats; tuned in TASK-011.
        debt.Add(new Fighter(hp: 150, strength: 16, dexterity: 14, constitution: 16,
            accuracy: 16, evasion: 3, damageMin: 12, damageMax: 18));
        debt.Add(new AiComponent { AiType = "basic", Faction = WeighingFaction });
        debt.Add(new SpeciesTag("the_debt"));
        state.Map.RegisterEntity(debt);
        state.Monsters.Add(debt);
        ws.DebtId = debt.Id;
        ws.Phase = WeighingPhase.DebtCombat;
    }

    private static Entity SpawnGuardian(GameState state, GuardianId id, (int X, int Y) pos,
        GuardianTier tier, List<TurnEvent> events)
    {
        bool allied = tier == GuardianTier.Allied;
        var (hp, dmgMin, dmgMax) = StatsFor(tier);

        var guardian = new Entity(NextId(state), GuardianName(id), pos.X, pos.Y, blocksMovement: true);
        guardian.Add(new Fighter(hp: hp, strength: 14, dexterity: 13, constitution: 14,
            accuracy: 14, evasion: 2, damageMin: dmgMin, damageMax: dmgMax));
        guardian.Add(new AiComponent
        {
            AiType = "basic",
            Faction = allied ? FactionRegistry.PlayerAllyFaction : WeighingFaction,
        });
        guardian.Add(new SpeciesTag(GuardianTypeId(id)));
        state.Map.RegisterEntity(guardian);
        state.Monsters.Add(guardian);

        events.Add(new GuardianRoseEvent { Guardian = id, Tier = tier, GuardianEntityId = guardian.Id });
        return guardian;
    }

    private static void ResolveDeath(GameState state, WeighingState ws, List<TurnEvent> events)
    {
        var outcome = ws.Phase == WeighingPhase.DebtCombat
            ? WeighingOutcome.DiedToDebt
            : WeighingOutcome.DiedToGuardians;
        Resolve(state, ws, outcome, events);
    }

    private static void Resolve(GameState state, WeighingState ws, WeighingOutcome outcome, List<TurnEvent> events)
    {
        ws.Phase = WeighingPhase.Resolved;
        var ending = AuditScorer.DetermineEnding(
            outcome, ws.Audit, ws.SwapChosen, ws.SwapAvailable, ws.OrcRepState, ws.CumulativeDeaths);
        state.Ending = ending;

        // Losses carry a distinct cause-code the memo evaluator / game-over routing reads.
        state.PlayerDeathCause = ending switch
        {
            EndingType.LossGuardians => WeighingConstants.LossGuardiansCause,
            EndingType.LossDebt => WeighingConstants.LossDebtCause,
            EndingType.LossRefused => WeighingConstants.LossRefusedCause,
            _ => state.PlayerDeathCause,
        };

        // Emit the resolution dialogue before WeighingResolvedEvent so it plays before game-over.
        // LossGuardians / LossDebt are death endings without Debt-specific dialogue (covered by
        // the broader ending-texts batch in a later handoff).
        var resolutionPages = state.WeighingAudit?.GetResolution(ending);
        if (resolutionPages != null && resolutionPages.Count > 0)
            EmitDialogue(state, events, $"resolution.{ending.ToString().ToLowerInvariant()}", resolutionPages);

        bool isHeavy = AuditScorer.IsHeavyRecord(ws.Audit, ws.OrcRepState, ws.CumulativeDeaths);
        events.Add(new WeighingResolvedEvent
        {
            Ending = ending,
            WardenTier    = ws.Audit.WardenOfWardens,
            OathkeeperTier = ws.Audit.Oathkeeper,
            AssemblyTier  = ws.Audit.AssemblyOfTheLost,
            AuditorTier   = ws.Audit.AuditorsOwn,
            AnySavage     = ws.Audit.AnySavage,
            IsHeavyRecord = isHeavy,
            OrcRepState   = ws.OrcRepState,
            CumulativeDeaths = ws.CumulativeDeaths,
            SwapChosen    = ws.SwapChosen,
            SwapAvailable = ws.SwapAvailable,
        });
    }

    private static void EmitDialogue(GameState state, List<TurnEvent> events, string key,
        IReadOnlyList<WeighingDialoguePage>? pages)
    {
        if (pages == null || pages.Count == 0) return;
        events.Add(new WeighingDialogueEvent
        {
            ActorId = state.Player.Id,
            DialogueKey = key,
            Pages = pages,
        });
    }

    private static bool IsAlive(GameState state, int entityId)
    {
        var e = state.Monsters.FirstOrDefault(m => m.Id == entityId);
        return e != null && e.Get<Fighter>()?.IsAlive == true;
    }

    private static int NextId(GameState state)
    {
        int max = state.Player.Id;
        foreach (var m in state.Monsters) if (m.Id > max) max = m.Id;
        return max + 1;
    }

    private static (int Hp, int DmgMin, int DmgMax) StatsFor(GuardianTier tier) => tier switch
    {
        GuardianTier.Savage => (120, 10, 16),
        GuardianTier.Neutral => (80, 6, 10),
        GuardianTier.Diminished => (50, 3, 6),
        GuardianTier.Allied => (90, 7, 11),
        _ => (80, 6, 10),
    };

    private static string GuardianName(GuardianId id) => id switch
    {
        GuardianId.WardenOfWardens => "The Warden-of-Wardens",
        GuardianId.Oathkeeper => "The Oathkeeper",
        GuardianId.AssemblyOfTheLost => "The Assembly of the Lost",
        GuardianId.AuditorsOwn => "The Auditor's Own",
        _ => "Guardian",
    };

    private static string GuardianTypeId(GuardianId id) => id switch
    {
        GuardianId.WardenOfWardens => "guardian_warden_of_wardens",
        GuardianId.Oathkeeper => "guardian_oathkeeper",
        GuardianId.AssemblyOfTheLost => "guardian_assembly_of_the_lost",
        GuardianId.AuditorsOwn => "guardian_auditors_own",
        _ => "guardian",
    };
}
