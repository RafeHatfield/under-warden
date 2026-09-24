using System.Collections.Generic;
using System.Text.Json;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Core;

/// <summary>
/// Builds the Tier 0 review corridor — a lit corridor with a junction, assembled from
/// authored data and rendered through the production renderer (SetupPresentation →
/// DungeonRenderer.Render), per ART-BIBLE-v0 §13.1: a candidate is judged in the lit scene
/// at true display size, never from a contact sheet.
///
/// Sibling of <see cref="ReviewSceneBuilder"/>, which seats prop candidates in an open room.
/// This builder exists because Tier 1 is floors and walls, and floors and walls are judged by
/// walking a corridor — specifically by the junction. The critic is asked which way they would
/// walk; a straight corridor cannot answer that, so the junction is load-bearing, not decoration.
///
/// Geometry is authored as a list of axis-aligned rectangles carved out of solid rock. That is
/// deliberately general: a T-junction, a crossroads, and a dogleg are all just different carve
/// lists, so the shape of the junction is a data decision at review time rather than a code
/// change here.
///
/// NOTHING in this class hard-codes a tile dimension, canvas size, or palette value. Which
/// pixels a tile ID resolves to is decided entirely by the tile-theme config the Presentation
/// layer is pointed at (--tile-theme-config), which is how candidate tiles and the §6.4 probe
/// arms enter without any file being overwritten. ART-BIBLE-v0 §4.3 marks canvas and tile sizes
/// PLACEHOLDER; this builder therefore never names one.
///
/// JSON schema:
/// {
///   "name":   string,                                  // recorded in the capture log
///   "width":  int, "height": int,                      // map extent, in tiles
///   "player": { "x": int, "y": int },                  // where the carried light is anchored
///   "carve":  [ { "x0":int,"y0":int,"x1":int,"y1":int } ... ]   // inclusive rects, floor
///   "legibility": [ { "x":int,"y":int,"expect":"lit"|"dark","bound_lum":float,"why":string } ... ]
/// }
///
/// THE LEGIBILITY LIST — floor session two, precondition 2. Optional, so every existing spec
/// still parses; present, it declares the points a capture is REQUIRED to be able to see and the
/// points it is required to leave dark.
///
/// It exists because `ProbeJunctionLuminance` guards a junction, and a floor review scene has
/// none — it reports `junction=NO` and the guard silently no-ops, so floor captures had no
/// legibility check of any kind. A scene whose subject had fallen outside the carried light
/// would still render, still pass determinism, and measure nothing (MISFED).
///
/// BOTH DIRECTIONS ARE DECLARED, and the second half is the one that is easy to leave out.
/// `expect: "dark"` points must STAY dark. §6.2.1 rules the readability pass "not a licence to
/// flood the Boundary with light" — *you begin as the only thing here that burns* is register and
/// outranks convenience — so a guard that only asked "is it bright enough" would green a scene
/// that had drowned the arc, which is the opposite failure and just as fatal.
/// </summary>
public static class CorridorReviewSceneBuilder
{
    /// <summary>Parsed geometry, exposed so tests can assert the junction without Godot.</summary>
    public readonly record struct Spec(
        string Name, int Width, int Height, int PlayerX, int PlayerY,
        IReadOnlyList<(int X0, int Y0, int X1, int Y1)> Carve,
        IReadOnlyList<LegibilityPoint> Legibility,
        IReadOnlyList<PropPlacement> Props);

    /// <summary>
    /// A standing object seated in the review scene, for the tier-two props gate.
    ///
    /// THE FIELDS ARE <c>ReviewSceneBuilder</c>'S, DELIBERATELY. That sibling builder has parsed
    /// and placed props since the candidate-review rounds, and <c>DungeonRenderer.Render</c> has
    /// drawn them in Pass 4 for as long. Giving this scene a second vocabulary for the same thing
    /// would mean two ways to say "a barrel at (5,12)" and one of them going stale.
    /// </summary>
    /// <summary>
    /// A prop seated in the review scene. <paramref name="W"/> and <paramref name="H"/> are its
    /// footprint in CELLS — bible §12.2 authors props at readability scale, and a standing stone
    /// taller than the character is two cells tall, not one cell with a small stone in it.
    /// <paramref name="Layout"/> is the row-major tile id per cell, exactly
    /// <c>W * H</c> entries; null means the prop is 1x1 and uses <paramref name="TileId"/> alone.
    /// </summary>
    public readonly record struct PropPlacement(
        int TileId, int X, int Y, bool Blocks, string Why,
        int W = 1, int H = 1, IReadOnlyList<int>? Layout = null, string On = "floor",
        string Footprint = "box", PropLight? Light = null);

