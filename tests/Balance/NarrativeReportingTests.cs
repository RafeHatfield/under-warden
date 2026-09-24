using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

// Tests for D1 (transcript) and D2 (voice-line histogram) narrative testing infrastructure

[TestFixture]
public class NarrativeReportingTests
{
    // ── D2: Voice-line histogram ──────────────────────────────────────────────

    [Test]
    public void VoiceLineHits_AggregatesAcrossRuns()
    {
        // Arrange: two run results with voice-line hits
        var run1 = new DungeonSoakRunResult
        {
            Outcome = OutcomeClassifier.Survived,
            FailureType = OutcomeClassifier.FailureNone,
            PerFloor = Array.Empty<FloorRunMetrics>(),
            VoiceLineHits = new Dictionary<string, int> { ["trigger_a"] = 2, ["trigger_b"] = 1 },
        };
        var run2 = new DungeonSoakRunResult
        {
            Outcome = OutcomeClassifier.Survived,
            FailureType = OutcomeClassifier.FailureNone,
            PerFloor = Array.Empty<FloorRunMetrics>(),
            VoiceLineHits = new Dictionary<string, int> { ["trigger_a"] = 1, ["trigger_c"] = 3 },
        };
        var run3 = new DungeonSoakRunResult
        {
            Outcome = OutcomeClassifier.Survived,
            FailureType = OutcomeClassifier.FailureNone,
            PerFloor = Array.Empty<FloorRunMetrics>(),
            VoiceLineHits = null,  // no voice lines
        };

        // Act
        var summary = DungeonSoakSummary.ComputeFrom(new List<DungeonSoakRunResult> { run1, run2, run3 });

        // Assert
        Assert.That(summary.VoiceLineHits["trigger_a"], Is.EqualTo(3));
        Assert.That(summary.VoiceLineHits["trigger_b"], Is.EqualTo(1));
        Assert.That(summary.VoiceLineHits["trigger_c"], Is.EqualTo(3));
    }

    [Test]
    public void VoiceLineHits_EmptyWhenNoRunsHaveData()
    {
        var run = new DungeonSoakRunResult
        {
            Outcome = OutcomeClassifier.Survived,
            FailureType = OutcomeClassifier.FailureNone,
            PerFloor = Array.Empty<FloorRunMetrics>(),
            VoiceLineHits = null,
        };
        var summary = DungeonSoakSummary.ComputeFrom(new List<DungeonSoakRunResult> { run });
        Assert.That(summary.VoiceLineHits, Is.Empty);
    }

    [Test]
    public void DungeonSoakReport_IncludesVoiceLineSection_WhenDataPresent()
    {
        var run = new DungeonSoakRunResult
        {
            Outcome = OutcomeClassifier.Survived,
            FailureType = OutcomeClassifier.FailureNone,
            PerFloor = Array.Empty<FloorRunMetrics>(),
            VoiceLineHits = new Dictionary<string, int> { ["wand_kicked_away"] = 5 },
        };
        var summary = DungeonSoakSummary.ComputeFrom(new List<DungeonSoakRunResult> { run });
        var report = DungeonSoakReport.Generate(summary);

        Assert.That(report, Does.Contain("Voice Line Emissions"));
        Assert.That(report, Does.Contain("wand_kicked_away"));
        Assert.That(report, Does.Contain("5"));
    }

    [Test]
    public void DungeonSoakReport_VoiceLineSectionGraceful_WhenNoData()
    {
        var run = new DungeonSoakRunResult
        {
            Outcome = OutcomeClassifier.Survived,
            FailureType = OutcomeClassifier.FailureNone,
            PerFloor = Array.Empty<FloorRunMetrics>(),
        };
        var summary = DungeonSoakSummary.ComputeFrom(new List<DungeonSoakRunResult> { run });
        var report = DungeonSoakReport.Generate(summary);

        Assert.That(report, Does.Contain("Voice Line Emissions"));
        Assert.That(report, Does.Contain("no voice lines fired"));
    }

    // ── D1: Run transcript ────────────────────────────────────────────────────

    [Test]
    public void TranscriptEntry_HasExpectedFields()
    {
        var entry = new TranscriptEntry
        {
            Depth = 2, FloorTurn = 15, EventType = "voice", Detail = "wand_kicked_away"
        };
        Assert.That(entry.Depth, Is.EqualTo(2));
        Assert.That(entry.FloorTurn, Is.EqualTo(15));
        Assert.That(entry.EventType, Is.EqualTo("voice"));
        Assert.That(entry.Detail, Is.EqualTo("wand_kicked_away"));
    }

    [Test]
    public void RunTranscript_ContainsFloorHeaders()
    {
        // Use the standard DungeonFloorBuilder test setup
        var harness = NarrativeTestHarnessFactory.Create();
        var transcript = harness.RunTranscript(floors: 2, seed: 1337);

        Assert.That(transcript, Does.Contain("[Floor 1]"));
    }

    [Test]
    public void RunTranscript_ContainsOutcomeLine()
    {
        var harness = NarrativeTestHarnessFactory.Create();
        var transcript = harness.RunTranscript(floors: 1, seed: 1337);

        Assert.That(transcript, Does.Contain("Outcome:"));
        Assert.That(transcript, Does.Contain("=== Run Transcript"));
    }
}

/// <summary>
/// Factory for creating a DungeonRunHarness in test context.
/// Replicates the minimal setup from DungeonRunTests / DungeonSoakTests:
/// lightweight builder without SpellItemFactory or boons — sufficient for
/// structural narrative testing (transcript format, voice-line aggregation).
/// </summary>
internal static class NarrativeTestHarnessFactory
{
    public static DungeonRunHarness Create()
    {
        // Walk up from the test binary directory to reach the project root config/
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
        return new DungeonRunHarness(floorBuilder);
    }
}
