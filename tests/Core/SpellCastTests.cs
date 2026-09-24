using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Integration tests for scroll and wand use through TurnController.CastSpell.
/// Tests the full flow: PlayerAction.CastSpell → TurnController → SpellResolver → events.
/// </summary>
[TestFixture]
public class SpellCastTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static GameState CreateState(int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(20, 20);

        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 4));
        player.Add(new Inventory());
        map.RegisterEntity(player);

        return new GameState(player, new List<Entity>(), map, rng);
    }

    private static Entity AddMonster(GameState state, int id, int x, int y, int hp = 50)
    {
        var m = new Entity(id, "Orc", x, y, blocksMovement: true);
        m.Add(new Fighter(hp: hp, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 0, damageMin: 2, damageMax: 4));
        state.Monsters.Add(m);
        state.Map.RegisterEntity(m);
        return m;
    }

    private static Entity MakeScrollOfLightning(int id = 100)
    {
        var scroll = new Entity(id, "Scroll of Lightning");
        scroll.Add(new Consumable(healAmount: 0) { StackSize = 1 });
        scroll.Add(new SpellEffect
        {
            SpellId = "lightning",
            Targeting = TargetingMode.AutoClosest,
            Damage = 40,
            Range = 5,
        });
        return scroll;
    }

    private static Entity MakeWandOfLightning(int id = 200, int charges = 3)
    {
        var wand = new Entity(id, "Wand of Lightning");
        wand.Add(new WandComponent { Charges = charges, MaxCharges = 10, RechargeScrollId = "lightning" });
        wand.Add(new SpellEffect
        {
            SpellId = "lightning",
            Targeting = TargetingMode.AutoClosest,
            Damage = 40,
            Range = 5,
        });
        return wand;
    }

    // ─── Scroll Use ──────────────────────────────────────────────────────────

    [Test]
    public void CastSpell_ScrollOfLightning_DamagesNearestMonster()
    {
        var state = CreateState();
        var monster = AddMonster(state, 1, x: 6, y: 5, hp: 100);
        var scroll = MakeScrollOfLightning();
        state.Player.Require<Inventory>().Add(scroll);

        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(scroll));

        var spellEvent = result.Events.OfType<SpellEvent>().Single();
        Assert.That(spellEvent.Success, Is.True);
        Assert.That(monster.Require<Fighter>().Hp, Is.LessThan(100));
    }

    [Test]
    public void CastSpell_ScrollOfLightning_ScrollRemovedFromInventoryAfterUse()
    {
        var state = CreateState();
        AddMonster(state, 1, x: 6, y: 5, hp: 100);
        var scroll = MakeScrollOfLightning();
        state.Player.Require<Inventory>().Add(scroll);

        TurnController.ProcessTurn(state, PlayerAction.CastSpell(scroll));

        Assert.That(state.Player.Require<Inventory>().Items, Does.Not.Contain(scroll));
    }

    [Test]
    public void CastSpell_StackedScrolls_DecrementStackNotRemoveSlot()
    {
        var state = CreateState();
        AddMonster(state, 1, x: 6, y: 5, hp: 100);

        var scroll = MakeScrollOfLightning();
        scroll.Require<Consumable>().StackSize = 3;
        state.Player.Require<Inventory>().Add(scroll);

        TurnController.ProcessTurn(state, PlayerAction.CastSpell(scroll));

        // Stack went from 3 → 2, item stays in inventory
        Assert.That(scroll.Require<Consumable>().StackSize, Is.EqualTo(2));
        Assert.That(state.Player.Require<Inventory>().Items, Does.Contain(scroll));
    }

    // ─── Wand Use ────────────────────────────────────────────────────────────

    [Test]
    public void CastSpell_Wand_DeductsOneCharge()
    {
        var state = CreateState();
        AddMonster(state, 1, x: 6, y: 5, hp: 100);
        var wand = MakeWandOfLightning(charges: 3);
        state.Player.Require<Inventory>().Add(wand);

        TurnController.ProcessTurn(state, PlayerAction.CastSpell(wand));

        Assert.That(wand.Require<WandComponent>().Charges, Is.EqualTo(2));
    }

    [Test]
    public void CastSpell_Wand_EmitsWandUseEventWithRemainingCharges()
    {
        var state = CreateState();
        AddMonster(state, 1, x: 6, y: 5, hp: 100);
        var wand = MakeWandOfLightning(charges: 5);
        state.Player.Require<Inventory>().Add(wand);

        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(wand));

        var wandEvent = result.Events.OfType<WandUseEvent>().Single();
        Assert.That(wandEvent.Success, Is.True);
        Assert.That(wandEvent.RemainingCharges, Is.EqualTo(4));
        Assert.That(wandEvent.WandDestroyed, Is.False);
    }

    [Test]
    public void CastSpell_Wand_LastCharge_WandDestroyedAndRemovedFromInventory()
    {
        var state = CreateState();
        AddMonster(state, 1, x: 6, y: 5, hp: 100);
        var wand = MakeWandOfLightning(charges: 1);
        state.Player.Require<Inventory>().Add(wand);

        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(wand));

        var wandEvent = result.Events.OfType<WandUseEvent>().Single();
        Assert.That(wandEvent.WandDestroyed, Is.True);
        Assert.That(state.Player.Require<Inventory>().Items, Does.Not.Contain(wand));
    }

    [Test]
    public void CastSpell_WandOutOfCharges_EmitsFailedWandEvent_ScrollNotConsumed()
    {
        var state = CreateState();
        AddMonster(state, 1, x: 6, y: 5, hp: 100);
        var wand = MakeWandOfLightning(charges: 0);
        state.Player.Require<Inventory>().Add(wand);

        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(wand));

        var wandEvent = result.Events.OfType<WandUseEvent>().Single();
        Assert.That(wandEvent.Success, Is.False);
        // Wand stayed in inventory (not destroyed — player can still see it and recharge it)
        Assert.That(state.Player.Require<Inventory>().Items, Does.Contain(wand));
    }

    [Test]
    public void CastSpell_InfiniteWand_NeverDeductsCharges()
    {
        var state = CreateState();
        AddMonster(state, 1, x: 6, y: 5, hp: 100);

        var wand = new Entity(200, "Wand of Portals");
        wand.Add(new WandComponent { Charges = 0, MaxCharges = 0, Infinite = true });
        wand.Add(new SpellEffect { SpellId = "lightning", Targeting = TargetingMode.AutoClosest,
            Damage = 40, Range = 5 });
        state.Player.Require<Inventory>().Add(wand);

        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(wand));

        var wandEvent = result.Events.OfType<WandUseEvent>().Single();
        Assert.That(wandEvent.Success, Is.True);
        Assert.That(wandEvent.WandDestroyed, Is.False);
        // Wand stays in inventory
        Assert.That(state.Player.Require<Inventory>().Items, Does.Contain(wand));
    }

    // ─── Wand Auto-Recharge ──────────────────────────────────────────────────

    [Test]
    public void PickUp_ScrollMatchingWand_RechargesWandInsteadOfInventoryAdd()
    {
        // Player starts at (5,5). Place scroll at (5,6) — one step south.
        // Moving south triggers pickup at the new position.
        var state = CreateState();

        // Wand already in inventory (charges below cap)
        var wand = MakeWandOfLightning(charges: 2);
        state.Player.Require<Inventory>().Add(wand);

        // Scroll of Lightning on floor one step south of player
        var scroll = MakeScrollOfLightning(id: 101);
        scroll.X = state.Player.X;     // 5
        scroll.Y = state.Player.Y + 1; // 6
        state.FloorItems.Add(scroll);
        state.Map.RegisterEntity(scroll);

        // Move south to pick up the scroll
        TurnController.ProcessTurn(state, PlayerAction.MoveTo(state.Player.X, state.Player.Y + 1));

        // Wand should have gained 1 charge (scroll consumed for recharge)
        Assert.That(wand.Require<WandComponent>().Charges, Is.EqualTo(3));
        // Scroll should NOT be in inventory
        Assert.That(state.Player.Require<Inventory>().Items, Does.Not.Contain(scroll));
    }

    [Test]
    public void PickUp_ScrollMatchingWandAtCap_AddsScrollToInventoryNormally()
    {
        // Player at (5,5), scroll at (5,6).
        var state = CreateState();

        // Wand at max charges — no room to recharge
        var wand = MakeWandOfLightning(charges: 10); // MaxCharges = 10
        state.Player.Require<Inventory>().Add(wand);

        // Scroll on floor one step south
        var scroll = MakeScrollOfLightning(id: 101);
        scroll.X = state.Player.X;
        scroll.Y = state.Player.Y + 1;
        state.FloorItems.Add(scroll);
        state.Map.RegisterEntity(scroll);

        TurnController.ProcessTurn(state, PlayerAction.MoveTo(state.Player.X, state.Player.Y + 1));

        // Wand charge unchanged (at cap)
        Assert.That(wand.Require<WandComponent>().Charges, Is.EqualTo(10));
        // Scroll added to inventory instead
        Assert.That(state.Player.Require<Inventory>().Items, Does.Contain(scroll));
    }
}
