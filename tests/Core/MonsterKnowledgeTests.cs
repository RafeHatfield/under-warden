using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Knowledge;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for MonsterKnowledgeEntry tier logic, MonsterKnowledgeSystem stat labels,
/// and TurnController integration (engaged/killed tracking via events).
/// </summary>
[TestFixture]
public class MonsterKnowledgeTests
{
    // ── Tier progression ──────────────────────────────────────────────────────

    [Test]
    public void RecordSeen_Zero_TierIsUnknown()
    {
        var entry = new MonsterKnowledgeEntry();
        Assert.That(entry.Tier, Is.EqualTo(KnowledgeTier.Unknown));
    }

    [Test]
    public void RecordSeen_Once_TierIsObserved()
    {
        var system = new MonsterKnowledgeSystem();
        system.RecordSeen("orc");
        Assert.That(system.GetEntry("orc").Tier, Is.EqualTo(KnowledgeTier.Observed));
    }

    [Test]
    public void RecordEngaged_TwoTimes_TierStillObserved()
    {
        var system = new MonsterKnowledgeSystem();
        system.RecordSeen("orc");
        system.RecordEngaged("orc");
        system.RecordEngaged("orc");
        Assert.That(system.GetEntry("orc").Tier, Is.EqualTo(KnowledgeTier.Observed));
    }

    [Test]
    public void RecordEngaged_ThreeTimes_TierIsBattled()
    {
        var system = new MonsterKnowledgeSystem();
        system.RecordSeen("orc");
        system.RecordEngaged("orc");
        system.RecordEngaged("orc");
        system.RecordEngaged("orc");
        Assert.That(system.GetEntry("orc").Tier, Is.EqualTo(KnowledgeTier.Battled));
    }

    [Test]
    public void RecordKilled_Four_TierStillBattled()
    {
        var system = new MonsterKnowledgeSystem();
        system.RecordSeen("orc");
        // 3 engagements to reach Battled first
        system.RecordEngaged("orc");
        system.RecordEngaged("orc");
        system.RecordEngaged("orc");
        // 4 kills — not enough for Understood (need 5)
        for (int i = 0; i < 4; i++) system.RecordKilled("orc");
        Assert.That(system.GetEntry("orc").Tier, Is.EqualTo(KnowledgeTier.Battled));
    }

    [Test]
    public void RecordKilled_FiveTimes_TierIsUnderstood()
    {
        var system = new MonsterKnowledgeSystem();
        for (int i = 0; i < 5; i++) system.RecordKilled("orc");
        Assert.That(system.GetEntry("orc").Tier, Is.EqualTo(KnowledgeTier.Understood));
    }

    [Test]
    public void MajorTrait_PlagueCarrier_TriggersBattledEarlyToUnderstood()
    {
        // plague_carrier is a major trait — discovering it pushes directly to Understood
        // regardless of kill count, matching PoC: TIER_3_TRAIT_EXPERIENCE_UNLOCKS = True
        var system = new MonsterKnowledgeSystem();
        system.RecordSeen("zombie");
        system.RecordTrait("zombie", "plague_carrier");
        Assert.That(system.GetEntry("zombie").Tier, Is.EqualTo(KnowledgeTier.Understood));
    }

    [Test]
    public void MajorTrait_SwarmAi_TriggersTierUnderstood()
    {
        var system = new MonsterKnowledgeSystem();
        system.RecordSeen("slime");
        system.RecordTrait("slime", "swarm_ai");
        Assert.That(system.GetEntry("slime").Tier, Is.EqualTo(KnowledgeTier.Understood));
    }

    // ── Stat label computation ────────────────────────────────────────────────

    private static MonsterKnowledgeSystem MakeUnderstoodSystem(string speciesId = "test_monster")
    {
        var system = new MonsterKnowledgeSystem();
        for (int i = 0; i < 5; i++) system.RecordKilled(speciesId);
        return system;
    }

    private static MonsterDefinition MakeDef(int hp, int defense, int damageMin, int damageMax,
        int power = 0, double speedBonus = 0, int accuracy = 2, int evasion = 1,
        string? faction = null, List<string>? tags = null)
    {
        return new MonsterDefinition
        {
            Name = "Test Monster",
            Faction = faction ?? "neutral",
            Tags = tags,
            SpeedBonus = speedBonus,
            Stats = new MonsterStats
            {
                Hp = hp,
                Defense = defense,
                DamageMin = damageMin,
                DamageMax = damageMax,
                Power = power,
                Accuracy = accuracy,
                Evasion = evasion,
            }
        };
    }

    [Test]
    public void StatLabels_Durability_FragileRange()
    {
        // hp=10, defense=1 → durability = 10 + 1*5 = 15 < 20 → Fragile
        var system = MakeUnderstoodSystem();
        var def = MakeDef(hp: 10, defense: 1, damageMin: 1, damageMax: 2);
        var view = system.GetInfoView("test_monster", def);
        Assert.That(view.DurabilityLabel, Is.EqualTo("Fragile"));
    }

