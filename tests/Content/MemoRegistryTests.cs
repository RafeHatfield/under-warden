using UnderWarden.Logic.Content;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

/// <summary>
/// Tests for MemoRegistry: YAML loading, key lookup, cause display name resolution,
/// and fire-index semantics.
/// </summary>
[TestFixture]
public class MemoRegistryTests
{
    private static string GetConfigPath(string filename)
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        return Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", "under_warden", filename));
    }

    private static MemoRegistry LoadFromFiles()
    {
        var memosYaml = File.ReadAllText(GetConfigPath("memos.yaml"));
        var causeYaml = File.ReadAllText(GetConfigPath("cause_display_names.yaml"));
        return MemoRegistry.LoadFromYaml(memosYaml, causeYaml, new AotObjectFactory());
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    [Test]
    public void LoadFromFiles_Succeeds_NoException()
    {
        Assert.DoesNotThrow(() => LoadFromFiles());
    }

    [Test]
    public void LoadFromFiles_Loads7Memos()
    {
        // The memos.yaml contains exactly 7 top-level memo entries as of this writing.
        // This test catches accidental additions or deletions to the YAML file.
        var registry = LoadFromFiles();

        var expectedKeys = new[]
        {
            "polite.death_first",
            "polite.floor_low",
            "polite.cause_trap",
            "polite.cause_acid",
            "polite.hall_warden_possession",
            "procedural_notice.hall_warden_possession",
            "formal_complaint.hall_warden_possession",
        };

        foreach (var key in expectedKeys)
        {
            var memo = registry.GetMemo(key);
            Assert.That(memo, Is.Not.Null, $"Expected memo key '{key}' to be present");
        }
    }

    [Test]
    public void GetMemo_PoliteDeath_First_ReturnsCorrectRegisterAndFields()
    {
        var registry = LoadFromFiles();
        var memo = registry.GetMemo("polite.death_first");

        Assert.That(memo, Is.Not.Null);
        Assert.That(memo!.Register, Is.EqualTo(MemoRegister.InternalCc));
        Assert.That(memo.To, Is.EqualTo("Occupancy Management, Sublevel Correspondence"));
        Assert.That(memo.Subject, Contains.Substring("{run_number}"));
        Assert.That(memo.Body.Count, Is.GreaterThan(0));
    }

    [Test]
    public void GetMemo_DirectMemo_HasDirectRegister()
    {
        var registry = LoadFromFiles();
        var memo = registry.GetMemo("polite.floor_low");

        Assert.That(memo, Is.Not.Null);
        Assert.That(memo!.Register, Is.EqualTo(MemoRegister.Direct));
        Assert.That(memo.To, Is.Null);
    }

    [Test]
    public void GetMemo_UnknownKey_ReturnsNull()
    {
        var registry = LoadFromFiles();
        var result = registry.GetMemo("does_not_exist.ever");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetMemo_EmptyKey_ReturnsNull()
    {
        var registry = LoadFromFiles();
        var result = registry.GetMemo("");
        Assert.That(result, Is.Null);
    }

    // ── Body content ──────────────────────────────────────────────────────────

    [Test]
    public void GetMemo_BodyIsNonEmpty()
    {
        var registry = LoadFromFiles();
        var memo = registry.GetMemo("polite.death_first");

        Assert.That(memo!.Body, Is.Not.Empty);
        Assert.That(memo.Body[0], Is.Not.Empty);
    }

    [Test]
    public void GetMemo_SubjectContainsSlotTokens()
    {
        // Spot-check that subject slot tokens survived deserialization intact.
        var registry = LoadFromFiles();
        var memo = registry.GetMemo("polite.cause_trap");

        Assert.That(memo!.Subject, Contains.Substring("{floor}"));
    }

    // ── Cause display names ───────────────────────────────────────────────────

    [Test]
    public void GetCauseDisplayName_SpikeTrap_ReturnsBureaucraticPhrase()
    {
        var registry = LoadFromFiles();
        var name = registry.GetCauseDisplayName("spike_trap");
        Assert.That(name, Is.EqualTo("encounter with a sprung floor trap"));
    }

    [Test]
    public void GetCauseDisplayName_OrcBrute_ReturnsBureaucraticPhrase()
    {
        var registry = LoadFromFiles();
        var name = registry.GetCauseDisplayName("orc_brute");
        Assert.That(name, Is.EqualTo("engagement with a hostile Unshriven combatant"));
    }

    [Test]
    public void GetCauseDisplayName_UnknownKey_ReturnsNull()
    {
        var registry = LoadFromFiles();
        var name = registry.GetCauseDisplayName("some_unknown_cause_xyz");
        Assert.That(name, Is.Null);
    }

    [Test]
    public void GetCauseDisplayName_EmptyKey_ReturnsNull()
    {
        var registry = LoadFromFiles();
        var name = registry.GetCauseDisplayName("");
        Assert.That(name, Is.Null);
    }

    // ── Inline YAML (strict AotObjectFactory) ─────────────────────────────────

    [Test]
    public void LoadFromYaml_Strict_SingleMemo_Works()
    {
        // Verifies NativeAOT-safe path: all required types must be in the factory.
        const string memosYaml = """
            polite.test:
              register: direct
              subject: "Test subject {floor}"
              body:
                - "First body line."
                - "Second variant."
            """;
        const string causeYaml = "spike_trap: \"encounter with a sprung floor trap\"";

        Assert.DoesNotThrow(() =>
            MemoRegistry.LoadFromYaml(memosYaml, causeYaml, new AotObjectFactory(strict: true)));
    }

    [Test]
    public void LoadFromYaml_InternalCc_RegisterParsedCorrectly()
    {
        const string memosYaml = """
            test.internal:
              register: internal_cc
              to: "Some Department"
              subject: "A subject"
              body:
                - "Body text."
            """;

        var registry = MemoRegistry.LoadFromYaml(memosYaml, "", new AotObjectFactory());
        var memo = registry.GetMemo("test.internal");

        Assert.That(memo, Is.Not.Null);
        Assert.That(memo!.Register, Is.EqualTo(MemoRegister.InternalCc));
        Assert.That(memo.To, Is.EqualTo("Some Department"));
    }
}
