using System.Text.Json.Serialization;
using UnderWarden.Logic.Core;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Metrics collected from a single scenario run.
/// </summary>
public sealed class RunMetrics
{
    public int TurnsTaken { get; set; }
    public bool PlayerDied { get; set; }

    /// <summary>
    /// True if this run was aborted by BotBrain's stuck detection (stuck counter >= 15).
    /// Aborted runs count as death-equivalent for Death% calculations.
    /// See OutcomeClassifier.Aborted and BotAction.ActionType.AbortRun.
    /// </summary>
    public bool WasAborted { get; set; }

    // Attack tracking
    public int PlayerAttacks { get; set; }
    public int PlayerHits { get; set; }
    public int PlayerDamageDealt { get; set; }
    public int MonsterAttacks { get; set; }
    public int MonsterHits { get; set; }
    public int MonsterDamageDealt { get; set; }

    // Kill tracking
    public int MonstersKilled { get; set; }

    // Healing
    public int PotionsUsed { get; set; }


    // ── 0c per-death lever capture (bridged onto ScenarioHarness from DungeonRunHarness) ──

    /// <summary>
    /// Entity ID of the killer from the player's DeathEvent (-1 = hazard, NoDeath = no death this run).
    /// Populated by RecordTurn; consumed by ScenarioHarness to call EngagementTracker.BuildDeathRecord.
    /// </summary>
    public int KillerId { get; set; } = NoDeath;
    private const int NoDeath = -2;

    /// <summary>True when at least one Spike/Fused monster was present at the start of the run.</summary>
    public bool HadSpike { get; set; }

    /// <summary>True when at least one Escalator/Fused monster was present at the start of the run.</summary>
    public bool HadEscalator { get; set; }

    /// <summary>
    /// Per-death lever record for this run. Null when the player survived or when the run was aborted
    /// (no engagement to attribute). Set by ScenarioHarness after the run completes.
    /// </summary>
    public PlayerDeathRecord? EngagementDeath { get; set; }

    // Momentum
    public int BonusAttacks { get; set; }

    // Captured at run start for hits-based (TtkHits/TtdHits) and rounds-based metric calculations
    public int PlayerMaxHp { get; set; }
    public double MonsterAvgMaxHp { get; set; }

    // ── Ranged Combat Metrics (Phase 22.2) ───────────────────────────────────

    /// <summary>Ranged attacks that resolved (hit or miss, but NOT denied). Excludes denial.</summary>
    public int RangedAttacksMadeByPlayer { get; set; }

    /// <summary>Ranged attacks blocked because target was out of range (d>8) or no LoS.</summary>
    public int RangedAttacksDeniedOutOfRange { get; set; }

    /// <summary>Total damage dealt via ranged attacks (hit-only, after band modifier).</summary>
    public int RangedDamageDealtByPlayer { get; set; }

    /// <summary>Sum of damage lost to range band penalties across all ranged hits.</summary>
    public int RangedDamagePenaltyTotal { get; set; }

    /// <summary>Times the defender retaliated on a d≤1 ranged shot.</summary>
    public int RangedAdjacentRetaliationsTriggered { get; set; }

    /// <summary>Successful ranged knockback procs (tiles_moved > 0).</summary>
    public int RangedKnockbackProcs { get; set; }

    /// <summary>Special ammo shots consumed (hit OR miss, not denied).</summary>
    public int SpecialAmmoShotsFired { get; set; }

    /// <summary>Times a special ammo on-hit effect (burning, entangled) was actually applied.</summary>
    public int SpecialAmmoEffectsApplied { get; set; }

    /// <summary>Times movement or leap was blocked due to EntangledEffect (player + monster + leap).</summary>
    public int EntangleMovesBlocked { get; set; }

    // ── End Ranged Combat Metrics ─────────────────────────────────────────────

