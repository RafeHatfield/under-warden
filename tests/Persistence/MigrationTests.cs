using System.Text.Json;
using System.Text.Json.Nodes;
using UnderWarden.Logic.Persistence;
using UnderWarden.Logic.Persistence.Namespaces;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

/// <summary>
/// Phase 2: migration framework tests.
/// Covers per-namespace version comparison, forward migration chain, and future-namespace
/// fallback (spec §3 OQ-1 resolution B).
/// </summary>
[TestFixture]
public class MigrationTests
{
    private string _tempDir = "";

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "yarl_mig_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FakePersistencePathProvider Provider() => new(_tempDir);

    // ── No migration needed (fast path) ─────────────────────────────────────

    [Test]
    public void ApplyMigrations_NoVersionMismatch_ReturnsOriginalString()
    {
        // Build a file with all namespaces at v1 — identical to LatestVersion.
        var state = PersistentRunState.LoadFromDisk(Provider());
        state.RunCounter.TotalRuns = 3;
        state.MarkDirty();
        state.Flush(Provider());

        var original = File.ReadAllText(Provider().GetMainSaveFilePath());
        var result = Migrations.ApplyMigrations(original);

        // Fast path: no modification, same string reference not guaranteed but content equal.
        Assert.That(result, Is.EqualTo(original));
    }

    // ── Future namespace (file version > binary version) ─────────────────────

    [Test]
    public void FutureNamespace_FallsBackToDefaults_OtherNamespacesIntact()
    {
        // Save a file with real data.
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);
        state.RunCounter.TotalRuns = 7;
        state.Borrek.ArcState = "allied";
        state.Borrek.OrcPositiveActions = 3;
        state.MarkDirty();
        state.Flush(provider);

        // Patch the saved JSON so run_counter.schema_version = 99 (simulates a newer save).
        var mainPath = provider.GetMainSaveFilePath();
        var fileJson = File.ReadAllText(mainPath);
        var patched = PatchNamespaceVersion(fileJson, "run_counter", 99);
        File.WriteAllText(mainPath, patched);

        // Load with binary that knows run_counter only up to v1.
        var errors = new List<string>();
        var loaded = PersistentRunState.LoadFromDisk(provider, err => errors.Add(err));

        // run_counter was future → reset to defaults.
        Assert.That(loaded.RunCounter.TotalRuns, Is.EqualTo(0),
            "Future namespace should default.");

        // borrek was at v1 (matches binary) → preserved.
        Assert.That(loaded.Borrek.ArcState, Is.EqualTo("allied"),
            "Other namespaces must not be affected by the future-namespace fallback.");
        Assert.That(loaded.Borrek.OrcPositiveActions, Is.EqualTo(3));

