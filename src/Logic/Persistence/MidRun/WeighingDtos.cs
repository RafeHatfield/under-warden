using UnderWarden.Logic.Endgame;

namespace UnderWarden.Logic.Persistence.MidRun;

// DTOs for the floor-25 Weighing subsystems (M1.4 4a.3b-3). SERIALIZE-class per spec. The EXCLUDE-class
// hooks (WeighingAuditOverride, WeighingHeadlessGatePolicy) are never touched.

public sealed record WeighingStateDto(
    WeighingPhase Phase, int CurrentGuardianIndex,
    GuardianTier AuditWarden, GuardianTier AuditOathkeeper, GuardianTier AuditAssembly, GuardianTier AuditAuditor,
    bool SwapAvailable, bool SwapChosen, string OrcRepState, int CumulativeDeaths,
    int? ActiveGuardianId, int[] AlliedGuardianIds, GuardianId[] AlliedGuardianTypes,
    int? DebtId, bool AlliesFellBack, bool WardenCursePending);

// WeighingArena.Map IS GameState.Map (same object) — only the anchors are serialized; the arena is
// rebuilt around the already-restored map on load, preserving identity.
public sealed record WeighingArenaDto(AnchorDto[] Anchors);
public sealed record AnchorDto(string Name, PointDto[] Cells);

// WeighingAuditRegistry is config-shaped dialogue, serialized as captured (self-contained ruling).
public sealed record WeighingAuditDto(AuditSequenceDto[] Sequences);
public sealed record AuditSequenceDto(string Key, DialoguePageDto[] Pages);
public sealed record DialoguePageDto(string Speaker, string Text);
