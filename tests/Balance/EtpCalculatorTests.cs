using UnderWarden.Logic.Balance.Etp;
using UnderWarden.Logic.Content;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Tests for the full ETP system.
/// Ported from ~/development/rlike/tests/test_etp_system.py.
/// Covers EtpConfig loading, band lookup, ETP calculation, and budget checking.
/// </summary>
[TestFixture]
public class EtpCalculatorTests
{
    private EtpConfig _cfg = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        // Resolve config path relative to the test binary location.
        // Tests run from tests/bin/Debug/net8.0/ — config/ is 4 levels up at project root.
        string testDir  = AppContext.BaseDirectory;
        string cfgPath  = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", "etp_config.yaml"));
        if (!File.Exists(cfgPath))
            cfgPath = Path.GetFullPath(Path.Combine(testDir, "config", "etp_config.yaml"));
        if (!File.Exists(cfgPath))
            throw new FileNotFoundException($"etp_config.yaml not found. Tried: {cfgPath}");

        _cfg = EtpConfigLoader.FromFile(cfgPath);
    }

    // ── EtpConfigLoader round-trip ─────────────────────────────────────────

    [Test]
    public void EtpConfigLoader_LoadsAllFiveBands()
    {
        Assert.That(_cfg.Bands.ContainsKey("B1"), Is.True);
        Assert.That(_cfg.Bands.ContainsKey("B2"), Is.True);
        Assert.That(_cfg.Bands.ContainsKey("B3"), Is.True);
        Assert.That(_cfg.Bands.ContainsKey("B4"), Is.True);
        Assert.That(_cfg.Bands.ContainsKey("B5"), Is.True);
    }

    [Test]
    public void EtpConfigLoader_LoadsBehaviorModifiers()
    {
        Assert.That(_cfg.BehaviorModifiers.ContainsKey("basic_melee"), Is.True);
        Assert.That(_cfg.BehaviorModifiers.ContainsKey("boss"), Is.True);
        Assert.That(_cfg.BehaviorModifiers.Count, Is.GreaterThanOrEqualTo(9));
    }

    [Test]
    public void BandConfig_AllBandsHavePositiveMultipliers()
    {
        foreach (var (name, band) in _cfg.Bands)
        {
            Assert.That(band.HpMultiplier,     Is.GreaterThan(0), $"{name}: hp_mult must be > 0");
            Assert.That(band.DamageMultiplier, Is.GreaterThan(0), $"{name}: dmg_mult must be > 0");
            Assert.That(band.RoomEtp.Max,      Is.GreaterThan(0), $"{name}: room_etp_max must be > 0");
            Assert.That(band.FloorEtp.Max,     Is.GreaterThan(0), $"{name}: floor_etp_max must be > 0");
        }
    }

    // ── BandForDepth ─────────────────────────────────────────────────────────

    [Test]
    public void BandForDepth_B1_Depths1To5()
    {
        foreach (int d in new[] { 1, 3, 5 })
            Assert.That(EtpCalculator.BandForDepth(_cfg, d), Is.EqualTo("B1"),
                $"depth {d} should map to B1");
    }

    [Test]
    public void BandForDepth_B2_Depths6To10()
    {
        foreach (int d in new[] { 6, 8, 10 })
            Assert.That(EtpCalculator.BandForDepth(_cfg, d), Is.EqualTo("B2"),
                $"depth {d} should map to B2");
    }

    [Test]
    public void BandForDepth_B3_Depths11To15()
    {
        foreach (int d in new[] { 11, 13, 15 })
            Assert.That(EtpCalculator.BandForDepth(_cfg, d), Is.EqualTo("B3"),
                $"depth {d} should map to B3");
    }

    [Test]
    public void BandForDepth_B4_Depths16To20()
    {
        foreach (int d in new[] { 16, 18, 20 })
            Assert.That(EtpCalculator.BandForDepth(_cfg, d), Is.EqualTo("B4"),
                $"depth {d} should map to B4");
    }

    [Test]
    public void BandForDepth_B5_Depths21To25()
    {
        foreach (int d in new[] { 21, 23, 25 })
            Assert.That(EtpCalculator.BandForDepth(_cfg, d), Is.EqualTo("B5"),
                $"depth {d} should map to B5");
    }

    [Test]
    public void BandForDepth_BeyondMax_ReturnsHighestBand()
    {
        // Depths beyond 25 → B5 (highest)
        foreach (int d in new[] { 26, 30, 50 })
            Assert.That(EtpCalculator.BandForDepth(_cfg, d), Is.EqualTo("B5"),
                $"depth {d} beyond max should return B5");
    }

    // ── DPS and Durability ────────────────────────────────────────────────────

    [Test]
    public void DpsCalc_NoPower_CorrectAverage()
    {
        // (4 + 6) / 2 + 0 = 5.0
        Assert.That(EtpCalculator.CalculateDps(4, 6, 0), Is.EqualTo(5.0).Within(0.001));
    }

    [Test]
    public void DpsCalc_WithPower_AddsToAverage()
    {
        // (4 + 6) / 2 + 2 = 7.0
        Assert.That(EtpCalculator.CalculateDps(4, 6, 2), Is.EqualTo(7.0).Within(0.001));
    }

    [Test]
    public void Durability_HpScaling()
    {
        // DurabilityFactor(hp, baseline=6.5): hp / (6.5 × 3)
        // hp=20 → 20 / 19.5 ≈ 1.026
        // hp=40 → 40 / 19.5 ≈ 2.051 ≈ 2×
        double d20 = EtpCalculator.DurabilityFactor(20);
        double d40 = EtpCalculator.DurabilityFactor(40);
        Assert.That(d20, Is.EqualTo(20.0 / 19.5).Within(0.01));
        Assert.That(d40, Is.EqualTo(d20 * 2.0).Within(0.05), "HP doubles → durability doubles");
    }

    // ── Behavior modifier ─────────────────────────────────────────────────────

    [Test]
    public void BehaviorModifier_BasicMelee_Is090()
    {
        Assert.That(EtpCalculator.GetBehaviorModifier(_cfg, "basic"), Is.EqualTo(0.90).Within(0.001));
    }

    [Test]
    public void BehaviorModifier_Boss_Is130()
    {
        Assert.That(EtpCalculator.GetBehaviorModifier(_cfg, "boss"), Is.EqualTo(1.30).Within(0.001));
    }

    [Test]
    public void BehaviorModifier_Unknown_Returns100()
    {
        Assert.That(EtpCalculator.GetBehaviorModifier(_cfg, "unknown_ai_type"), Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void BehaviorModifier_AliasResolution_Slime_IsControl()
    {
        // "slime" ai_type → "control" behavior role → 1.10
        Assert.That(EtpCalculator.GetBehaviorModifier(_cfg, "slime"), Is.EqualTo(1.10).Within(0.001));
    }

    // ── Speed multiplier ──────────────────────────────────────────────────────

    [Test]
    public void SpeedMultiplier_AtOrAbove2_Returns200()
    {
        Assert.That(EtpCalculator.GetSpeedMultiplier(2.0), Is.EqualTo(2.0).Within(0.001));
        Assert.That(EtpCalculator.GetSpeedMultiplier(3.0), Is.EqualTo(2.0).Within(0.001));
    }

    [Test]
    public void SpeedMultiplier_AtOrAbove15_Returns150()
    {
        Assert.That(EtpCalculator.GetSpeedMultiplier(1.5), Is.EqualTo(1.5).Within(0.001));
        Assert.That(EtpCalculator.GetSpeedMultiplier(1.8), Is.EqualTo(1.5).Within(0.001));
    }

    [Test]
    public void SpeedMultiplier_AtOrAbove11_Returns125()
    {
        Assert.That(EtpCalculator.GetSpeedMultiplier(1.1), Is.EqualTo(1.25).Within(0.001));
        Assert.That(EtpCalculator.GetSpeedMultiplier(1.3), Is.EqualTo(1.25).Within(0.001));
    }

    [Test]
    public void SpeedMultiplier_Below11_Returns100()
    {
        Assert.That(EtpCalculator.GetSpeedMultiplier(1.0), Is.EqualTo(1.0).Within(0.001));
        Assert.That(EtpCalculator.GetSpeedMultiplier(0.5), Is.EqualTo(1.0).Within(0.001));
    }

    // ── Monster ETP calculation ────────────────────────────────────────────────

    [Test]
    public void MonsterEtp_OrcAtDepth1_InExpectedRange()
    {
        // orc_grunt has etp_base=27, depth 1 = B1 (no multiplier)
        // band_mult = (1.0 + 1.0)/2 = 1.0, synergy=1.0, elite=1.0, speed=1.0
        // etp = 27 * 1.0 = 27.0
        var orc = MakeMonster("orc_grunt", etpBase: 27, speedBonus: 0.0);
        double etp = EtpCalculator.GetMonsterEtp(_cfg, orc, depth: 1);
        Assert.That(etp, Is.GreaterThan(20).And.LessThan(40),
            "Orc at depth 1 should be 20-40 ETP");
    }

    [Test]
    public void MonsterEtp_ScalesWithDepth()
    {
        var orc = MakeMonster("orc_grunt", etpBase: 27, speedBonus: 0.0);
        double etpB1 = EtpCalculator.GetMonsterEtp(_cfg, orc, depth: 1);
        double etpB3 = EtpCalculator.GetMonsterEtp(_cfg, orc, depth: 11);
        double etpB5 = EtpCalculator.GetMonsterEtp(_cfg, orc, depth: 21);
        Assert.That(etpB3, Is.GreaterThan(etpB1), "B3 ETP > B1 ETP");
        Assert.That(etpB5, Is.GreaterThan(etpB3), "B5 ETP > B3 ETP");
    }

    [Test]
    public void MonsterEtp_EliteMultiplierApplied()
    {
        var orc = MakeMonster("orc_grunt", etpBase: 27, speedBonus: 0.0);
        double normal = EtpCalculator.GetMonsterEtp(_cfg, orc, depth: 1, isElite: false);
        double elite  = EtpCalculator.GetMonsterEtp(_cfg, orc, depth: 1, isElite: true);
        Assert.That(elite, Is.EqualTo(normal * 1.5).Within(0.01),
            "Elite multiplier should be exactly 1.5×");
    }

    [Test]
    public void MonsterEtp_HigherEtpBaseGivesHigherEtp()
    {
        // goblin_troll etp_base=45 > orc_grunt etp_base=27
        var orc  = MakeMonster("orc_grunt",    etpBase: 27, speedBonus: 0.0);
        var troll = MakeMonster("goblin_troll", etpBase: 45, speedBonus: 0.0);
        double orcEtp   = EtpCalculator.GetMonsterEtp(_cfg, orc,   depth: 1);
        double trollEtp = EtpCalculator.GetMonsterEtp(_cfg, troll, depth: 1);
        Assert.That(trollEtp, Is.GreaterThan(orcEtp),
            "Monster with higher etp_base should have higher ETP");
    }

    // ── Budget queries ────────────────────────────────────────────────────────

    [Test]
    public void RoomBudget_IncreasesWithBand()
    {
        var (_, maxB1) = EtpCalculator.GetRoomEtpBudget(_cfg, 1);
        var (_, maxB3) = EtpCalculator.GetRoomEtpBudget(_cfg, 11);
        var (_, maxB5) = EtpCalculator.GetRoomEtpBudget(_cfg, 21);
        Assert.That(maxB3, Is.GreaterThan(maxB1), "B3 room budget > B1");
        Assert.That(maxB5, Is.GreaterThan(maxB3), "B5 room budget > B3");
    }

    [Test]
    public void FloorBudget_IncreasesWithBand()
    {
        var (_, maxB1) = EtpCalculator.GetFloorEtpBudget(_cfg, 1);
        var (_, maxB3) = EtpCalculator.GetFloorEtpBudget(_cfg, 11);
        var (_, maxB5) = EtpCalculator.GetFloorEtpBudget(_cfg, 21);
        Assert.That(maxB3, Is.GreaterThan(maxB1), "B3 floor budget > B1");
        Assert.That(maxB5, Is.GreaterThan(maxB3), "B5 floor budget > B3");
    }

    [Test]
    public void Spike_AllowsHigherMax()
    {
        var (_, normalMax) = EtpCalculator.GetRoomEtpBudget(_cfg, 1, allowSpike: false);
        var (_, spikeMax)  = EtpCalculator.GetRoomEtpBudget(_cfg, 1, allowSpike: true);
        Assert.That(spikeMax, Is.EqualTo(normalMax * _cfg.SpikeSettings.SpikeMultiplier).Within(0.01),
            "Spike max = normal max × spike_multiplier (1.5)");
    }

    // ── EtpBudgetChecker ──────────────────────────────────────────────────────

    [Test]
    public void CheckRoom_Under_WhenEtpBelowMinTolerance()
    {
        // B1 room_etp.min = 0 (empty rooms are OK), but B2 has min=20
        // At depth 6 (B2): min=20, tolerance=10% → effective_min=18
        // ETP = 5 → UNDER
        var result = EtpBudgetChecker.CheckRoom(_cfg, totalEtp: 5.0, depth: 6);
        Assert.That(result.Status, Is.EqualTo("UNDER"));
        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void CheckRoom_Over_WhenEtpAboveMaxTolerance()
    {
        // B1 max=50, tolerance=10% → effective_max=55
        // ETP = 100 → OVER
        var result = EtpBudgetChecker.CheckRoom(_cfg, totalEtp: 100.0, depth: 1);
        Assert.That(result.Status, Is.EqualTo("OVER"));
        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void CheckRoom_OK_WhenWithinTolerance()
    {
        // B1 max=50, tolerance=10% → effective_max=55
        // ETP = 45 → OK
        var result = EtpBudgetChecker.CheckRoom(_cfg, totalEtp: 45.0, depth: 1);
        Assert.That(result.Status, Is.EqualTo("OK"));
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void CheckRoom_Empty_AlwaysOK()
    {
        // Zero ETP with zero min (B1) → OK (empty room)
        var result = EtpBudgetChecker.CheckRoom(_cfg, totalEtp: 0.0, depth: 1);
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void CheckRoom_SpikeAllowance_PermitsHigherEtp()
    {
        // B1 max=50, spike_multiplier=1.5 → spike_max=75, then tolerance adds 10% → 82.5
        // ETP = 70 → OK with allowSpike=true
        var normalResult = EtpBudgetChecker.CheckRoom(_cfg, totalEtp: 70.0, depth: 1, allowSpike: false);
        var spikeResult  = EtpBudgetChecker.CheckRoom(_cfg, totalEtp: 70.0, depth: 1, allowSpike: true);
        Assert.That(normalResult.Status, Is.EqualTo("OVER"), "Without spike: OVER");
        Assert.That(spikeResult.Status,  Is.EqualTo("OK"),   "With spike: OK");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MonsterDefinition MakeMonster(
        string id, int etpBase = 27, double speedBonus = 0.0,
        int hp = 28, int dmgMin = 4, int dmgMax = 6, string aiType = "basic")
    {
        return new MonsterDefinition
        {
            Name     = id,
            AiType   = aiType,
            EtpBase  = etpBase,
            SpeedBonus = speedBonus,
            Stats = new MonsterStats
            {
                Hp        = hp,
                DamageMin = dmgMin,
                DamageMax = dmgMax,
            },
        };
    }
}
