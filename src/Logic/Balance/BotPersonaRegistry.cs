namespace UnderWarden.Logic.Balance;

/// <summary>
/// Registry of named bot personas. Provides hardcoded defaults (matching the PoC verbatim)
/// and supports optional override via YAML loading.
///
/// All five personas are defined in the PoC's bot_brain.py:
///   - PERSONAS dict (lines 80-127): combat/loot/explore parameters
///   - PERSONA_HEAL_CONFIG dict (lines 186-217): heal thresholds
///
/// Fallback contract: Get(null) and Get("unknown_name") both return the "balanced" persona.
/// A warning is emitted to stderr when an unknown (non-null) name is requested.
///
/// Thread-safety: Defaults is set once at class initialization and is immutable.
/// LoadFromFile replaces the backing dictionary atomically — safe for single-threaded harness use.
/// </summary>
public static class BotPersonaRegistry
{
    /// <summary>
    /// Hardcoded persona table. PoC-exact values from bot_brain.py.
    /// Initialized at class load time. YAML loading may replace this reference.
    /// </summary>
    public static IReadOnlyDictionary<string, BotPersonaConfig> Defaults { get; private set; }
        = BuildHardcodedDefaults();

    /// <summary>
    /// Retrieve a persona by name. Returns the balanced persona on null or unknown name.
    /// Emits a single stderr warning when name is non-null but unrecognized.
    /// Never throws.
    /// </summary>
    public static BotPersonaConfig Get(string? name)
    {
        if (name is null)
            return Defaults["balanced"];

        if (Defaults.TryGetValue(name, out var persona))
            return persona;

        Console.Error.WriteLine(
            $"[BotPersonaRegistry] Unknown persona '{name}' — falling back to 'balanced'. " +
            $"Valid options: {string.Join(", ", Defaults.Keys)}");
        return Defaults["balanced"];
    }

    /// <summary>
    /// Replace the default persona table from a YAML file loaded by BotPersonaLoader.
    /// If loading fails or the file is missing, the hardcoded table is preserved.
    /// Intended to be called once at harness startup.
    /// </summary>
    public static void LoadFromFile(string path)
    {
        try
        {
            var loaded = Content.BotPersonaLoader.LoadFromFile(path);
            if (loaded.Count > 0)
            {
                Defaults = loaded;
                Console.Error.WriteLine($"[BotPersonaRegistry] Loaded {loaded.Count} personas from {path}");
            }
            else
            {
                Console.Error.WriteLine($"[BotPersonaRegistry] YAML file had no personas — keeping hardcoded defaults.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[BotPersonaRegistry] Failed to load {path}: {ex.Message} — keeping hardcoded defaults.");
        }
    }

    // ── Hardcoded defaults (PoC-exact values) ─────────────────────────────────

    private static IReadOnlyDictionary<string, BotPersonaConfig> BuildHardcodedDefaults()
    {
        var personas = new[]
        {
            // PoC bot_brain.py PERSONAS["balanced"] + PERSONA_HEAL_CONFIG["balanced"]
            new BotPersonaConfig(
                Name:                     "balanced",
                RetreatHpThreshold:       0.25,
                BaseHealThreshold:        0.30,
                PanicHpThreshold:         0.15,
                PanicMultiEnemyCount:     2,
                CombatEngagementDistance: 8,
                LootPriority:             1,
                PreferStairs:             false,
                AvoidCombat:              false,
                AllowCombatHealing:       true),

            // PoC bot_brain.py PERSONAS["cautious"] + PERSONA_HEAL_CONFIG["cautious"]
            new BotPersonaConfig(
                Name:                     "cautious",
                RetreatHpThreshold:       0.40,
                BaseHealThreshold:        0.50,
                PanicHpThreshold:         0.30,
                PanicMultiEnemyCount:     2,
                CombatEngagementDistance: 5,
                LootPriority:             1,
                PreferStairs:             false,
                AvoidCombat:              true,
                AllowCombatHealing:       true),

            // PoC bot_brain.py PERSONAS["aggressive"] + PERSONA_HEAL_CONFIG["aggressive"]
            new BotPersonaConfig(
                Name:                     "aggressive",
                RetreatHpThreshold:       0.10,
                BaseHealThreshold:        0.20,
                PanicHpThreshold:         0.10,
                PanicMultiEnemyCount:     3,
                CombatEngagementDistance: 12,
                LootPriority:             0,
                PreferStairs:             false,
                AvoidCombat:              false,
                AllowCombatHealing:       true),

            // PoC bot_brain.py PERSONAS["greedy"] + PERSONA_HEAL_CONFIG["greedy"]
            new BotPersonaConfig(
                Name:                     "greedy",
                RetreatHpThreshold:       0.25,
                BaseHealThreshold:        0.30,
                PanicHpThreshold:         0.15,
                PanicMultiEnemyCount:     2,
                CombatEngagementDistance: 6,
                LootPriority:             2,
                PreferStairs:             false,
                AvoidCombat:              false,
                AllowCombatHealing:       true),

            // PoC bot_brain.py PERSONAS["speedrunner"] + PERSONA_HEAL_CONFIG["speedrunner"]
            new BotPersonaConfig(
                Name:                     "speedrunner",
                RetreatHpThreshold:       0.30,
                BaseHealThreshold:        0.40,
                PanicHpThreshold:         0.20,
                PanicMultiEnemyCount:     2,
                CombatEngagementDistance: 4,
                LootPriority:             0,
                PreferStairs:             true,
                AvoidCombat:              true,
                AllowCombatHealing:       true),

            // ── Escalator-fork experiment cohorts (hard-forced targeting, not game personas) ──
            // Uses balanced stats; only EscalatorTargetingPriority changes.
            // Run via --persona escalator_first / escalator_last on escalator scenarios.
            new BotPersonaConfig(
                Name:                        "escalator_first",
                RetreatHpThreshold:          0.25,
                BaseHealThreshold:           0.25,
                PanicHpThreshold:            0.15,
                PanicMultiEnemyCount:        2,
                CombatEngagementDistance:    6,
                LootPriority:                1,
                PreferStairs:                false,
                AvoidCombat:                 false,
                AllowCombatHealing:          true,
                EscalatorTargetingPriority:  "escalator_first"),

            new BotPersonaConfig(
                Name:                        "escalator_last",
                RetreatHpThreshold:          0.25,
                BaseHealThreshold:           0.25,
                PanicHpThreshold:            0.15,
                PanicMultiEnemyCount:        2,
                CombatEngagementDistance:    6,
                LootPriority:                1,
                PreferStairs:                false,
                AvoidCombat:                 false,
                AllowCombatHealing:          true,
                EscalatorTargetingPriority:  "escalator_last"),
        };

        return personas.ToDictionary(p => p.Name);
    }
}
