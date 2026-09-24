# Round 28 — the combined build: one room

> ## RULED (Rafe, 2026-09-05). ROUND ACCEPTED. THE LANE IS PARKED, AND #174 IS THE GATE.
>
> *"The combine cannot honestly PASS while #174 stands — the room's two core reads (§6.5 stack,
> corridor-as-corridor) are both measured across the broken arithmetic. #174 is now the gate.
> Park the combine at this composed-and-holding state; it resumes for the walk after #174 lands
> and the rig re-ratifies."*
>
> **Composition holds. The round stopped correctly at a ruling trigger and not at a guard.**
>
> | | ruling |
> |---|---|
> | **the joint flip** | **REFUSED and recorded.** *Packed joints take the shine* is ruled; two seats requesting the inverse is the **§13.4 case, not a reopening**. The repetition means **the treatment is not reading as intentional** — a MAKE-IT-READ polish item for later, **not a deepen-now capitulation**. Written up as bible **§13.4.1** |
> | **#174** | **Scope reframed** from *"the lamp is wrong"* to **"43% of §6.5-governed cross-plane separation runs on `LIGHT_COLOR`-not-`ENERGY` arithmetic; the planes are lit by different math."** Filed onto the issue and onto bible §6.2's asterisk. §6.5's measured figures are **VOID rather than provisional** — re-measured after #174, not adjusted |
> | **the chokepoint** | *"a vertical column of light with nothing casting it"* is **the wall mass-read question, ruled #174-provisional.** The fix is the floor-wall value relationship, which #174 corrupts; **it cannot be chased until the lamp arithmetic is one** |
> | **§12.1a's void** | **stays flat-dark-fallback.** Occluder outstanding, and recorded as the interim in the clause itself |
> | **the out-of-map grey** | filed as **#178** |
>
> **PARK STATE — composed and holding.** The scene, the capture path, the `--void-ring` flag, the
> hatch fix and the critic's `combined` surface are all landed and reproducible (§10). What is
> owed before the walk is **#174 and Ruling 56's re-ratification**, and nothing in this lane.
> Whoever resumes: re-capture through §10 unchanged, re-measure §5's table, and expect every
> cross-plane number in it to move.

**Both surface lanes stopped soloing. This is the first frame in the project's history that has a
real floor, real walls, a real cap and a real void in it at once, with no magenta anywhere.**

It does not reach the device. The critic said FAIL, and triage of its six flips found that
**every actionable one is inside a ring-fence this round was told not to open** — three are ruled
behaviour, one is issue #174, one is misidentified ground, one is dark-by-design. That is a
LOOP-PROCESS §1.1.4(b) ruling trigger with the round's own evidence behind it, and it is why the
line stops here rather than grinding.

---

## 0. What this returns, in one page

1. **One room, composed and captured.** `tier1_combined_review.json`. All five declared
   legibility points PASS. `void=129 face=24 top=63 cap=87 bindings=13`, no magenta, no mock.
2. **The forwarded hatch flip is FIXED and measured.** The dressing drew from four directions,
   two of them the same 45°; it draws from twelve now. 51.8% → 18.7% of runs at 45°, modal angle
   27.2% → 10.5%. The census fires on its own control.
3. **The forwarded joint flip is a RULING, not an edit**, and a second independent seat has now
   asked for it. §6 below.
4. **The floor's delivered light is 30% specular at the standing case, and #174 is why.** This is
   the round's finding. Nulling the polish gain moves the floor 122.04 → 85.24 and leaves every
   wall value byte-identical. §5.
5. **Delivered palette clean**: 0 off-palette on the albedo, control binds at 634 vs 31.
6. **Critic FAIL**, plant CAUGHT, rank 2 of 3. The plant outranked the build — the **sixth**
   recorded instance of §4's ordering finding.

---

## 1. The scene

`src/Presentation/assets/tier0_harness/scenes/tier1_combined_review.json`.

**Geometry is `tier1_wall_review`'s, byte for byte.** It is the only review spec that puts all
four of §3's contexts in one frame *and* carries a non-zero traffic field (spine 18 / routes 25),
and §9e records what a traffic-dead scene costs: every traffic-keyed system in it is untested
while looking tested.

