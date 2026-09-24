using System.Text;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Depth pressure curve reporting and analysis.
/// Ports ~/development/rlike/analysis/depth_pressure_model.py formatters.
///
/// Consumes DepthCurvePoint records (derived from AggregatedMetrics) and
/// produces ASCII diagnostic reports. No IO — all methods return strings.
/// </summary>
public static class DepthPressureReport
{
    // ── Data structures ────────────────────────────────────────────────────

    public sealed record DepthCurvePoint(
        int Depth,
        string ScenarioId,
        double RoundsToKill,
        double RoundsToDie,
        double DPR_P,
        double DPR_M,
        double PlayerHitRate,
        double MonsterHitRate,
        double DmgPerEncounter,
        double TurnsPerKill,
        double DeathRate);

    public sealed record MultiplierRecommendation(
        int Depth,
        double ObservedHmp,
        double TargetMidpoint,
        double ObservedMonsterDpr,
        double RequiredMonsterDpr,
        double ObservedAvgMonsterDmg,
        double RequiredAvgMonsterDmg,
        double RecommendedDamageMultiplier,
        bool AdjustmentNeeded);

    // ── Factories ──────────────────────────────────────────────────────────

    /// <summary>
    /// Derive a DepthCurvePoint from aggregated metrics.
    /// </summary>
    public static DepthCurvePoint FromAggregated(AggregatedMetrics m)
    {
        var pm = PressureModel.Compute(m, m.Depth, m.AvgMonsterMaxHp, (int)Math.Round(m.AvgPlayerMaxHp));
        double turnsPerKill = m.AvgMonstersKilled > 0 ? m.AvgTurns / m.AvgMonstersKilled : 0;
        return new DepthCurvePoint(
            Depth:           m.Depth,
            ScenarioId:      m.ScenarioId,
            RoundsToKill:            pm.RoundsToKill,
            RoundsToDie:            pm.RoundsToDie,
            DPR_P:           pm.DPR_P,
            DPR_M:           pm.DPR_M,
            PlayerHitRate:   m.PlayerHitRate,
            MonsterHitRate:  m.MonsterHitRate,
            DmgPerEncounter: pm.DmgPerEncounter,
            TurnsPerKill:    turnsPerKill,
            DeathRate:       m.DeathRate);
    }

    // ── Format: Pressure Table ─────────────────────────────────────────────

    /// <summary>
    /// ASCII table of observed RoundsToKill/RoundsToDie/DPR/death_rate per depth.
    /// Ports format_pressure_table() from depth_pressure_model.py:571-600.
    /// </summary>
    public static string FormatPressureTable(IEnumerable<DepthCurvePoint> curve)
    {
        var sorted = curve.OrderBy(p => p.Depth).ToList();
        var sb = new StringBuilder();
        sb.AppendLine(new string('=', 110));
        sb.AppendLine("OBSERVED DEPTH PRESSURE CURVE");
        sb.AppendLine(new string('=', 110));
        sb.AppendLine(
            $"{"Depth",5}  {"Scenario",-35}  {"RoundsToKill",6}  {"RoundsToDie",6}  " +
            $"{"DPR_P",6}  {"DPR_M",6}  {"P(hit_P)",8}  {"P(hit_M)",8}  " +
            $"{"DMG/Enc",8}  {"T/Kill",6}  {"Death%",6}");
        sb.AppendLine(new string('-', 110));

        foreach (var p in sorted)
        {
            sb.AppendLine(
                $"{p.Depth,5}  {p.ScenarioId,-35}  " +
                $"{p.RoundsToKill,6:F2}  {p.RoundsToDie,6:F2}  " +
                $"{p.DPR_P,6:F2}  {p.DPR_M,6:F2}  " +
                $"{p.PlayerHitRate,7:P1}  {p.MonsterHitRate,7:P1}  " +
                $"{p.DmgPerEncounter,8:F1}  {p.TurnsPerKill,6:F1}  " +
                $"{p.DeathRate,5:P1}");
        }

        sb.Append(new string('=', 110));
        return sb.ToString();
    }

    // ── Format: Target Comparison ──────────────────────────────────────────

