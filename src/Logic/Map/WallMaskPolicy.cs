namespace UnderWarden.Logic.Map;

/// <summary>
/// Which wall tile a cardinal neighbour mask resolves to — and the one invariant that resolution
/// must never break.
///
/// ART-BIBLE-v0 §3: **a wall tile shows a front face exactly where floor lies to its SOUTH.**
/// A face drawn anywhere else is a reveal cut into the middle of a solid mass.
///
/// ── THE BUG THIS FILE EXISTS TO MAKE IMPOSSIBLE ──────────────────────────────────────────────
/// `DungeonRenderer` collapsed cardinal masks 7 and 11 both to 3. Mask 11 (N,E,W wall; SOUTH
/// FLOOR) wants a face and mask 3 gives it one. Mask 7 (S,E,W wall; NORTH floor) has WALL to its
/// south and must not have one — and got one anyway, because both were mapped to the same tile.
///
/// Measured across the four review specs before the fix: **13 in-map cells per scene**, plus the
/// whole top border row. Every one of the 13 is a room's SOUTH wall, which is the most-looked-at
/// wall in any room, and the defect was invisible for as long as the walls were flat magenta
/// programmer-art with no planes in them to be wrong about.
///
/// ── WHY IT IS HERE AND NOT PATCHED IN PLACE ──────────────────────────────────────────────────
/// The collapse lived in the presentation layer, which imports Godot, which is why nothing in the
/// test suite could reach it and why the rule could be broken silently. It is arithmetic on four
/// bits; it belongs in the logic layer, where <see cref="SouthIsSolid"/> and
/// <see cref="Collapse"/> can be asserted against each other for all sixteen masks at once.
/// A fix that only corrects today's value leaves tomorrow's free.
///
/// The collapse itself is NOT removed, and that is deliberate. Its reason is recorded in the
/// renderer and is still true: the shipped sandstone set draws masks 7/11/13/14 as directional
/// T-junctions whose far side shows external rock, which is right where another wall structure
/// meets this one and wrong on a plain room or corridor edge. What changes is the DESTINATION for
/// mask 7 — from a face tile to the interior fill, which is what the far side of a room's south
/// wall actually is.
/// </summary>
public static class WallMaskPolicy
{
    /// <summary>Bit of the cardinal mask set when the NORTH neighbour is a wall.</summary>
    public const int North = 8;
    /// <summary>Bit set when the SOUTH neighbour is a wall. The one §3 cares about.</summary>
    public const int South = 4;
    public const int East = 2;
    public const int West = 1;

    /// <summary>The sentinel the renderer's tile table treats as "all four cardinals are wall".</summary>
    public const int InteriorFill = 15;

    /// <summary>True when this mask has wall to its south, so §3 forbids it a front face.</summary>
    public static bool SouthIsSolid(int mask) => (mask & South) != 0;

    /// <summary>
    /// Collapse a raw cardinal mask to the mask whose tile is actually drawn.
    ///
    /// 11 → 3 keeps its face: both have floor to the south, and mask 3's plain horizontal edge is
    ///       what a room's north wall looks like.
    /// 7  → 15 loses the face it should never have had. Its far side is more interior fill, which
    ///       the renderer's own comment already says is the correct content there.
    /// 13, 14 → 12: unchanged. All three have wall to the south, so no face is involved and the
    ///       §3 invariant is untouched either way.
    /// </summary>
    public static int Collapse(int cardinal) => cardinal switch
    {
        11 => 3,
        7 => InteriorFill,
        13 or 14 => 12,
        _ => cardinal,
    };
}
