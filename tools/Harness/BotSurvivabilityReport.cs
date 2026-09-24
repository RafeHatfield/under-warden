using System.Text;
using UnderWarden.Logic.Balance;

/// <summary>
/// Generates a 5×N bot persona survivability matrix.
///
/// For each (persona × scenario) pair: runs the scenario with that persona,
/// collects death rate, avg turns, TtkHits, TtdHits.
///
/// Output: markdown table with personas as columns and scenarios as rows.
/// Label: "Death Rate" per plan minor note M1 (not "Survival Rate").
///
/// Seeding: each (persona, scenario, run_idx) uses the same SeedDerivation.Stable base
/// so encounter layouts match across personas. Combat RNG diverges from turn 1 as personas
/// take different actions and consume different RNG draws. N=50 washes out stochastic variance.
/// </summary>
public static class BotSurvivabilityReport
{
    private static readonly string[] PersonaOrder = ["balanced", "cautious", "aggressive", "greedy", "speedrunner"];

    /// <summary>
    /// Run the matrix and return a formatted markdown string.
    /// </summary>
    public static string Generate(
        ScenarioRunner runner,
        string levelsDir,
        bool useFastMatrix,
        int runsPerScenario,
        int baseSeed)
    {
        var matrix = useFastMatrix ? SuiteRunner.FastMatrix : SuiteRunner.Matrix;
        var results = RunMatrix(runner, levelsDir, matrix, runsPerScenario, baseSeed);
        return FormatMarkdown(results, matrix, runsPerScenario, baseSeed, useFastMatrix);
    }

    // ── Matrix execution ──────────────────────────────────────────────────────

    private static Dictionary<(string Persona, string ScenarioId), AggregatedMetrics?> RunMatrix(
        ScenarioRunner runner,
        string levelsDir,
        IReadOnlyList<SuiteRunner.SuiteEntry> matrix,
        int runsPerScenario,
        int baseSeed)
    {
        var results = new Dictionary<(string, string), AggregatedMetrics?>();

        foreach (var persona in PersonaOrder)
        {
            var personaConfig = BotPersonaRegistry.Get(persona);
            Console.Error.WriteLine($"  Persona: {persona}");

            foreach (var entry in matrix)
            {
                Console.Error.Write($"    {entry.ScenarioId} ({runsPerScenario} runs)... ");

                var scenarioPath = FindScenarioPath(levelsDir, entry.ScenarioId);
                if (scenarioPath == null)
                {
                    Console.Error.WriteLine("NOT FOUND");
                    results[(persona, entry.ScenarioId)] = null;
                    continue;
                }

                try
                {
                    var metrics = runner.RunFromFile(scenarioPath, baseSeed, runsPerScenario, personaConfig);
                    results[(persona, entry.ScenarioId)] = metrics;
                    Console.Error.WriteLine($"death={metrics.DeathRate:P0}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"ERROR: {ex.Message}");
                    results[(persona, entry.ScenarioId)] = null;
                }
            }
        }

        return results;
    }

    // ── Markdown output ────────────────────────────────────────────────────────

    private static string FormatMarkdown(
        Dictionary<(string Persona, string ScenarioId), AggregatedMetrics?> results,
        IReadOnlyList<SuiteRunner.SuiteEntry> matrix,
        int runsPerScenario,
        int baseSeed,
        bool useFastMatrix)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Bot Persona Survivability Report");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Matrix:** {(useFastMatrix ? "fast" : "full")} ({matrix.Count} scenarios)");
        sb.AppendLine($"**Runs per cell:** {runsPerScenario}");
        sb.AppendLine($"**Base seed:** {baseSeed}");
        sb.AppendLine();
        sb.AppendLine("Seeding note: Personas share the same encounter layout (same seed → same spawn positions,");
        sb.AppendLine("same initial inventory, same map). Combat RNG streams diverge from turn 1 onward as");
        sb.AppendLine("different personas take different actions and consume different RNG draws.");
        sb.AppendLine();

        // ── Death Rate table ──
        sb.AppendLine("## Death Rate");
        sb.AppendLine();
        AppendTable(sb, matrix, results, (m) => m != null ? $"{m.DeathRate:P0}" : "N/A");

        // ── Avg turns table ──
        sb.AppendLine();
        sb.AppendLine("## Average Turns to Clear");
        sb.AppendLine();
        AppendTable(sb, matrix, results, (m) => m != null ? $"{m.AvgTurns:F1}" : "N/A");

