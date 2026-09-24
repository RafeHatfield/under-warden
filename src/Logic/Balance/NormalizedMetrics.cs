namespace UnderWarden.Logic.Balance;

/// <summary>
/// Frozen subset of AggregatedMetrics used for baseline comparison.
///
/// Why a separate record instead of reusing AggregatedMetrics:
/// The baseline JSON shape must be stable across schema changes — if AggregatedMetrics
/// grows new fields (e.g., ranged-combat counters), the baseline doesn't need to be
/// regenerated. NormalizedMetrics is a locked snapshot matching the PoC schema verbatim.
///
/// PoC equivalent: normalize_metrics() in ~/development/rlike/tools/balance_suite.py:119-151.
/// Schema matches PoC reports/baselines/balance_suite_baseline.json exactly.
/// </summary>
public sealed record NormalizedMetrics(
    string ScenarioId,
    int Runs,
    int Deaths,
    double DeathRate,
    double PlayerHitRate,
    double MonsterHitRate,
    /// <summary>
    /// Action-economy proxy: avg monster attacks/run minus avg player attacks/run.
    /// Positive = monsters are taking more actions per run than the player.
    /// PoC formula: (total_monster_attacks / runs) - (total_player_attacks / runs)
    /// </summary>
    double PressureIndex,
    double BonusAttacksPerRun)
{
    /// <summary>
    /// Derive normalized metrics from aggregated harness data.
    ///
    /// PressureIndex = AvgMonsterAttacksPerRun - AvgPlayerAttacksPerRun.
    /// This is the PoC formula from balance_suite.py:149.
    /// </summary>
    public static NormalizedMetrics From(AggregatedMetrics m)
    {
        double pressureIndex = m.AvgMonsterAttacksPerRun - m.AvgPlayerAttacksPerRun;
        int deaths = (int)Math.Round(m.DeathRate * m.TotalRuns);

        return new NormalizedMetrics(
            ScenarioId:        m.ScenarioId,
            Runs:              m.TotalRuns,
            Deaths:            deaths,
            DeathRate:         m.DeathRate,
            PlayerHitRate:     m.PlayerHitRate,
            MonsterHitRate:    m.MonsterHitRate,
            PressureIndex:     pressureIndex,
            BonusAttacksPerRun: m.AvgBonusAttacks);
    }
}