    /// <summary>
    /// Record metrics from a single turn's events. Call once per turn.
    /// Derives all counters from the event stream — no parallel tracking needed.
    /// </summary>
    public void RecordTurn(TurnResult result, int playerId)
    {
        TurnsTaken++;
        foreach (var evt in result.Events)
        {
            switch (evt)
            {
                case AttackEvent atk when atk.ActorId == playerId:
                    PlayerAttacks++;
                    if (atk.Hit)
                    {
                        PlayerHits++;
                        PlayerDamageDealt += atk.Damage;
                    }
                    if (atk.IsBonusAttack) BonusAttacks++;
                    if (atk.TargetKilled) MonstersKilled++;
                    break;

                case AttackEvent atk:
                    MonsterAttacks++;
                    if (atk.Hit)
                    {
                        MonsterHits++;
                        MonsterDamageDealt += atk.Damage;
                    }
                    if (atk.IsBonusAttack) BonusAttacks++;
                    break;

                case DeathEvent death when death.ActorId == playerId:
                    KillerId = death.KillerId; // -1 = ground hazard, ≥0 = monster entity id
                    break;

                case HealEvent:
                    PotionsUsed++;
                    break;

                // ── Ranged combat metrics derived from events ──────────────────
                case RangedAttackEvent ranged when ranged.ActorId == playerId:
                    if (ranged.Denied)
                    {
                        RangedAttacksDeniedOutOfRange++;
                    }
                    else
                    {
                        RangedAttacksMadeByPlayer++;
                        PlayerAttacks++;
                        if (ranged.Hit)
                        {
                            RangedDamageDealtByPlayer += ranged.Damage;
                            PlayerHits++;
                            PlayerDamageDealt += ranged.Damage;
                            // Penalty = pre-modifier damage minus actual damage dealt
                            int penalty = ranged.DamageBeforePenalty - ranged.Damage;
                            if (penalty > 0)
                                RangedDamagePenaltyTotal += penalty;
                        }
                        if (ranged.TargetKilled) MonstersKilled++;
                        if (ranged.RetaliationTriggered)
                            RangedAdjacentRetaliationsTriggered++;
                        if (ranged.SpecialEffectApplied)
                            SpecialAmmoEffectsApplied++;
                    }
                    break;

                case RangedKnockbackEvent:
                    RangedKnockbackProcs++;
                    break;

                case SpecialAmmoConsumedEvent:
                    SpecialAmmoShotsFired++;
                    break;

                case EntangleMoveBlockedEvent:
                    EntangleMovesBlocked++;
                    break;
            }
        }
    }
}

/// <summary>
/// Aggregated metrics across multiple runs of the same scenario.
/// </summary>
public sealed class AggregatedMetrics
{
    public string ScenarioId { get; init; } = "";
    public string Name { get; init; } = "";
    public int Depth { get; init; }
    public int TotalRuns { get; init; }
    public int Seed { get; init; }

    /// <summary>
    /// Mirrors ScenarioDefinition.IsProbe — scenario is a gear/affix probe, not a calibration target.
    /// </summary>
    public bool IsProbe { get; init; }

    // Averages
    public double AvgTurns { get; init; }
    public double DeathRate { get; init; }
    public double PlayerHitRate { get; init; }
    public double MonsterHitRate { get; init; }

    // Pressure model invariants
    public double AvgPlayerDamageDealt { get; init; }
    public double AvgMonsterDamageDealt { get; init; }
    public double AvgMonstersKilled { get; init; }

    /// <summary>Average player max HP across all runs (used for DPR-based RoundsToDie).</summary>
    public double AvgPlayerMaxHp { get; init; }

    /// <summary>Average monster max HP per scenario run (used for DPR-based RoundsToKill).</summary>
    public double AvgMonsterMaxHp { get; init; }

    /// <summary>TtkHits (hits-based): avg monster HP / avg player damage per hit.</summary>
    /// <remarks>On-disk JSON key kept as "h_pm" for baseline/report round-trip stability (FIND-005 rename).</remarks>
    [JsonPropertyName("h_pm")]
    public double TtkHits { get; init; }

