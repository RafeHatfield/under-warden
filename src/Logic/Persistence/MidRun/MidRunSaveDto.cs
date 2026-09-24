using UnderWarden.Logic.Core;
using UnderWarden.Logic.Endgame;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Persistence.MidRun;

/// <summary>
/// Full mid-run save (M1.4 §GameState field classification). Self-contained snapshot: the entity
/// table plus the SERIALIZE-class subsystem/scalar state, enough to reconstruct a playable GameState.
///
/// Scope (4a.3b): the mode-agnostic core + Map/FOV + Knowledge + GroundHazards + Rng + id watermark.
/// Dungeon-only subsystems (IdentificationRegistry, AppearancePool, Mural/Pity/Boon trackers, Rooms,
/// Props, LockedDoors, Weighing*) are 4a.3b-2 — SaveMidRun fails loudly if it sees them populated
/// rather than silently dropping them. EXCLUDE-class fields (IsHarnessMode, WeighingAuditOverride,
/// WeighingHeadlessGatePolicy) and cross-run PersistentState never appear here.
/// </summary>
public sealed class MidRunSaveDto
{
    public int SchemaVersion { get; set; } = MidRunSchema.Version;

    // Entity graph (flat, reference-resolved — see EntitySerializer).
    public EntityTableDto Entities { get; set; } = new();

    // Which table ids belong to which GameState list. Ids not in any list stay unattached table
    // members (inventory/equipment contents, unopened chest loot).
    public int PlayerId { get; set; }
    public int[] MonsterIds { get; set; } = Array.Empty<int>();
    public int[] FloorItemIds { get; set; } = Array.Empty<int>();
    public int[] CorpseIds { get; set; } = Array.Empty<int>();
    public int[] FeatureIds { get; set; } = Array.Empty<int>();
    public int[] PortalIds { get; set; } = Array.Empty<int>();
    public int? StairDownId { get; set; }

    // Map (tiles + walkable + FOV mask (visible) + explored + theme), flattened row-major.
    public GameMapDto Map { get; set; } = new();

    // Subsystems (mode-agnostic).
    public KnowledgeEntryDto[] Knowledge { get; set; } = Array.Empty<KnowledgeEntryDto>();
    public GroundHazardDto[] GroundHazards { get; set; } = Array.Empty<GroundHazardDto>();

    // Dungeon-only subsystems (null/empty in scenario mode).
    public IdentificationDto? Identification { get; set; }
    public AppearancePoolDto? Appearance { get; set; }
    public MuralTrackerDto? Murals { get; set; }
    public PityTrackerDto? Pity { get; set; }
    public BoonTrackerDto? Boons { get; set; }
    public RoomDto[]? Rooms { get; set; }
    public PlacedPropDto[]? Props { get; set; }
    public LockedDoorDto[] LockedDoors { get; set; } = Array.Empty<LockedDoorDto>();

    // Floor-25 Weighing subsystems (null off the weighing floor).
    public WeighingStateDto? Weighing { get; set; }
    public WeighingArenaDto? WeighingArena { get; set; }
    public WeighingAuditDto? WeighingAudit { get; set; }

    // Hollowmark voice scheduler run state (M1.5). Null in scenario/harness mode and until 5b builds
    // the scheduler — so every existing save stays byte-identical (S1/S2 unaffected).
    public VoiceSchedulerStateDto? Voice { get; set; }

    // Rng continuity (SeededRandom Seed + CallCount → Restore).
    public int RngSeed { get; set; }
    public long RngCallCount { get; set; }

    // IdAllocator next-id watermark (null when the run has no allocator — scenario mode).
    public int? IdAllocatorWatermark { get; set; }

    // Scalar block.
    public int CurrentDepth { get; set; } = 1;
    public int TurnCount { get; set; }
    public int TurnLimit { get; set; }
    public bool IsDungeonMode { get; set; }
    public Difficulty Difficulty { get; set; } = Difficulty.Medium;
    public int[] PastSashasEncounteredThisRun { get; set; } = Array.Empty<int>();
    public string? PlayerDeathKillerSpecies { get; set; }
    public string PlayerDeathCause { get; set; } = "monster";
    public EndingType Ending { get; set; } = EndingType.None;
}

/// <summary>Single source of truth for the on-disk schema version.</summary>
public static class MidRunSchema
{
    public const int Version = 1;
}

public sealed class GameMapDto
{
    public int Width { get; set; }
    public int Height { get; set; }
    public TileKind[] Tiles { get; set; } = Array.Empty<TileKind>();
    public bool[] Walkable { get; set; } = Array.Empty<bool>();
    public bool[] Visible { get; set; } = Array.Empty<bool>();   // the serialized FOV mask
    public bool[] Explored { get; set; } = Array.Empty<bool>();
    public TileTheme[] Theme { get; set; } = Array.Empty<TileTheme>();

    /// <summary>Ids of entities registered on the map (occupancy index) — re-registered on load.</summary>
    public int[] RegisteredEntityIds { get; set; } = Array.Empty<int>();

    /// <summary>Prop-blocked cells, flattened [x0,y0,x1,y1,...]. Empty in scenario mode.</summary>
    public int[] PropCells { get; set; } = Array.Empty<int>();
}

public sealed record KnowledgeEntryDto(string SpeciesId, int SeenCount, int EngagedCount, int KilledCount, string[] TraitsDiscovered);

public sealed record GroundHazardDto(HazardType Type, int X, int Y, int BaseDamage, int MaxDuration, int RemainingTurns, bool JustPlaced);
