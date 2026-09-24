using UnderWarden.Logic.Content;
using UnderWarden.Logic.Voice;
using NUnit.Framework;

namespace UnderWarden.Tests.Voice;

/// <summary>
/// Scheduler behaviour (docs/systems/voice_delivery.md §Scheduler), pure-logic — no GameState. Covers
/// shuffle bags, one-shots, priority + tie-break, cooldown, and THE ONE RULE (every non-render path is
/// non-consuming, one test per reason), plus determinism of the delivery sequence.
/// </summary>
[TestFixture]
public class VoiceSchedulerTests
{
    // Fixture: five families spanning the tier range; idle is ambient (tier 10 <= cutoff 20),
    // hp_critical is cooldown-exempt, species_first_sight is once-per-run.
    private static VoiceLineRegistry Registry() => VoiceLineRegistry.LoadFromYaml(
        "hp_critical: [hp1, hp2, hp3]\n" +
        "possession: [pos1, pos2]\n" +
        "species_first_sight: [first1]\n" +
        "trap: [trap1, trap2, trap3, trap4]\n" +
        "idle: [idle1, idle2]\n");

    private static VoiceTierMetadata Meta() => new(ambientCutoffTier: 20, new[]
    {
        new VoiceFamilyMeta("hp_critical", 70, 0, OncePerRun: false, CooldownExempt: true),
        new VoiceFamilyMeta("possession", 60, 1, OncePerRun: false, CooldownExempt: false),
        new VoiceFamilyMeta("species_first_sight", 40, 2, OncePerRun: true, CooldownExempt: false),
        new VoiceFamilyMeta("trap", 30, 3, OncePerRun: false, CooldownExempt: false),
        new VoiceFamilyMeta("idle", 10, 4, OncePerRun: false, CooldownExempt: false),
    });

    private static VoiceScheduler Fresh(int seed = 1337) => new(Registry(), Meta(), seed);

    private static string[] Trig(params string[] f) => f;

    /// <summary>A comparable snapshot of ALL mutable scheduler state, for the non-consumption tests.</summary>
    private static string Snap(VoiceScheduler s) =>
        $"rng={s.RngCallCount};last={s.LastDeliveredTurn};silence={s.CurrentFloorSilenced};" +
        $"fired=[{string.Join(",", s.FiredSnapshot())}];" +
        $"bags=[{string.Join("|", s.BagsSnapshot().Select(b => b.Family + ":" + string.Join(",", b.Remaining)))}];" +
        $"hist=[{string.Join(",", s.HistorySnapshot().Select(h => h.Family + "@" + h.Turn))}]";

    // ── shuffle bags ───────────────────────────────────────────────────────────

    [Test]
    public void Bag_NoRepeatUntilExhaustion_ThenReshuffles()
    {
        var s = Fresh();
        // hp_critical is cooldown-exempt → deliver every turn. Pool of 3.
        var first3 = new List<string>();
        for (int t = 0; t < 3; t++) first3.Add(s.TryDeliver(Trig("hp_critical"), VoiceMode.Verbose, t)!.Line);
        Assert.That(first3, Is.Unique, "a line must not repeat until its bag empties.");
        Assert.That(first3.OrderBy(x => x), Is.EqualTo(new[] { "hp1", "hp2", "hp3" }), "the bag drains the whole pool.");

        var next3 = new List<string>();
        for (int t = 3; t < 6; t++) next3.Add(s.TryDeliver(Trig("hp_critical"), VoiceMode.Verbose, t)!.Line);
        Assert.That(next3, Is.Unique, "the reshuffled bag also drains without repeats.");
        Assert.That(next3.OrderBy(x => x), Is.EqualTo(new[] { "hp1", "hp2", "hp3" }));
    }

    // ── one-shots ──────────────────────────────────────────────────────────────

    [Test]
    public void OneShot_FiresAtMostOncePerRun()
    {
        var s = Fresh();
        Assert.That(s.TryDeliver(Trig("species_first_sight"), VoiceMode.Verbose, 0)?.Line, Is.EqualTo("first1"));
        // Later turn (cooldown long cleared) — still suppressed, and non-consuming.
        var before = Snap(s);
        Assert.That(s.TryDeliver(Trig("species_first_sight"), VoiceMode.Verbose, 50), Is.Null);
        Assert.That(Snap(s), Is.EqualTo(before), "a spent one-shot must not consume again.");
    }

    // ── priority + tie-break ─────────────────────────────────────────────────────

    [Test]
    public void Priority_HighestTierWins_LosersNotConsumed()
    {
        var s = Fresh();
        var d = s.TryDeliver(Trig("trap", "possession", "idle"), VoiceMode.Verbose, 0);
        Assert.That(d!.Family, Is.EqualTo("possession"), "tier 60 beats trap 30 and idle 10.");
        var bagFamilies = s.BagsSnapshot().Select(b => b.Family).ToList();
        Assert.That(bagFamilies, Does.Not.Contain("trap"), "a losing family must not advance its bag.");
        Assert.That(bagFamilies, Does.Not.Contain("idle"), "a losing family must not advance its bag.");
    }

