# STALL REPORT — broken-judge

**The line has stopped and is not restarting itself.** LOOP-PROCESS §1.1.4 ruling trigger: this report is the evidence.

- **lane** `art/jamb-211`
- **surface** `combined`
- **guard** `broken-judge`
- **written** 2026-09-13T21:58:20

## Why it stopped

the picture-plant was missed 2 rounds running. The judging layer is broken; no round past it is readable and nothing ships past it.

## What was tried, round by round

`rank` is where the build placed in that round's blind shuffled deck, and `score` normalises it so decks of different sizes compare — 1.00 is first, 0.00 is last. `Δpic` is how far the delivered frame moved from the previous round: mean and worst cell, in luminance levels. `0.000 / 0` means the picture did not change at all.

| round | verdict | rank | score | best? | Δpic | build | the seat's own words |
|---|---|---|---|---|---|---|---|
| 1 | VOID | 2/4 | 0.67 |  | — | `67badeb89491` | Nothing in it has a surface. The grey wall blocks along the left (x≈0–50) and bottom (y≈205–288) are a single stamped tile with no top/face  |
| 2 | VOID | 3/4 | 0.33 |  | 0.053 / 1 | `8130865724ba` | It is the free one. The dirt floor (x55–345, y0–215) is a flat brown fill with a red-brown speckle over it — the speckle has no direction, n |
| 3 | VOID | 3/4 | 0.33 |  | 0.107 / 2 | `7607dbdf11c6` | There is no light falloff anywhere — the frame is one flat exposure edge to edge, so the carried-light premise isn't drawn at all. The grey  |

## The flip lists, verbatim

Void rounds do not appear here. §4: the plant was missed, so those findings are not read — they are kept in the verdict under `flip_list_withheld` and are not evidence.

**round 1 (VOID)**

- Remove the pale grey wedge at the corridor/wall junction (x≈505–545, y≈395–440); it is a corner-tile that is lit at a value the rest of the wall never reaches and reads as a rendering seam. Either draw a proper corner piece at the wall-face value or let the face run through.
- The X-brace at bottom-left (x≈215–305, y≈645–705) straddles the floor edge and the dark wall top below it; snap it wholly onto the floor tile or wholly onto the wall face, and give it a contact shadow on whichever it sits on.
- The A-brace on the right wall face (x≈575–680, y≈400–450) sits over the face band with no shadow and no pins where it meets the stone; add a dark shadow line under each leg and a driven pin at each foot so it is attached to the wall rather than pasted on.
- The standing slab left of the figure (x≈265–295, y≈535–640) is drawn in elevation (tall face, wider base) while the brazier is drawn in plan; redraw it as a top-down footprint with a short cast shadow to the right, matching the brazier's projection.
- The long crack from (≈240, 460) to (≈330, 620) crosses brick joints as one continuous overlaid line; break it at each joint and offset it a pixel so it is in the stones, not on them.
- The hatched scratch texture on the lit floor bricks (x≈480–740, y≈470–720) is at the same stroke density and angle on every brick; vary it per brick and drop it entirely from bricks under the figure's brightest spill.

**round 2 (VOID)**

- The wall mass in the top ~40% of the frame (block x180–500, y70–390 and block x570–750, y70–390) is a smooth gradient-noise cloud at sub-pixel resolution next to a floor drawn on a 2px grid. Redraw it as rock/masonry on the same 2px grid so the wall and floor are the same material fidelity.
- Inside those wall blocks there are hard straight facet edges (diagonal from about (360,70) to (180,130); the lighter wedge at x570–640 with a diagonal to (750,200)). These read as leaked shadow-caster polygons. Either snap them to tile edges or remove them.
- The A-frame timber at x580–680, y400–450 is the most saturated object in the top half and reads as an orange letter "A". Desaturate to the wall band's timber tone, add grain, add a cast shadow on the band.
- The brazier's orange wash (x600–750, y480–700) flattens the brick joints to near-invisible. Reduce its intensity or composite it multiply so the joints survive under it.
- The brazier sprite at (600,545) is drawn at 1px density while the hero, floor and props are 2px. Redraw at 2px.
- The vertical strip at x120–185, y390–700 is the floor texture washed to a smeared brown-grey with no brick structure; it does not read as wall face or wall top. Redraw it as one or the other, matching the horizontal band at y395–455.
- The cracks are long smooth arcs that run over 6–8 bricks without acknowledging a joint (the arc from (240,520) to (560,470); the arc at x560–750, y640–700). Break them at joints and add jogs so they follow the masonry.
- The hero's light halo blurs the floor under it (x400–520, y470–560) — the overlay is soft while the floor is crisp. Quantize the light overlay to the pixel grid or cut the blur radius.
- The X-brace at x220–310, y650–700 sits at the corridor junction with no ground contact shadow; add one.

## Where to look

Captures and transcripts, per round:

- round 1 — deck `/Users/rafehatfield/.claude/frame-critic/deck-68c7d98d764a516c`, transcript `.claude/skills/frame-critic/history/r001-art_jamb-211-transcript.txt`
- round 2 — deck `/Users/rafehatfield/.claude/frame-critic/deck-047cca5e3693fc33`, transcript `.claude/skills/frame-critic/history/r002-art_jamb-211-transcript.txt`
- round 3 — deck `/Users/rafehatfield/.claude/frame-critic/deck-7d43622161f60f65`, transcript `.claude/skills/frame-critic/history/r003-art_jamb-211-transcript-seat1-redraw.txt`

## What is being asked for

A ruling. Not another round — the guard fired precisely because another round is the wrong move. Nothing installs to the phone while this stands.
