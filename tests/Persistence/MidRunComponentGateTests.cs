using System.Collections;
using System.Reflection;
using System.Text.Json;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Persistence.MidRun;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

/// <summary>
/// The completeness gate (M1.4 §Component registry). It ENUMERATES via reflection every concrete
/// IComponent in the Logic assembly and proves each is registered and round-trips losslessly. A new
/// component with no registration fails here, naming the type — the anti-rot mechanism. The
/// enumeration is the reflection query itself, never a hand-maintained list.
/// </summary>
[TestFixture]
public class MidRunComponentGateTests
{
    /// <summary>THE enumeration — every concrete, non-abstract IComponent in the Logic assembly.</summary>
    public static IEnumerable<Type> ConcreteComponentTypes()
    {
        var asm = typeof(IComponent).Assembly;
        return asm.GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);
    }

    [Test]
    public void EveryConcreteComponent_HasARegistration()
    {
        var missing = ConcreteComponentTypes()
            .Where(t => !MidRunComponentRegistry.TryGet(t, out _))
            .Select(t => t.Name)
            .ToList();

        Assert.That(missing, Is.Empty,
            "Concrete IComponent types with no mid-run serialization registration (add them in " +
            $"MidRunComponentRegistrations): {string.Join(", ", missing)}");
    }

    [Test]
    public void RegistryHasNoOrphans_AndDiscriminatorsAreUnique()
    {
        var concrete = ConcreteComponentTypes().ToHashSet();
        var orphans = MidRunComponentRegistry.All
            .Where(c => !concrete.Contains(c.ComponentType))
            .Select(c => c.TypeName)
            .ToList();
        Assert.That(orphans, Is.Empty, $"Registered codecs for non-existent/abstract types: {string.Join(", ", orphans)}");

        var names = MidRunComponentRegistry.All.Select(c => c.TypeName).ToList();
        Assert.That(names.Count, Is.EqualTo(names.Distinct().Count()), "Duplicate discriminator names in the registry.");
    }

    [Test]
    public void RegisteredCount_EqualsEnumeratedConcreteCount()
    {
        Assert.That(MidRunComponentRegistry.All.Count, Is.EqualTo(ConcreteComponentTypes().Count()));
    }

    [TestCaseSource(nameof(ConcreteComponentTypes))]
    public void Component_RoundTripsLosslessly(Type type)
    {
        Assert.That(MidRunComponentRegistry.TryGet(type, out var codec), Is.True,
            $"No codec registered for {type.Name}.");

        // (b) default-ish instance round-trips.
        var baseline = Construct(type);
        AssertLossless(codec, baseline, type, "default-constructed");

        // (c) property-populated instance: distinct sentinels on every simple writable field, so a
        // dropped field cannot hide behind a default.
        var populated = Construct(type);
        var sentinels = PopulateSimpleSentinels(populated);
        AssertLossless(codec, populated, type, "property-populated");

        // Every simple writable field must survive the round-trip as its exact sentinel.
        var rebuilt = RoundTrip(codec, populated);
        foreach (var (prop, expected) in sentinels)
        {
            var actual = prop.GetValue(rebuilt);
            Assert.That(actual, Is.EqualTo(expected),
                $"{type.Name}.{prop.Name} was dropped/altered by the serializer (expected sentinel {expected}, got {actual}).");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void AssertLossless(ComponentCodec codec, IComponent instance, Type type, string label)
    {
        var rebuilt = RoundTrip(codec, instance);
        // Byte-identical re-serialization is the strong lossless signal for everything the DTO captures.
        string first = codec.Serialize(instance).GetRawText();
        string second = codec.Serialize(rebuilt).GetRawText();
        Assert.That(second, Is.EqualTo(first), $"{type.Name} ({label}) did not re-serialize byte-identically.");
        Assert.That(rebuilt.GetType(), Is.EqualTo(type), $"{type.Name} ({label}) deserialized to the wrong type.");
    }

    private static IComponent RoundTrip(ComponentCodec codec, IComponent instance)
    {
        var data = codec.Serialize(instance);
        // Force a real JSON parse so we exercise the on-disk shape, not the in-memory element.
        using var doc = JsonDocument.Parse(data.GetRawText());
        return codec.Deserialize(doc.RootElement.Clone(), StubResolver.Instance);
    }

    private static IComponent Construct(Type type)
    {
        var ctor = type.GetConstructors().OrderBy(c => c.GetParameters().Length).First();
        var args = ctor.GetParameters().Select(p => SampleValue(p.ParameterType)).ToArray();
        return (IComponent)ctor.Invoke(args);
    }

    // Set every simple writable property to a distinct non-default sentinel; return what was set.
    private static List<(PropertyInfo Prop, object? Value)> PopulateSimpleSentinels(IComponent instance)
    {
        var set = new List<(PropertyInfo, object?)>();
        int seed = 1;
        foreach (var p in instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.Name == nameof(IComponent.Owner)) continue;
            if (!p.CanWrite || p.SetMethod is null || !p.SetMethod.IsPublic) continue;
            if (!IsSimple(p.PropertyType)) continue;
            var sentinel = Sentinel(p.PropertyType, seed++);
            if (sentinel is null) continue;
            p.SetValue(instance, sentinel);
            set.Add((p, sentinel));
        }
        return set;
    }

    private static bool IsSimple(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsEnum || t == typeof(int) || t == typeof(long) || t == typeof(bool)
            || t == typeof(double) || t == typeof(float) || t == typeof(string);
    }

    private static object? Sentinel(Type t, int seed)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        if (u.IsEnum) { var vals = Enum.GetValues(u); return vals.Length > 0 ? vals.GetValue(seed % vals.Length) : null; }
        if (u == typeof(int)) return 1000 + seed;
        if (u == typeof(long)) return (long)(1000 + seed);
        if (u == typeof(bool)) return true;                 // default is false → non-default
        if (u == typeof(double)) return 0.25 + seed;
        if (u == typeof(float)) return 0.5f + seed;
        if (u == typeof(string)) return $"s{seed}";
        return null;
    }

    private static object? SampleValue(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        if (u.IsEnum) { var vals = Enum.GetValues(u); return vals.Length > 0 ? vals.GetValue(0) : Activator.CreateInstance(u); }
        if (u == typeof(int) || u == typeof(long)) return Convert.ChangeType(3, u);
        if (u == typeof(bool)) return true;
        if (u == typeof(double)) return 0.5d;
        if (u == typeof(float)) return 0.5f;
        if (u == typeof(string)) return "x";
        if (u == typeof(int[])) return new[] { 1, 2 };
        if (t == typeof(Entity) || t == typeof(Entity)) return null;
        if (typeof(IEnumerable<string>).IsAssignableFrom(u)) return new List<string> { "a" };
        if (u.IsValueType) return Activator.CreateInstance(u);
        return null; // reference types (Entity, nested components) default to null for construction
    }

    private sealed class StubResolver : IEntityResolver
    {
        public static readonly StubResolver Instance = new();
        public Entity Resolve(int id) => new(id, $"stub{id}");
        public Entity? ResolveOptional(int? id) => id is null ? null : Resolve(id.Value);
    }
}
