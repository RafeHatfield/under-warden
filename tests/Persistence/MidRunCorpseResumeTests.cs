using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Persistence.MidRun;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

/// <summary>
/// Device regression triage — symptom B: corpses render as live monsters after resume. Root cause is
/// DUAL MEMBERSHIP — a dead monster stays in state.Monsters AND state.Corpses, keeping its SpeciesTag
/// (TurnController.MakeCorpse). EntitySpriteManager.Initialize iterates state.Monsters and, on a fresh
/// floor, never sees a corpse; on resume it re-sprited them live. The fix skips Has&lt;CorpseComponent&gt;()
/// in Initialize.
///
/// This is the headless-catchable seam: the presentation-facing CLASSIFICATION signal (CorpseComponent
/// present, in Monsters, Fighter stripped) must survive resume so any correct renderer can distinguish
/// a corpse from a live monster. If a future change dropped the dual membership or stripped
/// CorpseComponent on load, the render fix would silently break — this test guards that contract.
/// </summary>
[TestFixture]
public class MidRunCorpseResumeTests
{
    [Test]
    public void DualMembershipCorpse_SurvivesResume_AsClassifiableCorpse()
    {
        var map = new GameMap(8, 8);
        for (int x = 0; x < 8; x++) for (int y = 0; y < 8; y++) map.SetTile(x, y, TileKind.Floor);

        var player = new Entity(1, "Player", 4, 4, blocksMovement: true);
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 10, constitution: 10, accuracy: 5, evasion: 0, damageMin: 1, damageMax: 2));
        map.RegisterEntity(player);

        // A live monster and a corpse (dual membership, mirroring TurnController.MakeCorpse output:
        // CorpseComponent added, Fighter stripped, SpeciesTag kept, present in BOTH lists).
        var live = new Entity(2, "Orc", 5, 4, blocksMovement: true);
        live.Add(new SpeciesTag("orc_grunt"));
        live.Add(new Fighter(hp: 20, strength: 12, dexterity: 10, constitution: 10, accuracy: 3, evasion: 1, damageMin: 3, damageMax: 5));
        map.RegisterEntity(live);

        var corpse = new Entity(3, "Orc", 6, 4);
        corpse.Add(new SpeciesTag("orc_grunt"));   // KEPT — the reason a naive renderer sprites it live
        corpse.Add(new CorpseComponent { OriginalMonsterId = "orc_grunt", OriginalName = "Orc", DeathTurn = 5, State = CorpseState.Fresh, CorpseId = "c1", BaseHp = 20 });
        corpse.BlocksMovement = false;
        map.RegisterEntity(corpse);

        var state = new GameState(player, new List<Entity> { live, corpse }, map, new SeededRandom(1), turnLimit: 1000);
        state.Corpses.Add(corpse);   // dual membership
        Assume.That(state.Monsters, Does.Contain(corpse));

        var json = JsonSerializer.Serialize(MidRunSerializer.SaveMidRun(state), MidRunSaveJsonContext.Default.MidRunSaveDto);
        var loaded = MidRunSerializer.LoadMidRun(JsonSerializer.Deserialize(json, MidRunSaveJsonContext.Default.MidRunSaveDto)!);

        var loadedCorpse = loaded.Monsters.SingleOrDefault(m => m.Id == 3);
        var loadedLive = loaded.Monsters.SingleOrDefault(m => m.Id == 2);

        Assert.Multiple(() =>
        {
            Assert.That(loadedCorpse, Is.Not.Null, "the corpse must remain in state.Monsters (dual membership).");
            Assert.That(loaded.Corpses.Any(c => c.Id == 3), Is.True, "the corpse must remain in state.Corpses.");
            Assert.That(loadedCorpse!.Has<CorpseComponent>(), Is.True, "CorpseComponent is the render-classification signal — it must survive resume.");
            Assert.That(loadedCorpse.Get<Fighter>(), Is.Null, "a corpse has no Fighter — it is not a live combatant.");
            Assert.That(loadedCorpse.Get<SpeciesTag>(), Is.Not.Null, "SpeciesTag is kept — which is exactly why the renderer must branch on CorpseComponent.");

            Assert.That(loadedLive, Is.Not.Null);
            Assert.That(loadedLive!.Has<CorpseComponent>(), Is.False, "the live monster must NOT be classified as a corpse.");
            Assert.That(loadedLive.Get<Fighter>(), Is.Not.Null, "the live monster keeps its Fighter.");
        });
    }

    /// <summary>
    /// The LIVE-play death seam must yield the same corpse-classified "visual intent" the resume seam
    /// guards above. After a real ProcessTurn kill, the dead entity is in BOTH state.Corpses and
    /// state.Monsters, carries CorpseComponent, has no Fighter, and keeps its SpeciesTag. That composite
    /// is exactly what the two presentation halves consume: CorpseSpriteManager.Sync builds a corpse
    /// sprite from state.Corpses (species sprite via the kept SpeciesTag, under the corpse treatment),
    /// and EntitySpriteManager skips Has&lt;CorpseComponent&gt;() so the remains are never re-sprited as a
    /// live monster. If a future change stopped emitting this composite at the live seam, corpses would
    /// silently stop rendering — this test guards that.
    /// </summary>
    [Test]
    public void LiveDeath_YieldsCorpseClassifiedVisualIntent()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(20, 20);

        var player = new Entity(0, "Player", 5, 10, blocksMovement: true);
        // dexterity 18 + damage 20 one-shots the 1-HP orc.
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 18, constitution: 12,
            accuracy: 5, evasion: 1, damageMin: 20, damageMax: 20));
        map.RegisterEntity(player);

        var orc = new Entity(1, "Orc", 6, 10, blocksMovement: true);
        orc.Add(new Fighter(hp: 1, strength: 10, dexterity: 4, constitution: 10,
            accuracy: 3, evasion: 1, damageMin: 2, damageMax: 4));
        orc.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        orc.Add(new SpeciesTag("orc"));
        map.RegisterEntity(orc);

        var state = new GameState(player, new List<Entity> { orc }, map, rng);

        TurnController.ProcessTurn(state, PlayerAction.Attack(orc));

        Assert.Multiple(() =>
        {
            Assert.That(state.Corpses, Does.Contain(orc), "corpse is in state.Corpses — CorpseSpriteManager.Sync renders from here.");
            Assert.That(state.Monsters, Does.Contain(orc), "corpse keeps dual membership in state.Monsters.");
            Assert.That(orc.Has<CorpseComponent>(), Is.True, "CorpseComponent is the render-classification signal.");
            Assert.That(orc.Get<Fighter>(), Is.Null, "a corpse has no Fighter — not a live combatant, so EntitySpriteManager.UpdatePositions leaves it alone.");
            Assert.That(orc.Get<SpeciesTag>(), Is.Not.Null, "SpeciesTag kept — CorpseSpriteManager.InferSpriteBase resolves the species sprite for the remains.");
        });
    }
}
