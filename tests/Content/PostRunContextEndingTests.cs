using System.Collections.Generic;
using System.Linq;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Endgame;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

/// <summary>
/// PR-A (mini-spec #42, ruling Q3): PostRunContext.Ending is content-only data plumbing.
/// Nothing consumes the field yet — these tests only prove the record carries the value
/// correctly, not any downstream behavior.
/// </summary>
[TestFixture]
public class PostRunContextEndingTests
{
    [Test]
    public void Ending_DefaultsToNone_WhenOmitted()
    {
        var ctx = new PostRunContext(Died: false, CauseOfDeath: null, KillerSpecies: null,
            FloorReached: 5, RunNumber: 1);

        Assert.That(ctx.Ending, Is.EqualTo(EndingType.None));
    }

    // ── Real end-of-run path: drive an actual Weighing resolution, then construct the
    //    context the same way Main.cs does (Ending: state.Ending), proving the plumbing
    //    against a genuinely-resolved GameState.Ending rather than a hand-typed literal. ──

    private static GameState ArenaState()
    {
        var arena = WeighingArenaDefinition.Build();
        var start = arena.FirstAnchor("player_start")!.Value;
        var player = new Entity(0, "Player", start.X, start.Y, blocksMovement: true);
        player.Add(new Fighter(hp: 500, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 14, evasion: 0, damageMin: 5, damageMax: 8));
        arena.Map.RegisterEntity(player);
        return new GameState(player, new List<Entity>(), arena.Map, new SeededRandom(1337), turnLimit: 10_000)
        {
            IsDungeonMode = true,
            CurrentDepth = WeighingConstants.FinalFloorDepth,
            WeighingArena = arena,
            PersistentState = null,
            WeighingHeadlessGatePolicy = WeighingGateDecision.Refuse,
        };
    }

    [Test]
    public void PostRunContext_ConstructedFromResolvedWeighing_CarriesTheRealEnding()
    {
        // Same setup shape as WeighingOrchestratorTests.HeadlessGatePolicy_Refuse_ResolvesToLossRefused
        // — the cheapest existing path to a real, non-default resolved Ending (no combat loop needed
        // beyond clearing whichever Guardians rise before the Refuse gate fires).
        var state = ArenaState();
        WeighingOrchestrator.BeginFromPersistence(state, new List<TurnEvent>());

        while (state.Weighing!.Phase == WeighingPhase.Guardians)
        {
            var g = state.Monsters.First(m => m.Id == state.Weighing.ActiveGuardianId);
            g.Require<Fighter>().TakeDamage(99999);
            WeighingOrchestrator.Advance(state, new List<TurnEvent>());
        }

        Assert.That(state.Weighing.Phase, Is.EqualTo(WeighingPhase.Resolved));
        Assert.That(state.Ending, Is.EqualTo(EndingType.LossRefused), "Sanity check on the resolved ending.");

        // Construct PostRunContext exactly the way Main.cs's real end-of-run flush does.
        var ctx = new PostRunContext(
            Died: false,
            CauseOfDeath: null,
            KillerSpecies: null,
            FloorReached: WeighingConstants.FinalFloorDepth,
            RunNumber: 1,
            Ending: state.Ending);

        Assert.That(ctx.Ending, Is.EqualTo(EndingType.LossRefused),
            "PostRunContext.Ending must carry the real resolved GameState.Ending through construction.");
    }
}