    /// <summary>
    /// Observed vs target comparison with status flags and diagnosis.
    /// Ports format_target_comparison() from depth_pressure_model.py:603-680.
    /// Uses PressureModel.EvaluateProvisional for band evaluation.
    /// </summary>
    public static string FormatTargetComparison(IEnumerable<DepthCurvePoint> curve)
    {
        var sorted = curve.OrderBy(p => p.Depth).ToList();
        var sb = new StringBuilder();
        sb.AppendLine(new string('=', 120));
        sb.AppendLine("TARGET CURVE COMPARISON");
        sb.AppendLine(new string('=', 120));

        sb.AppendLine(
            $"{"Depth",5}  {"Feel",-22}  " +
            $"{"Death%",6}  {"Target",11}  {"St",4}  " +
            $"{"RoundsToKill",6}  {"Target",9}  {"St",4}  " +
            $"{"RoundsToDie",6}  {"Target",9}  {"St",4}");
        sb.AppendLine(new string('-', 120));

        var allDiagnoses = new List<string>();

        foreach (var p in sorted)
        {
            var pm = new PressureMetrics
            {
                ScenarioId    = p.ScenarioId,
                Depth         = p.Depth,
                RoundsToKill          = p.RoundsToKill,
                RoundsToDie          = p.RoundsToDie,
                DPR_P         = p.DPR_P,
                DPR_M         = p.DPR_M,
                DmgPerEncounter = p.DmgPerEncounter,
                DeathRate     = p.DeathRate,
            };
            var eval = PressureModel.EvaluateProvisional(pm);
            var feel = DepthFeel(p.Depth);

            sb.AppendLine(
                $"{p.Depth,5}  {feel,-22}  " +
                $"{p.DeathRate,5:P0}  " +
                $"{eval.DeathRate_Target.Min,4:P0}–{eval.DeathRate_Target.Max,-4:P0}  " +
                $"{eval.DeathRate_Status,4}  " +
                $"{p.RoundsToKill,6:F2}  " +
                $"{eval.RoundsToKill_Target.Min,3:F0}–{eval.RoundsToKill_Target.Max,-3:F0}  " +
                $"{eval.RoundsToKill_Status,4}  " +
                $"{p.RoundsToDie,6:F2}  " +
                $"{eval.RoundsToDie_Target.Min,3:F0}–{eval.RoundsToDie_Target.Max,-3:F0}  " +
                $"{eval.RoundsToDie_Status,4}");

            var diags = PressureModel.Diagnose(eval);
            if (diags.Count > 0 && !(diags.Count == 1 && diags[0] == "All metrics within target bands."))
            {
                allDiagnoses.Add($"Depth {p.Depth}:");
                foreach (var d in diags) allDiagnoses.Add($"  {d}");
            }
        }

        sb.AppendLine(new string('=', 120));

        if (allDiagnoses.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("DIAGNOSIS");
            sb.AppendLine(new string('-', 60));
            foreach (var d in allDiagnoses) sb.AppendLine(d);
        }

        return sb.ToString();
    }

    // ── Format: Multiplier Recommendations ────────────────────────────────

    /// <summary>
    /// Derived damage-multiplier recommendations table.
    /// Ports format_multiplier_recommendations() from depth_pressure_model.py:683-714.
    /// </summary>
    public static string FormatMultiplierRecommendations(IEnumerable<DepthCurvePoint> curve)
    {
        var sorted = curve.OrderBy(p => p.Depth).ToList();
        var recs   = sorted.Select(DeriveMultiplierRecommendation).ToList();

        var sb = new StringBuilder();
        sb.AppendLine(new string('=', 95));
        sb.AppendLine("DERIVED DAMAGE MULTIPLIER RECOMMENDATIONS (NOT APPLIED)");
        sb.AppendLine(new string('=', 95));
        sb.AppendLine(
            $"{"Depth",5}  {"Obs RoundsToDie",8}  {"Tgt RoundsToDie",8}  " +
            $"{"Obs DPR_M",9}  {"Req DPR_M",9}  " +
            $"{"Obs DMG",7}  {"Req DMG",7}  {"Mult",6}  {"Adj?",4}");
        sb.AppendLine(new string('-', 95));

        foreach (var r in recs)
        {
            string adj = r.AdjustmentNeeded ? "YES" : "no";
            sb.AppendLine(
                $"{r.Depth,5}  " +
                $"{r.ObservedHmp,8:F2}  {r.TargetMidpoint,8:F2}  " +
                $"{r.ObservedMonsterDpr,9:F2}  {r.RequiredMonsterDpr,9:F2}  " +
                $"{r.ObservedAvgMonsterDmg,7:F2}  {r.RequiredAvgMonsterDmg,7:F2}  " +
                $"{r.RecommendedDamageMultiplier,6:F3}  {adj,4}");
        }

        sb.Append(new string('=', 95));
        return sb.ToString();
    }

