using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Endgame;
using UnderWarden.Logic.Persistence.MidRun;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

/// <summary>
/// S2 determinism soak (M1.4 §Determinism soak). Two branches run the same N turns — one continuing
/// in place (A), one via SaveMidRun→LoadMidRun (B) — and their per-turn state-hash SEQUENCES must be
/// identical element-by-element, not just at the endpoints.
///
/// Canonical StateHash = SHA-256 of SaveMidRun's serialized bytes. No bespoke state walk:
/// serialization is deterministic and byte-identical (S1), so the serializer IS the canonical state
/// function; the hash covers exactly the save boundary by definition.
///
/// Determinism-by-design (not flaky-by-design): BotBrain.Decide reads only GameState (player/monsters/
/// map — all hash-covered) plus its own per-instance stuck-detection state, and has NO out-of-state
/// randomness or wall-clock dependence (verified: no new Random/DateTime/Environment). Both branches
/// use FRESH bots at the branch point, so identical hashes ⇒ identical Decide inputs ⇒ identical
/// action streams. Combat RNG lives in state.Rng (Seed+CallCount), which the hash covers.
/// </summary>
[TestFixture]
public class MidRunS2SoakTests
{
    private const int SoakTurns = 55;   // >= 50 per spec

    // ── canonical hash ────────────────────────────────────────────────────────
    private static string StateHash(GameState s)
    {
        var dto = MidRunSerializer.SaveMidRun(s);
        var json = JsonSerializer.Serialize(dto, MidRunSaveJsonContext.Default.MidRunSaveDto);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static BotBrain FreshBot() => new(BotPersonaRegistry.Get("balanced"));

    private static PlayerAction Decide(BotBrain bot, GameState s) =>
        BotBrain.ToPlayerAction(bot.Decide(s.Player, s.PlayerFighter, s.PlayerInventory, s.Monsters, s.Map, null));

    private static List<string> RunRecording(GameState s, int n)
    {
        var bot = FreshBot();
        var hashes = new List<string>(n);
        for (int t = 0; t < n && !s.IsGameOver; t++)
        {
            TurnController.ProcessTurn(s, Decide(bot, s));
            hashes.Add(StateHash(s));
        }
        return hashes;
    }

    private static void RunS2(GameState atBranch, string label, int n = SoakTurns)
    {
        var branchDto = MidRunSerializer.SaveMidRun(atBranch);       // snapshot BEFORE mutating A
        var seqA = RunRecording(atBranch, n);
        var seqB = RunRecording(MidRunSerializer.LoadMidRun(branchDto), n);

        int min = Math.Min(seqA.Count, seqB.Count);
        for (int i = 0; i < min; i++)
            if (seqA[i] != seqB[i])
                Assert.Fail($"{label}: hash sequences diverge at turn {i} (A={seqA[i][..12]}… B={seqB[i][..12]}…).");
        Assert.That(seqB.Count, Is.EqualTo(seqA.Count),
            $"{label}: run lengths diverge (A={seqA.Count}, B={seqB.Count}) — save/load changed when the run ends.");
        Assert.That(seqA.Count, Is.GreaterThanOrEqualTo(50), $"{label}: soak must record >= 50 turns (got {seqA.Count}).");
    }

    // ── a base that reliably runs 50+ turns: tanky adjacent monsters + a very tanky player, so combat
    //    never ends (no all-dead), the bot always has an adjacent target (no stuck/abort), and the
    //    player survives. RNG still turns over every combat turn. ─────────────────────────────────
    private static (MonsterFactory m, ItemFactory i, ConsumableFactory c) Factories()
    {
        const string yaml = @"
monsters:
  orc_grunt:
    name: Orc
    stats: { hp: 4000, xp: 35, damage_min: 1, damage_max: 2, strength: 14, dexterity: 10, constitution: 12, accuracy: 4, evasion: 1 }
    char: o
    blocks: true
    faction: orc
weapons:
  short_sword: { name: Short Sword, slot: main_hand, damage_min: 1, damage_max: 2, to_hit_bonus: 1 }
armor:
  leather_armor: { name: Leather Armor, slot: chest, armor_class_bonus: 2 }
consumables:
  healing_potion: { name: Healing Potion, heal_amount: 40 }
";
        var b = new ContentLoader().LoadAll(yaml);
        var ef = new EntityFactory(startId: 1);
        return (new MonsterFactory(b.Monsters, ef), new ItemFactory(b.Items, ef), new ConsumableFactory(b.Consumables, ef));
    }

    private ConsumableFactory _consumables = null!;

    private GameState Base(int seed)
    {
        var (m, i, c) = Factories();
        _consumables = c;
        var scenario = new ScenarioDefinition
        {
            ScenarioId = "s2", Depth = 3, TurnLimit = 100_000, MapWidth = 16, MapHeight = 16,
            PlayerStartX = 6, PlayerStartY = 8,
            Player = new ScenarioPlayer { Hp = 100_000, Strength = 14, Weapon = "short_sword", Armor = "leather_armor" },
            Monsters = new()
            {
                new ScenarioMonster { Type = "orc_grunt", Count = 1, Position = new[] { 7, 8 } },
                new ScenarioMonster { Type = "orc_grunt", Count = 1, Position = new[] { 5, 8 } },
                new ScenarioMonster { Type = "orc_grunt", Count = 1, Position = new[] { 6, 7 } },
            },
            Items = new() { new ScenarioItem { Type = "healing_potion", Count = 2 } },
        };
        var s = GameStateFactory.FromScenario(scenario, seed, m, i, c);
        // a couple of warm-up turns so combat RNG has advanced before the branch
        var bot = FreshBot();
        for (int t = 0; t < 3 && !s.IsGameOver; t++) TurnController.ProcessTurn(s, Decide(bot, s));
        return s;
    }

    private static int FreshId(GameState s) =>
        new[] { s.Player }.Concat(s.Monsters).Concat(s.FloorItems).Concat(s.Corpses)
            .Concat(s.Features).Concat(s.Map.Entities).Select(e => e.Id).Max() + 1;

    // ── the soak, one feature per case (feature asserted PRESENT before capture) ──────────────────

    [Test]
    public void S2_StatusEffects()
    {
        var s = Base(1337);
        s.Monsters[0].Add(new BleedEffect { RemainingTurns = 40, Severity = 3 });
        s.Player.Add(new SluggishEffect { RemainingTurns = 40, SpeedPenaltyRatio = 0.5f });
        Assert.That(s.Monsters[0].Get<BleedEffect>(), Is.Not.Null);
        Assert.That(s.Player.Get<SluggishEffect>(), Is.Not.Null);
        RunS2(s, "status-effects");
    }

    [Test]
    public void S2_Corpses()
    {
        var s = Base(24601);
        var corpse = new Entity(FreshId(s), "orc corpse", 6, 9);
        corpse.Add(new SpeciesTag("orc_grunt"));
        corpse.Add(new CorpseComponent { OriginalMonsterId = "orc_grunt", OriginalName = "Orc", DeathTurn = 2, State = CorpseState.Fresh, CorpseId = "c1", BaseHp = 28 });
        s.Corpses.Add(corpse);
        s.Map.RegisterEntity(corpse);
        Assert.That(s.Corpses.Count, Is.GreaterThan(0));
        RunS2(s, "corpses");
    }

    [Test]
    public void S2_Possession()
    {
        var s = Base(8675309);
        var host = s.Monsters[0];
        host.Add(new PossessionEffect { RemainingTurns = 80, PossessorEntityId = s.Player.Id, DrainPerTurn = 0, Source = PossessionSource.PlayerInitiated, EnteredTurn = 2 });
        Assert.That(s.Monsters.Any(m => m.Get<PossessionEffect>()?.Source == PossessionSource.PlayerInitiated), Is.True);
        Assert.That(s.ControlledEntity, Is.SameAs(host), "possession must redirect control to the host.");
        RunS2(s, "possession");
    }

    [Test]
    public void S2_PortalPair()
    {
        var s = Base(4242);
        int a = FreshId(s);
        var pa = new Entity(a, "portal A", 2, 2); pa.Add(new PortalComponent { Type = PortalType.Entrance, LinkedPortalId = a + 1 });
        var pb = new Entity(a + 1, "portal B", 12, 12); pb.Add(new PortalComponent { Type = PortalType.Exit, LinkedPortalId = a });
        s.Portals.Add(pa); s.Portals.Add(pb);
        s.Map.RegisterEntity(pa); s.Map.RegisterEntity(pb);
        Assert.That(s.Portals.Count, Is.EqualTo(2));
        Assert.That(s.Portals[0].Require<PortalComponent>().LinkedPortalId, Is.EqualTo(s.Portals[1].Id));
        RunS2(s, "portal-pair");
    }

    [Test]
    public void S2_LockedDoor()
    {
        var s = Base(999);
        s.Map.SetTile(1, 8, TileKind.LockedDoor);
        s.LockedDoors[(1, 8)] = 2;
        Assert.That(s.LockedDoors.Count, Is.GreaterThan(0));
        RunS2(s, "locked-door");
    }

    [Test]
    public void S2_UnopenedChest()
    {
        var s = Base(31337);
        int id = FreshId(s);
        var loot = new Entity(id, "healing_potion"); loot.Add(new Consumable(healAmount: 20, isPotion: true)); loot.Add(new ItemTag("healing_potion"));
        var prop = new Entity(id + 1, "barrel", 6, 10, blocksMovement: true);
        prop.Add(new DestructiblePropComponent { PropKind = "barrel", LootEntityIds = new List<int> { loot.Id } });
        prop.Add(new ChestLootStash(new List<Entity> { loot }));
        s.Features.Add(prop);
        s.Map.RegisterEntity(prop);
        Assert.That(s.Features.Any(f => f.Get<ChestLootStash>()?.Items.Count > 0), Is.True, "an unopened chest with loot must be present.");
        RunS2(s, "unopened-chest");
    }

    [Test]
    public void S2_MidCooldownItem()
    {
        var s = Base(70707);
        s.PlayerFighter.PotionCooldownRemaining = 6;
        Assert.That(s.PlayerFighter.PotionCooldownRemaining, Is.GreaterThan(0), "a mid-cooldown item must be present.");
        RunS2(s, "mid-cooldown");
    }

    [Test]
    public void S2_LadenPlayer()
    {
        var s = Base(1337);
        int id = FreshId(s);
        var inv = s.Player.Get<Inventory>() ?? s.Player.Add(new Inventory());

        // Unique type ids so they don't merge with the scenario's existing healing_potion loadout.
        var stacked = new Entity(id++, "triage_stack"); stacked.Add(new ItemTag("triage_stack"));
        stacked.Add(new Consumable(healAmount: 20, isPotion: true) { StackSize = 8 });
        inv.Add(stacked);
        var partial = new Entity(id++, "triage_partial"); partial.Add(new ItemTag("triage_partial"));
        partial.Add(new Consumable(healAmount: 0, isPotion: true) { StackSize = 3 });
        inv.Add(partial);

        var eq = s.Player.Get<Equipment>() ?? s.Player.Add(new Equipment());
        if (eq.Head == null)
        {
            var helm = new Entity(id++, "helm"); helm.Add(new ItemTag("helm"));
            helm.Add(new Equippable(EquipmentSlot.Head) { ArmorClassBonus = 1 });
            eq.Head = helm;
        }
        if (eq.LeftRing == null)
        {
            var ring = new Entity(id++, "ring"); ring.Add(new ItemTag("ring"));
            ring.Add(new Equippable(EquipmentSlot.LeftRing));
            eq.LeftRing = ring;
        }

        Assert.That(inv.Items.Any(i => i.Get<Consumable>()?.StackSize == 8), Is.True, "the 8-stack must be present before capture.");
        Assert.That(inv.Items.Any(i => i.Get<Consumable>()?.StackSize == 3), Is.True, "the partial stack must be present before capture.");
        Assert.That(eq.Head, Is.Not.Null);
        RunS2(s, "laden-player");
    }

    [Test]
    public void S2_DungeonMode()
    {
        var (m, i, c) = Factories();
        var registry = LevelTemplateRegistry.FromYaml("levels: {}");
        var state = new DungeonFloorBuilder(registry, m, i, c).Build(3, new SeededRandom(2024));
        state.PlayerFighter.Hp = 100_000;   // survive the soak
        // Inject a tanky monster adjacent to the player so the bot has a target for the whole soak.
        var (px, py) = (state.Player.X, state.Player.Y);
        var cell = new[] { (px + 1, py), (px - 1, py), (px, py + 1), (px, py - 1) }
            .First(cc => state.Map.InBounds(cc.Item1, cc.Item2) && state.Map.IsWalkable(cc.Item1, cc.Item2));
        var orc = m.Create("orc_grunt", depth: 3, rng: new SeededRandom(1))!;
        orc.X = cell.Item1; orc.Y = cell.Item2;
        state.Monsters.Add(orc);
        state.Map.RegisterEntity(orc);
        Assert.That(state.IsDungeonMode, Is.True, "this seed must be a real dungeon floor.");
        RunS2(state, "dungeon-mode");
    }

    [Test]
    public void S2_WeighingFloor()
    {
        var arena = WeighingArenaDefinition.Build();
        var start = arena.FirstAnchor("player_start")!.Value;
        var player = new Entity(0, "Player", start.X, start.Y, blocksMovement: true);
        player.Add(new Fighter(hp: 100_000, strength: 14, dexterity: 14, constitution: 14, accuracy: 14, evasion: 0, damageMin: 1, damageMax: 2));
        arena.Map.RegisterEntity(player);
        var state = new GameState(player, new List<Entity>(), arena.Map, new SeededRandom(1337), turnLimit: 100_000)
        {
            IsDungeonMode = true, CurrentDepth = WeighingConstants.FinalFloorDepth, WeighingArena = arena,
            WeighingAudit = new WeighingAuditRegistry(new() { ["opening"] = new() { new WeighingDialoguePage("under_warden", "hi") } }),
        };
        var audit = new AuditScorer.AuditResult(GuardianTier.Savage, GuardianTier.Savage, GuardianTier.Savage, GuardianTier.Savage);
        WeighingOrchestrator.Begin(state, audit, swapAvailable: true, orcRepState: "hostile", cumulativeDeaths: 12, new List<TurnEvent>());
        Assert.That(state.Weighing, Is.Not.Null);
        Assert.That(state.Weighing!.ActiveGuardianId, Is.Not.Null, "a Guardian must be risen before the soak.");
        RunS2(state, "weighing-floor");
    }
}
