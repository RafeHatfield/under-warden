# The Under-Warden — ART-BIBLE v0

**Status: v0.15 — DRAFT. Two clauses have been derived from rendered assets on the device (§6.3)
or ruled at the gate on them (§8.3); §6.5 and §3.1 are measured against the asset bar and ruled,
awaiting the device gate; §3 is ratified for walls and §3.2 RULED for objects. Everything else
in this document still has not been derived.**

This bible is written *before* pixel work, deliberately. It records decisions taken in
conversation during Phase 1–3 of the art-direction rework (2026-08). It graduates to **v1**
only when the Phase 5 pilot has ratified the clauses marked PROVISIONAL and filled the
clauses marked PLACEHOLDER with derived values.

**Governing principle, inherited from Gemfall: no law is ratified ahead of its derivation.**
Where a rule has a design purpose but no measurement behind it yet, it is marked PLACEHOLDER
and says so. A bible that states numbers it never measured is worse than one with gaps,
because the gaps are honest and the numbers are not.

**Clause status vocabulary:**

| Marker             | Meaning                                                      |
| ------------------ | ------------------------------------------------------------ |
| **LOCKED**         | Decided and ratified. Changes only by explicit revision with a recorded reason. |
| **PROVISIONAL**    | Decided as a direction, pending pilot evidence. Expected to survive; may not. |
| **PLACEHOLDER**    | A rule with a stated design purpose and no derived value. Not law. Not usable as a gate. |
| **RETIRED**        | Formerly law, now struck, kept in place with the evidence that struck it. |
| **NOT LEGISLATED** | Measured or observed, deliberately not turned into a rule.   |

---

## 1. Register — LOCKED (Phase 1 sign-off, 2026-08)

> **The world does not notice you.**
>
> The Paths of the Dead are administered, not haunted. They have been in continuous heavy use
> since before anyone's records begin, and no one has ever cared for them. Nothing here is
> arranged for your benefit — not the light, not the horror, not the way out.
>
> - **The art plays it straight.** The world can be absurd; it is never cute. Every joke in
>   this game comes from Sasha and Hollowmark, and the world's refusal to join in is what
>   makes them a pair.
> - **Nothing is ruined; things are used up.** Surfaces record traffic. Where there is decay,
>   it means no one walks there — and that is information the player can use.
> - **Everything is held.** Nothing in the Paths is monolithic or self-supporting. Bound,
>   strapped, pinned, sealed, tagged. The world is made the way Marya was made.
> - **The light withdraws as you descend.** You arrive as the only thing here that burns. You
>   end somewhere lit for its own purposes.
> - **Sasha and Hollowmark are the warmest thing on screen, always.**
> - **Nothing is staged.** The horror is in the corner, tagged, and the corridor goes past it.
>
> **Anti-references:** Oryx's adventure-cheerfulness. And the off-the-shelf bureaucratic-dread
> kit — beige, rubber stamps, Kafka-by-numbers — which is the same trap in a different corpus.

### 1.1 The division of labour — LOCKED

**The art plays it completely straight. The voice carries all the warmth.**

Sasha and Hollowmark are the only funny thing in the game. The dungeon never winks. Every
joke on screen comes out of the two of them, and the world's total refusal to join in is what
makes them a pair rather than a tone.

This diagnoses the previous track's failure more precisely than "wrong tone." Oryx's art was
doing comedy *too* — a funny voice on top of a funny world, so the voice had nothing to push
against and the whole thing collapsed toward whimsy. It was not a mismatch. It was
**redundancy**: the art was making the writing's job impossible.

**The expression budget for world creatures is zero — RULED from the study pass.** The asset
bar's seriousness is substantially this one refusal: helmets, hoods, shadowed cowls, blank
skulls; nothing smiles, nothing emotes. Yarl adopts it as law: **world creatures do not have
readable faces.** Hidden, shadowed, or structural. The one face in this game belongs to Sasha,
because the warmth is his to carry — and even his is spent sparingly at sprite scale.
(Portraits, if the game ever wants them, are the sanctioned place for faces at dialogue range —
see §16.)

### 1.2 The generative test — LOCKED

**If a rule cannot tell you how to draw a thing nobody has drawn before, it is a bad rule and
it gets cut.**

The register is a function from arbitrary noun to Yarl asset. This is the property the
previous track lacked: there was no rule that could tell you what an Oryx-conformant version
of an unprecedented object looked like, only a corpus you could search and fail to find one in.

Worked example — a gold-encrusted bidet, which is the deliberately absurd test case:

- *Everything is held* — the gold is not inlaid, it is fixed on: pinned, banded, clamped by
  someone who wanted it not to come off. Visible hardware. Plumbing strapped to something
  structural.
- *Used up, not ruined* — gold worn through to base metal wherever a hand or hip has touched
  it. Polished bright on contact surfaces, dull and grimed everywhere else. Basin dished.
- *Nothing is staged* — not on a plinth in a rotunda. In a corridor, against a wall, slightly
  in the way.
- *Tagged* — it has an inventory number. Someone filed this thing.
- *Light* — it does not glow. It catches whatever light the region provides.

The constraints did not restrict the content. They told us how to draw content nobody had
considered. **Unrestricted content is a consequence of the bible being generative, not of the
gates being soft.** Yarl's gates are as strict as Gemfall's.

### 1.3 The named trap — LOCKED

**External-corpus matching is forbidden as a bar.**

The previous track measured conformance to a fixed external corpus (Oryx 16-bit fantasy).
Every drift was a defect, no rule generalised, and every asset was a fight. Nothing in the
instrument stack ever asked *"does this look like Yarl?"* — only *"does this match Oryx?"*

**The lesson is NOT that strictness was the problem.** Gemfall is stricter than the Oryx track
was — a locked palette with zero off-palette pixels tolerated, deterministic conformance, no
hand-edit carve-out — and it produces assets nightly. The variable is **who owns the target.**
Yarl conformed to a corpus someone else shipped, where the answer to "is this right?" lived in
files we did not write. Gemfall conforms to a document its author wrote, which can be amended
when it is wrong and can answer questions about assets nobody has drawn yet.

**This bible is the target. Nothing external is.**

Quality comparison against shipped games (§13.3) is a different operation from style
conformance and is permitted. The distinction is thin and is stated explicitly there.

---

## 2. Scope and derivation population — LOCKED

**This bible is written for five regions and derived from one.**

Build target is a **demo: the first region (the Boundary, floors 1–5), built to a finish
standard.** This aligns with the settled monetisation shape — first region free with
persistence, single unlock — so the demo is not a detour from the game; it is the part that
has to be best anyway, because it sells the other twenty floors.

Consequences, binding:

1. **Region-identity *mechanisms* are in scope now** (§5.2 reserved slots, §6.2 light arc,
   §7.3 binding authority), because they constrain how the Boundary is drawn.
2. **Only the Boundary's *values* are ratified.** Every other region's values are PLACEHOLDER
   and will be derived when that region is built.
3. **The derivation population for every constant in this document is the Boundary corpus.**
   Per Gemfall's Ruling 56: an instrument's verdict is valid only over the population it was
   derived from. Applying a Boundary-derived constant to a Weighing asset is a finding about
   the instrument, not about the asset.

### 2.1 The scope trap — named in advance

**Five floors to a finish standard is not obviously less work than twenty-five to an adequate
one**, and there is no next region forcing a stop, so the bar can drift upward indefinitely.

The park states apply and are law here: **"finalised, not iterated"** (stop improving this, it
is done) and **"prepared, not generated"** (staged, deliberately not spent on). A tier that has
been declared finalised does not reopen because a later tier raised the standard.

---

## 3. Projection and grid — **RATIFIED 2026-09-09**

- **Orthogonal square grid. Portrait orientation. Not isometric.**
- **Volume lives in what stands up, not in the ground plane.** The floor stays flat. **Walls**
  (continuous runs) present exactly two visible planes: a **front face** and a **top surface**.
  **Objects** present three: front, top, and a **right side receding at 45°** — cabinet
  oblique, **RULED for objects 2026-09-12, §3.2**. Round objects keep a true-circle top and a
  vertical body (§3.2). Wall ends, corners and pillars follow the objects (§3.2, follow-up).

⚠ **The two-plane clause below was drafted for objects and walls together and ratified for
WALLS only** — the 2026-09-09 walk was of a scene with no objects in it (Rafe, 2026-09-11). The
object half was provisional until §3.2 ruled it. Where the prose beneath says *"objects and
walls"*, read *walls*; §3.2 is the object law.

**Rationale.** Isometry is a way of showing volume, and its cost is paid entirely by the ground
plane — diagonal grids waste screen corners, visible tile count drops, and portrait is the
worst aspect ratio for it because the diamond's long axis runs the wrong way. The benefit
shows up in objects and walls, which gain a second face and start reading as things that
occupy space. **An isometric floor tile buys nothing; it is a diamond instead of a square.**
Orthogonal ground plus two-plane volume keeps the benefit and refuses the cost.

The two-plane rule survives from the retired Oryx track unchanged. It was never an Oryx rule —
it was a way to draw volume without paying isometry's tax, and it is re-adopted on its own
merits.

**STATUS (2026-09-09): ✅ RATIFIED AT THE DEVICE GATE. PROVISIONAL CLEARED.**

Rafe, walking build `9022b179` on the SE, on real floors and real walls:

> **"actually looks a lot better… pretty good; the floor largely works."**
> **"§3 RATIFIED — two-plane, no side face: walls read as mass, sides exist, the grammar holds
> at the gate on real assets."**

**THE RULED CONDITION IS DISCHARGED.** §3 rode provisional under *"depth arriving ratifies §3;
depth failing reopens it, with evidence"* (2026-08-26), and it has ridden that way through the
composition spike's eight noes, the 2026-08-27 device FAIL, the Q3 control, the sighted round,
the tier-one surface and four polish rounds. **Depth arrived.** Two-plane construction with no
side face is now law rather than a hypothesis under test, and the clause it was tested against —
*every seat asking for a side face* — was answered by the Q3 control finding that **the commercial
bar carries the same missing plane and does not suffer for it**.

⚠ **WHAT RATIFICATION DOES AND DOES NOT SETTLE.** It settles the PROJECTION: orthogonal square
grid, portrait, two visible planes, flat floor. It does not settle how well any particular family
executes it — the walk that ratified the clause routed two wall items in the same breath (the
top-to-face turn reading as two objects, and a repeating vertical at tile pitch), and both are
polish debt against a ratified grammar rather than evidence against it. **A ratified clause can
still be badly served by an asset; that is a build problem, not a law problem.**

**The status trail below is kept in full.** It is the record of a clause that was nearly struck
twice and was right both times, and the reasoning that held it — particularly the Q3 control —
is the reason it survived to be ratified rather than abandoned on the eight-round evidence.


The composition spike's condition fired: two-plane construction with invented numbers, judged
absolutely, in an all-top adversarial scene, did not deliver depth — eight rounds, eight noes,
every seat asking for a side face. **Three confounds are named against that evidence, and they
are why the section is under test rather than struck:**

1. **The scene contained zero §3-qualifying face cells.** A §2.2 violation in spirit: the
   shipping game is rooms and reveals, not only chokepoints.
2. **The seats judged in the absolute, with no genre grammar.** §13.3's comparative frame exists
   for exactly this.
3. **The construction numbers were invented**, where the bars' are measurable.

**Declared criterion, before the sighted round runs:** recipe-driven construction — measured
from the bars, pixels never crossing — judged **comparatively**, in a fair mixed scene. **Depth
arriving ratifies §3. Depth failing at budget reopens §3 for real**, side faces become a live
design question, and the licensing fallback becomes a real option to be argued honestly. Last
resort, but named — and named before the round, not after it.

**The reopening evidence stands exactly as recorded below; what it now feeds is that test rather
than an immediate ruling.**

**STATUS TRAIL (2026-08-27, AT THE DEVICE GATE): FAIL. §3 IS NEITHER RATIFIED NOR REJECTED —
IT RIDES PROVISIONAL INTO TIER ONE, per this bible's original sequencing, carrying the recipe,
the flat-top rule (§3.1) and the Q3 control finding intact.**

Rafe's verdict, recorded in his own words because a trail that paraphrases a gate is not a
trail:

> **"Gate verdict: FAIL — the phone overrules the stills."**
> **"two seats ranked this above the bar; the device says otherwise; §13.2 vindicated again."**

**What this does and does not overturn.** It does not touch the Q3 control finding below — the
bar's north–south walls still have no thickness, and that measurement is independent of whether
Yarl's execution is good enough. It does not touch §3.1 or §6.5, which were ruled on the
round's measurements rather than on its verdict. **What it overturns is the inference that
two blind seats ranking the candidate above the bar meant the construction was finished.** It
did not, and the phone said so.

**Two laws come out of the gate and are recorded where they belong:** the motif trap extends to
wall material (§8.3), and the rig gets a readability-tuning pass before any asset is judged
through it (§6.2). **Both are consequences of looking at this on a phone at gameplay distance,
which is the one thing no still and no seat in this round ever did.**

---

**STATUS TRAIL (2026-08-27): THE PREMISE IS VINDICATED BY THE Q3 CONTROL. §3 STAYS
PROVISIONAL; RATIFICATION WAITS ON THE DEVICE GATE (§13.1).**

The sighted round asked its seats a control question every round — *does either image show a
side face?* — and the answer came back **NEITHER, in every round**. That is the finding, because
the second image was **the asset bar**, running at full commercial quality:

> *"B's north–south wall (x=0–13) is flat 90-gray for all 240 rows with a single 71 seam at
> x=14 and no face anywhere — **a vertical wall in B has literally zero thickness**."* — r5
> *"its entire east flank is one native pixel of joint line, while its north-facing sibling gets
> 24 native px of face"* — r3

**The standard we measure against carries the exact limitation §3 imposes.** Two independent
seats measured it, unasked, never having been shown this section and with no idea they were
auditing a bible clause. **The eight rounds of "no thickness" were therefore not evidence that
two-plane construction fails — they were evidence that Yarl was executing it badly**, against a
bar that has the same missing plane and does not suffer for it.

**What was actually wrong was the value stack, and it was inverted** — see §6.5, which this
round produced. In rounds 4 and 5 two independent blind seats ranked the Yarl candidate **above
the bar** on wall depth, unhedged and with no cull, once the stack was corrected. §13.3's bar is
*"the answer must be Yarl, or a tie"*.

⚠ **QUALIFIED, and the qualification is carried at full strength.** The plant control was mixed:
rejected by 4 of the 5 seats that saw it, named on its own axis only once, and waved through by
one seat — which voided round 2 (§4). **The favourable result is evidence, not a validated
pass, and is weighed at 4-of-5-seat strength exactly as the round marked it.** `REPORT.md` §3a.

**RULED (Rafe, 2026-08-27): the premise stands vindicated; the clause is not yet ratified.**
§13.1 governs: in-scene, on device, by eye. A device build of the rounds-4/5 configuration is
owed and is the next step — the round's own captures are headless at device pixel size and are
**round evidence, not gate evidence** (`REPORT.md` §4a). §3 graduates from PROVISIONAL when that
walk happens and not before.

**STATUS TRAIL (2026-08-26): THE RULED CONDITION FIRED. §3 WAS REOPENED, WITH EVIDENCE — and
what replaces it, if anything, is Rafe's ruling and not the spike's.**

*Kept below in full. The reopening was correct on its evidence; what the sighted round changed
is the diagnosis, not the honesty of the record — and the confound named first at the time,
**"the construction numbers were invented, where the bars' are measurable"**, is exactly the one
that turned out to be carrying the failure.*

The ruling was *"depth arriving ratifies §3; depth failing reopens it with evidence"*, against
two rounds spent on plane-boundary occlusion and wall-top value separation with south-facing
front faces present in scene. Both rounds ran. **Depth did not arrive.** Eight blind critic
rounds have now answered the thickness question and every one of them answered no; the last, on
the arm it ranked FIRST of five, said:

> *"the reveal at the boundary reads as a shadow gap rather than a cap, so you still cannot
> distinguish a wall top from a wall face anywhere in the image."*

**The reopening rests on that arm and not on the two constructions the same round showed to be
mistakes.** Round 8 tried a coping course and a joint-deepening pass; the seat ranked both BELOW
the arm that had neither, and both failures are diagnosed in
`tools/composition_spike/SPIKE.md` §5.7. The arm that ranked first carried only the two ruled
variables at their best measured settings, and it still failed. That is the evidence.

What every seat asked for instead, unprompted and without ever being shown this section, was a
**side face** — *"no cap, no cross-section, and no way to tell the top of a wall from the face of
it"* — which is the one plane §3 forbids.

**Previous status, kept because a status trail that overwrites itself is not a trail:**
**STANDS UNAMENDED, PENDING EVIDENCE (Rafe, 2026-08-26).** The composition spike ran
six blind critic rounds against composed two-plane walls and the thickness question came back
**no on every arm in every round** — *"none of them has a wall with a top and a side, so the
whole set is a flat pattern with a path tinted through it."* Six independent seats asked for a
**side face**, unprompted, none having been shown this section.

Two further rounds are ruled to run on plane-boundary occlusion and wall-top value separation,
in a scene where south-facing front faces are actually present — the review corridor could only
ever show a face on 7.3% of its wall cells, which is what every one of those six verdicts rested
on. **Depth arriving ratifies §3. Depth failing reopens it, with evidence.** The evidence trail
is `tools/composition_spike/SPIKE.md`.

**Why PROVISIONAL:** this has not been tested on a Yarl asset at Yarl's density. The Phase 5
pilot builds floors and walls first, which makes it the natural probe. If two-plane walls in a
portrait orthogonal grid do not deliver the volume we want, we find out in the cheapest tier,
before a single prop or creature exists.

**Isometric *objects* are not forbidden** — the ban is on an isometric map.

**Corroboration:** the asset bar's walls are two-plane — front face plus top band, no side
face — running at full commercial quality. The provisional projection has a shipped precedent
in the very library named as the per-asset bar. Still ratified only by the pilot.

**And the corroboration is now measured rather than asserted.** The sighted round's Q3 control
had two independent seats read the bar's own north–south walls and report **zero face** — *"a
vertical wall in B has literally zero thickness"*. The shipped precedent carries §3's limitation
exactly, which is the status trail's 2026-08-27 entry above.

### 3.1 The top plane is FLAT — RULED (Rafe, 2026-08-27). A top surface is not face material re-toned.

**A plane is not made by changing the value of a texture. It is made by changing what the
texture is a picture of.**

The face is coursed because you are looking at *courses*. The top is not, because you are
looking at *the tops of stones*. Re-toning the same coursed masonry and laying it flat produces
a surface that reads as more elevation, whatever value it is given.

**Two blind seats found this independently, neither shown the other's verdict, and both culled
`wrong-projection` for it:**

> *"A top surface does not show five courses of face-brick."*
> *"the brick coursing above it has the same pitch, proportion and orientation as below, so it
> is not a top surface — **it is more face**."*

**And no value change reaches it.** The round that was culled already had §6.5's value stack
broadly right — one of those same seats measured the bar at *"face at 0.49× the top"* and
proposed §6.5's own ratio back as its flip list — and culled the arm anyway. **A plane textured
like elevation reads as elevation at any value.** This clause therefore sits with the projection
rule it protects, not with the values it is independent of.

**Register derivation — §8.1 wear, the same derivation §6.5 runs on.** Decay is traffic-driven
and **nothing walks on a wall top**. A surface nothing has ever touched has accumulated no
incident, so it carries nothing but the joints between the blocks it is made of. A material
derivation: it declares no light direction and survives §6.3.

**As built:** plane flat at its target value, 2 px joints on a 16 px grid at 0.78 of the plane,
phase-offset per variant. Measured on the bar at 91.5% of top-plane pixels holding one exact
value, joints at half-tile pitch. `tools/sighted_round/WALL-RECIPE.md` §2.3.

⚠ **Worth stating why this matters beyond walls: it is §8.3 in different clothes.** Coursing on
a top plane is *material describing the wrong thing*, repeated in every cell. The tile was not
badly drawn — it was a picture of the wrong surface, thirty times over.

### 3.2 Object projection — RULED (Rafe, on device, 2026-09-12). Cabinet oblique, receding RIGHT, ½ depth.

**Every object in the game stands in space the same way: a square-on front face, the top
visible, and the RIGHT side receding up-right at 45° with the receding run at ½ of true
depth.** Three planes. This is a §1.1.4 one-way door, walked and ruled on the handset across
two rounds (`docs/OBJECT-PROJECTION-RULING.md`), and it is object law, not provisional.

**The geometry, exactly, because a law that says "cabinet" without numbers is a label.** World
axes: x right, d into the scene (north), z up. Screen (y down):

    (x, d, z)  →  ( x + ½·d ,  −(z + ½·d) )

The receding run is **½ of the depth on each screen axis** — the way a pixel artist steps a 45°
edge — so the receding *edge* is 0.71·d long. This is the build Rafe walked (`projOdeep`: k=½,
right). ⚠ Strict technical-drawing cabinet draws the receding *edge* at ½·d (a per-axis run of
0.35·d), a visibly shallower side; the round's brief mislabelled the depths, and Rafe's ruling
used the textbook name for the ½ build he walked. **The law records the walked geometry.** If
the strict figure is ever wanted, that is a rebuild and a re-walk under §13.1, never a relabel.
The authority for the projection is `tools/tier2_props/projection_mesh.py` — each object as a
model, the projection as one function — and every re-authored object is generated from its
projected template, because generation cannot be told a projection (§13.7 platform fact,
2026-09-12: 16/16 Pro candidates returned the model's own view against a reference *and* an
instruction).

