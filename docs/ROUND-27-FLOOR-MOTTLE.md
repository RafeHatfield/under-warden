# Round 27 — the mottle, and what the frame count was actually measuring

**Lane** `art/floor-mottle` · **Round 1** · **Critic verdict FAIL** (`CRITIC-VERDICT.json`),
judge intact (plant CAUGHT), no guard fired.
**This feeds the combined build with walls. It does not solo-install** — the floor solo lane is
closed (Rafe, 2026-09-03).

---

## 1. The brief, and the two halves of it that measurement disagreed with

The round was called to find "the floor render stage producing off-palette, between-rung
intermediate values on delivery", named the consolidation pass's continuous terms (dish, chip,
crack-lip) or the polish shader as the candidates, and set the target that the delivered floor
should carry roughly its asset palette's colour count rather than 200%+ of it.

**Both named candidates are disproved, the target as stated is unreachable in a lit frame, and
the defect is real and was somewhere else.**

### 1.1 The composer was already clean

| stage | distinct colours |
|---|---|
| reference painter, no route (`assemble`, traffic=None) | **9** — the ladder, exactly |
| reference painter, with route: dish, chip, crack-lip, grit, chroma | **25** — 9 rungs × 3 chroma tints |
| **+ the contact-occlusion overlay composited** | **132** |

Every one of the briefed candidates already funnels through `Ladder[LadderIndex(…)]`;
`DishQuantise` was already on. Nothing in the consolidation pass puts a value between rungs.

### 1.2 A lit frame's colour count is the rig's number, not the palette's

Per cell on `mottle_lit` at the ratified rig, before any change:

| surface | mean colours/cell | worst cell |
|---|---|---|
| floor | 196.5 | 651 |
| **wall** | **106.8** | 283 |

A carried lamp is a continuous radial field and 8-bit output quantises it into hundreds of
values whatever the albedo is — which is why the wall, whose cap was hard-snapped to nine values,
also reads in the hundreds. Removing the occlusion overlay from the frame entirely moved the
floor's whole-frame count 3975 → 3761. **No change to the art gets a lit frame near its palette
count; that is arithmetic.** The reachable and meaningful quantity is the **albedo**, and there
the briefed target is exactly right.

---

## 2. The defect: §12.1's contact occlusion was a 130-step alpha ramp

`src/Presentation/assets/tier1_floors/tier1_floor_963{0,1,2,3}.png` — the N/E/S/W plane-boundary
sprites — are **one colour, `rgb(22,22,22)`, carrying 130 distinct alpha values**, alpha-blended
over the floor **one to three times per cell**. Compositing is `floor*(1-a) + 22a`, which lands
between rungs by construction.

It is the wall cap's disease (107 → 9, fixed there by snapping) on a different surface, reaching
the frame through a layer no instrument in the repo was reading: the lattice score, the
grid-coincidence ratio, the enclosure census and the ladder-reach audit **all read a field the
composer built and are blind to anything drawn over it.**

**It did not arrive with #169.** The sprites date from session one (`06102638`); only the
ambient-anchored 1–3× stacking is recent (`f3f5a207`, RULED 2026-09-02), and stacking multiplied
an existing smear rather than creating one.

Every seat that has called this floor *"continuous-tone procedural noise, not authored pixel
art"*, *"441 unique colours in 2,304 pixels, adjacent values differing by 1"*, and *"soft blobs …
anti-aliased airbrush gradients dropped onto an otherwise hard-edged pixel image"* has been
looking at this layer.

---

## 3. The fix, and the ruling it needed first

**RULED (Rafe, 2026-09-03): extend the ladder two rungs, then snap.**

Snapping the occlusion to a nine-rung ladder is free in the lit band and **deletes the stacking**:
at 1 layer the clamp costs 0.64 luminance at the contact edge and nothing beyond it; at 2 and 3
layers it costs 19.24 and 24.49, all of it in the dark band the 2026-09-02 ruling exists to serve.
Three layers want 22.11 and a nine-rung ladder stops at 48.56.

