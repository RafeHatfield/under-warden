---
name: frame-critic
description: Judge an art round the only way art rounds are judged in this project — a fresh blind critic's eyes on the delivered frames. Use when an art build is ready to be assessed, before any build goes to the device, and whenever an install is refused for want of a verdict. Also covers the loop guards that stop a round grinding.
---

# The frame critic

**An art round is judged by eyes on delivered frames. Nothing else judges it.**

A fresh blind `claude -p` seat is shown a small deck of finished pictures — this build's capture,
the asset bar, the last frame Rafe approved, and one **picture-plant** he personally culled —
shuffled and unlabelled. It ranks them, says which it would ship, and flags anything with an
obvious defect. That is the verdict.

```
.claude/skills/frame-critic/run_frame_critic.sh
```

```
exit 0  PASS   the seat would ship this frame, flagged nothing in it, and ranked it at or
               above the last Rafe-approved frame and near the asset bar. Any round.
        PASS-INSTALL  against a SEEDED reference: ranked ABOVE it, with no unrouted flags.
               Same exit, same gate. See §4a.
exit 1  FAIL   any of those missing; the flip list is in CRITIC-VERDICT.json, verbatim
exit 2  VOID   it did not catch the plant. Findings are NOT READ. Stop and fix the judge.
exit 3  STOP   a loop guard fired. Read STALL-REPORT.md, end the turn, hand it to Rafe.
exit 4  refused — a precondition failed and it says which
```

**PASS is reachable at any round and the guards never gate it** — they decide when to stop and
ask, never what may ship.

---

## 1. Why it is shaped like this

Two collapses, measured, both of the review layer, both the same shape: **the apparatus became
the judge, and then the apparatus broke.**

**One — the instruments became the judge.** At the wall device gate of 2026-08-27 every
instrument in the repo was green and the phone still said no. A gate FAIL against a fully green
instrument set is not a tuning miss. It says the thing being measured and the thing being judged
had come apart, and the numbers had been holding the gate for some time before anyone noticed.

**Two — the plant stopped being in the picture.** Wall rounds 9 and 10 both went VOID because the
generated plant differed from the family in **0.54% of pixels, in 21 cells**: since the cap pass
the cell's base is a cap window and the wall family's top tiles are never drawn, so ruining the
wall tiles ruined almost nothing. The control was *downstream of the engine*, so an engine change
neutralised it silently. Rounds 3 and 6 died the same way for a different reason.

The answers follow directly:

| the failure | the answer |
|---|---|
| a number held the gate and drifted from the eye | **the judge is eyes on pictures.** No threshold in it to drift |
| the control was generated, so the engine could disarm it | **the plant is a picture.** Bytes in `morgue/`. An engine change cannot touch a picture |
| a documented rule was not remembered | **the install script is the choke point.** The gate is the thing that installs |
| a round can iterate forever | **loop guards stop the line** and escalate, rather than grinding |

---

## 2. Instruments gate nothing

Every `measure_*.py`, every census, every screen in this repo **stays exactly where it is and
keeps running.** They are builder's tools: they tell you where to aim between rounds, they are
cheap, and they are often the only way to find out *why* a frame failed.

They do not gate. Not the round, not the install, not a landing. A number that can hold a gate
will eventually be optimised against, and it will silently outcompete every clause that has no
number — which is most of the register (bible §13.4).

An instrument result belongs in a round report and in the aiming. It never appears in a
CRITIC-VERDICT.

---

## 3. The deck

Four frames, shuffled, unlabelled, named `1.png`–`4.png` in a working directory **outside the
repo**:

1. **the build** — captured fresh by the command in `docs/FRAME-CRITIC.json`
2. **the asset bar** — the commercial bar (§13.3: measurements leave, pixels never do; the crop
   is written outside the repo and nowhere else)
3. **the last Rafe-approved capture of the same surface**
4. **the picture-plant** — one frame from `morgue/`, matching this round's surface

The same crop is applied to every Yarl frame so the crop can never become the tell. The shuffle
is seeded from the build id and the round number, so the deck is reproducible from the verdict
file alone — a deck nobody can reconstruct is a verdict nobody can check.

The seat gets the fiction, the tone, and the questions. **It never gets the bible**
(LOOP-PROCESS §3.2), never gets code, coordinates, thresholds, or any hint of which frame is
which.

The questions are fixed:

> Rank these for craft. Which would you ship? For the best and worst, say concretely why.
> Flag any image with an obvious defect.

---

## 4. The plant — the self-test that runs every round

