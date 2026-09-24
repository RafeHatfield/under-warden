using YamlDotNet.Serialization;

namespace UnderWarden.Logic.Balance.Etp;

/// <summary>
/// Spike allowance settings — rooms that can exceed normal ETP budgets.
/// </summary>
public sealed class SpikeSettings
{
    [YamlMember(Alias = "max_spike_percent")]
    public double MaxSpikePercent { get; set; } = 50;

    [YamlMember(Alias = "spike_multiplier")]
    public double SpikeMultiplier { get; set; } = 1.5;
}

/// <summary>
/// Budget enforcement tolerance settings.
/// </summary>
public sealed class ToleranceSettings
{
    [YamlMember(Alias = "room_tolerance")]
    public double RoomTolerance { get; set; } = 0.10;

    [YamlMember(Alias = "floor_tolerance")]
    public double FloorTolerance { get; set; } = 0.10;

    [YamlMember(Alias = "warning_threshold")]
    public double WarningThreshold { get; set; } = 0.15;

    [YamlMember(Alias = "error_threshold")]
    public double ErrorThreshold { get; set; } = 0.25;
}

/// <summary>
/// Top-level ETP configuration. Loaded from config/etp_config.yaml.
/// Ported from PoC etp.py:31-104 (ETPConfig dataclass) and etp_config.yaml.
/// </summary>
public sealed class EtpConfig
{
    [YamlMember(Alias = "version")]
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Band definitions keyed by band name ("B1".."B5").
    /// </summary>
    [YamlMember(Alias = "bands")]
    public Dictionary<string, BandConfig> Bands { get; set; } = new();

    /// <summary>
    /// Behavior role → ETP modifier. Keys match behavior_modifiers in etp_config.yaml.
    /// </summary>
    [YamlMember(Alias = "behavior_modifiers")]
    public Dictionary<string, double> BehaviorModifiers { get; set; } = new();

    /// <summary>
    /// AI type → behavior role alias mapping.
    /// Allows monster AI type strings to map to the canonical behavior role.
    /// Extended in C# YAML vs PoC (was hardcoded in etp.py:225-231).
    /// </summary>
    [YamlMember(Alias = "behavior_aliases")]
    public Dictionary<string, string> BehaviorAliases { get; set; } = new();

    [YamlMember(Alias = "spike_settings")]
    public SpikeSettings SpikeSettings { get; set; } = new();

    [YamlMember(Alias = "tolerance")]
    public ToleranceSettings Tolerance { get; set; } = new();
}