    /// <summary>TtdHits (hits-based): avg player HP / avg monster damage per hit.</summary>
    /// <remarks>On-disk JSON key kept as "h_mp" for baseline/report round-trip stability (FIND-005 rename).</remarks>
    [JsonPropertyName("h_mp")]
    public double TtdHits { get; init; }

    /// <summary>Average bonus attacks per run (player + monster combined).</summary>
    public double AvgBonusAttacks { get; init; }

    /// <summary>
    /// Average player attacks per run (hits + misses combined).
    /// Used by NormalizedMetrics.PressureIndex = AvgMonsterAttacksPerRun - AvgPlayerAttacksPerRun.
    /// PoC: total_player_attacks / runs.
    /// Note: PoC does not distinguish bonus attacks in this count; C# follows the same convention
    /// (BonusAttacks are included in RunMetrics.PlayerAttacks).
    /// </summary>
    public double AvgPlayerAttacksPerRun { get; init; }

    /// <summary>
    /// Average monster attacks per run (hits + misses combined).
    /// Used by NormalizedMetrics.PressureIndex = AvgMonsterAttacksPerRun - AvgPlayerAttacksPerRun.
    /// PoC: total_monster_attacks / runs.
    /// </summary>
    public double AvgMonsterAttacksPerRun { get; init; }

    // ── Ranged Combat Aggregates (Phase 22.2) ─────────────────────────────────

    public double AvgRangedAttacksMadeByPlayer { get; init; }
    public double AvgRangedAttacksDeniedOutOfRange { get; init; }
    public double AvgRangedDamageDealtByPlayer { get; init; }
    public double AvgRangedDamagePenaltyTotal { get; init; }
    public double AvgRangedAdjacentRetaliationsTriggered { get; init; }
    public double AvgRangedKnockbackProcs { get; init; }
    public double AvgSpecialAmmoShotsFired { get; init; }
    public double AvgSpecialAmmoEffectsApplied { get; init; }
    public double AvgEntangleMovesBlocked { get; init; }

    // ── End Ranged Combat Aggregates ──────────────────────────────────────────

    /// <summary>Average healing potions used per run. Key output for the cooldown sweep row.</summary>
    public double AvgPotionsUsed { get; init; }

    // ── Per-composition engagement band ──────────────────────────────────────

    /// <summary>
    /// Per-composition intended death-rate band from the scenario's target_death_pct field.
    /// Null when the scenario has no band (uses the depth-region band from the target table).
    /// Architecturally separate from the target table's depth bands (Layer-2).
    /// </summary>
    public TargetBand? EngagementBand { get; init; }

    // ── 0c per-death lever data (bridged from EngagementTracker) ─────────────

    /// <summary>
    /// Per-death lever records for runs that ended in a real player death (not abort, not survival).
    /// In a controlled scenario DistinctAttackers reflects the actual composition — uncontaminated.
    /// Empty when no deaths occurred or the harness ran without the tracker.
    /// </summary>
    public IReadOnlyList<PlayerDeathRecord> Deaths { get; init; } = Array.Empty<PlayerDeathRecord>();

    /// <summary>True when at least one Spike/Fused monster was present in any run's starting state.</summary>
    public bool HasSpike { get; init; }

    /// <summary>True when at least one Escalator/Fused monster was present in any run's starting state.</summary>
    public bool HasEscalator { get; init; }

    // ── End 0c lever data ─────────────────────────────────────────────────────

