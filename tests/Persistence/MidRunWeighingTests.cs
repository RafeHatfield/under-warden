using System.Text.Json;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Endgame;
using UnderWarden.Logic.Persistence.MidRun;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

/// <summary>
/// Floor-25 Weighing serializers (M1.4 4a.3b-3): a populated depth-25 gauntlet state round-trips
/// byte-identically (S1). With this, every GameState configuration is covered and the guard is gone.
/// </summary>
[TestFixture]
public class MidRunWeighingTests
{
    private static WeighingAuditRegistry SampleAudit() => new(new()
    {
        ["opening"] = new() { new WeighingDialoguePage("under_warden", "The ledger opens."), new WeighingDialoguePage("narrator", "A cold hall.") },
        ["debt"] = new() { new WeighingDialoguePage("guardian", "You owe.") },
    });

    private static GameState PopulatedWeighingFloor()
    {
        var arena = WeighingArenaDefinition.Build();
        var start = arena.FirstAnchor("player_start")!.Value;
        var player = new Entity(0, "Player", start.X, start.Y, blocksMovement: true);
        player.Add(new Fighter(hp: 500, strength: 14, dexterity: 14, constitution: 14, accuracy: 14, evasion: 0, damageMin: 5, damageMax: 8));
        arena.Map.RegisterEntity(player);

        var state = new GameState(player, new List<Entity>(), arena.Map, new SeededRandom(1337), turnLimit: 10_000)
        {
            IsDungeonMode = true,
            CurrentDepth = WeighingConstants.FinalFloorDepth,
            WeighingArena = arena,
            WeighingAudit = SampleAudit(),
        };

        // Begin the gauntlet (all-savage → a hostile Guardian rises) and take a few turns.
        var audit = new AuditScorer.AuditResult(GuardianTier.Savage, GuardianTier.Savage, GuardianTier.Savage, GuardianTier.Savage);
        WeighingOrchestrator.Begin(state, audit, swapAvailable: true, orcRepState: "hostile", cumulativeDeaths: 12, new List<TurnEvent>());
        for (int t = 0; t < 4 && !state.IsGameOver; t++)
            TurnController.ProcessTurn(state, PlayerAction.Wait);
        return state;
    }

    [Test]
    public void WeighingFloor_S1_ByteIdentical()
    {
        var state = PopulatedWeighingFloor();

        // Populated, non-default — a null round-trip would not be coverage.
        Assert.Multiple(() =>
        {
            Assert.That(state.Weighing, Is.Not.Null);
            Assert.That(state.Weighing!.Audit.AnySavage, Is.True, "audit must be populated (Savage tiers).");
            Assert.That(state.Weighing.ActiveGuardianId, Is.Not.Null, "a Guardian must be risen.");
            Assert.That(state.Weighing.SwapAvailable, Is.True);
            Assert.That(state.WeighingArena, Is.Not.Null);
            Assert.That(state.WeighingArena!.Anchors.Count, Is.GreaterThan(0), "arena anchors must be populated.");
            Assert.That(state.WeighingAudit, Is.Not.Null);
            Assert.That(state.WeighingAudit!.Sequences.Count, Is.GreaterThan(0), "audit dialogue must be populated.");
            Assert.That(state.Monsters.Any(m => m.Id == state.Weighing.ActiveGuardianId), Is.True, "the risen Guardian is a real entity.");
        });

        var dto1 = MidRunSerializer.SaveMidRun(state);
        string json1 = JsonSerializer.Serialize(dto1, MidRunSaveJsonContext.Default.MidRunSaveDto);

        var reloaded = MidRunSerializer.LoadMidRun(dto1);
        // Weighing state survived with identity into the risen Guardian.
        Assert.That(reloaded.Weighing!.ActiveGuardianId, Is.EqualTo(state.Weighing!.ActiveGuardianId));
        Assert.That(reloaded.WeighingArena!.Map, Is.SameAs(reloaded.Map), "the arena reuses the restored GameState map.");
        Assert.That(reloaded.WeighingAudit!.Sequences.Count, Is.EqualTo(state.WeighingAudit!.Sequences.Count));

        var dto2 = MidRunSerializer.SaveMidRun(reloaded);
        string json2 = JsonSerializer.Serialize(dto2, MidRunSaveJsonContext.Default.MidRunSaveDto);
        Assert.That(json2, Is.EqualTo(json1), "weighing-floor S1 must be byte-identical.");
    }
}
