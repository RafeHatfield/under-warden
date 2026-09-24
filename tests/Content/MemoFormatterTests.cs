using UnderWarden.Logic.Content;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

/// <summary>
/// Tests for MemoFormatter: slot substitution, cause-of-death resolution,
/// fire-index body selection, and edge cases.
/// </summary>
[TestFixture]
public class MemoFormatterTests
{
    private static readonly MemoFormatter Formatter = new();

    // Minimal registry with one known cause display name for testing
    private static MemoRegistry MakeRegistry(string causeYaml = "spike_trap: \"encounter with a sprung floor trap\"")
    {
        return MemoRegistry.LoadFromYaml(
            """
            test.memo:
              register: direct
              subject: "Subject {floor}"
              body:
                - "Body {floor} first."
                - "Body {floor} second."
            """,
            causeYaml,
            new AotObjectFactory());
    }

    private static MemoDefinition MakeMemo(string subject, params string[] bodyLines)
    {
        return new MemoDefinition
        {
            Register = MemoRegister.Direct,
            Subject = subject,
            Body = new List<string>(bodyLines),
        };
    }

    // ── Fire index body selection ──────────────────────────────────────────────

    [Test]
    public void Format_FireIndex0_ReturnsBodyZero()
    {
        var registry = MakeRegistry();
        var memo = MakeMemo("S", "body zero", "body one");
        var slots = new Dictionary<string, string>();

        var (_, body) = Formatter.Format(memo, fireIndex: 0, slots, registry);
        Assert.That(body, Is.EqualTo("body zero"));
    }

    [Test]
    public void Format_FireIndex1_ReturnsBodyOne()
    {
        var registry = MakeRegistry();
        var memo = MakeMemo("S", "body zero", "body one");
        var slots = new Dictionary<string, string>();

        var (_, body) = Formatter.Format(memo, fireIndex: 1, slots, registry);
        Assert.That(body, Is.EqualTo("body one"));
    }

    [Test]
    public void Format_FireIndex1_SingleBodyMemo_FallsBackToBodyZero()
    {
        // Single-variant memo: fireIndex=1 should fall back to body[0], not throw.
        var registry = MakeRegistry();
        var memo = MakeMemo("S", "only body");
        var slots = new Dictionary<string, string>();

        var (_, body) = Formatter.Format(memo, fireIndex: 1, slots, registry);
        Assert.That(body, Is.EqualTo("only body"));
    }

    [Test]
    public void Format_FireIndex5_ExceedsBodyCount_ClampsToLastBody()
    {
        // Out-of-range fireIndex should clamp to the last body variant, not fall back to
        // body[0]. This ensures the "increasingly weary Under-Warden" arc stays at the
        // shortest/most-exhausted steady state for repeat offenders rather than reverting
        // to the full first-encounter explanation.
        var registry = MakeRegistry();
        var memo = MakeMemo("S", "body zero", "body one");
        var slots = new Dictionary<string, string>();

        var (_, body) = Formatter.Format(memo, fireIndex: 5, slots, registry);
        Assert.That(body, Is.EqualTo("body one"));
    }

    [Test]
    public void Format_NegativeFireIndex_TreatedAsZero()
    {
        var registry = MakeRegistry();
        var memo = MakeMemo("S", "body zero", "body one");
        var slots = new Dictionary<string, string>();

        var (_, body) = Formatter.Format(memo, fireIndex: -1, slots, registry);
        Assert.That(body, Is.EqualTo("body zero"));
    }

    // ── Subject substitution ──────────────────────────────────────────────────

    [Test]
    public void Format_SubjectSlotSubstituted()
    {
        var registry = MakeRegistry();
        var memo = MakeMemo("Attrition Record: Unit #{run_number}, Floor {floor}", "body");
        var slots = new Dictionary<string, string>
        {
            ["run_number"] = "7",
            ["floor"] = "3",
        };

        var (subject, _) = Formatter.Format(memo, 0, slots, registry);
        Assert.That(subject, Is.EqualTo("Attrition Record: Unit #7, Floor 3"));
    }

    [Test]
    public void Format_BodySlotSubstituted()
    {
        var registry = MakeRegistry();
        var memo = MakeMemo("S", "Terminated on floor {floor}. Runs: {run_count}.");
        var slots = new Dictionary<string, string>
        {
            ["floor"] = "5",
            ["run_count"] = "12",
        };

        var (_, body) = Formatter.Format(memo, 0, slots, registry);
        Assert.That(body, Is.EqualTo("Terminated on floor 5. Runs: 12."));
    }

    [Test]
    public void Format_MultipleOccurrencesOfSameSlot_AllReplaced()
    {
        var registry = MakeRegistry();
        var memo = MakeMemo("S", "Floor {floor} again on floor {floor}.");
        var slots = new Dictionary<string, string> { ["floor"] = "4" };

        var (_, body) = Formatter.Format(memo, 0, slots, registry);
        Assert.That(body, Is.EqualTo("Floor 4 again on floor 4."));
    }

    [Test]
    public void Format_MissingSlotKey_LeavedPlaceholderAsIs()
    {
        var registry = MakeRegistry();
        var memo = MakeMemo("S {unknown_slot}", "Body {also_unknown}.");
        var slots = new Dictionary<string, string>();

        var (subject, body) = Formatter.Format(memo, 0, slots, registry);
        Assert.That(subject, Is.EqualTo("S {unknown_slot}"));
        Assert.That(body, Is.EqualTo("Body {also_unknown}."));
    }