**Rejected, and why, in Rafe's words.** *True isometric* — reads wrong on this floor; the
diamond fights the orthogonal grid (§3's own rationale, confirmed at the walk). *Flat front +
top* (the walls' grammar on objects, candidate W) — *"if we're doing dynamic lighting it should
count for something"*: a two-plane object has no surface for the carried lamp to turn, so the
rig's one expressive act — form revealed by a moving light — is wasted on every prop. The
walls can afford two planes because they are mass and run; an object has to be a thing.

**The round exception.** A cylinder has no front face to keep true, and oblique makes its ends
oblong — round one's honest archetype (the barrel, whose lid *"slides sideways and the body
leans"* under oblique) showed the cheat rather than hiding it. So **round and cylindrical objects
keep a true-circle top and a vertical body**: the ellipse of the lid states the camera, and
nothing shears. Barrels, cauldrons, wells, round pillars, tree trunks if there are ever any.

**Characters are unaffected.** Sprites are front-facing (§7.4: heraldic, planted) and stay so;
this clause is for what stands *in* the room, not who walks through it.

**Rationale, recorded so the ruling can be re-derived.** Cabinet is the technical-drawing
convention for exactly this problem — a true front elevation with depth indicated, never a
rotated view — and its ½ is the depth at which a side reads as a side without the front ceasing
to be an elevation. Right is the drafting convention and the pixel-art norm (the receding axis
runs up-right; the light side of a cabinet drawing is the front); no fixed light motivates it
here (§6.3 — the lamp moves), so it is a whole-game constant chosen by convention and it stays
chosen. Consistency is the actual law: one projection per scene was what made round two
judgeable at all, and it is what makes a room read.

**Walls, and what follows.** Continuous wall runs stay two-plane — §3 walls, ratified 2026-09-09,
untouched by this ruling. But where a wall *ends* — a run's terminus, a corner, a free-standing
pillar — it becomes an object in the room's terms and must agree with the objects beside it: a
**right/east face at ½ depth**. **RATIFIED for wall ends and pillars (Rafe, props walk on the
handset, 2026-09-13): *"a wall turning a corner."*** Built as `Tier1EastFace` (#211): a cell with
floor to its east that is not a north–south run draws a parallelogram 16×32 native hanging off the
reveal's right edge, each column sampling one column of the cell's OWN face so the courses recede,
value 0.75 of the face with a 1-px seam at the arris (§12.1 form). **The corridor jamb is the
exception and is REOPENED under #211:** at the same walk it *"reads as a hard diagonal edge, not a
face; the lit wedge in the corridor above it has a straight edge that looks like the occluder's
shadow mask, not a surface"* — a bounded round with a measured bar, not a re-ruling of the face.

**Filed separately, not projection:** props that do not sit against walls — the depth order
under the top band (which of prop and wall draws over which, and where a prop's base sits in
the cell) is placement law, and the round-two walk raised it as such.

**Shadows (cast-shadows round, 2026-09-12).** An object's shadow is cast by the engine from its
FOOTPRINT — the cabinet base parallelogram, or the round exception's circle — never from its
sprite, and never baked (§6.3). Its sprite is exempt from receiving shadows, which is what keeps
the ½-depth side lit under any lamp. See §12.1a's status.

**What re-authors under this clause.** The boundary marker stone, the three-variant barricade
family and the orc fire — B-PROP-001/002/003 — are re-authored from projected templates at the
ruled geometry (the fire is the round exception: ring true-circle, nothing sheared), and the
§12 cold-naming walk resumes on projection-correct objects. Round two's review block
(ids 9850–9884) stays a review block: nothing from the ruling instrument enters the prop set.

---

## 4. Canvas and density — PLACEHOLDER

**No canvas size, tile size, or density ratio in this document is derived. All values below
are the *shape* of the rules, not the rules.**

### 4.1 The density problem is Yarl's alone — LOCKED (as a stated design purpose)

Yarl and Gemfall share a reference device (iPhone SE, 750×1334, portrait) and differ in
**information density**. Gemfall's screen has room to breathe. Yarl's must hold floor, walls,
props, multiple creatures, the player unit, and UI simultaneously.

The transferable consequence is not a number. It is that **the isolation assumption does not
hold.** Gemfall can largely judge an asset on its own merits. Yarl cannot: the real question is
always whether this creature reads against that floor, next to that prop, beside three other
creatures. Every readability rule in this bible must be asked *"does this survive a busy
screen?"* and a rule that only holds in isolation is not a rule.

**Expect Yarl's readability rules to be harsher than Gemfall's** in ways that feel like a
downgrade in isolation: less interior detail, stronger silhouettes, larger value separation,
more environmental restraint so creatures can win the contrast fight. **An asset that would
pass Gemfall's bar can be correctly rejected here for being too good** — too much lovely
detail, competing for attention it is not entitled to.

This agrees with the register rather than fighting it. *Nothing is staged* and *Sasha and
Hollowmark are the warmest thing on screen* are both, read one way, attention-budget rules.

### 4.2 Density is a ratio between layers — PLACEHOLDER

Adopted in shape from Gemfall §2.6: density is legislated as a **relationship between layers**,
never as an absolute per layer. The environment is quieter than the figures. Ratios are
PLACEHOLDER pending the pilot.

**Quiet floors, detail at the edges — corroborated by the bar.** The asset bar's floors are
large flat fields with sparse texture events, and the events sit at walls and corners while
centres stay open. Their reason is readability. Our fiction supplies a better one: **traffic
keeps the centre clear** (§8.2's channel). Bar and register agree about where detail belongs.

### 4.3 Canvas sizes — PLACEHOLDER

Native canvas per layer, and the integer scale factor to logical pixels, are derived at Phase 5
against the reference device. **Integer scaling only; no fractional scaling baked into any
asset; nearest-neighbour filtering; no anti-aliasing.** That much is LOCKED. The numbers are not.

---

## 5. Palette — philosophy LOCKED, values PLACEHOLDER

### 5.1 One global palette, zero-mercy gate — LOCKED

One palette governs the whole game. Every opaque pixel is an exact bible hex or the asset is
rejected outright. Dithering between adjacent ramp steps is permitted. New colours require a
revision of this document.

This is adopted from Gemfall wholesale and for a mechanical reason as much as an aesthetic
one: **style-conditioned generation requires on-palette reference images**, so a locked palette
is what makes the generation recipe work at all.

**SCOPE — RULED (Rafe, 2026-08-28): §5.1 AND §4.3 GOVERN AUTHORED PIXELS. THE LIT CONTINUUM IS BY
DESIGN.**

The clause needed stating because a blind seat measured the gap and read it as a defect:

> *"The art is drawn at 2× (every pixel is a 2×2 block — I confirmed the column-duplication
> pattern directly), but the lighting is a smooth per-screen-pixel ramp… **Soft unquantised
> gradients sitting on top of hard chunky pixels. Mixed resolution.**"*

It is right about the pixels and wrong about the verdict. Measured over the lit ground of a
tier-one floor capture:

| | |
|---|---:|
| 2×2 screen blocks that are a single flat colour | 7.58% |
| **distinct luminance values in the lit ground** | **1,013** |
| rungs in the source family's ladder | **7** |

**The source is exact and the screen is continuous, and both are correct.** The floor family
quantises to seven values, refuses to emit anything off them, and its shipped atlases are verified
exact to the rung. The renderer then multiplies that by a light evaluated per screen pixel. §6.3
holds that assets **receive** light rather than depicting it — and a smoothly-lit chunky sprite is
an asset receiving light.

**THE DIVISION OF LABOUR THAT FOLLOWS, AND IT SETTLES WHAT EVERY INSTRUMENT IS FOR:**

> **Instruments measure SOURCES. Captures measure LEGIBILITY.**

A palette check, a ladder check, an anti-aliasing check — every one of them runs on the authored
asset, where the answer is exact and a violation is a fact. Running any of them on a lit capture
measures the rig and reports the renderer as an art defect. What a capture is for is the other
question entirely, the one no source check can answer: **can a person holding a phone in a dark
room see what this is?**

A consequence worth naming: **do not "fix" the continuum by quantising the light.** Nothing about
the register requires the lamp to land on the art grid, no clause asks for it, and it would be a
renderer change made to satisfy an instrument that was pointed at the wrong artefact.

### 5.2 Shared spine plus reserved region slots — LOCKED (mechanism), PLACEHOLDER (values)

The register requires five distinct regions and a light that withdraws with depth. Five
separate palettes would mean five gates, five reference sets, five chances for the regions to
look like different games, and a player unit that must be legal in all of them regardless.

Instead, keyed on **region** the way Gemfall's gem slots are keyed on **layer**:

- **A shared neutral spine**, common to all twenty-five floors: stone, brass, bone, grime, and
  the dark ramps. This is the cohesion. It is what makes a Yarl asset recognisable as a Yarl
  asset anywhere in the game.
- **A small reserved allocation per region**, legal only on assets belonging to that region.
  The Boundary's fire-warms are illegal in the Weighing; the Weighing's institutional greys are
  illegal in the Boundary. One gate, one flag, five dialects.

**Only the spine and the Boundary's reserved slots are derived at the pilot.** The other four
regions' allocations are PLACEHOLDER.

**Known exposure:** the failure mode of a shared spine is that it flattens the regions into one
another. This is the first thing the pilot should be asked about once a second region exists.

### 5.3 The light arc is an allocation shift, not a palette change — LOCKED (mechanism)

The descent is expressed as **the warm slots' share of canvas shrinking floor by floor** while
cold and neutral share grows. Same spine throughout. This makes the register's central
progression a single measurable number per asset rather than a matter of taste.

Threshold values: PLACEHOLDER. It is not yet known whether a defensible threshold exists; per
Gemfall's Ruling 70, **"no defensible threshold" is a complete calibration result**, and a
refusal is preferred to a number with a story attached.

### 5.4 Warmth is reserved — LOCKED, with one deliberate exception

Warmth belongs to Sasha and Hollowmark. Brass, skin, and firelight against a world that trends
colder and flatter with depth. This serves theme (a living man with the wrong gods in someone
else's filing system), readability (the player unit is never lost on a small screen), and the
difficulty curve (the world visually withdraws its warmth as it gets worse) with one decision.

**The Boundary is the single exception, and it is deliberate.** The Unshriven keep fires. They
are the last people down here who still tend one. The moment the player leaves them behind, the
warmth withdrawal begins in earnest and never reverses. **The Boundary is not a violation of
the rule; it is the rule's opening statement.**

**The warmth reservation is one instance of a general law, confirmed at three scales by the
asset bar: chroma is signal.** Per sprite: two or three material families and **one** saturated
accent doing identity work. Per room: long neutral stretches, then one saturated event that
*is* the room's identity. Per item: hue carries state, not decoration. A saturated pixel should
mean something happened. General richness is forbidden — saturation spent everywhere identifies
nothing.

### 5.5 Reference neutrality is a criterion — RULED (2026-08-26)

§5.1 adopts a locked palette partly because **style-conditioned generation requires on-palette
reference images**. This clause governs *which* images may be that reference, and it is a
measured rule, not a preference.

**Composition propagates with material.** Measured at 12/12 on the wall campaign: every child of
a charactered reference — A-VAB's recessed frame — inherited its *composition*, not only its
material. A reference does not hand down a surface; it hands down whatever it is a picture of.

References therefore divide by job, and the division is load-bearing:

- **Compositionally neutral references are style parents.** ~~C-GAB's crack-through-a-field is
  the shape of one~~ — a material, evenly presented, that is a picture of nothing in particular.
- **Charactered references are prop stock.** One-off assets in waiting — never conditioning
  parents, however good they are as images.

**The seed corpus wants the boring ones.** A reference chosen because it is handsome is a
composition about to be copied twelve times.

> **CRITERION SHARPENED — RULED (Rafe, 2026-08-27, at the gate). Compositionally neutral is not
> enough. The criterion is INCIDENT-FREE: §8.3.**
>
> The example struck through above is why. *Crack-through-a-field* was offered as the very shape
> of a neutral parent — and the gate ruled that same tile a **frame at field scale**. A crack is
> a *composition* of nothing in particular and it is still an **incident**, and §8.3 measures
> what happens to an incident when it is tiled: **repetition converts accident into intent.**
> One crack is an accident; the same crack in every cell is a motif, and the eye reads the
> pattern whatever the crack's quality.
>
> So the division by job survives and its test gets stricter. **A style parent carries material
> and nothing that happened to it.** Incident — cracks, wear, marks, the §8.2.1 channel — is not
> a property of a good parent; it is what the instance system adds later, per §8.3.

**CROSS-CONFIRMED (2026-08-27), by a different instrument on a different campaign.** This
clause's composition finding was measured 12/12 on the *wall* campaign, by tracing children back
to their reference. A blind seat then rediscovered it from the opposite end: shown the *floor*
campaign's lit corridor, never given this bible, never told which tile was which, it culled
A-VAB and named the construction —

> *"Two concentric closed rectangles inset in the middle of every tile, each one width and one
> value the whole way round — **the tile is a framed plaque**, and the moss clumps repeat at the
> same corners to confirm it."*

*A framed plaque* is *a recessed frame* arrived at independently, in a different medium, by an
instrument with no access to the first result. The clause is not resting on one campaign.

**Corpus status — RULED (Rafe, 2026-08-27), superseding the 2026-08-26 paragraph.** The four
§6.4 survivors divide by job, and the division is now assigned rather than pending:

| survivor | role | why |
|---|---|---|
| **C-GAB** | **primary style parent** — retained under screening; see the corpus note below | compositionally neutral ⚠ *"carries no ring, at any value" is **superseded**: RULED a **frame at field scale** (2026-08-27). Retained because references never ship, not because it meets the sharpened §8.3 criterion* |
| **A-HEB** | **secondary style parent** | a joint network, not a keyline; carries no ring |
| **A-VAB** | **prop stock — never a conditioning parent** | charactered. **The ruling holds regardless of surgery:** de-ringing removes a keyline, it does not make a framed plaque neutral, and it is the composition that propagates |
| **B-KAB** | **retired from conditioning. No remediation.** | 24 children conditioned on it came back 22 ringed; the regenerated candidate was culled by the seat and is **not promoted**. Its original stays in the ledger, un-remediated, and nothing conditions on it |

**The 2026-08-26 measurement in this paragraph was wrong and is corrected.** It read *"a uniform
~3px near-black ring, B-KAB at luminance 14 against a median of 130"* — a value framing, from an
instrument that thresholded luminance at 0.30× the tile median. §12.1's own worked example holds
the prohibition is value-agnostic. Measured on geometry instead: **two of the four carry rings,
not one and not four.** A-VAB carries *two* closed 1px loops (76 px and 32 px, each one width
the whole way round) at 0.48× median, which the value threshold could not see; B-KAB carries one
closed 1px loop of 62 px. A-HEB and C-GAB carry mortar joint networks and **never carried a ring
at all**. Instrument, controls and evidence: `tools/floor_remediation/`.

Remediated, provenance-linked versions supersede an original **upon Rafe's re-curation**; the
originals stay in the ledger untouched, because a ledger that edits its own history is not
evidence. **Nothing conditions on an un-remediated survivor**, and after this ruling nothing
conditions on A-VAB or B-KAB in any state.

**CORPUS NOTE — RESOLVED AT THE GATE (Rafe, 2026-08-27): FRAME AT FIELD SCALE.**

> **The dissenting seat was right at the scale that matters.** C-GAB's inset rectangle is a
> **frame**, not a crack through the stone — and what settles it is the field, not the tile.
> Laid nine-up, the rectangle turns its corners and returns inside every cell instead of running
> on into the neighbour. **The instrument could not have decided this and it is not its fault:
> it only ever looks at one 32×32 tile** (`REPORT.md` §6, stated there before this question
> arose), and the property in dispute does not exist at that scale.
>
> **C-GAB retains its conditioning role. References never ship**, and screening holds at the
> measured child rate — 5 of 20 mechanically, 9 of 20 at the seat-adjusted upper bound.
>
> ⚠ **Recorded so tier one does not misread the retention:** the criterion this clause now
> carries is **incident-free** (§8.3), and C-GAB *does not meet it*. It is retained as the best
> available parent under screening, not as one that satisfies the sharpened bar. **When tier one
> authors or selects parents, incident-free is the bar** — and an authored parent can actually
> meet it, which no §6.4 survivor was ever built to do.

The generalisation this ruling produced is law, and it is larger than this tile: **§8.3, the
motif trap.**

*The paragraphs below record what was contested and how it was measured, because a resolved
question should still show its working.*

Three blind seats have now judged the *same bytes* — the identical lit capture, sha256
`6b358533…`, re-derived byte-for-byte at two different commits:

| seat | cull |
|---|---|
| floor-remediation round A | `none` |
| parent-rate round CP | `none` — *"the best surface here by a distance"* |
| **parent-rate round CS** | **`keyline`** |

2–1. The dissenting seat named a four-sided contour *"one value, returning on itself… the dashed
top does not save it"*. **Its geometry was checked and is partly wrong**: the side it calls
closed is dark in 4 of 9 pixels. The instrument measures that same construction at side coverage
**0.791 against its 0.90 requirement** — a nearly-closed rectangle with one broken side, which
is an accurate description of what is there.

**And that is exactly why the instrument cannot settle it.** §12.1's own text holds that gaps do
not excuse a keyline — *"a border with a bite out of one corner, or one drawn as a dashed run of
ticks, is still a keyline"* — while `REPORT.md` §1 records the instrument's KNOWN LIMIT as
precisely this case, and the decision not to lower the threshold *because C-GAB's 0.791 was
taken to be a mortar joint network*. Whether a side present at 4-of-9 reads as a broken keyline
or as an absent one is not a measurement. **Per §13.2 the deadlock routes to the human gate**,
where it sits as one question — *crack through the stone, or frame around the tile?* — and
Rafe's answer settles this note. Building a number to break the tie is the move §13.4 forbids.

**Screening remains the operative guard regardless of how that question lands**, at the measured
child rate rather than at B-KAB's: 5 of 20 mechanically, 9 of 20 at the seat-adjusted upper
bound. Evidence: `tools/floor_remediation/REPORT-PARENT-RATE.md` §4.

**And the ring rate is a property of the reference, not of the surface — MEASURED
(2026-08-27).** Conditioning 20 generations on C-GAB, at levers and prompt held identical to the
B-KAB run's first wave, returned **5 of 20** ringed against B-KAB's **22 of 24** and its **8 of
8** on the matching wave (one-sided exact p = 6.4e-06 and 0.0038). This clause's whole thesis —
that *a reference hands down whatever it is a picture of* — now has a second, quantitative
measurement behind it, from the ring rather than from the composition. `REPORT.md` §4's
*"it will recur on every floor generated from this surface"* is corrected to **"from that
reference"**.

### 5.6 The working ladder — RULED (Rafe, 2026-08-28), PLACEHOLDER scaffold

**This clause governs a scaffold, not §5.1.** The tier-one families quantise onto a *working
ladder* derived from their donors, and that ladder is a stand-in for the global palette §5.1
will eventually name. It is recorded here so the palette-derivation pass inherits the reasoning
along with the numbers, rather than finding an unexplained pair of rungs.

**The ladder is DERIVED, never stored and trusted.** `lum_lo` and `lum_hi` are the donors' 5th and
95th percentiles — the measurement. The ladder is a rule applied to that measurement, and the rule
is allowed to change. Every consumer of a written manifest re-derives (`compose_family.ladder_for`,
`compose_family.rehydrate`); a manifest written under an older rule cannot silently keep it.

**Count: 7 derived rungs, plus 2 below the donors' band. Total 9** — noted for the palette pass,
which should derive that reach rather than extend it afterwards.

**Provenance of the two rungs — earned by `tools/tier1_floors/measure_ladder_reach.py`.** A floor
donor has no reason to contain the darkest value in the room, so the derived band held neither of
the two things that need one:

| | wants | seven rungs gave it |
|---|---:|---|
| the floor's own sheltered joint (§6.5: dark **because enclosed**) | 47.80 | clamped to 75.02 |
| §6.5's wall face at 0.60 × floor | 60.70 | clamped to 75.02 |
| §6.5's wall face at 0.50 × floor | 50.58 | clamped to 75.02 |

The joint clamp is the device gate's second verdict stated as a number: *"all the gaps look
standardized"* was not the wrong darkness but the **same** darkness — every sheltered joint in the
world quantising to one rung with zero spread, because the palette ended there. The wall clamp is
the same rung seen from the other side: a wall face could not be authored at either end of its own
ruled band.

**Two rungs at the derived spacing — 48.56 and 61.79 — land all three.** The joint lever's ceiling
(coverage × travel, the most any joint-confined lever can move a tile's mean) improves 0.0745 →
0.1253 against §13.8's floor of 0.1440. Still short: **the extension does not make the joints a
sufficient path lever**, and was never ruled to. It restores their variation and it makes a wall
face possible.

### 5.7 An anchor must be stable under field size — LAW (Rafe, 2026-08-28)

**A median may not be used as an anchor.** On a quantised surface the median *is* a rung, so it
snaps, and it jumps a whole rung the moment the 50th percentile crosses over. Measured on one
unchanged floor:

| field | median | mean |
|---|---:|---:|
| 12×12 | 113.85 | 107.30 |
| 16×16 | **100.86** | 105.91 |

The median moved 13.0 units — a full rung, 12% — while nothing about the floor changed. The mean
moved 1.3%. A reported anchor that is really a report of the measuring window will send every
ratio derived from it the wrong way, and §6.5's whole stack is ratios.

**Any value another clause takes ratios against must be shown stable across at least two field
sizes, and the statistic must be area-weighted.** For a plane, that means the mean including its
joints: §6.5 separates *planes*, and a median steps straight over the 21.85% of a floor that is
joint — ratios against it would be ratios against a floor with no joints in it, which is not the
floor anyone looks at.

**The tier-one floor's anchor: 101.16** (101.16 at 16×16, 103.00 at 12×12 — stable to 1.8%),
measured on the landed family under §5.6's nine-rung ladder. It replaces 114.50, and the fall is
real: the differential-wear pass deepened the joints, and §5.6's two rungs let the sheltered ones
reach the depth the bond had always authored. Wall work reads **101.16**.

> The 100.86 that opened the wall session was the median artefact above, not a moved floor. Both
> the correction and the law come out of the same measurement.

---

## 6. Light — LOCKED

### 6.1 Dynamic lighting is the direction — LOCKED

Lighting is engine-rendered, not painted in. This is a committed direction, not a preserved
option.

### 6.2 The light arc — LOCKED (as region-identity generator)

| Depth          | Source                                        | Tended by                              | Character                 |
| -------------- | --------------------------------------------- | -------------------------------------- | ------------------------- |
| Boundary       | Carried fire; orc fires                       | Someone who cares whether it stays lit | Warm, moving, unreliable  |
| Middle regions | The world's own — fungal, ambient, sourceless | No one                                 | Cold, even, unattended    |
| The core       | The institution's, for its own purposes       | A process                              | Flat, adequate, permanent |

The arc is the theme in luminance: **you begin as the only thing here that burns, and end
somewhere that was lit before you arrived and will be lit after you are filed.** Institutional
light hides nothing and reveals nothing worth seeing — which is how the Weighing gets genuine
menace with almost no gothic vocabulary.

~~Only the Boundary's values are derived at the pilot. The rest are PLACEHOLDER.~~

> ### ⚠ RE-RATIFIED WITH SHADOWS (Rafe, shadow walk on the handset, 2026-09-13). THIS TABLE IS THE LIVE ONE.
>
> The 2026-09-06 values were walked on a lamp that shone through rock (§12.1a). This is the walk
> taken with the occluder pass live — the void dark by occlusion, the props casting from the
> lamp, the orc fire lit as the second light — at gameplay distance, on the reference device.
> Every light value was **held** and every shadow value **set**:
>
> | knob | 2026-09-06 | **re-ratified with shadows (2026-09-13)** | unit / note |
> |---|---:|---:|---|
> | radius | 6.0 | **6.0** | tiles — held |
> | falloff | 1.00 | **1.00** | held at the identity, a third time |
> | ambient level | 1.50 | **1.50** | held |
> | energy | 1.6 | **1.6** | held |
> | **shadows** | — | **on, occluders `all`** | §12.1a: first surface exempt by mask, everything behind receives, every edge casts |
> | **void ring** | 1 (interim fallback) | **0** | the void is dark by occlusion; the fallback is retired |
> | **softness** | — | **12.0** | `shadow_filter_smooth`, PCF13, on a ladder to 64. *"The raised softness range melts edges (no beams at 12)."* |
> | **darkness** | — | **0.8** | leak `rgb = 0.2 × ambient hue ratios`. *"The ambient-tinted leak reads as cool dark, not grey."* |
> | **fire** | — | **energy 1.6, reach 4.0 tiles, tint `ff8a3c`** | B-PROP-003 emits (#205). **RULED at the props walk (Rafe, 2026-09-13): *"the fire is good."*** Reach and tint were exposed as `fire r` / `fire tint` for that walk and ratified where they stood; PLACEHOLDER stripped. Required by the engine: a fire's `light` block states all three or the scene refuses to build. #204 (props off the value ladder) stays open as-is — warming grey stone toward wood was accepted at the fire; the lamp question is unchanged. |
> | **flicker** | — | **ON** | *"the tended fire is §9.2's exception."* |
>
> **Two-light physics, confirmed on device:** the fire's cast shadow is visible once the
> player's lamp isn't filling it. **BOUNDARY ONLY**, as before; landed in
> `tools/tier0_harness/harness_config.yaml`, the device marker template and the critic's capture
> command, **passed explicitly and required by the engine**. The 2026-09-06 table below is
> **superseded-by-re-ratification** and kept as the record it is.
>
> ### ⚠ RE-RATIFIED (Rafe, 2026-09-06) ON THE CORRECTED SINGLE-ARITHMETIC LAMP — superseded-by-re-ratification 2026-09-13; kept as the record.
>
> Ruling 56's values below were walked against **a floor lit at energy 1.0 while the walls beside
> it ran at 1.6** — `tier1_polish.gdshader` discarded `LIGHT_ENERGY` (#174), so the two planes
> were lit by different arithmetic and the rig was tuned for a readability that did not exist.
> #174 made the lamp one quantity. This is the walk taken on the corrected one, on the reference
> device, at gameplay distance, across the lit radius — the ordering §6.2.1 demands.
>
> | knob | Ruling 56 (2026-08-28) | **re-ratified (2026-09-06)** | unit |
> |---|---:|---:|---|
> | **radius** | 5.0 | **6.0** | TILES. At the RULED 32px tile: a 384px radius, 768px light texture. |
> | **falloff** | 1.00 | **1.00** | EXPONENT on the radial ramp. Held at the identity a second time, on a different lamp — which makes it a repeated decision rather than an inherited one. |
> | **ambient level** | 0.70 | **1.50** | SCALAR on the ambient HUE. `#1a1a22 × 1.50 → rgb(39,39,51) = #272733`, the CanvasModulate actually applied. |
> | ambient hue | `1a1a22` | `1a1a22` | unchanged |
> | light colour | `ffb066` | `ffb066` | unchanged — §6.2's carried-fire warmth |
> | **energy** | 1.6 | **1.6** | **HELD, and this time it was reachable.** The panel had no energy knob until this round; every prior walk left it where it was because it *could not be moved*. This is the first walk in which holding it was a choice. |
>
> **BOUNDARY ONLY**, as before. Landed in `tools/tier0_harness/harness_config.yaml` and in the
> device marker template, **passed explicitly and required by the engine** — a ratified value that
> can be silently defaulted is a ratified value that can silently drift.
>
> ⚠ **AMBIENT WENT UP, AND §6.2.1's THIRD BULLET IS ENGAGED RATHER THAN BREACHED.** That bullet
> asks the pass to preserve §6.2's arc — *you begin as the only thing here that burns* — and the
> 2026-08-28 pass could point at ambient moving **down** (1.0 → 0.70) as evidence it had not
> flooded the Boundary. This one moves it to **1.50**, above even the pre-Ruling-56 value.
> Recorded plainly, because the clause deserves the note: **the arc is a register claim, carried
> eye-side and never instrumented (§13.4), and the gate that owns it is the one that ruled here.**
> A number does not get to overturn a look (§13.2). It is a note, not a finding.
>
> ⚠ **THE RE-DERIVATION RULE HAS FIRED AGAIN, AND IT REACHES WORK DONE HOURS EARLIER.** Radius
> 5.0 → 6.0 and ambient 0.70 → 1.50. **Every delivered figure round 29 re-took on the corrected
> lamp is now stale** — the composed floor-versus-wall table, `L(cap, floor)`, the white-point
> ladder, the delivered-reach profile. They were correct for the old rig and are re-measured on
> this one, **not adjusted**. That is the second time in one day this rule has fired, and it is
> the cost the coupling flag names: art downstream of a rig is art that moves when the rig does.

> **RULED (Rafe, 2026-08-28) — RULING 56. THE BOUNDARY'S RIG IS RATIFIED. PLACEHOLDER CLEARED FOR
> THIS REGION AND FOR NO OTHER.**
>
> Ratified by §6.2.1's readability pass: walked on the reference device (iPhone SE 3rd gen), at
> gameplay distance, across the lit radius, against the tier-one floor family — which is the
> ordering §6.2.1 demands, the rig before the asset.
>
> | knob | value | unit — stated, because a knob position is not a value |
> |---|---:|---|
> | **radius** | **5.0** | **TILES**, not pixels. At the RULED 32px tile: a 320px radius, 640px light texture. Tiles so the number cannot silently hard-code §4.3's still-undecided tile size. |
> | **falloff** | **1.00** | **EXPONENT** on the radial ramp `(1 − smoothstep(0,1,d))^falloff`. 1.00 is the identity — the plain smoothstep. Above 1 tightens the pool, below 1 carries light outward. **Ratified AT the identity, which is a decision and not an absence of one.** |
> | **ambient level** | **0.70** | **SCALAR on the ambient HUE, not a colour.** `#1a1a22 × 0.70 → rgb(18,18,24) = #121218`, which is the CanvasModulate actually applied. Hue held, brightness only — so a readability pass cannot restyle the region. |
> | ambient hue | `1a1a22` | unchanged by the pass |
> | light colour | `ffb066` | unchanged by the pass — §6.2's carried-fire warmth |
> | energy | 1.6 | unchanged by the pass; 0.0 remains the "lighting is live" control |
>
> **BOUNDARY ONLY.** Every other region derives its own at its own gate. Copying these into one
> would be conformance to a neighbouring region's answer, which is the same error §13.3 refuses
> when the neighbour is a commercial bar.
>
> Landed in `tools/tier0_harness/harness_config.yaml` (status `RULED (Boundary only)`) and in the
> device marker template. The two knobs that were previously code defaults — falloff and ambient
> level — are now **passed explicitly and required by the engine**, on the standing discipline
> that no capture is produced by an undeclared rig: *a ratified value that can be silently
> defaulted is a ratified value that can silently drift.*
>
> **Delivered profile, measured once on the ratified rig** (mean luminance by ring, floor-and-wall
> sample, a datum rather than a gate): flat to ~3 tiles, **0.73 at the radius edge**, 0.19 beyond
> it. Legible across the radius and dark outside it, which is what the pass was for.
>
> ⚠ **The scene it was ratified on has NO automated legibility guard.** `ProbeJunctionLuminance`
> refuses to write a capture whose junction is unlit, but it only runs where the geometry has a
> junction — and `tier1_floor_review` reports `junction=NO`, so the guard is skipped entirely. The
> corridor scene is protected and the floor scene is not. **Recorded for floor session two**; not
> built here, because the gate's instruction was to land the values and park.

**⚠ COUPLING FLAG — RULED (Rafe, 2026-08-27): THE ART IS NOW DOWNSTREAM OF THIS PLACEHOLDER.**

The sighted round measured that **the engine compresses the authored value ratio**, and by a
factor it had to solve backwards on Yarl's own rig:

| | authored face ÷ top | delivered, lit | factor |
|---|---:|---:|---:|
| recipe arm, room A north wall | 0.52 | **0.77** | **1.48** |

**The player is the lamp**, and stands south of a north wall — so the face is always one tile
nearer the light than its own top, everywhere, by construction. The engine brightens the face
relative to the top and flattens the separation §6.5 exists to create. A seat measured the
consequence without being told any of it: *"at x=40 that step is 42→28, only 14 points, and at
x=400 it is 56→21 — A's corner therefore exists only where the light happens to land."*

**So authored ratios are derived backwards from delivered targets on the current rig.** To
deliver 0.52 through this rig you author ≈ 0.35. **The bar cannot supply this number and never
could** — its scene is uniformly lit with no run-time light at all. It is the first quantity in
the recipe that had to come from Yarl's own engine.

**The dependency is named, not solved.** Compensating for a measured falloff is a material
decision and declares no light direction, so it survives §6.3. But **a recipe number that
depends on an underived rig value is a number with a fuse in it**: if this table's energy,
radius or ambient move when they are finally derived, the compensation is wrong and the walls
flatten again.

> **RULE: every authored ratio derived against the current rig is RE-DERIVED when §6.2's
> PLACEHOLDER values are ratified.** Whoever ratifies them owns that re-derivation. The honest
> alternatives — derive the rig before freezing the ratio, or give wall tiles a light-response
> clamp in the renderer — are both real and both outside the round that found this.

> ### ⚠ THE COUPLING IS WORSE THAN THIS CLAUSE SAYS, AND THE ASTERISK IS ISSUE #174.
> **RULED (Rafe, 2026-09-05), on round 28's measurement. THE TWO PLANES ARE LIT BY DIFFERENT
> ARITHMETIC, NOT BY DIFFERENT AMOUNTS.**
>
> Everything above describes ONE rig compressing an authored ratio — a factor, solvable
> backwards, which is what "solve the art backwards" assumes. That assumption is false.
> `tier1_polish.gdshader`'s `light()` reads `LIGHT_COLOR` and never `LIGHT_ENERGY`, and every
> floor tile carries a `ShaderMaterial` built from it. **The floor resolves its light through a
> different expression from the wall standing next to it.**
>
> Measured on the first composed room (round 28), nulling `polish_gain`, everything else held:
>
> | band | floor, polish ON | floor, NULLED | wall, ON | wall, NULLED |
> |---|---:|---:|---:|---:|
> | ≤2 tiles | 122.04 | **85.24** | 58.79 | **58.79** |
> | 2–4 | 47.94 | **35.00** | 20.75 | **20.75** |
> | >4 | 11.19 | 11.09 | 4.97 | 4.97 |
>
> **Every wall value is byte-identical and the floor loses 30%. 43% of the cross-plane
> separation at the range §6.5 governs runs through that term.**
>
> **AND IT CANNOT BE DIVIDED OUT.** `LIGHT = diffuse + LIGHT_COLOR.rgb · polish ·
> pow(delivered, polish_exp)`. Restoring `LIGHT_ENERGY` moves the diffuse linearly and the
> specular quadratically, so **no ratio measured under the defect survives a rescale.** The
> re-derivation rule above is therefore not sufficient here: a re-derivation presumes the old
> number was right about a rig that has since moved, and these numbers were never right about
> any rig.
>
> **What this puts in doubt, and it is the whole cross-plane body of work:** §6.5's stack, §3's
> status-trail measurements of the inverted value stack, the wall session's *k_top < 1 at every
> range*, and round 28's own floor-versus-wall table. **Void rather than provisional.** Each is
> re-measured after #174 lands, not adjusted.
>
> **#174 IS THE GATE FOR THE TIER-ONE SURFACE.** The combined build is parked
> composed-and-holding (PR #177) and resumes for the walk once the arithmetic is one and Ruling
> 56 is re-ratified. Evidence: `docs/ROUND-28-COMBINED-BUILD.md` §5.

> #### ⚠ #174 IS FIXED IN CODE, AND THE EXPRESSION THIS CLAUSE PRESCRIBES IS ITSELF WRONG.
> **RECORDED (round 29, 2026-09-05). A CORRECTION OF FACT, NOT A RULING** — no value law moves
> here and Ruling 56's re-ratification is still owed.
>
> `LIGHT = diffuse + LIGHT_COLOR.rgb · polish · pow(delivered, exp)` above, and #174's own exit
> line *"`* LIGHT_ENERGY` restored"*, both read as though `COLOR * LIGHT_COLOR * LIGHT_ENERGY`
> were the fix. **It is not. Written that way it measures a QUADRATIC energy response on a floor
> whose walls are linear** — 3.915 per doubling against the wall's 2.018. `LIGHT_COLOR` is two
> quantities and this clause names only one of them:
>
> | | what it carries | measured |
> |---|---|---|
> | `LIGHT_COLOR.rgb` | the light's TINT alone | constant `(1.002, 0.696, 0.423)` at 2.2, 3.2 and 4.5 tiles alike; zero outside the light's quad |
> | `LIGHT_COLOR.a` | the radial falloff | 0.574 at the cell measured |
> | `LIGHT_ENERGY` | the energy, exactly | 0.400 at energy 0.4, 0.815 at 0.8 |
> | the blend | **the engine multiplies `LIGHT.rgb` by `LIGHT.a`** | a vec4 scaled whole is scaled twice |
>
> **Energy goes on the RGB only.** Eight one-line shader probes, banked once so nobody buys them
> a third time: `tools/tier1_floors/SHADER-SEMANTICS.md`.
>
> **AND THERE WAS A SECOND DEFECT, which this clause does not know about.** The shader's
> `delivered` scalar was `max(LIGHT_COLOR.rgb)` — the tint's saturated red, **1.0 at every lit
> fragment** — so `pow(delivered, 2)` computed 1.0 from the day it was written and **Ruling 70's
> superlinear response has never run.** The polish was a flat `polish × gain` attenuated once:
> linear in delivered light, which is the baked value-lift §8.2.1 bans. Now built from
> `LIGHT_COLOR.a * LIGHT_ENERGY`, with two controls (`docs/ROUND-29-ONE-LAMP.md` §3).
>
> **#174's clipping premise is PARTLY met, and the first report of it here was wrong.** The
> literal *"clips to 255"* is not met: nothing in the delivered frame reaches 255 at the ratified
> rig. But **near-max floor pixels (≥246) went 72 → 1220 across the fix**, all of them inside one
> contiguous patch at the lamp core, and round 29's blind seat located that patch unaided and read
> it as *"the floor loses every joint and slab edge into flat cream."* A first measurement here
> reported 0.07% and called the premise dead — **it had excluded the player cell and its eight
> neighbours, which is exactly where all 1220 sit.** At energy 1.0, the energy the floor was
> actually lit at, the count is **zero**. **Ruling 56 re-opens on both counts**: the standing-case
> value moved 105.74 → 152.34 (×1.44), and the lamp core is now near the ceiling. The seat has
> proposed a white point of ~232 as a starting position for the walk.

> ### ⚠ THE BOUNDARY'S LAMP PINS RED FIRST — BANKED (2026-09-07), and it shapes every highlight lever
>
> `ffb066` has **red = 1.0**. So as delivered light rises, the red channel saturates while the
> others still have headroom, and a stone at the core of the pool loses its texture **in its
> dominant channel** while its hue slides toward the light's own colour. Measured on the round-30
> frame, inside the blown block a blind seat flagged:
>
> | channel | pixels at 255 | max |
> |---|---:|---:|
> | **R** | **11.0%** | 255 |
> | G | 2.6% | 255 |
> | B | **0.0%** | **176** |
>
> **2,044 pixels had red pinned while green was not** — that is the hue shift, not just the flat
> patch. Whole frame: 16,839 red-clipped pixels.
>
> **CONSEQUENCE FOR ANY LEVER THAT TOUCHES THE TOP END:** it is keyed on the **first-clipping
> channel** and applied **hue-preserving** — one scale factor for all three. Compressing channels
> independently fixes the texture and *keeps* the hue shift, which is half the defect left in
> place. The highlight shoulder is built this way (`tier1_polish.gdshader`), and so is anything
> that follows it.
>
> ⚠ **This is a property of the region's light colour, not of the floor.** A region whose lamp is
> not red-dominant will pin a different channel first, and its levers must be keyed on whichever
> that is rather than on red.

#### 6.2.1 TIER-ONE PRECONDITION — the rig is tuned for readability BEFORE any asset is judged through it. RULED (Rafe, 2026-08-27, at the device gate).

> **The §6.2 rig values — radius, falloff, ambient — get a readability-tuning pass before any
> asset is judged through them. The value stack must be legible at GAMEPLAY DISTANCE, not at
> two tiles.**

**This is a precondition, not a task: no tier-one asset round starts until it is done.** The
ordering is the whole of it. §6.2's values are PLACEHOLDER; every wall round so far has judged
art through them anyway, and the coupling flag above shows the art bending itself to fit an
undecided rig. **That is backwards, and the device gate is where it became visible.**

**What the gate saw that no still could.** The sighted round's captures were read at 2× on a
desktop, where a plane separation three tiles from the lamp is plainly there. On the phone, at
the distance the game is actually played and across the radius the light actually reaches, the
same separation is not doing the work — the pool is narrow, the falloff is steep, and §6.5's
stack is legible in a band around the player and gone outside it. **A value law that only holds
within two tiles of the lamp is not a value law, it is a vignette.**

**Why the rig is the thing to move rather than the art.** The art has already been solved
backwards once against these numbers (the coupling flag). Solving it backwards a second time,
harder, to survive a falloff nobody has ratified, would bake an unratified rig deeper into every
asset — and §6.5's ratios would then be carrying a lighting decision instead of a material one,
which is the §6.3 line. **The rig is one table of numbers and the corpus is every asset in the
game. Tune the cheap thing.**

**What the pass owes, at minimum:**

- **Legibility at gameplay distance**, stated as a distance and measured there — not at the
  lamp's centre.
- **The §6.5 stack surviving the falloff** across the lit radius, not only at its middle.
- **The §6.2 arc preserved**: this is a readability tuning, not a licence to flood the Boundary
  with light. *You begin as the only thing here that burns* is register and outranks
  convenience.
- **The ratified values written back here**, which fires the re-derivation rule above.

> **⚠ SUPERSEDED-BY-RE-RATIFICATION 2026-09-06.** Everything in this block describes the pass
> taken on the BROKEN lamp — floor at energy 1.0, walls at 1.6 (#174). Its ratified values
> (radius 5.0, ambient 0.70) are **no longer the rig**; see the re-ratification at the head of
> §6.2. **The block is kept in full, not rewritten**, because a status trail that overwrites
> itself is not a trail — and because what the pass *owed* is unchanged and is re-answered below.
>
> **What the re-ratification answers that this pass could not:** its second bullet — *the §6.5
> stack surviving the falloff across the lit radius* — was recorded ⚠ NOT ANSWERED because the
> scene's walls were programmer-art mocks. The 2026-09-06 walk had real walls, a real floor and
> one arithmetic. **It is still not answered as a §6.5 value law**, because the walk ratified the
> LAMP and §1.2.2a is explicit that a rig walk blesses no picture — but the obstacle is now the
> ruling's scope rather than the scene's contents.
>
> **DONE — RULING 56 (Rafe, 2026-08-28).** The values are in the §6.2 table above. What the pass
> owed, answered:
>
> - *Legibility at gameplay distance, stated as a distance and measured there* — walked on the SE
>   at the distance the game is played, not read off a 2× desktop crop. That was the specific
>   failure this clause was written to prevent.
> - *The §6.5 stack surviving the falloff across the lit radius* — ⚠ **NOT ANSWERED, and it could
>   not be.** §6.5's stack is a relationship between the wall's two planes and the floor, and the
>   scene's walls are programmer-art mocks. **The rig is ratified on floor legibility alone.**
>   Whether the value stack survives this falloff is owed by the first round that puts real walls
>   in the scene, and that round inherits the re-derivation below rather than a settled answer.
> - *The §6.2 arc preserved* — ambient moved 1.0 → 0.70, i.e. **darker**. The pass took light away
>   rather than adding it, so *you begin as the only thing here that burns* is not weakened by it.
> - *The values written back* — above.
>
> **NOMINAL RADIUS IS NOT DELIVERED REACH — RECORDED (Rafe, 2026-08-28), and it is a note on the
> clause rather than a qualification of the ruling.**
>
> The ratified `radius_tiles` is **5.0**. Measured on the ratified rig, floor luminance as a
> fraction of lit floor one tile from the lamp:
>
> | distance from the lamp | ratio |
> |---:|---:|
> | 2.0 tiles | 0.659 |
> | 3.2 | 0.341 / 0.328 |
> | **4.0** | **0.159** |
> | **5.0** | **0.060** |
> | ground declared dark (5–6 tiles) | 0.057 / 0.059 |
>
> **At the nominal radius the floor reads the same as ground the scene declares dark** — 0.060
> against an ambient floor of ~0.058. The lamp's *delivered* reach is about **four** tiles.
>
> **RATIFICATION STANDS, per §13.2.** The rig was ratified on the device, by eye, at gameplay
> distance — and the eye is the final instrument. A luminance ratio has never been calibrated
> against *legible to a person holding a phone in a dark room*, and a number does not get to
> overturn a look. What the measurement establishes is narrower and still useful: **nominal and
> delivered are different quantities, and scene design must use the delivered one.** A subject
> placed at the nominal radius is placed in the dark.
>
> **CONSEQUENCE FOR EVERY REGION AFTER THIS ONE — RULED: future regions ratify by DELIVERED
> REACH.** The Boundary's 5.0 is now a number with a known meaning; a second region ratifying its
> own 5.0 by eye would be adopting the Boundary's *label* without its measurement. Each region
> states the distance at which its light actually carries, measured, alongside whatever radius
> parameter produces it.
>
> ⚠ **The delivered figure is rig-shaped, not universal.** It follows from radius, falloff, energy
> and ambient together — change any of them and it moves. It is recorded here because §6.2 is
> where someone will be standing when they place something at "the edge of the light".
>
> **AND THE RE-DERIVATION RULE HAS FIRED.** The rig moved: radius 5.5 → 5.0, ambient 1.0 → 0.70.
> Every authored ratio derived against the old numbers is now compensating against a rig that no
> longer exists. Concretely: the sighted round's `WALL-RECIPE.md` authored face ÷ top at ≈0.35 to
> deliver 0.52 through the OLD rig. **That compensation is stale.** Those walls were culled at the
> 2026-08-27 gate and are in nothing shipping, so no live asset is invalidated — but the number is
> not to be picked up and reused. The tier-one FLOOR family derives no ratio against the rig at
> all (its values come from measured donor material), so it needs no re-derivation.
>
> ### ⚠ RULING 56 IS RE-OPENED BY #174 — RECORDED (round 29, 2026-09-05). NOT RE-RATIFIED.
>
> Every knob above was walked against **a floor lit at energy 1.0 while the walls beside it ran
> at this table's 1.6**, because `tier1_polish.gdshader` discarded `LIGHT_ENERGY`. The floor's
> delivered value at the standing case has since moved **105.74 → 152.34 (×1.44)**. The rig was
> tuned for readability against behaviour that no longer exists, which is exactly the coupling
> §6.2.1 was written to prevent, running in the other direction.
>
> The ladder is built and waiting: `tools/tier1_floors/capture_rig_ladder.sh`, ten one-knob rungs
> bracketing this table on the corrected lamp, with delivered floor luminance by range and the
> worst cell in each band (`docs/ROUND-29-ONE-LAMP.md` §7). **Those stills gate nothing** — §13.1
> and this clause's own history both say so. The ratification is Rafe's eye, on the reference
> device, at gameplay distance, across the lit radius.
>
> **When new values are ruled they are written back here and into `harness_config.yaml` as
> REQUIRED FLAGS**, and the table above is annotated *superseded-by-re-ratification* with the
> date. Until then this table stands and every capture carries it.

This is the first measured instance of art and rig being coupled on this project. It will not be
the last, and the reason to write it down here rather than in the recipe is that **§6.2 is where
someone will be standing when they break it.**

### 6.3 Assets are authored to RECEIVE light, not to DEPICT it — LOCKED, RATIFIED

**RATIFIED (Rafe, STOP 2, 2026-08-26), on the device, in the lit corridor: the lighting is worth
the effort.** §6.4's probe ran, its kill criterion was declared before any arm and was not
re-tuned after, and the clause survives it. This clause is no longer provisional.

⚠ **What was ratified, stated narrowly because the probe's own positive control failed.**
Stage 1 produced **no arm A** — no candidate in any arm depicted a directional key light, so the
three arms never separated on the lighting axis. **This ratifies the treatment under light. It
is not a victory over a baked arm, because no baked arm existed to beat.** §6.4's Ruling 47
clause governs that outcome and is not overridden here: A and B being indistinguishable is a
finding about test conditions, never permission to pick either. The comparison §6.4 set out to
run remains unrun, and nothing in this ratification should be cited as having run it.

**RULED (Rafe, 2026-08-26): AUTHORED OCCLUSION IS LAW. Receive-light never meant form-free.**

The clause has been read once too often as *draw nothing that could be mistaken for light*, and
that reading is wrong and is now closed. What §6.3 forbids is a DIRECTION: a highlight, a
gradient, a bevel, a bake that says *the light is over there* and that a torch arriving from
somewhere else contradicts. What §6.3 has never forbidden, and now positively requires, is
**form** — the occlusion a shape casts on itself and on the plane it meets, which is identical
under every azimuth and therefore contradicts nothing.

Self-occlusion, contact occlusion under a lip, and **plane-boundary occlusion (§12.1)** are all
form. An asset that omits them is not obeying this clause more strictly; it is under-drawn, and
it will read flat under any light the engine supplies.

**OCCLUSION, NOT ILLUMINATION — the vocabulary is the rule.** Geometry is drawn with
**occlusion** — dark where light cannot reach: crevices, under-edges, recesses, the shadow under
a strap's lip — and never with **illumination** — bright where light would strike: top chamfers,
lit crowns, directional ramps. Same information, opposite vocabulary. **A plane separates from
its neighbour by the dark seam where they meet, not by a highlight along its edge.**

**The vocabulary collision, recorded because it manufactured violations twice.** At 32px,
*"describe geometry with value"* and *"bake a key light"* share a vocabulary. The wall gauntlet's
own critic asked for a 1px chamfer top-and-bottom and received the gauntlet's only key-light
culls; the tiles-pro audit reproduced the same failure through a depth parameter instead of a
prompt. Two surfaces, two mechanisms — **a property of the scale, not of any tool.** Asking any
generator or any author for "depth" without specifying the occlusion vocabulary will manufacture
directional light. **Every future critic kit and prompt file carries this distinction
explicitly.**

**The instrument that enforces it — PROMOTED (§13.5), one axis.** The differencing check
(composition spike, round 8): **authored form must persist identically with the engine light
switched off.** A per-block top-bright/bottom-dark emboss that encodes a light direction fails
the diff. Its demonstrated fail is on the record — it is the method that culled the round-8
plant, quoted verbatim below — so its passes count, on its one axis. **It measures light
direction and nothing else; the eye still rules the rest** (§13.4). Audited in §15.

⚠ **CAVEAT TRAIL — the baked arm exists now, and it outranked every receive-light arm built
against it.** Recorded verbatim from the composition spike's report
(`tools/composition_spike/SPIKE.md` §5.1b, 2026-08-26) because it is adverse evidence on the
axis this clause's ratification could not test, and a caveat trail that summarises adverse
evidence in its own words is not a caveat trail. **§6.3 stands.** This is the record, not a
reopening.

*(Numbering per `tools/composition_spike/SPIKE.md` §5.1b — there is no bible §5.1b.)*

#### 5.1b THE PLANT OUTRANKED THE ARMS — and §6.4 said this comparison had never been run

This was not designed and it is the most consequential thing in the report.

The plant is boundB **plus** a baked per-course key light: identical stones, identical rig,
identical geometry, one forbidden construction added. Six independent blind seats ranked the
five captures best-to-worst:

| round | ranking, best → worst | plant | plant cull |
|---|---|---|---|
| 1 | **plant** > boundB > ctrlB > boundA > ctrlA | **1 of 5** | none |
| 2 | boundB > ctrlB > **plant** > boundA > ctrlA | 3 of 5 | none |
| 3 | boundB > boundA > **plant** > ctrlB > ctrlA | 3 of 5 | key-light |
| 4 | ctrlB > boundB > **plant** > boundA > ctrlA | 3 of 5 | key-light |
| 5 | **plant** > boundB > ctrlB > boundA > ctrlA | **1 of 5** | none |
| 6 | **plant** > boundB > ctrlB > boundA > ctrlA | **1 of 5** | none |

**First in three rounds of six. Never below third of five. Not once last.**

Round 6 named the mechanism: *"a depth cue applied on one axis only is worse than no depth cue
— it asserts a viewing direction the rest of the frame contradicts."* It read as depth. In six
rounds it is **the only thing that produced any depth read at all**, in a set where the
thickness question was otherwise answered no every single time.

**Why this matters more than a ranking usually would.** §6.3 is RATIFIED, and §6.4 states its
own limit in as many words:

> *"Stage 1 produced no arm A — no candidate in any arm depicted a directional key light, so the
> three arms never separated on the lighting axis. This ratifies the treatment under light. It
> is not a victory over a baked arm, because no baked arm existed to beat ... The comparison
> §6.4 set out to run remains unrun, and nothing in this ratification should be cited as having
> run it."*

**A baked arm now exists.** This session built one because LOOP-PROCESS §4 requires a plant, and
it is in one respect a *cleaner* comparison than §6.4's design: the plant and boundB are the
same stones under the same rig, differing by exactly the baked light, so it is a within-arm A/B
rather than three separately-generated arms.

**What this is NOT.** It is not a ruling and not a candidate for one:

- **The plant never passed.** It failed in all six rounds and was culled `key-light` in two,
  with a method — *"still present in the far-left corner at 6× exposure where the engine pool
  never reaches"* — and a measurement, *"+8.7/−6.2 top/bottom split against +2.3/−1.0 for every
  other image in the set."* The clause's own instrument works.
- **A ranking is not the gate.** §13.1 gives the verdict to Rafe on the device, and a seat
  preferring a still image is exactly the "wrong instrument in the wrong context" §6.3 warns
  about — except that these are lit in-scene captures, which is the *right* context, which is
  why it is being reported rather than dismissed.
- **The arms it beat are mocks.** A better-composed wall might beat it. Six rounds did not
  produce one.
- **It was built to be caught**, which biases it toward being conspicuous, not toward being
  liked.

**The honest statement: on the depth axis, in the lit scene, at the ruled canvas, the forbidden
construction outperformed every receive-light arm this session could build, six times out of
six — while remaining correctly detectable as forbidden.** §6.4's unrun comparison has run
incidentally and its first result is adverse. That is a finding for the record and a ruling for
Rafe; it is not this session's to resolve, and §6.3 stands untouched by it.

**What that record does and does not change.** It does not reopen §6.3: the plant never passed,
was culled `key-light` in two rounds by a seat that had demonstrated it could fail, and a
ranking is not §13.1's gate. It does close the sentence above that said the comparison "remains
unrun" — it has now been run once, incidentally, on a within-arm A/B, and its first result is
adverse on the depth axis. Anyone citing this clause should cite that too.

**THE REMATCH RAN, AND IT WENT THE CLAUSE'S WAY.** The two ruled rounds put authored occlusion
against the baked plant on identical stone, with plane-boundary occlusion deepened to the
requirement above and the wall-top albedo separated from the floor's. Across eight rounds the
plant's ranking among five captures went:

| round | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|
| plant's place | **1st** | 3rd | 3rd | 3rd | **1st** | **1st** | **1st** | **4th** |

**Round 8 is the first round in which legal form beat the baked arm**, and it beat it with
exactly the construction this ruling made law. The plant was also culled `key-light` that round
by a method stronger than any before it — *"differencing against C1 isolates a per-block
top-bright/bottom-dark emboss that would survive the engine light being switched off"* — so it
lost on the ranking and on the cull in the same round.

**Stated at its true strength and no higher.** One round is one round; the plant led the six
that preceded it; the arms are mocks; and a ranking is not §13.1's gate. What the record now
supports is narrower and more useful than a verdict: **the baked arm's advantage was an
advantage over receive-light assets drawn WITHOUT form, and it disappears once they are drawn
with it.** That is the strongest available reading of §5.1b and it is consistent with every
round in the trail.

**This is a one-way door and the most consequential construction rule in this document.**

Sprites are drawn with material and form under relatively even illumination. Highlights are not
baked in. Light arrives from the engine.

Baked highlights fix a single light direction into every asset; add a torch on the right and
everything is still lit from the upper left. The conflict is invisible until the renderer is
switched on, and the only fix is redrawing everything. Gemfall hit the same shape when its
outline rule moved from baked art to the engine (their §4, v1.1).

**Consequence that must be stated or it will cause false rejections:** receive-light assets
look flat and slightly disappointing on a contact sheet. **They come alive only in the lit
scene.** A critic — or a human at a gate — who rejects a receive-light asset for looking
underlit in isolation is applying the wrong instrument in the wrong context. This sharpens
§13.1 from a discipline into a technical necessity.

**Baked drop shadows are depicted lighting and are forbidden with the rest of it.** The asset
bar ships every item sheet twice — with and without painted shadows — because a baked shadow is
wrong in every context except the one it was painted for. That manual workaround is this clause
solved structurally: **the asset never grounds itself; the engine or a composited blob grounds
it per context.** One asset, every context, and nothing fights the probe.

### 6.4 The receive-light probe — RUN AND CLOSED, §6.3 RATIFIED

**Status: closed 2026-08-26.** The probe below is preserved as authored, because §13.6 and
LOOP-PROCESS §8 both turn on the bar having been declared before the answer was visible, and a
criterion rewritten after the fact cannot demonstrate that. What follows is the probe as it was
declared; the outcome is recorded at the end of this section.

§6.3 was a committed direction, **not an unconditional one.** It was the clause in this document
with the largest unmeasured effort cost, and it was to be struck if the probe below said so.

**The named risk, and it is the previous track's shape wearing new clothes.** Generation models
are trained on art that *depicts* light — baked highlights, a key direction, painted
specularity. Asking a generator for evenly-illuminated material-only sprites may be asking it
to do the thing it least wants to do. If so, receive-light becomes the new external-corpus trap
(§1.3): every asset a fight against the tool, for a payoff nobody can see.

**The distinction that probably decides it:** receive-light does **not** necessarily mean flat.
It can mean *no directional key light* while retaining form shading, self-occlusion, and
material value differences — which is far closer to what a generator naturally produces and is
still fully compatible with dynamic light.

**The probe.** One wall-and-floor fragment, three arms:

| Arm   | Authoring                                                    | Role                                             |
| ----- | ------------------------------------------------------------ | ------------------------------------------------ |
| **A** | Baked directional key light                                  | **Positive control.** The conventional approach. |
| **B** | No key light; form shading, occlusion, material value retained | The likely answer.                               |
| **C** | Flat — material value only                                   | The strict reading of §6.3.                      |

Lit in Godot, on the reference device, in a corridor. Four questions:

1. Do B and C look good **lit**?
2. Does the device hold frame rate with occluders present?
3. **How much harder was it to generate B and C than A?** ← the question that decides it.
4. Does the light deliver the §6.2 region arc, or is it merely darkness with a lamp in it?

**Positive control clause (Ruling 47).** Arm A exists so the instrument can be shown to
discriminate. **If A and B cannot be told apart in the lit scene, that is a finding about the
test conditions — not permission to pick either.**

**Kill criterion — declared now, before any result is visible (Ruling: nothing is cut to fit).**
If B and C cost materially more generation effort than A for a payoff not visible on the
device, **§6.3 is RETIRED, §6.1 falls back to baked lighting, and the evidence is recorded in
place.** The bar is not re-tuned after the answer is seen.

**This probe runs before the Phase 5 pilot.** It is the cheapest point at which the answer is
still free: §6.3 is a one-way door, and every asset drawn before it is settled is drawn twice
if it is wrong.

#### The outcome — RULED (Rafe, STOP 2, on the device, 2026-08-26)

**§6.3 RATIFIED.** The lighting is worth the effort. The kill criterion above was not met and
was not re-tuned.

**And the probe's own positive control failed, which narrows what that ratification says.**
Stage 1 ran 120 unconditioned generations across all three arms. **No arm produced a single
candidate depicting a directional key light — arm A included.** The blind census that measured
this passed its own control 10/10 against constructed plants, so the reading counts, with one
limit stated: the plants carry a *hard* key light, so what is licensed is *"no arm produced
directional lighting at plant strength"*, not *"none produced any"*.

Consequences, recorded rather than smoothed over:

- **The three-arm comparison never ran.** There was no arm A to lose to. Ruling 47's clause
  applies exactly as written — a finding about test conditions, not permission to pick an arm —
  and it is not overridden by the ratification.
- **What Stage 3 showed is real and is what was ruled on:** receive-light assets in the lit
  corridor, on the reference device, at 32×32 native at ×2, each against its own unlit
  companion under an otherwise identical rig. §6.3's central claim — that these assets *"look
  flat and slightly disappointing on a contact sheet"* and *"come alive only in the lit scene"*
  — is what the device answered, and it answered it yes.
- **The effort half of the kill criterion never acquired a denominator.** Effort was to be
  measured as generations-per-accepted-reference per arm, against arm A's. With the arms
  undifferentiated, the ratio measures subject difficulty, not lighting treatment. It was not
  computed, and no number should be cited as though it had been.

**Retirement triggers.** This ratification rests on the lit scene, not on a comparison. If a
baked-key-light arm is ever actually produced and beats receive-light on the device, or if the
effort ratio is ever measured with a real arm A and lands clearly beyond the abstention band,
§6.4 reopens. Absent that, §6.3 is settled and the door is shut.

**Evidence.** `tools/pixellab/probe_6_4/` — `AUDIT-FINDINGS.md` (surface freeze),
`STOP1-REPORT.md`, `STOP2-REPORT.md`, `PARK-STATE.md`, and the image ledgers. Every generation
is on disk with its full request payload, because nothing on this platform is seed-reproducible
and a parameter row is therefore not evidence. **Probe total: 174 generations.**

---

### 6.5 The value stack — RULED (Rafe, 2026-08-27), RE-SCOPED (2026-08-29), **ROW 1 RETIRED AS A DELIVERED TARGET (Rafe, 2026-09-07)**

> ## ⚠ ROW 1 IS RETIRED AS A DELIVERED TARGET — RULED (Rafe, 2026-09-07)
>
> *"Wall top ≈ 1.11 × floor — lighter than the floor"* is **withdrawn as something the engine is
> asked to deliver.** It is not softened, re-scoped or deferred: it is no longer a target.
>
> **The evidence, and it is three rounds deep and taken on ONE arithmetic:**
>
> | round | `L(cap, floor)`, standing case | direction |
> |---|---:|---|
> | 28 (across the #174 defect) | 50.22 levels | cap **below** floor |
> | 29 (corrected lamp, old rig) | 72.56 | cap **below** floor |
> | 30 (re-ratified rig) | **89.64** | cap **below** floor |
>
> **Every correction moved it further from row 1, never toward it.** The floor gains more than the
> cap does at every range, because the floor is nearer the carried lamp by construction — and a
> blind seat has asked for the separation three rounds running while the number was going the
> other way.
>
> **And the 1.11 was never a delivered number.** It came from the asset bar's own screenshot,
> whose scene has **no run-time light in it at all** — §6.5's own re-scoping said so in 2026-08-29
> and the consequence was not drawn. In a lightless frame a wall top brighter than the floor costs
> nothing. Under a carried lamp the floor a wall faces is always nearer the light than the wall's
> own top — by one tile plus the half-tile the top band sits back inside its own cell — so
> **k_top cannot reach 1.0 at any range**, which the flat-albedo probe measures as
> 0.87 / 0.67 / 0.48 / 0.30. A target that requires k_top > 1 is a target the rig cannot express.
>
> ### What the LAW keeps, and it is most of it
>
> 1. **The 2:1 separation between the wall's own two planes.** A local relationship, it survives
>    the falloff, and it was the whole finding of the wall campaign — Yarl's planes were
>    **inverted**, and that correction stands untouched.
> 2. **Cap-versus-floor distinctness — BY MATERIAL, TEXTURE AND BOUNDARY OCCLUSION, NEVER BY
>    BRIGHTNESS.** The cap must read as a different *thing* from the floor: a different grain, a
>    different course, a hard boundary where the plane turns. What it must no longer do is read as
>    a different thing *because it is lighter*, because under a carried lamp it cannot be.
> 3. **The register derivation is untouched** — the top catches light, the face is enclosed. That
>    is why the stack exists and it was never a measurement.
>
> ### Why this is a retirement and not a failure
>
> The clause was written from a lightless reference and asked the engine for something a carried
> lamp forbids. **Chasing it further would have meant authoring the cap brighter and brighter
> against a floor that keeps outrunning it** — solving the art backwards against physics, which is
> the §6.3 line and the failure the coupling flag was raised to prevent. Retiring the row is what
> the measurement has been saying for three rounds.
>
> ⚠ **Consequence for the seats:** *"give the wall top a value separation from the lit floor"*
> is now a **REFUSED** request wherever it asks for brightness, and a **MAKE-IT-READ** item
> wherever it asks for distinctness — §13.4.1's shape exactly. The successor is material, not
> value.

### 6.5 The value stack — RULED (Rafe, 2026-08-27), RE-SCOPED (Rafe, 2026-08-29, at the wall gate). ⚠ EVERY NUMBER IN THIS CLAUSE IS UNDER #174 (2026-09-05).

> ⚠ **READ §6.2's #174 ASTERISK BEFORE ANY NUMBER BELOW. RULED (Rafe, 2026-09-05).**
>
> This clause is a set of ratios between the floor and the wall's two planes, and **the floor and
> the wall are lit by different arithmetic**: `tier1_polish.gdshader` resolves the floor's light
> through `LIGHT_COLOR` where everything else takes `LIGHT_ENERGY`, and **43% of the cross-plane
> separation at the standing case runs through that term.** The specular squares its input, so no
> ratio taken under it survives a rescale.
>
> **Every measured figure in this clause is VOID rather than provisional, and is re-measured
> after #174 lands — not adjusted.** That includes the re-scoping's own range profile
> (0.87 / 0.67 / 0.48 / 0.30), the *k_top cannot reach 1.0* argument, and the 2:1 plane
> separation. **The LAW is untouched** — the floor sits between the planes, the top catches
> light, the face is enclosed — because it is a register derivation and not a measurement.
> What is void is every number offered in evidence for it.
>
> #174 is the gate for the tier-one surface. `docs/ROUND-28-COMBINED-BUILD.md` §5.

> ⚠ **THE BANNER IS CHALLENGED ON EVIDENCE AND IS NOT LIFTED HERE. Awaiting Rafe (round 29,
> 2026-09-05).**
>
> The banner voids *"the re-scoping's own range profile (0.87 / 0.67 / 0.48 / 0.30), the k_top
> cannot reach 1.0 argument, and the 2:1 plane separation."* **Those three were never measured
> across the defect.** `range_profile.py` runs on `wall_range_a/b.json` through
> `tile_themes_probe.yaml` — flat-albedo photometric probes captured with **no `--ashlar-floor`
> and no `--floor-overlays`**, so their floor is a plain theme sprite carrying **no
> `ShaderMaterial` at all** and it ran the default light pass, with the energy, the whole time.
>
> Re-captured on the corrected lamp: **all six probe PNGs are byte-identical to the committed
> pre-fix evidence.** Only the worktree path and the timestamps moved in the logs. Recomputed
> k_top: **0.8686 / 0.6696 / 0.4799 / 0.2995**. And the engine states it independently in every
> one of those logs — `[Tier1] ashlar floor: none declared` — which is §13.10's standard: the
> engine's own answer, not an instrument's inference.
>
> **PROPOSED, not applied:** the banner narrows to figures measured on the COMPOSED scene with
> the real ashlar floor — round 28 §5's table, and PR #151's status-trail measurements if those
> used it. Round 28 §5's table IS re-taken, on one lamp, in `docs/ROUND-29-ONE-LAMP.md` §6.
>
> ⚠ **AND ONE FIGURE MOVED IN THE DIRECTION THIS CLAUSE CARES ABOUT.** On the corrected lamp the
> floor gained 1.44× and the cap gained nothing, so `L(cap, floor)` at the standing case went
> **50.22 → 72.56 levels, sign negative — the cap further BELOW the floor.** Row 1 asks for the
> wall top to be *lighter* than the floor. Whether row 1 is reachable at the standing case on
> the corrected lamp, or whether the re-scoping's *dark-by-design* now extends inward, **is a
> value law and it is Rafe's at the gate.**

> **RE-SCOPED — RULED (Rafe, 2026-08-29, at the first gate with real walls in the scene).
> THE STACK IS A STANDING-DISTANCE LAW.**
>
> | | as it now stands |
> |---|---|
> | **where it holds** | **two tiles and in.** k_top ≈ **0.67** at the standing case. |
> | **what it requires there** | the **2:1 plane separation**, which is delivered (mean ≈0.55 against 0.53). |
> | **beyond three tiles** | the planes invert relative to the floor. **This is physics, and it is recorded as dark-by-design.** |
> | **what is forbidden** | **no authored chase past the ladder.** |
>
> **The 1.11× came from a lightless screenshot.** The asset bar's scene is uniformly lit with no
> run-time light in it at all, so a wall top brighter than the floor costs it nothing. Under a
> carried lamp the floor a wall faces is always nearer the light than the wall's own top — by one
> tile plus the half-tile the top band sits back inside its own cell — so k_top cannot reach 1.0
> at any range and the number was never reachable here. Measured across one to four tiles:
> **0.87 / 0.67 / 0.48 / 0.30**. Delivering 1.11× would need an authored 129 / 168 / 234 / 375
> against a ladder that stops at 154.38.
>
> **What is NOT withdrawn.** The 2:1 separation between the wall's own two planes is a local
> relationship, it survives the falloff, and it is what the whole wall campaign was actually
> short of — §6.5's original finding, that Yarl's planes were INVERTED, stands untouched.
>
> **The clause no longer licenses solving the art backwards to chase the first row.** The
> compensation the §6.2 coupling flag introduced is spent: what remains is a material law at the
> distance a person stands to look at a wall.
>
> Evidence: `tools/tier1_walls/STACK-FINDING.md`, `evidence/RANGE-PROFILE.json`,
> `LIGHT-FIELD-CONTROLS.json`. Instrument: a flat-albedo probe, four positive controls, the
> pipeline shown exactly multiplicative in albedo at 0.5000 with a worst-cell error of 0.0006.

**The original clause, kept in full because a status trail that overwrites itself is not a trail:**

> **The floor sits BETWEEN the wall's two planes.**
>
> | | target, floor-relative |
> |---|---|
> | **wall top** | **≈ 1.11 × floor** — lighter than the floor |
> | **floor** | 1.00 |
> | **wall face** | **≈ 0.5–0.6 × floor** — darker than the floor |
>
> **Each plane is separated from the floor in a different direction.** That is the law; the
> exact figures are targets carrying §5's PLACEHOLDER status, not constants.

**Register derivation — §6.3, occlusion expressed as a ratio.** The top catches light; the face
is where light cannot easily reach. A horizontal plane under a top-down ambient is open to it; a
vertical plane faces the wall opposite rather than the ceiling, and is the largest recess in the
scene. §6.4 arm B's own words — *"joints, recesses and undercuts sit darker because they are
enclosed"* — applied at the scale of a whole plane. **Enclosure is direction-free by
construction**: dark from every angle, so a torch arriving from anywhere does not contradict it.
This is why the stack is material and not depiction, and why it survives §6.3.

The floor's own position follows from §8.1 and is the half that is easy to miss: *"grime walked
into a surface until it is part of it."* **The floor is dark because it is used. The wall top is
light because nothing has ever touched it.** Two independent derivations — enclosure below,
traffic above — meeting on the same floor value from opposite sides.

**WHY IT IS LOAD-BEARING, and this is the finding of the whole wall campaign.** Yarl's walls had
the relationship **inverted**:

| | top | floor | face | face ÷ top |
|---|---:|---:|---:|---:|
| the bar | **1.11** | 1.00 | **0.59** | **0.53** |
| Yarl, composition spike `before` arm | **0.49** | 1.00 | **0.65** | **1.30** |

Both planes below the floor, 0.16 apart, with **the face brighter than the top**. Not mistuned —
inverted. **Eight blind rounds of "this wall has no thickness" were reading a plane relationship
that the values contradicted, and no side face is required to explain any of it.** Corrected,
two independent seats ranked the Yarl candidate above the bar on depth.

⚠ **The methodological lesson, banked because it will recur.** The spike swept wall-top albedo
at 0.62 and 0.76 of floor, found 0.62 better, and reasoned toward *darker*. Both samples sat on
the **same side** of the floor's value; the answer was on the other side, above 1.0. **A
two-point sweep entirely on one side of the true value points confidently in the wrong direction
and looks like clean evidence while doing it.** Bracket the target or say you have not.

**AND THE ENGINE COMPRESSES IT — COUPLING FLAG, see §6.2.** The authored ratio is not the
delivered ratio. Authored 0.52 arrives as 0.77 under the carried light, a compression factor of
1.48, because the player *is* the lamp and stands south of a north wall — so the face is always
one tile nearer the light than its own top. **Authored ratios are therefore derived backwards
from delivered targets on the current rig.** That dependency is named in §6.2 and is not solved.

**Evidence:** `tools/sighted_round/WALL-RECIPE.md` §0–§1 (measurement and register derivation
per number, under §13.3's origination rule), `bar_measurements.json`, and the seat transcripts.

> **THE FLOOR'S 1.00 IS NOW A MEASURED CONSTANT, NOT A PLACEHOLDER — RECORDED (floor session two,
> 2026-08-28).**
>
> §6.5 states its whole table *floor-relative*, and until now there was no floor to be relative
> to: the wall recipe had to invent one, which is how it came to author ratios against a rig and
> a floor that both moved underneath it. The edge-matched Boundary family fixes the reference.
>
> | | value |
> |---|---:|
> | **median luminance, as authored, unlit** | **114.5** |
> | mean | 113.3 |
> | per-tile mean spread across the 81 tiles | 3.3 |
> | p5 / p95 | 74.9 / 127.8 |
>
> **114.5 is §6.5's 1.00 for the Boundary.** Walls derive against it; it does not drift after
> landing. Measured on the tiles themselves rather than on a capture, deliberately — a lit
> measurement would fold the rig into the constant, and §6.2's re-derivation rule exists precisely
> because a number with a rig baked into it is a number with a fuse in it.
>
> ⚠ **`WALL-RECIPE.md`'s face ÷ top ≈ 0.35 is STALE and is not to be reused.** It was authored to
> deliver 0.52 through the pre-Ruling-56 rig. Those walls were culled at the 2026-08-27 gate, so
> nothing shipping depends on it — but the next wall round derives against the floor above and the
> rig in §6.2, from scratch.
>
> ⚠ **The per-tile spread of 3.3 is itself load-bearing.** Session one measured a 6.4-point spread
> between variants and a blind seat read it as *"the grid draws itself onto the ground"* — the
> cell's own average brightness sitting at a constant position is §8.3.1 with no feature in it at
> all. Every tile in this family is normalised to the family's value for that reason, and the
> spread is reported so the next family can be held to it.

---

## 7. Construction grammar — everything is held — LOCKED

### 7.1 The rule

**Nothing made in the Paths is monolithic or self-supporting.** Every made object visibly
shows what holds it together: strapped, banded, wired, pinned, mortared, tied, clamped, sealed.
Doors have hardware. Stones have cramps and ties. Beams are bracketed. Bundles are corded.

**And everything is tagged.** The institution has inventoried its world. Things wear their
paperwork.

**Failure test, and it is checkable by eye without taste entering into it:** *show me what holds
this together.* An asset that floats, or is carved from one seamless piece, or has no visible
fastening, fails — and the failure has a name and a fix rather than being "feels off."

### 7.2 Why this rule and not another

The fiction's central verb is *bound*. Marya bound into brass. The orcs bound to a line they
cannot advance. Souls bound in a queue awaiting audit. A ward re-anchored to something it
should no longer be attached to. Sasha bound by a debt. The Under-Warden bound by his charter,
which is why he cannot be reasoned with.

Three things this buys at once:

1. **A shared skeleton across five regions** while surface treatment varies. Same grammar, five
   dialects. This is the machinery region identity needs, and it is exactly what the previous
   track lacked.
2. **Readability at 1×.** Bands, straps, and tags are high-contrast linear elements — they are
   what still reads on an iPhone SE after interior detail has dissolved. A rule that serves
   theme and readability simultaneously is rare; take it.
3. **Hollowmark rhymes with the world instead of being an exception.** A woman bound into brass
   is the same operation the world performs on stone, doors, and orcs, applied to a person. The
   player absorbs the logic through hundreds of props before it lands on her.

### 7.3 Binding authority is a region signal — LOCKED (Boundary), PLACEHOLDER (others)

**Who did the binding, and how long ago, differs by region and is legible.**

- **Orc-made (the Boundary): redundant and visible.** Lashed twice. Over-built. Repaired on top
  of prior repairs, because four hundred years of holding a line means everything has been fixed
  a dozen times. Rope, driven pin, hide, timber, salvage. Work done by hand by people who need
  it to hold and do not care how it looks.
- **Institution-made (the core): minimal and correct.** One seal, one tag, done properly, never
  touched again.

The player feels the difference without being told: **the frontier is held together by people
who care whether it holds, and the core is held together by a process that does not care at
all.**

**RULED (Rafe, 2026-08-24): competent but tough. No interest in aesthetics — strength only.**

The Unshriven's work is not desperate and not decorative. It is the work of people who have
been doing this for four hundred years and are extremely good at it, who have never once cared
how it looks. Heavy, over-engineered, correct. Joints that would hold under more load than they
will ever see. Repairs laid over repairs because replacing was never worth it, each one as
sound as the last.

**Nothing on an orc-made object exists for appearance.** No carving that is not structural, no
ornament, no finish. If it is there, it is holding something. This is the sharpest available
contrast with institution-made work, which is also unornamented but for the opposite reason:
the orcs strip it because strength is all they want, and the institution strips it because
nobody cared enough to add any.

### 7.4 Known seams in the rule — flagged, to be tested at Phase 5

1. **Raw geology.** A cave wall is not bound by anything. The rule applies to **made** things;
   the boundary between made and found becomes a region signal in its own right. The Boundary is
   mostly found stone with orc work pinned into it; the deepest regions are made all the way
   down; the transition is a slow inversion of that ratio.
2. **Creature bodies.** An orc is not bound together. **Creatures inherit the grammar through
   their equipment, not their anatomy** — strapped armour, corded bundles, an oath you cannot
   see. This is a real seam and the first creature tier is where it gets tested.

**Creatures stand like they have always been there — RULED from the study pass.** The asset
bar's figures are heraldic: planted, frontal or near-frontal, weight straight down, at most one
deliberate asymmetry. No mid-action idle poses, no dynamic lean. This is where its "epic"
lives — emblem, not drama — and it is exactly right for a world of things bound in place: **the
stillness is the menace.** Idle sprites are icons; motion is spent only where §9 spends it.

---

## 8. Wear — LOCKED

### 8.1 Two independent axes

**Traffic and care are separate dials, and the institution neither repairs nor removes.**

- **Traffic without care → polish.** Treads worn concave. Edges rounded off. Stone smoothed to
  a shine at hand height. Thresholds hollowed. Grime walked into a surface until it is part of
  it.
- **No traffic and no care → decay.** Old things persist in place, half-collapsed, and the
  traffic simply routes around them. A shrine somebody built in a side passage is neither
  maintained nor cleared away, because the Under-Warden's charter covers neither.

**Nothing in the Paths is ruined; everything is used up.** Surfaces record traffic, not time.

**Failure test:** *is the state of this thing explained by traffic and indifference?* A
collapsed shrine off the path passes. A collapsed shrine in the middle of the main flow fails —
the flow would have worn a channel through it.

### 8.2 Wear is legible — LOCKED

**Polish means you are on the path. Decay means you have stepped off it.**

Navigation carried by surface treatment, with no marker, no colour-coding, and no signage —
which matters enormously on a screen with no room for any of those. It also produces exactly
the unease the register wants: **the safe route is the one worn down by the traffic of the
dead.**

### 8.2.2 Walls age too, and they age at the foot — RULED (Rafe, 2026-08-29, at the wall gate)

> **South faces workable in kind — but the walls have opted out of history.**

Wear was written from floors and applied to floors, and the first walls to reach a gate arrived
with a four-hundred-year-old frontier's masonry looking newly cut. The ruling is bounded and it
names its own field:

- **At the base courses**, where the world touches a wall — scuff, arris-rounding, and patina
  rising one to two courses where routes run adjacent.
- **Keyed to the EXISTING traffic field**, not to a new one and not to noise. `TrafficField` puts
  vaults and shrines at exactly zero, so **sealed rooms stay sharp** — and sharpness then MEANS
  something, in the same way §8.1's absence of wear does on a floor.
- **The orc repair overlays count as age by implication** (§8.3.1a).

**A wall's traffic is not its own.** Nobody walks on a wall; what rubs its foot is the traffic of
the floor it faces, which is the same cell §6.5 measures it against.

⚠ **The polish half is expressed as FORM, never as a pale value lift.** §8.2.1 banned a value lift
for the floor's channel and the floor session paid for the lesson twice — *a value lift cannot
signal under a carried lamp, because brightness is what the light is saying.* So four hundred years
of shoulders and gear arrive as **arris-rounding**, a change of shape, and the grime arrives as
darkening. Neither asks the lamp for permission.

**As built and measured** (`tools/tier1_walls/measure_age_signal.py`, an A/B against an ageless
build of the same family so every lighting term cancels): Weber **0.061 / 0.133 / 0.210** at ages
1/2/3, monotonic, sealed cells flat to 0.0000. The top step clears §13.8's floor-family reference
of 0.1440 by 46%; ages 1 and 2 sit under it deliberately, because light traffic on a remote branch
should be almost nothing.

### 8.2.1 The trodden channel — RULED (Rafe, 2026-08-25)

The primary expression of legible wear on floors is **a polished channel worn through a wider
hall** — the path of centuries of dead traffic, running down the middle of rooms and corridors
that are wider than it. Ordinary floor flanks it. The channel leads somewhere: stairs down, or
rooms that matter. Route legibility comes from the channel, not from signage.

One-tile-wide corridors remain fully in the game — the chokepoint is a load-bearing roguelike
verb and nothing about the art removes it. A one-wide corridor is either **trodden** (on the
main route: polished wall to wall, because the traffic had no room to spread) or **neglected**
(off-path: §8.1 decay). Both states must be drawable and must read apart at 1×.

**Consequence for review scenes:** a floor candidate is not fully reviewed until it has been
seen in at least: open floor, the channel, a trodden chokepoint, and a neglected passage. A
review scene shaped only as narrow corridor cannot pose the §8.2 question.

**RULING 70 — ABSENCE-ONLY IS BELOW THE PERCEPTUAL FLOOR UNDER A CARRIED LAMP (Rafe,
2026-08-28).**

The clause's reasoning stands and is not withdrawn: under a carried lamp brightness is what the
*light* is saying, and a value lift on a trodden stone is read as the torch. A blind seat did read
an earlier channel's lift exactly that way.

**But absence is bounded, and the bound has now been located.** Wear was driven to the limit of
what subtraction can do — grain kept 0.38 → **0.08**, value spread 0.45 → **0.20**, plus a new
arris pass taking **0.45** of the joint's depth beside a trodden stone — measuring **0.350 /
0.578 / 0.775** against the same stones unpolished. **Four rounds of blind seats did not report
it at all.** One checked for it explicitly:

> *"I checked specifically for a floor-based reason and there isn't one… **If you rotated the room
> 180° the floor would give me exactly the same amount of information: none.**"*

**Consequence: the trodden channel is REMOVED from the tier-one floor gate's scope.** It is not
abandoned and the implementation stands — wear is decided per stone from the map, so the channel
ends at a joint rather than at a tile edge, which is what §8.2.1 asked for and a per-cell channel
could never give. What is withdrawn is the requirement that a floor candidate *demonstrate* the
channel to pass, because the clause as written cannot be satisfied at 32px under this rig.

**The successor experiment is filed, not decided:** §5.4 holds that chroma is signal, and the
channel is exactly the kind of thing signal is for. Whether a bounded chroma shift can carry
polish where value cannot is a **polish-pass** question, and it is not this gate's.

**TIER-ONE REQUIREMENTS, banked from the floor-remediation seat — RULED (Rafe, 2026-08-27).**
A blind seat shown the survivor floors in the lit corridor culled every one of them, before and
after remediation, and the reasons that were *not* about keylines are the most useful thing it
produced. They are **requirements landing on tier one, not a verdict on the corpus** — they
measure the absence of systems tier one has not built yet, on a thirty-cell field of one
repeated tile:

> *"Nothing has been done to it. Every arris is sharp, every joint is the same width and depth,
> and the traffic line down the middle is as pristine as the edges."*
> *"the identical bracket-shaped stone sits at the identical position inside every single cell,
> so the eye locks onto a 32-unit lattice within one screen."*
> *"all five are laid to the same flawless repeating module, which is why not one of them reads
> as a floor anything has actually happened to."*

1. **A variant system.** One tile per role is a clone field, and a seat reads a clone field as
   printed paper at any quality of tile. The seat's own figure: author variants whose bond is
   offset between them, so a joint starting at x=8 in one cell lands mid-stone in the next.
   **§8.3 gives this its mechanism and raises its priority: the variant system is not only how
   a field stops looking printed, it is the ONLY place incident is allowed to live.** Until it
   exists there is nowhere to put a crack that does not turn it into a motif.
2. **A wear system, which is this clause.** The channel is what the seat asked for, unprompted
   and having never seen §8.2.1 — *"sand a 12-unit band down the centre, erase the joint detail
   inside it so joints fade where feet cross them, and leave joints at full depth only within
   4 units of the wall."*
3. **A floor-repair vocabulary.** §7.4's orc work exists on walls and nowhere on the ground —
   *"a cracked slab pinned flat with four driven iron pins, or a salvaged timber baulk dropped
   across a hole and worn smooth on its top edge."*
4. **A-HEB IS UNMEASURED AS A PARENT — twenty generations, or an explicit unknown-rate marker,
   before anything conditions on it (RULED, 2026-08-27).** Two of §5.5's four survivors now have
   a measured child ring rate: B-KAB 22 of 24, C-GAB 5 of 20. The secondary style parent has
   none, and the parent-rate run stopped short of taking it by its own declared fork. A
   `may_condition: true` flag is an authorisation, not a measurement, and after this week the
   two are no longer interchangeable — the same flag covers a reference that produced 92% ringed
   children and one that produced 25%. So A-HEB is either measured on the same twenty before it
   parents anything, or it carries an explicit unknown-rate marker wherever it is used, and the
   round using it budgets for screening at a rate nobody has measured.

⚠ **And the review scene owed the seat a fair question, which this clause had already said.**
Those verdicts were rendered on a one-tile-wide corridor, where there is no centre line distinct
from the flanks — so *"the middle of the corridor and the edge of the corridor are byte-identical"*
is partly the scene reporting its own shape. The paragraph above is the standing rule and it was
not met. **A floor round that means to pose the §8.2 question must build the four-scene set
first**; until it does, a wear cull is not fully chargeable to the tile.

---

### 8.3 The motif trap — RULED (Rafe, 2026-08-27, at the gate). LAW.

> **Any incident baked into a tile becomes a motif when tiled.**
>
> **Repetition converts accident into intent, and the eye reads pattern regardless of the
> incident's quality.**

**Therefore:**

- **Style parents carry incident-free material only.** §5.5's parent criterion sharpens from
  *compositionally neutral* to **incident-free**.
- **Incident arrives at the instance level, randomised** — cracks, wear, marks, and §8.2.1's
  channel — via **variants and overlays**: the floor system tier one builds.
- **A tile is the material; the incident is the variant.**

**Why this is a law and not a preference.** A tile is authored once and drawn hundreds of times,
and *drawn hundreds of times* is not a neutral operation — it is the operation that turns a
detail into a statement. Nothing about the incident changes; the **frequency** changes, and
frequency is read as intent. This is why a beautifully drawn crack is not a smaller version of
the problem than a clumsy one: **quality does not enter into it.** A perfect crack in every cell
is a perfect crack announcing itself thirty times, which is a wallpaper motif, which is
§1's *nothing is staged* broken by arithmetic rather than by intent.

**It explains the culls that had no other explanation.** Every blind seat that has looked at a
floor field said a version of this without being given the clause — *"the identical
bracket-shaped stone sits at the identical position inside every single cell"*, *"one bitmap
repeated edge to edge"*, *"the identical U-shaped notch centred on every slab"*, *"a centred,
orientation-locked ornamental motif repeated on every cell is decoration, not paving"*. Five
independent seats, four rounds, one finding. **They were not culling the tiles. They were
culling the tiling.**

**THE SCALE RULE, which is the part that bites hardest.** *The property lives at field scale and
does not exist at tile scale.* A single tile is not enough evidence to judge one, and neither is
a contact sheet of single tiles. **Judge a tile as laid** — this is what §13.1's in-scene rule
has always been protecting, now with a mechanism behind it. It is also why the ring instrument
could not settle C-GAB and was never going to: it looks at one 32×32 tile, and no amount of
tuning reaches a property that is not in its input (`REPORT.md` §6, §12.1's cross-reference).

**Consequence for review scenes, on top of §8.2.1's four:** a floor is reviewed **tiled**, over
enough cells for a repeat to become visible. A one-cell view cannot pose this question any more
than a one-wide corridor can pose the §8.2 one.

**RETROACTIVELY, and it completes a finding that was left open.** `REPORT.md`'s Round B rescore
banked the repetition and absent-wear culls as tier-one requirements rather than as a verdict on
the corpus, and could not fully say *why* the distinction was principled. This clause says it:
**those tiles were BASES, judged as FINISHED FLOORS, before the incident system existed.** The
seats were right and the tiles were not being asked a fair question — not because the review
corridor was one wide (that too, §8.2.1), but because **a base and a floor are different
objects**, and every round so far has shown a seat the first while calling it the second.

**The division this establishes, which tier one inherits:**

| | authored | carries | judged |
|---|---|---|---|
| **base tile** | once, per material | material only — no incident | never alone; only as laid |
| **variant / overlay** | per instance, randomised | the incident — cracks, wear, marks, channel | in the field it produces |

#### 8.3.1a AND IT WAS BROKEN AGAIN, BY THE ORC LAYER — RULED (Rafe, 2026-08-29, at the wall gate)

> **The comb and spike marks on the top band are bindings. Remove them. Tops are incident-free.**

The tier-one wall round built §7.1's orc layer as world-placed overlays — correct, and correctly
kept out of the tile — and then placed them on **both planes**. Identified at the gate:
`lash` is an iron strap with three rope turns across it, which at 32 px on a wall top is a comb;
`strap` is the plain vertical bar, which is the spike; `patch` is four sawn-grain strokes, which is
a second comb.

**Three blind seats had already described the first one without being told anything existed
there** — *"a vertical post with three horizontal crossbars"*, *"a stake or rack"* — and every one
of them said the same thing about it: **it is holding nothing.**

**§8.3.1 outranks §7.1 on a top plane, and this is the general form:** an overlay at a hashed
position on a plane the bible says carries no incident is still incident on that plane, however it
is placed and whatever it depicts. World-placement answers §8.3's *lattice* objection; it does not
answer §3.1's *this surface carries nothing but the joints between the blocks it is made of*.

⚠ **What it costs, stated because it is real:** a wall mass seen from above now shows no orc work
at all, and §7.1's *show me what holds this together* is answered only where a reveal is. The
bindings ship face-only.

**AND THE REPAIRS ARE AGE — RECORDED (Rafe, 2026-08-29).** A strap over a joint means the joint
moved. A patch means a course broke. A lash over a strap means the first repair failed and nobody
replaced it (§7.3, *repaired on top of prior repairs*). The orc layer is the wall's history stated
by implication, and it is counted as such alongside the aging pass rather than as decoration on
top of one.

#### 8.3.1 It applies to WALL material identically — RULED (Rafe, 2026-08-27, at the device gate)

**§8.3 was written from floors. It is not a floor clause.** The device gate looked at the
sighted round's walls on the phone and found the same arithmetic running:

> **Wall tops are incident-free material. Boundary rules and edge ticks are pattern, and they
> are out.**

**This culls a construction this bible ruled in four hours earlier, and the sequence is the
point.** §3.1 established that a wall top is flat — *not face material re-toned* — and the
sighted round built that flatness with a **regular 2 px joint grid on a 16 px pitch**,
phase-offset per variant. On a still, at 2×, that reads as the joints between blocks. Tiled
across a room on a phone, it is **a ruled grid** — an incident at fixed offset in every cell,
which is exactly what §8.3 forbids, arriving through the one part of the tile §3.1 had just
made prominent.

**§3.1 is not weakened by this and must not be read as weakened.** *A top surface is not face
material re-toned* stands. What §8.3.1 removes is the thing that was standing in for material
once the coursing came off: **flat does not mean gridded, and a boundary rule is not a
material.** A wall top's material is whatever the stone is; if that reads as empty at 32 px,
the answer is a variant system, not a lattice.

**Where the boundary between the planes goes instead.** The turn between top and face is
geometry and stays — it exists only where floor lies south, so it answers to what adjoins it
(§12.1, §6.5). What may not stay is a rule drawn at a **fixed offset inside every tile**,
because that offset is what the eye adds up.

**The general form, so the next asset class does not have to earn this a third time:**

> **Any treatment applied at a constant position within a tile becomes a lattice when tiled,
> whatever it depicts and however well it is drawn.** Joints, ticks, rules, borders, panels,
> caps. The test is not *what is it* — it is *where does it sit, and does it sit there every
> time.*

**Floors earned this clause; walls confirmed it; it is now written as a property of tiling
rather than of either.**

#### 8.3.2 MATCHING IS AGREEMENT, NOT CONSTANCY — RULED (Rafe, 2026-08-28). Edge-matched sets are legal.

**§8.3.1 forbids a treatment at a CONSTANT POSITION. It does not forbid two neighbouring tiles
from AGREEING about where their shared boundary is crossed** — and the difference between those
two things is the difference between a lattice and a floor.

The clause needed saying because the honest reading of §8.3.1 alone would have banned the only
construction that answers floor session one's terminal finding:

> *"Joints enclose nothing — 99.1% of the floor is one connected region. No stones, only
> scratches. … Every 'stone' leaks into every other stone. For an underworld whose whole premise
> is that it is ADMINISTERED, a floor that cannot show a single completed stone is arguing the
> opposite case."*

A joint network can only close if joints **agree across cell boundaries**, which requires the tile
chosen for a cell to depend on its neighbours. That is an edge-matched (Wang) set, and it is
hereby legal.

#### 8.3.3 The corner theorem — RULED (Rafe, 2026-08-28), and it is a PLATFORM THEOREM

**No stone may cross a horizontal tile boundary, so a full-width joint sits at exactly one tile
pitch — for ever, in every region, in any floor whose stone values are addressed at runtime.**

**It has a twin.** §13.7's *deflection theorem* runs the same argument in the other direction: a
treatment keyed to WORLD position cannot register against structure keyed at RUNTIME. Read the two
together — one says what the bond may not do, the other says what may not consult the bond.

The proof is short and does not depend on the art. Four tiles meet at a grid corner. A tile shares
one boundary family with its eastern neighbour and one with its southern, and shares **nothing
with its diagonal**. So a stone covering a grid corner is seen by four tiles that have no common
data, and no scheme can make them agree what it is worth. Measured on the crossing-joint geometry
that preceded the ashlar bond: **27 of 77 stones, 19.9% of stone pixels, unaddressable**.

**Authoring the value class into the tile index does not buy the geometry back.** Corner classes
as an index dimension make the same assumption — that a tile's regions map to its corners — and
fail identically. This is a theorem, not a budget: the only thing that permits a slab crossing a
course line is **giving up addressed stone values there**, and accepting the seam that leaves.

A blind seat found the consequence unaided and culled for it, which is why it is written down
rather than left as an implementation note:

> *"Five continuous unbroken full-width joints at exact 64px pitch, which no slab ever bridges.
> **That is a tile edge, not a mason's decision. A mason lays a long stone across a course line; a
> tiling engine cannot.**"*

**ACCEPTED, AND REGISTER-JUSTIFIED.** Institutional masonry is coursed to spec — the Paths are
ADMINISTERED, and a filing system for souls is laid by contractors working to a standard, not by
a mason improvising. **The tell is provenance, not irregularity.** Through-stone irregularity
belongs to the orc layer *above* the floor: salvage, driven pins, lashed timber, the things a
company of soldiers puts on top of a floor it did not lay and cannot replace.

**MEASURED AND BOUNDED.** `constant_pitch_lines` reports the share of full-width joints sitting at
the tile pitch. Two courses per tile gives ~53%; three gives ~33% at the cost of ~9px courses.
**It cannot reach 0 while stone values are addressed at runtime, and that is not a defect to be
chased.** The mitigation that does not cost course height: a slab may not *cross* the boundary,
but debris, a driven pin, a lashed plank or a spalled edge may sit *over* it, and incident is
world-placed (§8.3) rather than part of the bond.

**THE DEGENERATE CASE IS NAMED, AND IT IS CHECKABLE.** A set is *lattice-degenerate* when its edge
families are too few to vary the crossing positions — agreement collapses into constancy and
§8.3.1's lattice returns wearing the fix's clothes. Two floors:

1. **At least THREE edge families per boundary orientation.** Two would make every boundary a
   coin-flip between the same two offsets; one is a ruled grid by definition.
2. **Crossing-position variance measured across the ASSEMBLED FIELD, and reported.** A field whose
   joint crossings cluster at constant offsets has re-derived the lattice and **fails**, whatever
   its family count says. §8.3's scale rule again: the property lives at field scale, so a table
   of intended families is not evidence — the pixels are.

**Register derivation**, because §13.3's origination rule requires one and "it makes the floor
close" is a mechanism, not a justification. §1 holds the Paths are **administered** — built,
catalogued, maintained by somebody. A floor of closed, laid stones is that claim in material; a
floor of open scratches is the opposite claim, and a blind seat reached exactly that conclusion
from the pixels without ever being shown the clause. Agreement between neighbours is what
*laid* means: a mason sets a stone against the one already there. Constancy is what *printed*
means. The law distinguishes them because the register does.

**First measured under this clause** (floor session two, `tools/tier1_floors/field_wang.py`):
3 families per orientation, 81 combinations, an 8×8 assembled field —

| | session one | under this clause |
|---|---:|---:|
| largest single region, share of floor | **99.1%** | **3.9%** |
| enclosed regions | 2 of meaningful size | **147**, median 177 px, 77 ≥ 64 px |
| crossing offsets, distinct per orientation | n/a | 3–4, modal share **0.375** (≈ 1/3) |

⚠ **AND THE MECHANISM COSTS SOMETHING, RECORDED SO IT IS NOT DISCOVERED LATER.** An edge-matched
tile **cannot be rotated or flipped** — its orientation *is* its meaning, and turning one relabels
its four edges so it stops agreeing with its neighbours. Session one bought most of its variety
from eight free orientations of every tile (§6.3 paying out: a receive-light asset has no up to
break). That variety must now be bought with combinations instead, which is why the family count
per orientation is a floor rather than a target.

⚠ **The trap has a mirror and it is not licensed here.** *Incident-free* is not *featureless*.
Material has structure — joints, bond, grain, value break — and stripping that to avoid a motif
produces the flat clone field the same seats cull on sight. **The test is whether a feature is a
property of the material (a joint between two stones) or a thing that happened to it (a crack
through one).** The first belongs in the tile; the second does not.

---

## 9. Motion — policy LOCKED, specifications PLACEHOLDER

### 9.1 What moves

**The player unit gets the animation budget. The world stays still.**

Movement is attention. Anything that moves is asking to be looked at, and the register says the
environment declines to compete. A dungeon where the protagonist breathes and everything else
is motionless is unsettling in precisely the right way, and it costs a fraction of animating
the world.

**Scope: idle, walk, basic attack, take-a-hit. That is the set.** Against the §9 frame
arithmetic these four states consume the budget; there is no "and others." **An addition
displaces a frame rather than extending the count** — proposing a fifth state means naming
which of the four gives up a frame, and that trade is a ruling, not a default. A budget that
can be added to is not a budget.

**The bar's arithmetic — read from the asset bar's own sheets (§13.3).** Each Oryx Ultimate
figure carries approximately **five frames**: a two-frame idle, a two-frame walk, one attack.
The entire "baseline of animation" that earns the library its wow is single digits per figure.
**PROVISIONAL count** — read from a packed sheet; to be confirmed against source files and
corrected in place if wrong.

**Sasha's budget accordingly: idle 2, walk 2, attack 1–2, take-a-hit 1 — six to eight frames
total.** Hero-only animation at this count is a small, bounded ask. The four states above are
measured against this bar, not against ambition.

### 9.2 The named failure — LOCKED

**Idle-flicker-everywhere.** Guttering torches, waving banners, ambient sparkle. It reads as
production value, it is what most pixel roguelikes do, and it would quietly dismantle the
register by making the world lively and attentive. Forbidden.

### 9.3 A candidate rule, not adopted — NOT LEGISLATED

Animating *what the fiction says is bound* — a thing straining against what holds it — would
make motion carry information: if it moves, something is holding it. Recorded as an idea with a
real argument behind it. **Not adopted**, because §9.1 already spends the budget and this would
reopen it. Revisit only if hero-only animation ships and there is appetite for more.

### 9.4 Frame counts are recorded, not implicit — LOCKED (process)

Whenever hero animation is built, **frame counts and the frame on which a hit lands are
recorded in the asset's manifest row.** Sound is out of scope for this bible (§14), and this is
the single coupling point: audio timing binds to animation frames, and reconstructing that later
is avoidable waste.

---

## 10. The player unit — Sasha — LOCKED

**Sasha is not an ordinary asset and does not go through the ordinary pipeline.** He is
animated, he is rigged, he carries the reserved warmth channel, and he is the only thing on
screen that moves. He gets his own bible section, his own identity card, and his own pilot tier.

### 10.1 Rigged from frame one — LOCKED. One-way door.

**Sasha is authored as a rig with declared attachment points before the first pixel is drawn.**
A fixed body armature with named attachment points — hand, off-hand, back, shoulders — held
consistent across every frame of every animation.

**This cannot be deferred and it is not visible in the finished sprite.** A beautiful Sasha with
inconsistent hand positions looks identical to a beautiful Sasha with consistent ones, right up
until the first weapon is attached and swims. There is no test that catches it after the fact,
no critic that flags it, and no fix short of redrawing every frame.

**This clause is instrumentable** and should be: declared attachment points must sit within a
derived tolerance across all frames. Tolerance: PLACEHOLDER.

### 10.2 Equipment is layered, never pre-composed — LOCKED

The player is a stack: body, then weapon, shield, cloak drawn over it. Cost scales with items,
additively. Pre-composed variants scale with *combinations*, multiplicatively, and die on
contact with a roguelike item table.

### 10.3 Layer by silhouette class, not by item — LOCKED

At reference-device scale on a busy screen, role reads come from **silhouette and prop**, not
interior detail. The differences that matter are the ones that change the outline: a spear's
long diagonal, a shield's mass on one side, a cloak's fall behind the legs. **Sword versus mace
is a few pixels at the end of an arm and may not read at all.**

So: layer by class — long weapon, short weapon, shield, cloak — with variants inside a class
carried by palette rather than shape. A dozen or so layers, not a table of sixty. **A small
number of silhouette-changing layers delivers nearly all the perceived variety**, because what
the player registers is *"I look different now,"* not *"that is a mace."*

**Corroborated:** the asset bar's hero sheet is this clause running commercially — one base
figure, recoloured into families, variety through palette on shared structure. **Caution that
travels with it:** their recolours roam the whole spectrum; ours run inside the spine plus
region slots (§5.2). Yarl's families will be narrower and quieter than the bar's. That is the
register, not a shortfall, and a critic comparing family-variety against the bar must be told
so.

### 10.4 Layers bind to the rig — LOCKED

Every layer is authored against the same armature, with grip and attachment points landing on
the declared positions in **every** frame. A weapon whose grip drifts two pixels between walk
frames looks broken in a way that is very visible and very tedious to fix.

**Layering and animation multiply each other.** This is affordable only because §9.1 restricts
animation to one asset. Had the answer been "animate everything," §10.2 would be unaffordable.

### 10.5 Pilot sequencing — LOCKED

**Sasha comes last in the pilot**, after floors, walls, props, and one creature. He is the asset
most dependent on everything else being settled: he must read against the floors, win contrast
against the walls, carry the warmth the palette reserved for him, and stand next to the creature
he fights.

---

## 11. Layer and region ownership — PLACEHOLDER

The table below is a **shape**, not an assignment. Sources are not chosen and generation tooling
is a Phase 4 decision.

| Layer    | Content                                  | Source                               | Status      |
| -------- | ---------------------------------------- | ------------------------------------ | ----------- |
| Floor    | Floor tiles, wear states                 | TBD                                  | PLACEHOLDER |
| Wall     | Walls, two-plane volume, thresholds      | TBD                                  | PLACEHOLDER |
| Prop     | Made objects, fixtures, tagged inventory | TBD                                  | PLACEHOLDER |
| Creature | Orcs, undead, Hall Wardens, NPCs         | TBD                                  | PLACEHOLDER |
| Player   | Sasha, Hollowmark, equipment layers      | Bespoke — never a library pull (§10) | LOCKED      |
| UI       | HUD, memos, frames, type                 | TBD                                  | PLACEHOLDER |
| Light/FX | Engine-rendered (§6)                     | In-engine                            | LOCKED      |

**The asset manifest is the only source of truth for what exists.** If it is not in the
manifest, the project does not own it. Row format is adopted from Gemfall: file, layer, source,
verbatim prompt, style reference used, mean snap distance, licence, acceptance date — plus, for
animated assets, frame count and hit frame (§9.4).

---

## 12. Readability — PLACEHOLDER

Every clause in this section is a stated design purpose awaiting a derived value.

- **Names itself at 1×.** Identifiability at true display size, in the scene, is required
  regardless of style conformance. Carried over from the retired track; it was never an
  Oryx-specific rule.
- **Silhouette and prop carry the read.** Colour second. Interior detail not at all. An asset
  that only reads because of an interior detail you had to squint at is a failure.
- **Value separation from the surface beneath.** Yarl has **no allegiance chrome** — no rim, no
  backing plate, no under-figure highlight. Gemfall's value-floor rule is primary with an engine
  rim as backstop; **Yarl's equivalent is primary with nothing behind it**, because a plate under
  every creature is the institution helping you, and the institution does not help you.
  Threshold: PLACEHOLDER.
- **Survives a busy screen** (§4.1). Every rule above is tested with neighbours present.

### 12.2 Props are authored at READABILITY SCALE, not at true scale — RULED (Rafe, 2026-09-10)

**Occasioned by the props walk, which FAILED on identifiability:** *"small, unrecognizable except
the fire; colouring quite good."* That is this section's own first clause — **names itself at 1×**
— failing at the human gate, on assets that passed every instrument and a five-seat panel. The
panel ranked the frame first of four; not one of its six flips said *I cannot tell what that is*.
A seat asked to rank craft will rank craft. **Nobody had asked the naming question.**

**The law.** A prop's size relative to the tile and to the character is **exaggerated until its
identifying feature reads at device 1×**. Real proportions are preserved: never squashed, never
stretched, never given a cartoon outline — **the shape stays honest**. What changes is how much
of the frame the honest shape is allowed to occupy.

- **A prop fills most of its cell.** The prop family that failed this gate carried content as
  small as **10 × 15 px in a 32 px tile** — a fifth of the cell, 20 device pixels across.
- **Large objects may span 1 × 2 or 2 × 2 cells where the fiction allows.** A standing stone
  **taller than Sasha** is correct. A barricade is **chest-high and wide**.
- **Each prop's defining feature must occupy a legible fraction of the sprite** — the marker's
  dressed face under its lashings, the barricade's over-built timber, the fire's fuel and ring.
  A prop whose identity lives in a detail is already failing this section's second clause.

**Register derivation — §12, and the standard is named rather than invented.** This is the
Warcraft / Diablo-2 convention: a top-down world reads because its objects are drawn at the size
at which they can be recognised, not at the size they would be if you measured them against the
floor. §12's second clause already says *silhouette carries the read, interior detail not at all*
— and a silhouette twenty device pixels across has no read to carry. Scale is the only lever that
reaches it, because every other lever (value, chroma, joint, grain) operates **inside** a
silhouette that is already too small to name.

⚠ **THIS IS A PERMISSION, NOT A LICENCE TO DISTORT.** *"Real proportions preserved"* is the
binding half. A barricade may be built larger than life and must still be built like a barricade;
the moment the shape itself is bent to fit the cell, §12.2 has been used to break §7.1. Growing
the object is legal; changing what the object is, is not.

**Colouring is APPROVED AND FROZEN IN KIND** at this gate (Rafe, same walk): *"colouring quite
good."* A re-author under this clause changes scale and keeps the colour treatment it has. That
also settles what #204 may and may not do.

**The instrument this clause gets, and it is eye-side.** §12's naming clause has always been
carried at the human gate (§13.2) and the audit's own instrument row says so. It now also has a
blind-seat form: **the cold-naming test** — a seat is shown the prop in scene, unprompted, and
must say what it is. **A miss is a FAIL on §12, not a style note.** It is a naming question put
before any ranking question, because the walk proved that ranking does not ask it. Promoted under
§13.5 with its demonstrated failure on the record.

### 12.1a The void is dark by OCCLUSION, not by a ring — RULED (Rafe, 2026-09-03)

**Unexcavated mass is unlit by construction.** The lamp stops at the wall face, because solid
stone is behind it; what lies past the face is dark because nothing reaches it, not because a
different tile was laid there. **Occlusion-not-illumination, applied to the void.**

This RECONCILES two rules that had been pulling against each other for a fortnight:

- **§12.1 forbids the ring.** `void_ring` swapped a darker MATERIAL in at a fixed Chebyshev
  distance, and a classification that changes at a cell boundary puts a luminance step at that
  boundary. A blind seat found it unaided — *"two perfectly straight vertical seams in the
  darkness … 200px-tall ruled lines in the dark itself"* — and it was culled as an outline that
  placement bakes.
- **But dropping it made the lamp shine through rock.** With every wall cell capped, a frame
  critic measured the consequence: *"Light is passing through solid rock. At y=340 the rock at
  x=300 is (63,45,25) against the corridor floor at (410,300) = (82,64,41) — the unexcavated mass
  is 77% as bright as the walked surface."*

Both readings were right, and the thing wrong with each was the same: **darkness was being
authored instead of received.** A ring authors it into the tile; a flat void authors it into the
palette. §6.3 has always said assets receive light and never depict it, and the void is not an
exception to that — it is the case that proves it.

**So the void needs no material of its own and no ring.** It is the same found rock, occluded.
The transition is then a LIGHTING boundary at a plane boundary, which §12.1's own text names as
**form** rather than outline, and it moves with the lamp instead of sitting on the grid.

**Implementation note, recorded so the ruling is not mistaken for the build:** the renderer
currently lights wall cells regardless of what stands between them and the lamp. Making the lamp
occlude against the wall faces is a presentation change (an occluder pass), not a composition
one, and it is outstanding at the time of writing. §6.5's standing-distance law is unaffected —
it governs what a LIT surface delivers at range, and this governs which surfaces are lit at all.

**STATUS (2026-09-12, cast-shadows round): THE OCCLUDER PASS IS BUILT AND MEASURED; the walk
that lands it is Rafe's.** `ReviewLighting.AddOccluders` + the light-mask split in
`Tier1BoundaryWall`, `--occluders all`, void ring **0**. Measured on the props room, no
occluders → occluders: **face 40.16 → 39.61, cap 52.92 → 52.92** (the r29 failure, 37.90 → 5.51,
is closed), **unexcavated 27.35 → 12.50** — the same rock at ambient, where the ring's 2.58 was a
darker *material*. The interim fallback below is therefore retired for every capture this round
and after; it stays in the record as the ruling it was.

**THE SHADOW WALK (Rafe, on the handset, 2026-09-13): *"Props sit on the floor, the room has an
outside — the round's two questions are answered yes."*** Marks: darkness 0.8, softness 8.0 (the
knob's ceiling), void 1/3; **flicker RULED ON** — the tended exception over §9.2, the orc fire
is the one thing in the world that moves. Provisional reference seeded from the walked frame,
re-marked after the flips. Three flips, all engine, and each is the §13.4-shaped kind — the
instrument was fine, the travel was a builder's guess:

1. *"8.0 is the knob's ceiling and shadow edges are still traceable lines ... a lantern doesn't
   throw searchlights (§1: nothing is staged)."* The ladder now runs to the engine's 64
   (PCF13 was already the widest kernel); 24 is soft, 48 melts the edge.
2. *"Dropping darkness below 0.8 fills shadows with grey lamp-leak, not dark. Tint the leak
   toward the ruled ambient hue (§6.2)."* The leak now carries the ambient's channel ratios:
   at darkness 0.5 the shadowed floor reads rgb 72/72/93, not grey.
3. *"The fire glows but doesn't light its surroundings or cast; it needs real radius and
   energy."* 0.9 / 2.5 tiles → 1.6 / 4.0 tiles, a `fire` row on the panel — energy marked at
   the re-mark, reach and tint RULED at the props walk (2026-09-13: *"the fire is good"*). A "dark" probe 2.2 tiles from the fire had to move — a real fire lights it.

**RE-MARKED (Rafe, 2026-09-13), both fixes confirmed on device:** softness 12.0 (*"melts
edges — no beams at 12"*), darkness 0.8 (*"the ambient-tinted leak reads as cool dark, not
grey"*), fire 1.6 with its cast shadow visible once the player's lamp isn't filling it. Ratified
as required engine flags (§6.2's live table); this frame seeds the shadowed reference. Props sit
— yes; the room has an outside — yes.

**How, since the obvious mechanism was measured and did not survive.** A 2D occluder cannot
say *light this cell's own surface and stop behind it*: a per-cell quad whose light-facing edges
cast shadows its own face (r29), and with those edges culled the cells of a wall row shadow each
other obliquely through their far edges (face → 28.47 / 37.19; cap → 30.05 / 38.00 under the two
cull modes) — and a far edge cannot darken a thick mass whose far side is rock. So **the first
surface the lamp meets is exempt by mask, and everything behind it receives**: a wall cell with
floor anywhere in its 8-neighbourhood (the reveal, and the cap beside it) sits on a light mask
the lamps illuminate but never shadow; deeper mass, the floor and the walls behind receive; every
edge casts. Objects the same way (§3.2): a prop's sprite is drawn north of its footprint on
screen, exactly where a lamp from the south throws the footprint's shadow, so props never
self-shadow *by mask* — measured Δ +0.00 on every sprite's interior — and their footprints
(the cabinet base parallelogram, or the round exception's circle) cast onto the floor.

**The shadow is the ambient, and on this engine that is a black shadow colour.** Measured, after
two wrong guesses: `Light2D.ShadowColor`'s RGB is the fraction of the lamp that *leaks* into a
shadowed pixel (the ambient hue leaked 15% and read as a wash); its alpha is inert. Black leaks
nothing, so what remains in shadow is the `CanvasModulate` ambient — §6.2's hue — and the
darkness knob is `rgb = 1 − d`. Never black paint: the shadowed floor measures the ambient-lit
floor, not 0.

> **THE INTERIM, RULED (Rafe, 2026-09-05): THE FLAT-DARK FALLBACK STANDS UNTIL THE OCCLUDER
> SHIPS, AND IT IS DECLARED PER CAPTURE RATHER THAN BAKED.**
>
> Round 28 composed the first room with both real surfaces in it and measured what the ruled
> `void_ring: 0` delivers with the occluder still outstanding: **`void=0`, `face_suppressed=192`,
> `cap=216+0void` — the lamp lighting 192 cells of solid rock, and a room with no outside.**
> `tools/tier1_floors/evidence/combined_r1_ring0.png` is that frame. It is the consequence this
> clause's own note predicts, and a room that has no outside cannot be judged as a room.
>
> So a round that needs a real dark beyond the walls runs **the ring, as a flat-dark fallback**,
> through `--void-ring` — a **capture-time** flag. **The manifest keeps the ruled 0**, because a
> round does not get to quietly move a number a ruling put there, and every capture log carries
> `ring>1,OVERRIDE manifest=0` so no frame circulates without its own departure attached.
>
> **The cost is named and is not forgiven:** a ring is a classification that changes at a cell
> boundary, so it puts a luminance step on the grid, and round 8's seat read exactly that step
> unaided as *"two perfectly straight vertical seams in the darkness."* **This clause is not
> weakened by the interim.** Occlusion remains the ruling; the occluder pass remains outstanding
> and owns its own round with a walk behind it.

### 12.1 No baked outline — LOCKED (Rafe, 2026-08-24)

**Nothing in Yarl carries a baked dark ring.** Separation is delivered by the value floor above
and by §7.1's linear elements — bands, straps, pins, tags.

Reasoning, recorded because a weaker version of it was offered first and should not be what the
rule rests on. A baked outline does **not** conflict with dynamic light the way a baked
highlight does: a highlight declares a light *direction* that a torch arriving elsewhere
contradicts, whereas an outline is direction-agnostic and modulates with everything else. That
argument is soft and is not the basis of this ruling. The three that hold:

1. **A fully outlined sprite reads as a sticker.** It separates as a discrete object regardless
   of illumination, flattening the light response §6 exists to buy. Heavy outlining and dynamic
   lighting partly cancel.
2. **It fails where it is most needed.** A dark ring against the near-black ambient of a deep
   floor is invisible. The outline works in bright scenes and abandons the player in dark ones,
   which is the wrong way round for a game whose light withdraws with depth.
3. **It competes with §7.1.** Bands, straps, and tags are the high-contrast linear elements
   carrying the read at 1×. A ring around everything is a second linear system fighting the
   first for the same few pixels.

**Parked fallback — not adopted.** An outline can live in the engine rather than the art
(Gemfall moved theirs there at their v1.1). If the pilot shows figures failing to separate from
floors, an engine-side rim remains available with **no redraw**. Leaning against it: §12 already
holds that the institution does not help you, and a ring under every creature is help. Recorded
so the door stays on its hinges.

**RULED (Rafe, 2026-08-26): PLANE-BOUNDARY OCCLUSION IS FORM. Legal, and required. The ring
stays banned.**

The distinction, because the composition spike flagged this as a live tension and it is now
settled: a **ring** is a dark edge drawn around a thing *because it is a thing*, present on every
side regardless of what adjoins it, and it is what makes a sprite read as a sticker. **Plane-
boundary occlusion** is a dark edge drawn where one plane stops and another begins — on the
wall's own edge, only where floor is adjacent, absent where wall meets wall. The two look alike
listed as pixels and are opposite constructions: one describes the object, the other describes
the *geometry between* objects.

The evidence that forced the ruling: composed two-plane walls rendered without it put a lit wall
top at luminance 96 beside a lit floor at 122 with no boundary of any kind, and a blind critic
could not tell solid from walkable — a `cannot-read` cull, twice. The shipped placeholder tiles
this renderer's mask table was fitted to had always carried it. §3's two planes do not, on their
own, separate wall from floor; this is what does.

**It does not compete with §7.1's linear elements — it is not on the object at all.** Straps,
bands and tags still carry the read across a sprite; occlusion carries the read across a
boundary. Both, not either.

**And it is mandatory, not merely permitted: a wall-top meeting floor without its occluded edge
is not purity, it is a missing plane.** This is construction grammar (§7), not outline — an
occluded seam exists only where two planes actually meet, varies with what adjoins it, and is
therefore form (§6.3). The test that separates it from a ring is stated in the worked example
below.

**A WORKED EXAMPLE, because the spike got this wrong on its first attempt and the error is the
instructive part. THE RING PROHIBITION IS VALUE-AGNOSTIC: A PALE RING IS A RING.**

Round 8 of the composition spike answered a critic's request for a cap band by laying a coping
course of paler, smoother stone along every floor-facing wall edge, and reasoned that a
*material* change declares no light direction and is therefore legal. The material reasoning was
sound and the construction was not. The blind seat:

> *"A flat, featureless ribbon at floor value is applied to every wall edge for its entire
> length, ringing each mass in bright piping so the quadrants read as cut-out cards rather than
> stone."*

Cut-out cards is the sticker read, arrived at from the opposite end of the value scale. The
ruling above already excludes it — *drawn around a thing because it is a thing, present on every
side regardless of what adjoins it* — and nothing in that sentence says dark. **What separates
occlusion from a ring is whether the treatment answers to the geometry it sits on, not whether
it is lighter or darker than its surroundings.** A uniform ribbon of constant width and constant
value applied to every edge answers to nothing.

The same round's ranking is the corroboration: the arm carrying the coping ribbon placed third
of five, below the arm with no cap at all.

**AND THE PROHIBITION IS SCALE-DEPENDENT AS WELL AS VALUE-AGNOSTIC — RULED (Rafe, 2026-08-27,
at the gate).** The worked example above established that a pale ring is a ring. This
establishes where you have to stand to see one.

C-GAB was called ring-clean by an instrument and by two blind seats, and **a frame at field
scale** by the gate. Nothing about the tile changed between those readings; the number of copies
did. A contour that turns its corners and returns *inside its own cell* is invisible as a ring
when you hold one tile and unmistakable as one when you lay nine — because the test that
separates a ring from a joint is **whether it continues into the neighbour**, and a single tile
has no neighbour to continue into.

> **A ring is judged AS LAID. A single tile — and a contact sheet of single tiles — cannot
> answer this clause.**

This is §8.3's scale rule reaching §12.1, and it is why the ring instrument's limit is
structural rather than a tuning failure: it reads one 32×32 tile, so the evidence is not in its
input at any threshold.

**Consequence for the re-aim audit:** `tools/art_lint/outline_repair.py` has no successor under
this ruling and is retired with the Oryx track.

---

## 13. Acceptance — LOCKED

### 13.1 In-scene review is the only approval for anything that lands

No candidate is approved from a standalone contact sheet. Verdicts come from the production
renderer, in the lit scene, seated among approved neighbours. During Phases 1–3, standalone
artifacts are exploration and are judged freely; nothing lands, so nothing needs the scene. The
rule activates at Phase 5, which is sequenced to satisfy it: **the pilot builds floors and walls
first, so the first approved assets ARE the scene**, and every prop and creature after them is
judged in context from day one.

§6.3 makes this a technical necessity as well as a discipline: a receive-light asset **cannot**
be judged unlit.

### 13.2 The human gate is the final instrument

Machine checks are floors, never verdicts. Every metric ever promoted on this project was
eventually humbled by an in-context human look.

> **VINDICATED AGAIN — 2026-08-27, the sighted round's device gate. This is the sharpest
> instance the project has produced, because everything upstream of the phone was green.**
>
> The sighted round did not fail its instruments. It passed them:
>
> - two independent blind seats ranked the candidate **above the asset bar**, unhedged, with no
>   cull — §13.3's own bar, met;
> - the differencing check passed and the arm it was compared against failed it;
> - the ring instrument passed every composed tile, its own control suite green;
> - the recipe's delivered numbers hit the bar's measured construction to two decimal places.
>
> **Rafe's verdict on the phone: FAIL. "The phone overrules the stills."**
>
> Nothing in that list was wrong as far as it went. The seats really did prefer it; the numbers
> really did land. **What they could not see is the thing the clause exists for** — the work at
> the size, the distance and the light it is actually played in. Two of them were looking at a
> 2× crop on a desktop, and one of them was a number.
>
> **The operative lesson, and it is about sequencing rather than about seats:** a stack of
> green instruments is not evidence of quality, it is evidence that the instruments were
> satisfied. **Instrument agreement raises confidence in the instruments, not in the asset.**
> The gate produced two laws in one look — §8.3.1 and §6.2.1 — that six critic-held rounds and
> thirteen seat transcripts did not.

Two gate screens are adopted from Gemfall, and they are the most portable artifacts in that
project's apparatus:

**Name them cold.** Candidates shuffled with already-shipping assets, and **which is which is
not shown.** This blinds the *human*, not just the critic — no grading on novelty, no softening
toward the thing you know is the candidate. The shipped assets act as a positive control on the
eye itself: if one of them stops reading, that is a finding about the test conditions rather
than the candidate. Each slot asks its own card's question, and a card may legitimately ask
*"tell it apart, do not name it"* where naming would be an unfair question.

**The cast on its worst ground.** Each candidate is shown against its own measured *worst*
contexts. The measure is **uncalibrated, renders no verdict, and only decides ordering. No pass
or fail is drawn on the screen. The eye rules.** This is the machine finding the hardest case
and then shutting up, and it is a strict upgrade to §13.1 — which otherwise says "judge in
context" without saying *which* context, and the honest default left alone is a flattering one.

Every gate screen states the contexts it measures and flags any context the shipping game does
not contain.

### 13.3 The quality bar

**PASS means genuinely wowed.** "Fine," "acceptable," "good enough for now," "improved,"
"solid," "promising," and "close" are all **failing** verdicts. Hedging is failing. A critic who
finds no defect should suspect its own rigour before crediting the work.

The visual bar is a **blind side-by-side against shipped commercial games**, asking *"which of
these looks like the shipped game?"* — and the answer must be Yarl, or a tie.

**Reference set — three bars, each assigned to one question. The assignment is what keeps a bar
from becoming a style target.**

- **Shattered Pixel Dungeon — the structure bar.** Grid, readability, portrait layout, how much
  information fits legibly on a phone. It solves Yarl's exact problem. The gameplay bar; the
  look must exceed it.
- **Rogue Wizards — the scene bar.** Light, material, depth, and above all cohesion: everything
  made to the same standard, nothing left provisional. Landscape and isometric, so its
  projection is explicitly **not** a target (§3).
- **Oryx Ultimate Fantasy — the asset bar.** Per-sprite craft: the standard a single Yarl asset
  must meet or beat on a like-for-like look. Recorded in the owner's words, because they are the
  bar speaking: *clean, small, detailed, serious; the threats are threatening; a tasteful
  palette without being overdone; a baseline of animation; wow factor.* This library is also the
  animation-coverage spec for §9 (basic movement and attack, nothing lavish) and approved plant
  stock for critic control rounds — professionally drawn and in the wrong register is precisely
  the failure a soft critic waves through.

**The presentation caveat.** Oryx Ultimate's wow is partly presentational — heroes centred,
posed, composed for the sheet. Yarl's register forbids staging (§1). The bar is their craft,
never their presentation: a Yarl asset must stand beside theirs while unposed, indifferent, and
in a corridor. When a comparison reads "theirs looks better," the first question is **better
made, or merely better posed?** — and that question belongs in the critic's kit.

**This is a quality comparison, not a style target, and the distinction is thin enough to state
plainly: we ask whether Yarl looks as finished, never whether Yarl looks like them.** A finding
that an asset "doesn't match SPD" is not a defect. A finding that it "looks like the free one
next to the paid one" is.

**The guard — LAW.** A bar may never appear in a flip list. "Make it more like [bar]" is an
illegal critique in any round, from any critic, machine or human. Comparisons answer *are we as
good*; only the bible answers *what we should look like*. **DNA and bars never swap roles:
nothing conditions generation that we do not own** (§1.3), and nothing we own is above being
judged against the best.

**WHERE THE BARS LIVE, and the one rule that governs reading them — RECORDED (2026-08-27).**

| bar | location | status |
|---|---|---|
| **Oryx Ultimate Fantasy** — asset bar | **`~/development/assets/oryx`** — the full library, licensed, local | available for measurement |
| **Shattered Pixel Dungeon** — structure bar | not on this machine; **Rafe supplies captures** | outstanding |
| **Rogue Wizards** — scene bar | not on this machine | outstanding |

Recorded so a future round does not re-hunt for a source it already has, and so an absent one is
known to be absent rather than discovered mid-round. The sighted round lost its second source
that way: the brief named SPD, nothing on the machine matched, and the recipe went out
**single-sourced and said so** (`tools/sighted_round/WALL-RECIPE.md` §1).

> **RULED (Rafe, 2026-08-27): naming a source that is not there is the PROMPT's error, not the
> session's.** The correct response to a missing bar is to report the gap, not to substitute a
> nearby source and call the work two-sourced. A recipe that says *single-sourced, and here is
> what that costs* is worth more than one that quietly fills the hole.

**MEASUREMENTS LEAVE; PIXELS NEVER DO.** A tool may read a licensed local library and emit
numbers — `measure_bar.py` is the pattern: it wrote `bar_measurements.json` and nothing else.
**No bar pixel enters this repo, in any composite, reference, or corpus (§1.3), and a known path
does not relax that by one pixel.** The path above is an instruction for measurement tools, not
an invitation to the asset pipeline.

**The origination rule — LAW.** The bar may *occasion* a law; only the register may *justify*
one. Every law in this bible must cite its register derivation, not merely its bar observation
— the v0.5 pass is the worked precedent: faceless creatures follow from §1.1's division of
warmth, heraldic stance follows from a world where everything is bound in place; the bar was
where we noticed, the register is why it's true. **A proposed rule whose only justification is
"the bar does it" is conformance and is refused**, regardless of how good the bar is. This
clause exists because the bible now cites the asset bar in nine sections, and the distance
between "lessons cross" and "style conformance" must be a test, not a paragraph's goodwill.

### 13.4 Register clauses are carried eye-side and are never instrumented — LOCKED

**This is the most important process clause in this document.**

Gemfall's Ruling 77.5: *where a selection process optimises against a mixed set of criteria, the
ones with instruments win and the ones without them are silently traded away* — not because
anyone chose to, but because only one side of the trade is visible to the optimiser. Their
worked case: a figure's declared hi-vis vest ended with 10 torso texels against 68 in its hat,
because a candidate carrying real torso signal was traded away to satisfy a palette clause. The
palette clause won because it was the one with a number.

**Yarl's register is almost entirely uninstrumentable.** *The art plays it straight. Nothing is
ruined; things are used up. Nothing is staged.* No script checks these. And they are exactly
what got traded away last time — the previous track had instruments for everything except the
question that mattered.

Two binding consequences:

1. **Register clauses are carried at the human gate, with explicit weight, and are never
   assumed to survive an automated selection that cannot see them.**
2. **We do not close the gap by inventing an instrument.** A weak proxy is worse than an
   acknowledged absence, because it re-enters the optimisation and starts winning trades it has
   not earned. The precedent is a personhood predicate that passed **67.55% of random noise**
   with a ruling already resting on it. **There will be no "dread score" and no "staging
   detector."** §15's honest `NO INSTRUMENT` row is the correct output instead.

#### 13.4.1 A REPEATED SEAT REQUEST IS NOT A VOTE AGAINST A GATE RULING — RULED (Rafe, 2026-09-05). It is a report that the treatment is not reading as intentional.

**The occasion.** *A packed joint takes the shine* was ruled at the human gate, after a walk
contradicted the table: polish on stone faces only amplified the delivered face-to-joint contrast
by exactly the light, and the outline share in the lit band fell **16.3% → 1.3%** once joints took
their share. *"The source was clean the whole time; the renderer was drawing the ring."*

**Two independent blind seats, two rounds, two different decks, have since asked for the inverse:**

> *"sharpen the joints; they fade toward the light edge"* — round 27
> *"in the lit zone right of the figure the stone joints vanish entirely — you cannot tell where
> one slab ends. Restore joint contrast at full light so the grid survives exposure."* — round 28

**RULED: REFUSED. The flip is not applied and the clause is not reopened.**

**Why this is §13.4 and not new evidence.** A blind seat is a proxy for the gate, not the gate —
§13.2 and LOOP-PROCESS §1.2 both put the ruling with the eye that walked it. Counting seat
requests as votes is exactly §13.4's failure running in the review layer instead of the
generation layer: **the side of the trade with a repeatable signal wins, and the side carried
eye-side has no counter to offer.** The gate ruled on a walk; two seats on stills cannot outweigh
it by arriving twice. Nor is agreement between them independent — they are asked the same
question about the same defect class, so the second is close to a re-run of the first.

**AND THE REPETITION IS STILL INFORMATION — just not the information the flip asks for.** Two
observers reading a deliberate treatment as a *failure* is a report that **the treatment is not
reading as intentional.** A joint that packs shut because it has been walked into should look
walked into; one that merely stops being visible looks like a joint that was not drawn. Those are
the same pixels and different pictures.

> **So the successor is a MAKE-IT-READ item, not a DEEPEN-NOW one**, and the distinction is the
> whole ruling. Deepening the joint at full light is capitulation: it undoes the gate's finding
> and puts the ring back. Making the packing legible *as packing* — grit, fill, a shoulder, the
> vocabulary §8.2.1 already owns — answers what the seats actually saw without touching what was
> ruled. Parked as a polish item; it is not this round's and it is not urgent.

**The general form:** a ruling made at the human gate is overturned at the human gate. Everything
else is a report about how the ruled thing is being read, and reports about reading are answered
by making it read.

### 13.5 No instrument's pass counts until it has demonstrated it can fail — LOCKED

Adopted verbatim in force from Gemfall's Ruling 47. Positive control **before** verdict, for
every critic, census, measure, and harness. Stub the metric to a constant, plant the defect it
exists to catch, mutate the thing it guards — then show it goes red and record the verbatim
failure. **An instrument that cannot be made to fail is decorative and must be labelled so or
deleted.**

This is the same failure class already named on this project as MISFED. Gemfall supplies the
procedure that catches it.

### 13.6 A candidate never contributes to its own acceptance bar — LOCKED

Where a constant must be calibrated, derive it from the corpus already accepted, never from the
work seeking acceptance. **The eye leads the number: calibrate after the verdict, never before
it.**

### 13.8 The perceptual floor — LOCKED (Rafe, 2026-08-29, at the device gate)

> **A signal authored below the perceptual floor is ABSENT. Everything authored proves readable
> amplitude under the ratified rig at 1×.**

It is law because it is the third instance of one family, and the third time it cost a gate:

| | authored | delivered |
|---|---|---|
| **the trodden channel** | wear driven to the limit of subtraction — 0.350 / 0.578 / 0.775 against unpolished stone | four seat rounds did not report it; *"if you rotated the room 180° the floor would give me exactly the same amount of information: none"*. Ruling 70. |
| **the trodden channel, EXECUTED (2026-08-29)** | three further levers: joints packed with grit (age spread 0.98 → 5.02 rungs), two palette rungs below the donors, a constant-luminance chroma rotation delivering 11.25° of hue | seven seat rounds did not report it. CLOSED — and the reason is §13.9, not 32px: at the busiest tile the whole ladder renders to 8-bit 1…4 and a rotation worth ΔE 10.12 at full light rounds to ΔE 0.00. See `docs/RULING-70-CHANNEL-CLOSED.md`. |
| **the incident overlays** | 127 marks, `event=44` in the log | **median mark 4px**, mean delta 8.18 luminance — below one rung. *"The pepper."* *"No cracks. Not one."* |
| **the stone grain** | ±4 luminance against a 13.23 rung | faces quantise flat. **"The floor reads as linoleum."** |

Each was authored, present in the source, and verified shipped byte-for-byte. Each was absent.

**THE TRAP IS SPECIFIC AND IT IS NOT CARELESSNESS.** Every one of those signals was *correct*: on
the right axis, in the right vocabulary, world-addressed, defensible clause by clause. Each was
also verifiable — a source instrument could confirm its presence exactly, and did. Nothing in the
loop asked the only question that mattered, which is not *is it there* but **is it there loudly
enough to exist**.

**WHAT THIS OBLIGES.** Any authored signal — texture, wear, incident, damage, anything meant to be
seen rather than merely to be true — carries a measurement of its **delivered amplitude in a lit
capture at 1×**, alongside the source check that proves it is present. A source check alone no
longer discharges anything.

**HOW THE FLOOR IS SET, AND IT IS NEVER PICKED.** §13.6 forbids a candidate contributing to its own
bar, and an invented threshold is worse still — a number defending itself. The floor is derived
from **verdicts already given**: one signal a human ruled present and one the same human ruled
absent, measured the same way, in the same capture, under the same rig. The tier-one floor's are
the crack network (*"excellent"*) and the stone interior (*"linoleum"*), and the floor is their
geometric mean.

⚠ **MEASURE BOTH ENDPOINTS THE SAME WAY.** The first version of this instrument compared a
within-population range against a between-population difference and derived a floor from two
quantities that were not commensurable. ⚠ **AND DO NOT FLATTEN THE LAMP TO MEASURE AMPLITUDE.**
Dividing by a local blur — the right move for judging *layout*, and what a blind seat does —
normalises away any signal that covers a large fraction of its own neighbourhood, which is exactly
what a surface texture is. Raising the dressing depth by half a rung moved the flattened number
from 0.123 to 0.125: **the instrument was cancelling the signal it existed to measure, and would
have reported the fix as a failure.** Amplitude is Weber contrast against the feature's own local
brightness; the lamp divides out of a ratio for free.

**THE FLOOR IS NOT A TARGET.** Clearing it by 3% proves nothing — that is the geometric midpoint
between *present* and *absent*, which is precisely the ambiguous point. And the ruled-present
signal is not automatically the target either: a dressing mark that matched the joint network's
contrast would have stopped being a dressing mark and become a joint. The obligation is to state
where the signal sits between the two ruled points, and why it sits there rather than higher.

### 13.7 Platform facts — measured, recorded once so nobody re-buys them

Not law, and not banked speculation either: each line below was paid for by a run and is cited
to the audit that paid. They are here rather than in the tooling notes because each one closes a
question a future session would otherwise re-open with generations.

- **A 2D occluder cannot light its own surface and stop behind it.** Measured (cast-shadows
  round, 2026-09-12): a per-cell quad whose light-facing edges cast shadows its own face
  (r29, 37.90 → 5.51); with those edges culled, cells in a wall row shadow each other obliquely
  through their far edges (face 40.16 → 28.47 / 37.19, cap 52.92 → 30.05 / 38.00), and a far
  edge cannot darken a thick mass whose far side is rock. **The mechanism that survives: the
  first surface the lamp meets is exempt by light mask (ring-1 wall cells, every prop sprite),
  everything behind it receives, every edge casts.** Face 40.16 → 39.61, cap unchanged,
  unexcavated 27.35 → 12.50, sprites Δ 0.00. This replaces the "occluder behind the reveal"
  §12.1a first imagined. `ReviewLighting`, `Tier1BoundaryWall`.
- **`Light2D.ShadowColor`: RGB is the fraction of the lamp that leaks into shadow; alpha is
  inert.** 0.0 / 0.5 / 1.0 alpha delivered the identical shadowed value; the ambient-hue RGB
  leaked 15% and read as a wash. So "the shadow is the ambient" is black RGB (the lamp
  contributes nothing; the `CanvasModulate` hue remains), and a darkness knob is `rgb = 1 − d`.
  A fill light is not a darkness knob — additive, it lifted the lit floor too (62.9 → 69.7).
  An emitter inside its own occluder polygon shadows the whole room from itself: emitters get
  no occluder.
- **The SE holds 60 fps with 216 wall occluders, 3 prop occluders and two shadow-casting
  lights** — every steady 240-frame window 16.67 ms mean / p95 / max, identical to the build
  without occluders (`tools/cast_shadows/evidence/perf_*_boot.log`). Vsync-locked: the frame is
  *met*; headroom is unmeasured and a GPU-time probe would be needed to say how much.
- **A control lit differently from the build measures exposure, not craft.** Round 1 of
  `art/cast-shadows`: three of five seats ranked unshadowed culls above a shadowed room. RULED
  (Rafe, 2026-09-12): not a broken judge — a seat-blind axis (§13.2); **plants and reference
  are captured under the deck's lighting regime — scene, rig, and shadow state.** The judge
  refuses an off-regime plant or reference (`frame_critic.pick_plant`, `regime`).
- **Generation cannot be told a projection.** Pro, given a projection template as a labelled
  reference AND the projection in the prompt, returned its own ¾ view receding right on
  **16/16** chests against a left template, and **16/16** straight-on barrels. img2img holds an
  authored projection at `init_image_strength` ≥ 150 and loses it by 90, and at 150 adds no
  object to a bare box. So a projected object is **authored as geometry** (every band, hoop,
  plate and shelf in the template — `tools/tier2_props/projection_mesh.py`) and generation
  supplies surface. A forced palette from a LIT frame turns every material the same tan
  (limestone → pine): the floor-mottle law, at props. `docs/OBJECT-PROJECTION-RULING.md`,
  round two.
- **Architecture and conditioning do not exist on the same surface.** BitForge conditions
  (12/12 propagation, §5.5) and produced architecture **0/100**; tiles-pro produces clean parts
  (0 mechanical culls in 114) and refuses style conditioning on connectable features. **Any
  pipeline needing both composes across surfaces.**
- **tiles-pro is a parts supplier, not an instrument.** Promoted to the stock role on the
  audit's evidence; failed as a standalone wall instrument (**0/114** two-plane).
- **The wall road is composition.** Six-for-six on relationship defects (the composition spike);
  generation supplies materials and parts only.
- **All three camera parameters are spent.** `tile_view` is a silent no-op; `tile_view_angle` and
  `building_wall_angle` are live but reach only the front elevation; `tile_depth_ratio` extrudes
  thickness downward. **No parameter adds a plane that was never painted.**
- **Nothing on this platform is seed-reproducible** — measured on every surface tried. The ledger
  therefore stores **images, not parameters**, and that is process law rather than preference
  (§6.4's evidence note says the same thing from the other end: a parameter row is not evidence).
- **A world-keyed treatment cannot register against runtime-keyed structure.** The deflection
  theorem, below — the corner theorem's twin, and it is likewise permanent.

**THE DEFLECTION THEOREM — RULED (Rafe, 2026-09-03), and it is a PLATFORM THEOREM.**

*A treatment keyed to world position cannot register against structure keyed at runtime. Any
attempt to make it do so reintroduces the lattice.*

The proof is the corner theorem's, run in the other direction. A crack, a stain, a scatter of
debris — anything that must look the same to every tile it crosses — has to be a **pure function
of world position**; that is what makes each tile compute the identical mark, and it is the same
discipline §8.3.3 imposes on the stones. But the **bond is not a world function**: a tile's course
splits, drop pattern and stone origins come from the atlas **variant the map picks for that cell at
runtime**. So the two are addressed in different spaces, and a treatment that consulted the real
joints would compute a different mark depending on which tile drew it — with the disagreement
landing exactly on the tile boundaries. **The fix for one defect would draw §8.3.1's grid tell.**

The occasion was a frame critic asking, correctly, for cracks that deflect at joints — *"a crack
that crosses six slabs in one smooth curve without registering a single joint reads as a line drawn
over the floor, not damage in it."* The note is right about the picture and unbuildable as stated.

**THE SUBSTITUTE IS LOCAL, AND IT STANDS.** What a world-keyed treatment *may* consult is
information local to the pixel it is painting, which every painter already holds and which both
tiles either side of a boundary already agree about by edge-family construction. For the crack
that is `CRACK_SPALL`: where it passes through a joint the arrises either side break away — wide
at the bond, narrow across the slab, which is how a fracture actually crosses one. It registers
the joint at the place the critic said nothing registered it, without ever asking where the joints
are.

**DO NOT REOPEN DEFLECTION** — for cracks or for any successor treatment — without first changing
the constraint that forbids it. §8.3.3's mitigation is the same shape and is the one that remains:
a mark may not *consult* the bond, but incident may sit *over* it, world-placed.

Sources: PRs #142 (probe 6.4 surface audit), #144 (wall gauntlet), #145 (tiles-pro audit), #146
(composition spike); `c3d05980` (the floor consolidation pass, deflection theorem).

---

### 13.9 The representable floor — LAW (2026-08-29)

§13.8 rules that **a signal authored below the perceptual floor is absent**, and it is measured on
the source. This clause is its sibling and it is measured on the delivered frame:

> **A signal is absent if the rig does not deliver enough of it to be represented.** Everything
> authored must clear the perceptual floor *at the illumination it is actually seen under*, not at
> the illumination of the source file.

The difference is not academic. The tier-one floor's palette is a nine-rung ladder spanning 48.6
to 154.4, and its path was authored on it in three separate channels. Measured on the shipped
capture, at the tiles the traffic field calls busiest:

| illumination | the whole nine-rung ladder becomes | the full chroma rotation moves |
|---|---|---|
| full | 8-bit 49 … 154 | rgb(115,115,114) → rgb(103,122,113), **ΔE 10.12** |
| mean trodden tile (24.3/255) | 8-bit 5 … 15 | rgb(11,11,11) → rgb(10,12,11), **ΔE 1.06** |
| **the busiest tile (7.1/255)** | **8-bit 1 … 4** | **rgb(3,3,3) → rgb(3,3,3), ΔE 0.00** |

At the one tile the level's own graph says is walked most, the entire palette collapses into four
8-bit values and a colour rotation worth ten ΔE at full light **rounds to nothing at all**. The
signal is not faint there. It does not exist: it was quantised out of the frame before any eye,
instrument or seat was involved.

**87% of that scene's laid floor sits below luminance 70**, and its trodden tiles are *darker* than
its off-route tiles — mean 24.3 against 30.7 — because corridors are where traffic concentrates
and corridors are where the lamp is not.

**Consequences, and they bind every future round:**

1. **An instrument that measures the source has not measured the asset.** Report the delivered
   amplitude at the illumination the thing is seen under, or report nothing.
2. **A channel cannot be ruled impossible from one scene.** What failed above is *this* rig
   delivering *this* scene's route; the same lever at full light is worth ΔE 10.12, which is well
   clear of any floor. Name the illumination in the ruling or the ruling is wrong.
3. **Where a signal must read, either the light must reach it or the signal must not be surface
   material.** Value, texture and colour are all multiplied by the same falloff. No amount of
   authoring beats a multiplication by 0.03.

---

### 13.10 A measurement that convicts a witness needs the witness's proof standard — LAW (Rafe, 2026-09-02)

**Instruments derive ground truth from the engine's own probes and refuse on disagreement. They
never assume it.**

The law is banked because an assumption did the convicting. Three blind seats reported, in
confident and specific terms, what they saw on the floor; an instrument said they had described
ground the route does not touch, and their findings were struck on that basis across three rounds.
The instrument was computing a tile's screen position as `(H − rows·tile) // 2` — the field,
centred. **The camera follows the player.** Measured origins on three stations of the same scene:
`(−9, 34)`, `(−137, −158)`, `(−137, 34)`, where the assumption returned `(−169, −5)` for all three.

Round 24's seat placed the corridor at x 505–565. The engine places its mouth at x 535. **The seat
was right and the instrument contradicting it was wrong**, and the seat spent two rounds discredited
for it.

**What the law requires:**

1. **Ground truth comes from the engine.** Every capture's log already prints a tile and the pixel
   it landed on — that is the camera's own answer, and it is the only admissible one.
   `measure_traffic_read.tile_origin` reads it and **refuses outright if a log's probe points
   disagree with each other**, rather than averaging them into a plausible wrong number.
2. **An assumption is removed, not corrected.** This one survived nine rounds inside three separate
   instruments. Looking harder at it was never going to catch it; only deleting the code path did.
3. **The asymmetry is deliberate.** A seat's claim is checked against the engine before it is
   doubted, and an instrument's claim is checked against the engine before it is believed. The
   party with more to lose from being wrong is the one making the accusation.

> **The general form:** the standard of proof required to overturn a witness is at least the
> standard that witness was held to. An instrument that says a seat did not see what it says it saw
> is making the stronger claim and carries the heavier burden.

---

### 13.11 An instrument whose reference can saturate measures the ceiling, not the scene — LAW (Rafe, 2026-09-07)

**A relative bound is only as honest as its denominator.** If the reference can clip, the
instrument stops reporting the scene and starts reporting the top of the range — and it does so
silently, because a saturated reference looks like a very bright reference.

**The occasion.** The floor-legibility guard asked whether a declared point was dark *relative to
lit floor beside the player*. That reference cell **clipped at 255**. So every dark declaration in
the scene was a ratio against a pinned value, and the guard had been measuring the ceiling since
the day it was written.

It surfaced when the highlight shoulder (§6.2) removed the clipping. Isolated:

| | change |
|---|---:|
| the declared-dark point `(8,7)` | **+0.00 levels** — byte-identical |
| its reference | **−26.95 levels** |

**A fix that changed zero dark pixels made two dark declarations fail.** The guard was not wrong
about arithmetic; it was wrong about what it was dividing by. And the defect had a second face
nobody had looked for: a relative bound is **blind to global darkening**, because dimming the
whole scene moves numerator and denominator together. Measured at energy 0.15, two declared-lit
points sat at 0.0895 and 0.0981 delivered — too dark to see — while their *ratios* read **0.6651
and 0.7285**, comfortably passing a 0.12 bound.

> **RULED: legibility is an ABSOLUTE delivered-luminance bound, per point, per §13.8.** The
> question is *can a viewer see this point*, which is a property of the delivered frame and of
> nothing else in it. Declared as `bound_lum` in the scene spec, **with no default** — a bound
> that can be omitted is a bound that drifts, and the retired one spent its life measuring against
> a number nobody had declared.

**How a live instrument is replaced without laundering a re-tune.** The bounds were derived as
`reference_on_the_nulled_build_at_the_ratified_rig × the retired ratio`, which is algebraically
the old test with its denominator frozen at a known-good moment. **Every point's pass state was
preserved exactly on the day of the swap.** That is the discipline: change what the instrument
MEANS without changing what it SAYS, or nobody can tell a re-definition from a re-tuning.

**And it was proved in both directions before it was believed** (§13.5): the shouldered capture,
whose dark ground is byte-identical, **passes**; a genuinely darkened capture — Weber 0.71 against
§13.8's 0.1440 floor — **is refused**.

> ### THE SECOND AND THIRD INSTANCES, both on 2026-09-07, both in the review layer
>
> **SECOND — the progress metric.** `rank_score` is `(deck_size − position) / (deck_size − 1)`, so
> first place in a three-frame deck is **1.00 with nothing above it.** The stall guard demanded a
> NEW best and counted matching as standing still, so **any lane that ever ranked first was
> guaranteed to stop three readable rounds later, however good the work was.** RULED: progress at
> the ceiling is `(rank_score, shipped, −unresolved_flips)` — SHIP arriving counts at any rank, and
> at equal rank strictly fewer *unresolved* flips counts. And **a round excluded from a guard's
> evaluation cannot set that guard's best**; cleared rounds do not hold records.
>
> **THIRD — the build identifier.** `build_id` hashed every tracked and untracked file in the
> repository, so **acting on the gate's own ruling invalidated the verdict that ruling was about**:
> the human gate dispositioned every flip, and the install refused because implementing the ruling
> had edited `frame_critic.py` and this document. The delivered frame was byte-identical. RULED:
> the id hashes **shipped inputs only** — game source, assets, shaders, scene and theme configs,
> and the build scripts that affect output — and excludes the review layer's source, `.claude/skills`
> and `docs/`, on the ground the exclusion list already stated for the review layer's *artefacts*:
> **they describe the build, they are not in it.**
>
> > **A hash broader than the thing it identifies measures the repo, not the build.**
>
> ⚠ **The blacklist is deliberate and the direction of failure is chosen.** "Shipped inputs only"
> is whitelist language, and a whitelist that forgets a shipped directory yields an id that does
> **not** move when the build does — the gate goes blind and says nothing. A blacklist that forgets
> a non-shipped one yields an id that moves needlessly: loud, visible, one line to fix. Anything
> new in the repository counts until someone names it.
>
> **All three instances are the same shape**, and it is worth stating once: an instrument's INPUT
> must be no wider than the thing it claims to measure. A ratio whose denominator can saturate, a
> progress metric whose scale can top out, an identifier that hashes the room the build was made in
> — each keeps returning a confident number after it has stopped measuring the subject.

> **The general form:** before trusting a ratio, ask what happens to it when the denominator hits
> the end of its range. If the answer is *it keeps returning a number*, the instrument has a blind
> spot exactly where the picture is brightest, and that is where the eye is.

### 13.12 An assertion DERIVES the property; it never copies the value — LAW (Rafe, 2026-09-08)

**A check that copies a value is a snapshot of a conclusion.** It stops tracking the thing it was
written to protect the moment anything upstream moves, and — this is the part that costs rounds —
**it goes on asserting.** A copied constant cannot notice that its premises have changed, so it
fails in the worst available direction: confidently, silently, and in whichever direction the
drift happens to point.

**The occasion.** The device gate's ruled-fix registry carried

```
{"id": "lane-gain-stepped", "rule": "wear modulates the same stones", "check": "const:POLISH_LANE_GAIN==0.6"}
```

0.6 was the value that satisfied that rule the day it was pinned. Then #174 corrected the lamp and
Ruling 56 was re-ratified, the lane window moved underneath it (§6.2's re-derivation rule), and
**0.6 stopped satisfying the rule it was pinned for** — on-lane masonry 0.1338, *below* §13.8's
0.1440 floor. On 2026-09-08 the registry was simultaneously

- **blocking** a build that met the law, and
- **asserting** a value that no longer did.

The successor measures the law on the delivered frame: *on-lane identity and lane-vs-flank both at
or above the perceptual floor*. Proved in both directions on real captures, which is the whole
point — **the new assertion refuses the exact value the old one required:**

| build | on-lane | lane-vs-flank | derived check |
|---|---:|---:|---|
| lane 0.3 (ratified) | 0.1513 | 0.3456 | **passes** |
| lane 0.6 (the old pin's value) | **0.1338** | 0.3939 | **refuses** |

**THE THIRD INSTANCE, and the family is what makes it a law rather than an anecdote.**

| | what was copied | what should have been derived |
|---|---|---|
| **the working ladder** (§5.6) | a manifest's stored rungs, trusted by consumers | the rungs, re-derived from the donors' percentiles at every read — *"a manifest written under an older rule cannot silently keep it"* |
| **the shelter weights** | the weight tuple, as though it were the signal | the delivered **modal joint contrast**, which the tuple only influences — the weights looked fine while the mode sat at 0.107 Weber, under §13.8's floor, and the frame critic found it before the number did |
| **the lane-gain pin** (here) | `POLISH_LANE_GAIN == 0.6` | the lane **window**, measured on the build |

Each is the same shape: a *value* standing in for a *property*, holding correctly right up until
the relationship between them moved.

> **Write the assertion against the property the rule is about, and measure it on the artefact.
> If the check cannot be expressed that way, the rule is not yet understood well enough to gate
> on.**

⚠ **THE BOUNDARY — RATIFIED (Rafe, 2026-09-08):** *"§13.12 licenses deriving the measurement,
never the bar; re-deriving §13.8 per capture is §13.11 in disguise."*

The floor a derived property is compared against is **ruled and stays ruled**. §13.8's 0.1440 is
not re-derived per capture — a bar that moves with the artefact it judges is the saturating
comparator of §13.11 wearing this clause's name, and it would pass everything. **Derive the
measurement; never derive the bar.**

The two laws are therefore a pair and are read together: §13.12 says *measure the property on the
build*, §13.11 says *against a reference that cannot move with it*. Either one alone is a way to
build a check that always agrees with whoever wrote it.

### 13.13 A gate's binding term must have a MEASURED NOISE FLOOR, and must never be a single sample — LAW (Rafe, 2026-09-08)

**The occasion, and it is one frame.** On the morning of 2026-09-08 `PASS-INSTALL` was ratified:
a polish round passes when it *ranks above `approved_capture`*. That afternoon lane
`polish-c-183` judged **the same build twice** — sha `839fb12f`, with the round's own
no-change measure reading `picture moved mean 0.000 / worst 0` between them:

| round | build rank | reference rank | verdict under the new rule |
|---|---|---|---|
| r001 | **1 of 4** | 2 | PASS-INSTALL |
| r002 | **2 of 4** | 1 | FAIL |

**The build and the reference swapped places with no pixel changing.** The gate's entire
discriminator was a single draw from a distribution nobody had measured.

**Why the previous rule had not exposed it.** SHIP∧rank was ratified against a null reference, and
rank only ever *added* a condition to a SHIP-based decision. Making rank the **sole** binding term
moved the whole gate onto the least stable quantity the deck produces — and §4 had already
recorded, three times, that a blind seat's ordering does not reproduce the human gate's. What was
new was that it does not reproduce **itself**.

> **RULED: a term that binds a gate must (a) be sampled more than once, and (b) have its own
> variability measured and published beside it. A threshold on an unmeasured single sample is not
> a gate; it is a coin with a number written on it.**

**The refinement, for this gate:** a majority of **three independent blind seats** rank the build
above `approved_capture`, with **no unrouted flags from any**, each seat drawing its own
axis-matched plant.

**The asymmetry between the two terms is deliberate.** Rank takes a majority because rank is the
noisy term. A **flag does not** — one seat finding a defect is enough, because a flag outvoted 2–1
is still a defect two seats missed. Averaging *findings* would discard the only thing a panel is
good at, while averaging *rankings* is the entire reason it exists.

**And the noise floor is measured rather than assumed** — the same bytes through five seats, the
disagreement rate recorded and published as rank's error bar. That number belongs beside the
threshold wherever the threshold is quoted; a bar without it is a bar nobody can size.

⚠ **THIS IS NOT A LICENCE TO AVERAGE THE PLANT.** Every seat must catch its own. §4 voids a round
on one missed plant, and a panel does not get to dilute that into a proportion: a soft seat's
ballot is exactly what §4 refuses to read. More seats make the plant condition *harder*, never
softer.

⚠ **And a panel is not independence for free.** Where the axis-matched morgue set has one member,
every seat draws the same plant and their catches are **correlated** — the panel multiplies the
rank samples but not the plant's evidence. Reported per round rather than assumed away.

### 13.14 Flag disposition — matched, measured-false, or it goes to the gate — RULED (Rafe, 2026-09-08)

A blind seat's flag is not automatically work. It is one of three things, and **which one must be
checkable by someone other than the person asserting it.**

| state | means | requires | who may assert |
|---|---|---|---|
| `ROUTED-ALREADY` | this is the thing we already decided | **a citation** — an issue (`#nnn`) or a clause (`§x.y`) that RESOLVES | the builder |
| `MEASURED-FALSE` | the stated cause is not what is happening | **the measurement** that disproves it **and the percept, recorded** | the builder |
| `ROUTED` / `CLOSED` / `PARKED` | a new destination, or a decision not to chase | **Rafe's words, quoted** | Rafe only |

**Only new, unmatched flags block — and those go to Rafe.**

**The division of authority is the point.** A builder disposing by citation is *not* routing: it
asserts a **match against a record that already exists**, and the citation is what lets anyone
else look it up and contradict it. An uncited match is an opinion. A routing is the creation of a
new destination, and that stays with the human gate.

**The citation is checked, not trusted** (`critic_gate.check_dispositions`): a cited clause must
exist in the bible or the process law, and a cited issue must appear in the repository's own
record. ⚠ **And the search space is the RECORD, not the repo** — the first implementation grepped
everything, and its own proof caught it: the case asserting that an invented issue number is
refused has to *write that number into the test file*, so the grep found it and the citation
passed. An assertion whose search space includes its own fixtures is §13.11's shape again, an
input wider than the thing it measures.

**`MEASURED-FALSE` keeps the percept, and that is not a formality.** §13.4.1's whole finding is
that a seat's *explanation* fails while its *seeing* stands — three times in one session on this
project. A disposition that discarded the percept along with the explanation would throw away the
observation and keep only the argument, which is exactly backwards.

**Worked, on the round that occasioned it** (`r001-polish-abc-install`, three seats): nine flags
disposed — three measured-false (an off-centre falloff whose two named points were *wall cells*;
a collapsed range measured against the **plant's** blowout rather than the reference; a far-field
value claim off by a factor of 3.3) and six routed-already against #193, #194, §12.1a, §6.2.1 and
§13.4.1 — with **seven left unmatched and handed up**.

---

## 14. Out of scope for this document

**Sound.** Deferred deliberately, not overlooked. Sound has almost no coupling to the decisions
in this bible: it does not constrain how a sprite is drawn and has no one-way doors. Two notes
banked for a future workstream:

- Timing binds to animation frames (§9.4 exists for this reason).
- **The register applies to audio unchanged.** The world does not notice you; nothing is staged.
  The ambience is that of a place that would sound the same if you were not in it. Sasha and
  Hollowmark carry the warmth there too.

**Generation tooling, critic prompts, gate tables, and the screen stack.** Phase 4.

---

## 15. Instrument audit — which clauses can actually be checked

**A law nobody can measure is decorative. This is the honest audit, and at v0 it is mostly
gaps — which is the correct state for a bible whose pilot has not run.**

| Clause                                                 | Instrument                          | Status |
| ------------------------------------------------------ | ----------------------------------- | ---------- |
| §5.1 zero off-palette pixels                           | Palette check, adopted from Gemfall | **Portable, not yet built** |
| §5.2 region slot legality                              | Same check, region-flagged          | **Portable, not yet built** |
| §5.3 warm-share allocation per asset                   | None                                | ⚠ **NO INSTRUMENT.** Purpose stated; threshold may not exist (Ruling 70 applies). |
| §5.4 chroma is signal                                  | None, and none will be built        | ⚠ **NO INSTRUMENT — BY DESIGN.** **The countable proxy is named and refused:** saturated-pixel share is measurable, but share is not signal — a census can count saturation and cannot see meaning, so the number would gate the wrong thing and win trades it hasn't earned (§13.4). If a saturation census is ever built, it is ordering-only under the worst-ground pattern (§13.2), renders no verdict, and earns promotion like any instrument (§13.5). |
| §5.5 reference neutrality (style parent vs prop stock) | None                                | ⚠ **NO INSTRUMENT.** Applied when a reference is chosen, by eye. The 12/12 propagation measurement is the *evidence for the rule*, not a gate over candidates — a "composition similarity" score would be exactly the weak proxy §13.4 refuses. |
| §6.3 receive-light (no baked highlight)                | Blind LLM census, plant-controlled   | **BUILT AND PROMOTED, NARROWLY.** `tools/pixellab/probe_6_4/blind_census.py`. A blind read, not a metric (§13.4 v0.3: a script emitting a number is an instrument; a critic rendering a verdict is not). Passed its control **10/10** against *constructed* plants — ground truth taken from the prompt would have been circular. **Licensed for KEY vs not-KEY only, at plant strength.** The FORM-vs-FLAT boundary carries no control and is reported unlicensed, because a donor tile that is already near-flat yields a "flat plant" a truthful eye may correctly read as FORM, and tuning plants until that stopped would manufacture an instrument that cannot fail. |
| §6.3 occlusion, not illumination (no encoded light direction) | Differencing check, light-off      | **BUILT AND PROMOTED, ONE AXIS.** Authored form must survive the engine light being switched off; a per-block top-bright/bottom-dark emboss fails the diff. Demonstrated its fail on the round-8 plant before any pass was counted (§13.5). **Licensed for encoded-light-direction only** — it says nothing about whether the form that survives is any good, which stays eye-side (§13.4). **AND IT HAS NOW CAUGHT A REAL ARM, not only a plant (2026-08-27):** the composition spike's `before` arm measures unlit face÷top **1.22** — face brighter than top, no authored plane separation at all — so the plane structure eight rounds were measured against was the engine's light, not the art. That is the arm its own round-8 seat ranked first of five. An instrument's first real catch is worth more than its control, and this one overturned an inference rather than a candidate. |
| §6.5 the value stack (floor between the planes) | Plane-ratio measurement, `tools/sighted_round/checks.py` | **BUILT, AND HONESTLY NARROW.** Reads top/floor and face/top off a lit capture and off an unlit one. It measures whether the ratios are *present*, which is real and was the finding; it does **not** measure whether they read as planes to an eye — rounds 1 and 2 had the ratios broadly right and were culled `wrong-projection` for §3.1, which no ratio detects. **Licensed for ratio-presence only.** Its delivered numbers are also rig-coupled (§6.2's flag), so a pass is a pass *on this rig*. |
| §6.3 no baked drop shadows                             | None                                | ⚠ **NO INSTRUMENT.** Joins the directional-highlight census as owed; same status, same caution — Gemfall's analogue measured as a blunt proxy and was refused a verdict. |
| §7.1 everything is held                                | None, and none will be built        | ⚠ **NO INSTRUMENT — BY DESIGN (§13.4).** Eye-side, at the gate. |
| §7.4 heraldic stance (idle sprites are icons)          | None, and none will be built        | ⚠ **NO INSTRUMENT — BY DESIGN (§13.4).** Blind critic eye + human gate. |
| §8.1 wear explained by traffic and indifference        | None, and none will be built        | ⚠ **NO INSTRUMENT — BY DESIGN (§13.4).** |
| §8.3 the motif trap — no incident baked into a base tile | **Split, and the split is the point** | ⚠ **HALF INSTRUMENTABLE, HALF NEVER.** *Verbatim repetition* is trivially checkable and should be — a field laid from one tile is a byte comparison, and the check would have gone red on every floor round to date. **Whether a mark is material or incident is NOT**, and no proxy for it will be built: it is the difference between a joint between two stones and a crack through one, which is a reading, not a measurement (§13.4). Build the cheap half, refuse the other, and never let the cheap half's green stand in for the whole clause. |
| §12.1 ring judged as laid (scale rule)                 | None at tile scale — structurally    | ⚠ **NO INSTRUMENT, AND THE EXISTING ONE IS DISQUALIFIED BY INPUT.** `ring_instrument.py` reads one 32×32 tile; the property lives at field scale, so no threshold reaches it. Recorded because this is the audit's sharpest lesson to date: **an instrument can be correct, controlled, and still be answering at the wrong scale** — and its label now says so (`REPORT.md` §6). |
| §10.1 attachment-point tolerance across frames         | Buildable and should be built       | **Owed at the Sasha tier.** The one genuinely instrumentable register-adjacent clause in this document. |
| §12 value separation from surface beneath              | None                                | ⚠ **NO INSTRUMENT.** Gemfall's Ruling 70 found no defensible threshold for their analogue; expect the same and prefer the refusal. |
| §12 names itself at 1×                                 | The human gate (§13.2)              | **Eye-side by design.** |
| §1.1 zero expression budget (world creatures faceless) | None, and none will be built        | ⚠ **NO INSTRUMENT — BY DESIGN (§13.4).** Blind critic eye + human gate. |
| §1 register conformance, all clauses                   | None, and none will be built        | ⚠ **NO INSTRUMENT — BY DESIGN (§13.4).** |

**Fifteen of nineteen clauses have no working instrument today. None is papered over. Eight of
them will never have one, deliberately, and that is a decision rather than a gap.**

**One row goes GREEN on evidence at v0.11 and one existing row earns its keep — the first
revision where the audit moved in the project's favour without a new instrument being built.**
§6.5 arrives with a real measurement behind it and a narrow licence. The §6.3 differencing check
made its **first catch on a real arm** rather than a plant, and what it caught was an inference
that eight rounds had rested on. **The caution attached to both: a ratio check cannot see §3.1** —
rounds 1 and 2 had the values broadly right and were culled `wrong-projection` anyway.

**Two rows are new at v0.10, and both are red — which is the audit working in the direction that
costs something.** §8.3 arrives as law with its instrument **split**: build the byte-comparison
half, refuse the material-vs-incident half outright. §12.1's scale row is worse than a gap and is
recorded as such: an instrument that exists, passed its controls, and is **disqualified by its
input** — it reads one tile and the property is in the field. *An instrument can be correct,
controlled, and answering at the wrong scale*, and nothing in §13.5's promotion procedure catches
that, because a positive control built at tile scale confirms tile-scale behaviour perfectly.
**§13.5 gains an implied question a future revision should make explicit: at what scale does this
control prove anything?**

**Two rows were new at v0.8, and they move in opposite directions — which is the audit working.**
§6.3 gained a second instrument, the light-off differencing check, promoted on a demonstrated
fail and licensed to one axis; §5.5 arrived as law with **no** instrument and the countable proxy
named and refused in the row. A revision that only ever adds green rows is not auditing itself.

**One row moved at v0.7, and only one.** §6.3 gained a plant-controlled blind census —
the *first* instrument on this track to demonstrate it can fail before its passes were counted
(§13.5). It is deliberately not a script emitting a number, and its licence is narrow: one axis,
at plant strength. The unlicensed half is reported as unlicensed rather than quietly folded in,
which is the whole of §13.4 working as intended.

---

## 16. Banked observations — NOT LEGISLATED

Recorded so they are not re-derived; deliberately not law.

- **Layered portraits.** The asset bar composes portraits from separate base/hair/hood/feature
  layers — §10.2's additive philosophy applied to faces, and the one place the library permits
  expression. If the dialogue system ever wants NPC portraits (Borrek, Hael, the Under-Warden's
  office), the pattern is solved and composes with §1.1's expression rule: faces live at
  dialogue range, never at sprite range. Owned by the Spine thread if ever scoped.
- **Hue-coded item states.** Monochrome accent variants (the bar's gold/cyan/magenta weapon
  rows) are chroma-as-signal at item scale and would give identification/curse/mention
  mechanics a free visual vocabulary. Game-design surface, not art law. Owned by Combat/Spine
  if ever scoped.
- **Plant stock catalogued.** The bar's cobwebbed crypt corners and staged-horror compositions
  (blood pools, skull altars) are register-illegal in Yarl (§1, §8.1) and therefore ideal
  critic plants: professionally drawn, wrong register — the exact failure a soft critic waves
  through. Already sanctioned in §13.3; noted here so the specific sheets are remembered.

---

*Revision history:*

- *v0.15 — 2026-09-12. **§12.1a's occluder pass BUILT AND MEASURED** — the void is dark by
  occlusion, faces and caps untouched (40.16 → 39.61, 52.92 → 52.92), unexcavated mass at ambient
  (27.35 → 12.50), ring 0. The mechanism the clause imagined (an occluder behind the reveal) was
  measured and could not work in a 2D light; the first surface is exempt by light mask instead
  and everything behind it receives. Objects cast from their §3.2 footprints and never
  self-shadow (Δ 0.00). Godot's ShadowColor semantics recorded (rgb leaks, alpha inert). The orc
  fire is the second light (#205). Walk pending: softness, darkness, flicker are Rafe's.*

- *v0.14 — 2026-09-12. **§3.2 — OBJECT PROJECTION RULED.** Cabinet oblique, receding RIGHT,
  ½ depth (per-axis; the walked `projOdeep` build), on device across two rounds
  (`docs/OBJECT-PROJECTION-RULING.md`). §3's head clause now separates walls (two planes,
  ratified 2026-09-09) from objects (three), and records that the old joint wording was ratified
  for walls only. True isometric and flat front+top rejected — *"if we're doing dynamic lighting
  it should count for something."* Round objects excepted: true-circle top, vertical body.
  Characters unaffected. Wall ends/corners/pillars gain an east face at ½ depth — bounded
  follow-up. Props-not-against-walls filed as placement, not projection. New §13.7 platform
  fact: generation cannot be told a projection; geometry is authored (`projection_mesh.py`),
  generation supplies surface.*

- *v0.13 — 2026-09-03. **The deflection theorem — the corner theorem's twin, recorded in §13.7.**
  Ruled a PLATFORM THEOREM at the gate: a treatment keyed to WORLD position cannot register
  against structure keyed at RUNTIME, and any attempt to make it reintroduces the lattice. The
  proof is §8.3.3's, run backwards — a mark that must look the same to every tile it crosses has
  to be a pure function of world position, while the bond comes from the atlas variant the map
  picks at runtime, so a mark that consulted the real joints would disagree with itself exactly
  on the tile boundaries. Occasioned by a frame critic asking, correctly and unbuildably, for
  cracks that deflect at joints. **The substitute is local and stands:** `CRACK_SPALL` breaks the
  arrises where a crack crosses a joint, registering the joint without ever asking where it is.
  Deflection is not to be reopened without changing the constraint. §8.3.3 gains a pointer to
  its twin. See `docs/FLOOR-CONSOLIDATION-PASS.md` and LOOP-PROCESS §4.3.*

- *v0.12 — 2026-08-27. **The sighted round's device gate: FAIL, and two laws come out of it.**
  Rafe walked the rounds-4/5 build on the phone and overruled the stills. **§3's status trail
  gains the gate entry** — §3 is neither ratified nor rejected and **rides PROVISIONAL into tier
  one**, carrying the recipe, §3.1 and the Q3 control finding intact; the two entries beneath it
  are kept, because a trail that overwrites itself is not a trail. **New §8.3.1:** the motif trap
  applies to WALL material identically — wall tops are incident-free material, and boundary rules
  and edge ticks are pattern and are out. This culls the 16 px joint grid §3.1's own round built
  four hours earlier, and the clause generalises past both asset classes: *any treatment applied
  at a constant position within a tile becomes a lattice when tiled.* **New §6.2.1, a TIER-ONE
  PRECONDITION:** the rig's radius, falloff and ambient get a readability-tuning pass **before any
  asset is judged through them** — the value stack must be legible at gameplay distance, not at
  two tiles; tune the one table of numbers rather than every asset in the game. **§13.2 gains its
  sharpest instance**: every instrument upstream of the phone was green — two seats above the bar,
  differencing passed, ring clean, delivered numbers on target — and the gate still said FAIL.
  Instrument agreement raises confidence in the instruments, not in the asset.*
- *v0.11 — 2026-08-27. **The sighted round's rulings land — the first round on this project run
  with sight, under §13.3's origination rule.** **NEW §6.5, THE VALUE STACK**, and it is the wall
  campaign's load-bearing finding: **the floor sits BETWEEN the wall's two planes** — top ≈1.11×
  floor, face ≈0.5–0.6× floor. Register derivation is §6.3 occlusion expressed as a ratio: the
  top catches light, the face is where light cannot easily reach, and enclosure is direction-free
  so the stack is material rather than depiction. Yarl had the relationship **inverted** (face
  brighter than top, both below the floor), which is the whole of the eight-round "no thickness"
  finding — no side face required to explain it. Corrected, two blind seats ranked Yarl **above
  the asset bar** on depth. **NEW §3.1, THE FLAT-TOP RULE:** a top surface is not face material
  re-toned; a plane is made by changing what the texture is a picture of, not its value. Two
  seats culled `wrong-projection` for it independently, and no value change reaches it. **§3's
  premise is VINDICATED by the Q3 control** — the bar carries §3's own limitation, measured by
  two seats unasked ("a vertical wall in B has literally zero thickness"), so the clause was
  never on trial; §3 stays PROVISIONAL and ratification waits on the device gate (§13.1).
  **§6.2 gains a COUPLING FLAG:** the engine compresses the authored ratio by a measured 1.48
  because the player is the lamp, so authored ratios are derived backwards from delivered targets
  on the current rig and **must be re-derived when §6.2's PLACEHOLDER values are ratified** — the
  art-to-rig dependency is named, not solved. **§13.3 records where the bars live** and restates
  that measurements leave while pixels never do. All results are qualified at **4-of-5-seat
  strength**: the plant was waved through once, voiding round 2, which stays void. Evidence:
  `tools/sighted_round/`.*

- *v0.10 — 2026-08-27. **The gate answered the C-GAB question and the answer generalised into
  law. NEW §8.3 — THE MOTIF TRAP:** any incident baked into a tile becomes a motif when tiled;
  repetition converts accident into intent, and the eye reads pattern regardless of the
  incident's quality. Therefore **a tile is the material and the incident is the variant** —
  incident (cracks, wear, marks, the §8.2.1 channel) arrives at the instance level, randomised,
  through the variant and overlay system tier one builds. **§5.5's parent criterion sharpens**
  from *compositionally neutral* to **incident-free**, which strikes its own worked example: the
  crack-through-a-field offered as the shape of a neutral parent is the tile the gate then ruled
  a frame. **§5.5's flagged note is RESOLVED — frame at field scale**; C-GAB retains its
  conditioning role because references never ship, and is recorded as retained under screening
  rather than as meeting the sharpened bar. **§12.1 gains the scale rule:** the ring prohibition
  is scale-dependent as well as value-agnostic — **a ring is judged as laid**, and a single tile,
  or a contact sheet of single tiles, cannot answer the clause. The ring instrument's limit is
  therefore structural, not a tuning failure: it reads one 32×32 tile and the evidence is not in
  its input. **§8.2.1's variant system is re-scoped** as the only place incident is permitted to
  live. Retroactively, this completes the floor-remediation round B finding: those tiles were
  bases judged as finished floors before the incident system existed. Evidence:
  `tools/floor_remediation/REPORT-PARENT-RATE.md` and its `exhibit_cgab/` 3×3 plate — the view
  that could run the continue-or-return test no seat had ever been given.*

- *v0.9 — 2026-08-27. The floor campaign's rulings land. **§5.5 gains its corpus assignment**
  (Rafe): C-GAB primary style parent, A-HEB secondary, **A-VAB prop stock regardless of
  surgery** — de-ringing removes a keyline and does not make a framed plaque neutral, and it is
  the composition that propagates — and **B-KAB retired from conditioning with no remediation**,
  its regenerated candidate not promoted. §5.5 also gains a **cross-confirmation**: a blind seat
  on the floor campaign, never given this bible, rediscovered the clause's own composition
  finding by culling A-VAB as "a framed plaque" — the wall campaign's "recessed frame" reached
  independently, in a different medium. And **its 2026-08-26 measurement is corrected**: the
  ring was recorded in luminance ("~3px near-black, B-KAB at 14 against a median of 130") by an
  instrument that thresholded value at 0.30× the median; measured on geometry instead, **two of
  the four carry rings, not one** — A-VAB's two 1px loops sit at 0.48× and were invisible to it,
  while A-HEB and C-GAB never carried a ring at all. **§8.2.1 gains the tier-one requirements**
  the same seat produced — a variant system, the wear system this clause already specifies, and
  a floor-repair vocabulary — banked as requirements rather than as a verdict on the corpus,
  with the standing note that a one-wide corridor cannot pose the §8.2 question and this round's
  scene did not meet §8.2.1's own four-scene rule. Instrument, controls, verbatim seat
  transcripts: `tools/floor_remediation/`.*
- *v0.8 — 2026-08-27. The wall campaign's rulings land. **§6.3 gains the occlusion law** —
  occlusion, not illumination, stated as a vocabulary — with the **vocabulary-collision record**
  (a 1px chamfer request and a depth parameter manufactured the same violation on two different
  surfaces: a property of the scale, not of any tool) and the **light-off differencing check**
  promoted per §13.5 with its demonstrated fail on the record, one axis only. **§12.1** gains the
  mandatory half: a wall-top meeting floor without its occluded edge is a missing plane, not
  purity — construction grammar, not outline (the value-agnostic ruling and the pale-ring worked
  example landed with the spike and are unchanged here). **§3 is PROVISIONAL under active test**
  rather than reopened: three confounds named against the spike's evidence, and the sighted
  round's criterion declared before it runs, with the licensing fallback named as a last resort.
  **New §5.5:** composition propagates with material at 12/12, so reference neutrality is a
  criterion — neutral references are style parents, charactered ones are prop stock — carrying
  the corpus note that nothing conditions on an un-remediated §6.4 survivor. **New §13.7:**
  platform facts recorded once, so no future session re-buys them with generations. §15 gains
  two rows, one instrumented and one deliberately not. Sources: PRs #142–#146 and the
  design-thread rulings of 2026-08-25/26.*

- *v0.7 — 2026-08-26. **§6.3 RATIFIED** (Rafe, STOP 2, on the reference device): receive-light
  survives its probe and the clause is no longer provisional. §6.4 closed and preserved as
  authored, with the outcome appended rather than the criterion rewritten — a bar edited after
  the answer is visible cannot demonstrate it was declared before it. **The ratification is
  recorded narrowly:** Stage 1's positive control failed, no arm produced a baked key light, so
  this ratifies the treatment under light and is not a victory over a baked arm. Retirement
  triggers named. §15's §6.3 row moves from NO INSTRUMENT to a plant-controlled blind census —
  the first instrument on this track to demonstrate it can fail — licensed for one axis at plant
  strength, with the unlicensed half reported as unlicensed. Count 13→12.*

- *v0.6 — 2026-08-25. §15 audit catches up with the v0.5 law: four rows added (three
  BY-DESIGN, shadows census owed), counts corrected; the chroma share-proxy is named and
  refused in the row. §9.1 scope line rewritten — the four states are the set, additions
  displace. §13.3 gains the origination rule: the bar occasions, the register justifies.
  All three from the v0.5 post-merge review.*
- *v0.5 — 2026-08-25. The asset-bar study pass, both halves. §9 gains frame arithmetic
  (PROVISIONAL pending source-file count) and Sasha's 6–8 frame budget; §1.1 the zero
  expression budget; §7.4 the heraldic stance rule; §5.4 generalised to chroma-is-signal at all
  scales; §4.2/§3 corroborations recorded; §6.3 extended to baked shadows; §10.3 corroborated
  with the narrower-families caution; §16 added for banked non-law. Sourced from study of
  licensed sheets in chat; no Oryx pixel enters any pipeline.*
- *v0.4 — 2026-08-25. **§8.2.1 added** (Rafe): the trodden channel as the primary legible-wear
  grammar; one-wide corridors are trodden or neglected, both drawable; review scenes must pose
  the question in four contexts. **§13.3 restructured** (Rafe): three assigned bars — SPD
  structure, Rogue Wizards scene, Oryx Ultimate asset — with the presentation caveat and the
  bar-never-in-a-flip-list guard as LAW. Both from rulings taken in conversation 2026-08-25.*
- *v0.3 — 2026-08-24. **§12.1 RULED** (Rafe): no baked outline anywhere; separation by value
  floor and §7.1 linear elements. Engine-side rim parked as an unadopted fallback.
  `outline_repair.py` retired. **§13.4 amended** (Rafe): a script emitting a number is an
  instrument and enters the optimisation; a blind LLM critic rendering a prose verdict is not.
  Two gates — the critic gates the loop, the human gates the landing. No script ever scores
  register.*
- *v0.2 — 2026-08-24. **§7.3 RULED** (Rafe): Unshriven construction is competent but tough —
  strength only, nothing present for appearance. **New §6.4:** receive-light demoted from
  committed to PROVISIONAL, with a three-arm probe (baked control / no-key-light /
  flat), a positive control, and a kill criterion declared before the probe runs. The named
  risk is that generation models default to depicting light, which would make §6.3 the
  external-corpus trap in new clothes. Probe runs BEFORE the Phase 5 pilot, because §6.3 is a
  one-way door.*
- *v0.1 — 2026-08-24. Initial draft. Written before any pixel work, from Phase 1–3 decisions
  taken in conversation. §1 register locked at Phase 1 sign-off. §2 scope locked to demo-first
  (Boundary, floors 1–5). §3 projection provisional pending pilot. §5–§10 decided in Phase 3
  conversation; all numeric values PLACEHOLDER. §13 acceptance adopted from Gemfall's
  LOOP-PROCESS with §13.4 added — register clauses eye-side, no proxies — as the specific
  correction for the previous track's failure. §15 instrument audit honest at nine of ten
  clauses uninstrumented.*
- *Predecessor: the Oryx-conformance track (closed 2026-08, concluded rather than failed).
  Findings banked in §1.3 and §3. The two-plane perspective rule and "names itself at 1×"
  survive on their own merits and are re-adopted here, not inherited.*
