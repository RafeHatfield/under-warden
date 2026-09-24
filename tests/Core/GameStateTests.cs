using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Explicit regression guard for GameState.IsGameOver and the new dungeon-mode properties.
///
/// The IsGameOver change is the highest-risk modification in the milestone.
/// These tests lock in both the old (scenario) and new (dungeon) behaviours so that
/// any accidental change to the flag guard is immediately caught.
/// </summary>
[TestFixture]
public class GameStateTests
{
    // Shared map — just needs to be valid, not specifically arena
    private static GameMap MakeMap() => GameMap.CreateArena(12, 12);

    private static Entity MakeLivePlayer()
    {
        var p = new Entity(0, "Player", 3, 6, blocksMovement: true);
        p.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        return p;
    }

    private static Entity MakeDeadPlayer()
    {
        var p = MakeLivePlayer();
        p.Require<Fighter>().TakeDamage(999);
        return p;
    }

    private static Entity MakeLiveMonster(int id = 1)
    {
        var m = new Entity(id, "Orc", 4, 6, blocksMovement: true);
        m.Add(new Fighter(hp: 28, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        return m;
    }

    private static Entity MakeDeadMonster(int id = 1)
    {
        var m = MakeLiveMonster(id);
        m.Require<Fighter>().TakeDamage(999);
        return m;
    }

    // ─── IsGameOver — scenario mode (IsDungeonMode=false) ───────────────────

    [Test]
    [Description("Regression: scenario path — all monsters dead → IsGameOver=true (existing behaviour)")]
    public void IsGameOver_ScenarioMode_AllMonstersDead_IsTrue()
    {
        var state = new GameState(MakeLivePlayer(), new List<Entity> { MakeDeadMonster() }, MakeMap(),
            new SeededRandom(1337));
        // IsDungeonMode defaults to false
        Assert.That(state.IsDungeonMode, Is.False, "Precondition: not dungeon mode");
        Assert.That(state.IsGameOver, Is.True, "Scenario: all monsters dead → game over");
    }

    [Test]
    [Description("Regression: scenario path — player dead → IsGameOver=true")]
    public void IsGameOver_ScenarioMode_PlayerDead_IsTrue()
    {
        var state = new GameState(MakeDeadPlayer(), new List<Entity> { MakeLiveMonster() }, MakeMap(),
            new SeededRandom(1337));
        Assert.That(state.IsGameOver, Is.True, "Scenario: player dead → game over");
    }

    [Test]
    [Description("Regression: scenario path — turn limit → IsGameOver=true")]
    public void IsGameOver_ScenarioMode_TurnLimit_IsTrue()
    {
        var state = new GameState(MakeLivePlayer(), new List<Entity> { MakeLiveMonster() }, MakeMap(),
            new SeededRandom(1337), turnLimit: 5);
        state.TurnCount = 5;
        Assert.That(state.IsGameOver, Is.True, "Scenario: turn limit reached → game over");
    }

    [Test]
    [Description("Regression: scenario path — player alive, monsters alive, no turn limit → not game over")]
    public void IsGameOver_ScenarioMode_BothAlive_IsFalse()
    {
        var state = new GameState(MakeLivePlayer(), new List<Entity> { MakeLiveMonster() }, MakeMap(),
            new SeededRandom(1337));
        Assert.That(state.IsGameOver, Is.False);
    }

    // ─── IsGameOver — dungeon mode (IsDungeonMode=true) ─────────────────────

    [Test]
    [Description("New behaviour: dungeon mode — all monsters dead does NOT trigger game over")]
    public void IsGameOver_DungeonMode_AllMonstersDead_IsFalse()
    {
        var state = new GameState(MakeLivePlayer(), new List<Entity> { MakeDeadMonster() }, MakeMap(),
            new SeededRandom(1337))
        {
            IsDungeonMode = true,
            CurrentDepth = 1,
        };
        Assert.That(state.IsGameOver, Is.False,
            "Dungeon mode: all monsters dead → floor clear, not game over");
    }

    [Test]
    [Description("Dungeon mode — player dead → IsGameOver=true")]
    public void IsGameOver_DungeonMode_PlayerDead_IsTrue()
    {
        var state = new GameState(MakeDeadPlayer(), new List<Entity> { MakeLiveMonster() }, MakeMap(),
            new SeededRandom(1337))
        {
            IsDungeonMode = true,
            CurrentDepth = 1,
        };
        Assert.That(state.IsGameOver, Is.True, "Dungeon mode: player dead → game over");
    }

    [Test]
    [Description("Dungeon mode — turn limit → IsGameOver=true")]
    public void IsGameOver_DungeonMode_TurnLimit_IsTrue()
    {
        var state = new GameState(MakeLivePlayer(), new List<Entity> { MakeLiveMonster() }, MakeMap(),
            new SeededRandom(1337), turnLimit: 5)
        {
            IsDungeonMode = true,
            CurrentDepth = 1,
        };
        state.TurnCount = 5;
        Assert.That(state.IsGameOver, Is.True, "Dungeon mode: turn limit reached → game over");
    }

    // ─── IsFloorClear ────────────────────────────────────────────────────────

    [Test]
    public void IsFloorClear_DungeonMode_AllMonstersDead_IsTrue()
    {
        var state = new GameState(MakeLivePlayer(), new List<Entity> { MakeDeadMonster() }, MakeMap(),
            new SeededRandom(1337))
        {
            IsDungeonMode = true,
        };
        Assert.That(state.IsFloorClear, Is.True);
    }

    [Test]
    public void IsFloorClear_ScenarioMode_AllMonstersDead_IsFalse()
    {
        // IsFloorClear is only meaningful in dungeon mode
        var state = new GameState(MakeLivePlayer(), new List<Entity> { MakeDeadMonster() }, MakeMap(),
            new SeededRandom(1337));
        Assert.That(state.IsDungeonMode, Is.False);
        Assert.That(state.IsFloorClear, Is.False,
            "IsFloorClear only true in dungeon mode");
    }

    [Test]
    public void IsFloorClear_DungeonMode_MonstersAlive_IsFalse()
    {
        var state = new GameState(MakeLivePlayer(), new List<Entity> { MakeLiveMonster() }, MakeMap(),
            new SeededRandom(1337))
        {
            IsDungeonMode = true,
        };
        Assert.That(state.IsFloorClear, Is.False);
    }

    // ─── PlayerOnStairDown ───────────────────────────────────────────────────

    [Test]
    public void PlayerOnStairDown_PlayerAtStairPosition_IsTrue()
    {
        var player = MakeLivePlayer();
        player.X = 5;
        player.Y = 7;

        var stair = new Entity(99, "Stair Down", 5, 7, blocksMovement: false);
        stair.Add(new Stair(isDown: true, targetDepth: 2));

        var state = new GameState(player, new List<Entity>(), MakeMap(), new SeededRandom(1337))
        {
            IsDungeonMode = true,
            StairDown = stair,
        };

        Assert.That(state.PlayerOnStairDown, Is.True);
    }

    [Test]
    public void PlayerOnStairDown_PlayerNotAtStairPosition_IsFalse()
    {
        var player = MakeLivePlayer();
        player.X = 3;
        player.Y = 6;

        var stair = new Entity(99, "Stair Down", 5, 7, blocksMovement: false);

        var state = new GameState(player, new List<Entity>(), MakeMap(), new SeededRandom(1337))
        {
            IsDungeonMode = true,
            StairDown = stair,
        };

        Assert.That(state.PlayerOnStairDown, Is.False);
    }

    [Test]
    public void PlayerOnStairDown_NoStairDown_IsFalse()
    {
        var state = new GameState(MakeLivePlayer(), new List<Entity>(), MakeMap(), new SeededRandom(1337))
        {
            IsDungeonMode = true,
            StairDown = null,
        };
        Assert.That(state.PlayerOnStairDown, Is.False);
    }

    // ─── IsDungeonMode defaults ───────────────────────────────────────────────

    [Test]
    public void IsDungeonMode_DefaultsToFalse()
    {
        var state = new GameState(MakeLivePlayer(), new List<Entity>(), MakeMap(), new SeededRandom(1337));
        Assert.That(state.IsDungeonMode, Is.False, "Must default to false — harness depends on this");
    }

    [Test]
    public void CurrentDepth_DefaultsToOne()
    {
        var state = new GameState(MakeLivePlayer(), new List<Entity>(), MakeMap(), new SeededRandom(1337));
        Assert.That(state.CurrentDepth, Is.EqualTo(1));
    }
}
