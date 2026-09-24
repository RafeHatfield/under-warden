using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Persistence.MidRun;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

/// <summary>
/// Reachability closure (M1.4 §Entity-table): the table must contain every entity reachable BY ID
/// from a root, not just entities that appear in a top-level list. The delay-fuse case is prop/chest
/// loot: entities pre-generated at floor-gen that sit in NO floor list until the container opens.
///
/// These are serializer-level tests. The full end-to-end variant (build floor → save → load → open
/// chest → loot appears) belongs with GameState SaveMidRun/LoadMidRun (deferred — see PR/report).
/// </summary>
[TestFixture]
public class MidRunReachabilityTests
{
    private static Entity Loot(int id, string name)
    {
        var e = new Entity(id, name);
        e.Add(new ItemTag(name));
        e.Add(new Consumable(healAmount: 5, isPotion: true));
        return e;
    }

    [Test]
    public void UnopenedProp_LootHeldInStash_IsCaptured_AndNotInAnyList()
    {
        // A destructible prop holds two pre-generated loot entities in a ChestLootStash. They are in
        // NO floor list yet (the prop hasn't been bumped). The prop is the only root.
        var loot1 = Loot(100, "healing_potion");
        var loot2 = Loot(101, "acid_potion");
        var prop = new Entity(50, "barrel", 4, 4, blocksMovement: true);
        prop.Add(new DestructiblePropComponent
        {
            PropKind = "barrel",
            LootEntityIds = new List<int> { loot1.Id, loot2.Id },
        });
        prop.Add(new ChestLootStash(new List<Entity> { loot1, loot2 }));

        var table = EntitySerializer.Serialize(new[] { prop });

        // Closure reached the loot even though it was in no top-level list.
        Assert.That(table.Entities.Select(e => e.Id), Is.EquivalentTo(new[] { 50, 100, 101 }),
            "the loot entities must be captured via the prop's stash (reachability closure).");

        var byId = EntitySerializer.Deserialize(table);
        var prop2 = byId[50];

        // LootEntityIds (the id mirror) survives.
        Assert.That(prop2.Require<DestructiblePropComponent>().LootEntityIds, Is.EqualTo(new List<int> { 100, 101 }));

        // The stash resolves to the SAME restored entities (identity), and they carry their components.
        var stash = prop2.Require<ChestLootStash>().Items;
        Assert.That(stash.Select(e => e.Id), Is.EqualTo(new[] { 100, 101 }));
        Assert.That(ReferenceEquals(stash[0], byId[100]), Is.True);
        Assert.That(stash[0].Require<Consumable>().HealAmount, Is.EqualTo(5), "restored loot keeps its component state.");

        // And the loot lives only as a table member — attached to no floor list here (as pre-open loot does).
        Assert.That(byId.ContainsKey(100) && byId.ContainsKey(101), Is.True);
    }

    [Test]
    public void LootEntity_ReferencedFromStash_HasStableIdentityAfterLoad()
    {
        var loot = Loot(7, "scroll");
        var chest = new Entity(6, "chest");
        chest.Add(new ChestComponent { LootItemIds = new List<string> { "scroll" } });
        chest.Add(new ChestLootStash(new List<Entity> { loot }));

        var byId = EntitySerializer.Deserialize(EntitySerializer.Serialize(new[] { chest }));
        Assert.That(byId.ContainsKey(7), Is.True, "chest loot reachable through the stash must be restored.");
        Assert.That(byId[6].Require<ChestLootStash>().Items.Single().Id, Is.EqualTo(7));
    }
}
