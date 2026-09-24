using System.Diagnostics;
using System.Text;
using UnderWarden.Logic.Balance.LlmPlayer;
using UnderWarden.Logic.Balance.Transcript;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;
using UnderWarden.Logic.Persistence;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Per-floor metrics collected during a DungeonRun.
/// </summary>
public sealed class FloorRunMetrics
{
    /// <summary>Dungeon depth this floor was generated at (1-based).</summary>
    public int Depth { get; init; }

    /// <summary>Turns taken on this floor before descending or dying.</summary>
    public int TurnsTaken { get; set; }

    /// <summary>Monsters killed on this floor.</summary>
    public int MonstersKilled { get; set; }

    /// <summary>Player HP at end of floor (0 if player died).</summary>
    public int PlayerHpAtEnd { get; set; }

    /// <summary>Player max HP at end of floor. Used for HP-fraction calculations per floor.</summary>
    public int PlayerMaxHp { get; set; }

    /// <summary>True if the player died on this floor.</summary>
    public bool PlayerDied { get; set; }

    /// <summary>True if the player successfully descended (reached stair after clearing floor).</summary>
    public bool Descended { get; set; }

    /// <summary>
    /// Healing potions used on this floor (player HealEvent count, filtered to player ActorId only).
    /// Does NOT count accidental monster item-use that heals the player.
    /// </summary>
    public int PotionsUsed { get; set; }

    /// <summary>
    /// True when this floor ended because the per-floor turn cap was hit, rather than
    /// descent or death. Indicates a potential stuck-bot or pathfinding regression.
    /// </summary>
    public bool HitMaxTurns { get; set; }

    /// <summary>
    /// Loot category counts for this floor — how many items of each category spawned.
    /// Null if loot telemetry was not collected (e.g., no PityTracker was wired).
    /// Keys match loot_tags.yaml categories: healing, panic, offensive, utility,
    /// upgrade_weapon, upgrade_armor, rare, defensive, key.
    /// </summary>
    public IReadOnlyDictionary<string, int>? LootCategoryCounts { get; set; }

    /// <summary>
    /// Number of times hard pity forced a guaranteed item on this floor.
    /// </summary>
    public int LootHardPityFires { get; set; }

    // ── 0c role-aware health capture (see docs/balance/threat_archetypes.md, memory:0c_diagnostic) ──

    /// <summary>
    /// Total HP the player lost to combat on this floor (monster melee landed hits + DOT/bleed + Soul
    /// Bolt). Excludes environmental hazard/trap damage (traps are texture, not a balance lever). A floor
    /// vitals number; per-killer attribution lives on <see cref="Death"/>.
    /// </summary>
    public int DamageTakenThisFloor { get; set; }

    /// <summary>Turns on this floor in which the player attacked or was attacked (combat engagement length).</summary>
    public int CombatTurns { get; set; }

    /// <summary>
    /// ttk proxy: average number of the player's LANDED hits per monster killed on this floor.
    /// 0 when no monster was killed. Diagnostic only — not a balance verdict input.
    /// </summary>
    public double AvgHitsToKill { get; set; }

    /// <summary>True if any escalator/fused monster was present on this floor (initially or via spawn/raise).</summary>
    public bool EscalatorPresent { get; set; }

    /// <summary>True if any spike/fused monster was present on this floor (the role-aware spike-pushover check).</summary>
    public bool SpikePresent { get; set; }

    /// <summary>True if an escalator/fused monster was killed on this floor.</summary>
    public bool EscalatorNeutralized { get; set; }

    /// <summary>Floor-turn at which the FIRST escalator/fused monster died, or null if none was neutralized.</summary>
    public int? EscalatorNeutralizedAtTurn { get; set; }

    /// <summary>
    /// The role-aware death diagnostic — populated ONLY on the floor where the player died, else null.
    /// Carries the six bounded lever signals (see PlayerDeathRecord).
    /// </summary>
    public PlayerDeathRecord? Death { get; set; }
}

/// <summary>
/// The full per-death diagnostic capture for role-aware floor health (0c). One per player death.
///
/// Survival rate is the BALANCE verdict; this record is the SUBORDINATE diagnostic, consulted only after
/// the survival rate flags a floor, to attribute WHICH lever is off. Each field maps to one bounded lever
/// (memory:project_0c_diagnostic_design):
///   HitsToDown            → role-fastness (was each blow earning its kill too fast for the killer's role)
///   DamagePerHit          → monster-damage lever
///   KillerHitRate         → armor/AC lever (this game's armor is avoidance, not soak — hit-rate is the observable)
///   CounterattacksLanded  → weapon-speed / control lever
///   DistinctAttackers     → density lever
///   HitsToDown ÷ EngagementTurns → attack-frequency lever (the wraith lever; parameter-free)
///
/// The classifier consumes only (KillerArchetype, HitsToDown) via DeathRecord; the rest feeds the
/// additive lever-attribution layer (0c step 6).
/// </summary>
public sealed class PlayerDeathRecord
{
    /// <summary>Depth of the floor the player died on.</summary>
    public int Depth { get; init; }

    /// <summary>Entity id of the killer. -1 = ground hazard (no monster entity).</summary>
    public int KillerId { get; init; }

    /// <summary>YAML type id of the killer (SpeciesTag), or null for hazard/unknown.</summary>
    public string? KillerTypeId { get; init; }

    /// <summary>Threat archetype of the killer, or null when unclassified / a hazard.</summary>
    public ThreatArchetype? KillerArchetype { get; init; }

    /// <summary>The killer's landed blows the player absorbed before going down (role-fastness).</summary>
    public int HitsToDown { get; init; }

    /// <summary>Average final damage per the killer's landed hit on the player (monster-damage lever).</summary>
    public double DamagePerHit { get; init; }

    /// <summary>Killer's landed ÷ attempted swings against the player (armor/AC lever). 0 if it never swung.</summary>
    public double KillerHitRate { get; init; }

    /// <summary>Player's landed hits on the killer during the engagement (weapon-speed / control lever).</summary>
    public int CounterattacksLanded { get; init; }