    [Test]
    public void Priority_TieBrokenByFamilyOrder_NotArgumentOrder()
    {
        var reg = VoiceLineRegistry.LoadFromYaml("alpha: [a]\nbeta: [b]\n");
        var meta = new VoiceTierMetadata(0, new[]
        {
            new VoiceFamilyMeta("alpha", 50, 0, false, false),
            new VoiceFamilyMeta("beta", 50, 1, false, false),
        });
        var s = new VoiceScheduler(reg, meta, 1);
        // beta passed first, but alpha's lower Order wins the equal-tier tie.
        Assert.That(s.TryDeliver(Trig("beta", "alpha"), VoiceMode.Verbose, 0)!.Family, Is.EqualTo("alpha"));
    }

    // ── cooldown ─────────────────────────────────────────────────────────────────

    [Test]
    public void Cooldown_BlocksWithinThreeTurns_ExemptTierIgnoresIt()
    {
        var s = Fresh();
        Assert.That(s.TryDeliver(Trig("trap"), VoiceMode.Verbose, 0), Is.Not.Null);
        Assert.That(s.TryDeliver(Trig("trap"), VoiceMode.Verbose, 1), Is.Null, "1 turn later: on cooldown.");
        Assert.That(s.TryDeliver(Trig("trap"), VoiceMode.Verbose, 2), Is.Null, "2 turns later: still on cooldown.");
        Assert.That(s.TryDeliver(Trig("trap"), VoiceMode.Verbose, 3), Is.Not.Null, "3 turns later: cooldown cleared.");

        var s2 = Fresh();
        Assert.That(s2.TryDeliver(Trig("hp_critical"), VoiceMode.Verbose, 0), Is.Not.Null);
        Assert.That(s2.TryDeliver(Trig("hp_critical"), VoiceMode.Verbose, 1), Is.Not.Null, "top tier is cooldown-exempt.");
    }

    // ── THE ONE RULE: one non-consumption test per reason ───────────────────────

    [Test]
    public void NonConsuming_SilentMode()
    {
        var s = Fresh();
        var before = Snap(s);
        Assert.That(s.TryDeliver(Trig("hp_critical"), VoiceMode.Silent, 0), Is.Null);
        Assert.That(Snap(s), Is.EqualTo(before));
    }

    [Test]
    public void NonConsuming_TacticalBelowAmbientCutoff()
    {
        var s = Fresh();
        var before = Snap(s);
        // idle tier 10 <= cutoff 20 → hidden in Tactical.
        Assert.That(s.TryDeliver(Trig("idle"), VoiceMode.Tactical, 0), Is.Null);
        Assert.That(Snap(s), Is.EqualTo(before));
    }

    [Test]
    public void NonConsuming_Cooldown()
    {
        var s = Fresh();
        s.TryDeliver(Trig("trap"), VoiceMode.Verbose, 0);   // establishes lastDeliveredTurn
        var before = Snap(s);
        Assert.That(s.TryDeliver(Trig("trap"), VoiceMode.Verbose, 1), Is.Null);
        Assert.That(Snap(s), Is.EqualTo(before));
    }

    [Test]
    public void NonConsuming_PriorityLoss()
    {
        var s = Fresh();
        // trap loses to possession; assert trap specifically was untouched.
        s.TryDeliver(Trig("trap", "possession"), VoiceMode.Verbose, 0);
        Assert.That(s.BagsSnapshot().Any(b => b.Family == "trap"), Is.False, "the losing family must not consume.");
    }

    [Test]
    public void NonConsuming_SurfaceUnavailable()
    {
        var s = Fresh();
        var before = Snap(s);
        Assert.That(s.TryDeliver(Trig("hp_critical"), VoiceMode.Verbose, 0, surfaceAvailable: false), Is.Null);
        Assert.That(Snap(s), Is.EqualTo(before));
    }

    [Test]
    public void NonConsuming_RibbonSupersedeFails()
    {
        var s = Fresh();
        var before = Snap(s);
        // trap tier 30 cannot supersede a tier-99 line already on the ribbon.
        Assert.That(s.TryDeliver(Trig("trap"), VoiceMode.Verbose, 0, currentRibbonTier: 99), Is.Null);
        Assert.That(Snap(s), Is.EqualTo(before));
    }

    [Test]
    public void NonConsuming_FloorSilenced()
    {
        var s = Fresh();
        s.SilenceCurrentFloor();
        var before = Snap(s);
        Assert.That(s.TryDeliver(Trig("hp_critical"), VoiceMode.Verbose, 0), Is.Null);
        Assert.That(Snap(s), Is.EqualTo(before));
        // And entering a floor clears the mute — delivery resumes.
        s.OnFloorEntered();
        Assert.That(s.TryDeliver(Trig("hp_critical"), VoiceMode.Verbose, 0), Is.Not.Null);
    }

    // ── determinism + equal supersede + YAML schema ─────────────────────────────