    /// <summary>
    /// A point the capture must be able to see, or must leave dark. <paramref name="Why"/> is
    /// carried so the capture log says what each point is FOR — a coordinate with no reason
    /// beside it is a number nobody can maintain.
    /// </summary>
    /// <summary>
    /// A declared legibility point and THE ABSOLUTE DELIVERED LUMINANCE IT REQUIRES.
    ///
    /// RULED (Rafe, 2026-09-07): <c>BoundLum</c> replaced a ratio against the brightest lit floor.
    /// **An instrument whose reference can saturate measures the ceiling, not the scene.** The old
    /// reference cell clipped at 255, so every dark declaration was a ratio against a pinned
    /// value; a highlight shoulder that changed ZERO dark pixels made two of them "fail", purely
    /// because the denominator moved. The bound is now what a viewer can or cannot see, in
    /// delivered luminance, on the frame (§13.8).
    ///
    /// Units: normalised 0..1, the same quantity <c>PatchLuminance</c> returns.
    /// </summary>
    public readonly record struct LegibilityPoint(
        int X, int Y, bool MustBeLit, float BoundLum, string Why);

    public static Spec ParseSpec(string roundJsonPath)
        => ParseSpecJson(System.IO.File.ReadAllText(roundJsonPath));

    public static Spec ParseSpecJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string name = root.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
        int w = root.GetProperty("width").GetInt32();
        int h = root.GetProperty("height").GetInt32();

        var playerEl = root.GetProperty("player");
        int px = playerEl.GetProperty("x").GetInt32();
        int py = playerEl.GetProperty("y").GetInt32();

        var carve = new List<(int, int, int, int)>();
        foreach (var r in root.GetProperty("carve").EnumerateArray())
        {
            carve.Add((r.GetProperty("x0").GetInt32(), r.GetProperty("y0").GetInt32(),
                       r.GetProperty("x1").GetInt32(), r.GetProperty("y1").GetInt32()));
        }

        if (w <= 2 || h <= 2)
            throw new InvalidOperationException($"Corridor spec '{name}': map must be at least 3x3, got {w}x{h}.");
        if (carve.Count == 0)
            throw new InvalidOperationException($"Corridor spec '{name}': carve list is empty — that is solid rock, not a corridor.");

        var legibility = new List<LegibilityPoint>();
        if (root.TryGetProperty("legibility", out var legEl))
        {
            foreach (var e in legEl.EnumerateArray())
            {
                string expect = e.TryGetProperty("expect", out var ex) ? (ex.GetString() ?? "") : "";
                if (expect != "lit" && expect != "dark")
                    throw new InvalidOperationException(
                        $"Corridor spec '{name}': legibility point must declare expect \"lit\" or "
                        + $"\"dark\", got \"{expect}\". An undeclared expectation cannot be checked.");
                // NO DEFAULT, AND THAT IS THE POINT. A bound that can be omitted is a bound
                // that drifts silently, and the guard this replaced spent its whole life
                // measuring against a number nobody declared. A point without one fails loudly
                // at parse time rather than quietly at capture time.
                if (!e.TryGetProperty("bound_lum", out var bl))
                    throw new InvalidOperationException(
                        $"Corridor spec '{name}': legibility point ({e.GetProperty("x").GetInt32()},"
                        + $"{e.GetProperty("y").GetInt32()}) declares no \"bound_lum\". Since "
                        + "2026-09-07 legibility is an ABSOLUTE delivered-luminance bound, not a "
                        + "ratio against the brightest pixel — see LegibilityPoint. Derive it on "
                        + "the nulled build at the ratified rig and declare it.");
                legibility.Add(new LegibilityPoint(
                    e.GetProperty("x").GetInt32(), e.GetProperty("y").GetInt32(),
                    expect == "lit", (float)bl.GetDouble(),
                    e.TryGetProperty("why", out var wy) ? (wy.GetString() ?? "") : ""));
            }
        }