        // Error should have been logged.
        Assert.That(errors, Has.Some.Contains("run_counter"),
            "Should log a warning naming the affected namespace.");
        Assert.That(errors, Has.Some.Contains("99"),
            "Warning should mention the file version.");
    }

    [Test]
    public void FutureNamespace_MultipleAffected_EachFallsBackIndependently()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);
        state.RunCounter.TotalRuns = 5;
        state.Encounters.MetBorrek = true;
        state.Achievements.TryUnlock("first_run_complete");
        state.MarkDirty();
        state.Flush(provider);

        var mainPath = provider.GetMainSaveFilePath();
        var fileJson = File.ReadAllText(mainPath);
        var patched = PatchNamespaceVersion(PatchNamespaceVersion(fileJson, "run_counter", 99), "encounters", 99);
        File.WriteAllText(mainPath, patched);

        var loaded = PersistentRunState.LoadFromDisk(provider);

        Assert.That(loaded.RunCounter.TotalRuns, Is.EqualTo(0));
        Assert.That(loaded.Encounters.MetBorrek, Is.False);
        // achievements was not patched → preserved
        Assert.That(loaded.Achievements.HasUnlocked("first_run_complete"), Is.True);
    }

    // ── Forward migration (file version < binary version) ────────────────────

    [Test]
    public void ForwardMigration_AppliesRegisteredFunction_TransformsData()
    {
        var provider = Provider();

        // Simulate a v1 file where run_counter has a field "legacy_count" instead of "total_runs".
        // We'll write raw JSON that has the old shape.
        var v1Json = BuildV1FileWithLegacyField(legacyCount: 4);
        File.WriteAllText(provider.GetMainSaveFilePath(), v1Json);

        // Register a migration v1→v2: rename "legacy_count" to "total_runs".
        var testForward = new Dictionary<string, Dictionary<int, Func<JsonNode?, JsonNode>>>(Migrations.Forward)
        {
            ["run_counter"] = new Dictionary<int, Func<JsonNode?, JsonNode>>
            {
                [1] = data =>
                {
                    var obj = (data as JsonObject ?? new JsonObject()).DeepClone() as JsonObject ?? new JsonObject();
                    if (obj["legacy_count"] is JsonNode lc)
                    {
                        obj["total_runs"] = lc.GetValue<int>();
                        obj.Remove("legacy_count");
                    }
                    return obj;
                }
            }
        };

        // Binary thinks run_counter should be at v2.
        var testVersions = new Dictionary<string, int>(Migrations.LatestVersion)
        {
            ["run_counter"] = 2,
        };

        // Apply migrations using the test-injected tables.
        var raw = File.ReadAllText(provider.GetMainSaveFilePath());
        var migrated = Migrations.ApplyMigrations(
            raw,
            latestVersionOverride: testVersions,
            logger: msg => TestContext.Out.WriteLine(msg),
            forwardOverride: testForward);

        // The migrated JSON should have total_runs = 4, no legacy_count.
        var file = JsonSerializer.Deserialize(migrated, PersistenceJsonContext.Default.PersistenceFile)!;
        Assert.That(file.Namespaces.RunCounter.Data.TotalRuns, Is.EqualTo(4));
        Assert.That(file.Namespaces.RunCounter.SchemaVersion, Is.EqualTo(2),
            "Envelope schema_version should be bumped to target version after migration.");
        Assert.That(migrated, Does.Not.Contain("legacy_count"),
            "Old field name must not appear in migrated JSON.");
    }

    [Test]
    public void ForwardMigration_MissingMigrationFunction_PartialMigrationLoggedNotCrashed()
    {
        var provider = Provider();
        var state = PersistentRunState.LoadFromDisk(provider);
        state.RunCounter.TotalRuns = 2;
        state.MarkDirty();
        state.Flush(provider);

        // Binary thinks run_counter should be at v2 but no migration function is registered.
        var testVersions = new Dictionary<string, int>(Migrations.LatestVersion)
        {
            ["run_counter"] = 2,
        };

        var errors = new List<string>();
        // Should not throw.
        var loaded = PersistentRunState.LoadFromDisk(provider, err => errors.Add(err), testVersions);

        // Data that was present in v1 is preserved (fields didn't change, just version bumped).
        Assert.That(loaded.RunCounter.TotalRuns, Is.EqualTo(2));
        Assert.That(errors, Has.Some.Contains("run_counter"),
            "Should log that no migration function was found.");
    }

    // ── Missing namespace in file → defaults ─────────────────────────────────

    [Test]
    public void MissingNamespace_DefaultsOnDeserialize_NoMigrationNeeded()
    {
        var provider = Provider();
        // Write a file that's entirely missing the "under_warden" namespace.
        var json = BuildFileWithoutNamespace("under_warden");
        File.WriteAllText(provider.GetMainSaveFilePath(), json);

        var loaded = PersistentRunState.LoadFromDisk(provider);

        // under_warden defaults: TotalMemosSentEver = 0, AuditCompleted = false.
        Assert.That(loaded.UnderWarden.TotalMemosSentEver, Is.EqualTo(0));
        Assert.That(loaded.UnderWarden.AuditCompleted, Is.False);
        // Other namespaces from the file are preserved.
        Assert.That(loaded.RunCounter.TotalRuns, Is.EqualTo(3));
    }

    // ── ApplyMigrations: malformed JSON passthrough ──────────────────────────

    [Test]
    public void ApplyMigrations_MalformedJson_ReturnsOriginal_DoesNotThrow()
    {
        const string bad = "{ not valid json ]]]";
        string result = null!;
        Assert.DoesNotThrow(() => result = Migrations.ApplyMigrations(bad));
        Assert.That(result, Is.EqualTo(bad));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Patches a specific namespace's schema_version in a JSON string.
    private static string PatchNamespaceVersion(string json, string nsKey, int newVersion)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException();
        var nsObj = root["namespaces"] as JsonObject ?? throw new InvalidOperationException();
        var ns = nsObj[nsKey] as JsonObject ?? throw new InvalidOperationException();
        ns["schema_version"] = newVersion;
        return root.ToJsonString();
    }

    // Builds a valid v1 file JSON where run_counter has "legacy_count" instead of "total_runs".
    private static string BuildV1FileWithLegacyField(int legacyCount)
    {
        // Start from a real PersistenceFile, then patch the run_counter data.
        var file = new PersistenceFile();
        var json = JsonSerializer.Serialize(file, PersistenceJsonContext.Default.PersistenceFile);

        var root = (JsonNode.Parse(json) as JsonObject)!;
        var rcData = (root["namespaces"]!["run_counter"]!["data"] as JsonObject)!;
        rcData!.Remove("total_runs");
        rcData["legacy_count"] = legacyCount;
        // Leave schema_version at 1 — this is the "old" save.
        return root.ToJsonString();
    }

    // Builds a file JSON with a specific namespace removed from the namespaces object.
    private static string BuildFileWithoutNamespace(string nsKey)
    {
        var file = new PersistenceFile();
        // Pre-populate run_counter so we can assert it's preserved.
        file.Namespaces.RunCounter.Data.TotalRuns = 3;
        var json = JsonSerializer.Serialize(file, PersistenceJsonContext.Default.PersistenceFile);

        var root = (JsonNode.Parse(json) as JsonObject)!;
        var nsObj = (root["namespaces"] as JsonObject)!;
        nsObj.Remove(nsKey);
        return root.ToJsonString();
    }
}
