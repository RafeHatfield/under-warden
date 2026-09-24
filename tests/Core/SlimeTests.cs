using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for slime-specific mechanics: split-under-pressure and corrosion.
/// All tests use scenario-mode maps (IsDungeonMode=false).
/// </summary>
[TestFixture]
public class SlimeTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Create an arena state with a player adjacent to a large_slime-style monster.
    /// Player has guaranteed-hit setup (accuracy=99).
    /// Monster is created with MaxHp=40, then HP is set to currentHp to simulate prior damage.
    /// </summary>
    private static (GameState state, Entity monster) CreateSplitState(
        int monsterMaxHp = 40, int monsterCurrentHp = 40,
        int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(20, 20);

        var player = new Entity(0, "Player", 5, 10, blocksMovement: true);
        // High accuracy — guaranteed to hit; small damage (1) so hit doesn't kill monster
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 99, evasion: 1, damageMin: 1, damageMax: 1));
        map.RegisterEntity(player);

        var monster = new Entity(1, "Large Slime", 6, 10, blocksMovement: true);
        // Fighter is created with MaxHp = monsterMaxHp; then we set Hp to simulate prior damage
        var fighter = new Fighter(hp: monsterMaxHp, strength: 12, dexterity: 6, constitution: 10,
            accuracy: 3, evasion: 0, damageMin: 2, damageMax: 5);
        // Reduce HP to monsterCurrentHp to simulate damage already taken before this attack
        fighter.Hp = monsterCurrentHp;
        monster.Add(fighter);
        map.RegisterEntity(monster);

        var monsters = new List<Entity> { monster };
        var state = new GameState(player, monsters, map, rng);
        return (state, monster);
    }

    /// <summary>Create a minimal MonsterFactory loaded from real YAML config.</summary>
    private static MonsterFactory CreateFactory()
    {
        string entitiesPath = FindEntitiesYaml();
        var loader = new ContentLoader();
        var bundle = loader.LoadAllFromFile(entitiesPath);

        var entityFactory = new EntityFactory();
        var itemFactory = new ItemFactory(bundle.Items, entityFactory);
        return new MonsterFactory(bundle.Monsters, entityFactory, itemFactory);
    }

    private static string FindEntitiesYaml()
    {
        // Standard test runner pattern — TestDirectory is tests/bin/Debug/net8.0
        var testDir = TestContext.CurrentContext.TestDirectory;
        var path = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", "entities.yaml"));
        if (File.Exists(path)) return path;

        // Fallback for unusual working directories
        path = Path.GetFullPath(Path.Combine(testDir, "config", "entities.yaml"));
        if (File.Exists(path)) return path;

        throw new FileNotFoundException($"entities.yaml not found. Tried: {path}");
    }

    /// <summary>
    /// Attach a SplitTracker directly (no factory needed for non-spawn tests).
    /// </summary>
    private static void AttachSplitTracker(Entity monster,
        double triggerPct = 0.40, string childType = "slime",
        int minChildren = 2, int maxChildren = 3,
        int[]? weights = null)
    {
        monster.Add(new SplitTracker(triggerPct, childType, minChildren, maxChildren, weights));
    }

    /// <summary>
    /// Give the player a metal weapon with a known BaseDamageMax.
    /// </summary>
    private static Entity GivePlayerMetalWeapon(GameState state, int dmgMax = 4)
    {
        var weapon = new Entity(50, "Dagger", 0, 0);
        var eq = new Equippable(EquipmentSlot.MainHand)
        {
            DamageMin = 1,
            DamageMax = dmgMax,
            Material = "metal",
        };
        eq.SetBaseDamageMax();
        weapon.Add(eq);

        var equipment = state.Player.GetOrAdd<Equipment>();
        equipment.SetSlot(EquipmentSlot.MainHand, weapon);
        return weapon;
    }

    /// <summary>
    /// Give the player a wood weapon (immune to corrosion).
    /// </summary>
    private static Entity GivePlayerWoodWeapon(GameState state, int dmgMax = 6)
    {
        var weapon = new Entity(51, "Club", 0, 0);
        var eq = new Equippable(EquipmentSlot.MainHand)
        {
            DamageMin = 1,
            DamageMax = dmgMax,
            Material = "wood",
        };
        eq.SetBaseDamageMax();
        weapon.Add(eq);

        var equipment = state.Player.GetOrAdd<Equipment>();
        equipment.SetSlot(EquipmentSlot.MainHand, weapon);
        return weapon;
    }

    // ─── Split tests ──────────────────────────────────────────────────────────

    [Test]
    public void SlimeDoesNotSplitAboveHpThreshold()
    {
        // Monster at full HP (40/40 = 100%) — 40% threshold. No split should occur.
        // Run 20 attacks — monster HP will drop but starts above threshold each time.
        var (state, monster) = CreateSplitState(monsterMaxHp: 400, monsterCurrentHp: 400);
        AttachSplitTracker(monster, triggerPct: 0.40);

        // Run 20 attacks. Player deals 1 damage per hit; 20 hits brings monster to at most 380/400 = 95%.
        // 95% is still above the 40% threshold so split should never fire.
        for (int i = 0; i < 20; i++)
        {
            var r = TurnController.ProcessTurn(state, PlayerAction.Attack(monster));
            var splitEvents = r.Events.OfType<SplitEvent>().ToList();
            Assert.That(splitEvents, Is.Empty, $"No split should occur above threshold (turn {i+1})");
            if (!monster.Require<Fighter>().IsAlive) break; // shouldn't happen with HP=400, just safety
        }
    }

    [Test]
    public void SlimeSplitsAtHpThreshold_NullFactory_FallsBackToKill()
    {
        // Monster already at HP=1 (MaxHp=40 → 2.5%) — ANY hit triggers split.
        // With null factory → fallback kill (HP=0, DeathEvent), no children.
        // Retry up to 20 times to handle 5% fumble chance.
        var (state, monster) = CreateSplitState(monsterMaxHp: 40, monsterCurrentHp: 1);
        AttachSplitTracker(monster, triggerPct: 0.40);

        bool triggered = false;
        for (int i = 0; i < 20 && monster.Require<Fighter>().IsAlive; i++)
        {
            TurnController.ProcessTurn(state, PlayerAction.Attack(monster), monsterFactory: null);
            if (!monster.Require<Fighter>().IsAlive) { triggered = true; break; }
        }

        Assert.That(triggered, Is.True,
            "Original should be dead (null-factory fallback) within 20 attempts");
        Assert.That(state.Monsters.Count, Is.EqualTo(1),
            "No children spawned without factory");
    }

    [Test]
    public void SlimeSplitsAtHpThreshold_WithFactory_SpawnsChildren()
    {
        // Monster at HP=1 (MaxHp=40 → 2.5%) — below 40% threshold.
        // With a real factory, split fires on first hit and spawns 2-3 slime children.
        // Retry up to 20 times to handle 5% fumble.
        var (state, monster) = CreateSplitState(monsterMaxHp: 40, monsterCurrentHp: 1);

        AttachSplitTracker(monster, triggerPct: 0.40, childType: "slime",
            minChildren: 2, maxChildren: 3, weights: [40, 60]);

        var factory = CreateFactory();

        SplitEvent? splitEvent = null;
        for (int i = 0; i < 20 && splitEvent == null && monster.Require<Fighter>().IsAlive; i++)
        {
            var result = TurnController.ProcessTurn(state, PlayerAction.Attack(monster), factory);
            splitEvent = result.Events.OfType<SplitEvent>().FirstOrDefault();
        }

        Assert.That(splitEvent, Is.Not.Null, "SplitEvent should be emitted within 20 attempts");
        Assert.That(splitEvent!.ChildIds.Count, Is.InRange(2, 3),
            "2–3 children should spawn");
        Assert.That(monster.Require<Fighter>().IsAlive, Is.False,
            "Original should be removed from play (HP=0)");
        Assert.That(state.Monsters.Count, Is.EqualTo(1 + splitEvent.ChildIds.Count),
            "Children added to state.Monsters");
    }

    [Test]
    public void SlimeSplitsOnlyOnce()
    {
        // Monster at HP=1 — split fires on first hit.
        // HasSplit must be true afterward; split cannot fire a second time.
        var (state, monster) = CreateSplitState(monsterMaxHp: 40, monsterCurrentHp: 1);
        AttachSplitTracker(monster, triggerPct: 0.40, childType: "slime",
            minChildren: 2, maxChildren: 2, weights: null);

        var factory = CreateFactory();

        // Keep attacking until split fires (up to 20 turns)
        for (int i = 0; i < 20; i++)
        {
            var result = TurnController.ProcessTurn(state, PlayerAction.Attack(monster), factory);
            if (result.Events.OfType<SplitEvent>().Any()) break;
        }

        int afterFirstSplit = state.Monsters.Count;
        Assert.That(afterFirstSplit, Is.GreaterThan(1), "Children spawned after first split");

        // HasSplit must be true — prevents re-triggering
        Assert.That(monster.Get<SplitTracker>()?.HasSplit, Is.True,
            "HasSplit should be true after split fires");
    }

    [Test]
    public void SlimeDoesNotSplitWhenAlreadySplit()
    {
        // Pre-set HasSplit=true — split guard should prevent re-splitting
        var (state, monster) = CreateSplitState(monsterMaxHp: 40, monsterCurrentHp: 1);
        var tracker = new SplitTracker(0.40, "slime", 2, 3, null);
        tracker.HasSplit = true;
        monster.Add(tracker);

        var factory = CreateFactory();
        var action = PlayerAction.Attack(monster);
        var result = TurnController.ProcessTurn(state, action, factory);

        var splitEvents = result.Events.OfType<SplitEvent>().ToList();
        Assert.That(splitEvents, Is.Empty, "Split should not fire when HasSplit=true");
    }

    // ─── Corrosion tests ──────────────────────────────────────────────────────

    [Test]
    public void CorrosionDegradesMetal_WhenRollSucceeds()
    {
        // Slime with 100% corrosion chance will corrode on first hit.
        // MaxHit=0.95 so we retry up to 10 turns to guarantee a hit.
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(20, 20);

        var player = new Entity(0, "Player", 5, 10, blocksMovement: true);
        player.Add(new Fighter(hp: 200, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 1, evasion: 0, damageMin: 1, damageMax: 1));
        map.RegisterEntity(player);

        var slime = new Entity(1, "Slime", 6, 10, blocksMovement: true);
        slime.Add(new Fighter(hp: 200, strength: 8, dexterity: 6, constitution: 10,
            accuracy: 99, evasion: 0, damageMin: 1, damageMax: 1));
        slime.Add(new CorrosionComponent(chance: 1.0));
        map.RegisterEntity(slime);

        var state = new GameState(player, new List<Entity> { slime }, map, rng, turnLimit: 200);
        var weapon = GivePlayerMetalWeapon(state, dmgMax: 4);
        var equippable = weapon.Require<Equippable>();
        int dmgBefore = equippable.DamageMax;

        // Run turns until corrosion fires (guaranteed within a few turns at 95% hit rate)
        CorrosionEvent? corrEvt = null;
        for (int i = 0; i < 10 && corrEvt == null && state.PlayerFighter.IsAlive; i++)
        {
            var result = TurnController.ProcessTurn(state, PlayerAction.Wait);
            corrEvt = result.Events.OfType<CorrosionEvent>().FirstOrDefault();
        }

        Assert.That(corrEvt, Is.Not.Null, "CorrosionEvent should fire within 10 turns at 95% hit rate");
        Assert.That(equippable.DamageMax, Is.EqualTo(dmgBefore - 1), "DamageMax should decrease by 1");
        Assert.That(corrEvt!.NewDamageMax, Is.EqualTo(equippable.DamageMax));
        Assert.That(corrEvt.BaseDamageMax, Is.EqualTo(4));
    }

    [Test]
    public void CorrosionDoesNotAffectWoodWeapon()
    {
        // Run 20 turns — corrosion chance=1.0 but material=wood → no corrosion ever
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(20, 20);

        var player = new Entity(0, "Player", 5, 10, blocksMovement: true);
        player.Add(new Fighter(hp: 200, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 1, evasion: 0, damageMin: 1, damageMax: 1));
        map.RegisterEntity(player);

        var slime = new Entity(1, "Slime", 6, 10, blocksMovement: true);
        slime.Add(new Fighter(hp: 200, strength: 8, dexterity: 6, constitution: 10,
            accuracy: 99, evasion: 0, damageMin: 1, damageMax: 1));
        slime.Add(new CorrosionComponent(chance: 1.0));
        map.RegisterEntity(slime);

        var state = new GameState(player, new List<Entity> { slime }, map, rng, turnLimit: 200);
        var weapon = GivePlayerWoodWeapon(state, dmgMax: 6);
        var equippable = weapon.Require<Equippable>();
        int dmgBefore = equippable.DamageMax;

        // Run 20 turns — even with hits, wood should never corrode
        for (int i = 0; i < 20; i++)
            TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(equippable.DamageMax, Is.EqualTo(dmgBefore),
            "Wood weapon should never be corroded regardless of hits");
    }

    [Test]
    public void CorrosionFloorAt50Pct_StopsAtBaseDamageMaxHalf()
    {
        // BaseDamageMax=4, floor = Max(1, 4/2) = 2.
        // Corrosion should degrade 4 → 3 → 2, then stop.
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(20, 20);

        var player = new Entity(0, "Player", 5, 10, blocksMovement: true);
        player.Add(new Fighter(hp: 500, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 1, evasion: 0, damageMin: 1, damageMax: 1));
        map.RegisterEntity(player);

        var slime = new Entity(1, "Slime", 6, 10, blocksMovement: true);
        slime.Add(new Fighter(hp: 500, strength: 8, dexterity: 6, constitution: 10,
            accuracy: 99, evasion: 0, damageMin: 1, damageMax: 1));
        slime.Add(new CorrosionComponent(chance: 1.0));
        map.RegisterEntity(slime);

        var state = new GameState(player, new List<Entity> { slime }, map, rng, turnLimit: 500);
        var weapon = GivePlayerMetalWeapon(state, dmgMax: 4);
        var equippable = weapon.Require<Equippable>();

        // Run until DamageMax drops to 2 or we time out
        int corrodeCount = 0;
        for (int i = 0; i < 100 && corrodeCount < 3 && state.PlayerFighter.IsAlive; i++)
        {
            var result = TurnController.ProcessTurn(state, PlayerAction.Wait);
            if (result.Events.OfType<CorrosionEvent>().Any())
                corrodeCount++;
        }

        // After 2 corrosions: DamageMax should be 2 (= floor = Max(1, 4/2))
        Assert.That(equippable.DamageMax, Is.EqualTo(2),
            "DamageMax should stop at 50% of BaseDamageMax (= 2)");

        // Additional turns should not corrode further
        int stuckAt = equippable.DamageMax;
        for (int i = 0; i < 20 && state.PlayerFighter.IsAlive; i++)
        {
            var result = TurnController.ProcessTurn(state, PlayerAction.Wait);
            var extra = result.Events.OfType<CorrosionEvent>().FirstOrDefault();
            Assert.That(extra, Is.Null,
                "No further corrosion after reaching 50% floor");
        }
        Assert.That(equippable.DamageMax, Is.EqualTo(stuckAt),
            "DamageMax should not decrease below floor");
    }

    [Test]
    public void CorrosionDoesNotTriggerWhenNoWeaponEquipped()
    {
        // Slime hits player who has no weapon equipped — no CorrosionEvent
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(20, 20);

        var player = new Entity(0, "Player", 5, 10, blocksMovement: true);
        player.Add(new Fighter(hp: 200, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 1, evasion: 0, damageMin: 1, damageMax: 1));
        map.RegisterEntity(player);

        var slime = new Entity(1, "Slime", 6, 10, blocksMovement: true);
        slime.Add(new Fighter(hp: 200, strength: 8, dexterity: 6, constitution: 10,
            accuracy: 99, evasion: 0, damageMin: 1, damageMax: 1));
        slime.Add(new CorrosionComponent(chance: 1.0));
        map.RegisterEntity(slime);

        // Player has no equipment — corrosion silently does nothing
        var state = new GameState(player, new List<Entity> { slime }, map, rng, turnLimit: 200);

        for (int i = 0; i < 10; i++)
        {
            var result = TurnController.ProcessTurn(state, PlayerAction.Wait);
            var corrEvents = result.Events.OfType<CorrosionEvent>().ToList();
            Assert.That(corrEvents, Is.Empty, "No CorrosionEvent when no weapon equipped");
        }
    }

    // ─── YAML / Content loading tests ────────────────────────────────────────

    [Test]
    public void SlimeDefinition_LoadsFromYaml()
    {
        var factory = CreateFactory();
        var slime = factory.Create("slime", x: 5, y: 5, depth: 2, rng: new SeededRandom(1337));

        Assert.That(slime, Is.Not.Null, "slime must be in entities.yaml");
        Assert.That(slime!.Name, Is.EqualTo("Slime"));
        Assert.That(slime.Get<CorrosionComponent>()?.Chance, Is.EqualTo(0.05).Within(0.001));
        Assert.That(slime.Get<SplitTracker>(), Is.Null, "slime should not have SplitTracker");
    }

    [Test]
    public void LargeSlimeDefinition_LoadsFromYaml_WithSplitAndCorrosion()
    {
        var factory = CreateFactory();
        var large = factory.Create("large_slime", x: 5, y: 5, depth: 3, rng: new SeededRandom(1337));

        Assert.That(large, Is.Not.Null, "large_slime must be in entities.yaml");
        Assert.That(large!.Name, Is.EqualTo("Large Slime"));

        var corrosion = large.Get<CorrosionComponent>();
        Assert.That(corrosion?.Chance, Is.EqualTo(0.10).Within(0.001));

        var split = large.Get<SplitTracker>();
        Assert.That(split, Is.Not.Null, "large_slime should have SplitTracker");
        Assert.That(split!.TriggerHpPct, Is.EqualTo(0.40).Within(0.001));
        Assert.That(split.ChildType, Is.EqualTo("slime"));
        Assert.That(split.MinChildren, Is.EqualTo(2));
        Assert.That(split.MaxChildren, Is.EqualTo(3));
        Assert.That(split.Weights, Is.Not.Null);
        Assert.That(split.Weights!.Length, Is.EqualTo(2));
    }

    [Test]
    public void SlimeSpawnWeight_AtDepth2_IsNonZero()
    {
        var factory = CreateFactory();
        factory.TryGetDefinition("slime", out var def);
        Assert.That(def, Is.Not.Null);
        // Slime uses depth_weights — MinDepth should be 2
        Assert.That(def!.MinDepth, Is.EqualTo(2));
        Assert.That(def.DepthWeights, Is.Not.Null);
        Assert.That(def.DepthWeights![0].MinDepth, Is.EqualTo(2));
        Assert.That(def.DepthWeights[0].Weight, Is.GreaterThan(0));
    }

    [Test]
    public void LargeSlimeSpawnWeight_MinDepth_Is3()
    {
        var factory = CreateFactory();
        factory.TryGetDefinition("large_slime", out var def);
        Assert.That(def, Is.Not.Null);
        Assert.That(def!.MinDepth, Is.EqualTo(3));
        Assert.That(def.DepthWeights![0].MinDepth, Is.EqualTo(3));
    }

    [Test]
    public void DaggerHasMetalMaterial_FromYaml()
    {
        // Use LoadAll which correctly strips/separates sections — avoiding the
        // issue where LoadItems sees the monsters section's split_weights sequences.
        string entitiesPath = FindEntitiesYaml();
        var loader = new ContentLoader();
        var bundle = loader.LoadAllFromFile(entitiesPath);

        Assert.That(bundle.Items["dagger"].Material, Is.EqualTo("metal"));
        Assert.That(bundle.Items["club"].Material, Is.EqualTo("wood"));
        Assert.That(bundle.Items["shortsword"].Material, Is.EqualTo("metal"));
    }

    [Test]
    public void ItemFactory_SetBaseDamageMax_OnWeaponCreation()
    {
        // ItemFactory should call SetBaseDamageMax so BaseDamageMax == DamageMax at creation
        string entitiesPath = FindEntitiesYaml();
        var loader = new ContentLoader();
        var bundle = loader.LoadAllFromFile(entitiesPath);
        var entityFactory = new EntityFactory();
        var itemFactory = new ItemFactory(bundle.Items, entityFactory);

        var dagger = itemFactory.Create("dagger");
        Assert.That(dagger, Is.Not.Null);
        var eq = dagger!.Require<Equippable>();
        Assert.That(eq.BaseDamageMax, Is.EqualTo(eq.DamageMax),
            "BaseDamageMax must match DamageMax at creation");
        Assert.That(eq.Material, Is.EqualTo("metal"));
    }
}
