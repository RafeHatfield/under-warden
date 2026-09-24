# Round 30 — the combine un-parks on the re-ratified lamp

**Branch** `art/one-lamp` · **Predecessor** round 29 (#174, one lamp) and round 28 (PR #177, parked
composed-and-holding) · **Gate** the full frame-critic round on the recomposed build, whose PASS
seeds `approved_capture` — the room walk.

---

## 0. What un-parked, and on what

Round 28's combined build was parked **composed and holding, blocked on #174** — the first
occupant of that state. Both conditions the park named are now met:

1. **#174 landed.** The floor's diffuse honours `LIGHT_ENERGY` and the specular's `delivered`
   scalar is built from `LIGHT_COLOR.a * LIGHT_ENERGY`, so both planes are lit by one arithmetic.
2. **Ruling 56 re-ratified** (Rafe, 2026-09-06) on that corrected lamp, walked on device with an
   energy knob for the first time: **radius 5.0 → 6.0, ambient 0.70 → 1.50**, energy 1.6 and
   falloff 1.00 held.

**The park earned its keep.** Its rule — *the resumer re-does rather than re-reads* — fired twice:
once when the arithmetic was corrected, and again when the rig moved under those corrections four
hours later. The second pass invalidated numbers that had been measured correctly **that same
day**. A park called "finalised" would have shipped them into §6.5.

---

## 1. The rig, and what re-deriving against it cost

| | Ruling 56 (2026-08-28) | re-ratified (2026-09-06) |
|---|---:|---:|
| radius | 5.0 tiles | **6.0** |
| falloff | 1.00 | **1.00** (held) |
| ambient level | 0.70 → `#121218` | **1.50 → `#272733`** |
| energy | 1.6 | **1.6** (held — and reachable for the first time) |

⚠ **Ambient went UP, and §6.2.1's third bullet is engaged rather than breached.** The clause asks
the pass to preserve §6.2's arc — *you begin as the only thing here that burns* — and the previous
pass could cite ambient moving *down* as evidence. This one puts it above even the pre-Ruling-56
value. The arc is a register claim, carried eye-side and never instrumented (§13.4), and the gate
that owns it ruled here. **Measured, so the note is not only rhetoric: 33 of 81 in-view floor
cells still sit under the dark bound.** The room is not flooded.

### The delivered reach moved, and it is the figure everything else hangs off

Ruling 56 recorded that **nominal radius is not delivered reach** and that future ratifications
state the delivered one. On the re-ratified rig, floor luminance as a fraction of the nearest lit
floor cell:

| distance | ratio |
|---:|---:|
| 2.0 tiles | 0.590 |
| 3.2 | 0.336 |
| 4.0 | 0.229 |
| 5.0 | 0.118 |
| **5.4** | **0.091** |
| 5.8 | 0.080 |
| 6.0 — the nominal radius | 0.079 |
| beyond | ~0.075, the ambient plateau |

> **Delivered reach ≈ 5.2–5.5 tiles**, against the old rig's ≈ 4. Nominal 6.0 still overstates it,
> exactly as 5.0 overstated 4 — the gap is a property of the falloff, not of the number chosen.

### §6.5 re-derived, twice in one day

Every figure round 29 re-took on the corrected lamp was correct for radius 5.0 / ambient 0.70 and
is stale. Re-measured on the ratified rig, **not adjusted**:

| band | floor (r29) | **floor NEW** | wall (r29) | **wall NEW** | f/w (r29) | **f/w NEW** |
|---|---:|---:|---:|---:|---:|---:|
| ≤2 tiles | 152.34 | **168.73** | 58.40 | **66.51** | 2.609 | **2.537** |
| 2–4 | 57.33 | **87.58** | 20.15 | **31.41** | 2.846 | **2.788** |
| >4 | 9.00 | **22.61** | 5.05 | **11.28** | 1.781 | **2.004** |

**Worst cell in every band**, because a mean hides the cell that decides whether a band reads:

| band | floor min / max | wall min / max |
|---|---|---|
| ≤2 | 110.40 / 214.38 | 14.14 / 93.13 |
| 2–4 | **44.87** / 156.21 | **6.05** / 76.03 |
| >4 | 13.54 / 47.34 | **2.03** / 39.82 |

### The cap against the floor — and one bar changed state

| band | n | L(cap, floor) r28 | r29 | **NEW** | vs the 8-level bar |
|---|---:|---:|---:|---:|---|
| standing ≤2 | 3 | 50.22 | 72.56 | **89.64** | CLEARS |
| 3–4 tiles | 19 | 16.88 | 17.79 | **34.82** | CLEARS |
| beyond 4 | 35 | 4.26 | 4.27 | **10.33** | **CLEARS — it did not before** |