So `PALETTE_EXTEND_BELOW` goes 2 → 4: **48.56 → 35.34 → 22.11**, and 22.11 is the first rung that
reaches the deepest stack. The reach is *derived from what has to be representable*, not chosen.
It is the second instance of one shortfall, not a new one — PR #161's 7 → 9 was the first, for
exactly the same cause (a treatment clamped against the ladder's own end, invisibly, because every
clamped pixel is arithmetically correct).

Then the occlusion moves out of the overlay layer and into the floor's own pixels as a
**subtraction in whole ladder rungs**. A whole number of rungs off a ladder value is a ladder
value, so the snap that follows has nothing left to invent.

- **The sprite is still the datum.** Its alpha is read per pixel and now says *how many rungs*
  rather than *how much to blend*. That matters: the along-edge jitter survives the quantise
  (2–3 distinct rungs per row, measured) and it is the only thing holding the seam off a straight
  constant-pitch line on the tile grid.
- **Anchored at the median, not at the pixel.** The blend darkened a bright stone more than a dark
  one because a blend is a ratio. Occlusion is *form* (§12.1) and a form does not vary with the
  value of what it crosses — which is also what makes it exactly representable.
- **Drawn last**, over stones, joints and cracks alike, which is where the sprite it replaces sat.
- `Tier1FloorOverlays` still *makes* the decision and hands it over; only the **drawing** moves.
  Turning a treatment off and turning its drawing off are different things and only the second is
  on offer (`drawOcclusion: !ashlarActive`, reported in the log as `BAKED-BY-FLOOR`).
- The two painters are tied together by `occlusion_check` in the manifest — 48 samples the engine
  recomputes and **refuses** on. The paint check cannot cover this one: it compares composer
  against engine on a field with no map in it, and occlusion is decided by wall adjacency.
- Both painters round with `floor(x + 0.5)`. `numpy.round` breaks a tie **to even** and
  `Math.Round` breaks it **away from zero**, so a rung landing on a half would have been 4 in one
  painter and 5 in the other with neither able to be called wrong.

### 3.1 What the ladder extension moved — measured, and the first answer was wrong

The obvious claim is that nothing but the occlusion can reach the new rungs, because the bond
authors its joint at 0.42 × its stone (47.79 at the median) and 47.79 is nearer 48.56 than 35.34.
**That is true of a joint under a median stone and false of the field**: a joint is 0.42 × *the
stone it is cut into*, so under a stone at 88.24 it is authored at 37.06 — which nine rungs
clamped to 48.56 and eleven put on 35.34, where it belongs.

```
LADDER DELTA — extend_below 2 (9 rungs, bottom 48.56) -> 4 (11 rungs, bottom 22.11)
  pixels moved: 7278 of 262144 (2.78%)
    joints  83.6%   cracks   6.0%   stone faces  11.7%
  9 rungs   joint contrast mean 0.1765 p50 0.1628 | spread 5.026 rungs, deciles open 48.0 tight 114.5
  11 rungs  joint contrast mean 0.1886 p50 0.1745 | spread 5.026 rungs, deciles open 48.0 tight 114.5
```

**That is the clamp releasing, not PR #161's failure repeating, and the difference is in the
distribution rather than in the mean.** #161 put 92.7% of joint pixels on the bottom two rungs and
took mean joint contrast 0.272 → 0.510. Here the spread is *unchanged* at 5.026 rungs with both
deciles identical, and the mean moves 6.9%.

⚠ **An anchor saying "the bottom" is an anchor that moves.** `SHELTER_LIFT_RUNGS` lifts a joint
off the ladder's bottom; it survived this change only because the lift is applied to the joint's
own authored value and not to `ladder[0]`. The next treatment written against "the bottom rung"
will not. Bible §5.7's rule for anchors wants its twin: **an anchor is named by what it IS, never
by where the ladder happens to end.**

---

## 4. The new builder's tool

`tools/tier1_floors/measure_delivered_palette.py` — **prints every round, votes on nothing**
(LOOP-PROCESS §4.3). No threshold in the file, no verdict line.