    /// <summary>
    /// Derive the damage multiplier needed to bring RoundsToDie into target range.
    /// Ports derive_required_damage_multiplier() from depth_pressure_model.py:458-564.
    ///
    /// Math:
    ///   RoundsToDie = player_hp / DPR_M
    ///   To hit target_RoundsToDie: required_DPR_M = player_hp / target_midpoint
    ///   required_avg_dmg = required_DPR_M / monster_hit_rate
    ///   multiplier = required_avg_dmg / observed_avg_dmg
    /// </summary>
    public static MultiplierRecommendation DeriveMultiplierRecommendation(DepthCurvePoint p)
    {
        var hmpBand = PressureModel.GetProvisionalRoundsToDie(p.Depth);
        double targetMidpoint = (hmpBand.Min + hmpBand.Max) / 2.0;

        // Reconstruct player HP from RoundsToDie × DPR_M (PoC: player_hp = h_mp * dpr_m)
        double playerHp = p.RoundsToDie * p.DPR_M;
        if (playerHp <= 0) playerHp = 54.0; // PoC fallback

        double observedAvgDmg = p.MonsterHitRate > 0 ? p.DPR_M / p.MonsterHitRate : 0;

        // In range → multiplier = 1.0
        if (hmpBand.Contains(p.RoundsToDie))
        {
            return new MultiplierRecommendation(
                Depth:                         p.Depth,
                ObservedHmp:                   p.RoundsToDie,
                TargetMidpoint:                targetMidpoint,
                ObservedMonsterDpr:            p.DPR_M,
                RequiredMonsterDpr:            p.DPR_M,
                ObservedAvgMonsterDmg:         observedAvgDmg,
                RequiredAvgMonsterDmg:         observedAvgDmg,
                RecommendedDamageMultiplier:   1.0,
                AdjustmentNeeded:              false);
        }

        // Out of range — derive required multiplier
        double requiredDprM   = targetMidpoint > 0 ? playerHp / targetMidpoint : p.DPR_M;
        double requiredAvgDmg = p.MonsterHitRate > 0 ? requiredDprM / p.MonsterHitRate : 0;
        double multiplier     = observedAvgDmg > 0 ? requiredAvgDmg / observedAvgDmg : 1.0;

        return new MultiplierRecommendation(
            Depth:                         p.Depth,
            ObservedHmp:                   p.RoundsToDie,
            TargetMidpoint:                targetMidpoint,
            ObservedMonsterDpr:            p.DPR_M,
            RequiredMonsterDpr:            requiredDprM,
            ObservedAvgMonsterDmg:         observedAvgDmg,
            RequiredAvgMonsterDmg:         requiredAvgDmg,
            RecommendedDamageMultiplier:   multiplier,
            AdjustmentNeeded:              true);
    }

    // ── Format: Scaling Diagnosis ──────────────────────────────────────────

