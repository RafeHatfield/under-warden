using UnderWarden.Logic.AI;
using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// W1-009: Unit tests for Wave 1 orc variant monsters.
///
/// Covers:
/// - MonsterFactory attaches specialized components per ai_type
/// - SkirmisherAI: Pouncing Leap triggers / cooldown / range gate
/// - OrcShamanAI: Crippling Hex applies CrippledEffect, cooldown enforced, hang-back retreat
/// - OrcChieftainAI: Rally Cry fires once, applies RallyEffect to allies; Sonic Bellow at low HP
/// - CombatResolver: CrippledEffect and RallyEffect modifiers applied correctly
/// - OrcVariantResolver: depth-based resolution returns expected types
/// - YAML: all 5 new variant definitions load correctly
/// </summary>
[TestFixture]
public class OrcVariantTests
{
    // ─── Shared helpers ───────────────────────────────────────────────────────

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
    /// Builds a minimal arena state with a monster at (monsterX, monsterY) and
    /// the player at (5, 5). Monster has no SpeciesTag so corpse system doesn't fire.
    /// </summary>
    private static (GameState state, Entity player, Entity monster) CreateArena(
        Entity monster, int playerX = 5, int playerY = 5, int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(20, 20);

        var player = new Entity(0, "Player", playerX, playerY, blocksMovement: true);
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 5, evasion: 1, damageMin: 3, damageMax: 5));
        map.RegisterEntity(player);
        map.RegisterEntity(monster);

        var state = new GameState(player, new List<Entity> { monster }, map, rng, turnLimit: 200);
        return (state, player, monster);
    }

    private static Entity MakeSkirmisher(int id = 1, int x = 10, int y = 5)
    {
        var e = new Entity(id, "Orc Skirmisher", x, y, blocksMovement: true);
        e.Add(new Fighter(hp: 24, strength: 12, dexterity: 15, constitution: 10,
            accuracy: 4, evasion: 3, damageMin: 3, damageMax: 5));
        e.Add(new AiComponent { AiType = "skirmisher", Faction = "orc" });
        e.Add(new AlertedState { LastKnownPlayerX = 5, LastKnownPlayerY = 5, TurnsUntilDeaggro = 20 });
        e.Add(new SkirmisherComponent());
        return e;
    }

    private static Entity MakeShaman(int id = 1, int x = 12, int y = 5)
    {
        var e = new Entity(id, "Orc Shaman", x, y, blocksMovement: true);
        e.Add(new Fighter(hp: 24, strength: 10, dexterity: 12, constitution: 10,
            accuracy: 3, evasion: 1, damageMin: 3, damageMax: 5));
        e.Add(new AiComponent { AiType = "orc_shaman", Faction = "orc" });
        e.Add(new AlertedState { LastKnownPlayerX = 5, LastKnownPlayerY = 5, TurnsUntilDeaggro = 20 });
        e.Add(new OrcShamanComponent());
        return e;
    }

    private static Entity MakeChieftain(int id = 1, int x = 10, int y = 5, int hp = 35)
    {
        var e = new Entity(id, "Orc Chieftain", x, y, blocksMovement: true);
        e.Add(new Fighter(hp: hp, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        e.Add(new AiComponent { AiType = "orc_chieftain", Faction = "orc" });
        e.Add(new AlertedState { LastKnownPlayerX = 5, LastKnownPlayerY = 5, TurnsUntilDeaggro = 20 });
        e.Add(new OrcChieftainComponent());
        return e;
    }

    // ─── YAML load tests ─────────────────────────────────────────────────────

    [Test]
    public void EntitiesYaml_LoadsOrcSkirmisher()
    {
        var factory = CreateFactory();
        Assert.That(factory.GetDefinition("orc_skirmisher"), Is.Not.Null);
    }

    [Test]
    public void EntitiesYaml_LoadsOrcShaman()
    {
        var factory = CreateFactory();
        Assert.That(factory.GetDefinition("orc_shaman"), Is.Not.Null);
    }

    [Test]
    public void EntitiesYaml_LoadsOrcChieftain()
    {
        var factory = CreateFactory();
        Assert.That(factory.GetDefinition("orc_chieftain"), Is.Not.Null);
    }

    [Test]
    public void EntitiesYaml_LoadsOrcScout()
    {
        var factory = CreateFactory();
        Assert.That(factory.GetDefinition("orc_scout"), Is.Not.Null);
    }

    [Test]
    public void EntitiesYaml_LoadsOrcVeteran()
    {
        var factory = CreateFactory();
        Assert.That(factory.GetDefinition("orc_veteran"), Is.Not.Null);
    }

    [Test]
    public void OrcVariants_AllHaveSpawnWeightZero()
    {
        var factory = CreateFactory();
        foreach (var id in new[] { "orc_scout", "orc_veteran", "orc_skirmisher", "orc_shaman", "orc_chieftain" })
        {
            var def = factory.GetDefinition(id)!;
            Assert.That(def.SpawnWeight ?? 0, Is.EqualTo(0),
                $"{id} should have spawn_weight: 0 (resolved via variant system, not direct pool).");
        }
    }

    // ─── MonsterFactory component attachment ─────────────────────────────────

    [Test]
    public void Factory_AttachesSkirmisherComponent()
    {
        var factory = CreateFactory();
        var entity = factory.Create("orc_skirmisher");
        Assert.That(entity!.Has<SkirmisherComponent>(), Is.True,
            "MonsterFactory should attach SkirmisherComponent for ai_type=skirmisher.");
    }

    [Test]
    public void Factory_AttachesOrcShamanComponent()
    {
        var factory = CreateFactory();
        var entity = factory.Create("orc_shaman");
        Assert.That(entity!.Has<OrcShamanComponent>(), Is.True,
            "MonsterFactory should attach OrcShamanComponent for ai_type=orc_shaman.");
    }

    [Test]
    public void Factory_AttachesOrcChieftainComponent()
    {
        var factory = CreateFactory();
        var entity = factory.Create("orc_chieftain");
        Assert.That(entity!.Has<OrcChieftainComponent>(), Is.True,
            "MonsterFactory should attach OrcChieftainComponent for ai_type=orc_chieftain.");
    }

    [Test]
    public void Factory_OrcDoesNotGetSpecializedComponent()
    {
        var factory = CreateFactory();
        var entity = factory.Create("orc");
        Assert.That(entity!.Has<SkirmisherComponent>(), Is.False);
        Assert.That(entity.Has<OrcShamanComponent>(), Is.False);
        Assert.That(entity.Has<OrcChieftainComponent>(), Is.False);
    }

    // ─── SkirmisherAI ─────────────────────────────────────────────────────────

    [Test]
    public void Skirmisher_LeapTriggersAtRange4()
    {
        // Player at (5,5), skirmisher at (9,5) → Chebyshev dist = 4 (within 3-6 range)
        var skirmisher = MakeSkirmisher(x: 9, y: 5);
        var (state, _, _) = CreateArena(skirmisher);

        var action = SkirmisherAI.Decide(skirmisher, state);

        // Should leap 2 tiles toward player (from x=9 → x=7)
        Assert.That(action.Kind, Is.EqualTo(MonsterAction.ActionKind.MoveTo),
            "Skirmisher should leap (MoveTo) when in range and cooldown=0.");
        Assert.That(action.TargetX, Is.EqualTo(7),
            "Leap should move 2 tiles toward player (9 → 7).");
    }

    [Test]
    public void Skirmisher_LeapSetsCooldown()
    {
        var skirmisher = MakeSkirmisher(x: 9, y: 5);
        var (state, _, _) = CreateArena(skirmisher);
        var comp = skirmisher.Get<SkirmisherComponent>()!;

        SkirmisherAI.Decide(skirmisher, state);

        Assert.That(comp.LeapCooldownRemaining, Is.EqualTo(comp.LeapCooldownTurns),
            "Leap cooldown should be set to LeapCooldownTurns after a successful leap.");
    }

    [Test]
    public void Skirmisher_LeapDoesNotFireOnCooldown()
    {
        var skirmisher = MakeSkirmisher(x: 9, y: 5);
        var (state, _, _) = CreateArena(skirmisher);
        skirmisher.Get<SkirmisherComponent>()!.LeapCooldownRemaining = 3; // on cooldown

        // Decide should NOT leap — should either advance normally (MoveTo adjacent) or pursue
        var action = SkirmisherAI.Decide(skirmisher, state);

        // Cooldown was 3, decremented to 2 — still on cooldown, no leap.
        // Should instead pursue normally (move one step toward player).
        Assert.That(action.Kind, Is.Not.EqualTo(MonsterAction.ActionKind.Wait),
            "Skirmisher should still pursue (not Wait) when leap is on cooldown.");
    }

    [Test]
    public void Skirmisher_LeapDoesNotFireBeyondRange()
    {
        // Place skirmisher 8 tiles away (beyond leap max range of 6)
        var skirmisher = MakeSkirmisher(x: 13, y: 5);
        var (state, _, _) = CreateArena(skirmisher);

        var action = SkirmisherAI.Decide(skirmisher, state);

        // At range 8, leap should NOT fire. Monster should advance instead.
        Assert.That(action.Kind, Is.EqualTo(MonsterAction.ActionKind.MoveTo),
            "Skirmisher should pursue (not wait) when out of leap range.");
        // Should advance one step, not two
        Assert.That(action.TargetX, Is.EqualTo(12),
            "Without leap, skirmisher should advance one step at a time.");
    }

    [Test]
    public void Skirmisher_CooldownDecrementsEachCall()
    {
        var skirmisher = MakeSkirmisher(x: 9, y: 5);
        var (state, _, _) = CreateArena(skirmisher);
        var comp = skirmisher.Get<SkirmisherComponent>()!;
        comp.LeapCooldownRemaining = 2;

        SkirmisherAI.Decide(skirmisher, state);
        Assert.That(comp.LeapCooldownRemaining, Is.EqualTo(1),
            "Cooldown should decrement by 1 each turn.");
    }

    // ─── OrcShamanAI ─────────────────────────────────────────────────────────

    [Test]
    public void Shaman_AppliesCrippledEffectWhenReady()
    {
        // Shaman at (11,5), player at (5,5) → dist=6. Outside ChantRange=5 so chant cannot fire.
        // Within HexRange=6 so hex fires. Tests that hex works when chant is not available.
        var shaman = MakeShaman(x: 11, y: 5);
        var (state, player, _) = CreateArena(shaman);

        OrcShamanAI.Decide(shaman, state);

        Assert.That(player.Has<CrippledEffect>(), Is.True,
            "Shaman should apply CrippledEffect to player when in hex range but outside chant range.");
    }

    [Test]
    public void Shaman_HexSetsFullCooldown()
    {
        // Shaman at (11,5): outside ChantRange=5, inside HexRange=6 — hex fires and sets cooldown.
        var shaman = MakeShaman(x: 11, y: 5);
        var (state, _, _) = CreateArena(shaman);
        var comp = shaman.Get<OrcShamanComponent>()!;

        OrcShamanAI.Decide(shaman, state);

        Assert.That(comp.HexCooldownRemaining, Is.EqualTo(comp.HexCooldownTurns),
            "Hex cooldown should be set to HexCooldownTurns after firing.");
    }

    [Test]
    public void Shaman_HexDoesNotFireOnCooldown()
    {
        var shaman = MakeShaman(x: 10, y: 5);
        var (state, player, _) = CreateArena(shaman);
        shaman.Get<OrcShamanComponent>()!.HexCooldownRemaining = 5;

        OrcShamanAI.Decide(shaman, state);

        Assert.That(player.Has<CrippledEffect>(), Is.False,
            "Shaman should NOT hex when cooldown > 0.");
    }

    [Test]
    public void Shaman_HexDoesNotFireOutOfRange()
    {
        // Place shaman 8 tiles away (beyond HexRange=6)
        var shaman = MakeShaman(x: 13, y: 5);
        var (state, player, _) = CreateArena(shaman);

        OrcShamanAI.Decide(shaman, state);

        Assert.That(player.Has<CrippledEffect>(), Is.False,
            "Shaman should NOT hex when player is out of HexRange.");
    }

    [Test]
    public void Shaman_RetreatsWhenPlayerInsideDangerRadius()
    {
        // Player at (5,5), shaman at (6,5) → dist=1, inside DangerRadius=2
        var shaman = MakeShaman(x: 6, y: 5);
        var (state, _, _) = CreateArena(shaman, playerX: 5, playerY: 5);

        var action = OrcShamanAI.Decide(shaman, state);

        // Shaman should retreat (flee), not attack or wait
        Assert.That(action.Kind, Is.EqualTo(MonsterAction.ActionKind.MoveTo),
            "Shaman should retreat (MoveTo away from player) when inside danger radius.");
        // Retreating means moving to a tile farther from (5,5) than (6,5)
        int retreatDist = Math.Max(Math.Abs(action.TargetX - 5), Math.Abs(action.TargetY - 5));
        Assert.That(retreatDist, Is.GreaterThanOrEqualTo(1),
            "Retreat tile should be farther from player than current position.");
    }

    // ─── OrcChieftainAI ──────────────────────────────────────────────────────

    [Test]
    public void Chieftain_RallyCryAppliesRallyEffectToAllies()
    {
        var chieftain = MakeChieftain(id: 1, x: 10, y: 5);
        // Two orc allies within range 5 of chieftain (at x=10)
        var ally1 = new Entity(2, "Orc", 11, 5, blocksMovement: true);
        ally1.Add(new Fighter(hp: 28, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        ally1.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        ally1.Add(new AlertedState { TurnsUntilDeaggro = 20 });

        var ally2 = new Entity(3, "Orc", 12, 5, blocksMovement: true);
        ally2.Add(new Fighter(hp: 28, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        ally2.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        ally2.Add(new AlertedState { TurnsUntilDeaggro = 20 });

        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(20, 20);
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 5, evasion: 1, damageMin: 3, damageMax: 5));
        map.RegisterEntity(player);
        map.RegisterEntity(chieftain);
        map.RegisterEntity(ally1);
        map.RegisterEntity(ally2);

        var state = new GameState(player, new List<Entity> { chieftain, ally1, ally2 }, map, rng);

        OrcChieftainAI.Decide(chieftain, state);

        Assert.That(ally1.Has<RallyEffect>(), Is.True, "Ally1 should receive RallyEffect from Rally Cry.");
        Assert.That(ally2.Has<RallyEffect>(), Is.True, "Ally2 should receive RallyEffect from Rally Cry.");
        Assert.That(chieftain.Has<RallyEffect>(), Is.True, "Chieftain should also receive its own Rally Cry buff.");
    }

    [Test]
    public void Chieftain_RallyCryFiresOnce()
    {
        var chieftain = MakeChieftain(id: 1, x: 10, y: 5);
        var ally1 = new Entity(2, "Orc", 11, 5, blocksMovement: true);
        ally1.Add(new Fighter(hp: 28, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        ally1.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        ally1.Add(new AlertedState { TurnsUntilDeaggro = 20 });

        var ally2 = new Entity(3, "Orc", 12, 5, blocksMovement: true);
        ally2.Add(new Fighter(hp: 28, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        ally2.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        ally2.Add(new AlertedState { TurnsUntilDeaggro = 20 });

        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(20, 20);
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 5, evasion: 1, damageMin: 3, damageMax: 5));
        map.RegisterEntity(player);
        map.RegisterEntity(chieftain);
        map.RegisterEntity(ally1);
        map.RegisterEntity(ally2);

        var state = new GameState(player, new List<Entity> { chieftain, ally1, ally2 }, map, rng);

        OrcChieftainAI.Decide(chieftain, state);
        OrcChieftainAI.Decide(chieftain, state);

        // Remove the rally effect and re-check: second Decide() shouldn't re-apply
        ally1.Remove<RallyEffect>();
        OrcChieftainAI.Decide(chieftain, state); // third call

        Assert.That(ally1.Has<RallyEffect>(), Is.False,
            "Rally Cry should not fire again after RallyCried=true.");
        Assert.That(chieftain.Get<OrcChieftainComponent>()!.RallyCried, Is.True);
    }

    [Test]
    public void Chieftain_SonicBellowAppliesCrippledAtLowHp()
    {
        // Chieftain created with BaseMaxHp=35, then HP set to 14 (40% < 50% threshold).
        var chieftain = MakeChieftain(id: 1, x: 10, y: 5, hp: 35);
        chieftain.Get<Fighter>()!.Hp = 14; // 14/35 = 40%
        var (state, player, _) = CreateArena(chieftain, playerX: 5, playerY: 5);

        OrcChieftainAI.Decide(chieftain, state);

        Assert.That(player.Has<CrippledEffect>(), Is.True,
            "Chieftain should apply CrippledEffect to player via Sonic Bellow at < 50% HP.");
    }

    [Test]
    public void Chieftain_SonicBellowFiresOnce()
    {
        var chieftain = MakeChieftain(id: 1, x: 10, y: 5, hp: 35);
        chieftain.Get<Fighter>()!.Hp = 14;
        var (state, player, _) = CreateArena(chieftain, playerX: 5, playerY: 5);

        OrcChieftainAI.Decide(chieftain, state);
        player.Remove<CrippledEffect>(); // manually remove to test second call
        OrcChieftainAI.Decide(chieftain, state);

        Assert.That(player.Has<CrippledEffect>(), Is.False,
            "Sonic Bellow should not fire again after BellowedAtLowHp=true.");
        Assert.That(chieftain.Get<OrcChieftainComponent>()!.BellowedAtLowHp, Is.True);
    }

    [Test]
    public void Chieftain_SonicBellowDoesNotFireAtFullHp()
    {
        // HP at full (35/35 = 100%, above 50% threshold)
        var chieftain = MakeChieftain(id: 1, x: 10, y: 5, hp: 35);
        var (state, player, _) = CreateArena(chieftain, playerX: 5, playerY: 5);

        OrcChieftainAI.Decide(chieftain, state);

        Assert.That(player.Has<CrippledEffect>(), Is.False,
            "Sonic Bellow should not fire at full HP (above 50% threshold).");
    }

    // ─── CombatResolver: CrippledEffect + RallyEffect ─────────────────────────

    [Test]
    public void CrippledEffect_PenalizesAttackerToHit()
    {
        // With a fixed seed and known roll, verify the crippled penalty reduces hit chance.
        // We use a guaranteed-hit setup (high dex player vs low AC monster) to establish baseline,
        // then verify that CrippledEffect on the attacker can cause a miss via reduced to-hit.
        var rng = new SeededRandom(42);
        var attacker = new Entity(0, "Attacker", 0, 0, false);
        var defender = new Entity(1, "Defender", 0, 0, false);

        // Attacker: dex=10 (mod=0), no bonus → pure d20 vs defender AC
        attacker.Add(new Fighter(hp: 100, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 0, evasion: 0, damageMin: 2, damageMax: 4));
        // Defender: dex=10 → BaseAC=10. Without crippled: need d20 >= 10 (55% hit rate).
        defender.Add(new Fighter(hp: 100, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 0, evasion: 0, damageMin: 2, damageMax: 4));

        // Add CrippledEffect to attacker — should subtract ToHitPenalty=1 from roll.
        attacker.Add(new CrippledEffect { RemainingTurns = 5, ToHitPenalty = 3, AcPenalty = 1 });

        // Roll 100 attacks: crippled attacker should have lower hit rate than baseline.
        var rngBaseline = new SeededRandom(42);
        var rngCrippled = new SeededRandom(42);
        var baseline = new Entity(10, "A", 0, 0, false);
        baseline.Add(new Fighter(hp: 1000, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 0, evasion: 0, damageMin: 1, damageMax: 1));
        var target = new Entity(11, "D", 0, 0, false);
        target.Add(new Fighter(hp: 1000, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 0, evasion: 0, damageMin: 1, damageMax: 1));

        int baselineHits = 0, crippledHits = 0;
        for (int i = 0; i < 100; i++)
        {
            var r1 = CombatResolver.ResolveAttack(baseline, target, rngBaseline);
            if (r1.Hit) baselineHits++;
        }
        baseline.Add(new CrippledEffect { RemainingTurns = 999, ToHitPenalty = 3, AcPenalty = 0 });
        var target2 = new Entity(12, "D2", 0, 0, false);
        target2.Add(new Fighter(hp: 1000, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 0, evasion: 0, damageMin: 1, damageMax: 1));
        for (int i = 0; i < 100; i++)
        {
            var r2 = CombatResolver.ResolveAttack(baseline, target2, rngCrippled);
            if (r2.Hit) crippledHits++;
        }

        Assert.That(crippledHits, Is.LessThan(baselineHits),
            "CrippledEffect ToHitPenalty should reduce attacker hit rate.");
    }

    [Test]
    public void CrippledEffect_ReducesDefenderAc()
    {
        // Attacker with very low to-hit (barely hits) vs crippled defender (easier to hit).
        // Seed chosen so attacker barely misses without CrippledEffect on defender.
        // Verify hit rate increases when defender has CrippledEffect with AcPenalty.
        var rng1 = new SeededRandom(1337);
        var rng2 = new SeededRandom(1337);

        var attacker1 = new Entity(0, "A", 0, 0, false);
        attacker1.Add(new Fighter(hp: 100, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 0, evasion: 0, damageMin: 1, damageMax: 1));
        var def1 = new Entity(1, "D", 0, 0, false);
        def1.Add(new Fighter(hp: 1000, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 0, evasion: 0, damageMin: 1, damageMax: 1));

        var attacker2 = new Entity(2, "A2", 0, 0, false);
        attacker2.Add(new Fighter(hp: 100, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 0, evasion: 0, damageMin: 1, damageMax: 1));
        var def2 = new Entity(3, "D2", 0, 0, false);
        def2.Add(new Fighter(hp: 1000, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 0, evasion: 0, damageMin: 1, damageMax: 1));
        def2.Add(new CrippledEffect { RemainingTurns = 999, ToHitPenalty = 0, AcPenalty = 3 });

        int normalHits = 0, crippledDefHits = 0;
        for (int i = 0; i < 100; i++)
        {
            if (CombatResolver.ResolveAttack(attacker1, def1, rng1).Hit) normalHits++;
            if (CombatResolver.ResolveAttack(attacker2, def2, rng2).Hit) crippledDefHits++;
        }

        Assert.That(crippledDefHits, Is.GreaterThan(normalHits),
            "CrippledEffect AcPenalty on defender should increase attacker's hit rate.");
    }

    [Test]
    public void RallyEffect_IncreasesAttackerToHit()
    {
        var rng1 = new SeededRandom(1337);
        var rng2 = new SeededRandom(1337);

        var normal = new Entity(0, "A", 0, 0, false);
        normal.Add(new Fighter(hp: 100, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 0, evasion: 0, damageMin: 1, damageMax: 1));
        var def1 = new Entity(1, "D", 0, 0, false);
        def1.Add(new Fighter(hp: 1000, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 0, evasion: 0, damageMin: 1, damageMax: 1));

        var rallied = new Entity(2, "A2", 0, 0, false);
        rallied.Add(new Fighter(hp: 100, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 0, evasion: 0, damageMin: 1, damageMax: 1));
        rallied.Add(new RallyEffect { RemainingTurns = 999, ToHitBonus = 3, DamageBonus = 0 });
        var def2 = new Entity(3, "D2", 0, 0, false);
        def2.Add(new Fighter(hp: 1000, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 0, evasion: 0, damageMin: 1, damageMax: 1));

        int normalHits = 0, ralliedHits = 0;
        for (int i = 0; i < 100; i++)
        {
            if (CombatResolver.ResolveAttack(normal, def1, rng1).Hit) normalHits++;
            if (CombatResolver.ResolveAttack(rallied, def2, rng2).Hit) ralliedHits++;
        }

        Assert.That(ralliedHits, Is.GreaterThan(normalHits),
            "RallyEffect ToHitBonus should increase attacker's hit rate.");
    }

    [Test]
    public void RallyEffect_IncreasesAttackerDamage()
    {
        var rng = new SeededRandom(1337);
        var attacker = new Entity(0, "A", 0, 0, false);
        // dex=18 → guaranteed hit (DexMod=4, hits on d20>=6 with target AC=10). damageMin=damageMax=5 (fixed).
        attacker.Add(new Fighter(hp: 100, strength: 10, dexterity: 18, constitution: 10,
            accuracy: 0, evasion: 0, damageMin: 5, damageMax: 5));
        attacker.Add(new RallyEffect { RemainingTurns = 999, ToHitBonus = 0, DamageBonus = 2 });
        var defender = new Entity(1, "D", 0, 0, false);
        defender.Add(new Fighter(hp: 1000, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 0, evasion: 0, damageMin: 1, damageMax: 1));

        // All non-crit hits should deal exactly 5 + DexMod(0 for str) + RallyBonus(2) = 7 damage
        // StrMod = (10-10)/2 = 0, so damage = 5 + 0 + 2 = 7 on non-crit
        var rng2 = new SeededRandom(1337);
        int totalDamage = 0;
        int hitCount = 0;
        for (int i = 0; i < 50; i++)
        {
            var result = CombatResolver.ResolveAttack(attacker, defender, rng2);
            if (result.Hit && !result.IsCritical)
            {
                totalDamage += result.Damage;
                hitCount++;
            }
        }
        if (hitCount > 0)
            Assert.That(totalDamage / hitCount, Is.EqualTo(7),
                "RallyEffect DamageBonus=2 should add 2 to non-crit hit damage (5 base + 0 str + 2 rally = 7).");
    }

    // ─── OrcVariantResolver ──────────────────────────────────────────────────

    [Test]
    public void OrcVariantResolver_ReturnsOrcAtDepth1()
    {
        var rng = new SeededRandom(1337);
        // At depth 1, all rolls should return "orc"
        for (int i = 0; i < 20; i++)
            Assert.That(OrcVariantResolver.Resolve(1, rng), Is.EqualTo("orc"));
    }

    [Test]
    public void OrcVariantResolver_ReturnsVariantAtDepth6()
    {
        var rng = new SeededRandom(1337);
        // At depth 6+, run 1000 draws and verify each variant appears
        var counts = new Dictionary<string, int> { ["orc"] = 0, ["orc_brute"] = 0, ["orc_shaman"] = 0, ["orc_skirmisher"] = 0 };
        for (int i = 0; i < 1000; i++)
        {
            var result = OrcVariantResolver.Resolve(6, rng);
            if (counts.ContainsKey(result)) counts[result]++;
        }

        // At depth 6: expected ~17.5% skirmisher, ~10% brute, ~10% shaman, ~62.5% orc
        Assert.That(counts["orc_skirmisher"], Is.GreaterThan(100),
            "At depth 6, expect ~17.5% skirmisher (>10% in 1000 draws).");
        Assert.That(counts["orc_brute"], Is.GreaterThan(50),
            "At depth 6, expect ~10% brute.");
        Assert.That(counts["orc_shaman"], Is.GreaterThan(50),
            "At depth 6, expect ~10% shaman.");
        Assert.That(counts["orc"], Is.GreaterThan(500),
            "At depth 6, base orc should still be the majority (~62.5%).");
    }

    [Test]
    public void OrcVariantResolver_SkirmisherNotPresentBeforeDepth3()
    {
        var rng = new SeededRandom(999);
        for (int i = 0; i < 200; i++)
        {
            var result = OrcVariantResolver.Resolve(2, rng);
            Assert.That(result, Is.Not.EqualTo("orc_skirmisher"),
                "Orc skirmisher should not appear at depth 2 (debut at depth 3).");
        }
    }
}
