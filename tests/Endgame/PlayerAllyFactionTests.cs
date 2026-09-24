using System.Collections.Generic;
using UnderWarden.Logic.AI;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Endgame;
using NUnit.Framework;

namespace UnderWarden.Tests.Endgame;

/// <summary>
/// TASK-004 (player_ally faction matrix) + TASK-005 (ChooseTarget gate). A player-ally fights
/// beside Sasha: friendly to the player and other allies, hostile to every monster faction, and
/// it never targets the player even though the targeting code historically hardcoded that.
/// </summary>
[TestFixture]
public class PlayerAllyFactionTests
{
    private const string Ally = FactionRegistry.PlayerAllyFaction; // "player_ally"

    // ── TASK-004: hostility matrix ────────────────────────────────────────────

    [Test]
    public void Ally_IsFriendlyToPlayerAndOtherAllies()
    {
        Assert.That(FactionRegistry.AreHostile(Ally, "player"), Is.False);
        Assert.That(FactionRegistry.AreHostile("player", Ally), Is.False);
        Assert.That(FactionRegistry.AreHostile(Ally, Ally), Is.False);
    }

    [TestCase("orc")]
    [TestCase("undead")]
    [TestCase("cultist")]
    [TestCase("beast")]
    [TestCase("neutral")]
    public void Ally_IsMutuallyHostileWithAllMonsterFactions(string monsterFaction)
    {
        Assert.That(FactionRegistry.AreHostile(Ally, monsterFaction), Is.True);
        Assert.That(FactionRegistry.AreHostile(monsterFaction, Ally), Is.True);
    }

    [Test]
    public void PlayerHostility_Unchanged()
    {
        Assert.That(FactionRegistry.AreHostile("player", "orc"), Is.True);
        Assert.That(FactionRegistry.AreHostile("player", "player"), Is.False);
        Assert.That(FactionRegistry.AreHostile("orc", "undead"), Is.True);
        // orc↔neutral is non-hostile (neutral only attacks the player and beasts).
        Assert.That(FactionRegistry.AreHostile("orc", "neutral"), Is.False);
    }

    [Test]
    public void TargetPriority_AllyIsHighValueButBelowSasha()
    {
        Assert.That(FactionRegistry.GetTargetPriority("orc", "player"), Is.EqualTo(10));
        Assert.That(FactionRegistry.GetTargetPriority("orc", Ally), Is.EqualTo(8));
        Assert.That(FactionRegistry.GetTargetPriority(Ally, "orc"), Is.EqualTo(6));
    }

    // ── TASK-005: ChooseTarget gate ───────────────────────────────────────────

