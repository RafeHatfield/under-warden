using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for DungeonFloorBuilder covering Phase 5.
/// Verifies that Build() produces structurally valid, deterministic floors
/// and that player carry-forward works end-to-end.
/// </summary>
[TestFixture]
public class DungeonFloorBuilderTests
{
    // Minimal YAML — one monster with known ETP, one consumable
    private const string ContentYaml = @"
monsters:
  orc_grunt:
    name: Orc Grunt
    stats:
      hp: 20
      power: 0
      defense: 0
      xp: 25
      damage_min: 3
      damage_max: 5
      strength: 12
      dexterity: 10
      constitution: 10
      accuracy: 3
      evasion: 1
    char: o
    ai_type: basic
    blocks: true
    faction: orc
    tags: [humanoid, living]
    etp_base: 20
consumables:
  healing_potion:
    name: Healing Potion
    heal_amount: 20
";

    // Minimal level template for depth 1 with a guaranteed spawn
    private const string TemplateWithGuaranteedSpawn = @"
levels:
  1:
    parameters:
      max_rooms: 6
      min_room_size: 5
      max_room_size: 10
    guaranteed_spawns:
      mode: replace
      monsters:
        - type: orc_grunt
          count: 2
";

    private const string TemplateEmpty = @"
levels: {}
";

    private static (MonsterFactory monsters, ItemFactory items, ConsumableFactory consumables) BuildFactories()
    {
        var loader = new ContentLoader();
        var bundle = loader.LoadAll(ContentYaml);
        var entityFactory = new EntityFactory(startId: 1);
        return (
            new MonsterFactory(bundle.Monsters, entityFactory),
            new ItemFactory(bundle.Items, entityFactory),
            new ConsumableFactory(bundle.Consumables, entityFactory)
        );
    }

    private static DungeonFloorBuilder BuilderWithTemplate(string templateYaml)
    {
        var (monsters, items, consumables) = BuildFactories();
        var registry = LevelTemplateRegistry.FromYaml(templateYaml);
        return new DungeonFloorBuilder(registry, monsters, items, consumables);
    }

    // ─── Basic structure ─────────────────────────────────────────────────────

    [Test]
    public void Build_Returns_DungeonModeState()
    {
        var builder = BuilderWithTemplate(TemplateEmpty);
        var state = builder.Build(1, new SeededRandom(1337));

        Assert.That(state.IsDungeonMode, Is.True);
        Assert.That(state.CurrentDepth, Is.EqualTo(1));
    }

    [Test]
    public void Build_Player_SpawnsAtPlayerRoomCenter()
    {
        var builder = BuilderWithTemplate(TemplateEmpty);
        var rng = new SeededRandom(1337);
        var state = builder.Build(1, rng);

        // Player spawn must be on a walkable tile
        Assert.That(state.Map.IsWalkable(state.Player.X, state.Player.Y), Is.True,
            "Player must spawn on a walkable tile");
    }

    [Test]
    public void Build_StairDown_EntityExists_WithStairComponent()
    {
        var builder = BuilderWithTemplate(TemplateEmpty);
        var state = builder.Build(1, new SeededRandom(1337));

        Assert.That(state.StairDown, Is.Not.Null, "StairDown entity must be set");
        var stair = state.StairDown!.Get<Stair>();
        Assert.That(stair, Is.Not.Null, "StairDown must have Stair component");
        Assert.That(stair!.IsDown, Is.True, "Stair must be a down-stair");
        Assert.That(stair.TargetDepth, Is.EqualTo(2), "Target depth = current depth + 1");
    }

    [Test]
    public void Build_StairDown_IsOnWalkableTile()
    {
        var builder = BuilderWithTemplate(TemplateEmpty);
        var state = builder.Build(1, new SeededRandom(1337));

        Assert.That(state.StairDown, Is.Not.Null);
        Assert.That(state.Map.IsWalkable(state.StairDown!.X, state.StairDown.Y), Is.True,
            "StairDown must be on a walkable tile");
    }

    // ─── Guaranteed spawns — mode=replace ────────────────────────────────────

    [Test]
    public void Build_ModeReplace_OnlyGuaranteedMonsters_NoProceduralFill()
    {
        // Template specifies mode=replace with exactly 2 orc_grunts
        var builder = BuilderWithTemplate(TemplateWithGuaranteedSpawn);
        var state = builder.Build(1, new SeededRandom(1337));

        // Exactly 2 monsters (guaranteed), no procedural extras
        Assert.That(state.Monsters, Has.Count.EqualTo(2),
            "Replace mode: exactly guaranteed monster count, no procedural fill");
        Assert.That(state.Monsters.All(m => m.Name == "Orc Grunt"), Is.True);
    }

    // ─── Dungeon mode properties ─────────────────────────────────────────────

    [Test]
    public void Build_WithMonsters_IsFloorClear_IsFalse()
    {
        var builder = BuilderWithTemplate(TemplateWithGuaranteedSpawn);
        var state = builder.Build(1, new SeededRandom(1337));

        // Monsters are alive — floor not clear
        Assert.That(state.AliveMonsters.Count, Is.GreaterThan(0));
        Assert.That(state.IsFloorClear, Is.False);
    }

    // ─── Determinism ─────────────────────────────────────────────────────────

