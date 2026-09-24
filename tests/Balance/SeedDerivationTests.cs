using UnderWarden.Logic.Balance;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Tests for SeedDerivation.Stable — verifies SHA-256 key derivation, uniqueness contracts,
/// and cross-language byte-level compatibility with the PoC stable_scenario_seed().
/// </summary>
[TestFixture]
public class SeedDerivationTests
{
    // ── Cross-language reference value ────────────────────────────────────────
    //
    // Captured from PoC (~/development/rlike/engine/rng_config.py):
    //   import hashlib
    //   key = "depth3_orc_brutal:0:1337"
    //   seed = int.from_bytes(hashlib.sha256(key.encode('utf-8')).digest()[:4], byteorder='big')
    //   # => 3699130415
    //
    // As signed int32 this is -595836881 (high bit set). Both representations are identical bits;
    // we store as int since that's what Stable() returns and what SeededRandom accepts.
    private const int ReferenceValue_Depth3OrcBrutal_Run0_Seed1337 = unchecked((int)3699130415u);

    [Test]
    public void Stable_CrossLanguageReferenceValue_MatchesPoc()
    {
        int result = SeedDerivation.Stable("depth3_orc_brutal", 0, 1337);
        Assert.That(result, Is.EqualTo(ReferenceValue_Depth3OrcBrutal_Run0_Seed1337),
            "SHA-256 + big-endian byte extraction must match PoC stable_scenario_seed() byte-for-byte.");
    }

    [Test]
    public void Stable_SameInputs_AlwaysReturnSameValue()
    {
        const string id = "depth3_orc_brutal";
        int first = SeedDerivation.Stable(id, 0, 1337);
        for (int call = 0; call < 10; call++)
        {
            Assert.That(SeedDerivation.Stable(id, 0, 1337), Is.EqualTo(first),
                $"Call {call}: same inputs must always produce same seed");
        }
    }

    [Test]
    public void Stable_DifferentRunIdx_ProduceUniqueSeeds()
    {
        const string id = "depth3_orc_brutal";
        var seeds = new HashSet<int>();
        for (int run = 0; run < 50; run++)
        {
            int s = SeedDerivation.Stable(id, run, 1337);
            Assert.That(seeds.Add(s), Is.True, $"Run {run} produced duplicate seed {s}");
        }
    }

    [Test]
    public void Stable_DifferentScenarioIds_ProduceDifferentSeeds()
    {
        var baselineSeeds = new HashSet<int>();
        var fineSeeds = new HashSet<int>();

        for (int run = 0; run < 10; run++)
        {
            baselineSeeds.Add(SeedDerivation.Stable("depth2_orc_baseline", run, 1337));
            fineSeeds.Add(SeedDerivation.Stable("depth2_orc_baseline_fine", run, 1337));
        }

        // The two sets must be disjoint — no accidental seed collision across variants
        var intersection = new HashSet<int>(baselineSeeds);
        intersection.IntersectWith(fineSeeds);
        Assert.That(intersection, Is.Empty,
            "depth2_orc_baseline and depth2_orc_baseline_fine must produce disjoint seed sets for runs 0-9");
    }

    [Test]
    public void Stable_DifferentSeedBase_ProducesDifferentSeed()
    {
        int s1337 = SeedDerivation.Stable("depth3_orc_brutal", 0, 1337);
        int s42   = SeedDerivation.Stable("depth3_orc_brutal", 0, 42);
        Assert.That(s1337, Is.Not.EqualTo(s42),
            "Different seedBase values must produce different seeds");
    }

    [Test]
    public void Stable_DefaultSeedBase_Is1337()
    {
        // Verifies the default parameter matches PoC's default seed_base=1337
        int withDefault  = SeedDerivation.Stable("depth3_orc_brutal", 0);
        int withExplicit = SeedDerivation.Stable("depth3_orc_brutal", 0, 1337);
        Assert.That(withDefault, Is.EqualTo(withExplicit));
    }
}
