using UnderWarden.Logic.Content;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

/// <summary>
/// Phase 2 tests: YAML round-trip deserialization for interactive_props.yaml and floor_traps.yaml.
/// Tests load from the actual config files so we catch YAML content errors early.
/// </summary>
[TestFixture]
public class InteractivePropsRegistryTests
{
    private static string GetConfigPath(string filename)
    {
        var testDir = NUnit.Framework.TestContext.CurrentContext.TestDirectory;
        var path = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", filename));
        if (!File.Exists(path))
            path = Path.GetFullPath(Path.Combine(testDir, "config", filename));
        return path;
    }

    private static string PropsYamlPath => GetConfigPath("interactive_props.yaml");
    private static string TrapsYamlPath => GetConfigPath("floor_traps.yaml");

    // ── InteractivePropsRegistry load tests ───────────────────────────────────

    [Test]
    public void LoadInteractiveProps_LoadsAllThreePropTypes()
    {
        var loader = new ContentLoader();
        var registry = loader.LoadInteractivePropsFromFile(PropsYamlPath);

        Assert.That(registry.AllProps.ContainsKey("barrel"), Is.True, "Should have barrel");
        Assert.That(registry.AllProps.ContainsKey("bookshelf"), Is.True, "Should have bookshelf");
        Assert.That(registry.AllProps.ContainsKey("bone_pile"), Is.True, "Should have bone_pile");
    }

    [Test]
    public void LoadInteractiveProps_BarrelHasExpectedTileIds()
    {
        var loader = new ContentLoader();
        var registry = loader.LoadInteractivePropsFromFile(PropsYamlPath);

        var barrel = registry.Get("barrel");
        Assert.That(barrel.ClosedTileId, Is.EqualTo(268));
        Assert.That(barrel.OpenTileId, Is.EqualTo(269));
    }

    [Test]
    public void LoadInteractiveProps_BookshelfTileIdsAreEqual()
    {
        // Bookshelf: no visual change when searched — both tile IDs are 317.
        var loader = new ContentLoader();
        var registry = loader.LoadInteractivePropsFromFile(PropsYamlPath);

        var bookshelf = registry.Get("bookshelf");
        Assert.That(bookshelf.ClosedTileId, Is.EqualTo(317));
        Assert.That(bookshelf.OpenTileId, Is.EqualTo(317));
    }

    [Test]
    public void LoadInteractiveProps_BonePileHasRouseConfig()
    {
        var loader = new ContentLoader();
        var registry = loader.LoadInteractivePropsFromFile(PropsYamlPath);

        var bonePile = registry.Get("bone_pile");
        Assert.That(bonePile.RouseChance, Is.GreaterThan(0), "bone_pile should have rouse_chance > 0");
        Assert.That(bonePile.RouseMonster, Is.EqualTo("zombie"));
        Assert.That(bonePile.RouseMinDepth, Is.EqualTo(2));
    }

    [Test]
    public void LoadInteractiveProps_BookshelfTrapChanceIsZero()
    {
        var loader = new ContentLoader();
        var registry = loader.LoadInteractivePropsFromFile(PropsYamlPath);

        var bookshelf = registry.Get("bookshelf");
        Assert.That(bookshelf.TrapChance, Is.EqualTo(0.0), "Bookshelf should never be trapped");
    }

    [Test]
    public void LoadInteractiveProps_LoadsNamedPayloads()
    {
        var loader = new ContentLoader();
        var registry = loader.LoadInteractivePropsFromFile(PropsYamlPath);

        Assert.That(registry.AllPayloads.ContainsKey("fire_burst_small"), Is.True);
        Assert.That(registry.AllPayloads.ContainsKey("spike_burst_small"), Is.True);
        Assert.That(registry.AllPayloads.ContainsKey("spike_burst_chest"), Is.True);
    }

    [Test]
    public void LoadInteractiveProps_BarrelTrapTableHasEntries()
    {
        var loader = new ContentLoader();
        var registry = loader.LoadInteractivePropsFromFile(PropsYamlPath);

        var barrel = registry.Get("barrel");
        Assert.That(barrel.TrapTable, Is.Not.Null);
        Assert.That(barrel.TrapTable!.Count, Is.GreaterThan(0));
    }

    [Test]
    public void LoadInteractiveProps_MissingKeyThrows()
    {
        const string yaml = @"
interactive_props:
  barrel:
    closed_tile_id: 268
    open_tile_id: 269
trap_payloads: {}
";
        var loader = new ContentLoader();
        var registry = loader.LoadInteractiveProps(yaml);

        Assert.Throws<KeyNotFoundException>(() => registry.Get("nonexistent_prop"));
    }

    [Test]
    public void LoadInteractiveProps_InlineYaml_RoundTrip()
    {
        // Strict-mode deserialization test: verifies AotObjectFactory registrations are complete.
        const string yaml = @"
interactive_props:
  barrel:
    closed_tile_id: 268
    open_tile_id: 269
    loot:
      weights: { potion: 60, nothing: 40 }
      min_depth: 1
    trap_chance: 0.15
    trap_table:
      - { weight: 100, payload: fire_burst_small }

trap_payloads:
  fire_burst_small:
    actions:
      - { kind: damage, amount: 4 }
      - { kind: burning, duration: 4 }
";
        var loader = new ContentLoader(new AotObjectFactory(strict: true));
        var registry = loader.LoadInteractiveProps(yaml);

        Assert.That(registry.AllProps.ContainsKey("barrel"), Is.True);
        var barrel = registry.Get("barrel");
        Assert.That(barrel.ClosedTileId, Is.EqualTo(268));
        Assert.That(barrel.TrapChance, Is.EqualTo(0.15));

        var payload = registry.GetPayload("fire_burst_small");
        Assert.That(payload.Actions.Count, Is.EqualTo(2));
        Assert.That(payload.Actions[0].Kind, Is.EqualTo("damage"));
        Assert.That(payload.Actions[1].Kind, Is.EqualTo("burning"));
    }