**Declared before the first round, not negotiable afterwards:**

> The plant must land **worst or flagged**, and must not appear in SHIP.

Miss it and the round is **VOID**: its findings are not read, not discounted — void. A soft
critic's findings are worse than no findings because they will be acted on.

This needs **no vocabulary list**, and that is the point of moving to a picture. The generated
plant it replaces needed one — a hand-maintained list of ruin words — and that list went wrong in
every direction a list can. It carried `lichen`, which no plant ever contained. It lacked `hole`,
the plainest word for the plant's most prominent feature, for three rounds. A list widened by
reading transcripts is a test derived from its own outcome. **A rank has no vocabulary.**

### What the plant does NOT test — write this down, it matters

**The plant tests for softness, not for ordering.** It answers one question: would this seat ship
a frame the human gate already rejected? A seat that would is soft, and a soft critic's findings
are worse than none.

It does **not** answer whether the build is better than the plant, and **a blind seat's RANKING
does not reproduce Rafe's culls.** Three rounds, three different seats, three different shuffles,
same result. It is the most important thing in this file:

| run | what happened |
| --- | --- |
| **first live wall round** | the seat flagged all three frames, shipped none, and **ranked the plant first** — a frame Rafe culled for grey walls, put above the current build |
| **the plant self-test** | the build slot deliberately held `keyline-floor.png`, a frame Rafe culled outright. The seat **ranked it best of three and did not flag it.** It listed that frame's magenta placeholder walls in its flip list, so it had seen them — it simply did not call it a flagged defect |
| **the round that found the padded bar** | a different seat on the same wall build **ranked the plant above it again**, and ranked the commercial asset bar last — for a black band the crop box had put there |
| **the round that found the white margin** | a third seat, a third shuffle, **the plant above the build again** — and the bar last again, this time for the example sheet's own white paper margin |
| **the first round under the progress guards** | a fourth seat put the plant **first in the deck and flagged nothing in it.** The round went **VOID** — the first time the control actually fired, and the strongest form of the same finding |
| **combined rounds 3 and 4** | the plant sat above the build **twice more**, and on round 4 the seat also **did not flag it** — VOID again. Sixth and seventh instances |

Every one of them still came out **FAIL** with `SHIP: NONE`, and no gate opened. That is the whole
protective claim and it is narrower than it sounds:

> **Rank is never sufficient on its own.** PASS requires the seat to put this frame in SHIP,
> unflagged — and, since the progress amendment, *also* to have ranked it at or above the last
> approved frame and near the bar. Rank is a **necessary** condition added on top, never a
> substitute: a build that outranks everything and is not in SHIP still fails. What the mechanism
> guarantees is that a build reaching the phone is one a blind seat said it would ship. It does
> **not** guarantee the seat's ordering agrees with the human gate's — the evidence above is that
> it does not, which is exactly why rank was added as a filter and not as a verdict.

> ### THE SEVENTH INSTANCE, AND WHAT IT IS EVIDENCE OF — STANDING OBSERVATION (Rafe, 2026-09-07). NOT A DEFECT, NOT FIXED.
>
> Seven runs now. It is not noise and it is not a soft seat, and the reading that matters is
> **why the two orderings differ rather than which is right**:
>
> > **A seat ranks WHOLE-FRAME CRAFT. Rafe culls a SPECIFIC PROPERTY.** Those are different
> > questions, and a frame can lose on the first while winning on the second — or the reverse.
> > The gap is not the seat being wrong.
>
> **The consequence is the per-axis plant law, and this is the observation's whole practical
> content.** If a seat is ranking overall craft, then handing it a plant whose defect lies on some
> *other* axis gives it no reason to rank that plant last — the two frames differ on a question
> nobody asked. That is the mechanism behind wall round 5 and combined round 4, and it is why the
> plant must be **axis-matched** rather than merely known-bad.
>
> **What is NOT concluded from it:** that the seat should be replaced, re-prompted toward Rafe's
> criteria, or scored on agreement with him. Prompting a blind seat toward the gate's known
> answers is how a proxy stops being independent, and §13.4 already refuses to instrument what is
> carried eye-side. **The seat is kept as it is, and the plant is made to match the question.**

Two ordering facts remain **recorded and reported, never scored**: `outranked_build` when the
plant sits above the build, and `every_frame_flagged` when a seat flags everything including the
commercial bar and its flag therefore carries no discrimination. Folding either into the verdict
would say the wrong thing — VOID means *stop and fix the judge*, and there is nothing wrong with
the judge in either case.

