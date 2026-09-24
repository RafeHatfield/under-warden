using System;
using System.Collections.Generic;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Endgame;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tier 0 review-harness geometry tests (ART-BIBLE-v0 §13.1).
///
/// The junction is the load-bearing part of the review corridor: the blind critic is asked which
/// way they would walk, and a straight corridor cannot answer that. So HasJunction is an
/// instrument, and ART-LOOP-PROCESS-v0 §4 / bible §13.5 apply to it — its pass does not count
/// until it has been shown to go red. Both directions are asserted here, and the specific
/// false-positive that actually occurred (a corridor carved three tiles wide) has its own test.
/// </summary>
[TestFixture]
public class CorridorReviewSceneBuilderTests
{
    private const string TrunkAndBranch = @"{
        ""name"": ""t"", ""width"": 17, ""height"": 21,
        ""player"": { ""x"": 8, ""y"": 14 },
        ""carve"": [
            { ""x0"": 8, ""y0"": 3,  ""x1"": 8,  ""y1"": 18 },
            { ""x0"": 2, ""y0"": 11, ""x1"": 14, ""y1"": 11 }
        ] }";

    private const string StraightOnly = @"{
        ""name"": ""t"", ""width"": 17, ""height"": 21,
        ""player"": { ""x"": 8, ""y"": 14 },
        ""carve"": [ { ""x0"": 8, ""y0"": 3, ""x1"": 8, ""y1"": 18 } ] }";

    private const string ThreeWideTrunk = @"{
        ""name"": ""t"", ""width"": 17, ""height"": 21,
        ""player"": { ""x"": 8, ""y"": 14 },
        ""carve"": [ { ""x0"": 7, ""y0"": 3, ""x1"": 9, ""y1"": 18 } ] }";

    [Test]
    public void HasJunction_IsTrue_ForCrossedOneWideCorridors()
    {
        var state = CorridorReviewSceneBuilder.Build(
            CorridorReviewSceneBuilder.ParseSpecJson(TrunkAndBranch));

        Assert.That(CorridorReviewSceneBuilder.HasJunction(state.Map, out var at), Is.True);
        Assert.That(at, Is.EqualTo((8, 11)), "junction should be where the two carves cross");
    }

    /// <summary>The instrument going red: a corridor with no branch has no junction.</summary>
    [Test]
    public void HasJunction_IsFalse_ForAStraightCorridor()
    {
        var state = CorridorReviewSceneBuilder.Build(
            CorridorReviewSceneBuilder.ParseSpecJson(StraightOnly));

        Assert.That(CorridorReviewSceneBuilder.HasJunction(state.Map, out _), Is.False,
            "a straight corridor cannot answer 'which way would you walk'");
    }

    /// <summary>
    /// The false positive that actually happened. The first Tier 0 capture carved a 3-wide trunk;
    /// the original check counted open orthogonal neighbours only, so every cell in that wide
    /// span looked like a junction and it reported "junction=YES at (8,4)" — the top of a
    /// straight corridor. Regression-locked: a wide span is not a junction.
    /// </summary>
    [Test]
    public void HasJunction_IsFalse_ForAThreeWideCorridor_TheOriginalFalsePositive()
    {
        var state = CorridorReviewSceneBuilder.Build(
            CorridorReviewSceneBuilder.ParseSpecJson(ThreeWideTrunk));

        Assert.That(CorridorReviewSceneBuilder.HasJunction(state.Map, out var at), Is.False,
            $"a 3-wide corridor is a room, not a junction (reported {at})");
    }

    [Test]
    public void Build_EnclosesTheCorridorInSolidWall()
    {
        var state = CorridorReviewSceneBuilder.Build(
            CorridorReviewSceneBuilder.ParseSpecJson(TrunkAndBranch));
        var map = state.Map;

        for (int x = 0; x < map.Width; x++)
        {
            Assert.That(map.IsWalkable(x, 0), Is.False, $"top border ({x},0) must be wall");
            Assert.That(map.IsWalkable(x, map.Height - 1), Is.False, "bottom border must be wall");
        }
        for (int y = 0; y < map.Height; y++)
        {
            Assert.That(map.IsWalkable(0, y), Is.False, "left border must be wall");
            Assert.That(map.IsWalkable(map.Width - 1, y), Is.False, "right border must be wall");
        }
    }

    [Test]
    public void ParseSpec_RejectsAnEmptyCarveList()
    {
        const string noCarve = @"{ ""name"": ""t"", ""width"": 9, ""height"": 9,
            ""player"": { ""x"": 4, ""y"": 4 }, ""carve"": [] }";

        Assert.That(() => CorridorReviewSceneBuilder.ParseSpecJson(noCarve),
            Throws.InvalidOperationException, "solid rock is not a corridor");
    }

    // ── The review scene carries no losable game state ──────────────────────────────────────
    //
    // Regression-locking the "player dies on the first step" defect. The player never actually
    // died — IsAlive stayed true. The builder was constructed with turnLimit: 1, so the first
    // step took TurnCount to 1 >= 1 and IsGameOver went true by the turn-limit clause. On device
    // that surfaces as the end-of-run overlay, which reads as a death.
    //
    // These assert the INVARIANT rather than the old number: a review surface that can enter a
    // game-over state can capture a death overlay or a changed HUD, and the determinism control
    // would read that as a difference in the art (LOOP-PROCESS §2.3).

    [Test]
    public void Build_OneStepDoesNotEndTheScene_TheReportedDefect()
    {
        var state = CorridorReviewSceneBuilder.Build(
            CorridorReviewSceneBuilder.ParseSpecJson(TrunkAndBranch));

        Assert.That(state.IsGameOver, Is.False, "the scene must not start over");
        state.TurnCount = 1;
        Assert.That(state.IsGameOver, Is.False,
            "one step ended the review scene — this is the exact reported defect");
    }

    [Test]
    public void Build_CannotBeWalkedIntoAGameOver()
    {
        var state = CorridorReviewSceneBuilder.Build(
            CorridorReviewSceneBuilder.ParseSpecJson(TrunkAndBranch));

        // Far more turns than any review would ever take, plus the pathological case.
        foreach (var turn in new[] { 1, 2, 10, 1_000, 100_000, 10_000_000 })
        {
            state.TurnCount = turn;
            Assert.That(state.IsGameOver, Is.False, $"scene ended at turn {turn}");
        }
    }

    [Test]
    public void Build_HasNoLossConditionsAtAll()
    {
        var state = CorridorReviewSceneBuilder.Build(
            CorridorReviewSceneBuilder.ParseSpecJson(TrunkAndBranch));

        Assert.Multiple(() =>
        {
            Assert.That(state.TurnLimit, Is.EqualTo(int.MaxValue), "a turn limit is a loss condition");
            Assert.That(state.Monsters, Is.Empty, "nothing may exist that can deal damage");
            Assert.That(state.Ending, Is.EqualTo(EndingType.None), "no ending may be pre-set");
            Assert.That(state.PlayerFighter.IsAlive, Is.True);
            // ⚠ AMENDED for the tier-two props pass. This used to read "props are not part of a
            // floor/wall review", which was true of every scene that existed when it was
            // written and is still true of every FLOOR AND WALL scene — TrunkAndBranch declares
            // no props and gets none. What changed is that props became a SUBJECT of review, so
            // the invariant this test is named for is stated on what it was always about:
            // nothing in the scene may carry a loss condition. A prop is furniture, not a hazard.
            Assert.That(state.Props, Is.Empty,
                        "a spec that declares no props must produce none");
        });
    }

    /// <summary>
    /// The carve produces floor and wall only. A stair tile would let a walker trigger a descent
    /// mid-review and leave the scene being judged.
    /// </summary>
    [Test]
    public void Build_ContainsNoStairsOrDoors()
    {
        var state = CorridorReviewSceneBuilder.Build(
            CorridorReviewSceneBuilder.ParseSpecJson(TrunkAndBranch));
        var map = state.Map;

        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                var kind = map.GetTileKind(x, y);
                Assert.That(kind, Is.EqualTo(TileKind.Wall).Or.EqualTo(TileKind.Floor),
                    $"({x},{y}) is {kind} — the review corridor is floor and wall only");
            }
        }
    }

    [Test]
    public void Build_PlacesThePlayerWhereTheSpecSaid()
    {
        var state = CorridorReviewSceneBuilder.Build(
            CorridorReviewSceneBuilder.ParseSpecJson(TrunkAndBranch));

        Assert.That((state.Player.X, state.Player.Y), Is.EqualTo((8, 14)));
    }

    // ── §12.2 PROPS AT READABILITY SCALE ─────────────────────────────────────────────────────
    //
    // Rafe's props walk FAILED on identifiability — "small, unrecognizable except the fire" —
    // and the ruling authors props at the size at which they can be named: a prop fills most of
    // its cell, and a large object may span 1x2 or 2x2 where the fiction allows.
    //
    // ⚠ THE FIRST VERSION OF THIS FIXTURE SEALED THE CORRIDOR AND EVERY TEST STILL PASSED.
    // It seated a 1x2 blocking marker in the one-wide trunk. Walkable cells reachable from the
    // player went from 28 to FIVE, the junction ended up on the far side of a stone wall, and
    // HasJunction still answered YES because it does not ask what the player can reach. That is
    // why the scene below has a BAY — three cells deep — and why `Props_ThatSealTheCorridor...`
    // exists. A prop the walker cannot reach is not a prop the gate can judge.

    /// <summary>
    /// Trunk and branch, plus a bay wide enough that a prop standing in it does not sever the
    /// scene. The junction at (8,11) is untouched, so the junction tests still describe it.
    /// </summary>
    private const string TrunkBranchAndBay = @"{
        ""name"": ""t"", ""width"": 17, ""height"": 21,
        ""player"": { ""x"": 8, ""y"": 14 },
        ""carve"": [
            { ""x0"": 8, ""y0"": 3,  ""x1"": 8,  ""y1"": 18 },
            { ""x0"": 2, ""y0"": 11, ""x1"": 14, ""y1"": 11 },
            { ""x0"": 5, ""y0"": 16, ""x1"": 11, ""y1"": 18 }
        ] }";

    private static string WithProps(string props) =>
        TrunkBranchAndBay.Substring(0, TrunkBranchAndBay.LastIndexOf(']')) + "], \"props\": ["
        + props + "] }";

    [Test]
    public void Props_SpanningTwoCells_KeepTheirFootprintAndLayout()
    {
        var state = CorridorReviewSceneBuilder.Build(CorridorReviewSceneBuilder.ParseSpecJson(
            WithProps(@"{ ""tileId"": 9800, ""x"": 6, ""y"": 16, ""w"": 1, ""h"": 2,
                          ""layout"": [9800, 9801] }")));

        Assert.That(state.Props, Has.Count.EqualTo(1));
        var prop = state.Props[0];
        Assert.Multiple(() =>
        {
            Assert.That(prop.FootprintW, Is.EqualTo(1));
            Assert.That(prop.FootprintH, Is.EqualTo(2), "a 1x2 prop must reach the second cell");
            Assert.That(prop.TileLayout, Is.EqualTo(new[] { 9800, 9801 }));
        });
    }

    /// <summary>
    /// The row-major contract, which a 1x2 CANNOT test: with W=1, row-major and column-major are
    /// byte-identical. The other end of this contract is DungeonRenderer.CreatePropSprite, which
    /// indexes `dx = i % FootprintW, dy = i / FootprintW` — so layout[1] is the cell to the EAST
    /// of the anchor, not the one below it.
    /// </summary>
    [Test]
    public void Props_SpanningTwoByTwo_CoverExactlyTheirFootprint()
    {
        var state = CorridorReviewSceneBuilder.Build(CorridorReviewSceneBuilder.ParseSpecJson(
            WithProps(@"{ ""tileId"": 9810, ""x"": 6, ""y"": 16, ""w"": 2, ""h"": 2,
                          ""layout"": [9810, 9811, 9812, 9813] }")));

        var prop = state.Props[0];
        Assert.Multiple(() =>
        {
            Assert.That((prop.FootprintW, prop.FootprintH), Is.EqualTo((2, 2)));
            Assert.That(prop.TileLayout, Is.EqualTo(new[] { 9810, 9811, 9812, 9813 }),
                        "row-major: anchor, east, south, south-east");
            foreach (var (x, y) in new[] { (6, 16), (7, 16), (6, 17), (7, 17) })
                Assert.That(state.Map.IsPropCell(x, y), Is.True, $"({x},{y}) is under the prop");
            foreach (var (x, y) in new[] { (5, 16), (8, 16), (6, 18), (8, 17) })
                Assert.That(state.Map.IsPropCell(x, y), Is.False, $"({x},{y}) is not");
        });
    }

    // ── THE REFUSALS ─────────────────────────────────────────────────────────────────────────
    // §13.5: an instrument's pass does not count until it has been shown to go red. Each of
    // these asserts the DISTINGUISHING clause of its own message, not a substring both share —
    // a review found that "2x2" appeared in two different refusals, so either test passed on
    // the other one's failure.

    [Test]
    public void Props_ThatSealTheCorridor_AreRefused()
    {
        // A 1x2 blocking prop across the one-wide trunk. This is what the first version of this
        // fixture did by accident, and nothing caught it.
        var ex = Assert.Throws<InvalidOperationException>(
            () => CorridorReviewSceneBuilder.Build(CorridorReviewSceneBuilder.ParseSpecJson(
                WithProps(@"{ ""tileId"": 9800, ""x"": 8, ""y"": 12, ""w"": 1, ""h"": 2,
                              ""layout"": [9800, 9801] }"))));
        Assert.That(ex!.Message, Does.Contain("seal the corridor"));
    }

    [Test]
    public void Props_StandingOnTheJunction_AreRefused()
    {
        // A prop cell is not walkable, so HasJunction answers NO — and a NO makes the junction
        // luminance guard return true without measuring anything. The guard would not fire; it
        // would cease to exist.
        var ex = Assert.Throws<InvalidOperationException>(
            () => CorridorReviewSceneBuilder.Build(CorridorReviewSceneBuilder.ParseSpecJson(
                WithProps(@"{ ""tileId"": 9800, ""x"": 8, ""y"": 11 }"))));
        Assert.That(ex!.Message, Does.Contain("switches the junction"));
    }

    [Test]
    public void Props_CoveringAWallCell_AreRefused()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CorridorReviewSceneBuilder.Build(CorridorReviewSceneBuilder.ParseSpecJson(
                WithProps(@"{ ""tileId"": 9800, ""x"": 11, ""y"": 16, ""w"": 2, ""h"": 1,
                              ""layout"": [9800, 9801] }"))));
        Assert.That(ex!.Message, Does.Contain("is not floor"));
    }

    /// <summary>
    /// A footprint running past the edge of the map. MarkPropCell is `if (InBounds)`, so this
    /// used to truncate in silence while the renderer still drew a sprite for every layout
    /// entry — half an object over solid rock, at full brightness, no error anywhere.
    ///
    /// ⚠ It is refused for being off the FLOOR rather than off the MAP, and that is not a
    /// weaker result: a well-formed review map has a wall border, so a footprint leaving the
    /// map crosses that wall first. The bounds check behind it is unreachable for any scene
    /// with a border and is kept as defence for one without. Asserting "off the map" here
    /// would be asserting a message this geometry can never produce.
    /// </summary>
    [Test]
    public void Props_RunningPastTheMapEdge_AreRefused()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CorridorReviewSceneBuilder.Build(CorridorReviewSceneBuilder.ParseSpecJson(
                WithProps(@"{ ""tileId"": 9800, ""x"": 16, ""y"": 18, ""w"": 2, ""h"": 2,
                              ""layout"": [1, 2, 3, 4] }"))));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("(16,18)"), "names the offending cell");
            Assert.That(ex.Message, Does.Contain("is not floor"));
        });
    }

    [Test]
    public void Props_OnThePlayersStation_AreRefused()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CorridorReviewSceneBuilder.Build(CorridorReviewSceneBuilder.ParseSpecJson(
                WithProps(@"{ ""tileId"": 9800, ""x"": 8, ""y"": 14 }"))));
        Assert.That(ex!.Message, Does.Contain("player's own station"));
    }

    [Test]
    public void Props_WithAFootprintItsLayoutCannotFill_AreRefused()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CorridorReviewSceneBuilder.ParseSpecJson(
                WithProps(@"{ ""tileId"": 9800, ""x"": 6, ""y"": 16, ""w"": 2, ""h"": 2,
                              ""layout"": [9800, 9801] }")));
        Assert.That(ex!.Message, Does.Contain("layout of 2 tiles"));
    }

    [Test]
    public void Props_LargerThanOneCellWithNoLayout_AreRefused()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CorridorReviewSceneBuilder.ParseSpecJson(
                WithProps(@"{ ""tileId"": 9800, ""x"": 6, ""y"": 16, ""w"": 2, ""h"": 1 }")));
        Assert.That(ex!.Message, Does.Contain("needs a `layout` of 2 tile ids"));
    }

    [Test]
    public void Props_ThatAreOneByOneWithALayout_AreRefused()
    {
        // The renderer takes its multi-tile branch only when W > 1 || H > 1, so a layout here
        // would be dropped and `tileId` drawn instead — silently.
        var ex = Assert.Throws<InvalidOperationException>(
            () => CorridorReviewSceneBuilder.ParseSpecJson(
                WithProps(@"{ ""tileId"": 9800, ""x"": 6, ""y"": 16, ""layout"": [5555] }")));
        Assert.That(ex!.Message, Does.Contain("is 1x1 and also declares a layout"));
    }

    /// <summary>
    /// Every scene written before §12.2 must parse exactly as it did. A prop with no `w`/`h` is
    /// 1x1 and carries no layout, which is what the renderer's single-sprite path expects.
    /// </summary>
    [Test]
    public void Props_WithoutAFootprint_AreStillOneByOne()
    {
        var state = CorridorReviewSceneBuilder.Build(CorridorReviewSceneBuilder.ParseSpecJson(
            WithProps(@"{ ""tileId"": 9800, ""x"": 6, ""y"": 16 }")));

        var prop = state.Props[0];
        Assert.Multiple(() =>
        {
            Assert.That((prop.FootprintW, prop.FootprintH), Is.EqualTo((1, 1)));
            Assert.That(prop.TileLayout, Is.Null, "a 1x1 prop uses TileId alone");
        });
    }

    // ── THE SHIPPED SCENES THEMSELVES ────────────────────────────────────────────────────────
    //
    // ⚠ NOTHING IN THIS FIXTURE HAD EVER PARSED A SCENE FILE, and a review caught what that
    // costs: a staged `tier1_props_review.json` with every `bound_lum` stripped out threw on its
    // first legibility point, so the review build died at boot and produced no capture — the one
    // scene the multi-cell work exists to enable — while `dotnet test` reported 21 of 21 green.
    // The same hole would have swallowed the deletion of five `bound_derivation` blocks carrying
    // a ruled 2026-09-07 derivation.
    //
    // Every refusal above becomes a gate on every shipped scene for the price of this one test.

    private static IEnumerable<string> ShippedScenes()
    {
        var dir = System.IO.Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "src", "Presentation", "assets", "tier0_harness", "scenes");
        dir = System.IO.Path.GetFullPath(dir);
        return System.IO.Directory.Exists(dir)
            ? System.IO.Directory.GetFiles(dir, "*.json")
            : System.Linq.Enumerable.Empty<string>();
    }

    [TestCaseSource(nameof(ShippedScenes))]
    public void ShippedScene_ParsesAndBuilds(string path)
    {
        var spec = CorridorReviewSceneBuilder.ParseSpecJson(System.IO.File.ReadAllText(path));
        Assert.DoesNotThrow(() => CorridorReviewSceneBuilder.Build(spec),
                            $"{System.IO.Path.GetFileName(path)} does not build");
    }

    [Test]
    public void ShippedScenes_AreActuallyBeingChecked()
    {
        // A TestCaseSource that silently finds nothing is a green test that tests nothing —
        // exactly the shape of the hole this pair was added to close.
        Assert.That(ShippedScenes(), Is.Not.Empty, "no shipped scene files were found to check");
    }

    // ── THE THREE REFUSALS THAT HAD NO TEST ──────────────────────────────────────────────────
    // A review mutated each guard in turn and found that deleting any of these three left all 21
    // tests green — including the two that had just caught a real defect in a shipped build.

    [Test]
    public void Props_AnchoredOutsideTheMap_AreRefused()
    {
        // The border wall cannot intercept a footprint that never enters the map. An earlier
        // comment here claimed this branch was unreachable; it fires on an ordinary typo.
        var ex = Assert.Throws<InvalidOperationException>(
            () => CorridorReviewSceneBuilder.Build(CorridorReviewSceneBuilder.ParseSpecJson(
                WithProps(@"{ ""tileId"": 9800, ""x"": 99, ""y"": 99 }"))));
        Assert.That(ex!.Message, Does.Contain("off the map"));
    }

    [Test]
    public void Props_CoveringALegibilityPoint_AreRefused()
    {
        // This is the guard that caught (4,14) — a declared LIT FLOOR point with a barricade on
        // it, in every gated props build. `blocks: false` is the hole the first version had:
        // the renderer still draws a non-blocking prop, at 0.7 alpha, over the cell.
        foreach (var blocks in new[] { "true", "false" })
        {
            var json = TrunkBranchAndBay.Substring(0, TrunkBranchAndBay.LastIndexOf(']'))
                + @"], ""legibility"": [ { ""x"": 6, ""y"": 16, ""expect"": ""lit"",
                     ""bound_lum"": 0.1 } ], ""props"": [ { ""tileId"": 9800, ""x"": 6,
                     ""y"": 16, ""blocks"": " + blocks + " } ] }";
            var ex = Assert.Throws<InvalidOperationException>(
                () => CorridorReviewSceneBuilder.Build(
                    CorridorReviewSceneBuilder.ParseSpecJson(json)),
                $"blocks={blocks} must still be refused — the sprite draws either way");
            Assert.That(ex!.Message, Does.Contain("legibility point (6,16)"));
        }
    }

    [Test]
    public void Props_CoveringTheLuminanceReferenceCell_AreRefused()
    {
        // The reference cell is (playerX, playerY+1) and is documented as "lit floor with no
        // sprite standing on it". In a one-wide corridor the seal check pre-empts this, so the
        // bay is what makes it reachable: the player stands in it with floor all around.
        const string bayStation = @"{
            ""name"": ""t"", ""width"": 17, ""height"": 21,
            ""player"": { ""x"": 8, ""y"": 16 },
            ""carve"": [
                { ""x0"": 8, ""y0"": 3,  ""x1"": 8,  ""y1"": 18 },
                { ""x0"": 2, ""y0"": 11, ""x1"": 14, ""y1"": 11 },
                { ""x0"": 5, ""y0"": 16, ""x1"": 11, ""y1"": 18 }
            ],
            ""props"": [ { ""tileId"": 9800, ""x"": 8, ""y"": 17, ""blocks"": false } ] }";

        var ex = Assert.Throws<InvalidOperationException>(
            () => CorridorReviewSceneBuilder.Build(
                CorridorReviewSceneBuilder.ParseSpecJson(bayStation)));
        Assert.That(ex!.Message, Does.Contain("luminance reference cell"));
    }

    [Test]
    public void Props_ThatOverlapAnotherProp_AreRefused()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CorridorReviewSceneBuilder.Build(CorridorReviewSceneBuilder.ParseSpecJson(
                WithProps(@"{ ""tileId"": 9810, ""x"": 6, ""y"": 16, ""w"": 2, ""h"": 1,
                              ""layout"": [9810, 9811] },
                            { ""tileId"": 9800, ""x"": 7, ""y"": 16 }"))));
        Assert.That(ex!.Message, Does.Contain("already covered by prop 9810"));
    }

    // ── #167 WALL-TOP PROPS ──────────────────────────────────────────────────────────────────
    //
    // "The prop/overlay pass gives wall tops world-placed OBJECTS standing on them — a brazier,
    // a bundle, a driven post, salvage."
    //
    // ⚠ §12.2's OWN ENABLER WAS BLOCKING THIS. The footprint validation requires every covered
    // cell to be FLOOR, which is right for a prop on the walked surface and refuses exactly the
    // thing #167 asks for. A prop now declares its surface; "floor" is the default and every
    // scene written before this parses unchanged.
    //
    // Measured on the delivered frame: a prop on a wall cell changed 98 device pixels before the
    // draw order was fixed, and 2,288 after — the wall sprite had been drawing straight over it.

    [Test]
    public void Props_OnAWallTop_AreSeatedOnTheWallCell()
    {
        // (12,16) is inside the bay's surrounding rock: the bay is x 5..11, so x=12 is wall.
        var state = CorridorReviewSceneBuilder.Build(CorridorReviewSceneBuilder.ParseSpecJson(
            WithProps(@"{ ""tileId"": 9820, ""x"": 12, ""y"": 16, ""on"": ""wall"" }")));

        Assert.That(state.Props, Has.Count.EqualTo(1));
        Assert.That(state.Props[0].OnWallTop, Is.True,
                    "the renderer needs this to sort the sprite above the wall it stands on");
    }

    /// <summary>
    /// A wall-top prop must NOT be marked as a prop cell. MarkPropCell tells the FLOOR composer
    /// what stands on the floor and makes the cell unwalkable; a wall cell is already unwalkable
    /// and has no floor beneath it to suppress, so marking it would assert a fact about a surface
    /// that is not there — and would put the seal check to work reasoning about solid rock.
    /// </summary>
    [Test]
    public void Props_OnAWallTop_DoNotMarkAFloorPropCell()
    {
        var state = CorridorReviewSceneBuilder.Build(CorridorReviewSceneBuilder.ParseSpecJson(
            WithProps(@"{ ""tileId"": 9820, ""x"": 12, ""y"": 16, ""on"": ""wall"" }")));

        Assert.That(state.Map.IsPropCell(12, 16), Is.False);
    }

    [Test]
    public void Props_OnAWallTop_PlacedOnFloor_AreRefused()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CorridorReviewSceneBuilder.Build(CorridorReviewSceneBuilder.ParseSpecJson(
                WithProps(@"{ ""tileId"": 9820, ""x"": 6, ""y"": 16, ""on"": ""wall"" }"))));
        Assert.That(ex!.Message, Does.Contain("IS floor"));
    }

    [Test]
    public void Props_OnTheFloor_PlacedOnAWall_AreRefusedAndPointAt167()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CorridorReviewSceneBuilder.Build(CorridorReviewSceneBuilder.ParseSpecJson(
                WithProps(@"{ ""tileId"": 9820, ""x"": 12, ""y"": 16 }"))));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("is not floor"));
            Assert.That(ex.Message, Does.Contain("#167"),
                        "the refusal should name the way to do it deliberately");
        });
    }

    [Test]
    public void Props_WithAnUnknownSurface_AreRefused()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CorridorReviewSceneBuilder.ParseSpecJson(
                WithProps(@"{ ""tileId"": 9820, ""x"": 12, ""y"": 16, ""on"": ""ceiling"" }")));
        Assert.That(ex!.Message, Does.Contain("\"floor\" or \"wall\""));
    }

    [Test]
    public void Props_WithoutASurface_AreStillFloorProps()
    {
        var state = CorridorReviewSceneBuilder.Build(CorridorReviewSceneBuilder.ParseSpecJson(
            WithProps(@"{ ""tileId"": 9800, ""x"": 6, ""y"": 16 }")));
        Assert.Multiple(() =>
        {
            Assert.That(state.Props[0].OnWallTop, Is.False);
            Assert.That(state.Map.IsPropCell(6, 16), Is.True, "a floor prop still marks its cell");
        });
    }
}
