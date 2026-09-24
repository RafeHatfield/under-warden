using System.Collections.Generic;

namespace UnderWarden.Logic.ECS;

/// <summary>
/// A ROUTE, AS A LINE.
///
/// RULED (Rafe, 2026-08-29) on round 21's diagnosis: <b>a per-tile field cannot draw a line.</b>
/// Every wear lever built so far was keyed to a per-tile scalar, and a route is a path-scale
/// object. Measured on the review scene, the travel axis derived from that scalar agreed between
/// neighbouring tiles only <b>34% of the time</b> — it flipped two times in three, so a grain
/// keyed to it could never accumulate into the continuous line a viewer actually follows.
///
/// So the route becomes what it always was: a polyline. This is the crack network's shape applied
/// to traffic — a field-scale object drawn ACROSS tiles rather than inside them, which is the one
/// system class blind seats have consistently praised on this floor.
///
/// WORN, NOT BUILT. The polyline is KEYING, NEVER PAVING. Nothing is drawn along it; the existing
/// age layer — joint compaction, flattening, chroma, polish, hollows — simply re-keys to distance
/// from it and to its tangent, and therefore concentrates along it. No new visual treatment
/// appears in this class or because of it.
///
/// COHERENT BY CONSTRUCTION, and that is the whole point. Distance to a polyline and the tangent
/// of the nearest segment are pure functions of a WORLD position, so two tiles sharing a stone
/// across their boundary compute the identical answer for the identical pixel. The corner theorem
/// is satisfied without a keying table, because there is nothing per-tile left to disagree about.
/// </summary>
public static class RoutePolyline
{
    /// <summary>One route: a smoothed line in TILE coordinates, and how heavily it is walked.</summary>
    public sealed record Line(IReadOnlyList<(double X, double Y)> Points, double Weight);

    /// <summary>How far from the line the wear reaches, in tiles. Beyond this a route says nothing.</summary>
    public const double Reach = 1.35;

    /// <summary>
    /// How far along the line the tangent is taken, in points either side of the nearest segment.
    ///
    /// ⚠ NOT ONE SEGMENT. A single segment of a jittered, corner-cut line is a few hundredths of a
    /// tile long and its direction is almost entirely the jitter — a dead-straight north-south
    /// route came back as diagonals all the way down. The tangent has to be read over a span long
    /// enough to be the ROUTE rather than the wobble, which is also what a walker's eye follows:
    /// nobody reads the next four pixels of a path, they read where it is going.
    /// </summary>
    public const int TangentSpan = 6;

    /// <summary>
    /// Corner-cutting, twice — a walked line has walking curvature, not a staircase of grid moves.
    ///
    /// Chaikin rather than a spline because it stays inside the convex hull of the original path:
    /// a smoothed route cannot wander into a wall the A* path avoided, which a spline can.
    /// </summary>
    public static List<(double X, double Y)> Smooth(IReadOnlyList<(int X, int Y)> path, int passes = 2)
    {
        var pts = new List<(double X, double Y)>();
        foreach (var p in path) pts.Add((p.X, p.Y));
        for (int k = 0; k < passes && pts.Count > 2; k++)
        {
            var next = new List<(double X, double Y)> { pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                next.Add((0.75 * a.X + 0.25 * b.X, 0.75 * a.Y + 0.25 * b.Y));
                next.Add((0.25 * a.X + 0.75 * b.X, 0.25 * a.Y + 0.75 * b.Y));
            }
            next.Add(pts[^1]);
            pts = next;
        }
        return pts;
    }

