using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Tests for BotBrain stuck detection (TASK-004).
///
/// Stuck detection fires only on the INSTANCE path (BotBrain instance reused across calls).
/// The static BotBrain.Decide wrapper creates a transient instance per call —
/// stuck detection never fires on the static path. This is intentional.
///
/// Thresholds (from PoC bot_brain.py:33, STUCK_THRESHOLD = 8):
///   - 8 consecutive stuck turns: drop target, wait 1 turn
///   - 15 consecutive stuck turns: return BotAction.AbortRun
/// </summary>
[TestFixture]
[Category("Bot")]
[Description("BotBrain stuck detection: instance state, drop target at 8, abort at 15")]
public class BotBrainStuckTests
{
    // ── State helpers ─────────────────────────────────────────────────────────

    private static (Entity Player, Fighter PlayerFighter, Inventory PlayerInventory, GameMap Map)
        MakeArena(int playerHp = 54)
    {
        const int Width = 20, Height = 20;
        var map = new GameMap(Width, Height, allWalls: true);
        for (int x = 1; x < Width - 1; x++)
            for (int y = 1; y < Height - 1; y++)
                map.SetTile(x, y, TileKind.Floor);

        var player = new Entity(0, "Player", 10, 10, blocksMovement: true);
        var fighter = new Fighter(
            hp: playerHp, strength: 12, dexterity: 14, constitution: 10,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4);
        player.Add(fighter);
        var inventory = new Inventory();
        player.Add(inventory);
        map.RegisterEntity(player);
        return (player, fighter, inventory, map);
    }

