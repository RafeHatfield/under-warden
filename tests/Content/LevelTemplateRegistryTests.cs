using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

/// <summary>
/// Tests for LevelTemplateRegistry and the SpawnEntry YAML converter.
/// Covers Phase 2 of the dungeon generation milestone.
/// </summary>
[TestFixture]
public class LevelTemplateRegistryTests
{
    private const string MinimalYaml = @"
levels:
  1:
    parameters:
      max_rooms: 8
      min_room_size: 5
      max_room_size: 12
";

    private const string FullYaml = @"
levels:
  1:
    parameters:
      max_rooms: 8
      min_room_size: 5
      max_room_size: 12
      max_monsters_per_room: 2
      max_items_per_room: 2
    guaranteed_spawns:
      mode: additional
      monsters:
        - type: orc_grunt
          count: 2
        - type: orc_brute
          count: ""1-3""
    stairs:
      up: false
      down: true
    encounter_budget:
      etp_min: 20
      etp_max: 60
      allow_spike: false
  10:
    parameters:
      max_rooms: 15
";

    private const string TestingYaml = @"
levels:
  91:
    guaranteed_spawns:
      mode: replace
      monsters:
        - type: orc_grunt
          count: 1
  99:
    guaranteed_spawns:
      mode: additional
      monsters:
        - type: troll
          count: ""2-5""
";

    // --- Basic loading ---

    [Test]
    public void FromYaml_Minimal_ParsesWithoutError()
    {
        Assert.DoesNotThrow(() => LevelTemplateRegistry.FromYaml(MinimalYaml));
    }

    [Test]
    public void FromYaml_Full_ParsesWithoutError()
    {
        Assert.DoesNotThrow(() => LevelTemplateRegistry.FromYaml(FullYaml));
    }

    [Test]
    public void GetLevelOverride_UnconfiguredDepth_ReturnsNull()
    {
        var registry = LevelTemplateRegistry.FromYaml(MinimalYaml);
        Assert.That(registry.GetLevelOverride(99), Is.Null);
    }

    [Test]
    public void GetLevelOverride_ConfiguredDepth_ReturnsOverride()
    {
        var registry = LevelTemplateRegistry.FromYaml(MinimalYaml);
        var levelOverride = registry.GetLevelOverride(1);
        Assert.That(levelOverride, Is.Not.Null);
    }

    // --- Parameter loading ---

    [Test]
    public void Parameters_MaxRooms_LoadsCorrectly()
    {
        var registry = LevelTemplateRegistry.FromYaml(MinimalYaml);
        var levelOverride = registry.GetLevelOverride(1)!;
        Assert.That(levelOverride.Parameters!.MaxRooms, Is.EqualTo(8));
    }

    [Test]
    public void Parameters_RoomSizes_LoadCorrectly()
    {
        var registry = LevelTemplateRegistry.FromYaml(MinimalYaml);
        var levelOverride = registry.GetLevelOverride(1)!;
        Assert.That(levelOverride.Parameters!.MinRoomSize, Is.EqualTo(5));
        Assert.That(levelOverride.Parameters!.MaxRoomSize, Is.EqualTo(12));
    }

    // --- SpawnEntry count parsing ---

    [Test]
    public void SpawnEntry_IntCount_ParsesAsFixedCount()
    {
        var registry = LevelTemplateRegistry.FromYaml(FullYaml);
        var spawns = registry.GetLevelOverride(1)!.GuaranteedSpawns!;
        var orcGrunt = spawns.Monsters.First(m => m.Type == "orc_grunt");
        Assert.That(orcGrunt.CountMin, Is.EqualTo(2));
        Assert.That(orcGrunt.CountMax, Is.EqualTo(2));
    }

    [Test]
    public void SpawnEntry_RangeStringCount_ParsesAsRange()
    {
        var registry = LevelTemplateRegistry.FromYaml(FullYaml);
        var spawns = registry.GetLevelOverride(1)!.GuaranteedSpawns!;
        var orcBrute = spawns.Monsters.First(m => m.Type == "orc_brute");
        Assert.That(orcBrute.CountMin, Is.EqualTo(1));
        Assert.That(orcBrute.CountMax, Is.EqualTo(3));
    }

