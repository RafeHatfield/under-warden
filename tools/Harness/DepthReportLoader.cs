using System.Text.Json;
using UnderWarden.Logic.Balance;

/// <summary>
/// Loads DepthCurvePoints from a Phase 1 suite output directory or summary.json.
///
/// Accepts:
///   - A directory containing metrics/raw/*.json (AggregatedMetrics JSON files)
///   - A single summary.json file (NormalizedMetrics per scenario)
/// </summary>
public static class DepthReportLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Load curve points from a directory (reads metrics/raw/*.json) or a file (summary.json).
    /// Returns sorted by Depth.
    /// </summary>
    public static IReadOnlyList<DepthPressureReport.DepthCurvePoint> Load(string path)
    {
        if (Directory.Exists(path))
        {
            // Check for metrics/raw subdirectory first
            string rawDir = Path.Combine(path, "metrics", "raw");
            if (Directory.Exists(rawDir))
                return LoadFromRawDir(rawDir);

            // Also check for summary.json in the directory
            string summaryPath = Path.Combine(path, "summary.json");
            if (File.Exists(summaryPath))
                return LoadFromSummaryJson(summaryPath);

            return LoadFromRawDir(path);
        }

        if (File.Exists(path))
            return LoadFromSummaryJson(path);

        throw new FileNotFoundException($"Depth report input not found: '{path}'");
    }

    /// <summary>
    /// Load from metrics/raw/*.json files (each is a full AggregatedMetrics JSON).
    /// </summary>
    private static IReadOnlyList<DepthPressureReport.DepthCurvePoint> LoadFromRawDir(string dir)
    {
        var points = new List<DepthPressureReport.DepthCurvePoint>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var m    = JsonSerializer.Deserialize<AggregatedMetrics>(json, JsonOptions);
                if (m == null || m.Depth == 0) continue;
                points.Add(DepthPressureReport.FromAggregated(m));
            }
            catch { /* skip malformed files */ }
        }
        return points.OrderBy(p => p.Depth).ThenBy(p => p.ScenarioId).ToList();
    }

    /// <summary>
    /// Load from summary.json (NormalizedMetrics per scenario_id).
    /// Since NormalizedMetrics doesn't carry all fields needed for pressure model,
    /// this path produces a limited view (pressure model fields not available).
    /// Prefer the raw directory path when full analysis is needed.
    /// </summary>
    private static IReadOnlyList<DepthPressureReport.DepthCurvePoint> LoadFromSummaryJson(string path)
    {
        // summary.json is keyed by scenario_id → NormalizedMetrics
        // NormalizedMetrics doesn't have RoundsToKill/RoundsToDie/DPR directly — but we can still
        // provide the subset we have.
        var json = File.ReadAllText(path);
        var raw  = JsonSerializer.Deserialize<Dictionary<string, SummaryEntry>>(json, JsonOptions);
        if (raw == null) return Array.Empty<DepthPressureReport.DepthCurvePoint>();

        // Without full AggregatedMetrics we can only provide the normalized fields.
        // Return minimal curve points with available data.
        var points = raw.Values
            .Where(e => e.Runs > 0)
            .Select(e => new DepthPressureReport.DepthCurvePoint(
                Depth:           0,   // depth not available in summary.json
                ScenarioId:      e.ScenarioId ?? "unknown",
                RoundsToKill:            0,
                RoundsToDie:            0,
                DPR_P:           0,
                DPR_M:           0,
                PlayerHitRate:   e.PlayerHitRate,
                MonsterHitRate:  e.MonsterHitRate,
                DmgPerEncounter: 0,
                TurnsPerKill:    0,
                DeathRate:       e.DeathRate))
            .ToList();

        return points;
    }

    // Minimal deserialization target for summary.json entries
    private sealed class SummaryEntry
    {
        public string? ScenarioId     { get; set; }
        public int     Runs           { get; set; }
        public int     Deaths         { get; set; }
        public double  DeathRate      { get; set; }
        public double  PlayerHitRate  { get; set; }
        public double  MonsterHitRate { get; set; }
        public double  PressureIndex  { get; set; }
        public double  BonusAttacksPerRun { get; set; }
    }
}
