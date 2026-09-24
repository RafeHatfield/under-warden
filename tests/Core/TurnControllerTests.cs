using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

[TestFixture]
public class TurnControllerTests
{
    /// <summary>
    /// Create a minimal game state for testing. Player at (3,6), one monster at (4,6) (adjacent).
    /// </summary>
    private static GameState CreateSimpleState(int seed = 1337, int monsterHp = 28, int turnLimit = 100)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 6, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var monster = new Entity(1, "Orc", 4, 6, blocksMovement: true);
        monster.Add(new Fighter(hp: monsterHp, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        map.RegisterEntity(monster);

        var monsters = new List<Entity> { monster };
        return new GameState(player, monsters, map, rng, turnLimit);
    }

    /// <summary>
    /// Create a state with player far from monster (not adjacent) for move tests.
    /// </summary>
    private static GameState CreateDistantState(int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 2, 2, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var monster = new Entity(1, "Orc", 8, 8, blocksMovement: true);
        monster.Add(new Fighter(hp: 28, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        map.RegisterEntity(monster);

        return new GameState(player, new List<Entity> { monster }, map, rng);
    }

    [Test]
    public void ProcessTurn_AttackHit_EmitsAttackEvent()
    {
        var state = CreateSimpleState();
        var monster = state.Monsters[0];
        var action = PlayerAction.Attack(monster);

        var result = TurnController.ProcessTurn(state, action);

        var attackEvents = result.Events.OfType<AttackEvent>().Where(e => e.ActorId == 0).ToList();
        Assert.That(attackEvents, Is.Not.Empty, "Should have at least one player attack event");
        Assert.That(attackEvents[0].TargetId, Is.EqualTo(1));
        Assert.That(attackEvents[0].IsBonusAttack, Is.False, "First attack should not be a bonus attack");
    }

    [Test]
    public void ProcessTurn_AttackKillsMonster_EmitsDeathEvent()
    {
        // Low HP monster — guaranteed to die
        var state = CreateSimpleState(monsterHp: 1);
        var monster = state.Monsters[0];
        var action = PlayerAction.Attack(monster);

        var result = TurnController.ProcessTurn(state, action);

        var attackEvent = result.Events.OfType<AttackEvent>().First(e => e.ActorId == 0);
        if (attackEvent.Hit)
        {
            Assert.That(attackEvent.TargetKilled, Is.True);
            var deathEvents = result.Events.OfType<DeathEvent>().ToList();
            Assert.That(deathEvents, Has.Count.EqualTo(1));
            Assert.That(deathEvents[0].ActorId, Is.EqualTo(1));
            Assert.That(deathEvents[0].KillerId, Is.EqualTo(0));
        }
    }

    [Test]
    public void ProcessTurn_AllMonstersDefeated_GameOver()
    {
        var state = CreateSimpleState(monsterHp: 1);
        var monster = state.Monsters[0];

        // Keep attacking until monster dies
        TurnResult? result = null;
        for (int i = 0; i < 20; i++)
        {
            if (state.IsGameOver) break;
            result = TurnController.ProcessTurn(state, PlayerAction.Attack(monster));
            if (result.AllMonstersDefeated) break;
        }

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.AllMonstersDefeated, Is.True);
        Assert.That(result.GameOver, Is.True);
        Assert.That(result.PlayerDied, Is.False);
    }

    [Test]
    public void ProcessTurn_HealConsumesPotion_EmitsHealEvent()
    {
        var state = CreateSimpleState();
        var player = state.Player;

        // Add a potion and damage the player
        var inventory = player.Add(new Inventory());
        var potion = new Entity(99, "Healing Potion");
        potion.Add(new Consumable(healAmount: 40));
        inventory.Add(potion);
        state.PlayerFighter.TakeDamage(30);
        int hpBefore = state.PlayerFighter.Hp;

        var result = TurnController.ProcessTurn(state, PlayerAction.UseItem());

        var healEvents = result.Events.OfType<HealEvent>().ToList();
        Assert.That(healEvents, Has.Count.EqualTo(1));
        Assert.That(healEvents[0].AmountHealed, Is.GreaterThan(0));
        Assert.That(healEvents[0].ItemId, Is.EqualTo(99));
        Assert.That(state.PlayerFighter.Hp, Is.GreaterThan(hpBefore));
        Assert.That(inventory.Count, Is.EqualTo(0), "Potion should be consumed");
    }

    [Test]
    public void ProcessTurn_MoveUpdatesPosition_EmitsMoveEvent()
    {
        var state = CreateDistantState();
        var player = state.Player;
        int fromX = player.X, fromY = player.Y;

        var result = TurnController.ProcessTurn(state, PlayerAction.MoveToward(state.Monsters[0]));

        var moveEvents = result.Events.OfType<MoveEvent>().Where(e => e.ActorId == 0).ToList();
        Assert.That(moveEvents, Has.Count.EqualTo(1));
        Assert.That(moveEvents[0].FromX, Is.EqualTo(fromX));
        Assert.That(moveEvents[0].FromY, Is.EqualTo(fromY));
        Assert.That(player.X, Is.Not.EqualTo(fromX).Or.Not.EqualTo(fromY), "Player should have moved");
    }

    [Test]
    public void ProcessTurn_Wait_EmitsWaitEvent()
    {
        var state = CreateSimpleState();
        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var waitEvents = result.Events.OfType<WaitEvent>().ToList();
        Assert.That(waitEvents, Has.Count.EqualTo(1));
        Assert.That(waitEvents[0].ActorId, Is.EqualTo(0));
    }

    [Test]
    public void ProcessTurn_MonstersActAfterPlayer()
    {
        var state = CreateSimpleState();
        var monster = state.Monsters[0];
        var action = PlayerAction.Attack(monster);

        var result = TurnController.ProcessTurn(state, action);

        // Find first player event and first monster event
        int firstPlayerIdx = result.Events.FindIndex(e => e.ActorId == 0);
        int firstMonsterIdx = result.Events.FindIndex(e => e.ActorId == 1);

        if (firstMonsterIdx >= 0)
        {
            Assert.That(firstPlayerIdx, Is.LessThan(firstMonsterIdx),
                "Player events should come before monster events");
        }
    }

    [Test]
    public void ProcessTurn_MonsterMovesTowardPlayer_WhenDistant()
    {
        var state = CreateDistantState();
        var monster = state.Monsters[0];
        int monsterFromX = monster.X, monsterFromY = monster.Y;

        // Player waits, monster should move toward player
        TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(
            monster.X != monsterFromX || monster.Y != monsterFromY,
            Is.True, "Monster should have moved toward player");
    }

    [Test]
    public void ProcessTurn_IncrementsTurnCount()
    {
        var state = CreateSimpleState();
        Assert.That(state.TurnCount, Is.EqualTo(0));

        TurnController.ProcessTurn(state, PlayerAction.Wait);
        Assert.That(state.TurnCount, Is.EqualTo(1));

        TurnController.ProcessTurn(state, PlayerAction.Wait);
        Assert.That(state.TurnCount, Is.EqualTo(2));
    }

    [Test]
    public void ProcessTurn_Deterministic_SameSeedSameEvents()
    {
        var result1 = RunOneTurn(seed: 42);
        var result2 = RunOneTurn(seed: 42);

        Assert.That(result1.Events.Count, Is.EqualTo(result2.Events.Count));
        for (int i = 0; i < result1.Events.Count; i++)
        {
            var e1 = result1.Events[i];
            var e2 = result2.Events[i];
            Assert.That(e1.GetType(), Is.EqualTo(e2.GetType()));
            Assert.That(e1.ActorId, Is.EqualTo(e2.ActorId));

            if (e1 is AttackEvent a1 && e2 is AttackEvent a2)
            {
                Assert.That(a1.Hit, Is.EqualTo(a2.Hit));
                Assert.That(a1.Damage, Is.EqualTo(a2.Damage));
                Assert.That(a1.IsCritical, Is.EqualTo(a2.IsCritical));
                Assert.That(a1.TargetKilled, Is.EqualTo(a2.TargetKilled));
                Assert.That(a1.IsBonusAttack, Is.EqualTo(a2.IsBonusAttack));
            }
        }
    }

    [Test]
    public void ProcessTurn_DifferentSeeds_DifferentEvents()
    {
        // Run many turns with different seeds — results should eventually diverge
        bool foundDifference = false;
        for (int seed = 1; seed <= 20 && !foundDifference; seed++)
        {
            var r1 = RunOneTurn(seed: seed);
            var r2 = RunOneTurn(seed: seed + 1000);

            if (r1.Events.Count != r2.Events.Count)
            {
                foundDifference = true;
                continue;
            }
            for (int i = 0; i < r1.Events.Count; i++)
            {
                if (r1.Events[i] is AttackEvent a1 && r2.Events[i] is AttackEvent a2)
                {
                    if (a1.Hit != a2.Hit || a1.Damage != a2.Damage)
                    {
                        foundDifference = true;
                        break;
                    }
                }
            }
        }
        Assert.That(foundDifference, Is.True, "Different seeds should produce different results");
    }

    private static TurnResult RunOneTurn(int seed)
    {
        var state = CreateSimpleState(seed);
        return TurnController.ProcessTurn(state, PlayerAction.Attack(state.Monsters[0]));
    }

    // ─── Descend action tests ────────────────────────────────────────────────

    /// <summary>
    /// Build a dungeon-mode state where the player is on the stair and all monsters are dead.
    /// This is the success condition for Descend.
    /// </summary>
    private static GameState CreateDescendReadyState(int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        // Dead monster — floor is clear
        var deadMonster = new Entity(1, "Orc", 8, 8, blocksMovement: true);
        deadMonster.Add(new Fighter(hp: 1, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        deadMonster.Require<Fighter>().TakeDamage(999);
        map.RegisterEntity(deadMonster);

        // Stair at player position (5,5)
        var stair = new Entity(99, "Stair Down", 5, 5, blocksMovement: false);
        stair.Add(new Stair(isDown: true, targetDepth: 2));
        map.RegisterEntity(stair);

        return new GameState(player, new List<Entity> { deadMonster }, map, rng)
        {
            IsDungeonMode = true,
            CurrentDepth = 1,
            StairDown = stair,
        };
    }

    [Test]
    public void Descend_OnStair_FloorClear_DungeonMode_EmitsDescendEvent()
    {
        var state = CreateDescendReadyState();
        Assert.That(state.IsFloorClear, Is.True, "Precondition: floor is clear");
        Assert.That(state.PlayerOnStairDown, Is.True, "Precondition: player on stair");

        var result = TurnController.ProcessTurn(state, PlayerAction.Descend);

        var descendEvents = result.Events.OfType<DescendEvent>().ToList();
        Assert.That(descendEvents, Has.Count.EqualTo(1), "Should emit exactly one DescendEvent");
        Assert.That(descendEvents[0].ActorId, Is.EqualTo(0), "Actor is the player");
        Assert.That(descendEvents[0].NewDepth, Is.EqualTo(2), "New depth = current (1) + 1");
    }

    [Test]
    public void Descend_OnStair_FloorClear_DungeonMode_MonstersDoNotAct()
    {
        // A live monster somewhere on the map — but we only have dead monsters in state.Monsters
        // The point: on successful descend, monster turns are skipped entirely
        var state = CreateDescendReadyState();

        var result = TurnController.ProcessTurn(state, PlayerAction.Descend);

        // No monster attack events — they don't get to act when player descends
        var monsterAttacks = result.Events.OfType<AttackEvent>().Where(e => e.ActorId != 0).ToList();
        Assert.That(monsterAttacks, Is.Empty, "Monsters should not act on a successful Descend turn");
    }

    [Test]
    public void Descend_NotOnStair_EmitsWaitEvent()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 3, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        // Stair is elsewhere (not where player stands)
        var stair = new Entity(99, "Stair Down", 8, 8, blocksMovement: false);
        stair.Add(new Stair(isDown: true, targetDepth: 2));
        map.RegisterEntity(stair);

        var state = new GameState(player, new List<Entity>(), map, rng)
        {
            IsDungeonMode = true,
            CurrentDepth = 1,
            StairDown = stair,
        };

        var result = TurnController.ProcessTurn(state, PlayerAction.Descend);

        Assert.That(result.Events.OfType<DescendEvent>(), Is.Empty, "No DescendEvent — not on stair");
        Assert.That(result.Events.OfType<WaitEvent>().Any(e => e.ActorId == 0), Is.True,
            "Failed Descend emits WaitEvent");
    }

    [Test]
    public void Descend_MonstersAlive_EmitsWaitEvent()
    {
        // Player IS on stair, but monster is still alive — floor not clear
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var liveMonster = new Entity(1, "Orc", 8, 8, blocksMovement: true);
        liveMonster.Add(new Fighter(hp: 28, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        map.RegisterEntity(liveMonster);

        // Stair AT player position
        var stair = new Entity(99, "Stair Down", 5, 5, blocksMovement: false);
        stair.Add(new Stair(isDown: true, targetDepth: 2));
        map.RegisterEntity(stair);

        var state = new GameState(player, new List<Entity> { liveMonster }, map, rng)
        {
            IsDungeonMode = true,
            CurrentDepth = 1,
            StairDown = stair,
        };

        Assert.That(state.IsFloorClear, Is.False, "Precondition: floor is NOT clear");

        var result = TurnController.ProcessTurn(state, PlayerAction.Descend);

        // Design: player can descend any time they're on the stair — floor clear is not required.
        Assert.That(result.Events.OfType<DescendEvent>().Any(), Is.True,
            "Player can descend even with monsters alive");
    }

    [Test]
    public void Descend_NotDungeonMode_EmitsWaitEvent()
    {
        // Scenario mode — Descend should be a no-op (WaitEvent)
        var state = CreateSimpleState();
        Assert.That(state.IsDungeonMode, Is.False, "Precondition: not dungeon mode");

        var result = TurnController.ProcessTurn(state, PlayerAction.Descend);

        Assert.That(result.Events.OfType<DescendEvent>(), Is.Empty,
            "Descend in scenario mode emits WaitEvent, not DescendEvent");
        Assert.That(result.Events.OfType<WaitEvent>().Any(e => e.ActorId == 0), Is.True);
    }
}