        // ── PROPS, OPTIONAL AND ABSENT BY DEFAULT ────────────────────────────────────────
        //
        // Every scene that existed before the props pass parses identically: no `props` key means
        // an empty list, and an empty list means this builder behaves exactly as it did. The
        // floor and wall lanes' captures are unaffected, which matters because their reference
        // frames are still the bar.
        var props = new List<PropPlacement>();
        if (root.TryGetProperty("props", out var propsEl))
        {
            foreach (var e in propsEl.EnumerateArray())
            {
                // FOOTPRINT AND LAYOUT — bible §12.2. Absent means 1x1, so every scene written
                // before this clause parses exactly as it did.
                int ptid = e.GetProperty("tileId").GetInt32();
                int ppx = e.GetProperty("x").GetInt32();
                int ppy = e.GetProperty("y").GetInt32();
                string where = $"Corridor spec '{name}': prop {ptid} at ({ppx},{ppy})";

                // ── WHERE THE PROP STANDS — #167, and my own §12.2 guard was blocking it ────
                //
                // §12.2's enabler validates that every covered cell is FLOOR, which is right for
                // a prop standing on the walked surface and wrong as a universal rule. #167's
                // whole subject is the opposite case: "the prop/overlay pass gives wall tops
                // world-placed OBJECTS standing on them — a brazier, a bundle, a driven post,
                // salvage". A wall-top prop stands on a wall BY DEFINITION, so the floor check
                // refuses exactly the thing the issue asks for.
                //
                // So a prop declares its surface. "floor" is the default and every scene written
                // before this parses unchanged; "wall" inverts the cell test and changes what
                // seating does with it (see Build).
                string on = e.TryGetProperty("on", out var onEl) ? (onEl.GetString() ?? "floor")
                                                                 : "floor";
                if (on != "floor" && on != "wall")
                    throw new InvalidOperationException(
                        $"{where} declares `on: \"{on}\"`; it is \"floor\" or \"wall\".");

                int pw = e.TryGetProperty("w", out var wEl) ? wEl.GetInt32() : 1;
                int ph = e.TryGetProperty("h", out var hEl) ? hEl.GetInt32() : 1;
                if (pw < 1 || ph < 1)
                    throw new InvalidOperationException(
                        $"{where} has a footprint of {pw}x{ph}; it must be at least 1x1.");

                List<int>? layout = null;
                if (e.TryGetProperty("layout", out var layEl))
                {
                    layout = new List<int>();
                    foreach (var t in layEl.EnumerateArray()) layout.Add(t.GetInt32());
                    // A layout that does not match its footprint is a SILENT mis-draw: the
                    // renderer indexes row-major over FootprintW x FootprintH
                    // (DungeonRenderer.CreatePropSprite: dx = i % FootprintW), so a short list
                    // reads past the row it meant and a long one is truncated. Neither errors.
                    if (layout.Count != pw * ph)
                        throw new InvalidOperationException(
                            $"{where} has a layout of {layout.Count} tiles but its footprint is " +
                            $"{pw}x{ph} = {pw * ph} cells. One tile id per cell, row-major.");
                    // A layout on a 1x1 is DROPPED by the renderer, which takes the multi-tile
                    // branch only when FootprintW > 1 || FootprintH > 1 — so it would draw
                    // `tileId` and silently ignore what the scene declared.
                    if (pw * ph == 1)
                        throw new InvalidOperationException(
                            $"{where} is 1x1 and also declares a layout. The renderer would " +
                            "ignore the layout and draw `tileId`; say one or the other.");
                }
                else if (pw * ph > 1)
                {
                    throw new InvalidOperationException(
                        $"{where} is {pw}x{ph} and needs a `layout` of {pw * ph} tile ids; " +
                        "one tile id cannot fill more than one cell.");
                }

                // ── SHADOW FOOTPRINT AND EMITTED LIGHT (cast-shadows round) ─────────────────
                // "box" (default) or "round" — the round exception's circle (§3.2). A `light`
                // block makes the prop emit: the orc fire, and nothing else so far.
                string shape = e.TryGetProperty("shape", out var shEl) ? (shEl.GetString() ?? "box")
                                                                       : "box";
                if (shape != "box" && shape != "round")
                    throw new InvalidOperationException(
                        $"{where} declares `shape: \"{shape}\"`; it is \"box\" or \"round\".");
                PropLight? plight = null;
                if (e.TryGetProperty("light", out var lEl))
                    plight = new PropLight(
                        lEl.GetProperty("color").GetString() ?? "ffffff",
                        (float)lEl.GetProperty("energy").GetDouble(),
                        (float)lEl.GetProperty("radiusTiles").GetDouble());

                props.Add(new PropPlacement(
                    ptid, ppx, ppy,
                    !e.TryGetProperty("blocks", out var b) || b.GetBoolean(),
                    e.TryGetProperty("why", out var wy) ? (wy.GetString() ?? "") : "",
                    pw, ph, layout, on, shape, plight));
            }
        }

