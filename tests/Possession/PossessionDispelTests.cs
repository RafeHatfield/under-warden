using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Possession;

/// <summary>
/// Phase 5 tests: Dispel spell wiring through SpellResolver.
/// Covers both PlayerInitiated and WardenInitiated PossessionEffect paths,
/// generic status effect removal, out-of-range/no-effect failure cases,
/// and wraith/lich immunity to possession.
/// </summary>
[TestFixture]
public class PossessionDispelTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Entity MakePlayer(int x = 3, int y = 3, int hp = 30)
    {
        var p = new Entity(0, "Player", x, y, blocksMovement: true);
        p.Add(new Fighter(hp: hp, strength: 12, dexterity: 12, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 2, damageMax: 4));
        return p;
    }

    private static Entity MakeMonster(int id = 1, string species = "orc_grunt",
        int x = 5, int y = 3)
    {
        var m = new Entity(id, "Orc Grunt", x, y, blocksMovement: true);
        m.Add(new Fighter(hp: 20, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 1, damageMin: 2, damageMax: 4));
        m.Add(new AiComponent { Faction = "orc" });
        m.Add(new SpeciesTag(species));
        return m;
    }

    private static GameState MakeState(Entity player, Entity? monster = null, int depth = 1)
    {
        var map = GameMap.CreateArena(20, 20);
        map.RegisterEntity(player);
        var monsters = new List<Entity>();
        if (monster != null)
        {
            map.RegisterEntity(monster);
            monsters.Add(monster);
        }
        return new GameState(player, monsters, map, new SeededRandom(1337)) { CurrentDepth = depth };
    }

    private static SpellEffect MakeDispelSpell(int range = 5) =>
        new() { SpellId = "dispel", Targeting = TargetingMode.SingleTarget, Range = range };

    // ─── Dispel on PlayerInitiated possession ─────────────────────────────────

    [Test]
    public void Dispel_OnPlayerInitiatedPossession_ExitsPossession()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());
        Assert.That(host.Has<PossessionEffect>(), Is.True);

        var spell = MakeDispelSpell();
        var events = SpellResolver.Resolve(player, spell, state, targetEntityId: host.Id);

        Assert.That(host.Has<PossessionEffect>(), Is.False, "Dispel should remove possession.");
        Assert.That(events.OfType<PossessionExitedEvent>().Single().Reason, Is.EqualTo("dispelled"));
        Assert.That(events.OfType<SpellEvent>().Single().Success, Is.True);
    }

    [Test]
    public void Dispel_OnPlayerInitiatedPossession_AppliesDisorientation_ToCaster()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        var spell = MakeDispelSpell();
        SpellResolver.Resolve(player, spell, state, targetEntityId: host.Id);

        Assert.That(player.Has<DisorientationEffect>(), Is.True,
            "PlayerInitiated dispel applies DisorientationEffect to the home body.");
    }

    // ─── Dispel on WardenInitiated possession ─────────────────────────────────

    [Test]
    public void Dispel_OnWardenInitiatedPossession_CollapsesHost_RecordsPastSelfFreed()
    {
        var player = MakePlayer();
        var host = MakeMonster(species: "hall_warden");
        var state = MakeState(player, host);

        // Manually attach a WardenInitiated PossessionEffect (as DungeonFloorBuilder would at spawn)
        var effect = new PossessionEffect
        {
            PossessorEntityId = PossessionConfig.WardenPossessorSentinelId,
            Source = PossessionSource.WardenInitiated,
            DrainPerTurn = 0,
            EnteredTurn = 0,
            RemainingTurns = int.MaxValue,
        };
        host.Add(effect);

        var spell = MakeDispelSpell();
        var events = SpellResolver.Resolve(player, spell, state, targetEntityId: host.Id);

        Assert.That(host.Has<PossessionEffect>(), Is.False, "Dispel removes WardenInitiated effect.");
        Assert.That(host.Get<Fighter>()!.Hp, Is.EqualTo(0), "Host HP should be 0 after warden dispel.");
        Assert.That(events.OfType<DeathEvent>().Single().IsPossessionInduced, Is.True);
        Assert.That(events.OfType<SpellEvent>().Single().Success, Is.True);
        // RecordTrait("past_self","freed") should have been called — check via knowledge
        Assert.That(state.Knowledge.GetEntry("past_self")?.TraitsDiscovered?.Contains("freed"), Is.True,
            "Warden dispel should record 'past_self freed' trait.");
    }

    // ─── Dispel on generic status effect ─────────────────────────────────────

    [Test]
    public void Dispel_OnEntityWithGenericEffect_RemovesEffect()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        host.Add(new SlowedEffect { RemainingTurns = 5 });
        Assert.That(host.Has<SlowedEffect>(), Is.True);

        var spell = MakeDispelSpell();
        var events = SpellResolver.Resolve(player, spell, state, targetEntityId: host.Id);

        Assert.That(host.Has<SlowedEffect>(), Is.False, "Dispel should remove SlowedEffect.");
        Assert.That(events.OfType<StatusExpiredEvent>().Single().EffectName, Is.EqualTo("slowed"));
        Assert.That(events.OfType<StatusExpiredEvent>().Single().Reason, Is.EqualTo("dispelled"));
        Assert.That(events.OfType<SpellEvent>().Single().Success, Is.True);
    }

    // ─── Failure cases ────────────────────────────────────────────────────────

    [Test]
    public void Dispel_OnEntityWithNoEffects_FailsGracefully()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);

        var spell = MakeDispelSpell();
        var events = SpellResolver.Resolve(player, spell, state, targetEntityId: host.Id);

        Assert.That(events.OfType<SpellEvent>().Single().Success, Is.False,
            "Dispel on an entity with no effects should fail.");
    }

    [Test]
    public void Dispel_OutOfRange_Fails()
    {
        var player = MakePlayer(x: 1, y: 1);
        var host = MakeMonster(x: 10, y: 10);  // distance > 5
        var state = MakeState(player, host);
        host.Add(new SlowedEffect { RemainingTurns = 5 });

        var spell = MakeDispelSpell(range: 5);
        var events = SpellResolver.Resolve(player, spell, state, targetEntityId: host.Id);

        Assert.That(events.OfType<SpellEvent>().Single().Success, Is.False, "Dispel out of range should fail.");
        Assert.That(host.Has<SlowedEffect>(), Is.True, "Effect should remain after failed dispel.");
    }

    [Test]
    public void Dispel_PossessionTakesPriorityOverOtherEffects()
    {
        // Entity has both PossessionEffect and SlowedEffect — Possession should be removed first.
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());
        host.Add(new SlowedEffect { RemainingTurns = 5 });

        var spell = MakeDispelSpell();
        SpellResolver.Resolve(player, spell, state, targetEntityId: host.Id);

        Assert.That(host.Has<PossessionEffect>(), Is.False, "PossessionEffect removed (priority).");
        Assert.That(host.Has<SlowedEffect>(), Is.True, "SlowedEffect remains — dispel removes one effect.");
    }

    // ─── Dispel targeting a CORPSE (visible-corpses feature — past-Sasha visibility half) ─────

    [Test]
    public void Dispel_TargetingWardenPossessedCorpse_Resolves()
    {
        // The past-Sasha host is a CORPSE (Has<CorpseComponent>, in state.Corpses) that the Warden is
        // animating via a WardenInitiated PossessionEffect. Until corpses were visible you could neither
        // see nor tap it; now the presentation resolves a corpse carrying a PossessionEffect as a
        // SingleTarget (InputHandler.FindPossessedCorpseAt). This guards the Logic half: aiming Spell-
        // Break at that corpse entity must RESOLVE. The resolver already looks targets up in
        // state.Monsters (which includes corpses), so a corpse is a valid Spell-Break target.
        var player = MakePlayer();
        var host = MakeMonster(species: "hall_warden"); // MakeMonster gives it a Fighter (the animated body)
        var state = MakeState(player, host);

        // Classify the animated body as a corpse: CorpseComponent + dual membership in state.Corpses.
        host.Add(new CorpseComponent
        {
            OriginalMonsterId = "hall_warden", OriginalName = "Warden",
            State = CorpseState.Fresh, CorpseId = "c_warden",
        });
        state.Corpses.Add(host);

        // Warden animates it (mirrors DungeonFloorBuilder's WardenInitiated PossessionEffect at spawn).
        host.Add(new PossessionEffect
        {
            PossessorEntityId = PossessionConfig.WardenPossessorSentinelId,
            Source = PossessionSource.WardenInitiated,
            DrainPerTurn = 0,
            EnteredTurn = 0,
            RemainingTurns = int.MaxValue,
        });

        Assume.That(state.Corpses, Does.Contain(host), "pre-condition: target is a corpse.");
        Assume.That(host.Has<CorpseComponent>(), Is.True);

        var spell = MakeDispelSpell();
        var events = SpellResolver.Resolve(player, spell, state, targetEntityId: host.Id);

        Assert.That(events.OfType<SpellEvent>().Single().Success, Is.True,
            "Dispel aimed at a Warden-possessed corpse must resolve — a corpse is a valid Spell-Break target.");
        Assert.That(host.Has<PossessionEffect>(), Is.False,
            "Dispel removes the WardenInitiated effect from the corpse (collapses the host).");
    }

    // ─── Possession immunity (wraith / lich) ──────────────────────────────────

    [Test]
    public void IsValidTarget_ReturnsFalse_ForWraith()
    {
        // Wraith has "possessed" in status_immunities after Phase 5 change.
        // We test via StatusImmunityComponent directly (mirroring what entities.yaml defines).
        var player = MakePlayer();
        var wraith = new Entity(1, "Wraith", 5, 3, blocksMovement: true);
        wraith.Add(new Fighter(hp: 20, strength: 10, dexterity: 18, constitution: 10,
            accuracy: 3, evasion: 4, damageMin: 5, damageMax: 9));
        wraith.Add(new AiComponent { Faction = "undead" });
        wraith.Add(new SpeciesTag("wraith"));
        wraith.Add(new StatusImmunityComponent(new[] { "confusion", "slow", "fear", "possessed" }));
        var state = MakeState(player, wraith);

        Assert.That(PossessionSystem.IsValidTarget(wraith, player, state), Is.False,
            "Wraith is immune to possession.");
    }

    [Test]
    public void IsValidTarget_ReturnsFalse_ForLich()
    {
        var player = MakePlayer();
        var lich = new Entity(1, "Lich", 5, 3, blocksMovement: true);
        lich.Add(new Fighter(hp: 60, strength: 10, dexterity: 14, constitution: 14,
            accuracy: 5, evasion: 3, damageMin: 3, damageMax: 6));
        lich.Add(new AiComponent { Faction = "undead" });
        lich.Add(new SpeciesTag("lich"));
        lich.Add(new StatusImmunityComponent(new[] { "confusion", "slow", "fear", "poison", "bleed", "possessed" }));
        var state = MakeState(player, lich);

        Assert.That(PossessionSystem.IsValidTarget(lich, player, state), Is.False,
            "Lich is immune to possession.");
    }
}
