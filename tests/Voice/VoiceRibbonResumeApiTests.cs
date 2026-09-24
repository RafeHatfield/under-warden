using System.Linq;
using System.Text.Json;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Persistence.MidRun;
using UnderWarden.Logic.Voice;
using NUnit.Framework;

namespace UnderWarden.Tests.Voice;

/// <summary>
/// The M1.5b resume contract exactly as Main drives it: a voice-bearing save reloaded through the
/// 4-arg LoadMidRun (boonTable + ribbon registry + tier metadata) restores the scheduler so the ribbon
/// history and bag/one-shot continuity are visible through the API — no repeated one-shots after resume.
/// </summary>
[TestFixture]
public class VoiceRibbonResumeApiTests
{
    private const string ContentYaml = @"
monsters:
  orc_grunt:
    name: Orc
    stats: { hp: 20, xp: 25, damage_min: 3, damage_max: 5, strength: 12, dexterity: 10, constitution: 10, accuracy: 3, evasion: 1 }
    char: o
    ai_type: basic
    blocks: true
    faction: orc
";

    private static VoiceLineRegistry Registry() => VoiceLineRegistry.LoadFromYaml(
        "hp_critical: [hp1, hp2, hp3]\nfirst: [greet1]\ntrap: [trap1, trap2]\n");

    private static VoiceTierMetadata Meta() => new(ambientCutoffTier: 5, new[]
    {
        new VoiceFamilyMeta("hp_critical", 70, 0, false, CooldownExempt: true),
        new VoiceFamilyMeta("first", 40, 1, OncePerRun: true, false),   // one-shot
        new VoiceFamilyMeta("trap", 30, 2, false, false),
    });

    private static GameState DungeonFloor(int seed)
    {
        var b = new ContentLoader().LoadAll(ContentYaml);
        var ef = new EntityFactory(startId: 1);
        return new DungeonFloorBuilder(LevelTemplateRegistry.FromYaml("levels: {}"),
            new MonsterFactory(b.Monsters, ef), new ItemFactory(b.Items, ef), new ConsumableFactory(b.Consumables, ef))
            .Build(3, new SeededRandom(seed));
    }

    private static string[] T(params string[] f) => f;

    [Test]
    public void VoiceBearingSave_ResumesHistoryBagAndOneShotContinuity()
    {
        var state = DungeonFloor(1337);
        state.VoiceScheduler = new VoiceScheduler(Registry(), Meta(), state.Rng.Seed);

        // Pre-save: burn the one-shot + a couple of hp_critical draws (exempt → every turn).
        Assert.That(state.VoiceScheduler.TryDeliver(T("first"), VoiceMode.Verbose, 0)?.Line, Is.EqualTo("greet1"));
        state.VoiceScheduler.TryDeliver(T("hp_critical"), VoiceMode.Verbose, 1);
        state.VoiceScheduler.TryDeliver(T("hp_critical"), VoiceMode.Verbose, 2);
        var historyBefore = state.VoiceScheduler.HistorySnapshot()
            .Select(h => $"{h.Family}:{h.Line}:{h.Turn}").ToList();
        Assert.That(historyBefore, Has.Count.EqualTo(3));

        // Round-trip through the REAL serializer + JSON, then Main's 4-arg LoadMidRun.
        var json = JsonSerializer.Serialize(MidRunSerializer.SaveMidRun(state), MidRunSaveJsonContext.Default.MidRunSaveDto);
        var dto = JsonSerializer.Deserialize(json, MidRunSaveJsonContext.Default.MidRunSaveDto)!;
        var loaded = MidRunSerializer.LoadMidRun(dto, boonTable: null, voiceRegistry: Registry(), voiceMeta: Meta());

        Assert.That(loaded.VoiceScheduler, Is.Not.Null);
        var historyAfter = loaded.VoiceScheduler!.HistorySnapshot()
            .Select(h => $"{h.Family}:{h.Line}:{h.Turn}").ToList();
        Assert.That(historyAfter, Is.EqualTo(historyBefore), "ribbon history must survive resume, in order.");

        // One-shot already spent pre-save must NOT fire again after resume.
        Assert.That(loaded.VoiceScheduler.TryDeliver(T("first"), VoiceMode.Verbose, 20), Is.Null,
            "a one-shot fired before the save must stay spent after resume.");

        // Bag continuity: hp_critical continues its shuffle without an out-of-range or reset draw.
        var next = loaded.VoiceScheduler.TryDeliver(T("hp_critical"), VoiceMode.Verbose, 21);
        Assert.That(next, Is.Not.Null);
        Assert.That(new[] { "hp1", "hp2", "hp3" }, Does.Contain(next!.Line));
    }
}
