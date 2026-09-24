using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Endgame;
using UnderWarden.Logic.Knowledge;
using UnderWarden.Logic.Map;
using UnderWarden.Logic.Persistence;

namespace UnderWarden.Logic.Core;

/// <summary>
/// Mutable game state. Holds everything needed to process a turn.
/// Created once at game start, mutated by TurnController each turn.
/// </summary>
public sealed class GameState
{
    public Entity Player { get; }
    public List<Entity> Monsters { get; }
    public GameMap Map { get; }
    public SeededRandom Rng { get; }
    public int TurnCount { get; set; }
    public int TurnLimit { get; set; }

    // --- Dungeon mode properties ---

    /// <summary>
    /// True when running a full dungeon campaign floor. False in scenario/harness mode.
    /// This flag guards all dungeon-specific behavior — the scenario harness path is
    /// completely unaffected when this is false.
    /// </summary>
    public bool IsDungeonMode { get; init; }

    /// <summary>
    /// True when running the balance scenario harness (ScenarioHarness.RunOnce).
    /// Enables PoC-matching monster awareness: monsters start passive and only activate
    /// after being attacked (replicating PoC's fov_map=None behavior where
    /// monster_sees_player is always False and monsters only act when in_combat=True).
    /// False in all other scenario/test modes — those use the always-alerted model.
    /// </summary>
    public bool IsHarnessMode { get; set; }

    /// <summary>Current floor depth. 1-based. Only meaningful when IsDungeonMode=true.</summary>
    public int CurrentDepth { get; init; } = 1;

    /// <summary>The stair-down entity on this floor. Set by DungeonFloorBuilder after construction.</summary>
    public Entity? StairDown { get; set; }

    /// <summary>Items currently on the dungeon floor. Populated by DungeonFloorBuilder, removed on pickup.</summary>
    public List<Entity> FloorItems { get; } = new();

    /// <summary>
    /// Monster knowledge system for this run. Tracks per-species encounter history and exposes
    /// tier-gated info views for the inspect UI. Reset on new game via Knowledge.Reset().
    /// </summary>
    public MonsterKnowledgeSystem Knowledge { get; } = new();

    /// <summary>
    /// Active portal entities on the current floor. Always 0 or 2 (one entrance + one exit).
    /// Managed by PortalSystem — do not mutate directly.
    /// </summary>
    public List<Entity> Portals { get; } = new();

    /// <summary>
    /// Corpse entities on the current floor.
    /// When a monster dies and leaves_corpse is true, the entity is transformed in-place
    /// and added here (it remains in Monsters — dual membership).
    /// Cleared on floor descent. Used by necromancer AI and Raise Dead spell targeting.
    /// </summary>
    public List<Entity> Corpses { get; } = new();

    // ── Identification system ────────────────────────────────────────────────

    /// <summary>
    /// Per-run identification registry. Tracks which item types have been identified.
    /// Survives floor transitions — identified items stay identified across the whole run.
    /// Null in scenario mode (harness runs without identification).
    /// </summary>
    public IdentificationRegistry? IdentificationRegistry { get; init; }

    /// <summary>
    /// Per-run appearance pool. Assigns descriptors and mystery sprites to identifiable items.
    /// Survives floor transitions — same assignments throughout the run.
    /// Null in scenario mode.
    /// </summary>
    public AppearancePool? AppearancePool { get; init; }

    /// <summary>
    /// Difficulty level affecting pre-identification probabilities.
    /// Defaults to Medium (balanced starting identification rates).
    /// </summary>
    public Difficulty Difficulty { get; init; } = Difficulty.Medium;

    /// <summary>
    /// Active ground hazards on the current floor (burning tiles, poison gas, etc.).
    /// Created fresh per floor — hazards do not persist across descent.
    /// </summary>
    public GroundHazardManager GroundHazards { get; } = new();

    /// <summary>
    /// Props placed in rooms on this floor. Set by DungeonFloorBuilder after generation.
    /// Empty in scenario mode — scenarios use flat arenas with no prop placement.
    /// The presentation layer reads this to render prop sprites in Pass 4.
    /// </summary>
    public IReadOnlyList<PlacedProp> Props { get; init; } = Array.Empty<PlacedProp>();

