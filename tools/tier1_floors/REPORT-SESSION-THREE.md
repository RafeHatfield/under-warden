# Tier one, floor session three — the ashlar rebuild, and the ruling on K

**Branch** `worktree-tier1-floors` · **generation budget spent: 0 of 80.** Not one PixelLab call
was made. Session two's measured finding — *generation cannot supply architecture; 0 of 40 base
candidates were clean* — held, and everything below is composition across surfaces from the
material the donor manifest already carries. The budget is untouched and available.

---

## 1. K IS RUNTIME. No K is authored at all.

The question on the record: *can the stone class live at compose/shader time as a per-stone value
remap keyed on shared coordinates — tile set stays small, K becomes runtime — or does §6.3's
authored occlusion fail to survive a remap, so K must be authored?*

**§6.3 was never the blocker.** Measured on the crossing-joint geometry and unchanged by topology:

| remap form | authored joint-to-stone contrast retained |
|---|---:|
| **additive offset** | **100.0%** |
| flat replacement | 0.0% |

The contrast *is* the occlusion — §6.5 derives the joint as dark **because enclosed** — so an
additive offset preserves every internal difference and a replacement destroys the plane. The
remap must be additive, and additive is available at no cost.

**The blocker was the KEY, and it is a theorem about the corner.** Four tiles meet at a grid
corner. Tile (x,r) shares one boundary family with its eastern neighbour, one with its southern,
and **nothing with its diagonal**. A stone covering a corner is therefore seen by four tiles with
no common data and cannot be addressed by any scheme.

> **Authoring K does not dodge this.** Corner classes as an index dimension make the same
> assumption — that a tile's regions map to its four corners. On the crossing-joint geometry 27 of
> 77 stones (19.9% of stone pixels) spanned a boundary with no shared key, and they would have
> failed identically either way. **The geometry decides, not the K.** That is why K could not be
> sized until the rebuild existed.

Re-measured against the ashlar geometry, and re-measured again after every later change to it:

```
wholly inside one tile          -> the tile's own address    : 114
spans exactly ONE vertical bdy  -> that boundary's family    : 112
---- everything below has no shared key and must be zero ----
contains a grid corner                                       :   0
spans a horizontal boundary                                  :   0
spans more than one vertical boundary                        :   0
UNADDRESSABLE                                                :   0   (0.0% of stone px)
crossing-joint geometry, for comparison                      :  27   (19.9%)
```

Cost of the authored alternative, measured and on the record anyway: 457 B/tile, 3 ms/tile —
K=2 → 2,592 tiles / 1.2 MB; K=3 → 13,122 / 6.0 MB; K=4 → 41,472 / 19.0 MB. Cost was never the
discriminator.

---

## 2. Rulings (1) and (2), discharged as one rebuild

### The value step, superseded by construction

| | boundary-to-interior value step |
|---|---:|
| crossing-joint geometry | **7.44×** |
| blended (session two) | 2.95× |
| **course-aligned ashlar** | **0.59×** |

Below 1.00 — a tile boundary is now *quieter* than the material around it. With the grain switched
off it measures **exactly 0.000**, and that is what named the remaining culprit: the residue was
never value, it was texture. Two structural fixes followed rather than a tuning pass.

- **Grain belongs to the stone, not the tile.** `wrap_noise` wraps against *itself*, so two
  adjacent tiles drew unrelated material. A per-tile crossfade between family-keyed fields reached
  0.771 and left tile interiors measurably rougher than tile edges — a smaller lattice, still a
  lattice. Each stone now draws a grain patch chosen by its address and samples it in
  **stone-local coordinates measured from its boundary**, so both tiles either side sample
  consecutive columns of the same patch.
- **So the tiles carry the bond and nothing else.** Value and grain are both painted at compose
  time from the world address. There is no reach, no falloff, no whole-tile renormalisation.
- **And so there is no channel tile set.** Wear is a property of the stone, so the trodden channel
  needs no second 81 tiles and no longer has a straight edge on the tile lattice — it ends where a
  stone ends, which is what §8.2.1 asked for and a per-cell channel could never give.

### The bond, and what each family does

Every one of the four does real work. `fW`/`fE` place the head joints — and because tile x-1's
east family *is* tile x's west family, both tiles agree where their shared stone begins. `fN`/`fS`
carry the arris profile of the bed joint **on** their boundary, built to return to zero at both
ends of a span so a profile change at a vertical boundary is invisible.

Stones are 9–23 px wide against courses of 9–20 px. A tile may **sand one head joint away**,
merging two stones — exactly one, never both, because dropping both leaves a stone spanning two
vertical boundaries, which is unaddressable. The constraint decides it, not taste.

---

## 3. The measurements

Final build, 8×8 field, seed 1337. Everything re-measured after every geometry change.

| | ordinary | with channel |
|---|---:|---:|
| largest connected region (session one: **99.1%**) | **1.5%** | 1.5% |
| stones ≥64px, median size | 223, 190px | 223, 190px |
| **boundary step ratio** (7.44× → 2.95× → …) | **0.63** | 0.45 |
| continuity — lines crossing a vertical boundary | **1.00** (224/224) | 1.00 |
| grid hiding — boundary bed vs mid bed, density / value | **1.00 / 1.00** | 1.00 / 1.00 |
| boundary column vs other columns | 0.56 | 0.56 |
| course heights | **5 distinct** [10,12,15,18,20], modal 0.25 | same |
| distinct joint skeletons in 64 cells | **60**; 12.5% share one, **0 adjacent** | same |
| **full-width joints at the tile pitch** | **9 of 17 — 53%** | same |
| lattice score | 0.254 | 0.257 |

