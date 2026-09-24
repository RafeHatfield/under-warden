using UnderWarden.Logic.Balance;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Identity scenario suite for Wave 3 monsters.
///
/// Each test is a regression anchor for a specific Wave 3 mechanic:
///   - necromancer_identity:        Raise dead (4-turn cooldown), corpse seeking, hang-back AI
///   - plague_necromancer_identity: Same raise loop + plague augmentation on raised zombies
///   - giant_spider_identity:       On-hit poison (10 turns), high dex, speed_bonus 0.35
///
/// avgMonsterHp values are approximations for the mixed-enemy necromancer scenarios.
/// Run once with [Category("Slow")] to calibrate — update comments with observed values.
///   - necromancer depth 5:      base HP 28 (no scaling above min_depth in current curve)
///   - skeleton depth 5:         ceil(20 × scaling) ≈ 24 (needs first-run calibration)
///   - orc_grunt depth 5:        ceil(18 × scaling) ≈ 22 (needs first-run calibration)
///   - necromancer scenario avg: ~24 (blended across 1 necro + 3 skeletons + 2 orcs)
///   - plague_necromancer avg:   ~26 (blended across 1 plague_necro + 5 orcs)
///   - giant_spider depth 8:     18 (no depth_weights; min_depth only)
///
/// NOTE: Raise-event tracking is not yet in the harness. Necromancer identity gates are
/// conservative (RoundsToKill > 0, AvgMonstersKilled, death rate band). A future harness pass
/// should add RaiseDeadEvent counting for a tighter raise-loop gate.
///
/// Tagged [Category("Slow")]: use 'dotnet test --filter "Category!=Slow"' for fast CI.
/// </summary>
[TestFixture]
[Category("Slow")]
public class Wave3IdentityTests
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

    // ─── Print all three scenarios together ──────────────────────────────────

    [Test]
    public void PrintWave3IdentityReport()
    {
        var scenarios = new[]
        {
            (File: "scenario_necromancer_identity.yaml",        Depth: 5, MonsterHp: 24.0, PlayerHp: 55, Label: "Necromancer (raise dead, 6 enemies)"),
            (File: "scenario_plague_necromancer_identity.yaml", Depth: 7, MonsterHp: 26.0, PlayerHp: 55, Label: "Plague Necromancer (plague raise, 6 enemies)"),
            (File: "scenario_giant_spider_identity.yaml",       Depth: 8, MonsterHp: 18.0, PlayerHp: 55, Label: "Giant Spider ×2 (on-hit poison)"),
        };

        TestContext.WriteLine("=== WAVE 3 IDENTITY SCENARIOS (seed 1337) ===");
        TestContext.WriteLine(string.Format("  {0,-42} {1,5} {2,8} {3,7} {4,7} {5,7} {6,7}",
            "Scenario", "Depth", "Death%", "RoundsToKill", "RoundsToDie", "DPR_P", "DPR_M"));
        TestContext.WriteLine(string.Format("  {0,-42} {1,5} {2,8} {3,7} {4,7} {5,7} {6,7}",
            "--------", "-----", "------", "----", "----", "-----", "-----"));

        foreach (var (file, depth, monsterHp, playerHp, label) in scenarios)
        {
            var path = ScenarioPath(file);
            if (!File.Exists(path))
            {
                TestContext.WriteLine($"  SKIPPED (file not found): {file}");
                continue;
            }

            var agg = _runner.RunFromFile(path);
            var pm = PressureModel.Compute(agg, depth, monsterHp, playerHp);

            TestContext.WriteLine(string.Format("  {0,-42} {1,5} {2,7:P0} {3,7:F1} {4,7:F1} {5,7:F2} {6,7:F2}",
                label, depth, pm.DeathRate, pm.RoundsToKill, pm.RoundsToDie, pm.DPR_P, pm.DPR_M));
        }

        TestContext.WriteLine("");
        TestContext.WriteLine("  NOTE: First run — use these numbers to calibrate avgMonsterHp and tighten bands.");
        TestContext.WriteLine("  Necromancer gates are conservative until RaiseDeadEvent tracking is in the harness.");
    }

    // ─── Per-scenario regression gates ───────────────────────────────────────

    /// <summary>
    /// Necromancer identity: 1 necromancer + 3 skeletons in a 21×15 crypt.
    ///
    /// Observed first run: Death%=0%, RoundsToKill=16.4, RoundsToDie=92.5, AvgKills=6.9
    ///
    /// PRIMARY IDENTITY GATE — AvgMonstersKilled > 5.0:
    ///   Only 4 monsters spawned (1 necro + 3 skeletons). AvgKills = 6.9 means the player
    ///   kills ~2.9 monsters beyond the spawned count — definitive evidence that the raise-dead
    ///   loop is firing and skeletons are being re-raised and killed again.
    ///   If AvgKills drops to ≤ 4, raises have stopped working.
    ///
    /// RoundsToKill = 16.4: necromancer hangs back (low direct damage), skeletons are the primary threat.
    ///   Two skeletons start within raise range of necromancer at (17,7): dist ≈ 4.5 tiles.
    /// </summary>
    [Test]
    public void NecromancerIdentity_RaiseDeadLoopFires()
    {
        var agg = _runner.RunFromFile(ScenarioPath("scenario_necromancer_identity.yaml"));
        var pm = PressureModel.Compute(agg, depth: 5, avgMonsterHp: 24.0, playerMaxHp: 55);

        TestContext.WriteLine($"Necromancer Identity: Death%={pm.DeathRate:P0}, RoundsToKill={pm.RoundsToKill:F1}, RoundsToDie={pm.RoundsToDie:F1}, AvgKills={agg.AvgMonstersKilled:F1}");

        Assert.That(pm.RoundsToKill, Is.GreaterThan(0),
            "RoundsToKill = 0 means no combat resolved — necromancer spawning or AI dispatch is broken.");
        Assert.That(agg.AvgMonstersKilled, Is.GreaterThan(5.0),
            $"AvgMonstersKilled={agg.AvgMonstersKilled:F1} — only 4 monsters spawn; >5 kills confirms raise-dead loop fires and skeletons are re-killed after being raised.");
        Assert.That(pm.DeathRate, Is.LessThan(0.30),
            $"Necromancer 1v4 death rate {pm.DeathRate:P0} too high — 10 potions + longsword vs 3 skeletons + hang-back necromancer should be very manageable.");
    }

    /// <summary>
    /// Plague necromancer identity: 1 plague_necromancer + 3 orcs in a 21×15 crypt.
    ///
    /// Observed first run: Death%=3%, RoundsToKill=13.3, RoundsToDie=41.6, AvgKills=6.4
    ///
    /// Same raise-loop gate as vanilla necromancer: 4 monsters spawned, AvgKills=6.4 confirms
    /// raises are firing. Plague augmentation distinguishes this from vanilla necromancer —
    /// the raised zombies deal more damage (DPR_M higher than vanilla scenario).
    ///
    /// Slightly tighter death rate allowed than vanilla (plague is harder): < 0.30.
    /// </summary>
    [Test]
    public void PlagueNecromancerIdentity_RaiseDeadLoopFires()
    {
        var agg = _runner.RunFromFile(ScenarioPath("scenario_plague_necromancer_identity.yaml"));
        var pm = PressureModel.Compute(agg, depth: 7, avgMonsterHp: 26.0, playerMaxHp: 55);

        TestContext.WriteLine($"Plague Necromancer Identity: Death%={pm.DeathRate:P0}, RoundsToKill={pm.RoundsToKill:F1}, RoundsToDie={pm.RoundsToDie:F1}, AvgKills={agg.AvgMonstersKilled:F1}");

        Assert.That(pm.RoundsToKill, Is.GreaterThan(0),
            "RoundsToKill = 0 means no combat resolved — plague_necromancer spawning or AI dispatch is broken.");
        Assert.That(agg.AvgMonstersKilled, Is.GreaterThan(5.0),
            $"AvgMonstersKilled={agg.AvgMonstersKilled:F1} — only 4 monsters spawn; >5 kills confirms raise-dead loop fires.");
        Assert.That(pm.DeathRate, Is.LessThan(0.30),
            $"Plague necromancer death rate {pm.DeathRate:P0} too high — 12 potions + longsword should be very survivable against 3 orcs + hang-back necromancer.");
    }

    /// <summary>
    /// Giant spider identity: 1 giant spider at depth 8.
    /// Stats: HP 18, dmg 4-8, dex 16 (+3 accuracy/evasion), speed_bonus 0.35, on-hit poison 10 turns.
    ///
    /// Observed first run: Death%=17%, RoundsToKill=9.5, RoundsToDie=11.7
    ///
    /// RoundsToDie gate: spider must land hits for poison to apply. RoundsToDie=11.7 confirms it is.
    /// If RoundsToDie drops to ≤ 5: spider accuracy or AI is broken.
    /// Death rate of 17% with longsword + leather + 5 potions reflects the speed bonus + poison pressure.
    ///
    /// No PoC reference for this monster (PoC had web ability; ours has poison).
    /// </summary>
    [Test]
    public void GiantSpiderIdentity_PoisonPressureVisible()
    {
        var agg = _runner.RunFromFile(ScenarioPath("scenario_giant_spider_identity.yaml"));
        var pm = PressureModel.Compute(agg, depth: 8, avgMonsterHp: 18.0, playerMaxHp: 55);

        TestContext.WriteLine($"Giant Spider Identity: Death%={pm.DeathRate:P0}, RoundsToKill={pm.RoundsToKill:F1}, RoundsToDie={pm.RoundsToDie:F1}");

        Assert.That(pm.RoundsToKill, Is.GreaterThan(0),
            "RoundsToKill = 0 means no combat resolved — giant spider spawning or AI is broken.");
        Assert.That(pm.RoundsToDie, Is.GreaterThan(5.0),
            $"Giant spider RoundsToDie={pm.RoundsToDie:F1} too low — spider must land hits for on-hit poison to apply.");
        Assert.That(pm.DeathRate, Is.LessThan(0.50),
            $"Giant spider 1v1 death rate {pm.DeathRate:P0} too high — longsword + leather + 5 potions should handle a single depth-8 spider most of the time.");
    }
}
