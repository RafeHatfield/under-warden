using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Tests for persona-specific BotBrain decisions (TASK-008).
/// Each test shows a distinguishing decision for one persona that differs from the others.
///
/// From the plan:
///   - Aggressive_DoesNotDeviateForFloorPotion
///   - Greedy_DeviatesForFloorPotionUpToRadius6
///   - Cautious_HealsAt50Percent
///   - Speedrunner_DoesNotEngageAtDistance5
///   - Balanced_MatchesLegacyBehavior
///
/// All tests run against the static BotBrain.Decide path (no stuck detection).
/// Stuck detection tests are in BotBrainStuckTests.cs.
/// </summary>
[TestFixture]
[Category("Bot")]
[Description("Per-persona BotBrain decision tests — each persona makes a distinguishing decision")]
public class BotBrainPersonaTests
{
    // ── State helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Build a 20×20 open arena with the player at center (10, 10).
    /// </summary>
    private static (Entity Player, Fighter PlayerFighter, Inventory PlayerInventory, GameMap Map)
        MakeArena(int playerHp = 54)
    {
        const int Width = 20, Height = 20;
        var map = new GameMap(Width, Height, allWalls: true);
        for (int x = 1; x < Width - 1; x++)
            for (int y = 1; y < Height - 1; y++)
                map.SetTile(x, y, TileKind.Floor);

        var player = new Entity(0, "Player", 10, 10, blocksMovement: true);
        // CON 10 → MaxHp == 54 exactly for clean HP-fraction math
        var fighter = new Fighter(
            hp: playerHp, strength: 12, dexterity: 14, constitution: 10,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4);
        player.Add(fighter);

        var inventory = new Inventory();
        player.Add(inventory);
        map.RegisterEntity(player);

        return (player, fighter, inventory, map);
    }