**What moves is the station, and it moves for the FLOOR's law.** Round 24 ruled that stations for
traffic-keyed questions are sampled from the polyline. **The wall gate's station at (8,15) has
route strength ZERO** — it was chosen when the floor was not the question. A combined round taken
there would have judged the floor's whole traffic system on ground the level's own graph says
nobody walks, which is round 23's VOID-FOR-STATION failure reproduced silently.

The station is **(6,12)**, read off this scene's own route-strength map. From it: the route ahead
runs (5,13) (4,14) (3,15) inside the delivered pool; the masses' south-face row stands 1 tile
north at **n=11 cells** where the wall gate's station had n=1; the island's four outer corners are
3.2 tiles south-west; the chokepoint doorway with its two jambs is 2.2 tiles east.

### A station was rejected first, and the reason is a finding

The route's peak is **(5,13)** and the first capture was taken there. At x=5 the camera's left
edge falls at tile −0.86 — outside the map — and **the engine fills out-of-map ground with a flat
(77,77,77) that the light rig never touches.** Measured on that frame: a 55px column at standard
deviation **0.00**, full height of the dungeon view, against a void of 3.2 beside it. It is the
brightest thing in the frame after the lamp pool and it has a ruled straight edge.

**Reported, not fixed: it is neither floor nor wall material.** It is engine behaviour, it will
appear in the shipping game wherever a player stands within six tiles of a map edge, and it is
worth someone's round.

---

## 2. The void, and what it cost

**`--void-ring` is a new capture-time flag and the reason is §12.1a.**

That clause ruled the void dark by OCCLUSION rather than by a ring, and the wall manifest carries
`void_ring: 0` for it. **The clause's own text records that its implementation is outstanding** —
*"the renderer currently lights wall cells regardless of what stands between them and the lamp."*
So at zero, nothing is void. Measured on this scene's first combined capture, at the ruled value:

```
void=0(choice=0,ring>0)   face_suppressed=192   cap=216+0void
```

**192 cells of solid rock, lit, out to the map edge, and a room with no outside.** That is exactly
the consequence a frame critic measured when the ring was first dropped — *"the unexcavated mass
is 77% as bright as the walked surface."* `evidence/combined_r1_ring0.png` is that frame.

So this round runs the **flat-dark fallback**, and the cost is named rather than hidden: a ring is
a classification that changes at a cell boundary, so it puts a luminance step on the grid, and
round 8's seat read that step unaided as *"two perfectly straight vertical seams in the darkness."*
**That cost is this round's and it is flagged.** §12.1a is not re-ruled; the occluder pass is the
real fix and it is a later round with a walk behind it.

**The departure is on the command line and not in the manifest**, because the manifest's 0 is a
RULED value and a round does not get to quietly move one. `--void-ring` is echoed into every
capture log with the manifest value beside it — `ring>1,OVERRIDE manifest=0` — so no frame can
circulate without carrying its own departure.

---

## 3. Forwarded flip 6 — the hatch motif trap. FIXED.

> *"vary the 45° hatch; same angle and spacing on a dozen slabs"*

**One direction per stone was never the defect.** A mason does work one stone one way and that
clause stands. The defect is that the world held **four** directions, two of them the same 45°, so
a dozen stones in one lit pool drew the identical hatch. §8.3.1 is written about a treatment at a
constant POSITION; this is the same arithmetic one axis over — **a treatment at a constant ANGLE
is a motif once the field is large enough to hold a dozen of it**, however well each one is
addressed.

Twelve directions now, as rational slopes: 0°, ±18.4°, ±26.6°, ±45°, ±63.4°, ±71.6°, 90°.

**And the spacing was never 2px, which is the other half of the same verdict.** The teeth were
offset by the raw `(-dy, dx)` — a normal of the right direction and the wrong length, since
`|(-dy,dx)|` is 1 only for an axis step. A diagonal stone's teeth sat **2.83px** apart where an
orthogonal stone's sat 2.00, and with one diagonal angle in the table that 1.41× was a *constant*.
The culled hatch had one angle and one spacing, and the second followed from the first. Both the
offset and the run are taken along the direction's own geometry now (`mark_normal`, `mark_run`),
so 2px means 2px at every angle.

`measure_dressing_angles.py`, 16×16 field, seed 1337, **981 runs**:

