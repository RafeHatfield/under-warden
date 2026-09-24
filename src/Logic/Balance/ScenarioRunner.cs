using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Top-level orchestrator: loads entities.yaml + scenario YAML files,
/// creates the harness, and runs scenarios. This is the entry point
/// for file-based balance testing.
/// </summary>
public sealed class ScenarioRunner
{
    private readonly ScenarioHarness _harness;
    private readonly ContentLoader _loader;

    /// <summary>
    /// Create a runner from a content bundle (pre-loaded entities).
    /// </summary>
    public ScenarioRunner(ContentBundle content)
    {
        _loader = new ContentLoader();
        var entityFactory = new EntityFactory();

        var itemFactory = new ItemFactory(content.Items, entityFactory);
        var spellItemFactory = new SpellItemFactory(content.SpellItems, entityFactory);
        _harness = new ScenarioHarness(
            new MonsterFactory(content.Monsters, entityFactory, itemFactory),
            itemFactory,
            new ConsumableFactory(content.Consumables, entityFactory),
            spellItemFactory);
    }

    /// <summary>
    /// Create a runner by loading entities from a YAML file.
    /// </summary>
    public static ScenarioRunner FromEntitiesFile(string entitiesPath)
    {
        var loader = new ContentLoader();
        var content = loader.LoadAllFromFile(entitiesPath);
        return new ScenarioRunner(content);
    }

    /// <summary>
    /// Run a scenario from a YAML file path. Returns aggregated metrics.
    /// When persona is null, defaults to "balanced" (balanced harness behavior).
    /// </summary>
    public AggregatedMetrics RunFromFile(string scenarioPath, int baseSeed = 1337, int? runsOverride = null, BotPersonaConfig? persona = null)
    {
        var scenario = _loader.LoadScenarioFromFile(scenarioPath);
        return _harness.Run(scenario, baseSeed, runsOverride, persona);
    }

    /// <summary>
    /// Run a scenario with explicit run count and turn limit overrides.
    /// Used by SuiteRunner where the matrix defines runs/turn_limit, not the YAML.
    /// </summary>
    public AggregatedMetrics RunFromFileWithOverrides(
        string scenarioPath, int baseSeed, int runsOverride, int turnLimitOverride)
    {
        var scenario = _loader.LoadScenarioFromFile(scenarioPath);
        // Apply the matrix's turn limit and run count, not the YAML's defaults.
        // ScenarioDefinition is a class, so mutate the loaded instance directly
        // (it is a local, not shared with any other caller).
        scenario.TurnLimit = turnLimitOverride;
        scenario.Runs = runsOverride;
        return _harness.Run(scenario, baseSeed, runsOverride);
    }

    /// <summary>
    /// Run a scenario definition directly.
    /// </summary>
    public AggregatedMetrics Run(ScenarioDefinition scenario, int baseSeed = 1337, int? runsOverride = null)
    {
        return _harness.Run(scenario, baseSeed, runsOverride);
    }

    /// <summary>
    /// Run a single iteration of a scenario from file. Returns per-run metrics.
    /// </summary>
    public RunMetrics RunOnceFromFile(string scenarioPath, int seed = 1337)
    {
        var scenario = _loader.LoadScenarioFromFile(scenarioPath);
        return _harness.RunOnce(scenario, seed);
    }
}
