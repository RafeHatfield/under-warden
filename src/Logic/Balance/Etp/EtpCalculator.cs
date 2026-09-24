using UnderWarden.Logic.Content;

namespace UnderWarden.Logic.Balance.Etp;

/// <summary>
/// Full ETP (Effective Threat Points) calculator.
/// Replaces the stub EtpCalculator in src/Logic/Balance/EtpCalculator.cs.
///
/// All methods are pure functions over EtpConfig — no mutable state.
/// Ported from ~/development/rlike/balance/etp.py:439-553 (get_monster_etp),
/// etp.py:177-234 (band lookup), etp.py:237-310 (DPS + durability + speed).
///
/// Design note: C# DepthScaling bands (2-floor width, (depth-1)/2) are DIFFERENT
/// from EtpConfig bands (5-floor width, B1=1-5 etc.). These are separate concepts:
/// DepthScaling bands stat multipliers; EtpConfig bands encounter budgets.
/// See DepthScaling.cs comment for details.
/// </summary>
public static class EtpCalculator
{
    /// <summary>Baseline player average damage (1d8+2 STR ≈ 4.5+2 = 6.5). Matches PoC etp.py:267.</summary>
    public const double BaselinePlayerDamage = 6.5;

    /// <summary>Elite monster ETP multiplier. PoC etp.py:276.</summary>
    public const double EliteMultiplier = 1.5;

    /// <summary>
    /// Default ETP returned when a monster has no etp_base and stats can't be derived.
    /// PoC etp.py:476-479 returns 20.0 as fallback.
    /// </summary>
    public const double DefaultEtp = 20.0;

    // ── Band lookup ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the band name ("B1".."B5") for a depth.
    /// Depths beyond the last band's max return the highest band.
    /// PoC: band_for_depth() in etp.py:177-208.
    /// </summary>
    public static string BandForDepth(EtpConfig cfg, int depth)
    {
        foreach (var (name, band) in cfg.Bands.OrderBy(b => b.Value.FloorMin))
        {
            if (depth >= band.FloorMin && depth <= band.FloorMax)
                return name;
        }
        // Beyond all defined bands — return the last (highest) band
        return cfg.Bands
            .OrderByDescending(b => b.Value.FloorMax)
            .First().Key;
    }

    /// <summary>
    /// Returns the BandConfig for a depth.
    /// </summary>
    public static BandConfig BandConfigForDepth(EtpConfig cfg, int depth)
    {
        var name = BandForDepth(cfg, depth);
        return cfg.Bands[name];
    }

    // ── Behavior modifier ────────────────────────────────────────────────────

    /// <summary>
    /// Lookup behavior modifier for a monster's AI type.
    /// First resolves alias (ai_type → behavior role), then looks up modifier.
    /// Returns 1.0 for unknown AI types (no modifier applied).
    /// PoC: _get_behavior_modifier() in etp.py:225-231.
    /// </summary>
    public static double GetBehaviorModifier(EtpConfig cfg, string aiType)
    {
        // Resolve alias if present
        string role = cfg.BehaviorAliases.TryGetValue(aiType, out var alias) ? alias : aiType;

        // Look up modifier
        if (cfg.BehaviorModifiers.TryGetValue(role, out double modifier))
            return modifier;

        // Unknown → 1.0 (no adjustment). Log-worthy but non-fatal.
        return 1.0;
    }

    // ── Stat derivation ──────────────────────────────────────────────────────

    /// <summary>
    /// Average DPS of a monster against a baseline player.
    /// PoC: calculate_monster_dps() in etp.py:237-250.
    /// </summary>
    public static double CalculateDps(int dmgMin, int dmgMax, int power = 0)
    {
        // Average of (dmgMin+dmgMax)/2 + power
        return (dmgMin + dmgMax) / 2.0 + power;
    }

    /// <summary>
    /// Durability factor: scaled_hp / (baseline_player_damage × 3).
    /// Normalized so that 3 hits to kill = 1.0 durability.
    /// PoC: calculate_durability() in etp.py:252-272.
    /// </summary>
    public static double DurabilityFactor(int hp, double baselinePlayerDamage = BaselinePlayerDamage)
    {
        double denominator = baselinePlayerDamage * 3.0;
        return denominator > 0 ? hp / denominator : 0;
    }

    /// <summary>
    /// Speed ETP multiplier based on the ratio of monster speed to player speed.
    /// PoC: etp.py:280-285 speed tier table.
    ///
    /// Tiers:
    ///   speed_ratio ≥ 2.0 → 2.0×
    ///   speed_ratio ≥ 1.5 → 1.5×
    ///   speed_ratio ≥ 1.1 → 1.25×
    ///   speed_ratio < 1.1 → 1.0×
    /// </summary>
    public static double GetSpeedMultiplier(double speedRatio)
    {
        if (speedRatio >= 2.0) return 2.0;
        if (speedRatio >= 1.5) return 1.5;
        if (speedRatio >= 1.1) return 1.25;
        return 1.0;
    }