    /// <summary>Distinct monsters that dealt the player damage on this floor (density lever).</summary>
    public int DistinctAttackers { get; init; }

    /// <summary>Floor-turns from the killer's first landed hit to the player's death (with HitsToDown → frequency).</summary>
    public int EngagementTurns { get; init; }

    /// <summary>Realized attack frequency of the killer: landed hits per engagement turn (the wraith lever).</summary>
    public double AttackFrequency => EngagementTurns > 0 ? (double)HitsToDown / EngagementTurns : 0.0;
}

/// <summary>
/// Aggregate result of a full DungeonRun across multiple floors.
/// </summary>
public sealed class DungeonRunResult
{
    /// <summary>Seed used for this run.</summary>
    public int Seed { get; init; }

    /// <summary>Number of floors the bot attempted to traverse.</summary>
    public int FloorsAttempted { get; init; }

    /// <summary>Number of floors the bot successfully cleared and descended from.</summary>
    public int FloorsCompleted { get; set; }

    /// <summary>Total turns taken across all floors.</summary>
    public int TotalTurns { get; set; }

    /// <summary>Total monsters killed across all floors.</summary>
    public int TotalKills { get; set; }

    /// <summary>Player HP at end of the final floor (0 if player died).</summary>
    public int FinalHp { get; set; }

    /// <summary>Whether the player died before completing all floors.</summary>
    public bool PlayerDied { get; set; }

    /// <summary>Per-floor breakdown. Count == FloorsAttempted.</summary>
    public IReadOnlyList<FloorRunMetrics> PerFloor { get; init; } = Array.Empty<FloorRunMetrics>();
}

/// <summary>
/// Runs BotBrain through N dungeon floors, collecting per-floor metrics.
/// Pure Logic layer — no Godot dependency.
///
/// Reusable for balance experiments and regression tests on the dungeon campaign loop.
/// Complements ScenarioHarness (arena scenarios) with a full floor-progression view.
///
/// Usage:
///   var harness = new DungeonRunHarness(floorBuilder);
///   var result = harness.Run(floors: 5, baseSeed: 1337);
///   var summary = harness.RunSoak(floors: 3, runs: 100, baseSeed: 1337);
/// </summary>
public sealed class DungeonRunHarness
{
    // After floor is cleared, bot walks toward stair. Cap turns per floor to prevent
    // infinite loops if the bot gets stuck (e.g. pathfinding edge case).
    // 1000 gives a full dungeon floor (120×80, ~40 rooms, 10+ monsters) comfortable clearance.
    // 500 was too tight — large floors with scattered monsters could time out legitimately.
    private const int MaxTurnsPerFloor = 1000;

    // LLM path uses a much lower per-floor cap. The bot has goal-directed navigation;
    // the LLM can circle a floor for hundreds of turns without finding the stair.
    // 200 turns is enough to fight, explore, and descend on a typical floor, while
    // keeping API costs bounded at ~$0.09/floor at Haiku rates.
    private const int LlmMaxTurnsPerFloor = 200;

    // Override the GameState TurnLimit for dungeon floors. The default is 100 (scenario mode),
    // which is far too low for a procedural dungeon floor with pathfinding to a distant stair.
    // DungeonFloorBuilder does not set this — it's the harness's responsibility to impose
    // a reasonable bound without making IsGameOver trigger prematurely.
    private const int DungeonFloorTurnLimit = 2000;

    // Sentinel for "the player did not die via a DeathEvent this floor" (distinct from -1 = hazard kill).
    private const int NoDeath = -2;

    private readonly DungeonFloorBuilder _floorBuilder;

    public DungeonRunHarness(DungeonFloorBuilder floorBuilder)
    {
        _floorBuilder = floorBuilder;
    }

    /// <summary>
    /// Run BotBrain through <paramref name="floors"/> dungeon floors starting at depth 1.
    ///
    /// Seed derivation per floor: baseSeed + depth * 1_000_003
    /// (matches MultiFloorSmokeTests — ensures same floor geometry for the same seed).
    ///
    /// The bot uses BotBrain for combat and healing decisions. When a floor is clear
    /// (all monsters dead), the bot paths to the stair and descends. If the player dies
    /// or the per-floor turn cap is hit, the run stops early.
    ///
    /// Backward-compatible: callers that used Run() previously get identical results.
    /// </summary>
    public DungeonRunResult Run(int floors, int baseSeed = 1337, BotPersonaConfig? persona = null)
    {
        var soakResult = RunSingle(floors, baseSeed, enableTelemetry: false, persona: persona);

        // Reconstruct the legacy DungeonRunResult from the enriched soak result.
        // FloorsAttempted is PerFloor.Count — same logic as before.
        return new DungeonRunResult
        {
            Seed             = soakResult.Seed,
            FloorsAttempted  = soakResult.PerFloor.Count,
            FloorsCompleted  = soakResult.FloorsCompleted,
            TotalTurns       = soakResult.TotalTurns,
            TotalKills       = soakResult.TotalKills,
            FinalHp          = soakResult.FinalHp,
            PlayerDied       = soakResult.Outcome == OutcomeClassifier.Died,
            PerFloor         = soakResult.PerFloor,
        };
    }

    /// <summary>
    /// Run a single dungeon campaign and return a formatted narrative transcript.
    /// Captures floor entries, voice lines, monster kills, deaths, descents, and traps.
    /// Intended for D1 narrative testing — prints a human-readable play-by-play.
    /// </summary>
    public string RunTranscript(int floors, int seed, BotPersonaConfig? persona = null)
    {
        var entries = new List<TranscriptEntry>();
        var result = RunSingle(floors, seed, enableTelemetry: false, transcript: entries, persona: persona);
        return FormatTranscript(result, entries, floors, seed);
    }

