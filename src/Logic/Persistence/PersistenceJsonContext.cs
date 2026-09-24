using System.Text.Json.Serialization;
using UnderWarden.Logic.Persistence.Namespaces;

namespace UnderWarden.Logic.Persistence;

/// <summary>
/// Source-generated JSON serialization context. Required for iOS NativeAOT with
/// InvariantGlobalization=true — reflection-based serialization is not safe under NativeAOT.
/// Every persistence type is registered here so the compiler generates the serializers.
///
/// Root types (passed to JsonSerializer directly):
///   PersistenceFile, DailySeedsFile
///
/// All other types are reached transitively by the source generator from these two roots.
/// Closed generics (NamespaceEnvelope<T>) are registered explicitly for each T.
/// </summary>
[JsonSerializable(typeof(PersistenceFile))]
[JsonSerializable(typeof(DailySeedsFile))]
// Namespace envelopes — each closed generic explicitly registered for NativeAOT safety.
[JsonSerializable(typeof(NamespaceEnvelope<RunCounterData>))]
[JsonSerializable(typeof(NamespaceEnvelope<PastSashasData>))]
[JsonSerializable(typeof(NamespaceEnvelope<FactionsData>))]
[JsonSerializable(typeof(NamespaceEnvelope<BorrekData>))]
[JsonSerializable(typeof(NamespaceEnvelope<VeshData>))]
[JsonSerializable(typeof(NamespaceEnvelope<HaelData>))]
[JsonSerializable(typeof(NamespaceEnvelope<MaryaFragmentsData>))]
[JsonSerializable(typeof(NamespaceEnvelope<HaelHintsData>))]
[JsonSerializable(typeof(NamespaceEnvelope<FreedPastSelvesData>))]
[JsonSerializable(typeof(NamespaceEnvelope<UnshrivenGeasData>))]
[JsonSerializable(typeof(NamespaceEnvelope<HollowmarkMetaData>))]
[JsonSerializable(typeof(NamespaceEnvelope<AchievementsData>))]
[JsonSerializable(typeof(NamespaceEnvelope<EncountersData>))]
[JsonSerializable(typeof(NamespaceEnvelope<HollowmarkSpanData>))]
[JsonSerializable(typeof(NamespaceEnvelope<UnderWardenData>))]
// Namespace data classes
[JsonSerializable(typeof(RunCounterData))]
[JsonSerializable(typeof(PastSashasData))]
[JsonSerializable(typeof(FactionsData))]
[JsonSerializable(typeof(BorrekData))]
[JsonSerializable(typeof(VeshData))]
[JsonSerializable(typeof(HaelData))]
[JsonSerializable(typeof(MaryaFragmentsData))]
[JsonSerializable(typeof(HaelHintsData))]
[JsonSerializable(typeof(FreedPastSelvesData))]
[JsonSerializable(typeof(UnshrivenGeasData))]
[JsonSerializable(typeof(HollowmarkMetaData))]
[JsonSerializable(typeof(AchievementsData))]
[JsonSerializable(typeof(EncountersData))]
[JsonSerializable(typeof(HollowmarkSpanData))]
[JsonSerializable(typeof(UnderWardenData))]
// Nested record types
[JsonSerializable(typeof(PendingMemo))]
[JsonSerializable(typeof(PastSashaRecord))]
[JsonSerializable(typeof(GearItemRecord))]
[JsonSerializable(typeof(FactionStateEntry))]
[JsonSerializable(typeof(MaryaFragmentRecord))]
[JsonSerializable(typeof(HaelHintRecord))]
[JsonSerializable(typeof(FreedPastSelfRecord))]
[JsonSerializable(typeof(AchievementRecord))]
[JsonSerializable(typeof(DailySeedRecord))]
// Collection types
[JsonSerializable(typeof(List<PendingMemo>))]
[JsonSerializable(typeof(List<PastSashaRecord>))]
[JsonSerializable(typeof(List<GearItemRecord>))]
[JsonSerializable(typeof(List<MaryaFragmentRecord>))]
[JsonSerializable(typeof(List<HaelHintRecord>))]
[JsonSerializable(typeof(List<FreedPastSelfRecord>))]
[JsonSerializable(typeof(List<AchievementRecord>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(HashSet<string>))]
[JsonSerializable(typeof(Dictionary<string, FactionStateEntry>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(Dictionary<string, DailySeedRecord>))]
public partial class PersistenceJsonContext : JsonSerializerContext { }