    [Test]
    public void StatLabels_Durability_MonstruosRange()
    {
        // hp=60, defense=3 → durability = 60 + 3*5 = 75 >= 70 → Monstrous
        var system = MakeUnderstoodSystem();
        var def = MakeDef(hp: 60, defense: 3, damageMin: 1, damageMax: 2);
        var view = system.GetInfoView("test_monster", def);
        Assert.That(view.DurabilityLabel, Is.EqualTo("Monstrous"));
    }

    [Test]
    public void StatLabels_Damage_LightRange()
    {
        // damageMin=1, damageMax=3, power=0 → avg = 2.0 < 4 → Light
        var system = MakeUnderstoodSystem();
        var def = MakeDef(hp: 10, defense: 0, damageMin: 1, damageMax: 3, power: 0);
        var view = system.GetInfoView("test_monster", def);
        Assert.That(view.DamageLabel, Is.EqualTo("Light"));
    }

    [Test]
    public void StatLabels_Damage_BrutalRange()
    {
        // damageMin=10, damageMax=20, power=0 → avg = 15.0 >= 14 → Brutal
        var system = MakeUnderstoodSystem();
        var def = MakeDef(hp: 10, defense: 0, damageMin: 10, damageMax: 20, power: 0);
        var view = system.GetInfoView("test_monster", def);
        Assert.That(view.DamageLabel, Is.EqualTo("Brutal"));
    }

    [Test]
    public void StatLabels_Speed_SluggishRange()
    {
        // speedBonus = 0.4 < 0.6 → Sluggish
        var system = MakeUnderstoodSystem();
        var def = MakeDef(hp: 10, defense: 0, damageMin: 1, damageMax: 2, speedBonus: 0.4);
        var view = system.GetInfoView("test_monster", def);
        Assert.That(view.SpeedLabel, Is.EqualTo("Sluggish"));
    }

    [Test]
    public void StatLabels_Speed_LightningFastRange()
    {
        // speedBonus = 2.0 >= 1.8 → Lightning Fast
        var system = MakeUnderstoodSystem();
        var def = MakeDef(hp: 10, defense: 0, damageMin: 1, damageMax: 2, speedBonus: 2.0);
        var view = system.GetInfoView("test_monster", def);
        Assert.That(view.SpeedLabel, Is.EqualTo("Lightning Fast"));
    }

    // ── InfoView tier gating ──────────────────────────────────────────────────

    [Test]
    public void InfoView_UnknownTier_AllLabelsNull()
    {
        var system = new MonsterKnowledgeSystem();
        var def = MakeDef(hp: 20, defense: 2, damageMin: 3, damageMax: 6);
        var view = system.GetInfoView("unseen", def);

        Assert.That(view.Tier, Is.EqualTo(KnowledgeTier.Unknown));
        Assert.That(view.FactionLabel,    Is.Null);
        Assert.That(view.RoleLabel,       Is.Null);
        Assert.That(view.SpeedLabel,      Is.Null);
        Assert.That(view.DurabilityLabel, Is.Null);
        Assert.That(view.DamageLabel,     Is.Null);
        Assert.That(view.AccuracyLabel,   Is.Null);
        Assert.That(view.EvasionLabel,    Is.Null);
        Assert.That(view.SpecialWarnings, Is.Empty);
        Assert.That(view.AdviceLine,      Is.Null);
    }

    [Test]
    public void InfoView_ObservedTier_OnlyFactionAndRoleAndSpeed()
    {
        var system = new MonsterKnowledgeSystem();
        system.RecordSeen("orc");
        var def = MakeDef(hp: 20, defense: 2, damageMin: 3, damageMax: 6,
            faction: "orc", speedBonus: 1.0);
        var view = system.GetInfoView("orc", def);

        Assert.That(view.Tier, Is.EqualTo(KnowledgeTier.Observed));
        Assert.That(view.FactionLabel, Is.Not.Null);  // "Orc"
        // Combat stats NOT revealed at Observed
        Assert.That(view.DurabilityLabel, Is.Null);
        Assert.That(view.DamageLabel,     Is.Null);
        Assert.That(view.AccuracyLabel,   Is.Null);
        Assert.That(view.EvasionLabel,    Is.Null);
    }

