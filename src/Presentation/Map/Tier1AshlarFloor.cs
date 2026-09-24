using System.Text.Json;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;
using Godot;

namespace UnderWarden.Presentation.Map;

/// <summary>
/// Lays the COURSE-ALIGNED ASHLAR floor and paints its stones.
///
/// This does more than pick a texture, and the reason is a ruling. Assigning a stone's value from
/// anything the TILE knows puts that value on the tile lattice — the same value repeating wherever
/// the family pattern repeats — which is §8.3.1 arriving through value instead of through shape.
/// So the shipped asset is the BOND ONLY, and the material is painted here from each stone's
/// WORLD ADDRESS:
///
///     a stone spanning a vertical boundary is addressed by THAT BOUNDARY, which is the one piece
///     of data both tiles either side of it possess. Both compute the same key, so both paint the
///     same value onto the same stone, and the seam is zero rather than small.
///
/// Measured on the assembled field: 7.44x boundary-to-interior value step under the old geometry,
/// 2.95x after blending it, and 0.59x here — below 1.00, meaning a tile boundary is no longer
/// distinguishable from anywhere else on the floor.
///
/// WHY NO STONE MAY CONTAIN A GRID CORNER, which is what forces the coursing.
/// Four tiles meet at a grid corner. Tile (x,r) shares one boundary family with its eastern
/// neighbour and one with its southern, and shares NOTHING with its diagonal. So a stone covering
/// a corner cannot be addressed at all. A bed joint on every tile boundary puts one through every
/// corner and the problem does not arise. Measured: 0 unaddressable stones, against 27 of 77
/// (19.9% of stone pixels) under the crossing-joint geometry that preceded it.
///
/// The INTERIOR bed joint is not so constrained, and it moves per tile row. When it did not, every
/// course in the world was 16px tall and a blind seat read the floor as "a stack of horizontal
/// stripes before it reads as stone". Four course splits, chosen by row, give 5 distinct course
/// heights across a field where there was 1.
///
/// WHAT SHIPS
///   * 81 atlases, one per family combination, each a 6x6 of the four course splits by the nine
///     head-joint merge cases.
///     R is an INDEX INTO THE LADDER (exact — a byte of luminance would round, and a rounding
///     error lands as a value seam). G is the stone class, 0 for joints.
///   * one 512x512 grain bank, 64 patches, two scales packed into R and G.
///
/// ⚠ THE COMPOSER'S ARITHMETIC EXISTS TWICE, here and in `tools/tier1_floors/compose_ashlar.py`.
/// That is the copy-that-drifts hazard this project has been bitten by, and it is tolerated only
/// because BOTH HALVES ARE CHECKED: the manifest carries an edge-family vector and a stone-offset
/// vector, and <see cref="Apply"/> refuses to lay anything if this code fails to reproduce either.
/// A duplicate with an enforcement is a different thing from a duplicate with a comment
/// (LOOP-PROCESS §4.2).
/// </summary>
public static class Tier1AshlarFloor
{
    private const int T = 32;
    private const int Courses = 2;
    private const int AtlasCols = 6;
    private const int GrainSide = 2 * T;      // one patch
    private const int BankCols = 8;

    private sealed class Config
    {
        public int Families = 3;
        public int Seed;
        public int HorizSalt = 101, VertSalt = 202, SpanSalt = 3001, InteriorSalt = 3002;
        public int DropSalt = 3003, ClusterSalt = 3004, SplitSalt = 3005;
        public int[][] Splits = System.Array.Empty<int[]>();
        public int GrainBank = 64;
        public double GrainAmp = 1.0, Coarse = 0.34, Fine = 0.14, WornMul = 0.38;
        public double WearSpread = 0.20, WearArris = 0.45, LumMedian = 114.0;
        public int CrackSalt = 3006, CrackRate = 7, CrackMinTiles = 3, CrackMaxTiles = 7;
        public int CrackScale = 1024, CrackTurn = 5;
        public double CrackDepth = 0.42;
        public int[][] CrackDirs = System.Array.Empty<int[]>();
        public int MarksSalt = 3007, MarkMinLen = 5, MarkMaxLen = 10;
        public double MarkDepth = 1.0, PitDepth = 1.0;
        public int MarkBands = 5, MarkPits = 3, WearBands = 3, WearPits = 1;
        public int WearSalt = 3008, ChipSalt = 3009, WearLo = 70, WearHi = 200, ChannelWear = 235;
        public double ChipRate = 0.55, DressingKeep = 0.45;
        public int JointBreakSalt = 3010;
        public double OcclusionFloor = 22.0;
        public readonly List<(string Side, int Px, int Py, int Layers, int Rungs)> OcclusionCheck = new();
        public double[] JointFill = { 0.0, 0.0, 1.0, 2.0 };
        public double[] JointBreak = { 0.0, 0.0, 0.20, 0.45 };
        public int[][] WearOctaves = System.Array.Empty<int[]>();
        public double[] WearAges = { 0.0, 0.34, 0.67, 1.0 };
        public int[][] MarkDirs = System.Array.Empty<int[]>();
        public int[] OffsetSteps = System.Array.Empty<int>();
        public int[] ClusterTable = { -1, 0, 0, 1 };
        public double[] Ladder = System.Array.Empty<double>();
        public double[] Tint = { 1, 1, 1 };
        public double[] ChromaByAge = { 0, 0, 0, 0 };
        public double[] ChromaDir = { 0, 0, 0 };
        public double[] PolishByAge = { 0, 0, 0, 0 };
        public double PolishExp = 2.0, PolishGain = 1.0;
        // The highlight shoulder (RULED Rafe 2026-09-07). Defaults are the identity-safe pair;
        // the manifest is the authority, as it is for every other lever here.
        public double ShoulderKnee = 0.75, ShoulderCeiling = 0.92;
        // #198: the specular scales with the fragment's own value, normalised by the family's
        // median albedo. `SpecShade = 0` is the flat additive term it replaces, exactly.
        public double SpecShade = 1.0, AlbedoMedian = 0.446;
        public double[] DeformFlatten = { 0, 0, 0, 0 };
        public double DeformAniso = 0.8;
        public double HollowDepth = 1.3, HollowRim = 0.45;
        public int HollowSalt = 3011;
        public int StriaSalt = 3012, LaneDishSalt = 3013, GritSalt = 3014, ShelterSalt = 3015;
        public double[] ShelterLift = { 5, 4, 3, 0 };
        public double[] ShelterWeights = { 0.15, 0.50, 0.20, 0.15 };
        public int ShelterBlock = 8;
        public int CrackVarySalt = 3017;
        public double CrackDepthVary = 0.18;
        public double MarkBareShare = 0.34;
        public bool ChipTakesJoint = true, DishQuantise = true, CrackSpall = true;
        public double CrackLip = 0.55;
        public int CrackLipSalt = 3018, MarkClusterSalt = 3019;
        public int MarkClusterPeriod = 5;
        public double MarkClusterSwing = 0.30;
        public int LaneFraySalt = 3016;
        public double LaneFray = 0.32;
        public double JointPolishFloor = 0.70;

        /// <summary>
        /// How much of its shine a fully-occluded contact pixel keeps (#184). 1.0 is the NULL
        /// CONTROL — the identity, and the build before this term existed. 0.0 says the deepest
        /// row of §12.1's plane boundary takes no specular at all; the ramp between comes from
        /// the occlusion sprite's own alpha, so the transition is the boundary's shape and not a
        /// threshold. Declared in the manifest; this default is the null so a family that does
        /// not declare it is unchanged.
        /// </summary>
        public double OcclusionPolishFloor = 1.0;
        public double PolishLaneGain = 1.9, PolishLaneWidth = 0.62, PolishShoulder = 1.15;
        public int StriaPeriod = 3;
        public double StriaDepth = 0.45;
        public double LaneDishDepth = 0.85, LaneDishRim = 0.30;
        public double GritInner = 0.70, GritOuter = 1.90, GritRate = 0.13, GritDepth = 1.25;
        public System.Collections.Generic.IReadOnlyList<Logic.ECS.RoutePolyline.Line> Lines =
            new System.Collections.Generic.List<Logic.ECS.RoutePolyline.Line>();
        public int[][] ATable = System.Array.Empty<int[]>();
        public int[][] MvTable = System.Array.Empty<int[]>();
        public readonly Dictionary<int, string> Atlas = new();
        public string GrainPath = "";
        public readonly List<(int X, int Y, int Salt, int Family)> EdgeCheck = new();
        public readonly List<(int X, int K, int Kind, int Drop, int Steps)> StoneCheck = new();
        public readonly List<(int X, int Y, int Px, int Py, int R, int G, int B)> PaintCheck = new();
        public int[] PaintCheckWornColumns = System.Array.Empty<int>();
        // THE CHECK'S OWN TRAFFIC FIELD. The real one is derived from the level graph and
        // the composer has no map, so the finished-pixel check carries a SYNTHETIC field
        // that both sides agree on. What it verifies is the PAINTING given a field; the
        // derivation is verified by the Logic layer's own tests, where a hierarchy can be
        // asserted without a scene, a device or a capture.
        public byte[,]? CheckTraffic;
        public System.Collections.Generic.List<Logic.ECS.RoutePolyline.Line> CheckLines = new();
    }

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

    /// <summary>0 keep both head joints, 1 sand the A joint away, 2 sand the MV joint away.</summary>
    private static int DropChoice(Config c, int tx, int courseK)
    {
        int d = Mix(tx, courseK, c.DropSalt + c.Seed) % 7;
        return d == 0 ? 1 : (d == 1 ? 2 : 0);
    }

    /// <summary>
    /// With the MV joint gone, the stone labelled `interior` is not interior any more: it runs on
    /// into the tile to the east and must be addressed by THAT boundary, or the two tiles paint it
    /// differently. This one line is the difference between a merge and a seam.
    /// </summary>
    private static int Address(int kind, int drop) => (kind == 1 && drop == 2) ? 2 : kind;

    /// <summary>
    /// Which course split this TILE ROW uses.
    ///
    /// The joint ON a tile boundary cannot move — the corner theorem needs one through every grid
    /// corner. The INTERIOR one is free, and it moves, because when it did not a blind seat read
    /// the floor as "a stack of horizontal stripes before it reads as stone": one unbroken ruled
    /// line every 16px across the whole map. Chosen per ROW so every tile in a row agrees and the
    /// joint stays continuous across every vertical boundary, while successive rows give courses
    /// of genuinely different heights.
    /// </summary>
    private static int RowSplit(Config c, int r) => Mix(0, r, c.SplitSalt + c.Seed) % c.Splits.Length;

    /// <summary>Stone-local y = 0 for this course under this split.</summary>
    private static int CourseOriginY(Config c, int splitI, int course)
        => course == 0 ? 1 : c.Splits[splitI][0] + 1;

    private static int ClusterBias(Config c, int bx, int courseK)
        => c.ClusterTable[Mix(bx / 3, courseK / 2, c.ClusterSalt + c.Seed) % c.ClusterTable.Length];

    private static int OffsetSteps(Config c, int key, int bias)
    {
        int k = c.OffsetSteps[key % c.OffsetSteps.Length] + bias;
        return System.Math.Clamp(k, -3, 3);
    }

    /// <summary>
    /// Stone-local x = 0 in this tile's coordinates. A SPANNING stone is measured from ITS
    /// BOUNDARY, never from its own left edge: with a head joint sanded away it can begin back in
    /// the previous tile at an offset chosen by a family the far tile cannot see. Measured from
    /// the boundary, the west tile's columns run 0..31 and the east tile's 32.., contiguous, and
    /// derived from nothing but which side of the boundary this tile is on.
    /// </summary>
    private static int StoneOrigin(Config c, int fw, int kind, int course, int drop)
    {
        if (kind == 0) return -T;
        if (kind == 2 || drop == 2) return 0;
        return c.ATable[fw][course];
    }

    // ---- THE CRACK NETWORK, AT FIELD SCALE ------------------------------------------------
    //
    // A crack belongs to an ANCHOR TILE and runs for whole tiles beyond it, so every cell it
    // crosses generates the same polyline from the same world address — the construction that
    // makes a stone continuous, applied to a line.
    //
    // ⚠ TWO INTEGER TRAPS, both of which would desync this from the composer silently and only
    // near the map's origin, which is exactly where a review scene sits:
    //
    //   FLOOR DIVISION. Python's `//` floors toward negative infinity; C#'s `/` truncates toward
    //   zero. The anchor scan reaches eight tiles left and up of the cell being painted, so at
    //   x=0 it visits negative tiles and the polyline carries negative pixel coordinates.
    //   -1 / 1024 is 0 here and -1 there.
    //
    //   MODULO OF A NEGATIVE. Python's `%` returns non-negative; C#'s can return negative. The
    //   direction index random-walks by -1, so it reaches -1 and must wrap to 31, not to -1.
    //
    // Neither would throw. Both would draw a different crack on one side of the origin.
    private static int FloorDiv(int a, int b) => (int)System.Math.Floor((double)a / b);

    private static int Mod(int a, int m) => ((a % m) + m) % m;

    private static int Lcg(int state) => (int)(((long)state * 1103515245 + 12345) & 0x7FFFFFFF);

