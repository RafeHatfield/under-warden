using UnderWarden.Logic.Balance;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Measures tuned scenarios to establish provisional C# target bands.
/// These scenarios use realistic player loadouts (weapon progression + speed)
/// instead of the dagger-only original baselines.
/// </summary>
[TestFixture]
[Category("Slow")]
public class TunedBaselineTests
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

    private static readonly (string File, int Depth, double MonsterHp, string Weapon)[] TunedScenarios =
    [
        ("scenario_depth1_tuned.yaml", 1, 29, "Dagger"),
        ("scenario_depth2_tuned.yaml", 2, 29, "Shortsword+Speed"),
        ("scenario_depth3_tuned.yaml", 3, 31, "Longsword+Speed"),
        ("scenario_depth4_tuned.yaml", 4, 28, "Longsword+Speed"),
        ("scenario_depth5_tuned.yaml", 5, 42, "Longsword+Brute"),  // 2g(36)+1brute(54) avg=42
        ("scenario_depth6_tuned.yaml", 6, 40, "MW LS+Brute"),       // 4g(36)+1brute(54) avg=39.6→40
    ];

    [Test]
    public void PrintTunedMeasurements()
    {
        TestContext.WriteLine("=== TUNED BASELINE MEASUREMENTS (seed 1337) ===");
        TestContext.WriteLine("    These measurements define the provisional C# target bands.");
        TestContext.WriteLine("");
        TestContext.WriteLine(string.Format("  {0,-22} {1,5} {2,8} {3,7} {4,7} {5,7} {6,7} {7,6}",
            "Scenario", "Depth", "Death%", "RoundsToKill", "RoundsToDie", "DPR_P", "DPR_M", "Kills"));

        foreach (var (file, depth, monsterHp, weapon) in TunedScenarios)
        {
            var path = ScenarioPath(file);
            if (!File.Exists(path)) continue;

            var agg = _runner.RunFromFile(path);
            var pm = PressureModel.Compute(agg, depth, monsterHp, 55);

            TestContext.WriteLine(string.Format("  {0,-22} {1,5} {2,7:P0} {3,7:F1} {4,7:F1} {5,7:F2} {6,7:F2} {7,6:F1}",
                agg.ScenarioId, depth, pm.DeathRate, pm.RoundsToKill, pm.RoundsToDie,
                pm.DPR_P, pm.DPR_M, agg.AvgMonstersKilled));
        }

        // Also print prototype target bands for comparison
        TestContext.WriteLine("");
        TestContext.WriteLine("  --- Prototype Target Bands (for convergence reference) ---");
        TestContext.WriteLine(string.Format("  {0,-12} {1,10} {2,10} {3,12}",
            "Depth Band", "RoundsToKill", "RoundsToDie", "Death%"));
        for (int band = 0; band < 5; band++)
        {
            int d = band * 2 + 1;
            var hpm = PressureModel.GetRoundsToKillTarget(d);
            var hmp = PressureModel.GetRoundsToDieTarget(d);
            var dr = PressureModel.GetDeathRateTarget(d);
            string label = band switch
            {
                0 => "1-2",
                1 => "3-4",
                2 => "5-6",
                3 => "7-8",
                4 => "9+",
                _ => "?"
            };
            TestContext.WriteLine(string.Format("  {0,-12} {1,4:F1}-{2,-5:F1} {3,4:F0}-{4,-5:F0} {5,5:P0}-{6,-5:P0}",
                label, hpm.Min, hpm.Max, hmp.Min, hmp.Max, dr.Min, dr.Max));
        }

        Assert.Pass();
    }

    [Test]
    public void PrintTunedValidation()
    {
        TestContext.WriteLine("=== TUNED VALIDATION (Provisional C# Bands) ===");
        TestContext.WriteLine("");
        TestContext.WriteLine(string.Format("  {0,-22} {1,5} {2,8} {3,7} {4,7} {5,6} {6,6} {7,6}",
            "Scenario", "Depth", "Death%", "RoundsToKill", "RoundsToDie", "D%?", "HPM?", "HMP?"));

        int inBand = 0;
        int total = 0;

        foreach (var (file, depth, monsterHp, weapon) in TunedScenarios)
        {
            var path = ScenarioPath(file);
            if (!File.Exists(path)) continue;

            var agg = _runner.RunFromFile(path);
            var pm = PressureModel.Compute(agg, depth, monsterHp, 55);
            var eval = PressureModel.EvaluateProvisional(pm);

            TestContext.WriteLine(string.Format("  {0,-22} {1,5} {2,7:P0} {3,7:F1} {4,7:F1} {5,6} {6,6} {7,6}",
                agg.ScenarioId, depth, pm.DeathRate, pm.RoundsToKill, pm.RoundsToDie,
                eval.DeathRate_Status, eval.RoundsToKill_Status, eval.RoundsToDie_Status));

            if (eval.AllInBand) inBand++;
            total++;
        }

        TestContext.WriteLine("");
        TestContext.WriteLine($"  === {inBand}/{total} scenarios in provisional bands ===");

        Assert.Pass();
    }

    [Test]
    public void PrintConvergenceGap()
    {
        TestContext.WriteLine("=== CONVERGENCE GAP: Provisional vs Prototype Bands ===");
        TestContext.WriteLine("");
        TestContext.WriteLine(string.Format("  {0,-12} {1,14} {2,14} {3,14} {4,14}",
            "Depth Band", "Prov RoundsToKill", "Proto RoundsToKill", "Prov RoundsToDie", "Proto RoundsToDie"));

        for (int band = 0; band < 5; band++)
        {
            int d = band * 2 + 1;
            var pHpm = PressureModel.GetProvisionalRoundsToKill(d);
            var tHpm = PressureModel.GetRoundsToKillTarget(d);
            var pHmp = PressureModel.GetProvisionalRoundsToDie(d);
            var tHmp = PressureModel.GetRoundsToDieTarget(d);
            string label = band switch
            {
                0 => "1-2", 1 => "3-4", 2 => "5-6", 3 => "7-8", 4 => "9+", _ => "?"
            };
            TestContext.WriteLine(string.Format("  {0,-12} {1,5:F1}-{2,-7:F1} {3,5:F1}-{4,-7:F1} {5,5:F0}-{6,-7:F0} {7,5:F0}-{8,-5:F0}",
                label, pHpm.Min, pHpm.Max, tHpm.Min, tHpm.Max,
                pHmp.Min, pHmp.Max, tHmp.Min, tHmp.Max));
        }

        TestContext.WriteLine("");
        TestContext.WriteLine("  RoundsToKill gap: provisional ~2x wider and ~2x higher than prototype");
        TestContext.WriteLine("  RoundsToDie gap: provisional ~3x wider and ~2x higher than prototype");
        TestContext.WriteLine("  Levers to close: bonus attacks, monster abilities, tighter arenas");

        Assert.Pass();
    }
}
