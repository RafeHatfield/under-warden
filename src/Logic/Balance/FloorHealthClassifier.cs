namespace UnderWarden.Logic.Balance;

/// <summary>
/// The threat role a monster plays, per docs/balance/threat_archetypes.md §2.
/// Drives role-aware health: the same death-speed means opposite things depending on
/// the killer's archetype (fast death to a baseline = broken; fast death to a spike = working).
/// </summary>
public enum ThreatArchetype
{
    Baseline,   // durable; danger is attrition. Target ttd HIGH.
    Spike,      // lethal-if-ignored, counterable. Target ttd LOW.
    Escalator,  // transforms the swarm; tuned by multiplier × response window (not ttd).
    Fused,      // both (lich) — spike + escalator.
}

/// <summary>
/// The role-aware health verdict for one floor. This is the operational definition of "balanced"
/// (docs/balance/threat_archetypes.md §4): not "did the player die" but "did the RIGHT thing kill
/// them for the RIGHT reason." Healthy is the in-band, intent-matching state (baseline-OK and
/// spike-OK are both Healthy sub-cases — the report annotates which archetypes were present).
/// </summary>
public enum FloorHealth
{
    Healthy,           // in band; deaths match archetype intent (incl. baseline-OK / spike-OK)
    TooEasy,           // death% below band, no archetype-attributable cause
    TooHard,           // death% above band, no archetype-attributable cause
    BaselineBroken,    // a baseline mob is secretly lethal (fast deaths to baseline), OR an escalator
                       //   was neutralized early and the player STILL lost — the baseline was the real threat
    SpikeBroken,       // a spike is a pushover — present but produced no deaths while the floor is too easy
    EscalatorBroken,   // an escalator doesn't escalate — leaving it alive isn't even hard
    EscalatorUnfair,   // an escalator kills with no response window — the lever exists but can't be reached
}

/// <summary>
/// Per-archetype intended HITS-to-down-player (the killer's landed blows the player should absorb).
/// A baseline's is HIGH (durable; survive many hits), a spike's LOW (downs you in few hits if ignored).
/// Unit is hits, not turns: a landed hit is the monster's actual offensive contribution, which strips
/// the swarm-window contamination (idle turns, positioning, other monsters) that corrupts a turns-based
/// measure, and has no free measurement parameter. Attack FREQUENCY (how fast those hits arrive) is a
/// SEPARATE lever (hits÷engagement-turns), deliberately not folded in here.
/// </summary>
public sealed record ArchetypeTarget(double TargetHitsToDown);

/// <summary>
/// The canonical per-floor targets the classifier checks observed reality against.
/// In production this is hydrated from config/balance/target_table.yaml; in tests it is built inline.
/// </summary>
public sealed record FloorTarget(
    TargetBand DeathPct,
    IReadOnlyDictionary<ThreatArchetype, ArchetypeTarget> ByArchetype);

/// <summary>
/// One player death, reduced to the inputs the verdict logic needs: who landed the kill, and how many
/// of that killer's blows the player absorbed before going down (HitsToDown — the role-fastness signal).
/// The full diagnostic capture (damage-per-hit, hit-rate, counterattacks, attacker-count, engagement-turns)
/// lives on PlayerDeathRecord in the harness; this is the projection the classifier consumes.
/// </summary>
public sealed record DeathRecord(ThreatArchetype KillerArchetype, double HitsToDown);

/// <summary>
/// Refinement 2's third signal. Death% for the same composition under two sub-conditions:
/// escalator left alive vs escalator killed early. Produced by the staged-start machinery (0c step 8);
/// the classifier only consumes it. Null when the floor has no escalator.
/// </summary>
public sealed record EscalatorComparison(double DeathPctAlive, double DeathPctKilledEarly);

/// <summary>Observed reality for one floor, aggregated over a soak's runs.</summary>
public sealed record FloorObserved(
    double DeathPct,
    IReadOnlyList<DeathRecord> Deaths,
    bool HasSpike,
    bool HasEscalator,
    bool EscalatorReachable,            // was there a response window to reach/neutralize the escalator
    EscalatorComparison? Escalator);    // null if no escalator on the floor