Rank does one more job, and only one: it is **the progress signal the loop guards read** (§5).
Deciding when to stop and ask a human is a much weaker use than deciding what ships, and it is
the use the evidence supports.

A seat too harsh ever to PASS is a real failure too, and it belongs to the **stall** guard, not to
the plant.

### ⚠ One assumption, stated rather than buried

The amendment that introduced these guards described PASS as *"still = ranks at/above the
last-approved frame and near the bar."* On main, PASS was SHIP-based and said nothing about rank,
so *"still"* cannot mean *unchanged*. It is read here as **not loosened**: PASS is the conjunction
of the rule that shipped and the comparative rule. That can only refuse more builds than either
reading alone, which is the safe direction for an install gate to be wrong in.

If rank alone was meant, drop the two SHIP terms from the verdict line in `frame_critic.py`. It is
one edit, and it **loosens** the gate, so it is Rafe's to make.

**The rule was declared before the first round and was not touched after it.** LOOP-PROCESS §8: a
bar found wanting mid-run is held frozen, cleared honestly, and impeached in the same report —
never re-tuned once the answer is visible. This section is that impeachment.

### A plant must be resolvable at DECK SCALE — LAW (Rafe, 2026-09-13)

> *"A plant whose distance from the build is below what a seat can resolve at deck scale is not
> a control. When a failure is caught by eye at a scale the seats can't see, the deck gets a
> coarse version of the same failure, and the fine fact becomes a derived assertion."*

**The occasion.** Lane `art/lambda-212` r001: the deck's one grounding plant was the walked
frame itself, culled by Rafe for a Λ-frame whose base parallelogram lay 11 px over the wall's
face. The built law moved the sprite 12 px. Three of five seats neither flagged nor ranked last
a frame that differed from the build by 12 px in a 750-px deck, and the round voided on the
judge. The same trap had been named one round earlier for `jamb-hard-edge` (a 30-px defect,
recorded and deliberately not dealt) and then walked into anyway because one seat had once
flagged the Λ as a flip item — a flip is a seat's finding about a build; it is not evidence the
seat would catch the same frame as a control against that build.

**What the law does.** Two things, and both are required:

1. **The deck gets a coarse version.** `lambda-on-the-cap` is retired as a control (it stays in
   the morgue as the walked record) and `lambda-in-the-face` is DRAWN to Rafe's words — the same
   Λ with its feet ~16 px up the face, no foot line between the legs — the same failure at a
   scale a seat catches. Ruling 47 applies: it proves it can be caught before its round counts.
2. **The fine fact becomes a derived assertion.** The 12 px do not disappear from the gate; they
   move to the item exit (`docs/FRAME-CRITIC.json`): *the sprite's bottom row at foot + 12; zero
   cap pixels over the lower half* — derived from the capture, never copied (bible §13.12). The
   walk carries the rest.

A drawn plant is a picture with a recorded provenance, not a generator: the frame is the built
law's own, captured at the regime, with one placement edit that did not land, and the edit is
in the entry's `source`.

### Plants are drawn PER AXIS

**LAW (Rafe, 2026-09-03).** *"Per-axis morgue plants — tag entries by axis, assemble the plant to
match the deck's question; this is why round 5 VOIDed."*

A plant is only a control if it is wrong **on the axis under test**. Morgue entries carry an
`axis` list; `docs/FRAME-CRITIC.json` carries the round's `axis`; the deck draws a plant that
matches. An entry with no `axis` answers any question, so the morgue stays usable while it is
being tagged.

The wall lane paid for this. Round 5 was judged on **construction** — does the cap read as stone
or as cement — and was handed the `grey-walls` plant, whose defect is **chroma**. The build's
chroma had already been fixed, so the two frames differed on an axis the plant was not carrying,
the seat had no reason to rank the plant last, and the round voided **on the judge rather than on
the art**. The right image for the wrong question is not a control.

### Plants and the reference are captured under the deck's LIGHTING REGIME

**LAW (Rafe, 2026-09-12).** *"Plants and reference are captured under the deck's lighting regime
— scene, rig, AND shadow state; re-capture the object/wall plants shadowed before any critic
round runs on this lane."*

Round 1 of `art/cast-shadows` put a room with cast shadows into a deck whose plants and
reference were all captured before shadows existed. Three of five seats ranked a culled,
unshadowed frame above the build and flagged nothing in it. That was **not a broken judge — a
seat-blind axis (§13.2)**: the seats compared exposure, not craft, because the only difference
they could see was the light. A control lit differently from the build is not a control.

