using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for the ring system — Phase 1 (10 rings fully wired) and content loading.
///
/// Covers:
/// - ContentLoader loads all 16 ring definitions (10 Phase 1 + 6 Phase 2 stubs)
/// - ItemFactory creates ring entities with correct components
/// - Stat rings (protection, strength, dexterity, constitution, might) equip/unequip
/// - Speed rings (speed, hummingbird) adjust SpeedBonusTracker.RingRatio
/// - Regeneration ring heals passively every 5 turns
/// - FreeAction ring blocks slow and paralysis, not poison
/// - Teleportation ring triggers on-hit teleport with 20% chance
/// - Ring slot auto-assignment (left → right when left is full)
/// - Displaced ring effect is reversed when swapping
/// - Two identical rings stack correctly
/// - Ring identified on equip
/// - Ring effects survive floor transition (ReapplyRingEffects)
/// </summary>
[TestFixture]
public class RingTests
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

    private static ItemFactory CreateItemFactory()
    {
        var loader = new ContentLoader();
        var bundle = loader.LoadAllFromFile(FindEntitiesYaml());
        var entityFactory = new EntityFactory();
        return new ItemFactory(bundle.Items, entityFactory);
    }

    /// <summary>Minimal arena state with a player at (5,5).</summary>
    private static (GameState state, Entity player) CreateArena(int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(20, 20);
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 80, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 5, evasion: 1, damageMin: 3, damageMax: 5));
        player.Add(new Inventory());
        map.RegisterEntity(player);

        var state = new GameState(player, new List<Entity>(), map, rng, turnLimit: 200);
        return (state, player);
    }

    /// <summary>Create a ring entity via ItemFactory and inject it into the player inventory.</summary>
    private static Entity GiveRing(Entity player, ItemFactory factory, string ringId)
    {
        var ring = factory.Create(ringId);
        Assert.That(ring, Is.Not.Null, $"Ring '{ringId}' not found in factory");
        player.Get<Inventory>()!.Add(ring!);
        return ring!;
    }

    /// <summary>Equip a ring by processing a PlayerAction.Equip turn.</summary>
    private static TurnResult EquipRing(GameState state, Entity ring)
        => TurnController.ProcessTurn(state, PlayerAction.Equip(ring));

    /// <summary>Unequip from a slot by processing a PlayerAction.Unequip turn.</summary>
    private static TurnResult UnequipRing(GameState state, EquipmentSlot slot)
        => TurnController.ProcessTurn(state, PlayerAction.Unequip(slot));

    // ─── Content loading ──────────────────────────────────────────────────────

    [Test]
    public void ContentLoader_Loads_All_Phase1_Rings()
    {
        var loader = new ContentLoader();
        var bundle = loader.LoadAllFromFile(FindEntitiesYaml());

        string[] phase1 = [
            "ring_of_protection", "ring_of_regeneration", "ring_of_strength",
            "ring_of_dexterity", "ring_of_constitution", "ring_of_might",
            "ring_of_speed", "ring_of_hummingbird", "ring_of_free_action",
            "ring_of_teleportation",
        ];

        foreach (var id in phase1)
            Assert.That(bundle.Items.ContainsKey(id), Is.True, $"Missing Phase 1 ring: {id}");
    }

    [Test]
    public void ContentLoader_Loads_Phase2_Stubs()
    {
        var loader = new ContentLoader();
        var bundle = loader.LoadAllFromFile(FindEntitiesYaml());

        string[] phase2 = [
            "ring_of_resistance", "ring_of_clarity", "ring_of_invisibility",
            "ring_of_searching", "ring_of_wizardry", "ring_of_luck",
        ];

        foreach (var id in phase2)
            Assert.That(bundle.Items.ContainsKey(id), Is.True, $"Missing Phase 2 stub ring: {id}");
    }

    [Test]
    public void ItemFactory_Creates_Ring_With_Components()
    {
        var factory = CreateItemFactory();
        var ring = factory.Create("ring_of_protection");

        Assert.That(ring, Is.Not.Null);
        Assert.That(ring!.Get<Equippable>(), Is.Not.Null, "Ring should have Equippable");
        Assert.That(ring.Get<Equippable>()!.Slot, Is.EqualTo(EquipmentSlot.LeftRing));

        var effect = ring.Get<RingEffectComponent>();
        Assert.That(effect, Is.Not.Null, "Ring should have RingEffectComponent");
        Assert.That(effect!.Kind, Is.EqualTo(RingEffectKind.Protection));
        Assert.That(effect.Strength, Is.EqualTo(2));

        var idComp = ring.Get<IdentifiableItem>();
        Assert.That(idComp, Is.Not.Null, "Ring should have IdentifiableItem");
        Assert.That(idComp!.IdentifiedName, Is.EqualTo("Ring Of Protection"));
    }

    [Test]
    public void ItemFactory_Creates_Speed_Ring_With_Correct_SpeedRatio()
    {
        var factory = CreateItemFactory();

        var speedRing = factory.Create("ring_of_speed");
        Assert.That(speedRing!.Get<RingEffectComponent>()!.SpeedRatio, Is.EqualTo(0.10).Within(0.001));

        var hummingbird = factory.Create("ring_of_hummingbird");
        Assert.That(hummingbird!.Get<RingEffectComponent>()!.SpeedRatio, Is.EqualTo(0.25).Within(0.001));
    }

    // ─── Protection ring ──────────────────────────────────────────────────────

    [Test]
    public void Protection_Ring_Adds_AC_On_Equip()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_protection");
        int baseDef = player.Get<Fighter>()!.BaseDefense;

        EquipRing(state, ring);

        Assert.That(player.Get<Fighter>()!.BaseDefense, Is.EqualTo(baseDef + 2));
    }

    [Test]
    public void Protection_Ring_Removes_AC_On_Unequip()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_protection");
        int baseDef = player.Get<Fighter>()!.BaseDefense;

        EquipRing(state, ring);
        UnequipRing(state, EquipmentSlot.LeftRing);

        Assert.That(player.Get<Fighter>()!.BaseDefense, Is.EqualTo(baseDef));
    }

    [Test]
    public void Two_Protection_Rings_Stack()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring1 = GiveRing(player, factory, "ring_of_protection");
        var ring2 = GiveRing(player, factory, "ring_of_protection");
        int baseDef = player.Get<Fighter>()!.BaseDefense;

        EquipRing(state, ring1);
        EquipRing(state, ring2);

        Assert.That(player.Get<Fighter>()!.BaseDefense, Is.EqualTo(baseDef + 4));
        // Both slots filled
        var eq = player.Get<Equipment>()!;
        Assert.That(eq.LeftRing, Is.Not.Null);
        Assert.That(eq.RightRing, Is.Not.Null);
    }

    // ─── Strength ring ───────────────────────────────────────────────────────

    [Test]
    public void Strength_Ring_Modifies_Fighter_Strength()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_strength");
        int baseStr = player.Get<Fighter>()!.Strength;

        EquipRing(state, ring);
        Assert.That(player.Get<Fighter>()!.Strength, Is.EqualTo(baseStr + 2));

        UnequipRing(state, EquipmentSlot.LeftRing);
        Assert.That(player.Get<Fighter>()!.Strength, Is.EqualTo(baseStr));
    }

    // ─── Dexterity ring ──────────────────────────────────────────────────────

    [Test]
    public void Dexterity_Ring_Modifies_Fighter_Dexterity()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_dexterity");
        int baseDex = player.Get<Fighter>()!.Dexterity;

        EquipRing(state, ring);
        Assert.That(player.Get<Fighter>()!.Dexterity, Is.EqualTo(baseDex + 2));

        UnequipRing(state, EquipmentSlot.LeftRing);
        Assert.That(player.Get<Fighter>()!.Dexterity, Is.EqualTo(baseDex));
    }

    // ─── Constitution ring ───────────────────────────────────────────────────

    [Test]
    public void Constitution_Ring_Adds_Con_And_MaxHp()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_constitution");
        var fighter = player.Get<Fighter>()!;
        int baseCon = fighter.Constitution;
        int baseMaxHp = fighter.MaxHp;
        int baseHp = fighter.Hp;

        EquipRing(state, ring);

        Assert.That(fighter.Constitution, Is.EqualTo(baseCon + 2));
        // MaxHp = base + CON mod + RingMaxHpBonus.
        // +2 CON → +1 ConstitutionMod, plus +20 RingMaxHpBonus = net +21 MaxHp.
        Assert.That(fighter.MaxHp, Is.EqualTo(baseMaxHp + 21));
        // Current HP also boosted by +20 on equip
        Assert.That(fighter.Hp, Is.EqualTo(baseHp + 20));
    }

    [Test]
    public void Constitution_Ring_Unequip_Clamps_Hp()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_constitution");
        var fighter = player.Get<Fighter>()!;

        EquipRing(state, ring);
        // Set HP above the pre-ring MaxHp to force clamping
        fighter.Hp = fighter.MaxHp; // fill to max while ring is equipped

        UnequipRing(state, EquipmentSlot.LeftRing);

        // After unequip, MaxHp drops back — Hp must be clamped
        Assert.That(fighter.Hp, Is.LessThanOrEqualTo(fighter.MaxHp));
    }

    // ─── Might ring ──────────────────────────────────────────────────────────

    [Test]
    public void Might_Ring_Adds_Damage_Range()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_might");
        var fighter = player.Get<Fighter>()!;
        int baseDmgMin = fighter.DamageMin;
        int baseDmgMax = fighter.DamageMax;

        EquipRing(state, ring);
        Assert.That(fighter.DamageMin, Is.EqualTo(baseDmgMin + 1));
        Assert.That(fighter.DamageMax, Is.EqualTo(baseDmgMax + 4));

        UnequipRing(state, EquipmentSlot.LeftRing);
        Assert.That(fighter.DamageMin, Is.EqualTo(baseDmgMin));
        Assert.That(fighter.DamageMax, Is.EqualTo(baseDmgMax));
    }

    // ─── Speed rings ─────────────────────────────────────────────────────────

    [Test]
    public void Speed_Ring_Sets_RingRatio()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_speed");

        EquipRing(state, ring);

        var tracker = player.Get<SpeedBonusTracker>();
        Assert.That(tracker, Is.Not.Null);
        Assert.That(tracker!.RingRatio, Is.EqualTo(0.10).Within(0.001));

        UnequipRing(state, EquipmentSlot.LeftRing);
        Assert.That(player.Get<SpeedBonusTracker>()?.RingRatio ?? 0.0, Is.EqualTo(0.0).Within(0.001));
    }

    [Test]
    public void Hummingbird_Ring_Sets_Higher_RingRatio()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_hummingbird");

        EquipRing(state, ring);

        var tracker = player.Get<SpeedBonusTracker>();
        Assert.That(tracker, Is.Not.Null);
        Assert.That(tracker!.RingRatio, Is.EqualTo(0.25).Within(0.001));
    }

    [Test]
    public void Two_Speed_Rings_Stack_RingRatio()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring1 = GiveRing(player, factory, "ring_of_speed");
        var ring2 = GiveRing(player, factory, "ring_of_speed");

        EquipRing(state, ring1);
        EquipRing(state, ring2);

        var tracker = player.Get<SpeedBonusTracker>();
        Assert.That(tracker!.RingRatio, Is.EqualTo(0.20).Within(0.001));
    }

    // ─── Regeneration ring ───────────────────────────────────────────────────

    [Test]
    public void Regen_Ring_Heals_Every_5_Turns()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_regeneration");
        EquipRing(state, ring);

        // Reduce HP to make room for heals
        var fighter = player.Get<Fighter>()!;
        fighter.Hp = fighter.MaxHp - 10;
        int hpBefore = fighter.Hp;

        // Advance 5 turns — regen should fire on turn 5
        for (int i = 0; i < 5; i++)
            TurnController.ProcessTurn(state, PlayerAction.Wait);

        // Turn 5: TurnCount % 5 == 0 → heal
        Assert.That(fighter.Hp, Is.EqualTo(hpBefore + 1));
    }

    [Test]
    public void Regen_Ring_Does_Not_Overheal()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_regeneration");
        EquipRing(state, ring);

        // Already at max HP
        var fighter = player.Get<Fighter>()!;
        fighter.Hp = fighter.MaxHp;

        for (int i = 0; i < 5; i++)
            TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(fighter.Hp, Is.EqualTo(fighter.MaxHp));
    }

    [Test]
    public void Regen_Ring_No_Heal_On_Turn_Zero()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_regeneration");

        var fighter = player.Get<Fighter>()!;
        fighter.Hp = fighter.MaxHp - 10;
        int hpBefore = fighter.Hp;

        // Equip the ring (advances turn count to 1 via ProcessTurn internally)
        EquipRing(state, ring);

        // Verify turn count is now 1, and HP is unchanged from equip turn
        Assert.That(state.TurnCount, Is.EqualTo(1));
        // Regen doesn't fire on turn 1 (not divisible by 5)
        Assert.That(fighter.Hp, Is.EqualTo(hpBefore));
    }

    // ─── FreeAction ring ─────────────────────────────────────────────────────

    [Test]
    public void Free_Action_Blocks_Slow()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_free_action");
        EquipRing(state, ring);

        Assert.That(player.Has<FreeActionTag>(), Is.True);

        // Attempt to apply slow — should be blocked
        var events = new List<TurnEvent>();
        var effect = StatusEffectProcessor.ApplyEffect<SlowedEffect>(player, 5);

        Assert.That(effect, Is.Null, "FreeAction should block SlowedEffect");
        Assert.That(player.Has<SlowedEffect>(), Is.False);
    }

    [Test]
    public void Free_Action_Blocks_Paralysis()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_free_action");
        EquipRing(state, ring);

        var effect = StatusEffectProcessor.ApplyEffect<ImmobilizedEffect>(player, 5);

        Assert.That(effect, Is.Null, "FreeAction should block ImmobilizedEffect");
        Assert.That(player.Has<ImmobilizedEffect>(), Is.False);
    }

    [Test]
    public void Free_Action_Does_Not_Block_Poison()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_free_action");
        EquipRing(state, ring);

        var effect = StatusEffectProcessor.ApplyEffect<PoisonEffect>(player, 5);

        Assert.That(effect, Is.Not.Null, "FreeAction should NOT block PoisonEffect");
        Assert.That(player.Has<PoisonEffect>(), Is.True);
    }

    [Test]
    public void Free_Action_Tag_Removed_On_Unequip()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_free_action");

        EquipRing(state, ring);
        Assert.That(player.Has<FreeActionTag>(), Is.True);

        UnequipRing(state, EquipmentSlot.LeftRing);
        Assert.That(player.Has<FreeActionTag>(), Is.False);
    }

    // ─── Teleportation ring ──────────────────────────────────────────────────

    [Test]
    public void Teleportation_Ring_Can_Trigger_On_Hit()
    {
        // Seed 1337: we need a monster to hit the player and the ring to proc.
        // We run many turns until either a proc happens or we give up — deterministic seed.
        var (state, player) = CreateArena(seed: 42);
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_teleportation");
        EquipRing(state, ring);

        // Add an adjacent monster that will attack every turn
        var monster = new Entity(1, "Orc", 6, 5, blocksMovement: true);
        monster.Add(new Fighter(hp: 999, strength: 12, dexterity: 10, constitution: 10,
            accuracy: 100, evasion: 0, damageMin: 2, damageMax: 2)); // always hits
        monster.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        monster.Add(new AlertedState { LastKnownPlayerX = 5, LastKnownPlayerY = 5, TurnsUntilDeaggro = 999 });
        state.Map.RegisterEntity(monster);
        state.Monsters.Add(monster);

        bool teleportFired = false;
        for (int i = 0; i < 30 && !teleportFired; i++)
        {
            // Keep player alive
            player.Get<Fighter>()!.Hp = 80;
            var result = TurnController.ProcessTurn(state, PlayerAction.Wait);
            teleportFired = result.Events.OfType<TeleportEvent>()
                .Any(e => e.Reason == "ring_of_teleportation");
        }

        // Over 30 hits at 20% each, there's a (0.8^30 ≈ 0.12%) chance this fails.
        // Acceptable for a deterministic test suite.
        Assert.That(teleportFired, Is.True, "Teleportation ring should proc within 30 monster attacks");
    }

    [Test]
    public void Teleportation_Ring_Cancels_Bonus_Attacks()
    {
        // When ring procs, the monster's bonus attack chain must be cancelled.
        // We verify this by checking that after a TeleportEvent, no further AttackEvent
        // from the same monster follows in the same turn's events.
        // (This is architecture-level: the proc returns early, so no bonus attack fires.)
        var (state, player) = CreateArena(seed: 42);
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_teleportation");
        EquipRing(state, ring);

        // High-speed monster that always triggers bonus attacks
        var monster = new Entity(1, "Fast Orc", 6, 5, blocksMovement: true);
        monster.Add(new Fighter(hp: 999, strength: 12, dexterity: 10, constitution: 10,
            accuracy: 100, evasion: 0, damageMin: 1, damageMax: 1));
        monster.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        monster.Add(new AlertedState { LastKnownPlayerX = 5, LastKnownPlayerY = 5, TurnsUntilDeaggro = 999 });
        // Give it a high speed bonus so it nearly always gets bonus attacks
        monster.Add(new SpeedBonusTracker(baseRatio: 0.9));
        state.Map.RegisterEntity(monster);
        state.Monsters.Add(monster);

        bool teleportCancelledChain = false;
        for (int i = 0; i < 50 && !teleportCancelledChain; i++)
        {
            player.Get<Fighter>()!.Hp = 80;
            var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

            var events = result.Events;
            int teleportIdx = events.FindIndex(e => e is TeleportEvent te && te.Reason == "ring_of_teleportation");
            if (teleportIdx >= 0)
            {
                // No bonus attack from this monster should appear after the teleport event
                bool bonusAfterTeleport = events.Skip(teleportIdx + 1)
                    .OfType<AttackEvent>()
                    .Any(a => a.ActorId == monster.Id && a.IsBonusAttack);
                teleportCancelledChain = !bonusAfterTeleport;
                break; // found a teleport proc — verdict determined
            }
        }

        Assert.That(teleportCancelledChain, Is.True,
            "When teleportation ring procs, the bonus attack chain should be cancelled");
    }

    // ─── Ring slot auto-assignment ────────────────────────────────────────────

    [Test]
    public void Second_Ring_Auto_Redirects_To_Right_Slot()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring1 = GiveRing(player, factory, "ring_of_protection");
        var ring2 = GiveRing(player, factory, "ring_of_strength");

        EquipRing(state, ring1);
        EquipRing(state, ring2);

        var eq = player.Get<Equipment>()!;
        Assert.That(eq.LeftRing, Is.Not.Null, "Left ring should be occupied");
        Assert.That(eq.RightRing, Is.Not.Null, "Right ring should be auto-assigned");
    }

    // ─── Displaced ring effect reversal ──────────────────────────────────────

    [Test]
    public void Displaced_Ring_Effect_Reversed()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring1 = GiveRing(player, factory, "ring_of_protection");
        var ring2 = GiveRing(player, factory, "ring_of_strength");
        var ring3 = GiveRing(player, factory, "ring_of_dexterity");

        EquipRing(state, ring1); // left: protection (+2 AC)
        EquipRing(state, ring2); // right: strength (+2 STR)

        var fighter = player.Get<Fighter>()!;
        int defAfter2Rings = fighter.BaseDefense;
        int strAfter2Rings = fighter.Strength;

        // Now equip ring3 into left slot, displacing ring1 (protection)
        // ring3 goes left because left is occupied and right is also occupied → left displaces
        // Actually left is occupied AND right is occupied — no auto-redirect, ring3 replaces left
        EquipRing(state, ring3); // replaces left ring (protection removed, dexterity added)

        Assert.That(fighter.BaseDefense, Is.EqualTo(defAfter2Rings - 2), "Protection effect should be reversed");
        Assert.That(fighter.Dexterity, Is.EqualTo(14 + 2), "Dexterity ring effect should be applied");
    }

    // ─── Identification on equip ─────────────────────────────────────────────

    [Test]
    public void Ring_Identified_On_Equip()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_protection");

        // Set up identification registry so the equip-identification path fires
        var registry = new IdentificationRegistry();
        var stateWithId = new GameState(player, new List<Entity>(),
            state.Map, new SeededRandom(1337), turnLimit: 200)
        {
            IdentificationRegistry = registry,
        };

        EquipRing(stateWithId, ring);

        Assert.That(registry.IsIdentified("ring_of_protection"), Is.True,
            "Ring should be identified when equipped");
    }

    // ─── Floor transition — ReapplyRingEffects ────────────────────────────────

    [Test]
    public void Ring_Effects_Survive_Floor_Transition()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();

        // Equip a speed ring and a constitution ring to exercise multiple reapply paths
        var speedRing = GiveRing(player, factory, "ring_of_speed");
        var conRing = GiveRing(player, factory, "ring_of_constitution");
        var freeRing = GiveRing(player, factory, "ring_of_free_action");

        EquipRing(state, speedRing);  // left ring → SpeedBonusTracker.RingRatio = 0.10
        EquipRing(state, conRing);    // right ring → Constitution+2, RingMaxHpBonus+20
        // free action can't equip (both slots full) — test it separately
        UnequipRing(state, EquipmentSlot.RightRing);
        // Give con ring back to inventory and equip free action
        player.Get<Inventory>()!.Add(conRing);
        EquipRing(state, freeRing);   // right ring → FreeActionTag

        // Simulate floor transition: PlayerCarryForward.Apply creates new player entity,
        // then ReapplyRingEffects restores what can't be carried via stat copy.
        var newPlayer = PlayerCarryForward.Apply(player);
        TurnController.ReapplyRingEffects(newPlayer);

        // FreeActionTag should be restored
        Assert.That(newPlayer.Has<FreeActionTag>(), Is.True, "FreeActionTag should survive floor transition");

        // SpeedBonusTracker.RingRatio should be restored
        var tracker = newPlayer.Get<SpeedBonusTracker>();
        Assert.That(tracker, Is.Not.Null, "SpeedBonusTracker should be created by ReapplyRingEffects");
        Assert.That(tracker!.RingRatio, Is.EqualTo(0.10).Within(0.001));
    }

    [Test]
    public void Constitution_Ring_MaxHpBonus_Restored_On_Floor_Transition()
    {
        var (state, player) = CreateArena();
        var factory = CreateItemFactory();
        var ring = GiveRing(player, factory, "ring_of_constitution");
        EquipRing(state, ring);

        var oldFighter = player.Get<Fighter>()!;
        Assert.That(oldFighter.RingMaxHpBonus, Is.EqualTo(20));

        // Floor transition
        var newPlayer = PlayerCarryForward.Apply(player);
        TurnController.ReapplyRingEffects(newPlayer);

        var newFighter = newPlayer.Get<Fighter>()!;
        Assert.That(newFighter.RingMaxHpBonus, Is.EqualTo(20), "RingMaxHpBonus should be restored after floor transition");
    }
}
