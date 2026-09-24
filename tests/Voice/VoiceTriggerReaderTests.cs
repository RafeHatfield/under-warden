using System.Collections.Generic;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Voice;
using NUnit.Framework;

namespace UnderWarden.Tests.Voice;

/// <summary>
/// The M1.5 trigger bus derivation (pure logic — the Presentation glue just calls this each
/// TurnCompleted). Under scheme Option A the reader emits the SPECIFIC authored pool key
/// ("hp_threshold.25", "trap_first.spike_trap", "species_first_sight.orc_grunt", "long_idle") — the
/// scheduler resolves the tier by family prefix. Edge-triggered families fire once per condition-onset;
/// per-event families fire on their event; first-sight fires once per species.
/// </summary>
[TestFixture]
public class VoiceTriggerReaderTests
{
    private static Fighter Body(int hp) =>
        new(hp: hp, strength: 10, dexterity: 10, constitution: 10, accuracy: 5, evasion: 0, damageMin: 1, damageMax: 2);

    private static GameState State(out Entity player, out Entity monster)
    {
        var map = new GameMap(12, 12);
        for (int x = 0; x < 12; x++)
            for (int y = 0; y < 12; y++) map.SetTile(x, y, TileKind.Floor);

        player = new Entity(1, "Player", 5, 5, blocksMovement: true);
        player.Add(Body(100));
        map.RegisterEntity(player);

        monster = new Entity(2, "Orc", 6, 5, blocksMovement: true);
        monster.Add(new SpeciesTag("orc_grunt"));
        monster.Add(Body(30));

        return new GameState(player, new List<Entity> { monster }, map, new SeededRandom(1), turnLimit: 1000);
    }

    private static TurnResult Turn(params TurnEvent[] events) => new() { Events = new List<TurnEvent>(events) };

    [Test]
    public void HpThreshold_EmitsSpecificBandKey_EdgeTriggeredOnMoreSevereBand()
    {
        var reader = new VoiceTriggerReader();
        var state = State(out _, out _);

        state.PlayerFighter.Hp = 20;   // 20/100 = 20% <= 25% band
        Assert.That(reader.Read(Turn(), state), Does.Contain("hp_threshold.25"));
        Assert.That(reader.Read(Turn(), state), Does.Not.Contain("hp_threshold.25"), "same band must not re-fire.");

        state.PlayerFighter.Hp = 8;    // 8% <= 10% — a MORE-severe band fires
        Assert.That(reader.Read(Turn(), state), Does.Contain("hp_threshold.10"));

        state.PlayerFighter.Hp = 90;   // recover above all bands — re-arm
        Assert.That(reader.Read(Turn(), state), Is.Empty);
        state.PlayerFighter.Hp = 20;   // drop again → 25 band re-fires
        Assert.That(reader.Read(Turn(), state), Does.Contain("hp_threshold.25"), "a fresh descent re-fires.");
    }

    [Test]
    public void HpThreshold_DoesNotEmitBareFamilyKey()
    {
        var reader = new VoiceTriggerReader();
        var state = State(out _, out _);
        state.PlayerFighter.Hp = 20;
        Assert.That(reader.Read(Turn(), state), Does.Not.Contain("hp_threshold"),
            "the reader emits the specific band key, never the bare family.");
    }

    [Test]
    public void TrapFirst_EmitsPerTypeKey_OncePerType_PlayerOnly()
    {
        var reader = new VoiceTriggerReader();
        var state = State(out var player, out var monster);

        Assert.That(reader.Read(Turn(new TrapTriggeredEvent { TargetId = monster.Id, Source = "spike_trap" }), state),
            Is.Empty, "a trap that hit a monster is not the player's.");

        Assert.That(reader.Read(Turn(new TrapTriggeredEvent { TargetId = player.Id, Source = "spike_trap" }), state),
            Does.Contain("trap_first.spike_trap"));
        Assert.That(reader.Read(Turn(new TrapTriggeredEvent { TargetId = player.Id, Source = "spike_trap" }), state),
            Does.Not.Contain("trap_first.spike_trap"), "the same trap type does not re-fire this run.");
        Assert.That(reader.Read(Turn(new TrapTriggeredEvent { TargetId = player.Id, Source = "fire_trap" }), state),
            Does.Contain("trap_first.fire_trap"), "a new trap type fires.");
    }

    [Test]
    public void LongIdle_FiresOnWaitOrSkip()
    {
        var reader = new VoiceTriggerReader();
        var state = State(out _, out _);
        Assert.That(reader.Read(Turn(new WaitEvent()), state), Does.Contain("long_idle"));
    }

    [Test]
    public void FirstSight_EmitsSpeciesTypeKey_OncePerSpecies()
    {
        var reader = new VoiceTriggerReader();
        var state = State(out _, out var monster);

        // Out of FOV — no first sight.
        Assert.That(reader.Read(Turn(), state), Is.Empty);

        state.Map.SetVisible(monster.X, monster.Y);
        Assert.That(reader.Read(Turn(), state), Does.Contain("species_first_sight.orc_grunt"));
        Assert.That(reader.Read(Turn(), state), Does.Not.Contain("species_first_sight.orc_grunt"),
            "a species already seen this run does not re-fire.");

        reader.Reset();   // new run
        Assert.That(reader.Read(Turn(), state), Does.Contain("species_first_sight.orc_grunt"),
            "reset re-arms first-sight for a new run.");
    }
}
