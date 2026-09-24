using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Step 8 staged-start, end to end through the real engine (Slow): RunSoakStaged begins at the requested
/// depth with a geared player, covers only depths startDepth..floors, and is deterministic. This is the
/// machinery that lets a region be soaked at its own gear level (and, downstream, produces the escalator
/// alive-vs-killed comparison the capture already records the neutralized-when half of).
/// </summary>
[TestFixture]
[Category("Slow")]
public class StagedStartTests
{
    private DungeonRunHarness _harness = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        string Under(params string[] p) =>
            Path.GetFullPath(Path.Combine(new[] { testDir, "..", "..", "..", ".." }.Concat(p).ToArray()));

        var loader = new ContentLoader();
        var content = loader.LoadAllFromFile(Under("config", "entities.yaml"));
        var entityFactory = new EntityFactory();
        var itemFactory = new ItemFactory(content.Items, entityFactory);
        var monsterFactory = new MonsterFactory(content.Monsters, entityFactory, itemFactory);
        var consumableFactory = new ConsumableFactory(content.Consumables, entityFactory);
        var templates = LevelTemplateRegistry.FromFile(Under("config", "level_templates.yaml"));
        var floorBuilder = new DungeonFloorBuilder(templates, monsterFactory, itemFactory, consumableFactory);
        _harness = new DungeonRunHarness(floorBuilder);
    }

    private static GearProfile B3() => new()
    {
        Name = "b3", MainHand = "shortsword", Chest = "chain_mail",
        HealingPotions = 4, BonusHp = 26, Strength = 16, Constitution = 15,
    };

    [Test]
    public void RunSoakStaged_CoversOnlyStartDepthThroughFloors()
    {
        var summary = _harness.RunSoakStaged(floors: 5, runs: 8, startDepth: 3, gearProfile: B3(), baseSeed: 1337);

        var floorsSeen = summary.Runs.SelectMany(r => r.PerFloor).ToList();
        Assert.That(floorsSeen, Is.Not.Empty);
        Assert.That(floorsSeen.Select(f => f.Depth), Is.All.InRange(3, 5),
            "staged soak must only touch depths startDepth..floors");

        // Every run that produced floors must START at depth 3, not 1.
        foreach (var run in summary.Runs.Where(r => r.PerFloor.Count > 0))
            Assert.That(run.PerFloor[0].Depth, Is.EqualTo(3), "first floor of a staged run is startDepth");
    }

    [Test]
    public void RunSoakStaged_IsDeterministic_ForFixedSeed()
    {
        var a = _harness.RunSoakStaged(floors: 5, runs: 6, startDepth: 3, gearProfile: B3(), baseSeed: 1337);
        var b = _harness.RunSoakStaged(floors: 5, runs: 6, startDepth: 3, gearProfile: B3(), baseSeed: 1337);
        Assert.That(a.SurvivalRate, Is.EqualTo(b.SurvivalRate));
        Assert.That(a.AvgFloorsCompleted, Is.EqualTo(b.AvgFloorsCompleted));
    }
}
