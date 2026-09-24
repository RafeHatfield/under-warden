using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests that BurningEffect suppresses InnateRegenComponent, matching the PoC rule:
///   regen suppressed when damage_type in ['acid', 'fire'] (fighter.py lines 553–570).
///
/// AcidEffect regression tests are also included to confirm no regression from the change.
/// </summary>
[TestFixture]
public class TrollFireSuppressionTests
{
    private static Entity MakeTrollWithRegen(int hp = 30, int healPerTurn = 2)
    {
        var troll = new Entity(1, "Troll", 3, 3, blocksMovement: true);
        troll.Add(new Fighter(hp: hp, strength: 16, dexterity: 8, constitution: 16,
            accuracy: 2, evasion: 0, damageMin: 8, damageMax: 12));
        troll.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        troll.Add(new InnateRegenComponent { HealPerTurn = healPerTurn });
        return troll;
    }

    [Test]
    public void Troll_WithBurningEffect_DoesNotRegenerate()
    {
        // Burning troll: InnateRegen should be suppressed, HP should not increase.
        var troll = MakeTrollWithRegen();
        var fighter = troll.Require<Fighter>();
        fighter.TakeDamage(10); // 20/30 HP
        int hpBefore = fighter.Hp;

        troll.Add(new BurningEffect { RemainingTurns = 5, DamagePerTurn = 3 });

        var events = new List<TurnEvent>();
        StatusEffectProcessor.ProcessTurnStart(troll, events, turnCount: 1);

        // BurningEffect deals 3 damage per turn, so HP should go down, not up.
        // The key assertion is that innate regen did NOT fire (no HotHealEvent with innate_regen).
        Assert.That(events.OfType<HotHealEvent>().Any(e => e.EffectName == "innate_regen"), Is.False,
            "Burning troll should NOT regenerate — InnateRegen suppressed by BurningEffect");
        Assert.That(events.OfType<RegenSuppressedEvent>().Any(), Is.True,
            "RegenSuppressedEvent must be emitted when BurningEffect suppresses InnateRegen");
    }

    [Test]
    public void Troll_WithAcidEffect_DoesNotRegenerate_Regression()
    {
        // Regression: AcidEffect suppression still works after the BurningEffect change.
        var troll = MakeTrollWithRegen();
        var fighter = troll.Require<Fighter>();
        fighter.TakeDamage(10); // 20/30 HP

        troll.Add(new AcidEffect { RemainingTurns = 8 });

        var events = new List<TurnEvent>();
        StatusEffectProcessor.ProcessTurnStart(troll, events, turnCount: 1);

        Assert.That(events.OfType<HotHealEvent>().Any(e => e.EffectName == "innate_regen"), Is.False,
            "Acid troll should NOT regenerate — existing acid suppression must still work");
        Assert.That(events.OfType<RegenSuppressedEvent>().Any(), Is.True,
            "RegenSuppressedEvent must fire for acid suppression");
    }

    [Test]
    public void Troll_WithBothBurningAndAcid_StaysSuppressed()
    {
        // Both effects together: still suppressed, exactly one RegenSuppressedEvent emitted.
        var troll = MakeTrollWithRegen();
        var fighter = troll.Require<Fighter>();
        fighter.TakeDamage(5); // 25/30 HP

        troll.Add(new BurningEffect { RemainingTurns = 3, DamagePerTurn = 3 });
        troll.Add(new AcidEffect { RemainingTurns = 4 });

        var events = new List<TurnEvent>();
        StatusEffectProcessor.ProcessTurnStart(troll, events, turnCount: 1);

        Assert.That(events.OfType<HotHealEvent>().Any(e => e.EffectName == "innate_regen"), Is.False,
            "Troll with both effects should still have regen suppressed");
        Assert.That(events.OfType<RegenSuppressedEvent>().Count(), Is.EqualTo(1),
            "Exactly one RegenSuppressedEvent should fire (the condition is an OR, not two separate checks)");
    }

    [Test]
    public void Troll_WithNeitherEffect_RegeneratesNormally()
    {
        // Baseline: no suppression effects → troll heals as expected.
        var troll = MakeTrollWithRegen(healPerTurn: 2);
        var fighter = troll.Require<Fighter>();
        fighter.TakeDamage(10); // 20/30 HP
        int hpBefore = fighter.Hp;

        var events = new List<TurnEvent>();
        StatusEffectProcessor.ProcessTurnStart(troll, events, turnCount: 1);

        Assert.That(fighter.Hp, Is.EqualTo(hpBefore + 2),
            "Troll with no suppression effects should heal 2 HP per turn");
        Assert.That(events.OfType<HotHealEvent>().Any(e => e.EffectName == "innate_regen"), Is.True,
            "HotHealEvent with innate_regen should fire when not suppressed");
        Assert.That(events.OfType<RegenSuppressedEvent>().Any(), Is.False,
            "No RegenSuppressedEvent when not suppressed");
    }
}