**Channel legibility**, against *the same stones unpolished* (1.000 = delivered nothing):

| texture | variety | arris |
|---:|---:|---:|
| **0.372** | **0.582** | **0.786** |

Every wear term is a **subtraction**. §8.2.1 is binding — polish signals by absence, never
brightness, because under a carried lamp brightness is what the light is saying.

**The shipped asset reproduces all of it.** `verify_atlas_path` walks the atlases the engine
actually reads and compares against the composer, on both arms: **0 of 65536 pixels differ** with
no channel and 0 with the channel declared, and a single altered ladder index is caught. The
engine then reproduces the composer's **finished pixels** — `paint_check=96/OK`, proven failable
on one channel of one pixel.

## 4. Instruments, and the ten defects their own plants found

No instrument's pass counts until it has demonstrated it can fail (§4, bible §13.5). **Eight
plants fire**; building them found ten real defects, seven of them in the instruments themselves.

| plant | fires | on the clean field |
|---|---:|---:|
| `per_tile_value` — stones addressed by the tile that sees them | 52× | 0.59 |
| `value_lattice` — a value ramp on the tile grid | 53× | 0.59 |
| `boundary_frame` — a joint along the tile's own edge | 4.48 | 0.56 |
| `broken_courses` — coursing that does not travel | 0.00 | 1.00 |
| `uniform_courses` — one course height everywhere | 1 spacing | 5 |
| `flat_channel` — wear that takes nothing away | 1.000 exactly | 0.37 |
| `one_family` — every boundary collapsed to one family | 89.1% duplicate | 12.5% |
| `one_course` — one course per tile, every joint a tile edge | 100% at tile pitch | 53% |

**What the plants caught:**

1. **A continuity test that counted head joints as failed lines** and read 0.4978 on a sound
   field. A joint that was never travelling cannot fail to travel.
2. **`0.0 or 1.0` is `1.0`.** The plant that severed *every* course scored a perfect failure and
   was reported as a pass. Explicit `is not None`, never truthiness, on any value whose legitimate
   range includes zero.
3. **A class mask split at the nominal joint position while the joint wandered**, leaving 1px
   slivers of a neighbour's value with no joint between them — the very seam the construction
   exists to prevent, reintroduced beside every head joint.
4. **Every spanning stone exactly 16px wide.** Constant extent is constant position.
5. **An atlas plant aimed at a family combination the field never lays** — nothing read it, and
   the check reported itself decorative. Correct verdict, wrong reason: the instrument was fine
   and the plant was pointed at nothing.
6. **The three wear constants do not share a null.** Grain and spread are multipliers on what a
   stone keeps (null 1.0); arris is a fraction of the way the joint rises (null 0.0). Setting all
   three to 1.0 built the most worn floor the system can make; the instrument correctly reported a
   visible channel; the plant read that as the instrument failing. Two mistakes agreeing.
7. **"Texture" was measuring joint contrast and calling it grain** — a 3×3 window straddling a
   joint reports the joint.
8. **The channel ratio was mostly the band's own content.** On a field with *no* channel, the
   inside/outside ratio for a 2-cell band runs 0.74–1.39 across the seven bands of an 8×8 field —
   wider than the effect being looked for. The band is now differenced against itself.
9. **The plant matcher agreed with a seat by accident.** Round 8's plant seat scored its hit on
   the word *collapse* — inside the sentence *"Not one large collapse."* The verdict was right and
   the matcher had nothing to do with it; it would have said CAUGHT for a seat declaring the floor
   **clean** of every defect in its list. Now negation-aware, and its vocabulary matched to the
   plant this session actually builds rather than session two's mossy one. Both corrections
   declared for the **next** round, not applied retroactively — §4's whole discipline is that the
   test is fixed before the seats run.
10. **A plant mutating shipping data.** The constant-pitch plant appends a degenerate split to the
    splits table, and the composer writes that table into the manifest. In separate processes
    harmless; in one interpreter it would ship a split nobody authored, as art. The composer now
    refuses if the table it is about to write is not the table it declared.

### The gap nobody was watching: between the tool and the game

Every number above was measured on a field the composer built in memory. What **ships** is 81
atlases and a grain bank, read by the engine. `verify_atlas_path.py` walks the shipped path and
compares:

```
first run     331 of 65536 pixels differed — the composer recovered luminance from its own
              colourised output while the atlas stores an exact ladder rung
after that     50 differed — the bank ships as one byte per sample at 1/64, and the composer
              was composing against the unquantised float
now             0 differ, on BOTH arms (no channel, and channel declared)
              and a single altered ladder index in live data is CAUGHT
```

The channel arm was added later and matters: the first version ran with no channel declared, so
every wear term could have been wrong in both directions and the check would still have said
IDENTICAL.

---

## 5. Two things that would have shipped the wrong picture

**There are two files named `UnderWarden.Presentation.csproj`** — one at the repo root and one
at `src/Presentation/`. **Godot loads the root one.** Building the other succeeds, prints
`Build succeeded.`, and changes nothing Godot will run. The symptom was a `Report(...)` line that
plainly exists in `Main.cs` producing no output at all, twice, while the scene rendered happily
with the previous assembly. The same trap applies to `--import --path src/Presentation`, which
prints *"Can't run project: no main scene defined"* and imports nothing; the new atlases then
failed at load with *"No loader found for resource"*, which reads as a bad asset rather than an
un-run import. In that state the floor overlays reported `drawn(grit=0 event=0 channel=0
occlusion=0)` and the capture still looked plausible.