    private static List<(int X, int Y)> CrackPolyline(Config c, int ax, int ay)
    {
        var pts = new List<(int, int)>();
        int h = Mix(ax, ay, c.CrackSalt + c.Seed);
        if (h % 100 >= c.CrackRate) return pts;

        int st = Lcg(h | 1);
        int length = c.CrackMinTiles + (st >> 7) % (c.CrackMaxTiles - c.CrackMinTiles + 1);
        st = Lcg(st);
        int x = ax * T + (st >> 5) % T;
        st = Lcg(st);
        int y = ay * T + (st >> 5) % T;
        st = Lcg(st);
        int d = (st >> 9) % c.CrackDirs.Length;

        int px = x * c.CrackScale, py = y * c.CrackScale;
        for (int i = 0; i < length * T; i++)
        {
            st = Lcg(st);
            if ((st >> 11) % c.CrackTurn == 0)
            {
                st = Lcg(st);
                d = Mod(d + (((st >> 13) % 2) != 0 ? 1 : -1), c.CrackDirs.Length);
            }
            px += c.CrackDirs[d][0];
            py += c.CrackDirs[d][1];
            pts.Add((FloorDiv(px, c.CrackScale), FloorDiv(py, c.CrackScale)));
        }
        return pts;
    }

    private static HashSet<(int, int)> CrackPixels(Config c, int tx, int ty,
                                                   Dictionary<(int, int), List<(int X, int Y)>> cache)
    {
        var outp = new HashSet<(int, int)>();
        int reach = c.CrackMaxTiles + 1;
        int x0 = tx * T, y0 = ty * T;
        for (int ay = ty - reach; ay <= ty + reach; ay++)
        {
            for (int ax = tx - reach; ax <= tx + reach; ax++)
            {
                if (!cache.TryGetValue((ax, ay), out var line))
                {
                    line = CrackPolyline(c, ax, ay);
                    cache[(ax, ay)] = line;
                }
                foreach (var (wx, wy) in line)
                {
                    int lx = wx - x0, ly = wy - y0;
                    if (lx >= 0 && lx < T && ly >= 0 && ly < T) outp.Add((ly, lx));
                }
            }
        }
        return outp;
    }

    /// <summary>
    /// The dressing on one stone, in STONE-LOCAL pixels: (u, v, depth in ladder rungs).
    ///
    /// The device gate: *"material texture is below the perceptual floor — the floor reads as
    /// linoleum."* The grain this replaces was authored at about ±4 luminance against a 13.23
    /// rung, so it never survived quantisation and a stone face was one flat value with a border.
    ///
    /// What replaces it is not louder noise. These are the marks of a stone that was DRESSED —
    /// claw-chisel striations running ONE WAY PER STONE, because one mason worked one stone one
    /// way — chosen from TWELVE directions, because with four in the table (two of them the same
    /// 45°) a dozen stones in one lit pool drew the identical hatch and §8.3's motif arrived
    /// through the angle. And pits where the tooth tore out rather than cut. All of it is
    /// occlusion vocabulary
    /// and nothing else: every mark is a recess, so every mark is darker, and none has a lit side
    /// and a shaded side. A dressing mark drawn with a highlight would be depicted lighting.
    ///
    /// Addressed by the stone and sampled in stone-local coordinates measured from the boundary,
    /// so it cannot repeat on the tile grid and both tiles either side of a spanning stone dress
    /// it identically.
    /// </summary>
    /// <summary>
    /// The stone's own extent in stone-local coordinates: (uLo, uHi, vHi).
    ///
    /// Marks were first scattered across the whole 64×32 stone-local box, and a stone occupies a
    /// fraction of it — so roughly three quarters of every stone's dressing landed outside the
    /// class mask and was discarded. The interior amplitude moved 0.068 → 0.073 of a rung, which
    /// is nothing, on the change that was supposed to be the whole point.
    ///
    /// Derivable from the family tables both tiles already share, so it needs no new agreement
    /// between them. Mirrors <see cref="StoneOrigin"/> case for case.
    /// </summary>
    private static (int Lo, int Hi, int VHi) StoneExtent(Config c, int fw, int fe, int kind,
                                                         int course, int drop, int splitI)
    {
        int aW = c.ATable[fw][course], mvW = c.MvTable[fw][course];
        int aE = c.ATable[fe][course], mvE = c.MvTable[fe][course];
        int vHi = (course == 0 ? c.Splits[splitI][0] - 1 : T - 1) - CourseOriginY(c, splitI, course);

        // THE EXTENT MUST BE DERIVED FROM THE BOUNDARY ALONE, never from the merge. A merged
        // stone's real extent depends on this tile's drop and on the family of its far side,
        // neither of which the tile across the boundary can see — so the two dressed the same
        // stone from different extents and the seam landed on the boundary. Caught by the
        // boundary-step instrument at 1.277, on an axis the device gate had already passed.
        if (kind == 0) return (mvW, T + aW, vHi);
        if (kind == 2 || drop == 2) return (mvE, T + aE, vHi);
        return (0, mvE - aW, vHi);
    }

    /// <summary>
    /// `n` pixels along (dx, dy), Bresenham on the major axis — the C# twin of
    /// `compose_ashlar.mark_run`.
    ///
    /// `(u + dx*i, v + dy*i)` was exact while every direction in the table was a unit step, and
    /// draws a DOTTED line the moment one is not. The walk is on the major axis so a run of `n`
    /// deposits exactly `n` pixels at every angle: widening the direction table changes the
    /// angle and nothing about coverage or delivered amplitude (§13.8).
    /// </summary>
    private static void MarkRun(List<(int U, int V, double D)> outp, int u, int v,
                                int dx, int dy, int n, double depth)
    {
        int adx = System.Math.Abs(dx), ady = System.Math.Abs(dy);
        int sx = dx > 0 ? 1 : -1, sy = dy > 0 ? 1 : -1;
        int x = u, y = v;
        if (adx >= ady)
        {
            int err = adx / 2;
            for (int i = 0; i < n; i++)
            {
                outp.Add((x, y, depth));
                err -= ady;
                if (err < 0) { y += sy; err += adx; }
                x += sx;
            }
        }
        else
        {
            int err = ady / 2;
            for (int i = 0; i < n; i++)
            {
                outp.Add((x, y, depth));
                err -= adx;
                if (err < 0) { x += sx; err += ady; }
                y += sy;
            }
        }
    }

    /// <summary>
    /// Tooth `t`'s offset from the run: `gap * t` pixels along the direction's own UNIT normal —
    /// the C# twin of `compose_ashlar.mark_normal`.
    ///
    /// The raw (-dy, dx) it replaces is a normal of the right direction and the wrong length:
    /// |(-dy, dx)| is 1 only for an axis step, so a diagonal stone's teeth sat 2.83px apart
    /// against an orthogonal stone's 2.00, and a 3:1 stone's would sit at 6.32.
    ///
    /// `System.Math.Round(double)` is round-half-to-even, which is what Python's `round` does.
    /// The two must agree pixel for pixel or `paint_check` fails, which is the point of having it.
    /// </summary>
    private static (int U, int V) MarkNormal(int dx, int dy, int gap, int t)
    {
        if (t == 0) return (0, 0);
        double n = System.Math.Sqrt(dx * dx + dy * dy);
        int ou = (int)System.Math.Round(-dy / n * gap * t);
        int ov = (int)System.Math.Round(dx / n * gap * t);
        if (ou == 0 && ov == 0)          // never stack two teeth on one run
            return System.Math.Abs(dx) >= System.Math.Abs(dy) ? (0, 1) : (1, 0);
        return (ou, ov);
    }

    private static List<(int U, int V, double D)> StoneMarks(Config c, int key, bool worn,
                                                             (int Lo, int Hi, int VHi) ext,
                                                             double wear, int wx = -1, int wy = -1)
    {
        // TRAFFICKED STONES POLISH SMOOTHER AS THEIR JOINTS OPEN; sheltered stones stay sharp and
        // tight. The dressing is what traffic takes off first, so its count and its depth both
        // fall with wear — continuous now, where it used to be a binary channel flag.
        // A THIRD OF THE STONES CARRY NOTHING, drawn from the stone's own key before anything
        // else so a bare stone is bare from both tiles that see it. The frame critic: "the
        // diagonal-hatch motif recurs on a visible rhythm across the lit area — omit it on a
        // third of the slabs." An even scatter over every stone stops being an event and becomes
        // the material, which is §8.3's motif trap arriving through the dressing.
        // AND THE MARKED STONES CLUSTER. A per-stone coin flip is an even field by construction
        // however low its rate — "distributed as an even noise field across the whole floor".
        // The coin is biased by a coarse world field, so working ran in one part of a room and
        // not another, and a cluster crosses tile boundaries the way a hand would.
        double bare = c.MarkBareShare;
        if (wx >= 0)
        {
            int cp = c.MarkClusterPeriod * T;
            double cl = (Mix(wx / cp, wy / cp, c.MarkClusterSalt + c.Seed) % 1000) / 1000.0;
            bare = System.Math.Clamp(bare + (cl - 0.5) * 2.0 * c.MarkClusterSwing, 0.0, 0.95);
        }
        if (((key ^ (c.MarksSalt + c.Seed)) % 1000) / 1000.0 < bare)
            return new List<(int, int, double)>();

        double keep = 1.0 - c.DressingKeep * wear;
        var outp = new List<(int, int, double)>();
        int st = Lcg((key ^ (c.MarksSalt + c.Seed)) | 1);
        int uSpan = System.Math.Max(1, ext.Hi - ext.Lo - 1);
        int vSpan = System.Math.Max(1, ext.VHi - 1);

        // ONE DIRECTION PER STONE. A mason does not change hands halfway across a flag.
        var d = c.MarkDirs[(st >> 6) % c.MarkDirs.Length];
        int dx = d[0], dy = d[1];

        // STROKES COME IN BANDS, because a claw chisel has several teeth and a mason works in
        // passes. Scattered singly they read as SCRATCHES — a few long slashes at odd angles
        // across a face, which is damage, not dressing. Clustered into parallel runs 2px apart
        // they read as tooling, and each stroke still clears the readable-extent bar on its own.
        // MORE BANDS, NOT MORE TEETH PER BAND. Teeth raise regularity; bands raise
        // coverage while staying ragged. At three bands the delivered contrast sat at
        // 0.148 against a floor of 0.144, and a 3% margin proves nothing.
        int n = System.Math.Max(1, (int)System.Math.Round(
            ((worn ? c.WearBands : c.MarkBands) + ((st >> 9) % 2)) * keep));
        for (int j = 0; j < n; j++)
        {
            st = Lcg(st); int u = ext.Lo + (st >> 5) % uSpan;
            st = Lcg(st); int v = (st >> 5) % vSpan;
            st = Lcg(st);
            int length = c.MarkMinLen + (st >> 7) % (c.MarkMaxLen - c.MarkMinLen + 1);
            st = Lcg(st);
            int teeth = 2 + (st >> 10) % 2;
            st = Lcg(st);
            int gap = 2 + (st >> 12) % 2;      // 2 or 3 px between teeth, not always 2
            for (int t = 0; t < teeth; t++)
            {
                var (du, dv) = MarkNormal(dx, dy, gap, t);
                int ou = u + du, ov = v + dv;
                // EVERY TOOTH A DIFFERENT LENGTH. Equal-length teeth on an equal pitch is a
                // barcode: the first clustered version read as tally marks on some stones. A
                // chisel skips and bites unevenly, and a ragged end is the difference between
                // tooling and hatching.
                st = Lcg(st);
                int ln = System.Math.Max(c.MarkMinLen, length - (st >> 8) % 3);
                MarkRun(outp, ou, ov, dx, dy, ln, c.MarkDepth * keep);
            }
        }

        int m = System.Math.Max(0, (int)System.Math.Round(
            ((worn ? c.WearPits : c.MarkPits) + ((st >> 11) % 3)) * keep));
        for (int j = 0; j < m; j++)
        {
            st = Lcg(st); int u = ext.Lo + (st >> 5) % uSpan;
            st = Lcg(st); int v = (st >> 5) % vSpan;
            st = Lcg(st);
            int wdt = 2 + (st >> 13) % 2;      // 2 or 3 across — never the 1px speck
            for (int aa = 0; aa < wdt; aa++)
                for (int bb = 0; bb < 2; bb++)
                    outp.Add((u + aa, v + bb, c.PitDepth * keep));
        }
        return outp;
    }

    /// <summary>
    /// THE TRAFFIC FIELD, sampled at a world pixel — bilinear between TILE CENTRES.
    ///
    /// Per-tile is what the level graph can say; per-pixel is what the floor needs. Consuming the
    /// per-tile scalar directly would paint the traffic model onto the tile grid, which is
    /// §8.3.1's lattice with a better excuse. Interpolating between centres means a route crosses
    /// a tile boundary without knowing there was one.
    /// </summary>
    private static int TrafficAt(byte[,] f, int wx, int wy)
    {
        int w = f.GetLength(0), h = f.GetLength(1);
        int sx = wx - T / 2, sy = wy - T / 2;
        int gx = FloorDiv(sx, T), gy = FloorDiv(sy, T);
        int fx = sx - gx * T, fy = sy - gy * T;
        int Smp(int x, int y) => f[System.Math.Clamp(x, 0, w - 1), System.Math.Clamp(y, 0, h - 1)];
        int top = Smp(gx, gy) * (T - fx) + Smp(gx + 1, gy) * fx;
        int bot = Smp(gx, gy + 1) * (T - fx) + Smp(gx + 1, gy + 1) * fx;
        return (top * (T - fy) + bot * fy) / (T * T);
    }

