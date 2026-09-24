using System.Linq;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Voice;
using NUnit.Framework;

namespace UnderWarden.Tests.Voice;

/// <summary>
/// NativeAOT (iOS) safety for the voice YAML loaders. `AotObjectFactory(strict: true)` throws on any
/// type it can't construct without reflection — exactly what trimmed NativeAOT does on device. Loading
/// the voice tiers + pools under strict is the headless reproduction of the device failure: before the
/// factory registered VoiceTierMetadata's DTOs, this threw, the voice-load catch swallowed it,
/// `_voiceTierMeta` stayed null, and the scheduler was never built — so the ribbon was silent on device
/// while every desktop (JIT) test passed. This test fails if that regression returns.
/// </summary>
[TestFixture]
public class VoiceAotSafetyTests
{
    [Test]
    public void TierMetadata_ParsesUnderStrictAot()
    {
        const string yaml = """
            ambient_cutoff_tier: 20
            families:
              - key: hp_critical
                tier: 70
                cooldown_exempt: true
              - key: species_first_sight
                tier: 40
                once_per_run: true
              - key: idle
                tier: 10
            """;

        // strict: true == simulate NativeAOT — any unregistered deserialized type throws here.
        var meta = VoiceTierMetadata.LoadFromYaml(yaml, new AotObjectFactory(strict: true));

        Assert.That(meta.AmbientCutoffTier, Is.EqualTo(20));
        Assert.That(meta.Families.Select(f => f.Key), Is.EqualTo(new[] { "hp_critical", "species_first_sight", "idle" }));
        Assert.That(meta.Get("hp_critical")!.CooldownExempt, Is.True);
        Assert.That(meta.Get("species_first_sight")!.OncePerRun, Is.True);
    }

    [Test]
    public void LinePools_ParseUnderStrictAot()
    {
        var reg = VoiceLineRegistry.LoadFromYaml("hp_critical: [a, b]\nidle: [c]\n", new AotObjectFactory(strict: true));
        Assert.That(reg.GetPool("hp_critical")!.Count, Is.EqualTo(2));
    }
}
