namespace UnderWarden.Logic.Balance;

/// <summary>
/// The actionable tuning levers a too-hard death can implicate. One signal disambiguates one lever
/// (memory:project_0c_diagnostic_design) — the point is a BOUNDED set, each mapped to a specific dial,
/// so attribution is precise instead of "something is broken."
/// </summary>
public enum BalanceLever
{
    MonsterDamage,        // damage-per-hit too high → the monster's damage stat
    Armor,                // killer hit-rate too high → the armor/AC curve underabsorbs at depth
    WeaponSpeedControl,   // too few counterattacks landed → slow weapon / got locked down
    Density,              // too many distinct attackers → encounter composition (count), not a stat
    AttackFrequency,      // hits-per-engagement-turn too high → the speed dial (the wraith lever)
}

/// <summary>
/// Per-region EXPECTED values for the five lever signals, at baseline gear. Authored during tuning
/// (decide-on-merits, then measure), same as the target band — these are placeholders until then.
/// A death's observed signal is read as a distance from these.
/// </summary>
public sealed record LeverExpectation(
    double DamagePerHit,
    double KillerHitRate,
    double CounterattacksLanded,
    double DistinctAttackers,
    double AttackFrequency);

/// <summary>One implicated lever and how far the observed signal ran past expectation (relative, signed +).</summary>
public sealed record LeverFinding(BalanceLever Lever, double RelativeDeviation);

/// <summary>Tunable knobs for attribution. Separate from the verdict logic so thresholds can move freely.</summary>
public sealed record LeverConfig
{
    /// <summary>A signal implicates its lever when it deviates from expectation, in the bad direction, by ≥ this fraction.</summary>
    public double ImplicationTolerance { get; init; } = 0.25;
}

/// <summary>
/// Pure, side-effect-free lever attribution — the diagnostic layer that sits BESIDE FloorHealthClassifier.
///
/// Division of labour (the whole reason this is multi-signal, not lone-fast-death):
///   • SURVIVAL RATE is the balance verdict (multivariate; computed from outcomes, not here).
///   • FloorHealthClassifier renders the role-aware HEALTH verdict from hits-to-down (role-fastness).
///   • This classifier explains WHY a flagged death was fast, disambiguating among the FIVE actionable
///     levers from the other signals — so a fast death to a baseline reads "armor underabsorbing at
///     depth 7", not "baseline mob broken", when the monster's own damage-per-hit is normal.
///
/// hits-to-down (the sixth signal) is the upstream trigger, not re-evaluated here — attribution is only
/// meaningful once health flagged the floor. Frequency is its OWN lever, never folded into damage: a
/// wraith landing two normal-damage blows per turn implicates AttackFrequency, not MonsterDamage.
/// </summary>
public static class LeverAttributionClassifier
{
    /// <summary>
    /// The levers a death implicates, ordered by how far past expectation they ran (worst first).
    /// Empty when every signal is within tolerance — the death is "as designed" and the too-hard floor
    /// is then composition/variance rather than any single dial.
    /// </summary>
    public static IReadOnlyList<LeverFinding> Attribute(
        PlayerDeathRecord death, LeverExpectation expected, LeverConfig cfg)
    {
        var findings = new List<LeverFinding>(5);

        // Higher-is-worse signals: observed running ABOVE expectation implicates the lever.
        AddIfOver(findings, BalanceLever.MonsterDamage,   death.DamagePerHit,                expected.DamagePerHit,         cfg);
        AddIfOver(findings, BalanceLever.Armor,           death.KillerHitRate,               expected.KillerHitRate,        cfg);
        AddIfOver(findings, BalanceLever.Density,         death.DistinctAttackers,           expected.DistinctAttackers,    cfg);
        AddIfOver(findings, BalanceLever.AttackFrequency, death.AttackFrequency,             expected.AttackFrequency,      cfg);

        // Lower-is-worse: too FEW counterattacks implicates the weapon-speed / control lever.
        AddIfUnder(findings, BalanceLever.WeaponSpeedControl, death.CounterattacksLanded,    expected.CounterattacksLanded, cfg);

        findings.Sort((a, b) => b.RelativeDeviation.CompareTo(a.RelativeDeviation));
        return findings;
    }

    /// <summary>The single most-implicated lever, or null when nothing is out of tolerance.</summary>
    public static BalanceLever? Dominant(PlayerDeathRecord death, LeverExpectation expected, LeverConfig cfg)
    {
        var findings = Attribute(death, expected, cfg);
        return findings.Count > 0 ? findings[0].Lever : null;
    }

    private static void AddIfOver(
        List<LeverFinding> findings, BalanceLever lever, double observed, double expected, LeverConfig cfg)
    {
        if (expected <= 0) return; // no ratio to form against a zero/absent expectation
        double rel = (observed - expected) / expected;
        if (rel >= cfg.ImplicationTolerance)
            findings.Add(new LeverFinding(lever, rel));
    }

    private static void AddIfUnder(
        List<LeverFinding> findings, BalanceLever lever, double observed, double expected, LeverConfig cfg)
    {
        if (expected <= 0) return;
        double rel = (expected - observed) / expected; // shortfall as a positive fraction
        if (rel >= cfg.ImplicationTolerance)
            findings.Add(new LeverFinding(lever, rel));
    }
}
