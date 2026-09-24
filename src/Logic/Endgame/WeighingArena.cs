using System;
using System.Collections.Generic;
using System.Linq;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Endgame;

/// <summary>
/// A loaded Weighing arena: the fixed-layout map plus the named anchor positions the orchestration
/// needs (player start, per-Guardian spawn tiles, the Debt's tile, the ally fall-back zone).
///
/// The Weighing arena is hand-authored, not procedural (design decision: the courtroom is a ritual
/// constant). This is the engine capability; the actual layout is content authored against it.
/// </summary>
public sealed class WeighingArena
{
    public GameMap Map { get; }
    private readonly Dictionary<string, List<(int X, int Y)>> _anchors;

    public WeighingArena(GameMap map, Dictionary<string, List<(int X, int Y)>> anchors)
    {
        Map = map;
        _anchors = anchors;
    }

    /// <summary>All tiles tagged with the given anchor name (empty if none).</summary>
    public IReadOnlyList<(int X, int Y)> AnchorsFor(string name) =>
        _anchors.TryGetValue(name, out var list) ? list : Array.Empty<(int X, int Y)>();

    /// <summary>The first tile for an anchor (e.g. the unique player start), or null if absent.</summary>
    public (int X, int Y)? FirstAnchor(string name) =>
        _anchors.TryGetValue(name, out var list) && list.Count > 0 ? list[0] : null;

    public IReadOnlyDictionary<string, List<(int X, int Y)>> Anchors => _anchors;
}

/// <summary>
/// Builds a <see cref="WeighingArena"/> from an ASCII grid. '#' is wall; every other char is floor.
/// Anchor chars (per the legend) are floor AND record their position under a named key, so the
/// orchestration can place the player, raise each Guardian, and stage the allies-fall-back beat on
/// known ground. Layout-agnostic: the same loader serves any authored grid (and any future template).
/// </summary>
public static class WeighingArenaLoader
{
    public const char Wall = '#';

    // Standard anchor legend for the Weighing. One arena uses these; the orchestration reads by name.
    public static readonly IReadOnlyDictionary<char, string> DefaultAnchorLegend = new Dictionary<char, string>
    {
        ['P'] = "player_start",
        ['W'] = "guardian_warden",      // Warden-of-Wardens
        ['O'] = "guardian_oathkeeper",  // Oathkeeper
        ['A'] = "guardian_assembly",    // Assembly of the Lost
        ['U'] = "guardian_auditor",     // Auditor's Own
        ['B'] = "debt",                 // The Debt (faced alone)
        ['F'] = "ally_fallback",        // where allies withdraw to before the Debt rises
        ['J'] = "under_warden",         // the Under-Warden presides here (narrative; never fights)
    };

    public static WeighingArena FromAscii(
        IReadOnlyList<string> rows,
        IReadOnlyDictionary<char, string>? anchorLegend = null)
    {
        if (rows == null || rows.Count == 0)
            throw new ArgumentException("Arena grid must have at least one row.", nameof(rows));

        anchorLegend ??= DefaultAnchorLegend;
        int height = rows.Count;
        int width = rows.Max(r => r.Length);

        var map = new GameMap(width, height, allWalls: true);
        var anchors = new Dictionary<string, List<(int X, int Y)>>();

        for (int y = 0; y < height; y++)
        {
            string row = rows[y];
            for (int x = 0; x < row.Length; x++)
            {
                char c = row[x];
                if (c == Wall)
                {
                    map.SetTile(x, y, TileKind.Wall);
                    continue;
                }

                // '.', anchors, and any other non-wall char carve floor.
                map.SetTile(x, y, TileKind.Floor);

                if (anchorLegend.TryGetValue(c, out var name))
                {
                    if (!anchors.TryGetValue(name, out var list))
                        anchors[name] = list = new List<(int X, int Y)>();
                    list.Add((x, y));
                }
            }
            // Cells past the end of a short row remain wall (from allWalls), padding to rectangle.
        }

        return new WeighingArena(map, anchors);
    }
}
