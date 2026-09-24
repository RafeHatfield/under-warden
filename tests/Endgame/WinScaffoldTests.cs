using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Endgame;
using NUnit.Framework;

namespace UnderWarden.Tests.Endgame;

/// <summary>
/// TASK-008 win scaffold (layout-independent parts): the dungeon-mode victory state, the
/// Weighing-floor gate, and the ASCII arena loader. The authored arena layout itself is content.
/// </summary>
[TestFixture]
public class WinScaffoldTests
{
    // ── Weighing-floor gate ───────────────────────────────────────────────────

    [TestCase(24, false)]
    [TestCase(25, true)]
    [TestCase(26, false)]
    public void IsWeighingFloor_OnlyFinalDepth(int depth, bool expected)
    {
        Assert.That(WeighingConstants.IsWeighingFloor(depth), Is.EqualTo(expected));
    }

    // ── Dungeon victory state on GameState ────────────────────────────────────

    private static GameState DungeonState()
    {
        var map = GameMap.CreateArena(8, 8);
        var player = new Entity(0, "Player", 4, 4, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 12, dexterity: 12, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(player);
        return new GameState(player, new System.Collections.Generic.List<Entity>(), map,
            new SeededRandom(1337), turnLimit: 500) { IsDungeonMode = true };
    }

    [Test]
    public void FreshDungeonRun_IsOngoing_NotGameOver()
    {
        var state = DungeonState();
        Assert.That(state.Ending, Is.EqualTo(EndingType.None));
        Assert.That(state.IsGameOver, Is.False);
        Assert.That(state.IsDungeonVictory, Is.False);
    }

    [TestCase(EndingType.CleanAudit, true)]
    [TestCase(EndingType.Theft, true)]
    [TestCase(EndingType.Swap, true)]
    public void WinningEnding_EndsRunAsVictory(EndingType ending, bool victory)
    {
        var state = DungeonState();
        state.Ending = ending;
        Assert.That(state.IsGameOver, Is.True, "a resolved ending ends the run");
        Assert.That(state.IsDungeonVictory, Is.EqualTo(victory));
    }

    [TestCase(EndingType.LossGuardians)]
    [TestCase(EndingType.LossDebt)]
    [TestCase(EndingType.LossRefused)]
    public void LosingEnding_EndsRun_ButIsNotVictory(EndingType ending)
    {
        var state = DungeonState();
        state.Ending = ending;
        Assert.That(state.IsGameOver, Is.True, "a loss (including the chosen refusal) ends the run");
        Assert.That(state.IsDungeonVictory, Is.False);
    }

    [Test]
    public void Refused_IsNonDeathLoss_PlayerStillAlive()
    {
        var state = DungeonState();
        state.Ending = EndingType.LossRefused;
        Assert.That(state.PlayerFighter.IsAlive, Is.True, "refusal is a chosen loss, not a death");
        Assert.That(state.IsGameOver, Is.True, "yet the run still ends");
    }

    // ── ASCII arena loader ────────────────────────────────────────────────────

    [Test]
    public void ArenaLoader_BuildsMap_AndCapturesAnchors()
    {
        // P player start, W/B guardian + debt anchors, F fall-back; '#' walls, '.' floor.
        var arena = WeighingArenaLoader.FromAscii(new[]
        {
            "#######",
            "#..W..#",
            "#.....#",
            "#P.F.B#",
            "#######",
        });

        Assert.That(arena.Map.Width, Is.EqualTo(7));
        Assert.That(arena.Map.Height, Is.EqualTo(5));

        // Borders are walls; interior is walkable.
        Assert.That(arena.Map.IsWalkable(0, 0), Is.False);
        Assert.That(arena.Map.IsWalkable(1, 1), Is.True);

        Assert.That(arena.FirstAnchor("player_start"), Is.EqualTo((1, 3)));
        Assert.That(arena.FirstAnchor("guardian_warden"), Is.EqualTo((3, 1)));
        Assert.That(arena.FirstAnchor("debt"), Is.EqualTo((5, 3)));
        Assert.That(arena.FirstAnchor("ally_fallback"), Is.EqualTo((3, 3)));
        // Anchor tiles are themselves walkable floor.
        Assert.That(arena.Map.IsWalkable(1, 3), Is.True);
    }

    // ── The authored Tribunal Hall (concrete anchor positions) ───────────────

    [Test]
    public void TribunalHall_HasAllAnchors_InCourtroomGeometry()
    {
        var arena = WeighingArenaDefinition.Build();

        Assert.That(arena.Map.Width, Is.EqualTo(15));
        Assert.That(arena.Map.Height, Is.EqualTo(19));

        var underWarden = arena.FirstAnchor("under_warden")!.Value;
        var debt = arena.FirstAnchor("debt")!.Value;
        var warden = arena.FirstAnchor("guardian_warden")!.Value;
        var oathkeeper = arena.FirstAnchor("guardian_oathkeeper")!.Value;
        var assembly = arena.FirstAnchor("guardian_assembly")!.Value;
        var auditor = arena.FirstAnchor("guardian_auditor")!.Value;
        var player = arena.FirstAnchor("player_start")!.Value;
        var fallback = arena.AnchorsFor("ally_fallback");

        // Exact positions (y increases southward) — the concrete grid the audit stages against.
        Assert.That(underWarden, Is.EqualTo((7, 1)));
        Assert.That(debt, Is.EqualTo((7, 2)));
        Assert.That(assembly, Is.EqualTo((4, 5)));
        Assert.That(auditor, Is.EqualTo((10, 5)));
        Assert.That(warden, Is.EqualTo((4, 9)));
        Assert.That(oathkeeper, Is.EqualTo((10, 9)));
        Assert.That(player, Is.EqualTo((7, 13)));
        Assert.That(fallback, Is.EquivalentTo(new[] { (6, 16), (8, 16) }));

        // Courtroom geometry: Under-Warden + Debt at the head (north); player in the well (south of
        // all Guardians); allies fall back behind the player (further south); south rank nearer the
        // player than the north rank.
        Assert.That(underWarden.Item2, Is.LessThan(debt.Item2), "Under-Warden presides north of the Debt");
        Assert.That(debt.Item2, Is.LessThan(assembly.Item2), "Debt is north of all Guardians");
        Assert.That(assembly.Item2, Is.LessThan(warden.Item2), "north rank (Assembly/Auditor) is north of the south rank");
        Assert.That(warden.Item2, Is.LessThan(player.Item2), "the player stands south of all Guardians");
        Assert.That(player.Item2, Is.LessThan(fallback[0].Item2), "allies fall back behind (south of) the player");

        // All anchors are walkable floor.
        foreach (var (x, y) in new[] { debt, warden, oathkeeper, assembly, auditor, player, fallback[0], fallback[1] })
            Assert.That(arena.Map.IsWalkable(x, y), Is.True, $"anchor ({x},{y}) should be floor");
    }

    [Test]
    public void ArenaLoader_MultipleAnchorsOfSameKind_AllCaptured()
    {
        // Two ally fall-back tiles.
        var arena = WeighingArenaLoader.FromAscii(new[]
        {
            "#####",
            "#F.F#",
            "#####",
        });

        Assert.That(arena.AnchorsFor("ally_fallback"), Has.Count.EqualTo(2));
        Assert.That(arena.FirstAnchor("missing_anchor"), Is.Null);
        Assert.That(arena.AnchorsFor("missing_anchor"), Is.Empty);
    }
}
