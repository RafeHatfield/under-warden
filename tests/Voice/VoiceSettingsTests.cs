using UnderWarden.Logic.Voice;
using NUnit.Framework;

namespace UnderWarden.Tests.Voice;

/// <summary>
/// Device-settings round-trip (M1.5b). The pure part of the device store (JSON in/out, defaults,
/// cycling, duration mapping) — the Godot file IO in VoiceSettingsStore is a thin wrapper over this.
/// </summary>
[TestFixture]
public class VoiceSettingsTests
{
    [Test]
    public void RoundTrip_PreservesModeAndDurationAndDismiss()
    {
        foreach (var mode in new[] { VoiceMode.Verbose, VoiceMode.Tactical, VoiceMode.Silent })
        foreach (var dur in new[] { VoiceDuration.Short, VoiceDuration.Normal, VoiceDuration.Long })
        foreach (var dis in new[] { VoiceDismiss.Manual, VoiceDismiss.Timed })
        {
            var s = new VoiceSettings(mode, dur, dis);
            var back = VoiceSettings.FromJson(s.ToJson());
            Assert.That(back, Is.EqualTo(s), $"round-trip must preserve ({mode},{dur},{dis}).");
        }
    }

    [Test]
    public void Defaults_AreVerboseNormalManual_AndDurationMapsCorrectly()
    {
        var d = new VoiceSettings();
        Assert.That(d.Mode, Is.EqualTo(VoiceMode.Verbose));
        Assert.That(d.Duration, Is.EqualTo(VoiceDuration.Normal));
        Assert.That(d.Dismiss, Is.EqualTo(VoiceDismiss.Manual), "default dismiss is click-to-remove.");
        Assert.That(new VoiceSettings(Duration: VoiceDuration.Short).DurationSeconds, Is.EqualTo(2.5f));
        Assert.That(new VoiceSettings(Duration: VoiceDuration.Normal).DurationSeconds, Is.EqualTo(4f));
        Assert.That(new VoiceSettings(Duration: VoiceDuration.Long).DurationSeconds, Is.EqualTo(6f));
    }

    [Test]
    public void BadOrEmptyJson_FallsBackToDefaults()
    {
        Assert.That(VoiceSettings.FromJson(null), Is.EqualTo(new VoiceSettings()));
        Assert.That(VoiceSettings.FromJson(""), Is.EqualTo(new VoiceSettings()));
        Assert.That(VoiceSettings.FromJson("{garbage}"), Is.EqualTo(new VoiceSettings()));
        Assert.That(VoiceSettings.FromJson("{\"mode\":\"Nonsense\"}").Mode, Is.EqualTo(VoiceMode.Verbose));
    }

    [Test]
    public void Cycles_WrapThroughAllValues()
    {
        Assert.That(new VoiceSettings(Mode: VoiceMode.Verbose).CycleMode(), Is.EqualTo(VoiceMode.Tactical));
        Assert.That(new VoiceSettings(Mode: VoiceMode.Tactical).CycleMode(), Is.EqualTo(VoiceMode.Silent));
        Assert.That(new VoiceSettings(Mode: VoiceMode.Silent).CycleMode(), Is.EqualTo(VoiceMode.Verbose));
        Assert.That(new VoiceSettings(Duration: VoiceDuration.Short).CycleDuration(), Is.EqualTo(VoiceDuration.Normal));
        Assert.That(new VoiceSettings(Duration: VoiceDuration.Long).CycleDuration(), Is.EqualTo(VoiceDuration.Short));
        Assert.That(new VoiceSettings(Dismiss: VoiceDismiss.Manual).CycleDismiss(), Is.EqualTo(VoiceDismiss.Timed));
        Assert.That(new VoiceSettings(Dismiss: VoiceDismiss.Timed).CycleDismiss(), Is.EqualTo(VoiceDismiss.Manual));
    }
}