    /// <summary>
    /// Run a single dungeon campaign and emit the ENRICHED LLM-testing transcript
    /// (JSONL) defined in docs/llm-testing/shared-transcript-schema.md, alongside the
    /// usual soak result. This is the Analyst/Player shared format: header + one
    /// TurnRecord per turn (full action_taken for replay + verbatim event stream) +
    /// RunSummary (HP profile, system triggers, memos delivered verbatim).
    ///
    /// <paramref name="voiceRegistry"/> (optional) resolves VoiceLineEvent text into the
    /// transcript; <paramref name="memoRegistry"/> (optional) drives end-of-run memo capture.
    /// Both are nullable — absent registries leave the corresponding fields empty/null,
    /// which is itself greppable as a capture gap.
    /// </summary>
    public (DungeonSoakRunResult Result, string Jsonl) RunWithTranscript(
        int floors, int seed, BotPersonaConfig? persona = null,
        VoiceLineRegistry? voiceRegistry = null, MemoRegistry? memoRegistry = null)
    {
        var resolvedPersona = persona ?? BotPersonaRegistry.Get("balanced");
        var recorder = new TranscriptRecorder(seed, resolvedPersona.Name, voiceRegistry);
        var result = RunSingle(
            floors, seed, enableTelemetry: true, persona: resolvedPersona,
            transcriptRecorder: recorder, memoRegistry: memoRegistry);
        return (result, recorder.ToJsonl());
    }

    private static string FormatTranscript(DungeonSoakRunResult result, List<TranscriptEntry> entries, int floors, int seed)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Run Transcript (seed {seed}, {floors} floors) ===");
        sb.AppendLine($"Outcome: {result.Outcome}  |  Floors completed: {result.FloorsCompleted}/{floors}  |  Kills: {result.TotalKills}  |  Turns: {result.TotalTurns}");
        if (!string.IsNullOrEmpty(result.FailureDetail))
            sb.AppendLine($"Death: {result.FailureDetail}");
        sb.AppendLine();

        int currentDepth = 0;
        foreach (var entry in entries)
        {
            if (entry.Depth != currentDepth)
            {
                currentDepth = entry.Depth;
                // floor_enter entries are the depth header — skip duplicate header
            }

            string line = entry.EventType switch
            {
                "floor_enter"    => $"\n[Floor {entry.Depth}]",
                "voice"          => $"  T{entry.FloorTurn,4} | Voice:   {entry.Detail}",
                "monster_killed" => $"  T{entry.FloorTurn,4} | Kill:    {entry.Detail}",
                "player_died"    => $"  T{entry.FloorTurn,4} | DIED:    killed by {entry.Detail}",
                "descended"      => $"  T{entry.FloorTurn,4} | Descended to floor {entry.Depth + 1}",
                "trap_triggered" => $"  T{entry.FloorTurn,4} | Trap:    {entry.Detail}",
                _                => $"  T{entry.FloorTurn,4} | {entry.EventType}: {entry.Detail}",
            };
            sb.AppendLine(line);
        }

