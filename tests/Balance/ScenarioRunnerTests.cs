using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Integration tests that load real YAML files from config/.
/// Validates the full file-based pipeline: entities.yaml + scenario YAML -> metrics.
/// </summary>
[TestFixture]
public class ScenarioRunnerTests
{
    private static string ConfigPath(string filename) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "config", filename);

    private static string ScenarioPath(string filename) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "config", "levels", filename);

    private ScenarioRunner _runner = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var entitiesPath = ConfigPath("entities.yaml");
        Assert.That(File.Exists(entitiesPath), Is.True,
            $"entities.yaml not found at {entitiesPath}");

        _runner = ScenarioRunner.FromEntitiesFile(entitiesPath);
    }

    [Test]
    public void LoadAndRun_Depth2OrcBaseline()
    {
        var path = ScenarioPath("scenario_depth2_orc_baseline.yaml");
        Assert.That(File.Exists(path), Is.True, $"Scenario file not found: {path}");

        var agg = _runner.RunFromFile(path);

        Assert.That(agg.ScenarioId, Is.EqualTo("depth2_orc_baseline"));
        Assert.That(agg.TotalRuns, Is.EqualTo(50));
        Assert.That(agg.PlayerHitRate, Is.InRange(0.5, 0.9));
        Assert.That(agg.MonsterHitRate, Is.InRange(0.2, 0.6));
    }

    [Test]
    public void LoadAndRun_Depth2Orc1v1()
    {
        var path = ScenarioPath("scenario_depth2_orc_1v1.yaml");
        var agg = _runner.RunFromFile(path);

        Assert.That(agg.ScenarioId, Is.EqualTo("depth2_orc_1v1"));
        // 1v1 equipped player should almost always win
        Assert.That(agg.DeathRate, Is.LessThan(0.15));
    }

    [Test]
    public void LoadAndRun_Depth5OrcPressure()
    {
        var path = ScenarioPath("scenario_depth5_orc_pressure.yaml");
        var agg = _runner.RunFromFile(path);

        Assert.That(agg.ScenarioId, Is.EqualTo("depth5_orc_pressure"));
        Assert.That(agg.TotalRuns, Is.GreaterThan(0));
        // Death rate is balance-tuning sensitive — validated via harness bands, not fixed assertions
    }

    [Test]
    public void LoadAndRun_Depth5Zombie()
    {
        var path = ScenarioPath("scenario_depth5_zombie.yaml");
        var agg = _runner.RunFromFile(path);

        Assert.That(agg.ScenarioId, Is.EqualTo("depth5_zombie"));
        // 5 zombies at depth 5 — bumped to 100 runs for reliable death rate signal (zombie variance is high)
        Assert.That(agg.TotalRuns, Is.EqualTo(100));
    }

    [Test]
    public void Deterministic_AcrossLoads()
    {
        var path = ScenarioPath("scenario_depth2_orc_baseline.yaml");

        var agg1 = _runner.RunFromFile(path, baseSeed: 1337);

        // Create a completely new runner from scratch
        var runner2 = ScenarioRunner.FromEntitiesFile(ConfigPath("entities.yaml"));
        var agg2 = runner2.RunFromFile(path, baseSeed: 1337);

        Assert.That(agg1.DeathRate, Is.EqualTo(agg2.DeathRate));
        Assert.That(agg1.AvgTurns, Is.EqualTo(agg2.AvgTurns));
        Assert.That(agg1.PlayerHitRate, Is.EqualTo(agg2.PlayerHitRate));
    }

    [Test]
    public void PrintAllScenarios()
    {
        TestContext.WriteLine("=== All Scenarios from YAML Files (seed 1337) ===");
        TestContext.WriteLine("");
        TestContext.WriteLine(string.Format("  {0,-30} {1,8} {2,10} {3,10} {4,10}",
            "Scenario", "Death%", "AvgTurns", "AvgKills", "HitRate"));

        var scenarioDir = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", "levels");

        foreach (var file in Directory.GetFiles(scenarioDir, "scenario_*.yaml").OrderBy(f => f))
        {
            var agg = _runner.RunFromFile(file);
            TestContext.WriteLine(string.Format("  {0,-30} {1,7:P0} {2,10:F1} {3,10:F1} {4,9:P0}",
                agg.ScenarioId, agg.DeathRate, agg.AvgTurns, agg.AvgMonstersKilled, agg.PlayerHitRate));
        }

        Assert.Pass();
    }
}