    // --- Mode default ---

    [Test]
    public void GuaranteedSpawns_ModeDefaults_ToAdditional()
    {
        const string yaml = @"
levels:
  1:
    guaranteed_spawns:
      monsters:
        - type: orc_grunt
          count: 1
";
        var registry = LevelTemplateRegistry.FromYaml(yaml);
        Assert.That(registry.GetLevelOverride(1)!.GuaranteedSpawns!.Mode, Is.EqualTo("additional"));
    }

    [Test]
    public void GuaranteedSpawns_ModeReplace_LoadsCorrectly()
    {
        var registry = LevelTemplateRegistry.FromYaml(TestingYaml);
        Assert.That(registry.GetLevelOverride(91)!.GuaranteedSpawns!.Mode, Is.EqualTo("replace"));
    }

    // --- Stair rules ---

    [Test]
    public void StairRules_Load_Correctly()
    {
        var registry = LevelTemplateRegistry.FromYaml(FullYaml);
        var stairs = registry.GetLevelOverride(1)!.Stairs!;
        Assert.That(stairs.Up, Is.False);
        Assert.That(stairs.Down, Is.True);
    }

    // --- Encounter budget ---

    [Test]
    public void EncounterBudget_Loads_Correctly()
    {
        var registry = LevelTemplateRegistry.FromYaml(FullYaml);
        var budget = registry.GetLevelOverride(1)!.EncounterBudget!;
        Assert.That(budget.EtpMin, Is.EqualTo(20));
        Assert.That(budget.EtpMax, Is.EqualTo(60));
        Assert.That(budget.AllowSpike, Is.False);
    }

    // --- Multiple depths ---

    [Test]
    public void MultipleDepths_BothAccessible()
    {
        var registry = LevelTemplateRegistry.FromYaml(FullYaml);
        Assert.That(registry.GetLevelOverride(1), Is.Not.Null);
        Assert.That(registry.GetLevelOverride(10), Is.Not.Null);
    }

    // --- Testing overrides (91-99) ---

    [Test]
    public void TestingOverrides_ParseWithoutError()
    {
        Assert.DoesNotThrow(() => LevelTemplateRegistry.FromYaml(TestingYaml));
    }

    [Test]
    public void TestingOverride_Depth99_RangeCount_ParsesCorrectly()
    {
        var registry = LevelTemplateRegistry.FromYaml(TestingYaml);
        var troll = registry.GetLevelOverride(99)!.GuaranteedSpawns!.Monsters.First(m => m.Type == "troll");
        Assert.That(troll.CountMin, Is.EqualTo(2));
        Assert.That(troll.CountMax, Is.EqualTo(5));
    }

    // --- Real YAML files ---

    [Test]
    public void RealLevelTemplatesYaml_ParsesWithoutError()
    {
        // Resolve path relative to the test assembly location — config/ is at project root
        string configPath = FindConfigFile("level_templates.yaml");
        Assert.That(File.Exists(configPath), Is.True, $"config file not found at: {configPath}");
        Assert.DoesNotThrow(() => LevelTemplateRegistry.FromFile(configPath));
    }

    [Test]
    public void RealTestingTemplatesYaml_ParsesWithoutError()
    {
        string configPath = FindConfigFile("level_templates_testing.yaml");
        Assert.That(File.Exists(configPath), Is.True, $"config file not found at: {configPath}");
        Assert.DoesNotThrow(() => LevelTemplateRegistry.FromFile(configPath));
    }

    [Test]
    public void RealLevelTemplatesYaml_Depth1_HasParameters()
    {
        string configPath = FindConfigFile("level_templates.yaml");
        var registry = LevelTemplateRegistry.FromFile(configPath);
        var depth1 = registry.GetLevelOverride(1);
        Assert.That(depth1, Is.Not.Null, "Depth 1 should be configured");
        Assert.That(depth1!.Parameters, Is.Not.Null);
        Assert.That(depth1.Parameters!.MaxRooms, Is.GreaterThan(0));
    }

    // Walks up from the test bin directory to find config/ at the project root
    private static string FindConfigFile(string fileName)
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "config", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "config", fileName);
    }
}