    private static Entity Mob(int id, string name, int x, int y, string faction, int hp = 100)
    {
        var m = new Entity(id, name, x, y, blocksMovement: true);
        m.Add(new Fighter(hp: hp, strength: 12, dexterity: 12, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        m.Add(new AiComponent { AiType = "basic", Faction = faction });
        return m;
    }

    private static (GameState state, Entity player) Build(Entity player, params Entity[] mobs)
    {
        var map = GameMap.CreateArena(20, 20);
        map.RegisterEntity(player);
        foreach (var m in mobs) map.RegisterEntity(m);
        var state = new GameState(player, new System.Collections.Generic.List<Entity>(mobs), map,
            new SeededRandom(1337));
        return (state, player);
    }

    private static Entity Player(int x, int y)
    {
        var p = new Entity(0, "Player", x, y, blocksMovement: true);
        p.Add(new Fighter(hp: 100, strength: 12, dexterity: 12, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        return p;
    }

    [Test]
    public void Ally_TargetsHostileMonster_NotThePlayer()
    {
        var player = Player(5, 5);
        var ally = Mob(1, "Champion", 6, 5, Ally);
        var orc = Mob(2, "Orc", 8, 5, "orc");
        var (state, _) = Build(player, ally, orc);

        var target = BasicMonsterAI.ChooseTarget(ally, player, state);

        Assert.That(target.Id, Is.EqualTo(orc.Id), "ally fights the orc, not Sasha");
        Assert.That(target.Id, Is.Not.EqualTo(player.Id));
    }

    [Test]
    public void Ally_WithNoHostiles_ReturnsSelfSentinel_AndIdles()
    {
        var player = Player(5, 5);
        var ally = Mob(1, "Champion", 6, 5, Ally);
        var (state, _) = Build(player, ally);

        var target = BasicMonsterAI.ChooseTarget(ally, player, state);
        Assert.That(target.Id, Is.EqualTo(ally.Id), "no enemy in range → self-target sentinel");

        var action = BasicMonsterAI.Decide(ally, state);
        Assert.That(action.Kind, Is.EqualTo(MonsterAction.ActionKind.Wait),
            "the sentinel resolves to idle, not an attack on Sasha");
    }

    [Test]
    public void TwoAllies_DoNotTargetEachOther()
    {
        var player = Player(5, 5);
        var ally1 = Mob(1, "Champion", 6, 5, Ally);
        var ally2 = Mob(2, "PastSelf", 7, 5, Ally);
        var orc = Mob(3, "Orc", 9, 5, "orc");
        var (state, _) = Build(player, ally1, ally2, orc);

        var target = BasicMonsterAI.ChooseTarget(ally1, player, state);
        Assert.That(target.Id, Is.EqualTo(orc.Id), "allies ignore each other and Sasha, fight the orc");
    }

    [Test]
    public void Monster_CanTargetAlly_WhenPlayerNotAvailable()
    {
        // Player invisible → the orc's only hostile is the ally.
        var player = Player(3, 5);
        player.Add(new InvisibilityEffect { RemainingTurns = 10 });
        var ally = Mob(1, "Champion", 6, 5, Ally);
        var orc = Mob(2, "Orc", 7, 5, "orc");
        var (state, _) = Build(player, ally, orc);

        var target = BasicMonsterAI.ChooseTarget(orc, player, state);
        Assert.That(target.Id, Is.EqualTo(ally.Id), "the orc treats the ally as a valid hostile target");
    }

    [Test]
    public void NormalMonster_StillTargetsPlayer()
    {
        // Regression: the gate must not change ordinary monster behavior.
        var player = Player(15, 5);
        var orc = Mob(1, "Orc", 5, 5, "orc");
        var (state, _) = Build(player, orc);

        var target = BasicMonsterAI.ChooseTarget(orc, player, state);
        Assert.That(target.Id, Is.EqualTo(player.Id));
    }

    // ── TASK-006: friendly-fire surface ───────────────────────────────────────

    [Test]
    public void PlayerAoe_SkipsAlly_HitsMonster()
    {
        // Player at (5,5) casts Earthquake (radius 3): ally at (6,5), orc at (7,5) both in range.
        var player = Player(5, 5);
        var ally = Mob(1, "Champion", 6, 5, Ally, hp: 100);
        var orc = Mob(2, "Orc", 7, 5, "orc", hp: 100);
        var (state, _) = Build(player, ally, orc);

        SpellResolver.Resolve(player, new SpellEffect { SpellId = "earthquake", Radius = 3, Damage = 20 }, state);

        Assert.That(ally.Require<Fighter>().Hp, Is.EqualTo(100), "ally must not take player AoE damage");
        Assert.That(orc.Require<Fighter>().Hp, Is.LessThan(100), "orc still takes the AoE");
    }

    [Test]
    public void Possession_CannotTargetAlly_CanTargetMonster()
    {
        var player = Player(5, 5);
        var ally = Mob(1, "Champion", 6, 5, Ally);
        var orc = Mob(2, "Orc", 7, 5, "orc");
        var (state, _) = Build(player, ally, orc);

        Assert.That(PossessionSystem.IsValidTarget(ally, player, state), Is.False,
            "Sasha cannot possess his own ally");
        Assert.That(PossessionSystem.IsValidTarget(orc, player, state), Is.True,
            "a hostile orc remains a valid possession target");
    }

    // ── TASK-007: Warden-of-Wardens turns an ally hostile (enrage), spell-break restores ──

    [Test]
    public void TurnAllyHostile_MakesAllyTargetSasha()
    {
        var player = Player(5, 5);
        var ally = Mob(1, "Champion", 6, 5, Ally);
        var (state, _) = Build(player, ally);

        GuardianAbilities.TurnAllyHostile(ally, new List<TurnEvent>());

        Assert.That(ally.Has<EnragedEffect>(), Is.True);
        var target = BasicMonsterAI.ChooseTarget(ally, player, state);
        Assert.That(target.Id, Is.EqualTo(player.Id),
            "an enraged ally (HostileToAll) turns on the nearest entity — Sasha");
    }

    [Test]
    public void SpellBreak_RemovesEnrage_RestoresAllyToFightingForSasha()
    {
        var player = Player(5, 5);
        var ally = Mob(1, "Champion", 6, 5, Ally);
        var orc = Mob(2, "Orc", 8, 5, "orc");
        var (state, _) = Build(player, ally, orc);

        GuardianAbilities.TurnAllyHostile(ally, new List<TurnEvent>());
        Assert.That(BasicMonsterAI.ChooseTarget(ally, player, state).Id, Is.EqualTo(player.Id),
            "precondition: enraged ally targets Sasha");

        // Hollowmark's spell-break (Dispel) removes the enrage — composes through the existing path.
        SpellResolver.Resolve(player, new SpellEffect { SpellId = "dispel", Range = 5 }, state,
            targetEntityId: ally.Id);

        Assert.That(ally.Has<EnragedEffect>(), Is.False, "spell-break clears the enrage");
        Assert.That(BasicMonsterAI.ChooseTarget(ally, player, state).Id, Is.EqualTo(orc.Id),
            "restored ally fights the orc again, not Sasha");
    }

    [Test]
    public void PlayerAoe_HitsEnragedAlly()
    {
        // A turned (enraged) ally is a legitimate threat — the friendly-fire guard lifts for it.
        var player = Player(5, 5);
        var ally = Mob(1, "Champion", 6, 5, Ally, hp: 100);
        var (state, _) = Build(player, ally);
        GuardianAbilities.TurnAllyHostile(ally, new List<TurnEvent>());

        SpellResolver.Resolve(player, new SpellEffect { SpellId = "earthquake", Radius = 3, Damage = 20 }, state);

        Assert.That(ally.Require<Fighter>().Hp, Is.LessThan(100),
            "once turned hostile, the ally loses friendly-fire protection");
    }
}
