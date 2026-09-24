using System.Text.Json;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Persistence.MidRun;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

/// <summary>
/// Graph-shape guarantees for the entity-table serializer (M1.4 §Entity-table serialization):
/// identity preservation, cycles, and Owner reconstruction.
/// </summary>
[TestFixture]
public class MidRunEntityGraphTests
{
    private static Entity Item(int id, string name)
    {
        var e = new Entity(id, name);
        e.Add(new ItemTag(name));
        e.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 1, DamageMax = 6 });
        return e;
    }

    [Test]
    public void Identity_EntityReferencedTwice_DeserializesToOneObject()
    {
        // The same weapon entity is both in the player's inventory AND equipped in main hand.
        var sword = Item(2, "sword");
        var player = new Entity(1, "player");
        var inv = player.Add(new Inventory());
        inv.Add(sword);
        player.Add(new Equipment { MainHand = sword });

        var table = EntitySerializer.Serialize(new[] { player });
        var byId = EntitySerializer.Deserialize(table);

        var p = byId[1];
        var invItem = p.Require<Inventory>().Items.Single();
        var equipped = p.Require<Equipment>().MainHand;
        Assert.That(ReferenceEquals(invItem, equipped), Is.True, "The doubly-referenced entity must be ONE object after load.");
        Assert.That(invItem.Id, Is.EqualTo(2));
        // And the table contained the item exactly once.
        Assert.That(table.Entities.Count, Is.EqualTo(2));
    }

    [Test]
    public void Cycle_MutualReferences_RoundTripWithoutDuplicationOrRecursion()
    {
        // A ⇄ B via PortalCastState.PendingEntrance (an Entity object reference on each side).
        var a = new Entity(10, "portalA");
        var b = new Entity(11, "portalB");
        a.Add(new PortalCastStateComponent { Step = PortalCastStep.EntrancePlaced, PendingEntrance = b });
        b.Add(new PortalCastStateComponent { Step = PortalCastStep.EntrancePlaced, PendingEntrance = a });

        var table = EntitySerializer.Serialize(new[] { a });      // closure must reach b through a
        Assert.That(table.Entities.Count, Is.EqualTo(2));

        var byId = EntitySerializer.Deserialize(table);
        var a2 = byId[10];
        var b2 = byId[11];
        Assert.That(ReferenceEquals(a2.Require<PortalCastStateComponent>().PendingEntrance, b2), Is.True);
        Assert.That(ReferenceEquals(b2.Require<PortalCastStateComponent>().PendingEntrance, a2), Is.True);
    }

    [Test]
    public void Owner_IsReconstructedForEveryComponent()
    {
        var e = new Entity(5, "orc", 3, 4, blocksMovement: true);
        e.Add(new Fighter(20, damageMin: 2, damageMax: 5));
        e.Add(new SpeciesTag("orc"));
        e.Add(new PoisonEffect { RemainingTurns = 4, DamagePerTurn = 2 });
        e.Add(new AiComponent { AiType = "melee", Faction = "orc" });

        var byId = EntitySerializer.Deserialize(EntitySerializer.Serialize(new[] { e }));
        var loaded = byId[5];

        Assert.That(loaded.X, Is.EqualTo(3));
        Assert.That(loaded.Y, Is.EqualTo(4));
        Assert.That(loaded.BlocksMovement, Is.True);
        foreach (var component in loaded.GetAllComponents())
            Assert.That(ReferenceEquals(component.Owner, loaded), Is.True,
                $"{component.GetType().Name}.Owner must point back at its entity after load.");

        Assert.That(loaded.Require<Fighter>().Hp, Is.EqualTo(20));
        Assert.That(loaded.Require<PoisonEffect>().RemainingTurns, Is.EqualTo(4));
        Assert.That(loaded.Require<SpeciesTag>().TypeId, Is.EqualTo("orc"));
    }

    [Test]
    public void RoundTrip_ReserializesByteIdentically()
    {
        var sword = Item(2, "sword");
        var player = new Entity(1, "player", 6, 6);
        var fighter = player.Add(new Fighter(54, strength: 14));
        fighter.RingMaxHpBonus = 20;
        var inv = player.Add(new Inventory());
        inv.Add(sword);
        player.Add(new Equipment { MainHand = sword });
        player.Add(new BleedEffect { RemainingTurns = 3, Severity = 2 });

        var table1 = EntitySerializer.Serialize(new[] { player });
        string json1 = JsonSerializer.Serialize(table1, MidRunJsonContext.Default.EntityTableDto);

        var byId = EntitySerializer.Deserialize(table1);
        var table2 = EntitySerializer.Serialize(byId.Values);
        string json2 = JsonSerializer.Serialize(table2, MidRunJsonContext.Default.EntityTableDto);

        Assert.That(json2, Is.EqualTo(json1), "serialize → deserialize → serialize must be byte-identical (S1-lite).");
    }
}
