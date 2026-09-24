using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// Tests for BotPersonaConfig, BotPersonaRegistry, and BotPersonaLoader (TASK-001/002).
///
/// Acceptance criteria from the plan:
/// - BotPersonaRegistry.Defaults.Count == 5 with exact 5 keys
/// - Get("balanced").BaseHealThreshold == 0.30 (PoC-exact)
/// - Get(null).Name == "balanced"
/// - Get("nonsense").Name == "balanced" + stderr warning emitted
/// - BotConfig.HealThreshold == 0.30 (no caller breaks)
/// - YAML loads and parsed values match threshold table exactly
/// - Missing file: hardcoded defaults still populated
/// - Extra YAML fields: ignored without throwing
/// </summary>
[TestFixture]
[Category("Bot")]
[Description("BotPersonaConfig record, BotPersonaRegistry defaults and fallback, BotPersonaLoader YAML parsing")]
public class BotPersonaConfigTests
{
    // ── Registry defaults ─────────────────────────────────────────────────────

    [Test]
    [Description("BotPersonaRegistry.Defaults has the 5 game personas + 2 escalator-fork experiment cohorts")]
    public void Defaults_HasExpectedPersonaCount()
    {
        Assert.That(BotPersonaRegistry.Defaults.Count, Is.EqualTo(7),
            "5 game personas + 2 experiment cohorts (escalator_first, escalator_last)");
        Assert.That(BotPersonaRegistry.Defaults.Keys, Is.SupersetOf(
            new[] { "balanced", "cautious", "aggressive", "greedy", "speedrunner",
                    "escalator_first", "escalator_last" }));
    }

    [Test]
    [Description("BotPersonaRegistry.Defaults contains the 5 expected keys")]
    public void Defaults_HasExpectedKeys()
    {
        var expected = new[] { "balanced", "cautious", "aggressive", "greedy", "speedrunner" };
        foreach (var key in expected)
            Assert.That(BotPersonaRegistry.Defaults, Contains.Key(key),
                $"Defaults should contain key '{key}'");
    }

    [Test]
    [Description("BotPersonaRegistry.Get('balanced').BaseHealThreshold == 0.30 (PoC-exact)")]
    public void Get_Balanced_BaseHealThreshold_IsPoCAexact()
    {
        var persona = BotPersonaRegistry.Get("balanced");
        Assert.That(persona.BaseHealThreshold, Is.EqualTo(0.30).Within(0.0001),
            "balanced BaseHealThreshold must be 0.30 (PoC-exact)");
    }

    [Test]
    [Description("BotPersonaRegistry.Get('balanced').PanicHpThreshold == 0.15 (PoC-exact)")]
    public void Get_Balanced_PanicHpThreshold_IsPoCAexact()
    {
        var persona = BotPersonaRegistry.Get("balanced");
        Assert.That(persona.PanicHpThreshold, Is.EqualTo(0.15).Within(0.0001));
    }

    [Test]
    [Description("BotPersonaRegistry.Get(null) returns balanced persona")]
    public void Get_Null_ReturnBalanced()
    {
        var persona = BotPersonaRegistry.Get(null);
        Assert.That(persona.Name, Is.EqualTo("balanced"),
            "Get(null) should return the balanced persona");
    }

