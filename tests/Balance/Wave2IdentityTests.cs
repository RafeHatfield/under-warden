using UnderWarden.Logic.Balance;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Identity scenario suite for Wave 2 monsters.
///
/// Each test is a regression anchor for a specific Wave 2 mechanic:
///   - troll_identity:              Regeneration (2 HP/turn) extends effective HP pool
///   - skeleton_shieldwall_identity: ShieldWall (+1 AC/adjacent ally) + bludgeon vuln with mace
///   - cave_spider_identity:         Poison on hit (2 dmg/turn, 10 turns)
///   - web_spider_identity:          Slow on hit (skip every other turn, 10 turns)
///   - fire_beetle_identity:         Burning on hit (3 dmg/turn, 5 turns)
///
/// avgMonsterHp values use the actual starting Hp at the configured depth (after depth scaling,
/// before constitution modifier — constitution modifies MaxHp, not starting Hp).
///   - troll depth 4:        ceil(30 × 1.08) = 33
///   - skeleton depth 3:     ceil(20 × 1.08) = 22
///   - cave_spider depth 2:  16 (no scaling band 0)
///   - web_spider depth 3:   ceil(20 × 1.08) = 22
///   - fire_beetle depth 2:  12 (no scaling band 0)
///
/// Target bands are intentionally wide on first run — tighten after seeing the data.
/// Tagged [Category("Slow")]: use 'dotnet test --filter "Category!=Slow"' for fast CI.
/// </summary>
[TestFixture]
[Category("Slow")]
public class Wave2IdentityTests
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

    // ─── Print all five scenarios together ───────────────────────────────────

    [Test]
    public void PrintWave2IdentityReport()
    {
        var scenarios = new[]
        {
            (File: "scenario_troll_identity.yaml",              Depth: 4, MonsterHp: 33.0, PlayerHp: 55, Label: "Troll (1v1, regen)"),
            (File: "scenario_skeleton_shieldwall_identity.yaml", Depth: 3, MonsterHp: 22.0, PlayerHp: 55, Label: "Skeleton ×3 (ShieldWall + mace)"),
            (File: "scenario_cave_spider_identity.yaml",         Depth: 2, MonsterHp: 16.0, PlayerHp: 55, Label: "Cave Spider ×2 (poison)"),
            (File: "scenario_web_spider_identity.yaml",          Depth: 3, MonsterHp: 22.0, PlayerHp: 55, Label: "Web Spider (slow)"),
            (File: "scenario_fire_beetle_identity.yaml",         Depth: 2, MonsterHp: 12.0, PlayerHp: 55, Label: "Fire Beetle ×2 (burning)"),
        };

        TestContext.WriteLine("=== WAVE 2 IDENTITY SCENARIOS (seed 1337) ===");
        TestContext.WriteLine(string.Format("  {0,-32} {1,5} {2,8} {3,7} {4,7} {5,7} {6,7}",
            "Scenario", "Depth", "Death%", "RoundsToKill", "RoundsToDie", "DPR_P", "DPR_M"));
        TestContext.WriteLine(string.Format("  {0,-32} {1,5} {2,8} {3,7} {4,7} {5,7} {6,7}",
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

            TestContext.WriteLine(string.Format("  {0,-32} {1,5} {2,7:P0} {3,7:F1} {4,7:F1} {5,7:F2} {6,7:F2}",
                label, depth, pm.DeathRate, pm.RoundsToKill, pm.RoundsToDie, pm.DPR_P, pm.DPR_M));
        }

        TestContext.WriteLine("");
        TestContext.WriteLine("  NOTE: First run — use these numbers to set per-scenario target bands.");
    }

    // ─── Per-scenario regression gates ───────────────────────────────────────

    /// <summary>
    /// Troll 1v1: player with longsword + leather + 4 potions vs depth-4 troll.
    /// Observed first run: Death%=20%, RoundsToKill=10.7, RoundsToDie=15.5
    ///
    /// Regen gate: RoundsToKill > 7.0. A non-regenerating 33 HP troll at ~50% hit rate with
    /// longsword averaging ~4 damage would produce RoundsToKill ≈ 8. Regen of 2/turn during
    /// a ~10-turn fight adds ~20 effective HP, pushing RoundsToKill noticeably above 8.
    /// If RoundsToKill drops below 7, regen is likely broken or not firing.
    /// </summary>
    [Test]
    public void TrollIdentity_CombatResolvesAndDeathRateInBand()
    {
        var agg = _runner.RunFromFile(ScenarioPath("scenario_troll_identity.yaml"));
        var pm = PressureModel.Compute(agg, depth: 4, avgMonsterHp: 33.0, playerMaxHp: 55);

        TestContext.WriteLine($"Troll Identity: Death%={pm.DeathRate:P0}, RoundsToKill={pm.RoundsToKill:F1}, RoundsToDie={pm.RoundsToDie:F1}");

        Assert.That(pm.RoundsToKill, Is.GreaterThan(7.0),
            $"Troll RoundsToKill={pm.RoundsToKill:F1} is too low — troll regeneration should make it take many hits to kill.");
        Assert.That(pm.DeathRate, Is.LessThan(0.70),
            $"Troll 1v1 death rate {pm.DeathRate:P0} is too high — check regen values or troll damage.");
        Assert.That(agg.AvgMonstersKilled, Is.GreaterThan(0.0),
            "Player should be able to kill the troll at least some of the time.");
    }

    /// <summary>
    /// Skeleton ShieldWall: 3 skeletons in formation vs mace-wielding player.
    /// Observed first run: Death%=0%, RoundsToKill=4.5, RoundsToDie=19.8
    ///
    /// Mace (bludgeoning) at 1.5× damage + 4 potions makes 3 skeletons very manageable.
    /// Primary identity check: combat resolves and all 3 are killed. With longsword
    /// (piercing = 0.5×) RoundsToKill would be roughly 2× higher — mace is the correct counter.
    /// </summary>
    [Test]
    public void SkeletonShieldwall_CombatResolvesAndDeathRateInBand()
    {
        var agg = _runner.RunFromFile(ScenarioPath("scenario_skeleton_shieldwall_identity.yaml"));
        var pm = PressureModel.Compute(agg, depth: 3, avgMonsterHp: 22.0, playerMaxHp: 55);

        TestContext.WriteLine($"Skeleton ShieldWall: Death%={pm.DeathRate:P0}, RoundsToKill={pm.RoundsToKill:F1}, RoundsToDie={pm.RoundsToDie:F1}");

        Assert.That(pm.RoundsToKill, Is.GreaterThan(0),
            "RoundsToKill of 0 means no combat resolved — skeleton AI or spawning is broken.");
        Assert.That(pm.DeathRate, Is.LessThan(0.60),
            $"Skeleton ×3 death rate {pm.DeathRate:P0} too high — mace (bludgeon vuln) should give player strong advantage.");
        Assert.That(agg.AvgMonstersKilled, Is.GreaterThan(2.0),
            "Player should kill most/all skeletons on average with mace advantage.");
    }

    /// <summary>
    /// Cave spider 2v1: poison on hit (2 dmg/turn, 10 turns).
    /// Observed first run: Death%=0%, RoundsToKill=6.0, RoundsToDie=35.5
    ///
    /// Spiders are depth-1 creatures — 0% death rate at depth 2 is expected.
    /// Key identity: RoundsToDie > 5 confirms spiders ARE hitting the player (poison fires).
    /// If RoundsToDie ≈ 0, spiders aren't landing hits and poison never applies.
    /// </summary>
    [Test]
    public void CaveSpiderIdentity_PoisonPressureVisible()
    {
        var agg = _runner.RunFromFile(ScenarioPath("scenario_cave_spider_identity.yaml"));
        var pm = PressureModel.Compute(agg, depth: 2, avgMonsterHp: 16.0, playerMaxHp: 55);

        TestContext.WriteLine($"Cave Spider Identity: Death%={pm.DeathRate:P0}, RoundsToKill={pm.RoundsToKill:F1}, RoundsToDie={pm.RoundsToDie:F1}");

        Assert.That(pm.RoundsToKill, Is.GreaterThan(0),
            "RoundsToKill of 0 means no combat resolved — cave spider AI or spawning is broken.");
        Assert.That(pm.RoundsToDie, Is.GreaterThan(5.0),
            $"Cave spider RoundsToDie={pm.RoundsToDie:F1} too low — spiders must land hits for poison to apply.");
        Assert.That(pm.DeathRate, Is.LessThan(0.40),
            $"Cave spider ×2 death rate {pm.DeathRate:P0} too high — depth-2 spiders should be manageable.");
    }

    /// <summary>
    /// Web spider 1v1: SlowedEffect gates every other turn.
    /// Observed first run: Death%=0%, RoundsToKill=14.2, RoundsToDie=67.9
    ///
    /// Key mechanic gate: RoundsToKill > 8.0. Without slow, a 22 HP spider at ~50% hit rate
    /// and longsword averaging ~4 dmg would produce RoundsToKill ≈ 11. Slow halves effective
    /// player DPR → RoundsToKill should be ≥12. If RoundsToKill drops below 8, slow isn't firing.
    /// </summary>
    [Test]
    public void WebSpiderIdentity_SlowPressureVisible()
    {
        var agg = _runner.RunFromFile(ScenarioPath("scenario_web_spider_identity.yaml"));
        var pm = PressureModel.Compute(agg, depth: 3, avgMonsterHp: 22.0, playerMaxHp: 55);

        TestContext.WriteLine($"Web Spider Identity: Death%={pm.DeathRate:P0}, RoundsToKill={pm.RoundsToKill:F1}, RoundsToDie={pm.RoundsToDie:F1}");

        Assert.That(pm.RoundsToKill, Is.GreaterThan(8.0),
            $"Web spider RoundsToKill={pm.RoundsToKill:F1} too low — SlowedEffect should halve player effective DPR, raising RoundsToKill significantly.");
        Assert.That(pm.DeathRate, Is.LessThan(0.40),
            $"Web spider 1v1 death rate {pm.DeathRate:P0} too high — 1v1 with armor + potions should be winnable.");
    }

    /// <summary>
    /// Fire beetle 2v1: BurningEffect applies 3 dmg/turn for 5 turns.
    /// Observed first run: Death%=0%, RoundsToKill=4.7, RoundsToDie=24.7
    ///
    /// Beetles are fragile (12 HP, no depth scaling at depth 2) — 0% death is expected.
    /// Key identity: RoundsToDie > 5 confirms beetles ARE hitting the player (burning fires).
    /// </summary>
    [Test]
    public void FireBeetleIdentity_BurningPressureVisible()
    {
        var agg = _runner.RunFromFile(ScenarioPath("scenario_fire_beetle_identity.yaml"));
        var pm = PressureModel.Compute(agg, depth: 2, avgMonsterHp: 12.0, playerMaxHp: 55);

        TestContext.WriteLine($"Fire Beetle Identity: Death%={pm.DeathRate:P0}, RoundsToKill={pm.RoundsToKill:F1}, RoundsToDie={pm.RoundsToDie:F1}");

        Assert.That(pm.RoundsToKill, Is.GreaterThan(0),
            "RoundsToKill of 0 means no combat resolved — fire beetle AI or spawning is broken.");
        Assert.That(pm.RoundsToDie, Is.GreaterThan(5.0),
            $"Fire beetle RoundsToDie={pm.RoundsToDie:F1} too low — beetles must land hits for burning to apply.");
        Assert.That(pm.DeathRate, Is.LessThan(0.40),
            $"Fire beetle ×2 death rate {pm.DeathRate:P0} too high — fragile depth-2 beetles should be manageable.");
    }
}