So `docs/FRAME-CRITIC.json` names the deck's `regime`, every morgue entry carries the regime it
was captured under, and the approved reference carries its own. `pick_plant` **refuses** an
off-regime plant and the runner **refuses** an off-regime reference — before any seat is spent.
A regime's first reference is seeded by Rafe's walk; there is no round before it. Off-regime
misses in a covered round are excused by a `JUDGE-CLEARED.json` entry naming `deck_regime`, and
only where the morgue's own tag disagrees with it (`prove_judge_clear.py` R1–R4).

### The morgue

`morgue/` holds frames **Rafe personally culled at the device gate**, with his verbatim words and
the commit whose build produced them. `MORGUE.json` records a sha256 per entry and the runner
refuses to start if any file has changed — a plant that can be edited is a plant that can be
softened.

Entries are **tagged by surface** and the deck draws a plant of the round's surface. A floor plant
in a wall round gets caught for its magenta wall mocks: the right image flagged for the wrong
reason, which is not a control at all.

**Adding an entry** is the correct response to any device-gate cull. One file, one block in
`MORGUE.json`, with the quote and the commit.

---

## 4a. Two laws about the plant — RULED 2026-09-08

**An axis is the cull's percept, never its mechanism.** `replaced-tiles-lane.png` was tagged
`tonal` because it was *made* by restoring `POLISH_LANE_GAIN` to 1.9. What Rafe saw was that the
stones had stopped being the same stones — material identity, which is `value`. Tag what the eye
rejected, never the knob that produced it: a seat is never asked about the knob.

**A seat voided by a mis-tagged plant is re-drawn, not the round; a correct plant missed still
voids.** `--redraw-seat N` implements it and is fenced: the frame's sha must not have moved,
nothing is re-captured, the other seats are carried unchanged and never re-parsed, the ballot is
written to `-seat{N}-redraw.txt` so no transcript is overwritten, and an existing re-drawn ballot
is REUSED — the no-re-roll check runs **before** the seat is spent, because the first version ran
after it and guarded nothing. The re-draw is recorded by an added artifact, `SEAT-REDRAWN.json`.

⚠ **The softness control is untouched.** A seat that misses a plant correctly tagged for the axis
it was asked about is soft, and softness voids the round. That is the only control the mechanism
has, and nothing here weakens it.

## 4b. The autonomy amendment — what stops the run, and what does not

**RULED (Rafe, 2026-09-08).** LOOP-PROCESS §1.1.4 is amended and §1.1.5 added. There are now
**three** reasons to return to a human mid-run:

1. **a one-way door** — canvas, projection (§3), palette lock (§5.1), rig ratification (the
   Ruling 56 family), or a **landing** at the surface gate;
2. **the bible is silent or self-contradictory** on a question the round needs answered — a
   genuine gap, quoted;
3. **broken judge** (the plant missed twice) **or budget exhausted**.

Everything else is the builder's, under the bible:

| was escalated | now |
|---|---|
| a flip touching a ruled system | **ruled BY the clause**, by the builder, citation recorded. *"This touches §X"* is an answer |
| build-id drift, pin staleness, plant axis mismatch, hash exclusions, guard scope | **engineering.** Fix under existing law, record, continue. Rafe never hears about a hash |
| routing a flag | **the builder routes**, with a citation the gate verifies |
| a PASS-INSTALL | **installs as `latest`.** Builds queue; Rafe walks whichever is current |
| a single item's STOP | **write the report and continue to the next item** |

**Why: escalation is free for the machine and expensive for the human.** A loop that is uncertain
drifts toward asking unless that asymmetry is corrected, and the run of 2026-09-07/08 stopped for
a hash exclusion, a value pin and a routing decision — none of which needed a human, each of which
cost a night.

⚠ **Nothing here weakens a guard.** The plants, the panel, the progress guards and the citation
checks are what make an unattended run safe, and §4's rule is unchanged: no check's pass counts
until it has demonstrated it can fail. `prove_gate.py` case **J1** is the amendment working — a
flip citing a resolvable clause disposed with no human anywhere in it — and **J2/J3/J4/J5** are
the guard that makes J1 safe: an unresolvable citation, a bare assertion, a missing destination,
and a `CLOSED` attempted by citation all still refuse.

## 5. The loop guards — they measure progress, not rounds

The line stops rather than grinding, **and it does not stop a lane that is working.** Every stop
writes `STALL-REPORT.md` and is a **LOOP-PROCESS §1.1.4 ruling trigger** with that report as its
evidence.

### The signal

