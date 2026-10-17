# #212 R1 (ruled, option d): STOP, §1.1.4(b), placed but refused by the scene. NOT INSTALLED

**Ruling (Rafe, 2026-09-25):** *"The Λ-frame is a barricade; its placement is an opening, not a wall
face. Move it to a gap or corridor mouth where it spans edge to edge (same placement rule as the
X-frame)… If no gap in tier1_props_review fits a Λ, place it at a corridor mouth and say which."*

## What was done
- **No gap fits.** The scene has exactly one two-cell east–west opening, (3–4,15), and the X stands
  in it. Every other opening is one cell wide: the corridor (8, 6–11) and its two mouths. The alcove
  (1–2, 16–17) opens north–south.
- **Corridor mouth chosen: (8,11)**, where the north corridor enters the room. The Λ was re-authored
  **one cell wide** (`projection_mesh.barricade_b1`, same grammar, standing height kept, §12.2 held)
  so it spans the mouth wall to wall. Seed 1337 landed as tile 9812 (hold 0.952). Floor to its
  north, so not a wall prop (law part 2).
- **The plant**, `lambda-against-wall.png` (the walked frame, axis `scale`), was proven first
  (Ruling 47): 4 of 5 seats flagged the Λ on the wall by name.

## Why it stopped
**The engine refused the scene.** `CorridorReviewSceneBuilder` carries a standing scene law: no prop
may seal floor the player could reach.

> *"Corridor spec 'tier1_props_review': the props seal the corridor — (8,10) is floor the player could
> reach before they were seated and cannot reach after. Move a prop, or make it non-blocking."*

A blocking barricade that spans a one-wide corridor **necessarily** seals it: the corridor and the
whole north room (3–13, 2–5) have no other way in. The X doesn't trip this law because its gap has a
route east of the pillar. The engine's two escapes don't fit:
- **Move it:** there is no other opening, which is why the brief's fallback was reached at all.
- **Non-blocking:** such props draw at 0.7 alpha (the builder's own comment), so the barricade would
  be translucent and walkable. That isn't a barricade.

So the ruling's fallback and the scene's no-seal law contradict each other in this scene: a ruling gap
the brief did not anticipate (§1.1.4(b)). Nothing installed.

## ⚠ Mechanism finding: the capture judged a frame that was never drawn
After the engine threw, the capture still wrote a PNG: **one flat grey, RGB 77,77,77, one unique
colour** (`blank_capture.png`, log excerpt `capture_refusal.log`). The frame critic then ran five
seats on it. All five saw it (*"There is no picture… a failed render or an empty capture"*), ranked it
last, and flagged it. The panel still returned **INSTALL-LATEST**, because the install rule blocks only
on a ≥ 4/5 strong regression and the rank slack let 2 seats count as "not below".

The install gate still held, since a flagged build needs every flip dispositioned and *"the frame is
blank"* can't be routed honestly. But this is §4.2's shape: a step that failed and reported success.
The capture should go red when the scene builder throws. That history record is
`.claude/skills/frame-critic/history/r001-art_lambda-opening-212.json`: **a capture failure, not a
verdict on the Λ.** It is kept, not deleted, and its rank (0.00) is a statement about the capture.

## What Rafe rules
1. **Open a second way into the north room** (a scene edit: carve one more opening), so the Λ at the
   corridor mouth no longer seals anything. This changes the review scene's walls, which the ruling
   said stay untouched; it's yours to say whether that clause meant wall height (§3) only.
2. **Swap the barricades:** the Λ takes the two-cell gap (3–4,15) as a 2×1 (the landed B needs no
   re-author there), and the X, a row of stakes, goes somewhere else that opens onto an alternate
   route.
3. **Freestanding** (law part 2's other branch): the Λ stands in open floor, spanning nothing.
4. **Make "a barricade that seals" legal in review scenes:** a change to the scene builder's law, not
   an art change.

The one-cell Λ (`barricade_b1`, tile 9812) and its driver stay on this branch, ready for whichever
option needs them.
