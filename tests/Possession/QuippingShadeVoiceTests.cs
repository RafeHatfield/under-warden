using System.IO;
using System.Text.RegularExpressions;
using UnderWarden.Logic.Content;
using NUnit.Framework;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Tests.Possession;

/// <summary>
/// Quipping shade voice deepening: the dead past-Sasha that narrates its own self-inflicted
/// death. Content-only — these pools are forward-authored (the granular self-inflicted cause
/// producer the keys are keyed on is unbuilt; the engine emits only monster/hazard/weighing_loss_*),
/// so this deepens the 8 existing keys for future cross-run shuffle-bag variety. The shade is a
/// distinct character from Hollowmark (first-person self-death narration, never says "Boss").
/// </summary>
[TestFixture]
public class QuippingShadeVoiceTests
{
    private static string ShadePath() =>
        Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", "voice_lines", "quipping_shade.yaml");

    // The 8 shipped cause-keys (unchanged; only deepened).
    private static readonly string[] Keys =
    {
        "oil_slick_fire", "possession_neglect", "own_poison", "own_trap",
        "fall_damage", "acid", "possessed_wrong_host", "hollowmark_out_of_range",
    };

    private static Dictionary<string, List<string>> LoadPools()
    {
        var yaml = File.ReadAllText(ShadePath());
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .WithObjectFactory(new AotObjectFactory())
            .Build();
        return deserializer.Deserialize<Dictionary<string, List<string>>>(yaml)
               ?? new Dictionary<string, List<string>>();
    }

    [Test]
    public void ShadeYaml_LoadsWithStrictFactory()
    {
        var yaml = File.ReadAllText(ShadePath());
        Assert.DoesNotThrow(() => VoiceLineRegistry.LoadFromYaml(yaml, new AotObjectFactory(strict: true)));
    }

    [Test]
    public void Registry_HasTrigger_ForAllEightShadeKeys()
    {
        var yaml = File.ReadAllText(ShadePath());
        var registry = VoiceLineRegistry.LoadFromYaml(yaml, new AotObjectFactory());
        foreach (var key in Keys)
            Assert.That(registry.HasTrigger(key), Is.True, $"Missing shade trigger: {key}");
    }

    [Test]
    public void PoolCounts_EachKeyDeepenedToThree()
    {
        var pools = LoadPools();
        var total = 0;
        foreach (var key in Keys)
        {
            Assert.That(pools.ContainsKey(key), Is.True, $"Missing shade pool: {key}");
            Assert.That(pools[key].Count, Is.EqualTo(3), $"Shade pool {key} should hold 3 lines.");
            total += pools[key].Count;
        }
        Assert.That(total, Is.EqualTo(24), "Total shade line count across the 8 keys should be 24 (8 x 3).");
    }

    // ─── Anti-tell character tier (config/rubric/voice-anti-tell-lint.md) ──────
    // The shade uses NO markdown at all — em-dash, en-dash, ellipsis glyph, and any
    // of [*`_#] all fail. Hyphen (-) and three-period (...) are authored voice.

    private static readonly Regex CharacterTierViolation =
        new("—|–|…|[*`_#]", RegexOptions.Compiled);

    [Test]
    public void AllShadeLines_ContainNoAntiTellCharacterTierViolations()
    {
        var pools = LoadPools();
        var violations = new List<string>();
        foreach (var (key, lines) in pools)
            foreach (var line in lines)
                if (CharacterTierViolation.IsMatch(line))
                    violations.Add($"{key}: \"{line}\"");

        Assert.That(violations, Is.Empty,
            "Anti-tell character-tier hard-block violated:\n" + string.Join("\n", violations));
    }

    [Test]
    public void AntiTellCheck_CanaryFixture_EmDashFailsTheCheck()
    {
        const string fixtureLine = "I lit the oil—you didn't.";
        Assert.That(CharacterTierViolation.IsMatch(fixtureLine), Is.True,
            "Canary fixture with an em-dash must fail the anti-tell check.");
    }

    [Test]
    public void ShadeLines_NeverAddressBoss()
    {
        // Distinctness invariant: "Boss" is Hollowmark's address tag; the shade (Sasha talking
        // to Sasha across death) never uses it. A shade line containing "Boss" is a register leak.
        var pools = LoadPools();
        foreach (var (key, lines) in pools)
            foreach (var line in lines)
                Assert.That(Regex.IsMatch(line, @"\bBoss\b"), Is.False,
                    $"Shade line in {key} addresses \"Boss\" (Hollowmark's tag): \"{line}\"");
    }
}
