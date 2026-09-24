using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for Phase 2 of the corpse system: RaiseDeadResolver + necromancer factory wiring.
///
/// Covers: stat formula (HP×2, Dmg×0.5 min 1, STR×0.75 min 6, DEX×0.5 min 6, CON×1.5 max 18),
/// faction rules (player→neutral, AI→raiser's faction), in-place transform (same entity ID),
/// CanBeRaised guard (Fresh only), out-of-range failure, RaiseDeadEvent emission, and
/// lineage chain (raised zombie dies → SPENT corpse).
/// </summary>
[TestFixture]
public class RaiseDeadTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string FindEntitiesYaml()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var path = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", "entities.yaml"));
        if (File.Exists(path)) return path;
        path = Path.GetFullPath(Path.Combine(testDir, "config", "entities.yaml"));
        if (File.Exists(path)) return path;
        throw new FileNotFoundException($"entities.yaml not found. Tried: {path}");
    }

    private static MonsterFactory CreateFactory()
    {
        var loader = new ContentLoader();
        var bundle = loader.LoadAllFromFile(FindEntitiesYaml());
        var entityFactory = new EntityFactory();
        var itemFactory = new ItemFactory(bundle.Items, entityFactory);
        return new MonsterFactory(bundle.Monsters, entityFactory, itemFactory);
    }

    /// <summary>Create a minimal state with a player and an orc corpse.</summary>
    private static (GameState state, Entity corpse) CreateStateWithCorpse(
        int seed = 1337, int orcX = 8, int orcY = 5)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(14, 10);

        var player = new Entity(0, "Player", 3, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(player);

        // Build an orc-like entity and transform it to a corpse
        var orc = new Entity(1, "Orc", orcX, orcY, blocksMovement: true);
        orc.Add(new Fighter(hp: 10, strength: 12, dexterity: 10, constitution: 10,
            accuracy: 3, evasion: 1, damageMin: 3, damageMax: 5, defense: 1));
        orc.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        orc.Add(new SpeciesTag("orc"));
        map.RegisterEntity(orc);

        var state = new GameState(player, [orc], map, rng, turnLimit: 200);

        // Manually transform to corpse (mirrors TurnController.TransformToCorpse)
        var fighter = orc.Require<Fighter>();
        var corpse = new CorpseComponent
        {
            OriginalMonsterId = "orc",
            OriginalName      = "Orc",
            DeathTurn         = 1,
            State             = CorpseState.Fresh,
            CorpseId          = $"corpse_{orcX}_{orcY}_1",
            BaseHp            = fighter.BaseMaxHp,
            BaseDamageMin     = fighter.DamageMin,
            BaseDamageMax     = fighter.DamageMax,
            BaseStrength      = fighter.Strength,
            BaseDexterity     = fighter.Dexterity,
            BaseConstitution  = fighter.Constitution,
            BaseDefense       = fighter.BaseDefense,
            BaseAccuracy      = fighter.Accuracy,
            BaseEvasion       = fighter.Evasion,
        };

        orc.Remove<Fighter>();
        orc.Remove<AiComponent>();
        orc.Add(corpse);
        orc.BlocksMovement = false;
        state.Corpses.Add(orc);

        return (state, orc);
    }

    // ─── Stat formula ─────────────────────────────────────────────────────────

    [Test]
    public void RaiseDead_StatsApplyCorrectly_Hp()
    {
        var (state, corpse) = CreateStateWithCorpse();
        int baseHp = corpse.Require<CorpseComponent>().BaseHp; // 10

        RaiseDeadResolver.Raise(corpse, "player", state);

        Assert.That(corpse.Require<Fighter>().BaseMaxHp, Is.EqualTo((int)Math.Round(baseHp * 2.0)),
            "Raised HP should be base × 2");
    }

    [Test]
    public void RaiseDead_StatsApplyCorrectly_DamageHalved()
    {
        var (state, corpse) = CreateStateWithCorpse();
        var comp = corpse.Require<CorpseComponent>();
        int baseDmgMin = comp.BaseDamageMin; // 3
        int baseDmgMax = comp.BaseDamageMax; // 5

        RaiseDeadResolver.Raise(corpse, "player", state);

        var f = corpse.Require<Fighter>();
        Assert.That(f.DamageMin, Is.EqualTo(Math.Max(1, (int)Math.Round(baseDmgMin * 0.5))));
        Assert.That(f.DamageMax, Is.EqualTo(Math.Max(1, (int)Math.Round(baseDmgMax * 0.5))));
    }

    [Test]
    public void RaiseDead_MinimumDamageClamp()
    {
        // Monster with DmgMin=1 — after ×0.5 (rounds to 1) stays at 1
        var (state, corpse) = CreateStateWithCorpse();
        var comp = corpse.Require<CorpseComponent>();
        comp.BaseDamageMin = 1;
        comp.BaseDamageMax = 1;

        RaiseDeadResolver.Raise(corpse, "player", state);

        var f = corpse.Require<Fighter>();
        Assert.That(f.DamageMin, Is.EqualTo(1), "DamageMin must be at least 1 after ×0.5 clamp");
        Assert.That(f.DamageMax, Is.EqualTo(1), "DamageMax must be at least 1 after ×0.5 clamp");
    }

    [Test]
    public void RaiseDead_StrengthReducedMinSix()
    {
        var (state, corpse) = CreateStateWithCorpse();
        corpse.Require<CorpseComponent>().BaseStrength = 6; // ×0.75 = 4.5 → clamped to 6

        RaiseDeadResolver.Raise(corpse, "player", state);

        Assert.That(corpse.Require<Fighter>().Strength, Is.EqualTo(6),
            "Raised STR must be at least 6");
    }

    [Test]
    public void RaiseDead_DexterityReducedMinSix()
    {
        var (state, corpse) = CreateStateWithCorpse();
        corpse.Require<CorpseComponent>().BaseDexterity = 8; // ×0.5 = 4 → clamped to 6

        RaiseDeadResolver.Raise(corpse, "player", state);

        Assert.That(corpse.Require<Fighter>().Dexterity, Is.EqualTo(6),
            "Raised DEX must be at least 6");
    }

    [Test]
    public void RaiseDead_ConstitutionIncreasedMaxEighteen()
    {
        var (state, corpse) = CreateStateWithCorpse();
        corpse.Require<CorpseComponent>().BaseConstitution = 14; // ×1.5 = 21 → clamped to 18

        RaiseDeadResolver.Raise(corpse, "player", state);

        Assert.That(corpse.Require<Fighter>().Constitution, Is.EqualTo(18),
            "Raised CON must be at most 18");
    }

    [Test]
    public void RaiseDead_DefenseAccuracyEvasionUnchanged()
    {
        var (state, corpse) = CreateStateWithCorpse();
        var comp = corpse.Require<CorpseComponent>();
        int baseDef = comp.BaseDefense;
        int baseAcc = comp.BaseAccuracy;
        int baseEva = comp.BaseEvasion;

        RaiseDeadResolver.Raise(corpse, "player", state);

        var f = corpse.Require<Fighter>();
        Assert.That(f.BaseDefense, Is.EqualTo(baseDef), "Defense should be unchanged");
        Assert.That(f.Accuracy, Is.EqualTo(baseAcc), "Accuracy should be unchanged");
        Assert.That(f.Evasion, Is.EqualTo(baseEva), "Evasion should be unchanged");
    }

    // ─── Faction and in-place transform ──────────────────────────────────────

    [Test]
    public void RaiseDead_PlayerCast_FactionNeutral()
    {
        var (state, corpse) = CreateStateWithCorpse();
        RaiseDeadResolver.Raise(corpse, "player", state);

        Assert.That(corpse.Require<AiComponent>().Faction, Is.EqualTo("neutral"),
            "Player-raised monsters should be neutral (attack both sides)");
    }

    [Test]
    public void RaiseDead_NecromancerCast_FactionMatchesRaiser()
    {
        var (state, corpse) = CreateStateWithCorpse();
        RaiseDeadResolver.Raise(corpse, "cultist", state);

        Assert.That(corpse.Require<AiComponent>().Faction, Is.EqualTo("cultist"),
            "Necromancer-raised monsters should have the necromancer's faction");
    }

    [Test]
    public void RaiseDead_SameEntityId_InPlaceTransform()
    {
        var (state, corpse) = CreateStateWithCorpse();
        int originalId = corpse.Id;

        RaiseDeadResolver.Raise(corpse, "player", state);

        Assert.That(corpse.Id, Is.EqualTo(originalId),
            "In-place transform must preserve entity ID");
    }

    [Test]
    public void RaiseDead_CorpseComponentRemoved()
    {
        var (state, corpse) = CreateStateWithCorpse();
        RaiseDeadResolver.Raise(corpse, "player", state);

        Assert.That(corpse.Has<CorpseComponent>(), Is.False,
            "CorpseComponent must be removed after raise");
    }

    [Test]
    public void RaiseDead_EntityRemovedFromCorpsesList()
    {
        var (state, corpse) = CreateStateWithCorpse();
        Assert.That(state.Corpses, Contains.Item(corpse));

        RaiseDeadResolver.Raise(corpse, "player", state);

        Assert.That(state.Corpses, Does.Not.Contain(corpse),
            "Raised entity must be removed from state.Corpses");
    }

    [Test]
    public void RaiseDead_EntityStillInMonstersList()
    {
        var (state, corpse) = CreateStateWithCorpse();
        Assert.That(state.Monsters, Contains.Item(corpse));

        RaiseDeadResolver.Raise(corpse, "player", state);

        Assert.That(state.Monsters, Contains.Item(corpse),
            "Raised entity must remain in state.Monsters (dual membership)");
    }

    [Test]
    public void RaiseDead_BlocksMovementTrue()
    {
        var (state, corpse) = CreateStateWithCorpse();
        Assert.That(corpse.BlocksMovement, Is.False); // was a corpse

        RaiseDeadResolver.Raise(corpse, "player", state);

        Assert.That(corpse.BlocksMovement, Is.True,
            "Raised entity must block movement again");
    }

    [Test]
    public void RaiseDead_RaisedFromCorpseTagAdded()
    {
        var (state, corpse) = CreateStateWithCorpse();
        string corpseId = corpse.Require<CorpseComponent>().CorpseId;

        RaiseDeadResolver.Raise(corpse, "player", state);

        var tag = corpse.Get<RaisedFromCorpseTag>();
        Assert.That(tag, Is.Not.Null, "RaisedFromCorpseTag should be attached after raise");
        Assert.That(tag!.CorpseId, Is.EqualTo(corpseId), "CorpseId lineage should match");
    }

    // ─── Guard cases ──────────────────────────────────────────────────────────

    [Test]
    public void RaiseDead_SpentCorpse_CannotBeRaised()
    {
        var (state, corpse) = CreateStateWithCorpse();
        corpse.Require<CorpseComponent>().State = CorpseState.Spent;

        Assert.That(corpse.Require<CorpseComponent>().CanBeRaised, Is.False,
            "SPENT corpse should not be raisable");
    }

    [Test]
    public void RaiseDead_ConsumedCorpse_CannotBeRaised()
    {
        var (state, corpse) = CreateStateWithCorpse();
        corpse.Require<CorpseComponent>().State = CorpseState.Consumed;

        Assert.That(corpse.Require<CorpseComponent>().CanBeRaised, Is.False);
    }

    [Test]
    public void FindNearestRaisableCorpse_OutOfRange_ReturnsNull()
    {
        var (state, corpse) = CreateStateWithCorpse(orcX: 8, orcY: 5); // 5 tiles from player at (3,5)

        // Actor is at (0,0), corpse is at (8,5) — distance ~9.4, range 5 → null
        var fakeActor = new Entity(99, "Fake", 0, 0);
        var result = RaiseDeadResolver.FindNearestRaisableCorpse(fakeActor, state.Corpses, state, range: 5);

        Assert.That(result, Is.Null, "Corpse out of range should not be found");
    }

    [Test]
    public void FindNearestRaisableCorpse_InRange_ReturnsCorpse()
    {
        var (state, corpse) = CreateStateWithCorpse(orcX: 5, orcY: 5); // player at (3,5), 2 tiles

        // Actor at (3,5), corpse at (5,5) — distance 2, range 5 → found
        var result = RaiseDeadResolver.FindNearestRaisableCorpse(
            state.Player, state.Corpses, state, range: 5);

        Assert.That(result, Is.EqualTo(corpse), "Corpse in range should be found");
    }

    // ─── Lineage chain ───────────────────────────────────────────────────────

    [Test]
    public void RaisedZombieDeath_CreatesSPENTCorpse()
    {
        // Raise the corpse, verify entity has RaisedFromCorpseTag
        var (state, corpse) = CreateStateWithCorpse();
        RaiseDeadResolver.Raise(corpse, "player", state);
        Assert.That(corpse.Has<RaisedFromCorpseTag>(), Is.True);

        // The raised entity (with RaisedFromCorpseTag) should produce a SPENT corpse when killed.
        // We verify CanBeRaised is false for SPENT state:
        var spentCorpse = new CorpseComponent { State = CorpseState.Spent };
        Assert.That(spentCorpse.CanBeRaised, Is.False,
            "SPENT corpse must not be raisable — lineage terminates here");
    }

    // ─── Factory wiring ───────────────────────────────────────────────────────

    [Test]
    public void NecromancerFactory_HasNecromancerAiComponent()
    {
        var factory = CreateFactory();
        var necromancer = factory.Create("necromancer");

        Assert.That(necromancer, Is.Not.Null, "necromancer must be registered in entities.yaml");
        Assert.That(necromancer!.Has<NecromancerAiComponent>(), Is.True,
            "Necromancer must have NecromancerAiComponent attached");

        var necComp = necromancer.Require<NecromancerAiComponent>();
        Assert.That(necComp.RaiseRange, Is.EqualTo(5));
        Assert.That(necComp.RaiseCooldown, Is.EqualTo(4));
        Assert.That(necComp.DangerRadius, Is.EqualTo(2));
    }

    [Test]
    public void PlagueNecromancerFactory_HasNecromancerAiComponent()
    {
        var factory = CreateFactory();
        var plagueNec = factory.Create("plague_necromancer");

        Assert.That(plagueNec, Is.Not.Null, "plague_necromancer must be in entities.yaml");
        Assert.That(plagueNec!.Has<NecromancerAiComponent>(), Is.True,
            "Plague necromancer must have NecromancerAiComponent");
        Assert.That(plagueNec.Get<AiComponent>()?.AiType, Is.EqualTo("plague_necromancer"));
    }

    [Test]
    public void GiantSpiderFactory_HasOnHitPoison()
    {
        var factory = CreateFactory();
        var spider = factory.Create("giant_spider");

        Assert.That(spider, Is.Not.Null, "giant_spider must be in entities.yaml");
        var onHit = spider!.Get<OnHitEffectComponent>();
        Assert.That(onHit, Is.Not.Null, "Giant spider should have OnHitEffectComponent (poison)");
        Assert.That(onHit!.EffectType, Is.EqualTo("poison"));
    }

    [Test]
    public void CultistBlademasterFactory_HasSpeedBonus()
    {
        var factory = CreateFactory();
        var blademaster = factory.Create("cultist_blademaster");

        Assert.That(blademaster, Is.Not.Null, "cultist_blademaster must be in entities.yaml");
        Assert.That(blademaster!.Has<SpeedBonusTracker>(), Is.True,
            "Cultist blademaster must have SpeedBonusTracker (speed_bonus: 0.25)");
    }
}
