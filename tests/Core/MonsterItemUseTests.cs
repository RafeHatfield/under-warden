using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for monster item usage and the 3-way failure system.
///
/// The AI triggers item usage 10% of the time (checked via seeded RNG), but
/// ResolveMonsterItemUse is also tested directly by forcing the action through
/// a deterministic seed that guarantees the decision fires.
/// </summary>
[TestFixture]
public class MonsterItemUseTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Arena state with player adjacent to monster (so the AI would normally attack).
    /// Monster has an Inventory and Equipment. Player starts at near-full HP.
    /// </summary>
    private static (GameState state, Entity monster, Entity potion) CreateStateWithPotion(
        int seed = 1337,
        int monsterHp = 10,
        int potionHeal = 20)
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
        // CanUseItems = true required — off by default to match PoC can_use_potions = False.
        // Tests explicitly enable it to exercise the failure system.
        monster.Add(new AiComponent { CanUseItems = true });
        monster.Add(new Inventory());
        monster.Add(new Equipment());
        map.RegisterEntity(monster);

        // Give monster a healing potion in inventory
        var potion = new Entity(10, "Healing Potion", 0, 0);
        potion.Add(new Consumable(healAmount: potionHeal));
        monster.Require<Inventory>().Add(potion);

        var state = new GameState(player, new List<Entity> { monster }, map, rng);
        return (state, monster, potion);
    }

    // ─── Resolution tests (driving ResolveMonsterItemUse via ProcessTurn) ────

    /// <summary>
    /// Force a UseItem action by finding a seed where the 10% roll fires.
    /// Run up to 100 seeds until one produces an ItemUseEvent — assert the event exists.
    /// This avoids hardcoding a seed that could shift if RNG usage changes elsewhere.
    /// </summary>
    [Test]
    public void MonsterWithPotion_EventuallyEmitsItemUseEvent()
    {
        bool found = false;
        for (int seed = 1; seed <= 200; seed++)
        {
            var (state, monster, _) = CreateStateWithPotion(seed: seed);
            // Move player away so the monster isn't adjacent (to allow the item check to fire
            // before the attack check — they're at the same position in CreateState, so just
            // check whether ItemUseEvent appears in any turn over 20 turns).
            for (int t = 0; t < 20; t++)
            {
                var result = TurnController.ProcessTurn(state, PlayerAction.Wait);
                if (result.Events.OfType<ItemUseEvent>().Any())
                {
                    found = true;
                    break;
                }
                if (state.IsGameOver) break;
            }
            if (found) break;
        }

        Assert.That(found, Is.True, "Expected at least one ItemUseEvent across 200 seeds * 20 turns");
    }

    /// <summary>
    /// On success, the monster's HP should increase (healed), and the item is consumed.
    /// We test this by directly verifying the state after a forced success path:
    /// use seed + monster HP combination that guarantees a potion effect.
    /// </summary>
    [Test]
    public void ItemUse_SuccessPath_HealsMonstersHP()
    {
        // Find a seed where the monster's item use succeeds (80% chance per attempt)
        // and the monster is at reduced HP so healing is visible.
        bool verifiedHealing = false;

        for (int seed = 1; seed <= 200; seed++)
        {
            var (state, monster, potion) = CreateStateWithPotion(seed: seed, monsterHp: 5, potionHeal: 20);
            int hpBefore = monster.Require<Fighter>().Hp;

            for (int t = 0; t < 20; t++)
            {
                var result = TurnController.ProcessTurn(state, PlayerAction.Wait);
                var useEvent = result.Events.OfType<ItemUseEvent>().FirstOrDefault();
                if (useEvent != null && useEvent.Success)
                {
                    int hpAfter = monster.Require<Fighter>().Hp;
                    Assert.That(hpAfter, Is.GreaterThan(hpBefore),
                        "Monster HP should increase after successful potion use");
                    Assert.That(useEvent.EffectAmount, Is.GreaterThan(0),
                        "EffectAmount should be positive on successful heal");
                    verifiedHealing = true;
                    break;
                }
                if (state.IsGameOver) break;
            }
            if (verifiedHealing) break;
        }

        Assert.That(verifiedHealing, Is.True, "Expected at least one successful heal event");
    }

    /// <summary>
    /// Item is consumed (removed from monster inventory) regardless of success or failure.
    /// After an ItemUseEvent fires, the inventory should have one fewer potion.
    /// </summary>
    [Test]
    public void ItemUse_ConsumesItemRegardlessOfOutcome()
    {
        bool verified = false;

        for (int seed = 1; seed <= 200; seed++)
        {
            var (state, monster, _) = CreateStateWithPotion(seed: seed);
            int countBefore = monster.Require<Inventory>().Count;

            for (int t = 0; t < 20; t++)
            {
                var result = TurnController.ProcessTurn(state, PlayerAction.Wait);
                if (result.Events.OfType<ItemUseEvent>().Any())
                {
                    int countAfter = monster.Require<Inventory>().Count;
                    Assert.That(countAfter, Is.LessThan(countBefore),
                        "Inventory should shrink after item use (consumed regardless of outcome)");
                    verified = true;
                    break;
                }
                if (state.IsGameOver) break;
            }
            if (verified) break;
        }

        Assert.That(verified, Is.True, "Expected at least one ItemUseEvent to verify consumption");
    }

    /// <summary>
    /// On failure with wrong_target mode, the player gains HP — the monster's potion
    /// heals the enemy. EffectAmount should be positive and player HP should increase.
    /// </summary>
    [Test]
    public void ItemUse_WrongTarget_HealsPlayer()
    {
        bool verified = false;

        for (int seed = 1; seed <= 500; seed++)
        {
            var (state, monster, _) = CreateStateWithPotion(seed: seed, potionHeal: 40);

            // Wound the player so healing is visible
            state.PlayerFighter.TakeDamage(20);
            int playerHpBefore = state.PlayerFighter.Hp;

            for (int t = 0; t < 20; t++)
            {
                var result = TurnController.ProcessTurn(state, PlayerAction.Wait);
                var useEvent = result.Events.OfType<ItemUseEvent>()
                    .FirstOrDefault(e => e.FailureMode == "wrong_target");

                if (useEvent != null)
                {
                    int playerHpAfter = state.PlayerFighter.Hp;
                    Assert.That(playerHpAfter, Is.GreaterThan(playerHpBefore),
                        "Player HP should increase on wrong_target failure");
                    Assert.That(useEvent.EffectAmount, Is.GreaterThan(0),
                        "EffectAmount should be positive on wrong_target heal");
                    verified = true;
                    break;
                }
                if (state.IsGameOver) break;
            }
            if (verified) break;
        }

        Assert.That(verified, Is.True, "Expected at least one wrong_target failure event across 500 seeds");
    }

    /// <summary>
    /// On failure with equipment_damage mode, the monster's weapon DamageMax decreases.
    /// EffectAmount should be 1 (one point of damage reduction).
    /// </summary>
    [Test]
    public void ItemUse_EquipmentDamage_ReducesWeaponDamageMax()
    {
        bool verified = false;

        for (int seed = 1; seed <= 500; seed++)
        {
            var (state, monster, _) = CreateStateWithPotion(seed: seed);

            // Equip a weapon so equipment_damage has something to degrade.
            var weapon = new Entity(20, "Iron Sword", 0, 0);
            weapon.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 3, DamageMax = 7 });
            monster.Require<Equipment>().SetSlot(EquipmentSlot.MainHand, weapon);
            int damageMaxBefore = weapon.Require<Equippable>().DamageMax;

            for (int t = 0; t < 20; t++)
            {
                var result = TurnController.ProcessTurn(state, PlayerAction.Wait);
                var useEvent = result.Events.OfType<ItemUseEvent>()
                    .FirstOrDefault(e => e.FailureMode == "equipment_damage");

                if (useEvent != null)
                {
                    int damageMaxAfter = weapon.Require<Equippable>().DamageMax;
                    Assert.That(damageMaxAfter, Is.LessThan(damageMaxBefore),
                        "Weapon DamageMax should decrease on equipment_damage failure");
                    Assert.That(useEvent.EffectAmount, Is.EqualTo(1),
                        "EffectAmount should be 1 for equipment_damage");
                    verified = true;
                    break;
                }
                if (state.IsGameOver) break;
            }
            if (verified) break;
        }

        Assert.That(verified, Is.True, "Expected at least one equipment_damage failure event across 500 seeds");
    }

    /// <summary>
    /// A monster with no usable items never emits an ItemUseEvent.
    /// </summary>
    [Test]
    public void MonsterWithoutUsableItems_NeverEmitsItemUseEvent()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 6, blocksMovement: true);
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var monster = new Entity(1, "Orc", 4, 6, blocksMovement: true);
        monster.Add(new Fighter(hp: 100, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 2, damageMax: 4));
        monster.Add(new Inventory()); // empty inventory
        map.RegisterEntity(monster);

        var state = new GameState(player, new List<Entity> { monster }, map, rng, turnLimit: 50);

        bool sawItemUse = false;
        for (int t = 0; t < 50 && !state.IsGameOver; t++)
        {
            var result = TurnController.ProcessTurn(state, PlayerAction.Wait);
            if (result.Events.OfType<ItemUseEvent>().Any())
            {
                sawItemUse = true;
                break;
            }
        }

        Assert.That(sawItemUse, Is.False, "Monster with empty inventory should never emit ItemUseEvent");
    }

    /// <summary>
    /// ItemUseEvent carries the correct item name.
    /// </summary>
    [Test]
    public void ItemUseEvent_CarriesCorrectItemName()
    {
        bool verified = false;
        for (int seed = 1; seed <= 200; seed++)
        {
            var (state, _, _) = CreateStateWithPotion(seed: seed);

            for (int t = 0; t < 20; t++)
            {
                var result = TurnController.ProcessTurn(state, PlayerAction.Wait);
                var useEvent = result.Events.OfType<ItemUseEvent>().FirstOrDefault();
                if (useEvent != null)
                {
                    Assert.That(useEvent.ItemName, Is.EqualTo("Healing Potion"),
                        "ItemUseEvent should carry the item's name");
                    verified = true;
                    break;
                }
                if (state.IsGameOver) break;
            }
            if (verified) break;
        }

        Assert.That(verified, Is.True, "Expected at least one ItemUseEvent to verify item name");
    }
}