```
DELIVERED PALETTE — src/Presentation/assets/tier1_ashlar
  ladder: 11 rungs, 22.11 .. 154.38, step 13.227
  authored: 33 colours (11 rungs x chroma tints); 65 legal 8-bit renderings of them

THE ALBEDO — the reference painter, 16x16 cells, seed 1337
  no walls in the field        delivered=  30   authored= 33   ratio=  90.9%   OFF-PALETTE=0
  with contact occlusion       delivered=  31   authored= 33   ratio=  93.9%   OFF-PALETTE=0
  occlusion stacked x2         delivered=  31   authored= 33   ratio=  93.9%   OFF-PALETTE=0
  occlusion stacked x3         delivered=  31   authored= 33   ratio=  93.9%   OFF-PALETTE=0

CONTROL — the occlusion BLENDED, the way the sprite did it
  alpha-blended occlusion      delivered= 636   authored= 33   ratio=1927.3%   OFF-PALETTE=599
      599 off-palette colours span 125 luminances, longest unbroken run 119
  CONTROL BINDS
```

**The control is the point.** §13.5's habit is kept even though nothing here gates: the census is
re-run with the occlusion composited the old way, on the field rather than in the composer, so it
differs from the shipped arm in exactly one thing. A census that cannot be made to report a
130-step ramp blended into a quantised floor would not have seen the one that shipped.

Two smaller things the tool had to be corrected on, both worth keeping:

- **It reported 29 of 30 authored colours as off-palette on its first run.** The reference painter
  finishes with `.astype(np.uint8)`, which truncates; the engine writes a float into an `Rgb8`
  Image, which rounds. Both renderings of each authored value are legal now — and **the underlying
  one-level disagreement between the two painters is real and unexamined.**
- **`authored` and `legal` are separate numbers.** Using the both-renderings set as the ratio's
  denominator halved the reported ratio. One number doing two jobs is how an instrument ends up
  2× wrong in the direction that flatters it.

`--capture` reports the delivered frame per cell, floor **and wall** side by side, labelled as the
rig's number rather than the art's — because that is the comparison §1.2 needed and nobody had
made it.

---

## 5. The seam the ruling protects got stronger

Interior minus contact, in delivered levels, on wall-to-north floor cells:

| range | before (sprite blend) | after (rung bake) |
|---|---|---|
| 0–2 tiles (n=1) | **−5.89** — inverted | **+18.21** |
| 3–4 tiles (n=8) | mean 8.82, **worst cell 2.43** | mean 11.96, **worst cell 5.91** |
| 5+ tiles | **n=0** — this scene has none in view |

Reported at the worst cell in every band, not the mean that agrees with the change. The worst cell
in the 3–4 band is still **below the 8-level bar** the 2026-09-02 ruling cited, before and after.
The near band was *inverted* under the blend — the contact read brighter than the cell's interior —
which is a §12.1 violation nobody had measured.

---

## 6. ⚠ THE LARGER FINDING — the floor is not lit by the ratified rig

