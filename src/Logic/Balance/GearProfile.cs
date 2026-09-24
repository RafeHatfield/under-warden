using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// A named starting loadout for staged-start soaks — the gear + stats a player would plausibly have on
/// reaching a region, so a region can be soaked at its own power level instead of grinding up from floor 1.
/// Placeholder values until authored for real during tuning (same status as the target table's numbers).
/// </summary>
public sealed class GearProfile
{
    /// <summary>Profile key (set from the YAML map key by the loader).</summary>
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    // Equipment by slot (YAML item ids). Null/empty = slot left empty.
    public string? MainHand { get; set; }
    public string? Chest { get; set; }
    public string? OffHand { get; set; }
    public string? Head { get; set; }
    public string? Feet { get; set; }

    /// <summary>Healing potions in the starting inventory.</summary>
    public int HealingPotions { get; set; } = 3;

    /// <summary>HP added to the base 54 — a coarse stand-in for character growth at this region.</summary>
    public int BonusHp { get; set; }

    // Stat overrides (null = keep the default 14). Stand-ins for level-up gains.
    public int? Strength { get; set; }
    public int? Dexterity { get; set; }
    public int? Constitution { get; set; }
}

/// <summary>
/// Loads config/balance/gear_profiles.yaml into named GearProfiles. Same YamlDotNet +
/// UnderscoredNamingConvention pattern as TargetTableLoader / EtpConfigLoader.
/// </summary>
public static class GearProfileLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static IReadOnlyDictionary<string, GearProfile> FromFile(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Gear profiles not found: '{path}'");
        return FromYaml(File.ReadAllText(path));
    }

    public static IReadOnlyDictionary<string, GearProfile> FromYaml(string yaml)
    {
        var dto = Deserializer.Deserialize<GearProfilesDto>(yaml)
            ?? throw new InvalidOperationException("Failed to deserialize gear_profiles.yaml.");
        if (dto.Profiles == null || dto.Profiles.Count == 0)
            throw new InvalidOperationException("gear_profiles.yaml has no profiles.");

        // Stamp each profile with its map key so callers can resolve + report by name.
        foreach (var (key, profile) in dto.Profiles)
            profile.Name = key;
        return dto.Profiles;
    }

    private sealed class GearProfilesDto
    {
        public Dictionary<string, GearProfile>? Profiles { get; set; }
    }
}