**The family had no way to reach the device.** `build_review_app.sh` has knobs for the scene, the
theme and the overlays and had none for the floor family — so every device build since the
edge-matched family was written showed whatever the theme picked, under the family's name, at the
one gate that decides anything (§13.1). `TIER1_ASHLAR` added. The family cannot ride in on
`TIER0_THEME` because it is not a tile role: the theme's floor entry is a **magenta placeholder**
that exists only so a sprite is present to repaint. Magenta on purpose (LOOP-PROCESS §4.2) — a
theme fallback that looked like stone would mean a capture of the wrong floor reviews as the right
one.

---

## 6. What the blind seat found that four instruments passed

The first round on the ashlar (transcript: `evidence/seats/r7pre_F1_transcript.txt`) **culled it**,
and named three things nothing in this session was measuring.

> **Q1: "Cut stone blocks — rectangular sandstone-brown pavers laid in a running bond, mortared."**

That is the first time in three sessions the material has been named correctly. Session one drew
*brickwork* and then *dried mud*; session two's family read as flagstone but carried a tint
lattice. Ruling (2) delivered its objective.

> **Q5: Repeated.** — and **CULL: "Four hundred years of heavy use and endless crude repair, and
> the floor is unmarked, uncracked, unpatched, unworn."**

The three findings, all answered in this branch:

1. **"The floor reads as a stack of horizontal stripes before it reads as stone."** Enclosure,
   boundary step, continuity and grid hiding all passed that floor, because a regular course
   rhythm is not a tile artefact — it is a property of the material, and it was the first thing a
   human eye reported. The interior bed joint now moves per tile row: **1 course height → 5**,
   lattice 0.378 → 0.273. New instrument `banding`, with its plant.
2. **"It is the same shape every time, which converts it from irregularity into a motif."** The
   head-joint wander was seeded per tile index, so every tile sharing four families drew the
   identical jog everywhere on the map. That is §8.3.1 in its own words — a wander is an
   *incident*, a tile is a *parent*, and there is no amplitude of it that is safe. Head joints now
   run true; the irregularity comes only from world-addressed things that cannot repeat on the
   grid.
3. **"No wear lane, no scuff, no stain, nothing that says traffic went one way."** The channel was
   there — 20 cells — and three rounds of seats have failed to see it. Wear pushed to its
   subtractive limit: grain kept 0.38 → 0.08, spread 0.45 → 0.20, plus a new arris pass.

---

## 6b. Round 7 — two independent seats, and the finding that ends the round

Captures pinned: `scene_ashlar_r7.png` (sha `69008da5…`), `scene_ashlar_plant_r7.png`
(sha `aa878a77…`). **Plant caught** — the control seat culled and named the damage
(*"It has been shot at, or something has been eating holes in it. Fifteen black voids…"*), so the
round is valid and its findings are read.

**The material is now named correctly by both seats, and named as flagstone rather than brick.**

| seat | Q1 |
|---|---|
| F1 | *"Dressed rectangular stone slabs — sandstone or a warm limestone — laid dry in a running bond, thin dark joints between them. **Flagstone paving.**"* |
| F3 | *"Cut sandstone flags — rectangular slabs of one honey-brown stone, laid in courses with thin mortar joints. Dry, indoor, quarried. **Not natural rock, not tile, not brick.**"* |

That is the ruling (2) objective delivered and independently confirmed. Session one drew
*brickwork* and then *dried mud*; the pre-split ashlar drew *"a tidy generic dungeon brick floor"*.
Straight head joints and varied course heights moved it the rest of the way.

**The two seats disagreed about tiling, and an instrument settled it.**

> **F1:** *"The joint layout has a hard 64px period… duplicate 32×32 patches across the whole
> floor, the top matches are all at displacement exactly (64,0) or (0,64), correlating 0.99+."*
>
> **F3:** *"It doesn't tile: no periodic peak in x at any offset from 1 to 44, and only a weak
> 0.068 bump at dy=32 which is just the course pitch."*

F1 was right and F3's method was too blunt: an image-wide autocorrelation averages a handful of
exact duplicates into nothing, while a duplicate-patch search finds them. A cell's skeleton is
fixed by its four families, its row's split and its two merges, and two cells agreeing on all of
those are pixel-identical. Nothing in this session was comparing one cell to another. New
instrument `skeleton_repeats`, with plant `one_family`:

| families | atlases | distinct skeletons in 64 cells | cells sharing one | identical neighbours |
|---:|---:|---:|---:|---:|
| 3 (what the seats saw) | 81 | 47 | **31 — 48.4%** | 1 of 112 |
| 4 | 256 | 51 | 22 — 34.4% | 0 |
| **5 (now)** | **625** | **60** | **8 — 12.5%** | **0 of 112** |

Nearly half the room was drawing a bond some other cell in the same room was already drawing. The
fix is the one axis where more is simply better and the cost is linear — 625 atlases, ~2 MB, a
minute to generate. **More joint POSITIONS per family is not available**: a position must be
agreed by both tiles either side of a boundary, and the boundary's family is the only thing they
share, so position variety *is* family count.

**Both seats culled on the same axis, and it is not the one the family can answer.**

> **F1:** *"Almost nothing. It is a freshly-laid floor… It reads as a floor commissioned last
> month by someone with a budget."*
> **F3:** *"Four hundred years of unrepaired heavy use, and the stone is unworn, unbroken,
> unpatched — reads new."*

