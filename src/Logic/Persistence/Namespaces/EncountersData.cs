using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.Namespaces;

public sealed class EncountersData
{
    [JsonPropertyName("met_borrek")]
    public bool MetBorrek { get; set; }

    [JsonPropertyName("met_vesh")]
    public bool MetVesh { get; set; }

    [JsonPropertyName("met_hael")]
    public bool MetHael { get; set; }

    [JsonPropertyName("met_under_warden")]
    public bool MetUnderWarden { get; set; }

    // Reserved — the Lady of the Long Hour does not appear in v1.
    [JsonPropertyName("met_lady_of_long_hour")]
    public bool MetLadyOfLongHour { get; set; }

    public void RecordMetBorrek()      => MetBorrek = true;
    public void RecordMetVesh()        => MetVesh = true;
    public void RecordMetHael()        => MetHael = true;
    public void RecordMetUnderWarden() => MetUnderWarden = true;
}
