using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Full depth 1-6 validation suite. Runs all baseline scenarios, computes
/// pressure metrics, evaluates against target bands, and prints a diagnostic
/// report. This is the "are we done?" test for Harness Parity.
///
/// Tagged [Category("Slow")] since it runs many scenarios.
/// </summary>
[TestFixture]
[Category("Slow")]
public class TargetBandValidationTests
{
    private ScenarioRunner _runner = null!;

    private static string ConfigPath(string f) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "config", f);

    private static string ScenarioPath(string f) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "config", "levels", f);

    [OneTimeSetUp]
    public void Setup()
    {
        _runner = ScenarioRunner.FromEntitiesFile(ConfigPath("entities.yaml"));
    }

    // Scenario configs: (file, depth, avgMonsterHp, playerMaxHp)
    private static readonly (string File, int Depth, double MonsterHp, int PlayerHp, string Feel)[] Scenarios =
    [
        ("scenario_depth1_orc_easy.yaml", 1, 29, 55, "Safe learning"),
        ("scenario_depth2_orc_baseline.yaml", 2, 29, 55, "Warm-up"),
        ("scenario_depth3_orc_brutal.yaml", 3, 31, 55, "Pressure begins"),
        ("scenario_depth4_mixed.yaml", 4, 28, 55, "Serious"),
        ("scenario_depth5_orc_pressure.yaml", 5, 36, 55, "Dangerous"),
        ("scenario_depth5_zombie.yaml", 5, 25, 55, "Dangerous (zombie)"),
        ("scenario_depth6_orc_siege.yaml", 6, 40, 55, "Brutal (fine LS)"),  // 4g(36)+1brute(54) avg≈40
    ];

    [Test]
    public void PrintFullValidationReport()
    {
        TestContext.WriteLine("=== HARNESS PARITY VALIDATION REPORT ===");
        TestContext.WriteLine($"    Seed: 1337 | Date: {DateTime.Now:yyyy-MM-dd}");
        TestContext.WriteLine("");

        // Header
        TestContext.WriteLine(string.Format("  {0,-28} {1,5} {2,8} {3,7} {4,7} {5,8} {6,8} {7,8}",
            "Scenario", "Depth", "Death%", "RoundsToKill", "RoundsToDie", "D%?", "HPM?", "HMP?"));
        TestContext.WriteLine(string.Format("  {0,-28} {1,5} {2,8} {3,7} {4,7} {5,8} {6,8} {7,8}",
            "--------", "-----", "------", "----", "----", "---", "----", "----"));

        int inBand = 0;
        int total = 0;
        var allFindings = new List<string>();

        foreach (var (file, depth, monsterHp, playerHp, feel) in Scenarios)
        {
            var path = ScenarioPath(file);
            if (!File.Exists(path)) continue;

            var agg = _runner.RunFromFile(path);
            var pm = PressureModel.Compute(agg, depth, monsterHp, playerHp);
            var eval = PressureModel.Evaluate(pm);

            TestContext.WriteLine(string.Format("  {0,-28} {1,5} {2,7:P0} {3,7:F1} {4,7:F1} {5,8} {6,8} {7,8}",
                agg.ScenarioId, depth, pm.DeathRate, pm.RoundsToKill, pm.RoundsToDie,
                eval.DeathRate_Status, eval.RoundsToKill_Status, eval.RoundsToDie_Status));

            if (eval.AllInBand) inBand++;
            total++;

            if (!eval.AllInBand)
            {
                var findings = PressureModel.Diagnose(eval);
                foreach (var f in findings)
                    allFindings.Add($"  [{agg.ScenarioId}] {f}");
            }
        }

        TestContext.WriteLine("");
        TestContext.WriteLine($"  === {inBand}/{total} scenarios fully in-band ===");

        if (allFindings.Count > 0)
        {
            TestContext.WriteLine("");
            TestContext.WriteLine("  FINDINGS:");
            foreach (var f in allFindings)
                TestContext.WriteLine(f);
        }

        Assert.Pass();
    }

    [Test]
    public void EvaluateAndDiagnose_ProduceActionableOutput()
    {
        // Synthetic test: verify diagnosis produces correct output for out-of-band metrics.
        // RoundsToKill=12 >> band [6-10] → HIGH (player kills too slowly)
        // RoundsToDie=40  > band [20-24] → HIGH (monsters not threatening enough)
        // DeathRate=44% >> band [0-8%] → HIGH
        var pm = new PressureMetrics
        {
            ScenarioId = "test", Depth = 2,
            RoundsToKill = 12.0, RoundsToDie = 40.0, DPR_P = 2.4, DPR_M = 2.4,
            DeathRate = 0.44,
        };

        var eval = PressureModel.Evaluate(pm);

        Assert.That(eval.RoundsToKill_Status, Is.EqualTo("HIGH"));
        Assert.That(eval.RoundsToDie_Status, Is.EqualTo("HIGH"));
        Assert.That(eval.DeathRate_Status, Is.EqualTo("HIGH"));
        Assert.That(eval.AllInBand, Is.False);

        var findings = PressureModel.Diagnose(eval);
        Assert.That(findings.Count, Is.GreaterThan(0));
        Assert.That(findings.Any(f => f.Contains("kills too slowly")), Is.True);
        Assert.That(findings.Any(f => f.Contains("not threatening enough")), Is.True);
    }
}
