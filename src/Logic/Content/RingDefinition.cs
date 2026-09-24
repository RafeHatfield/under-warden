using YamlDotNet.Serialization;

namespace UnderWarden.Logic.Content;

/// <summary>
/// YAML deserialization class for a single ring entry in the rings: section.
/// Converted to ItemDefinition by ContentLoader.LoadRings for uniform item handling.
/// </summary>
public sealed class RingDefinition
{
    [YamlMember(Alias = "char")]
    public string Char { get; set; } = "=";

    [YamlMember(Alias = "color")]
    public int[] Color { get; set; } = [255, 255, 255];

    [YamlMember(Alias = "ring_effect")]
    public string RingEffect { get; set; } = "";

    [YamlMember(Alias = "effect_strength")]
    public int EffectStrength { get; set; }
}
