using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Bot heal thresholds — delegates to the "balanced" persona in BotPersonaRegistry.
///
/// Legacy API kept for backward compatibility: these fields were previously `const double`.
/// They are now `static readonly double` computed from the registry so all call-sites
/// continue to compile without change.
///
/// Note: These are `const`-like but not compile-time constants — they cannot be used as
/// default parameter values or attribute arguments. For those use-cases, use
/// BotPersonaRegistry.Defaults["balanced"].BaseHealThreshold directly.
/// </summary>
public static class BotConfig
{
    public static readonly double HealThreshold  = BotPersonaRegistry.Defaults["balanced"].BaseHealThreshold;
    public static readonly double PanicThreshold = BotPersonaRegistry.Defaults["balanced"].PanicHpThreshold;
}

/// <summary>
/// Runs scenario simulations with tile-based positioning and BotBrain AI.
/// Delegates turn processing to TurnController — the shared engine used by
/// both the harness and the presentation layer.
/// </summary>
public sealed class ScenarioHarness
{
    private readonly MonsterFactory _monsterFactory;
    private readonly ItemFactory? _itemFactory;
    private readonly ConsumableFactory? _consumableFactory;
    private readonly SpellItemFactory? _spellItemFactory;

    public ScenarioHarness(
        MonsterFactory monsterFactory,
        ItemFactory? itemFactory = null,
        ConsumableFactory? consumableFactory = null,
        SpellItemFactory? spellItemFactory = null)
    {
        _monsterFactory = monsterFactory;
        _itemFactory = itemFactory;
        _consumableFactory = consumableFactory;
        _spellItemFactory = spellItemFactory;
    }

    public AggregatedMetrics Run(ScenarioDefinition scenario, int baseSeed = 1337, int? runsOverride = null, BotPersonaConfig? persona = null)
    {
        int runCount = runsOverride ?? scenario.Runs;
        var allRuns = new List<RunMetrics>();
        for (int i = 0; i < runCount; i++)
            // Per-scenario isolated seed: each (scenario, runIdx) pair gets an independent
            // seed derived via SHA-256. Prevents cross-scenario correlation when running a
            // matrix (depth3_orc_brutal and depth3_orc_brutal_fine at run 5 now diverge).
            allRuns.Add(RunOnce(scenario, SeedDerivation.Stable(scenario.ScenarioId, i, baseSeed), persona: persona));
        return AggregatedMetrics.FromRuns(scenario.ScenarioId, baseSeed, allRuns,
            name: scenario.Name, depth: scenario.Depth, isProbe: scenario.IsProbe,
            engagementBand: scenario.EngagementBand);
    }