    // ── FloorTrapRegistry load tests ──────────────────────────────────────────

    [Test]
    public void LoadFloorTraps_LoadsAllNineTypes()
    {
        var loader = new ContentLoader();
        var registry = loader.LoadFloorTrapsFromFile(TrapsYamlPath);

        string[] expected = ["spike_trap", "web_trap", "gas_trap", "fire_trap",
                             "alarm_plate", "teleport_trap", "root_trap", "hole_trap", "acid_trap"];

        foreach (var id in expected)
            Assert.That(registry.All.ContainsKey(id), Is.True, $"Should have trap '{id}'");
    }

    [Test]
    public void LoadFloorTraps_SpikeTrapHasExpectedValues()
    {
        var loader = new ContentLoader();
        var registry = loader.LoadFloorTrapsFromFile(TrapsYamlPath);

        var spike = registry.Get("spike_trap");
        Assert.That(spike.VisibleTileId, Is.EqualTo(429));
        Assert.That(spike.IsDetectable, Is.True);
        Assert.That(spike.PassiveDetectChance, Is.EqualTo(0.10));
        Assert.That(spike.Actions.Count, Is.EqualTo(2));
        Assert.That(spike.Actions[0].Kind, Is.EqualTo("damage"));
        Assert.That(spike.Actions[0].Amount, Is.EqualTo(7));
        Assert.That(spike.Actions[1].Kind, Is.EqualTo("bleed"));
    }

    [Test]
    public void LoadFloorTraps_RootTrapHasTileModulate()
    {
        var loader = new ContentLoader();
        var registry = loader.LoadFloorTrapsFromFile(TrapsYamlPath);

        var root = registry.Get("root_trap");
        Assert.That(root.TileModulate, Is.Not.Null, "root_trap should have tile_modulate");
        Assert.That(root.TileModulate!.Count, Is.EqualTo(4));
    }

    [Test]
    public void LoadFloorTraps_WebTrapHasNoModulate()
    {
        var loader = new ContentLoader();
        var registry = loader.LoadFloorTrapsFromFile(TrapsYamlPath);

        var web = registry.Get("web_trap");
        Assert.That(web.TileModulate, Is.Null, "web_trap has no tile_modulate");
    }

    [Test]
    public void LoadFloorTraps_HoleTrapHasDescendAction()
    {
        var loader = new ContentLoader();
        var registry = loader.LoadFloorTrapsFromFile(TrapsYamlPath);

        var hole = registry.Get("hole_trap");
        Assert.That(hole.Actions.Any(a => a.Kind == "descend"), Is.True);
    }

    [Test]
    public void LoadFloorTraps_AlarmPlateHasAlertFactionAction()
    {
        var loader = new ContentLoader();
        var registry = loader.LoadFloorTrapsFromFile(TrapsYamlPath);

        var alarm = registry.Get("alarm_plate");
        var alertAction = alarm.Actions.FirstOrDefault(a => a.Kind == "alert_faction");
        Assert.That(alertAction, Is.Not.Null);
        Assert.That(alertAction!.Target, Is.EqualTo("orc"));
        Assert.That(alertAction.Radius, Is.EqualTo(8));
    }

    [Test]
    public void LoadFloorTraps_MissingKeyThrows()
    {
        const string yaml = @"
floor_traps:
  spike_trap:
    visible_tile_id: 429
    is_detectable: true
    passive_detect_chance: 0.10
    actions:
      - { kind: damage, amount: 7 }
";
        var loader = new ContentLoader();
        var registry = loader.LoadFloorTraps(yaml);

        Assert.Throws<KeyNotFoundException>(() => registry.Get("nonexistent_trap"));
    }

    [Test]
    public void LoadFloorTraps_InlineYaml_StrictMode_RoundTrip()
    {
        // Strict-mode: ensures all DTOs are registered in AotObjectFactory.
        const string yaml = @"
floor_traps:
  spike_trap:
    visible_tile_id: 429
    is_detectable: true
    passive_detect_chance: 0.10
    actions:
      - { kind: damage, amount: 7 }
      - { kind: bleed, amount: 1, duration: 3 }
  root_trap:
    visible_tile_id: 430
    tile_modulate: [0.6, 1.0, 0.5, 1.0]
    is_detectable: true
    passive_detect_chance: 0.10
    actions:
      - { kind: entangle, duration: 3 }
";
        var loader = new ContentLoader(new AotObjectFactory(strict: true));
        var registry = loader.LoadFloorTraps(yaml);

        Assert.That(registry.All.ContainsKey("spike_trap"), Is.True);
        Assert.That(registry.All.ContainsKey("root_trap"), Is.True);

        var root = registry.Get("root_trap");
        Assert.That(root.TileModulate, Is.Not.Null);
        Assert.That(root.TileModulate![0], Is.EqualTo(0.6f).Within(0.001f));
    }
}