And **neither seat found the trodden channel**, in a capture that carried it at the strengthened
values. F1 checked for it explicitly:

> *"I checked specifically for a floor-based reason and there isn't one: the paving in the corridor
> mouth is the identical bond, identical joint width, identical stone tone as the paving in the far
> corner… If you rotated the room 180° the floor would give me exactly the same amount of
> information: none."*

That is four seat rounds now, the last of them against a channel measuring 0.350 / 0.578 / 0.775
against the same stones unpolished — most of what absence has left to take. See §7(b).

---

## 6c. Round 8 — the five-family build, and the finding that ends the session

Captures pinned: `scene_ashlar_r8.png` (sha `f4fe90b0…`), `scene_ashlar_plant_r8.png`
(sha `3a08c85e…`). **Round valid — plant caught.** The control seat culled it and read the
*register*, not merely the craft: *"It has been punctured, repeatedly, by one thing — and it has
never been walked on"*; **CULL:** *"Twenty-two identical black blobs and a single 3° hue: damage
stamped on, not a surface anyone used."* Damage without use is exactly the §8.1 failure the plant
is built to be.

**The material is named correctly by both solo seats, for the second round running:**

> **F1:** *"Cut stone slabs — flat rectangular flagstones of varying size, laid in an irregular
> running bond with a recessed joint between them. Neutral grey stone; **the warm tan is the lamp,
> not the material**."*
>
> **F3:** *"Cut stone flagstones — rectangular slabs of a soft sandstone or dressed limestone,
> laid in courses with thin mortar joints."*

F1's parenthesis is worth keeping: a seat that has never read §6.3 has separated the asset from
the light on it, unprompted. That is the clause working.

**The five-family fix landed, and the seat that would have caught it says so.** F3, on Q5:

> *"The slab **layout** is generated, not tiled — course pitch is fixed at ~30px but slab widths
> and joint positions vary continuously across the field, so **there's no repeating block**."*

Round 7's F1 had found duplicate 32×32 patches at exactly one-tile displacement correlating
0.99+. After 3 → 5 families the same class of seat reports the layout as generated. The
`skeleton_repeats` numbers agree: 48.4% of cells shared a bond, now 12.5%, none of them adjacent.

**And then F1 located the constraint itself, which is where this session ends.**

> *"That hand-work is confined inside a 64px band, and the band boundary is exposed: five
> continuous unbroken full-width joints at exact 64px pitch, which no slab ever bridges."*
>
> **CULL:** *"A full-width unbroken joint every 64px exposes the tile grid — no slab ever crosses
> it."*

That is the corner theorem, found by a blind critic from the pixels alone, and it is not a defect
in the build — it is the price of runtime stone addressing. Now measured (`constant_pitch_lines`:
9 of 17, **53%**) and instrumented with a plant that fires at 100%. See §7(a).

### Where the three seats agree, across both rounds

| | |
|---|---|
| **would it ship?** | YES, all four solo seats, both rounds |
| **is it good?** | *"Merely competent"*, all four, unanimously |
| **material** | named correctly by all four; **flagstone, not brick** |
| **against the asset bar** | ranked **above it, "by a wide margin"** |
| **what they cull** | **use**, not craft — and one of them, the tile pitch |

> **F3, round 8 CULL:** *"One hue, random per-slab tone, zero wear or repair — reads generic
> dungeon, not four-hundred-year contested underworld."*

**One hue is not a defect and should not be "fixed" without a ruling.** §5.4 reserves warmth and
holds that chroma is signal, so a floor with no hue variation is the clause obeyed, not broken — a
seat that has not read the bible cannot know that, and this is exactly the case §13.4 anticipates,
where an unbriefed critic asks for something a clause forbids. *"Random per-slab tone"* is the
half of that cull worth taking: the per-stone values are drawn from a distribution and clustered
in coarse patches, and they carry no meaning. Values that meant something — a batch of stone
replaced, a wet corner, a patch scorched — would need a **cause** in the map to key on, which is
scene information the floor family is not given.

---

## 6d. Round 9 — VOID, and what may and may not be taken from it

Captures pinned: `scene_ashlar_r9.png` (sha `572878d8…`), `scene_ashlar_plant_r9.png`
(sha `1a4f4903…`).

> ### ⚠ ROUND 9 IS VOID. `plant_caught: false`, zero vocabulary hits.
>
> §4 is unambiguous: *if the critic does not catch the plant, the round is VOID and its findings
> are not read. Not discounted — void.* **Its seat opinions below are recorded as PROVISIONAL and
> are not rulings.** They are written down because the round happened, not because it counts.

**The critic was not soft; the MATCHER's vocabulary was short.** The plant seat culled, and named
the plant's damage plainly:

> *"It's been shot through with **holes** and scribbled on, evenly, everywhere, by nothing in
> particular. There are roughly twenty punched-through black **holes** across a floor of about 92
> tiles."*
> **CULL:** *"Damage is uniform decorative scatter — the ground records no traffic, no event, and
> no repair."*

It used *hole* fourteen times. **The plainest word for the plant's most prominent feature had never
been in the declared list**, through three rounds — while the list carried *lichen*, which this
plant has never contained. The matcher's two `collapse` hits were correctly rejected: one is the
negation *"no collapse edge"*, the other is in the flip list.