**Where the build ranked** in that round's blind shuffled deck, against the asset bar, the last
Rafe-approved frame and the plant. Normalised so decks of different sizes compare:

```
rank_score = (deck_size − rank_position) / (deck_size − 1)      1.00 first, 0.00 last
```

It costs nothing extra — the round already produces it — and it is a judgement about the picture
rather than about the apparatus. **A round count is not.** The five-round park it replaces counted
rounds, which is the wrong quantity in both directions at once: five rounds that are getting
somewhere should keep going, and two that are not should already have stopped.

**The seat is never told the round number, the history, or that anything is being tracked.** Not
in the prompt, not in the deck, and not in the path it sits in — the working directory is named by
a hash, because it used to be named `<lane>-r7` and that is the seat's own cwd.

### The guards, in the order they are checked

| guard | fires when | why the order |
|---|---|---|
| **broken judge** | the plant is missed twice running | nothing past it is readable, so every guard below would be reasoning about rounds §4 forbids reading |
| **no change** | two consecutive FAILs whose delivered frames are within **2 bits of a 256-bit perceptual hash** | the cheapest true statement available: the fix did not reach the picture at all |
| **thrash** | the same flip item across two consecutive FAILs **and no movement in rank** | the same request twice, with nothing to show for it |
| **stall** | **3** readable rounds with no new best rank | matching the best is not progress; the lane is not converging |
| **ceiling** | **15** rounds on the lane | the backstop, and it should never be the one that fires |

**`two strikes` stays, as the builder's judgement overlay — reported, never a stop.** A flip item
can legitimately survive a round the build won on every other axis, and stopping there sends a
ruling to Rafe about a lane that is working. That is what `thrash` adds the rank condition for:
same substance, one more condition, and the condition is exactly what separates a stuck lane from
a busy one. When the advisory speaks and the guard does not, the runner says so and the builder
decides.

A **VOID** round's rank is not evidence — §4 says its findings are not read, and that has to
include its rank — so void rounds are excluded from the progress series. They still count toward
the ceiling: they consumed a round.

**THE SERIES IS THE ITEM UNDER WORK, NOT THE LANE — LAW (Rafe, 2026-09-08).**

> *"Progress-guard scope = the item under work, not the lane; the routed-PASS record cannot cap
> future items."*

A PASS state **closes an item**; rounds after it belong to the next one and do not inherit the
closed one's record. Without this the guards saturate: a `PASS-WITH-ROUTED-ITEMS` at rank 1.00
with zero unresolved flips sets `(1.00, shipped, 0)` — the arithmetic maximum a deck can produce
— and **nothing can ever beat it**, so the stall guard was certain to fire three readable rounds
later however good the following work was. That is bible §13.11 a fourth time, in the guard
§13.11's second instance was written into.

The cut is **derived from the verdicts on disk**, at the last PASS. It is not a counter reset and
nothing is deleted — the same law the park-clear runs on.

### The series is in the verdict files

Every verdict carries `progress`, including the **whole rank series to date** and each round's
perceptual hash. Not merely derivable by walking `history/` — written into the file, so it is in
the diff, in the PR, and in the stall report. **A counter a restart can clear is a suggestion with
a number in it**, and so is one that lives only in a directory listing. Clearing this means
deleting committed files.

**When a guard fires: write nothing else, run no further rounds, end the turn.** Say plainly that
the line stopped, which guard fired, and where the report is. Do not summarise the report away —
Rafe reads it.

---

### PASS-INSTALL — what a PASS means once a reference is seeded

**LAW (Rafe, 2026-09-08).**

> *"PASS for polish rounds against a seeded reference = rank above `approved_capture` ∧ no
> unrouted flags → PASS-INSTALL; SHIP stays recorded as the wowed signal, not the install gate —
> the seeded reference is the human-ratified install bar."*

**THE REASON, and it is why this is not a loosening: SHIP∧rank was ratified against a NULL
REFERENCE.** Every round the combined lane ran before 2026-09-07 recorded *"NO APPROVED FRAME IN
THE DECK — that half of the bar is untested this round."* Half the comparative bar was missing
since the lane began, so **SHIP was the only thing standing between a build and the phone and it
had to carry the whole gate alone.** §4's own impeachment says what that cost: a blind seat's
ordering does not reproduce the human gate's, and SHIP is a stranger's answer to *would you put
this in front of a paying player and defend it* — a question about finished work, not about
whether a build may be walked.

With a serviceable reference seeded, the deck contains **a frame the human gate has already
ratified as installable**. Beating it gates the build *above the bar it measures against*, which
is a stronger claim than the null-reference rule could make and a different one from SHIP.

