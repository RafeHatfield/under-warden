using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Phase 1 lifecycle tests for the status effect system.
/// Covers: DOT damage, HOT healing, skip-turn logic, duration decrement,
/// expiry, no-stack/refresh, floor descent clear, and deadlock regression.
///
/// All tests use minimal game state helpers — no YAML loading required.
/// </summary>
[TestFixture]
public class StatusEffectLifecycleTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal game state: player at (3,6), one monster at (4,6) (adjacent).
    /// Both have enough HP to survive multiple DOT ticks.
    /// </summary>
    private static GameState CreateState(int seed = 1337, int playerHp = 100, int monsterHp = 100, int turnLimit = 200)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 6, blocksMovement: true);
        player.Add(new Fighter(hp: playerHp, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var monster = new Entity(1, "Orc", 4, 6, blocksMovement: true);
        monster.Add(new Fighter(hp: monsterHp, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        map.RegisterEntity(monster);

        return new GameState(player, new List<Entity> { monster }, map, rng, turnLimit);
    }

    /// <summary>
    /// Run N turns passing a Wait action (player does nothing each turn).
    /// Returns all accumulated events.
    /// </summary>
    private static List<TurnEvent> RunWaitTurns(GameState state, int count)
    {
        var allEvents = new List<TurnEvent>();
        for (int i = 0; i < count; i++)
        {
            var result = TurnController.ProcessTurn(state, PlayerAction.Wait);
            allEvents.AddRange(result.Events);
            if (result.GameOver) break;
        }
        return allEvents;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Poison — DOT
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void Poison_TicksDamageEachTurn()
    {
        var state = CreateState(playerHp: 100);
        var player = state.Player;
        var fighter = player.Require<Fighter>();
        int hpBefore = fighter.Hp;

        player.Add(new PoisonEffect { RemainingTurns = 3, DamagePerTurn = 2 });
        RunWaitTurns(state, 1);

        // Player took 2 DOT damage from poison tick at start of turn
        Assert.That(fighter.Hp, Is.EqualTo(hpBefore - 2), "Poison should deal 2 damage per turn");
    }

    [Test]
    public void Poison_ExpiredAfterDuration()
    {
        var state = CreateState(playerHp: 100);
        var player = state.Player;

        player.Add(new PoisonEffect { RemainingTurns = 3, DamagePerTurn = 2 });

        // Run 4 turns — effect should expire after 3 ticks
        RunWaitTurns(state, 4);

        Assert.That(player.Has<PoisonEffect>(), Is.False, "PoisonEffect should be removed after duration expires");
    }

    [Test]
    public void Poison_DotEventEmittedPerTick()
    {
        var state = CreateState(playerHp: 100);
        var player = state.Player;

        player.Add(new PoisonEffect { RemainingTurns = 2, DamagePerTurn = 2 });
        var allEvents = RunWaitTurns(state, 2);

        var dotEvents = allEvents.OfType<DotDamageEvent>()
            .Where(e => e.EntityId == player.Id && e.EffectName == "poison")
            .ToList();
        Assert.That(dotEvents.Count, Is.EqualTo(2), "DotDamageEvent should be emitted each tick");
        Assert.That(dotEvents[0].Damage, Is.EqualTo(2));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Burning — DOT
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void Burning_TicksDamageEachTurn()
    {
        var state = CreateState(playerHp: 100);
        var player = state.Player;
        var fighter = player.Require<Fighter>();
        int hpBefore = fighter.Hp;

        player.Add(new BurningEffect { RemainingTurns = 2, DamagePerTurn = 3 });
        RunWaitTurns(state, 1);

        Assert.That(fighter.Hp, Is.EqualTo(hpBefore - 3), "Burning should deal 3 damage per turn");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Regeneration — HOT
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void Regeneration_HealsEachTurn()
    {
        // Damage player first so there's HP to restore
        var state = CreateState(playerHp: 50);
        var player = state.Player;
        var fighter = player.Require<Fighter>();
        fighter.TakeDamage(20); // HP now 30
        int hpAfterDamage = fighter.Hp;

        player.Add(new RegenerationEffect { RemainingTurns = 3, HealPerTurn = 2 });
        RunWaitTurns(state, 1);

        Assert.That(fighter.Hp, Is.EqualTo(hpAfterDamage + 2), "Regen should heal 2 HP per turn");
    }

    [Test]
    public void Regeneration_HotEventEmitted()
    {
        var state = CreateState(playerHp: 50);
        var player = state.Player;
        var fighter = player.Require<Fighter>();
        fighter.TakeDamage(20);

        player.Add(new RegenerationEffect { RemainingTurns = 2, HealPerTurn = 2 });
        var allEvents = RunWaitTurns(state, 2);

        var hotEvents = allEvents.OfType<HotHealEvent>()
            .Where(e => e.EntityId == player.Id && e.EffectName == "regeneration")
            .ToList();
        Assert.That(hotEvents.Count, Is.EqualTo(2), "HotHealEvent should be emitted each tick");
        Assert.That(hotEvents[0].Amount, Is.EqualTo(2));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SlowedEffect — skip odd turns
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void Slow_SkipsOddTurns()
    {
        // Put player far from monster (no combat) so skip behavior is observable via WaitEvent
        var state = CreateState(playerHp: 100);
        var player = state.Player;

        player.Add(new SlowedEffect { RemainingTurns = 5 });

        // Turn 1 (odd) → should be skipped
        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);
        var skipEvents = result.Events.OfType<SkipTurnEvent>()
            .Where(e => e.EntityId == player.Id && e.EffectName == "slowed")
            .ToList();
        Assert.That(skipEvents.Count, Is.EqualTo(1), "Turn 1 (odd) should emit SkipTurnEvent for slowed player");
    }

    [Test]
    public void Slow_ActsOnEvenTurns()
    {
        var state = CreateState(playerHp: 100);
        var player = state.Player;

        player.Add(new SlowedEffect { RemainingTurns = 5 });

        // Turn 1 (odd) — skipped
        TurnController.ProcessTurn(state, PlayerAction.Wait);
        // Turn 2 (even) — should act
        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var skipEvents = result.Events.OfType<SkipTurnEvent>()
            .Where(e => e.EntityId == player.Id && e.EffectName == "slowed")
            .ToList();
        Assert.That(skipEvents.Count, Is.EqualTo(0), "Turn 2 (even) should NOT emit SkipTurnEvent — slowed player acts");
    }

    [Test]
    public void Slow_SkipTurnEventEmitted()
    {
        var state = CreateState(playerHp: 100);
        var player = state.Player;

        player.Add(new SlowedEffect { RemainingTurns = 3 });
        // Turn 1 (TurnCount=1, odd)
        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(result.Events.OfType<SkipTurnEvent>().Any(e => e.EntityId == player.Id), Is.True,
            "SkipTurnEvent should be emitted when slowed player's turn is skipped");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ImmobilizedEffect — skip all turns
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void Immobilized_SkipsAllTurns()
    {
        var state = CreateState(playerHp: 100);
        var player = state.Player;

        player.Add(new ImmobilizedEffect { RemainingTurns = 3 });

        // Run 3 turns — all should be skipped
        var allEvents = RunWaitTurns(state, 3);

        var skipEvents = allEvents.OfType<SkipTurnEvent>()
            .Where(e => e.EntityId == player.Id && e.EffectName == "immobilized")
            .ToList();
        Assert.That(skipEvents.Count, Is.EqualTo(3), "All 3 turns should be skipped while immobilized");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SleepEffect
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void Sleep_SkipsTurns()
    {
        var state = CreateState(playerHp: 100);
        var player = state.Player;

        player.Add(new SleepEffect { RemainingTurns = 2 });
        var allEvents = RunWaitTurns(state, 2);

        var skipEvents = allEvents.OfType<SkipTurnEvent>()
            .Where(e => e.EntityId == player.Id && e.EffectName == "sleep")
            .ToList();
        Assert.That(skipEvents.Count, Is.EqualTo(2), "Both turns should be skipped while sleeping");
    }

    [Test]
    public void Sleep_WakesOnAttackDamage()
    {
        // Sleeping monster adjacent to player. Player attacks — monster should wake.
        var state = CreateState(playerHp: 100, monsterHp: 100);
        var monster = state.Monsters[0];
        monster.Add(new SleepEffect { RemainingTurns = 10 });

        // Player attacks until a hit (to trigger OnDamageTaken). Use seed with guaranteed hit.
        // Attack at least once — ensure monster loses Sleep on hit.
        bool woke = false;
        for (int i = 0; i < 30 && !woke; i++)
        {
            TurnController.ProcessTurn(state, PlayerAction.Attack(monster));
            woke = !monster.Has<SleepEffect>();
        }

        Assert.That(monster.Has<SleepEffect>(), Is.False, "SleepEffect should be removed when monster is hit by an attack");
    }

    [Test]
    public void Sleep_DoesNotWakeOnDotDamage()
    {
        // Sleeping player with poison — DOT should not wake them
        var state = CreateState(playerHp: 100, monsterHp: 100);
        var player = state.Player;
        player.Add(new SleepEffect { RemainingTurns = 5 });
        player.Add(new PoisonEffect { RemainingTurns = 3, DamagePerTurn = 2 });

        // Run 1 turn — DOT fires but sleep should persist
        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var dotEvents = result.Events.OfType<DotDamageEvent>().Where(e => e.EntityId == player.Id).ToList();
        Assert.That(dotEvents.Count, Is.GreaterThan(0), "DOT damage should have ticked");
        Assert.That(player.Has<SleepEffect>(), Is.True, "SleepEffect should NOT be removed by DOT damage");
    }

    [Test]
    public void Sleep_WakeEmitsExpiredEvent()
    {
        var state = CreateState(playerHp: 100, monsterHp: 100);
        var monster = state.Monsters[0];
        monster.Add(new SleepEffect { RemainingTurns = 10 });

        // Find a seed where the player hits the monster
        bool woke = false;
        List<TurnEvent> allEvents = new();
        for (int i = 0; i < 30 && !woke; i++)
        {
            var result = TurnController.ProcessTurn(state, PlayerAction.Attack(monster));
            allEvents.AddRange(result.Events);
            woke = !monster.Has<SleepEffect>();
        }

        var expiredEvents = allEvents.OfType<StatusExpiredEvent>()
            .Where(e => e.EntityId == monster.Id && e.EffectName == "sleep" && e.Reason == "woke_on_damage")
            .ToList();
        Assert.That(expiredEvents.Count, Is.GreaterThan(0), "StatusExpiredEvent(woke_on_damage) should be emitted when sleep breaks");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Effect Expiry
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void EffectExpiry_RemainingTurnsDecrements()
    {
        var state = CreateState();
        var player = state.Player;
        player.Add(new SlowedEffect { RemainingTurns = 3 });

        TurnController.ProcessTurn(state, PlayerAction.Wait);

        // After one turn, RemainingTurns should decrement from 3 to 2
        // (ProcessTurnEnd ran at the START of this turn on the previous state — wait, first turn there are no prior effects)
        // Actually: ProcessTurnEnd runs at start of turn. On turn 1, player has SlowedEffect=3.
        // ProcessTurnEnd decrements it to 2. Then ProcessTurnStart checks skip (odd turn → skip).
        // After turn 1: RemainingTurns = 2.
        var effect = player.Get<SlowedEffect>();
        Assert.That(effect, Is.Not.Null, "SlowedEffect should still exist after 1 of 3 turns");
        Assert.That(effect!.RemainingTurns, Is.EqualTo(2), "RemainingTurns should decrement by 1 per turn");
    }

    [Test]
    public void EffectExpiry_RemovesComponentAtZero()
    {
        var state = CreateState();
        var player = state.Player;
        player.Add(new SlowedEffect { RemainingTurns = 2 });

        // After 2 full turns, effect should be removed
        RunWaitTurns(state, 3); // run 3 to be sure

        Assert.That(player.Has<SlowedEffect>(), Is.False, "SlowedEffect should be removed when RemainingTurns reaches 0");
    }

    [Test]
    public void EffectExpiry_ExpiredEventEmitted()
    {
        var state = CreateState();
        var player = state.Player;
        player.Add(new SlowedEffect { RemainingTurns = 1 });

        // Turn 1: ProcessTurnEnd decrements 1→0 and removes it, emitting StatusExpiredEvent
        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var expiredEvents = result.Events.OfType<StatusExpiredEvent>()
            .Where(e => e.EntityId == player.Id && e.EffectName == "slowed" && e.Reason == "duration")
            .ToList();
        Assert.That(expiredEvents.Count, Is.EqualTo(1), "StatusExpiredEvent should be emitted when effect expires by duration");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // No-Stack / Refresh Rule
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void NoStack_ReapplyRefreshesDuration()
    {
        var state = CreateState();
        var player = state.Player;

        // Apply with 3 turns, then reapply with 10 turns — should refresh to 10
        StatusEffectProcessor.ApplyEffect<SlowedEffect>(player, 3);
        StatusEffectProcessor.ApplyEffect<SlowedEffect>(player, 10);

        var effect = player.Require<SlowedEffect>();
        Assert.That(effect.RemainingTurns, Is.EqualTo(10), "Reapply with longer duration should refresh to max");
    }

    [Test]
    public void NoStack_DoesNotAddDuplicate()
    {
        var state = CreateState();
        var player = state.Player;

        // Apply twice — should only have one component
        StatusEffectProcessor.ApplyEffect<SlowedEffect>(player, 5);
        StatusEffectProcessor.ApplyEffect<SlowedEffect>(player, 5);

        int statusEffectCount = player.GetAllComponents().OfType<SlowedEffect>().Count();
        Assert.That(statusEffectCount, Is.EqualTo(1), "Reapplying an effect should not create a second component");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Multiple Effects
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void MultipleEffects_AllTickIndependently()
    {
        var state = CreateState(playerHp: 100);
        var player = state.Player;
        var fighter = player.Require<Fighter>();
        fighter.TakeDamage(20); // damage to leave room for regen
        int hpBefore = fighter.Hp;

        player.Add(new PoisonEffect { RemainingTurns = 3, DamagePerTurn = 2 });
        player.Add(new RegenerationEffect { RemainingTurns = 3, HealPerTurn = 2 });

        RunWaitTurns(state, 1);

        // Net HP change should be 0 (poison -2, regen +2)
        Assert.That(fighter.Hp, Is.EqualTo(hpBefore), "Poison and Regen should both tick — net HP unchanged");
    }

    [Test]
    public void MultipleEffects_OneExpiresOtherContinues()
    {
        var state = CreateState(playerHp: 100);
        var player = state.Player;

        player.Add(new SlowedEffect { RemainingTurns = 1 }); // expires after 1 turn
        player.Add(new ImmobilizedEffect { RemainingTurns = 5 }); // persists

        // Run 2 turns
        RunWaitTurns(state, 2);

        Assert.That(player.Has<SlowedEffect>(), Is.False, "SlowedEffect (duration 1) should have expired");
        Assert.That(player.Has<ImmobilizedEffect>(), Is.True, "ImmobilizedEffect (duration 5) should still be active");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Plague — DOT on monster
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void Plague_TicksDamageEachTurn()
    {
        var state = CreateState(monsterHp: 100);
        var monster = state.Monsters[0];
        var fighter = monster.Require<Fighter>();
        int hpBefore = fighter.Hp;

        monster.Add(new PlagueEffect { RemainingTurns = 3, DamagePerTurn = 1 });
        RunWaitTurns(state, 1);

        Assert.That(fighter.Hp, Is.EqualTo(hpBefore - 1), "Plague should deal 1 damage per turn to the monster");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Floor Descent — ClearAllEffects
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void FloorDescent_ClearsAllEffects()
    {
        var state = CreateState();
        var player = state.Player;

        // Apply several effects
        player.Add(new PoisonEffect { RemainingTurns = 10 });
        player.Add(new SlowedEffect { RemainingTurns = 5 });
        player.Add(new ShieldEffect { RemainingTurns = 8 });

        // Clear all effects (simulates floor descent)
        StatusEffectProcessor.ClearAllEffects(player);

        Assert.That(player.Has<PoisonEffect>(), Is.False, "PoisonEffect should be cleared on floor descent");
        Assert.That(player.Has<SlowedEffect>(), Is.False, "SlowedEffect should be cleared on floor descent");
        Assert.That(player.Has<ShieldEffect>(), Is.False, "ShieldEffect should be cleared on floor descent");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ClearAllEffects — all effect types removed
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that ClearAllEffects removes every effect type without requiring a per-type
    /// registration list. This is the regression guard for DEBT-003: after collapsing
    /// RemoveEffect to use RemoveByType, adding a new IStatusEffect no longer requires
    /// updating a switch statement — this test catches any type that breaks that guarantee.
    /// </summary>
    [Test]
    public void ClearAllEffects_RemovesAllEffectTypes()
    {
        var state = CreateState();
        var player = state.Player;

        // Apply a spread of effect types covering DOT, HOT, skip-turn, buff, and debuff categories.
        // These include both effects with active ProcessTurnStart logic and inert stub effects.
        player.Add(new PoisonEffect         { RemainingTurns = 5, DamagePerTurn = 2 });
        player.Add(new BurningEffect        { RemainingTurns = 5, DamagePerTurn = 3 });
        player.Add(new PlagueEffect         { RemainingTurns = 5, DamagePerTurn = 1 });
        player.Add(new RegenerationEffect   { RemainingTurns = 5, HealPerTurn = 2 });
        player.Add(new SlowedEffect         { RemainingTurns = 5 });
        player.Add(new ImmobilizedEffect    { RemainingTurns = 5 });
        player.Add(new SleepEffect          { RemainingTurns = 5 });
        player.Add(new FearEffect           { RemainingTurns = 5 });
        player.Add(new BlindedEffect        { RemainingTurns = 5 });
        player.Add(new ShieldEffect         { RemainingTurns = 5 });
        player.Add(new SilencedEffect       { RemainingTurns = 5 });
        player.Add(new WeaknessEffect       { RemainingTurns = 5 });

        // Precondition: at least 5 effects attached
        int countBefore = player.GetAllComponents().OfType<IStatusEffect>().Count();
        Assert.That(countBefore, Is.GreaterThanOrEqualTo(5), "Precondition: at least 5 effects applied");

        StatusEffectProcessor.ClearAllEffects(player);

        // All IStatusEffect components must be gone after clear
        int countAfter = player.GetAllComponents().OfType<IStatusEffect>().Count();
        Assert.That(countAfter, Is.EqualTo(0),
            "ClearAllEffects must remove every IStatusEffect component — no type should be missed");

        // Spot-check individual types to produce clear failure messages if a specific type breaks
        Assert.That(player.Has<PoisonEffect>(),       Is.False, "PoisonEffect should be cleared");
        Assert.That(player.Has<BurningEffect>(),      Is.False, "BurningEffect should be cleared");
        Assert.That(player.Has<PlagueEffect>(),       Is.False, "PlagueEffect should be cleared");
        Assert.That(player.Has<RegenerationEffect>(), Is.False, "RegenerationEffect should be cleared");
        Assert.That(player.Has<SlowedEffect>(),       Is.False, "SlowedEffect should be cleared");
        Assert.That(player.Has<ImmobilizedEffect>(),  Is.False, "ImmobilizedEffect should be cleared");
        Assert.That(player.Has<SleepEffect>(),        Is.False, "SleepEffect should be cleared");
        Assert.That(player.Has<FearEffect>(),         Is.False, "FearEffect should be cleared");
        Assert.That(player.Has<BlindedEffect>(),      Is.False, "BlindedEffect should be cleared");
        Assert.That(player.Has<ShieldEffect>(),       Is.False, "ShieldEffect should be cleared");
        Assert.That(player.Has<SilencedEffect>(),     Is.False, "SilencedEffect should be cleared");
        Assert.That(player.Has<WeaknessEffect>(),     Is.False, "WeaknessEffect should be cleared");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Deadlock regression — SlowedEffect should not cause infinite loops
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    [CancelAfter(5000)]
    public void HarnessRun_SlowedPlayer_DoesNotDeadlock()
    {
        // A slowed player should skip odd turns but the TurnController should complete
        // without hanging. Run 10 turns and verify game progresses normally.
        var state = CreateState(playerHp: 100, monsterHp: 100, turnLimit: 20);
        var player = state.Player;

        player.Add(new SlowedEffect { RemainingTurns = 10 });

        // Run 10 turns — should complete within 5 seconds (Timeout attribute catches deadlocks)
        int completedTurns = 0;
        for (int i = 0; i < 10; i++)
        {
            TurnController.ProcessTurn(state, PlayerAction.Wait);
            completedTurns++;
        }

        Assert.That(completedTurns, Is.EqualTo(10), "All 10 turns should complete without deadlock");
        Assert.That(state.TurnCount, Is.EqualTo(10), "TurnCount should match completed turns");
    }
}