So the test worked exactly as designed and the round is genuinely void. The defect is that the
vocabulary was inherited from session two's mossy, cobwebbed plant and then widened, after round
8, **by reading round 8's transcript** — which is the same error as relaxing a threshold after
seeing a result: the test ends up derived from the outcome.

**Corrected for the next round, and not applied to this one.** The list is now derived from
`plant_ashlar.py`'s three draw calls and nothing else, as stems, with a standing obligation to
change when the plant's construction changes. Verified four ways: it catches rounds 8 and 9 on
their own words; it rejects a transcript that *denies* every defect; and it rejects one naming only
the family's legal cracks — which matters more than ever now that the FAMILY draws cracks as its
primary incident.

> **Round 9 is not re-declared valid by that correction.** It ran under the list that was declared
> when it ran.

### What survives the void: the measurements

Instrument output is not a seat opinion and does not depend on the round. These stand:

| | retired per-tile overlay | field-scale network |
|---|---:|---:|
| connected marks over the field | 127 | **4** |
| **median mark** | **4 px** | **129 px** |
| marks crossing a tile boundary | — | **75%** |
| crack-meets-bed-joint junctions (8×8 field) | — | 42, components 1–4px |

### Provisional, from a void round

**The integration appears to have worked, and the seat said so unprompted:**

> *"The cracks are real and **they're the best thing here** — I count 23 distinct strokes over 10px
> in both axes, individually drawn, sweeping across the room (the long arc from ~(250,290) curving
> to ~(470,395), the X crossing at ~(430,320))."*

Against the system it replaced, whose median mark was four pixels and which the same class of seat
reported as *"No cracks. Not one. Across ~140 visible blocks."* Material named correctly for the
fifth consecutive seat: *"Cut stone paving — rectangular flagstones of varying width laid in
courses, dark mortar joints between them."*

### And then it found the next one, and it is §8.3.1 in a place the clause has not been before

> **CULL:** *"All damage snaps to a grid — 138 copies of one 4×4 wedge, every one glued to a
> joint."*
>
> *"Every single one is locked to a horizontal mortar joint… Each value is exactly 5px above or
> 3px below a joint. Never mid-slab, never on a vertical joint, never off-grid. So the room has
> ten horizontal ribbons of identical little arrowheads… they read as UI ticks, not damage."*

**Measured rather than guessed.** The composed field's own 2×2 pattern census shows nothing
anomalous beside the bed lines (158 distinct patterns adjacent, 171 well away) — so the motif is
not *in* the tiles. It is an **interaction**:

```
crack-meets-bed-joint junctions in an 8x8 field : 42
their component sizes                           : 1-4 px  (a 2x2 art shape at 2x display)
scaled to the seat's room                       : ~119
the seat's count                                : 138
```

**Every crack crosses every bed line the same way, because neither one deflects.** So the junction
is the same little shape every time, in ribbons along the horizontals — and §8.3.1's motif trap has
arrived through an *interaction between two systems*, where both systems are individually
incident-free and world-addressed. That is a genuinely new location for the clause and it is
recorded here as one.

The seat's own flip list already names the fix, and it is the same sentence as its other crack
criticism:

> *"They sit **on** the floor rather than **in** it."*
> *"Give the cracks physical consequence: widen them at the mid-span, drop the slab a value on one
> side, and **spall the mortar where a crack meets a joint**."*

**Not done in this session, deliberately.** The bound was one integration and one seat check, and
both are spent — and the seat check came back void, so acting on its finding would be acting on a
reading §4 says is not read. Making the change now would put in front of the human gate a state no critic has
seen — which is the failure §1.1.1 exists to prevent, in the other direction. The walk build is
therefore **exactly what round 9's seat looked at**, and Rafe's eye is the next instrument under
the three-outcome rule.

### The rest of round 9, for the record

- **Q2 is still directionally silent** — expected, and no longer a defect at this gate: Ruling 70
  removed the channel from scope.
- *"Put texture inside the slab faces — 2–3 value steps of grain or pitting per face"*. The grain
  is there and is world-addressed; at the delivered contrast it is not reading as grain. A real
  finding for the next round, not a regression.
- *"Break the courses… so the eye can cross the room instead of riding stripes"* — the corner
  theorem again (§8.3.3), independently found for the second round running, and now ruled
  permanent.
- **Q6: YES it would ship; merely competent.** Five for five across three rounds.

---

## 7. On the record for ruling

### (a) The corner theorem constrains the ART, permanently, and should be written down

**A blind seat found this constraint on its own and culled for it.** It is no longer a candidate
clause offered on reasoning; it is a measured, disqualifying defect with a structural cause.

> *"Five continuous unbroken full-width joints at exact 64px pitch, which no slab ever bridges.
> **That is a tile edge, not a mason's decision. A mason lays a long stone across a course line; a
> tiling engine cannot.** The variety is decoration painted onto a grid that is still visible
> underneath it."*
>
> **CULL:** *"A full-width unbroken joint every 64px exposes the tile grid — no slab ever crosses
> it."*

Runtime K is bought with a geometric constraint no floor family can escape: **no stone may span a
horizontal tile boundary**, because four tiles meet at a grid corner and the diagonal pair share
nothing to address a stone with. Therefore a full-width joint sits at exactly one tile pitch, for
ever, in every region, in any floor whose stone values are addressed at runtime.

`grid_hiding` could not see it and was never going to. It asks whether the boundary line *looks*
different from the mid-tile line — and the answer is no, they are identical, 1.00 on both density
and value. The seat's question is sharper: **how many of the lines sit at a pitch a viewer can
predict?** The interior lines move with their row's split; the boundary line never does. New
instrument `constant_pitch_lines`, with plant `one_course`:

