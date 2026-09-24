using System.Text.Json;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Core;

/// <summary>
/// Builds an in-scene candidate-review floor — the sibling of <see cref="ArtAcceptanceSceneBuilder"/>,
/// on the same authored-data → production-renderer path (SetupPresentation →
/// DungeonRenderer.Render), so a review candidate is judged exactly as it renders in the game, seated
/// among approved/canon neighbours on themed floor (review protocol, art-lint-spec Part B, 2026-08).
///
/// The round is authored data read from a JSON spec (so no recompile per round). Candidates enter via
/// temporary in-place pixel writes to their tile IDs in the worktree (render → capture → revert);
/// nothing lands. This class only lays the tiles out — the pixels a tile ID resolves to are whatever
/// is on disk at capture time.
///
/// JSON: { "width": int, "height": int, "playerX": int, "playerY": int,
///         "props": [ { "tileId": int, "x": int, "y": int, "blocks": bool } ... ] }
/// </summary>
public static class ReviewSceneBuilder
{
    public static GameState Build(string roundJsonPath)
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(roundJsonPath));
        var root = doc.RootElement;
        int w = root.GetProperty("width").GetInt32();
        int h = root.GetProperty("height").GetInt32();
        int px = root.GetProperty("playerX").GetInt32();
        int py = root.GetProperty("playerY").GetInt32();

        var map = new GameMap(w, h, allWalls: true);
        // Carve a full interior room (1-tile wall border), themed sandstone (only theme defined).
        for (int x = 1; x < w - 1; x++)
            for (int y = 1; y < h - 1; y++)
                map.SetTile(x, y, TileKind.Floor);
        map.SetTileThemeRect(0, 0, w - 1, h - 1, TileTheme.Grey);

        var player = new Entity(0, "Player", px, py, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
                               accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        player.Add(new SpeedBonusTracker(baseRatio: 0.25));
        map.RegisterEntity(player);

        var props = new List<PlacedProp>();
        foreach (var p in root.GetProperty("props").EnumerateArray())
        {
            int tileId = p.GetProperty("tileId").GetInt32();
            int x = p.GetProperty("x").GetInt32();
            int y = p.GetProperty("y").GetInt32();
            bool blocks = !p.TryGetProperty("blocks", out var b) || b.GetBoolean();
            props.Add(new PlacedProp($"review_{tileId}", x, y, 1, 1, blocks, tileId));
            if (blocks) map.MarkPropCell(x, y);
        }

        map.RevealAll();

        var state = new GameState(player, new List<Entity>(), map, new SeededRandom(0), turnLimit: 1)
        {
            IsDungeonMode = true,
            CurrentDepth = 1,
            Props = props,
        };
        return state;
    }
}
