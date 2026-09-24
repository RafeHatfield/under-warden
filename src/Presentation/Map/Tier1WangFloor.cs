using System.Text.Json;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;
using Godot;

namespace UnderWarden.Presentation.Map;

/// <summary>
/// Lays the EDGE-MATCHED floor family. Floor session two, task 1.
///
/// `TileThemeConfig.PickVariant` chooses a tile from a position hash, which cannot express edge
/// matching: it has no way to make cell (x,y)'s eastern edge agree with cell (x+1,y)'s western
/// one. So the selection happens here, after the renderer has placed its sprites, and each floor
/// sprite's texture is replaced with the tile whose four edge families match the four boundaries
/// that cell actually has.
///
/// THE EDGE OWNS ITS FAMILY, NOT THE TILE — which is what removes the need for a solver.
/// Every boundary in the map is assigned a family by hashing its OWN coordinates, so two
/// neighbours reading the same boundary get the same answer by construction:
///
///     N = H(x, y)      S = H(x, y+1)      W = V(x, y)      E = V(x+1, y)
///
/// No scan order, no dead ends, deterministic from the map alone.
///
/// ⚠ THE HASH EXISTS TWICE — here and in `tools/tier1_floors/compose_wang.py`, which drew the
/// tiles. That is the copy-that-drifts hazard this project has already been bitten by, and it is
/// tolerated ONLY because the two are checked against each other: the manifest carries a
/// cross-check vector of sample (x, y, salt) -> family values, and <see cref="Apply"/> refuses to
/// lay the floor if this code does not reproduce every one of them. A duplicate with an
/// enforcement is a different thing from a duplicate with a comment (LOOP-PROCESS §4.2).
/// </summary>
public static class Tier1WangFloor
{
    private sealed class Config
    {
        public int Families = 3;
        public int Seed;
        public int HorizSalt = 101, VertSalt = 202;
        public readonly Dictionary<int, string> BasePath = new();      // tile index -> path
        public readonly Dictionary<int, string> ChannelPath = new();
        public readonly List<(int X, int Y, int Salt, int Family)> Check = new();
    }

    /// <summary>The hash the composer used, reproduced. Verified against the manifest's vector.</summary>
    private static int Mix(int x, int y, int salt)
    {
        unchecked
        {
            int h = x * 7919 + y * 104729 + salt * 15485863;
            h ^= h >> 13;
            h *= 1274126177;
            h ^= h >> 16;
            return h & 0x7FFFFFFF;
        }
    }

    private static int EdgeFamily(int a, int b, int salt, int seed, int families)
        => Mix(a, b, salt + seed) % families;

    private static int TileIndex(int n, int e, int s, int w, int f)
        => ((n * f + e) * f + s) * f + w;

    /// <summary>
    /// Replace every floor sprite's texture with its edge-matched tile. Returns a log line.
    /// `isChannel` selects the worn rendering — the SAME bond drawn smoother, so a channel cell
    /// still matches its ordinary neighbours and the enclosure survives the transition.
    /// </summary>
    public static string Apply(TileLayer tileLayer, GameMap map, string manifestResPath,
                               System.Func<int, int, bool> isChannel)
    {
        var cfg = Load(manifestResPath, out string status);
        if (cfg == null) return $"[Tier1] wang floor: NOT APPLIED — {status}";

        // The cross-check, BEFORE anything is laid. If this code and the code that drew the tiles
        // disagree about a single boundary, every joint in the scene is one pixel from meeting
        // its neighbour and nothing downstream would say so.
        foreach (var (x, y, salt, expect) in cfg.Check)
        {
            int got = EdgeFamily(x, y, salt, cfg.Seed, cfg.Families);
            if (got != expect)
                return $"[Tier1] wang floor: REFUSED — edge-family cross-check failed at "
                     + $"({x},{y}) salt={salt}: composer said {expect}, engine says {got}. The "
                     + $"tiles were drawn by a different hash than the one laying them.";
        }

        int laid = 0, channel = 0, missing = 0;
        foreach (var (pos, node) in tileLayer.TileSprites)
        {
            if (!map.IsWalkable(pos.X, pos.Y)) continue;
            if (node is not Sprite2D s) continue;

            int n = EdgeFamily(pos.X, pos.Y, cfg.HorizSalt, cfg.Seed, cfg.Families);
            int so = EdgeFamily(pos.X, pos.Y + 1, cfg.HorizSalt, cfg.Seed, cfg.Families);
            int w = EdgeFamily(pos.X, pos.Y, cfg.VertSalt, cfg.Seed, cfg.Families);
            int e = EdgeFamily(pos.X + 1, pos.Y, cfg.VertSalt, cfg.Seed, cfg.Families);

            int idx = TileIndex(n, e, so, w, cfg.Families);
            bool worn = isChannel != null && isChannel(pos.X, pos.Y);
            var table = worn ? cfg.ChannelPath : cfg.BasePath;
            if (!table.TryGetValue(idx, out var path)) { missing++; continue; }
            var tex = GD.Load<Texture2D>(path);
            if (tex == null) { missing++; continue; }

            // NO FLIP, NO ROTATION. On an edge-matched tile the orientation IS the meaning —
            // turning one relabels its four edges and it stops agreeing with its neighbours. The
            // variety that flipping bought on the session-one family is bought here by the 81
            // combinations instead.
            s.Texture = tex;
            s.FlipH = false;
            s.FlipV = false;
            laid++;
            if (worn) channel++;
        }

        return $"[Tier1] wang floor: laid={laid} channel={channel} missing={missing} "
             + $"families={cfg.Families} seed={cfg.Seed} cross_check={cfg.Check.Count}/OK "
             + $"manifest={manifestResPath}";
    }

    private static Config? Load(string manifestResPath, out string status)
    {
        status = "";
        try
        {
            using var f = Godot.FileAccess.Open(manifestResPath, Godot.FileAccess.ModeFlags.Read);
            if (f == null) { status = $"manifest not found: {manifestResPath}"; return null; }
            using var doc = JsonDocument.Parse(f.GetAsText());
            var root = doc.RootElement;
            string dir = manifestResPath[..(manifestResPath.LastIndexOf('/') + 1)];

            var cfg = new Config
            {
                Families = root.GetProperty("families").GetInt32(),
                Seed = root.GetProperty("seed").GetInt32(),
            };
            var salts = root.GetProperty("salts");
            cfg.HorizSalt = salts.GetProperty("horizontal").GetInt32();
            cfg.VertSalt = salts.GetProperty("vertical").GetInt32();

            int f4 = cfg.Families;
            void Read(string key, Dictionary<int, string> into)
            {
                foreach (var e in root.GetProperty(key).EnumerateArray())
                {
                    int idx = TileIndex(e.GetProperty("n").GetInt32(), e.GetProperty("e").GetInt32(),
                                        e.GetProperty("s").GetInt32(), e.GetProperty("w").GetInt32(),
                                        f4);
                    into[idx] = dir + e.GetProperty("file").GetString();
                }
            }
            Read("base", cfg.BasePath);
            Read("channel", cfg.ChannelPath);

            foreach (var e in root.GetProperty("edge_family_check").EnumerateArray())
                cfg.Check.Add((e.GetProperty("x").GetInt32(), e.GetProperty("y").GetInt32(),
                               e.GetProperty("salt").GetInt32(), e.GetProperty("family").GetInt32()));

            status = "ok";
            return cfg;
        }
        catch (System.Exception ex)
        {
            status = $"manifest unreadable: {ex.Message}";
            return null;
        }
    }
}