    /// <summary>
    /// Trend analysis across the depth curve.
    /// Ports format_scaling_diagnosis() from depth_pressure_model.py:717-807.
    ///
    /// Categories (first→last depth deltas):
    ///   RoundsToKill Δ > +0.5 AND RoundsToDie Δ in (-1.0, +1.0) → HP-HEAVY SCALING
    ///   RoundsToKill Δ > +0.5 AND RoundsToDie Δ < -1.0           → BALANCED SCALING
    ///   RoundsToKill Δ < +0.3 AND RoundsToDie Δ < -1.5           → SPIKE LETHALITY
    ///   RoundsToKill Δ ≈0     AND RoundsToDie Δ ≈0               → FLAT SCALING
    ///   Otherwise                                   → MIXED SIGNALS
    /// </summary>
    public static string FormatScalingDiagnosis(IEnumerable<DepthCurvePoint> curve)
    {
        var sorted = curve.OrderBy(p => p.Depth).ToList();
        var sb = new StringBuilder();
        sb.AppendLine(new string('=', 70));
        sb.AppendLine("SCALING SYSTEM DIAGNOSIS");
        sb.AppendLine(new string('=', 70));

        if (sorted.Count < 2)
        {
            sb.AppendLine("Insufficient depth data for trend analysis (need >= 2 depths)");
            sb.Append(new string('=', 70));
            return sb.ToString();
        }

        var first = sorted.First();
        var last  = sorted.Last();

        double hPmDelta  = last.RoundsToKill   - first.RoundsToKill;
        double hMpDelta  = last.RoundsToDie   - first.RoundsToDie;
        double dprPDelta = last.DPR_P  - first.DPR_P;
        double dprMDelta = last.DPR_M  - first.DPR_M;

        sb.AppendLine($"Depth range analyzed: {first.Depth} → {last.Depth}");
        sb.AppendLine();
        sb.AppendLine($"Trend Analysis (depth {first.Depth} → {last.Depth}):");
        sb.AppendLine($"  RoundsToKill (player rounds-to-kill):   {first.RoundsToKill:F2} → {last.RoundsToKill:F2}  (Δ = {hPmDelta:+.2f})");
        sb.AppendLine($"  RoundsToDie (monster rounds-to-die):  {first.RoundsToDie:F2} → {last.RoundsToDie:F2}  (Δ = {hMpDelta:+.2f})");
        sb.AppendLine($"  DPR_P (player DPR):           {first.DPR_P:F2} → {last.DPR_P:F2}  (Δ = {dprPDelta:+.2f})");
        sb.AppendLine($"  DPR_M (monster DPR):          {first.DPR_M:F2} → {last.DPR_M:F2}  (Δ = {dprMDelta:+.2f})");
        sb.AppendLine();

        // Diagnosis categories (PoC depth_pressure_model.py:759-790)
        string diagnosis, explanation;
        if (hPmDelta > 0.5 && Math.Abs(hMpDelta) < 1.0)
        {
            diagnosis   = "HP-HEAVY SCALING";
            explanation = "Monsters take more hits to kill at deeper depths (RoundsToKill rising),\n" +
                          "  but monster lethality is flat (RoundsToDie stable). This creates ATTRITION:\n" +
                          "  fights are longer, not deadlier. Player dies from resource exhaustion\n" +
                          "  rather than tactical failure. Damage scaling needs to increase.";
        }
        else if (hPmDelta > 0.5 && hMpDelta < -1.0)
        {
            diagnosis   = "BALANCED SCALING";
            explanation = "Both RoundsToKill (fight duration) and RoundsToDie (survival budget) are moving\n" +
                          "  in the expected directions. Monsters are getting tougher AND deadlier.";
        }
        else if (hPmDelta < 0.3 && hMpDelta < -1.5)
        {
            diagnosis   = "SPIKE LETHALITY";
            explanation = "Monster damage is scaling faster than HP. Fights are about the same\n" +
                          "  length but significantly deadlier. Risk of unavoidable spike deaths.";
        }
        else if (Math.Abs(hPmDelta) < 0.3 && Math.Abs(hMpDelta) < 1.0)
        {
            diagnosis   = "FLAT SCALING";
            explanation = "Neither fight duration nor lethality is changing meaningfully.\n" +
                          "  Deeper depths feel similar to shallower ones. Scaling is too timid.";
        }
        else
        {
            diagnosis   = "MIXED SIGNALS";
            explanation = "Trends do not clearly fit a single pattern. This may be due to\n" +
                          "  mixed monster types across depths or insufficient data points.";
        }

        sb.AppendLine($"Diagnosis: {diagnosis}");
        sb.AppendLine($"  {explanation}");
        sb.AppendLine();

        // Per-depth attrition indicator
        sb.AppendLine("Attrition vs Lethality Indicator:");
        foreach (var p in sorted)
        {
            double ratio     = p.RoundsToDie > 0 ? p.RoundsToKill / p.RoundsToDie : 0;
            string indicator = ratio > 0.6 ? "ATTRITION" : (ratio < 0.3 ? "LETHAL" : "BALANCED");
            sb.AppendLine($"  Depth {p.Depth}: RoundsToKill/RoundsToDie = {ratio:F3}  → {indicator}");
        }

        sb.Append(new string('=', 70));
        return sb.ToString();
    }

    // ── Format: Full Report ────────────────────────────────────────────────

    /// <summary>
    /// All four sections concatenated. Equivalent to PoC print_pressure_report().
    /// </summary>
    public static string FormatFullReport(IEnumerable<DepthCurvePoint> curve)
    {
        var pts = curve.ToList();
        return string.Join("\n\n", new[]
        {
            FormatPressureTable(pts),
            FormatTargetComparison(pts),
            FormatMultiplierRecommendations(pts),
            FormatScalingDiagnosis(pts),
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Depth "feel" labels from PoC target_bands.py:78-121.
    /// </summary>
    private static string DepthFeel(int depth) => depth switch
    {
        1 => "safe learning",
        2 => "warm-up",
        3 => "pressure begins",
        4 => "serious",
        5 => "dangerous",
        6 => "brutal but survivable",
        _ => $"depth {depth}",
    };
}