        return new Spec(name, w, h, px, py, carve, legibility, props);
    }

    /// <summary>
    /// Floor cells reachable from a start cell, orthogonally, treating prop cells as solid.
    /// The connectivity half of `RoomPropPlacer`'s footprint contract, in the form this scene
    /// needs: there are no rooms and no entrances here, only the player's station and the
    /// corridor the critic is asked to walk.
    /// </summary>
    private static HashSet<(int X, int Y)> Reachable(GameMap map, int sx, int sy)
    {
        var seen = new HashSet<(int X, int Y)>();
        if (!map.InBounds(sx, sy)) return seen;
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((sx, sy));
        seen.Add((sx, sy));
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var n = (X: x + dx, Y: y + dy);
                if (seen.Contains(n) || !map.InBounds(n.X, n.Y)) continue;
                if (map.GetTileKind(n.X, n.Y) != TileKind.Floor) continue;
                if (map.IsPropCell(n.X, n.Y)) continue;
                seen.Add(n);
                queue.Enqueue(n);
            }
        }
        return seen;
    }

    /// <summary>
    /// True when the carved geometry contains a genuine CORRIDOR junction: a walkable cell with
    /// three or more walkable orthogonal neighbours AND all four diagonals solid.
    ///
    /// The diagonal condition is not fussiness, it is the whole test. Without it, a corridor
    /// carved three tiles wide reports a junction at its very first cell — every cell in an open
    /// area has three or more open neighbours — and the scene silently stops posing the question
    /// it exists to pose. That is not hypothetical: the first Tier 0 capture carved a 3-wide
    /// trunk and this check duly reported "junction=YES at (8,4)", which was the top of a
    /// straight corridor. At a clean T or + junction of one-wide corridors the diagonals are all
    /// wall, and in any widened area at least one is not.
    ///
    /// Checked at build time and reported in the capture log, because a spec that quietly
    /// degrades to a straight corridor cannot answer "which way would you walk" and nothing else
    /// in the pipeline would notice.
    /// </summary>
    public static bool HasJunction(GameMap map, out (int X, int Y) at)
    {
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (!map.IsWalkable(x, y)) continue;

                int open = 0;
                if (map.IsWalkable(x + 1, y)) open++;
                if (map.IsWalkable(x - 1, y)) open++;
                if (map.IsWalkable(x, y + 1)) open++;
                if (map.IsWalkable(x, y - 1)) open++;
                if (open < 3) continue;

                bool diagonalsSolid = !map.IsWalkable(x - 1, y - 1) && !map.IsWalkable(x + 1, y - 1)
                                   && !map.IsWalkable(x - 1, y + 1) && !map.IsWalkable(x + 1, y + 1);
                if (!diagonalsSolid) continue;

                at = (x, y);
                return true;
            }
        }
        at = (-1, -1);
        return false;
    }

    public static GameState Build(string roundJsonPath) => Build(ParseSpec(roundJsonPath));

    public static GameState Build(Spec spec)
    {
        var map = new GameMap(spec.Width, spec.Height, allWalls: true);

        foreach (var (x0, y0, x1, y1) in spec.Carve)
        {
            // Clamp to a 1-tile solid border so the corridor is always enclosed by wall —
            // an unenclosed corridor would show the void, not a wall candidate.
            int cx0 = Math.Max(1, Math.Min(x0, x1));
            int cx1 = Math.Min(spec.Width  - 2, Math.Max(x0, x1));
            int cy0 = Math.Max(1, Math.Min(y0, y1));
            int cy1 = Math.Min(spec.Height - 2, Math.Max(y0, y1));
            for (int x = cx0; x <= cx1; x++)
                for (int y = cy0; y <= cy1; y++)
                    map.SetTile(x, y, TileKind.Floor);
        }

        map.SetTileThemeRect(0, 0, spec.Width - 1, spec.Height - 1, TileTheme.Grey);

        var player = new Entity(0, "Player", spec.PlayerX, spec.PlayerY, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
                               accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        player.Add(new SpeedBonusTracker(baseRatio: 0.25));
        map.RegisterEntity(player);

        // RevealAll, deliberately: fog-of-war is a gameplay-information system and this scene is
        // a lighting instrument. Darkness in this capture must come from the engine light rig
        // (§6.1), not from FOV dimming — otherwise the "lighting is live" control could be
        // satisfied by fog and the harness would be measuring the wrong thing.
        // The props, seated before RevealAll so a blocking one marks its cell for the floor
        // composer — a prop must sit on plain floor, not on a worn or accent tile, or it reads as
        // standing on a pedestal (the defect PR #103 fixed for the game and #128 tracks).
        // ── SEATING A PROP IS THREE STEPS, NOT ONE ───────────────────────────────────────────
        //
        // `RoomPropPlacer` has seated multi-cell blocking props in this codebase for a year, and
        // it does it in three: CanPlaceFootprint, MarkFootprint, ValidateConnectivity. The first
        // version of §12.2's enabler ported the middle one alone, and a review found what that
        // costs — with the diff's own test fixture, a 1x2 marker in the trunk took the corridor
        // from 28 walkable cells reachable from the player to FIVE, put the junction on the far
        // side of a sealed wall, and left every test passing. A prop that cannot be walked to is
        // not a prop the gate can judge.
        //
        // So: check the footprint against the map, then mark it, then prove nothing was severed.
        // Each step REFUSES rather than dropping the prop. `RoomPropPlacer` drops-and-retries
        // because it is generating a dungeon and any valid room will do; this is an INSTRUMENT,
        // and an instrument that quietly discards its subject reports on a scene nobody authored.
        var junctionBefore = HasJunction(map, out var junctionCellBefore);
        var reachableBefore = Reachable(map, spec.PlayerX, spec.PlayerY);

        var placed = new List<PlacedProp>();
        // Cells already claimed by an earlier prop. `RoomPropPlacer` gets this free, because
        // IsRawWalkable has already gone false under a placed footprint; here the floor check
        // below still says Floor under an existing prop, so two props can claim one cell and
        // stack two sprites in the same Z band with nothing reported. Multi-cell footprints make
        // that much easier to do by hand — it is the layout-count refusal's defect one layer up.
        var claimed = new Dictionary<(int X, int Y), int>();
        foreach (var pp in spec.Props)
        {
            // 1. CAN IT STAND THERE. Every covered cell, not just the anchor: MarkPropCell is
            //    `if (InBounds)`, so a footprint running off the map is silently truncated while
            //    the renderer still draws a sprite for every layout entry — half an object on
            //    top of solid rock, at full brightness, with no error anywhere.
            for (int dx = 0; dx < pp.W; dx++)
                for (int dy = 0; dy < pp.H; dy++)
                {
                    int cx = pp.X + dx, cy = pp.Y + dy;
                    string at = $"Corridor spec '{spec.Name}': prop {pp.TileId} at ({pp.X},{pp.Y})"
                              + $" is {pp.W}x{pp.H} and covers ({cx},{cy}), which";
                    // ⚠ THIS FIRES, AND AN EARLIER COMMENT HERE CLAIMED IT COULD NOT.
                    // The claim was that a wall border always intercepts a footprint leaving the
                    // map, so the floor check below would report it first. That is true only of
                    // a footprint that STARTS inside: an anchor at (99,99) or (-1,y) never
                    // crosses the border at all, and it is an ordinary authoring typo. A review
                    // ran it and the branch fired. The claim is withdrawn rather than softened,
                    // because a comment asserting a branch is dead is an invitation to delete it.
                    //
                    // What it prevents: MarkPropCell is `if (InBounds)`, so an out-of-map cell is
                    // silently unmarked while the renderer still draws a sprite for every layout
                    // entry.
                    if (!map.InBounds(cx, cy))
                        throw new InvalidOperationException($"{at} is off the map.");
                    bool isFloor = map.GetTileKind(cx, cy) == TileKind.Floor;
                    if (pp.On == "floor" && !isFloor)
                        throw new InvalidOperationException(
                            $"{at} is not floor. A prop stands on the walked surface; a sprite "
                            + "over solid rock is drawn, lit, and meaningless. If this is meant "
                            + "to stand on a wall top, declare `on: \"wall\"` (#167).");
                    if (pp.On == "wall" && isFloor)
                        throw new InvalidOperationException(
                            $"{at} IS floor. A wall-top prop stands on the mass, not in the "
                            + "room — a `on: \"wall\"` prop on a walked cell would float.");
                    if (cx == spec.PlayerX && cy == spec.PlayerY)
                        throw new InvalidOperationException(
                            $"{at} is the player's own station.");
                    if (claimed.TryGetValue((cx, cy), out int other))
                        throw new InvalidOperationException(
                            $"{at} is already covered by prop {other}. Two sprites would draw "
                            + "in the same cell at the same depth, and neither would report it.");
                    claimed[(cx, cy)] = pp.TileId;
                }

            placed.Add(new PlacedProp($"review_{pp.TileId}", pp.X, pp.Y, pp.W, pp.H,
                                      pp.Blocks && pp.On == "floor", pp.TileId,
                                      TileLayout: pp.Layout, OnWallTop: pp.On == "wall",
                                      Footprint: pp.Footprint, Light: pp.Light));

            // 2. MARK EVERY CELL, not just the anchor. MarkPropCell is what keeps the floor
            //    composer from laying a worn or accent tile under a prop (#128's pedestal);
            //    marking only the anchor would put a pedestal under three quarters of a 2x2.
            //
            //    ⚠ A WALL-TOP PROP IS NOT MARKED, and that is not an oversight. MarkPropCell
            //    exists to tell the FLOOR composer what is standing on the floor, and it also
            //    makes the cell unwalkable. A wall cell is already unwalkable and has no floor
            //    under it to suppress, so marking it would claim a fact about a surface that is
            //    not there — and would make the seal check below reason about rock.
            if (pp.Blocks && pp.On == "floor")
                for (int dx = 0; dx < pp.W; dx++)
                    for (int dy = 0; dy < pp.H; dy++)
                        map.MarkPropCell(pp.X + dx, pp.Y + dy);
        }

        // 3. DID SEATING THEM SEVER THE SCENE. A prop cell is not walkable, so a blocking prop
        //    across a one-wide corridor is a wall — and this builder's whole subject is one-wide
        //    corridors.
        if (spec.Props.Count > 0)
        {
            // THE JUNCTION IS CHECKED FIRST, AND THE ORDER IS LOAD-BEARING. Standing on a
            // junction also severs an arm of it, so the seal check below would fire on the same
            // scene and report the less useful of the two facts — "you cannot reach (9,11)"
            // rather than "you have switched the junction guard off". Both are true; only one
            // names the instrument that stopped working.
            //
            // HasJunction reads IsWalkable, which a prop cell fails. A NO makes
            // `ProbeJunctionLuminance` return true WITHOUT MEASURING ANYTHING — the guard does
            // not fire, it ceases to exist.
            bool junctionAfter = HasJunction(map, out _);
            if (junctionBefore && !junctionAfter)
                throw new InvalidOperationException(
                    $"Corridor spec '{spec.Name}': the carved geometry has a junction at "
                    + $"({junctionCellBefore.X},{junctionCellBefore.Y}) but a prop stands on it. "
                    + "That does not merely hide the junction — it switches the junction "
                    + "legibility guard off without reporting anything.");

            var reachableAfter = Reachable(map, spec.PlayerX, spec.PlayerY);
            foreach (var cell in reachableBefore)
                if (!reachableAfter.Contains(cell) && !map.IsPropCell(cell.X, cell.Y))
                    throw new InvalidOperationException(
                        $"Corridor spec '{spec.Name}': the props seal the corridor — "
                        + $"({cell.X},{cell.Y}) is floor the player could reach before they were "
                        + "seated and cannot reach after. Move a prop, or make it non-blocking.");

            // AND IS AN INSTRUMENT NOW MEASURING A PROP. A legibility point names what it
            // expects to find; a sprite standing on one means the probe reports the prop's
            // luminance as the floor's.
            //
            // ⚠ THIS ASKS ABOUT DRAWING, NOT ABOUT WALKING, AND THE DIFFERENCE IS A HOLE.
            // The first version tested `map.IsPropCell`, which is set only for BLOCKING props —
            // so `"blocks": false` walked straight through both guards while the renderer still
            // drew the prop, at 0.7 alpha, over the cell. A review executed it: a non-blocking
            // 1x2 sitting exactly on a declared `expect: lit` point was ACCEPTED, and the probe
            // would have measured a sprite blended onto floor and called it floor — which is
            // precisely the defect these two lines were added to catch, reachable by adding one
            // word to a scene file. The set below is every cell any prop covers, blocking or not.
            var covered = new HashSet<(int X, int Y)>();
            foreach (var pp in spec.Props)
                for (int dx = 0; dx < pp.W; dx++)
                    for (int dy = 0; dy < pp.H; dy++)
                        covered.Add((pp.X + dx, pp.Y + dy));

            foreach (var lp in spec.Legibility)
                if (covered.Contains((lp.X, lp.Y)))
                    throw new InvalidOperationException(
                        $"Corridor spec '{spec.Name}': legibility point ({lp.X},{lp.Y}) is "
                        + "covered by a prop. It would measure the sprite and report it as "
                        + "floor.");
            if (covered.Contains((spec.PlayerX, spec.PlayerY + 1)))
                throw new InvalidOperationException(
                    $"Corridor spec '{spec.Name}': the luminance reference cell "
                    + $"({spec.PlayerX},{spec.PlayerY + 1}) is covered by a prop. It is supposed "
                    + "to be lit floor with no sprite on it.");
        }

        map.RevealAll();

        // THE REVIEW SCENE CARRIES NO LOSABLE GAME STATE. This is the fix for "the player dies on
        // the first step", and the symptom was not what it looked like: the player never died.
        // IsAlive stayed true. This builder was constructed with turnLimit: 1, copied from
        // ReviewSceneBuilder where it is harmless because that scene is captured and quit and is
        // never walked. In dungeon mode IsGameOver is
        //     !IsAlive || TurnCount >= TurnLimit || Ending != None
        // so the FIRST step took TurnCount to 1 >= 1 and ended the run. On device that surfaces
        // as the end-of-run overlay, which reads as a death.
        //
        // The fix is structural rather than a larger number, because a larger number only moves
        // the failure to turn N. This harness exists to produce byte-comparable captures
        // (LOOP-PROCESS §2.3), and a review surface that can enter ANY state unrelated to the art
        // is not a measuring instrument: a death overlay, a corpse sprite or a changed HUD would
        // be read by the determinism control as a difference in the art.
        //
        // So every loss condition is removed at the source, and the invariant is asserted by
        // CorridorReviewSceneBuilderTests rather than left as a comment:
        //   - no turn limit      — int.MaxValue, so TurnCount can never reach it in a review
        //   - no monsters        — nothing exists that can deal damage
        //   - no ending          — EndingType.None, and nothing in this scene sets it
        //   - no stairs, no traps, no hazards, no props — the carve produces floor and wall only
        // The player keeps an ordinary Fighter: this scene is walked by a human at the §13.2
        // gate, and a walker with no stats would take a different code path through the
        // presentation layer than the game does, which would defeat the point of using the
        // production renderer.
        return new GameState(player, new List<Entity>(), map, new SeededRandom(0),
                             turnLimit: int.MaxValue)
        {
            IsDungeonMode = true,
            CurrentDepth  = 1,
            // The scene's props. Empty for every floor and wall round — no spec before the
            // tier-two pass declares any — and the subject of the review for the props rounds.
            Props         = placed,
        };
    }
}