    [Test]
    public void RibbonSupersede_RequiresStrictlyHigher_EqualTierDropped()
    {
        var s = Fresh();
        // possession tier 60 vs a tier-60 line already showing → equal, not strictly higher → dropped.
        var before = Snap(s);
        Assert.That(s.TryDeliver(Trig("possession"), VoiceMode.Verbose, 0, currentRibbonTier: 60), Is.Null);
        Assert.That(Snap(s), Is.EqualTo(before));
        // one higher clears it.
        Assert.That(s.TryDeliver(Trig("hp_critical"), VoiceMode.Verbose, 0, currentRibbonTier: 60), Is.Not.Null);
    }

    [Test]
    public void Determinism_SameSeedSameDeliverySequence()
    {
        static List<string> Script(VoiceScheduler s)
        {
            var outp = new List<string>();
            for (int t = 0; t < 40; t++)
            {
                // hp_critical every turn (exempt) drives repeated bag reshuffles; others add contention.
                var d = s.TryDeliver(Trig("hp_critical", "trap", "idle"), VoiceMode.Verbose, t);
                if (d != null) outp.Add($"{t}:{d.Family}:{d.Line}");
            }
            return outp;
        }

        var a = Script(new VoiceScheduler(Registry(), Meta(), 4242));
        var b = Script(new VoiceScheduler(Registry(), Meta(), 4242));
        Assert.That(b, Is.EqualTo(a), "identical seed ⇒ identical delivery sequence.");
        Assert.That(a.Count, Is.GreaterThan(0));

        var c = Script(new VoiceScheduler(Registry(), Meta(), 99));
        Assert.That(c, Is.Not.EqualTo(a), "a different voice seed must change the shuffle order.");
    }

    // ── Option A: specific pool key → family-resolved tier ───────────────────────

    // Registry keyed by SPECIFIC pool keys; meta keyed by FAMILIES the reader never emits bare.
    private static VoiceScheduler OptionA(int seed = 1337)
    {
        var reg = VoiceLineRegistry.LoadFromYaml(
            "species_first_sight.orc_grunt: [orc_line]\n" +
            "hp_threshold.25: [q1, q2]\n");
        var meta = new VoiceTierMetadata(ambientCutoffTier: 20, new[]
        {
            new VoiceFamilyMeta("hp_threshold", 90, 0, OncePerRun: false, CooldownExempt: true),
            new VoiceFamilyMeta("species_first_sight", 50, 1, OncePerRun: true, CooldownExempt: false),
        });
        return new VoiceScheduler(reg, meta, seed);
    }

    [Test]
    public void SpecificKey_DrawsFromItsPool_TierFromResolvedFamily()
    {
        var s = OptionA();
        var d = s.TryDeliver(Trig("species_first_sight.orc_grunt"), VoiceMode.Verbose, 0);
        Assert.That(d, Is.Not.Null);
        Assert.That(d!.Line, Is.EqualTo("orc_line"), "the pool is the SPECIFIC key's, not the family's.");
        Assert.That(d.Tier, Is.EqualTo(50), "the tier comes from the resolved family.");
    }

    [Test]
    public void SpecificKey_HigherFamilyTierWins_AcrossDifferentKeys()
    {
        var s = OptionA();
        // hp_threshold.25 (family tier 90) must beat species_first_sight.orc_grunt (family tier 50).
        var d = s.TryDeliver(Trig("species_first_sight.orc_grunt", "hp_threshold.25"), VoiceMode.Verbose, 0);
        Assert.That(d!.Tier, Is.EqualTo(90));
        Assert.That(d.Line, Is.AnyOf("q1", "q2"));
    }

    [Test]
    public void UnknownKey_SkippedWithoutConsuming()
    {
        var s = OptionA();
        var before = Snap(s);
        Assert.That(s.TryDeliver(Trig("hp_threshold_x", "not_a_family.thing"), VoiceMode.Verbose, 5,
            currentRibbonTier: null, surfaceAvailable: true, out var reason), Is.Null);
        Assert.That(reason, Is.EqualTo(VoiceDeliverReason.NoEligibleFamily));
        Assert.That(Snap(s), Is.EqualTo(before), "an unresolvable key must not draw, fire, or advance history.");
    }

    [Test]
    public void MetadataYaml_ParsesSchema_PreservingOrderAndFlags()
    {
        var meta = VoiceTierMetadata.LoadFromYaml(
            "ambient_cutoff_tier: 25\n" +
            "families:\n" +
            "  - key: hp_critical\n" +
            "    tier: 70\n" +
            "    cooldown_exempt: true\n" +
            "  - key: species_first_sight\n" +
            "    tier: 40\n" +
            "    once_per_run: true\n");
        Assert.That(meta.AmbientCutoffTier, Is.EqualTo(25));
        Assert.That(meta.Families.Select(f => f.Key), Is.EqualTo(new[] { "hp_critical", "species_first_sight" }),
            "family declaration order must be preserved (it is the tie-break).");
        Assert.That(meta.Get("hp_critical")!.CooldownExempt, Is.True);
        Assert.That(meta.Get("hp_critical")!.Order, Is.EqualTo(0));
        Assert.That(meta.Get("species_first_sight")!.OncePerRun, Is.True);
        Assert.That(meta.Get("species_first_sight")!.Order, Is.EqualTo(1));
    }
}
