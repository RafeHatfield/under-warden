using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

[TestFixture]
public class ScenarioHarnessTests
{
    private ScenarioHarness _harness = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        const string yaml = @"
monsters:
  orc_grunt:
    name: ""Orc""
    stats:
      hp: 28
      xp: 35
      damage_min: 4
      damage_max: 6
      strength: 14
      dexterity: 10
      constitution: 12
      accuracy: 4
      evasion: 1
    char: ""o""
    color: [63, 127, 63]
    blocks: true
    faction: ""orc""
    etp_base: 27
";
        var loader = new ContentLoader();
        var defs = loader.LoadMonsters(yaml);
        _harness = new ScenarioHarness(new MonsterFactory(defs, new EntityFactory()));
    }

    private static ScenarioDefinition MakeScenario(int monsterCount = 1, int runs = 40)
    {
        return new ScenarioDefinition
        {
            ScenarioId = "test_orc_baseline",
            Name = "Test Orc Baseline",
            Depth = 2,
            TurnLimit = 100,
            Runs = runs,
            Player = new ScenarioPlayer
            {
                Hp = 54,
                Strength = 12,
                Dexterity = 14,
                Constitution = 12,
                Accuracy = 2,
                Evasion = 1,
                DamageMin = 1,
                DamageMax = 4,
            },
            Monsters = new List<ScenarioMonster>
            {
                new() { Type = "orc_grunt", Count = monsterCount }
            }
        };
    }

    [Test]
    public void RunOnce_ReturnMetrics()
    {
        var scenario = MakeScenario(monsterCount: 1, runs: 1);
        var metrics = _harness.RunOnce(scenario, seed: 1337);

        Assert.That(metrics.TurnsTaken, Is.GreaterThan(0));
        Assert.That(metrics.PlayerAttacks, Is.GreaterThan(0));
        Assert.That(metrics.MonsterAttacks, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void RunOnce_Deterministic()
    {
        var scenario = MakeScenario(monsterCount: 1, runs: 1);

        var m1 = _harness.RunOnce(scenario, seed: 1337);
        var m2 = _harness.RunOnce(scenario, seed: 1337);

        Assert.That(m1.TurnsTaken, Is.EqualTo(m2.TurnsTaken));
        Assert.That(m1.PlayerDied, Is.EqualTo(m2.PlayerDied));
        Assert.That(m1.PlayerHits, Is.EqualTo(m2.PlayerHits));
        Assert.That(m1.MonsterHits, Is.EqualTo(m2.MonsterHits));
        Assert.That(m1.PlayerDamageDealt, Is.EqualTo(m2.PlayerDamageDealt));
        Assert.That(m1.MonsterDamageDealt, Is.EqualTo(m2.MonsterDamageDealt));
        Assert.That(m1.MonstersKilled, Is.EqualTo(m2.MonstersKilled));
    }

    [Test]
    public void Run_AggregatesMultipleRuns()
    {
        var scenario = MakeScenario(monsterCount: 1, runs: 20);

        var agg = _harness.Run(scenario, baseSeed: 1337);

        Assert.That(agg.TotalRuns, Is.EqualTo(20));
        Assert.That(agg.ScenarioId, Is.EqualTo("test_orc_baseline"));
        Assert.That(agg.AvgTurns, Is.GreaterThan(0));
        Assert.That(agg.PlayerHitRate, Is.InRange(0.1, 1.0));
        Assert.That(agg.MonsterHitRate, Is.InRange(0.1, 1.0));
    }

    [Test]
    public void Run_DeathRateInReasonableRange()
    {
        // Player vs 3 orcs — should be dangerous but not always fatal
        var scenario = MakeScenario(monsterCount: 3, runs: 50);

        var agg = _harness.Run(scenario, baseSeed: 1337);

        // With 3 orcs, death rate should be meaningful but not 100%
        Assert.That(agg.DeathRate, Is.InRange(0.0, 1.0));
        Assert.That(agg.AvgMonstersKilled, Is.GreaterThan(0));
    }

    [Test]
    public void Run_PlayerVsSingleOrc_MostlyWins()
    {
        // 1v1 with default player stats — player should usually win
        var scenario = MakeScenario(monsterCount: 1, runs: 50);

        var agg = _harness.Run(scenario, baseSeed: 1337);

        // Player should win most 1v1s against an orc
        Assert.That(agg.DeathRate, Is.LessThan(0.5));
        Assert.That(agg.AvgMonstersKilled, Is.GreaterThanOrEqualTo(0.8));
    }

    [Test]
    public void Run_Seed1337_Reproducible()
    {
        var scenario = MakeScenario(monsterCount: 2, runs: 30);

        var agg1 = _harness.Run(scenario, baseSeed: 1337);
        var agg2 = _harness.Run(scenario, baseSeed: 1337);

        Assert.That(agg1.DeathRate, Is.EqualTo(agg2.DeathRate));
        Assert.That(agg1.AvgTurns, Is.EqualTo(agg2.AvgTurns));
        Assert.That(agg1.PlayerHitRate, Is.EqualTo(agg2.PlayerHitRate));
    }
}
