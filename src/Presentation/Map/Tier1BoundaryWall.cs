using System.Linq;
using System.Text.Json;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;
using Godot;

namespace UnderWarden.Presentation.Map;

/// <summary>
/// Lays the tier-one Boundary WALL family, and the void beyond it.
///
/// It replaces the theme's wall sprite on every wall cell, exactly the way
/// <see cref="Tier1AshlarFloor"/> replaces the floor's, and for three reasons that the theme's
/// mask table cannot serve.
///
/// ── 1. THE MASK TABLE DRAWS A FRONT FACE WHERE THERE IS NO REVEAL ────────────────────────────
/// ART-BIBLE-v0 §3: a tile shows a front face exactly where floor lies to its SOUTH.
/// `DungeonRenderer` collapses cardinal masks 7 and 11 both to 3 — and mask 7 is a wall whose
/// SOUTH NEIGHBOUR IS WALL (floor lies north of it), while mask 3's south neighbour is floor. One
/// tile therefore serves two opposite geometries, and on the face side it draws a reveal into the
/// middle of a solid mass. Measured on the four review specs: **13 in-map cells per scene** plus
/// the whole top border row — the south walls of every room, which is the most-looked-at wall in
/// any of them.
///
/// The collapse is not a bug in the renderer; its comment says plainly that it was fitted to the
/// old sandstone tileset, where masks 194–197 drew directional T-junctions that looked wrong on
/// plain room edges. It is a bug *here*, under §3, and it could not have been seen before now:
/// the walls in every capture so far have been magenta programmer-art mocks with no planes in
/// them at all. **This class computes the cardinal mask itself, without the collapse.**
///
/// ── 2. EDGE MATCHING NEEDS THE NEIGHBOURS, AND `PickVariant` CANNOT SEE THEM ──────────────────
/// §8.3.2 makes edge-matched sets legal — *matching is agreement, not constancy* — and a run of
/// masonry only reads as one wall if the block crossing a tile boundary is drawn the same on both
/// sides of it. `TileThemeConfig.PickVariant` chooses from a position hash, so two neighbours
/// choose independently and a crossing block is a coin flip. Here the tile is chosen by the two
/// BOUNDARY keys it shares with its neighbours, so both tiles read the same key and draw the same
/// block: the seam is zero rather than small.
///
/// ── 3. THE VOID IS NOT A WALL ────────────────────────────────────────────────────────────────
/// A cell with no floor anywhere near it is not masonry anybody can see — it is the dark beyond
/// the room. Drawing it as stone spends the scene's whole value range on rock nobody looks at and
/// leaves the wall that bounds the room reading as a bright ribbon around a floor. So wall cells
/// are laid in RINGS: ring 1 is the wall a room actually has, and everything past `void_ring` is
/// void.
///
/// **`void_ring` STARTED AT 2 AND THE SEATS MOVED IT TO 1.** Two came from `WALL-RECIPE.md` §2.2's
/// measured *"every room boundary in the bar is two tiles or more"* — which is a statement about
/// MAP GEOMETRY, and was read here as one about how many rings are drawn as lit stone. Round 2's
/// seat found the consequence: *"More of the same stuff … it is wall, and it goes on. What it is
/// NOT is dark."* At two rings the void only appears where a mass is five cells deep or more, and
/// an ordinary dungeon's masses are two to four — so the darkness beyond the walls would
/// essentially never be seen. The map still has its two cells of mass; you simply cannot see
/// through the first one, which is also what a lamp at floor level actually shows you.
///
/// ⚠ **THE VOID VALUE IS NOT RULED HERE.** Three candidates ship and the panel switches between
/// them; §13.1 gives the choice to Rafe, in scene, on the device.
///
/// ⚠ **NO CONTACT SEAM IS DRAWN BY THIS CLASS.** §12.1 makes plane-boundary occlusion mandatory
/// and `WALL-RECIPE.md` §3.1 measured where it belongs — on the FLOOR cell, which is the one that
/// knows what adjoins it. `Tier1FloorOverlays` already draws it per edge. Adding a second copy on
/// the wall side would double the darkening and put a dark edge on every side of every wall tile,
/// which is the definition of a ring.
/// </summary>
public static class Tier1BoundaryWall
{
    private const int T = 32;

    private sealed class Config
    {
        public string Family = "";
        public string Root = "";
        public int Families = 3;
        public int SaltV, SaltH;
        public int VoidRing = 2;
        public int Variants = 1;
        public int Ages = 1;
        public readonly Dictionary<string, int> Face = new();
        public readonly Dictionary<string, int> TopH = new();
        public readonly Dictionary<string, int> TopV = new();
        public readonly List<int> Void = new();
        public readonly List<(int Salt, string Tag, int X, int Y, int Expect)> EdgeCheck = new();
        public double TopValue, FaceValue;
    }

