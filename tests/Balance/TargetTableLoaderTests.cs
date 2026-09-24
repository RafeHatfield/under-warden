using UnderWarden.Logic.Balance;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// 0c step 2: target_table.yaml → TargetTable → FloorTarget. Pins the schema, depth→region resolution
/// (incl. clamp at the edges so the harness never throws on an out-of-range depth), and that the
/// shipped config loads and feeds the classifier a usable FloorTarget.
/// </summary>
[TestFixture]
public class TargetTableLoaderTests
{
    private const string Yaml = """
        version: "1.0"
        regions:
          B1:
            depth_min: 1
            depth_max: 5
            death_pct: { min: 0.05, max: 0.15 }
            hits_to_down: { baseline: 9, spike: 3, escalator: 4, fused: 3 }
          B2:
            depth_min: 6
            depth_max: 9
            death_pct: { min: 0.08, max: 0.20 }
            hits_to_down: { baseline: 8, spike: 2, escalator: 5, fused: 2 }
        """;

    [Test]
    public void Loads_BothRegions_WithBandsAndArchetypes()
    {
        var table = TargetTableLoader.FromYaml(Yaml);
        Assert.That(table.Regions, Has.Count.EqualTo(2));

        var b1 = table.RegionForDepth(3);
        Assert.That(b1.Name, Is.EqualTo("B1"));
        Assert.That(b1.DeathPct, Is.EqualTo(new TargetBand(0.05, 0.15)));
        Assert.That(b1.ByArchetype[ThreatArchetype.Baseline].TargetHitsToDown, Is.EqualTo(9));
        Assert.That(b1.ByArchetype[ThreatArchetype.Spike].TargetHitsToDown, Is.EqualTo(3));
        Assert.That(b1.ByArchetype[ThreatArchetype.Fused].TargetHitsToDown, Is.EqualTo(3));
    }

    [TestCase(1, "B1")]
    [TestCase(5, "B1")]
    [TestCase(6, "B2")]
    [TestCase(9, "B2")]
    public void RegionForDepth_ResolvesWithinRange(int depth, string expected)
        => Assert.That(TargetTableLoader.FromYaml(Yaml).RegionForDepth(depth).Name, Is.EqualTo(expected));

    [Test]
    public void RegionForDepth_ClampsBelowFirstAndAboveLast()
    {
        var table = TargetTableLoader.FromYaml(Yaml);
        Assert.That(table.RegionForDepth(0).Name, Is.EqualTo("B1"), "depth below first region clamps to it");
        Assert.That(table.RegionForDepth(99).Name, Is.EqualTo("B2"), "depth above last region clamps to it");
    }

    [Test]
    public void ForDepth_ProducesFloorTargetTheClassifierCanConsume()
    {
        var table = TargetTableLoader.FromYaml(Yaml);
        var target = table.ForDepth(7); // B2

        // An in-band, attrition death should read Healthy through this target — end-to-end wiring proof.
        var observed = new FloorObserved(
            DeathPct: 0.10,
            Deaths: new[] { new DeathRecord(ThreatArchetype.Baseline, 8) },
            HasSpike: false, HasEscalator: false, EscalatorReachable: false, Escalator: null);

        Assert.That(FloorHealthClassifier.Classify(observed, target, new ClassifierConfig()),
            Is.EqualTo(FloorHealth.Healthy));
    }

    [Test]
    public void Rejects_UnknownArchetypeKey()
    {
        const string bad = """
            version: "1.0"
            regions:
              B1:
                depth_min: 1
                depth_max: 5
                death_pct: { min: 0.05, max: 0.15 }
                hits_to_down: { tank: 9 }
            """;
        Assert.That(() => TargetTableLoader.FromYaml(bad), Throws.InvalidOperationException);
    }

    [Test]
    public void ShippedConfig_Loads_AndCoversB1ThroughB5()
    {
        var path = FindConfig();
        var table = TargetTableLoader.FromFile(path);
        Assert.That(table.Regions, Has.Count.GreaterThanOrEqualTo(5));
        // Every region the shipped table defines must carry all four archetype anchors.
        foreach (var r in table.Regions)
            Assert.That(r.ByArchetype.Keys, Is.EquivalentTo(new[]
            {
                ThreatArchetype.Baseline, ThreatArchetype.Spike,
                ThreatArchetype.Escalator, ThreatArchetype.Fused,
            }), $"region {r.Name} must define all four archetype targets");
    }

    private static string FindConfig()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var path = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", "balance", "target_table.yaml"));
        if (File.Exists(path)) return path;
        path = Path.GetFullPath(Path.Combine(testDir, "config", "balance", "target_table.yaml"));
        if (File.Exists(path)) return path;
        throw new FileNotFoundException($"target_table.yaml not found. Tried: {path}");
    }
}
