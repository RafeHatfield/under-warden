using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Persistence.MidRun;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

/// <summary>
/// Device regression triage (item 4/5) — symptom A: a LADEN player (stacked consumables, full
/// inventory, multi-slot equipment) through the real SaveMidRun → JSON → LoadMidRun path. Asserts exact
/// stack counts, slot occupancy, no consumable-in-equipment, and entity-id uniqueness.
/// </summary>
[TestFixture]
public class MidRunLadenPlayerTests
{
    private static Entity Junk(int id, string type)
    {
        var e = new Entity(id, type);
        e.Add(new ItemTag(type));
        return e;
    }

    private static Entity Potion(int id, string type, int stack)
    {
        var e = new Entity(id, type);
        e.Add(new ItemTag(type));
        e.Add(new Consumable(healAmount: 20, isPotion: true) { StackSize = stack });
        return e;
    }

    private static Entity Gear(int id, string type, EquipmentSlot slot)
    {
        var e = new Entity(id, type);
        e.Add(new ItemTag(type));
        e.Add(new Equippable(slot) { DamageMin = 1, DamageMax = 6, ArmorClassBonus = 2 });
        return e;
    }

    private static GameState LadenState()
    {
        var map = new GameMap(8, 8);
        for (int x = 0; x < 8; x++) for (int y = 0; y < 8; y++) map.SetTile(x, y, TileKind.Floor);

        var player = new Entity(1, "Player", 4, 4, blocksMovement: true);
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 10, constitution: 10, accuracy: 5, evasion: 0, damageMin: 1, damageMax: 2));
        map.RegisterEntity(player);

        var inv = player.Add(new Inventory());
        inv.Add(Potion(100, "healing_potion", 8));   // the 8-stack from the device report
        inv.Add(Potion(101, "mana_potion", 3));       // a partially-used stack
        // Fill the rest of the 25 slots with distinct junk so the inventory is FULL on reload.
        for (int i = 0; i < Inventory.Capacity - 2; i++) inv.Add(Junk(200 + i, $"junk_{i}"));
        Assume.That(inv.IsFull, Is.True, "the repro needs a full inventory.");

        player.Add(new Equipment
        {
            MainHand = Gear(300, "sword", EquipmentSlot.MainHand),
            Chest = Gear(301, "plate", EquipmentSlot.Chest),
            LeftRing = Gear(302, "ring", EquipmentSlot.LeftRing),
        });

        return new GameState(player, new List<Entity>(), map, new SeededRandom(1337), turnLimit: 1000);
    }

    [Test]
    public void LadenPlayer_SurvivesResume_WithExactStacksSlotsAndUniqueIds()
    {
        var state = LadenState();
        int savedCount = state.Player.Require<Inventory>().Count;

        var json = JsonSerializer.Serialize(MidRunSerializer.SaveMidRun(state), MidRunSaveJsonContext.Default.MidRunSaveDto);
        var dto = JsonSerializer.Deserialize(json, MidRunSaveJsonContext.Default.MidRunSaveDto)!;
        var loaded = MidRunSerializer.LoadMidRun(dto);

        var inv = loaded.Player.Require<Inventory>();
        var equip = loaded.Player.Require<Equipment>();

        Assert.Multiple(() =>
        {
            Assert.That(inv.Count, Is.EqualTo(savedCount), "inventory slot count must survive resume.");
            var healing = inv.Items.FirstOrDefault(i => i.Get<ItemTag>()?.TypeId == "healing_potion");
            Assert.That(healing, Is.Not.Null, "the healing-potion stack must still be present.");
            Assert.That(healing!.Require<Consumable>().StackSize, Is.EqualTo(8), "the 8-stack must not collapse.");
            Assert.That(inv.Items.First(i => i.Get<ItemTag>()?.TypeId == "mana_potion").Require<Consumable>().StackSize,
                Is.EqualTo(3), "the partial stack must survive exactly.");

            Assert.That(equip.MainHand?.Get<ItemTag>()?.TypeId, Is.EqualTo("sword"));
            Assert.That(equip.Chest?.Get<ItemTag>()?.TypeId, Is.EqualTo("plate"));
            Assert.That(equip.LeftRing?.Get<ItemTag>()?.TypeId, Is.EqualTo("ring"));

            foreach (var slot in new[] { equip.MainHand, equip.OffHand, equip.Head, equip.Chest, equip.Feet,
                                         equip.LeftRing, equip.RightRing, equip.Neck, equip.Quiver })
                Assert.That(slot?.Get<Consumable>(), Is.Null, "no consumable may occupy an equipment slot after resume.");
        });

        // Entity-id uniqueness across the whole saved table (an id collision is how items cross-wire slots).
        var ids = MidRunSerializer.SaveMidRun(loaded).Entities.Entities.Select(e => e.Id).ToList();
        Assert.That(ids.Count, Is.EqualTo(ids.Distinct().Count()), "entity ids must be unique after resume.");
    }

    private static string Json(MidRunSaveDto d) => JsonSerializer.Serialize(d, MidRunSaveJsonContext.Default.MidRunSaveDto);

    // S1 for the laden player: serialize → load → serialize is byte-identical (closes the coverage gap
    // that the prior S1 matrix used sparse players).
    [Test]
    public void LadenPlayer_S1_ByteIdentical()
    {
        var json1 = Json(MidRunSerializer.SaveMidRun(LadenState()));
        var loaded = MidRunSerializer.LoadMidRun(JsonSerializer.Deserialize(json1, MidRunSaveJsonContext.Default.MidRunSaveDto)!);
        var json2 = Json(MidRunSerializer.SaveMidRun(loaded));
        Assert.That(json2, Is.EqualTo(json1), "laden-player save must round-trip byte-identically (S1).");
    }
}