| | |
|---|---:|
| full-width joints in an 8×8 field | 17 |
| **sitting at the tile pitch** | **9 of 17 — 53%** |
| the same field with one course per tile (the plant) | 100% |
| **the floor this number can reach** | **not 0, while K is runtime** |

**The trade, stated plainly, because it is a ruling and not a tuning pass:**

| | runtime K (this build) | authored value on the tile |
|---|---|---|
| stone value across a vertical boundary | exact, 0.000 before grain | a step, or a blend that hides one |
| horizontal joints | **constant pitch, seat-visible, 53%** | free — a slab may cross anything |
| tile count | 625 | 625 × K⁴ *and the corner is still unaddressable* |

The third column is the one worth staring at: **authoring K does not buy the geometry back.** It
was measured on the crossing-joint family and it is a theorem, not a budget — corner classes as an
index dimension make the same assumption about stones mapping to corners, and the 27 unaddressable
stones fail identically. The only thing that buys a slab crossing a course line is **giving up
addressed stone values there**, and accepting whatever seam that leaves.

**Mitigations that do not need a ruling**, in the order I would try them:

1. **More courses per tile.** Three courses puts the boundary at 1 line in 3 — 53% → ~33% — at the
   cost of ~10px courses, so ~8px stones. The register may not want stones that short.
2. **Interrupt the line with incident rather than with stone.** A slab may not *cross* the
   boundary, but nothing stops debris, a driven pin, a lashed plank or a spalled edge from sitting
   *over* it. Incident is world-placed overlay, not bond (§8.3), so it costs nothing structurally
   — and it is what every seat has asked for anyway. This is the one I would do next, and it is
   blocked behind §7(c): the overlay system currently emits marks with a **median size of 4px**.

Option 2 answers both live culls at once — the floor gets its four hundred years of history, and
the one line that can never move stops being continuously visible. It is not attempted here
because it is overlay work, and the family was what was briefed.

### (b) §8.2.1's "signals by absence" has a floor, and it has now been located

**This is the trigger this session most wants a ruling on, and it is no longer a prediction.**

Absence is bounded: subtract enough and there is nothing left to subtract. The channel was
strengthened to the limit of what subtraction can do —

| | before | now |
|---|---:|---:|
| grain a trodden stone keeps | 0.38 | **0.08** |
| value spread it keeps | 0.45 | **0.20** |
| arris (joint depth lost beside worn stone) | none | **0.45** |

— which measures **0.350 / 0.578 / 0.775** against the same stones unpolished. Two thirds of the
grain, two fifths of the value variety and a quarter of the joint depth, all gone.

**Round 7's two independent blind seats still did not see it.** Not "found it weak" — did not
report it at all, in a capture carrying 20 channel cells, with one of them checking for it
explicitly:

> *"I checked specifically for a floor-based reason and there isn't one: the paving in the corridor
> mouth is the identical bond, identical joint width, identical stone tone as the paving in the far
> corner. No path is worn smoother, no direction is more scuffed, no threshold is dished from feet.
> **If you rotated the room 180° the floor would give me exactly the same amount of information:
> none.**"*

That is four seat rounds across two sessions. The clause's own words are *"polish signals by
ABSENCE, never brightness"*, and the reasoning behind it is sound — under a carried lamp,
brightness is what the light is saying, and session two watched a seat read the old channel's
value lift as the torch. But the measurement now says the permitted lever, driven to its limit,
is **below the perceptual floor at 32px under that lamp**.

**Three ways out, and the choice is not mine:**

1. **Absence stands, and the channel is abandoned** as a thing the floor can express — traffic is
   carried by incident (debris drift, worn edges, damage at chokepoints) rather than by polish.
2. **§8.2.1 is amended** to permit a bounded value change on trodden stone, with the amount
   derived rather than chosen, and with the torch-confusion risk carried explicitly.
3. **The channel changes shape** — polish widened from stones to a route the eye can follow at
   room scale, rather than a two-cell band, so that absence has enough *extent* to read even
   though it has little *contrast*.

Option 3 is the one this session would try next under its own steam; it is the only one that does
not need a clause changed. It is not attempted here because it is a scene-design change, not a
family change, and the family is what was briefed.

### (c) The cull axis is §8.1 damage and repair, which the FLOOR FAMILY cannot supply

*"No cracks. Not one. Across ~140 visible blocks."* — while the overlay system reported
`drawn(grit=45 event=44 occlusion=64)` in the same capture. §8.3 puts incident in the **overlay**,
not the tile, and correctly so: incident baked into a parent is the motif trap. So the family
cannot answer this cull by itself.

**The gap is measurable, and it is not a matter of taste.** The same scene captured with and
without the overlay system, differenced over the lit ground:

| | |
|---|---:|
| pixels the overlays change at all | 48.72% |
| changed by **≥ 1 ladder step** (13.23) | **7.21%** |
| changed by ≥ 2 ladder steps | 1.09% |
| mean delta where changed | 8.18 luminance — *below one rung* |
| connected marks of ≥ 1 step | 127 |
| **median mark size** | **4 px** |
| marks larger than 20 px | 26 |

