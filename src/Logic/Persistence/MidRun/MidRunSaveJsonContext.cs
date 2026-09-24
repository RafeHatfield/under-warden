using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.MidRun;

/// <summary>
/// Source-generated JSON context for the full mid-run save (NativeAOT-safe, reflection-free hot path).
/// MidRunSaveDto transitively reaches the entity table, map, and subsystem DTOs + their enums.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(MidRunSaveDto))]
[JsonSerializable(typeof(GameMapDto))]
[JsonSerializable(typeof(KnowledgeEntryDto))]
[JsonSerializable(typeof(GroundHazardDto))]
[JsonSerializable(typeof(EntityTableDto))]
[JsonSerializable(typeof(VoiceSchedulerStateDto))]
[JsonSerializable(typeof(VoiceBagDto))]
[JsonSerializable(typeof(VoiceHistoryDto))]
public partial class MidRunSaveJsonContext : JsonSerializerContext { }