    /// <summary>
    /// What the wear pass actually consumes: the traffic field, FRAYED by the old noise.
    ///
    /// The register guardrail is that the path is discovered, never staged — and a pure
    /// interpolation of an accumulated route is a smooth ribbon, which is exactly what "reads as
    /// a drawn route" means. A quarter of the old two-octave field is mixed back in so the edges
    /// break up and the width wanders, without moving where the route goes.
    /// </summary>
    /// <summary>
    /// THE WEAR SCALAR, RE-KEYED TO THE ROUTE ITSELF.
    ///
    /// It was a per-tile accumulation sampled bilinearly. Round 21 measured that such a field
    /// cannot supply a line: the direction derived from it agreed between neighbouring tiles only
    /// 34% of the time, so a grain keyed to it never accumulated into anything a viewer could
    /// follow. The magnitude now comes from DISTANCE TO THE POLYLINE — a pure function of world
    /// position, coherent along the route by construction rather than by luck.
    ///
    /// THE OLD NOISE STAYS, mixed at the same quarter it always was. The register guardrail is
    /// that a path is discovered and never staged, and a pure distance falloff is a smooth ribbon,
    /// which is exactly what "reads as a drawn route" means. The noise frays the shoulders without
    /// moving where the route goes.
    /// </summary>
    private static int WearScalar(Config c, byte[,]? traffic, int wx, int wy)
    {
        int noise = WearAt(c, wx, wy);
        if (c.Lines.Count > 0)
        {
            double v = Logic.ECS.RoutePolyline.Strength(
                c.Lines, (wx + 0.5) / (double)T, (wy + 0.5) / (double)T) * 255.0;
            return ((int)System.Math.Round(v) * 3 + noise) / 4;
        }
        if (traffic == null) return noise;
        return (TrafficAt(traffic, wx, wy) * 3 + noise) / 4;
    }

    /// <summary>
    /// The old wear scalar at a world pixel, 0..255 — two octaves of value noise at FIVE and ELEVEN
    /// tiles, bilinear, integer throughout so the composer is reproduced exactly.
    ///
    /// Coprime periods, coprime with the tile: nothing in the field lands on the grid or on any
    /// harmonic of it. A single octave at any period would draw its own.
    /// </summary>
    private static int WearAt(Config c, int px, int py)
    {
        long total = 0;
        int wsum = 0;
        foreach (var oct in c.WearOctaves)
        {
            int span = oct[0] * T, weight = oct[1];
            int gx = FloorDiv(px, span), gy = FloorDiv(py, span);
            int fx = px - gx * span, fy = py - gy * span;
            long v00 = Mix(gx, gy, c.WearSalt + c.Seed) & 255;
            long v10 = Mix(gx + 1, gy, c.WearSalt + c.Seed) & 255;
            long v01 = Mix(gx, gy + 1, c.WearSalt + c.Seed) & 255;
            long v11 = Mix(gx + 1, gy + 1, c.WearSalt + c.Seed) & 255;
            long top = v00 * (span - fx) + v10 * fx;
            long bot = v01 * (span - fx) + v11 * fx;
            total += ((top * (span - fy) + bot * fy) / ((long)span * span)) * weight;
            wsum += weight;
        }
        return (int)(total / wsum);
    }

    /// <summary>
    /// The wear scalar as one of four AGES. Not taste — a correctness fix: a continuous scalar
    /// against a seven-rung ladder puts pixels on quantisation knife-edges, and at exactly w=0.5
    /// a joint lands HALF A RUNG between two levels where the tie is broken by floating-point
    /// noise. The composer and its mirror disagreed on 65 pixels for no reason either could be
    /// said to be wrong about, and a third implementation would have been a third coin flip.
    /// Mortar is tight, opening, open, or gone.
    /// </summary>
    private static double Wear01(Config c, int raw, bool channel)
    {
        if (channel) raw = System.Math.Max(raw, c.ChannelWear);
        if (raw <= c.WearLo) return 0.0;
        if (raw >= c.WearHi) return 1.0;
        double f = (raw - c.WearLo) / (double)(c.WearHi - c.WearLo);
        double best = c.WearAges[0];
        foreach (var age in c.WearAges)
            if (System.Math.Abs(age - f) < System.Math.Abs(best - f)) best = age;
        return best;
    }

    /// <summary>Which of the four wear ages this scalar snapped to.</summary>
    private static int AgeIndex(Config c, double w)
    {
        int best = 0;
        for (int i = 1; i < c.WearAges.Length; i++)
            if (System.Math.Abs(c.WearAges[i] - w) < System.Math.Abs(c.WearAges[best] - w)) best = i;
        return best;
    }

    /// <summary>
    /// THE CHROMA CHANNEL's tint for one wear age: the material tint rotated toward the ruled
    /// direction at CONSTANT LUMINANCE.
    ///
    /// The luminance component of the direction is projected out here rather than baked into the
    /// manifest, so that the invariant — this lever moves colour and no value — is enforced in
    /// the code that uses it and cannot drift if the direction is ever re-ruled.
    /// </summary>
    private static double[] ChromaTint(Config c, int age)
    {
        double[] w = { 0.299, 0.587, 0.114 };
        double s = c.ChromaByAge[age];
        double num = 0, den = 0;
        for (int i = 0; i < 3; i++) { num += w[i] * c.Tint[i] * c.ChromaDir[i]; den += w[i] * c.Tint[i]; }
        double k = den == 0 ? 0 : num / den;
        var outv = new double[3];
        for (int i = 0; i < 3; i++) outv[i] = c.Tint[i] * (1.0 + s * (c.ChromaDir[i] - k));
        return outv;
    }

    /// <summary>
    /// The local axis of travel, derived from the traffic field alone: 0 = E-W, 1 = NE-SW,
    /// 2 = N-S, 3 = NW-SE, -1 = none. Traffic is roughly constant ALONG a route and falls away
    /// ACROSS it, so the gradient points across the path and the travel axis is perpendicular.
    /// Derived rather than carried beside the field, so both painters reach the same answer from
    /// the same numbers with no second channel to fall out of sync.
    /// </summary>
    private static System.Collections.Generic.IReadOnlyList<Logic.ECS.RoutePolyline.Line>
        linesForAxis = new System.Collections.Generic.List<Logic.ECS.RoutePolyline.Line>();

    /// <summary>How far a sheltered joint is lifted off the ladder's bottom, in rungs.</summary>
    private static double ShelterLift(Config c, int wx, int wy)
    {
        double h = (Mix(wx / c.ShelterBlock, wy / c.ShelterBlock, c.ShelterSalt + c.Seed) % 1000)
                   / 1000.0;
        double acc = 0.0;
        for (int i = 0; i < c.ShelterWeights.Length; i++)
        {
            acc += c.ShelterWeights[i];
            if (h < acc) return c.ShelterLift[System.Math.Min(i, c.ShelterLift.Length - 1)];
        }
        return c.ShelterLift[^1];
    }

    /// <summary>
    /// The specular lane's strength at a world pixel: continuous down the centre, streaked along
    /// the tangent, FRAYED at its shoulder.
    ///
    /// THE FRAY IS RULED, not a nicety. A smoothstep to zero at a fixed distance gives the lane a
    /// hard upper edge, and the walk read that as a spotlight stripe laid on the floor — which is
    /// staging, and §8.1 does not allow it. The distance is jittered on a coarse world block
    /// before the falloff is taken, so the edge wanders by a fraction of a tile instead of
    /// arriving on a line.
    /// </summary>
    private static double LanePolish(Config c, int wx, int wy)
    {
        if (c.Lines.Count == 0) return 0.0;
        var nr = Logic.ECS.RoutePolyline.Nearest(c.Lines, (wx + 0.5) / T, (wy + 0.5) / T);
        double fray = ((Mix(wx / c.ShelterBlock, wy / c.ShelterBlock, c.LaneFraySalt + c.Seed)
                        % 1000) / 1000.0 - 0.5) * 2.0 * c.LaneFray;
        double d = nr.Dist + fray;
        double lane = System.Math.Clamp(
            (c.PolishShoulder - d) / System.Math.Max(c.PolishShoulder - c.PolishLaneWidth, 1e-6),
            0.0, 1.0);
        lane = lane * lane * (3.0 - 2.0 * lane) * c.PolishLaneGain;

        double nlen = System.Math.Sqrt(nr.Tx * nr.Tx + nr.Ty * nr.Ty);
        if (nlen < 1e-9) nlen = 1.0;
        double perp = (-nr.Ty / nlen) * wx + (nr.Tx / nlen) * wy;
        long band = ((long)System.Math.Floor(perp) % c.StriaPeriod + c.StriaPeriod) % c.StriaPeriod;
        double hs = (Mix(wx, wy, c.StriaSalt + c.Seed) % 100) / 100.0;
        if (band == 0 || (band == 1 && hs < c.StriaDepth)) lane *= 1.0 - c.StriaDepth;
        return lane;
    }

    /// <summary>
    /// The travel axis AT A PIXEL. With a route present this is the line's own tangent there, so
    /// the compaction's direction changes where the route turns rather than where the tiles do.
    /// Without one it falls back to the per-tile field, which is all a fieldless scene has.
    /// </summary>
    private static int PixelAxis(Config c, byte[,]? traffic, int wx, int wy, int tx, int ty)
    {
        if (c.Lines.Count > 0)
            return Logic.ECS.RoutePolyline.Axis(c.Lines, (wx + 0.5) / T, (wy + 0.5) / T);
        return TravelAxis(traffic, tx, ty);
    }

    private static int TravelAxis(byte[,]? t, int tx, int ty)
    {
        // THE ROUTE RUNS WHERE THE TRAFFIC CONTINUES. Sum the two neighbours along each of the
        // four axes a pixel grid allows; the busiest axis is the way the feet went. Walls carry
        // no traffic, so a corridor's own walls vote against crossing them.
        //
        // The first version took the perpendicular of the gradient and was wrong in a one-tile
        // corridor — both across-neighbours are wall, the across-gradient is identically zero,
        // and the perpendicular then reads the along-route variation as a route running the other
        // way. It labelled the review scene's north-south chokepoint E-W down its whole length.
        // THE LINE'S OWN TANGENT, when there is a line. Everything below is the fallback for a
        // scene with no route model, and it is the code round 21 found aimed ninety degrees wrong
        // in a one-tile corridor — kept only because a floor with no routes must still lay.
        if (linesForAxis.Count > 0)
            return Logic.ECS.RoutePolyline.Axis(linesForAxis, tx + 0.5, ty + 0.5);
        if (t == null) return -1;
        int w = t.GetLength(0), h = t.GetLength(1);
        double At(int x, int y) =>
            (x < 0 || y < 0 || x >= w || y >= h) ? 0.0 : t[x, y];
        int[] dxs = { 1, 1, 0, 1 };
        int[] dys = { 0, -1, 1, 1 };
        double best = -1, worst = double.MaxValue;
        int bi = -1;
        for (int i = 0; i < 4; i++)
        {
            double v = At(tx + dxs[i], ty + dys[i]) + At(tx - dxs[i], ty - dys[i]);
            if (v > best) { best = v; bi = i; }
            if (v < worst) worst = v;
        }
        return (best - worst < 12.0) ? -1 : bi;      // DIR_MIN_GRAD * 2
    }

    /// <summary>How much a bed joint and a head joint each compact, on this travel axis.</summary>
    private static (double bed, double head) AnisoWeights(Config c, int axis)
    {
        double k = 1.0 - c.DeformAniso;
        if (axis < 0) return (1.0, 1.0);
        if (axis == 2) return (1.0, k);                   // north-south: the bed joints are crossed
        if (axis == 0) return (k, 1.0);                   // east-west: the head joints are crossed
        double d = 1.0 - c.DeformAniso / 2.0;
        return (d, d);
    }

    /// <summary>
    /// Is this cell a MOUTH — the place a narrow way meets an open one?
    ///
    /// Derived from the map's own shape, never hand-placed: a walkable cell with two or fewer
    /// orthogonal walkable neighbours, standing next to a cell that has three or more. That is a
    /// doorway or a corridor end by construction, and it is where routes converge and the stone
    /// dishes.
    ///
    /// It is NOT a sill and NOT a kerb. Nothing is built here — the register is found-and-annexed
    /// with thin administration, so traffic carved what it needed and nobody installed a piece.
    /// </summary>
    /// <summary>
    /// Is this cell FLOOR — regardless of whether anything stands on it?
    ///
    /// Walkable-minus-props is a movement predicate and this is a rendering one. A cell with a
    /// barrel on it is still a floor cell; it is simply a floor cell you cannot walk into.
    /// </summary>
    private static bool IsFloorCell(GameMap map, int x, int y)
        => map.IsWalkable(x, y) || map.IsPropCell(x, y);


    private static bool IsMouth(GameMap map, int x, int y)
    {
        if (!map.IsWalkable(x, y)) return false;
        int Open(int cx, int cy)
        {
            int n = 0;
            if (map.IsWalkable(cx, cy - 1)) n++;
            if (map.IsWalkable(cx, cy + 1)) n++;
            if (map.IsWalkable(cx - 1, cy)) n++;
            if (map.IsWalkable(cx + 1, cy)) n++;
            return n;
        }
        if (Open(x, y) > 2) return false;
        return Open(x, y - 1) >= 3 || Open(x, y + 1) >= 3
            || Open(x - 1, y) >= 3 || Open(x + 1, y) >= 3;
    }

