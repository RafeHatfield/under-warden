using System.Text.RegularExpressions;
using UnderWarden.Logic.Content;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

/// <summary>
/// Body-variant progressions for the four twice-fireable memo keys that previously had only
/// body[0]. Each now carries a 3-stage weariness progression (full -> shorter -> terse), matching
/// the shipped polite.cause_trap / polite.cause_acid template. MemoFormatter clamps fireIndex to
/// the last variant, so body[2] is the permanent steady state for repeat offenders.
/// </summary>
[TestFixture]
public class MemoBodyVariantTests
{
    private static string ConfigPath(string filename) =>
        Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", "under_warden", filename));

    private static MemoRegistry Load()
    {
        var memos = File.ReadAllText(ConfigPath("memos.yaml"));
        var causes = File.ReadAllText(ConfigPath("cause_display_names.yaml"));
        return MemoRegistry.LoadFromYaml(memos, causes, new AotObjectFactory());
    }

    // The four keys that gained body[1]/body[2] this batch (the other two twice-fireable keys,
    // cause_trap and cause_acid, already shipped 3-stage variants).
    private static readonly string[] DeepenedKeys =
    {
        "polite.floor_low",
        "procedural_notice.cause_possession_neglect",
        "procedural_notice.death_repeat",
        "procedural_notice.run_clean",
    };

    [Test]
    public void MemosYaml_LoadsWithoutError()
    {
        Assert.DoesNotThrow(() => Load());
    }

    [Test]
    public void DeepenedKeys_EachHaveThreeDistinctNonEmptyBodyVariants()
    {
        var registry = Load();
        foreach (var key in DeepenedKeys)
        {
            var memo = registry.GetMemo(key);
            Assert.That(memo, Is.Not.Null, $"Missing memo key: {key}");
            Assert.That(memo!.Body.Count, Is.EqualTo(3), $"{key} should have body[0..2] (3 variants).");
            foreach (var b in memo.Body)
                Assert.That(b, Is.Not.Null.And.Not.Empty, $"{key} has an empty body variant.");
            // Each stage must differ from the others (a weariness progression, not copies).
            Assert.That(memo.Body.Distinct().Count(), Is.EqualTo(3), $"{key} body variants must be distinct.");
            // Contraction: each later stage is shorter than the one before it.
            Assert.That(memo.Body[1].Length, Is.LessThan(memo.Body[0].Length), $"{key} body[1] should be shorter than body[0].");
            Assert.That(memo.Body[2].Length, Is.LessThan(memo.Body[1].Length), $"{key} body[2] should be shorter than body[1].");
        }
    }

    // ─── Anti-tell character tier (config/rubric/voice-anti-tell-lint.md) ──────
    // Memos are in scope for the lint. The character-tier hard-block covers em-dash,
    // en-dash, and the ellipsis glyph. The "**Summary:**" bold is the established shipped
    // memo convention (RichTextLabel markup) and is NOT scanned here.

    private static readonly Regex GlyphViolation = new("—|–|…", RegexOptions.Compiled);

    [Test]
    public void DeepenedKeyBodies_ContainNoEmDashEnDashOrEllipsisGlyph()
    {
        var registry = Load();
        var violations = new List<string>();
        foreach (var key in DeepenedKeys)
            foreach (var body in registry.GetMemo(key)!.Body)
                if (GlyphViolation.IsMatch(body))
                    violations.Add($"{key}: \"{body}\"");

        Assert.That(violations, Is.Empty,
            "Anti-tell character-tier hard-block violated:\n" + string.Join("\n", violations));
    }

    [Test]
    public void AntiTellCheck_CanaryFixture_EmDashFailsTheCheck()
    {
        const string fixtureLine = "Termination filed—no comment.";
        Assert.That(GlyphViolation.IsMatch(fixtureLine), Is.True,
            "Canary fixture with an em-dash must fail the anti-tell check.");
    }
}