> **The wider pool made wall mass read at range.** *"Beyond four tiles"* has been under the
> perceptual-floor bar in every measurement this project has taken; on this rig it clears at 10.33
> levels. That is the re-ratification paying for itself on a number nobody tuned for.

Sign is negative throughout: the cap is **darker** than the floor. ⚠ **§6.5 row 1 wants the wall
top LIGHTER than the floor, and it is further from that than ever** — the floor gained more than
the cap did at every range. That remains a value-law question and it is Rafe's, at a gate, not an
instrument's. It is **not** ruled here.

---

## 2. The legibility guard refused the first capture, and was right

`tier1_combined_review.json` declares `(11,13)` at 5.1 tiles **expect=dark**, because the old rig's
delivered reach was about four. On the new rig it reads **0.1160** against a 0.1000 bound — it is
legitimately lit, and the guard refused to write the frame.

> **THE BOUND WAS NOT TOUCHED.** Loosening a threshold to pass one's own capture is the failure
> LOOP-PROCESS §4.3 point 1 names, and it would have been the easy move: one number, and the
> refusal disappears.

The **probe moved instead**, to `(11,14)` at 5.4 tiles — ground that is actually past the new
reach — measuring **0.0718**, 28% clear of the bound. The declaration keeps its meaning (*the arc
is part of the distribution and a scene where everything is lit is not the game*); only the cell it
points at moved, and both values and the reasoning are recorded in the declaration's own `why`.

⚠ **FLAGGED: the scene's other dark declaration, `(8,7)`, now passes at 0.0976 against the same
0.1000 bound — 2.4% of margin.** It is marginal on this rig and is the next thing that will refuse
a capture. Recorded rather than pre-emptively moved: moving a guard that has not fired, to stop it
firing later, is the same error in slower motion.

---

## 3. Routed from the walk

| # | the walk's finding | where it went |
|---|---|---|
| 1 | *Sasha reads washed out at this lamp — the sprite receives less of the lamp's top end; warmest never brightest* | **#183**, a hero pass and explicitly not a rig change |
| 2 | *Msg button overlaps the bottom rig control — unreadable* | **FIXED here.** Both anchor bottom-left of the same overlay; the panel's rect ran to −8 so its last row sat under the button. Bottom now stops at −60, derived from `MsgButton`'s own constants, and `GrowVertical.Begin` grows the panel upward so a future row cannot push it back |
| 3a | *polish slightly hot on worn lanes; wall-base occlusion washed by shine* | **#184** — actionable only now the lamp is one quantity and the rig is set |
| 3b | *hallway-bottom brickwork artefact* | **#185**, its own bug |

---

## 3b. The critic round — FAIL, and the plant outranked the build

**FAIL**, `SHIP: NONE`, rank **2 of 3**, score **0.50** against a best of 1.00. Plant
`cement-cap.png` **CAUGHT**, so the round is readable. Picture moved mean 12.147 / worst 56
luminance levels from the previous readable round, so the guard's premise was genuinely false and
round 3 ran — the #182 mechanism working on its first live use.

> ⚠ **THE PLANT OUTRANKED THE BUILD.** A blind seat put a frame Rafe personally culled — *"caps
> are still grey and read as cement, not stone"* — above this one. That is a statement about the
> build, not about the judge. **`approved_capture` is not seeded and the room walk does not happen
> on this build.**

### The headline flip is about the rig that was ratified this morning, and it measures true

> *"The lit floor is blown out. Excluding UI and sprite, 12,159 pixels sit above luma 190 … the
> floor around the figure reaches ≈(240,235,215)."*

| frame | floor px > luma 190 | share | max |
|---|---:|---:|---:|
| round 29 — old rig (r 5.0 / a 0.70) | 7,168 | 3.68% | 247.9 |
| round 30 — **re-ratified** (r 6.0 / a 1.50) | **10,408** | **5.35%** | 249.2 |

*(measured on floor surface with the player's own cell excluded; the seat's 12,159 uses a looser
exclusion and is the same finding.)* **The re-ratification added 45% more near-blown floor.**

### And flip 2 is Rafe's own walk finding, arriving from the opposite side

> *"the sprite stops reading. The shield's cream face and the sword blade's interior land at the
> same value as the floor beside them, so only the dark outline holds the figure together."*

| | sprite cell p95 | floor beside it p95 | separation |
|---|---:|---:|---:|
| old rig | 248.1 | 241.3 | **+6.7 levels** |
| re-ratified | 249.2 | 248.2 | **+1.0 levels** |

§13.8 puts the *ambiguous* point at 8 levels. **At +1.0 the separation is not faint, it is
absent** — which is exactly what the walk saw and routed as *"Sasha reads washed out."*

