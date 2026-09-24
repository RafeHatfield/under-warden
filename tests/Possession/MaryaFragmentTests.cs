using System.IO;
using System.Text.RegularExpressions;
using UnderWarden.Logic.Persistence.Namespaces;
using NUnit.Framework;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Tests.Possession;

/// <summary>
/// Hollowmark voice batch 1f: Marya memory fragments. These are NOT ribbon voice
/// lines — they are cross-run one-shot fragments consumed via
/// MaryaFragmentRecord.FragmentTextRef and re-read from the catalog. The schema
/// is fragment-per-key with text + meta, not VoiceLineRegistry's key -> list
/// schema, so this file is never loaded through VoiceLineRegistry.
/// Content-only: no fragment loader, no mural-trigger hookup, no
/// MaryaFragmentsData changes. meta.tier / meta.min_depth are unlock-gating
/// intent for future wiring, not consumed by anything yet.
/// </summary>
[TestFixture]
public class MaryaFragmentTests
{
    private static string FragmentsPath() =>
        Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", "voice_lines", "marya_fragments.yaml");

    private static readonly string[] ExpectedIds =
    {
        "mf_stonework", "mf_wend_bells", "mf_folds", "mf_erasure", "mf_ana_north",
        "mf_stayed", "mf_wand_making", "mf_death", "mf_waking", "mf_suspects",
        "mf_name", "mf_span",
    };

    private static readonly HashSet<string> ValidTiers = new() { "common", "mid", "late", "rare" };

    private sealed class FragmentMeta
    {
        public string Tier { get; set; } = "";
        public int MinDepth { get; set; }
    }

    private sealed class FragmentEntry
    {
        public string Text { get; set; } = "";
        public FragmentMeta Meta { get; set; } = new();
    }

    private sealed class FragmentsFile
    {
        public string Version { get; set; } = "";
        public Dictionary<string, FragmentEntry> Fragments { get; set; } = new();
    }

    private static FragmentsFile LoadFragments()
    {
        var yaml = File.ReadAllText(FragmentsPath());
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<FragmentsFile>(yaml) ?? new FragmentsFile();
    }

    [Test]
    public void FragmentsFile_ParsesWithoutError()
    {
        Assert.DoesNotThrow(() => LoadFragments());
    }

    [Test]
    public void FragmentsFile_HasVersionAndFragmentsNode()
    {
        var file = LoadFragments();
        Assert.That(file.Version, Is.EqualTo("1.0"));
        Assert.That(file.Fragments, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void FragmentIds_ExactlyMatchExpectedSet()
    {
        var file = LoadFragments();
        Assert.That(file.Fragments.Count, Is.EqualTo(12), "Expected exactly 12 fragment ids.");
        Assert.That(file.Fragments.Keys, Is.EquivalentTo(ExpectedIds));
    }

    [Test]
    public void EveryFragment_HasNonEmptyTextValidTierAndMinDepthInRange()
    {
        var file = LoadFragments();
        foreach (var (id, entry) in file.Fragments)
        {
            Assert.That(entry.Text, Is.Not.Null.And.Not.Empty, $"{id}: empty text.");
            Assert.That(ValidTiers, Does.Contain(entry.Meta.Tier), $"{id}: invalid tier '{entry.Meta.Tier}'.");
            Assert.That(entry.Meta.MinDepth, Is.InRange(1, 25), $"{id}: min_depth out of [1,25].");
        }
    }

    [Test]
    public void MinDepth_IsNonDecreasing_InDocumentOrder()
    {
        // Dictionary iteration order isn't a language guarantee, so derive the
        // real document order from the raw file text (regex over the
        // two-space-indented fragment-id lines under "fragments:"), the same
        // pattern used for entities.yaml elsewhere in this test project.
        var lines = File.ReadAllLines(FragmentsPath());
        var fragmentsStart = Array.FindIndex(lines, l => l == "fragments:");
        Assert.That(fragmentsStart, Is.GreaterThanOrEqualTo(0), "No top-level 'fragments:' key found.");

        var orderedIds = new List<string>();
        for (var i = fragmentsStart + 1; i < lines.Length; i++)
        {
            var m = Regex.Match(lines[i], @"^  (?<id>mf_[a-z_]+):\s*$");
            if (m.Success)
                orderedIds.Add(m.Groups["id"].Value);
        }
        Assert.That(orderedIds, Is.EqualTo(ExpectedIds), "Document order must match the authored fragment order.");

        var file = LoadFragments();
        var depths = orderedIds.Select(id => file.Fragments[id].Meta.MinDepth).ToList();
        for (var i = 1; i < depths.Count; i++)
            Assert.That(depths[i], Is.GreaterThanOrEqualTo(depths[i - 1]),
                $"min_depth decreased at position {i} ({orderedIds[i]}: {depths[i]} < {orderedIds[i - 1]}: {depths[i - 1]}).");
    }

    // ─── Anti-tell character tier (config/rubric/voice-anti-tell-lint.md) ──────

    private static readonly Regex CharacterTierViolation =
        new("—|–|…|[*`_#]", RegexOptions.Compiled);

    [Test]
    public void AllFragmentText_ContainsNoAntiTellCharacterTierViolations()
    {
        var file = LoadFragments();
        var violations = new List<string>();

        foreach (var (id, entry) in file.Fragments)
        {
            if (CharacterTierViolation.IsMatch(entry.Text))
                violations.Add($"{id}: \"{entry.Text}\"");
        }

        Assert.That(violations, Is.Empty,
            "Anti-tell character-tier hard-block violated:\n" + string.Join("\n", violations));
    }

    [Test]
    public void AntiTellCheck_CanaryFixture_EmDashFailsTheCheck()
    {
        const string fixtureLine = "Boss—don't.";
        Assert.That(CharacterTierViolation.IsMatch(fixtureLine), Is.True,
            "Canary fixture with an em-dash must fail the anti-tell check.");
    }

    // ─── MaryaFragmentsData round-trip sanity ──────────────────────────────────

    [Test]
    public void MaryaFragmentsData_TryUnlock_FiresOnceForARealFragmentId()
    {
        var file = LoadFragments();
        var realId = ExpectedIds[0];
        Assert.That(file.Fragments.ContainsKey(realId), Is.True,
            "Sanity check: the id used for the round-trip test must exist in the authored file.");

        var data = new MaryaFragmentsData();
        var first = data.TryUnlock(realId, unlockedRun: 1, place: "test", fragmentTextRef: realId);
        Assert.That(first, Is.Not.Null, "First unlock should succeed.");
        Assert.That(data.HasUnlocked(realId), Is.True);

        var second = data.TryUnlock(realId, unlockedRun: 2, place: "test", fragmentTextRef: realId);
        Assert.That(second, Is.Null, "Second unlock of the same id should be a no-op.");
    }
}
