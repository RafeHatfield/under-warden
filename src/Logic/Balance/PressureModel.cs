namespace UnderWarden.Logic.Balance;

/// <summary>
/// Target bands for pressure invariants by depth band.
/// From docs/DEPTH_PRESSURE_MODEL.md.
/// </summary>
public readonly record struct TargetBand(double Min, double Max)
{
    public bool Contains(double value) => value >= Min && value <= Max;
    public bool Below(double value) => value < Min;   // observed under the band
    public bool Above(double value) => value > Max;   // observed over the band
    public string Status(double value) => Contains(value) ? "OK" : value < Min ? "LOW" : "HIGH";
}

/// <summary>
/// Derived pressure metrics from a scenario run.
/// These are the core invariants that define whether combat feels right.
/// </summary>
public sealed class PressureMetrics
{
    public string ScenarioId { get; init; } = "";
    public int Depth { get; init; }

    /// <summary>Player damage per round.</summary>
    public double DPR_P { get; init; }

    /// <summary>Monster damage per round.</summary>
    public double DPR_M { get; init; }

    /// <summary>Player rounds (turns) to kill one monster. Higher = monsters are tankier.</summary>
    public double RoundsToKill { get; init; }

    /// <summary>Monster rounds (turns) to kill the player. Lower = more dangerous.</summary>
    public double RoundsToDie { get; init; }

    /// <summary>Average damage taken per monster killed.</summary>
    public double DmgPerEncounter { get; init; }

    /// <summary>Ratio RoundsToKill / RoundsToDie. Attrition > 0.6, balanced 0.3-0.6, spike < 0.3.</summary>
    public double PressureRatio => RoundsToDie > 0 ? RoundsToKill / RoundsToDie : 0;

    public double DeathRate { get; init; }
}

/// <summary>
/// Result of evaluating pressure metrics against target bands.
/// </summary>
public sealed class PressureEvaluation
{
    public string ScenarioId { get; init; } = "";
    public int Depth { get; init; }
    public PressureMetrics Metrics { get; init; } = null!;

    public string RoundsToKill_Status { get; init; } = "";
    public string RoundsToDie_Status { get; init; } = "";
    public string DeathRate_Status { get; init; } = "";

    public TargetBand RoundsToKill_Target { get; init; }
    public TargetBand RoundsToDie_Target { get; init; }
    public TargetBand DeathRate_Target { get; init; }

    /// <summary>True if all three metrics are within target bands.</summary>
    public bool AllInBand => RoundsToKill_Status == "OK" && RoundsToDie_Status == "OK" && DeathRate_Status == "OK";
}

/// <summary>
/// Computes pressure model invariants from aggregated harness metrics.
/// Pure math — no state, no IO.
///
/// RoundsToKill = avg monster HP / DPR_P  (how many rounds to kill one monster)
/// RoundsToDie  = player max HP / DPR_M   (how many rounds for monsters to kill player)
/// DPR_P = avg player damage per turn spent attacking
/// DPR_M = avg monster damage per turn
/// </summary>
public static class PressureModel
{
    // Target bands by depth band index (0 = depth 1-2, 1 = depth 3-4, etc.)
    // From Python prototype balance/target_bands.py — union of per-depth ranges within each band.
    // Depth 1: [6-8], depth 2: [7-9]   → band 0: [6-9]
    // Depth 3: [8-10], depth 4: [9-11] → band 1: [8-11]
    // Depth 5: [9-12], depth 6: [10-13]→ band 2: [9-13]
    private static readonly TargetBand[] RoundsToKill_Targets =
    [
        new(6.0, 9.0),    // depth 1-2
        new(8.0, 11.0),   // depth 3-4
        new(9.0, 13.0),   // depth 5-6
        new(10.0, 14.0),  // depth 7-8 (extrapolated)
        new(11.0, 15.0),  // depth 9+  (extrapolated)
    ];

