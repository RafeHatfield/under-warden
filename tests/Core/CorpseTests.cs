using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for Phase 1 of the corpse system:
/// death → corpse transformation, dual membership, component stripping,
/// leaves_corpse flag, floor descent cleanup, and raised-zombie lineage.
/// </summary>
[TestFixture]
public class CorpseTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string FindEntitiesYaml()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var path = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", "entities.yaml"));
        if (File.Exists(path)) return path;
        path = Path.GetFullPath(Path.Combine(testDir, "config", "entities.yaml"));
        if (File.Exists(path)) return path;
        throw new FileNotFoundException($"entities.yaml not found. Tried: {path}");
    }

    private static MonsterFactory CreateFactory()
    {
        var loader = new ContentLoader();
        var bundle = loader.LoadAllFromFile(FindEntitiesYaml());
        var entityFactory = new EntityFactory();
        var itemFactory = new ItemFactory(bundle.Items, entityFactory);
        return new MonsterFactory(bundle.Monsters, entityFactory, itemFactory);
    }

    /// <summary>
    /// Creates a basic orc entity without using the factory (avoids YAML dependency).
    /// Low dexterity (AC = 7) and 1 HP so any hit kills it.
    /// </summary>
    private static Entity MakeOrc(int id = 1)
    {
        var orc = new Entity(id, "Orc", 6, 10, blocksMovement: true);
        // dexterity: 4 → BaseArmorClass = 7 (easy to hit). HP: 1 so any hit kills.
        orc.Add(new Fighter(hp: 1, strength: 10, dexterity: 4, constitution: 10,
            accuracy: 3, evasion: 1, damageMin: 2, damageMax: 4));
        orc.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        orc.Add(new SpeciesTag("orc"));
        return orc;
    }

    /// <summary>
    /// Creates a state where the player one-shots the given monster.
    /// Player has dexterity 18 (toHitBonus = 4; hits on d20 >= 3) and high damage.
    /// </summary>
    private static (GameState state, Entity monster) CreateKillState(Entity monster, int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(20, 20);

        var player = new Entity(0, "Player", 5, 10, blocksMovement: true);
        // dexterity: 18 → DexMod = 4, toHitBonus = 4. Monster AC ≤ 10. Hits on d20 >= 6 (75%).
        // damageMin/Max: 20 kills any 1-HP monster guaranteed.
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 18, constitution: 12,
            accuracy: 5, evasion: 1, damageMin: 20, damageMax: 20));
        map.RegisterEntity(player);

        monster.X = 6;
        monster.Y = 10;
        map.RegisterEntity(monster);

        var state = new GameState(player, new List<Entity> { monster }, map, rng);
        return (state, monster);
    }

    private static TurnResult KillMonster(GameState state, Entity monster, MonsterFactory? factory = null)
        => TurnController.ProcessTurn(state, PlayerAction.Attack(monster), factory);

    // ─── TASK-006 Tests ───────────────────────────────────────────────────────

    [Test]
    public void MonsterDeath_CreatesCorpseWithFreshState()
    {
        var orc = MakeOrc();
        var (state, monster) = CreateKillState(orc);

        KillMonster(state, monster);

        var corpse = monster.Get<CorpseComponent>();
        Assert.That(corpse, Is.Not.Null, "CorpseComponent should be added on death");
        Assert.That(corpse!.State, Is.EqualTo(CorpseState.Fresh));
    }

    [Test]
    public void MonsterDeath_CorpseIsNonBlocking()
    {
        var orc = MakeOrc();
        var (state, monster) = CreateKillState(orc);

        KillMonster(state, monster);

        Assert.That(monster.BlocksMovement, Is.False, "Corpse should not block movement");
    }

    [Test]
    public void MonsterDeath_CorpseStripsComponents()
    {
        var orc = MakeOrc();
        var (state, monster) = CreateKillState(orc);

        KillMonster(state, monster);

        Assert.That(monster.Has<Fighter>(), Is.False, "Fighter should be stripped");
        Assert.That(monster.Has<AiComponent>(), Is.False, "AiComponent should be stripped");
    }

    [Test]
    public void MonsterDeath_CorpsePreservesSpeciesTag()
    {
        var orc = MakeOrc();
        var (state, monster) = CreateKillState(orc);

        KillMonster(state, monster);

        var tag = monster.Get<SpeciesTag>();
        Assert.That(tag, Is.Not.Null, "SpeciesTag should be preserved after death");
        Assert.That(tag!.TypeId, Is.EqualTo("orc"));
    }

    [Test]
    public void MonsterDeath_CorpsePreservesPosition()
    {
        var orc = MakeOrc();
        var (state, monster) = CreateKillState(orc);
        int expectedX = monster.X;
        int expectedY = monster.Y;

        KillMonster(state, monster);

        Assert.That(monster.X, Is.EqualTo(expectedX), "Corpse X should match death position");
        Assert.That(monster.Y, Is.EqualTo(expectedY), "Corpse Y should match death position");
    }

    [Test]
    public void MonsterDeath_CorpseTracksOriginalMonsterId()
    {
        var orc = MakeOrc();
        var (state, monster) = CreateKillState(orc);

        KillMonster(state, monster);

        var corpse = monster.Get<CorpseComponent>();
        Assert.That(corpse!.OriginalMonsterId, Is.EqualTo("orc"));
    }

    [Test]
    public void MonsterDeath_CorpseTracksDeathTurn()
    {
        var orc = MakeOrc();
        var (state, monster) = CreateKillState(orc);

        KillMonster(state, monster);

        var corpse = monster.Get<CorpseComponent>();
        Assert.That(corpse!.DeathTurn, Is.EqualTo(state.TurnCount),
            "DeathTurn should match TurnCount at time of death");
    }

    [Test]
    public void MonsterDeath_CorpseIdFormat()
    {
        var orc = MakeOrc();
        var (state, monster) = CreateKillState(orc);

        KillMonster(state, monster);

        var corpse = monster.Get<CorpseComponent>();
        string expected = $"corpse_{monster.X}_{monster.Y}_{state.TurnCount}";
        Assert.That(corpse!.CorpseId, Is.EqualTo(expected));
    }

    [Test]
    public void MonsterDeath_CorpseInStateCorpsesList()
    {
        var orc = MakeOrc();
        var (state, monster) = CreateKillState(orc);

        KillMonster(state, monster);

        Assert.That(state.Corpses, Contains.Item(monster),
            "Dead monster should be added to state.Corpses");
    }

    [Test]
    public void MonsterDeath_CorpseStillInMonstersList()
    {
        var orc = MakeOrc();
        var (state, monster) = CreateKillState(orc);

        KillMonster(state, monster);

        Assert.That(state.Monsters, Contains.Item(monster),
            "Dead monster should remain in state.Monsters (dual membership)");
    }

    [Test]
    public void MonsterDeath_EmitsCorpseCreatedEvent()
    {
        var orc = MakeOrc();
        var (state, monster) = CreateKillState(orc);

        var result = KillMonster(state, monster);

        var evt = result.Events.OfType<CorpseCreatedEvent>().SingleOrDefault();
        Assert.That(evt, Is.Not.Null, "CorpseCreatedEvent should be emitted");
        Assert.That(evt!.CorpseEntityId, Is.EqualTo(monster.Id));
        Assert.That(evt.OriginalMonsterId, Is.EqualTo("orc"));
    }

    [Test]
    public void RaisedZombieDeath_CreatesSPENTCorpse()
    {
        // An entity with RaisedFromCorpseTag that dies should become a SPENT corpse
        var orc = MakeOrc();
        orc.Add(new RaisedFromCorpseTag { CorpseId = "corpse_5_5_1" });
        var (state, monster) = CreateKillState(orc);

        KillMonster(state, monster);

        var corpse = monster.Get<CorpseComponent>();
        Assert.That(corpse, Is.Not.Null);
        Assert.That(corpse!.State, Is.EqualTo(CorpseState.Spent),
            "Previously-raised entity should become SPENT corpse, not FRESH");
    }

    [Test]
    public void FloorDescent_ClearsCorpses()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(20, 20);

        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 100, strength: 10, dexterity: 10, constitution: 10));
        map.RegisterEntity(player);

        var stair = new Entity(99, "Stair Down", 5, 5, blocksMovement: false);
        stair.Add(new Stair(isDown: true, targetDepth: 2));
        map.RegisterEntity(stair);

        var state = new GameState(player, new List<Entity>(), map, rng)
        {
            IsDungeonMode = true,
            CurrentDepth = 1,
            StairDown = stair,
        };

        // Add a fake corpse to the list
        var fakeCorpse = new Entity(1, "Dead Orc", 3, 3);
        fakeCorpse.Add(new CorpseComponent { OriginalMonsterId = "orc" });
        state.Corpses.Add(fakeCorpse);
        Assert.That(state.Corpses.Count, Is.EqualTo(1), "Pre-condition: corpse in list");

        TurnController.ProcessTurn(state, PlayerAction.Descend);

        Assert.That(state.Corpses.Count, Is.EqualTo(0),
            "state.Corpses should be cleared on floor descent");
    }

    [Test]
    public void PlayerDeath_NoCorpseCreated()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(20, 20);

        // Player with 1 HP; monster with guaranteed hit and high damage
        var player = new Entity(0, "Player", 5, 10, blocksMovement: true);
        player.Add(new Fighter(hp: 1, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 1, evasion: 99, damageMin: 0, damageMax: 0));

        var monster = new Entity(1, "Orc", 6, 10, blocksMovement: true);
        monster.Add(new Fighter(hp: 100, strength: 14, dexterity: 12, constitution: 10,
            accuracy: 99, evasion: 0, damageMin: 10, damageMax: 10));
        monster.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        monster.Add(new SpeciesTag("orc"));
        map.RegisterEntity(player);
        map.RegisterEntity(monster);

        var state = new GameState(player, new List<Entity> { monster }, map, rng);

        TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(player.Has<CorpseComponent>(), Is.False, "Player should never get a CorpseComponent");
    }

    [Test]
    public void SlimeDeath_NoCorpseCreated()
    {
        // Slimes have leaves_corpse: false in YAML — should not create a corpse
        var factory = CreateFactory();
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(20, 20);

        var slime = factory.Create("slime", x: 6, y: 10)!;
        Assert.That(slime, Is.Not.Null, "slime must be in entities.yaml");

        var player = new Entity(0, "Player", 5, 10, blocksMovement: true);
        int slimeHp = slime.Require<Fighter>().MaxHp;
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 99, evasion: 1, damageMin: slimeHp, damageMax: slimeHp));
        map.RegisterEntity(player);
        map.RegisterEntity(slime);

        var state = new GameState(player, new List<Entity> { slime }, map, rng);

        TurnController.ProcessTurn(state, PlayerAction.Attack(slime), factory);

        Assert.That(slime.Has<CorpseComponent>(), Is.False,
            "Slime with leaves_corpse: false should not get a CorpseComponent");
        Assert.That(state.Corpses.Count, Is.EqualTo(0),
            "Slime death should not add to state.Corpses");
    }

    // ─── CorpseComponent unit tests ──────────────────────────────────────────

    [Test]
    public void CanBeRaised_ReturnsTrueForFreshUnraisedCorpse()
    {
        var corpse = new CorpseComponent { State = CorpseState.Fresh, RaiseCount = 0, MaxRaises = 1 };
        Assert.That(corpse.CanBeRaised, Is.True);
    }

    [Test]
    public void CanBeRaised_ReturnsFalseForSpentCorpse()
    {
        var corpse = new CorpseComponent { State = CorpseState.Spent, RaiseCount = 0, MaxRaises = 1 };
        Assert.That(corpse.CanBeRaised, Is.False);
    }

    [Test]
    public void CanBeRaised_ReturnsFalseWhenMaxRaisesReached()
    {
        var corpse = new CorpseComponent { State = CorpseState.Fresh, RaiseCount = 1, MaxRaises = 1 };
        Assert.That(corpse.CanBeRaised, Is.False);
    }
}
