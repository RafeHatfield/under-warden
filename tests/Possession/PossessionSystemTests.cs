using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Possession;

/// <summary>
/// Phase 1 tests: PossessionEffect primitive, state machine transitions,
/// visibility constraint, drain clock, and knowledge integration.
/// §17 test plan from plan_possession_system.md.
/// </summary>
[TestFixture]
public class PossessionSystemTests
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
        int x = 5, int y = 3, bool immune = false)
    {
        var m = new Entity(id, "Orc Grunt", x, y, blocksMovement: true);
        m.Add(new Fighter(hp: 20, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 1, damageMin: 2, damageMax: 4));
        m.Add(new AiComponent { Faction = "orc" });
        m.Add(new SpeciesTag(species));
        if (immune)
            m.Add(new StatusImmunityComponent(new[] { "possessed" }));
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

    // ─── §1 — PossessionEffect primitive ─────────────────────────────────────

    [Test]
    public void Enter_AppliesPossessionEffect_ToHost()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();

        bool entered = PossessionSystem.Enter(host, state, events);

        Assert.That(entered, Is.True);
        Assert.That(host.Has<PossessionEffect>(), Is.True);
    }

    [Test]
    public void Enter_MarksHomeBodyWithUnattendedBodyTag()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();

        PossessionSystem.Enter(host, state, events);

        Assert.That(player.Has<UnattendedBodyTag>(), Is.True);
    }

    [Test]
    public void Enter_EmitsPossessionEnteredEvent()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();

        PossessionSystem.Enter(host, state, events);

        var entered = events.OfType<PossessionEnteredEvent>().SingleOrDefault();
        Assert.That(entered, Is.Not.Null);
        Assert.That(entered!.HostEntityId, Is.EqualTo(host.Id));
    }

    [Test]
    public void Enter_SetsEffectFields_Correctly()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host, depth: 5);
        var events = new List<TurnEvent>();
        state.TurnCount = 42;

        PossessionSystem.Enter(host, state, events);

        var effect = host.Get<PossessionEffect>()!;
        Assert.That(effect.PossessorEntityId, Is.EqualTo(player.Id));
        Assert.That(effect.OriginatorBodyId, Is.EqualTo(player.Id));
        Assert.That(effect.Source, Is.EqualTo(PossessionSource.PlayerInitiated));
        Assert.That(effect.EnteredTurn, Is.EqualTo(42));
        Assert.That(effect.DrainPerTurn, Is.EqualTo(PossessionConfig.DrainPerTurnByDepth(5)));
    }

    // ─── ControlledEntity computed property ──────────────────────────────────

    [Test]
    public void ControlledEntity_ReturnPlayer_WhenNotPossessing()
    {
        var player = MakePlayer();
        var state = MakeState(player);

        Assert.That(state.ControlledEntity, Is.SameAs(player));
    }

    [Test]
    public void ControlledEntity_ReturnsHost_DuringActivePossession()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();

        PossessionSystem.Enter(host, state, events);

        Assert.That(state.ControlledEntity, Is.SameAs(host));
    }

    // ─── §8.1 — Voluntary exit ────────────────────────────────────────────────

    [Test]
    public void ExitVoluntary_RemovesPossessionEffect_FromHost()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();
        PossessionSystem.Enter(host, state, events);

        PossessionSystem.ExitVoluntary(state, events);

        Assert.That(host.Has<PossessionEffect>(), Is.False);
    }

    [Test]
    public void ExitVoluntary_RemovesUnattendedBodyTag_FromPlayer()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();
        PossessionSystem.Enter(host, state, events);

        PossessionSystem.ExitVoluntary(state, events);

        Assert.That(player.Has<UnattendedBodyTag>(), Is.False);
    }

    [Test]
    public void ExitVoluntary_RevertsControlledEntity_ToPlayer()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();
        PossessionSystem.Enter(host, state, events);

        PossessionSystem.ExitVoluntary(state, events);

        Assert.That(state.ControlledEntity, Is.SameAs(player));
    }

    [Test]
    public void ExitVoluntary_EmitsPossessionExitedEvent()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();
        PossessionSystem.Enter(host, state, events);

        PossessionSystem.ExitVoluntary(state, events);

        var exited = events.OfType<PossessionExitedEvent>().SingleOrDefault();
        Assert.That(exited, Is.Not.Null);
        Assert.That(exited!.Reason, Is.EqualTo("voluntary"));
    }

    // ─── §11 — Knowledge unlock ───────────────────────────────────────────────

    [Test]
    public void ExitVoluntary_After5OrMoreTurns_RecordsTier3Trait()
    {
        var player = MakePlayer();
        var host = MakeMonster(species: "orc_grunt");
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();

        state.TurnCount = 0;
        PossessionSystem.Enter(host, state, events);
        state.TurnCount = 5; // held for exactly 5 turns

        PossessionSystem.ExitVoluntary(state, events);

        var entry = state.Knowledge.GetEntry("orc_grunt");
        Assert.That(entry?.TraitsDiscovered.Contains("possessed_by_player"), Is.True,
            "Voluntary exit after ≥5 turns should unlock possessed_by_player trait.");
    }

    [Test]
    public void ExitVoluntary_After1Turn_OnlyRecordsEngagement()
    {
        var player = MakePlayer();
        var host = MakeMonster(species: "orc_grunt");
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();

        state.TurnCount = 0;
        PossessionSystem.Enter(host, state, events);
        state.TurnCount = 1;

        PossessionSystem.ExitVoluntary(state, events);

        var entry = state.Knowledge.GetEntry("orc_grunt");
        Assert.That(entry?.TraitsDiscovered.Contains("possessed_by_player"), Is.False,
            "Short possession should not unlock Tier 3.");
        Assert.That(entry?.EngagedCount ?? 0, Is.GreaterThan(0),
            "Short possession should still record engagement.");
    }

    // ─── §1 — Immunity check ──────────────────────────────────────────────────

    [Test]
    public void Enter_ReturnsFalse_WhenTargetIsImmunetoPossession()
    {
        var player = MakePlayer();
        var host = MakeMonster(immune: true);
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();

        bool entered = PossessionSystem.Enter(host, state, events);

        Assert.That(entered, Is.False);
        Assert.That(host.Has<PossessionEffect>(), Is.False);
    }

    // ─── §4 — Visibility constraint ──────────────────────────────────────────

    [Test]
    public void CheckVisibilityConstraint_ForcesExit_WhenHostTooFar()
    {
        var player = MakePlayer(x: 3, y: 3);
        var host = MakeMonster(x: 5, y: 3); // within range initially
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();
        PossessionSystem.Enter(host, state, events);
        events.Clear();

        // Move host out of range (> 4 tiles Chebyshev)
        host.X = 10;

        PossessionSystem.CheckVisibilityConstraint(state, events);

        Assert.That(host.Has<PossessionEffect>(), Is.False);
        Assert.That(events.OfType<PossessionExitedEvent>().Single().Reason, Is.EqualTo("visibility_broken"));
    }

    [Test]
    public void CheckVisibilityConstraint_ForcesExit_WhenWallBlocksLOS()
    {
        var player = MakePlayer(x: 3, y: 3);
        var host = MakeMonster(x: 6, y: 3); // within 4 tiles
        var state = MakeState(player, host);

        // Place a wall between player and host
        state.Map.SetTile(5, 3, TileKind.Wall);

        var events = new List<TurnEvent>();
        PossessionSystem.Enter(host, state, events);
        events.Clear();

        PossessionSystem.CheckVisibilityConstraint(state, events);

        Assert.That(host.Has<PossessionEffect>(), Is.False, "Wall blocking LOS should force exit.");
        Assert.That(events.OfType<PossessionExitedEvent>().Single().Reason, Is.EqualTo("visibility_broken"));
    }

    [Test]
    public void CheckVisibilityConstraint_NoExit_WhenWithinRangeAndClearLOS()
    {
        var player = MakePlayer(x: 3, y: 3);
        var host = MakeMonster(x: 5, y: 3); // 2 tiles away, clear LOS
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();
        PossessionSystem.Enter(host, state, events);
        events.Clear();

        PossessionSystem.CheckVisibilityConstraint(state, events);

        Assert.That(host.Has<PossessionEffect>(), Is.True);
        Assert.That(events.OfType<PossessionExitedEvent>(), Is.Empty);
    }

    // ─── §7 — Drain clock ────────────────────────────────────────────────────

    [Test]
    public void ApplyDrainTick_DeductsHpFromHomeBody()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host, depth: 5); // depth 5 → drain 1
        var events = new List<TurnEvent>();
        PossessionSystem.Enter(host, state, events);
        int hpBefore = state.PlayerFighter.Hp;
        events.Clear();

        PossessionSystem.ApplyDrainTick(state, events);

        Assert.That(state.PlayerFighter.Hp, Is.EqualTo(hpBefore - PossessionConfig.DrainPerTurnByDepth(5)));
        Assert.That(events.OfType<PossessionDrainEvent>(), Is.Not.Empty);
    }

    [Test]
    public void ApplyDrainTick_SafetyRail_ClampsToOneHp_AndForcesExit()
    {
        var player = MakePlayer();
        player.Get<Fighter>()!.Hp = 1; // home body at 1 HP already
        var host = MakeMonster();
        var state = MakeState(player, host, depth: 5); // drain=1
        var events = new List<TurnEvent>();
        PossessionSystem.Enter(host, state, events);
        events.Clear();

        PossessionSystem.ApplyDrainTick(state, events);

        Assert.That(state.PlayerFighter.Hp, Is.EqualTo(1), "Drain should not kill home body.");
        Assert.That(host.Has<PossessionEffect>(), Is.False, "Safety rail should force exit.");
        Assert.That(events.OfType<PossessionExitedEvent>().Single().Reason, Is.EqualTo("voluntary"));
    }

    [Test]
    public void ApplyDrainTick_DrainScales_WithDepth()
    {
        int drain1 = PossessionConfig.DrainPerTurnByDepth(1);
        int drain9 = PossessionConfig.DrainPerTurnByDepth(9);
        int drain17 = PossessionConfig.DrainPerTurnByDepth(17);

        Assert.That(drain1, Is.EqualTo(1));
        Assert.That(drain9, Is.EqualTo(2));
        Assert.That(drain17, Is.EqualTo(3));
    }

    // ─── §8.2 — Host died ────────────────────────────────────────────────────

    [Test]
    public void OnPossessionInducedHostDeath_ExitsPossession_NoKillCredit()
    {
        var player = MakePlayer();
        var host = MakeMonster(species: "orc_grunt");
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();
        PossessionSystem.Enter(host, state, events);
        events.Clear();

        PossessionSystem.OnPossessionInducedHostDeath(host, state, events);

        Assert.That(host.Has<PossessionEffect>(), Is.False);
        Assert.That(player.Has<UnattendedBodyTag>(), Is.False);
        Assert.That(state.ControlledEntity, Is.SameAs(player));

        // Should have DeathEvent but NO kill credit → RecordEngaged only (not RecordKilled)
        Assert.That(events.OfType<DeathEvent>(), Is.Not.Empty);
        var entry = state.Knowledge.GetEntry("orc_grunt");
        Assert.That(entry?.TraitsDiscovered.Contains("possessed_by_player"), Is.False,
            "Host death should not grant Tier 3 unlock.");
        Assert.That(entry?.KilledCount, Is.EqualTo(0), "Host death should not count as a kill.");
    }

    // ─── §8.5 — Dispel (PlayerInitiated) ─────────────────────────────────────

    [Test]
    public void OnPossessionDispelled_PlayerInitiated_ExitsAndAppliesDisorientation()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();
        PossessionSystem.Enter(host, state, events);
        var effect = host.Get<PossessionEffect>()!;
        events.Clear();

        PossessionSystem.OnPossessionDispelled(host, effect, state, events);

        Assert.That(host.Has<PossessionEffect>(), Is.False);
        Assert.That(player.Has<DisorientationEffect>(), Is.True,
            "Dispelled player possession should leave home body disoriented.");
        Assert.That(events.OfType<PossessionExitedEvent>().Single().Reason, Is.EqualTo("dispelled"));
    }

    // ─── §8.5 — Dispel (WardenInitiated / Variant 3) ─────────────────────────

    [Test]
    public void OnPossessionDispelled_WardenInitiated_CollapsesHost_RecordsFreedTrait()
    {
        var player = MakePlayer();
        var host = MakeMonster(species: "hall_warden");
        var state = MakeState(player, host);

        // Simulate Warden-placed possession (as DungeonFloorBuilder would do for past-Sasha)
        var wardenEffect = new PossessionEffect
        {
            PossessorEntityId = PossessionConfig.WardenPossessorSentinelId,
            OriginatorBodyId = null,
            DrainPerTurn = 0,
            Source = PossessionSource.WardenInitiated,
            EnteredTurn = 0,
        };
        host.Add(wardenEffect);

        var events = new List<TurnEvent>();

        PossessionSystem.OnPossessionDispelled(host, wardenEffect, state, events);

        Assert.That(host.Get<Fighter>()!.Hp, Is.EqualTo(0), "Variant 3 dispel should collapse the host.");
        var entry = state.Knowledge.GetEntry("past_self");
        Assert.That(entry?.TraitsDiscovered.Contains("freed"), Is.True,
            "Variant 3 should record past_self:freed, not Hall Warden kill credit.");
        var entry2 = state.Knowledge.GetEntry("hall_warden");
        Assert.That(entry2?.KilledCount ?? 0, Is.EqualTo(0), "No Hall Warden kill credit on Variant 3.");
    }

    // ─── §3 — IsValidTarget ───────────────────────────────────────────────────

    [Test]
    public void IsValidTarget_False_WhenAlreadyPossessed()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();
        PossessionSystem.Enter(host, state, events);

        Assert.That(PossessionSystem.IsValidTarget(host, player, state), Is.False,
            "Cannot possess an already-possessed entity.");
    }

    [Test]
    public void IsValidTarget_False_WhenOutOfRange()
    {
        var player = MakePlayer(x: 1, y: 1);
        var host = MakeMonster(x: 10, y: 1); // 9 tiles away
        var state = MakeState(player, host);

        Assert.That(PossessionSystem.IsValidTarget(host, player, state), Is.False);
    }

    [Test]
    public void IsValidTarget_True_WhenAdjacentNoWall()
    {
        var player = MakePlayer(x: 3, y: 3);
        var host = MakeMonster(x: 4, y: 3);
        var state = MakeState(player, host);

        Assert.That(PossessionSystem.IsValidTarget(host, player, state), Is.True);
    }

    // ─── §4 — HasLineOfSight ──────────────────────────────────────────────────

    [Test]
    public void HasLineOfSight_True_WhenClearPath()
    {
        var map = GameMap.CreateArena(10, 10);
        Assert.That(map.HasLineOfSight(1, 5, 8, 5), Is.True);
    }

    [Test]
    public void HasLineOfSight_False_WhenWallInBetween()
    {
        var map = GameMap.CreateArena(10, 10);
        map.SetTile(5, 5, TileKind.Wall);

        Assert.That(map.HasLineOfSight(1, 5, 9, 5), Is.False);
    }

    [Test]
    public void HasLineOfSight_True_ForSameTile()
    {
        var map = GameMap.CreateArena(10, 10);
        Assert.That(map.HasLineOfSight(5, 5, 5, 5), Is.True);
    }

    // ─── PossessionConfig ─────────────────────────────────────────────────────

    [Test]
    public void PossessionConfig_DrainPerTurnByDepth_BandBoundaries()
    {
        Assert.That(PossessionConfig.DrainPerTurnByDepth(8), Is.EqualTo(1));
        Assert.That(PossessionConfig.DrainPerTurnByDepth(9), Is.EqualTo(2));
        Assert.That(PossessionConfig.DrainPerTurnByDepth(16), Is.EqualTo(2));
        Assert.That(PossessionConfig.DrainPerTurnByDepth(17), Is.EqualTo(3));
        Assert.That(PossessionConfig.DrainPerTurnByDepth(25), Is.EqualTo(3));
    }

    // ─── Phase 3: Near-death warning ─────────────────────────────────────────

    [Test]
    public void ApplyDrainTick_EmitsNearDeathWarning_WhenHomeFallsToQuarterHp()
    {
        // MakePlayer(hp:20, con:12) → MaxHp = 20 + ConstitutionMod(12) = 21.
        // Set current HP to 6. Drain 1 → 5. floor(21 * 0.25) = 5 → threshold hit.
        var player = MakePlayer(hp: 20);  // MaxHp = 21 (BaseMaxHp 20 + ConstitutionMod 1)
        var fighter = player.Get<Fighter>()!;
        fighter.Hp = 6;   // drain by 1 → 5 ≤ 21/4 = 5 → fires warning
        var host = MakeMonster();
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        var events = new List<TurnEvent>();
        PossessionSystem.ApplyDrainTick(state, events);

        Assert.That(events.OfType<PossessionNearDeathWarningEvent>(), Is.Not.Empty,
            "Warning should fire when home body falls to ≤25% MaxHp.");
        var warn = events.OfType<PossessionNearDeathWarningEvent>().Single();
        Assert.That(warn.CurrentHp, Is.EqualTo(5));
        Assert.That(warn.MaxHp, Is.EqualTo(fighter.MaxHp));
    }

    [Test]
    public void ApplyDrainTick_NoNearDeathWarning_WhenHpAboveThreshold()
    {
        var player = MakePlayer(hp: 20);
        player.Get<Fighter>()!.Hp = 20; // 100% — drain by 1 → 19, well above 25%
        var host = MakeMonster();
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        var events = new List<TurnEvent>();
        PossessionSystem.ApplyDrainTick(state, events);

        Assert.That(events.OfType<PossessionNearDeathWarningEvent>(), Is.Empty);
    }

    [Test]
    public void ApplyDrainTick_NearDeathWarning_FiresOnlyOnce_PerSession()
    {
        var player = MakePlayer(hp: 20);
        player.Get<Fighter>()!.Hp = 6; // drain by 1 → 5 (25%), fires warning
        var host = MakeMonster();
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        var events = new List<TurnEvent>();
        PossessionSystem.ApplyDrainTick(state, events); // fires warning
        PossessionSystem.ApplyDrainTick(state, events); // 5→4, already warned — no second fire

        var warnings = events.OfType<PossessionNearDeathWarningEvent>().ToList();
        Assert.That(warnings.Count, Is.EqualTo(1), "Warning fires once per session.");
    }

    [Test]
    public void ApplyDrainTick_NearDeathWarning_NotFired_WhenSafetyRailKicks()
    {
        // At 1HP, safety rail fires before drain tick reaches the warning threshold.
        // Safety rail sets HP to 1 (no change since already 1) and exits — no warning event.
        var player = MakePlayer(hp: 20);
        player.Get<Fighter>()!.Hp = 1;
        var host = MakeMonster();
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        var events = new List<TurnEvent>();
        PossessionSystem.ApplyDrainTick(state, events);

        // Safety rail fires ExitVoluntary — no PossessionNearDeathWarningEvent
        Assert.That(events.OfType<PossessionNearDeathWarningEvent>(), Is.Empty,
            "Safety rail exit path does not emit the near-death warning.");
        Assert.That(host.Has<PossessionEffect>(), Is.False, "Safety rail should have exited possession.");
    }

    // ─── Voice line events ────────────────────────────────────────────────────

    [Test]
    public void ExitVoluntary_EmitsExitVoluntaryVoiceEvent()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        var events = new List<TurnEvent>();
        PossessionSystem.Enter(host, state, events);

        events.Clear();
        PossessionSystem.ExitVoluntary(state, events);

        Assert.That(events.OfType<VoiceLineEvent>()
            .Any(v => v.TriggerId == "possession_exit_voluntary"), Is.True,
            "ExitVoluntary must emit a VoiceLineEvent with trigger 'possession_exit_voluntary'.");
    }

    [Test]
    public void CheckVisibilityConstraint_EmitsOutOfRangeVoiceEvent_WhenDistanceBroken()
    {
        var player = MakePlayer(x: 0, y: 0);
        var host = MakeMonster(x: 0, y: 0);
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        // Move host far enough to break MaxPossessionDistance.
        host.X = PossessionConfig.MaxPossessionDistance + 2;
        host.Y = 0;

        var events = new List<TurnEvent>();
        PossessionSystem.CheckVisibilityConstraint(state, events);

        Assert.That(events.OfType<VoiceLineEvent>()
            .Any(v => v.TriggerId == "possession_exit_out_of_range"), Is.True,
            "CheckVisibilityConstraint must emit 'possession_exit_out_of_range' on visibility break.");
    }

    [Test]
    public void OnPossessionInducedHostDeath_EmitsHostDeathVoiceEvent_ForHostDiedReason()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        var events = new List<TurnEvent>();
        PossessionSystem.OnPossessionInducedHostDeath(host, state, events, reason: "host_died");

        Assert.That(events.OfType<VoiceLineEvent>()
            .Any(v => v.TriggerId == "possession_exit_host_death"), Is.True,
            "host_died reason must emit 'possession_exit_host_death' voice event.");
    }

    [Test]
    public void OnPossessionInducedHostDeath_NoHostDeathVoice_ForWardenDispelledReason()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        // Simulate warden-initiated possession.
        var effect = StatusEffectProcessor.ApplyEffect<PossessionEffect>(host, int.MaxValue)!;
        effect.PossessorEntityId = PossessionConfig.WardenPossessorSentinelId;
        effect.Source = PossessionSource.WardenInitiated;

        var events = new List<TurnEvent>();
        PossessionSystem.OnPossessionInducedHostDeath(host, state, events, reason: "warden_dispelled");

        Assert.That(events.OfType<VoiceLineEvent>()
            .Any(v => v.TriggerId == "possession_exit_host_death"), Is.False,
            "warden_dispelled reason must not emit 'possession_exit_host_death'.");
    }

    [Test]
    public void ApplyDrainTick_EmitsDrainWarning25_WhenHpFallsToSeventyFivePercent()
    {
        // MaxHp = 21 (base 20 + con mod 1). HP ≤ 75% = ≤15.
        var player = MakePlayer(hp: 20);
        player.Get<Fighter>()!.Hp = 16;  // drain 1 → 15 ≤ 15 → fires
        var host = MakeMonster();
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        var events = new List<TurnEvent>();
        PossessionSystem.ApplyDrainTick(state, events);

        Assert.That(events.OfType<VoiceLineEvent>()
            .Any(v => v.TriggerId == "possession_drain_warning_25"), Is.True,
            "DrainWarning25 should fire when HP falls to ≤75% MaxHp.");
    }

    [Test]
    public void ApplyDrainTick_EmitsDrainWarning50_WhenHpFallsToFiftyPercent()
    {
        // MaxHp = 21. HP ≤ 50% = ≤10.
        var player = MakePlayer(hp: 20);
        player.Get<Fighter>()!.Hp = 11;  // drain 1 → 10 ≤ 10 → fires
        var host = MakeMonster();
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        var events = new List<TurnEvent>();
        PossessionSystem.ApplyDrainTick(state, events);

        Assert.That(events.OfType<VoiceLineEvent>()
            .Any(v => v.TriggerId == "possession_drain_warning_50"), Is.True,
            "DrainWarning50 should fire when HP falls to ≤50% MaxHp.");
    }

    [Test]
    public void ApplyDrainTick_DrainWarnings_EachFireOnlyOnce()
    {
        var player = MakePlayer(hp: 20);
        player.Get<Fighter>()!.Hp = 11;  // will cross 50% after one tick
        var host = MakeMonster();
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        var events = new List<TurnEvent>();
        PossessionSystem.ApplyDrainTick(state, events); // crosses 50% threshold
        PossessionSystem.ApplyDrainTick(state, events); // second tick — must not re-fire

        var w50Events = events.OfType<VoiceLineEvent>()
            .Where(v => v.TriggerId == "possession_drain_warning_50").ToList();
        Assert.That(w50Events.Count, Is.EqualTo(1), "DrainWarning50 must fire exactly once per possession.");
    }

    [Test]
    public void OnHomeBodyHit_EmitsThreatenedVoice_FirstHitOnly()
    {
        var player = MakePlayer();
        var host = MakeMonster(id: 2);
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        var events = new List<TurnEvent>();
        PossessionSystem.OnHomeBodyHit(state, events);
        PossessionSystem.OnHomeBodyHit(state, events); // second call — must not re-fire

        var voiceEvents = events.OfType<VoiceLineEvent>()
            .Where(v => v.TriggerId == "possession_home_body_threatened").ToList();
        Assert.That(voiceEvents.Count, Is.EqualTo(1),
            "possession_home_body_threatened fires once per possession, not on every hit.");
    }

    [Test]
    public void OnHomeBodyHit_NoVoice_WhenNotPossessing()
    {
        var player = MakePlayer();
        var state = MakeState(player);

        var events = new List<TurnEvent>();
        PossessionSystem.OnHomeBodyHit(state, events);

        Assert.That(events.OfType<VoiceLineEvent>(), Is.Empty,
            "OnHomeBodyHit should be a no-op when the player is not possessing.");
    }
}
