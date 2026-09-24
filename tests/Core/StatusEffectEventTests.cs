using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Phase 5 logic-layer tests for status effect events in TurnResult.
/// Verifies that the correct TurnEvents are emitted for effect application,
/// expiry, DOT damage, HOT healing, and SkipTurn.
///
/// These tests cover the event emission side — the presentation layer uses these
/// events to display toasts and update the StatusEffectBar.
/// No Godot dependencies — pure logic layer.
/// </summary>
[TestFixture]
public class StatusEffectEventTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static (GameState state, Entity player, Entity monster) CreateState(
        int playerHp = 100, int monsterHp = 100, int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: playerHp, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(player);

        var monster = new Entity(1, "Orc", 6, 5, blocksMovement: true);
        monster.Add(new Fighter(hp: monsterHp, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        monster.Add(new AiComponent { AiType = "basic" });
        map.RegisterEntity(monster);

        var state = new GameState(player, [monster], map, rng, turnLimit: 200);
        return (state, player, monster);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // StatusAppliedEvent
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void StatusAppliedEvent_InTurnResult_ForPlayerEffect()
    {
        // SpellResolver emits SpellEvent.StatusApplied when applying effects via spells.
        // Verify the SpellEvent appears in the turn result when a self-targeting status spell fires.
        // (StatusAppliedEvent is emitted by AoE spells like Fear; single-target spells use SpellEvent.StatusApplied)
        var (state, player, _) = CreateState();

        // Use a self-targeting spell — cast invisibility on self.
        var scroll = new Entity(10, "Scroll of Invisibility", 0, 0);
        scroll.Add(new SpellEffect { SpellId = "invisibility", Targeting = TargetingMode.Self });
        scroll.Add(new Consumable());
        var inventory = player.GetOrAdd<Inventory>();
        inventory.Add(scroll);

        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(scroll));

        // Self-targeting status spells emit SpellEvent with StatusApplied set.
        var spellEvent = result.Events.OfType<SpellEvent>()
            .FirstOrDefault(e => e.ActorId == player.Id && e.SpellId == "invisibility");

        Assert.That(spellEvent, Is.Not.Null,
            "SpellEvent should be emitted when a self-targeting status effect spell fires.");
        Assert.That(spellEvent!.StatusApplied, Is.EqualTo("invisibility"),
            "SpellEvent.StatusApplied should be set for status effect spells.");
        Assert.That(player.Has<InvisibilityEffect>(), Is.True,
            "InvisibilityEffect should be applied to the player.");
    }

    [Test]
    public void StatusAppliedEvent_InTurnResult_ForMonsterEffect()
    {
        // SpellResolver emits SpellEvent with StatusApplied for single-target status spells.
        // AoE spells like Fear also emit per-entity StatusAppliedEvent for each affected monster.
        var (state, player, monster) = CreateState();

        var scroll = new Entity(10, "Scroll of Confusion", 0, 0);
        scroll.Add(new SpellEffect
        {
            SpellId = "confusion",
            Targeting = TargetingMode.SingleTarget,
            Range = 10
        });
        scroll.Add(new Consumable());
        var inventory = player.GetOrAdd<Inventory>();
        inventory.Add(scroll);

        var result = TurnController.ProcessTurn(state,
            PlayerAction.CastSpell(scroll, targetEntityId: monster.Id));

        // Single-target status spells emit SpellEvent with StatusApplied and TargetId set.
        var spellEvent = result.Events.OfType<SpellEvent>()
            .FirstOrDefault(e => e.ActorId == player.Id && e.TargetId == monster.Id);

        Assert.That(spellEvent, Is.Not.Null,
            "SpellEvent should be emitted when a targeted status spell is cast on a monster.");
        Assert.That(spellEvent!.StatusApplied, Is.EqualTo("disoriented"),
            "Confusion spell should apply 'disoriented' effect name via SpellEvent.StatusApplied.");
        Assert.That(monster.Has<DisorientationEffect>(), Is.True,
            "DisorientationEffect should be applied to the monster.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // StatusExpiredEvent
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void StatusExpiredEvent_InTurnResult_WhenDurationHits0()
    {
        // StatusExpiredEvent should appear in the turn result when an effect expires.
        var (state, player, _) = CreateState();
        player.Add(new SlowedEffect { RemainingTurns = 1 });

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var expired = result.Events.OfType<StatusExpiredEvent>()
            .FirstOrDefault(e => e.EntityId == player.Id && e.EffectName == "slowed");

        Assert.That(expired, Is.Not.Null,
            "StatusExpiredEvent should be emitted when SlowedEffect expires after 1 turn.");
        Assert.That(expired!.Reason, Is.EqualTo("duration"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DotDamageEvent
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void DotDamageEvent_InTurnResult_WithCorrectDamageAmount()
    {
        // PoisonEffect should emit DotDamageEvent with the exact damage amount each tick.
        var (state, player, _) = CreateState(playerHp: 100);
        player.Add(new PoisonEffect { RemainingTurns = 5, DamagePerTurn = 3 });

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var dotEvent = result.Events.OfType<DotDamageEvent>()
            .FirstOrDefault(e => e.EntityId == player.Id && e.EffectName == "poison");

        Assert.That(dotEvent, Is.Not.Null, "DotDamageEvent should be emitted for PoisonEffect tick.");
        Assert.That(dotEvent!.Damage, Is.EqualTo(3), "DotDamageEvent should report exactly 3 damage.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // HotHealEvent
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void HotHealEvent_InTurnResult_WithCorrectHealAmount()
    {
        // RegenerationEffect should emit HotHealEvent each tick.
        var (state, player, _) = CreateState(playerHp: 80);
        player.Add(new RegenerationEffect { RemainingTurns = 5, HealPerTurn = 2 });

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var hotEvent = result.Events.OfType<HotHealEvent>()
            .FirstOrDefault(e => e.EntityId == player.Id && e.EffectName == "regeneration");

        Assert.That(hotEvent, Is.Not.Null, "HotHealEvent should be emitted for RegenerationEffect tick.");
        Assert.That(hotEvent!.Amount, Is.GreaterThanOrEqualTo(1),
            "HotHealEvent should report at least 1 heal (actual heal may be 0 if already at max HP).");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Multiple effects expiring
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void MultipleEffectsExpiring_AllEventsEmitted()
    {
        // Multiple effects expiring in the same turn should all emit StatusExpiredEvent.
        var (state, player, _) = CreateState();
        player.Add(new SlowedEffect { RemainingTurns = 1 });
        player.Add(new BlindedEffect { RemainingTurns = 1 });
        player.Add(new WeaknessEffect { RemainingTurns = 1 });

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var expiredNames = result.Events.OfType<StatusExpiredEvent>()
            .Where(e => e.EntityId == player.Id)
            .Select(e => e.EffectName)
            .ToHashSet();

        Assert.That(expiredNames, Contains.Item("slowed"), "SlowedEffect should emit StatusExpiredEvent.");
        Assert.That(expiredNames, Contains.Item("blinded"), "BlindedEffect should emit StatusExpiredEvent.");
        Assert.That(expiredNames, Contains.Item("weakness"), "WeaknessEffect should emit StatusExpiredEvent.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Effect refresh (no-double-apply)
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void EffectRefresh_NoDoubleApplyEvent()
    {
        // Re-applying an effect via ApplyEffect should refresh, not add a second component.
        var (state, player, _) = CreateState();

        StatusEffectProcessor.ApplyEffect<PoisonEffect>(player, 10);
        StatusEffectProcessor.ApplyEffect<PoisonEffect>(player, 5); // should not add second

        // Only one PoisonEffect component should exist.
        var effects = player.GetAllComponents().OfType<IStatusEffect>()
            .Where(e => e.EffectName == "poison").ToList();

        Assert.That(effects.Count, Is.EqualTo(1),
            "Re-applying PoisonEffect should refresh, not stack (no duplicate component).");
        Assert.That(effects[0].RemainingTurns, Is.EqualTo(10),
            "Duration should be the max of the two applied durations (10, not 5).");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SkipTurnEvent
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void SkipTurnEvent_EmittedWhenSlowSkips()
    {
        // SlowedEffect skips odd-numbered turns. Turn 1 (turnCount=1) → skip.
        var (state, player, _) = CreateState();
        player.Add(new SlowedEffect { RemainingTurns = 5 });

        // First turn processed increments TurnCount to 1 (odd) → player skips.
        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var skipEvents = result.Events.OfType<SkipTurnEvent>()
            .Where(e => e.EntityId == player.Id && e.EffectName == "slowed").ToList();

        Assert.That(skipEvents, Is.Not.Empty,
            "SkipTurnEvent should be emitted when SlowedEffect causes a skipped turn.");
    }
}
