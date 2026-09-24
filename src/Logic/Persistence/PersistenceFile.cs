using System.Text.Json.Serialization;
using UnderWarden.Logic.Persistence.Namespaces;

namespace UnderWarden.Logic.Persistence;

/// <summary>
/// Top-level on-disk format for the main persistence file.
/// Shape: { "schema_version": 1, "saved_at": "...", "namespaces": { ... } }
/// </summary>
public sealed class PersistenceFile
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("saved_at")]
    public DateTimeOffset SavedAt { get; set; }

    [JsonPropertyName("namespaces")]
    public NamespacesDto Namespaces { get; set; } = new();
}

/// <summary>
/// All 15 namespace envelopes. Each carries its own schema_version for fine-grained migration.
/// </summary>
public sealed class NamespacesDto
{
    [JsonPropertyName("run_counter")]
    public NamespaceEnvelope<RunCounterData> RunCounter { get; set; } = new();

    [JsonPropertyName("past_sashas")]
    public NamespaceEnvelope<PastSashasData> PastSashas { get; set; } = new();

    [JsonPropertyName("factions")]
    public NamespaceEnvelope<FactionsData> Factions { get; set; } = new();

    [JsonPropertyName("borrek")]
    public NamespaceEnvelope<BorrekData> Borrek { get; set; } = new();

    [JsonPropertyName("vesh")]
    public NamespaceEnvelope<VeshData> Vesh { get; set; } = new();

    [JsonPropertyName("hael")]
    public NamespaceEnvelope<HaelData> Hael { get; set; } = new();

    [JsonPropertyName("marya_fragments")]
    public NamespaceEnvelope<MaryaFragmentsData> MaryaFragments { get; set; } = new();

    [JsonPropertyName("hael_hints")]
    public NamespaceEnvelope<HaelHintsData> HaelHints { get; set; } = new();

    [JsonPropertyName("freed_past_selves")]
    public NamespaceEnvelope<FreedPastSelvesData> FreedPastSelves { get; set; } = new();

    [JsonPropertyName("unshriven_geas")]
    public NamespaceEnvelope<UnshrivenGeasData> UnshrivenGeas { get; set; } = new();

    [JsonPropertyName("hollowmark_meta")]
    public NamespaceEnvelope<HollowmarkMetaData> HollowmarkMeta { get; set; } = new();

    [JsonPropertyName("achievements")]
    public NamespaceEnvelope<AchievementsData> Achievements { get; set; } = new();

    [JsonPropertyName("encounters")]
    public NamespaceEnvelope<EncountersData> Encounters { get; set; } = new();

    [JsonPropertyName("hollowmark_span")]
    public NamespaceEnvelope<HollowmarkSpanData> HollowmarkSpan { get; set; } = new();

    [JsonPropertyName("under_warden")]
    public NamespaceEnvelope<UnderWardenData> UnderWarden { get; set; } = new();
}

/// <summary>
/// Per-namespace wrapper that carries the namespace's own schema_version alongside its data.
/// This allows per-namespace migrations independent of the top-level schema version.
/// See spec §3 for the migration rules (when to bump vs. rely on default-value escape hatch).
/// </summary>
public sealed class NamespaceEnvelope<T> where T : new()
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("data")]
    public T Data { get; set; } = new();
}