| | gates the install | recorded |
|---|---|---|
| **rank above `approved_capture`** | **yes** — the human-ratified bar | always |
| **no unrouted flags** | **yes** | always |
| SHIP | no — it is the *wowed* signal | always, in the verdict |

**It REQUIRES the reference.** With `approved_capture` null there is nothing to be above, and the
rule falls back to the ratified SHIP∧rank conjunction — the state it was written for. A gate that
silently weakens when its comparator goes missing is the failure this whole mechanism exists to
prevent, so `critic_gate.py` refuses a PASS-INSTALL whose deck carried no reference rather than
treating the absence as a pass.

**"No unrouted flags" is evaluated at two points.** At round time it means *the seat did not flag
the build*; a flagged build is a FAIL. Only the human gate can route a flag, by amending the
verdict with a quoted ruling and a named destination per item — and `critic_gate.py` re-derives
both conditions from the verdict's own recorded numbers rather than trusting the label, because a
verdict that merely *says* PASS-INSTALL proves nothing.

> ### ⚠ IMPEACHED ON THE DAY IT WAS RATIFIED — IMPEACHMENT UPHELD, RULE REFINED
>
> **The rank term is not stable across seats on an unchanged picture.** Lane `polish-c-183` judged
> **the same frame twice** — build sha `839fb12f`, "picture moved mean 0.000 / worst 0":
>
> | round | build rank | reference rank | verdict under this rule |
> |---|---|---|---|
> | r001 | **1 of 4** | 2 | PASS-INSTALL |
> | r002 | **2 of 4** | 1 | FAIL |
>
> The build and the reference **swapped places with no pixel changing**. §4 already records that a
> blind seat's ordering does not reproduce the human gate's; this is the sharper version — the
> ordering does not reproduce *itself*.
>
> **The rule is NOT re-tuned here.** LOOP-PROCESS §8: a bar found wanting mid-run is held frozen,
> cleared honestly and impeached in the same report, never adjusted once the answer is visible.
> It is recorded so that whoever rules on it next is ruling on the evidence:
>
> - PASS-INSTALL rests entirely on rank, and rank flipped on a re-run.
> - The old SHIP∧rank rule was not exposed to this, because SHIP was the binding term and rank
>   only ever *added* a condition. Making rank the sole discriminator moved the whole gate onto
>   the least stable thing the deck produces.
> - The safe direction is unchanged: the gate refuses more often than it opens, and a build that
>   flips to FAIL on a re-run simply does not install.
>
> **What it does not touch:** the plant, which caught on both rounds, and the two SHIP terms,
> which are recorded either way.
>
> ### THE RULING ON THE IMPEACHMENT (Rafe, 2026-09-08) — UPHELD, AND REFINED
>
> > *"PASS-INSTALL rests on a single rank sample and flipped on identical bytes — impeachment
> > upheld. Refine: majority of three independent blind seats rank the build above
> > `approved_capture`, no unrouted flags from any; each seat its own axis-matched plant.
> > Measure the comparator's noise floor: same bytes through five seats, record the flip rate,
> > publish it as rank's error bar."*
> >
> > **LAW: a gate's binding term must have a measured noise floor and must never be a single
> > sample.** (bible §13.13)
>
> **Run it with `--seats 3`.** Each seat gets its own working directory, its own shuffle and its
> own plant draw; the directory is a hash of (lane, round, build, seat) for the same reason the
> round number is hashed — a seat that can read *"seat 2 of 3"* off its own cwd can infer it is
> one of a panel.
>
> **The two terms are asymmetric and the asymmetry is the ruling:**
>
> | term | why | rule |
> |---|---|---|
> | **rank** | the noisy one — it flipped on identical bytes | **majority** of seats |
> | **a flag** | a *finding*; one seat seeing it is enough | **any** seat disqualifies |
> | **the plant** | §4 refuses to read a soft seat's ballot at all | **every** seat must catch its own |
>
> More seats make the plant condition *harder*, never softer. `prove_panel.py` drives the real
> `panel_verdict` through all of it — a lost majority, a lone flag against a unanimous rank, a
> single missed plant, and a deck with no reference — because a new gating rule's pass counts for
> nothing until it has been shown to refuse (§13.5).
>
> ⚠ **A panel is not independence for free.** Where the axis-matched morgue set has one member —
> which is the case for `combined`/`tonal` today — every seat draws the *same* plant and their
> catches are **correlated**. The panel multiplies the rank samples, not the plant's evidence.
> The round prints this rather than assuming it away.