So the incident is overwhelmingly **sub-rung noise at a median of four pixels** — at the review
build's 2× display, a two-by-two speck. That is not a crack; it is the "pepper" an earlier seat
named (*"~1,250 single-art-pixel dark dots spread evenly over the entire floor... at phone size
the floor reads as static before it reads as stone"*). A system emitting 127 marks of which 101
are smaller than a fingernail clipping is not depicting four centuries of use, and no amount of
work on the FLOOR FAMILY will change that number.

Named here rather than absorbed: this is the overlay system's finding, not the family's, and it
is the live half of the cull the family cannot answer.

---

### (d) THE RIG DISSOLVES THE PALETTE, and this is not a floor finding

Round 7's comparative seat ranked this floor **above the asset bar, "by a wide margin"** — the
bar's ground *"fails on a measurable, disqualifying defect: a single 48×48 tile stamped
bitwise-identically across the entire floor, and a material that cannot be identified"*, against
*"a real authored surface with genuine slab-level variation that no repeat test can catch"*. Then
it named something nobody had looked for:

> *"The art is drawn at 2× (every pixel is a 2×2 block — I confirmed the column-duplication
> pattern directly), but the lighting is a smooth per-screen-pixel ramp (red channel walking
> 62→166→42 in single-unit steps along y=250), and the soft stain blobs are gaussian smudges.
> **Soft unquantised gradients sitting on top of hard chunky pixels. Mixed resolution.**"*

Measured on the round-8 capture, over the lit ground:

| | |
|---|---:|
| 2×2 screen blocks that are a single flat colour | **7.58%** |
| mean colour spread *inside* one 2×2 block | 8.20 |
| **distinct luminance values in the lit ground** | **1,013** |
| rungs in the family's ladder | **7** |

**Every rung of palette discipline in the composer is dissolved before the image reaches the
eye.** §4.3 forbids anti-aliasing and §5.1 is a zero-mercy palette; the family is quantised to
seven values and refuses to emit anything off them, and `verify_atlas_path` proves the shipped
asset is exact to the rung. The renderer then multiplies it by a light computed at screen
resolution and delivers a thousand.

**This is not a floor finding and cannot be fixed in a floor family.** It is a property of the rig
— `PointLight2D` and `CanvasModulate` evaluating per screen pixel while the art is authored at
32px and displayed at 2×. It affects every asset in the game equally; floors are simply where it
was first measured, because floors are most of the screen.

Whether it is *wrong* is a ruling and not mine to make. §6.3 holds that assets **receive** light
rather than depicting it, and a smoothly-lit chunky sprite is receiving light. But the register
the bible names is chunky, and a game that authors to seven values and ships a thousand is not
working to the palette it wrote down. The narrow shape of a fix, if one is wanted, is a renderer
setting rather than an art change: render the lit scene at art resolution and upscale
nearest-neighbour, so the light lands on the same grid the art does.

Recorded with its measurement, and left alone.

---

## 7b. The four rulings, and what was done with each

### (1) Corner theorem — ACCEPTED as a permanent platform theorem. Landed as bible **§8.3.3**.

Register-justified: institutional masonry is coursed to spec, the tell is provenance, and
through-stone irregularity belongs to the orc layer *above* the floor. Recorded with its proof,
its measurement, and the note that authoring K does not buy the geometry back.

**"Push toward the 33% floor only if cheap" — it is not cheap, and it would trade away the thing
the seats praised.** Examined rather than asserted:

| what a third course costs | |
|---|---|
| course height | 16 → **~10px**, so stones ~9px tall against today's 14–20 |
| atlas cells per family | 36 (4 splits × 9 merges) → **108** (× 27 merges) |
| atlas size | 192×192 → 352×352; the set 2 MB → ~5.6 MB |
| class model | ids 1..6 → 1..9, arrays and both cross-check vectors regenerate |
| engine | `Courses`, cell indexing, `CourseOriginY`, the per-class arrays |
| **what it buys** | the boundary line is 1 line in 3 rather than 1 in 2 |

The last row is the argument against it. **The boundary line stays the only line at a predictable
pitch either way** — three courses dilutes it, it does not hide it — while ~9px courses put stone
height back near the brick reading that two rounds of work moved it away from. Both seats that
have praised this floor praised the same thing: *"flat rectangular flagstones of varying size"*,
*"somebody deliberately mixing widths"*. Not taken.

### (2) Channel — **Ruling 70** recorded in §8.2.1; removed from this gate's scope.

The clause's reasoning is carried forward intact, the bound is recorded with its numbers, and the
implementation stands (wear is per stone from the map, so the channel ends at a joint). The §5.4
chroma-signal experiment is filed as **issue #159**, M5, `thread:art`, `type:idea` — with the
condition that it fails if the shift reads as *warmer light here* rather than *different stone
here*.

### (3) §5.1/§4.3 scoped to authored pixels. Landed in **§5.1**.

> **Instruments measure SOURCES. Captures measure LEGIBILITY.**

With the consequence stated where someone will look for it: **do not "fix" the continuum by
quantising the light.** No clause asks for it, and it would be a renderer change made to satisfy
an instrument pointed at the wrong artefact.

### (4) Incident at field scale — done, and the per-tile marks are retired.

A crack is now one event that happened once, and it is long. It belongs to an **anchor tile** and
runs for whole tiles beyond it; every cell it crosses generates the same polyline from the same
world address, which is the construction that makes a stone continuous, applied to a line.

| | retired per-tile overlay | field-scale network |
|---|---:|---:|
| connected marks over the field | 127 | **4** |
| **median mark** | **4 px** | **129 px** |
| largest mark | 4838 px *(the occlusion band, not a mark)* | 559 px |
| marks crossing a tile boundary | — | **75%** |
| mean delta where changed | 8.18 — *below one rung* | on-ladder, exact |