| | distinct angles | modal share | at 45° |
|---|---:|---:|---:|
| **control** — the culled table | 4 | 0.272 | **0.518** |
| **live** | **12** | **0.105** | **0.187** |

**§13.5: the control is built in and runs every time.** A run whose control comes back clean
prints REFUSING and exits non-zero — an instrument that cannot fail has not passed.

**Mirrored in the engine, and the engine is what draws it.** `Tier1AshlarFloor.StoneMarks` paints
the dressing at runtime from the manifest's table, so the fix is two implementations that must
agree pixel for pixel. They do: `paint_check=96/OK` on every capture in this round, which is the
check that refuses to lay a floor at all on disagreement.

---

## 4. Forwarded flip 5 — packed joints. NOT APPLIED. It is a ruling.

> *"sharpen the joints; they fade toward the light edge"* — round 27's seat
> *"in the lit zone right of the figure the stone joints vanish entirely — you cannot tell where
> one slab ends. Restore joint contrast at full light."* — this round's seat

**These are the same request and it is a request to undo ruled behaviour.** *A packed joint takes
the shine* was ruled after a walk contradicted the table, and the code carries the ruling and the
measurement: setting polish on stone faces only amplified the delivered face-to-joint contrast by
exactly the light, and the outline share in the lit band fell **16.3% → 1.3%** when the joints
were given their share. *"The source was clean the whole time; the renderer was drawing the
ring."*

Round 27 forwarded this as needing a ruling rather than an edit. **A second independent blind seat
has now named it**, which is the fact that makes it worth Rafe's attention rather than a second
deferral: two seats, two rounds, two different decks, the same request. The behaviour is doing
what it was ruled to do and two people looking at the result have asked for it back.

**LOOP-PROCESS §1.1.4(b). Not bundled, not applied, printed in full (§9).**

---

## 5. The floor/wall value relationship — and it is #174 all the way down

**FLAGGED #174-PROVISIONAL. Nothing here is re-ruled.**

Measured on the composed frame, mean delivered luminance per cell, binned by range from the lamp,
tiles classified from the scene's own carve list and positioned from **the engine's logged grid**
(`centre00=(23.0,-190.5) pitch=64`) rather than from an assumed centring — §13.10.

| band | floor n | floor mean | wall n | wall mean | floor ÷ wall |
|---|---:|---:|---:|---:|---:|
| ≤2 tiles | 9 | **122.04** | 4 | 58.79 | **2.076** |
| 2–4 | 21 | 47.94 | 15 | 20.75 | 2.311 |
| >4 | 51 | 11.19 | 68 | 4.97 | 2.252 |