        // ── TtkHits table ──
        sb.AppendLine();
        sb.AppendLine("## TtkHits (Hits to Kill Monster)");
        sb.AppendLine();
        AppendTable(sb, matrix, results, (m) => m != null ? $"{m.TtkHits:F1}" : "N/A");

        // ── TtdHits table ──
        sb.AppendLine();
        sb.AppendLine("## TtdHits (Monster Hits to Kill Player)");
        sb.AppendLine();
        AppendTable(sb, matrix, results, (m) => m != null ? $"{m.TtdHits:F1}" : "N/A");

        // ── Observations ──
        sb.AppendLine();
        sb.AppendLine("## Observations");
        sb.AppendLine();
        AppendObservations(sb, matrix, results);

        return sb.ToString();
    }

    private static void AppendTable(
        StringBuilder sb,
        IReadOnlyList<SuiteRunner.SuiteEntry> matrix,
        Dictionary<(string Persona, string ScenarioId), AggregatedMetrics?> results,
        Func<AggregatedMetrics?, string> valueSelector)
    {
        // Header
        sb.Append($"| {"Scenario",-40} |");
        foreach (var persona in PersonaOrder)
            sb.Append($" {persona,12} |");
        sb.AppendLine();

        // Separator
        sb.Append($"|{new string('-', 42)}|");
        foreach (var _ in PersonaOrder)
            sb.Append($"{new string('-', 14)}|");
        sb.AppendLine();

        // Data rows
        foreach (var entry in matrix)
        {
            sb.Append($"| {entry.ScenarioId,-40} |");
            foreach (var persona in PersonaOrder)
            {
                results.TryGetValue((persona, entry.ScenarioId), out var metrics);
                string cell = valueSelector(metrics);
                sb.Append($" {cell,12} |");
            }
            sb.AppendLine();
        }
    }

    private static void AppendObservations(
        StringBuilder sb,
        IReadOnlyList<SuiteRunner.SuiteEntry> matrix,
        Dictionary<(string Persona, string ScenarioId), AggregatedMetrics?> results)
    {
        // Sanity check: cautious vs aggressive on each scenario
        int cautiousWins = 0;
        int scenariosCompared = 0;

        foreach (var entry in matrix)
        {
            results.TryGetValue(("cautious", entry.ScenarioId), out var cautiousMetrics);
            results.TryGetValue(("aggressive", entry.ScenarioId), out var aggressiveMetrics);

            if (cautiousMetrics == null || aggressiveMetrics == null) continue;
            scenariosCompared++;
            if (cautiousMetrics.DeathRate <= aggressiveMetrics.DeathRate)
                cautiousWins++;
        }

        sb.AppendLine($"- Cautious persona death rate <= Aggressive on {cautiousWins}/{scenariosCompared} scenarios");
        if (cautiousWins < scenariosCompared / 2)
        {
            sb.AppendLine("  **WARNING**: Cautious is NOT more survivable than Aggressive on the majority of scenarios.");
            sb.AppendLine("  This may indicate the avoid_combat mechanic is ineffective in arena scenarios.");
        }

        // Find highest and lowest death rates
        var balancedRates = matrix
            .Where(e => results.TryGetValue(("balanced", e.ScenarioId), out var m) && m != null)
            .Select(e => (ScenarioId: e.ScenarioId, DeathRate: results[("balanced", e.ScenarioId)]!.DeathRate))
            .OrderByDescending(x => x.DeathRate)
            .ToList();

        if (balancedRates.Count > 0)
        {
            sb.AppendLine($"- Hardest scenario for balanced persona: {balancedRates[0].ScenarioId} ({balancedRates[0].DeathRate:P0} death rate)");
            sb.AppendLine($"- Easiest scenario for balanced persona: {balancedRates[^1].ScenarioId} ({balancedRates[^1].DeathRate:P0} death rate)");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? FindScenarioPath(string levelsDir, string scenarioId)
    {
        var direct = Path.Combine(levelsDir, $"scenario_{scenarioId}.yaml");
        if (File.Exists(direct)) return direct;
        var alt = Path.Combine(levelsDir, $"{scenarioId}.yaml");
        if (File.Exists(alt)) return alt;
        return null;
    }
}
