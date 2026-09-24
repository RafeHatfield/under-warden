using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;

namespace UnderWarden.Presentation.Map;

/// <summary>Which part of the trodden channel a cell carries. ART-BIBLE-v0 §8.2.1.</summary>
public enum ChannelKind
{
    /// Off the channel. Ordinary floor, or §8.1 decay if it is also off the route.
    None,
    /// The channel's western edge — the band's left shoulder crosses this cell.
    Left,
    /// Fully inside the channel.
    Mid,
    /// The channel's eastern edge.
    Right,
    /// A one-wide chokepoint ON the route: polished wall to wall, because the traffic had
    /// nowhere to spread. §8.2.1 requires this state and the neglected one to read apart at 1x.
    Full,
}

/// <summary>One cell's incident decisions. Every field is an INDEX, never a pixel.</summary>
public readonly record struct FloorIncident(
    int GritId,        // -1 for none
    int EventId,       // -1 for none — at most one event per cell
    int ChannelId,     // -1 for none
    ChannelKind Channel,
    bool Neglected,    // off the route: §8.1 decay rather than §8.1 polish
    int[] OcclusionIds);  // §12.1 contact occlusion, one per wall edge this cell actually has

/// <summary>
/// THE INCIDENT SYSTEM — ART-BIBLE-v0 §8.3's other half, which this project has never had.
///
/// §8.3, LAW (Rafe, 2026-08-27):
///
///     Any incident baked into a tile becomes a motif when tiled. Repetition converts accident
///     into intent, and the eye reads pattern regardless of the incident's quality.
///     ... **Incident arrives at the instance level, randomised** — cracks, wear, marks, and
///     §8.2.1's channel — via variants and overlays: the floor system tier one builds.
///     ... **Until it exists there is nowhere to put a crack that does not turn it into a motif.**
///
/// This class is that placement. It decides, per cell, which incident overlay lands there and
/// which part of the trodden channel crosses it. It touches no pixels and knows no textures —
/// it emits indices, and <see cref="Tier1FloorOverlays"/> draws them. No Godot dependency:
/// the logic/presentation boundary is the project's most important architectural line, and a
/// placement rule the harness cannot execute is a rule nobody can measure.
///
/// DETERMINISM IS NOT DECORATION HERE. Every decision comes from a position hash rather than
/// from a running RNG, so a cell's incident does not depend on the order cells were visited,
/// and the same map and seed produce the same floor on every re-render. The renderer re-runs on
/// visibility updates; a floor whose cracks moved when the fog lifted would be a different
/// defect every turn. CLAUDE.md's determinism rule, applied where it actually bites.
///
/// WHAT IT DELIBERATELY DOES NOT DO: it does not compose. §1 holds that nothing is staged, and
/// §13.4 keeps that clause eye-side with NO INSTRUMENT. Placement here is blind — a hash and a
/// rate — precisely so that nothing in the pipeline is arranging anything.
/// </summary>
public static class FloorIncidentPlanner
{
    /// <summary>
    /// Placement rates and family sizes, read from the family's own MANIFEST.json rather than
    /// duplicated here. Single source of truth: the composer writes them, the field preview and
    /// this planner both read them, and neither keeps a copy that can drift.
    /// </summary>
    public class Config
    {
        public int[] GritIds = System.Array.Empty<int>();
        public int[] EventIds = System.Array.Empty<int>();
        /// Per-event-overlay probability, index-aligned with EventIds.
        public float[] EventRates = System.Array.Empty<float>();
        public float GritRate;
        /// Left, Mid, Right, Full — index by (int)ChannelKind - 1.
        public int[] ChannelIds = System.Array.Empty<int>();
        /// §12.1 contact occlusion, in the order N, E, S, W.
        public int[] OcclusionIds = System.Array.Empty<int>();
    }

    /// <summary>Deterministic per-cell hash. Same shape the tile-variant picker already uses.</summary>
    private static int Hash(int x, int y, int salt)
    {
        unchecked
        {
            int h = x * 7919 + y * 104729 + salt * 15485863;
            h ^= h >> 13;
            h *= 1274126177;
            h ^= h >> 16;
            return h & 0x7FFFFFFF;
        }
    }

