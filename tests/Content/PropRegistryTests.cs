using UnderWarden.Logic.Content;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

/// <summary>
/// Tests for PropRegistry and PropDefinition YAML loading from config/props.yaml.
/// Covers PROP-005: PropDefinition, PropPlacement, PropsFile, PropRegistry.
/// </summary>
[TestFixture]
public class PropRegistryTests
{
    private static string PropsYamlPath() =>
        Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", "props.yaml"));

    private static PropRegistry LoadRegistry() =>
        new ContentLoader().LoadPropsFromFile(PropsYamlPath());

    // ─── Structural ──────────────────────────────────────────────────────────

    [Test]
    public void LoadProps_ParsesAllEntries()
    {
        var registry = LoadRegistry();
        // config/props.yaml has 63 entries as of initial authoring; gate on >=60 for forward-compat
        Assert.That(registry.All.Count, Is.GreaterThanOrEqualTo(60),
            $"Expected at least 60 props, got {registry.All.Count}");
    }

    // ─── TileIds ─────────────────────────────────────────────────────────────

    [Test]
    public void TileIds_ParsedCorrectly()
    {
        var registry = LoadRegistry();
        var barrel = registry.Get("barrel");
        Assert.That(barrel, Is.Not.Null);
        Assert.That(barrel!.TileIds, Is.EqualTo(new List<int> { 268 }));
    }

    // ─── Multi-tile props ────────────────────────────────────────────────────

    [Test]
    public void MultiTileProp_HasTileLayouts()
    {
        var registry = LoadRegistry();
        var rug = registry.Get("rug");
        Assert.That(rug, Is.Not.Null);
        Assert.That(rug!.TileLayouts, Is.Not.Null, "rug.TileLayouts should not be null");
        Assert.That(rug.TileLayouts!.Count, Is.EqualTo(2), "rug should have 2 layout variants");
    }

    // ─── OverlayTileId ───────────────────────────────────────────────────────

    [Test]
    public void OverlayTileId_ParsedCorrectly()
    {
        var registry = LoadRegistry();
        var brazier = registry.Get("brazier");
        Assert.That(brazier, Is.Not.Null);
        Assert.That(brazier!.OverlayTileId, Is.EqualTo(97));
    }

    // ─── Footprint ───────────────────────────────────────────────────────────

    [Test]
    public void Footprint_ParsedCorrectly()
    {
        var registry = LoadRegistry();

        var rug = registry.Get("rug");
        Assert.That(rug, Is.Not.Null);
        Assert.That(rug!.FootprintW, Is.EqualTo(3), "rug FootprintW");
        Assert.That(rug.FootprintH, Is.EqualTo(3), "rug FootprintH");

        var table = registry.Get("alchemy_table");
        Assert.That(table, Is.Not.Null);
        Assert.That(table!.FootprintW, Is.EqualTo(3), "alchemy_table FootprintW");
        Assert.That(table.FootprintH, Is.EqualTo(1), "alchemy_table FootprintH");
    }

    // ─── Placement ───────────────────────────────────────────────────────────

    [Test]
    public void Placement_ParsedCorrectly()
    {
        var registry = LoadRegistry();
        var barrel = registry.Get("barrel");
        Assert.That(barrel, Is.Not.Null);
        Assert.That(barrel!.Placement, Is.EqualTo(PropPlacement.Corner));
    }

    // ─── TryGet ──────────────────────────────────────────────────────────────

    [Test]
    public void TryGet_ReturnsFalse_ForUnknownId()
    {
        var registry = LoadRegistry();
        var found = registry.TryGet("nonexistent_prop_xyz", out var def);
        Assert.That(found, Is.False);
        Assert.That(def, Is.Null);
    }

    // ─── BlocksMovement ──────────────────────────────────────────────────────

    [Test]
    public void BlocksMovement_FalseForOverlays()
    {
        var registry = LoadRegistry();

        // rubble and cobweb are both documented as blocks_movement: false in props.yaml
        var rubble = registry.Get("rubble");
        Assert.That(rubble, Is.Not.Null);
        Assert.That(rubble!.BlocksMovement, Is.False, "rubble should not block movement");

        var cobweb = registry.Get("cobweb");
        Assert.That(cobweb, Is.Not.Null);
        Assert.That(cobweb!.BlocksMovement, Is.False, "cobweb should not block movement");
    }

    // ─── NativeAOT compatibility ─────────────────────────────────────────────

    [Test]
    [Category("NativeAot")]
    public void PropsYaml_DeserializesWithStrictAotFactory()
    {
        // If this test fails, the app will crash on iOS at startup with a grey screen.
        // Add the missing type to AotObjectFactory._factories before merging.
        var yaml = File.ReadAllText(PropsYamlPath());
        var loader = new ContentLoader(new AotObjectFactory(strict: true));
        Assert.DoesNotThrow(() => loader.LoadProps(yaml),
            "props.yaml contains a type not registered in AotObjectFactory. " +
            "Check the exception message for the missing type and add it to _factories.");
    }
}