    // Depth 1: [20-24], depth 2: [20-24] → band 0: [20-24]
    // Depth 3: [20-23], depth 4: [18-22] → band 1: [18-23]
    // Depth 5: [17-21], depth 6: [16-20] → band 2: [16-21]
    private static readonly TargetBand[] RoundsToDie_Targets =
    [
        new(20.0, 24.0),  // depth 1-2
        new(18.0, 23.0),  // depth 3-4
        new(16.0, 21.0),  // depth 5-6
        new(14.0, 20.0),  // depth 7-8 (extrapolated)
        new(12.0, 18.0),  // depth 9+  (extrapolated)
    ];

    // Death rate targets by depth band — from Python prototype target_bands.py
    // Depth 1: [0-5%], depth 2: [0-8%]   → band 0: [0-8%]
    // Depth 3: [5-15%], depth 4: [15-30%]→ band 1: [5-30%]
    // Depth 5: [25-40%], depth 6: [35-55%]→band 2: [25-55%]
    private static readonly TargetBand[] DeathRate_Targets =
    [
        new(0.00, 0.08),  // depth 1-2: safe learning
        new(0.05, 0.30),  // depth 3-4: pressure begins → serious
        new(0.25, 0.55),  // depth 5-6: dangerous → brutal
        new(0.35, 0.65),  // depth 7-8: brutal (extrapolated)
        new(0.40, 0.70),  // depth 9+: brutal (extrapolated)
    ];

    // Provisional C# bands — empirically calibrated from harness runs on well-tuned scenarios.
    //
    // C# RoundsToKill is LOWER than PoC theory because player DPR is slightly higher per turn
    // (bot always attacks, no turn waste from item-seeking diversion).
    //
    // C# RoundsToDie is HIGHER than PoC theory because PoC monsters have a two-phase hit system
    // (flat pre-check 75% × d20 hit = ~18-19% effective), whereas C# uses pure d20 (~35%).
    // Despite C# monsters hitting more often, DPR_M is diluted by travel/approach turns,
    // so RoundsToDie remains high. Zombie scenarios inflate RoundsToDie further (travel between spread
    // monsters dominates turn count, reducing DPR_M toward zero).
    //
    // Empirical baseline from well-calibrated scenarios (post Phase-0 SHA-256 reseed at seed=1337):
    //   depth1_tuned (0% death):    RoundsToKill=9.1,  RoundsToDie=34.1
    //   depth2_baseline (0%):       RoundsToKill=8.2,  RoundsToDie=37.2
    //   depth3_orc_brutal (18%):    RoundsToKill=8.1,  RoundsToDie=32.7
    //   depth4_tuned (8%):          RoundsToKill=7.3,  RoundsToDie=43.9
    //   depth6_tuned (38%):         RoundsToKill=6.3,  RoundsToDie=38.4
    private static readonly TargetBand[] Provisional_RoundsToKill =
    [
        new(5.0, 10.0),   // depth 1-2: observed 7.7-7.8 baseline; 5.5-5.8 for fine/masterwork probes
        new(6.0, 12.0),   // depth 3-4: observed 6.9-9.5; wider band covers gear variance
        new(6.0, 22.0),   // depth 5-6: wide — orc ~6-9, zombie ~15-20 (HP+resistance)
        new(7.0, 24.0),   // depth 7-8: extrapolated
        new(8.0, 26.0),   // depth 9+: extrapolated
    ];

    private static readonly TargetBand[] Provisional_RoundsToDie =
    [
        new(32.0, 55.0),  // depth 1-2: observed 37-49 across all orc scenarios
        new(28.0, 52.0),  // depth 3-4: observed 35-43 (orc); zombie scenarios will inflate
        new(22.0, 85.0),  // depth 5-6: orc 30-44; zombie spread scenarios inflate to 50-80 (travel time dilution)
        new(20.0, 48.0),  // depth 7-8: extrapolated
        new(16.0, 45.0),  // depth 9+: extrapolated
    ];