    /// <summary>
    /// Maps locked door positions to their LockColorId. Populated by DungeonFloorBuilder
    /// after EntityPlacer.PlaceFloorFeatures runs. Empty in scenario mode.
    /// TurnController reads this when the player bumps a LockedDoor tile to identify the lock color.
    /// Entries are removed when a door is unlocked (DoorUnlockedEvent fired).
    /// </summary>
    public Dictionary<(int X, int Y), int> LockedDoors { get; } = new();

    /// <summary>
    /// Interactive feature entities on the current floor: chests, signposts, murals.
    /// These block movement (BlocksMovement=true) and respond to bump-interaction via TurnController.
    /// Populated by EntityPlacer.PlaceFloorFeatures after FillRooms; empty in scenario mode.
    /// Unlike Monsters, features have no AI or Fighter — they are purely interactive objects.
    /// Cleared on floor transition by building a new GameState (same contract as Monsters/FloorItems).
    /// </summary>
    public List<Entity> Features { get; } = new();

    /// <summary>
    /// Per-run tracker ensuring mural IDs are unique within each floor.
    /// Carries forward across floors — murals placed on floor 1 are excluded from floor 2+
    /// until the pool is exhausted, then the tracker resets.
    /// Null in scenario mode (harness runs without features unless explicitly enabled).
    /// </summary>
    public MuralTracker? MuralTracker { get; init; }

    // ── Loot pity system ────────────────────────────────────────────────────

    /// <summary>
    /// Per-run pity tracker. Tracks rooms-since-last-appearance for critical loot categories.
    /// Survives floor transitions — passed from old state to new via DungeonFloorBuilder.
    /// Null in scenario mode (harness runs without pity tracking unless explicitly enabled).
    /// </summary>
    public Balance.PityTracker? PityTracker { get; init; }

    // ── Depth boon system ────────────────────────────────────────────────────

    /// <summary>
    /// Per-run boon tracker. Tracks visited depths and applied boons.
    /// Survives floor transitions — passed from old state to new via DungeonFloorBuilder.
    /// Null in scenario mode (harness runs without boons unless explicitly enabled).
    /// </summary>
    public BoonTracker? BoonTracker { get; init; }

    /// <summary>
    /// Depth → BoonDefinition mapping loaded from config/depth_boons.yaml.
    /// Static content for the run — set once by DungeonFloorBuilder constructor.
    /// Null when boons are not loaded (scenario mode).
    /// </summary>
    public IReadOnlyDictionary<int, BoonDefinition>? BoonTable { get; init; }

    /// <summary>
    /// Per-floor entity ID allocator. Used by runtime spawn actions (trap rouse, bone pile)
    /// to generate new entity IDs that won't collide with existing entities.
    /// Null in scenario mode — spawns from traps use a fallback allocator starting from
    /// max-existing-id + 1 when this is not set.
    /// Set by DungeonFloorBuilder after construction.
    /// </summary>
    public EntityIdAllocator? IdAllocator { get; init; }

    /// <summary>
    /// Rooms from the generated floor map. Populated by DungeonFloorBuilder.Build().
    /// Null in scenario/harness mode. Used by ETP sanity analysis to correlate
    /// monster positions to rooms.
    /// </summary>
    public IReadOnlyList<UnderWarden.Logic.ECS.Room>? Rooms { get; init; }

    // ── Cross-run persistence ────────────────────────────────────────────────

    /// <summary>
    /// Cross-run persistent state. Loaded once at app start; survives floor transitions.
    /// Null in scenario/harness mode — cross-run state has no meaning in isolated runs.
    /// Consumers call MarkDirty() after mutations; Flush() at narrative-event boundaries.
    /// See plan_cross_run_persistence.md for the full lifecycle spec.
    /// </summary>
    public PersistentRunState? PersistentState { get; init; }

    // ── Run-scoped tier-1 state kept here per spec §2 tier discipline ────────