    // ── Cause of death resolution ─────────────────────────────────────────────

    [Test]
    public void Format_CauseOfDeath_MappedValue_SubstitutesDisplayName()
    {
        // spike_trap → "encounter with a sprung floor trap"
        var registry = MakeRegistry();
        var memo = MakeMemo("S", "Cause: {cause_of_death}.");
        var slots = new Dictionary<string, string> { ["cause_of_death"] = "spike_trap" };

        var (_, body) = Formatter.Format(memo, 0, slots, registry);
        Assert.That(body, Is.EqualTo("Cause: encounter with a sprung floor trap."));
    }

    [Test]
    public void Format_CauseOfDeath_UnmappedValue_TitleCaseFallback()
    {
        // "some_unknown" → no display name → "Some Unknown" (underscore→space + title-case)
        var registry = MakeRegistry();
        var memo = MakeMemo("S", "Cause: {cause_of_death}.");
        var slots = new Dictionary<string, string> { ["cause_of_death"] = "some_unknown" };

        var (_, body) = Formatter.Format(memo, 0, slots, registry);
        Assert.That(body, Is.EqualTo("Cause: Some Unknown."));
    }

    [Test]
    public void Format_CauseOfDeath_MultiWordUnmapped_TitleCaseFallback()
    {
        // "an_obscure_cause_here" → "An Obscure Cause Here"
        var registry = MakeRegistry();
        var memo = MakeMemo("S", "{cause_of_death}");
        var slots = new Dictionary<string, string> { ["cause_of_death"] = "an_obscure_cause_here" };

        var (_, body) = Formatter.Format(memo, 0, slots, registry);
        Assert.That(body, Is.EqualTo("An Obscure Cause Here"));
    }

    [Test]
    public void Format_CauseOfDeathInSubject_AlsoSubstituted()
    {
        var registry = MakeRegistry();
        var memo = MakeMemo("Termination: {cause_of_death}", "body");
        var slots = new Dictionary<string, string> { ["cause_of_death"] = "spike_trap" };

        var (subject, _) = Formatter.Format(memo, 0, slots, registry);
        Assert.That(subject, Is.EqualTo("Termination: encounter with a sprung floor trap"));
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Test]
    public void Format_EmptyBody_ReturnsEmptyString()
    {
        var registry = MakeRegistry();
        var memo = new MemoDefinition
        {
            Register = MemoRegister.Direct,
            Subject = "S",
            Body = new List<string>(),
        };
        var slots = new Dictionary<string, string>();

        var (_, body) = Formatter.Format(memo, 0, slots, registry);
        Assert.That(body, Is.EqualTo(""));
    }

    [Test]
    public void Format_NoSlotsInText_TextPassedThrough()
    {
        var registry = MakeRegistry();
        var memo = MakeMemo("Plain subject", "Plain body with no slots.");
        var slots = new Dictionary<string, string> { ["unused"] = "value" };

        var (subject, body) = Formatter.Format(memo, 0, slots, registry);
        Assert.That(subject, Is.EqualTo("Plain subject"));
        Assert.That(body, Is.EqualTo("Plain body with no slots."));
    }

    [Test]
    public void Format_BothSubjectAndBody_BothGetSlots()
    {
        var registry = MakeRegistry();
        var memo = MakeMemo("Floor {floor} report", "Run {run_number} terminated on floor {floor}.");
        var slots = new Dictionary<string, string>
        {
            ["floor"] = "8",
            ["run_number"] = "42",
        };

        var (subject, body) = Formatter.Format(memo, 0, slots, registry);
        Assert.That(subject, Is.EqualTo("Floor 8 report"));
        Assert.That(body, Is.EqualTo("Run 42 terminated on floor 8."));
    }

    // ── Integration: real YAML file ───────────────────────────────────────────

    [Test]
    public void Format_RealMemo_PoliteDeathFirst_SubstitutesExpectedSlots()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var memosYaml = File.ReadAllText(Path.GetFullPath(
            Path.Combine(testDir, "..", "..", "..", "..", "config", "under_warden", "memos.yaml")));
        var causeYaml = File.ReadAllText(Path.GetFullPath(
            Path.Combine(testDir, "..", "..", "..", "..", "config", "under_warden", "cause_display_names.yaml")));

        var registry = MemoRegistry.LoadFromYaml(memosYaml, causeYaml, new AotObjectFactory());
        var memo = registry.GetMemo("polite.death_first");
        Assert.That(memo, Is.Not.Null);

        var slots = new Dictionary<string, string>
        {
            ["run_number"] = "3",
            ["floor"] = "2",
            ["cause_of_death"] = "spike_trap",
        };

        var (subject, body) = Formatter.Format(memo!, 0, slots, registry);

        Assert.That(subject, Contains.Substring("3"));
        Assert.That(subject, Contains.Substring("2"));
        Assert.That(body, Contains.Substring("encounter with a sprung floor trap"));
        // Verify run_number substitution in body (appears as "Unit #3" in the template)
        Assert.That(body, Contains.Substring("#3"));
    }
}
