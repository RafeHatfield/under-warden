using UnderWarden.Logic.Map;
using NUnit.Framework;

namespace UnderWarden.Tests.Logic;

/// <summary>
/// ART-BIBLE-v0 §3, asserted as arithmetic: a wall tile shows a front face exactly where floor
/// lies to its SOUTH.
///
/// The rule was broken for as long as it lived in the presentation layer, where nothing in this
/// suite could reach it: masks 7 and 11 both collapsed to 3, and mask 7 has wall to its south.
/// The failure was silent because the walls were magenta programmer-art blocks with no planes in
/// them to be wrong about — it became visible on the day real two-plane walls were laid, and by
/// then it was on 13 in-map cells of every review scene.
///
/// These tests are deliberately exhaustive over all sixteen masks rather than picking the two
/// that were wrong. A test that checks the value that broke last time only forbids that value.
/// </summary>
[TestFixture]
public class WallMaskPolicyTests
{
    [Test]
    public void SouthSolidMaskNeverResolvesToAFace()
    {
        for (int mask = 0; mask < 16; mask++)
        {
            if (!WallMaskPolicy.SouthIsSolid(mask)) continue;
            int eff = WallMaskPolicy.Collapse(mask);
            Assert.That(WallMaskPolicy.SouthIsSolid(eff), Is.True,
                $"mask {mask} has wall to its south and collapsed to {eff}, which draws a front "
                + "face. ART-BIBLE-v0 §3: a face exists only where floor lies south, and a face "
                + "anywhere else is a reveal cut into the middle of a solid mass.");
        }
    }

    [Test]
    public void SouthOpenMaskKeepsItsFace()
    {
        // The invariant has to hold in both directions or "collapse everything to interior fill"
        // would pass it. A wall with floor to the south MUST still be able to show one.
        for (int mask = 0; mask < 16; mask++)
        {
            if (WallMaskPolicy.SouthIsSolid(mask)) continue;
            int eff = WallMaskPolicy.Collapse(mask);
            Assert.That(WallMaskPolicy.SouthIsSolid(eff), Is.False,
                $"mask {mask} has floor to its south and collapsed to {eff}, which is solid to "
                + "the south and therefore draws no face. §3 requires the reveal.");
        }
    }

    [Test]
    public void MaskSevenGoesToInteriorFillAndNotToThreeAnyMore()
    {
        // The specific regression, named, so a future edit that reverts it says so by name rather
        // than only by the invariant above.
        Assert.That(WallMaskPolicy.Collapse(7), Is.EqualTo(WallMaskPolicy.InteriorFill));
        Assert.That(WallMaskPolicy.Collapse(11), Is.EqualTo(3));
    }

    [Test]
    public void TheOtherCollapsesAreUnchanged()
    {
        Assert.That(WallMaskPolicy.Collapse(13), Is.EqualTo(12));
        Assert.That(WallMaskPolicy.Collapse(14), Is.EqualTo(12));
        foreach (int m in new[] { 0, 1, 2, 3, 4, 5, 6, 8, 9, 10, 12, 15 })
            Assert.That(WallMaskPolicy.Collapse(m), Is.EqualTo(m), $"mask {m} should pass through");
    }
}
