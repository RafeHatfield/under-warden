namespace UnderWarden.Logic.Balance;

/// <summary>
/// Per-decision telemetry record captured by BotBrain on each call to Decide().
/// Value type — readonly record struct — so that recording 10,000 decisions per soak run
/// adds no heap pressure beyond the backing List storage.
///
/// Port of PoC's BotDecisionTelemetry (io_layer/bot_metrics.py), trimmed to fields
/// that C# BotBrain actually knows at decision time.
/// </summary>
public readonly record struct BotDecisionRecord
{
    /// <summary>Game turn number when this decision was made.</summary>
    public int TurnNumber { get; init; }

    /// <summary>Dungeon depth (1-based) at which this decision was made.</summary>
    public int FloorDepth { get; init; }

    /// <summary>
    /// Action type string. Stable values used as dictionary keys in analysis:
    /// "Attack", "Heal", "MoveToward", "MoveTo", "Wait", "Descend", "NavigateToStair"
    /// </summary>
    public string ActionType { get; init; }

    /// <summary>
    /// Human-readable reason string. Stable values — changing these breaks downstream
    /// JSONL parsing. Avoid renaming without a migration plan.
    /// Values: "panic_heal", "threshold_heal", "retreat_to_choke", "attack_lowest_hp",
    ///         "move_to_nearest", "no_targets", "navigate_stair"
    /// </summary>
    public string Reason { get; init; }

    /// <summary>Player HP / MaxHP at decision time (0.0 to 1.0).</summary>
    public double HpFraction { get; init; }

    /// <summary>Count of alive monsters. Proxy for "visible" since scenarios use full visibility.</summary>
    public int VisibleEnemies { get; init; }

    /// <summary>Count of alive monsters within Chebyshev distance 1 of the player.</summary>
    public int AdjacentEnemies { get; init; }

    /// <summary>Count of healing potions in player inventory at decision time.</summary>
    public int HealingPotionsAvailable { get; init; }

    /// <summary>True if at least one enemy is adjacent (Chebyshev distance <= 1).</summary>
    public bool InCombat { get; init; }

    /// <summary>True if HpFraction is at or below the persona's BaseHealThreshold.</summary>
    public bool LowHp { get; init; }

    /// <summary>
    /// Persona name active when this decision was made (e.g. "balanced", "aggressive").
    /// Defaults to "balanced" when missing from JSONL files (forward-compatible deserialization).
    /// </summary>
    public string Persona { get; init; }
}

/// <summary>
/// Carries telemetry context from the harness into BotBrain.Decide() without
/// bloating the Decide() parameter list. A value type (readonly record struct)
/// so it passes by copy — no allocation, no sharing concerns.
///
/// Persona defaults to "balanced" when not provided — backward-compatible with
/// existing callers that construct BotDecisionContext without the persona field.
/// </summary>
public readonly record struct BotDecisionContext(
    IBotTelemetryRecorder Recorder,
    int TurnNumber,
    int FloorDepth,
    string Persona = "balanced"
);

/// <summary>
/// Receives BotDecisionRecord instances during a run and produces a BotRunSummary.
/// The interface allows the harness to inject telemetry collection without BotBrain
/// depending on a concrete recorder class.
/// </summary>
public interface IBotTelemetryRecorder
{
    /// <summary>Record one decision. Called once per BotBrain.Decide() invocation when telemetry is enabled.</summary>
    void Record(BotDecisionRecord decision);

    /// <summary>Compute aggregate statistics from all recorded decisions.</summary>
    BotRunSummary Summarize();

    /// <summary>All decisions recorded so far, in order.</summary>
    IReadOnlyList<BotDecisionRecord> Decisions { get; }
}

/// <summary>
/// In-memory implementation of IBotTelemetryRecorder.
/// Thread-safety not required — the bot loop is single-threaded.
///
/// Null-recorder pattern: passing null for IBotTelemetryRecorder means "disabled".
/// Creating a recorder instance means "enabled". No enabled/disabled flag needed.
/// </summary>
public sealed class BotTelemetryRecorder : IBotTelemetryRecorder
{
    private readonly List<BotDecisionRecord> _decisions = new();

    public IReadOnlyList<BotDecisionRecord> Decisions => _decisions;

    public void Record(BotDecisionRecord decision)
        => _decisions.Add(decision);

    public BotRunSummary Summarize()
        => BotRunSummary.ComputeFrom(_decisions);
}

/// <summary>
/// Aggregate bot behavior statistics computed from one run's decision records.
/// Produced by BotTelemetryRecorder.Summarize() at the end of a run.
///
/// Serializable via System.Text.Json. Dictionary keys (ActionCounts, etc.) use
/// the original string values ("Attack", "in_combat") — not snake_case — because
/// they are data values that analysis tools key on.
///
/// Port of PoC's BotRunSummary (io_layer/bot_metrics.py).
/// </summary>
public sealed class BotRunSummary
{
    /// <summary>
    /// Persona name used for this run (e.g. "balanced", "aggressive").
    /// Computed from the first decision's Persona field. Defaults to "balanced" for
    /// empty decision lists and for JSONL files missing the persona field.
    /// </summary>
    public string Persona { get; init; } = "balanced";

    /// <summary>Total decisions recorded this run.</summary>
    public int TotalDecisions { get; init; }

