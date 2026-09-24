namespace UnderWarden.Logic.Map;

/// <summary>
/// Minimal 2D simplex noise implementation for floor composition variation.
///
/// Pure math — no Godot dependencies. Deterministic for the same inputs.
/// Based on Stefan Gustavson's simplex noise algorithm (2012).
///
/// Used by FloorComposer to generate spatially coherent noise-driven tile variation.
/// Clusters of accent tiles emerge organically because simplex noise is smooth and
/// continuous — unlike hash-based approaches that produce salt-and-pepper patterns.
/// </summary>
public static class SimplexNoise
{
    // Standard Gustavson permutation table (256-value deterministic shuffle).
    // Doubled to 512 to avoid modulo 256 everywhere.
    private static readonly int[] _perm = BuildPerm();

    private static int[] BuildPerm()
    {
        // The 256-value starting sequence from Gustavson's reference implementation.
        int[] p =
        {
            151, 160, 137,  91,  90,  15, 131,  13, 201,  95,  96,  53, 194, 233,   7, 225,
            140,  36, 103,  30,  69, 142,   8,  99,  37, 240,  21,  10,  23, 190,   6, 148,
            247, 120, 234,  75,   0,  26, 197,  62,  94, 252, 219, 203, 117,  35,  11,  32,
             57, 177,  33,  88, 237, 149,  56,  87, 174,  20, 125, 136, 171, 168,  68, 175,
             74, 165,  71, 134, 139,  48,  27, 166,  77, 146, 158, 231,  83, 111, 229, 122,
             60, 211, 133, 230, 220, 105,  92,  41,  55,  46, 245,  40, 244, 102, 143,  54,
             65,  25,  63, 161,   1, 216,  80,  73, 209,  76, 132, 187, 208,  89,  18, 169,
            200, 196, 135, 130, 116, 188, 159,  86, 164, 100, 109, 198, 173, 186,   3,  64,
             52, 217, 226, 250, 124, 123,   5, 202,  38, 147, 118, 126, 255,  82,  85, 212,
            207, 206,  59, 227,  47,  16,  58,  17, 182, 189,  28,  42, 223, 183, 170, 213,
            119, 248, 152,   2,  44, 154, 163,  70, 221, 153, 101, 155, 167,  43, 172,   9,
            129,  22,  39, 253,  19,  98, 108, 110,  79, 113, 224, 232, 178, 185, 112, 104,
            218, 246,  97, 228, 251,  34, 242, 193, 238, 210, 144,  12, 191, 179, 162, 241,
             81,  51, 145, 235, 249,  14, 239, 107,  49, 192, 214,  31, 181, 199, 106, 157,
            184,  84, 204, 176, 115, 121,  50,  45, 127,   4, 150, 254, 138, 236, 205,  93,
            222, 114,  67,  29,  24,  72, 243, 141, 128, 195,  78,  66, 215,  61, 156, 180,
        };

        // Double to 512 to allow index without modulo 256
        var perm = new int[512];
        for (int i = 0; i < 512; i++)
            perm[i] = p[i & 255];
        return perm;
    }

    // Gradient vectors for 2D simplex noise (8 directions)
    private static readonly (float, float)[] _grad2 =
    {
        ( 1f,  1f), (-1f,  1f), ( 1f, -1f), (-1f, -1f),
        ( 1f,  0f), (-1f,  0f), ( 0f,  1f), ( 0f, -1f),
    };

    /// <summary>
    /// Returns a value in [-1, 1]. Deterministic for the same inputs.
    ///
    /// Frequency of the noise is controlled by the caller — multiply x/y by a scale
    /// factor before passing in. E.g., x * 0.25f gives low-frequency blobs suitable
    /// for floor accent clusters.
    /// </summary>
    public static float Evaluate(float x, float y)
    {
        // Skewing constants for 2D simplex grid
        const float F2 = 0.3660254f;  // (sqrt(3) - 1) / 2
        const float G2 = 0.2113249f;  // (3 - sqrt(3)) / 6

        // Skew input to find which simplex cell (i, j) the point is in
        float s = (x + y) * F2;
        int i = FastFloor(x + s);
        int j = FastFloor(y + s);

        // Unskew back to (x, y) space for the corner contributions
        float t = (i + j) * G2;
        float x0 = x - (i - t);
        float y0 = y - (j - t);

        // Determine which simplex triangle: lower-left or upper-right
        int i1, j1;
        if (x0 > y0) { i1 = 1; j1 = 0; }  // lower triangle
        else         { i1 = 0; j1 = 1; }  // upper triangle

        // Second and third corner offsets
        float x1 = x0 - i1 + G2;
        float y1 = y0 - j1 + G2;
        float x2 = x0 - 1f + 2f * G2;
        float y2 = y0 - 1f + 2f * G2;

        // Permutation table hash for the three corners
        int ii = i & 255;
        int jj = j & 255;
        int gi0 = _perm[ii     + _perm[jj    ]] % 8;
        int gi1 = _perm[ii + i1 + _perm[jj + j1]] % 8;
        int gi2 = _perm[ii + 1  + _perm[jj + 1 ]] % 8;

        // Noise contributions from the three corners
        float n0 = CornerContribution(gi0, x0, y0);
        float n1 = CornerContribution(gi1, x1, y1);
        float n2 = CornerContribution(gi2, x2, y2);

        // Scale to [-1, 1]. The 70.0 factor is calibrated for this gradient set.
        return 70.0f * (n0 + n1 + n2);
    }

    private static float CornerContribution(int gi, float x, float y)
    {
        // Radial attenuation: 0.5 - distance^2, ramped to zero at edge of kernel
        float t = 0.5f - x * x - y * y;
        if (t < 0f) return 0f;
        t *= t;
        var (gx, gy) = _grad2[gi];
        return t * t * (gx * x + gy * y);
    }

    private static int FastFloor(float x)
        => x >= 0f ? (int)x : (int)x - 1;
}
