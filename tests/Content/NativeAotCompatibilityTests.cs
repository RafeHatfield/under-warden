using UnderWarden.Logic.Content;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

/// <summary>
/// Guards against the "grey screen of death" on iOS caused by types missing from
/// AotObjectFactory._factories.
///
/// On desktop, YamlDotNet falls back to Activator.CreateInstance — so missing
/// registrations silently pass. On iOS (NativeAOT), Activator fails for reflected
/// types, crashing the app at startup. These tests simulate that constraint by
/// using AotObjectFactory(strict: true), which skips the Activator fallback.
///
/// Rule: every new YAML-deserialized type needs three factory entries:
///   [typeof(T)], [typeof(Dictionary<string, T>)], [typeof(Dictionary<string, Dictionary<string, T>>)]
///
/// If you add a new section to entities.yaml or level_templates.yaml and forget to
/// register the type, these tests will fail with a clear error message naming the
/// missing type — before it ever reaches an iOS device.
/// </summary>
[TestFixture]
public class NativeAotCompatibilityTests
{
    private static string ConfigPath(string fileName) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", fileName);

    private static readonly AotObjectFactory StrictFactory = new(strict: true);

    [Test]
    [Category("NativeAot")]
    public void EntitiesYaml_DeserializesWithStrictAotFactory()
    {
        // If this test fails, the app will crash on iOS at startup with a grey screen.
        // Add the missing type to AotObjectFactory._factories before merging.
        var yaml = File.ReadAllText(ConfigPath("entities.yaml"));
        var loader = new ContentLoader(StrictFactory);
        Assert.DoesNotThrow(() => loader.LoadAll(yaml),
            "entities.yaml contains a type not registered in AotObjectFactory. " +
            "Check the exception message for the missing type and add it to _factories.");
    }

    [Test]
    [Category("NativeAot")]
    public void LevelTemplatesYaml_DeserializesWithStrictAotFactory()
    {
        var yaml = File.ReadAllText(ConfigPath("level_templates.yaml"));
        Assert.DoesNotThrow(() => LevelTemplateRegistry.FromYaml(yaml, StrictFactory),
            "level_templates.yaml contains a type not registered in AotObjectFactory. " +
            "Check the exception message for the missing type and add it to _factories.");
    }
}
