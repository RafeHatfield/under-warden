using System.Text.Json;

namespace UnderWarden.Logic.Balance;

/// <summary>Per-depth snapshot of the balance-relevant soak metrics a tuning change moves.</summary>
public sealed record SoakFloorMetrics(
    int Depth,
    double DeathRate,
    double AvgTurns,
    double AvgKills,
    double AvgHpEndFraction);

/// <summary>
/// A frozen snapshot of a soak's headline + per-floor metrics, persisted to
/// reports/baselines/soak_baseline.json. The next soak diffs against it (SoakBaselineEvaluator) so a
/// tuning change shows its effect against the prior run rather than in a vacuum. Soak analogue of the
/// scenario-suite baseline (reports/baselines/balance_suite_baseline.json).
/// </summary>
public sealed record SoakBaseline(
    double SurvivalRate,
    int Runs,
    IReadOnlyList<SoakFloorMetrics> Floors)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, // matches the soak JSONL + suite baseline
        WriteIndented = true,
    };

    /// <summary>Build a baseline snapshot from a soak summary (per-depth death rate, turns, kills, end-HP).</summary>
    public static SoakBaseline FromSummary(DungeonSoakSummary summary)
    {
        var byDepth = new SortedDictionary<int, List<FloorRunMetrics>>();
        foreach (var run in summary.Runs)
        foreach (var floor in run.PerFloor)
        {
            if (!byDepth.TryGetValue(floor.Depth, out var list))
                byDepth[floor.Depth] = list = new List<FloorRunMetrics>();
            list.Add(floor);
        }

        var floors = new List<SoakFloorMetrics>(byDepth.Count);
        foreach (var (depth, fs) in byDepth)
        {
            double deathRate = fs.Count > 0 ? (double)fs.Count(f => f.PlayerDied) / fs.Count : 0.0;
            double avgTurns  = fs.Count > 0 ? fs.Average(f => f.TurnsTaken) : 0.0;
            double avgKills  = fs.Count > 0 ? fs.Average(f => f.MonstersKilled) : 0.0;
            var hpFloors     = fs.Where(f => f.PlayerMaxHp > 0).ToList();
            double avgHpFrac = hpFloors.Count > 0
                ? hpFloors.Average(f => (double)Math.Max(0, f.PlayerHpAtEnd) / f.PlayerMaxHp)
                : 0.0;
            floors.Add(new SoakFloorMetrics(depth, deathRate, avgTurns, avgKills, avgHpFrac));
        }

        return new SoakBaseline(summary.SurvivalRate, summary.RunsAttempted, floors);
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static SoakBaseline FromJson(string json)
        => JsonSerializer.Deserialize<SoakBaseline>(json, JsonOptions)
           ?? throw new InvalidOperationException("Failed to deserialize SoakBaseline.");

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, ToJson());
    }

    public static SoakBaseline Load(string path) => FromJson(File.ReadAllText(path));
}