    private static float Unit(int x, int y, int salt) => Hash(x, y, salt) / (float)0x7FFFFFFF;

    public static Dictionary<(int X, int Y), FloorIncident> Plan(GameMap map, Config cfg, int seed)
    {
        var channel = TroddenChannel(map, seed);
        var result = new Dictionary<(int X, int Y), FloorIncident>();

        for (int x = 0; x < map.Width; x++)
        for (int y = 0; y < map.Height; y++)
        {
            if (!map.IsWalkable(x, y)) continue;

            channel.TryGetValue((x, y), out var kind);
            bool onRoute = kind != ChannelKind.None;

            // §8.1's two axes are two dials, and being off the route moves BOTH: no traffic and
            // no care produces decay, so an off-route cell gets more grit and more broken stone
            // and less polish. "Polish means you are on the path. Decay means you have stepped
            // off it" (§8.2) is delivered here, by rate, and carried by the channel above.
            bool neglected = !onRoute && !NearChannel(channel, x, y);

            // Does a wall touch one of this cell's four sides? Used twice below: it is where the
            // §12.1 contact occlusion goes, and it is where §8.1's loose material piles up.
            bool occAdjacent = map.IsWallTile(x, y - 1) || map.IsWallTile(x + 1, y)
                            || map.IsWallTile(x, y + 1) || map.IsWallTile(x - 1, y);

            // GRIT IS SWEPT OFF THE PATH AND PILES UP AGAINST THE WALLS — and this is the
            // correction three independent seats converged on without any of them being shown
            // §8.2. Every one of them answered "which way would you walk" with a version of
            // *nothing about the ground influenced that*, and the third said exactly why:
            //
            //     "~1,250 single-pixel dark dots spread evenly over the entire floor, in every
            //      cell, at the same density. It is the loudest texture in the image and it has
            //      NO SHAPE — it doesn't pool in joints, doesn't gather at the wall bases,
            //      doesn't thin under the lamp. At phone size the floor reads as STATIC before
            //      it reads as stone."
            //
            // The channel was there the whole time and could not be seen through it. And the
            // deeper reason the channel alone was never going to carry §8.2 is §6.3's own logic
            // turned around: the channel's polish is delivered as a VALUE LIFT, and under a
            // carried lamp a value lift is read as LIGHT. The same seat said so in as many
            // words — "the warmth is entirely the torch". An asset authored to receive light
            // cannot signal with brightness, because brightness is what the light is saying.
            //
            // So the wear signal has to be STRUCTURAL, in something the lamp cannot explain:
            // where the loose material is and is not. §8.1 supplies it directly — traffic
            // clears a floor, and what it clears has to go somewhere:
            //
            //     ON THE CHANNEL   swept bare. Feet take the grit away.
            //     AGAINST A WALL   piled. It is where the traffic pushes it and nobody sweeps.
            //     NEGLECTED        heavier. No traffic and no care (§8.1's decay quadrant).
            //     ORDINARY FLOOR   sparse.
            //
            // "Polish means you are on the path. Decay means you have stepped off it" (§8.2),
            // carried by the absence of a texture rather than by the presence of a brightness.
            int gritId = -1;
            if (cfg.GritIds.Length > 0)
            {
                float rate;
                if (onRoute)          rate = cfg.GritRate * 0.10f;   // swept
                else if (neglected)   rate = cfg.GritRate * 1.35f;
                else if (occAdjacent) rate = cfg.GritRate * 1.20f;   // piled at the wall base
                else                  rate = cfg.GritRate * 0.55f;
                if (Unit(x, y, seed + 11) < rate)
                    gritId = cfg.GritIds[Hash(x, y, seed + 12) % cfg.GritIds.Length];
            }

            // AT MOST ONE EVENT PER CELL. Two reads as damage rather than as use, and §8.1's
            // failure test is "is the state of this thing explained by traffic and indifference?"
            int eventId = -1;
            for (int i = 0; i < cfg.EventIds.Length && i < cfg.EventRates.Length; i++)
            {
                if (Unit(x, y, seed + 20 + i) < cfg.EventRates[i])
                {
                    eventId = cfg.EventIds[i];
                    break;
                }
            }

            int channelId = -1;
            if (kind != ChannelKind.None && cfg.ChannelIds.Length >= 4)
                channelId = cfg.ChannelIds[(int)kind - 1];

            // §12.1 CONTACT OCCLUSION — RULED form, legal and REQUIRED: "a wall-top meeting
            // floor without its occluded edge is not purity, it is a missing plane". Placed by
            // ADJACENCY, one per edge that actually has a wall behind it, so a corner cell gets
            // two and an open-floor cell gets none. That is the clause's own test — the
            // treatment must answer to the geometry it sits on — and it is what the renderer's
            // whole-cell `DarkFloorModulate` cannot do: the same 8% multiply lands on a cell
            // with a wall to its north and one with a wall to its south-west, and its edge is
            // the cell's edge, which is a 32px square step and §8.3.1's lattice.
            //
            // IsWallTile, not !IsWalkable — the same predicate the wall autotiler and
            // FloorComposer's edge pass use. Walkability also excludes blocking props, and
            // borrowing it here is what produced the "pedestal halo" render bug around
            // furniture (ruled, Rafe 2026-08). Occlusion answers to walls.
            var occ = new List<int>(2);
            if (cfg.OcclusionIds.Length >= 4)
            {
                if (map.IsWallTile(x, y - 1)) occ.Add(cfg.OcclusionIds[0]);   // N
                if (map.IsWallTile(x + 1, y)) occ.Add(cfg.OcclusionIds[1]);   // E
                if (map.IsWallTile(x, y + 1)) occ.Add(cfg.OcclusionIds[2]);   // S
                if (map.IsWallTile(x - 1, y)) occ.Add(cfg.OcclusionIds[3]);   // W
            }

            result[(x, y)] = new FloorIncident(gritId, eventId, channelId, kind, neglected,
                                               occ.ToArray());
        }
        return result;
    }

