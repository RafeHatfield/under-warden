R = '/Users/rafehatfield/development/c-yarl/.claude/worktrees/queue/'
def patch(path, pairs):
    p = R + path; s = open(p).read()
    for old, new in pairs:
        assert old in s, (path, old[:70]); s = s.replace(old, new)
    open(p, 'w').write(s); print('patched', path)

# ── ReviewLighting: the base depth is one constant, shared by the occluder and the placement ──
patch('src/Presentation/Map/ReviewLighting.cs', [
("    /// <summary>Canvas light-mask bit for props: lit by every lamp, shadowed by none.</summary>\n    public const int PropLightMask = 2;",
 "    /// <summary>Canvas light-mask bit for props: lit by every lamp, shadowed by none.</summary>\n    public const int PropLightMask = 2;\n\n"
 "    /// <summary>A box prop's PLAN DEPTH as a fraction of the cell (§3.2 footprint). One number,\n"
 "    /// two consumers: the footprint occluder's base parallelogram, and the against-wall placement\n"
 "    /// that puts that parallelogram's FAR edge on the reveal's foot (#212, Rafe 2026-09-13). The\n"
 "    /// screen rise of the base is ½ of it (k = ½ per §3.2).</summary>\n"
 "    public const float PropBaseDepth = 0.35f;\n"
 "    public static float PropBaseRun(float cellH) => 0.5f * PropBaseDepth * cellH;"),
("                float depth = 0.35f * cellH;                  // plan depth; k = ½ per §3.2\n                float run = 0.5f * depth;",
 "                float run = PropBaseRun(cellH);               // plan depth 0.35 cell; k = ½ per §3.2"),
])

# ── DungeonRenderer: the base's far edge on the foot; no cap band over the prop ──────────────
patch('src/Presentation/Map/DungeonRenderer.cs', [
("""    /// <summary>The wall cells whose CAP BAND is re-drawn over an against-wall prop's top, with
    /// the z it must draw at (the prop's own, added later so it wins the tie). Consumed by
    /// Tier1BoundaryWall when it lays the cap.</summary>
    public Dictionary<(int X, int Y), int> CapBandCells { get; } = new();

""", ""),
("""        // ── #212: A PROP AGAINST A WALL SITS AT THE WALL'S FOOT, UNDER THE TOP BAND ──────────
        //
        // RULED-SHAPED by the cast-shadows walk (Rafe, 2026-09-13: "props sit against walls under
        // the top band"). Before this, a prop one cell south of a wall stood a full cell away from
        // the reveal — its base at its own cell's bottom edge, the wall's face beginning a cell
        // north — and read as placed in the room rather than against anything. The face's foot
        // IS the shared edge (§3: the reveal is the wall's south surface, rising from the cell
        // boundary), so a prop against the wall has its base there. The shift is measured off the
        // sprite's own bottom transparent rows, not typed. The wall's cap band is re-laid over the
        // prop's top by Tier1BoundaryWall (CapBandCells) so the top surface stays in front:
        // face < prop < cap band. Floor props only; a wall-top prop already sorts above its wall.
""",
"""        // ── #212: A PROP AGAINST A WALL STANDS ON THE FLOOR AT THE REVEAL'S FOOT ─────────────
        //
        // LAW (Rafe, props walk, 2026-09-13): "a prop placed against a wall stands on the floor at
        // the reveal's foot, in front of the face, and is never overdrawn by the cap."
        //
        // The first build of this put the SPRITE'S bottom row on the foot line and re-laid the
        // cap's upper half over the prop's top. Walked, it failed: "feet land on the cap band, not
        // below it; no reveal foot visible under it." Measured, the reason is §3.2's own geometry:
        // a box prop's base is a parallelogram rising ½·depth up-right from the sprite's bottom
        // edge (the occluder draws exactly that), so with the bottom row ON the foot the whole
        // footprint lay over the FACE — the prop's feet were inside the wall, and the eye put the
        // prop on top of it. The base lies on the FLOOR: its far edge meets the foot line and its
        // near edge — the sprite's bottom — sits ½·depth south of it, on the floor cell, so the
        // wall's foot shows between the legs. The depth is ReviewLighting.PropBaseDepth, the one
        // number the occluder also uses. The shift is measured off the sprite's own bottom
        // transparent rows, not typed. Nothing is laid over the prop: it draws in front of the
        // face and in front of the cap, as a thing standing before a wall does. Floor props only;
        // a wall-top prop already sorts above its wall.
"""),
("""                if (margin == float.MaxValue) margin = 0f;
                float shift = tileH - margin;""",
"""                if (margin == float.MaxValue) margin = 0f;
                float shift = tileH - margin - ReviewLighting.PropBaseRun(tileH);"""),
("""                tileLayer.PropShift[propIdx] = shift;
                for (int dx = 0; dx < prop.FootprintW; dx++)
                    tileLayer.CapBandCells[(prop.X + dx, prop.Y - 1)] = renderer.GetTileSortOrder(prop.X, prop.Y) + 2;
""",
"""                tileLayer.PropShift[propIdx] = shift;
"""),
])

# ── Tier1BoundaryWall: the cap band re-lay goes ─────────────────────────────────────────────
patch('src/Presentation/Map/Tier1BoundaryWall.cs', [
("""    private const string CapBandNode = "Tier1CapBand";   // #212: the cap re-laid over an against-wall prop
""", ""),
("        foreach (var n in new[] { FaceNode, BindNode, CapBandNode, EastFaceNode })",
 "        foreach (var n in new[] { FaceNode, BindNode, EastFaceNode })"),
("""        int capBands = 0;                          // #212: cap bands re-laid over against-wall props
""", ""),
("""                // ── #212: THE CAP BAND OVER AN AGAINST-WALL PROP ─────────────────────────────
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

""",
"""                // #212's cap-band re-lay over an against-wall prop lived here and is GONE — LAW
                // (Rafe, 2026-09-13): a prop against a wall "is never overdrawn by the cap".

"""),
("""             + $"cap_bands_over_props={capBands} east_faces={eastFaces}(ground-mask) \"""",
 """             + $"east_faces={eastFaces}(ground-mask) \""""),
])