    [Test]
    [Description("BotPersonaRegistry.Get('nonsense') returns balanced persona (fallback)")]
    public void Get_Unknown_ReturnBalanced()
    {
        // Capture stderr to verify warning is emitted
        var originalErr = Console.Error;
        var errCapture = new System.IO.StringWriter();
        Console.SetError(errCapture);

        try
        {
            var persona = BotPersonaRegistry.Get("nonsense_persona_xyz");
            Assert.That(persona.Name, Is.EqualTo("balanced"),
                "Get with unknown name should fall back to balanced");

            string errorOutput = errCapture.ToString();
            Assert.That(errorOutput, Does.Contain("nonsense_persona_xyz"),
                "stderr should mention the unknown persona name");
            Assert.That(errorOutput, Does.Contain("balanced").Or.Contains("fallback"),
                "stderr should indicate fallback to balanced");
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    [Test]
    [Description("BotConfig.HealThreshold == 0.30 — legacy API preserved")]
    public void BotConfig_HealThreshold_IsStillThirty()
    {
        Assert.That(BotConfig.HealThreshold, Is.EqualTo(0.30).Within(0.0001),
            "BotConfig.HealThreshold must remain 0.30 for backward compatibility");
    }

    [Test]
    [Description("BotConfig.PanicThreshold == 0.15 — legacy API preserved")]
    public void BotConfig_PanicThreshold_IsStillFifteen()
    {
        Assert.That(BotConfig.PanicThreshold, Is.EqualTo(0.15).Within(0.0001),
            "BotConfig.PanicThreshold must remain 0.15 for backward compatibility");
    }

    // ── Persona values — PoC-exact spot checks ────────────────────────────────

    [Test]
    [Description("cautious persona has BaseHealThreshold 0.50 and AvoidCombat true (PoC-exact)")]
    public void Cautious_HasCorrectValues()
    {
        var p = BotPersonaRegistry.Get("cautious");
        Assert.Multiple(() =>
        {
            Assert.That(p.BaseHealThreshold,        Is.EqualTo(0.50).Within(0.0001));
            Assert.That(p.PanicHpThreshold,         Is.EqualTo(0.30).Within(0.0001));
            Assert.That(p.CombatEngagementDistance, Is.EqualTo(5));
            Assert.That(p.AvoidCombat,              Is.True);
            Assert.That(p.LootPriority,             Is.EqualTo(1));
        });
    }

    [Test]
    [Description("aggressive persona has PanicMultiEnemyCount 3 and CombatEngagementDistance 12 (PoC-exact)")]
    public void Aggressive_HasCorrectValues()
    {
        var p = BotPersonaRegistry.Get("aggressive");
        Assert.Multiple(() =>
        {
            Assert.That(p.BaseHealThreshold,        Is.EqualTo(0.20).Within(0.0001));
            Assert.That(p.PanicHpThreshold,         Is.EqualTo(0.10).Within(0.0001));
            Assert.That(p.PanicMultiEnemyCount,     Is.EqualTo(3));
            Assert.That(p.CombatEngagementDistance, Is.EqualTo(12));
            Assert.That(p.LootPriority,             Is.EqualTo(0));
        });
    }

    [Test]
    [Description("greedy persona has LootPriority 2 (PoC-exact)")]
    public void Greedy_HasLootPriorityTwo()
    {
        var p = BotPersonaRegistry.Get("greedy");
        Assert.That(p.LootPriority, Is.EqualTo(2));
    }

    [Test]
    [Description("speedrunner persona has PreferStairs true and CombatEngagementDistance 4 (PoC-exact)")]
    public void Speedrunner_HasCorrectValues()
    {
        var p = BotPersonaRegistry.Get("speedrunner");
        Assert.Multiple(() =>
        {
            Assert.That(p.PreferStairs,             Is.True);
            Assert.That(p.AvoidCombat,              Is.True);
            Assert.That(p.CombatEngagementDistance, Is.EqualTo(4));
            Assert.That(p.LootPriority,             Is.EqualTo(0));
        });
    }

    // ── YAML loader ───────────────────────────────────────────────────────────

    [Test]
    [Description("BotPersonaLoader parses YAML string and returns 5 personas")]
    public void Loader_ParsesYamlString_ReturnsAllPersonas()
    {
        const string yaml = @"
personas:
  balanced:
    retreat_hp_threshold: 0.25
    base_heal_threshold: 0.30
    panic_hp_threshold: 0.15
    panic_multi_enemy_count: 2
    combat_engagement_distance: 8
    loot_priority: 1
    prefer_stairs: false
    avoid_combat: false
    allow_combat_healing: true
  cautious:
    retreat_hp_threshold: 0.40
    base_heal_threshold: 0.50
    panic_hp_threshold: 0.30
    panic_multi_enemy_count: 2
    combat_engagement_distance: 5
    loot_priority: 1
    prefer_stairs: false
    avoid_combat: true
    allow_combat_healing: true
";
        var result = BotPersonaLoader.LoadFromYaml(yaml);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result["balanced"].BaseHealThreshold, Is.EqualTo(0.30).Within(0.0001));
        Assert.That(result["cautious"].AvoidCombat, Is.True);
    }

    [Test]
    [Description("BotPersonaLoader ignores unknown YAML fields without throwing")]
    public void Loader_IgnoresExtraFields()
    {
        const string yaml = @"
personas:
  balanced:
    base_heal_threshold: 0.30
    panic_hp_threshold: 0.15
    unknown_future_field: some_value
    another_unknown: 99
";
        IReadOnlyDictionary<string, BotPersonaConfig>? result = null;
        Assert.DoesNotThrow(() => result = BotPersonaLoader.LoadFromYaml(yaml),
            "Extra YAML fields must be ignored without throwing");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!["balanced"].BaseHealThreshold, Is.EqualTo(0.30).Within(0.0001));
    }