`measure_mass_read` on the same frame, cap cells only: **L(cap, floor) = 50.22 levels** at the
standing case against the 8-level bar (2.4× the wall lane's own 19.27, at n=3 rather than n=2),
16.88 at 3–4 tiles where the wall lane read 2.69, 4.26 beyond. Sign negative throughout — the cap
is **darker** than the floor, which is the intended direction.

### The control, and it is the round's finding

`polish_gain: 0.0`, same scene, same station, same rig, everything else held:

| band | floor, polish ON | floor, polish NULLED | wall, ON | wall, NULLED |
|---|---:|---:|---:|---:|
| ≤2 | 122.04 | **85.24** | 58.79 | **58.79** |
| 2–4 | 47.94 | **35.00** | 20.75 | **20.75** |
| >4 | 11.19 | 11.09 | 4.97 | 4.97 |

**Every wall value is byte-identical and the floor loses 30% of its delivered light at the
standing case.** The shader touches only the floor, exactly as designed — and that is the problem:

> **The floor's delivered value at the range §6.5 governs is diffuse plus a specular term worth a
> third of it. 43% of the floor-to-wall separation at the standing case is the shader.**
> Beyond four tiles the term contributes nothing (11.19 against 11.09), which is `pow(delivered,
> 2)` doing what §13.9 predicts.

**And this is #174, seen from the picture side.** The shader computes its `delivered` scalar from
`LIGHT_COLOR`, which is why the floor and the wall are lit by *different arithmetic* rather than
by different amounts. **Fix `LIGHT_ENERGY` and both terms move, non-proportionally, because the
specular squares it.** So:

> **The floor-versus-wall relationship measured today does not survive #174, and no cross-plane
> value law should be written from this table.** It is recorded so #174's round has a
> before-picture, and for nothing else. §6.5, k_top and #151 are untouched.

`evidence/combined_nopolish.png` is the control frame. Two notes on producing it, both earned:

- The review spec **refuses to write a PNG** when a declared lit point comes back dark, and
  nulling the polish makes three of them come back dark. That refusal is correct. The control gets
  its own legibility-free spec (`tier1_combined_probe.json`), documented as an instrument scene
  that nothing is ever judged through.
- **Edit the manifest by text substitution, never by a JSON round-trip.** The first attempt used
  `json.load`/`json.dump` and the capture came back with *every* point at 0.0000 including the
  reference — a whole-frame failure that looks nothing like the change being tested, and would
  have been reported as one if the second attempt had not been made a different way.

---

## 6. Delivered palette — clean

| | delivered | authored | off-palette |
|---|---:|---:|---:|
| albedo, no walls | 31 | 33 | **0** |
| with contact occlusion | 31 | 33 | **0** |
| occlusion stacked ×2, ×3 | 31 | 33 | **0** |
| **control** — occlusion alpha-blended | **634** | 33 | **596** |

**Composition reintroduced no continuous-tone layer.** The control binds at 634 against 31.

Delivered-frame counts, labelled as the rig's number and not the palette's: floor 113.9
colours/cell, wall 35.2. A carried lamp is continuous; a whole-frame count measures the lamp.

⚠ **The ladder is ELEVEN rungs in the code and the manifest, and bible §5.6 still says
"Count: 7 derived rungs, plus 2 below the donors' band. Total 9."** The tools have moved and the
clause has not. Recorded as a doc/code disagreement for whoever owns §5.6 — not amended here.

---

## 7. The critic round

**FAIL**, `SHIP: NONE`, rank **2 of 3**, score 0.50, new best. Plant `cement-cap.png` — Rafe's own
*"caps are still grey and read as cement, not stone"* — **CAUGHT** (flagged, not shipped), so the
round is readable. Axis **construction**.

**The plant outranked the build.** Recorded and not scored, and it is the **sixth** consecutive
instance of §4's finding that a blind seat's ordering does not reproduce Rafe's culls. The seat
also flagged every frame including the commercial bar, so its flag carries no discrimination this
round. `approved_capture` is null and every verdict says so.

### The morgue had to be taught what a whole-scene round is

The deck draws its plant **by surface**, and the morgue was tagged when every round judged one
surface at a time. Which entries can serve `combined` was settled **by reading the pictures**:

| | magenta pixels | serves `combined` |
|---|---:|---|
| keyline-floor, washed-slab-lane, tile-quantized-wear | **3904** each (0.39%) | **no** |
| grey-walls, cement-cap | **0** | yes |

The three floor culls were taken before the wall family existed. In a deck whose build has real
walls, a seat catches one of those for the debug colour rather than for its defect — §4.2's
failure exactly, *the right image flagged for the wrong reason*. The two wall culls carry no
magenta and were captured with the real floor under them, so they are already whole-scene frames.
`surface` accepts a list now, and every entry records that finding in `combined_eligibility` so
the next reader does not re-measure it.

**Axis `construction` rather than `chroma`**, which is what selects cement-cap over grey-walls: a
whole-room frame is judged on whether the place reads as built and held, and PR #171 found that a
chroma plant cannot judge craft — two VOIDs.

**And the verdict was not recording its axis.** The axis chooses the plant, so a verdict that does
not name it cannot be checked later against the rule it was drawn under, and *the deck is
reproducible from the verdict file alone* is the property that makes these files worth committing.
It was selecting correctly and saying nothing about it. Fixed; this round's two verdicts stamped.

### The six flips, triaged

| # | the seat's flip | ground |
|---|---|---|
| 1 | *"A uniform ~45° hatch overlay runs across the entire lit floor … it is screen-space, not surface texture"* | **REAL, and it is #174.** See below |
| 2 | *"x 130–440, y 0–140 is soft brown blobs with no joints — stones dissolved by blur"* | wall cap at 4+ tiles. §13.9: below the representable floor at that range, recorded dark-by-design |
| 3 | *"The wall top … at (490,470) measures 170 against the floor below it at (520,500) measuring 68"* | **the luminances are right to within 3 levels; the identification is not.** Both points are FLOOR — tiles (7,12) and (8,12) — per the engine's own logged grid, verified against its own probe (`legibility(5,12) at px(343,578)`, and 23+64·5 = 343). It is floor-to-floor inside the falloff, not a plane inversion |
| 4 | *"a vertical column of light at x≈510–530, y 250–450 with nothing in the frame casting it"* | **REAL, and it is the wall lane's own open question.** Those are tiles (8, 8–11) — the lit chokepoint. **The corridor does not read as a corridor; it reads as an unexplained shaft.** §5 of the wall report's gate questions, answered no. **RULED 2026-09-05: #174-PROVISIONAL.** It is the wall mass-read question, its fix is the floor-wall value relationship, and #174 corrupts that — **it cannot be chased until the lamp arithmetic is one** |
| 5 | *"the stone joints vanish entirely … restore joint contrast at full light"* | **ruled behaviour**, §4 above. Second seat |
| 6 | *"the frame is soft everywhere. Kill the blur pass"* | no blur pass exists — `default_texture_filter=0` is nearest. The softness is the mottle and erosion layers, which LOOP-PROCESS §4.3 already ruled SUPERSEDED-BY-GATE |

### On flip 1, because I nearly got it wrong

**My first two instruments said the seat was wrong.** A lag autocorrelation found no ±45°
preference in any region; a structure tensor put the dominant orientation at 89.6° / 90.0° /
178.7° — the bond. Both were measuring the wrong thing: **the artefact is a cross-hatch weave, so
it has energy at every angle and no direction dominates.** High-passing the frame and amplifying
6× shows it immediately (`/tmp` working images; reproduce from §9).

**§13.10, and it nearly ran the other way.** The seat's two cited luminances were exact to three
levels. An instrument that says a seat did not see what it says it saw is making the stronger
claim and carries the heavier burden — and here two of mine made that claim and were wrong.

The artefact is the polish mask: `refl = PolishByAge[fa]` picks one of **four** discrete
reflectivities **per pixel**, from a noise-derived wear scalar, and `pow(delivered, 2)` stamps
that four-level dither wherever the light is strong. It ignores stone boundaries because it is
not stone material. **It is not fixable in this round**: it is Ruling 70's system, it sits on top
of #174's wrong `delivered` quantity, and #174's fix changes its amplitude non-proportionally.

---

## 8. Why the line stops here

LOOP-PROCESS §1.1.2 is explicit that a critic FAIL is a reprompt and *"tried once or twice, then
asked"* is a malformed session. **The progress guards did not fire** — round 1, new best rank, no
stall, no thrash, no ceiling. This is not a guard stop and it is not checkpoint creep.

It is **§1.1.4(b), an amendment to something frozen**, and the trigger is that after triage
**there is no flip left that this round is permitted to apply**:

- flips 1 and 5 are ruled systems (Ruling 70, the packed-joint clause)
- flip 1 is additionally inside #174, which this round's brief ring-fences by name
- flip 3 is misidentified ground
- flips 2 and 6 are already ruled dark-by-design and SUPERSEDED-BY-GATE
- flip 4 is the wall lane's open mass-read question, not a floor or composition change

Applying any of them would be bundling a ruling into a composition round. **The room is composed
and it holds; what it is waiting on is #174's walk.**

---

## 9. Anything awaiting a ruling, printed in full (§9)

1. **Packed joints take the shine.** — **RULED 2026-09-05: REFUSED.** Two independent blind seats,
   two rounds, asked for the joints back at full light; the behaviour is ruled and is doing what
   it was ruled to do. Verbatim, both: *"sharpen the joints; they fade toward the light edge"* /
   *"in the lit zone right of the figure the stone joints vanish entirely — you cannot tell where
   one slab ends. Restore joint contrast at full light so the grid survives exposure."*
   A blind seat is a proxy for the gate, not the gate, and counting seat requests as votes is
   §13.4's failure running in the review layer. **But the repetition is information: the
   treatment is not reading as INTENTIONAL.** The successor is a make-it-read item — make the
   packing legible *as* packing, in §8.2.1's own vocabulary — and explicitly **not** a
   deepen-now capitulation, which would put the ring back. Bible **§13.4.1** carries the ruling
   and its general form.
2. **#174 and the polish term.** — **RULED 2026-09-05: SCOPE REFRAMED, AND #174 IS THE GATE.**
   Not *"the lamp is wrong"* but *"43% of §6.5-governed cross-plane separation runs on
   `LIGHT_COLOR`-not-`ENERGY` arithmetic; the planes are lit by different math."* Filed onto the
   issue and onto bible §6.2's asterisk; §6.5's measured figures are now **void rather than
   provisional**. The visible cost is a four-level per-pixel dither stamped over the lit floor,
   which this round's seat made its first flip. **This round did not touch `LIGHT_ENERGY`, as
   instructed.** Whoever re-ratifies the rig owns both.
3. **§12.1a's occluder.** — **RULED 2026-09-05: THE FLAT-DARK FALLBACK STANDS, OCCLUDER
   OUTSTANDING.** Not attempted. At the ruled `void_ring: 0` the lamp lights 192 cells of solid
   rock and the room has no outside; this round ran the flat-dark fallback on the command line
   and flagged it. Recorded as the interim in §12.1a itself, with its grid-step cost named.
4. **The out-of-map grey.** — **FILED as #178.** A flat (77,77,77), sd 0.00, unlit, wherever the
   camera sees past the map edge.
5. **Bible §5.6 says nine rungs; the code and the manifest ship eleven.**

---

## 10. Reproducing

```bash
tools/tier1_floors/rebuild_ashlar.sh
dotnet build UnderWarden.Presentation.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --import

# the room
tools/tier1_floors/capture_combined.sh combined_r1 1
tools/tier1_floors/capture_combined.sh combined_r1_unlit 1 --light-energy 0
tools/tier1_floors/capture_combined.sh combined_r1_ring0 0     # the ruled void_ring, for contrast

# the dressing's angles, with its control
python3 tools/tier1_floors/measure_dressing_angles.py \
  --json-out tools/tier1_floors/evidence/DRESSING-ANGLES.json

# the palette, with its control
python3 tools/tier1_floors/measure_delivered_palette.py --controls --ladder-delta \
  --capture tools/tier1_floors/evidence/combined_r1.png \
            tools/tier1_floors/evidence/combined_r1.log \
  --scene src/Presentation/assets/tier0_harness/scenes/tier1_combined_review.json

# the planes, and the cap against the floor
python3 tools/tier1_walls/measure_mass_read.py \
  --scene src/Presentation/assets/tier0_harness/scenes/tier1_combined_review.json \
  --png tools/tier1_floors/evidence/combined_r1.png \
  --log tools/tier1_floors/evidence/combined_r1.log \
  --assets src/Presentation/assets/tier1_walls --tag combined_r1

# the round
.claude/skills/frame-critic/run_frame_critic.sh
```

The null-polish control, which is the one thing here that is not a single command — and the two
traps in it are in `tier1_combined_probe.json`'s own comment:

```bash
cp src/Presentation/assets/tier1_ashlar/MANIFEST.json /tmp/MANIFEST.bak.json
sed -i '' 's/"polish_gain": 1.0/"polish_gain": 0.0/' \
    src/Presentation/assets/tier1_ashlar/MANIFEST.json
python3 tools/tier0_harness/capture_corridor.py \
  --out tools/tier1_floors/evidence/combined_nopolish.png \
  --scene-spec src/Presentation/assets/tier0_harness/scenes/tier1_combined_probe.json \
  --theme-config res://src/Presentation/assets/tier1_ashlar/tile_themes_tier1_ashlar.yaml \
  --floor-overlays res://src/Presentation/assets/tier1_floors/MANIFEST.json \
  --ashlar-floor  res://src/Presentation/assets/tier1_ashlar/MANIFEST.json \
  --boundary-wall res://src/Presentation/assets/tier1_walls/MANIFEST.json \
  --wall-bindings res://src/Presentation/assets/tier1_bindings/MANIFEST.json \
  --wall-cap      res://src/Presentation/assets/tier1_cap/MANIFEST.json \
  --void-ring 1 --log-out tools/tier1_floors/evidence/combined_nopolish.log
cp /tmp/MANIFEST.bak.json src/Presentation/assets/tier1_ashlar/MANIFEST.json   # ALWAYS
```