    /// <summary>
    /// Has the traffic reached anywhere near this cell?
    ///
    /// The first version asked how OPEN the cell was — walkable neighbours in its 5x5 — on the
    /// idea that a cramped cell is an off-path one. It never fired once on the review scene
    /// (`neglected=0` in the capture log, which is what surfaced it), and it was measuring the
    /// wrong thing regardless: a wide dead-end hall is neglected and a narrow corridor on the
    /// main route is trodden, so openness and traffic are independent. §8.1's two axes are two
    /// dials and this is the TRAFFIC one.
    ///
    /// Distance from the channel is the direct question, and it is the one §8.2 asks: *"polish
    /// means you are on the path. Decay means you have stepped off it."* Two cells clear of the
    /// worn route is off it.
    /// </summary>
    private static bool NearChannel(Dictionary<(int X, int Y), ChannelKind> channel, int x, int y)
    {
        for (int dx = -2; dx <= 2; dx++)
        for (int dy = -2; dy <= 2; dy++)
            if (channel.ContainsKey((x + dx, y + dy))) return true;
        return false;
    }

    // =========================================================================================
    // THE TRODDEN CHANNEL — §8.2.1, RULED (Rafe, 2026-08-25)
    //
    //     The primary expression of legible wear on floors is a polished channel worn through a
    //     wider hall — the path of centuries of dead traffic, running down the middle of rooms
    //     and corridors that are wider than it. Ordinary floor flanks it. THE CHANNEL LEADS
    //     SOMEWHERE: stairs down, or rooms that matter.
    //
    // "Leads somewhere" is the load-bearing half and it is why this is not a medial axis. A
    // medial axis treads every room equally, including the dead ends, which says nothing: §8.2
    // makes the channel the navigation signal — "polish means you are on the path" only means
    // something if some floor is off it.
    //
    // THE ROUTE IS THE MAP'S LONGEST THROUGH-PATH, found by double BFS: the farthest cell from
    // an arbitrary start, then the farthest cell from that one. That is the graph diameter, and
    // on a dungeon floor it is the trunk the traffic would actually wear — it necessarily runs
    // through the connective tissue rather than into a side room.
    //
    // ⚠ IN THE SHIPPING GAME THE STAIRS OVERRIDE THIS, and the clause names them first. The
    // review scene has no stairs at all — deliberately, because the tier-0 harness removed every
    // losable state from it after the turn-limit incident — so the diameter is what stands in.
    // Stated here rather than left as a silent equivalence: they are not the same rule, and the
    // one that ships is the one with stairs in it.
    // =========================================================================================