> **THE TWO READINGS AGREE ON THE FACT AND DISAGREE ON WHICH SIDE MOVES, AND THAT IS A RULING.**
>
> - **The walk (#183):** the hero takes *less* of the lamp's top end — *warmest never brightest*.
>   That restores separation by putting the figure **below** the lit ground.
> - **The seat (flip 1–2):** pull the floor's peak down until the brightest stone is well clear of
>   the sprite. That restores it by putting the figure **above** the ground.
>
> Both work arithmetically. They produce **opposite pictures**, and the choice is a register
> decision, not a measurement — §13.2 gives it to the eye that walked it, and §13.4.1 is explicit
> that a seat is a proxy for the gate rather than a vote against it. **#183 as routed stands.
> Nothing here re-opens it.**
>
> ⚠ **BUT FLIPS 1 AND 3 SURVIVE #183, AND THIS IS THE PART THAT NEEDS RAFE.** A darker hero does
> not un-blow the floor. *"At x 430–700, y 470–560 there is a flat pale block roughly 110×28 px
> with no interior texture"* — **the blown region is losing its joints**, which is a floor-versus-rig
> question and not a hero one. The re-ratified rig is four hours old and this is the first blind
> read of it.

### The nine flips, triaged

| # | flip | ground |
|---|---|---|
| 1 | the lit floor is blown out | **REAL, measured above.** Consequence of the re-ratification. Survives #183. **Rafe's** |
| 2 | the sprite stops reading | **REAL, +1.0 levels.** Same observation as the walk's own; routed **#183**, direction is Rafe's |
| 3 | restore joints in the blown region | **REAL, and it is flip 1's consequence.** Not separately actionable until 1 is ruled |
| 4 | unify detail scale in that region | floor material — the mottle/erosion layers, **SUPERSEDED-BY-GATE** (LOOP-PROCESS §4.3) |
| 5 | free-floating dark bars at ~(505,505) | unattributed. Candidate for **#185**'s bug, not confirmed — not folded in without a measurement |
| 6 | block edges soft/anti-aliased vs a hard sprite | same layer as 4; `default_texture_filter=0` is nearest, so this is authored softness, not a filter |
| 7 | strip the mottle off the wall tops; it crosses stone divisions | **wall lane**, and it is §8.3's motif/stain shape — a stain that ignores the joints beneath it |
| 8 | give the wall top a value separation from the lit floor | **THIS IS §6.5 ROW 1**, and §1 above measures it moving *further* out of reach. **Rafe's, at a gate** |
| 9 | grey axis-aligned rectangle in the black | **the declared cost of `void_ring 1`.** Measured near-black (14–18, sd 2.2), not #178's flat (77,77,77). A ring classifies at a cell boundary and puts a luminance step there — named in `capture_combined.sh`'s own comment and owed to §12.1a's outstanding occluder |

### Why the line stops here

**No guard fired** — rank fell but one round without a new best is not a stall, and the picture
moved. This is **§1.1.4**, the same shape as round 28's stop: **after triage there is no flip this
round is permitted to apply.**

- 1, 2, 3 turn on a ruling about which side moves, and the rig is four hours ratified
- 8 is §6.5's value law, explicitly reserved
- 4 and 6 are SUPERSEDED-BY-GATE
- 7 is the wall lane, 9 is §12.1a's occluder, 5 is unattributed

Grinding a round against those would be re-opening a gate ruling on a seat's say-so, which
§13.4.1 forbids by name.

---

## 4. What this round does NOT rule

- **§6.5's value law.** Row 1 (*wall top lighter than the floor*) is further out of reach, not
  closer. Measured and reported; not ruled.
- **§6.5's void banner.** Round 29 proposed narrowing it on proof that the range-probe scenes never
  carried the shader. Still **proposed, not applied.**
- **Round 28's flip-1 attribution.** Still unannotated in its own document.
- **Which side moves — the hero or the floor.** Flips 1–2 and walk finding #183 are the same
  observation from opposite directions. **Not ruled here.**
- **The ~45° band (#179).** Untouched; two seats, no location, and not the polish mask.

---

## 5. Reproducing

```bash
dotnet build UnderWarden.Presentation.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --import

tools/tier1_floors/capture_combined.sh rerat_r1 1        # refuses if a declared point is wrong
tools/tier1_floors/capture_energy_probe.sh rerat_reach 1.6   # the reach profile, legibility-free

python3 tools/tier1_walls/measure_mass_read.py \
  --scene src/Presentation/assets/tier0_harness/scenes/tier1_combined_review.json \
  --png tools/tier1_floors/evidence/rerat_r1.png \
  --log tools/tier1_floors/evidence/rerat_r1.log \
  --assets src/Presentation/assets/tier1_walls --tag rerat_r1

.claude/skills/frame-critic/run_frame_critic.sh --lane combined
```
