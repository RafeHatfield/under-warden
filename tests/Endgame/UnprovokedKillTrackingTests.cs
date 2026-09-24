using UnderWarden.Logic.AI;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Endgame;
using NUnit.Framework;

namespace UnderWarden.Tests.Endgame;

/// <summary>
/// TASK-003: the unprovoked-kill predicate (the excess-metric / faction-rep signal).
/// A kill is unprovoked iff the victim never attacked the player (sticky HasAttackedPlayerTag).
/// Driven through the real ProcessTurn path so the UpdateKnowledge detection is exercised.
/// </summary>
[TestFixture]
public class UnprovokedKillTrackingTests
{
    /// <summary>Player adjacent to a monster, both registered in a fresh arena.</summary>
    private static (GameState state, Entity player, Entity monster) Arena(
        int monsterHp = 100, int playerAccuracy = 10, string faction = "orc", int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 200, strength: 14, dexterity: 14, constitution: 14,
            accuracy: playerAccuracy, evasion: 0, damageMin: 5, damageMax: 8));
        map.RegisterEntity(player);

        var monster = new Entity(1, "Orc", 6, 5, blocksMovement: true);
        monster.Add(new Fighter(hp: monsterHp, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        monster.Add(new AiComponent { AiType = "basic", Faction = faction });
        map.RegisterEntity(monster);

        var state = new GameState(player, [monster], map, rng, turnLimit: 200);
        return (state, player, monster);
    }

    [Test]
    public void MonsterAttackingPlayer_SetsStickyAggressionTag()
    {
        // Adjacent monster (scenario mode = always alerted) attacks the player on the Wait turn.
        var (state, _, monster) = Arena(monsterHp: 100);
        Assert.That(monster.Has<HasAttackedPlayerTag>(), Is.False, "tag should be absent before any attack");

        TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(monster.Has<HasAttackedPlayerTag>(), Is.True,
            "a monster that attacks the player (hit or miss) is flagged as having chosen violence");
    }

    [Test]
    public void PlayerKillsUnprovokedMonster_IncrementsTallyAndOrcCounter()
    {
        // 1-HP monster, high player accuracy → one-shot on the player's phase, before the monster
        // can act. The victim never attacked → the kill is unprovoked.
        var (state, player, _) = Arena(monsterHp: 1, playerAccuracy: 50, faction: "orc");

        TurnController.ProcessTurn(state, PlayerAction.Attack(state.Monsters[0]));

        var tally = player.Get<RunAggressionTally>();
        Assert.That(tally, Is.Not.Null, "player should have a run aggression tally after an unprovoked kill");
        Assert.That(tally!.UnprovokedKillsFor("orc"), Is.EqualTo(1));
        Assert.That(state.UnprovokedOrcKillsThisRun, Is.EqualTo(1),
            "the faction-rep this-run counter reads through the tally");
    }

    [Test]
    public void PlayerKillsProvokedMonster_DoesNotIncrement()
    {
        // The monster attacked Sasha at some point (tag present) → killing it is provoked work.
        var (state, player, monster) = Arena(monsterHp: 1, playerAccuracy: 50, faction: "orc");
        monster.Add(new HasAttackedPlayerTag());

        TurnController.ProcessTurn(state, PlayerAction.Attack(state.Monsters[0]));

        var tally = player.Get<RunAggressionTally>();
        int orcKills = tally?.UnprovokedKillsFor("orc") ?? 0;
        Assert.That(orcKills, Is.EqualTo(0), "a provoked kill must not count toward excess");
        Assert.That(state.UnprovokedOrcKillsThisRun, Is.EqualTo(0));
    }

    [Test]
    public void UnprovokedKill_TalliesByVictimFaction()
    {
        var (state, player, _) = Arena(monsterHp: 1, playerAccuracy: 50, faction: "undead");

        TurnController.ProcessTurn(state, PlayerAction.Attack(state.Monsters[0]));

        var tally = player.Get<RunAggressionTally>()!;
        Assert.That(tally.UnprovokedKillsFor("undead"), Is.EqualTo(1));
        Assert.That(tally.UnprovokedKillsFor("orc"), Is.EqualTo(0), "orc subset unaffected by an undead kill");
        Assert.That(state.UnprovokedOrcKillsThisRun, Is.EqualTo(0));
    }

    [Test]
    public void MonsterAttackingAnAlly_IsFlaggedProvoked()
    {
        // TASK-007 set-site extension: attacking Sasha's side (an ally) counts as choosing violence.
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        // Player invisible and far, so the orc's only target is the adjacent ally.
        var player = new Entity(0, "Player", 1, 1, blocksMovement: true);
        player.Add(new Fighter(hp: 200, strength: 12, dexterity: 12, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        player.Add(new InvisibilityEffect { RemainingTurns = 50 });
        map.RegisterEntity(player);

        var ally = new Entity(1, "Champion", 6, 5, blocksMovement: true);
        ally.Add(new Fighter(hp: 100, strength: 12, dexterity: 12, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        ally.Add(new AiComponent { AiType = "basic", Faction = FactionRegistry.PlayerAllyFaction });
        map.RegisterEntity(ally);

        var orc = new Entity(2, "Orc", 7, 5, blocksMovement: true);
        orc.Add(new Fighter(hp: 100, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        orc.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        map.RegisterEntity(orc);

        var state = new GameState(player, [ally, orc], map, rng, turnLimit: 200);

        TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(orc.Has<HasAttackedPlayerTag>(), Is.True,
            "a monster that attacks a player-ally is flagged provoked, same as attacking Sasha");
    }

    // ── Pure-logic: tally + carry-forward ─────────────────────────────────────

    [Test]
    public void RunAggressionTally_AccumulatesAndTotals()
    {
        var t = new RunAggressionTally();
        t.AddUnprovokedKill("orc");
        t.AddUnprovokedKill("orc");
        t.AddUnprovokedKill("undead");

        Assert.That(t.UnprovokedKillsFor("orc"), Is.EqualTo(2));
        Assert.That(t.UnprovokedKillsFor("undead"), Is.EqualTo(1));
        Assert.That(t.UnprovokedKillsFor("cultist"), Is.EqualTo(0));
        Assert.That(t.Total(), Is.EqualTo(3));
    }

    // ── Possession loophole: a proxy kill is still Sasha's if HE is the possessor ──────

    private static Entity PossessedHost(int hostId, PossessionSource source, int possessorId)
    {
        var host = new Entity(hostId, "Host", 7, 5, blocksMovement: true);
        host.Add(new Fighter(hp: 30, strength: 14, dexterity: 12, constitution: 12,
            accuracy: 50, evasion: 0, damageMin: 5, damageMax: 8));
        host.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        host.Add(new PossessionEffect { Source = source, PossessorEntityId = possessorId });
        return host;
    }

    [Test]
    public void DealtByPlayer_OwnBody_IsTrue()
    {
        Assert.That(ExcessKillEvaluator.DealtByPlayer(killer: null, killerId: 0, playerId: 0), Is.True);
    }

    [Test]
    public void DealtByPlayer_HostPossessedByPlayer_IsTrue()
    {
        var host = PossessedHost(hostId: 9, PossessionSource.PlayerInitiated, possessorId: 0);
        Assert.That(ExcessKillEvaluator.DealtByPlayer(host, killerId: 9, playerId: 0), Is.True,
            "a kill through a host Sasha is possessing counts — the metric must not be launderable via a proxy");
    }

    [Test]
    public void DealtByPlayer_WardenPossessedHost_IsFalse()
    {
        // WardenInitiated (e.g. a filed past-Sasha corpse) — not Sasha's choice.
        var host = PossessedHost(hostId: 9, PossessionSource.WardenInitiated, possessorId: -2);
        Assert.That(ExcessKillEvaluator.DealtByPlayer(host, killerId: 9, playerId: 0), Is.False);
    }

    [Test]
    public void DealtByPlayer_PlayerInitiatedButDifferentPossessor_IsFalse()
    {
        // Defensive: PlayerInitiated tag but possessor isn't this player id.
        var host = PossessedHost(hostId: 9, PossessionSource.PlayerInitiated, possessorId: 42);
        Assert.That(ExcessKillEvaluator.DealtByPlayer(host, killerId: 9, playerId: 0), Is.False);
    }

    [Test]
    public void DealtByPlayer_PlainMonsterKiller_IsFalse()
    {
        // Monster-vs-monster, or an enraged former ally — no PossessionEffect at all.
        var other = new Entity(9, "Orc", 7, 5, blocksMovement: true);
        other.Add(new AiComponent { Faction = "orc" });
        Assert.That(ExcessKillEvaluator.DealtByPlayer(other, killerId: 9, playerId: 0), Is.False);
    }

    [Test]
    public void PlayerPossessingHost_UnprovokedKill_TalliesAgainstSasha()
    {
        // Player (5,5), orc host (6,5), unprovoked undead victim (7,5) adjacent to the host.
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 200, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        player.Add(new RunAggressionTally());
        map.RegisterEntity(player);

        var host = new Entity(1, "Orc", 6, 5, blocksMovement: true);
        host.Add(new Fighter(hp: 100, strength: 16, dexterity: 14, constitution: 12,
            accuracy: 50, evasion: 0, damageMin: 8, damageMax: 12));
        host.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        map.RegisterEntity(host);

        var victim = new Entity(2, "Zombie", 7, 5, blocksMovement: true);
        victim.Add(new Fighter(hp: 1, strength: 8, dexterity: 8, constitution: 8,
            accuracy: 5, evasion: 0, damageMin: 1, damageMax: 2));
        victim.Add(new AiComponent { AiType = "basic", Faction = "undead" });
        map.RegisterEntity(victim);

        var state = new GameState(player, [host, victim], map, rng, turnLimit: 200);

        PossessionSystem.Enter(host, state, new List<TurnEvent>());
        Assert.That(state.ControlledEntity.Id, Is.EqualTo(host.Id), "player should be controlling the host");

        // The controlled host strikes the unprovoked victim.
        TurnController.ProcessTurn(state, PlayerAction.Attack(victim));

        Assert.That(victim.Require<Fighter>().IsAlive, Is.False, "host should have killed the 1-HP victim");
        Assert.That(player.Get<RunAggressionTally>()!.UnprovokedKillsFor("undead"), Is.EqualTo(1),
            "a proxy kill of an unprovoked victim counts toward Sasha's excess");
    }

    [Test]
    public void PlayerCarryForward_PreservesAggressionTallyAcrossFloors()
    {
        var oldPlayer = new Entity(0, "Player", 5, 5, blocksMovement: true);
        oldPlayer.Add(new Fighter(hp: 30, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 10, evasion: 0, damageMin: 1, damageMax: 2));
        var tally = oldPlayer.Add(new RunAggressionTally());
        tally.AddUnprovokedKill("orc");
        tally.AddUnprovokedKill("beast");

        var newPlayer = PlayerCarryForward.Apply(oldPlayer);

        var carried = newPlayer.Get<RunAggressionTally>();
        Assert.That(carried, Is.Not.Null, "the run tally must survive a floor transition");
        Assert.That(carried!.UnprovokedKillsFor("orc"), Is.EqualTo(1));
        Assert.That(carried.UnprovokedKillsFor("beast"), Is.EqualTo(1));
        Assert.That(carried.Total(), Is.EqualTo(2));
    }
}
