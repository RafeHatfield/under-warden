using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Logic.Features;

/// <summary>
/// TASK-020: Acid weapon coating from acid_trap walk-over.
///
/// When the player triggers an acid_trap and survives, their equipped main-hand weapon
/// gains WeaponAcidCoatingComponent{HitsRemaining:4}. On each successful hit with the
/// coated weapon, AcidEffect(duration:6) is applied to the target and HitsRemaining decrements.
/// The component is removed when HitsRemaining reaches 0.
///
/// Secondary: AcidEffect suppresses InnateRegenComponent (troll regen) — CoatedWeapon tests
/// confirm this end-to-end via StatusEffectProcessor.
/// </summary>
[TestFixture]
public class WeaponAcidCoatingTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Entity MakePlayer(int id = 0, int x = 3, int y = 5)
    {
        var e = new Entity(id, "Player", x, y, blocksMovement: true);
        e.Add(new Fighter(hp: 50, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 20, evasion: 1, damageMin: 3, damageMax: 5));
        return e;
    }

    private static Entity MakeWeapon(int id = 99, string name = "Short Sword")
    {
        var w = new Entity(id, name, 0, 0, blocksMovement: false);
        w.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 3, DamageMax = 5 });
        return w;
    }

    private static Entity MakeTroll(int id = 2, int x = 4, int y = 5)
    {
        var e = new Entity(id, "Troll", x, y, blocksMovement: true);
        e.Add(new Fighter(hp: 40, strength: 16, dexterity: 8, constitution: 14,
            accuracy: 2, evasion: 0, damageMin: 8, damageMax: 12));
        e.Add(new InnateRegenComponent { HealPerTurn = 2 });
        return e;
    }

    private static Entity MakeMonster(int id = 2, int x = 4, int y = 5)
    {
        var e = new Entity(id, "Orc", x, y, blocksMovement: true);
        e.Add(new Fighter(hp: 40, strength: 10, dexterity: 8, constitution: 10,
            accuracy: 2, evasion: 0, damageMin: 3, damageMax: 6));
        return e;
    }

    /// <summary>
    /// Build a game state with the player holding a weapon, and an acid_trap on tile (4,5).
    /// Player is at (3,5) — one step away. MoveRight triggers the trap.
    /// </summary>
    private static (GameState state, Entity weapon, Entity trapFeature) CreateAcidTrapScenario(
        int seed = 1337, List<Entity>? monsters = null)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = MakePlayer();
        map.RegisterEntity(player);

        var weapon = MakeWeapon();
        var equip = new Equipment();
        equip.SetSlot(EquipmentSlot.MainHand, weapon);
        player.Add(equip);

        // Acid trap at (4,5) — guaranteed trigger (detectChance=0, not pre-detected)
        var payload = new TrapPayloadComponent();
        payload.Actions.Add(new TrapAction { Kind = "acid", Duration = 8 });
        payload.Actions.Add(new TrapAction { Kind = "damage", Amount = 2 }); // small, won't kill

        var trap = new Entity(10, "Acid Trap", 4, 5, blocksMovement: false);
        trap.Add(new FloorTrapComponent
        {
            TrapType            = "acid_trap",
            IsSpent             = false,
            IsDetected          = false,
            IsDetectable        = true,
            PassiveDetectChance = 0.0, // never detect in tests
            Payload             = payload,
            VisibleTileId       = 431,
        });

        var monsterList = monsters ?? new List<Entity>();
        foreach (var m in monsterList)
            map.RegisterEntity(m);

        var state = new GameState(player, monsterList, map, rng);
        state.Features.Add(trap);

        return (state, weapon, trap);
    }

    private static PlayerAction MoveRight() => PlayerAction.MoveTo(4, 5);

    // ── AcidTrap_CoatsWeapon ──────────────────────────────────────────────────

    [Test]
    public void AcidTrap_PlayerSurvives_WeaponGetsCoating()
    {
        var (state, weapon, _) = CreateAcidTrapScenario();

        TurnController.ProcessTurn(state, MoveRight());

        var coating = weapon.Get<WeaponAcidCoatingComponent>();
        Assert.That(coating, Is.Not.Null, "Weapon should have WeaponAcidCoatingComponent after acid trap");
        Assert.That(coating!.HitsRemaining, Is.EqualTo(4), "Coating should start at 4 hits remaining");
    }

    [Test]
    public void AcidTrap_EmitsWeaponAcidCoatedEvent()
    {
        var (state, _, _) = CreateAcidTrapScenario();

        var result = TurnController.ProcessTurn(state, MoveRight());

        var coatEvent = result.Events.OfType<WeaponAcidCoatedEvent>().FirstOrDefault();
        Assert.That(coatEvent, Is.Not.Null, "WeaponAcidCoatedEvent should be emitted");
        Assert.That(coatEvent!.HitsRemaining, Is.EqualTo(4));
    }

    [Test]
    public void AcidTrap_NoWeaponEquipped_NoCoating()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        // Player with no weapon
        var player = MakePlayer();
        player.Add(new Equipment()); // empty equipment
        map.RegisterEntity(player);

        var payload = new TrapPayloadComponent();
        payload.Actions.Add(new TrapAction { Kind = "acid", Duration = 8 });
        payload.Actions.Add(new TrapAction { Kind = "damage", Amount = 1 });

        var trap = new Entity(10, "Acid Trap", 4, 5, blocksMovement: false);
        trap.Add(new FloorTrapComponent
        {
            TrapType            = "acid_trap",
            IsSpent             = false,
            IsDetected          = false,
            PassiveDetectChance = 0.0,
            Payload             = payload,
            VisibleTileId       = 431,
        });

        var state = new GameState(player, new List<Entity>(), map, rng);
        state.Features.Add(trap);

        var result = TurnController.ProcessTurn(state, MoveRight());

        // No WeaponAcidCoatedEvent — no weapon to coat
        var coatEvent = result.Events.OfType<WeaponAcidCoatedEvent>().FirstOrDefault();
        Assert.That(coatEvent, Is.Null, "No coating event when player has no weapon");
    }

    [Test]
    public void AcidTrap_AlreadyCoated_TakesHigherHitsRemaining()
    {
        var (state, weapon, trapFeature) = CreateAcidTrapScenario();

        // Pre-coat the weapon with only 1 hit remaining
        weapon.Add(new WeaponAcidCoatingComponent { HitsRemaining = 1, EffectDuration = 6 });

        TurnController.ProcessTurn(state, MoveRight());

        // Should refresh to 4 since 4 > 1
        var coating = weapon.Get<WeaponAcidCoatingComponent>();
        Assert.That(coating, Is.Not.Null);
        Assert.That(coating!.HitsRemaining, Is.EqualTo(4), "Coating refreshed to higher count");
    }

    // ── CoatedWeapon_AppliesAcidOnHit ─────────────────────────────────────────

    [Test]
    public void CoatedWeapon_AppliesAcidEffect_OnHit()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = MakePlayer();
        var weapon = MakeWeapon();
        weapon.Add(new WeaponAcidCoatingComponent { HitsRemaining = 4, EffectDuration = 6 });
        var equip = new Equipment();
        equip.SetSlot(EquipmentSlot.MainHand, weapon);
        player.Add(equip);
        map.RegisterEntity(player);

        // Monster adjacent, guaranteed to be hit (evasion 0)
        var monster = MakeMonster(id: 2, x: 4, y: 5);
        map.RegisterEntity(monster);

        var state = new GameState(player, new List<Entity> { monster }, map, rng);

        // Force a hit by trying attacks until we get one
        int maxAttempts = 20;
        bool gotAcidEffect = false;
        for (int i = 0; i < maxAttempts; i++)
        {
            var st = new GameState(player, new List<Entity> { monster }, map, new SeededRandom(1337 + i));
            // Ensure coating is fresh each attempt
            weapon.Remove<WeaponAcidCoatingComponent>();
            weapon.Add(new WeaponAcidCoatingComponent { HitsRemaining = 4, EffectDuration = 6 });
            monster.Get<Fighter>()!.Hp = 40; // reset HP

            TurnController.ProcessTurn(st, PlayerAction.Attack(monster));

            if (monster.Has<AcidEffect>())
            {
                gotAcidEffect = true;
                var acid = monster.Get<AcidEffect>()!;
                // Applied at 6; ProcessTurnStart may tick it down by 1 same turn — allow 5 or 6.
                Assert.That(acid.RemainingTurns, Is.GreaterThanOrEqualTo(5), "AcidEffect should have at least 5 turns remaining");
                break;
            }
        }

        Assert.That(gotAcidEffect, Is.True,
            "Coated weapon should apply AcidEffect to target within 20 attack attempts");
    }

    [Test]
    public void CoatedWeapon_ExpiresAfterNHits()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = MakePlayer();
        // High accuracy + high damage so attacks always hit and we can track hit count
        var weapon = MakeWeapon();
        weapon.Add(new WeaponAcidCoatingComponent { HitsRemaining = 4, EffectDuration = 6 });
        var equip = new Equipment();
        equip.SetSlot(EquipmentSlot.MainHand, weapon);
        player.Add(equip);
        map.RegisterEntity(player);

        // Very tanky monster so it doesn't die in 4 hits
        var monster = new Entity(2, "Tank", 4, 5, blocksMovement: true);
        monster.Add(new Fighter(hp: 999, strength: 5, dexterity: 1, constitution: 10,
            accuracy: 1, evasion: 0, damageMin: 1, damageMax: 1));
        map.RegisterEntity(monster);

        int hits = 0;
        for (int attempt = 0; attempt < 100 && hits < 4; attempt++)
        {
            var st = new GameState(player, new List<Entity> { monster }, map, new SeededRandom(attempt));
            if (weapon.Get<WeaponAcidCoatingComponent>() == null) break; // already expired

            var result = TurnController.ProcessTurn(st, PlayerAction.Attack(monster));
            var atk = result.Events.OfType<AttackEvent>().FirstOrDefault(e => e.ActorId == player.Id);
            if (atk?.Hit == true) hits++;
        }

        // After 4 hits the coating should be gone
        Assert.That(weapon.Get<WeaponAcidCoatingComponent>(), Is.Null,
            "WeaponAcidCoatingComponent should be removed after 4 successful hits");
    }

    // ── CoatedWeapon_SuppressesTrollInnateRegen ───────────────────────────────

    [Test]
    public void CoatedWeapon_SuppressesTrollInnateRegen()
    {
        // Arrange: troll hit by coated weapon receives AcidEffect.
        // Next turn, InnateRegenComponent should be suppressed — troll does not heal.
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = MakePlayer();
        var weapon = MakeWeapon();
        weapon.Add(new WeaponAcidCoatingComponent { HitsRemaining = 4, EffectDuration = 6 });
        var equip = new Equipment();
        equip.SetSlot(EquipmentSlot.MainHand, weapon);
        player.Add(equip);
        map.RegisterEntity(player);

        var troll = MakeTroll(id: 2, x: 4, y: 5);
        map.RegisterEntity(troll);

        // First: get a hit that applies acid. Use seeds until we land one.
        bool trollGotAcid = false;
        int seed = 1337;
        for (; seed < 1337 + 30 && !trollGotAcid; seed++)
        {
            var st = new GameState(player, new List<Entity> { troll }, map, new SeededRandom(seed));
            weapon.Remove<WeaponAcidCoatingComponent>();
            weapon.Add(new WeaponAcidCoatingComponent { HitsRemaining = 4, EffectDuration = 6 });
            troll.Get<Fighter>()!.Hp = 40;
            troll.Remove<AcidEffect>();

            TurnController.ProcessTurn(st, PlayerAction.Attack(troll));
            if (troll.Has<AcidEffect>())
                trollGotAcid = true;
        }

        Assert.That(trollGotAcid, Is.True, "Troll should receive AcidEffect from coated weapon hit");

        // Now run a turn where only the troll acts — regen should be suppressed.
        // Build fresh state with troll already acid-affected. Take troll HP snapshot.
        var map2 = GameMap.CreateArena(12, 12);
        var player2 = MakePlayer(id: 0, x: 9, y: 9); // far away, won't fight
        map2.RegisterEntity(player2);

        var troll2 = MakeTroll(id: 2, x: 4, y: 5);
        troll2.Get<Fighter>()!.Hp = 20; // reduced from full so regen would be visible
        troll2.Add(new AcidEffect { RemainingTurns = 6 });
        map2.RegisterEntity(troll2);

        var state2 = new GameState(player2, new List<Entity> { troll2 }, map2, new SeededRandom(42));
        int hpBefore = troll2.Get<Fighter>()!.Hp;

        // Process a no-op turn (player waits), troll acts, regen should NOT fire
        TurnController.ProcessTurn(state2, PlayerAction.Wait);

        int hpAfter = troll2.Get<Fighter>()!.Hp;
        Assert.That(hpAfter, Is.EqualTo(hpBefore),
            "Troll with AcidEffect should NOT regenerate (InnateRegenComponent suppressed)");
    }
}