    /// <summary>FNV-1a 64 over "salt:tag:x:y" — the composer's arithmetic, restated.</summary>
    private static ulong Fnv(int salt, string tag, int x, int y)
    {
        ulong h = 0xCBF29CE484222325UL;
        void Feed(string s)
        {
            foreach (char c in s) h = (h ^ (byte)c) * 0x100000001B3UL;
            h = (h ^ 0x3A) * 0x100000001B3UL;
        }
        Feed(salt.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Feed(tag);
        Feed(x.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Feed(y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return h;
    }

    private static int Key(Config c, int salt, string tag, int x, int y)
        => (int)(Fnv(salt, tag, x, y) % (ulong)c.Families);

    private static Config? Load(string resPath, out string status)
    {
        status = "";
        using var f = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
        if (f == null) { status = $"manifest unreadable: {resPath}"; return null; }
        var doc = JsonDocument.Parse(f.GetAsText());
        var r = doc.RootElement;
        var c = new Config
        {
            Family = r.GetProperty("family").GetString() ?? "",
            Root = resPath.Substring(0, resPath.LastIndexOf('/') + 1),
            Families = r.GetProperty("edge_families").GetInt32(),
            VoidRing = r.TryGetProperty("void_ring", out var vr) ? vr.GetInt32() : 2,
            SaltV = r.GetProperty("salts").GetProperty("v").GetInt32(),
            SaltH = r.GetProperty("salts").GetProperty("h").GetInt32(),
            TopValue = r.GetProperty("planes").GetProperty("top_value").GetDouble(),
            FaceValue = r.GetProperty("planes").GetProperty("face_value").GetDouble(),
        };
        c.Variants = r.GetProperty("variants").GetInt32();
        c.Ages = r.TryGetProperty("ages", out var ag) ? ag.GetInt32() : 1;
        var table = r.GetProperty("table");
        foreach (var e in table.GetProperty("face").EnumerateObject()) c.Face[e.Name] = e.Value.GetInt32();
        foreach (var e in table.GetProperty("top_h").EnumerateObject()) c.TopH[e.Name] = e.Value.GetInt32();
        foreach (var e in table.GetProperty("top_v").EnumerateObject()) c.TopV[e.Name] = e.Value.GetInt32();
        foreach (var e in table.GetProperty("void").EnumerateObject()) c.Void.Add(e.Value.GetInt32());
        foreach (var e in r.GetProperty("edge_check").EnumerateArray())
        {
            var a = e.EnumerateArray().ToArray();
            c.EdgeCheck.Add((a[0].GetInt32(), a[1].GetString() ?? "", a[2].GetInt32(),
                             a[3].GetInt32(), a[4].GetInt32()));
        }
        // Tile ids are laid out by the composer's own file naming; the manifest carries the file
        // for each id so nothing here has to guess a pattern.
        foreach (var t in r.GetProperty("tiles").EnumerateArray())
            _files[t.GetProperty("id").GetInt32()] = t.GetProperty("file").GetString() ?? "";
        return c;
    }

    private static readonly Dictionary<int, string> _files = new();

    /// <summary>
    /// How many void candidates the last-applied family shipped. Read by the rig panel so the
    /// toggle offers exactly the candidates that exist rather than a hard-coded three — a panel
    /// that offered a fourth would be proposing a value, and the void is Rafe's to rule.
    /// </summary>
    public static int LastVoidCount { get; private set; }

    /// <summary>
    /// Does a ring-1 neighbour on the given axis face floor across that axis?
    ///
    /// Ring 2 stands behind ring 1 and has no floor of its own to be parallel to. It takes the
    /// orientation of the cell it backs, so a wall's two courses agree instead of crossing —
    /// which is what a two-stone-thick wall actually looks like from above.
    /// </summary>
    private static bool HasNeighbourFacing(GameMap map, int x, int y, bool vertical)
    {
        foreach (var (dx, dy) in vertical ? new[] { (-1, 0), (1, 0) } : new[] { (0, -1), (0, 1) })
        {
            int nx = x + dx, ny = y + dy;
            if (!map.InBounds(nx, ny) || !map.IsWallTile(nx, ny)) continue;
            if (vertical)
            {
                if ((map.InBounds(nx - 1, ny) && !map.IsWallTile(nx - 1, ny))
                 || (map.InBounds(nx + 1, ny) && !map.IsWallTile(nx + 1, ny))) return true;
            }
            else
            {
                if ((map.InBounds(nx, ny - 1) && !map.IsWallTile(nx, ny - 1))
                 || (map.InBounds(nx, ny + 1) && !map.IsWallTile(nx, ny + 1))) return true;
            }
        }
        return false;
    }

    /// <summary>Chebyshev distance to the nearest walkable cell, capped — which ring this is.</summary>
    private static int RingOf(GameMap map, int x, int y, int cap)
    {
        for (int r = 1; r <= cap; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != r) continue;
                    if (!map.IsWallTile(x + dx, y + dy) && map.InBounds(x + dx, y + dy)) return r;
                }
        return cap + 1;
    }

    private const string BindNode = "Tier1Binding";
    private const string FaceNode = "Tier1Face";
    private const string CapBandNode = "Tier1CapBand";   // #212: the cap re-laid over an against-wall prop
    private const string EastFaceNode = "Tier1EastFace"; // #211: the wall end's east face (§3.2)

    /// <summary>
    /// Does this overlay child cover EXACTLY its parent cell, and nothing of the cell next door?
    ///
    /// The fourth cross-check in this family, and it exists for the same reason as the other
    /// three: `edge_check` says the engine agrees with the composer about the bond, `stone_check`
    /// about the material, `occlusion_check` about the rungs — and each of them REFUSES rather
    /// than warning, because a silent disagreement is a defect nothing downstream reports.
    ///
    /// This one says the engine agrees with itself about WHERE. #185/#186 were a half-cell
    /// placement error that survived every one of those checks, every unit test, and a device
    /// gate, because all of them ask what is drawn and none of them asked where. A face half a
    /// cell out is §3's violation — a reveal cut into ground that is not this cell's — arriving
    /// through placement rather than through the mask, which is the layer
    /// <see cref="Logic.Map.WallMaskPolicy"/> was built to make safe.
    /// </summary>
    private static bool Contained(Sprite2D parent, Sprite2D child, out string why)
    {
        var pr = parent.GetRect();
        var cr = child.GetRect();
        if (cr.Position != pr.Position || cr.Size != pr.Size)
        {
            why = $"child rect {cr.Position}+{cr.Size} against the cell's {pr.Position}+{pr.Size}"
                + (child.Centered != parent.Centered
                   ? $" (centred={child.Centered} under a parent centred={parent.Centered})" : "");
            return false;
        }
        why = "";
        return true;
    }

    /// <summary>Drop any overlay this class put on a cell, so a re-lay never stacks two.</summary>
    private static void ClearOverlays(Sprite2D s)
    {
        foreach (var n in new[] { FaceNode, BindNode, CapBandNode, EastFaceNode })
        {
            var old = s.GetNodeOrNull<Sprite2D>(n);
            if (old != null) { s.RemoveChild(old); old.QueueFree(); }
        }
    }

    /// <summary>
    /// The CAP field: one continuous surface, cut into windows, chosen by world position.
    ///
    /// RULED (Rafe, 2026-08-30): the tops read as dim floor — tile-frequency seams, featureless,
    /// too close to the ground. The seams were §8.3.3's forced bed joint, and it could not be
    /// removed while the cap was made of blocks whose values had to be addressed per tile.
    ///
    /// So the cap stopped being blocks. It is found rock now (§7.4: *the Boundary is mostly found
    /// stone with orc work pinned into it*), authored as ONE field and cut into an N×N grid of
    /// windows. Cell (x, y) draws the window at (x mod N, y mod N), so two adjacent cells draw two
    /// adjacent windows and **the tile boundary is not a boundary at all** — continuous by
    /// construction rather than by agreement, which is stronger than edge matching and free.
    ///
    /// The VOID is the same field carried down to ambient, because it is the same rock unlit —
    /// ruled at the same gate: *unlit rock with faint grain, not a flat fill.*
    /// </summary>
    private sealed class Cap
    {
        public string Root = "";
        public int FieldTiles = 16;
        public readonly Dictionary<string, int> Window = new();
        public readonly List<Dictionary<string, int>> Void = new();
        public readonly Dictionary<int, string> Files = new();
    }

    private static Cap? LoadCap(string resPath, out string status)
    {
        status = "";
        using var f = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
        if (f == null) { status = $"cap manifest unreadable: {resPath}"; return null; }
        var doc = JsonDocument.Parse(f.GetAsText());
        var r = doc.RootElement;
        var c = new Cap
        {
            Root = resPath.Substring(0, resPath.LastIndexOf('/') + 1),
            FieldTiles = r.GetProperty("field_tiles").GetInt32(),
        };
        foreach (var e in r.GetProperty("table").EnumerateObject())
            c.Window[e.Name] = e.Value.GetInt32();
        foreach (var e in r.GetProperty("void_table").EnumerateObject())
        {
            var d = new Dictionary<string, int>();
            foreach (var w in e.Value.EnumerateObject()) d[w.Name] = w.Value.GetInt32();
            c.Void.Add(d);
        }
        foreach (var t in r.GetProperty("tiles").EnumerateArray())
            c.Files[t.GetProperty("id").GetInt32()] = t.GetProperty("file").GetString() ?? "";
        return c;
    }

    private sealed class Bindings
    {
        public string Root = "";
        public double FaceRate;
        public readonly List<(int Id, string Kind, string Plane, string File)> Tiles = new();
    }

    private static Bindings? LoadBindings(string resPath, out string status)
    {
        status = "";
        using var f = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
        if (f == null) { status = $"binding manifest unreadable: {resPath}"; return null; }
        var doc = JsonDocument.Parse(f.GetAsText());
        var r = doc.RootElement;
        var b = new Bindings
        {
            Root = resPath.Substring(0, resPath.LastIndexOf('/') + 1),
            // FACE ONLY. §8.3.1, ruled at the gate: wall tops are incident-free, and an overlay
            // at a hashed position on a top plane is incident whatever it depicts. The manifest
            // no longer declares a top rate; reading one would be reading a key the composer
            // deliberately stopped writing.
            FaceRate = r.GetProperty("rates").GetProperty("face").GetDouble(),
        };
        foreach (var t in r.GetProperty("tiles").EnumerateArray())
            b.Tiles.Add((t.GetProperty("id").GetInt32(),
                         t.GetProperty("kind").GetString() ?? "",
                         t.GetProperty("plane").GetString() ?? "",
                         t.GetProperty("file").GetString() ?? ""));
        return b;
    }

    /// <param name="voidRingOverride">
    /// A CAPTURE-TIME override of the manifest's `void_ring`, and it exists so that a round can
    /// depart from a RULED value without editing the ruled artefact.
    ///
    /// §12.1a (Rafe, 2026-09-03) ruled the void dark by OCCLUSION rather than by a ring, and the
    /// manifest carries `void_ring: 0` for it. That ruling's implementation — making the lamp
    /// occlude against the wall faces — is RECORDED-OUTSTANDING, so at zero the renderer lights
    /// every cell of unexcavated mass and the lamp shines through rock. A round that needs a
    /// room with a real dark beyond it therefore has to run the flat-dark fallback, and the
    /// honest way to do that is on the command line, echoed into the capture log, rather than by
    /// quietly moving a number a ruling put in a manifest.
    ///
    /// Null means "whatever the manifest says", which is what every build does.
    /// </param>
    public static string Apply(TileLayer tileLayer, GameMap map, string manifestResPath,
                               int voidChoice, string? bindingManifest = null,
                               string? capManifest = null, int? voidRingOverride = null)
    {
        var cfg = Load(manifestResPath, out string status);
        if (cfg == null) return $"[Tier1] boundary wall: NOT APPLIED — {status}";
        int manifestRing = cfg.VoidRing;
        if (voidRingOverride is int vr) cfg.VoidRing = vr;

        // THE CROSS-CHECK, BEFORE ANYTHING IS LAID. The composer and this class each compute the
        // boundary keys; if they disagree, every crossing block is drawn from two different
        // halves and the run stops being masonry. A duplicate with an enforcement is a different
        // thing from a duplicate with a comment.
        foreach (var (salt, tag, x, y, expect) in cfg.EdgeCheck)
        {
            int got = Key(cfg, salt, tag, x, y);
            if (got != expect)
                return $"[Tier1] boundary wall: REFUSED — edge-family cross-check failed at "
                     + $"({x},{y}) salt={salt} tag={tag}: composer said {expect}, engine says {got}.";
        }

        if (cfg.Void.Count == 0) return "[Tier1] boundary wall: REFUSED — no void candidates.";
        LastVoidCount = cfg.Void.Count;
        int vc = System.Math.Clamp(voidChoice, 0, cfg.Void.Count - 1);

        Cap? cap = null;
        if (capManifest is { Length: > 0 })
        {
            cap = LoadCap(capManifest, out string capStatus);
            if (cap == null) return $"[Tier1] boundary wall: REFUSED — {capStatus}";
        }

        Bindings? bind = null;
        string bindStatus = "none declared";
        if (bindingManifest is { Length: > 0 })
        {
            bind = LoadBindings(bindingManifest, out bindStatus);
            if (bind == null) return $"[Tier1] boundary wall: REFUSED — {bindStatus}";
        }

        // The floor's field, computed the same way the floor computes it. Not cached across the
        // two systems on purpose: sharing a mutable field between the floor painter and the wall
        // painter would couple two things that only need to AGREE, and agreement here is free —
        // it is a pure function of the map.
        var tf = TrafficField.ComputeFromMap(map);
        var traffic = tf.Field;

        int face = 0, top = 0, voidCells = 0, missing = 0, faceSuppressed = 0;
        int firstSurface = 0, occludedMass = 0;   // the light-mask split, reported
        int capBands = 0;                          // #212: cap bands re-laid over against-wall props
        int eastFaces = 0;                         // #211: east faces on wall ends, corners, pillars
        int bound = 0, capLaid = 0, capVoid = 0;
        var ageHist = new int[System.Math.Max(1, 8)];
        var ageMap = new Dictionary<(int X, int Y), int>();
        var boundKinds = new Dictionary<string, int>();
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (!map.IsWallTile(x, y)) continue;
                if (!tileLayer.TileSprites.TryGetValue((x, y), out var node) || node is not Sprite2D s)
                    continue;

                // VoidRing == 0 MEANS NO VOID, and it is not a degenerate setting — it is the
                // remedy for §12.1's ring outline. `RingOf` is a classification that changes at a
                // cell boundary, so it puts a luminance step at a grid position; round 8's seat
                // read the result as two ruled 197px verticals in the dark with nothing standing
                // to explain them. At zero there is no classification and therefore no step: every
                // wall cell is capped, and distant mass is dark because the lamp does not reach it
                // rather than because a tile was authored dark (§6.3 — assets receive light, they
                // never depict it). Short-circuited rather than expressed as a huge cap, because
                // RingOf is O(r²) per cell.
                //
                // ⚠ THE PREDICATE, NOT THE RING. The first attempt set `ring = 1` at zero and left
                // the test as `ring > cfg.VoidRing` — which is `1 > 0`, so every wall cell in the
                // map became void and the capture came back `cap=0+216void`: the exact inverse of
                // the intent, loudly, in the renderer's own counters. Whether a cell is void is
                // one question and it is asked once, here.
                bool isVoid = cfg.VoidRing > 0 && RingOf(map, x, y, cfg.VoidRing) > cfg.VoidRing;
                int ring = isVoid ? cfg.VoidRing + 1 : 1;
                bool southOpenCache = !map.IsWallTile(x, y + 1) && map.InBounds(x, y + 1);

                // THE WINDOW THIS CELL SEES INTO THE FIELD — world position and nothing else. No
                // hash, no variant, no per-cell decision of any kind. That IS the mechanism:
                // adjacent cells get adjacent windows, so there is no seam left to match.
                string wkey = cap == null ? "" :
                    $"{((x % cap.FieldTiles) + cap.FieldTiles) % cap.FieldTiles}," +
                    $"{((y % cap.FieldTiles) + cap.FieldTiles) % cap.FieldTiles}";

                int id;
                if (isVoid)
                {
                    voidCells++;
                    if (cap != null && vc < cap.Void.Count
                        && cap.Void[vc].TryGetValue(wkey, out int vid)
                        && cap.Files.TryGetValue(vid, out var vfile))
                    {
                        var vtex = GD.Load<Texture2D>(cap.Root + vfile);
                        if (vtex != null)
                        {
                            s.Texture = vtex;
                            s.FlipH = false;
                            s.FlipV = false;
                            ClearOverlays(s);
                            capVoid++;
                            continue;
                        }
                    }
                    id = cfg.Void[vc];
                }
                else
                {
                    // §3, computed here rather than read off the collapsed mask: a face exists
                    // exactly where the SOUTH neighbour is not wall.
                    bool southOpen = southOpenCache;
                    // Ring 2 never shows a face: its south neighbour is ring-1 wall by
                    // construction, so the test above already refuses it. Counted anyway, because
                    // a number that is always zero is the cheapest possible regression test.
                    if (!southOpen && ring == 1) faceSuppressed++;

                    // WHICH WAY THE COURSES RUN. A mason lays them parallel to the face, so the
                    // orientation follows the direction of the floor this wall bounds: floor to
                    // the north or south means the wall runs east-west, floor to the east or west
                    // means it runs north-south. Decided here rather than baked, because the same
                    // material has to serve both and an edge-matched tile cannot be rotated at
                    // run time without relabelling its own edges (§8.3.3).
                    bool ns = (!map.IsWallTile(x, y - 1) && map.InBounds(x, y - 1))
                              || southOpenCache;
                    bool ew = (!map.IsWallTile(x - 1, y) && map.InBounds(x - 1, y))
                              || (!map.IsWallTile(x + 1, y) && map.InBounds(x + 1, y));
                    if (ring > 1)
                    {
                        // Ring 2 has no floor of its own to face; it takes the orientation of the
                        // ring-1 cell it stands behind, so a wall's two courses agree.
                        ns = HasNeighbourFacing(map, x, y, vertical: false);
                        ew = HasNeighbourFacing(map, x, y, vertical: true);
                    }
                    bool horizontal = ns || !ew;   // ties go to east-west, the commoner run

                    int ka, kb;
                    if (horizontal)
                    {
                        ka = Key(cfg, cfg.SaltV, "v", x, y);
                        kb = Key(cfg, cfg.SaltV, "v", x + 1, y);
                    }
                    else
                    {
                        ka = Key(cfg, cfg.SaltH, "h", x, y);
                        kb = Key(cfg, cfg.SaltH, "h", x, y + 1);
                    }
                    int var = (int)(Fnv(90777, horizontal ? "h" : "v", x, y) % (ulong)cfg.Variants);

                    // ── AGE, AND IT COMES FROM THE FLOOR'S OWN FIELD ────────────────────────
                    // RULED at the gate: *"walls have opted out of history … wall aging at the
                    // base courses, keyed to the existing traffic/age fields."*
                    //
                    // A wall's traffic is not its own — nobody walks on a wall. It is the traffic
                    // of the FLOOR IT FACES, which is the ground whose boots have been rubbing
                    // its foot for four hundred years. So the age of a reveal is read from the
                    // cell to its south, the same cell §6.5 measures it against.
                    //
                    // TrafficField puts vaults and shrines at exactly zero, and that zero is what
                    // makes this keyed rather than noised: a sealed room's wall stays sharp, and
                    // sharpness then MEANS something — nobody comes here.
                    int age = 0;
                    if (southOpen && traffic != null && cfg.Ages > 1)
                    {
                        int t = traffic[x, y + 1];
                        age = System.Math.Clamp(t * cfg.Ages / 256, 0, cfg.Ages - 1);
                        ageHist[age]++;
                        ageMap[(x, y)] = age;
                    }
                    string k = southOpen ? $"{ka},{kb},{var},{age}" : $"{ka},{kb},{var}";
                    var tbl = southOpen ? cfg.Face : (horizontal ? cfg.TopH : cfg.TopV);
                    if (!tbl.TryGetValue(k, out id)) { missing++; continue; }
                    if (southOpen) face++; else top++;
                }

                // ── THE CAP IS THE BASE ON EVERY WALL CELL ──────────────────────────────────
                // A reveal draws the cap window underneath and its FACE-ONLY tile over it, so the
                // two planes stop sharing an asset. A face tile carrying its own top band would
                // put block material against the field beside it — a seam at exactly the boundary
                // this pass exists to remove, arriving through the one tile class that still
                // painted its own cap.
                ClearOverlays(s);
                bool capBase = false;
                if (cap != null && cap.Window.TryGetValue(wkey, out int cid)
                    && cap.Files.TryGetValue(cid, out var cfile))
                {
                    var ctex = GD.Load<Texture2D>(cap.Root + cfile);
                    if (ctex != null)
                    {
                        s.Texture = ctex;
                        s.FlipH = false;
                        s.FlipV = false;
                        capBase = true;
                        capLaid++;
                    }
                }

                if (!_files.TryGetValue(id, out var file)) { missing++; continue; }
                var tex = GD.Load<Texture2D>(cfg.Root + file);
                if (tex == null) { missing++; continue; }

                if (capBase && southOpenCache)
                {
                    // The face rides over the cap as a child, so the cap keeps the cell's base.
                    //
                    // ⚠ `Centered` FOLLOWS THE PARENT AND IS NOT A CONSTANT — #185/#186.
                    //
                    // It was `Centered = true` on a child of a tile sprite the renderer creates
                    // `Centered = false` at the cell's TOP-LEFT. A centred child at local (0,0)
                    // therefore drew its 32px texture CENTRED ON THE CELL'S CORNER: half a tile
                    // up and half a tile left of the cell it belongs to. Every reveal in the game
                    // was drawn a half-cell out of place, and it was invisible for as long as the
                    // neighbour was also wall — which is why the walk found it at a corridor
                    // mouth, where the neighbour is FLOOR and the face lands on walkable ground.
                    //
                    // Measured on the corridor mouth (8,11), the floor cell in the doorway: a
                    // 52.8-level column step at EXACTLY the half-tile, the largest step in the
                    // cell, with wall-face values to its right. Bisected without changing any
                    // code, using the flag that selects this path: captured with `--wall-cap`
                    // omitted, so the face is set on the tile sprite instead of on a child, the
                    // half-tile step is GONE and the largest step falls back to 30.1 at column 4,
                    // which is the west contact occlusion sitting correctly on the cell edge.
                    //
                    // This is also #186's *"its left edge lands mid-tile, which no tile-laid
                    // family can produce"*. A centred child on a cell corner can produce exactly
                    // that, and it is the only thing in this pipeline that can.
                    //
                    // Taking the parent's value rather than writing `false` is deliberate: the
                    // claim is that the child sits where its cell sits, and that claim survives
                    // the renderer changing its mind. The engine then CHECKS it below rather than
                    // trusting this comment.
                    var fs = new Sprite2D
                    {
                        Name = FaceNode, Texture = tex, Centered = s.Centered,
                        TextureFilter = CanvasItem.TextureFilterEnum.Nearest, ZIndex = 1,
                    };
                    s.AddChild(fs);
                    if (!Contained(s, fs, out string why))
                        return $"[Tier1] boundary wall: REFUSED — the face at ({x},{y}) is drawn "
                             + $"outside its own cell: {why}. A reveal in a neighbouring cell is "
                             + "§3's violation arriving through placement instead of through the "
                             + "mask (#185/#186).";
                }
                else if (!capBase)
                {
                    s.Texture = tex;
                    // A wall tile carries no orientation: flipping one relabels its edges and it
                    // stops agreeing with its neighbours (§8.3.3's cost of edge matching).
                    s.FlipH = false;
                    s.FlipV = false;
                }

                // ── THE ORC LAYER ────────────────────────────────────────────────────────────
                // Placed here, per cell, from the cell's WORLD ADDRESS — never baked into the
                // segment. §8.3.1: a binding drawn into a tile is a binding on every cell that
                // tile lands on, which is thirty identical repairs to a wall nobody repaired
                // thirty times.
                if (bind != null && ring == 1 && southOpenCache)
                {
                    const string plane = "face";

                    // ── BINDINGS ARE KEYED TO TRAFFIC (RULED, Rafe, 2026-09-03) ──────────────
                    //
                    // *"Bindings re-key to the traffic field — density on route-adjacent walls,
                    // thresholds, chokepoints; sparse on remote runs (register: orcs repair what
                    // use breaks)."*
                    //
                    // A flat rate over a position hash spread eleven repairs evenly across every
                    // wall cell in the map, and most wall cells are nowhere near the lamp or the
                    // route — so the critic's flip list said *"nothing rigged, lashed or pinned
                    // anywhere in the lit radius"* on a build carrying eleven bindings. §7.1 was
                    // being answered where nobody could see it.
                    //
                    // The field is the floor's own `TrafficField`, already read a few lines above
                    // for the aging, so the two systems cannot disagree about where use is. A
                    // wall's traffic is the traffic of the ground at its foot — nobody walks on a
                    // wall — which is the same reading the age uses.
                    //
                    // The register carries the shape: orcs repair what use breaks. A wall beside
                    // the spine gets worked on; a remote run gets left. So the rate rises with
                    // traffic and does not fall to zero without it — a sealed room may still have
                    // one old cramp in it, and a rate of exactly zero would be a rule about the
                    // map rather than about the orcs.
                    double rate = bind.FaceRate;
                    if (traffic != null)
                    {
                        int t = traffic[x, y + 1];              // the ground this reveal faces
                        double f = System.Math.Clamp(t / 160.0, 0.0, 1.0);
                        rate *= 0.35 + 1.15 * f;
                    }
                    // A separate salt from the tile keys, so a cell's binding is independent of
                    // the tile it landed on. Sharing one would correlate the two and put the same
                    // strap on the same tile every time — §8.3.1 arriving one level up.
                    ulong r = Fnv(90210, plane, x, y);
                    if ((r % 1000) < (ulong)(rate * 1000))
                    {
                        var pool = bind.Tiles.Where(t => t.Plane == plane).ToList();
                        if (pool.Count > 0)
                        {
                            var pick = pool[(int)(Fnv(90211, plane, x, y) % (ulong)pool.Count)];
                            var btex = GD.Load<Texture2D>(bind.Root + pick.File);
                            if (btex != null)
                            {
                                // Same placement rule as the face above, and the same defect:
                                // a binding centred on the cell's corner is a strap drawn across
                                // the join of four cells. §7.1's linear elements are the read at
                                // 1x, so a half-cell offset puts the one thing carrying the read
                                // in the wrong place.
                                var bs = new Sprite2D
                                {
                                    Name = BindNode, Texture = btex, Centered = s.Centered,
                                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                                    ZIndex = 1,
                                };
                                s.AddChild(bs);
                                if (!Contained(s, bs, out string bwhy))
                                    return $"[Tier1] boundary wall: REFUSED — the binding at "
                                         + $"({x},{y}) is drawn outside its own cell: {bwhy}.";
                                bound++;
                                boundKinds[pick.Kind] = boundKinds.GetValueOrDefault(pick.Kind) + 1;
                            }
                        }
                    }
                }

                // ── #211: A WALL END, CORNER OR PILLAR GAINS THE EAST FACE AT ½ DEPTH (§3.2) ──
                //
                // RULED with the object projection (Rafe, 2026-09-12): continuous runs stay
                // two-plane; where a wall ENDS it becomes an object in the room's terms and must
                // agree with the objects beside it — a right/east face receding at 45° at ½ depth.
                // The cell qualifies when floor lies to its EAST and it is not a north-south run
                // (north and south both wall): the east end of an E-W run, a corner, a pillar,
                // and the corridor mouth's jamb (the first wall end in every room — r002's seat
                // asked for exactly this: "the wall face band is simply cut where the corridor
                // passes through it").
                //
                // Geometry, in native px on a 32 cell whose face is the lower 16 rows: the block
                // is one cell deep in plan, so the receding run is ½ x 32 = 16 right and 16 up.
                // The parallelogram hangs off the reveal's right edge: (32,32)-(32,16)-(48,0)-(48,16)
                // — its top-right corner meets the cap's back edge, its bottom-left the face's
                // foot. Each output column samples one column of the cell's OWN face, so the
                // courses recede diagonally rather than run flat — a side, not a stretched front.
                // Value 0.75 of the face and a 1px seam at the arris: the side separates from the
                // front by the seam where they meet (§12.1 form), the ratio matching the ½-depth
                // sides the landed §3.2 props carry.
                bool eastOpen = map.InBounds(x + 1, y) && !map.IsWallTile(x + 1, y);
                bool nsRun = map.InBounds(x, y - 1) && map.IsWallTile(x, y - 1)
                          && map.InBounds(x, y + 1) && map.IsWallTile(x, y + 1);
                if (eastOpen && !nsRun && !isVoid && tex != null)
                {
                    var src = tex.GetImage();
                    int tw = src.GetWidth(), th = src.GetHeight();
                    int half = th / 2, run = tw / 2;
                    var side = Image.CreateEmpty(run, th, false, Image.Format.Rgba8);
                    for (int u = 0; u < run; u++)
                    {
                        for (int v = 0; v < th; v++)
                        {
                            int lo = half - u, hi = th - u;          // the strip slides up 1px per column
                            if (v < lo || v >= hi) continue;
                            int sy = half + (v - lo);                // the face's rows, top to bottom
                            int sx = Mathf.Min(tw - 1, tw - run + u);// the face's right half, one column each
                            var c = src.GetPixel(sx, Mathf.Clamp(sy, 0, th - 1));
                            if (c.A <= 0.01f) c = src.GetPixel(sx, th - 1);
                            float k = u == 0 ? 0.5f : 0.75f;         // the arris seam, then the side
                            side.SetPixel(u, v, new Color(c.R * k, c.G * k, c.B * k, 1f));
                        }
                    }
                    var ef = new Sprite2D
                    {
                        Name = EastFaceNode, Texture = ImageTexture.CreateFromImage(side),
                        Centered = s.Centered, Position = new Vector2(tw, 0),
                        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                        // +5, like a wall-top prop: the parallelogram hangs over the EAST floor
                        // cell, whose overlay children reach +4 and are added after this cell, so
                        // at +1 it drew underneath them and was not in the frame at all.
                        ZIndex = 5,
                    };
                    s.AddChild(ef);
                    eastFaces++;
                }

                // ── #212: THE CAP BAND OVER AN AGAINST-WALL PROP ─────────────────────────────
                // The prop's base is on this cell's south edge (DungeonRenderer shifted it there)
                // and it draws over the face. The top surface must stay in front of the prop's
                // top: the cap's upper half is re-laid as a child at the prop's own z, added after
                // the prop so it wins the tie. face < prop < cap band.
                if (capBase && tileLayer.CapBandCells.TryGetValue((x, y), out int bandZ)
                    && s.Texture != null)
                {
                    int th = s.Texture.GetHeight(), tw = s.Texture.GetWidth();
                    var band = new Sprite2D
                    {
                        Name = CapBandNode, Texture = s.Texture, Centered = s.Centered,
                        RegionEnabled = true, RegionRect = new Rect2(0, 0, tw, th / 2f),
                        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                        ZAsRelative = false, ZIndex = bandZ,
                    };
                    s.AddChild(band);
                    capBands++;
                }

                // ── THE LAMP STOPS AT THE FACE — BY MASK (cast-shadows round, §12.1a) ──────
                //
                // A 2D occluder cannot say "light this cell's own surface, stop behind it":
                // measured, a per-cell quad with its light-facing edges casting shadows its own
                // face (r29: 37.90 -> 5.51), and with them culled the cells in a wall row shadow
                // each other obliquely through their far edges (face 40.16 -> 28.47 cw, 37.19
                // ccw; cap 52.92 -> 30.05 / 38.00) — and a far edge cannot darken a thick mass
                // whose far side is rock. So the FIRST SURFACE the lamp meets is exempt from
                // shadows and everything behind it receives them: a wall cell with floor
                // anywhere in its 8-neighbourhood — the reveal the player sees, and the cap
                // beside it — lights as it always did; a cell with no floor beside it is
                // unexcavated mass and goes dark by occlusion. §12.1 calls the boundary this
                // draws FORM: it sits on geometry, moves with the lamp, and no ring is laid.
                bool adjacentFloor = false;
                for (int dy = -1; dy <= 1 && !adjacentFloor; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (map.InBounds(x + dx, y + dy) && !map.IsWallTile(x + dx, y + dy))
                        { adjacentFloor = true; break; }
                int lmask = adjacentFloor ? ReviewLighting.PropLightMask : 1;
                s.LightMask = lmask;
                foreach (var ch in s.GetChildren())
                    if (ch is CanvasItem cci) cci.LightMask = lmask;
                if (adjacentFloor) firstSurface++; else occludedMass++;
            }
        }

        string ages = string.Join("/", System.Linq.Enumerable.Range(0, 4).Select(i => ageHist[i]));

        // THE AGE MAP, PRINTED. An off-line measurement needs to know which age landed on which
        // cell, and both alternatives were worse than the engine simply saying so: reimplement
        // TrafficField in Python — a fourth copy of arithmetic, and the one most likely to drift —
        // or infer the age from the pixels, which is an instrument reading its own subject to
        // decide what its subject is. The floor's channel map set the precedent.
        var am = new System.Text.StringBuilder();
        am.Append("[Tier1] wall age map (. not a reveal, 0=sharp/sealed .. 3=on the spine)\n");
        for (int ay = 0; ay < map.Height; ay++)
        {
            am.Append("[Tier1]   ");
            for (int ax = 0; ax < map.Width; ax++)
                am.Append(ageMap.TryGetValue((ax, ay), out int av) ? (char)('0' + av) : '.');
            am.Append('\n');
        }
        GD.Print(am.ToString().TrimEnd());
        Diag.Log(am.ToString().TrimEnd());
        string kinds = boundKinds.Count == 0 ? "-"
            : string.Join(",", boundKinds.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));
        return $"[Tier1] boundary wall: family={cfg.Family} face={face} top={top} "
             + $"void={voidCells}(choice={vc},ring>{cfg.VoidRing}"
             + (cfg.VoidRing == manifestRing ? "" : $",OVERRIDE manifest={manifestRing}")
             + $") missing={missing} "
             + $"face_suppressed={faceSuppressed} "
             + $"planes(top={cfg.TopValue:0.##} face={cfg.FaceValue:0.##}) "
             + $"edge_check={cfg.EdgeCheck.Count}/OK bindings={bound}({kinds}) "
             + $"cap={capLaid}+{capVoid}void "
             + $"lightmask(first_surface={firstSurface},occluded_mass={occludedMass}) "
             + $"cap_bands_over_props={capBands} east_faces={eastFaces} "
             + $"age0..3={ages} traffic=spine:{tf.SpineLength:F0}/routes:{tf.Routes} "
             + $"manifest={manifestResPath}";
    }
}
