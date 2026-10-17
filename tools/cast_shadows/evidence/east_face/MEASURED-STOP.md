# #211 east faces: measured before building. STOP: the expected law and §3.2 disagree

The frame is `tools/tier1_floors/evidence/combined.png` (c8932ed3, the build walked as 50d07f16). The
geometry comes from `Tier1BoundaryWall.cs:657-747` and is confirmed on the pixels:
`east_faces_annotated.png`, where the cyan outline is the code's wedge, the upper line is the cap
top and the lower line is the foot. Scripts are in this folder. The scene has 7 east faces, and 6 of
them are in the frame. (12,6), the bay's north wall at the doorway, is off the capture's right edge;
its geometry is identical by code.

## 1. Top row vs cap top; bottom row vs foot row (native px, cell = 32, face = lower 16 rows)

The wedge is the face's 16 rows, sheared up 1 px per column: (32,32)-(32,16)-(48,0)-(48,16).

| edge | arris column (u=0) | far column (u=15) |
|---|---|---|
| top | row 16, the face's top (the cap/face arris) | row 1, **1 px below the cap top** |
| bottom | row 31, **on the foot row** | row 16, **16 px (½ tile) above the foot row** |

**It never rises above the cap top**, by code and by pixel. What does rise above a line is the wedge's
**top edge**. For its whole run it sits above the face/cap arris, to the right of the cap's vertical
right edge (x=32), with no top surface beside it. The triangle (32,16)-(48,0)-(32,0) between the cap
and the wedge shows whatever lies behind:

| cell | wedge | that triangle (behind) | |
|---|---|---|---|
| (5,15) pillar, lamp NE | 27.9 | **87.5**, lit floor | a dark diagonal panel against bright floor |
| (10,11) bay wall, lights W | 19.6 | 23.7 | low contrast |

That triangle is the "leaning up-right into the dark" read. My reading is that frame 2's "above the
cap line" is this edge above the arris, not above the cap top. The pixels can't confirm frame 2
itself: (12,6) is off the capture.

## 2. Does it receive the rig? Yes, with a baked 0.75 on top

`LightMask = 1`, the ground mask: the lamp and the fire light it, and its own cell's occluder
shadows it when the light is west. The bake is ×0.75, with a 0.5 seam column at the arris; that is
§3.2's text, ratified 2026-09-13.

| cell | wedge / its face's right half | wedge / floor east | hue r/b, wedge vs face |
|---|---|---|---|
| (5,15) lamp NE: lit | 0.80 (the bake) | 0.32 | 1.54 vs 2.19 |
| (10,11) lamp and fire W: in shadow | 0.44 | 0.45 | 1.14 vs 3.02, ambient grey |
| (7,6), (2,18) unlit mass | 0.94 / 0.65 | — | ambient |

(7,11) and (2,15) are covered by the Λ and the X-frames and are not read. Where it is lit, it
tracks its face at the bake. In shadow, it falls to ambient while its face stays lit, because the
face is the first surface and exempt. The rig is driving it; it is not painted dark. But at
(5,15) the face's own albedo × 0.75 puts it at 0.32 of the lit floor beside it.

## 3. Slab structure and hue

Each column samples one column of the face's right half, so the courses recede and the slab
structure is carried. Its spread is 6.8 against the face's 11.6 at (5,15), where the bake predicts
8.7, so it is somewhat flatter. The hue shifts toward the ambient's blue as it darkens (r/b 1.54 vs
2.19). It is not a flat quad, but at its values it reads close to one.

## 4. Against the cap or against the face? Against the face

It is anchored at the face's right edge and sampled from the face, and it spans the face's height
(16 rows), not floor-to-cap-top (32). Its top edge is exactly where §3.2 puts the right edge of an
object's TOP. Under §3.2, `(x,d,z) → (x+½d, −(z+½d))`, a box with z = 16 and d = 32 has exactly
this side. **The side is already §3.2's. The top isn't:** the cap is the two-plane rectangle and
stops at x=32.

## The conflict

The expected law asks for full height from the floor at the foot to the cap top, foot on the floor,
never above the cap top, and ½ depth receding right per §3.2.

- **Reading A, inside §3.2:** keep the side and complete the end's TOP with §3.2's top triangle,
  (32,16)-(48,0)-(32,0), in cap material. The end becomes a box: its silhouette is
  (0,0)-(48,0)-(48,16)-(32,32)-(0,32). It never rises above the cap top, and the east side runs
  from the floor at the arris to the cap top at the far corner. The foot recedes up-right ½ tile
  along §3.2's floor line. This changes the cap at wall ends only; no §3.2 or §6.2 value moves.
- **Reading B, literal:** the foot stays on the foot row across the whole side, and the side spans
  the full 32 rows. That is a 16×32 elevation with no 45° recession, which is not a §3.2 side. It
  needs §3.2 changed, so it is a hard stop.

The ruling is Rafe's. No build, no plant and no seat has been spent.
