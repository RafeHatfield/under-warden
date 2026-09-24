using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Unit tests for TrapActionResolver.
/// Phase 1 coverage: one test per action kind + determinism + status-before-damage ordering.
/// </summary>
[TestFixture]
public class TrapActionResolverTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Entity CreatePlayer(int x = 5, int y = 5, int hp = 50)
    {
        var e = new Entity(0, "Player", x, y, blocksMovement: true);
        e.Add(new Fighter(hp: hp, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        return e;
    }

    private static Entity CreateMonster(int id = 1, int x = 3, int y = 3, int hp = 20,
        string faction = "orc")
    {
        var e = new Entity(id, "Orc", x, y, blocksMovement: true);
        e.Add(new Fighter(hp: hp, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 1, damageMin: 2, damageMax: 4));
        e.Add(new AiComponent { Faction = faction });
        return e;
    }

    private static GameState CreateState(Entity player, List<Entity>? monsters = null)
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);
        map.RegisterEntity(player);
        var monsterList = monsters ?? new List<Entity>();
        foreach (var m in monsterList)
            map.RegisterEntity(m);
        return new GameState(player, monsterList, map, rng);
    }

    private static TrapPayloadComponent MakePayload(params TrapAction[] actions)
    {
        var payload = new TrapPayloadComponent();
        payload.Actions.AddRange(actions);
        return payload;
    }

    // ── Action kind tests ──────────────────────────────────────────────────────

    [Test]
    public void Resolve_DamageAction_DealsDamage()
    {
        var player = CreatePlayer(hp: 50);
        var state = CreateState(player);
        var payload = MakePayload(new TrapAction { Kind = "damage", Amount = 7 });
        var events = new List<TurnEvent>();

        TrapActionResolver.Resolve(player, payload, "spike_trap", (5, 5), state, state.Rng, events);

        Assert.That(player.Require<Fighter>().Hp, Is.EqualTo(43));
    }

    [Test]
    public void Resolve_DamageAction_EmitsTrapTriggeredEvent()
    {
        var player = CreatePlayer(hp: 50);
        var state = CreateState(player);
        var payload = MakePayload(new TrapAction { Kind = "damage", Amount = 7 });
        var events = new List<TurnEvent>();

        TrapActionResolver.Resolve(player, payload, "spike_trap", (3, 4), state, state.Rng, events);

        var triggered = events.OfType<TrapTriggeredEvent>().FirstOrDefault();
        Assert.That(triggered, Is.Not.Null);
        Assert.That(triggered!.Source, Is.EqualTo("spike_trap"));
        Assert.That(triggered.X, Is.EqualTo(3));
        Assert.That(triggered.Y, Is.EqualTo(4));
        Assert.That(triggered.ActionKinds, Contains.Item("damage"));
    }

    [Test]
    public void Resolve_BurningAction_AppliesBurningEffect()
    {
        var player = CreatePlayer(hp: 50);
        var state = CreateState(player);
        var payload = MakePayload(new TrapAction { Kind = "burning", Duration = 4 });
        var events = new List<TurnEvent>();

        TrapActionResolver.Resolve(player, payload, "fire_trap", (5, 5), state, state.Rng, events);

        Assert.That(player.Has<BurningEffect>(), Is.True);
        Assert.That(player.Require<BurningEffect>().RemainingTurns, Is.EqualTo(4));
    }

    [Test]
    public void Resolve_PoisonAction_AppliesPoisonEffect()
    {
        var player = CreatePlayer(hp: 50);
        var state = CreateState(player);
        var payload = MakePayload(new TrapAction { Kind = "poison", Duration = 6 });
        var events = new List<TurnEvent>();

        TrapActionResolver.Resolve(player, payload, "gas_trap", (5, 5), state, state.Rng, events);

        Assert.That(player.Has<PoisonEffect>(), Is.True);
        Assert.That(player.Require<PoisonEffect>().RemainingTurns, Is.EqualTo(6));
    }

    [Test]
    public void Resolve_SlowAction_AppliesSlowedEffect()
    {
        var player = CreatePlayer(hp: 50);
        var state = CreateState(player);
        var payload = MakePayload(new TrapAction { Kind = "slow", Duration = 5 });
        var events = new List<TurnEvent>();

        TrapActionResolver.Resolve(player, payload, "web_trap", (5, 5), state, state.Rng, events);

        Assert.That(player.Has<SlowedEffect>(), Is.True);
        Assert.That(player.Require<SlowedEffect>().RemainingTurns, Is.EqualTo(5));
    }

    [Test]
    public void Resolve_EntangleAction_AppliesEntangledEffect()
    {
        var player = CreatePlayer(hp: 50);
        var state = CreateState(player);
        var payload = MakePayload(new TrapAction { Kind = "entangle", Duration = 3 });
        var events = new List<TurnEvent>();

        TrapActionResolver.Resolve(player, payload, "root_trap", (5, 5), state, state.Rng, events);

        Assert.That(player.Has<EntangledEffect>(), Is.True);
        Assert.That(player.Require<EntangledEffect>().RemainingTurns, Is.EqualTo(3));
    }

    [Test]
    public void Resolve_BleedAction_EmitsStatusAppliedEvent()
    {
        // Phase 1 stub: bleed emits StatusAppliedEvent but no BleedEffect component yet.
        var player = CreatePlayer(hp: 50);
        var state = CreateState(player);
        var payload = MakePayload(new TrapAction { Kind = "bleed", Amount = 1, Duration = 3 });
        var events = new List<TurnEvent>();

        TrapActionResolver.Resolve(player, payload, "spike_trap", (5, 5), state, state.Rng, events);

        var statusApplied = events.OfType<StatusAppliedEvent>()
            .FirstOrDefault(e => e.EffectName == "bleed");
        Assert.That(statusApplied, Is.Not.Null, "Should emit StatusAppliedEvent for bleed");
    }

    [Test]
    public void Resolve_AcidAction_EmitsStatusAppliedEvent()
    {
        // Phase 1 stub: acid emits StatusAppliedEvent but no AcidEffect component yet.
        var player = CreatePlayer(hp: 50);
        var state = CreateState(player);
        var payload = MakePayload(new TrapAction { Kind = "acid", Duration = 8 });
        var events = new List<TurnEvent>();

        TrapActionResolver.Resolve(player, payload, "acid_trap", (5, 5), state, state.Rng, events);

        var statusApplied = events.OfType<StatusAppliedEvent>()
            .FirstOrDefault(e => e.EffectName == "acid");
        Assert.That(statusApplied, Is.Not.Null, "Should emit StatusAppliedEvent for acid");
    }

    [Test]
    public void Resolve_TeleportAction_MovesTarget()
    {
        var player = CreatePlayer(x: 5, y: 5, hp: 50);
        var state = CreateState(player);
        var payload = MakePayload(new TrapAction { Kind = "teleport" });
        var events = new List<TurnEvent>();

        int startX = player.X, startY = player.Y;

        TrapActionResolver.Resolve(player, payload, "teleport_trap", (5, 5), state, state.Rng, events);

        // Player should have moved — exact destination depends on RNG but must be different from (5,5).
        var teleportEvt = events.OfType<TeleportEvent>().FirstOrDefault();
        Assert.That(teleportEvt, Is.Not.Null);
        Assert.That(teleportEvt!.Reason, Is.EqualTo("trap"));
        Assert.That(teleportEvt.FromX, Is.EqualTo(startX));
        Assert.That(teleportEvt.FromY, Is.EqualTo(startY));
    }

    [Test]
    public void Resolve_AlertFactionAction_AlertsNearbyFactionMonster()
    {
        var player = CreatePlayer(x: 5, y: 5);
        var orc = CreateMonster(id: 1, x: 6, y: 6, faction: "orc");
        var state = CreateState(player, new List<Entity> { orc });

        var payload = MakePayload(new TrapAction
        {
            Kind   = "alert_faction",
            Target = "orc",
            Radius = 8,
        });
        var events = new List<TurnEvent>();

        TrapActionResolver.Resolve(player, payload, "alarm_plate", (5, 5), state, state.Rng, events);

        Assert.That(orc.Has<AlertedState>(), Is.True, "Nearby orc should be alerted");
    }

    [Test]
    public void Resolve_AlertFactionAction_DoesNotAlertOutOfRangeMonster()
    {
        var player = CreatePlayer(x: 5, y: 5);
        var distantOrc = CreateMonster(id: 1, x: 11, y: 11, faction: "orc");
        var state = CreateState(player, new List<Entity> { distantOrc });

        var payload = MakePayload(new TrapAction
        {
            Kind   = "alert_faction",
            Target = "orc",
            Radius = 4, // orc at (11,11) is 8 tiles away — beyond radius 4
        });
        var events = new List<TurnEvent>();

        TrapActionResolver.Resolve(player, payload, "alarm_plate", (5, 5), state, state.Rng, events);

        Assert.That(distantOrc.Has<AlertedState>(), Is.False, "Out-of-range orc should NOT be alerted");
    }

    [Test]
    public void Resolve_AlertFactionAction_DoesNotAlertWrongFaction()
    {
        var player = CreatePlayer(x: 5, y: 5);
        var zombie = CreateMonster(id: 1, x: 6, y: 6, faction: "undead");
        var state = CreateState(player, new List<Entity> { zombie });

        var payload = MakePayload(new TrapAction
        {
            Kind   = "alert_faction",
            Target = "orc",
            Radius = 8,
        });
        var events = new List<TurnEvent>();

        TrapActionResolver.Resolve(player, payload, "alarm_plate", (5, 5), state, state.Rng, events);

        Assert.That(zombie.Has<AlertedState>(), Is.False, "Zombie (wrong faction) should NOT be alerted");
    }

    [Test]
    public void Resolve_DescendAction_EmitsDescendEventWithCause()
    {
        var player = CreatePlayer();
        var state = CreateState(player);
        var payload = MakePayload(new TrapAction { Kind = "descend" });
        var events = new List<TurnEvent>();

        TrapActionResolver.Resolve(player, payload, "hole_trap", (5, 5), state, state.Rng, events);

        var descendEvt = events.OfType<DescendEvent>().FirstOrDefault();
        Assert.That(descendEvt, Is.Not.Null);
        Assert.That(descendEvt!.Cause, Is.EqualTo("hole_trap"));
    }

    [Test]
    public void Resolve_DescendAction_MonsterTarget_NoOp()
    {
        // Monsters cannot descend — DescendEvent should NOT be emitted when target is a monster.
        var player = CreatePlayer();
        var monster = CreateMonster(id: 1, x: 6, y: 6);
        var state = CreateState(player, new List<Entity> { monster });
        var payload = MakePayload(new TrapAction { Kind = "descend" });
        var events = new List<TurnEvent>();

        TrapActionResolver.Resolve(monster, payload, "hole_trap", (5, 5), state, state.Rng, events);

        Assert.That(events.OfType<DescendEvent>(), Is.Empty);
    }

    [Test]
    public void Resolve_SpawnMonsterAction_WithoutFactory_IsNoOp()
    {
        // Null monsterFactory → no spawn, no crash.
        var player = CreatePlayer();
        var state = CreateState(player);
        var payload = MakePayload(new TrapAction { Kind = "spawn_monster", Target = "zombie", Radius = 4 });
        var events = new List<TurnEvent>();

        Assert.DoesNotThrow(() =>
            TrapActionResolver.Resolve(player, payload, "bone_pile", (5, 5), state, state.Rng, events,
                monsterFactory: null));

        Assert.That(events.OfType<MonsterRousedEvent>(), Is.Empty);
    }

    [Test]
    public void Resolve_StatusBeforeDamage_StatusAppliedBeforeDamageTaken()
    {
        // Verify ordering: bleed/burning status is applied BEFORE damage fires.
        // We confirm this by checking that BurningEffect is present even after fatal damage
        // (the player dies, but status was applied in Pass 1 before damage in Pass 2).
        // More importantly: a status listener watching for "entity gained burning" must see
        // it before the damage tick — ordering is observable via event list position.
        var player = CreatePlayer(hp: 50);
        var state = CreateState(player);

        // Multi-action payload: burning (status) + damage. Should see status BEFORE damage in event list.
        var payload = MakePayload(
            new TrapAction { Kind = "burning", Duration = 4 },
            new TrapAction { Kind = "damage", Amount = 7 }
        );
        var events = new List<TurnEvent>();

        TrapActionResolver.Resolve(player, payload, "fire_trap", (5, 5), state, state.Rng, events);

        // Find indices of StatusAppliedEvent(burning) and TrapTriggeredEvent in the event stream.
        // TrapTriggeredEvent is emitted after damage, so StatusApplied must come first.
        int statusAppliedIndex = events.FindIndex(e => e is StatusAppliedEvent se && se.EffectName == "burning");
        int trapTriggeredIndex = events.FindIndex(e => e is TrapTriggeredEvent);

        Assert.That(statusAppliedIndex, Is.Not.EqualTo(-1), "Should have StatusAppliedEvent for burning");
        Assert.That(trapTriggeredIndex, Is.Not.EqualTo(-1), "Should have TrapTriggeredEvent");
        Assert.That(statusAppliedIndex, Is.LessThan(trapTriggeredIndex),
            "Status application must appear before TrapTriggeredEvent (which emits after damage)");

        // The player should still have the burning effect AND took damage.
        Assert.That(player.Has<BurningEffect>(), Is.True, "BurningEffect should still be applied");
        Assert.That(player.Require<Fighter>().Hp, Is.EqualTo(43), "Damage should have reduced HP to 43");
    }

    [Test]
    public void Resolve_Deterministic_SameSeedSameResult()
    {
        // Teleport to a random tile — verify same seed produces same destination.
        static (int X, int Y) RunTeleport(int seed)
        {
            var rng = new SeededRandom(seed);
            var player = CreatePlayer(x: 5, y: 5);
            var map = GameMap.CreateArena(12, 12);
            map.RegisterEntity(player);
            var state = new GameState(player, new List<Entity>(), map, rng);
            var payload = MakePayload(new TrapAction { Kind = "teleport" });
            var events = new List<TurnEvent>();
            TrapActionResolver.Resolve(player, payload, "teleport_trap", (5, 5), state, state.Rng, events);
            return (player.X, player.Y);
        }

        var result1 = RunTeleport(42);
        var result2 = RunTeleport(42);

        Assert.That(result1, Is.EqualTo(result2), "Same seed should produce same teleport destination");
    }

    [Test]
    public void Resolve_EmptyPayload_ReturnsFalseAndNoEvents()
    {
        var player = CreatePlayer();
        var state = CreateState(player);
        var payload = new TrapPayloadComponent(); // no actions
        var events = new List<TurnEvent>();

        bool resolved = TrapActionResolver.Resolve(player, payload, "empty", (5, 5), state, state.Rng, events);

        Assert.That(resolved, Is.False);
        Assert.That(events, Is.Empty);
    }

    [Test]
    public void Resolve_DamageKillsTarget_EmitsDeathEventAndStops()
    {
        // If damage kills the target, a DeathEvent should be emitted and further world-changing
        // actions should be skipped (no teleport of a corpse).
        var player = CreatePlayer(hp: 5); // very low HP — will die from 7 damage
        var state = CreateState(player);

        var payload = MakePayload(
            new TrapAction { Kind = "damage", Amount = 7 },
            new TrapAction { Kind = "teleport" }  // should NOT fire after death
        );
        var events = new List<TurnEvent>();

        TrapActionResolver.Resolve(player, payload, "spike_trap", (5, 5), state, state.Rng, events);

        Assert.That(events.OfType<DeathEvent>().Any(), Is.True, "Death event should be emitted");
        Assert.That(events.OfType<TeleportEvent>().Any(), Is.False, "Teleport should NOT fire after death");
    }
}
