using UnderWarden.Logic.Balance.LlmPlayer;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Unit tests for SignificantEventDetector — verifies hook detection logic in isolation.
/// The detector is Logic-layer only; no Harness or API dependencies needed.
/// </summary>
[TestFixture]
[Category("Balance")]
[Description("SignificantEventDetector: hook detection semantics (near-death, first-orc, mural, possession)")]
public class SignificantEventDetectorTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// AttackEvent where entity IDs 1..N are "orc" and 0 is always the player (non-orc).
    /// isOrcEntity: entity with ID >= 1 is an orc for test purposes.
    /// </summary>
    private static Func<int, bool> OrcResolver(params int[] orcIds)
    {
        var set = new HashSet<int>(orcIds);
        return id => set.Contains(id);
    }

    /// <summary>Resolver that always returns false (no orcs on the floor).</summary>
    private static Func<int, bool> NoOrcs() => _ => false;

    private static AttackEvent MakeAttack(int actorId, int targetId) =>
        new AttackEvent { ActorId = actorId, TargetId = targetId, Hit = true, Damage = 5 };

    private static MuralExaminedEvent MakeMuralEvent() =>
        new MuralExaminedEvent { ActorId = 0, X = 3, Y = 3, Text = "Ancient runes.", MuralId = "mural_1" };

    private static PossessionEnteredEvent MakePossessionEvent() =>
        new PossessionEnteredEvent { ActorId = 0, HostEntityId = 2, HostSpecies = "orc", OriginatorBodyId = 0 };

    private static IReadOnlyList<TurnEvent> NoEvents() => Array.Empty<TurnEvent>();

    // ── near_death tests ──────────────────────────────────────────────────────

    [Test]
    [Description("near_death fires when hpPct crosses below 0.2 on the first dip")]
    public void NearDeath_FiresAtCrossingBelow20Pct()
    {
        var detector = new SignificantEventDetector(LlmPersona.SystemExplorer);

        var hooks = detector.ProcessTurn(1, NoEvents(), hpPctAfterTurn: 0.19, NoOrcs());

        Assert.That(hooks.Any(h => h.HookName == "near_death"), Is.True,
            "Expected near_death hook when hpPct = 0.19");
    }

    [Test]
    [Description("near_death does NOT fire when hpPct is 0.21 (above the 0.2 threshold)")]
    public void NearDeath_DoesNotFireAt21Pct()
    {
        var detector = new SignificantEventDetector(LlmPersona.SystemExplorer);

        var hooks = detector.ProcessTurn(1, NoEvents(), hpPctAfterTurn: 0.21, NoOrcs());

        Assert.That(hooks.Any(h => h.HookName == "near_death"), Is.False,
            "near_death should not fire at 0.21");
    }

    [Test]
    [Description("near_death fires only once per dip — calling twice at 0.19 fires on turn 1 only")]
    public void NearDeath_DoesNotRefire_WhileBelowThreshold()
    {
        var detector = new SignificantEventDetector(LlmPersona.SystemExplorer);

        var first = detector.ProcessTurn(1, NoEvents(), hpPctAfterTurn: 0.19, NoOrcs());
        var second = detector.ProcessTurn(2, NoEvents(), hpPctAfterTurn: 0.15, NoOrcs());

        Assert.That(first.Any(h => h.HookName == "near_death"), Is.True, "Should fire on first dip");
        Assert.That(second.Any(h => h.HookName == "near_death"), Is.False, "Should NOT re-fire while still below threshold");
    }

    [Test]
    [Description("near_death re-arms at 0.3 and fires again on a second dip below 0.2")]
    public void NearDeath_ReArmsAfterRecoveryTo30Pct()
    {
        var detector = new SignificantEventDetector(LlmPersona.SystemExplorer);

        // First dip — fires and disarms
        var t1 = detector.ProcessTurn(1, NoEvents(), hpPctAfterTurn: 0.19, NoOrcs());
        // Recovery to >= 0.3 — re-arms
        var t2 = detector.ProcessTurn(2, NoEvents(), hpPctAfterTurn: 0.35, NoOrcs());
        // Second dip — fires again
        var t3 = detector.ProcessTurn(3, NoEvents(), hpPctAfterTurn: 0.15, NoOrcs());

        Assert.That(t1.Any(h => h.HookName == "near_death"), Is.True, "Should fire on first dip");
        Assert.That(t2.Any(h => h.HookName == "near_death"), Is.False, "Should not fire during recovery");
        Assert.That(t3.Any(h => h.HookName == "near_death"), Is.True, "Should fire again after re-arm");
    }

    [Test]
    [Description("near_death does NOT re-arm at 0.29 (below the 0.3 re-arm threshold) so the hook stays dormant")]
    public void NearDeath_DoesNotRearmAt29Pct()
    {
        var detector = new SignificantEventDetector(LlmPersona.SystemExplorer);

        // First dip — fires and disarms
        detector.ProcessTurn(1, NoEvents(), hpPctAfterTurn: 0.19, NoOrcs());
        // At 0.29: below 0.3, so re-arm does NOT trigger
        detector.ProcessTurn(2, NoEvents(), hpPctAfterTurn: 0.29, NoOrcs());
        // Another dip — should NOT fire because still not re-armed
        var t3 = detector.ProcessTurn(3, NoEvents(), hpPctAfterTurn: 0.15, NoOrcs());

        Assert.That(t3.Any(h => h.HookName == "near_death"), Is.False,
            "near_death should not fire because the 0.29 pass did not re-arm it");
    }

    // ── first_orc_interaction tests ───────────────────────────────────────────

    [Test]
    [Description("first_orc_interaction fires when the actor (attacker) is an orc")]
    public void FirstOrcInteraction_FiresOnOrcAttack()
    {
        var detector = new SignificantEventDetector(LlmPersona.SystemExplorer);
        // Entity 1 is an orc; entity 0 is the player
        var events = new TurnEvent[] { MakeAttack(actorId: 1, targetId: 0) };

        var hooks = detector.ProcessTurn(1, events, hpPctAfterTurn: 0.8, OrcResolver(1));

        Assert.That(hooks.Any(h => h.HookName == "first_orc_interaction"), Is.True,
            "Should fire when the attacker is an orc");
    }

    [Test]
    [Description("first_orc_interaction fires when the defender (target) is an orc (player attacks orc)")]
    public void FirstOrcInteraction_FiresOnOrcDefender()
    {
        var detector = new SignificantEventDetector(LlmPersona.SystemExplorer);
        // Entity 2 is an orc; player (0) attacks it
        var events = new TurnEvent[] { MakeAttack(actorId: 0, targetId: 2) };

        var hooks = detector.ProcessTurn(1, events, hpPctAfterTurn: 0.8, OrcResolver(2));

        Assert.That(hooks.Any(h => h.HookName == "first_orc_interaction"), Is.True,
            "Should fire when the defender is an orc");
    }

    [Test]
    [Description("first_orc_interaction fires on the first orc turn and NOT on subsequent orc turns")]
    public void FirstOrcInteraction_FiresOnlyOnce()
    {
        var detector = new SignificantEventDetector(LlmPersona.SystemExplorer);
        var orcAttack = new TurnEvent[] { MakeAttack(actorId: 1, targetId: 0) };

        var t1 = detector.ProcessTurn(1, orcAttack, hpPctAfterTurn: 0.8, OrcResolver(1));
        var t2 = detector.ProcessTurn(2, orcAttack, hpPctAfterTurn: 0.8, OrcResolver(1));

        Assert.That(t1.Any(h => h.HookName == "first_orc_interaction"), Is.True,
            "Should fire on first orc attack");
        Assert.That(t2.Any(h => h.HookName == "first_orc_interaction"), Is.False,
            "Should NOT fire again on second orc attack");
    }

    // ── mural_read tests ──────────────────────────────────────────────────────

    [Test]
    [Description("mural_read fires for the Reader persona when a MuralExaminedEvent is present")]
    public void MuralRead_FiresForReaderPersona()
    {
        var detector = new SignificantEventDetector(LlmPersona.Reader);
        var events = new TurnEvent[] { MakeMuralEvent() };

        var hooks = detector.ProcessTurn(1, events, hpPctAfterTurn: 0.9, NoOrcs());

        Assert.That(hooks.Any(h => h.HookName == "mural_read"), Is.True,
            "mural_read should fire for the Reader persona");
    }

    [Test]
    [Description("mural_read does NOT fire for the SystemExplorer persona")]
    public void MuralRead_DoesNotFireForSystemExplorer()
    {
        var detector = new SignificantEventDetector(LlmPersona.SystemExplorer);
        var events = new TurnEvent[] { MakeMuralEvent() };

        var hooks = detector.ProcessTurn(1, events, hpPctAfterTurn: 0.9, NoOrcs());

        Assert.That(hooks.Any(h => h.HookName == "mural_read"), Is.False,
            "mural_read should NOT fire for the SystemExplorer persona");
    }

    // ── first_possession tests ────────────────────────────────────────────────

    [Test]
    [Description("first_possession fires on the first PossessionEnteredEvent and NOT on subsequent ones")]
    public void FirstPossession_FiresOnce()
    {
        var detector = new SignificantEventDetector(LlmPersona.SystemExplorer);
        var possessionEvents = new TurnEvent[] { MakePossessionEvent() };

        var t1 = detector.ProcessTurn(1, possessionEvents, hpPctAfterTurn: 0.8, NoOrcs());
        var t2 = detector.ProcessTurn(2, possessionEvents, hpPctAfterTurn: 0.8, NoOrcs());

        Assert.That(t1.Any(h => h.HookName == "first_possession"), Is.True,
            "first_possession should fire on first possession entry");
        Assert.That(t2.Any(h => h.HookName == "first_possession"), Is.False,
            "first_possession should NOT fire a second time");
    }

    // ── prompt text verification tests ───────────────────────────────────────

    [Test]
    [Description("near_death prompt text contains the distinctive phrase 'arrived-without-decision-point' from plan-player.md §5")]
    public void PromptText_NearDeath_MatchesPlanSpec()
    {
        var detector = new SignificantEventDetector(LlmPersona.SystemExplorer);

        var hooks = detector.ProcessTurn(1, NoEvents(), hpPctAfterTurn: 0.19, NoOrcs());
        var hook = hooks.First(h => h.HookName == "near_death");

        Assert.That(hook.PromptBlock, Does.Contain("arrived-without-decision-point"),
            "near_death prompt must contain the distinctive phrase from plan-player.md §5");
    }

    [Test]
    [Description("first_orc_interaction prompt text contains 'opaque' — the distinctive phrase from plan-player.md §5")]
    public void PromptText_FirstOrc_MatchesPlanSpec()
    {
        var detector = new SignificantEventDetector(LlmPersona.SystemExplorer);
        var events = new TurnEvent[] { MakeAttack(actorId: 1, targetId: 0) };

        var hooks = detector.ProcessTurn(1, events, hpPctAfterTurn: 0.8, OrcResolver(1));
        var hook = hooks.First(h => h.HookName == "first_orc_interaction");

        Assert.That(hook.PromptBlock, Does.Contain("opaque"),
            "first_orc_interaction prompt must contain 'opaque' — the distinctive phrase from plan-player.md §5");
    }

    // ── baseline / no-op tests ────────────────────────────────────────────────

    [Test]
    [Description("Empty event list and hp >= 0.3 produces no hooks")]
    public void NoEvents_NoHooks()
    {
        var detector = new SignificantEventDetector(LlmPersona.Reader);

        var hooks = detector.ProcessTurn(1, NoEvents(), hpPctAfterTurn: 0.9, NoOrcs());

        Assert.That(hooks, Is.Empty, "No hooks should fire on an empty turn with healthy HP");
    }
}