    [Test]
    public void InfoView_BattledTier_IncludesCombatStats()
    {
        var system = new MonsterKnowledgeSystem();
        system.RecordSeen("orc");
        system.RecordEngaged("orc");
        system.RecordEngaged("orc");
        system.RecordEngaged("orc");
        var def = MakeDef(hp: 28, defense: 2, damageMin: 4, damageMax: 6, accuracy: 4, evasion: 1);
        var view = system.GetInfoView("orc", def);

        Assert.That(view.Tier, Is.EqualTo(KnowledgeTier.Battled));
        Assert.That(view.DurabilityLabel, Is.Not.Null);
        Assert.That(view.DamageLabel,     Is.Not.Null);
        Assert.That(view.AccuracyLabel,   Is.Not.Null);
        Assert.That(view.EvasionLabel,    Is.Not.Null);
        // Tier 3 content still locked
        Assert.That(view.SpecialWarnings, Is.Empty);
        Assert.That(view.AdviceLine,      Is.Null);
    }

    [Test]
    public void InfoView_UnderstoodTier_IncludesWarnings()
    {
        var system = new MonsterKnowledgeSystem();
        system.RecordTrait("plague_zombie", "plague_carrier");
        system.RecordSeen("plague_zombie");
        var def = MakeDef(hp: 20, defense: 0, damageMin: 2, damageMax: 5,
            tags: new List<string> { "undead", "plague_carrier" });
        var view = system.GetInfoView("plague_zombie", def);

        Assert.That(view.Tier, Is.EqualTo(KnowledgeTier.Understood));
        Assert.That(view.SpecialWarnings, Has.Count.GreaterThan(0));
        Assert.That(view.AdviceLine, Is.Not.Null);
    }

    // ── Knowledge reset ───────────────────────────────────────────────────────

    [Test]
    public void Knowledge_Reset_ClearsAllEntries()
    {
        var system = new MonsterKnowledgeSystem();
        system.RecordSeen("orc");
        system.RecordKilled("orc");
        Assert.That(system.GetEntry("orc").SeenCount, Is.GreaterThan(0));

        system.Reset();

        var entry = system.GetEntry("orc");
        Assert.That(entry.SeenCount,    Is.EqualTo(0));
        Assert.That(entry.EngagedCount, Is.EqualTo(0));
        Assert.That(entry.KilledCount,  Is.EqualTo(0));
        Assert.That(entry.Tier, Is.EqualTo(KnowledgeTier.Unknown));
    }

    // ── TurnController integration ────────────────────────────────────────────

    /// <summary>
    /// Create a scenario-mode state with a SpeciesTag monster for integration tests.
    /// Player has very high accuracy (99) and damage (999) when killing is required.
    /// </summary>
    private static (GameState state, Entity monster) CreateTaggedState(
        string speciesId = "orc", int monsterHp = 28, bool guaranteedKill = false, int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 6, blocksMovement: true);
        // Use high accuracy/damage when guaranteedKill is needed to avoid RNG misses
        int playerAccuracy = guaranteedKill ? 99 : 2;
        int playerDmgMin   = guaranteedKill ? 999 : 1;
        int playerDmgMax   = guaranteedKill ? 999 : 4;
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: playerAccuracy, evasion: 1, damageMin: playerDmgMin, damageMax: playerDmgMax));
        map.RegisterEntity(player);

        var monster = new Entity(1, "Orc", 4, 6, blocksMovement: true);
        monster.Add(new Fighter(hp: monsterHp, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        monster.Add(new SpeciesTag(speciesId));
        map.RegisterEntity(monster);

        var state = new GameState(player, new List<Entity> { monster }, map, rng);
        // RevealAll so all tiles are visible — matches scenario harness behavior.
        map.RevealAll();

        return (state, monster);
    }

    [Test]
    public void TurnController_AttackEvent_IncrementEngaged()
    {
        var (state, monster) = CreateTaggedState(speciesId: "orc", guaranteedKill: true);
        var action = PlayerAction.Attack(monster);

        TurnController.ProcessTurn(state, action);

        // Attack event emitted → engaged count incremented
        var entry = state.Knowledge.GetEntry("orc");
        Assert.That(entry.EngagedCount, Is.GreaterThan(0),
            "Attack event should have triggered RecordEngaged");
    }

    [Test]
    public void TurnController_DeathEvent_IncrementKilled()
    {
        var (state, _) = CreateTaggedState(speciesId: "orc", monsterHp: 1, guaranteedKill: true);
        var monster = state.Monsters[0];

        // Attack until the monster dies (RNG might miss on first swing — retry up to 10 turns).
        // With damage 999 and HP 1, any hit guarantees a kill. With accuracy 99 vs evasion 1 the
        // HitModel gives 95% chance per swing, but the d20 CombatResolver path means we need
        // enough swings. 10 tries at 65% hit rate = >99.99% kill probability.
        int maxTries = 10;
        for (int i = 0; i < maxTries && state.AliveMonsters.Count > 0; i++)
        {
            TurnController.ProcessTurn(state, PlayerAction.Attack(monster));
        }

        var entry = state.Knowledge.GetEntry("orc");
        Assert.That(entry.KilledCount, Is.EqualTo(1),
            "DeathEvent for monster should have triggered RecordKilled within 10 attack turns");
    }
}
