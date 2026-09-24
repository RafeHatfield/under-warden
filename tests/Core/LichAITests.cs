using UnderWarden.Logic.AI;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Integration tests for Lich AI — tests the full TurnController flow
/// including Soul Bolt charge/resolve cycle, cooldown, and fizzle.
/// </summary>
[TestFixture]
public class LichAITests
{
    /// <summary>
    /// Create a lich entity with all required components for AI to function.
    /// </summary>
    private static Entity CreateLich(int id, int x, int y)
    {
        var lich = new Entity(id, "Lich", x, y, blocksMovement: true);
        lich.Add(new Fighter(hp: 60, damageMin: 3, damageMax: 6,
            strength: 10, dexterity: 14, constitution: 14, accuracy: 5, evasion: 3));
        lich.Add(new AiComponent
        {
            AiType = "lich",
            Faction = "undead",
            Tags = ["undead", "high_undead", "caster", "no_flesh"],
        });
        lich.Add(new NecromancerAiComponent
        {
            RaiseRange = 5,
            RaiseCooldown = 4,
            DangerRadius = 2,
            PreferredDistanceMin = 4,
            PreferredDistanceMax = 7,
        });
        lich.Add(new LichAiComponent
        {
            SoulBoltRange = 7,
            SoulBoltDamagePct = 0.18,
            SoulBoltCooldownTurns = 8,
            CommandTheDeadRadius = 6,
            DeathSiphonRadius = 6,
        });
        return lich;
    }

    private static (GameState state, Entity player, Entity lich) CreateArena(
        int lichX = 8, int lichY = 5, int playerX = 3, int playerY = 5)
    {
        var map = GameMap.CreateArena(20, 20);
        // Compute FOV so lich tile is visible (used as LOS proxy)
        map.RevealAll();

        var player = new Entity(0, "Player", playerX, playerY, blocksMovement: true);
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 14, constitution: 10,
            accuracy: 5, evasion: 1, damageMin: 5, damageMax: 8));
        player.Add(new Inventory());

        var lich = CreateLich(1, lichX, lichY);

        map.RegisterEntity(player);
        map.RegisterEntity(lich);

        var state = new GameState(player, [lich], map, new SeededRandom(1337), turnLimit: 200);
        return (state, player, lich);
    }

    // ─── Soul Bolt 2-turn cycle via TurnController ───────────────────────

    [Test]
    public void LichAI_SoulBolt_ChargeAndResolve_TwoTurns()
    {
        var (state, player, lich) = CreateArena(lichX: 8, playerX: 3);

        // Turn 1: player waits, lich should charge Soul Bolt
        var result1 = TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(result1.Events.Any(e => e is ChannelEvent ce && ce.AbilityName == "Soul Bolt"),
            Is.True, "Turn 1: lich should emit ChannelEvent for Soul Bolt");

        int hpBefore = player.Require<Fighter>().Hp;

        // Turn 2: player waits, lich should resolve Soul Bolt
        var result2 = TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(result2.Events.Any(e => e is SoulBoltEvent),
            Is.True, "Turn 2: lich should emit SoulBoltEvent");

        var soulBoltEvent = result2.Events.OfType<SoulBoltEvent>().FirstOrDefault();
        Assert.That(soulBoltEvent, Is.Not.Null);
        Assert.That(soulBoltEvent!.Damage, Is.EqualTo(18)); // ceil(0.18 * 100)
        Assert.That(player.Require<Fighter>().Hp, Is.EqualTo(hpBefore - 18));
    }

    [Test]
    public void LichAI_OnCooldown_FallsToNecromancer()
    {
        var (state, player, lich) = CreateArena();
        var lichComp = lich.Get<LichAiComponent>()!;

        // Manually set cooldown so lich can't charge
        lichComp.SoulBoltCooldownRemaining = 5;

        // Turn: player waits, lich should NOT charge (on cooldown)
        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(result.Events.Any(e => e is ChannelEvent), Is.False,
            "Lich on cooldown should not channel");
        Assert.That(result.Events.Any(e => e is SoulBoltEvent), Is.False,
            "Lich on cooldown should not fire Soul Bolt");
    }

    [Test]
    public void LichAI_Decide_OffCooldown_PlayerInRange_StartsCharge()
    {
        var (state, _, lich) = CreateArena(lichX: 6, playerX: 3);

        // Pre-alert the lich (normally done by BasicMonsterAI.UpdateAwareness)
        lich.Add(new AlertedState());

        var action = LichAI.Decide(lich, state);

        Assert.That(action.Kind, Is.EqualTo(MonsterAction.ActionKind.Channel));
        Assert.That(action.AbilityName, Is.EqualTo("Soul Bolt"));
        Assert.That(lich.Has<ChargingSoulBoltEffect>(), Is.True);
    }

    [Test]
    public void LichAI_Charging_PlayerInRange_ResolvesSoulBolt()
    {
        var (state, player, lich) = CreateArena(lichX: 6, playerX: 3);
        lich.Add(new AlertedState());
        lich.Add(new ChargingSoulBoltEffect()); // simulate charge from previous turn

        var action = LichAI.Decide(lich, state);

        Assert.That(action.Kind, Is.EqualTo(MonsterAction.ActionKind.SoulBolt));
        Assert.That(action.Target, Is.EqualTo(player));
        Assert.That(lich.Has<ChargingSoulBoltEffect>(), Is.False, "Charge should be consumed");
    }

    [Test]
    public void LichAI_Charging_PlayerOutOfRange_Fizzles()
    {
        var (state, player, lich) = CreateArena(lichX: 15, playerX: 3);
        // Distance 12 > Soul Bolt range 7
        lich.Add(new AlertedState());
        lich.Add(new ChargingSoulBoltEffect());

        var action = LichAI.Decide(lich, state);

        // Should NOT be SoulBolt (fizzled)
        Assert.That(action.Kind, Is.Not.EqualTo(MonsterAction.ActionKind.SoulBolt),
            "Out-of-range bolt should fizzle");
        Assert.That(lich.Has<ChargingSoulBoltEffect>(), Is.False, "Charge should still be consumed on fizzle");
    }

    [Test]
    public void MonsterFactory_Lich_HasBothComponents()
    {
        var factory = CreateFactory();
        var lich = factory.Create("lich")!;

        Assert.That(lich.Get<LichAiComponent>(), Is.Not.Null, "Lich must have LichAiComponent");
        Assert.That(lich.Get<NecromancerAiComponent>(), Is.Not.Null, "Lich must have NecromancerAiComponent");
        Assert.That(lich.Get<AiComponent>()?.AiType, Is.EqualTo("lich"));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string FindEntitiesYaml()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var path = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", "entities.yaml"));
        if (File.Exists(path)) return path;
        path = Path.GetFullPath(Path.Combine(testDir, "config", "entities.yaml"));
        if (File.Exists(path)) return path;
        throw new FileNotFoundException($"entities.yaml not found.");
    }

    private static MonsterFactory CreateFactory()
    {
        var loader = new ContentLoader();
        var bundle = loader.LoadAllFromFile(FindEntitiesYaml());
        var entityFactory = new EntityFactory();
        var itemFactory = new ItemFactory(bundle.Items, entityFactory);
        return new MonsterFactory(bundle.Monsters, entityFactory, itemFactory);
    }
}
