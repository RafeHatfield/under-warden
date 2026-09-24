using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Unit tests for WandComponent charge mechanics and depth-scaling creation formula.
/// Covers: TryConsume behavior, infinite wands, CreateWand depth formula, charge cap,
/// and wand auto-recharge on scroll pickup.
/// </summary>
[TestFixture]
public class WandTests
{
    // ─── WandComponent.TryConsume ────────────────────────────────────────────

    [Test]
    public void TryConsume_DecrementsCharges()
    {
        var wand = new WandComponent { Charges = 5, MaxCharges = 10 };

        var result = wand.TryConsume();

        Assert.That(result, Is.True);
        Assert.That(wand.Charges, Is.EqualTo(4));
    }

    [Test]
    public void TryConsume_EmptyWand_ReturnsFalse()
    {
        var wand = new WandComponent { Charges = 0, MaxCharges = 10 };

        var result = wand.TryConsume();

        Assert.That(result, Is.False);
        Assert.That(wand.Charges, Is.EqualTo(0), "Charges should not go negative");
    }

    [Test]
    public void TryConsume_InfiniteWand_AlwaysTrue_NoDecrement()
    {
        var wand = new WandComponent { Charges = 0, MaxCharges = 0, Infinite = true };

        var result1 = wand.TryConsume();
        var result2 = wand.TryConsume();
        var result3 = wand.TryConsume();

        Assert.That(result1, Is.True);
        Assert.That(result2, Is.True);
        Assert.That(result3, Is.True);
        Assert.That(wand.Charges, Is.EqualTo(0), "Infinite wand charges should not change");
    }

    [Test]
    public void HasCharges_NormalWandWithCharges_True()
    {
        var wand = new WandComponent { Charges = 1, MaxCharges = 10 };
        Assert.That(wand.HasCharges, Is.True);
    }

    [Test]
    public void HasCharges_NormalWandEmpty_False()
    {
        var wand = new WandComponent { Charges = 0, MaxCharges = 10 };
        Assert.That(wand.HasCharges, Is.False);
    }

    [Test]
    public void HasCharges_InfiniteWand_AlwaysTrue()
    {
        var wand = new WandComponent { Charges = 0, MaxCharges = 0, Infinite = true };
        Assert.That(wand.HasCharges, Is.True);
    }

    // ─── CreateWand depth scaling ────────────────────────────────────────────

    private const string WandYaml = """
        wands:
          wand_of_lightning:
            name: "Wand of Lightning"
            spell_id: "lightning"
            targeting: "auto_closest"
            damage: 40
            range: 5
            is_wand: true
            min_charges: 3
            max_charges: 6
            charge_cap: 10
            recharge_scroll: "lightning"
            char: "/"
            color: [255, 255, 100]
        """;

    private static SpellItemFactory BuildFactory()
    {
        var loader = new ContentLoader();
        var defs = loader.LoadSpellItems(WandYaml);
        return new SpellItemFactory(defs, new EntityFactory());
    }

    [Test]
    public void CreateWand_AtDepth1_ChargesWithinMinMax()
    {
        var factory = BuildFactory();
        var rng = new SeededRandom(1337);

        var entity = factory.CreateWand("wand_of_lightning", rng, depth: 1)!;
        var charges = entity.Require<WandComponent>().Charges;

        // depth=1: bonus = depth-1 = 0, so range is [3, 6]
        Assert.That(charges, Is.InRange(3, 6), "At depth 1, no bonus — charges should be in [3, 6]");
    }

    [Test]
    public void CreateWand_ChargeFormula_DepthScaling()
    {
        // With a fixed rng and depth, we can verify the formula:
        // charges = rand(min, max) + (depth - 1)
        // At depth 5, bonus = 4. min=3, max=6, so result should be in [7, 10].
        var factory = BuildFactory();

        // Run many samples to verify depth bonus applies
        for (int i = 0; i < 50; i++)
        {
            var rng = new SeededRandom(i);
            var entity = factory.CreateWand("wand_of_lightning", rng, depth: 5)!;
            var charges = entity.Require<WandComponent>().Charges;

            // depth=5 → bonus=4 → min_with_bonus = 3+4 = 7
            Assert.That(charges, Is.GreaterThanOrEqualTo(7),
                $"At depth 5, base [3-6] + bonus 4 should give at least 7 (seed {i})");
        }
    }

