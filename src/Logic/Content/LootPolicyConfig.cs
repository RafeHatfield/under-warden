using UnderWarden.Logic.Balance;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Deserialized band-level EV targets from config/loot_policy.yaml.
/// One entry per band (B1-B5), keyed by band name.
/// </summary>
public sealed class LootPolicyBandConfig
{
    [YamlMember(Alias = "healing_ev")]
    public double HealingEv { get; set; }

    [YamlMember(Alias = "defensive_ev")]
    public double DefensiveEv { get; set; }

    [YamlMember(Alias = "panic_ev")]
    public double PanicEv { get; set; }

    [YamlMember(Alias = "offensive_ev")]
    public double OffensiveEv { get; set; }

    [YamlMember(Alias = "utility_ev")]
    public double UtilityEv { get; set; }

    [YamlMember(Alias = "upgrade_weapon_ev")]
    public double UpgradeWeaponEv { get; set; }

    [YamlMember(Alias = "upgrade_armor_ev")]
    public double UpgradeArmorEv { get; set; }

    [YamlMember(Alias = "rare_ev")]
    public double RareEv { get; set; }

    // key_ev intentionally omitted — no keys exist yet (DEVIATION-004)
}

/// <summary>Pity thresholds (soft/hard) for a single band.</summary>
public sealed class PityBandThreshold
{
    [YamlMember(Alias = "soft")]
    public int Soft { get; set; }

    [YamlMember(Alias = "hard")]
    public int Hard { get; set; }
}

/// <summary>Pity configuration section from loot_policy.yaml.</summary>
public sealed class LootPityConfig
{
    [YamlMember(Alias = "soft_bias_factor")]
    public double SoftBiasFactor { get; set; } = 2.0;

    [YamlMember(Alias = "tracked_categories")]
    public List<string> TrackedCategories { get; set; } = new();

    [YamlMember(Alias = "thresholds")]
    public Dictionary<string, PityBandThreshold> Thresholds { get; set; } = new();
}

/// <summary>Root YAML structure for loot_policy.yaml.</summary>
internal sealed class LootPolicyFile
{
    [YamlMember(Alias = "version")]
    public string Version { get; set; } = "1.0";

    [YamlMember(Alias = "bands")]
    public Dictionary<string, LootPolicyBandConfig> Bands { get; set; } = new();

    [YamlMember(Alias = "pity")]
    public LootPityConfig Pity { get; set; } = new();
}

/// <summary>
/// Loaded and queryable view of config/loot_policy.yaml.
///
/// Provides band-level EV targets for each category (used to build the category
/// selection weight table in LootController) and pity thresholds (used by PityTracker).
///
/// Loaded once at startup; read-only after construction.
/// </summary>
public sealed class LootPolicyConfig
{
    private readonly Dictionary<string, LootPolicyBandConfig> _bands;
    private readonly LootPityConfig _pity;

    private LootPolicyConfig(Dictionary<string, LootPolicyBandConfig> bands, LootPityConfig pity)
    {
        _bands = bands;
        _pity = pity;
    }

    /// <summary>
    /// Load from a YAML string. Uses YamlDotNet with underscore naming convention.
    /// </summary>
    public static LootPolicyConfig FromYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithObjectFactory(new AotObjectFactory())
            .IgnoreUnmatchedProperties()
            .Build();

        var file = deserializer.Deserialize<LootPolicyFile>(yaml);

        return new LootPolicyConfig(
            file?.Bands ?? new Dictionary<string, LootPolicyBandConfig>(),
            file?.Pity ?? new LootPityConfig());
    }

    /// <summary>Load from a file path. Convenience wrapper for tests and harness.</summary>
    public static LootPolicyConfig FromFile(string path)
        => FromYaml(File.ReadAllText(path));

    /// <summary>
    /// Get the category → EV weight dictionary for a band.
    ///
    /// Returns a dictionary suitable for weighted-random category selection:
    ///   "healing" → 2.5, "panic" → 1.5, etc.
    ///
    /// The "key" category is always omitted (no keys yet).
    /// Returns an empty dictionary if no band config is found (defensive).
    /// </summary>
    public IReadOnlyDictionary<string, double> GetBandEvs(LootBand band)
    {
        string key = band.ToString(); // "B1", "B2", etc.
        if (!_bands.TryGetValue(key, out var cfg))
            return new Dictionary<string, double>();

        return new Dictionary<string, double>
        {
            ["healing"]        = cfg.HealingEv,
            ["defensive"]      = cfg.DefensiveEv,
            ["panic"]          = cfg.PanicEv,
            ["offensive"]      = cfg.OffensiveEv,
            ["utility"]        = cfg.UtilityEv,
            ["upgrade_weapon"] = cfg.UpgradeWeaponEv,
            ["upgrade_armor"]  = cfg.UpgradeArmorEv,
            ["rare"]           = cfg.RareEv,
            // key deliberately omitted
        };
    }

    /// <summary>
    /// Get soft and hard pity thresholds for a tracked category in a band.
    ///
    /// Thresholds are band-level: B1 is more forgiving than B3+.
    /// Returns (soft: 6, hard: 8) as a safe default if the category or band is not configured.
    /// </summary>
    public (int Soft, int Hard) GetPityThreshold(string category, LootBand band)
    {
        string bandKey = band.ToString();

        // Thresholds are per-band (not per-category) in the YAML.
        // The same threshold set applies to all tracked categories within a band.
        if (_pity.Thresholds.TryGetValue(bandKey, out var threshold))
            return (threshold.Soft, threshold.Hard);

        // Safe defaults: B3+ values
        return (4, 6);
    }

    /// <summary>Weight multiplier applied to a category when soft pity is active (default 2.0x).</summary>
    public double SoftBiasFactor => _pity.SoftBiasFactor;

    /// <summary>Categories that have pity counters (PoC-exact: healing, panic, upgrade_weapon, upgrade_armor).</summary>
    public IReadOnlyList<string> TrackedCategories => _pity.TrackedCategories;
}