**Minimum readable extent is a refusal, not a preference:** a crack shorter than three tiles is
not drawn. A mark too small to read is not cheap — it spends contrast and returns noise.

**No taper, no feather, no alpha.** The old crack tapered to nothing at both ends and was
feathered into the floor, which is most of why its median mark was four pixels — and a feathered
edge is an anti-aliased edge, which §4.3 forbids in authored pixels. Cracks are now drawn into
the cell's own pixels on the family's ladder, at the joint's own depth, because a crack is dark
for the same reason a joint is (§6.5) and one that met a joint at a different value would announce
itself as a decal laid over the bond rather than a split through it.

**Retired:** `event` and `grit` per-tile overlays under this family (`drawn(grit=0 event=0)`).
**Kept:** the occlusion overlays — §12.1's plane boundary is form, not a mark.

Two integer traps were caught on the way, both of which would have desynced the engine from the
composer **silently and only near the map's origin**, which is exactly where a review scene sits:
Python's `//` floors where C#'s `/` truncates, and Python's `%` returns non-negative where C#'s
does not. The anchor scan reaches eight tiles left and up of the cell being painted, so at x=0 it
visits negative tiles. Neither would have thrown.

New instrument `crack_field` — extent and boundary-crossing — with plant `tile_confined_cracks`,
which restores the retired system's defect by clipping every crack to the tile that anchors it.
**Nine plants now, all firing.** The first version of that plant clipped on a tile-local
coordinate and was tautological; it reported SILENT, which was the correct verdict about a plant
that was not planting anything.

---

## 8. State

- `K` — **RULED RUNTIME**, on 0 unaddressable stones, re-measured after every geometry change.
- Rulings (1) and (2) — **discharged by construction**; the value step is 0.000 before grain and
  0.59 after it.
- Preconditions 1–4 — carried forward from session two, precondition 4 closed at commit
  `416f105c`.
- Round 7 seats — **complete and valid** (plant caught). Material named correctly by both solo
  seats and as flagstone rather than brick; culled by both on §8.1 use; ranked **above the asset
  bar** by the comparative seat.
- Round 8 seats — **complete and valid** (plant caught). The five-family fix confirmed by the
  seat class that found the defect; material named correctly by both solo seats; culled on **use**
  by both, and on the **tile pitch** by one — the corner theorem, found from the pixels alone.
- **The floor is not gate-ready and is not offered as such.** Four solo seats across two rounds
  say it would ship and none says it is good. Every remaining cull traces to something outside the
  family: the corner theorem (§7a), §8.2.1's absence floor (§7b), the overlay system's 4px median
  mark (§7c), and §5.4's reserved chroma, which a blind seat reads as a defect and the bible
  requires.
- The engine reproduces the composer's **finished pixels**: `paint_check=96/OK`, proven failable
  on one channel of one pixel.
- Device — `TIER1_ASHLAR` knob exists and the family has **not** been to the handset. Nothing here
  has been to the device and nothing here is claimed to have passed a gate. The device leg is one
  command with the phone unlocked:

  ```
  TIER0_SCENE=res://src/Presentation/assets/tier0_harness/scenes/tier1_floor_review.json \
  TIER0_THEME=res://src/Presentation/assets/tier1_ashlar/tile_themes_tier1_ashlar.yaml \
  TIER1_OVERLAYS=res://src/Presentation/assets/tier1_floors/MANIFEST.json \
  TIER1_ASHLAR=res://src/Presentation/assets/tier1_ashlar/MANIFEST.json \
  tools/tier0_harness/build_review_app.sh

  tools/tier0_harness/verify_on_device.sh
  ```

  **All four environment variables are load-bearing.** Omit `TIER1_ASHLAR` and the phone shows the magenta
  placeholder, which is the point of its being magenta. Omit `TIER1_OVERLAYS` and the incident
  disappears. Read the painter's line back off the device log before believing anything: it must
  say `laid=…  missing=0  edge_check=…/OK  stone_check=…/OK`.

### Reproducing everything in this report

```
python3 tools/tier1_floors/compose_ashlar.py          # 625 atlases + the grain bank
python3 tools/tier1_floors/plant_ashlar.py            # LOOP-PROCESS §4's plant
python3 tools/tier1_floors/export_theme_ashlar.py --plant
python3 tools/tier1_floors/field_ashlar.py --plants   # 7 plants, then the field
python3 tools/tier1_floors/probe_stone_address.py     # the K ruling
python3 tools/tier1_floors/verify_atlas_path.py --plants   # tool vs shipped asset
python3 tools/tier1_floors/measure_overlay_legibility.py   # needs both captures

dotnet build UnderWarden.Presentation.csproj      # ⚠ THE ROOT ONE
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --import   # ⚠ ROOT PATH
```

⚠ **Both of those warnings are load-bearing** — see §5. Building
`src/Presentation/UnderWarden.Presentation.csproj` succeeds and changes nothing Godot runs.

**A round's evidence must not move under it.** Round 3 was left running while the family was
rebuilt and its capture overwritten in place, so the seats still queued would have judged a
different build than the first seat saw — four opinions of one floor, and nothing would have
failed. That round is stopped and kept at its first seat only. `run_seats` now hashes every
capture before the first seat, re-checks before each one after it, and refuses on a change; and
captures are round-scoped so a later round cannot need to overwrite an earlier round's evidence
at all. A rule that removes the hazard beats a check that catches it.