    [Test]
    public void CreateWand_ChargeCap_Enforced()
    {
        // At very high depth, charges should not exceed charge_cap (10).
        var factory = BuildFactory();

        for (int i = 0; i < 20; i++)
        {
            var rng = new SeededRandom(i);
            var entity = factory.CreateWand("wand_of_lightning", rng, depth: 20)!;
            var charges = entity.Require<WandComponent>().Charges;

            Assert.That(charges, Is.LessThanOrEqualTo(10),
                $"Charges must never exceed charge_cap of 10 (seed {i})");
        }
    }

    [Test]
    public void CreateWand_DefaultDepth1_SameAsExplicitDepth1()
    {
        var factory = BuildFactory();
        var rng1 = new SeededRandom(42);
        var rng2 = new SeededRandom(42);

        var e1 = factory.CreateWand("wand_of_lightning", rng1)!;         // default depth=1
        var e2 = factory.CreateWand("wand_of_lightning", rng2, depth: 1)!; // explicit depth=1

        Assert.That(e1.Require<WandComponent>().Charges,
            Is.EqualTo(e2.Require<WandComponent>().Charges),
            "Default depth parameter should behave identically to passing depth=1");
    }

    // ─── Wand recharge via TurnController (integration) ─────────────────────

    private static GameState CreatePickupState()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(15, 15);
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 4));
        player.Add(new Inventory());
        map.RegisterEntity(player);
        return new GameState(player, [], map, rng);
    }

    [Test]
    public void Recharge_WandAtCap_ScrollKeptInInventory()
    {
        var state = CreatePickupState();

        // Wand already at max charges
        var wand = new Entity(10, "Wand of Lightning");
        wand.Add(new WandComponent { Charges = 10, MaxCharges = 10, RechargeScrollId = "lightning" });
        wand.Add(new SpellEffect { SpellId = "lightning" });
        state.Player.Require<Inventory>().Add(wand);

        // Scroll on floor one step south
        var scroll = new Entity(20, "Scroll of Lightning");
        scroll.Add(new Consumable(healAmount: 0) { StackSize = 1 });
        scroll.Add(new SpellEffect { SpellId = "lightning" });
        scroll.X = 5;
        scroll.Y = 6;
        state.FloorItems.Add(scroll);
        state.Map.RegisterEntity(scroll);

        TurnController.ProcessTurn(state, PlayerAction.MoveTo(5, 6));

        // Wand should NOT gain a charge (already at cap)
        Assert.That(wand.Require<WandComponent>().Charges, Is.EqualTo(10));
        // Scroll should be in inventory (normal pickup, not consumed for recharge)
        Assert.That(state.Player.Require<Inventory>().Items, Does.Contain(scroll));
    }

    [Test]
    public void Recharge_NoMatchingWand_ScrollAddedToInventory()
    {
        var state = CreatePickupState();
        // No wand in inventory at all

        // Scroll of lightning on floor one step south
        var scroll = new Entity(20, "Scroll of Lightning");
        scroll.Add(new Consumable(healAmount: 0) { StackSize = 1 });
        scroll.Add(new SpellEffect { SpellId = "lightning" });
        scroll.X = 5;
        scroll.Y = 6;
        state.FloorItems.Add(scroll);
        state.Map.RegisterEntity(scroll);

        TurnController.ProcessTurn(state, PlayerAction.MoveTo(5, 6));

        // Scroll goes into inventory normally
        Assert.That(state.Player.Require<Inventory>().Items, Does.Contain(scroll));
    }

    [Test]
    public void EmptyWand_UseEmitsOutOfChargesEvent()
    {
        var state = CreatePickupState();
        state.Monsters.Add(new Entity(1, "Orc", 6, 5, true));
        state.Monsters[0].Add(new Fighter(hp: 100, strength: 10, dexterity: 10,
            constitution: 10, accuracy: 2, evasion: 0, damageMin: 2, damageMax: 4));
        state.Map.RegisterEntity(state.Monsters[0]);

        var wand = new Entity(10, "Wand of Lightning");
        wand.Add(new WandComponent { Charges = 0, MaxCharges = 10 });
        wand.Add(new SpellEffect { SpellId = "lightning", Targeting = TargetingMode.AutoClosest,
            Damage = 40, Range = 5 });
        state.Player.Require<Inventory>().Add(wand);

        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(wand));

        var wandEvent = result.Events.OfType<WandUseEvent>().Single();
        Assert.That(wandEvent.Success, Is.False, "Empty wand should emit failed use event");
        Assert.That(wandEvent.RemainingCharges, Is.EqualTo(0));
    }
}
