using UnderWarden.Logic.AI;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for the Orc Shaman's Chant of Dissonance ability.
///
/// PoC reference: orc_shaman_ai.py lines 233-313; chant_move_energy_tax = 1.
/// Key behaviors: channeled for 3 turns, interrupts on attack damage (not DOT),
/// silenced shaman cannot start, player movement skips every other turn while chanted.
/// Chant cleanup when shaman dies during channel (R4 risk).
/// </summary>
[TestFixture]
public class OrcShamanChantTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Entity MakeShaman(int id = 1, int x = 8, int y = 5)
    {
        // Shaman at distance 3 from player at (5,5) — within ChantRange=5, outside DangerRadius=2.
        var e = new Entity(id, "Orc Shaman", x, y, blocksMovement: true);
        e.Add(new Fighter(hp: 24, strength: 10, dexterity: 12, constitution: 10,
            accuracy: 3, evasion: 1, damageMin: 3, damageMax: 5));
        e.Add(new AiComponent { AiType = "orc_shaman", Faction = "orc" });
        e.Add(new AlertedState { LastKnownPlayerX = 5, LastKnownPlayerY = 5, TurnsUntilDeaggro = 20 });
        e.Add(new OrcShamanComponent());
        return e;
    }

    private static (GameState state, Entity player, Entity shaman)
        CreateArena(Entity shaman, int playerX = 5, int playerY = 5, int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(20, 20);

        var player = new Entity(0, "Player", playerX, playerY, blocksMovement: true);
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 3, damageMax: 5));
        map.RegisterEntity(player);
        map.RegisterEntity(shaman);

        var state = new GameState(player, new List<Entity> { shaman }, map, rng, turnLimit: 200);
        return (state, player, shaman);
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Test]
    public void Chant_StartsWhenInRangeAndOffCooldown()
    {
        // Shaman within ChantRange=5, ChantCooldownRemaining=0 → chant starts.
        var shaman = MakeShaman(x: 8, y: 5); // dist=3 from player at (5,5)
        var (state, player, _) = CreateArena(shaman);
        var comp = shaman.Get<OrcShamanComponent>()!;

        OrcShamanAI.Decide(shaman, state);

        Assert.That(comp.IsChanneling, Is.True, "Shaman should start channeling");
        Assert.That(comp.ChantTurnsRemaining, Is.EqualTo(3), "ChantTurnsRemaining should be 3 on start");
    }

    [Test]
    public void Chant_AppliesDissonantChantEffectToPlayer()
    {
        var shaman = MakeShaman(x: 8, y: 5);
        var (state, player, _) = CreateArena(shaman);

        OrcShamanAI.Decide(shaman, state);

        var effect = player.Get<DissonantChantEffect>();
        Assert.That(effect, Is.Not.Null, "Player should have DissonantChantEffect after shaman starts chanting");
        Assert.That(effect!.ChantingShamanId, Is.EqualTo(shaman.Id),
            "ChantingShamanId should match the shaman's entity ID");
    }

    [Test]
    public void Chant_ConsumesShamanTurn()
    {
        // While channeling, shaman returns MonsterAction.Wait (no attack, no move).
        var shaman = MakeShaman(x: 8, y: 5);
        var (state, player, _) = CreateArena(shaman);

        // Start channel.
        OrcShamanAI.Decide(shaman, state);
        Assert.That(shaman.Get<OrcShamanComponent>()!.IsChanneling, Is.True);

        // On the next call, shaman should continue channeling (return Wait).
        var action = OrcShamanAI.Decide(shaman, state);
        Assert.That(action.Kind, Is.EqualTo(MonsterAction.ActionKind.Wait),
            "Channeling shaman should return Wait — not attack or move");
    }

    [Test]
    public void Chant_ContinuesForThreeTurns()
    {
        // Channel lasts 3 turns. After 3 decrements, IsChanneling becomes false.
        var shaman = MakeShaman(x: 8, y: 5);
        var (state, player, _) = CreateArena(shaman);
        var comp = shaman.Get<OrcShamanComponent>()!;

        // Turn 0: start channel (ChantTurnsRemaining = 3 set, not decremented yet).
        OrcShamanAI.Decide(shaman, state);
        Assert.That(comp.ChantTurnsRemaining, Is.EqualTo(3));

        // Turn 1: channeling → decrement to 2.
        OrcShamanAI.Decide(shaman, state);
        Assert.That(comp.ChantTurnsRemaining, Is.EqualTo(2));

        // Turn 2: channeling → decrement to 1.
        OrcShamanAI.Decide(shaman, state);
        Assert.That(comp.ChantTurnsRemaining, Is.EqualTo(1));

        // Turn 3: channeling → decrement to 0, channel ends naturally.
        OrcShamanAI.Decide(shaman, state);
        Assert.That(comp.IsChanneling, Is.False, "Channel should end after 3 turns");
    }

    [Test]
    public void Chant_EndsNaturallyAfterThreeTurns_SetsCooldown15()
    {
        // Natural expiry: ChantCooldownRemaining set to 15, DissonantChantEffect removed.
        var shaman = MakeShaman(x: 8, y: 5);
        var (state, player, _) = CreateArena(shaman);
        var comp = shaman.Get<OrcShamanComponent>()!;

        // Start + run through 3 turns.
        OrcShamanAI.Decide(shaman, state); // start
        OrcShamanAI.Decide(shaman, state); // turn 1
        OrcShamanAI.Decide(shaman, state); // turn 2
        OrcShamanAI.Decide(shaman, state); // turn 3 → natural end

        Assert.That(comp.IsChanneling, Is.False, "Channel should be done");
        Assert.That(comp.ChantCooldownRemaining, Is.EqualTo(comp.ChantCooldownTurns),
            "ChantCooldownRemaining should equal ChantCooldownTurns (15) after natural expiry");
        Assert.That(player.Has<DissonantChantEffect>(), Is.False,
            "DissonantChantEffect should be removed from player after natural channel end");
    }

    [Test]
    public void Chant_InterruptedByAttackDamage_EndsImmediately()
    {
        // Attack damage on the shaman interrupts the channel, sets cooldown, removes player effect.
        var shaman = MakeShaman(id: 1, x: 6, y: 5); // adjacent to player at (5,5)
        var (state, player, _) = CreateArena(shaman);
        var comp = shaman.Get<OrcShamanComponent>()!;

        // Manually start a channel (as if shaman was further away earlier).
        player.Add(new DissonantChantEffect { RemainingTurns = 999, ChantingShamanId = shaman.Id });
        comp.IsChanneling = true;
        comp.ChantTurnsRemaining = 2;

        // Turn 1 (odd): player skips due to DissonantChantEffect. Shaman continues channel.
        TurnController.ProcessTurn(state, PlayerAction.Wait);
        Assert.That(comp.IsChanneling, Is.True, "Channel should still be active after first (skipped) turn");

        // Turn 2 (even): player acts. Attack shaman — should hit and interrupt channel.
        TurnController.ProcessTurn(state, PlayerAction.Attack(shaman));

        Assert.That(comp.IsChanneling, Is.False, "Channel should be interrupted by attack damage");
        // ChantCooldownRemaining is set to 15 on interrupt, then the shaman's own turn
        // decrements it by 1 (step 6 of AI). So at end of full round: 15 - 1 = 14.
        Assert.That(comp.ChantCooldownRemaining, Is.EqualTo(comp.ChantCooldownTurns - 1),
            "ChantCooldownRemaining should be 14 after interrupt (set to 15 then ticked once by shaman's turn)");
        Assert.That(player.Has<DissonantChantEffect>(), Is.False,
            "DissonantChantEffect should be removed from player on interrupt");
    }

    [Test]
    public void Chant_NotInterruptedByDotDamage()
    {
        // DOT damage (burning tick) on a channeling shaman does NOT interrupt the chant.
        var shaman = MakeShaman(x: 8, y: 5);
        var (state, player, _) = CreateArena(shaman);
        var comp = shaman.Get<OrcShamanComponent>()!;

        // Start channel manually.
        player.Add(new DissonantChantEffect { RemainingTurns = 999, ChantingShamanId = shaman.Id });
        comp.IsChanneling = true;
        comp.ChantTurnsRemaining = 2;

        // DOT damage via ProcessTurnStart (burning tick).
        StatusEffectProcessor.ApplyEffect<BurningEffect>(shaman, 3);
        var events = new List<TurnEvent>();
        StatusEffectProcessor.ProcessTurnStart(shaman, events, turnCount: 1, state: state);

        Assert.That(comp.IsChanneling, Is.True,
            "Channel should NOT be interrupted by DOT damage — only attack damage interrupts");
        Assert.That(player.Has<DissonantChantEffect>(), Is.True,
            "DissonantChantEffect should persist through DOT damage");
    }

    [Test]
    public void Chant_SilencedShamanCannotStart()
    {
        // Silenced shaman: chant cannot start. The shaman falls through to other actions.
        var shaman = MakeShaman(x: 8, y: 5);
        var (state, player, _) = CreateArena(shaman);
        var comp = shaman.Get<OrcShamanComponent>()!;

        // Silence the shaman and put hex on cooldown so it falls through to positioning.
        shaman.Add(new SilencedEffect { RemainingTurns = 5 });
        comp.HexCooldownRemaining = 1; // hex on cooldown so it doesn't fire either

        OrcShamanAI.Decide(shaman, state);

        Assert.That(comp.IsChanneling, Is.False, "Silenced shaman should NOT start chanting");
        Assert.That(player.Has<DissonantChantEffect>(), Is.False,
            "DissonantChantEffect should not be applied when shaman is silenced");
    }

    [Test]
    public void Chant_BlockedByDisorientation()
    {
        // Status override: DisorientationEffect causes shaman to use random movement, not chant.
        var shaman = MakeShaman(x: 8, y: 5);
        var (state, player, _) = CreateArena(shaman);
        var comp = shaman.Get<OrcShamanComponent>()!;

        shaman.Add(new DisorientationEffect { RemainingTurns = 5 });

        OrcShamanAI.Decide(shaman, state);

        Assert.That(comp.IsChanneling, Is.False,
            "Disoriented shaman should not start chanting — status overrides take priority");
        Assert.That(player.Has<DissonantChantEffect>(), Is.False,
            "DissonantChantEffect should not be applied while shaman is disoriented");
    }

    [Test]
    public void Chant_PlayerMovementCostsExtraTurn()
    {
        // DissonantChantEffect: player skips every other turn (turnCount % 2 == 1).
        var shaman = MakeShaman(x: 8, y: 5);
        var (state, player, _) = CreateArena(shaman);

        // Apply chant effect manually (simulating shaman having started it).
        player.Add(new DissonantChantEffect { RemainingTurns = 999, ChantingShamanId = shaman.Id });

        // Odd turn → skip.
        var eventsOdd = new List<TurnEvent>();
        bool skipOdd = StatusEffectProcessor.ProcessTurnStart(player, eventsOdd, turnCount: 1);
        Assert.That(skipOdd, Is.True, "Chanted player should skip on odd turns");
        Assert.That(eventsOdd.OfType<SkipTurnEvent>().Any(e => e.EffectName == "dissonant_chant"), Is.True,
            "SkipTurnEvent with 'dissonant_chant' should fire on odd turns");

        // Even turn → act.
        var eventsEven = new List<TurnEvent>();
        bool skipEven = StatusEffectProcessor.ProcessTurnStart(player, eventsEven, turnCount: 2);
        Assert.That(skipEven, Is.False, "Chanted player should NOT skip on even turns");
    }

    [Test]
    public void Chant_HexAndChantCooldownsIndependent()
    {
        // HexCooldownRemaining and ChantCooldownRemaining tick independently.
        // Starting a chant doesn't affect the hex cooldown, and vice versa.
        var shaman = MakeShaman(x: 8, y: 5);
        var (state, _, _) = CreateArena(shaman);
        var comp = shaman.Get<OrcShamanComponent>()!;
        comp.HexCooldownRemaining = 5; // hex on cooldown

        // Chant starts (fires before hex).
        OrcShamanAI.Decide(shaman, state);
        Assert.That(comp.IsChanneling, Is.True, "Chant should start");

        // Hex cooldown should have decremented by 1 from step 6 (cooldowns tick before chant check).
        Assert.That(comp.HexCooldownRemaining, Is.EqualTo(4),
            "HexCooldownRemaining should tick independently while channeling");
    }

    [Test]
    public void Chant_PlayerEffectClearedWhenShamanDies()
    {
        // R4: if the shaman dies while channeling, DissonantChantEffect must be removed
        // from the player so they are not permanently slowed.
        var shaman = MakeShaman(id: 1, x: 6, y: 5); // adjacent to player
        var (state, player, _) = CreateArena(shaman);
        var comp = shaman.Get<OrcShamanComponent>()!;

        // Apply chant effect (as if shaman had channeled already).
        player.Add(new DissonantChantEffect { RemainingTurns = 999, ChantingShamanId = shaman.Id });
        comp.IsChanneling = true;
        comp.ChantTurnsRemaining = 1;

        Assert.That(player.Has<DissonantChantEffect>(), Is.True, "Effect should be present before death");

        // Kill the shaman via player attack (high damage: damageMin=3, damageMax=5, shaman has 24 HP).
        // With DexMod=+2 and shaman AC=10, attack roll always hits.
        // We need multiple attacks potentially, but a 24 HP shaman should die in 5-8 hits.
        for (int i = 0; i < 20 && shaman.Require<Fighter>().IsAlive; i++)
            TurnController.ProcessTurn(state, PlayerAction.Attack(shaman));

        Assert.That(shaman.Require<Fighter>().IsAlive, Is.False, "Shaman must be dead for this test");
        Assert.That(player.Has<DissonantChantEffect>(), Is.False,
            "DissonantChantEffect must be removed from player when the channeling shaman dies (R4)");
    }
}