    /// <summary>
    /// Push each point sideways by a deterministic amount, then refuse any push that leaves
    /// walkable ground.
    ///
    /// DISCOVERED, NOT DRAWN. A route that runs down the exact centre of every corridor is a route
    /// somebody surveyed, and §8.1's register does not have a surveyor in it. The jitter is
    /// perpendicular to the local tangent, keyed on world position so it is the same every run,
    /// and bounded well inside a tile so the line wanders without ever leaving the way it walks.
    /// </summary>
    public static List<(double X, double Y)> Jitter(List<(double X, double Y)> pts,
                                                    System.Func<int, int, bool> walkable,
                                                    int seed, double amp = 0.28)
    {
        var outp = new List<(double X, double Y)>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            var (px, py) = pts[i];
            var a = pts[System.Math.Max(i - 1, 0)];
            var b = pts[System.Math.Min(i + 1, pts.Count - 1)];
            double tx = b.X - a.X, ty = b.Y - a.Y;
            double len = System.Math.Sqrt(tx * tx + ty * ty);
            if (len < 1e-9) { outp.Add((px, py)); continue; }
            double nx = -ty / len, ny = tx / len;
            int hsh = Hash((int)System.Math.Round(px * 16), (int)System.Math.Round(py * 16), seed);
            double d = ((hsh % 1000) / 1000.0 - 0.5) * 2.0 * amp;
            double qx = px + nx * d, qy = py + ny * d;
            outp.Add(walkable((int)System.Math.Floor(qx + 0.5), (int)System.Math.Floor(qy + 0.5))
                     ? (qx, qy) : (px, py));
        }
        return outp;
    }

    /// <summary>Distance in TILES from a world position to the nearest point of any line, plus that line's weight and tangent.</summary>
    public static (double Dist, double Weight, double Tx, double Ty) Nearest(
        IReadOnlyList<Line> lines, double x, double y)
    {
        double best = double.MaxValue, bw = 0, btx = 1, bty = 0;
        foreach (var line in lines)
        {
            var p = line.Points;
            for (int i = 0; i + 1 < p.Count; i++)
            {
                double ax = p[i].X, ay = p[i].Y, bx = p[i + 1].X, by = p[i + 1].Y;
                double dx = bx - ax, dy = by - ay;
                double L2 = dx * dx + dy * dy;
                double t = L2 < 1e-12 ? 0.0
                    : System.Math.Clamp(((x - ax) * dx + (y - ay) * dy) / L2, 0.0, 1.0);
                double cx = ax + t * dx, cy = ay + t * dy;
                double d = System.Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d < best)
                {
                    var lo = p[System.Math.Max(i - TangentSpan, 0)];
                    var hi = p[System.Math.Min(i + 1 + TangentSpan, p.Count - 1)];
                    best = d; bw = line.Weight;
                    btx = hi.X - lo.X; bty = hi.Y - lo.Y;
                }
            }
        }
        return (best, bw, btx, bty);
    }

    /// <summary>
    /// The wear strength at a world position: full on the line, gone by <see cref="Reach"/>.
    ///
    /// Smoothstep rather than linear so the lane has soft shoulders instead of a drawn edge —
    /// a worn strip fades out, it does not stop.
    /// </summary>
    public static double Strength(IReadOnlyList<Line> lines, double x, double y)
    {
        if (lines.Count == 0) return 0.0;
        var (d, w, _, _) = Nearest(lines, x, y);
        if (d >= Reach) return 0.0;
        double u = 1.0 - d / Reach;
        return w * u * u * (3.0 - 2.0 * u);
    }

    /// <summary>The travel axis at a world position: 0 = E-W, 1 = NE-SW, 2 = N-S, 3 = NW-SE, -1 = off every route.</summary>
    public static int Axis(IReadOnlyList<Line> lines, double x, double y)
    {
        if (lines.Count == 0) return -1;
        var (d, _, tx, ty) = Nearest(lines, x, y);
        if (d >= Reach) return -1;
        double ang = System.Math.Atan2(ty, tx) * 180.0 / System.Math.PI;
        ang = ((ang % 180.0) + 180.0) % 180.0;
        return ((int)System.Math.Round(ang / 45.0)) % 4;
    }

    private static int Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            return System.Math.Abs(h ^ (h >> 16));
        }
    }
}