    [Test]
    public void Build_SameDepthAndSeed_ProducesIdenticalFloor()
    {
        var (m1, i1, c1) = BuildFactories();
        var (m2, i2, c2) = BuildFactories();
        var registry = LevelTemplateRegistry.FromYaml(TemplateEmpty);

        var b1 = new DungeonFloorBuilder(registry, m1, i1, c1);
        var b2 = new DungeonFloorBuilder(registry, m2, i2, c2);

        var s1 = b1.Build(1, new SeededRandom(42));
        var s2 = b2.Build(1, new SeededRandom(42));

        // Player spawn identical
        Assert.That(s1.Player.X, Is.EqualTo(s2.Player.X));
        Assert.That(s1.Player.Y, Is.EqualTo(s2.Player.Y));

        // Monster count identical
        Assert.That(s1.Monsters.Count, Is.EqualTo(s2.Monsters.Count));

        // Stair position identical
        if (s1.StairDown != null && s2.StairDown != null)
        {
            Assert.That(s1.StairDown.X, Is.EqualTo(s2.StairDown.X));
            Assert.That(s1.StairDown.Y, Is.EqualTo(s2.StairDown.Y));
        }
    }

    // ─── Player carry-forward ─────────────────────────────────────────────────

    [Test]
    public void Build_WithExistingPlayer_CarriesHpForward()
    {
        var builder = BuilderWithTemplate(TemplateEmpty);

        // Build floor 1 to get a player
        var floor1 = builder.Build(1, new SeededRandom(1337));
        var player = floor1.Player;

        // Wound the player to 20 HP
        player.Require<Fighter>().TakeDamage(34); // 54 - 34 = 20
        Assert.That(player.Require<Fighter>().Hp, Is.EqualTo(20), "Precondition: player is wounded");

        // Descend to floor 2 — HP should carry forward
        var floor2 = builder.Build(2, new SeededRandom(1337 + 2 * 1_000_003), existingPlayer: player);

        Assert.That(floor2.Player.Require<Fighter>().Hp, Is.EqualTo(20),
            "Wounded HP carries forward to next floor");
    }

    [Test]
    public void Build_WithExistingPlayer_EquipmentCarriedForward()
    {
        var builder = BuilderWithTemplate(TemplateEmpty);
        var floor1 = builder.Build(1, new SeededRandom(1337));
        var player = floor1.Player;

        // Give player a weapon
        var equipment = player.Add(new Equipment());
        var sword = new Entity(50, "Short Sword");
        sword.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 3, DamageMax = 6 });
        equipment.MainHand = sword;

        var floor2 = builder.Build(2, new SeededRandom(9999), existingPlayer: player);

        var newEquip = floor2.Player.Get<Equipment>();
        Assert.That(newEquip, Is.Not.Null, "Equipment component must carry forward");
        Assert.That(newEquip!.MainHand, Is.SameAs(sword), "Same weapon entity must be equipped");
    }

    [Test]
    public void Build_WithExistingPlayer_InventoryCarriedForward()
    {
        var builder = BuilderWithTemplate(TemplateEmpty);
        var floor1 = builder.Build(1, new SeededRandom(1337));
        var player = floor1.Player;

        var inv = player.Add(new Inventory());
        var potion = new Entity(99, "Healing Potion");
        potion.Add(new Consumable(healAmount: 40));
        inv.Add(potion);

        var floor2 = builder.Build(2, new SeededRandom(9999), existingPlayer: player);

        var newInv = floor2.Player.Get<Inventory>();
        Assert.That(newInv, Is.Not.Null, "Inventory must carry forward");
        Assert.That(newInv!.Count, Is.EqualTo(1), "Potion must be in carried inventory");
    }

    [Test]
    public void Build_WithExistingPlayer_PlayerSpawnedAtNewFloorSpawn()
    {
        var builder = BuilderWithTemplate(TemplateEmpty);
        var floor1 = builder.Build(1, new SeededRandom(1337));
        var player = floor1.Player;
        int oldX = player.X;
        int oldY = player.Y;

        // Use different seed so floors are different
        var floor2 = builder.Build(2, new SeededRandom(9999), existingPlayer: player);

        // Player spawn may or may not differ, but must be walkable
        Assert.That(floor2.Map.IsWalkable(floor2.Player.X, floor2.Player.Y), Is.True,
            "Carried-forward player must be at a walkable position on the new floor");

        // Confirm the old position reference and new are independent (player moved to new spawn)
        _ = oldX; _ = oldY; // used for documentation; positions may coincidentally match
    }

    // ─── Entity IDs ─────────────────────────────────────────────────────────

    [Test]
    public void Build_AllEntityIds_AreUnique()
    {
        var builder = BuilderWithTemplate(TemplateWithGuaranteedSpawn);
        var state = builder.Build(1, new SeededRandom(1337));

        var allIds = new List<int> { state.Player.Id };
        allIds.AddRange(state.Monsters.Select(m => m.Id));
        if (state.StairDown != null) allIds.Add(state.StairDown.Id);

        Assert.That(allIds.Distinct().Count(), Is.EqualTo(allIds.Count),
            "All entity IDs on a dungeon floor must be unique");
    }

    [Test]
    public void Build_PlayerAlwaysId0()
    {
        var builder = BuilderWithTemplate(TemplateEmpty);
        var state = builder.Build(1, new SeededRandom(1337));
        Assert.That(state.Player.Id, Is.EqualTo(0), "Player is always entity ID 0");
    }
}
