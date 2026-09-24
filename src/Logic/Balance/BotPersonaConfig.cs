namespace UnderWarden.Logic.Balance;

/// <summary>
/// Immutable configuration for a bot persona. Defines all thresholds and priorities
/// that control BotBrain's decision-making for a specific playstyle.
///
/// Canonical source of truth: Python PoC bot_brain.py, two config dicts:
///   - PERSONAS (lines 80-127): combat engagement, loot, explore preferences
///   - PERSONA_HEAL_CONFIG (lines 186-217): heal thresholds, panic conditions
///
/// Design note: The PoC has a deprecated duplication between BotPersonaConfig.potion_hp_threshold
/// and PersonaHealConfig.base_heal_threshold. C# collapses these into one field per persona
/// (BaseHealThreshold) to avoid carrying forward the PoC's vestigial duplication.
///
/// Distance note: CombatEngagementDistance uses Manhattan distance (abs(dx) + abs(dy)),
/// matching the PoC's semantics. The rest of BotBrain uses Chebyshev distance for adjacency
/// and weapon-range checks. This asymmetry is intentional — the engagement-distance values
/// (4, 5, 6, 8, 12) were tuned against Manhattan in the PoC and must not be normalized.
/// </summary>
public sealed record BotPersonaConfig(
    /// <summary>Human-readable persona name. Used as dictionary key and in telemetry.</summary>
    string Name,

    /// <summary>
    /// HP fraction below which the bot considers retreating to a choke point.
    /// Range: 0.0–1.0. PoC: retreat_hp_threshold.
    /// </summary>
    double RetreatHpThreshold,

    /// <summary>
    /// HP fraction below which the bot drinks a healing potion (normal threshold).
    /// Range: 0.0–1.0. PoC: PERSONA_HEAL_CONFIG.base_heal_threshold.
    /// </summary>
    double BaseHealThreshold,

    /// <summary>
    /// HP fraction below which the bot panic-heals regardless of other conditions.
    /// Range: 0.0–1.0. PoC: PERSONA_HEAL_CONFIG.panic_threshold.
    /// </summary>
    double PanicHpThreshold,

    /// <summary>
    /// Number of adjacent enemies required to trigger panic heal.
    /// Panic heal fires when HP &lt;= PanicHpThreshold AND adjacent enemies >= this count.
    /// PoC: PERSONA_HEAL_CONFIG.panic_multi_enemy_count (default 2).
    /// </summary>
    int PanicMultiEnemyCount,

    /// <summary>
    /// Maximum Manhattan distance (abs(dx)+abs(dy)) to chase an enemy.
    /// Enemies beyond this distance are ignored — bot waits instead of chasing.
    /// Note: Manhattan distance, NOT Chebyshev. Matches PoC bot_brain.py semantics.
    /// PoC: BotPersonaConfig.combat_engagement_distance.
    /// </summary>
    int CombatEngagementDistance,

    /// <summary>
    /// Loot pickup priority. 0 = ignore all floor loot (aggressive/speedrunner),
    /// 1 = normal (search radius 3 tiles), 2 = greedy (search radius 6 tiles).
    /// PoC: BotPersonaConfig.loot_priority.
    /// </summary>
    int LootPriority,

    /// <summary>
    /// If true, the bot prioritizes reaching the downstair over full floor exploration.
    /// PoC: BotPersonaConfig.prefer_stairs.
    /// </summary>
    bool PreferStairs,

    /// <summary>
    /// If true, the bot actively avoids non-adjacent enemies instead of engaging them.
    /// When true, rule 5 (avoid-combat detour) fires instead of rule 6 (engage).
    /// PoC: BotPersonaConfig.avoid_combat.
    /// </summary>
    bool AvoidCombat,

    /// <summary>
    /// If true, the bot heals during combat (when enemies are adjacent or visible).
    /// All current personas have this enabled. Reserved for a future berserker persona.
    /// PoC: PERSONA_HEAL_CONFIG.allow_combat_healing.
    /// </summary>
    bool AllowCombatHealing,

    /// <summary>
    /// Hard-forced escalator targeting priority for controlled experiments. This is NOT a game
    /// behavior; it is a testing/experiment lever for the escalator-fork measurement. Two cohorts:
    ///   "escalator_first" — always attack an Escalator/Fused monster if one is alive (hard override).
    ///   "escalator_last"  — never attack an Escalator/Fused monster while any non-escalator lives.
    ///   null              — normal behavior (lowest-HP focus fire, unchanged).
    /// The knob must be HARD-FORCED (not a soft preference) so cohort membership is clean:
    /// partial targeting blurs the cohorts and slides the measurement back toward selection bias.
    /// </summary>
    string? EscalatorTargetingPriority = null
);
