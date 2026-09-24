using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Persistence.MidRun;

// DTOs for the dungeon-mode subsystems (M1.4 §GameState field classification). Every collection is
// serialized in a canonical order by the saver so S1 stays byte-identical.

public sealed record StringPairDto(string Key, string Value);
public sealed record IntEntryDto(string Key, int Count);

public sealed record IdentificationDto(string[] Identified, string[] DecidedUnidentified, bool AlwaysIdentified);

public sealed record AppearancePoolDto(
    StringPairDto[] Descriptors, StringPairDto[] MysterySprites,
    string[] PotionTypes, string[] ScrollTypes, string[] WandTypes, string[] RingTypes);

public sealed record MuralTrackerDto(string[] UsedThisFloor, string[] UsedThisRun);

public sealed record PityTrackerDto(IntEntryDto[] Counters, string[] PendingHardInjects, IntEntryDto[] LootItemCounts, int HardPityFireCount);

public sealed record BoonTrackerDto(int[] VisitedDepths, string[] BoonsApplied, bool DisableDepthBoons);

public sealed record RoomDto(int X, int Y, int Width, int Height, RoomShape Shape, RoomArchetype Archetype,
    bool IsDeadEnd, bool IsGrandShrine, bool IsVault, RoomMaintenanceState MaintenanceState);

public sealed record PlacedPropDto(string PropId, int X, int Y, int FootprintW, int FootprintH,
    bool BlocksMovement, int TileId, int? OverlayTileId, int[]? TileLayout, bool FlipH);

public sealed record LockedDoorDto(int X, int Y, int LockColorId);