**Proved before it was believed** (§13.5). `prove_gate.py` drives the real gate through the three
ways the state can fail — no reference in the deck, build below the reference, an item left
undispositioned — as well as the two ways it opens. A new state's pass counts for nothing until
it has been shown to refuse.

### PASS-WITH-ROUTED-ITEMS — the third lawful verdict state

**LAW (Rafe, 2026-09-03).** *"Valid only with a quoted Rafe ruling and a named destination lane —
builder can never route."* Implemented in `critic_gate.py`.

A FAIL can contain items **no round on this lane can ever discharge.** The wall lane's r003 asked
for *objects* — rope, driven pins, salvaged timber standing in the scene — and the review scene
has no prop system at all. Grinding that lane produces nothing. The human gate routes the item to
the lane that owns it and the build goes to the walk.

**Every flip must carry a disposition.** A state that discharges some items and stays silent about
the rest is a FAIL wearing a better name.

| state | means | requires |
|---|---|---|
| `ROUTED` | another lane owns it | a quoted ruling **and** a named destination lane |
| `CLOSED` | ruled not to be chased | a quoted ruling |
| `PARKED` | first-time item, awaiting Rafe eye on the walk | a quoted ruling |

> ### ⚠ SUPERSEDED IN PART — THE BUILDER NOW ROUTES, BY VERIFIED CITATION
>
> **RULED (Rafe, 2026-09-08):** *"CC routes flags to issues with verified citations. Routing is no
> longer a human-only act; the citation verifier is the laundering guard. Rafe audits the routing
> table at the walk."*
>
> The sentence below — *the builder can never route* — was written when the only guard available
> was a human recognising his own words. There is a machine guard now: `critic_gate` refuses a
> `ROUTED` whose citation does not **resolve** — a clause must exist in the bible or the process
> law, an issue must resolve in the record. **A routing nobody can look up is refused by the gate
> rather than by someone's memory**, which is a stronger guarantee than the signature it replaces,
> not a weaker one.
>
> `CLOSED` and `PARKED` are **unchanged and still need Rafe's words**, and the distinction is
> exact: routing says *this belongs over there*, which is checkable; closing says *a human decided
> not to chase this*, which is not.
>
> What survives from the paragraph below is its principle, and it still does the work: **the
> enforcement is visibility.** Every disposition is printed at the gate and stamped onto the
> handset, and the routing table is audited at the walk.

**The builder can never route** *(superseded above for `ROUTED`; still true of `CLOSED` and
`PARKED`)*, and the enforcement is not a signature — it is **visibility**.
Every disposition is printed at the gate and stamped onto the handset, so a routing the builder
invented is a quote Rafe does not recognise, on his own screen, while he is holding the build.

### A historical report never gates. Only a LIVE guard does.

**LAW (Rafe, 2026-09-03).** *"A historical report must never gate installs; only a live guard
does."*

`critic_gate.py` used to refuse whenever `STALL-REPORT.md` existed. **A file's existence is not a
fact about the line.** The floor lane's five-round-park report sat committed on main *after that
guard had been retired* — the park was replaced by these progress guards — and went on blocking
installs for **every lane**, including lanes that had never stalled and any combined build. It
also broke the gate's own proof: `prove_gate.py` failed *"PASS verdict for this build → allow"*
and *"a gated build's marker carries no stamp"* for no reason but a stale document on disk.

The gate now recomputes the guards from the verdict files and refuses only if one is firing
**now, on this lane**. A report is the record of a ruling trigger; **records do not gate.** Stale
reports belong in `history/`, kept, not deleted.

⚠ **A gate that cannot evaluate its guards still refuses.** *Could not check* is not *clear*.

Both directions are proved every run — `E4` plants two consecutive VOIDs and requires the gate to
name the live guard; `E5` removes them and requires it to open again. Removing a blocking
condition without showing that its replacement bites is how a gate quietly stops being one.

### A guard clears by an ADDED artifact, never by removing evidence

**LAW (Rafe, 2026-09-03).** The counters are derived from the verdict files on disk, which means
the only way to clear one is to delete them — and **a mechanism whose reset is *destroy the
record* teaches exactly the wrong reflex.** The lane that most wants a guard gone is the lane
holding the evidence against itself.

So nothing is ever deleted. A ruling that clears a guard is written as an **added** artifact at
the repo root (`PARK-CLEARED.json` is the worked example) naming the lane, the rounds it covers,
and the ruling verbatim; the guard then counts rounds **after** the marker. It **cannot clear a
broken judge** — a soft critic is not a lane problem, and no ruling about a lane makes an
unreadable verdict readable.