    // ── Core ETP calculation ──────────────────────────────────────────────────

    /// <summary>
    /// Calculate ETP for a monster at a given depth.
    ///
    /// Formula (PoC etp.py:512-553):
    ///
    ///   If monster has etp_base in YAML:
    ///     band_multiplier = (hp_mult + dmg_mult) / 2
    ///     etp = etp_base × band_multiplier × synergy × elite × speed
    ///
    ///   Else (derive from stats):
    ///     scaled_hp    = base_hp × hp_mult
    ///     dps          = (dmg_min + dmg_max)/2 + power (scaled by dmg_mult)
    ///     durability   = scaled_hp / (6.5 × 3)
    ///     behavior     = modifier_lookup(ai_type)
    ///     etp = dps × 6 × durability × behavior × synergy × elite × speed
    ///
    /// The "×6" in the derived formula comes from dps × 6 being an approximate
    /// survival-turns normalization (3 hits durability × 2 to normalize to TTK=6).
    /// </summary>
    public static double GetMonsterEtp(
        EtpConfig cfg,
        MonsterDefinition monster,
        int depth,
        double synergyBonus = 0.0,
        bool isElite = false)
    {
        var band = BandConfigForDepth(cfg, depth);
        double synergy      = 1.0 + synergyBonus;
        double eliteMult    = isElite ? EliteMultiplier : 1.0;
        double speedRatio   = 1.0 + monster.SpeedBonus; // SpeedBonus is additive (0.25 = 25% faster)
        double speedMult    = GetSpeedMultiplier(speedRatio);

        if (monster.EtpBase > 0)
        {
            // Explicit etp_base path — scale by band multiplier average
            double bandMult = (band.HpMultiplier + band.DamageMultiplier) / 2.0;
            return monster.EtpBase * bandMult * synergy * eliteMult * speedMult;
        }

        // Derived path — requires stats
        var stats = monster.Stats;
        if (stats == null)
        {
            // No stats and no etp_base — use default (PoC etp.py:476-479)
            return DefaultEtp;
        }

        double scaledHp    = stats.Hp  * band.HpMultiplier;
        double scaledDmg   = stats.DamageMax * band.DamageMultiplier; // unused in DPS but kept for parity
        double scaledPower = stats.Power * band.DamageMultiplier;

        // DPS uses scaled damage values
        int scaledMin = (int)Math.Round(stats.DamageMin * band.DamageMultiplier);
        int scaledMax = (int)Math.Round(stats.DamageMax * band.DamageMultiplier);
        int scaledPow = (int)Math.Round(scaledPower);

        double dps         = CalculateDps(scaledMin, scaledMax, scaledPow);
        double durability  = DurabilityFactor((int)Math.Round(scaledHp));
        double behavior    = GetBehaviorModifier(cfg, monster.AiType);

        return dps * 6.0 * durability * behavior * synergy * eliteMult * speedMult;
    }

    // ── Budget queries ───────────────────────────────────────────────────────

    /// <summary>
    /// Room ETP budget range for a depth. allowSpike allows up to 1.5× the max.
    /// </summary>
    public static (double Min, double Max) GetRoomEtpBudget(
        EtpConfig cfg, int depth, bool allowSpike = false)
    {
        var band = BandConfigForDepth(cfg, depth);
        double max = allowSpike
            ? band.RoomEtp.Max * cfg.SpikeSettings.SpikeMultiplier
            : band.RoomEtp.Max;
        return (band.RoomEtp.Min, max);
    }

    /// <summary>
    /// Floor ETP budget range for a depth.
    /// </summary>
    public static (double Min, double Max) GetFloorEtpBudget(EtpConfig cfg, int depth)
    {
        var band = BandConfigForDepth(cfg, depth);
        return (band.FloorEtp.Min, band.FloorEtp.Max);
    }

    /// <summary>
    /// Simple legacy-compatible ETP accessor. Returns etp_base for a monster.
    /// Preserved for callers that use the old stub API (e.g., EntityPlacer).
    /// </summary>
    public static int GetEtp(MonsterDefinition def) => def.EtpBase;

    /// <summary>
    /// Legacy FitsInBudget helper. Preserved for existing callers.
    /// </summary>
    public static bool FitsInBudget(int currentEtp, int addEtp, int maxEtp, bool allowSpike)
        => (currentEtp + addEtp) <= (allowSpike ? (int)(maxEtp * 1.5) : maxEtp);
}