    /// <summary>
    /// Alpha -> whole ladder rungs of darkening. ONE definition, two painters; the composer's
    /// twin is `occlusion_rungs` in `tools/tier1_floors/compose_ashlar.py`, and the two are tied
    /// together by `occlusion_check` in the manifest rather than by this comment.
    ///
    /// `layers` is the ambient-anchored stack (RULED 2026-09-02) — the boundary re-drawn where
    /// the lamp does not reach. Under the blend that compounded as 1-(1-a)^n and it still does;
    /// what changed is that the result is quantised. Three layers reach 22.11, which is why the
    /// ladder needed two more rungs below (see PALETTE_EXTEND_BELOW).
    ///
    /// ANCHORED AT THE MEDIAN, NOT AT THE PIXEL. The blend darkened a bright stone more than a
    /// dark one because a blend is a ratio. Occlusion is FORM and a form does not vary with the
    /// value of what it crosses — which is also what makes it exactly representable, since a
    /// fixed number of rungs off a ladder value is a ladder value.
    /// </summary>
    private static int OcclusionRungs(Config c, double alpha, int layers)
    {
        double rung = c.Ladder[1] - c.Ladder[0];
        double eff = 1.0 - System.Math.Pow(1.0 - alpha, layers);
        // FLOOR(x + 0.5), NOT Round(). C#'s Math.Round is away-from-zero at a tie and numpy's is
        // to-even, so a rung landing exactly on a half would be 5 in the engine and 4 in the
        // composer with neither able to be called wrong — the same knife-edge WEAR_AGES was
        // snapped to four values to avoid. One expression, both painters, no tie-break at all.
        return (int)System.Math.Floor(eff * (c.LumMedian - c.OcclusionFloor) / rung + 0.5);
    }