        // Voice-line summary at end of transcript
        if (result.VoiceLineHits?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Voice Line Summary:");
            foreach (var (triggerId, count) in result.VoiceLineHits.OrderByDescending(kv => kv.Value))
                sb.AppendLine($"  {triggerId,-55}  x{count}");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Run N dungeon campaigns and return aggregate soak statistics.
    ///
    /// Seed per run: baseSeed + i (i = 0..runs-1), matching the PoC convention.
    /// Each run is wrapped in try/catch — exceptions produce an "exception" result
    /// and are logged to stderr, but do NOT abort the whole session.
    ///
    /// This is the primary entry point for automated regression testing.
    /// </summary>
    public DungeonSoakSummary RunSoak(int floors, int runs, int baseSeed = 1337, BotPersonaConfig? persona = null,
        int startDepth = 1, GearProfile? gearProfile = null)
    {
        var results = new List<DungeonSoakRunResult>(runs);

        for (int i = 0; i < runs; i++)
        {
            int seed = baseSeed + i;
            try
            {
                // Telemetry always enabled in soak mode — no reason to run N iterations without it.
                var result = RunSingle(floors, seed, enableTelemetry: true, persona: persona,
                    startDepth: startDepth, gearProfile: gearProfile);
                results.Add(result);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [soak] run {i} (seed {seed}) threw: {ex.Message}");
                results.Add(new DungeonSoakRunResult
                {
                    Seed          = seed,
                    Outcome       = OutcomeClassifier.Exception,
                    FailureType   = OutcomeClassifier.FailureException,
                    FailureDetail = ex.Message,
                    FloorsCompleted = 0,
                    PerFloor      = Array.Empty<FloorRunMetrics>(),
                });
            }
        }

        return DungeonSoakSummary.ComputeFrom(results, configuredFloors: floors);
    }

    /// <summary>
    /// Staged-start soak (0c step 8): run depths <paramref name="startDepth"/>..<paramref name="floors"/>
    /// with a region-appropriate player built from <paramref name="gearProfile"/>, instead of grinding up
    /// from floor 1. Seed-per-depth is absolute, so a staged floor N reproduces a full run's floor-N
    /// geometry — letting a region be soaked at its own gear level. Thin wrapper over RunSoak.
    /// </summary>
    public DungeonSoakSummary RunSoakStaged(int floors, int runs, int startDepth, GearProfile gearProfile,
        int baseSeed = 1337, BotPersonaConfig? persona = null)
        => RunSoak(floors, runs, baseSeed, persona, startDepth: startDepth, gearProfile: gearProfile);

    /// <summary>
    /// Run a single dungeon campaign with the LLM Player brain and emit the enriched
    /// JSONL transcript. Mirrors RunWithTranscript but uses IPlayerBrain instead of BotBrain.
    /// Creates a fresh TranscriptRecorder with playerType="llm" and the given model id.
    /// BotTelemetry is disabled (the LLM path does not use BotBrain at the harness level).
    /// </summary>
    public (DungeonSoakRunResult Result, string Jsonl) RunWithLlmPlayer(
        int floors, int seed, IPlayerBrain brain, string personaName, string llmModel,
        VoiceLineRegistry? voiceRegistry = null, MemoRegistry? memoRegistry = null)
    {
        var recorder = new TranscriptRecorder(seed, personaName, voiceRegistry,
            playerType: "llm", llmModel: llmModel);
        var result = RunSingle(
            floors, seed, enableTelemetry: false, persona: null,
            transcriptRecorder: recorder, memoRegistry: memoRegistry,
            llmBrain: brain);
        return (result, recorder.ToJsonl());
    }

    /// <summary>
    /// Core single-run implementation. Returns a fully populated DungeonSoakRunResult
    /// including killerName, potions tracking, boon count, and timing.
    ///
    /// Both Run() and RunSoak() call this method. It replaces the original run loop
    /// while preserving identical behavior for all existing callers.
    ///
    /// When <paramref name="enableTelemetry"/> is true (default for soak mode), creates a
    /// BotTelemetryRecorder and passes a BotDecisionContext to every BotBrain.Decide() call,
    /// storing the resulting BotRunSummary in the returned DungeonSoakRunResult.BotSummary.
    /// When false (legacy Run() path), no recorder is created and BotSummary is null.
    ///
    /// When <paramref name="llmBrain"/> is non-null, the brain decides every turn (skipping
    /// the harness-level auto-stair-navigation branch), BotTelemetry is unused, and the
    /// transcript captures LLM metadata (reasoning, structural assessments, fallback events).
    /// </summary>
    private DungeonSoakRunResult RunSingle(int floors, int baseSeed, bool enableTelemetry = false, IList<TranscriptEntry>? transcript = null, BotPersonaConfig? persona = null, TranscriptRecorder? transcriptRecorder = null, MemoRegistry? memoRegistry = null, int startDepth = 1, GearProfile? gearProfile = null, IPlayerBrain? llmBrain = null)
    {
        var sw = Stopwatch.StartNew();

        var perFloor      = new List<FloorRunMetrics>(Math.Max(1, floors - startDepth + 1));
        // Staged start (step 8): begin at startDepth with a region-appropriate geared player instead of
        // grinding up from floor 1. gearProfile != null builds that player once per run; otherwise the
        // first Build() creates the floor-1 default. Seed-per-depth is absolute, so a staged floor N has
        // the same geometry as a full run that reached floor N.
        Entity? player    = gearProfile != null ? _floorBuilder.CreateGearedPlayer(gearProfile) : null;
        Entity? lastFloorPlayer = null; // always the last floor's player entity (even on death)
        BoonTracker? boonTracker = null;
        PityTracker? pityTracker = null;
        int totalTurns    = 0;
        int totalKills    = 0;
        int floorsCompleted = 0;
        int totalPotionsUsed = 0;
        int finalHp       = 0;
        int finalMaxHp    = 0;
        string? killerName = null;
        var voiceLineHits = new Dictionary<string, int>();

        // Create a single recorder for the whole run when telemetry is enabled.
        // All BotBrain.Decide() calls share this recorder; context carries per-call metadata.
        // Telemetry is not applicable on the LLM path (BotBrain decisions don't happen there).
        BotTelemetryRecorder? recorder = (enableTelemetry && llmBrain == null) ? new BotTelemetryRecorder() : null;

        // One BotBrain instance per run — stuck detection state persists across floors.
        // Per plan TASK-006: harnesses must use the instance path so TASK-004 stuck detection fires.
        // When llmBrain is non-null, BotBrain lives inside LlmBotBrain as a fallback; the harness
        // does not need its own BotBrain instance.
        var resolvedPersona = persona ?? BotPersonaRegistry.Get("balanced");
        BotBrain? botBrain = llmBrain == null ? new BotBrain(resolvedPersona) : null;
        string personaName = resolvedPersona.Name;
        bool runWasAborted = false;

        for (int depth = startDepth; depth <= floors; depth++)
        {
            transcript?.Add(new TranscriptEntry { Depth = depth, FloorTurn = 0, EventType = "floor_enter", Detail = $"Depth {depth}" });

            // Distinct-but-reproducible seed per depth — same pattern as smoke tests
            var rng = new SeededRandom(baseSeed + depth * 1_000_003);
            var state = _floorBuilder.Build(depth, rng, player, boonTracker: boonTracker, pityTracker: pityTracker);

            // Dungeon floors need far more turns than the scenario default (100).
            // Set a generous limit so IsGameOver only fires on player death, not turn-out.
            state.TurnLimit = DungeonFloorTurnLimit;

            var floorMetrics = new FloorRunMetrics { Depth = depth };
            int floorTurns   = 0;
            int floorKills   = 0;
            int floorPotions = 0;
            int playerId     = state.Player.Id;
            bool descended   = false;
            bool forceDescend = false; // set when BotBrain signals ForceDescend; stays true for the floor

            // 0c role-aware health capture: accumulate the per-death lever signals + floor vitals.
            var combat = new EngagementTracker(playerId, state);
            int killerId = NoDeath;          // set from the player's DeathEvent (-1 = hazard)
            int playerDeathFloorTurn = 0;    // floor-turn at which the player went down

            // Enriched transcript: HP-profile point at floor entry.
            transcriptRecorder?.BeginFloor(depth, HpFraction(state.PlayerFighter));
            // LLM Player hook: called after floor is built and before the first Decide.
            // For depth > first floor, the brain queues a FLOOR COMPLETE prompt block.
            llmBrain?.OnFloorEnter(depth);

            // LLM stuck detection: mirrors BotBrain's per-run counters but scoped per-floor.
            // Bot path has stuck detection inside BotBrain; the LLM path needs it here because
            // the LLM can issue the same blocked-move for hundreds of turns with no progress.
            int  llmStuckTurns   = 0;
            bool llmForceDescend = false;
            var  llmLastPos      = (X: state.Player.X, Y: state.Player.Y);
            const int LlmStuckWarnThreshold        = 4;   // inject warning into next prompt
            const int LlmStuckForceDescendThreshold = 12;  // take over navigation

            int turnCap = llmBrain != null ? LlmMaxTurnsPerFloor : MaxTurnsPerFloor;
            while (!state.IsGameOver && floorTurns < turnCap)
            {
                PlayerAction action;
                // TurnNumber for telemetry: accumulated total turns so far + current floor turns.
                // This gives a monotonically increasing turn number across the whole run.
                int currentTurnNumber = totalTurns + floorTurns + 1;

                // LLM metadata for transcript capture (null on the bot path).
                string? llmDecisionContext = null;
                StructuralAssessment? llmAssessment = null;
                IReadOnlyList<StructuralAssessment>? llmExtraJudgments = null;
                bool llmUsedFallback = false;
                string? llmFallbackReason = null;

                if (llmBrain != null)
                {
                    if (llmForceDescend && state.StairDown != null)
                    {
                        // Stuck force-descend: bypass the LLM and navigate to the stair directly.
                        // Same logic as the bot's auto-stair-nav branch.
                        if (state.PlayerOnStairDown)
                        {
                            action = PlayerAction.Descend;
                        }
                        else
                        {
                            var path = Pathfinder.AStar(
                                state.Map,
                                state.Player.X, state.Player.Y,
                                state.StairDown.X, state.StairDown.Y,
                                state.Player, canPassDoors: true);
                            action = path is { Count: > 0 }
                                ? PlayerAction.MoveTo(path[0].X, path[0].Y)
                                : PlayerAction.Wait;
                        }
                        llmUsedFallback  = true;
                        llmFallbackReason = "stuck_force_descend";
                    }
                    else
                    {
                        // LLM path: the brain decides EVERY turn — including post-floor-clear navigation.
                        // The describer's menu already includes a pathed "Move toward the staircase"
                        // entry, so Reader/Explorer can choose when to descend (hardening note 5).
                        var decision = llmBrain.Decide(state);
                        action            = decision.Action;
                        llmDecisionContext = decision.Reasoning;
                        llmAssessment      = decision.Assessment;
                        llmExtraJudgments  = decision.ExtraJudgments;
                        llmUsedFallback    = decision.UsedFallback;
                        llmFallbackReason  = decision.FallbackReason;
                    }
                }
                else if ((state.IsFloorClear || forceDescend) && state.StairDown != null)
                {
                    // All monsters dead — navigate to the stair and descend.
                    // Emit NavigateToStair telemetry for the harness-driven path,
                    // which is not inside BotBrain.Decide() (see plan risk #3).
                    if (state.PlayerOnStairDown)
                    {
                        // Emit a Descend decision for the actual descend step
                        recorder?.Record(BuildNavigateRecord(state, currentTurnNumber, depth, "Descend", "navigate_stair"));
                        action = PlayerAction.Descend;
                    }
                    else
                    {
                        // Path one step toward the stair
                        var path = Pathfinder.AStar(
                            state.Map,
                            state.Player.X, state.Player.Y,
                            state.StairDown.X, state.StairDown.Y,
                            state.Player,
                            canPassDoors: true);

                        if (path != null && path.Count > 0)
                        {
                            recorder?.Record(BuildNavigateRecord(state, currentTurnNumber, depth, "NavigateToStair", "navigate_stair"));
                            var (nx, ny) = path[0];
                            action = PlayerAction.MoveTo(nx, ny);
                        }
                        else
                        {
                            // Pathfinding failed (stair unreachable) — wait rather than infinite-loop
                            recorder?.Record(BuildNavigateRecord(state, currentTurnNumber, depth, "Wait", "navigate_stair"));
                            action = PlayerAction.Wait;
                        }
                    }
                }
                else
                {
                    // Normal combat/heal decision via BotBrain instance (not static path).
                    // Using the instance ensures stuck detection (TASK-004) fires across turns.
                    // Pass context when telemetry is enabled so BotBrain emits a decision record.
                    BotDecisionContext? decisionContext = recorder != null
                        ? new BotDecisionContext(recorder, currentTurnNumber, depth, personaName)
                        : null;

                    var botAction = botBrain!.Decide(
                        state.Player,
                        state.PlayerFighter,
                        state.PlayerInventory,
                        state.Monsters,
                        state.Map,
                        decisionContext,
                        floorItems: state.FloorItems);

                    // ForceDescend: bot stuck with unreachable enemy — navigate to stair even with
                    // monsters alive. Preserves the run; the next floor gets a fresh attempt.
                    if (botAction.Type == BotAction.ActionType.ForceDescend)
                    {
                        forceDescend = true;
                        action = PlayerAction.Wait; // safe no-op this turn; next iterations use stair nav
                    }
                    // AbortRun: intercept before ToPlayerAction — count as death-equivalent.
                    // Set flag and break the floor loop immediately. The outer loop will detect
                    // runWasAborted and break the floor iteration as well.
                    else if (botAction.Type == BotAction.ActionType.AbortRun)
                    {
                        runWasAborted = true;
                        killerName = "stuck_abort";
                        break;
                    }
                    else
                    {
                        // Use BotActionConverter for A*-pathed movement so the bot can navigate
                        // across dungeon floors with rooms and corridors (not just greedy moves).
                        action = BotActionConverter.ToPlayerActionWithPathing(botAction, state);
                    }
                }

                // Capture pre-action state for the enriched transcript (HP fraction and
                // available-action count reflect the situation the action responded to).
                double hpPctBeforeTurn = transcriptRecorder != null ? HpFraction(state.PlayerFighter) : 0.0;
                int availableActions   = transcriptRecorder != null ? ComputeAvailableActionCount(state) : 0;

                var turnResult = TurnController.ProcessTurn(state, action);
                floorTurns++;

                // LLM path: inject a fallback event into the resolved event stream so the
                // transcript captures the reason. Events is a List<TurnEvent> (TurnResult.cs:10) —
                // safe to append post-ProcessTurn without copying.
                if (llmUsedFallback && !string.IsNullOrEmpty(llmFallbackReason))
                    turnResult.Events.Add(new LlmFallbackEvent { Reason = llmFallbackReason });

                if (transcriptRecorder != null)
                {
                    // Post-action scalar fields the rubric v1 predicates read (see config/rubric/v1.yaml).
                    transcriptRecorder.RecordTurn(
                        currentTurnNumber, depth,
                        new TurnVitals(
                            PlayerHpPct:        hpPctBeforeTurn,
                            AvailableActionCount: availableActions,
                            IsGameOver:         state.IsGameOver,
                            RunAggressionTally: state.Player.Get<RunAggressionTally>()?.Total() ?? 0,
                            PossessionActive:   IsPlayerPossessionActive(state),
                            ControlledEntityId: state.ControlledEntity.Id,
                            PlayerEntityId:     state.Player.Id),
                        action, turnResult.Events,
                        decisionContext: llmDecisionContext,
                        structuralAssessment: llmAssessment);

                    // LLM path: floor_summary and reflection judgments arrive as ExtraJudgments —
                    // stamp the current turn number and accumulate into the run-level list.
                    // StructuralAssessment is a class (not a record), so we create new instances.
                    if (llmExtraJudgments != null)
                    {
                        foreach (var j in llmExtraJudgments)
                            transcriptRecorder.AddStructuralJudgment(new StructuralAssessment
                            {
                                Judgment = j.Judgment,
                                Note     = j.Note,
                                Turn     = currentTurnNumber,
                            });
                    }
                }

                // LLM path: feed the resolved events back to the brain so it can update its
                // ring buffer and run hook detection (near-death, first-orc, mural, possession).
                llmBrain?.OnTurnResolved(currentTurnNumber, turnResult.Events, state);

                // LLM stuck detection: update counter after turn resolution.
                if (llmBrain != null && !llmForceDescend)
                {
                    var newPos = (X: state.Player.X, Y: state.Player.Y);
                    if (newPos == llmLastPos)
                    {
                        llmStuckTurns++;
                        if (llmStuckTurns == LlmStuckWarnThreshold)
                            llmBrain.InjectPromptBlock(
                                "WARNING: You have not moved for several turns. Your chosen actions " +
                                "are not working. Try a completely different approach — move in a " +
                                "different direction, attack an enemy, wait, or descend the staircase.");
                        if (llmStuckTurns >= LlmStuckForceDescendThreshold)
                            llmForceDescend = true;
                    }
                    else
                    {
                        llmStuckTurns = 0;
                    }
                    llmLastPos = newPos;
                }

                // 0c: ingest this turn's combat into the per-floor lever-signal accumulators.
                combat.IngestTurn(turnResult.Events, floorTurns, state);

                // Process events from this turn
                foreach (var evt in turnResult.Events)
                {
                    switch (evt)
                    {
                        case DeathEvent death when death.ActorId != playerId:
                            // Monster died — count kill
                            floorKills++;
                            if (transcript != null)
                            {
                                string mName = state.Monsters.FirstOrDefault(m => m.Id == death.ActorId)?.Name ?? "unknown";
                                transcript.Add(new TranscriptEntry { Depth = depth, FloorTurn = floorTurns, EventType = "monster_killed", Detail = mName });
                            }
                            break;

                        case DeathEvent death when death.ActorId == playerId:
                            // Player died — capture killer name immediately.
                            // Dead monsters remain in state.Monsters (not removed until next floor),
                            // so this lookup works even if the killer died in the same turn.
                            // KillerId = -1 means a ground hazard kill (fire/gas); no entity to look up.
                            killerName = death.KillerId == -1
                                ? "Ground Hazard"
                                : state.Monsters.FirstOrDefault(m => m.Id == death.KillerId)?.Name;
                            killerId = death.KillerId;
                            playerDeathFloorTurn = floorTurns;
                            transcript?.Add(new TranscriptEntry { Depth = depth, FloorTurn = floorTurns, EventType = "player_died", Detail = killerName ?? "unknown" });
                            break;

                        case HealEvent heal when heal.ActorId == playerId:
                            // Player used a healing item. Filter to player only — monster item-use
                            // that accidentally heals the player via HealEvent also has a different
                            // ActorId (the monster), so this check is correct.
                            floorPotions++;
                            break;

                        case DescendEvent:
                            descended = true;
                            floorsCompleted++;
                            transcript?.Add(new TranscriptEntry { Depth = depth, FloorTurn = floorTurns, EventType = "descended", Detail = "" });
                            break;

                        case VoiceLineEvent v:
                            voiceLineHits.TryGetValue(v.TriggerId, out int vlc);
                            voiceLineHits[v.TriggerId] = vlc + 1;
                            transcript?.Add(new TranscriptEntry { Depth = depth, FloorTurn = floorTurns, EventType = "voice", Detail = v.TriggerId });
                            break;

                        case TrapTriggeredEvent trap when trap.TargetId == playerId:
                            transcript?.Add(new TranscriptEntry { Depth = depth, FloorTurn = floorTurns, EventType = "trap_triggered", Detail = trap.Source });
                            break;
                    }
                }

                if (descended) break;
            }

            floorMetrics.TurnsTaken    = floorTurns;
            floorMetrics.MonstersKilled = floorKills;
            floorMetrics.PlayerHpAtEnd  = state.PlayerFighter.Hp;
            floorMetrics.PlayerMaxHp    = state.PlayerFighter.MaxHp;
            floorMetrics.PlayerDied     = !state.PlayerFighter.IsAlive || runWasAborted;
            floorMetrics.Descended      = descended;
            floorMetrics.PotionsUsed    = floorPotions;
            // HitMaxTurns: floor ended at the cap without descent or death
            floorMetrics.HitMaxTurns    = floorTurns >= turnCap
                && !floorMetrics.Descended
                && !floorMetrics.PlayerDied;

            // 0c role-aware health: floor vitals + the per-death lever record (death floors only).
            floorMetrics.DamageTakenThisFloor    = combat.DamageTaken;
            floorMetrics.CombatTurns             = combat.CombatTurns;
            floorMetrics.AvgHitsToKill           = combat.AvgHitsToKill;
            floorMetrics.EscalatorPresent        = combat.EscalatorPresent;
            floorMetrics.SpikePresent            = combat.SpikePresent;
            floorMetrics.EscalatorNeutralizedAtTurn = combat.EscalatorNeutralizedAtTurn;
            floorMetrics.EscalatorNeutralized    = combat.EscalatorNeutralizedAtTurn.HasValue;
            // Build the death record only on a real player DeathEvent (not stuck-aborts, which set
            // PlayerDied but have no killer/engagement to attribute).
            if (floorMetrics.PlayerDied && killerId != NoDeath)
                floorMetrics.Death = combat.BuildDeathRecord(depth, killerId, playerDeathFloorTurn, state);

            // Snapshot per-floor loot telemetry from PityTracker before carrying it forward.
            if (state.PityTracker != null)
            {
                var (counts, fires) = state.PityTracker.SnapshotAndResetFloorTelemetry();
                floorMetrics.LootCategoryCounts = counts;
                floorMetrics.LootHardPityFires  = fires;
            }

            perFloor.Add(floorMetrics);

            totalTurns   += floorTurns;
            totalKills   += floorKills;
            totalPotionsUsed += floorPotions;
            finalHp      = state.PlayerFighter.Hp;
            finalMaxHp   = state.PlayerFighter.MaxHp;
            // Track the last-seen player entity for post-run inventory scan.
            // On death, this is the dead player entity from the final floor.
            lastFloorPlayer = state.Player;

            if (!state.PlayerFighter.IsAlive || runWasAborted)
                break;

            // Carry the same player entity, boon tracker, and pity tracker forward to the next floor.
            // PityTracker persists across floors — drought counters don't reset on descent (PoC-exact).
            player       = state.Player;
            boonTracker  = state.BoonTracker;
            pityTracker  = state.PityTracker;
        }

        sw.Stop();

        // Build the intermediate DungeonRunResult for OutcomeClassifier.
        bool playerDiedThisRun = killerName != null
            || (perFloor.Count > 0 && perFloor[^1].PlayerDied)
            || runWasAborted;

        // Aborted runs bypass OutcomeClassifier and go directly to "aborted" outcome.
        // They count as death-equivalent for Death% and PressureModel calculations.
        string outcome, failureType, failureDetail;
        if (runWasAborted)
        {
            outcome       = OutcomeClassifier.Aborted;
            failureType   = OutcomeClassifier.FailureAborted;
            failureDetail = "stuck_abort: bot stuck counter exceeded threshold";
        }
        else
        {
            var runResult = new DungeonRunResult
            {
                Seed            = baseSeed,
                FloorsAttempted = perFloor.Count,
                FloorsCompleted = floorsCompleted,
                TotalTurns      = totalTurns,
                TotalKills      = totalKills,
                FinalHp         = finalHp,
                PlayerDied      = playerDiedThisRun,
                PerFloor        = perFloor,
            };
            (outcome, failureType, failureDetail) = OutcomeClassifier.Classify(runResult, killerName);
        }

        // Clamp finalHp to 0 on death — the plan specifies "0 if player died".
        // In-game HP can go below 0 on a fatal hit; clamp here for clean data.
        int reportedFinalHp = playerDiedThisRun ? 0 : finalHp;
        double hpFraction   = (finalMaxHp > 0 && !playerDiedThisRun)
            ? (double)finalHp / finalMaxHp
            : 0.0;

        // Count remaining potions from the last-seen player entity.
        // lastFloorPlayer is set every floor (including the death floor), so it is
        // never null after at least one floor was attempted.
        int potionsRemaining = CountHealingPotions(lastFloorPlayer);

        // Finalize telemetry: summarize and adjust DeathsWithUnusedPotions.
        // ComputeFrom() sets DeathsWithUnusedPotions = 1 if last decision had potions.
        // We correct it here: only count if the run actually ended in death.
        BotRunSummary? botSummary = null;
        if (recorder != null && recorder.Decisions.Count > 0)
        {
            var rawSummary = recorder.Summarize();
            int deathsWithUnused = playerDiedThisRun && rawSummary.DeathsWithUnusedPotions > 0 ? 1 : 0;

            botSummary = new BotRunSummary
            {
                Persona                 = personaName,
                TotalDecisions          = rawSummary.TotalDecisions,
                FloorsVisited           = rawSummary.FloorsVisited,
                ActionCounts            = rawSummary.ActionCounts,
                ReasonCounts            = rawSummary.ReasonCounts,
                ContextCounts           = rawSummary.ContextCounts,
                AvgHpWhenHealing        = rawSummary.AvgHpWhenHealing,
                HealDecisions           = rawSummary.HealDecisions,
                DeathsWithUnusedPotions = deathsWithUnused,
            };
        }

        // Enriched transcript: finalize with run-level rollup + verbatim memos.
        if (transcriptRecorder != null)
        {
            int depthReached = perFloor.Count > 0 ? perFloor[^1].Depth : 0;
            var memos = BuildDeliveredMemos(memoRegistry, playerDiedThisRun, killerName, depthReached);
            // Final aggression tally for the run-level value_reconciliation detector.
            int finalTally = lastFloorPlayer?.Get<RunAggressionTally>()?.Total() ?? 0;

            // LLM path: call OnRunEnd BEFORE Finish so the narrative reaches the transcript.
            // OnRunEnd is synchronous with its own timeout; null on failure (safe sentinel).
            string? runNarrative = llmBrain?.OnRunEnd(outcome);

            transcriptRecorder.Finish(
                runId: $"{baseSeed}-{Guid.NewGuid():N}",
                depthReached: depthReached,
                floorCount: perFloor.Count,
                turnCount: totalTurns,
                ending: outcome,
                memos: memos,
                runAggressionTally: finalTally,
                runNarrative: runNarrative);
        }

        return new DungeonSoakRunResult
        {
            Seed                = baseSeed,
            Outcome             = outcome,
            FailureType         = failureType,
            FailureDetail       = failureDetail,
            DeepestFloorReached = perFloor.Count > 0 ? perFloor[^1].Depth : 0,
            FloorsCompleted     = floorsCompleted,
            TotalTurns          = totalTurns,
            TotalKills          = totalKills,
            FinalHp             = reportedFinalHp,
            FinalMaxHp          = finalMaxHp,
            FinalHpFraction     = hpFraction,
            PotionsUsed         = totalPotionsUsed,
            PotionsRemaining    = potionsRemaining,
            BoonsAcquired       = boonTracker?.BoonsApplied.Count ?? 0,
            DurationSeconds     = sw.Elapsed.TotalSeconds,
            PerFloor            = perFloor,
            BotSummary          = botSummary,
            VoiceLineHits       = voiceLineHits.Count > 0 ? voiceLineHits : null,
            WasAborted          = runWasAborted,
            Persona             = personaName,
        };
    }

    /// <summary>
    /// Build a BotDecisionRecord for the harness-driven navigate-to-stair path.
    /// This path is NOT inside BotBrain.Decide() (it's harness logic), so we emit
    /// telemetry manually here with the player's current state.
    /// </summary>
    private static BotDecisionRecord BuildNavigateRecord(
        GameState state,
        int turnNumber,
        int floorDepth,
        string actionType,
        string reason)
    {
        var fighter = state.PlayerFighter;
        double hpFraction = fighter.MaxHp > 0 ? (double)fighter.Hp / fighter.MaxHp : 0.0;
        var inventory = state.PlayerInventory;

        int potions = 0;
        if (inventory != null)
        {
            foreach (var item in inventory.Items)
            {
                var consumable = item.Get<Consumable>();
                if (consumable?.IsHealing == true)
                    potions += consumable.StackSize;
            }
        }

        return new BotDecisionRecord
        {
            TurnNumber              = turnNumber,
            FloorDepth              = floorDepth,
            ActionType              = actionType,
            Reason                  = reason,
            HpFraction              = hpFraction,
            VisibleEnemies          = 0, // floor is clear when navigating to stair
            AdjacentEnemies         = 0,
            HealingPotionsAvailable = potions,
            InCombat                = false,
            LowHp                   = hpFraction <= BotConfig.HealThreshold, // balanced threshold for navigate records
        };
    }

    private static double HpFraction(Fighter fighter)
        => fighter.MaxHp > 0 ? (double)fighter.Hp / fighter.MaxHp : 0.0;

    /// <summary>
    /// True iff a player-initiated PossessionEffect exists on any monster this turn. Derived
    /// from the effect's presence — NOT from ControlledEntity — so the rubric's
    /// `possession_body_inconsistent` predicate (possession_active AND controlled==player) can
    /// catch the bug where the effect exists but control was never transferred. ControlledEntity
    /// additionally requires PossessorEntityId == Player.Id, so a mismatch there trips the check.
    /// </summary>
    private static bool IsPlayerPossessionActive(GameState state)
    {
        foreach (var m in state.Monsters)
            if (m.Get<Combat.StatusEffects.PossessionEffect>() is { Source: Combat.StatusEffects.PossessionSource.PlayerInitiated })
                return true;
        return false;
    }

    /// <summary>
    /// Count of meaningful actions available to the player this turn — the signal the
    /// soft_lock / dead_action_space predicates key on. Formula (documented in the schema):
    /// walkable adjacent tiles (8-dir) + adjacent living monsters + (1 if a healing item is
    /// held) + (1 if standing on the down stair). Reaches 0 only in a genuine soft-lock.
    /// </summary>
    private static int ComputeAvailableActionCount(GameState state)
    {
        var player = state.Player;
        int count = 0;

        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            int nx = player.X + dx, ny = player.Y + dy;

            // An adjacent living monster is an attack option.
            bool monsterHere = false;
            foreach (var m in state.AliveMonsters)
                if (m.X == nx && m.Y == ny) { monsterHere = true; break; }
            if (monsterHere) { count++; continue; }

            if (state.Map.CanMoveToWith(nx, ny, player, canPassDoors: true))
                count++;
        }

        if (CountHealingPotions(player) > 0) count++;
        if (state.PlayerOnStairDown) count++;

        return count;
    }

