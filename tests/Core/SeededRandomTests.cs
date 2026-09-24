using UnderWarden.Logic.Core;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

[TestFixture]
public class SeededRandomTests
{
    [Test]
    public void SameSeed_ProducesSameSequence()
    {
        var rng1 = new SeededRandom(1337);
        var rng2 = new SeededRandom(1337);

        for (int i = 0; i < 100; i++)
        {
            Assert.That(rng1.Next(1000), Is.EqualTo(rng2.Next(1000)));
        }
    }

    [Test]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var rng1 = new SeededRandom(1337);
        var rng2 = new SeededRandom(42);

        bool anyDifferent = false;
        for (int i = 0; i < 100; i++)
        {
            if (rng1.Next(1000) != rng2.Next(1000))
            {
                anyDifferent = true;
                break;
            }
        }

        Assert.That(anyDifferent, Is.True);
    }

    [Test]
    public void DefaultSeed_Is1337()
    {
        var rng = new SeededRandom();
        Assert.That(rng.Seed, Is.EqualTo(1337));
    }

    // ── M1.4 save/resume continuity ──────────────────────────────────────────

    /// <summary>
    /// The added call counter must not perturb the sequence: a mixed run of draws produces the
    /// exact values a same-seed run produces. (This is the local guarantee behind "the balance
    /// baseline must not move" — the counter only observes, it never touches the PRNG.)
    /// </summary>
    [Test]
    public void CallCounter_DoesNotChangeSequence()
    {
        var counted = new SeededRandom(1337);
        var reference = new Random(1337); // raw legacy PRNG, no counter

        for (int i = 0; i < 300; i++)
        {
            // Interleave every wrapper method; each must equal the raw single-sample draw.
            switch (i % 4)
            {
                case 0: Assert.That(counted.Next(1000), Is.EqualTo(reference.Next(1000))); break;
                case 1: Assert.That(counted.Next(5, 250), Is.EqualTo(reference.Next(5, 250))); break;
                case 2: Assert.That(counted.NextDouble(), Is.EqualTo(reference.NextDouble())); break;
                case 3: Assert.That(counted.NextFloat(), Is.EqualTo((float)reference.NextDouble())); break;
            }
        }
        Assert.That(counted.CallCount, Is.EqualTo(300));
    }

    [Test]
    public void CallCount_IncrementsOncePerDraw()
    {
        var rng = new SeededRandom(1337);
        Assert.That(rng.CallCount, Is.EqualTo(0));
        rng.Next(10);        Assert.That(rng.CallCount, Is.EqualTo(1));
        rng.Next(0, 10);     Assert.That(rng.CallCount, Is.EqualTo(2));
        rng.NextDouble();    Assert.That(rng.CallCount, Is.EqualTo(3));
        rng.NextFloat();     Assert.That(rng.CallCount, Is.EqualTo(4));
    }

    /// <summary>
    /// The core save/restore contract: restoring at position K reproduces the tail of the original
    /// sequence exactly, for a mixed draw pattern and across several checkpoints.
    /// </summary>
    [Test]
    public void Restore_ResumesSequenceFromSavedPosition()
    {
        const int seed = 24601;

        // Record a long mixed reference sequence and CallCount at each step.
        var reference = new SeededRandom(seed);
        var values = new List<double>();
        for (int i = 0; i < 500; i++)
        {
            values.Add(i % 3 switch
            {
                0 => reference.Next(1_000_000),
                1 => reference.Next(-50, 50),
                _ => reference.NextDouble(),
            });
        }

        // Restore at several checkpoints; the tail from that point must match byte-for-byte.
        foreach (int k in new[] { 0, 1, 63, 200, 499 })
        {
            var restored = SeededRandom.Restore(seed, k);
            Assert.That(restored.CallCount, Is.EqualTo(k), $"CallCount after Restore(k={k})");
            for (int i = k; i < 500; i++)
            {
                double got = i % 3 switch
                {
                    0 => restored.Next(1_000_000),
                    1 => restored.Next(-50, 50),
                    _ => restored.NextDouble(),
                };
                Assert.That(got, Is.EqualTo(values[i]), $"seed={seed} restore@{k} draw#{i}");
            }
        }
    }

    [Test]
    public void Restore_ZeroCallCount_EqualsFreshConstruction()
    {
        var fresh = new SeededRandom(777);
        var restored = SeededRandom.Restore(777, 0);
        for (int i = 0; i < 50; i++)
            Assert.That(restored.Next(10_000), Is.EqualTo(fresh.Next(10_000)));
    }

    [Test]
    public void Restore_NegativeCallCount_Throws()
    {
        Assert.That(() => SeededRandom.Restore(1337, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