    private static Dictionary<(int X, int Y), ChannelKind> TroddenChannel(GameMap map, int seed)
    {
        var start = FirstWalkable(map);
        var channel = new Dictionary<(int X, int Y), ChannelKind>();
        if (start == null) return channel;

        // THE ROUTE RUNS BETWEEN THE TWO LARGEST ROOMS, NOT BETWEEN THE TWO FARTHEST CELLS.
        //
        // The first version took the graph DIAMETER — farthest cell from anywhere, then farthest
        // from that — and it is structurally wrong in a way no tuning reaches: **the farthest
        // cell from anywhere is always the end of a dead end.** So the diameter is drawn INTO
        // cul-de-sacs by construction, and a cul-de-sac is precisely where traffic does not go.
        //
        // Measured on this session's own review scene, from the engine's channel map:
        //
        //     ##MM#.......      the four-cell dead-end stub built to be the NEGLECTED passage
        //     ##.MFMMMMR..      came out as the most trodden ground in the scene, and room A's
        //     #####.xxxxxx      north row came out neglected instead
        //
        // The scene was built with this risk named in its own comment and it fired anyway, which
        // is the useful part: the defect is in the derivation, not in the map. §8.2.1 says where
        // a channel goes — "the channel leads somewhere: STAIRS DOWN, OR ROOMS THAT MATTER" —
        // and a dead-end stub is neither. Rooms are the endpoints; corridors are what the route
        // passes THROUGH on its way between them.
        //
        // ⚠ IN THE SHIPPING GAME THE STAIRS OVERRIDE THIS, and the clause names them first. The
        // review scene has no stairs at all — the tier-0 harness removed every losable state from
        // it after the turn-limit incident — so "the two largest rooms" is what stands in. They
        // are not the same rule and the one that ships is the one with stairs in it.
        var rooms = Rooms(map);
        (int X, int Y) a, b;
        Dictionary<(int X, int Y), (int X, int Y)> cameFrom;
        if (rooms.Count >= 2)
        {
            rooms.Sort((p, q) => q.Count.CompareTo(p.Count));
            a = Centre(rooms[0]);
            b = Centre(rooms[1]);
            Farthest(map, a, out cameFrom);          // BFS tree rooted at a; b walks back up it
            if (!cameFrom.ContainsKey(b) && !b.Equals(a)) return channel;   // not connected
        }
        else
        {
            // One room or none: nothing to route between, so fall back to the diameter and say
            // so. A single-room map has no "leads somewhere" to express.
            a = Farthest(map, start.Value, out _);
            b = Farthest(map, a, out cameFrom);
        }

        var spine = new List<(int X, int Y)>();
        var cur = b;
        while (true)
        {
            spine.Add(cur);
            if (cur.Equals(a)) break;
            if (!cameFrom.TryGetValue(cur, out var prev)) break;
            cur = prev;
        }

        var onSpine = new HashSet<(int X, int Y)>(spine);
        foreach (var s in spine)
        {
            // A one-wide corridor on the route is TRODDEN WALL TO WALL — §8.2.1 in terms:
            // "the traffic had no room to spread". Anything wider gets a channel narrower
            // than the hall, with ordinary floor flanking it.
            bool oneWideNS = !map.IsWalkable(s.X - 1, s.Y) && !map.IsWalkable(s.X + 1, s.Y);
            bool oneWideEW = !map.IsWalkable(s.X, s.Y - 1) && !map.IsWalkable(s.X, s.Y + 1);
            if (oneWideNS || oneWideEW)
            {
                channel[s] = ChannelKind.Full;
                continue;
            }

            channel[s] = ChannelKind.Mid;

            // BOTH SHOULDERS, ALWAYS — and the first version's hash gate here was the defect.
            //
            // It added one shoulder or the other with probability 0.62, on the reasoning that a
            // band of constant width is §12.1's "uniform ribbon ... answers to nothing". The
            // reasoning was right and the mechanism was wrong: dropping a shoulder does not make
            // the band's edge wander, it makes the band END ON A CELL BOUNDARY. A Mid cell with
            // ordinary floor beside it meets it along a perfectly straight 32-pixel line, which
            // is §8.3.1's lattice arriving through the one feature that is supposed to read as
            // wear. Visible in the capture as square steps down the side of the polished path.
            //
            // The wander belongs INSIDE the shoulder tile, not in whether the tile is there.
            // A shoulder overlay carries a soft, per-row wandering alpha edge (see
            // `compose_family.build_channel`), so the band's real edge moves by several pixels
            // down its length and never coincides with a cell boundary. Every Mid cell therefore
            // gets a shoulder on each side where there is floor to put one.
            for (int d = -1; d <= 1; d += 2)
            {
                var side = (X: s.X + d, Y: s.Y);
                if (!map.IsWalkable(side.X, side.Y) || onSpine.Contains(side)) continue;
                if (!channel.ContainsKey(side))
                    channel[side] = d < 0 ? ChannelKind.Left : ChannelKind.Right;
            }
        }
        return channel;
    }

