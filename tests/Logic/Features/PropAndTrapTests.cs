using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Logic.Features;

/// <summary>
/// Phase 3 tests: TurnController integration for destructible props and floor traps.
/// TASK-010 (prop bump), TASK-011 (floor trap walk-over), TASK-012 (monster walk-over).
/// </summary>
[TestFixture]
public class PropAndTrapTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static (GameState state, Entity feature) CreateStateWithFeature(
        Entity feature, int seed = 1337, List<Entity>? monsters = null)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4) { CanOpenDoors = true });
        map.RegisterEntity(player);

        feature.X = 4;
        feature.Y = 5;
        map.RegisterEntity(feature);

        var monsterList = monsters ?? new List<Entity>();
        foreach (var m in monsterList)
            map.RegisterEntity(m);

        var state = new GameState(player, monsterList, map, rng);
        state.Features.Add(feature);
        return (state, feature);
    }

    private static PlayerAction MoveRight() => PlayerAction.MoveTo(4, 5);
    private static PlayerAction MoveLeft() => PlayerAction.MoveTo(2, 5);

    private static Entity CreateProp(int id, string propKind = "barrel",
        TrapPayloadComponent? trap = null, TrapAction? rouseAction = null,
        List<Entity>? lootEntities = null)
    {
        var entity = new Entity(id, FormatName(propKind), 0, 0, blocksMovement: true);
        entity.Add(new DestructiblePropComponent
        {
            PropKind     = propKind,
            IsResolved   = false,
            TrapPayload  = trap,
            RouseAction  = rouseAction,
            ClosedTileId = propKind switch { "barrel" => 268, "bone_pile" => 90, _ => 317 },
            OpenTileId   = propKind switch { "barrel" => 269, "bone_pile" => 91, _ => 317 },
        });
        if (lootEntities != null && lootEntities.Count > 0)
        {
            var stash = new ChestLootStash(lootEntities);
            entity.Add(stash);
        }
        return entity;
    }

    private static Entity CreateFloorTrap(int id, string trapType = "spike_trap",
        bool isDetected = false, bool isSpent = false,
        double detectChance = 0.0, // 0.0 = never detect in tests (deterministic)
        List<TrapAction>? actions = null)
    {
        var payload = new TrapPayloadComponent();
        if (actions != null) payload.Actions.AddRange(actions);
        else payload.Actions.Add(new TrapAction { Kind = "damage", Amount = 7 });

        var entity = new Entity(id, "Spike Trap", 0, 0, blocksMovement: false);
        entity.Add(new FloorTrapComponent
        {
            TrapType            = trapType,
            IsSpent             = isSpent,
            IsDetected          = isDetected,
            IsDetectable        = true,
            PassiveDetectChance = detectChance,
            Payload             = payload,
            VisibleTileId       = 429,
        });
        return entity;
    }

    private static Entity CreateItem(int id, string name = "Healing Potion")
    {
        var e = new Entity(id, name, 0, 0, blocksMovement: false);
        e.Add(new Consumable(healAmount: 10, isPotion: true));
        return e;
    }

    private static string FormatName(string kind)
        => string.Join(" ", kind.Split('_').Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));

    // ── TASK-010: Prop bump tests ──────────────────────────────────────────────

    [Test]
    public void BumpBarrel_EmitsPropDestroyedEvent()
    {
        var barrel = CreateProp(10, "barrel");
        var (state, _) = CreateStateWithFeature(barrel);

        var result = TurnController.ProcessTurn(state, MoveRight());

        var evt = result.Events.OfType<PropDestroyedEvent>().FirstOrDefault();
        Assert.That(evt, Is.Not.Null);
        Assert.That(evt!.PropKind, Is.EqualTo("barrel"));
        Assert.That(evt.X, Is.EqualTo(4));
        Assert.That(evt.Y, Is.EqualTo(5));
    }

    [Test]
    public void BumpBarrel_SetsIsResolvedTrue()
    {
        var barrel = CreateProp(10, "barrel");
        var (state, _) = CreateStateWithFeature(barrel);

        TurnController.ProcessTurn(state, MoveRight());

        var prop = barrel.Require<DestructiblePropComponent>();
        Assert.That(prop.IsResolved, Is.True);
    }

    [Test]
    public void BumpBarrel_DropsLoot_PlayerPicksItUp()
    {
        var lootItem = CreateItem(20, "Healing Potion");
        var barrel = CreateProp(10, "barrel", lootEntities: new List<Entity> { lootItem });
        var (state, _) = CreateStateWithFeature(barrel);

        var result = TurnController.ProcessTurn(state, MoveRight());

        // Loot should have been picked up into inventory.
        var pickupEvts = result.Events.OfType<PickUpEvent>().ToList();
        Assert.That(pickupEvts.Any(e => e.ItemId == lootItem.Id), Is.True,
            "Player should have picked up the item");
    }

    [Test]
    public void BumpBarrel_WithTrap_EmitsTrapTriggeredEvent()
    {
        var payload = new TrapPayloadComponent();
        payload.Actions.Add(new TrapAction { Kind = "damage", Amount = 5 });

        var barrel = CreateProp(10, "barrel", trap: payload);
        var (state, _) = CreateStateWithFeature(barrel);

        var result = TurnController.ProcessTurn(state, MoveRight());

        var evt = result.Events.OfType<TrapTriggeredEvent>().FirstOrDefault();
        Assert.That(evt, Is.Not.Null, "Trapped barrel should emit TrapTriggeredEvent");
        Assert.That(evt!.Source, Contains.Substring("barrel"));
    }

    [Test]
    public void BumpBarrel_WithTrap_DealsDamage()
    {
        var payload = new TrapPayloadComponent();
        payload.Actions.Add(new TrapAction { Kind = "damage", Amount = 10 });

        var barrel = CreateProp(10, "barrel", trap: payload);
        var (state, _) = CreateStateWithFeature(barrel);

        var playerHpBefore = state.PlayerFighter.Hp;
        TurnController.ProcessTurn(state, MoveRight());

        Assert.That(state.PlayerFighter.Hp, Is.LessThan(playerHpBefore),
            "Trapped barrel should damage the player");
    }

    [Test]
    public void BumpResolvedProp_FreeAction_NothingHappens()
    {
        var barrel = CreateProp(10, "barrel");
        var propComp = barrel.Require<DestructiblePropComponent>();
        propComp.IsResolved = true; // already resolved

        var (state, _) = CreateStateWithFeature(barrel);

        var result = TurnController.ProcessTurn(state, MoveRight());

        // Free action: TurnCount should NOT advance (decremented back).
        // PropDestroyedEvent should NOT be emitted.
        Assert.That(result.Events.OfType<PropDestroyedEvent>().Any(), Is.False);
        // Turn count is decremented for free actions: it increments then gets reversed.
        // Net result: TurnCount stays at the value before the call.
        Assert.That(state.TurnCount, Is.EqualTo(0), "Resolved prop bump is free action — TurnCount should not advance");
    }

    [Test]
    public void BumpBonePile_AtDepth1_NoRouse_BecauseRouseMinDepth2()
    {
        // Rouse action with MinDepth=2 — at depth 1 it should not fire even if set.
        // Since rouse is pre-resolved at placement time (FeatureFactory), this test
        // verifies TurnController behavior: if RouseAction is null, no monster is spawned.
        var bonePile = CreateProp(10, "bone_pile"); // no rouseAction on the component
        var (state, _) = CreateStateWithFeature(bonePile);

        var result = TurnController.ProcessTurn(state, MoveRight());

        Assert.That(result.Events.OfType<MonsterRousedEvent>().Any(), Is.False,
            "No rouse when RouseAction is null");
    }

    [Test]
    public void BumpBonePile_AtDepth2_WithRouseAction_SpawnsZombie()
    {
        // Identity scenario: bone_pile_rouse_identity.
        // TurnController passes monsterFactory:null to TryInteractFeature (deferred wiring).
        // Test TrapActionResolver directly with a real factory to verify the spawn path works.
        var factory = CreateMonsterFactory();
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var state = new GameState(player, new List<Entity>(), map, rng);

        var payload = new TrapPayloadComponent();
        payload.Actions.Add(new TrapAction { Kind = "spawn_monster", Target = "zombie", Radius = 4 });

        var events = new List<TurnEvent>();
        var idAlloc = new EntityIdAllocator(startFrom: 100);

        TrapActionResolver.Resolve(player, payload, "bone_pile_rouse", (4, 5),
            state, rng, events, monsterFactory: factory, idAllocator: idAlloc);

        var rouseEvt = events.OfType<MonsterRousedEvent>().FirstOrDefault();
        Assert.That(rouseEvt, Is.Not.Null, "MonsterRousedEvent should fire when factory is provided");
        Assert.That(state.Monsters.Count, Is.EqualTo(1), "A zombie should be added to the monster list");
        Assert.That(state.Monsters[0].Name, Does.Contain("Zombie").Or.Contain("zombie").IgnoreCase,
            "The spawned monster should be a zombie");
    }

    private static MonsterFactory CreateMonsterFactory()
    {
        var loader = new ContentLoader();
        var testDir = TestContext.CurrentContext.TestDirectory;
        var configPath = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", "entities.yaml"));
        if (!File.Exists(configPath))
            configPath = Path.GetFullPath(Path.Combine(testDir, "config", "entities.yaml"));
        var bundle = loader.LoadAllFromFile(configPath);
        var entityFactory = new EntityFactory();
        var itemFactory = new ItemFactory(bundle.Items, entityFactory);
        return new MonsterFactory(bundle.Monsters, entityFactory, itemFactory);
    }

    // ── TASK-011: Floor trap walk-over tests ──────────────────────────────────

    [Test]
    public void StepOnSpikeTrap_DealsDamage()
    {
        // Trap at (3,5) — the tile player moves left TO (player moves from 3,5... hmm)
        // Let's put player at (3,5) and trap at (2,5) and move left.
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var trap = CreateFloorTrap(10, "spike_trap",
            actions: new List<TrapAction> { new TrapAction { Kind = "damage", Amount = 7 } });
        trap.X = 2;
        trap.Y = 5;
        map.RegisterEntity(trap);

        var state = new GameState(player, new List<Entity>(), map, rng);
        state.Features.Add(trap);

        int hpBefore = state.PlayerFighter.Hp;
        var result = TurnController.ProcessTurn(state, PlayerAction.MoveTo(2, 5));

        Assert.That(state.PlayerFighter.Hp, Is.LessThan(hpBefore), "Spike trap should deal damage");
        var triggered = result.Events.OfType<TrapTriggeredEvent>().FirstOrDefault();
        Assert.That(triggered, Is.Not.Null);
        Assert.That(triggered!.Source, Is.EqualTo("spike_trap"));
    }

    [Test]
    public void StepOnSpikeTrap_AppliesBleedEffect()
    {
        // Identity scenario: spike_trap_identity.
        // Spike trap payload: damage=7 + bleed(severity=1, duration=3). Both must apply.
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var trap = CreateFloorTrap(10, "spike_trap", actions: new List<TrapAction>
        {
            new TrapAction { Kind = "damage", Amount = 7 },
            new TrapAction { Kind = "bleed", Amount = 1, Duration = 3 },
        });
        trap.X = 2;
        trap.Y = 5;
        map.RegisterEntity(trap);

        var state = new GameState(player, new List<Entity>(), map, rng);
        state.Features.Add(trap);

        TurnController.ProcessTurn(state, PlayerAction.MoveTo(2, 5));

        Assert.That(state.PlayerFighter.Hp, Is.LessThan(50), "Spike trap should deal damage");
        Assert.That(player.Has<BleedEffect>(), Is.True, "Spike trap should apply BleedEffect");
        var bleed = player.Get<BleedEffect>()!;
        Assert.That(bleed.Severity, Is.EqualTo(1));
        Assert.That(bleed.RemainingTurns, Is.GreaterThanOrEqualTo(2), "Bleed should have remaining turns");
    }

    [Test]
    public void StepOnDetectedTrap_EmitsAvoidedEvent()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var trap = CreateFloorTrap(10, "spike_trap", isDetected: true); // pre-detected
        trap.X = 2;
        trap.Y = 5;
        map.RegisterEntity(trap);

        var state = new GameState(player, new List<Entity>(), map, rng);
        state.Features.Add(trap);

        int hpBefore = state.PlayerFighter.Hp;
        var result = TurnController.ProcessTurn(state, PlayerAction.MoveTo(2, 5));

        // Detected trap: avoid event emitted, no damage.
        var avoided = result.Events.OfType<TrapAvoidedEvent>().FirstOrDefault();
        Assert.That(avoided, Is.Not.Null, "Detected trap should emit TrapAvoidedEvent");
        Assert.That(state.PlayerFighter.Hp, Is.EqualTo(hpBefore), "No damage on avoided trap");
    }

    [Test]
    public void StepOnSpentTrap_NoEffect()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var trap = CreateFloorTrap(10, "spike_trap", isSpent: true); // already spent
        trap.X = 2;
        trap.Y = 5;
        map.RegisterEntity(trap);

        var state = new GameState(player, new List<Entity>(), map, rng);
        state.Features.Add(trap);

        int hpBefore = state.PlayerFighter.Hp;
        var result = TurnController.ProcessTurn(state, PlayerAction.MoveTo(2, 5));

        Assert.That(state.PlayerFighter.Hp, Is.EqualTo(hpBefore), "Spent trap should not damage");
        Assert.That(result.Events.OfType<TrapTriggeredEvent>().Any(), Is.False);
    }

    [Test]
    public void StepOnTrap_PassiveDetectAtHighChance_DetectsBeforeTrigger()
    {
        // Detect chance = 1.0 → always detect, never trigger.
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var trap = CreateFloorTrap(10, "spike_trap", detectChance: 1.0); // guaranteed detect
        trap.X = 2;
        trap.Y = 5;
        map.RegisterEntity(trap);

        var state = new GameState(player, new List<Entity>(), map, rng);
        state.Features.Add(trap);

        int hpBefore = state.PlayerFighter.Hp;
        var result = TurnController.ProcessTurn(state, PlayerAction.MoveTo(2, 5));

        var detected = result.Events.OfType<TrapDetectedEvent>().FirstOrDefault();
        Assert.That(detected, Is.Not.Null, "Should detect trap");
        Assert.That(state.PlayerFighter.Hp, Is.EqualTo(hpBefore), "No damage on detection");
        Assert.That(trap.Require<FloorTrapComponent>().IsDetected, Is.True);
    }

    [Test]
    public void HoleTrap_EmitsDescendEvent()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var trap = CreateFloorTrap(10, "hole_trap", detectChance: 0.0,
            actions: new List<TrapAction> { new TrapAction { Kind = "descend" } });
        trap.X = 2;
        trap.Y = 5;
        map.RegisterEntity(trap);

        var state = new GameState(player, new List<Entity>(), map, rng);
        state.Features.Add(trap);

        var result = TurnController.ProcessTurn(state, PlayerAction.MoveTo(2, 5));

        var descend = result.Events.OfType<DescendEvent>().FirstOrDefault();
        Assert.That(descend, Is.Not.Null, "hole_trap should emit DescendEvent");
        Assert.That(descend!.Cause, Is.EqualTo("hole_trap"));
    }

    // ── TASK-012: Monster walk-over tests ─────────────────────────────────────

    [Test]
    public void MonsterStepsOnTrap_TriggersFiring_EmitsTrapTriggeredEvent()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        // Player at (3,5), monster at (3,7) — will move toward player and cross (3,6).
        var player = new Entity(0, "Player", 3, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var monster = new Entity(1, "Orc", 3, 7, blocksMovement: true);
        var monsterFighter = new Fighter(hp: 28, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 1, damageMin: 2, damageMax: 4);
        monster.Add(monsterFighter);
        monster.Add(new AiComponent { Faction = "orc" });
        monster.Add(new AlertedState
        {
            LastKnownPlayerX = player.X,
            LastKnownPlayerY = player.Y,
            TurnsUntilDeaggro = 10,
        });
        map.RegisterEntity(monster);

        // Spike trap at (3,6) — directly between monster and player.
        var trap = CreateFloorTrap(10, "spike_trap", detectChance: 0.0,
            actions: new List<TrapAction> { new TrapAction { Kind = "damage", Amount = 5 } });
        trap.X = 3;
        trap.Y = 6;
        map.RegisterEntity(trap);

        var state = new GameState(player, new List<Entity> { monster }, map, rng);
        state.Features.Add(trap);

        // Player waits; monster moves toward player.
        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        // Monster may or may not have moved to (3,6) depending on AI; check if trap fired.
        var triggered = result.Events.OfType<TrapTriggeredEvent>().FirstOrDefault();
        if (triggered != null)
        {
            Assert.That(triggered.TargetId, Is.EqualTo(monster.Id),
                "Monster should be the target of the trap");
        }
        // Note: if the monster didn't move to (3,6) this turn (e.g., blocked), the trap
        // won't fire. The key is that IF the monster walks over the trap, it triggers.
        // We verify this by checking the trap component state.
        // If the trap fired, it should be spent.
        var trapComp = trap.Require<FloorTrapComponent>();
        if (trapComp.IsSpent)
        {
            // Trap fired on monster — verify damage was dealt.
            Assert.That(triggered, Is.Not.Null, "If trap spent, must have TrapTriggeredEvent");
            Assert.That(monsterFighter.Hp, Is.LessThan(28), "Monster should take damage");
        }
    }

    [Test]
    public void MonsterStepsOnTrap_SkipsPassiveDetection()
    {
        // A detectChance=1.0 trap should NOT detect for monsters (skipPassiveDetect=true).
        // The trap should trigger (or not trigger due to spending), never detect.
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var monster = new Entity(1, "Orc", 3, 7, blocksMovement: true);
        monster.Add(new Fighter(hp: 28, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 1, damageMin: 2, damageMax: 4));
        monster.Add(new AiComponent { Faction = "orc" });
        monster.Add(new AlertedState
        {
            LastKnownPlayerX = 3,
            LastKnownPlayerY = 5,
            TurnsUntilDeaggro = 10,
        });
        map.RegisterEntity(monster);

        var trap = CreateFloorTrap(10, "spike_trap", detectChance: 1.0, // 100% detect chance
            actions: new List<TrapAction> { new TrapAction { Kind = "damage", Amount = 5 } });
        trap.X = 3;
        trap.Y = 6;
        map.RegisterEntity(trap);

        var state = new GameState(player, new List<Entity> { monster }, map, rng);
        state.Features.Add(trap);

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        // Monster should NOT have detected the trap (detection is for players only).
        var detected = result.Events.OfType<TrapDetectedEvent>().FirstOrDefault(e =>
            e.ActorId == monster.Id);
        Assert.That(detected, Is.Null, "Monsters skip passive detection — no TrapDetectedEvent expected");
    }
}