    /// <summary>
    /// Past-Sasha record IDs consumed in the current run. Resets at run start.
    /// The records themselves are cross-run (PersistentState.PastSashas); this set
    /// is run-scoped to prevent re-encountering the same corpse twice in one run.
    /// </summary>
    public HashSet<int> PastSashasEncounteredThisRun { get; } = new();

    /// <summary>
    /// Counter driving the "3 unprovoked kills → Hostile" faction transition.
    /// Single-sourced from the run-scoped tally on the player entity (RunAggressionTally, TASK-003),
    /// which accumulates across floors and resets per run. "orc" mirrors FactionsData.OrcFactionId.
    /// The resulting reputation enum is cross-run (PersistentState.Factions).
    /// </summary>
    public int UnprovokedOrcKillsThisRun =>
        Player.Get<RunAggressionTally>()?.UnprovokedKillsFor("orc") ?? 0;

    /// <summary>
    /// Species TypeId of the entity that dealt the killing blow to the player this run.
    /// Set in TurnController when a monster's attack kills the player. Null if death was
    /// by hazard/DoT or the player survived. Used to populate PastSashaRecord on game-over.
    /// </summary>
    public string? PlayerDeathKillerSpecies { get; set; }

    /// <summary>
    /// High-level cause of death for the current run. "monster" | "hazard" | "under_warden".
    /// Defaults to "monster". Set in TurnController when cause is known at the kill site.
    /// </summary>
    public string PlayerDeathCause { get; set; } = "monster";

    public GameState(Entity player, List<Entity> monsters, GameMap map, SeededRandom rng, int turnLimit = 100)
    {
        Player = player;
        Monsters = monsters;
        Map = map;
        Rng = rng;
        TurnLimit = turnLimit;
    }

    /// <summary>
    /// Recompute field of view from the player's current position.
    /// No-op in scenario mode (IsDungeonMode=false) — scenarios call RevealAll at startup
    /// and never need per-turn FOV recomputation. This guard is what keeps the scenario
    /// harness unaffected by Phase 2.
    /// </summary>
    public void RecomputeFov(int radius = 8)
    {
        if (!IsDungeonMode) return;
        FovComputer.Compute(Map, Player.X, Player.Y, radius);
    }

    public Fighter PlayerFighter => Player.Require<Fighter>();
    public Inventory? PlayerInventory => Player.Get<Inventory>();

    /// <summary>
    /// The entity the player is currently controlling.
    /// During Active possession: the monster carrying PossessionEffect{ Source=PlayerInitiated }.
    /// All other times: Player (the home body).
    ///
    /// TurnController uses this for Move/Attack/UseItem dispatch so the possessed host
    /// acts as the player during possession without branching everywhere.
    /// </summary>
    public Entity ControlledEntity =>
        Monsters.FirstOrDefault(m =>
            m.Get<Combat.StatusEffects.PossessionEffect>() is { } e
            && e.Source == Combat.StatusEffects.PossessionSource.PlayerInitiated
            && e.PossessorEntityId == Player.Id)
        ?? Player;

    /// <summary>
    /// Fighter of the currently controlled entity.
    /// Answers combat and movement rolls during possession.
    /// PlayerFighter always refers to the home body (for game-over checks and drain).
    /// </summary>
    public Fighter ControlledFighter => ControlledEntity.Require<Fighter>();

    // Cached alive-monster list — rebuilt once per turn instead of once per access.
    // Avoids 4-6 List<Entity> allocations per turn from callers hitting AliveMonsters.
    private List<Entity>? _aliveMonsterCache;
    private int _aliveMonsterCacheTurn = -1;

    /// <summary>
    /// Whether the current dungeon floor is clear of living monsters.
    /// Only meaningful in dungeon mode — always false in scenario mode.
    /// </summary>
    public bool IsFloorClear => IsDungeonMode && AliveMonsters.Count == 0;

    /// <summary>
    /// Whether the player is standing on the stair-down tile.
    /// </summary>
    public bool PlayerOnStairDown => StairDown != null && Player.X == StairDown.X && Player.Y == StairDown.Y;

