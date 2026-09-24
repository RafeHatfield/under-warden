using System.Security.Cryptography;
using System.Text;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Stable, deterministic per-scenario seed derivation.
///
/// Ports PoC stable_scenario_seed() from ~/development/rlike/engine/rng_config.py:88-121.
/// Uses SHA-256 (not MD5 — explicitly chosen for cross-version stability in the PoC).
/// Takes the first 4 bytes of the digest as a big-endian 32-bit integer.
///
/// Key format: "{scenarioId}:{runIdx}:{seedBase}" (colon-separated, UTF-8, no padding).
///
/// Why SHA-256 + not just baseSeed+i:
/// - baseSeed+i is deterministic but not isolated per scenario — two different scenarios
///   at the same run index share the same seed, meaning their combat rolls are correlated.
/// - SHA-256 derivation gives each (scenario, run) pair a statistically independent seed
///   so depth3_orc_brutal at run 5 and depth3_orc_brutal_fine at run 5 diverge immediately.
/// </summary>
public static class SeedDerivation
{
    /// <summary>
    /// Derive a deterministic 32-bit seed for the given scenario run.
    ///
    /// Cross-language verified: Stable("depth3_orc_brutal", 0, 1337) == 3699130415 (unsigned)
    /// which is -595836881 when interpreted as signed int32 — both representations are valid
    /// since SeededRandom accepts negative int seeds.
    /// </summary>
    public static int Stable(string scenarioId, int runIdx, int seedBase = 1337)
    {
        // Key format must match PoC exactly: "scenarioId:runIdx:seedBase"
        string key = $"{scenarioId}:{runIdx}:{seedBase}";
        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        byte[] hashBytes = SHA256.HashData(keyBytes);

        // First 4 bytes, big-endian — matches PoC int.from_bytes(hash_bytes[:4], byteorder='big')
        // Note: may return negative values when the high bit is set; that's fine for SeededRandom.
        return (int)((uint)hashBytes[0] << 24
                   | (uint)hashBytes[1] << 16
                   | (uint)hashBytes[2] << 8
                   | (uint)hashBytes[3]);
    }
}