    /// <summary>
    /// Connected groups of ROOM cells — a cell is a room cell when its whole 3x3 neighbourhood
    /// is walkable, which is true in the body of an open area and false in any corridor one or
    /// two cells wide, and false at every wall edge.
    ///
    /// Deliberately crude, and crude in the safe direction: it under-reports rooms rather than
    /// over-reporting them, so a corridor can never be mistaken for a route endpoint. That is
    /// the error that matters here — the whole point of the change is that the route must not
    /// terminate in something that is not a room.
    /// </summary>
    private static List<List<(int X, int Y)>> Rooms(GameMap map)
    {
        bool IsRoomCell(int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (!map.IsWalkable(x + dx, y + dy)) return false;
            return true;
        }

        var seen = new HashSet<(int X, int Y)>();
        var groups = new List<List<(int X, int Y)>>();
        for (int x = 0; x < map.Width; x++)
        for (int y = 0; y < map.Height; y++)
        {
            if (!IsRoomCell(x, y) || !seen.Add((x, y))) continue;
            var group = new List<(int X, int Y)>();
            var stack = new Stack<(int X, int Y)>();
            stack.Push((x, y));
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                group.Add(c);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var n = (X: c.X + dx, Y: c.Y + dy);
                    if (IsRoomCell(n.X, n.Y) && seen.Add(n)) stack.Push(n);
                }
            }
            groups.Add(group);
        }
        return groups;
    }

    /// <summary>The cell of a room nearest its own centroid — a stable, interior endpoint.</summary>
    private static (int X, int Y) Centre(List<(int X, int Y)> room)
    {
        double cx = 0, cy = 0;
        foreach (var c in room) { cx += c.X; cy += c.Y; }
        cx /= room.Count; cy /= room.Count;
        var best = room[0];
        double bd = double.MaxValue;
        foreach (var c in room)
        {
            double d = (c.X - cx) * (c.X - cx) + (c.Y - cy) * (c.Y - cy);
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    private static (int X, int Y)? FirstWalkable(GameMap map)
    {
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            if (map.IsWalkable(x, y)) return (x, y);
        return null;
    }

    private static (int X, int Y) Farthest(GameMap map, (int X, int Y) from,
                                           out Dictionary<(int X, int Y), (int X, int Y)> cameFrom)
    {
        cameFrom = new Dictionary<(int X, int Y), (int X, int Y)>();
        var seen = new HashSet<(int X, int Y)> { from };
        var q = new Queue<(int X, int Y)>();
        q.Enqueue(from);
        var last = from;
        int[] dx = { 0, 0, -1, 1 };
        int[] dy = { -1, 1, 0, 0 };
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            last = c;
            for (int i = 0; i < 4; i++)
            {
                var n = (X: c.X + dx[i], Y: c.Y + dy[i]);
                if (!map.IsWalkable(n.X, n.Y) || !seen.Add(n)) continue;
                cameFrom[n] = c;
                q.Enqueue(n);
            }
        }
        return last;
    }
}
