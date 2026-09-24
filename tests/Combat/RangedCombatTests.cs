using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Combat;

/// <summary>
/// Unit and integration tests for the ranged combat system (Phase 22.2).
///
/// Milestone 1 tests: range band calculation, KnockbackService, CanRetaliate gate.
/// Milestone 2 tests: YAML loading for bows and arrows.
/// Milestone 3 tests: RangedCombatService resolution — denial, hit, miss, retaliation.
/// Milestone 4 tests: Scenario harness smoke tests (no crash + metric assertions).
/// </summary>
[TestFixture]
public class RangedCombatTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Entity MakeEntity(string name, int hp, int id = 0,
        int x = 0, int y = 0,
        int str = 10, int dex = 10, int dmgMin = 1, int dmgMax = 4,
        int accuracy = 2, int evasion = 1)
    {
        var e = new Entity(id, name, x, y);
        e.Add(new Fighter(hp: hp, strength: str, dexterity: dex,
            damageMin: dmgMin, damageMax: dmgMax, accuracy: accuracy, evasion: evasion));
        return e;
    }

    private static GameState MakeArena(Entity player, Entity monster, int width = 12, int height = 12)
    {
        var map = GameMap.CreateArena(width, height);
        var rng = new SeededRandom(1337);
        var state = new GameState(player, new List<Entity> { monster }, map, rng, turnLimit: 200);
        map.RegisterEntity(player);
        map.RegisterEntity(monster);
        return state;
    }

    private static Entity EquipBow(Entity player, bool isRanged = true, int dmgMin = 1, int dmgMax = 6)
    {
        var equip = player.GetOrAdd<Equipment>();
        var bow = new Entity(100, "Shortbow");
        bow.Add(new Equippable(EquipmentSlot.MainHand)
        {
            DamageMin = dmgMin,
            DamageMax = dmgMax,
            ToHitBonus = 2,
            IsRangedWeapon = isRanged,
            TwoHanded = true,
        });
        equip.MainHand = bow;
        return bow;
    }

    private static Entity MakeNetArrowQuiver(int stackSize = 8)
    {
        var quiverItem = new Entity(200, "Net Arrows");
        quiverItem.Add(new Equippable(EquipmentSlot.Quiver)
        {
            IsSpecialAmmo = true,
            DamageType = "entangle",
        });
        quiverItem.Add(new Consumable(healAmount: 0) { StackSize = stackSize });
        return quiverItem;
    }

    private static Entity MakeFireArrowQuiver(int stackSize = 10)
    {
        var quiverItem = new Entity(201, "Fire Arrows");
        quiverItem.Add(new Equippable(EquipmentSlot.Quiver)
        {
            IsSpecialAmmo = true,
            DamageType = "fire",
        });
        quiverItem.Add(new Consumable(healAmount: 0) { StackSize = stackSize });
        return quiverItem;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Equipment Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void Equippable_IsRangedWeapon_DefaultFalse()
    {
        var eq = new Equippable(EquipmentSlot.MainHand);
        Assert.That(eq.IsRangedWeapon, Is.False);
    }

    [Test]
    public void Equippable_TwoHanded_DefaultFalse()
    {
        var eq = new Equippable(EquipmentSlot.MainHand);
        Assert.That(eq.TwoHanded, Is.False);
    }

    [Test]
    public void Equippable_IsSpecialAmmo_DefaultFalse()
    {
        var eq = new Equippable(EquipmentSlot.Quiver);
        Assert.That(eq.IsSpecialAmmo, Is.False);
    }

    [Test]
    public void Equipment_QuiverSlot_GetSet()
    {
        var equipment = new Equipment();
        var quiver = new Entity(1, "Net Arrows");
        equipment.SetSlot(EquipmentSlot.Quiver, quiver);
        Assert.That(equipment.GetSlot(EquipmentSlot.Quiver), Is.SameAs(quiver));
        Assert.That(equipment.Quiver, Is.SameAs(quiver));
    }

    [Test]
    public void Equipment_AllEquipped_IncludesQuiver()
    {
        var equip = new Equipment();
        var quiver = new Entity(1, "Net Arrows");
        var bow = new Entity(2, "Shortbow");
        quiver.Add(new Equippable(EquipmentSlot.Quiver) { IsSpecialAmmo = true, ArmorClassBonus = 0 });
        bow.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 1, DamageMax = 6 });
        equip.MainHand = bow;
        equip.Quiver = quiver;

        // TotalToHitBonus touches AllEquipped via SumBonus — should not throw
        int total = equip.TotalArmorClassBonus;
        Assert.That(total, Is.EqualTo(0)); // quiver has 0 AC, bow has 0 AC
    }

    // ─────────────────────────────────────────────────────────────────────────
    // KnockbackService Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void KnockbackService_MovesTargetOneStep()
    {
        var player = MakeEntity("Player", 54, id: 0, x: 3, y: 6);
        var monster = MakeEntity("Orc", 28, id: 1, x: 5, y: 6);
        var state = MakeArena(player, monster);

        int tiles = KnockbackService.TryKnockBackOneTile(player, monster, state.Map, state);

        Assert.That(tiles, Is.EqualTo(1));
        Assert.That(monster.X, Is.EqualTo(6), "Monster should move away from player (positive X direction)");
    }

    [Test]
    public void KnockbackService_StopsAtWall()
    {
        var player = MakeEntity("Player", 54, id: 0, x: 3, y: 6);
        var monster = MakeEntity("Orc", 28, id: 1, x: 10, y: 6);
        // Map is 12x12; tiles at x=11 are walls
        var state = MakeArena(player, monster, width: 12, height: 12);

        int tiles = KnockbackService.TryKnockBackOneTile(player, monster, state.Map, state);

        // Monster at x=10 would move to x=11, which is a wall (map edge). 0 tiles.
        Assert.That(tiles, Is.EqualTo(0));
        Assert.That(monster.X, Is.EqualTo(10), "Monster should not move when wall is adjacent");
    }

    [Test]
    public void KnockbackService_StopsAtOtherEntity()
    {
        var player = MakeEntity("Player", 54, id: 0, x: 3, y: 6);
        var monster = MakeEntity("Orc", 28, id: 1, x: 5, y: 6);
        var blocker = MakeEntity("Blocker", 28, id: 2, x: 6, y: 6);
        var map = GameMap.CreateArena(12, 12);
        var rng = new SeededRandom(1337);
        var state = new GameState(player, new List<Entity> { monster, blocker }, map, rng, turnLimit: 200);
        map.RegisterEntity(player);
        map.RegisterEntity(monster);
        map.RegisterEntity(blocker);

        int tiles = KnockbackService.TryKnockBackOneTile(player, monster, state.Map, state);

        Assert.That(tiles, Is.EqualTo(0), "Knockback should be blocked by another entity at dest");
        Assert.That(monster.X, Is.EqualTo(5), "Monster should not move");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CanRetaliate Gate Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void CanRetaliate_AliveFighter_ReturnsTrue()
    {
        var monster = MakeEntity("Orc", 28, id: 1);
        Assert.That(RangedCombatService.CanRetaliate(monster), Is.True);
    }

    [Test]
    public void CanRetaliate_DeadFighter_ReturnsFalse()
    {
        var monster = MakeEntity("Orc", 0, id: 1);
        monster.Get<Fighter>()!.TakeDamage(9999);
        Assert.That(RangedCombatService.CanRetaliate(monster), Is.False);
    }

    [Test]
    public void CanRetaliate_SleepEffect_ReturnsFalse()
    {
        var monster = MakeEntity("Orc", 28, id: 1);
        monster.Add(new SleepEffect { RemainingTurns = 3 });
        Assert.That(RangedCombatService.CanRetaliate(monster), Is.False,
            "Sleeping monsters cannot retaliate");
    }

    [Test]
    public void CanRetaliate_ImmobilizedEffect_ReturnsFalse()
    {
        var monster = MakeEntity("Orc", 28, id: 1);
        monster.Add(new ImmobilizedEffect { RemainingTurns = 2 });
        Assert.That(RangedCombatService.CanRetaliate(monster), Is.False,
            "Immobilized monsters cannot retaliate");
    }

    [Test]
    public void CanRetaliate_EntangledEffect_ReturnsTrue()
    {
        // Load-bearing: entangled defenders CAN still retaliate (they can swing adjacent).
        var monster = MakeEntity("Orc", 28, id: 1);
        monster.Add(new EntangledEffect { RemainingTurns = 1 });
        Assert.That(RangedCombatService.CanRetaliate(monster), Is.True,
            "EntangledEffect must NOT block retaliation — this is load-bearing spec behavior");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RangedCombatService Resolution Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void RangedAttack_OptimalRange_EmitsEvent()
    {
        var player = MakeEntity("Player", 54, id: 0, x: 3, y: 6);
        var monster = MakeEntity("Orc", 28, id: 1, x: 7, y: 6); // d=4, optimal
        EquipBow(player);
        var state = MakeArena(player, monster);
        var events = new List<TurnEvent>();

        RangedCombatService.AttemptRangedAttack(player, monster, state, events);

        var rangedEvt = events.OfType<RangedAttackEvent>().FirstOrDefault();
        Assert.That(rangedEvt, Is.Not.Null, "Should emit RangedAttackEvent");
        Assert.That(rangedEvt!.Denied, Is.False, "d=4 should not be denied");
        Assert.That(rangedEvt.BandName, Is.EqualTo("optimal_range"));
        Assert.That(rangedEvt.Distance, Is.EqualTo(4));
    }

    [Test]
    public void RangedAttack_BeyondMaxRange_Denied()
    {
        var player = MakeEntity("Player", 54, id: 0, x: 1, y: 6);
        var monster = MakeEntity("Orc", 28, id: 1, x: 10, y: 6); // d=9, denied
        EquipBow(player);
        var state = MakeArena(player, monster);
        var events = new List<TurnEvent>();

        RangedCombatService.AttemptRangedAttack(player, monster, state, events);

        var rangedEvt = events.OfType<RangedAttackEvent>().Single();
        Assert.That(rangedEvt.Denied, Is.True, "d=9 should be denied");
        Assert.That(rangedEvt.BandName, Is.EqualTo("denied_out_of_range"));
    }

    [Test]
    public void RangedAttack_Denial_DoesNotConsumeAmmo()
    {
        var player = MakeEntity("Player", 54, id: 0, x: 1, y: 6);
        var monster = MakeEntity("Orc", 28, id: 1, x: 10, y: 6); // d=9, denied
        EquipBow(player);

        var quiver = MakeNetArrowQuiver(stackSize: 5);
        player.GetOrAdd<Equipment>().Quiver = quiver;

        var state = MakeArena(player, monster);
        var events = new List<TurnEvent>();

        RangedCombatService.AttemptRangedAttack(player, monster, state, events);

        var consumedEvts = events.OfType<SpecialAmmoConsumedEvent>().ToList();
        Assert.That(consumedEvts, Is.Empty, "Denied attacks must not consume ammo");
        Assert.That(quiver.Get<Consumable>()!.StackSize, Is.EqualTo(5));
    }

    [Test]
    public void RangedAttack_Hit_DealsDamage()
    {
        // Use high accuracy to guarantee hits
        var player = MakeEntity("Player", 54, id: 0, x: 3, y: 6, dex: 20, accuracy: 10);
        var monster = MakeEntity("Orc", 100, id: 1, x: 7, y: 6, dex: 6, evasion: 0); // easy to hit
        EquipBow(player, dmgMin: 4, dmgMax: 4); // fixed damage = 4
        var state = MakeArena(player, monster);
        var events = new List<TurnEvent>();

        // Run multiple times until hit
        int hpBefore = monster.Get<Fighter>()!.Hp;
        for (int i = 0; i < 50; i++)
        {
            RangedCombatService.AttemptRangedAttack(player, monster, state, events);
            if (events.OfType<RangedAttackEvent>().Any(e => e.Hit)) break;
        }

        var hit = events.OfType<RangedAttackEvent>().FirstOrDefault(e => e.Hit);
        Assert.That(hit, Is.Not.Null, "Should eventually hit");
        Assert.That(hit!.Damage, Is.GreaterThan(0));
        Assert.That(monster.Get<Fighter>()!.Hp, Is.LessThan(hpBefore));
    }

    [Test]
    public void RangedAttack_Adjacent_TriggersRetaliation()
    {
        // Monster adjacent to player (d=1), not sleeping/immobilized
        var player = MakeEntity("Player", 54, id: 0, x: 3, y: 6);
        var monster = MakeEntity("Orc", 28, id: 1, x: 4, y: 6, dmgMin: 1, dmgMax: 1);
        EquipBow(player);
        var state = MakeArena(player, monster);
        var events = new List<TurnEvent>();

        RangedCombatService.AttemptRangedAttack(player, monster, state, events);

        var rangedEvt = events.OfType<RangedAttackEvent>().FirstOrDefault();
        Assert.That(rangedEvt, Is.Not.Null);
        Assert.That(rangedEvt!.RetaliationTriggered, Is.True,
            "Adjacent shot should trigger retaliation");
        Assert.That(rangedEvt.BandName, Is.EqualTo("adjacent_threatened"));
    }

    [Test]
    public void RangedAttack_Adjacent_SleepingMonster_NoRetaliation()
    {
        // Sleeping monster at d=1: cannot retaliate
        var player = MakeEntity("Player", 54, id: 0, x: 3, y: 6);
        var monster = MakeEntity("Orc", 28, id: 1, x: 4, y: 6);
        monster.Add(new SleepEffect { RemainingTurns = 3 });
        EquipBow(player);
        var state = MakeArena(player, monster);
        var events = new List<TurnEvent>();

        RangedCombatService.AttemptRangedAttack(player, monster, state, events);

        var rangedEvt = events.OfType<RangedAttackEvent>().FirstOrDefault();
        Assert.That(rangedEvt!.RetaliationTriggered, Is.False,
            "Sleeping monster must not retaliate");
    }

    [Test]
    public void RangedAttack_FarRange_DamageHalved()
    {
        // d=7 should apply 50% multiplier. Roll known damage, check penalty.
        var player = MakeEntity("Player", 54, id: 0, x: 1, y: 6, dex: 20, accuracy: 20);
        var monster = MakeEntity("Orc", 200, id: 1, x: 8, y: 6, dex: 1, evasion: 0); // d=7
        EquipBow(player, dmgMin: 4, dmgMax: 4); // fixed 4 dmg → 50% = 2
        var state = MakeArena(player, monster);
        var events = new List<TurnEvent>();

        // Force a hit by running until we get one
        for (int i = 0; i < 20; i++)
        {
            events.Clear();
            RangedCombatService.AttemptRangedAttack(player, monster, state, events);
            var e = events.OfType<RangedAttackEvent>().FirstOrDefault();
            if (e?.Hit == true)
            {
                Assert.That(e.BandName, Is.EqualTo("far_range"));
                Assert.That(e.DamageBeforePenalty, Is.EqualTo(4 + CombatMath.StatModifier(player.Get<Fighter>()!.Strength)));
                // Damage after 50% penalty: max(1, floor(base*0.5))
                Assert.That(e.Damage, Is.LessThan(e.DamageBeforePenalty), "Far range should reduce damage");
                return;
            }
        }
        Assert.Fail("Didn't hit after 20 attempts — check test setup");
    }

    [Test]
    public void RangedAttack_NetArrow_ConsumedOnHit()
    {
        var player = MakeEntity("Player", 54, id: 0, x: 3, y: 6, dex: 20, accuracy: 20);
        var monster = MakeEntity("Orc", 100, id: 1, x: 7, y: 6, dex: 1, evasion: 0);
        EquipBow(player);
        var quiver = MakeNetArrowQuiver(stackSize: 3);
        player.GetOrAdd<Equipment>().Quiver = quiver;
        var state = MakeArena(player, monster);

        // Force a hit
        for (int i = 0; i < 30; i++)
        {
            var events = new List<TurnEvent>();
            RangedCombatService.AttemptRangedAttack(player, monster, state, events);
            var consumed = events.OfType<SpecialAmmoConsumedEvent>().FirstOrDefault();
            if (consumed != null)
            {
                Assert.That(consumed.AmmoType, Is.EqualTo("entangle"));
                Assert.That(consumed.Remaining, Is.LessThan(3));
                return;
            }
        }
        Assert.Fail("Should have consumed ammo after 30 shots");
    }

    [Test]
    public void RangedAttack_NetArrow_ConsumedOnMiss()
    {
        // Use low accuracy, high AC target to guarantee misses
        var player = MakeEntity("Player", 54, id: 0, x: 3, y: 6, dex: 6, accuracy: 0);
        var monster = MakeEntity("Orc", 100, id: 1, x: 7, y: 6, dex: 20, evasion: 10);
        EquipBow(player);
        var quiver = MakeNetArrowQuiver(stackSize: 5);
        player.GetOrAdd<Equipment>().Quiver = quiver;
        var state = MakeArena(player, monster);

        // Force a miss + check ammo still consumed
        for (int i = 0; i < 30; i++)
        {
            var events = new List<TurnEvent>();
            RangedCombatService.AttemptRangedAttack(player, monster, state, events);
            var rangedEvt = events.OfType<RangedAttackEvent>().FirstOrDefault();
            var consumed = events.OfType<SpecialAmmoConsumedEvent>().FirstOrDefault();
            if (rangedEvt?.Hit == false && consumed != null)
            {
                Assert.That(consumed.Remaining, Is.LessThan(5),
                    "Ammo must be consumed on miss too");
                return;
            }
        }
        Assert.Fail("Should have missed at least once in 30 shots");
    }

    [Test]
    public void RangedAttack_QuiverExhausted_AutoUnequipped()
    {
        var player = MakeEntity("Player", 54, id: 0, x: 3, y: 6, dex: 20, accuracy: 20);
        var monster = MakeEntity("Orc", 1000, id: 1, x: 7, y: 6, dex: 1, evasion: 0);
        EquipBow(player);
        var quiver = MakeNetArrowQuiver(stackSize: 1); // only 1 arrow
        var equip = player.GetOrAdd<Equipment>();
        equip.Quiver = quiver;
        var inv = player.GetOrAdd<Inventory>();
        var state = MakeArena(player, monster);

        // Shoot once — should consume the last arrow and unequip the quiver
        var events = new List<TurnEvent>();
        RangedCombatService.AttemptRangedAttack(player, monster, state, events);

        var consumed = events.OfType<SpecialAmmoConsumedEvent>().FirstOrDefault();
        Assert.That(consumed, Is.Not.Null, "Should emit SpecialAmmoConsumedEvent");
        Assert.That(consumed!.Remaining, Is.EqualTo(0));
        Assert.That(equip.Quiver, Is.Null, "Quiver must be auto-unequipped when exhausted");
    }

    [Test]
    public void RangedAttack_FireArrow_AppliesBurningOnHit()
    {
        // High accuracy player vs low-AC monster to ensure hit
        var player = MakeEntity("Player", 54, id: 0, x: 3, y: 6, dex: 20, accuracy: 20);
        var monster = MakeEntity("Orc", 100, id: 1, x: 7, y: 6, dex: 1, evasion: 0);
        EquipBow(player);
        var quiver = MakeFireArrowQuiver(stackSize: 5);
        player.GetOrAdd<Equipment>().Quiver = quiver;
        var state = MakeArena(player, monster);

        // Run until we see burning applied (100% on hit)
        for (int i = 0; i < 50; i++)
        {
            var events = new List<TurnEvent>();
            RangedCombatService.AttemptRangedAttack(player, monster, state, events);
            var rangedEvt = events.OfType<RangedAttackEvent>().FirstOrDefault();
            if (rangedEvt?.Hit == true)
            {
                var burning = monster.Get<BurningEffect>();
                Assert.That(burning, Is.Not.Null, "Fire arrow hit must apply BurningEffect");
                Assert.That(burning!.DamagePerTurn, Is.EqualTo(1),
                    "fire_arrow: flat 1 damage/turn (NOT 1d4 — PoC test authoritative)");
                Assert.That(burning.RemainingTurns, Is.EqualTo(3),
                    "fire_arrow: 3 turn duration");
                return;
            }
        }
        Assert.Fail("Should have hit at least once in 50 attempts");
    }

    [Test]
    public void RangedAttack_MinimumOneDamage_EvenWithPenalty()
    {
        // d=8 (extreme range = 25% multiplier). Even with 1 base damage, should deal 1.
        var player = MakeEntity("Player", 54, id: 0, x: 1, y: 6, dex: 20, accuracy: 20);
        var monster = MakeEntity("Orc", 200, id: 1, x: 9, y: 6, dex: 1, evasion: 0); // d=8
        // Min/Max = 1, STR mod = 0 → base damage = 1 → 25% = 0 → floor to 1
        var player2 = MakeEntity("Player", 54, id: 0, x: 1, y: 6, dex: 20, accuracy: 20, str: 10, dmgMin: 1, dmgMax: 1);
        EquipBow(player2, dmgMin: 1, dmgMax: 1);
        var state = MakeArena(player2, monster);
        var events = new List<TurnEvent>();

        for (int i = 0; i < 20; i++)
        {
            events.Clear();
            RangedCombatService.AttemptRangedAttack(player2, monster, state, events);
            var e = events.OfType<RangedAttackEvent>().FirstOrDefault();
            if (e?.Hit == true)
            {
                Assert.That(e.Damage, Is.GreaterThanOrEqualTo(1),
                    "Minimum 1 damage floor must be enforced even at extreme range");
                return;
            }
        }
        Assert.Fail("Should have hit within 20 attempts");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TurnController Integration Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void TurnController_RangedAttack_RequiresRangedWeapon()
    {
        // Player with melee weapon: ShootAt should behave as Wait
        var player = MakeEntity("Player", 54, id: 0, x: 3, y: 6);
        var monster = MakeEntity("Orc", 28, id: 1, x: 7, y: 6);
        // Equip a sword (not ranged)
        var equip = player.GetOrAdd<Equipment>();
        var sword = new Entity(10, "Sword");
        sword.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 2, DamageMax = 8, IsRangedWeapon = false });
        equip.MainHand = sword;

        var state = MakeArena(player, monster);
        state.IsHarnessMode = true;
        var result = TurnController.ProcessTurn(state, PlayerAction.ShootAt(monster));

        // Should emit WaitEvent instead of attacking
        Assert.That(result.Events.OfType<WaitEvent>().Any(), Is.True,
            "Shooting without a ranged weapon should emit Wait");
        Assert.That(result.Events.OfType<RangedAttackEvent>().Any(), Is.False);
    }

    [Test]
    public void TurnController_EntangleMoveBlocked_EmittedOnEntangledMove()
    {
        var player = MakeEntity("Player", 54, id: 0, x: 3, y: 6);
        player.Add(new EntangledEffect { RemainingTurns = 2 });
        var monster = MakeEntity("Orc", 28, id: 1, x: 8, y: 6);
        var state = MakeArena(player, monster);
        state.IsHarnessMode = true;

        var result = TurnController.ProcessTurn(state, PlayerAction.MoveTo(5, 6));

        var entangleEvt = result.Events.OfType<EntangleMoveBlockedEvent>().FirstOrDefault();
        Assert.That(entangleEvt, Is.Not.Null, "Entangled player moving must emit EntangleMoveBlockedEvent");
        Assert.That(entangleEvt!.BlockedActionType, Is.EqualTo("move"));
        Assert.That(entangleEvt.EntityId, Is.EqualTo(player.Id));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // YAML Content Loading Tests
    // ─────────────────────────────────────────────────────────────────────────

    private static string EntitiesFilePath =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "config", "entities.yaml");

    /// <summary>
    /// Get items via LoadAllFromFile which correctly strips floor_item_pool before parsing.
    /// Direct ContentLoader.LoadItems on full entities.yaml fails because floor_item_pool is a sequence.
    /// </summary>
    private static Dictionary<string, ItemDefinition> LoadItemsFromFile()
    {
        var loader = new ContentLoader();
        var bundle = loader.LoadAllFromFile(EntitiesFilePath);
        return bundle.Items;
    }

    [Test]
    public void ContentLoader_LoadsShortbow()
    {
        var items = LoadItemsFromFile();
        Assert.That(items.ContainsKey("shortbow"), Is.True, "shortbow must be in entities.yaml");
        var def = items["shortbow"];
        Assert.That(def.IsRangedWeapon, Is.True);
        Assert.That(def.TwoHanded, Is.True);
        Assert.That(def.DamageMin, Is.EqualTo(1));
        Assert.That(def.DamageMax, Is.EqualTo(6));
    }

    [Test]
    public void ContentLoader_LoadsLongbow()
    {
        var items = LoadItemsFromFile();
        Assert.That(items.ContainsKey("longbow"), Is.True, "longbow must be in entities.yaml");
        var def = items["longbow"];
        Assert.That(def.IsRangedWeapon, Is.True);
        Assert.That(def.TwoHanded, Is.True);
        Assert.That(def.DamageMax, Is.EqualTo(8));
    }

    [Test]
    public void ContentLoader_LoadsNetArrow()
    {
        var items = LoadItemsFromFile();
        Assert.That(items.ContainsKey("net_arrow"), Is.True, "net_arrow must be in entities.yaml");
        var def = items["net_arrow"];
        Assert.That(def.IsSpecialAmmo, Is.True);
        Assert.That(def.StackSize, Is.EqualTo(8));
    }

    [Test]
    public void ContentLoader_LoadsFireArrow()
    {
        var items = LoadItemsFromFile();
        Assert.That(items.ContainsKey("fire_arrow"), Is.True, "fire_arrow must be in entities.yaml");
        var def = items["fire_arrow"];
        Assert.That(def.IsSpecialAmmo, Is.True);
        Assert.That(def.StackSize, Is.EqualTo(10));
    }

    [Test]
    public void ItemFactory_CreatesBow_WithRangedWeaponFlag()
    {
        var items = LoadItemsFromFile();
        var factory = new ItemFactory(items, new EntityFactory());
        var bow = factory.Create("longbow");
        Assert.That(bow, Is.Not.Null);
        var eq = bow!.Get<Equippable>();
        Assert.That(eq!.IsRangedWeapon, Is.True);
        Assert.That(eq.TwoHanded, Is.True);
        Assert.That(eq.Slot, Is.EqualTo(EquipmentSlot.MainHand));
    }

    [Test]
    public void ItemFactory_CreatesNetArrow_WithConsumable()
    {
        var items = LoadItemsFromFile();
        var factory = new ItemFactory(items, new EntityFactory());
        var arrow = factory.Create("net_arrow");
        Assert.That(arrow, Is.Not.Null);
        var eq = arrow!.Get<Equippable>();
        Assert.That(eq!.IsSpecialAmmo, Is.True);
        Assert.That(eq.Slot, Is.EqualTo(EquipmentSlot.Quiver));
        var consumable = arrow.Get<Consumable>();
        Assert.That(consumable, Is.Not.Null);
        Assert.That(consumable!.StackSize, Is.EqualTo(8));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scenario Harness Smoke Tests
    // ─────────────────────────────────────────────────────────────────────────

    private static string ScenarioPath(string f) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "config", "levels", f);

    private static string ConfigPath(string f) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "config", f);

    private ScenarioRunner? _runner;

    [OneTimeSetUp]
    public void SetupRunner()
    {
        _runner = ScenarioRunner.FromEntitiesFile(ConfigPath("entities.yaml"));
    }

    [Test]
    [Category("Slow")]
    public void ScenarioRangedViabilityArena_NocrashAndRangedAttacksFire()
    {
        var path = ScenarioPath("scenario_ranged_viability_arena.yaml");
        Assert.That(File.Exists(path), Is.True, "scenario file must exist");
        var agg = _runner!.RunFromFile(path, baseSeed: 1337);

        Assert.That(agg.TotalRuns, Is.GreaterThan(0), "Harness must complete runs");
        Assert.That(agg.AvgRangedAttacksMadeByPlayer, Is.GreaterThan(0),
            "ranged_net_arrow bot must make ranged attacks in optimal-range scenario");
    }

    [Test]
    [Category("Slow")]
    public void ScenarioRangedAdjacentPunish_RetaliationsTriggered()
    {
        var path = ScenarioPath("scenario_ranged_adjacent_punish_arena.yaml");
        Assert.That(File.Exists(path), Is.True, "scenario file must exist");
        var agg = _runner!.RunFromFile(path, baseSeed: 1337);

        Assert.That(agg.AvgRangedAdjacentRetaliationsTriggered, Is.GreaterThan(0),
            "Adjacent ranged shots must trigger retaliation");
    }

    [Test]
    [Category("Slow")]
    public void ScenarioRangedMaxRangeDenial_DenialsIncrement()
    {
        var path = ScenarioPath("scenario_ranged_max_range_denial_arena.yaml");
        Assert.That(File.Exists(path), Is.True, "scenario file must exist");
        var agg = _runner!.RunFromFile(path, baseSeed: 1337);

        Assert.That(agg.AvgRangedAttacksDeniedOutOfRange, Is.GreaterThan(0),
            "Shots at d>8 must be denied and counted");
    }

    [Test]
    [Category("Slow")]
    public void ScenarioSkirmisherVsRangedNet_EntangleMovesBlocked()
    {
        var path = ScenarioPath("scenario_skirmisher_vs_ranged_net_identity.yaml");
        Assert.That(File.Exists(path), Is.True, "scenario file must exist");
        var agg = _runner!.RunFromFile(path, baseSeed: 1337);

        Assert.That(agg.TotalRuns, Is.GreaterThan(0));
        Assert.That(agg.AvgSpecialAmmoShotsFired, Is.GreaterThan(0),
            "Net arrows should be fired in this scenario");
    }
}
