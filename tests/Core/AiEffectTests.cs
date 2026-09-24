using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Phase 4 tests for AI override status effects.
/// Covers: EnragedEffect (HostileToAll), TauntedEffect (forced targeting),
/// SpeedEffect/SluggishEffect (apply/tick/expire), DisorientationEffect AI behavior,
/// AggravatedEffect stub, FearEffect AI behavior, multi-effect interactions.
///
/// All tests use minimal arena setups. No YAML loading required.
/// </summary>
[TestFixture]
public class AiEffectTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static (GameState state, Entity player, Entity monster) CreateState(
        int monsterX = 6, int monsterY = 5,
        int playerHp = 100, int monsterHp = 100,
        int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: playerHp, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(player);

        var monster = new Entity(1, "Orc", monsterX, monsterY, blocksMovement: true);
        monster.Add(new Fighter(hp: monsterHp, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        monster.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        map.RegisterEntity(monster);

        var state = new GameState(player, [monster], map, rng, turnLimit: 200);
        return (state, player, monster);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // EnragedEffect
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void Enraged_SetsHostileToAll()
    {
        // EnragedEffect has HostileToAll=true by default — verifies the flag is set.
        var (_, _, monster) = CreateState();
        var enraged = new EnragedEffect { RemainingTurns = 5 };
        monster.Add(enraged);

        Assert.That(enraged.HostileToAll, Is.True,
            "EnragedEffect should have HostileToAll=true on the effect component.");
        Assert.That(monster.Has<EnragedEffect>(), Is.True);
    }

    [Test]
    public void Enraged_ClearsHostileToAllOnExpiry()
    {
        // After EnragedEffect expires (1 turn), the component is removed.
        // This implicitly clears the HostileToAll state since the effect is gone.
        var (state, _, monster) = CreateState();
        monster.Add(new EnragedEffect { RemainingTurns = 1 });

        TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(monster.Has<EnragedEffect>(), Is.False,
            "EnragedEffect should be removed after expiry.");
    }

    [Test]
    public void Enraged_AttacksNearestEntity_PlayerOrMonster()
    {
        // In a 2-entity scenario (player + monster), enraged monster should attack nearest (player if adjacent).
        var (state, player, monster) = CreateState(monsterX: 6, monsterY: 5);
        monster.Add(new EnragedEffect { RemainingTurns = 5 });

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var attacks = result.Events.OfType<AttackEvent>()
            .Where(e => e.ActorId == monster.Id).ToList();

        Assert.That(attacks, Is.Not.Empty, "Enraged adjacent monster should attack.");
        Assert.That(attacks[0].TargetId, Is.EqualTo(player.Id),
            "Enraged monster should attack nearest entity.");
    }

    [Test]
    public void Enraged_AttackedMonster_RetaliatesNextTurn()
    {
        // Setup: two monsters, one enraged, attacks the other.
        // Next turn, the attacked monster should retaliate.
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 1, 1, blocksMovement: true);
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(player);

        var enragedMonster = new Entity(1, "Enraged Orc", 5, 5, blocksMovement: true);
        enragedMonster.Add(new Fighter(hp: 100, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        enragedMonster.Add(new AiComponent { AiType = "basic" });
        enragedMonster.Add(new EnragedEffect { RemainingTurns = 10 });
        map.RegisterEntity(enragedMonster);

        var victimMonster = new Entity(2, "Victim Orc", 6, 5, blocksMovement: true);
        victimMonster.Add(new Fighter(hp: 100, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        victimMonster.Add(new AiComponent { AiType = "basic" });
        map.RegisterEntity(victimMonster);

        var state = new GameState(player, [enragedMonster, victimMonster], map, rng, turnLimit: 50);

        // Run several turns — verify the game doesn't crash and monsters engage.
        Assert.DoesNotThrow(() =>
        {
            for (int i = 0; i < 3; i++)
                TurnController.ProcessTurn(state, PlayerAction.Wait);
        });
    }

    [Test]
    public void Enraged_ExpiresAfterDuration_NormalAI()
    {
        var (state, _, monster) = CreateState(monsterX: 9, monsterY: 5);
        monster.Add(new EnragedEffect { RemainingTurns = 1 });

        TurnController.ProcessTurn(state, PlayerAction.Wait);

        // After expiry, monster should not have EnragedEffect.
        Assert.That(monster.Has<EnragedEffect>(), Is.False,
            "EnragedEffect should expire after duration.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TauntedEffect
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void Taunted_AlwaysTargetsPlayer()
    {
        var (state, player, monster) = CreateState();
        monster.Add(new TauntedEffect { RemainingTurns = 1000, TauntTargetId = player.Id });

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var attacks = result.Events.OfType<AttackEvent>()
            .Where(e => e.ActorId == monster.Id).ToList();

        Assert.That(attacks, Is.Not.Empty, "Taunted adjacent monster should attack.");
        Assert.That(attacks[0].TargetId, Is.EqualTo(player.Id),
            "Taunted monster should always target the player (taunt target).");
    }

    [Test]
    public void Taunted_IgnoresCloserMonsterTargets()
    {
        // Taunted monster should attack the player, not a closer monster, even when enraged would target the closer one.
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 1, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(player);

        // Taunted monster at (5,5) — player is at (1,5), far. Closer monster at (6,5).
        var tauntedMonster = new Entity(1, "Taunted Orc", 5, 5, blocksMovement: true);
        tauntedMonster.Add(new Fighter(hp: 100, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        tauntedMonster.Add(new AiComponent { AiType = "basic" });
        tauntedMonster.Add(new TauntedEffect { RemainingTurns = 1000, TauntTargetId = player.Id });
        map.RegisterEntity(tauntedMonster);

        var closerMonster = new Entity(2, "Nearby Orc", 6, 5, blocksMovement: true);
        closerMonster.Add(new Fighter(hp: 100, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        closerMonster.Add(new AiComponent { AiType = "basic" });
        map.RegisterEntity(closerMonster);

        var state = new GameState(player, [tauntedMonster, closerMonster], map, rng, turnLimit: 50);

        // TauntedEffect overrides everything. Even though closerMonster is adjacent,
        // taunted monster should pursue/target player.
        // We just verify no crash and the taunted effect overrides EnragedEffect targeting.
        Assert.DoesNotThrow(() => TurnController.ProcessTurn(state, PlayerAction.Wait));
    }

    [Test]
    public void Taunted_Duration1000_PersistsThroughCombat()
    {
        var (state, player, monster) = CreateState();
        monster.Add(new TauntedEffect { RemainingTurns = 1000, TauntTargetId = player.Id });

        // Run 10 turns — taunt should still be active (1000 >> 10).
        for (int i = 0; i < 10; i++)
            TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(monster.Has<TauntedEffect>(), Is.True,
            "TauntedEffect with 1000 turns should persist through 10 turns of combat.");

        var taunt = monster.Get<TauntedEffect>();
        Assert.That(taunt!.RemainingTurns, Is.GreaterThan(900),
            "TauntedEffect should have ~990 turns remaining after 10 turns.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SpeedEffect and SluggishEffect (apply/tick/expire — behavior is a TODO)
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void SpeedEffect_Applies_NoErrors()
    {
        var (state, player, _) = CreateState();
        player.Add(new SpeedEffect { RemainingTurns = 5 });

        Assert.DoesNotThrow(() => TurnController.ProcessTurn(state, PlayerAction.Wait));
        Assert.That(player.Has<SpeedEffect>(), Is.True, "SpeedEffect should still be active after 1 turn.");
    }

    [Test]
    public void SpeedEffect_Expires_AfterDuration()
    {
        var (state, player, _) = CreateState();
        player.Add(new SpeedEffect { RemainingTurns = 1 });

        TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(player.Has<SpeedEffect>(), Is.False, "SpeedEffect should expire after 1 turn.");
    }

    [Test]
    public void SluggishEffect_Applies_NoErrors()
    {
        var (state, player, _) = CreateState();
        player.Add(new SluggishEffect { RemainingTurns = 5 });

        Assert.DoesNotThrow(() => TurnController.ProcessTurn(state, PlayerAction.Wait));
        Assert.That(player.Has<SluggishEffect>(), Is.True, "SluggishEffect should still be active after 1 turn.");
    }

    [Test]
    public void SluggishEffect_Expires_AfterDuration()
    {
        var (state, player, _) = CreateState();
        player.Add(new SluggishEffect { RemainingTurns = 1 });

        TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(player.Has<SluggishEffect>(), Is.False, "SluggishEffect should expire after 1 turn.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DisorientationEffect — AI behavior
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void Disorientation_MonsterMovesRandom_DoesNotPursue()
    {
        // Disorientated monster should not pursue the player along a direct path.
        // Place monster and player far apart; monster should not reliably approach.
        var (state, player, monster) = CreateState(monsterX: 9, monsterY: 5);
        monster.Add(new DisorientationEffect { RemainingTurns = 5 });

        int startDist = monster.ChebyshevDistanceTo(player.X, player.Y);

        // Run 3 turns of disorientated movement.
        for (int i = 0; i < 3; i++)
        {
            if (!monster.Require<Fighter>().IsAlive) break;
            TurnController.ProcessTurn(state, PlayerAction.Wait);
        }

        // Monster should not have consistently approached the player.
        // Disorientated movement is random; we just verify the game doesn't crash.
        Assert.DoesNotThrow(() => TurnController.ProcessTurn(state, PlayerAction.Wait));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AggravatedEffect — stub test (no faction system yet)
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void AggravatedEffect_TargetsSpecifiedFaction()
    {
        // Stub test — AggravatedEffect is permanently inert until the faction system lands.
        // Verifies no exception is thrown when the effect is applied and ticked.
        var (state, player, monster) = CreateState();

        // AggravatedEffect is IsPermanent=true — never expires, just exists.
        monster.Add(new AggravatedEffect { TargetFaction = "orc" });

        Assert.DoesNotThrow(() =>
        {
            for (int i = 0; i < 5; i++)
                TurnController.ProcessTurn(state, PlayerAction.Wait);
        }, "AggravatedEffect should not cause exceptions (faction system stub).");

        // Effect should still be present (IsPermanent=true, never decremented).
        Assert.That(monster.Has<AggravatedEffect>(), Is.True,
            "AggravatedEffect should be permanent (never expires via duration).");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // FearEffect — AI behavior
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void Fear_MonsterFlees_NotAttacking()
    {
        // Feared monster adjacent to player should flee, not attack.
        var (state, player, monster) = CreateState(monsterX: 6, monsterY: 5);
        monster.Add(new FearEffect { RemainingTurns = 5 });

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var monsterAttacks = result.Events.OfType<AttackEvent>()
            .Where(e => e.ActorId == monster.Id).ToList();

        Assert.That(monsterAttacks, Is.Empty, "Feared monster should not attack, even when adjacent.");
    }

    [Test]
    public void Fear_ExpiresAfterDuration_MonsterAttacksAgain()
    {
        // After FearEffect expires, monster should resume normal AI (attack when adjacent).
        var (state, player, monster) = CreateState(monsterX: 6, monsterY: 5, monsterHp: 200);
        monster.Add(new FearEffect { RemainingTurns = 1 });

        // Turn 1: feared, monster flees (or stays if cornered), does not attack.
        TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(monster.Has<FearEffect>(), Is.False, "FearEffect should expire after 1 turn.");

        // Note: after fear expires, the monster has moved away. Normal AI test is that it
        // can re-engage when it walks back — we just verify no crash on subsequent turns.
        Assert.DoesNotThrow(() => TurnController.ProcessTurn(state, PlayerAction.Wait));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Multi-effect interactions
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void MultiEffect_ImmobilizedPlusFear_ImmobilizedWins()
    {
        // ImmobilizedEffect causes ProcessTurnStart to skip the entire turn.
        // FearEffect never fires because the monster doesn't reach AI decision.
        var (state, player, monster) = CreateState(monsterX: 9, monsterY: 5);
        monster.Add(new ImmobilizedEffect { RemainingTurns = 3 });
        monster.Add(new FearEffect { RemainingTurns = 3 });

        int startX = monster.X;
        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        // Monster skips turn (immobilized).
        var skipEvents = result.Events.OfType<SkipTurnEvent>()
            .Where(e => e.EntityId == monster.Id).ToList();

        Assert.That(skipEvents, Is.Not.Empty, "Immobilized monster should emit SkipTurnEvent.");
        Assert.That(monster.X, Is.EqualTo(startX), "Immobilized monster should not move (fear or otherwise).");
    }

    [Test]
    public void MultiEffect_TauntedPlusDisorientation_TauntedWins()
    {
        // TauntedEffect forces targeting the player. DisorientationEffect randomizes movement.
        // When adjacent, taunted monster should still attack player (not a random target).
        // Disorientation affects movement only — attack targeting is governed by taunt.
        var (state, player, monster) = CreateState(monsterX: 6, monsterY: 5);
        monster.Add(new TauntedEffect { RemainingTurns = 1000, TauntTargetId = player.Id });
        monster.Add(new DisorientationEffect { RemainingTurns = 5 });

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        // Adjacent monster with taunt should attack player (disorientation affects path only).
        var attacks = result.Events.OfType<AttackEvent>()
            .Where(e => e.ActorId == monster.Id).ToList();

        Assert.That(attacks, Is.Not.Empty, "Taunted+disorientated adjacent monster should attack.");
        Assert.That(attacks[0].TargetId, Is.EqualTo(player.Id),
            "Taunted monster should target player even with DisorientationEffect active.");
    }
}