**FILED, NOT FIXED** (Rafe's ruling, 2026-09-03): it changes the delivered floor by 1.6× and needs
Ruling 56 re-ratified on device, which is a different logical change from this one.

`--light-energy` **has no effect on the floor at all.** Captures at energy **0, 1.6 and 8.0 are
byte-identical on every floor pixel**; 15.26% of the frame — walls, caps, the figure — does
respond, so the knob reaches everything except the surface this lane has been tuning.

```
y= 650  e0=[133 94 60]   e1.6=[133 94 60]   e8=[133 94 60]
y= 730  e0=[ 92 72 47]   e1.6=[ 92 72 47]   e8=[ 92 72 47]
legibility(8,10) ratio  e0=0.5264            e8=0.5263
```

**Cause.** `src/Presentation/assets/shaders/tier1_polish.gdshader`'s `light()` reads `LIGHT_COLOR`
and never `LIGHT_ENERGY`, under a comment that asserts the opposite:

> *"LIGHT_COLOR already carries the light's colour, its energy and the radial falloff texture."*

It does not. A custom `light()` that ignores `LIGHT_ENERGY` discards it, and every floor tile
carries a `ShaderMaterial` built from that shader.

**Positive control.** Adding `* LIGHT_ENERGY` to the diffuse term moves the floor at (535, 650)
from `(133, 94, 60)` to `(255, 188, 114)` — a clean 1.6×, clipping — and at energy 0 the floor
goes dark enough that the legibility probe refuses the capture. The probe was reverted; the
evidence that stands in the tree is `energy_probe_e0.png` / `energy_probe_e8.png`, whose floors
are byte-identical and whose walls are not.

**Why it matters more than the mottle.** Every floor in every capture and on the device has been
lit at **energy 1.0** while walls, caps and props are at Ruling 56's **1.6**. Floor-versus-wall
value work in this lane — §6.5's stack, the inverted value stack of PR #151, the wall session's
`k_top < 1 at every range` — has been comparing two differently-lit planes, and the ratio is
wrong by a factor of 1.6 in the direction that makes walls too bright against the floor.

**The fix is one word and it is not safe to apply alone.** At 1.6 the corrected floor clips to 255
near the lamp, so Ruling 56's radius / falloff / ambient were tuned against the broken behaviour
and would need Rafe's walk to re-ratify.

---

## 7. The critic round

**FAIL**, `SHIP: NONE`, rank 3 of 3. The plant (`keyline-floor.png`, culled for *"outlined
chips"*) was **CAUGHT** — flagged and not shipped — so the round is readable. It also **outranked
the build**, which is recorded and not scored, and is the fifth consecutive instance of the
§4 finding that a blind seat's ordering does not reproduce Rafe's culls. The seat flagged every
frame including the commercial bar, so its flag carries no discrimination this round.

**No approved floor capture is in the deck** — `approved_capture` is `null` for this surface in
`docs/FRAME-CRITIC.json`, so half the comparative bar was untested. That is one line for Rafe to
fill in and it is not the builder's to choose.

### The flip list, split by what this lane owns

Four of the six items are about content sitting at **delivered luminance 2 to 9** — the seat says
so itself (*"Gamma-lifted, the frame is a floor-plan rectangle"*). The cited crack "crossing ~90px
of void" at (0, 255) is at tile (0,4), which is **wall**, at luminance 7.1. §13.9 puts all of it
below the representable floor; none of it is visible in the delivered frame.

| # | flip | ground |
|---|---|---|
| 1 | mask the crack decal to the floor footprint | **wall texture at lum 7.1**, not the floor's crack layer |
| 2 | delete the floating masonry bars | walls |
| 3 | build an actual wall at the room perimeter | walls — the open wall-lane problem |
| 4 | replace the void fill | void |
| 5 | sharpen the joints; they fade toward the light edge | **floor, real** — and it is the ruled *"a packed joint takes the shine"* behaviour, so undoing it needs a ruling, not an edit |
| 6 | vary the 45° hatch; same angle and spacing on a dozen slabs | **floor, real** — a §8.3 motif trap in `stone_marks`, and its own logical change |

Neither floor item is a mottle item, and items 2–4 are the ground the combined build exists to
cover. **The lane does not iterate against them**: the floor solo lane is closed, this round feeds
the combined build, and grinding a mottle PR against wall findings is the scope creep
one-logical-change exists to refuse. Items 5 and 6 are handed forward.

---

## 8. Reproducing

```bash
tools/tier1_floors/rebuild_ashlar.sh
dotnet build UnderWarden.Presentation.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --import

python3 tools/tier1_floors/measure_delivered_palette.py --controls --ladder-delta \
  --capture tools/tier1_floors/evidence/fc_standing.png \
            tools/tier1_floors/evidence/fc_standing.log

.claude/skills/frame-critic/run_frame_critic.sh
```

The energy finding, from the tree as it stands:

```bash
# same scene, energy 0 and 8; the floor column is byte-identical, 15% of the frame is not
python3 - <<'PY'
import numpy as np; from PIL import Image
E="tools/tier1_floors/evidence"
a=np.array(Image.open(f"{E}/energy_probe_e0.png").convert("RGB")).astype(int)
b=np.array(Image.open(f"{E}/energy_probe_e8.png").convert("RGB")).astype(int)
print("%.2f%% of pixels differ" % (100*(np.abs(a-b).max(2)>0).mean()))
print([tuple(a[y,535]) for y in (650,730)], [tuple(b[y,535]) for y in (650,730)])
PY
```