    private static readonly TargetBand[] Provisional_DeathRate =
    [
        new(0.00, 0.08),  // depth 1-2: PoC [0-5%/0-8%]; d1 observed 2%
        new(0.05, 0.30),  // depth 3-4: PoC [5-15%/15-30%]
        new(0.25, 0.60),  // depth 5-6: PoC [25-40%/35-55%]; C# sequential eng. → max bumped to 60%
        new(0.35, 0.65),  // depth 7-8: extrapolated
        new(0.40, 0.70),  // depth 9+: extrapolated
    ];

    /// <summary>Get prototype RoundsToKill target band for a depth.</summary>
    public static TargetBand GetRoundsToKillTarget(int depth) =>
        RoundsToKill_Targets[Math.Clamp(DepthScaling.GetBand(depth), 0, RoundsToKill_Targets.Length - 1)];

    /// <summary>Get prototype RoundsToDie target band for a depth.</summary>
    public static TargetBand GetRoundsToDieTarget(int depth) =>
        RoundsToDie_Targets[Math.Clamp(DepthScaling.GetBand(depth), 0, RoundsToDie_Targets.Length - 1)];

    /// <summary>Get provisional C# RoundsToKill target band for a depth.</summary>
    public static TargetBand GetProvisionalRoundsToKill(int depth) =>
        Provisional_RoundsToKill[Math.Clamp(DepthScaling.GetBand(depth), 0, Provisional_RoundsToKill.Length - 1)];

    /// <summary>Get provisional C# RoundsToDie target band for a depth.</summary>
    public static TargetBand GetProvisionalRoundsToDie(int depth) =>
        Provisional_RoundsToDie[Math.Clamp(DepthScaling.GetBand(depth), 0, Provisional_RoundsToDie.Length - 1)];

    /// <summary>Get provisional C# death rate target band for a depth.</summary>
    public static TargetBand GetProvisionalDeathRate(int depth) =>
        Provisional_DeathRate[Math.Clamp(DepthScaling.GetBand(depth), 0, Provisional_DeathRate.Length - 1)];

    /// <summary>Evaluate against provisional C# bands (current combat system).</summary>
    public static PressureEvaluation EvaluateProvisional(PressureMetrics pm)
    {
        var rtkBand = GetProvisionalRoundsToKill(pm.Depth);
        var rtdBand = GetProvisionalRoundsToDie(pm.Depth);
        var deathBand = GetProvisionalDeathRate(pm.Depth);

        return new PressureEvaluation
        {
            ScenarioId = pm.ScenarioId,
            Depth = pm.Depth,
            Metrics = pm,
            RoundsToKill_Status = rtkBand.Status(pm.RoundsToKill),
            RoundsToDie_Status = rtdBand.Status(pm.RoundsToDie),
            DeathRate_Status = deathBand.Status(pm.DeathRate),
            RoundsToKill_Target = rtkBand,
            RoundsToDie_Target = rtdBand,
            DeathRate_Target = deathBand,
        };
    }

    /// <summary>Get death rate target band for a depth.</summary>
    public static TargetBand GetDeathRateTarget(int depth) =>
        DeathRate_Targets[Math.Clamp(DepthScaling.GetBand(depth), 0, DeathRate_Targets.Length - 1)];

    /// <summary>
    /// Evaluate pressure metrics against target bands. Returns a structured evaluation.
    /// </summary>
    public static PressureEvaluation Evaluate(PressureMetrics pm)
    {
        var rtkBand = GetRoundsToKillTarget(pm.Depth);
        var rtdBand = GetRoundsToDieTarget(pm.Depth);
        var deathBand = GetDeathRateTarget(pm.Depth);

        return new PressureEvaluation
        {
            ScenarioId = pm.ScenarioId,
            Depth = pm.Depth,
            Metrics = pm,
            RoundsToKill_Status = rtkBand.Status(pm.RoundsToKill),
            RoundsToDie_Status = rtdBand.Status(pm.RoundsToDie),
            DeathRate_Status = deathBand.Status(pm.DeathRate),
            RoundsToKill_Target = rtkBand,
            RoundsToDie_Target = rtdBand,
            DeathRate_Target = deathBand,
        };
    }