    private static Entity MakeMonster(int id, int x, int y, GameMap map, int hp = 28)
    {
        var orc = new Entity(id, "Orc", x, y, blocksMovement: true);
        orc.Add(new Fighter(hp: hp, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        map.RegisterEntity(orc);
        return orc;
    }

    private static Entity MakeFloorPotion(int id, int x, int y, GameMap map)
    {
        var potion = new Entity(id, "Healing Potion", x, y);
        potion.Add(new Consumable(healAmount: 40, isPotion: true));
        map.RegisterEntity(potion);
        return potion;
    }

    private static void AddInventoryPotion(Inventory inventory)
    {
        var potion = new Entity(99, "Healing Potion");
        potion.Add(new Consumable(healAmount: 40, isPotion: true));
        inventory.Add(potion);
    }

    // ── Test 1: Aggressive ignores floor potion ────────────────────────────────

    [Test]
    [Description("Aggressive_DoesNotDeviateForFloorPotion: 1 enemy at distance 3, potion at distance 2 → move-toward-enemy")]
    public void Aggressive_DoesNotDeviateForFloorPotion()
    {
        var (player, fighter, inventory, map) = MakeArena(playerHp: 54);
        // Enemy at distance 3 (Manhattan = |13-10|+|10-10| = 3)
        var enemy = MakeMonster(1, 13, 10, map);
        // Potion at distance 2 (Manhattan = |12-10|+|10-10| = 2)
        var floorPotion = MakeFloorPotion(2, 12, 10, map);

        var monsters = new List<Entity> { enemy };
        var floorItems = new List<Entity> { floorPotion };
        var persona = BotPersonaRegistry.Get("aggressive");

        var action = BotBrain.Decide(player, fighter, inventory, monsters, map,
            floorItems: floorItems, persona: persona);

        // Aggressive has LootPriority=0 — never deviates for floor items
        Assert.That(action.Type, Is.EqualTo(BotAction.ActionType.MoveToward),
            "Aggressive should move toward enemy, not deviate for a floor potion");
        Assert.That(action.Target, Is.SameAs(enemy),
            "Target should be the enemy, not the potion");
    }

    // ── Test 2: Greedy deviates for potion up to radius 6 ────────────────────

    [Test]
    [Description("Greedy_DeviatesForFloorPotionUpToRadius6: potion at distance 5, no adjacent enemies → move toward potion")]
    public void Greedy_DeviatesForFloorPotionUpToRadius6()
    {
        var (player, fighter, inventory, map) = MakeArena(playerHp: 54);
        // Enemy far away (distance 14) — well beyond adjacency
        var enemy = MakeMonster(1, 10, 15, map);
        // Potion at distance 5 — within greedy's radius 6
        var floorPotion = MakeFloorPotion(2, 10, 5, map); // Manhattan 5 from (10,10)

        var monsters = new List<Entity> { enemy };
        var floorItems = new List<Entity> { floorPotion };
        var persona = BotPersonaRegistry.Get("greedy");

        var action = BotBrain.Decide(player, fighter, inventory, monsters, map,
            floorItems: floorItems, persona: persona);

        // Greedy has LootPriority=2 → search radius 6, so potion at distance 5 is in range
        Assert.That(action.Type, Is.EqualTo(BotAction.ActionType.MoveTo),
            "Greedy should deviate to pick up potion within radius 6");
        Assert.That(action.TargetX, Is.EqualTo(floorPotion.X),
            "Target X should be the potion's position");
        Assert.That(action.TargetY, Is.EqualTo(floorPotion.Y),
            "Target Y should be the potion's position");
    }

    [Test]
    [Description("Aggressive_DoesNotDeviateForFloorPotion_Radius6: potion at distance 5 still ignored by aggressive")]
    public void Aggressive_DoesNotDeviateForFloorPotion_EvenAtRadius6()
    {
        var (player, fighter, inventory, map) = MakeArena(playerHp: 54);
        var enemy = MakeMonster(1, 10, 15, map);
        var floorPotion = MakeFloorPotion(2, 10, 5, map); // Manhattan 5

        var monsters = new List<Entity> { enemy };
        var floorItems = new List<Entity> { floorPotion };
        var persona = BotPersonaRegistry.Get("aggressive");

        var action = BotBrain.Decide(player, fighter, inventory, monsters, map,
            floorItems: floorItems, persona: persona);

        // Aggressive LootPriority=0 — never picks up potions
        Assert.That(action.Type, Is.Not.EqualTo(BotAction.ActionType.MoveTo),
            "Aggressive should never deviate for a floor potion regardless of distance");
    }

    // ── Test 3: Cautious heals at 50% ─────────────────────────────────────────

    [Test]
    [Description("Cautious_HealsAt50Percent: HP at 0.49, 1 potion, no adjacent enemies → expect Heal")]
    public void Cautious_HealsAt50Percent()
    {
        // MaxHp = 54 (CON 10). 49% ≈ 26 HP.
        var (player, fighter, inventory, map) = MakeArena(playerHp: 54);
        fighter.Hp = 26; // 26/54 ≈ 48% — below cautious threshold of 50%
        AddInventoryPotion(inventory);

        // One distant enemy (not adjacent)
        var enemy = MakeMonster(1, 10, 16, map); // Manhattan 6
        var monsters = new List<Entity> { enemy };
        var persona = BotPersonaRegistry.Get("cautious");

        var action = BotBrain.Decide(player, fighter, inventory, monsters, map, persona: persona);

        Assert.That(action.Type, Is.EqualTo(BotAction.ActionType.HealSelf),
            "Cautious should heal at 48% HP (below 50% base_heal_threshold)");
    }

    [Test]
    [Description("Balanced_DoesNotHealAt50Percent: HP at 0.49, 1 potion, no adjacent enemies — balanced threshold is 30%")]
    public void Balanced_DoesNotHealAt50Percent()
    {
        var (player, fighter, inventory, map) = MakeArena(playerHp: 54);
        fighter.Hp = 26; // ≈ 48%
        AddInventoryPotion(inventory);

        var enemy = MakeMonster(1, 10, 16, map);
        var monsters = new List<Entity> { enemy };
        var persona = BotPersonaRegistry.Get("balanced");

        var action = BotBrain.Decide(player, fighter, inventory, monsters, map, persona: persona);

        // Balanced has BaseHealThreshold=0.30 — 48% is above that, so no heal
        Assert.That(action.Type, Is.Not.EqualTo(BotAction.ActionType.HealSelf),
            "Balanced should NOT heal at 48% HP (threshold is 30%)");
    }

    // ── Test 4: Speedrunner doesn't engage at distance 5 ──────────────────────

    [Test]
    [Description("Speedrunner_DoesNotEngageAtDistance5: enemy at Manhattan distance 5 (above engagement_distance 4) → Wait")]
    public void Speedrunner_DoesNotEngageAtDistance5()
    {
        var (player, fighter, inventory, map) = MakeArena(playerHp: 54);
        // Enemy at Manhattan distance 5 (5 tiles east)
        var enemy = MakeMonster(1, 15, 10, map); // |15-10|+|10-10| = 5
        var monsters = new List<Entity> { enemy };
        var persona = BotPersonaRegistry.Get("speedrunner");

        var action = BotBrain.Decide(player, fighter, inventory, monsters, map, persona: persona);

        // Speedrunner has CombatEngagementDistance=4 and AvoidCombat=true
        // At distance 5 (> 4), it should NOT engage — should Wait
        Assert.That(action.Type, Is.Not.EqualTo(BotAction.ActionType.MoveToward),
            "Speedrunner should not move toward enemy at Manhattan distance 5 (above engagement distance 4)");
        Assert.That(action.Type, Is.Not.EqualTo(BotAction.ActionType.AttackTarget),
            "Speedrunner should not attack enemy at distance 5");
    }

    [Test]
    [Description("Speedrunner_EngagesAtDistance4: enemy exactly at engagement_distance 4 — avoid_combat steps away")]
    public void Speedrunner_AvoidCombatDetoursAtDistance4()
    {
        var (player, fighter, inventory, map) = MakeArena(playerHp: 54);
        // Enemy at Manhattan distance 4 (within engagement distance)
        var enemy = MakeMonster(1, 14, 10, map); // |14-10|+|10-10| = 4
        var monsters = new List<Entity> { enemy };
        var persona = BotPersonaRegistry.Get("speedrunner");

        var action = BotBrain.Decide(player, fighter, inventory, monsters, map, persona: persona);

        // Speedrunner has AvoidCombat=true — enemy within engagement distance but not adjacent
        // → should take one step AWAY from enemy (avoid-combat detour), not toward it
        Assert.That(action.Type, Is.Not.EqualTo(BotAction.ActionType.AttackTarget),
            "Speedrunner should not attack enemy at distance 4 when avoid_combat=true");
        // Bot should either move away or wait (if no walkable retreat tile)
        Assert.That(action.Type, Is.EqualTo(BotAction.ActionType.MoveTo).Or.EqualTo(BotAction.ActionType.DoNothing),
            "Speedrunner should move away from enemy or wait (avoid-combat detour)");
    }

    // ── Test 5: Balanced matches legacy behavior ───────────────────────────────

    [Test]
    [Description("Balanced_MatchesLegacyBehavior: at full HP with adjacent enemy, attacks it")]
    public void Balanced_AttacksAdjacentEnemy_AtFullHp()
    {
        var (player, fighter, inventory, map) = MakeArena(playerHp: 54);
        // Adjacent enemy (Chebyshev distance 1)
        var enemy = MakeMonster(1, 11, 10, map);
        var monsters = new List<Entity> { enemy };
        var persona = BotPersonaRegistry.Get("balanced");

        var action = BotBrain.Decide(player, fighter, inventory, monsters, map, persona: persona);

        Assert.That(action.Type, Is.EqualTo(BotAction.ActionType.AttackTarget),
            "Balanced should attack adjacent enemy at full HP");
        Assert.That(action.Target, Is.SameAs(enemy));
    }

    [Test]
    [Description("Balanced_PanicHealRequires2AdjacentEnemies: HP at 14% with 1 adjacent enemy — no panic heal (PoC-correct)")]
    public void Balanced_PanicHealRequires2AdjacentEnemies()
    {
        var (player, fighter, inventory, map) = MakeArena(playerHp: 54);
        fighter.Hp = 7; // 7/54 ≈ 13% — below panic threshold 15%
        AddInventoryPotion(inventory);

        // Only 1 adjacent enemy — panic requires 2+ for balanced
        var enemy = MakeMonster(1, 11, 10, map);
        var monsters = new List<Entity> { enemy };
        var persona = BotPersonaRegistry.Get("balanced");

        var action = BotBrain.Decide(player, fighter, inventory, monsters, map, persona: persona);

        // Panic heal needs 2+ adjacent enemies. With only 1, it should fall through.
        // At 13% HP (below 30% base heal threshold) with allow_combat_healing=true, should threshold-heal instead.
        Assert.That(action.Type, Is.EqualTo(BotAction.ActionType.HealSelf),
            "Balanced should heal at 13% HP (threshold heal fires since allow_combat_healing=true) — not panic (requires 2 enemies)");
    }

    [Test]
    [Description("Balanced_PanicHealFiresWith2AdjacentEnemies: HP at 13% with 2 adjacent enemies → panic heal")]
    public void Balanced_PanicHealFiresWith2AdjacentEnemies()
    {
        var (player, fighter, inventory, map) = MakeArena(playerHp: 54);
        fighter.Hp = 7; // 7/54 ≈ 13% — below panic threshold 15%
        AddInventoryPotion(inventory);

        // 2 adjacent enemies — panic fires
        var enemy1 = MakeMonster(1, 11, 10, map);
        var enemy2 = MakeMonster(2, 10, 11, map);
        var monsters = new List<Entity> { enemy1, enemy2 };
        var persona = BotPersonaRegistry.Get("balanced");

        var action = BotBrain.Decide(player, fighter, inventory, monsters, map, persona: persona);

        Assert.That(action.Type, Is.EqualTo(BotAction.ActionType.HealSelf),
            "Balanced panic heal should fire with 2+ adjacent enemies at 13% HP");
    }

    [Test]
    [Description("Aggressive_NoHealAt15PercentSingleEnemy: aggressive panic requires 3 enemies")]
    public void Aggressive_PanicRequires3Enemies()
    {
        var (player, fighter, inventory, map) = MakeArena(playerHp: 54);
        fighter.Hp = 5; // ≈ 9% — below aggressive panic threshold 10%
        AddInventoryPotion(inventory);

        // Only 2 adjacent enemies — aggressive requires 3+ for panic
        var enemy1 = MakeMonster(1, 11, 10, map);
        var enemy2 = MakeMonster(2, 10, 11, map);
        var monsters = new List<Entity> { enemy1, enemy2 };
        var persona = BotPersonaRegistry.Get("aggressive");

        var action = BotBrain.Decide(player, fighter, inventory, monsters, map, persona: persona);

        // With 2 adjacent enemies (< 3 required for panic), aggressive should NOT panic heal.
        // At 9% HP with allow_combat_healing=true and BaseHealThreshold=0.20 (above 9%), threshold heal fires.
        Assert.That(action.Type, Is.EqualTo(BotAction.ActionType.HealSelf),
            "Aggressive should threshold-heal at 9% HP (below 20% base threshold) with allow_combat_healing");
    }
}