/// <summary>
/// Tunable knobs. The classifier TESTS pin the verdict LOGIC; these stay adjustable so the
/// thresholds can be retuned without changing what "broken" means.
/// </summary>
public sealed record ClassifierConfig
{
    /// <summary>A death is "fast for its killer" when HitsToDown &lt; this × the killer archetype's target hits-to-down.</summary>
    public double FastDeathTtdFraction { get; init; } = 1.0 / 3.0;

    /// <summary>Share of all deaths that must be fast-to-baseline to call the baseline broken.</summary>
    public double BaselineBrokenShare { get; init; } = 0.25;

    /// <summary>Death% (absolute, in fraction points) that killing the escalator early must shave off to count as "improves".</summary>
    public double EscalatorImprovementDelta { get; init; } = 0.10;
}

/// <summary>
/// Pure, side-effect-free role-aware floor health classifier. Sibling to OutcomeClassifier and
/// BalanceSuiteEvaluator. The instrument of truth for the balance pass — and per the outcome-testing
/// requirement, its verdicts are unit-tested at the OUTCOME level (a known-broken composition must
/// classify as broken), never merely that inputs were attached.
///
/// See docs/balance/threat_archetypes.md §4 and tasks/0c_balance_report.md.
/// </summary>
public static class FloorHealthClassifier
{
    public static FloorHealth Classify(FloorObserved o, FloorTarget t, ClassifierConfig cfg)
    {
        int total = o.Deaths.Count;

        // ── 1. Baseline must never kill fast (refinement 1: anchored to the KILLER'S archetype ttd). ──
        // A baseline's target ttd is high; a death well under it means the baseline is secretly lethal.
        // A fast death to a SPIKE is the spike doing its job, so it is deliberately not counted here.
        if (total > 0)
        {
            int fastToBaseline = o.Deaths.Count(d =>
                d.KillerArchetype == ThreatArchetype.Baseline && IsFastForArchetype(d, t, cfg));
            if ((double)fastToBaseline / total >= cfg.BaselineBrokenShare)
                return FloorHealth.BaselineBroken;
        }

        // ── 2. Escalator: three signals, each isolating a distinct failure (refinement 2). ──
        if (o.HasEscalator && o.Escalator is { } e)
        {
            bool aliveHard      = t.DeathPct.Above(e.DeathPctAlive);                  // leaving it alive is too-much (intended)
            bool improves       = (e.DeathPctAlive - e.DeathPctKilledEarly) >= cfg.EscalatorImprovementDelta;
            bool killedWinnable = !t.DeathPct.Above(e.DeathPctKilledEarly);           // killing it brings death% into/under band

            // 2a. Not escalating — leaving it alive isn't even hard. It isn't the threat it claims to be.
            if (!aliveHard)
                return FloorHealth.EscalatorBroken;

            // 2b. Killing it early doesn't move death% — the escalator isn't the lever; the baseline underneath is.
            if (!improves)
                return FloorHealth.BaselineBroken;

            // 2c. It IS the lever and improvement is real, but the player can't reach it in time — no response window.
            if (!o.EscalatorReachable)
                return FloorHealth.EscalatorUnfair;

            // 2d. Even neutralized early, the floor is still too-much → the baseline is overtuned, not the escalator.
            if (!killedWinnable)
                return FloorHealth.BaselineBroken;

            // else: escalator healthy — fall through to the remaining checks.
        }

        // ── 3. Spike must be lethal-if-ignored. Present but produced no deaths while the floor is too easy = pushover. ──
        if (o.HasSpike && t.DeathPct.Below(o.DeathPct))
        {
            bool anySpikeDeath = o.Deaths.Any(d =>
                d.KillerArchetype is ThreatArchetype.Spike or ThreatArchetype.Fused);
            if (!anySpikeDeath)
                return FloorHealth.SpikeBroken;
        }

        // ── 4. Generic death% band check. ──
        if (t.DeathPct.Above(o.DeathPct)) return FloorHealth.TooHard;
        if (t.DeathPct.Below(o.DeathPct)) return FloorHealth.TooEasy;
        return FloorHealth.Healthy;
    }

    /// <summary>True when this death was fast relative to the KILLER archetype's intended hits-to-down.</summary>
    private static bool IsFastForArchetype(DeathRecord d, FloorTarget t, ClassifierConfig cfg)
    {
        if (!t.ByArchetype.TryGetValue(d.KillerArchetype, out var at))
            return false;
        return d.HitsToDown < cfg.FastDeathTtdFraction * at.TargetHitsToDown;
    }
}
