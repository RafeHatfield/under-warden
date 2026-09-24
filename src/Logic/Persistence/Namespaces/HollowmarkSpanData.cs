using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.Namespaces;

// Reserved for the Hollowmark binding span mechanic — see spec §6.14 and v3 §C-prime.
// Do NOT implement in v1. The namespace is reserved so v1.x can fill it without a migration.
public sealed class HollowmarkSpanData
{
    [JsonPropertyName("remaining_span")]
    public int? RemainingSpan { get; set; }
}
