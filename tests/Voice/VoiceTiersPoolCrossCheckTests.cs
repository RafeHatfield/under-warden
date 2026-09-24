using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Voice;
using NUnit.Framework;

namespace UnderWarden.Tests.Voice;

/// <summary>
/// Cross-validation of the real ribbon content (scheme Option A). The reader emits SPECIFIC authored
/// pool keys; the scheduler resolves each to a voice family by separator-exact prefix. This test binds
/// the two authored files — config/voice_lines/hollowmark.yaml (the pools) and
/// config/voice_lines/voice_tiers.yaml (the families) — so a typo, an orphaned pool, or a family with no
/// pool is a LOUD CI failure rather than a line that silently never speaks on device.
///
/// Both files load under `AotObjectFactory(strict: true)` — the NativeAOT (iOS) reproduction. This is the
/// headless guard the device saga proved we needed.
/// </summary>
[TestFixture]
public class VoiceTiersPoolCrossCheckTests
{
    private static VoiceLineRegistry Hollowmark() =>
        VoiceLineRegistry.LoadFromYaml(File.ReadAllText(Config("hollowmark.yaml")), new AotObjectFactory(strict: true));

    private static VoiceTierMetadata Tiers() =>
        VoiceTierMetadata.LoadFromYaml(File.ReadAllText(Config("voice_tiers.yaml")), new AotObjectFactory(strict: true));

    [Test]
    public void EveryAuthoredPoolKey_ResolvesToExactlyOneFamily()
    {
        var pools = Hollowmark();
        var tiers = Tiers();

        var orphans = pools.PoolKeys.Where(k => tiers.ResolveFamily(k) == null).OrderBy(k => k).ToList();
        Assert.That(orphans, Is.Empty,
            "hollowmark.yaml pool keys with no owning family in voice_tiers.yaml (typo, or a missing family): "
            + string.Join(", ", orphans));
    }

    [Test]
    public void EveryFamily_OwnsAtLeastOnePool()
    {
        var pools = Hollowmark();
        var tiers = Tiers();

        var keys = pools.PoolKeys.ToList();
        var starved = tiers.Families
            .Where(f => !keys.Any(k => tiers.ResolveFamily(k)?.Key == f.Key))
            .Select(f => f.Key)
            .ToList();
        Assert.That(starved, Is.Empty,
            "voice_tiers.yaml families that own no hollowmark.yaml pool (dead tier entry): "
            + string.Join(", ", starved));
    }

    [Test]
    public void ResolveFamily_IsSeparatorExact_NotSubstring()
    {
        var tiers = Tiers();
        // "hp_threshold" is a real family; a future "hp_threshold_x" key must NOT resolve to it.
        Assert.That(tiers.ResolveFamily("hp_threshold_x"), Is.Null);
        // The bare family key resolves to itself; the dot-prefixed specific key resolves up to it.
        Assert.That(tiers.ResolveFamily("hp_threshold")!.Key, Is.EqualTo("hp_threshold"));
        Assert.That(tiers.ResolveFamily("hp_threshold.25")!.Key, Is.EqualTo("hp_threshold"));
    }

    // Walks up from the test bin directory to config/voice_lines/<fileName> at the project root.
    private static string Config(string fileName)
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "config", "voice_lines", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "config", "voice_lines", fileName);
    }
}