    private static int LadderIndex(Config c, double v)
    {
        int best = 0;
        double bd = double.MaxValue;
        for (int i = 0; i < c.Ladder.Length; i++)
        {
            double d = System.Math.Abs(v - c.Ladder[i]);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    public static string Apply(TileLayer tileLayer, GameMap map, string manifestResPath,
                               System.Func<int, int, bool> isChannel)
        => Apply(tileLayer, map, manifestResPath, isChannel, null);

    /// <summary>
    /// As above, and paints §12.1's contact occlusion into the floor's own pixels.
    ///
    /// The boundary used to be an alpha-blended sprite laid over this floor, and compositing it
    /// took the family's nine authored albedo values to a hundred and thirty-two — the wall cap's
    /// disease (107 -> 9) arriving on the floor through a layer nobody was measuring. Here it is
    /// a subtraction in WHOLE LADDER RUNGS, so the delivered floor carries the family's palette
    /// and nothing else. The sprite is still the datum: its alpha is read per pixel and now says
    /// how many rungs rather than how much to blend, which keeps the along-edge jitter that is
    /// the only thing holding the seam off a straight constant-pitch line.
    /// </summary>
    public static string Apply(TileLayer tileLayer, GameMap map, string manifestResPath,
                               System.Func<int, int, bool> isChannel,
                               OcclusionBake? bake)
    {
        var cfg = Load(manifestResPath, out string status);
        if (cfg == null) return $"[Tier1] ashlar floor: NOT APPLIED — {status}";

        // BOTH CROSS-CHECKS, BEFORE ANYTHING IS LAID. The first says this code agrees with the
        // composer about the bond; the second says it agrees about the material. A silent
        // disagreement in either is a seam at every tile boundary that nothing downstream reports.
        foreach (var (x, y, salt, expect) in cfg.EdgeCheck)
        {
            int got = EdgeFamily(x, y, salt, cfg.Seed, cfg.Families);
            if (got != expect)
                return $"[Tier1] ashlar floor: REFUSED — edge-family cross-check failed at "
                     + $"({x},{y}) salt={salt}: composer said {expect}, engine says {got}.";
        }
        foreach (var (x, k, kind, drop, expect) in cfg.StoneCheck)
        {
            int gotDrop = DropChoice(cfg, x, k);
            int addr = Address(kind, gotDrop);
            int bx = addr == 2 ? x + 1 : x;
            int key = addr == 1 ? Mix(x, k, cfg.InteriorSalt + cfg.Seed)
                                : Mix(bx, k, cfg.SpanSalt + cfg.Seed);
            int got = OffsetSteps(cfg, key, ClusterBias(cfg, bx, k));
            if (gotDrop != drop || got != expect)
                return $"[Tier1] ashlar floor: REFUSED — stone cross-check failed at tile x={x} "
                     + $"course={k} kind={kind}: composer said drop={drop} steps={expect}, engine "
                     + $"says drop={gotDrop} steps={got}. The stones would be painted by different "
                     + $"arithmetic than the one that drew the bond.";
        }

        var grainImg = LoadImage(cfg.GrainPath);
        if (grainImg == null)
            return $"[Tier1] ashlar floor: NOT APPLIED — grain bank unreadable: {cfg.GrainPath}";

        var atlasCache = new Dictionary<int, Image>();
        var paintFail = SelfCheck(cfg, grainImg, atlasCache);
        if (paintFail != null) return $"[Tier1] ashlar floor: REFUSED — {paintFail}";

        // THE OCCLUSION CROSS-CHECK. The paint check cannot cover this one: it compares composer
        // against engine on a field with no map in it, and occlusion is decided by WALL
        // ADJACENCY. So the two derivations are tied together the way the edge families are.
        // §4.2 — a duplicate with an enforcement is a different thing from a duplicate with a
        // comment, and this is what goes red if the two ever disagree about a rung.
        int occChecked = 0;
        if (bake != null && cfg.OcclusionCheck.Count > 0)
        {
            var order = new[] { "N", "E", "S", "W" };
            foreach (var (side, px, py, layers, expect) in cfg.OcclusionCheck)
            {
                int si = System.Array.IndexOf(order, side);
                var alphaImg = si >= 0 ? bake.Value.AlphaBySide[si] : null;
                if (alphaImg == null)
                    return $"[Tier1] ashlar floor: REFUSED — occlusion cross-check has no sprite "
                         + $"for side {side}; the composer measured one.";
                int got = OcclusionRungs(cfg, alphaImg.GetPixel(px, py).A, layers);
                if (got != expect)
                    return $"[Tier1] ashlar floor: REFUSED — occlusion cross-check failed at "
                         + $"{side}({px},{py}) layers={layers}: composer says {expect} rungs, "
                         + $"engine says {got}.";
                occChecked++;
            }
        }

        // WHERE PEOPLE ACTUALLY WALK — derived from the level graph, once, before anything is
        // laid. The review scenes carry no Room records, so the map-only derivation is used: the
        // spine is the level's own longest walk and every leaf hanging off it is a branch.
        var tf = TrafficField.ComputeFromMap(map);
        var traffic = tf.Field;

        // THE FIELD, IN THE LOG, so it can be audited rather than trusted. Counts alone cannot
        // say WHERE the traffic went, and a route through the wrong part of a room would look
        // exactly like a route through the right one in any summary statistic. Ten levels, '.'
        // for unwalked through '#' for the busiest.
        var ramp = " .:-=+*#%@";
        var sb = new System.Text.StringBuilder();
        sb.Append("[Tier1] traffic field (space=unwalked .. @=busiest)\n");
        for (int y = 0; y < map.Height; y++)
        {
            sb.Append("[Tier1]   ");
            for (int x = 0; x < map.Width; x++)
                sb.Append(map.IsWalkable(x, y)
                    ? ramp[System.Math.Clamp(traffic[x, y] * (ramp.Length - 1) / 255, 0, ramp.Length - 1)]
                    : '#');
            sb.Append('\n');
        }
        // THE ROUTES, AS LINES. Keying only — nothing is drawn along them (§8.1: worn, never
        // built). The age layer that already shipped simply re-keys to distance from the line and
        // to its tangent, and therefore concentrates along it.
        cfg.Lines = tf.Lines;
        linesForAxis = tf.Lines;

        // THE AXIS MAP, LOGGED. Round 21's finding was a number nobody had measured — the derived
        // travel axis agreed between neighbouring tiles only 34% of the time — and it surfaced
        // only because the axis could be drawn. It is logged every boot now so the next reader
        // does not have to think of it first.
        sb.Append("[Tier1] travel axis (- E-W  / NE-SW  | N-S  \\ NW-SE  . none)\n");
        int coherent = 0, adjacent = 0;
        for (int y = 0; y < map.Height; y++)
        {
            sb.Append("[Tier1]   ");
            for (int x = 0; x < map.Width; x++)
            {
                if (!map.IsWalkable(x, y)) { sb.Append('#'); continue; }
                int ax = TravelAxis(traffic, x, y);
                sb.Append(ax == 0 ? '-' : ax == 1 ? '/' : ax == 2 ? '|' : ax == 3 ? '\\' : '.');
                for (int k = 0; k < 2; k++)
                {
                    int nx = x + (k == 0 ? 1 : 0), ny = y + (k == 0 ? 0 : 1);
                    if (nx >= map.Width || ny >= map.Height || !map.IsWalkable(nx, ny)) continue;
                    int bx = TravelAxis(traffic, nx, ny);
                    if (ax < 0 || bx < 0) continue;   // off-route is a boundary, not a disagreement
                    adjacent++;
                    if (ax == bx) coherent++;
                }
            }
            sb.Append('\n');
        }
        // THE ROUTE STRENGTH, LOGGED, and it replaces the traffic field as what instruments
        // bucket by. The keying moved to the line this round; a measurement still bucketing by
        // the per-tile field is measuring a different population from the one the painter reads,
        // and it showed: the instrument's own matching control started leaking and its null draws
        // collapsed to zero. Measure what is keyed, or measure nothing.
        sb.Append("[Tier1] route strength (space=off-route .. @=on the line)\n");
        for (int y = 0; y < map.Height; y++)
        {
            sb.Append("[Tier1]   ");
            for (int x = 0; x < map.Width; x++)
            {
                if (!map.IsWalkable(x, y)) { sb.Append('#'); continue; }
                double v = cfg.Lines.Count == 0 ? 0.0
                    : Logic.ECS.RoutePolyline.Strength(cfg.Lines, x + 0.5, y + 0.5);
                int k = System.Math.Clamp((int)System.Math.Round(v * 9.0), 0, 9);
                sb.Append(" .:-=+*#%@"[k]);
            }
            sb.Append('\n');
        }

        sb.Append("[Tier1] axis coherence: " + coherent + " of " + adjacent
                  + " adjacent on-route pairs agree ("
                  + (adjacent > 0 ? (100.0 * coherent / adjacent).ToString("F0") : "0")
                  + "%) - round 21 measured 34% on the per-tile field\n");

        var crackCache = new Dictionary<(int, int), List<(int X, int Y)>>();
        int laid = 0, channel = 0, missing = 0, polished = 0, mouths = 0, occCells = 0;

        // §4.2: A STEP THAT DOES NOTHING MUST GO RED. If the shader fails to load, the floor
        // still lays and still looks almost right — the chroma and the joints carry on — and the
        // one lever this round exists to test would be silently absent. So its absence is
        // counted and reported on the same line as everything else.
        var polishShader = ResourceLoader.Load<Shader>(PolishShaderPath);

        foreach (var (pos, node) in tileLayer.TileSprites)
        {
            // ⚠ A PROP'S CELL IS STILL FLOOR — #128's conflation, in a second painter.
            //
            // `IsWalkable` is `_walkable && !_propCells.Contains(...)`: it answers CAN AN ACTOR
            // STEP HERE, which is a movement question. Using it to decide WHAT TO PAINT means a
            // blocking prop's cell is skipped by the family and left showing the theme's magenta
            // placeholder — and the props pass found exactly that, three props each sitting on a
            // magenta square. #128 is the same fault in FloorComposer: *"Pass 1 conflates
            // walkability with floor — wall-adjacent props render as light pedestals."*
            //
            // The magenta is the guard working (§4.2: a painter that misses comes back screaming
            // rather than plausible), so the fix is to paint the cell, never to change the
            // placeholder. Floor-ness is `IsFloor`; walkability is somebody else's question.
            if (!IsFloorCell(map, pos.X, pos.Y)) continue;
            if (node is not Sprite2D sprite) continue;

            int n = EdgeFamily(pos.X, pos.Y, cfg.HorizSalt, cfg.Seed, cfg.Families);
            int so = EdgeFamily(pos.X, pos.Y + 1, cfg.HorizSalt, cfg.Seed, cfg.Families);
            int fw = EdgeFamily(pos.X, pos.Y, cfg.VertSalt, cfg.Seed, cfg.Families);
            int fe = EdgeFamily(pos.X + 1, pos.Y, cfg.VertSalt, cfg.Seed, cfg.Families);
            int idx = TileIndex(n, fe, so, fw, cfg.Families);

            if (!atlasCache.TryGetValue(idx, out var atlas))
            {
                if (!cfg.Atlas.TryGetValue(idx, out var path)) { missing++; continue; }
                atlas = LoadImage(path);
                if (atlas == null) { missing++; continue; }
                atlasCache[idx] = atlas;
            }

            bool mouthHere = IsMouth(map, pos.X, pos.Y);
            if (mouthHere) mouths++;
            (int SideMask, int Layers)? occHere = null;
            if (bake != null && bake.Value.Cells.TryGetValue((pos.X, pos.Y), out var oc))
            { occHere = oc; occCells++; }
            var outImg = PaintCell(cfg, atlas, grainImg, pos.X, pos.Y, fw, fe, traffic,
                                   isChannel, crackCache, mouthHere, out bool anyWorn,
                                   out var polishImg, occHere,
                                   bake?.AlphaBySide);

            // NO FLIP, NO ROTATION. Orientation is meaning on an edge-matched tile, and the
            // coursing has a direction: turning one would stand its bed joints on end.
            sprite.Texture = ImageTexture.CreateFromImage(outImg);
            sprite.FlipH = false;
            sprite.FlipV = false;

            // THE POLISH LEVER, attached per sprite. A ShaderMaterial each, because the mask is
            // per tile — 74 of them on this scene, which is the cost of a floor that answers the
            // lamp rather than being painted as if it had.
            if (polishShader != null)
            {
                var pm = new ShaderMaterial { Shader = polishShader };
                pm.SetShaderParameter("polish_tex", ImageTexture.CreateFromImage(polishImg));
                pm.SetShaderParameter("polish_exp", (float)cfg.PolishExp);
                pm.SetShaderParameter("polish_gain", (float)cfg.PolishGain);
                pm.SetShaderParameter("shoulder_knee", (float)cfg.ShoulderKnee);
                pm.SetShaderParameter("shoulder_ceiling", (float)cfg.ShoulderCeiling);
                pm.SetShaderParameter("spec_shade", (float)cfg.SpecShade);
                pm.SetShaderParameter("albedo_median", (float)cfg.AlbedoMedian);
                sprite.Material = pm;
                polished++;
            }
            laid++;
            if (anyWorn) channel++;
        }

        return $"[Tier1] ashlar floor: laid={laid} channel_cells={channel} missing={missing} "
             + $"families={cfg.Families} seed={cfg.Seed} atlases={atlasCache.Count} "
             + $"edge_check={cfg.EdgeCheck.Count}/OK stone_check={cfg.StoneCheck.Count}/OK "
             + $"paint_check={cfg.PaintCheck.Count}/OK "
             + $"occlusion={(bake == null ? "SPRITE(not baked)" : $"baked:{occCells}/check:{occChecked}/OK")} "
             + $"lines={cfg.Lines.Count} mouths={mouths} polished={polished}{(polishShader == null ? "/SHADER-MISSING" : "")} "
             + $"traffic=spine:{tf.SpineLength:F0}/routes:{tf.Routes} "
             + $"manifest={manifestResPath}\n" + sb.ToString().TrimEnd();
    }


    /// <summary>
    /// Paint one cell. Extracted so that the SELF-CHECK below runs the very code that lays the
    /// floor rather than a second copy of it — a check against a reimplementation only proves the
    /// reimplementation.
    /// </summary>
    private const string PolishShaderPath = "res://src/Presentation/assets/shaders/tier1_polish.gdshader";

    private static Image PaintCell(Config cfg, Image atlas, Image grainImg, int tx, int ty,
                                   int fw, int fe, byte[,]? traffic, System.Func<int, int, bool>? isChannel,
                                   Dictionary<(int, int), List<(int X, int Y)>> crackCache,
                                   bool isMouth, out bool anyWorn, out Image polish,
                                   (int SideMask, int Layers)? occlusion = null,
                                   Image?[]? occlusionAlpha = null)
    {
        var drops = new int[Courses];
        for (int c = 0; c < Courses; c++)
            drops[c] = DropChoice(cfg, tx, ty * Courses + c);
        int splitI = RowSplit(cfg, ty);
        int cellIndex = splitI * 9 + drops[0] * 3 + drops[1];

        // Per-class parameters computed ONCE, then a single pass over the pixels. The
        // first version looped the whole tile once per class: seven passes and about 7k
        // GetPixel calls per cell before a single pixel was written.
        var offset = new double[7];
        var gmul = new double[7];
        var bankX = new int[7];
        var bankY = new int[7];
        var ox = new int[7];
        var oy = new int[7];
        var wornClass = new bool[7];
        var markDepth = new double[T, T];
        double rung = cfg.Ladder[1] - cfg.Ladder[0];
        anyWorn = false;

        for (int c = 0; c < Courses; c++)
        {
            int courseK = ty * Courses + c;
            for (int kind = 0; kind < 3; kind++)
            {
                int cls = 1 + c * 3 + kind;
                int addr = Address(kind, drops[c]);
                int bx = addr == 2 ? tx + 1 : tx;
                int key = addr == 1 ? Mix(tx, courseK, cfg.InteriorSalt + cfg.Seed)
                                    : Mix(bx, courseK, cfg.SpanSalt + cfg.Seed);
                int steps = OffsetSteps(cfg, key, ClusterBias(cfg, bx, courseK));

                // Wear is read off the MAP, which both tiles either side of a boundary can
                // read, and never off "which tile am I". So the channel ends at a JOINT rather
                // than at a tile edge - the soft boundary section 8.2.1 asks for, delivered
                // structurally instead of by feathering.
                bool worn = isChannel != null && (addr switch
                {
                    0 => isChannel(tx - 1, ty) && isChannel(tx, ty),
                    2 => isChannel(tx, ty) && isChannel(tx + 1, ty),
                    _ => isChannel(tx, ty),
                });
                if (worn) anyWorn = true;
                wornClass[cls] = worn;

                offset[cls] = steps * (cfg.Ladder[1] - cfg.Ladder[0])
                            * (worn ? cfg.WearSpread : 1.0);
                gmul[cls] = cfg.GrainAmp * (worn ? cfg.WornMul : 1.0);
                int bank = key % cfg.GrainBank;
                bankX[cls] = (bank % BankCols) * GrainSide;
                bankY[cls] = (bank / BankCols) * GrainSide;
                ox[cls] = StoneOrigin(cfg, fw, kind, c, drops[c]);
                oy[cls] = CourseOriginY(cfg, splitI, c);

                // THE WORKED SURFACE, masked by the stone's own class so a mark falling past a
                // joint is simply not drawn. Overlapping marks accumulate, exactly as the
                // composer's do.
                int mAx = (cellIndex % AtlasCols) * T, mAy = (cellIndex / AtlasCols) * T;
                var mExt = StoneExtent(cfg, fw, fe, kind, c, drops[c], splitI);
                double sw = Wear01(cfg, WearScalar(cfg, traffic, tx * T + T / 2, ty * T + T / 2), worn);
                foreach (var (mu, mv, md) in StoneMarks(cfg, key, worn, mExt, sw,
                                                       tx * T, ty * T))
                {
                    int mlx = mu + ox[cls], mly = mv + oy[cls];
                    if (mlx < 0 || mlx >= T || mly < 0 || mly >= T) continue;
                    int atCls = (int)System.Math.Round(
                        atlas.GetPixel(mAx + mlx, mAy + mly).G * 255.0);
                    if (atCls == cls) markDepth[mly, mlx] -= md * rung;
                }
            }
        }

        // ONE RAW ACCUMULATION, THEN ONE QUANTISE, and the order is load-bearing rather than
        // tidy. The composer rounds the whole tile once at the end; rounding per pixel and then
        // subtracting wear computes quantise(quantise(x) + w), which is not quantise(x + w). The
        // mirror did exactly that and disagreed on 1368 pixels — every one a chipped arris,
        // because a chip is the only thing that subtracts from a STONE pixel after the class
        // loop has run.
        var raw = new double[T, T];
        var clsArr = new int[T, T];
        int ax = (cellIndex % AtlasCols) * T, ay = (cellIndex / AtlasCols) * T;

        for (int py = 0; py < T; py++)
        {
            for (int px = 0; px < T; px++)
            {
                var src = atlas.GetPixel(ax + px, ay + py);
                int cls = (int)System.Math.Round(src.G * 255.0);
                clsArr[py, px] = cls;
                double L = cfg.Ladder[(int)System.Math.Round(src.R * 255.0)];

                if (cls > 0 && cls < 7)
                {
                    int lx = ((px - ox[cls]) % GrainSide + GrainSide) % GrainSide;
                    int ly = ((py - oy[cls]) % GrainSide + GrainSide) % GrainSide;
                    var gp = grainImg.GetPixel(bankX[cls] + lx, bankY[cls] + ly);
                    double g = (gp.R * 255.0 - 128.0) / 64.0 * cfg.Coarse
                             + (gp.G * 255.0 - 128.0) / 64.0 * cfg.Fine;
                    L += offset[cls] + g * gmul[cls] + markDepth[py, px];
                }
                raw[py, px] = L;
            }
        }

        // THE ARRIS PASS. A joint beside a trodden stone is shallower, because feet round the
        // edges off — geometry, not light, and a subtraction rather than an addition. Each joint
        // pixel takes its wear from the stones it actually TOUCHES, bounds-checked, never wrapped.
        if (anyWorn && cfg.WearArris > 0.0)
        {
            for (int py = 0; py < T; py++)
            {
                for (int px = 0; px < T; px++)
                {
                    if (clsArr[py, px] != 0) continue;
                    bool nearWorn = false;
                    for (int d = 0; d < 4 && !nearWorn; d++)
                    {
                        int ny = py + (d == 0 ? -1 : d == 1 ? 1 : 0);
                        int nx = px + (d == 2 ? -1 : d == 3 ? 1 : 0);
                        if (ny < 0 || ny >= T || nx < 0 || nx >= T) continue;
                        int nc = clsArr[ny, nx];
                        if (nc > 0 && nc < 7 && wornClass[nc]) nearWorn = true;
                    }
                    if (nearWorn)
                        raw[py, px] += (cfg.LumMedian - raw[py, px]) * cfg.WearArris;
                }
            }
        }

        // ================= THE DIFFERENTIAL-WEAR PASS =================
        //
        // THE DEVICE GATE, second walk: "all the gaps look standardized... freshly laid and
        // mortared, like someone scoured new stone to make it look old." Ruled a register
        // violation: uniform joints are STAGED AGE, and wear is earned differentially.
        //
        // (a) a joint OPENS where feet passed — deeper, therefore darker. Keyed on world position
        //     alone, so both tiles either side of a boundary agree by construction.
        // (b) the stones beside an open joint lose their arrises: a pixel of stone goes with the
        //     joint, and sometimes a second — a corner gone.
        // ================= WEAR IGNORES THE TILE GRID =================
        //
        // RULED at the gate: "one tile worn, the one next to it not — that's not how it works."
        // ANY WEAR BOUNDARY COINCIDING WITH A TILE EDGE IS STAGED.
        //
        // Two per-tile evaluations were feeding every wear pixel in this cell, and both drew
        // their boundaries on the grid by construction:
        //
        //   THE CHANNEL FLAG was a per-tile boolean that raised the wear scalar to a floor for
        //   the whole tile — a hard step at the tile edge, in the joints, the chroma and the
        //   flatten alike. It predates the route model. With a route present the route IS the
        //   channel, so the legacy flag no longer applies at all.
        //
        //   THE TRAVEL AXIS was computed once at the tile and then handed to every joint pixel
        //   in it, so the compaction's DIRECTION changed on tile edges. It is per pixel now,
        //   from the same Nearest() the strength already costs.
        bool channelHere = isChannel != null && isChannel(tx, ty) && cfg.Lines.Count == 0;
        var openAmt = new double[T, T];
        for (int py = 0; py < T; py++)
            for (int px = 0; px < T; px++)
                if (clsArr[py, px] == 0)
                {
                    openAmt[py, px] = Wear01(cfg, WearScalar(cfg, traffic, tx * T + px, ty * T + py), channelHere);

                    // THE JOINT CARRIES THE TRAFFIC. Off-route it stays as deep and as dark as
                    // the bond drew it; trodden it is packed with grit, and a packed joint is a
                    // shallower one — lighter as a consequence of geometry, never as paint. Some
                    // of it fills level with the floor entirely, so the line between two stones
                    // stops being a line, which is what "stones wearing into one another" looks
                    // like at 32px.
                    //
                    // The BOND is untouched: the class mask still divides the stones, so every
                    // stone keeps its address and the corner theorem is unaffected. What degrades
                    // along a path is the VISIBLE enclosure, deliberately.
                    int age = AgeIndex(cfg, openAmt[py, px]);
                    int pxAxis = PixelAxis(cfg, traffic, tx * T + px, ty * T + py, tx, ty);
                    var (anisoBed, anisoHead) = AnisoWeights(cfg, pxAxis);
                    // A joint lying ACROSS the route is crossed and packed shut; one running WITH
                    // it stays open and dark. Along a north-south corridor the bed joints close
                    // and the head joints survive as continuous lines: the directional grain,
                    // out of the bond that was already there.
                    double kw = 1.0;
                    if (pxAxis >= 0)
                    {
                        bool bedJ = px > 0 && px < T - 1
                                    && clsArr[py, px - 1] == 0 && clsArr[py, px + 1] == 0;
                        bool headJ = py > 0 && py < T - 1
                                     && clsArr[py - 1, px] == 0 && clsArr[py + 1, px] == 0;
                        if (bedJ && !headJ) kw = anisoBed;
                        else if (headJ && !bedJ) kw = anisoHead;
                    }
                    int hb = Mix(tx * T + px, ty * T + py, cfg.JointBreakSalt + cfg.Seed);
                    if ((hb % 1000) / 1000.0 < cfg.JointBreak[age] * kw)
                        raw[py, px] = cfg.LumMedian;
                    else
                    {
                        // THE SHELTERED JOINT DRAWS ITS OWN DEPTH, then the route's fill packs it
                        // further. Drawn on a coarse world block so the depth varies ALONG a
                        // joint's run — mortar, not a drawn line — and world-keyed, so both tiles
                        // either side of a boundary draw the identical depth for the same pixel.
                        //
                        // CAPPED AT THE STONE'S OWN LEVEL. A packed joint rises toward the floor
                        // and stops there; uncapped, lift plus fill put 2% of joints ABOVE their
                        // stone, which is a joint that emits light — the exact inversion of "dark
                        // BECAUSE enclosed".
                        double lift = ShelterLift(cfg, tx * T + px, ty * T + py);
                        double up = System.Math.Min(lift + cfg.JointFill[age] * kw,
                                                    (cfg.LumMedian - raw[py, px]) / rung);
                        raw[py, px] += System.Math.Max(up, 0.0) * rung;
                    }
                }

        // ORDER IS LOAD-BEARING, and the paint check is what said so. These two passes run
        // HERE — after the joints compact, before the arrises chip — because that is where
        // the reference painter runs them. Flatten is a PROPORTIONAL pull toward the median
        // and chipping is a FIXED subtraction, so swapping them gives a different pixel with
        // no error anywhere to point at: composer rgb(90,107,100) against engine
        // rgb(102,121,113) at cell (2,7), one rung apart, both arithmetically correct.
        // GROUND LOWER AND FLATTER. A walked stone loses its crown: its value collapses toward
        // the material's median. A pull to the median is symmetric, so it takes contrast away
        // without darkening anything on average — a stone that merely got darker would be a
        // stone that got painted.
        for (int py = 0; py < T; py++)
            for (int px = 0; px < T; px++)
                if (clsArr[py, px] != 0)
                {
                    double fl = cfg.DeformFlatten[AgeIndex(cfg, Wear01(cfg,
                        WearScalar(cfg, traffic, tx * T + px, ty * T + py), channelHere))];
                    raw[py, px] += (cfg.LumMedian - raw[py, px]) * fl;
                }

        // ================= THE ADDITIVE LAYER =================
        //
        // Round 22 moved the failure axis: the signal is keyed to a real, coherent line and is
        // present on it, and is still too small to route by. Every lever before this one
        // SUBTRACTS — flattening removes value spread, compaction removes joints, chipping
        // removes arrises — and a floor made only of absence reads as unfinished rather than
        // used. These put something back, and all of them key off the same line geometry.
        for (int py = 0; py < T; py++)
            for (int px = 0; px < T; px++)
            {
                if (clsArr[py, px] == 0) continue;
                int wx = tx * T + px, wy = ty * T + py;
                double ld = cfg.Lines.Count == 0 ? 1e9
                    : Logic.ECS.RoutePolyline.Nearest(cfg.Lines, (wx + 0.5) / T, (wy + 0.5) / T).Dist;

                // DISHING ALONG THE LINE — deepest on the centre-line, gone by the shoulder. The
                // threshold hollows below are untouched and compose on top of this. Genuinely
                // lower stone with the rim shadow that implies.
                double jitD = (Mix(wx, wy, cfg.LaneDishSalt + cfg.Seed) % 100) / 500.0;
                double u = System.Math.Clamp(1.0 - ld / cfg.PolishShoulder - jitD, 0.0, 1.0);
                double dishL = u * u * cfg.LaneDishDepth;
                double rimL = (ld > cfg.PolishShoulder * 0.80 && ld < cfg.PolishShoulder * 1.05)
                              ? cfg.LaneDishRim : 0.0;
                // THE DISH STEPS. A smooth radial subtraction is airbrush on a quantised surface
                // — the critic's "large soft value-blobs that follow no geometry, they read as
                // airbrush, not as light or as material". Whole rungs read as depth.
                double dishTotal = dishL + rimL;
                if (cfg.DishQuantise) dishTotal = System.Math.Round(dishTotal);
                raw[py, px] -= dishTotal * rung;

                // MARGIN GRIT. Traffic sweeps the centre clean and drives what it lifts to the
                // flanks, so the swept lane reads as conspicuously bare BETWEEN gritty edges.
                // THE CONTRAST IS THE SIGNAL, not the grit — absence can only say something when
                // there is something either side of it.
                if (ld > cfg.GritInner && ld < cfg.GritOuter)
                {
                    double t = System.Math.Clamp((ld - cfg.GritInner) / (cfg.GritOuter - cfg.GritInner),
                                                 0.0, 1.0);
                    double rate = cfg.GritRate * (1.0 - t) * (1.0 - t);
                    if ((Mix(wx, wy, cfg.GritSalt + cfg.Seed) % 1000) / 1000.0 < rate)
                        raw[py, px] -= cfg.GritDepth * rung;
                }
            }

        // THRESHOLD HOLLOW. Where routes converge on a mouth the stone dishes: genuinely lower
        // in the middle, with a rim that shadows just inside its edge. Occlusion-legal by
        // construction — this is a recess, drawn as a recess — and salted so two mouths in one
        // level are not the same dish (§8.3.1: an incident repeated is a motif).
        if (isMouth)
        {
            double cc = (T - 1) / 2.0;
            for (int py = 0; py < T; py++)
                for (int px = 0; px < T; px++)
                {
                    if (clsArr[py, px] == 0) continue;
                    double dy = (py - cc) / cc, dx = (px - cc) / cc;
                    double rr = System.Math.Sqrt(dy * dy + dx * dx);
                    double jit = (Mix(tx * T + px, ty * T + py, cfg.HollowSalt + cfg.Seed) % 100)
                                 / 400.0;
                    double dish = System.Math.Clamp(1.0 - rr - jit, 0.0, 1.0) * cfg.HollowDepth;
                    double rim = (rr > 0.82 && rr < 1.02) ? cfg.HollowRim : 0.0;
                    raw[py, px] -= (dish + rim) * rung;
                }
        }


        for (int py = 0; py < T; py++)
        {
            for (int px = 0; px < T; px++)
            {
                if (clsArr[py, px] == 0) continue;
                double near = 0.0;
                if (py > 0) near = System.Math.Max(near, openAmt[py - 1, px]);
                if (py < T - 1) near = System.Math.Max(near, openAmt[py + 1, px]);
                if (px > 0) near = System.Math.Max(near, openAmt[py, px - 1]);
                if (px < T - 1) near = System.Math.Max(near, openAmt[py, px + 1]);
                if (near <= 0.0) continue;
                int h = Mix(tx * T + px, ty * T + py, cfg.ChipSalt + cfg.Seed);
                if ((h % 1000) / 1000.0 < cfg.ChipRate * near)
                {
                    // A CHIPPED ARRIS IS JOINT, NOT A BLEND ON THE WAY TO ONE. Subtracting a
                    // fraction of a rung put intermediate values along every open joint, which is
                    // the two-to-three pixel ramp the critic saw the slab edges dissolve into.
                    // The stone that broke away is gone: the pixel takes the joint's own value.
                    double jv = 0.0;
                    if (py > 0 && clsArr[py - 1, px] == 0) jv = System.Math.Max(jv, raw[py - 1, px]);
                    if (py < T - 1 && clsArr[py + 1, px] == 0) jv = System.Math.Max(jv, raw[py + 1, px]);
                    if (px > 0 && clsArr[py, px - 1] == 0) jv = System.Math.Max(jv, raw[py, px - 1]);
                    if (px < T - 1 && clsArr[py, px + 1] == 0) jv = System.Math.Max(jv, raw[py, px + 1]);
                    if (cfg.ChipTakesJoint && jv > 0.0) raw[py, px] = jv;
                    else raw[py, px] -= near * rung * 1.6;
                }
            }
        }

        // THE CRACK SET, WITH THE ARRISES THAT SPALL WHERE IT CROSSES A JOINT. The frame
        // critic: "a crack that crosses six slabs in one smooth curve without registering a
        // single joint reads as a line drawn over the floor." It cannot deflect — a crack is a
        // pure function of world position so that every tile it crosses draws the identical
        // line, and the bond is a per-atlas-variant property, not a world function; deflecting on
        // the real joints would make the crack disagree with itself on the tile boundaries.
        // What it can do, from purely local information, is BREAK THE ARRISES at the crossing:
        // wide at the bond, narrow across the slab. See compose_ashlar.crack_spall.
        var cset = new HashSet<(int, int)>();
        if (crackCache != null)
        {
            var craw = new HashSet<(int, int)>(CrackPixels(cfg, tx, ty, crackCache));
            foreach (var q in craw) cset.Add(q);
            if (cfg.CrackSpall)
                foreach (var (ly, lx) in craw)
                {
                    if (clsArr[ly, lx] == 0) continue;
                    bool touches =
                        (ly > 0 && clsArr[ly - 1, lx] == 0 && craw.Contains((ly - 1, lx)))
                     || (ly < T - 1 && clsArr[ly + 1, lx] == 0 && craw.Contains((ly + 1, lx)))
                     || (lx > 0 && clsArr[ly, lx - 1] == 0 && craw.Contains((ly, lx - 1)))
                     || (lx < T - 1 && clsArr[ly, lx + 1] == 0 && craw.Contains((ly, lx + 1)));
                    if (!touches) continue;
                    bool vert = craw.Contains((ly - 1, lx)) || craw.Contains((ly + 1, lx));
                    var cand = vert
                        ? new[] { (ly, lx - 1), (ly, lx + 1) }
                        : new[] { (ly - 1, lx), (ly + 1, lx) };
                    foreach (var (ny, nx) in cand)
                    {
                        if (ny < 0 || ny >= T || nx < 0 || nx >= T) continue;
                        if (clsArr[ny, nx] == 0 || craw.Contains((ny, nx))) continue;
                        cset.Add((ny, nx));
                    }
                }
        }

        // A CRACK HAS A SECTION. The frame critic: "a uniform 1px black stroke has no depth."
        // A real fracture has a LIP where the stone broke away on one side and a channel below
        // it. The lip goes in HERE, before quantisation, so it lands on a rung like everything
        // else — and the crack pixels themselves are still painted last, over the top.
        if (crackCache != null && cfg.CrackLip != 0.0)
        {
            foreach (var (ly, lx) in cset)
            {
                int side = (int)(Mix((tx * T + lx) / 4, (ty * T + ly) / 4,
                                     cfg.CrackLipSalt + cfg.Seed) % 2);
                int lx2 = lx + (side != 0 ? 1 : -1);
                if (lx2 < 0 || lx2 >= T) continue;
                if (clsArr[ly, lx2] == 0 || cset.Contains((ly, lx2))) continue;
                raw[ly, lx2] += cfg.CrackLip * rung;
            }
        }

        // ================= §12.1's CONTACT OCCLUSION, IN RUNGS =================
        //
        // LAST, because the sprite it replaces was drawn last — over the stones, the joints and
        // the cracks alike. Order is load-bearing in this painter (see the flatten/chip note
        // above) and the safest reproduction of a layer that sat on top of everything is a term
        // that runs after everything.
        //
        // The sides composite the way the sprites did: each was a separate draw, so their alphas
        // multiply through their complements and a corner carrying two edges is darker than
        // either. `layers` then compounds that the same way. What is new is only the last step —
        // the result is whole rungs, and a whole number of rungs off a ladder value is a ladder
        // value, so the snap below has nothing left to invent.
        // AND THE ENCLOSURE IS REMEMBERED, not merely subtracted — see the polish note below.
        // Null everywhere the boundary is not drawn, so a cell with no wall beside it allocates
        // nothing and is byte-identical to the build before this term existed.
        double[,]? occEnclosure = null;
        if (occlusion is { } occ && occlusionAlpha != null)
        {
            for (int py = 0; py < T; py++)
                for (int px = 0; px < T; px++)
                {
                    double keep = 1.0;
                    for (int side = 0; side < 4; side++)
                    {
                        if ((occ.SideMask & (1 << side)) == 0) continue;
                        var img = occlusionAlpha[side];
                        if (img == null) continue;
                        keep *= 1.0 - img.GetPixel(px, py).A;
                    }
                    if (keep >= 1.0) continue;
                    int rungs = OcclusionRungs(cfg, 1.0 - keep, occ.Layers);
                    raw[py, px] -= rungs * (cfg.Ladder[1] - cfg.Ladder[0]);
                    if (rungs <= 0) continue;
                    occEnclosure ??= new double[T, T];
                    // HOW DEEP THE BOUNDARY IS, as a fraction of the depth this family can
                    // represent — the same denominator the joint's `filled` uses, so the two
                    // recesses are described on one scale rather than on two.
                    occEnclosure[py, px] = System.Math.Clamp(
                        rungs * (cfg.Ladder[1] - cfg.Ladder[0])
                        / System.Math.Max(cfg.LumMedian - cfg.Ladder[0], 1e-6), 0.0, 1.0);
                }
        }

        // ================= THE CHROMA CHANNEL =================
        //
        // Step two of the pre-declared ladder, after the joint lever was discharged BY PROOF: a
        // lever confined to the joints owns 21.85% of the surface and cannot reach §13.8's floor
        // at any setting. This one runs on the 78.14% the joints never touch.
        //
        // A ratio between channels survives the light rig's multiplication, which an authored
        // value difference does not — and more than half this floor sits below luminance 70,
        // where value work is spent where nobody can see it.
        //
        // FACES ONLY, and at constant luminance. A joint is dark because it is ENCLOSED, and
        // enclosure has no hue; a colour that also darkened would be an occlusion claim with no
        // recess behind it.
        //
        // Read from the UNMASKED wear scalar, not from `openAmt` — that array is the same scalar
        // masked to joints, and indexing chroma with it would give every stone face age 0 and
        // ship a floor with no chroma channel while the reference painter drew one.
        var tints = new double[cfg.ChromaByAge.Length][];
        for (int i = 0; i < tints.Length; i++) tints[i] = ChromaTint(cfg, i);

        // ================= POLISH: THE MASK, NOT THE BRIGHTNESS =================
        //
        // A trodden stone reflects more. That is a response to light, and it is written into a
        // separate single-channel mask that only the shader's light() pass ever reads — never
        // into the colour below. Faces only: a joint is dark because it is ENCLOSED and a crack
        // is a hole, and neither takes a shine.
        var outImg = Image.CreateEmpty(T, T, false, Image.Format.Rgb8);
        var polishImg = Image.CreateEmpty(T, T, false, Image.Format.L8);
        for (int py = 0; py < T; py++)
        {
            for (int px = 0; px < T; px++)
            {
                double L = cfg.Ladder[LadderIndex(cfg, System.Math.Clamp(
                    raw[py, px], cfg.Ladder[0], cfg.Ladder[^1]))];
                double[] t = cfg.Tint;
                // ================= A PACKED JOINT TAKES THE SHINE =================
                //
                // RULED after the walk contradicted the table. The polish mask was set on STONE
                // FACES ONLY and every joint got zero — so under the lamp the faces gained a
                // superlinear specular and the joints gained nothing, and the delivered
                // face-to-joint contrast was amplified by exactly the light. Measured by nulling
                // the gain: in the lit band the outline share falls 16.3% -> 1.3%, a twelvefold
                // drop, while the dark band barely moves. The source was clean the whole time;
                // the renderer was drawing the ring. That is §13.9's converse.
                //
                // The fix is physical rather than a cap. A joint PACKED LEVEL WITH ITS STONE is
                // not a recess any more — it is walked surface, and walked surface shines. So a
                // joint's share of the polish is how FILLED it is: none while it is still a hole,
                // all of it once it is level. Nothing else changes, and a deep joint stays matte
                // because a deep joint is still a hole.
                double refl = 0.0;
                if (clsArr[py, px] == 0)
                {
                    double bottom = cfg.Ladder[0];
                    // AND THE RATIO IS CAPPED. Letting a joint's shine track how filled it is
                    // is physically right and, alone, moved the delivered outline only 16.3% ->
                    // 14.4% in the lit band: the deep minority still went matte beside faces
                    // carrying a 1.9 gain, and THAT ratio is what draws the line. So the joint's
                    // share has a floor — no joint is more than (1 - floor) below the face beside
                    // it in specular, however deep it is. The story survives: grit packed into a
                    // deep joint in a polished lane catches some of the same sheen.
                    double filled = System.Math.Clamp(
                        (raw[py, px] - bottom) / System.Math.Max(cfg.LumMedian - bottom, 1e-6),
                        cfg.JointPolishFloor, 1.0);
                    if (filled > 0.0)
                        refl = LanePolish(cfg, tx * T + px, ty * T + py) * filled;
                }
                if (clsArr[py, px] != 0)
                {
                    int fa = AgeIndex(cfg, Wear01(cfg,
                        WearScalar(cfg, traffic, tx * T + px, ty * T + py), channelHere));
                    t = tints[fa];
                    refl = cfg.PolishByAge[fa];

                    // THE SPECULAR LANE, OFF THE LINE RATHER THAN OFF THE FRAYED FIELD.
                    //
                    // The polish has read the wear scalar since it was built, and that scalar is
                    // traffic FRAYED BY NOISE — right for AGE, since a path's edges should break
                    // up rather than end on a pixel, and wrong for a LANE: a specular streak
                    // chopped into noise cannot be followed. Width now comes from the line
                    // distance directly, so the lane runs continuous down the centre; the noise
                    // returns at its shoulders through the age layer underneath.
                    refl = System.Math.Max(refl, LanePolish(cfg, tx * T + px, ty * T + py));
                }

                // ================= THE CONTACT BOUNDARY TAKES NO SHINE =================
                //
                // ISSUE #184, second reading, ruled at the 2026-09-07 room walk: *"the worn lane
                // is slightly too shiny and its shine washes out the wall-base occlusion shadow,
                // so walls lose mass where the lane meets them."*
                //
                // THE ARITHMETIC OF THE WASH, because it is a bigger effect than it sounds. The
                // contact occlusion is a SUBTRACTION FROM THE ALBEDO — at the seam's deepest row
                // (sprite alpha 0.72, one layer) it is 5 rungs, 66 luminance units. The specular
                // is an ADDITION IN light(), `polish * gain * delivered^2` on LIGHT_COLOR, and on
                // a lane pixel `polish` reaches the lane gain 0.6 — about 153 levels of red at
                // full delivery. The seam was being subtracted from the stone and then handed
                // back, with interest, by a term that had never heard of it. Measured on the
                // approved capture (8745c556): the seam reads Weber 0.3085 on flank cells and
                // 0.0094 on the one wall-adjacent lane cell in view — a lane/flank ratio of
                // 0.030. Ninety-seven per cent of the boundary, gone exactly where the player
                // walks.
                //
                // THE FIX IS THE ONE THIS FILE HAS ALREADY MADE TWICE, and it is physics rather
                // than a cap. A joint takes the lane's shine in proportion to how FILLED it is; a
                // crack takes it "at the same fraction a joint of that depth would". The contact
                // boundary is the deepest enclosure in the floor plane — it is where the ground
                // stops — so it takes the same treatment on the same scale. §12.1 rules
                // plane-boundary occlusion FORM, and form does not fade because something is
                // shining on it.
                //
                // ⚠ IT IS NOT AN OUTLINE AND CANNOT BECOME ONE (§12.1). The attenuation exists
                // only where the boundary is drawn — only on the side a wall actually adjoins,
                // never where wall meets wall — and its profile is the shipped sprite's own
                // alpha ramp, jitter and all. A ring is drawn round a thing because it is a
                // thing; this answers to the geometry between two planes and to nothing else.
                //
                // NULL CONTROL: `occlusion_polish_floor: 1.0` makes this the identity everywhere
                // and the build byte-identical to the one before it.
                if (occEnclosure != null && occEnclosure[py, px] > 0.0)
                    refl *= System.Math.Clamp(1.0 - occEnclosure[py, px],
                                              cfg.OcclusionPolishFloor, 1.0);

                outImg.SetPixel(px, py, new Color(
                    (float)(L * t[0] / 255.0), (float)(L * t[1] / 255.0),
                    (float)(L * t[2] / 255.0)));
                polishImg.SetPixel(px, py, new Color((float)refl, (float)refl, (float)refl));
            }
        }

        // THE CRACK NETWORK, drawn last so it crosses stones and joints alike. Authored pixels
        // on the family's own ladder — not an overlay, no alpha, no feather, no taper. The
        // per-tile overlay this replaces had a median mark of four pixels and blind seats
        // reported "No cracks. Not one." in captures whose log said event=44.
        if (crackCache != null)
        {
            // THE CRACK VARIES ALONG ITS LENGTH. One value end to end is a drawn line; the
            // frame critic named it twice as "the identical overlay, uniform 1px black". Keyed on
            // world position so the fracture varies as it travels and both tiles agree.
            foreach (var (ly, lx) in cset)
            {
                double vv = ((Mix(tx * T + lx, ty * T + ly, cfg.CrackVarySalt + cfg.Seed) % 1000)
                             / 1000.0 - 0.5) * 2.0;
                double cvp = cfg.LumMedian * cfg.CrackDepth * (1.0 + vv * cfg.CrackDepthVary);
                double cv2 = cfg.Ladder[LadderIndex(cfg, cvp)];
                outImg.SetPixel(lx, ly, new Color(
                    (float)(cv2 * cfg.Tint[0] / 255.0), (float)(cv2 * cfg.Tint[1] / 255.0),
                    (float)(cv2 * cfg.Tint[2] / 255.0)));

                // A CRACK IS LIT LIKE EVERYTHING ELSE IT IS CUT INTO. The frame critic: "they are
                // pure black inside the brightest part of the lamp pool, which makes them the
                // darkest thing in the frame; they need to be lit along with the surface they're
                // in." Giving cracks a flat zero polish was the same mistake the joints had —
                // fixed there, missed here — and it is worse for a crack, because the surface
                // around it is the polished lane and the contrast is therefore largest exactly
                // where the player is looking.
                //
                // A recess catches less light, not none. It takes the lane's shine at the same
                // fraction a joint of that depth would.
                double crackLit = System.Math.Clamp(
                    (cv2 - cfg.Ladder[0]) / System.Math.Max(cfg.LumMedian - cfg.Ladder[0], 1e-6),
                    cfg.JointPolishFloor, 1.0);
                double cpol = LanePolish(cfg, tx * T + lx, ty * T + ly) * crackLit;
                // A crack running under the contact boundary is enclosed twice; the boundary's
                // attenuation applies here for the same reason it applies above, and this loop
                // overwrites the mask so it has to be applied again rather than inherited.
                if (occEnclosure != null && occEnclosure[ly, lx] > 0.0)
                    cpol *= System.Math.Clamp(1.0 - occEnclosure[ly, lx],
                                              cfg.OcclusionPolishFloor, 1.0);
                polishImg.SetPixel(lx, ly, new Color((float)cpol, (float)cpol, (float)cpol));
            }
        }

        polish = polishImg;
        return outImg;
    }

    /// <summary>
    /// DOES THE ENGINE PRODUCE THE RIGHT PIXELS, not merely the right numbers?
    ///
    /// The edge-family and stone-offset vectors prove this code agrees with the composer about
    /// which family a boundary has and how many ladder steps a stone moves. They prove nothing
    /// about the two largest pieces of arithmetic in the painter: WHERE IN THE GRAIN BANK a stone
    /// samples, and WHICH JOINTS the arris pass rounds. Both could have been wrong in a way that
    /// produced a plausible floor, on the device, with every existing check green.
    ///
    /// So the manifest carries finished RGB for a scatter of pixels — joints, plain stone, trodden
    /// stone, and joints beside trodden stone — and this refuses to lay anything if a single one
    /// of them disagrees.
    /// </summary>
    private static string? SelfCheck(Config cfg, Image grainImg,
                                     Dictionary<int, Image> atlasCache)
    {
        if (cfg.PaintCheck.Count == 0) return null;
        var checkCracks = new Dictionary<(int, int), List<(int X, int Y)>>();
        var cols = new HashSet<int>(cfg.PaintCheckWornColumns);
        System.Func<int, int, bool> worn = (x, y) => cols.Contains(x);
        var cells = new Dictionary<(int, int), Image>();

        foreach (var s in cfg.PaintCheck)
        {
            if (!cells.TryGetValue((s.X, s.Y), out var img))
            {
                int n = EdgeFamily(s.X, s.Y, cfg.HorizSalt, cfg.Seed, cfg.Families);
                int so = EdgeFamily(s.X, s.Y + 1, cfg.HorizSalt, cfg.Seed, cfg.Families);
                int fw = EdgeFamily(s.X, s.Y, cfg.VertSalt, cfg.Seed, cfg.Families);
                int fe = EdgeFamily(s.X + 1, s.Y, cfg.VertSalt, cfg.Seed, cfg.Families);
                int idx = TileIndex(n, fe, so, fw, cfg.Families);
                if (!cfg.Atlas.TryGetValue(idx, out var path)) return
                    $"paint check: no atlas for tile index {idx} at ({s.X},{s.Y})";
                if (!atlasCache.TryGetValue(idx, out var atlas))
                {
                    atlas = LoadImage(path);
                    if (atlas == null) return $"paint check: atlas unreadable: {path}";
                    atlasCache[idx] = atlas;
                }
                var liveLines = cfg.Lines;
                var liveAxis = linesForAxis;
                cfg.Lines = cfg.CheckLines;
                linesForAxis = cfg.CheckLines;
                img = PaintCell(cfg, atlas, grainImg, s.X, s.Y, fw, fe, cfg.CheckTraffic, worn,
                                checkCracks, false, out _, out _);
                cfg.Lines = liveLines;
                linesForAxis = liveAxis;
                cells[(s.X, s.Y)] = img;
            }
            var c = img.GetPixel(s.Px, s.Py);
            int r = (int)System.Math.Round(c.R * 255.0);
            int g = (int)System.Math.Round(c.G * 255.0);
            int bl = (int)System.Math.Round(c.B * 255.0);
            if (r != s.R || g != s.G || bl != s.B)
                return $"paint check FAILED at cell ({s.X},{s.Y}) pixel ({s.Px},{s.Py}): "
                     + $"composer says rgb({s.R},{s.G},{s.B}), engine paints rgb({r},{g},{bl}). "
                     + $"The two agree about the numbers and disagree about the picture.";
        }
        return null;
    }

    private static Image? LoadImage(string resPath)
    {
        var tex = GD.Load<Texture2D>(resPath);
        return tex?.GetImage();
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
                GrainBank = root.GetProperty("grain_bank").GetInt32(),
                GrainAmp = root.GetProperty("grain_amp").GetDouble(),
            };
            var salts = root.GetProperty("salts");
            cfg.HorizSalt = salts.GetProperty("horizontal").GetInt32();
            cfg.SplitSalt = salts.GetProperty("split").GetInt32();
            cfg.VertSalt = salts.GetProperty("vertical").GetInt32();
            cfg.SpanSalt = salts.GetProperty("span").GetInt32();
            cfg.InteriorSalt = salts.GetProperty("interior").GetInt32();
            cfg.DropSalt = salts.GetProperty("drop").GetInt32();
            cfg.ClusterSalt = salts.GetProperty("cluster").GetInt32();

            var gs = root.GetProperty("grain_scales");
            cfg.Coarse = gs.GetProperty("coarse").GetDouble();
            cfg.Fine = gs.GetProperty("fine").GetDouble();
            cfg.WornMul = gs.GetProperty("worn_multiplier").GetDouble();
            var wear = root.GetProperty("wear");
            cfg.WearSpread = wear.GetProperty("spread").GetDouble();
            cfg.WearArris = wear.GetProperty("arris").GetDouble();
            cfg.WearBands = wear.GetProperty("bands").GetInt32();
            cfg.WearPits = wear.GetProperty("pits").GetInt32();
            cfg.MarkBands = wear.GetProperty("bands_ordinary").GetInt32();
            cfg.MarkPits = wear.GetProperty("pits_ordinary").GetInt32();


            var steps = new List<int>();
            foreach (var v in root.GetProperty("offset_steps").EnumerateArray())
                steps.Add(v.GetInt32());
            cfg.OffsetSteps = steps.ToArray();

            var ct = new List<int>();
            foreach (var v in root.GetProperty("cluster_table").EnumerateArray()) ct.Add(v.GetInt32());
            cfg.ClusterTable = ct.ToArray();

            var mat = root.GetProperty("material");
            var lad = new List<double>();
            foreach (var v in mat.GetProperty("ladder").EnumerateArray()) lad.Add(v.GetDouble());
            cfg.Ladder = lad.ToArray();
            cfg.LumMedian = mat.GetProperty("lum_median").GetDouble();
            var tint = new List<double>();
            foreach (var v in mat.GetProperty("tint").EnumerateArray()) tint.Add(v.GetDouble());
            cfg.Tint = tint.ToArray();
            var cba = new List<double>();
            foreach (var v in mat.GetProperty("chroma_by_age").EnumerateArray()) cba.Add(v.GetDouble());
            cfg.ChromaByAge = cba.ToArray();
            var cdir = new List<double>();
            foreach (var v in mat.GetProperty("chroma_dir").EnumerateArray()) cdir.Add(v.GetDouble());
            cfg.ChromaDir = cdir.ToArray();
            var pba = new List<double>();
            foreach (var v in mat.GetProperty("polish_by_age").EnumerateArray()) pba.Add(v.GetDouble());
            cfg.PolishByAge = pba.ToArray();
            if (mat.TryGetProperty("occlusion_floor", out var of)) cfg.OcclusionFloor = of.GetDouble();
            cfg.PolishExp = mat.GetProperty("polish_exp").GetDouble();
            cfg.PolishGain = mat.GetProperty("polish_gain").GetDouble();
            if (mat.TryGetProperty("shoulder_knee", out var sk)) cfg.ShoulderKnee = sk.GetDouble();
            if (mat.TryGetProperty("shoulder_ceiling", out var sc)) cfg.ShoulderCeiling = sc.GetDouble();
            if (mat.TryGetProperty("spec_shade", out var ss)) cfg.SpecShade = ss.GetDouble();
            // DERIVED, NEVER COPIED (§13.12). The median albedo the specular is normalised by is
            // the family's own `lum_median`, which the compositor measured off the donors — so it
            // moves when the family does, and no second number can drift away from the first.
            if (mat.TryGetProperty("lum_median", out var lm))
                cfg.AlbedoMedian = lm.GetDouble() / 255.0;
            var dfl = new List<double>();
            foreach (var v in mat.GetProperty("deform_flatten").EnumerateArray()) dfl.Add(v.GetDouble());
            cfg.DeformFlatten = dfl.ToArray();
            cfg.DeformAniso = mat.GetProperty("deform_aniso").GetDouble();
            cfg.HollowDepth = mat.GetProperty("hollow_depth").GetDouble();
            cfg.HollowRim = mat.GetProperty("hollow_rim").GetDouble();
            cfg.HollowSalt = salts.GetProperty("hollow").GetInt32();
            cfg.StriaSalt = salts.GetProperty("stria").GetInt32();
            cfg.LaneDishSalt = salts.GetProperty("lane_dish").GetInt32();
            cfg.GritSalt = salts.GetProperty("grit").GetInt32();
            cfg.ShelterSalt = salts.GetProperty("shelter").GetInt32();
            var sl = new List<double>();
            foreach (var v in mat.GetProperty("shelter_lift").EnumerateArray()) sl.Add(v.GetDouble());
            cfg.ShelterLift = sl.ToArray();
            var sw = new List<double>();
            foreach (var v in mat.GetProperty("shelter_weights").EnumerateArray()) sw.Add(v.GetDouble());
            cfg.ShelterWeights = sw.ToArray();
            cfg.ShelterBlock = mat.GetProperty("shelter_block").GetInt32();
            cfg.CrackVarySalt = salts.GetProperty("crack_vary").GetInt32();
            cfg.CrackDepthVary = mat.GetProperty("crack_depth_vary").GetDouble();
            cfg.MarkBareShare = mat.GetProperty("mark_bare_share").GetDouble();
            cfg.ChipTakesJoint = mat.GetProperty("chip_takes_joint").GetBoolean();
            cfg.DishQuantise = mat.GetProperty("dish_quantise").GetBoolean();
            cfg.CrackSpall = mat.GetProperty("crack_spall").GetBoolean();
            cfg.CrackLip = mat.GetProperty("crack_lip").GetDouble();
            cfg.CrackLipSalt = salts.GetProperty("crack_lip").GetInt32();
            cfg.MarkClusterSalt = salts.GetProperty("mark_cluster").GetInt32();
            cfg.MarkClusterPeriod = mat.GetProperty("mark_cluster_period").GetInt32();
            cfg.MarkClusterSwing = mat.GetProperty("mark_cluster_swing").GetDouble();
            cfg.LaneFraySalt = salts.GetProperty("lane_fray").GetInt32();
            cfg.LaneFray = mat.GetProperty("lane_fray").GetDouble();
            cfg.JointPolishFloor = mat.GetProperty("joint_polish_floor").GetDouble();
            if (mat.TryGetProperty("occlusion_polish_floor", out var opf))
                cfg.OcclusionPolishFloor = opf.GetDouble();
            var pl = mat.GetProperty("polish_lane");
            cfg.PolishLaneGain = pl[0].GetDouble();
            cfg.PolishLaneWidth = pl[1].GetDouble();
            cfg.PolishShoulder = pl[2].GetDouble();
            var st = mat.GetProperty("striation");
            cfg.StriaPeriod = st[0].GetInt32();
            cfg.StriaDepth = st[1].GetDouble();
            var ldh = mat.GetProperty("lane_dish");
            cfg.LaneDishDepth = ldh[0].GetDouble();
            cfg.LaneDishRim = ldh[1].GetDouble();
            var gr = mat.GetProperty("grit");
            cfg.GritInner = gr[0].GetDouble();
            cfg.GritOuter = gr[1].GetDouble();
            cfg.GritRate = gr[2].GetDouble();
            cfg.GritDepth = gr[3].GetDouble();
            // THE ONE ASSERTION THAT KEEPS THIS LEVER HONEST. At an exponent of 1.0 the specular
            // term is linear in delivered light, which is arithmetically identical to changing the
            // stone's albedo — the baked value-lift §8.2.1 bans, wearing this lever's name. It is
            // checked here rather than trusted to a comment.
            if (cfg.PolishExp <= 1.0)
                throw new System.InvalidOperationException(
                    $"polish_exp is {cfg.PolishExp}: at or below 1.0 the polish lever IS a baked "
                    + "value-lift, which §8.2.1 bans. Response modulation must be superlinear.");

            static int[][] Table(JsonElement e)
            {
                var rows = new List<int[]>();
                foreach (var r in e.EnumerateArray())
                {
                    var row = new List<int>();
                    foreach (var v in r.EnumerateArray()) row.Add(v.GetInt32());
                    rows.Add(row.ToArray());
                }
                return rows.ToArray();
            }
            cfg.ATable = Table(root.GetProperty("a_table"));
            cfg.Splits = Table(root.GetProperty("splits"));

            var cr = root.GetProperty("crack");
            cfg.CrackSalt = salts.GetProperty("crack").GetInt32();
            cfg.CrackRate = cr.GetProperty("rate").GetInt32();
            cfg.CrackMinTiles = cr.GetProperty("min_tiles").GetInt32();
            cfg.CrackMaxTiles = cr.GetProperty("max_tiles").GetInt32();
            cfg.CrackScale = cr.GetProperty("scale").GetInt32();
            cfg.CrackTurn = cr.GetProperty("turn").GetInt32();
            cfg.CrackDepth = cr.GetProperty("depth").GetDouble();
            cfg.CrackDirs = Table(cr.GetProperty("dirs"));

            var mk = root.GetProperty("marks");
            cfg.MarksSalt = salts.GetProperty("marks").GetInt32();
            cfg.MarkMinLen = mk.GetProperty("min_len").GetInt32();
            cfg.MarkMaxLen = mk.GetProperty("max_len").GetInt32();
            cfg.MarkDepth = mk.GetProperty("depth").GetDouble();
            cfg.PitDepth = mk.GetProperty("pit_depth").GetDouble();
            cfg.MarkDirs = Table(mk.GetProperty("dirs"));

            var df = root.GetProperty("differential");
            cfg.WearSalt = salts.GetProperty("wear").GetInt32();
            cfg.ChipSalt = salts.GetProperty("chip").GetInt32();
            cfg.WearOctaves = Table(df.GetProperty("octaves"));
            cfg.WearLo = df.GetProperty("lo").GetInt32();
            cfg.WearHi = df.GetProperty("hi").GetInt32();
            cfg.JointBreakSalt = salts.GetProperty("joint_break").GetInt32();
            var jf = new List<double>();
            foreach (var v in df.GetProperty("joint_fill").EnumerateArray()) jf.Add(v.GetDouble());
            cfg.JointFill = jf.ToArray();
            var jb = new List<double>();
            foreach (var v in df.GetProperty("joint_break").EnumerateArray()) jb.Add(v.GetDouble());
            cfg.JointBreak = jb.ToArray();
            cfg.ChipRate = df.GetProperty("chip_rate").GetDouble();
            cfg.DressingKeep = df.GetProperty("dressing_keep").GetDouble();
            cfg.ChannelWear = df.GetProperty("channel_wear").GetInt32();
            var ages = new List<double>();
            foreach (var v in df.GetProperty("ages").EnumerateArray()) ages.Add(v.GetDouble());
            cfg.WearAges = ages.ToArray();
            cfg.MvTable = Table(root.GetProperty("mv_table"));

            foreach (var e in root.GetProperty("base").EnumerateArray())
            {
                int idx = TileIndex(e.GetProperty("n").GetInt32(), e.GetProperty("e").GetInt32(),
                                    e.GetProperty("s").GetInt32(), e.GetProperty("w").GetInt32(),
                                    cfg.Families);
                cfg.Atlas[idx] = dir + e.GetProperty("file").GetString();
            }
            cfg.GrainPath = dir + root.GetProperty("grain_file").GetString();

            foreach (var e in root.GetProperty("edge_family_check").EnumerateArray())
                cfg.EdgeCheck.Add((e.GetProperty("x").GetInt32(), e.GetProperty("y").GetInt32(),
                                   e.GetProperty("salt").GetInt32(),
                                   e.GetProperty("family").GetInt32()));
            if (root.TryGetProperty("occlusion_check", out var occChk))
                foreach (var e in occChk.EnumerateArray())
                    cfg.OcclusionCheck.Add((e.GetProperty("side").GetString() ?? "",
                                            e.GetProperty("px").GetInt32(),
                                            e.GetProperty("py").GetInt32(),
                                            e.GetProperty("layers").GetInt32(),
                                            e.GetProperty("rungs").GetInt32()));
            foreach (var e in root.GetProperty("stone_check").EnumerateArray())
                cfg.StoneCheck.Add((e.GetProperty("x").GetInt32(), e.GetProperty("k").GetInt32(),
                                    e.GetProperty("kind").GetInt32(), e.GetProperty("drop").GetInt32(),
                                    e.GetProperty("steps").GetInt32()));

            if (root.TryGetProperty("paint_check", out var pc))
            {
                var wc = new List<int>();
                foreach (var v in pc.GetProperty("worn_columns").EnumerateArray())
                    wc.Add(v.GetInt32());
                cfg.PaintCheckWornColumns = wc.ToArray();
                if (pc.TryGetProperty("traffic", out var tr))
                {
                    var rowsList = new List<int[]>();
                    foreach (var row in tr.EnumerateArray())
                    {
                        var vals = new List<int>();
                        foreach (var v in row.EnumerateArray()) vals.Add(v.GetInt32());
                        rowsList.Add(vals.ToArray());
                    }
                    if (rowsList.Count > 0)
                    {
                        var t = new byte[rowsList[0].Length, rowsList.Count];
                        for (int yy = 0; yy < rowsList.Count; yy++)
                            for (int xx = 0; xx < rowsList[yy].Length; xx++)
                                t[xx, yy] = (byte)rowsList[yy][xx];
                        cfg.CheckTraffic = t;
                        if (pc.TryGetProperty("route", out var rt))
                        {
                            var pts = new System.Collections.Generic.List<(double X, double Y)>();
                            foreach (var pr in rt.EnumerateArray())
                            {
                                var e = pr.EnumerateArray().GetEnumerator();
                                e.MoveNext(); double rx = e.Current.GetDouble();
                                e.MoveNext(); double ry = e.Current.GetDouble();
                                pts.Add((rx, ry));
                            }
                            if (pts.Count >= 2)
                                cfg.CheckLines.Add(new Logic.ECS.RoutePolyline.Line(pts, 1.0));
                        }
                    }
                }
                foreach (var e in pc.GetProperty("samples").EnumerateArray())
                    cfg.PaintCheck.Add((e.GetProperty("x").GetInt32(), e.GetProperty("y").GetInt32(),
                                        e.GetProperty("px").GetInt32(), e.GetProperty("py").GetInt32(),
                                        e.GetProperty("r").GetInt32(), e.GetProperty("g").GetInt32(),
                                        e.GetProperty("b").GetInt32()));
            }

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