## 6. The install gate

`critic_gate.py` is the one implementation, with two callers: `build_review_app.sh` runs it before
it exports, and a PreToolUse hook (`.claude/hooks/critic_install_guard.sh`) runs it against any
shell command that would put a build on a device. Two callers, one implementation — a gate
reimplemented in a second place has two behaviours and the second one is always the lenient one.

It requires **CRITIC-VERDICT.json to exist, to match this working tree's build id exactly, and to
read PASS.** The build id folds in the commit, every tracked change and every untracked file, so
a recomposed family moves it even when the commit does not.

**The override is visible:**

```
YARL_SKIP_CRITIC=1 tools/tier1_walls/device.sh build
```

installs, and stamps `SKIPPED-REVIEW` into the review marker. The app **draws it on screen** and
reports it in its `BUILD IDENTITY` line for as long as that build is on the handset. It exists for
producing a build to *measure*; nothing walked on a SKIPPED-REVIEW build is a gate verdict. An
override nobody can see from the phone is the same as no gate.

---

## 7. Running a round

1. Build the thing. Aim with instruments as much as you like.
2. `run_frame_critic.sh`.
3. **PASS** — the round ends. `CRITIC-VERDICT.json` goes in the PR diff and is named in the PR
   body. The device gate is next, and it is Rafe's.
4. **FAIL** — apply the flip list, run again. Automatically, without returning to a human
   (§1.1.2). This is the loop. Read the `== progress` block: it says where the build ranked,
   whether that is a new best, and how far the picture actually moved. **A round that moved the
   picture very little is a round to look at before spending another seat** — the no-change guard
   will say so eventually, but it needs two rounds to say it and you have the number now.
5. **VOID** — stop. The judge is what failed, not the art. Nothing from that round is evidence,
   including its rank. Check whether the plant is actually in the picture the seat saw.
6. **STOP** — end the turn and hand `STALL-REPORT.md` to Rafe.

If the runner prints the **two-strikes advisory** and does not stop, that is working as intended:
the same request survived, but the build moved in the deck. Decide whether to keep going. That
judgement is yours; `thrash` only takes it out of your hands when the rank has stopped moving too.

`--no-capture` replays the round on the frame already on disk. `--build-frame <path>` puts a
chosen frame in the build's slot — that is the judge's own self-test, it runs on its own
`-selftest` lane so it cannot touch a real lane's progress series, and its verdict is marked
`self_test` and can never open the install gate.

`--check-guards --history <dir> --lane <name>` evaluates the guards against a history without
running a round or spending a seat. It is how the fixtures in `evidence/guard-fixtures/` drive
the real `guards()` rather than a copy of it.

---

## 8. Configuration

`docs/FRAME-CRITIC.json` — surface, capture command, crop, asset bar, approved capture. Editing
it is how a session moves the critic to a new surface. **Nothing content-specific lives in this
skill**, so that a content change has nothing here to break.

`approved_capture` is **read off the record, never chosen.** It is the frame Rafe approved, with
his words and the commit. If the record contains no approval for a surface, it is `null` and the
deck runs three frames — picking one yourself means choosing the baseline your own work is judged
against, which is the conflict the comparative frame exists to remove.

---

## 9. Proving it still works

The mechanism gets the discipline it enforces. Three scripts, none of which reimplements what it
tests — they drive the real gate, the real guards and the real build id:

```
python3 .claude/skills/frame-critic/prove_gate.py       > /tmp/out.txt 2>&1   # 12 cases
python3 .claude/skills/frame-critic/prove_build_id.py                        # 3 properties
python3 .claude/skills/frame-critic/frame_critic.py --check-guards \
        --lane guard-two-strikes \
        --history .claude/skills/frame-critic/evidence/guard-fixtures/two-strikes
```

⚠ **Do not redirect a proof's output into the repo.** Any untracked file written into the tree
while a check is running moves the build id under it, and cases go red — the mechanism working
correctly and the test being wrong. Write outside the tree and copy the transcript in.

Results and what they found are in `evidence/PROOF.md`. Run them after touching anything in this
directory.

## 10. What this does not do

It does not gate non-art work. It does not judge assets on a contact sheet — every frame in the
deck is a delivered frame from the production renderer, lit, at device pixel size (§2.1). It does
not land anything: **a critic PASS ends a round; only Rafe's walk lands an asset** (§1).