    /// <summary>Number of distinct floor depths visited.</summary>
    public int FloorsVisited { get; init; }

    /// <summary>Decision count per ActionType string (e.g. "Attack" → 45).</summary>
    public Dictionary<string, int> ActionCounts { get; init; } = new();

    /// <summary>Decision count per Reason string (e.g. "attack_lowest_hp" → 45).</summary>
    public Dictionary<string, int> ReasonCounts { get; init; } = new();

    /// <summary>
    /// Decision count per context tag. Tags can overlap per decision:
    ///   "in_combat" — InCombat == true
    ///   "exploring" — InCombat == false and ActionType != "Descend"
    ///   "low_hp"    — LowHp == true
    ///   "floor_clear" — VisibleEnemies == 0
    /// </summary>
    public Dictionary<string, int> ContextCounts { get; init; } = new();

    /// <summary>
    /// Average HpFraction across all Heal decisions.
    /// 0.0 if there were no heal decisions (guard against division-by-zero).
    /// </summary>
    public double AvgHpWhenHealing { get; init; }

    /// <summary>Count of Heal actions recorded this run.</summary>
    public int HealDecisions { get; init; }

    /// <summary>
    /// 1 if the bot died while still holding unused healing potions (last decision had
    /// HealingPotionsAvailable > 0 and the run ended in death). 0 otherwise.
    /// Per-run value — aggregate across runs by summing.
    /// </summary>
    public int DeathsWithUnusedPotions { get; init; }

    /// <summary>
    /// Compute BotRunSummary from a list of decision records.
    /// Called once per run by BotTelemetryRecorder.Summarize().
    /// </summary>
    public static BotRunSummary ComputeFrom(IReadOnlyList<BotDecisionRecord> decisions)
    {
        if (decisions.Count == 0)
        {
            return new BotRunSummary
            {
                Persona        = "balanced",
                TotalDecisions = 0,
                FloorsVisited  = 0,
                ActionCounts   = new Dictionary<string, int>(),
                ReasonCounts   = new Dictionary<string, int>(),
                ContextCounts  = new Dictionary<string, int>(),
                AvgHpWhenHealing        = 0.0,
                HealDecisions           = 0,
                DeathsWithUnusedPotions = 0,
            };
        }

        var actionCounts  = new Dictionary<string, int>();
        var reasonCounts  = new Dictionary<string, int>();
        var contextCounts = new Dictionary<string, int>();

        int healDecisions   = 0;
        double healHpSum    = 0.0;
        var floorsVisited   = new HashSet<int>();

        foreach (var d in decisions)
        {
            floorsVisited.Add(d.FloorDepth);

            // Action counts
            actionCounts.TryGetValue(d.ActionType, out int ac);
            actionCounts[d.ActionType] = ac + 1;

            // Reason counts
            reasonCounts.TryGetValue(d.Reason, out int rc);
            reasonCounts[d.Reason] = rc + 1;

            // Context counts — tags can overlap for a single decision
            if (d.InCombat)
            {
                contextCounts.TryGetValue("in_combat", out int c);
                contextCounts["in_combat"] = c + 1;
            }
            if (!d.InCombat && d.ActionType != "Descend")
            {
                contextCounts.TryGetValue("exploring", out int c);
                contextCounts["exploring"] = c + 1;
            }
            if (d.LowHp)
            {
                contextCounts.TryGetValue("low_hp", out int c);
                contextCounts["low_hp"] = c + 1;
            }
            if (d.VisibleEnemies == 0)
            {
                contextCounts.TryGetValue("floor_clear", out int c);
                contextCounts["floor_clear"] = c + 1;
            }

            // Heal stats
            if (d.ActionType == "Heal")
            {
                healDecisions++;
                healHpSum += d.HpFraction;
            }
        }

        double avgHpWhenHealing = healDecisions > 0 ? healHpSum / healDecisions : 0.0;

        // Deaths with unused potions: last decision had potions available.
        // The caller (harness) sets this based on outcome; we compute it from the decision list:
        // if the last recorded decision has HealingPotionsAvailable > 0, the bot had potions
        // when the run ended. Whether the run ended in death is context the recorder doesn't
        // have — so we record the raw value here and the harness can override if needed.
        // Per plan: "if the run ended in death and HealingPotionsAvailable > 0 on the last decision"
        // The harness sets DeathsWithUnusedPotions post-summarize for correctness; here we just
        // compute whether the last decision had potions available.
        var lastDecision = decisions[^1];
        int deathsWithUnusedPotions = lastDecision.HealingPotionsAvailable > 0 ? 1 : 0;

        // Persona: derived from first decision's Persona field (all decisions in a run share persona)
        string persona = decisions.Count > 0 ? decisions[0].Persona : "balanced";

        return new BotRunSummary
        {
            Persona                 = persona,
            TotalDecisions          = decisions.Count,
            FloorsVisited           = floorsVisited.Count,
            ActionCounts            = actionCounts,
            ReasonCounts            = reasonCounts,
            ContextCounts           = contextCounts,
            AvgHpWhenHealing        = avgHpWhenHealing,
            HealDecisions           = healDecisions,
            DeathsWithUnusedPotions = deathsWithUnusedPotions,
        };
    }
}
