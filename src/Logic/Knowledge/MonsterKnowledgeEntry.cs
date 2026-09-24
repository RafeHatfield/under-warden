namespace UnderWarden.Logic.Knowledge;

/// <summary>
/// Knowledge tier the player has reached for a given monster species.
/// Higher tiers unlock more information in MonsterInfoView.
/// Thresholds match balance/knowledge_config.py exactly.
/// </summary>
public enum KnowledgeTier
{
    /// <summary>Never seen this species.</summary>
    Unknown = 0,

    /// <summary>Seen at least once — unlocks faction, role, coarse speed.</summary>
    Observed = 1,

    /// <summary>Engaged in combat 3+ times — unlocks durability, damage, accuracy, evasion.</summary>
    Battled = 2,

    /// <summary>Killed 5+ times (or experienced a major trait) — unlocks warnings and advice.</summary>
    Understood = 3,
}

/// <summary>
/// Tracks player experience with a single monster species.
/// Tier is computed from accumulated counts — no separate tier field to avoid drift.
/// </summary>
public sealed class MonsterKnowledgeEntry
{
    // Traits that unlock Tier 3 (Understood) immediately when discovered,
    // regardless of kill count. Matches MAJOR_TRAITS in knowledge_config.py.
    private static readonly HashSet<string> MajorTraits = new() { "plague_carrier", "swarm_ai" };

    public int SeenCount { get; set; }
    public int EngagedCount { get; set; }
    public int KilledCount { get; set; }

    /// <summary>Trait names discovered through direct experience (e.g., "plague_carrier", "swarm_ai").</summary>
    public HashSet<string> TraitsDiscovered { get; } = new();

    /// <summary>
    /// Current knowledge tier. Computed from accumulated counts each access — no separate state to sync.
    ///
    /// Tier precedence (highest wins):
    ///   Understood: killed >= 5 OR has a major trait
    ///   Battled:    engaged >= 3
    ///   Observed:   seen >= 1
    ///   Unknown:    never seen
    /// </summary>
    public KnowledgeTier Tier
    {
        get
        {
            if (SeenCount == 0)
                return KnowledgeTier.Unknown;

            // Tier 3 can be reached via kills OR major trait experience (PoC: TIER_3_TRAIT_EXPERIENCE_UNLOCKS = True)
            if (KilledCount >= 5 || TraitsDiscovered.Any(t => MajorTraits.Contains(t)))
                return KnowledgeTier.Understood;

            if (EngagedCount >= 3)
                return KnowledgeTier.Battled;

            return KnowledgeTier.Observed;
        }
    }
}