    /// <summary>
    /// CRITICAL: IsGameOver is guarded by IsDungeonMode to preserve exact scenario behaviour.
    ///
    /// Scenario path (IsDungeonMode=false): game over when player dies, all monsters die, or turn limit hit.
    ///   All-monsters-dead → game over is how the harness detects scenario completion.
    ///
    /// Dungeon path (IsDungeonMode=true): game over only when player dies or turn limit hit.
    ///   Floor completion is detected via IsFloorClear + PlayerOnStairDown → DescendEvent, not game over.
    ///   Killing all monsters alone does NOT end a dungeon run.
    /// </summary>
    public bool IsGameOver =>
        IsDungeonMode
            ? !PlayerFighter.IsAlive || TurnCount >= TurnLimit || Ending != EndingType.None
            : !PlayerFighter.IsAlive || (Monsters.Count > 0 && AliveMonsters.Count == 0) || TurnCount >= TurnLimit;

    public bool PlayerWon => PlayerFighter.IsAlive && Monsters.Count > 0 && AliveMonsters.Count == 0;

    /// <summary>
    /// The Weighing's resolved ending (TASK-008). Default None = the run is ongoing. Set by the
    /// Weighing orchestration when the gauntlet resolves; a terminal value also ends the run via
    /// IsGameOver above (covers the chosen non-death loss `LossRefused` and the survival wins, which
    /// are not death- or turn-limit-driven). Dungeon-mode victory state, which did not exist before.
    /// </summary>
    public EndingType Ending { get; set; } = EndingType.None;

    /// <summary>True once the Weighing resolved into one of the three winning endings.</summary>
    public bool IsDungeonVictory =>
        Ending is EndingType.CleanAudit or EndingType.Theft or EndingType.Swap;

    /// <summary>The loaded Weighing arena (map + named anchors), set when floor 25 is built. Null otherwise.</summary>
    public WeighingArena? WeighingArena { get; set; }

    /// <summary>Live Weighing orchestration state (phase, current Guardian, audit tiers). Null off the Weighing floor.</summary>
    public WeighingState? Weighing { get; set; }

    /// <summary>Loaded audit dialogue content. Null until weighing_audit.yaml is loaded at boot.</summary>
    public Content.WeighingAuditRegistry? WeighingAudit { get; set; }

    /// <summary>
    /// Hollowmark voice delivery scheduler (M1.5, docs/systems/voice_delivery.md). Null in scenario/
    /// harness mode and until 5b constructs it from the voice registry + tier metadata. Its mutable run
    /// state is SERIALIZE-class (mid-run save); the registry + metadata are RECONSTRUCT (caller-provided).
    /// Never draws from <see cref="Rng"/> — the gameplay stream is isolated from voice.
    /// </summary>
    public Voice.VoiceScheduler? VoiceScheduler { get; set; }

    /// <summary>
    /// Headless override for the per-Guardian audit tiers. When set, the orchestrator uses this
    /// instead of scoring from persistence — lets the balance pass (TASK-011) drive a specific
    /// audit configuration (e.g. an all-Savage wall) without constructing a matching save. Null in
    /// production: the audit is scored from the run's persistence.
    /// </summary>
    public Endgame.AuditScorer.AuditResult? WeighingAuditOverride { get; set; }

    /// <summary>
    /// Headless decision for the Debt choice gate. When set, the orchestrator applies it instead of
    /// emitting the gate and waiting for UI — the enabler for running the Weighing headlessly in the
    /// harness. Null in production: the presentation layer drives Force / Self / Refuse.
    /// </summary>
    public Endgame.WeighingGateDecision? WeighingHeadlessGatePolicy { get; set; }

    public List<Entity> AliveMonsters
    {
        get
        {
            if (_aliveMonsterCache == null || _aliveMonsterCacheTurn != TurnCount)
            {
                // Use Get<Fighter>() (not Require) — corpse entities in state.Monsters have no Fighter.
                _aliveMonsterCache = Monsters.Where(m => m.Get<Fighter>()?.IsAlive == true).ToList();
                _aliveMonsterCacheTurn = TurnCount;
            }
            return _aliveMonsterCache;
        }
    }
}
