using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// 0c step 1: the per-floor metrics + full per-death lever record captured live by DungeonRunHarness.
/// These run the real engine (Slow), so they assert INVARIANTS the capture must always satisfy rather
/// than exact numbers: vitals populate when combat happens, the six death signals are internally
/// consistent and in-range, and the whole capture is deterministic for a fixed seed.
/// </summary>
[TestFixture]
[Category("Slow")]
public class FloorMetricsCaptureTests
{
    private DungeonRunHarness _harness = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var entitiesPath = Path.Combine(testDir, "..", "..", "..", "..", "config", "entities.yaml");
        var levelTemplatesPath = Path.Combine(testDir, "..", "..", "..", "..", "config", "level_templates.yaml");

        var loader = new ContentLoader();
        var content = loader.LoadAllFromFile(entitiesPath);
        var entityFactory     = new EntityFactory();
        var itemFactory       = new ItemFactory(content.Items, entityFactory);
        var monsterFactory    = new MonsterFactory(content.Monsters, entityFactory, itemFactory);
        var consumableFactory = new ConsumableFactory(content.Consumables, entityFactory);
        var templates         = LevelTemplateRegistry.FromFile(levelTemplatesPath);

        var floorBuilder = new DungeonFloorBuilder(templates, monsterFactory, itemFactory, consumableFactory);
        _harness = new DungeonRunHarness(floorBuilder);
    }

    private static IEnumerable<FloorRunMetrics> AllFloors(DungeonSoakSummary s)
        => s.Runs.SelectMany(r => r.PerFloor);

    [Test]
    public void Vitals_Populate_WhenCombatHappens()
    {
        var summary = _harness.RunSoak(floors: 6, runs: 20, baseSeed: 1337);
        var floors = AllFloors(summary).ToList();
        Assert.That(floors, Is.Not.Empty);

        // Some floor must have inflicted combat damage and run some combat turns over the soak.
        Assert.That(floors.Any(f => f.DamageTakenThisFloor > 0), "expected combat damage somewhere in the soak");
        Assert.That(floors.Any(f => f.CombatTurns > 0), "expected combat turns somewhere in the soak");

        foreach (var f in floors)
        {
            Assert.That(f.DamageTakenThisFloor, Is.GreaterThanOrEqualTo(0));
            Assert.That(f.CombatTurns, Is.InRange(0, f.TurnsTaken), "combat turns cannot exceed turns taken");
            // ttk is only meaningful when something was killed.
            if (f.MonstersKilled > 0)
                Assert.That(f.AvgHitsToKill, Is.GreaterThan(0), "killed monsters but recorded 0 hits-to-kill");
            else
                Assert.That(f.AvgHitsToKill, Is.EqualTo(0));
            // Escalator bookkeeping is internally consistent.
            if (f.EscalatorNeutralized) Assert.That(f.EscalatorNeutralizedAtTurn, Is.Not.Null);
            if (f.EscalatorNeutralizedAtTurn != null) Assert.That(f.EscalatorPresent, Is.True);
        }
    }

    [Test]
    public void DeathRecord_OnlyOnDeathFloors_AndSignalsAreConsistent()
    {
        // Deep soak so the balanced bot dies often enough to exercise the death-record path.
        var summary = _harness.RunSoak(floors: 14, runs: 40, baseSeed: 1337);
        var floors = AllFloors(summary).ToList();

        // Death record appears iff the player died on that floor to an actual killer (not a stuck-abort).
        foreach (var f in floors)
            if (!f.PlayerDied)
                Assert.That(f.Death, Is.Null, "non-death floor must carry no death record");

        var deaths = floors.Where(f => f.Death != null).Select(f => f.Death!).ToList();
        Assert.That(deaths, Is.Not.Empty, "expected at least one attributable death across a 14-floor / 40-run soak");

        foreach (var d in deaths)
        {
            Assert.That(d.HitsToDown, Is.GreaterThanOrEqualTo(0));
            Assert.That(d.EngagementTurns, Is.GreaterThanOrEqualTo(1), "engagement window is at least one turn");
            Assert.That(d.KillerHitRate, Is.InRange(0.0, 1.0), "hit-rate is a fraction");
            Assert.That(d.DamagePerHit, Is.GreaterThanOrEqualTo(0));
            Assert.That(d.CounterattacksLanded, Is.GreaterThanOrEqualTo(0));
            // The frequency lever is exactly hits ÷ engagement-turns (parameter-free).
            Assert.That(d.AttackFrequency,
                Is.EqualTo((double)d.HitsToDown / d.EngagementTurns).Within(1e-9));
            // A monster killer (not a -1 hazard) that landed blows must be attributed + counted as an attacker.
            if (d.KillerId != -1 && d.HitsToDown > 0)
            {
                Assert.That(d.DistinctAttackers, Is.GreaterThanOrEqualTo(1));
                Assert.That(d.KillerTypeId, Is.Not.Null.And.Not.Empty,
                    "a monster killer that landed hits must resolve a species type id");
            }
        }
    }

    [Test]
    public void Capture_IsDeterministic_ForFixedSeed()
    {
        var a = AllFloors(_harness.RunSoak(floors: 10, runs: 10, baseSeed: 1337)).ToList();
        var b = AllFloors(_harness.RunSoak(floors: 10, runs: 10, baseSeed: 1337)).ToList();
        Assert.That(a.Count, Is.EqualTo(b.Count));

        for (int i = 0; i < a.Count; i++)
        {
            Assert.That(a[i].DamageTakenThisFloor, Is.EqualTo(b[i].DamageTakenThisFloor));
            Assert.That(a[i].CombatTurns, Is.EqualTo(b[i].CombatTurns));
            Assert.That(a[i].AvgHitsToKill, Is.EqualTo(b[i].AvgHitsToKill));
            Assert.That(a[i].EscalatorNeutralizedAtTurn, Is.EqualTo(b[i].EscalatorNeutralizedAtTurn));
            Assert.That(a[i].Death?.HitsToDown, Is.EqualTo(b[i].Death?.HitsToDown));
            Assert.That(a[i].Death?.KillerTypeId, Is.EqualTo(b[i].Death?.KillerTypeId));
            Assert.That(a[i].Death?.EngagementTurns, Is.EqualTo(b[i].Death?.EngagementTurns));
        }
    }
}