    /// <summary>
    /// Generate actionable diagnosis text from an evaluation.
    /// </summary>
    public static List<string> Diagnose(PressureEvaluation eval)
    {
        var findings = new List<string>();
        var pm = eval.Metrics;

        if (eval.RoundsToKill_Status == "HIGH")
            findings.Add($"RoundsToKill {pm.RoundsToKill:F1} above target {eval.RoundsToKill_Target.Max} — player kills too slowly. Needs higher DPR_P (currently {pm.DPR_P:F2}). Levers: better weapon, affixes, momentum.");
        else if (eval.RoundsToKill_Status == "LOW")
            findings.Add($"RoundsToKill {pm.RoundsToKill:F1} below target {eval.RoundsToKill_Target.Min} — monsters die too fast. Player DPR_P ({pm.DPR_P:F2}) may be too high, or monster HP too low.");

        if (eval.RoundsToDie_Status == "HIGH")
            findings.Add($"RoundsToDie {pm.RoundsToDie:F1} above target {eval.RoundsToDie_Target.Max} — monsters not threatening enough. Monster DPR_M ({pm.DPR_M:F2}) too low. Levers: monster damage/accuracy scaling, encounter composition.");
        else if (eval.RoundsToDie_Status == "LOW")
            findings.Add($"RoundsToDie {pm.RoundsToDie:F1} below target {eval.RoundsToDie_Target.Min} — monsters too lethal. Reduce monster damage/accuracy at this depth.");

        if (eval.DeathRate_Status == "HIGH")
            findings.Add($"Death rate {pm.DeathRate:P0} above target {eval.DeathRate_Target.Max:P0}. If RoundsToKill/RoundsToDie are in band, this is a composition problem (too many simultaneous enemies), not a scaling problem.");
        else if (eval.DeathRate_Status == "LOW")
            findings.Add($"Death rate {pm.DeathRate:P0} below target {eval.DeathRate_Target.Min:P0}. Encounter may be too easy — consider more enemies or fewer potions.");

        if (pm.PressureRatio > 0.6)
            findings.Add($"Pressure ratio {pm.PressureRatio:F2} indicates attrition — fights are long grinds. Consider increasing monster damage to make fights shorter and deadlier.");
        else if (pm.PressureRatio < 0.3)
            findings.Add($"Pressure ratio {pm.PressureRatio:F2} indicates spike lethality — monsters kill fast but die fast. May need more monster HP.");

        if (findings.Count == 0)
            findings.Add("All metrics within target bands.");

        return findings;
    }

    /// <summary>
    /// Derive pressure metrics from aggregated scenario data.
    /// Requires knowing the average monster HP at the scenario depth
    /// and the player's max HP.
    /// </summary>
    public static PressureMetrics Compute(
        AggregatedMetrics agg,
        int depth,
        double avgMonsterHp,
        int playerMaxHp)
    {
        // DPR_P = total player damage / total turns (across all runs)
        // We use per-run averages: avg damage per run / avg turns per run
        double dprP = agg.AvgTurns > 0 ? agg.AvgPlayerDamageDealt / agg.AvgTurns : 0;
        double dprM = agg.AvgTurns > 0 ? agg.AvgMonsterDamageDealt / agg.AvgTurns : 0;

        // RoundsToKill = monster HP / player DPR (rounds to kill one monster)
        double roundsToKill = dprP > 0 ? avgMonsterHp / dprP : 0;

        // RoundsToDie = player HP / monster DPR (rounds for monsters to kill player)
        double roundsToDie = dprM > 0 ? playerMaxHp / dprM : 0;

        // Damage per encounter = avg monster damage / avg kills
        double dmgPerEnc = agg.AvgMonstersKilled > 0
            ? agg.AvgMonsterDamageDealt / agg.AvgMonstersKilled
            : 0;

        return new PressureMetrics
        {
            ScenarioId = agg.ScenarioId,
            Depth = depth,
            DPR_P = dprP,
            DPR_M = dprM,
            RoundsToKill = roundsToKill,
            RoundsToDie = roundsToDie,
            DmgPerEncounter = dmgPerEnc,
            DeathRate = agg.DeathRate,
        };
    }
}
