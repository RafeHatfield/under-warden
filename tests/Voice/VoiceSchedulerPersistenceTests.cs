using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Persistence.MidRun;
using UnderWarden.Logic.Voice;
using NUnit.Framework;

namespace UnderWarden.Tests.Voice;

/// <summary>
/// Voice scheduler through the SAVE boundary (docs/systems/voice_delivery.md §Save boundary +
/// §Determinism). Proves: the run state round-trips byte-identically through the real MidRunSerializer +
/// source-gen JSON (voice-scoped S1); a save→load mid-run continues identically (piggybacks the S2
/// pattern); and the gameplay stream is isolated — gameplay hashes are identical whether or not the
/// scheduler runs, and TryDeliver never touches the gameplay Rng.
/// </summary>
[TestFixture]
public class VoiceSchedulerPersistenceTests
{
    private const string ContentYaml = @"
monsters:
  orc_grunt:
    name: Orc
    stats: { hp: 4000, xp: 25, damage_min: 1, damage_max: 2, strength: 12, dexterity: 10, constitution: 10, accuracy: 3, evasion: 1 }
    char: o
    ai_type: basic
    blocks: true
    faction: orc
weapons:
  short_sword: { name: Short Sword, slot: main_hand, damage_min: 1, damage_max: 2, to_hit_bonus: 1 }
consumables:
  healing_potion: { name: Healing Potion, heal_amount: 20 }
";

    private static VoiceLineRegistry Registry() => VoiceLineRegistry.LoadFromYaml(
        "hp_critical: [hp1, hp2, hp3]\ntrap: [trap1, trap2, trap3, trap4]\nidle: [idle1, idle2]\n");

    private static VoiceTierMetadata Meta() => new(ambientCutoffTier: 20, new[]
    {
        new VoiceFamilyMeta("hp_critical", 70, 0, false, CooldownExempt: true),
        new VoiceFamilyMeta("trap", 30, 1, false, false),
        new VoiceFamilyMeta("idle", 10, 2, false, false),
    });

    private static string[] Trig(params string[] f) => f;

    private static (MonsterFactory m, ItemFactory i, ConsumableFactory c) Factories()
    {
        var b = new ContentLoader().LoadAll(ContentYaml);
        var ef = new EntityFactory(startId: 1);
        return (new MonsterFactory(b.Monsters, ef), new ItemFactory(b.Items, ef), new ConsumableFactory(b.Consumables, ef));
    }

    private static GameState DungeonFloor(int seed)
    {
        var (m, i, c) = Factories();
        return new DungeonFloorBuilder(LevelTemplateRegistry.FromYaml("levels: {}"), m, i, c)
            .Build(3, new SeededRandom(seed));
    }

    private static string Json(MidRunSaveDto dto) =>
        JsonSerializer.Serialize(dto, MidRunSaveJsonContext.Default.MidRunSaveDto);

    // A fixed post-save trigger script, starting well past the pre-save turns.
    private static List<string> Continue(VoiceScheduler s)
    {
        var outp = new List<string>();
        for (int t = 100; t < 130; t++)
        {
            var d = s.TryDeliver(Trig("hp_critical", "trap"), VoiceMode.Verbose, t);
            if (d != null) outp.Add($"{t}:{d.Family}:{d.Line}");
        }
        return outp;
    }

    // ── voice-scoped S1 + save→load continuation ────────────────────────────────

    [Test]
    public void Voice_RoundTripsByteIdentical_AndContinuesIdentically()
    {
        var state = DungeonFloor(1337);
        state.VoiceScheduler = new VoiceScheduler(Registry(), Meta(), state.Rng.Seed);
        // Deliver a handful pre-save so bags/history/rng/cooldown all carry non-trivial state.
        for (int t = 0; t < 8; t++) state.VoiceScheduler.TryDeliver(Trig("hp_critical", "trap"), VoiceMode.Verbose, t);
        Assert.That(state.VoiceScheduler.HistorySnapshot(), Is.Not.Empty, "pre-save state must be non-trivial.");

        var json1 = Json(MidRunSerializer.SaveMidRun(state));
        var dto2 = JsonSerializer.Deserialize(json1, MidRunSaveJsonContext.Default.MidRunSaveDto)!;
        var loaded = MidRunSerializer.LoadMidRun(dto2, voiceRegistry: Registry(), voiceMeta: Meta());

        // Voice-scoped S1: re-save of the loaded state is byte-identical.
        Assert.That(loaded.VoiceScheduler, Is.Not.Null, "voice state must survive the round-trip.");
        var json2 = Json(MidRunSerializer.SaveMidRun(loaded));
        Assert.That(json2, Is.EqualTo(json1), "voice run state must round-trip byte-identically.");

        // S2-style continuation: both schedulers deliver the identical sequence from here.
        var contA = Continue(state.VoiceScheduler!);
        var contB = Continue(loaded.VoiceScheduler!);
        Assert.That(contB, Is.EqualTo(contA), "save→load must continue the delivery sequence identically.");
        Assert.That(contA, Is.Not.Empty);
    }