    /// <summary>
    /// Run MemoDeliveryEvaluator against a FRESH in-memory persistent state and return the
    /// memos delivered this run, verbatim. A clean slate means first-fire variants dominate
    /// (death_first, floor_low) — sufficient for capturing memo text in the transcript;
    /// cross-run memo evolution is a separate concern. Returns empty if no registry is wired.
    /// </summary>
    private static IReadOnlyList<MemoRecord> BuildDeliveredMemos(
        MemoRegistry? memoRegistry, bool died, string? killerName, int depthReached)
    {
        if (memoRegistry == null) return Array.Empty<MemoRecord>();

        var pstate = PersistentRunState.CreateEmpty();
        var ctx = new PostRunContext(
            Died: died,
            CauseOfDeath: null,        // raw engine cause string is not tracked by this harness
            KillerSpecies: killerName,
            FloorReached: depthReached,
            RunNumber: 1,
            // No authoritative EndingType source in scope: the per-floor GameState built inside
            // the floor loop above (_floorBuilder.Build(...)) is block-scoped to that loop and is
            // already out of scope here. This harness also never calls WeighingOrchestrator, so
            // even a captured GameState would carry the default. None is honest, not a guess.
            Ending: Logic.Endgame.EndingType.None);

        new MemoDeliveryEvaluator().EvaluateRunEnd(ctx, pstate, memoRegistry);

        var memos = new List<MemoRecord>();
        foreach (var pm in pstate.UnderWarden.PendingMemos)
            memos.Add(new MemoRecord { Key = pm.Key, Subject = pm.Subject, Body = pm.Body });
        return memos;
    }

    /// <summary>
    /// Count healing potions in the entity's inventory.
    /// Returns 0 if entity is null or has no inventory.
    /// </summary>
    private static int CountHealingPotions(Entity? entity)
    {
        var inventory = entity?.Get<Inventory>();
        if (inventory == null) return 0;

        int count = 0;
        foreach (var item in inventory.Items)
        {
            var consumable = item.Get<Consumable>();
            if (consumable?.IsHealing == true)
                count += consumable.StackSize;
        }
        return count;
    }

}
