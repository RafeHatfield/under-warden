using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Tests for Phase 2 bot telemetry infrastructure:
/// BotDecisionRecord, BotTelemetryRecorder, BotRunSummary, and BotBrain integration.
///
/// All tests run without Godot — pure logic layer.
/// None depend on specific combat outcomes; combat-touching tests use deterministic seeds.
/// </summary>
[TestFixture]
[Description("Bot telemetry: recording, summary computation, BotBrain integration")]
public class BotTelemetryTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a minimal game state: a 15x15 open floor with the player at center
    /// and an optional list of monsters. Used for BotBrain wiring tests.
    /// </summary>
    private static (Entity Player, Fighter PlayerFighter, Inventory PlayerInventory,
        List<Entity> Monsters, GameMap Map)
        MakeState(int playerHp = 54, bool addMonsterAdjacent = false, bool addHealingPotion = false)
    {
        const int Width = 15, Height = 15;
        var map = new GameMap(Width, Height, allWalls: true);
        for (int x = 1; x < Width - 1; x++)
            for (int y = 1; y < Height - 1; y++)
                map.SetTile(x, y, TileKind.Floor);

        var player = new Entity(0, "Player", 7, 7, blocksMovement: true);
        // constitution: 10 → CON mod = 0, so MaxHp == BaseMaxHp == hp. Clean for HP-fraction math.
        var fighter = new Fighter(
            hp: playerHp, strength: 12, dexterity: 14, constitution: 10,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4);
        player.Add(fighter);

        var inventory = new Inventory();
        player.Add(inventory);
        map.RegisterEntity(player);

        if (addHealingPotion)
        {
            var potion = new Entity(10, "Healing Potion");
            potion.Add(new Consumable(healAmount: 40, isPotion: true));
            inventory.Add(potion);
        }

        var monsters = new List<Entity>();
        if (addMonsterAdjacent)
        {
            // Place a monster at (8, 7) — one step east of player, Chebyshev distance 1
            var orc = new Entity(1, "Orc", 8, 7, blocksMovement: true);
            orc.Add(new Fighter(hp: 28, strength: 14, dexterity: 10, constitution: 12,
                accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
            map.RegisterEntity(orc);
            monsters.Add(orc);
        }

        return (player, fighter, inventory, monsters, map);
    }

    // ── Test 1: Recorder captures decisions ──────────────────────────────────

    [Test]
    [Description("BotTelemetryRecorder: Record() 5 times produces Decisions.Count == 5")]
    public void Recorder_CapturesDecisions()
    {
        var recorder = new BotTelemetryRecorder();

        for (int i = 0; i < 5; i++)
        {
            recorder.Record(new BotDecisionRecord
            {
                TurnNumber     = i + 1,
                FloorDepth     = 1,
                ActionType     = "Attack",
                Reason         = "attack_lowest_hp",
                HpFraction     = 0.8,
                VisibleEnemies = 1,
                AdjacentEnemies = 1,
                HealingPotionsAvailable = 0,
                InCombat       = true,
                LowHp          = false,
            });
        }

        Assert.That(recorder.Decisions.Count, Is.EqualTo(5),
            "Recorder should have captured exactly 5 decisions");
    }

    // ── Test 2: BotBrain with null context doesn't crash ─────────────────────

    [Test]
    [Description("BotBrain.Decide() with null context returns a valid action without throwing")]
    public void BotBrain_NullContext_NoException()
    {
        var (player, fighter, inventory, monsters, map) = MakeState(playerHp: 54, addMonsterAdjacent: true);

        BotAction action = default!;
        Assert.DoesNotThrow(
            () => action = BotBrain.Decide(player, fighter, inventory, monsters, map, null),
            "BotBrain.Decide with null context must not throw");

        // With an adjacent orc, the bot should attack
        Assert.That(action, Is.Not.Null);
        Assert.That(action.Type, Is.EqualTo(BotAction.ActionType.AttackTarget),
            "Bot should attack adjacent monster");
    }

    // ── Test 3: BotBrain with recorder emits exactly one decision ────────────

    [Test]
    [Description("BotBrain.Decide() with context emits exactly 1 decision with ActionType='Attack'")]
    public void BotBrain_WithContext_EmitsAttackDecision()
    {
        var (player, fighter, inventory, monsters, map) = MakeState(playerHp: 54, addMonsterAdjacent: true);

        var recorder = new BotTelemetryRecorder();
        var context  = new BotDecisionContext(recorder, TurnNumber: 1, FloorDepth: 1);

        BotBrain.Decide(player, fighter, inventory, monsters, map, context);

        Assert.That(recorder.Decisions.Count, Is.EqualTo(1),
            "Exactly one decision should be recorded per Decide() call");
        Assert.That(recorder.Decisions[0].ActionType, Is.EqualTo("Attack"),
            "ActionType should be 'Attack' when a monster is adjacent");
        Assert.That(recorder.Decisions[0].Reason, Is.EqualTo("attack_lowest_hp"),
            "Reason should be 'attack_lowest_hp' for focus-fire attack");
    }

    // ── Test 4: BotBrain heal reason at low HP ────────────────────────────────

    [Test]
    [Description("BotBrain.Decide() with player at ~20% HP and potion emits Reason='threshold_heal'")]
    public void BotBrain_HealReason_ThresholdHeal()
    {
        // Start at full HP (54), then reduce to 11 → 11/54 ≈ 20% — below HealThreshold (0.30)
        // but above PanicThreshold (0.15). CON 10 means MaxHp == BaseMaxHp == 54.
        var (player, fighter, inventory, monsters, map) = MakeState(
            playerHp: 54, addHealingPotion: true);
        // Simulate damage: reduce current HP to 11 without changing MaxHp
        fighter.Hp = 11;

        var recorder = new BotTelemetryRecorder();
        var context  = new BotDecisionContext(recorder, TurnNumber: 5, FloorDepth: 2);

        // Add a distant monster so the bot is not in no_targets state
        var distantOrc = new Entity(2, "DistantOrc", 1, 1, blocksMovement: true);
        distantOrc.Add(new Fighter(hp: 28, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        map.RegisterEntity(distantOrc);
        monsters.Add(distantOrc);

        BotBrain.Decide(player, fighter, inventory, monsters, map, context);

        Assert.That(recorder.Decisions.Count, Is.EqualTo(1));
        var d = recorder.Decisions[0];
        Assert.That(d.ActionType, Is.EqualTo("Heal"),
            "Bot should heal at 20% HP when a potion is available");
        Assert.That(d.Reason, Is.EqualTo("threshold_heal"),
            "Reason should be 'threshold_heal' at 20% HP");
        Assert.That(d.HpFraction, Is.EqualTo((double)fighter.Hp / fighter.MaxHp).Within(0.001),
            "HpFraction should match playerFighter.Hp / playerFighter.MaxHp");
    }

    // ── Test 5: Summary action counts ────────────────────────────────────────

    [Test]
    [Description("Summarize() correctly aggregates action counts from 18 decisions")]
    public void Summary_ActionCounts_Correct()
    {
        var recorder = new BotTelemetryRecorder();

        void AddDecisions(string actionType, string reason, int count)
        {
            for (int i = 0; i < count; i++)
                recorder.Record(new BotDecisionRecord
                {
                    TurnNumber  = 1, FloorDepth = 1,
                    ActionType  = actionType, Reason = reason,
                    HpFraction  = 0.8, VisibleEnemies = 1,
                });
        }

        AddDecisions("Attack",     "attack_lowest_hp", 10);
        AddDecisions("Heal",       "threshold_heal",    5);
        AddDecisions("MoveToward", "move_to_nearest",   3);

        var summary = recorder.Summarize();

        Assert.That(summary.ActionCounts["Attack"],     Is.EqualTo(10));
        Assert.That(summary.ActionCounts["Heal"],       Is.EqualTo(5));
        Assert.That(summary.ActionCounts["MoveToward"], Is.EqualTo(3));
        Assert.That(summary.TotalDecisions,             Is.EqualTo(18));
    }

    // ── Test 6: Summary avg HP when healing ──────────────────────────────────

    [Test]
    [Description("AvgHpWhenHealing is computed correctly from 3 heal decisions")]
    public void Summary_AvgHpWhenHealing_Correct()
    {
        var recorder = new BotTelemetryRecorder();

        // 3 heal decisions at 0.15, 0.25, 0.30 → average = 0.2333...
        foreach (double hp in new[] { 0.15, 0.25, 0.30 })
        {
            recorder.Record(new BotDecisionRecord
            {
                TurnNumber  = 1, FloorDepth = 1,
                ActionType  = "Heal", Reason = "threshold_heal",
                HpFraction  = hp, VisibleEnemies = 0,
                InCombat    = false, LowHp = true,
            });
        }

        var summary = recorder.Summarize();

        Assert.That(summary.HealDecisions, Is.EqualTo(3),
            "HealDecisions should count all Heal actions");
        Assert.That(summary.AvgHpWhenHealing,
            Is.EqualTo((0.15 + 0.25 + 0.30) / 3.0).Within(0.001),
            "AvgHpWhenHealing should be the mean of HpFraction across heal decisions");
    }

    // ── Test 7: Summary context counts ───────────────────────────────────────

    [Test]
    [Description("ContextCounts['in_combat'] counts decisions where InCombat=true")]
    public void Summary_ContextCounts_InCombat()
    {
        var recorder = new BotTelemetryRecorder();

        // 5 in-combat decisions
        for (int i = 0; i < 5; i++)
            recorder.Record(new BotDecisionRecord
            {
                TurnNumber = 1, FloorDepth = 1,
                ActionType = "Attack", Reason = "attack_lowest_hp",
                HpFraction = 0.8, VisibleEnemies = 1, AdjacentEnemies = 1,
                InCombat   = true, LowHp = false,
            });

        // 3 exploring decisions
        for (int i = 0; i < 3; i++)
            recorder.Record(new BotDecisionRecord
            {
                TurnNumber = 1, FloorDepth = 1,
                ActionType = "MoveToward", Reason = "move_to_nearest",
                HpFraction = 1.0, VisibleEnemies = 1, AdjacentEnemies = 0,
                InCombat   = false, LowHp = false,
            });

        var summary = recorder.Summarize();

        Assert.That(summary.ContextCounts["in_combat"], Is.EqualTo(5),
            "in_combat count should match decisions where InCombat=true");
        Assert.That(summary.ContextCounts["exploring"], Is.EqualTo(3),
            "exploring count should match decisions where InCombat=false and ActionType!='Descend'");
    }

    // ── Test 8: Soak integration — every run has non-null BotSummary ──────────

    [Test]
    [Category("Slow")]
    [Description("RunSoak produces non-null BotSummary with TotalDecisions > 0 for every run")]
    public void RunSoak_WithTelemetry_AllRunsHaveBotSummary()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var entitiesPath = Path.Combine(testDir, "..", "..", "..", "..", "config", "entities.yaml");
        var levelTemplatesPath = Path.Combine(testDir, "..", "..", "..", "..", "config", "level_templates.yaml");

        if (!File.Exists(entitiesPath) || !File.Exists(levelTemplatesPath))
            Assert.Ignore("config files not found — skipping integration test");

        var loader = new ContentLoader();
        var content = loader.LoadAllFromFile(entitiesPath);
        var entityFactory     = new EntityFactory();
        var itemFactory       = new ItemFactory(content.Items, entityFactory);
        var monsterFactory    = new MonsterFactory(content.Monsters, entityFactory, itemFactory);
        var consumableFactory = new ConsumableFactory(content.Consumables, entityFactory);
        var templates         = LevelTemplateRegistry.FromFile(levelTemplatesPath);

        var floorBuilder = new DungeonFloorBuilder(templates, monsterFactory, itemFactory, consumableFactory);
        var harness      = new DungeonRunHarness(floorBuilder);

        // 3 runs × 2 floors: small enough for fast suite but exercises the soak path
        var summary = harness.RunSoak(floors: 2, runs: 3, baseSeed: 1337);

        Assert.That(summary.Runs.Count, Is.EqualTo(3));
        foreach (var run in summary.Runs)
        {
            Assert.That(run.BotSummary, Is.Not.Null,
                $"Run seed {run.Seed}: BotSummary should be non-null when telemetry is enabled");
            Assert.That(run.BotSummary!.TotalDecisions, Is.GreaterThan(0),
                $"Run seed {run.Seed}: TotalDecisions should be > 0");
            Assert.That(run.BotSummary.ActionCounts, Contains.Key("Attack").Or.ContainKey("MoveToward"),
                $"Run seed {run.Seed}: ActionCounts should contain at least Attack or MoveToward");
        }
    }

    // ── Additional: AvgHpWhenHealing is 0 when no heals ─────────────────────

    [Test]
    [Description("AvgHpWhenHealing returns 0.0 when no heal decisions recorded — guards division by zero")]
    public void Summary_NoHealDecisions_AvgHpIsZero()
    {
        var recorder = new BotTelemetryRecorder();

        recorder.Record(new BotDecisionRecord
        {
            TurnNumber = 1, FloorDepth = 1,
            ActionType = "Attack", Reason = "attack_lowest_hp",
            HpFraction = 0.9, VisibleEnemies = 1,
            InCombat = true, LowHp = false,
        });

        var summary = recorder.Summarize();

        Assert.That(summary.HealDecisions,  Is.EqualTo(0));
        Assert.That(summary.AvgHpWhenHealing, Is.EqualTo(0.0),
            "AvgHpWhenHealing must be 0.0 when no heal decisions were recorded");
    }

    // ── Additional: Summarize empty recorder ─────────────────────────────────

    [Test]
    [Description("Summarize() on empty recorder returns zero-value summary without throwing")]
    public void Summary_EmptyRecorder_ReturnsZeroSummary()
    {
        var recorder = new BotTelemetryRecorder();
        var summary  = recorder.Summarize();

        Assert.That(summary.TotalDecisions, Is.EqualTo(0));
        Assert.That(summary.FloorsVisited,  Is.EqualTo(0));
        Assert.That(summary.ActionCounts,   Is.Empty);
        Assert.That(summary.AvgHpWhenHealing, Is.EqualTo(0.0));
    }
}