    /// <summary>
    /// Aggregate a list of run metrics into summary statistics.
    /// </summary>
    public static AggregatedMetrics FromRuns(string scenarioId, int seed, List<RunMetrics> runs,
        string name = "", int depth = 0, bool isProbe = false, TargetBand? engagementBand = null)
    {
        if (runs.Count == 0)
            return new AggregatedMetrics { ScenarioId = scenarioId, Name = name, Depth = depth, Seed = seed, IsProbe = isProbe };

        int totalPlayerAttacks = runs.Sum(r => r.PlayerAttacks);
        int totalPlayerHits    = runs.Sum(r => r.PlayerHits);
        int totalMonsterAttacks = runs.Sum(r => r.MonsterAttacks);
        int totalMonsterHits   = runs.Sum(r => r.MonsterHits);
        int totalPlayerDamage  = runs.Sum(r => r.PlayerDamageDealt);
        int totalMonsterDamage = runs.Sum(r => r.MonsterDamageDealt);
        double avgMonsterMaxHp = runs.Average(r => r.MonsterAvgMaxHp);
        double avgPlayerMaxHp  = runs.Average(r => (double)r.PlayerMaxHp);

        // Hits-based TtkHits/TtdHits — useful for "how hard is each swing" analysis
        double ttkHits = totalPlayerHits > 0 && totalPlayerDamage > 0
            ? avgMonsterMaxHp / ((double)totalPlayerDamage / totalPlayerHits)
            : 0;
        double ttdHits = totalMonsterHits > 0 && totalMonsterDamage > 0
            ? avgPlayerMaxHp / ((double)totalMonsterDamage / totalMonsterHits)
            : 0;

        return new AggregatedMetrics
        {
            ScenarioId            = scenarioId,
            Name                  = name,
            Depth                 = depth,
            Seed                  = seed,
            IsProbe               = isProbe,
            TotalRuns             = runs.Count,
            AvgTurns              = runs.Average(r => r.TurnsTaken),
            DeathRate             = (double)runs.Count(r => r.PlayerDied) / runs.Count,
            PlayerHitRate         = totalPlayerAttacks > 0 ? (double)totalPlayerHits / totalPlayerAttacks : 0,
            MonsterHitRate        = totalMonsterAttacks > 0 ? (double)totalMonsterHits / totalMonsterAttacks : 0,
            AvgPlayerDamageDealt  = runs.Average(r => r.PlayerDamageDealt),
            AvgMonsterDamageDealt = runs.Average(r => r.MonsterDamageDealt),
            AvgMonstersKilled     = runs.Average(r => r.MonstersKilled),
            AvgPlayerMaxHp        = avgPlayerMaxHp,
            AvgMonsterMaxHp       = avgMonsterMaxHp,
            TtkHits               = ttkHits,
            TtdHits               = ttdHits,
            AvgBonusAttacks          = runs.Average(r => r.BonusAttacks),
            AvgPlayerAttacksPerRun   = runs.Average(r => r.PlayerAttacks),
            AvgMonsterAttacksPerRun  = runs.Average(r => r.MonsterAttacks),
            // Ranged aggregates
            AvgRangedAttacksMadeByPlayer          = runs.Average(r => r.RangedAttacksMadeByPlayer),
            AvgRangedAttacksDeniedOutOfRange       = runs.Average(r => r.RangedAttacksDeniedOutOfRange),
            AvgRangedDamageDealtByPlayer           = runs.Average(r => r.RangedDamageDealtByPlayer),
            AvgRangedDamagePenaltyTotal            = runs.Average(r => r.RangedDamagePenaltyTotal),
            AvgRangedAdjacentRetaliationsTriggered = runs.Average(r => r.RangedAdjacentRetaliationsTriggered),
            AvgRangedKnockbackProcs                = runs.Average(r => r.RangedKnockbackProcs),
            AvgSpecialAmmoShotsFired               = runs.Average(r => r.SpecialAmmoShotsFired),
            AvgSpecialAmmoEffectsApplied           = runs.Average(r => r.SpecialAmmoEffectsApplied),
            AvgEntangleMovesBlocked                = runs.Average(r => r.EntangleMovesBlocked),
            AvgPotionsUsed                         = runs.Average(r => r.PotionsUsed),
            // 0c per-death lever data
            Deaths        = runs.Where(r => r.EngagementDeath != null).Select(r => r.EngagementDeath!).ToList(),
            HasSpike      = runs.Any(r => r.HadSpike),
            HasEscalator  = runs.Any(r => r.HadEscalator),
            EngagementBand = engagementBand,
        };
    }
}
