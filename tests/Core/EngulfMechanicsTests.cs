using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for the Slime Engulf mechanic.
///
/// PoC reference: tests/test_engulf_mechanics.py; entities.yaml slime entries.
/// PoC values: duration=3, refresh on adjacency, no RNG (always applies on hit),
///             movement penalty = skip every other turn.
/// </summary>
[TestFixture]
public class EngulfMechanicsTests
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

    private static MonsterFactory CreateFactory()
    {
        var loader = new ContentLoader();
        var bundle = loader.LoadAllFromFile(FindEntitiesYaml());
        var entityFactory = new EntityFactory();
        var itemFactory = new ItemFactory(bundle.Items, entityFactory);
        return new MonsterFactory(bundle.Monsters, entityFactory, itemFactory);
    }

    private static Entity MakeSlime(int id = 1, int x = 4, int y = 5)
    {
        var slime = new Entity(id, "Slime", x, y, blocksMovement: true);
        // High dexterity (20) → DexMod=+5 → always hits vs AC ~11.
        // Test isolation: we want deterministic "slime hits player" behavior.
        slime.Add(new Fighter(hp: 15, strength: 8, dexterity: 20, constitution: 10,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 3));
        slime.Add(new AiComponent { AiType = "basic", Faction = "beast" });
        slime.Add(new AlertedState { LastKnownPlayerX = 5, LastKnownPlayerY = 5, TurnsUntilDeaggro = 20 });
        slime.Add(new EngulfsOnHitTag());
        return slime;
    }

    private static Entity MakeNonEngulfer(int id = 1, int x = 4, int y = 5)
    {
        var orc = new Entity(id, "Orc", x, y, blocksMovement: true);
        orc.Add(new Fighter(hp: 20, strength: 12, dexterity: 10, constitution: 12,
            accuracy: 3, evasion: 1, damageMin: 2, damageMax: 5));
        orc.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        orc.Add(new AlertedState { LastKnownPlayerX = 5, LastKnownPlayerY = 5, TurnsUntilDeaggro = 20 });
        // No EngulfsOnHitTag
        return orc;
    }

    private static (GameState state, Entity player, Entity monster) CreateArena(
        Entity monster, int playerX = 5, int playerY = 5, int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", playerX, playerY, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 10, dexterity: 12, constitution: 10,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(player);
        map.RegisterEntity(monster);

        var state = new GameState(player, new List<Entity> { monster }, map, rng, turnLimit: 200);
        return (state, player, monster);
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Test]
    public void Engulf_AppliedOnSlimeHit()
    {
        // Slime adjacent to player — slime should hit and apply EngulfedEffect.
        // Slime has high dexterity (20 → DexMod=+5) to ensure reliable hits.
        // Run multiple turns to guarantee at least one hit.
        var slime = MakeSlime(x: 4, y: 5);
        var (state, player, _) = CreateArena(slime);

        // Run up to 5 turns — with DexMod=+5 vs AC=11, hit chance is very high.
        for (int i = 0; i < 5 && !player.Has<EngulfedEffect>(); i++)
            TurnController.ProcessTurn(state, PlayerAction.Wait);

        // Slime should have attacked and applied EngulfedEffect.
        Assert.That(player.Has<EngulfedEffect>(), Is.True,
            "Player should have EngulfedEffect after being hit by adjacent slime (5-turn window)");
    }

    [Test]
    public void Engulf_NotAppliedByNonEngulfer()
    {
        // Orc (no EngulfsOnHitTag) attacks player — no EngulfedEffect.
        var orc = MakeNonEngulfer(x: 4, y: 5);
        var (state, player, _) = CreateArena(orc);

        // Run turns until orc attacks.
        TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(player.Has<EngulfedEffect>(), Is.False,
            "Player should NOT have EngulfedEffect after being hit by a non-engulfer");
    }

    [Test]
    public void Engulf_RefreshesDurationWhileAdjacent()
    {
        // Player has EngulfedEffect at RemainingTurns=1.
        // Slime is adjacent at ProcessTurnStart → duration should refresh to 3.
        var slime = MakeSlime(x: 4, y: 5); // adjacent to player at (5,5)
        var (state, player, _) = CreateArena(slime);

        // Apply engulf with low duration.
        player.Add(new EngulfedEffect { RemainingTurns = 1 });

        var events = new List<TurnEvent>();
        // Process turn start with slime adjacent — should refresh to 3.
        StatusEffectProcessor.ProcessTurnStart(player, events, turnCount: 1, state: state);

        var effect = player.Get<EngulfedEffect>();
        Assert.That(effect, Is.Not.Null, "EngulfedEffect should still be present");
        Assert.That(effect!.RemainingTurns, Is.EqualTo(3),
            "Duration should refresh to 3 while adjacent to slime");
    }

    [Test]
    public void Engulf_DecaysWhenNotAdjacent()
    {
        // Player has EngulfedEffect at RemainingTurns=3 but slime is NOT adjacent.
        // Duration should tick down normally.
        var slime = MakeSlime(x: 9, y: 5); // far from player at (5,5)
        var (state, player, _) = CreateArena(slime);

        player.Add(new EngulfedEffect { RemainingTurns = 3 });

        var events = new List<TurnEvent>();
        // ProcessTurnStart: slime not adjacent → no refresh.
        StatusEffectProcessor.ProcessTurnStart(player, events, turnCount: 1, state: state);

        var effect = player.Get<EngulfedEffect>();
        Assert.That(effect, Is.Not.Null, "EngulfedEffect should still be present (not expired)");
        Assert.That(effect!.RemainingTurns, Is.EqualTo(3),
            "Duration should NOT change during ProcessTurnStart (decay happens in ProcessTurnEnd)");

        // ProcessTurnEnd: decrements RemainingTurns.
        StatusEffectProcessor.ProcessTurnEnd(player, events);
        effect = player.Get<EngulfedEffect>();
        Assert.That(effect?.RemainingTurns, Is.EqualTo(2),
            "Duration should decrement by 1 in ProcessTurnEnd when slime not adjacent");
    }

    [Test]
    public void Engulf_RefreshFromMultipleSlimes()
    {
        // Two slimes adjacent. Only needs one for refresh — but two shouldn't double-refresh.
        var slime1 = MakeSlime(id: 1, x: 4, y: 5);
        var slime2 = MakeSlime(id: 2, x: 5, y: 4);

        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 10, dexterity: 12, constitution: 10,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(player);
        map.RegisterEntity(slime1);
        map.RegisterEntity(slime2);
        var state = new GameState(player, new List<Entity> { slime1, slime2 }, map, rng, turnLimit: 200);

        player.Add(new EngulfedEffect { RemainingTurns = 1 });

        var events = new List<TurnEvent>();
        StatusEffectProcessor.ProcessTurnStart(player, events, turnCount: 1, state: state);

        var effect = player.Get<EngulfedEffect>();
        Assert.That(effect?.RemainingTurns, Is.EqualTo(3),
            "Duration should refresh to 3 with multiple adjacent slimes (same as single)");
    }

    [Test]
    public void Engulf_SkipsEveryOtherTurn()
    {
        // Engulfed player: on odd turns (turnCount % 2 == 1), the turn is skipped.
        // Even turns are acted normally.
        var slime = MakeSlime(x: 9, y: 5); // not adjacent — no refresh
        var (state, player, _) = CreateArena(slime);

        player.Add(new EngulfedEffect { RemainingTurns = 5 });

        var eventsOdd = new List<TurnEvent>();
        bool skipOdd = StatusEffectProcessor.ProcessTurnStart(player, eventsOdd, turnCount: 1); // odd
        Assert.That(skipOdd, Is.True, "Engulfed player should skip on odd turns");
        Assert.That(eventsOdd.OfType<SkipTurnEvent>().Any(e => e.EffectName == "engulfed"), Is.True,
            "SkipTurnEvent with 'engulfed' should be emitted on odd turns");

        var eventsEven = new List<TurnEvent>();
        bool skipEven = StatusEffectProcessor.ProcessTurnStart(player, eventsEven, turnCount: 2); // even
        Assert.That(skipEven, Is.False, "Engulfed player should NOT skip on even turns");
    }

    [Test]
    public void Engulf_UnifiedSkipGate_OnlySkipsOnce()
    {
        // Player with both SlowedEffect AND EngulfedEffect:
        // unified gate fires at most once per alternating-skip check.
        var slime = MakeSlime(x: 9, y: 5);
        var (state, player, _) = CreateArena(slime);

        player.Add(new SlowedEffect { RemainingTurns = 5 });
        player.Add(new EngulfedEffect { RemainingTurns = 5 });

        var events = new List<TurnEvent>();
        bool skip = StatusEffectProcessor.ProcessTurnStart(player, events, turnCount: 1); // odd
        Assert.That(skip, Is.True, "Should skip on odd turn with either effect");
        Assert.That(events.OfType<SkipTurnEvent>().Count(), Is.EqualTo(1),
            "Exactly one SkipTurnEvent should fire — unified gate, not cascading");
    }

    [Test]
    public void Engulf_AppliedToPlayerOnly()
    {
        // Slimes should not engulf other monsters (monster-vs-monster hits don't trigger engulf).
        // Validated by checking that EngulfsOnHitTag presence alone doesn't add engulf —
        // TurnController's ResolveMonsterAttack gates on target.Id == state.Player.Id.
        var slime = MakeSlime(id: 1, x: 4, y: 5);
        var otherMonster = MakeNonEngulfer(id: 2, x: 6, y: 5);

        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(12, 12);

        // Player far away; slime adjacent to otherMonster.
        var player = new Entity(0, "Player", 11, 11, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 10, dexterity: 12, constitution: 10,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(player);
        map.RegisterEntity(slime);
        map.RegisterEntity(otherMonster);

        var state = new GameState(player, new List<Entity> { slime, otherMonster }, map, rng, turnLimit: 200);

        // Run a few turns — slime may attack otherMonster but should not engulf it.
        for (int i = 0; i < 3; i++)
            TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(otherMonster.Has<EngulfedEffect>(), Is.False,
            "Non-player monsters should not receive EngulfedEffect from slime hits");
    }

    [Test]
    public void Engulf_FactoryAttachesTagToSlimes()
    {
        // Verify the factory correctly attaches EngulfsOnHitTag to slime variants from YAML.
        var factory = CreateFactory();

        var slime = factory.Create("slime");
        Assert.That(slime, Is.Not.Null, "slime must be in entities.yaml");
        Assert.That(slime!.Has<EngulfsOnHitTag>(), Is.True, "slime should have EngulfsOnHitTag");

        var largeSlime = factory.Create("large_slime");
        Assert.That(largeSlime, Is.Not.Null, "large_slime must be in entities.yaml");
        Assert.That(largeSlime!.Has<EngulfsOnHitTag>(), Is.True, "large_slime should inherit EngulfsOnHitTag");

        var greaterSlime = factory.Create("greater_slime");
        Assert.That(greaterSlime, Is.Not.Null, "greater_slime must be in entities.yaml");
        Assert.That(greaterSlime!.Has<EngulfsOnHitTag>(), Is.True, "greater_slime should inherit EngulfsOnHitTag");
    }
}