    [Test]
    public void LoadWithVoiceStateButNoRegistry_FailsLoud()
    {
        var state = DungeonFloor(7);
        state.VoiceScheduler = new VoiceScheduler(Registry(), Meta(), state.Rng.Seed);
        state.VoiceScheduler.TryDeliver(Trig("hp_critical"), VoiceMode.Verbose, 0);
        var dto = MidRunSerializer.SaveMidRun(state);
        // RECONSTRUCT contract: voice present but no registry/meta provided → loud fail, not silent drop.
        Assert.That(() => MidRunSerializer.LoadMidRun(dto), Throws.InvalidOperationException);
    }

    // ── gameplay-stream isolation ───────────────────────────────────────────────

    [Test]
    public void TryDeliver_NeverTouchesGameplayRng()
    {
        var state = DungeonFloor(1337);
        var sched = new VoiceScheduler(Registry(), Meta(), state.Rng.Seed);
        long before = state.Rng.CallCount;
        for (int t = 0; t < 30; t++) sched.TryDeliver(Trig("hp_critical", "trap"), VoiceMode.Verbose, t);
        Assert.That(state.Rng.CallCount, Is.EqualTo(before), "voice must not advance the gameplay Rng.");
        Assert.That(sched.RngCallCount, Is.GreaterThan(0), "the voice Rng must have actually done work.");
    }

    // ── isolation soak: gameplay hashes identical with the scheduler active vs absent ──

    private static BotBrain FreshBot() => new(BotPersonaRegistry.Get("balanced"));

    private static PlayerAction Decide(BotBrain bot, GameState s) =>
        BotBrain.ToPlayerAction(bot.Decide(s.Player, s.PlayerFighter, s.PlayerInventory, s.Monsters, s.Map, null));

    // gameplay-only hash: voice is temporarily detached so the hash covers exactly the gameplay save.
    private static string GameplayHash(GameState s)
    {
        var v = s.VoiceScheduler;
        s.VoiceScheduler = null;
        var h = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json(MidRunSerializer.SaveMidRun(s)))));
        s.VoiceScheduler = v;
        return h;
    }

    private static GameState TankyDungeon(int seed)
    {
        var s = DungeonFloor(seed);
        s.PlayerFighter.Hp = 100_000;                 // survive the soak
        var (m, _, _) = Factories();
        var (px, py) = (s.Player.X, s.Player.Y);
        var cell = new[] { (px + 1, py), (px - 1, py), (px, py + 1), (px, py - 1) }
            .First(cc => s.Map.InBounds(cc.Item1, cc.Item2) && s.Map.IsWalkable(cc.Item1, cc.Item2));
        var orc = m.Create("orc_grunt", depth: 3, rng: new SeededRandom(1))!;
        orc.X = cell.Item1; orc.Y = cell.Item2;
        s.Monsters.Add(orc);
        s.Map.RegisterEntity(orc);
        return s;
    }

    [Test]
    public void GameplayHashes_IdenticalWithSchedulerActiveVsAbsent()
    {
        var voiced = TankyDungeon(24601);
        var quiet = TankyDungeon(24601);
        voiced.VoiceScheduler = new VoiceScheduler(Registry(), Meta(), voiced.Rng.Seed);

        var botV = FreshBot();
        var botQ = FreshBot();
        int turns = 0;
        for (int t = 0; t < 30 && !voiced.IsGameOver && !quiet.IsGameOver; t++)
        {
            TurnController.ProcessTurn(voiced, Decide(botV, voiced));
            TurnController.ProcessTurn(quiet, Decide(botQ, quiet));
            voiced.VoiceScheduler!.TryDeliver(Trig("hp_critical", "trap"), VoiceMode.Verbose, t);

            Assert.That(voiced.Rng.CallCount, Is.EqualTo(quiet.Rng.CallCount), $"gameplay Rng diverged at turn {t}.");
            Assert.That(GameplayHash(voiced), Is.EqualTo(GameplayHash(quiet)), $"gameplay hash diverged at turn {t}.");
            turns++;
        }
        Assert.That(turns, Is.GreaterThanOrEqualTo(25), "the soak must actually run a meaningful number of turns.");
        Assert.That(voiced.VoiceScheduler!.RngCallCount, Is.GreaterThan(0), "voice must have actually run during the soak.");
    }
}