    /// <summary>
    /// Run a single scenario iteration, returning per-run metrics.
    ///
    /// The optional <paramref name="context"/> is passed through to BotBrain.Decide() unchanged.
    /// ScenarioHarness does not create or consume the recorder — callers that want telemetry
    /// create a BotDecisionContext with their own recorder and pass it here. The public Run()
    /// method does not use telemetry; callers that need it call RunOnce() directly.
    ///
    /// The optional <paramref name="persona"/> selects the bot persona. When null, defaults to
    /// "balanced". YAML player_bot field wins over this parameter for ranged_net_arrow scenarios.
    ///
    /// One BotBrain instance per RunOnce call — so stuck detection (TASK-004) fires correctly
    /// for the duration of this run. The static BotBrain.Decide() wrapper is NOT used here.
    ///
    /// All existing test callers that omit context and persona continue to work unchanged.
    /// </summary>
    public RunMetrics RunOnce(ScenarioDefinition scenario, int seed, BotDecisionContext? context = null, BotPersonaConfig? persona = null)
    {
        var state = GameStateFactory.FromScenario(
            scenario, seed, _monsterFactory, _itemFactory, _consumableFactory, _spellItemFactory);
        // PoC scenario_level_loader ignores state:"aware" on monsters — they activate via
        // item-seeking diversion (ground potion draws sequential aggro). C# harness mode
        // replicates this: monsters passive until attacked (Hp < MaxHp proxy).
        // Exception: state:"aware" in the scenario YAML pre-sets AlertedState so the monster
        // acts from turn 1 — needed for ranged scenarios where denial shots deal no damage.
        state.IsHarnessMode = true;

        // Wire state:"aware" → AlertedState pre-set. Monsters are created in YAML order;
        // we expand count>1 groups so index correspondence holds.
        int monsterIdx = 0;
        var player0 = state.Player;
        foreach (var monsterDef in scenario.Monsters)
        {
            for (int i = 0; i < monsterDef.Count; i++, monsterIdx++)
            {
                if (monsterIdx >= state.Monsters.Count) break;
                if (monsterDef.State == "aware")
                {
                    var alert = state.Monsters[monsterIdx].GetOrAdd<AlertedState>();
                    alert.LastKnownPlayerX = player0.X;
                    alert.LastKnownPlayerY = player0.Y;
                    alert.TurnsUntilDeaggro = AlertedState.DeaggroTurns;
                }
            }
        }

        var metrics = new RunMetrics();
        var player = state.Player;
        var playerFighter = state.PlayerFighter;
        var inventory = state.PlayerInventory;

        // 0c per-death lever capture: same EngagementTracker that the dungeon soak uses.
        // In a controlled scenario DistinctAttackers = the actual composition, not bot-pulled chaos.
        var tracker = new EngagementTracker(player.Id, state);
        metrics.HadSpike = tracker.SpikePresent;
        metrics.HadEscalator = tracker.EscalatorPresent;

        // Capture initial HP values for hits-based (TtkHits/TtdHits) and rounds-based (RoundsToKill/RoundsToDie) calculations
        metrics.PlayerMaxHp = playerFighter.MaxHp;
        if (state.Monsters.Count > 0)
        {
            var monsterHps = state.Monsters
                .Select(m => m.Get<Fighter>()?.MaxHp ?? 0)
                .Where(hp => hp > 0)
                .ToList();
            if (monsterHps.Count > 0)
                metrics.MonsterAvgMaxHp = monsterHps.Average();
        }

        // Construct one BotBrain instance per run so stuck detection fires correctly.
        // YAML player_bot wins over CLI persona for ranged_net_arrow scenarios (3-way dispatch).
        var resolvedPersona = persona ?? BotPersonaRegistry.Get("balanced");
        var botBrain = new BotBrain(resolvedPersona);

        while (!state.IsGameOver)
        {
            // 3-way bot dispatch:
            //   1. player_bot == "ranged_net_arrow" → RangedNetArrowBot (YAML wins)
            //   2. player_bot == named persona → BotBrain with that persona
            //   3. null/unknown player_bot → BotBrain with CLI/default persona
            PlayerAction playerAction;
            if (scenario.Player.PlayerBot == "ranged_net_arrow")
            {
                playerAction = RangedNetArrowBot.Decide(player, playerFighter, inventory, state.Monsters, state.Map, state);
            }
            else
            {
                // Use instance botBrain so stuck detection state persists across turns
                var botAction = botBrain.Decide(player, playerFighter, inventory, state.Monsters, state.Map, context);

                // AbortRun: intercept before ToPlayerAction — count as death-equivalent
                if (botAction.Type == BotAction.ActionType.AbortRun)
                {
                    metrics.PlayerDied = true;
                    metrics.WasAborted = true;
                    break;
                }

                playerAction = BotBrain.ToPlayerAction(botAction);
            }
            var result = TurnController.ProcessTurn(state, playerAction);
            metrics.RecordTurn(result, player.Id);
            tracker.IngestTurn(result.Events, metrics.TurnsTaken, state);
        }

        if (!metrics.WasAborted)
            metrics.PlayerDied = !playerFighter.IsAlive;

        // Build per-death lever record on any real death (not stuck-abort, not survival).
        if (metrics.PlayerDied && !metrics.WasAborted)
            metrics.EngagementDeath = tracker.BuildDeathRecord(scenario.Depth, metrics.KillerId, metrics.TurnsTaken, state);

        return metrics;
    }
}
