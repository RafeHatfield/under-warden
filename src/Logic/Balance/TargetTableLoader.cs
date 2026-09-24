using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Loads config/balance/target_table.yaml into a <see cref="TargetTable"/>.
/// Follows the EtpConfigLoader pattern — YamlDotNet with UnderscoredNamingConvention for snake_case keys.
/// </summary>
public static class TargetTableLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static TargetTable FromFile(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Target table not found: '{path}'");
        return FromYaml(File.ReadAllText(path));
    }

    public static TargetTable FromYaml(string yaml)
    {
        var dto = Deserializer.Deserialize<TargetTableDto>(yaml)
            ?? throw new InvalidOperationException("Failed to deserialize target_table.yaml.");
        if (dto.Regions == null || dto.Regions.Count == 0)
            throw new InvalidOperationException("target_table.yaml has no regions.");

        var regions = new List<TargetRegion>(dto.Regions.Count);
        foreach (var (name, r) in dto.Regions)
        {
            if (r.DeathPct == null)
                throw new InvalidOperationException($"Region '{name}' is missing death_pct.");

            var byArchetype = new Dictionary<ThreatArchetype, ArchetypeTarget>();
            if (r.HitsToDown != null)
            {
                foreach (var (key, hits) in r.HitsToDown)
                {
                    var archetype = ThreatArchetypeTag.Parse(key)
                        ?? throw new InvalidOperationException(
                            $"Region '{name}' has unknown archetype key '{key}' under hits_to_down.");
                    byArchetype[archetype] = new ArchetypeTarget(hits);
                }
            }

            LeverExpectation? leverExpectation = r.LeverExpectations == null
                ? null
                : new LeverExpectation(
                    DamagePerHit: r.LeverExpectations.DamagePerHit,
                    KillerHitRate: r.LeverExpectations.KillerHitRate,
                    CounterattacksLanded: r.LeverExpectations.CounterattacksLanded,
                    DistinctAttackers: r.LeverExpectations.DistinctAttackers,
                    AttackFrequency: r.LeverExpectations.AttackFrequency);

            regions.Add(new TargetRegion(
                Name: name,
                DepthMin: r.DepthMin,
                DepthMax: r.DepthMax,
                DeathPct: new TargetBand(r.DeathPct.Min, r.DeathPct.Max),
                ByArchetype: byArchetype,
                LeverExpectation: leverExpectation));
        }

        return new TargetTable(regions);
    }

    // ── YAML DTOs (snake_case via UnderscoredNamingConvention) ──────────────────────────────────
    private sealed class TargetTableDto
    {
        public string Version { get; set; } = "";
        public Dictionary<string, RegionDto>? Regions { get; set; }
    }

    private sealed class RegionDto
    {
        public int DepthMin { get; set; }
        public int DepthMax { get; set; }
        public BandDto? DeathPct { get; set; }
        public Dictionary<string, double>? HitsToDown { get; set; }
        public LeverExpectationsDto? LeverExpectations { get; set; }
    }

    private sealed class BandDto
    {
        public double Min { get; set; }
        public double Max { get; set; }
    }

    private sealed class LeverExpectationsDto
    {
        public double DamagePerHit { get; set; }
        public double KillerHitRate { get; set; }
        public double CounterattacksLanded { get; set; }
        public double DistinctAttackers { get; set; }
        public double AttackFrequency { get; set; }
    }
}