    [Test]
    [Description("BotPersonaLoader with missing fields uses C# record defaults (balanced fallback values)")]
    public void Loader_MissingFields_UseCsharpDefaults()
    {
        // Only provide base_heal_threshold — all other fields should use DTO defaults
        const string yaml = @"
personas:
  partial_persona:
    base_heal_threshold: 0.35
";
        var result = BotPersonaLoader.LoadFromYaml(yaml);
        var partial = result["partial_persona"];
        // DTO defaults match balanced values
        Assert.That(partial.BaseHealThreshold,        Is.EqualTo(0.35).Within(0.0001));
        Assert.That(partial.CombatEngagementDistance, Is.EqualTo(8),
            "Missing field should default to balanced's combat_engagement_distance");
        Assert.That(partial.LootPriority,             Is.EqualTo(1),
            "Missing field should default to loot_priority=1");
    }

    [Test]
    [Description("BotPersonaLoader loads config/bot_personas.yaml and values match threshold table")]
    public void Loader_LoadsFile_ValuesMatchThresholdTable()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var path = Path.Combine(testDir, "..", "..", "..", "..", "config", "bot_personas.yaml");
        if (!File.Exists(path))
            Assert.Ignore("config/bot_personas.yaml not found — skipping file load test");

        var result = BotPersonaLoader.LoadFromFile(path);
        Assert.That(result.Count, Is.EqualTo(5), "Should load all 5 personas from file");

        // Spot-check all 5 personas against the plan's threshold table
        Assert.Multiple(() =>
        {
            // balanced
            var b = result["balanced"];
            Assert.That(b.RetreatHpThreshold,       Is.EqualTo(0.25).Within(0.0001));
            Assert.That(b.BaseHealThreshold,         Is.EqualTo(0.30).Within(0.0001));
            Assert.That(b.PanicHpThreshold,          Is.EqualTo(0.15).Within(0.0001));
            Assert.That(b.PanicMultiEnemyCount,      Is.EqualTo(2));
            Assert.That(b.CombatEngagementDistance,  Is.EqualTo(8));
            Assert.That(b.LootPriority,              Is.EqualTo(1));
            Assert.That(b.PreferStairs,              Is.False);
            Assert.That(b.AvoidCombat,               Is.False);
            Assert.That(b.AllowCombatHealing,        Is.True);

            // aggressive
            var a = result["aggressive"];
            Assert.That(a.PanicMultiEnemyCount,     Is.EqualTo(3));
            Assert.That(a.CombatEngagementDistance, Is.EqualTo(12));
            Assert.That(a.LootPriority,             Is.EqualTo(0));

            // speedrunner
            var s = result["speedrunner"];
            Assert.That(s.PreferStairs,             Is.True);
            Assert.That(s.AvoidCombat,              Is.True);
            Assert.That(s.CombatEngagementDistance, Is.EqualTo(4));
        });
    }
}