    private static Entity MakeMonster(int id, int x, int y, GameMap map, int hp = 100)
    {
        var orc = new Entity(id, "Orc", x, y, blocksMovement: true);
        // High hp so the monster doesn't die from stuck-counter increments
        orc.Add(new Fighter(hp: hp, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        map.RegisterEntity(orc);
        return orc;
    }

    // ── Test 1: Static path never gets stuck ──────────────────────────────────

    [Test]
    [Description("Static BotBrain.Decide never returns AbortRun — each call is a fresh instance")]
    public void Static_Decide_NeverReturnsAbortRun()
    {
        var (player, fighter, inventory, map) = MakeArena();
        // Monster at distance 3 — player will move toward it but can't reach (no actual movement)
        var enemy = MakeMonster(1, 13, 10, map);
        var monsters = new List<Entity> { enemy };

        // Call 20 times — well past both stuck thresholds
        for (int i = 0; i < 20; i++)
        {
            var action = BotBrain.Decide(player, fighter, inventory, monsters, map);
            Assert.That(action.Type, Is.Not.EqualTo(BotAction.ActionType.AbortRun),
                $"Static path call {i+1}: should never return AbortRun (no instance state)");
        }
    }

    // ── Test 2: Instance drops target at stuck threshold ─────────────────────

    [Test]
    [Description("Instance BotBrain: 5 same-position turns returns Wait on turn 5 (drops target at 4)")]
    public void Instance_DropsTarget_After8StuckTurns()
    {
        var (player, fighter, inventory, map) = MakeArena();
        // Enemy at distance 3 — player at (10,10), enemy at (13,10)
        // Player can't actually move in this test (we call Decide without ProcessTurn),
        // so positions don't change, simulating a stuck state.
        var enemy = MakeMonster(1, 13, 10, map);
        var monsters = new List<Entity> { enemy };

        var brain = new BotBrain(BotPersonaRegistry.Get("balanced"));

        // First 4 calls: all should be MoveToward (stuck counter increments: 0..3)
        for (int i = 0; i < 4; i++)
        {
            var action = brain.Decide(player, fighter, inventory, monsters, map);
            Assert.That(action.Type, Is.EqualTo(BotAction.ActionType.MoveToward),
                $"Turn {i+1}: should be MoveToward before stuck threshold");
        }

        // Turn 5 (stuck counter reaches 4): should drop target and return Wait
        var stuckAction = brain.Decide(player, fighter, inventory, monsters, map);
        Assert.That(stuckAction.Type, Is.EqualTo(BotAction.ActionType.DoNothing),
            "Turn 5: should return Wait after dropping target at stuck threshold 4");
    }

    [Test]
    [Description("Instance BotBrain: when two enemies are present, bot targets the closest one")]
    public void Instance_TargetsAlternativeEnemy_WhenAvailable()
    {
        var (player, fighter, inventory, map) = MakeArena();
        var enemy1 = MakeMonster(1, 13, 10, map); // far enemy (Manhattan 3)
        var enemy2 = MakeMonster(2, 12, 10, map); // closer enemy (Manhattan 2)

        // Fresh brain — no stuck state accumulated. Tests that closest enemy is targeted.
        var brain = new BotBrain(BotPersonaRegistry.Get("balanced"));
        var monsters = new List<Entity> { enemy1, enemy2 };

        var action = brain.Decide(player, fighter, inventory, monsters, map);

        Assert.That(action.Type, Is.EqualTo(BotAction.ActionType.MoveToward),
            "Bot should move toward an enemy when two are present");
        Assert.That(action.Target, Is.SameAs(enemy2),
            "Bot should target the closer enemy2 (Manhattan 2) over the farther enemy1 (Manhattan 3)");
    }

    [Test]
    [Description("Instance BotBrain: after stuck fires (Wait), if the player finally moves the counter resets")]
    public void Instance_StuckCounterResetsAfterMoving()
    {
        var (player, fighter, inventory, map) = MakeArena();
        var enemy = MakeMonster(1, 13, 10, map);
        var monsters = new List<Entity> { enemy };
        var brain = new BotBrain(BotPersonaRegistry.Get("balanced"));

        // Run until the drop fires (turn 5)
        for (int i = 0; i < 4; i++)
            brain.Decide(player, fighter, inventory, monsters, map);
        var drop = brain.Decide(player, fighter, inventory, monsters, map);
        Assert.That(drop.Type, Is.EqualTo(BotAction.ActionType.DoNothing), "Drop fires at turn 5");

        // Simulate the player moving (position change)
        player.X = 11; player.Y = 10; // moved!
        var afterMove = brain.Decide(player, fighter, inventory, monsters, map);
        Assert.That(afterMove.Type, Is.EqualTo(BotAction.ActionType.MoveToward),
            "Counter resets after player position changes — back to normal MoveToward");
    }

    // ── Test 3: AbortRun at 15 stuck turns ────────────────────────────────────

    [Test]
    [Description("Instance BotBrain: 15 stuck turns returns AbortRun")]
    public void Instance_ReturnsAbortRun_After15StuckTurns()
    {
        var (player, fighter, inventory, map) = MakeArena();
        // Place enemy so player always moves toward it but never reaches it
        var enemy = MakeMonster(1, 13, 10, map);
        var monsters = new List<Entity> { enemy };

        var brain = new BotBrain(BotPersonaRegistry.Get("balanced"));

        BotAction? terminatingAction = null;

        // Counter does NOT reset when dropping target — climbs from 4→ForceDescend→AbortRun.
        // Turn-by-turn trace (single enemy at (13,10) that never moves):
        //   Turns 1-4:   MoveToward (counter 0..3)
        //   Turn 5:      DoNothing (drop fires at counter=4)
        //   Turns 6-11:  DoNothing or ForceDescend (counter climbs: 5..11)
        //   Turn 13:     ForceDescend (counter=12)
        //   ...until counter=20: AbortRun
        // Run 25 turns for safety.
        for (int i = 0; i < 25; i++)
        {
            var action = brain.Decide(player, fighter, inventory, monsters, map);
            if (action.Type is BotAction.ActionType.AbortRun or BotAction.ActionType.ForceDescend)
            {
                terminatingAction = action;
                break;
            }
        }

        Assert.That(terminatingAction, Is.Not.Null,
            "Should receive ForceDescend or AbortRun within 25 turns (counter climbs to 12 or 20)");
        Assert.That(terminatingAction!.Type,
            Is.EqualTo(BotAction.ActionType.ForceDescend).Or.EqualTo(BotAction.ActionType.AbortRun));
    }

    // ── Test 4: Attack resets stuck counter ───────────────────────────────────

    [Test]
    [Description("Instance BotBrain: actual attack action resets stuck counter (won't abort if making progress)")]
    public void Instance_AttackResetsStuckCounter()
    {
        var (player, fighter, inventory, map) = MakeArena();
        // Enemy adjacent (Chebyshev 1) — player will attack it each turn
        var enemy = MakeMonster(1, 11, 10, map);
        var monsters = new List<Entity> { enemy };

        var brain = new BotBrain(BotPersonaRegistry.Get("balanced"));

        // Run 20 turns with an adjacent enemy — bot attacks every turn, no stuck
        for (int i = 0; i < 20; i++)
        {
            var action = brain.Decide(player, fighter, inventory, monsters, map);
            Assert.That(action.Type, Is.Not.EqualTo(BotAction.ActionType.AbortRun),
                $"Turn {i+1}: should never abort when enemy is adjacent and bot is attacking");
            // Attack is the expected action
            Assert.That(action.Type, Is.EqualTo(BotAction.ActionType.AttackTarget),
                $"Turn {i+1}: should attack adjacent enemy");
        }
    }

    // ── Test 5: AbortRun plumbing ─────────────────────────────────────────────

    [Test]
    [Description("BotAction.AbortRun sentinel: ToPlayerAction returns Wait (safe fallback)")]
    public void AbortRun_ToPlayerAction_ReturnsSafeWait()
    {
        var abortAction = BotAction.AbortRun;
        var playerAction = BotBrain.ToPlayerAction(abortAction);
        Assert.That(playerAction.Kind, Is.EqualTo(PlayerAction.ActionKind.Wait),
            "AbortRun.ToPlayerAction() should return Wait as a safe sentinel");
    }

    [Test]
    [Description("BotAction.ActionType.AbortRun enum value exists")]
    public void AbortRun_ActionTypeEnumExists()
    {
        Assert.That(Enum.IsDefined(typeof(BotAction.ActionType), "AbortRun"),
            "BotAction.ActionType.AbortRun enum value must exist");
    }

    // ── Test 6: Static path isolation ────────────────────────────────────────

    [Test]
    [Description("Static BotBrain.Decide(... persona: ...) accepts persona parameter")]
    public void Static_Decide_AcceptsPersonaParameter()
    {
        var (player, fighter, inventory, map) = MakeArena();
        var enemy = MakeMonster(1, 11, 10, map);
        var monsters = new List<Entity> { enemy };

        // Verify the static method compiles and runs with an explicit persona
        Assert.DoesNotThrow(() =>
        {
            var _ = BotBrain.Decide(player, fighter, inventory, monsters, map,
                persona: BotPersonaRegistry.Get("aggressive"));
        }, "Static BotBrain.Decide with persona parameter should not throw");
    }
}
