using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;
using Godot;

namespace UnderWarden.Presentation.Map;

/// <summary>
/// COUNTS WHAT WAS ACTUALLY DRAWN. Floor session two, precondition 1.
///
/// Session one found `TileThemeConfig.PositionHash` to be LINEAR, so variant choice was periodic
/// along every straight line and a 24-variant family was delivering 3 — the same tile every third
/// cell down the diagonal. The fix was verified by arithmetic (a hand-computed additive order) and
/// by a blind seat's pixel correlation. **Neither is a standing check**, and LOOP-PROCESS §4.2's
/// question was left unanswered: what goes red if it silently comes back?
///
/// This is the answer. It reads the TEXTURE OF EVERY SPRITE THE RENDERER PLACED — not the theme,
/// not the hash, not a re-implementation of the picker — so it censuses the end of the pipeline
/// and cannot agree with a broken picker by sharing its logic.
///
/// WHAT IT REPORTS, and why each number is here:
///
///   cells / distinct      the headline. For N ids drawn uniformly across C cells the expected
///                         distinct count is N(1-(1-1/N)^C); the line prints both, so "24 of 96"
///                         is read against arithmetic rather than against a feeling.
///   max_multiplicity      how many times the most-repeated variant landed. A linear hash pins
///                         this high while `distinct` can still look plausible.
///   step_top2 per axis    THE TEST THAT ACTUALLY CATCHES IT, and it is scale-free. For each
///                         direction, take the variant ID at every cell and at its neighbour one
///                         step along, and histogram the raw difference. **A LINEAR HASH MAKES
///                         THAT DIFFERENCE CONSTANT** — h(p+step) - h(p) is the same for every p
///                         by definition — so after `% N` it takes at most TWO values, d and
///                         d-N, and the two commonest buckets cover everything: 1.000. A mixed
///                         hash spreads it and the figure collapses toward chance. **No pool
///                         size is needed**, which is what lets one statistic cover every N.
///
///                         ⚠ THIS REPLACES A `repeat@3` PROBE THAT ITS OWN CONTROL EXPOSED AS
///                         BLIND. Distance three is the period a linear hash produces on a
///                         24-id pool (step 112648 mod 24 = 16, additive order 3). The pool is
///                         now 96, where the same hash has period 12 — so the probe reported
///                         0.000 on the planted defect and would have certified it as fixed.
///                         The defect is LINEARITY; the period is a symptom whose value depends
///                         on the pool. Testing the symptom meant testing one pool size.
///
/// It renders no verdict. §13.2 gives verdicts to the eye and this is a floor, not a gate — but
/// unlike the ring instrument it is not measuring taste at all, only whether a hash is doing the
/// job its own docstring claims.
/// </summary>
public static class FloorVariantCensus
{
    /// <summary>One line for the capture log, plus the axis-resolved repeat table.</summary>
    public static string Describe(TileLayer tileLayer, GameMap map)
    {
        var byCell = new Dictionary<(int X, int Y), string>();
        foreach (var (pos, node) in tileLayer.TileSprites)
        {
            if (!map.IsWalkable(pos.X, pos.Y)) continue;          // floors only; walls are mocks
            if (node is not Sprite2D s || s.Texture == null) continue;
            var path = s.Texture.ResourcePath;
            if (string.IsNullOrEmpty(path)) continue;
            byCell[pos] = path;
        }
        if (byCell.Count == 0)
            return "[Tier1] variant census: NO FLOOR SPRITES — nothing to count";

        var counts = new Dictionary<string, int>();
        foreach (var p in byCell.Values)
            counts[p] = counts.TryGetValue(p, out var n) ? n + 1 : 1;

        int cells = byCell.Count;
        int distinct = counts.Count;
        int maxMult = 0;
        foreach (var n in counts.Values) maxMult = System.Math.Max(maxMult, n);

        var sb = new System.Text.StringBuilder();
        sb.Append($"[Tier1] variant census: cells={cells} distinct={distinct} ")
          .Append($"max_multiplicity={maxMult}");

        // Variant id per cell, parsed from the filename's trailing integer.
        //
        // ⚠ AN EARLIER VERSION INDEXED THE *OBSERVED* SET — the distinct paths that happened to
        // appear — and that silently broke the test. `PickVariant` computes `hash % N` over the
        // theme's FULL list, so under a linear hash the id difference along a step is constant in
        // the real id space; compressed onto the 50-odd ids that showed up, it is not. The
        // control measured the planted defect at 0.43 instead of 1.00 and correctly refused to
        // pass. Reading the real id removes the compression.
        //
        // NO POOL SIZE IS NEEDED, which is what makes this scale-free. Under `hash % N` with a
        // linear hash, the raw difference `id(q) - id(p)` takes at most TWO values — d, and d-N
        // where the index wrapped. So the fraction of steps falling in the two most common
        // buckets is 1.000 under linearity and small otherwise, at any N, without the census
        // having to know N.
        var id = new Dictionary<string, int>();
        bool haveIds = true;
        foreach (var path in counts.Keys)
        {
            int e = path.LastIndexOf('.');
            int b = e;
            while (b > 0 && char.IsDigit(path[b - 1])) b--;
            if (b >= e) { haveIds = false; break; }
            id[path] = int.Parse(path[b..e]);
        }
        if (!haveIds)
        {
            var order = new List<string>(counts.Keys);
            order.Sort(System.StringComparer.Ordinal);
            id.Clear();
            for (int i = 0; i < order.Count; i++) id[order[i]] = i;
        }

        (string Name, int Dx, int Dy)[] axes =
        {
            ("row", 1, 0), ("col", 0, 1), ("diag", 1, 1), ("anti", 1, -1),
        };
        sb.Append($"  step_top2{(haveIds ? "" : "(ordinal-fallback)")}:");
        double worst = 0;
        foreach (var (name, dx, dy) in axes)
        {
            var hist = new Dictionary<int, int>();
            int pairs = 0;
            foreach (var (pos, path) in byCell)
            {
                var q = (X: pos.X + dx, Y: pos.Y + dy);
                if (!byCell.TryGetValue(q, out var other)) continue;
                int d = id[other] - id[path];
                hist[d] = hist.TryGetValue(d, out var c) ? c + 1 : 1;
                pairs++;
            }
            var top = new List<int>(hist.Values);
            top.Sort();
            top.Reverse();
            int cover = 0;
            for (int i = 0; i < top.Count && i < 2; i++) cover += top[i];
            double f = pairs == 0 ? -1 : (double)cover / pairs;
            if (f > worst) worst = f;
            sb.Append($" {name}={(pairs == 0 ? "n/a" : f.ToString("0.000"))}({cover}/{pairs})");
        }
        sb.Append($"  worst_top2={worst:0.000}");

        // The expectation, so the measured distinct count is read against arithmetic rather than
        // against an impression. Pool size is inferred from the theme's own spread: the census
        // cannot know N, but it can report what N would have to be for this result to be typical.
        double expected = ExpectedDistinct(distinct, cells);
        sb.Append($"  chance_mode~{(distinct > 0 ? (1.0 / distinct).ToString("0.000") : "n/a")}");
        sb.Append($"  pool_implied>={expected:0}");
        return sb.ToString();
    }

    /// <summary>
    /// The smallest pool N for which drawing `cells` times would typically yield at least
    /// `distinct` distinct values. Reported so a reader can see whether the observed spread is
    /// consistent with the family's real size or with a fraction of it.
    /// </summary>
    private static double ExpectedDistinct(int distinct, int cells)
    {
        for (int n = distinct; n <= 4096; n++)
        {
            double e = n * (1.0 - System.Math.Pow(1.0 - 1.0 / n, cells));
            if (e >= distinct) return n;
        }
        return distinct;
    }
}
