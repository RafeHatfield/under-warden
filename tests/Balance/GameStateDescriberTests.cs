using UnderWarden.Logic.Balance.LlmPlayer;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

[TestFixture]
[Category("Balance")]
[Description("GameStateDescriber: prompt generation, action menu, persona rendering")]
public class GameStateDescriberTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (GameState State, GameMap Map) MakeEmptyFloor(int width = 20, int height = 20)
    {
        var map = new GameMap(width, height);
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 30, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        player.Add(new Inventory());
        map.RegisterEntity(player);

        var rng = new SeededRandom(1337);
        var state = new GameState(player, new List<Entity>(), map, rng);
        return (state, map);
    }

    private static Entity MakeMonster(int id, int x, int y, GameMap map, int hp = 20, string name = "Orc")
    {
        var m = new Entity(id, name, x, y, blocksMovement: true);
        m.Add(new Fighter(hp: hp, strength: 10, dexterity: 8, constitution: 10,
            accuracy: 2, evasion: 1, damageMin: 2, damageMax: 4));
        map.RegisterEntity(m);
        return m;
    }

    private static DescriberContext EmptyCtx() =>
        new(Array.Empty<string>(), null);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Test]
    [Description("Empty floor returns a valid description with TURN header and at least a Wait action")]
    public void EmptyFloor_ReturnsValidDescription()
    {
        var (state, _) = MakeEmptyFloor();
        var result = GameStateDescriber.Describe(state, LlmPersona.Reader, EmptyCtx());

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Text, Does.Contain("TURN"));
        Assert.That(result.Actions, Is.Not.Empty);
        Assert.That(result.Actions.Any(a => a.Action.Kind == PlayerAction.ActionKind.Wait),
            Is.True, "Should always have a Wait entry");
    }

    [Test]
    [Description("Adjacent monster yields an Attack entry targeting that monster")]
    public void AdjacentMonster_YieldsAttackEntry()
    {
        var (state, map) = MakeEmptyFloor();
        var monster = MakeMonster(1, 6, 5, map);  // 1 tile east of player at (5,5)
        state.Monsters.Add(monster);

        var result = GameStateDescriber.Describe(state, LlmPersona.Reader, EmptyCtx());

        var attackEntry = result.Actions.FirstOrDefault(a => a.Action.Kind == PlayerAction.ActionKind.Attack);
        Assert.That(attackEntry, Is.Not.Null, "Should have an Attack entry for adjacent monster");
        Assert.That(attackEntry!.Action.Target, Is.SameAs(monster));
        Assert.That(attackEntry.Label, Does.Contain("Attack"));
        Assert.That(attackEntry.Label, Does.Contain("Orc"));
    }

    [Test]
    [Description("Non-adjacent monster yields a 'Move toward' entry with Kind==Move")]
    public void NonAdjacentMonster_YieldsMoveTowardEntry()
    {
        var (state, map) = MakeEmptyFloor();
        var monster = MakeMonster(1, 9, 5, map);  // 4 tiles east of player at (5,5)
        state.Monsters.Add(monster);

        var result = GameStateDescriber.Describe(state, LlmPersona.Reader, EmptyCtx());

        var moveEntry = result.Actions.FirstOrDefault(a =>
            a.Action.Kind == PlayerAction.ActionKind.Move && a.Label.Contains("Move toward"));
        Assert.That(moveEntry, Is.Not.Null, "Should have a Move-toward entry for non-adjacent monster");
        // The step should be adjacent to the player (one A* step)
        Assert.That(moveEntry!.Action.TargetX, Is.Not.Null);
        Assert.That(moveEntry.Action.TargetY, Is.Not.Null);
    }

    [Test]
    [Description("When player is on the stair, a Descend entry appears; when off stair, it does not")]
    public void OnStair_YieldsDescendEntry()
    {
        var (state, map) = MakeEmptyFloor();

        // Place stair at player position
        var stair = new Entity(99, "Staircase", 5, 5);
        state.StairDown = stair;

        var onStairResult = GameStateDescriber.Describe(state, LlmPersona.Reader, EmptyCtx());
        Assert.That(onStairResult.Actions.Any(a => a.Action.Kind == PlayerAction.ActionKind.Descend),
            Is.True, "Should have Descend when standing on stair");

        // Move stair away from player
        stair.X = 15;
        stair.Y = 15;

        var offStairResult = GameStateDescriber.Describe(state, LlmPersona.Reader, EmptyCtx());
        Assert.That(offStairResult.Actions.Any(a => a.Action.Kind == PlayerAction.ActionKind.Descend),
            Is.False, "Should NOT have Descend when not standing on stair");
    }

    [Test]
    [Description("Reader persona shows verbatim mural text in description")]
    public void ReaderPersona_MuralShownVerbatim()
    {
        var (state, _) = MakeEmptyFloor();
        const string muralText = "In darkness dwells the warden, patient and unbound.";

        var muralEntity = new Entity(10, "Mural", 6, 5, blocksMovement: true);
        muralEntity.Add(new MuralComponent { Text = muralText, MuralId = "mural_001" });
        state.Features.Add(muralEntity);

        var result = GameStateDescriber.Describe(state, LlmPersona.Reader, EmptyCtx());

        Assert.That(result.Text, Does.Contain(muralText),
            "Reader persona should embed verbatim mural text");
    }

    [Test]
    [Description("SystemExplorer persona shows compressed mural form and NOT the verbatim text")]
    public void SystemExplorerPersona_MuralCompressed()
    {
        var (state, _) = MakeEmptyFloor();
        const string muralText = "In darkness dwells the warden, patient and unbound.";

        var muralEntity = new Entity(10, "Mural", 6, 5, blocksMovement: true);
        muralEntity.Add(new MuralComponent { Text = muralText, MuralId = "mural_001" });
        state.Features.Add(muralEntity);

        var result = GameStateDescriber.Describe(state, LlmPersona.SystemExplorer, EmptyCtx());

        Assert.That(result.Text, Does.Contain("Ancient inscription"),
            "SystemExplorer should show compressed mural label");
        Assert.That(result.Text, Does.Not.Contain(muralText),
            "SystemExplorer should NOT embed verbatim mural text");
    }

    [Test]
    [Description("PendingPromptBlock is rendered verbatim when non-null")]
    public void PendingPromptBlock_RenderedWhenNonNull()
    {
        var (state, _) = MakeEmptyFloor();
        const string block = "FLOOR 1 COMPLETE\nWhat did you learn on this floor?";
        var ctx = new DescriberContext(Array.Empty<string>(), block);

        var result = GameStateDescriber.Describe(state, LlmPersona.Reader, ctx);

        Assert.That(result.Text, Does.Contain(block),
            "PendingPromptBlock should be rendered verbatim in the text");
    }

    [Test]
    [Description("Base description without mural is under 850 tokens (3400 chars)")]
    public void TokenBudget_BaseUnder850()
    {
        var (state, _) = MakeEmptyFloor();
        var result = GameStateDescriber.Describe(state, LlmPersona.Reader, EmptyCtx());

        Assert.That(result.Text.Length, Is.LessThan(3400),
            $"Base description should be under 3400 chars (≈850 tokens). Actual: {result.Text.Length}");
    }

    [Test]
    [Description("Mural text longer than 2000 chars is truncated with '[...]'")]
    public void MuralTruncation_EnforcedAt2000Chars()
    {
        var (state, _) = MakeEmptyFloor();
        string longText = new string('A', 2500);

        var muralEntity = new Entity(10, "Mural", 6, 5, blocksMovement: true);
        muralEntity.Add(new MuralComponent { Text = longText, MuralId = "mural_long" });
        state.Features.Add(muralEntity);

        var result = GameStateDescriber.Describe(state, LlmPersona.Reader, EmptyCtx());

        Assert.That(result.Text, Does.Contain("[...]"),
            "Long mural text should be truncated with '[...]'");
        Assert.That(result.Text, Does.Not.Contain(new string('A', 2001)),
            "Verbatim text beyond 2000 chars should not appear");
    }

    [Test]
    [Description("SummarizeEvents: empty list returns 'Nothing happened'")]
    public void SummarizeEvents_EmptyList_ReturnsNothingHappened()
    {
        var summary = GameStateDescriber.SummarizeEvents(Array.Empty<TurnEvent>());
        Assert.That(summary, Is.EqualTo("Nothing happened"));
    }

    [Test]
    [Description("SummarizeEvents: attack hit by player produces readable summary")]
    public void SummarizeEvents_AttackHit_ByPlayer()
    {
        var events = new List<TurnEvent>
        {
            new AttackEvent { ActorId = 0, TargetId = 1, Hit = true, Damage = 7 }
        };
        var summary = GameStateDescriber.SummarizeEvents(events);
        Assert.That(summary, Does.Contain("7"));
        Assert.That(summary, Does.Contain("You hit"));
    }

    [Test]
    [Description("SummarizeEvents: heal event mentions healed amount")]
    public void SummarizeEvents_HealEvent()
    {
        var events = new List<TurnEvent>
        {
            new HealEvent { ActorId = 0, AmountHealed = 12 }
        };
        var summary = GameStateDescriber.SummarizeEvents(events);
        Assert.That(summary, Does.Contain("12"));
        Assert.That(summary, Does.Contain("healed"));
    }
}
