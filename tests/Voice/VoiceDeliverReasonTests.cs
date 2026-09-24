using UnderWarden.Logic.Content;
using UnderWarden.Logic.Voice;
using NUnit.Framework;

namespace UnderWarden.Tests.Voice;

/// <summary>
/// The diagnostic reason codes (Phase 1, symptom E). The out-reason overload must report exactly WHY a
/// turn did or didn't deliver — this is what the on-device diagnostic logs to localize the blank ribbon.
/// The reason is observed at the existing gates, so behaviour is identical to the reason-less overload
/// (proven by the unchanged scheduler + isolation tests).
/// </summary>
[TestFixture]
public class VoiceDeliverReasonTests
{
    private static VoiceScheduler Fresh() => new(
        VoiceLineRegistry.LoadFromYaml("hp_critical: [hp1, hp2]\ntrap: [t1, t2]\nidle: [i1, i2]\n"),
        new VoiceTierMetadata(ambientCutoffTier: 20, new[]
        {
            new VoiceFamilyMeta("hp_critical", 70, 0, false, CooldownExempt: true),
            new VoiceFamilyMeta("trap", 30, 1, false, false),
            new VoiceFamilyMeta("idle", 10, 2, false, false),
        }),
        runSeed: 1337);

    private static VoiceDeliverReason Reason(VoiceScheduler s, string[] fams, VoiceMode mode, int turn,
        int? curTier = null, bool surface = true)
    {
        s.TryDeliver(fams, mode, turn, curTier, surface, out var reason);
        return reason;
    }

    [Test]
    public void EachGate_ReportsItsReason()
    {
        Assert.That(Reason(Fresh(), new[] { "trap" }, VoiceMode.Silent, 0), Is.EqualTo(VoiceDeliverReason.SilentMode));
        Assert.That(Reason(Fresh(), new[] { "trap" }, VoiceMode.Verbose, 0, surface: false), Is.EqualTo(VoiceDeliverReason.SurfaceUnavailable));
        Assert.That(Reason(Fresh(), new[] { "unknown_family" }, VoiceMode.Verbose, 0), Is.EqualTo(VoiceDeliverReason.NoEligibleFamily));
        Assert.That(Reason(Fresh(), new[] { "trap" }, VoiceMode.Verbose, 0, curTier: 99), Is.EqualTo(VoiceDeliverReason.SupersededByCurrent));
        Assert.That(Reason(Fresh(), new[] { "hp_critical" }, VoiceMode.Verbose, 0), Is.EqualTo(VoiceDeliverReason.Delivered));

        // Cooldown: deliver a non-exempt family, then a second attempt within 3 turns.
        var s = Fresh();
        s.TryDeliver(new[] { "trap" }, VoiceMode.Verbose, 0, null, true, out _);
        Assert.That(Reason(s, new[] { "trap" }, VoiceMode.Verbose, 1), Is.EqualTo(VoiceDeliverReason.Cooldown));
    }

    [Test]
    public void FloorSilenced_Reports()
    {
        var s = Fresh();
        s.SilenceCurrentFloor();
        Assert.That(Reason(s, new[] { "hp_critical" }, VoiceMode.Verbose, 0), Is.EqualTo(VoiceDeliverReason.FloorSilenced));
    }
}
