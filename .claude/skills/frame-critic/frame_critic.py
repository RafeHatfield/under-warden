#!/usr/bin/env python3
"""THE FRAME CRITIC — an art round is judged by eyes on delivered frames, and by nothing else.

    .claude/skills/frame-critic/run_frame_critic.sh

WHAT THIS IS
------------
A fresh blind `claude -p` seat is shown a small deck of finished frames — this build's capture,
the asset bar, the last frame Rafe approved, and ONE PICTURE-PLANT drawn from the morgue — and
asked to rank them, say which it would ship, and flag anything with an obvious defect. The deck
is shuffled and unlabelled. The seat gets no code, no coordinates, no thresholds, no bible.

    PASS   the seat would ship this frame, flagged no defect in it, AND ranked it at or above
           the last Rafe-approved frame and near the asset bar (§5's visual bar as a rank).
           Reachable at any round; the guards below never gate it.
    FAIL   any of those missing; the flip list is recorded verbatim
    VOID   the seat did not catch the plant. Findings are NOT READ (LOOP-PROCESS §4)

WHY IT IS BUILT THIS WAY — two measured collapses, both of the review layer, both the same shape
-----------------------------------------------------------------------------------------------
1. THE INSTRUMENTS BECAME THE JUDGE. The wall gate of 2026-08-27: every instrument green, and the
   phone still said no. A device gate FAIL against a fully green instrument set is not a tuning
   miss — it says the thing being measured and the thing being judged had come apart.

2. THE PLANT STOPPED BEING IN THE PICTURE. Wall rounds 9 and 10 both went VOID because the
   generated plant differed from the family in 0.54% of pixels: since the cap pass, the cell's
   base is a cap window and the wall family's top tiles are never drawn, so ruining the wall tiles
   ruined almost nothing. The control was downstream of the engine, so an engine change silently
   neutralised it. Rounds 3 and 6 died the same way for a different reason.

Both collapses are apparatus failures, and both would have been survived by a mechanism with
nothing in it that can break. So:

    THE JUDGE IS EYES ON PICTURES. There is no threshold in it to drift, no metric to
    outcompete a clause that has no number, and no code path between the build and the verdict
    except the capture itself.

    THE PLANT IS A PICTURE. A known-bad frame Rafe personally culled, kept as bytes in
    `morgue/`. An engine change cannot neutralise a picture. The only way to disarm one is to
    delete the file, and that is a visible diff.

    INSTRUMENTS ARE BUILDER'S TOOLS. They are welcome, they are useful for aiming between
    rounds, and they gate nothing. Every measure_*.py in this repo stays exactly where it is.

THE LOOP GUARDS — this must never grind, and it must not stop a lane that is working
------------------------------------------------------------------------------------
THEY MEASURE PROGRESS, NOT ROUNDS. The five-round park counted rounds, which is the wrong
quantity in both directions at once: five rounds that are getting somewhere should keep going,
and two that are not should already have stopped.

The signal is the one every round already produces at no extra cost — WHERE THE BUILD RANKED in
the blind shuffled deck against the bar, the last approved frame and the plant. It is a judgement
about the picture, and the seat cannot see it coming: it is never told the round number, the
history, or that anything is being tracked. Even the working directory it sits in is named by a
hash now, because it used to be named `<lane>-r7`.

    BROKEN JUDGE  the plant missed twice consecutively -> STOP, and never ship past one
    NO CHANGE     two consecutive FAILs whose delivered frames are within NO_CHANGE_MAD /
                  NO_CHANGE_MAX luminance levels of each other -> STOP. The fix did not
                  reach the picture at all.
    THRASH        the same flip item across two consecutive FAILs AND no movement in rank
                  -> STOP
    STALL         STALL_ROUNDS readable rounds with no new best rank -> STOP
    CEILING       ROUND_CEILING rounds, absolute backstop -> STOP

    TWO STRIKES   the same flip item across two consecutive FAILs, rank movement ignored.
                  ADVISORY — reported and recorded, never a stop. It is the builder's
                  judgement overlay: a flip item can survive a round the build won on every
                  other axis, and stopping there sends a ruling about a lane that is working.

Every STOP writes STALL-REPORT.md and is a §1.1.4 ruling trigger with that report as its
evidence. THE COUNTERS ARE DERIVED FROM THE VERDICT FILES ON DISK, not held in memory, so
restarting a session cannot reset them — the only way to clear a counter is to delete committed
files, which shows up in a diff.

EXIT CODES
    0 PASS   1 FAIL   2 VOID   3 STOP (a guard fired)   4 precondition/usage
"""
import argparse
import datetime
import hashlib
import json
import os
import random
import re
import shutil
import subprocess
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, HERE)
import build_id as BID                                    # noqa: E402

CONFIG = os.path.join(REPO, "docs", "FRAME-CRITIC.json")
MORGUE = os.path.join(HERE, "morgue")
HISTORY = os.path.join(HERE, "history")
VERDICT = os.path.join(REPO, "CRITIC-VERDICT.json")
STALL = os.path.join(REPO, "STALL-REPORT.md")

# ── THE GUARDS' NUMBERS, DECLARED HERE BEFORE ANY ROUND RUNS ──────────────────────────────────
# LOOP-PROCESS §8: a bar is never re-tuned after the answer is seen. These are the bar.
#
# THE GUARDS MEASURE PROGRESS NOW, NOT ROUNDS. The five-round park counted rounds, which is the
# wrong quantity: five rounds that are getting somewhere should keep going, and two that are not
# should already have stopped. A round cap can only be wrong in both directions at once.
#
# The signal is the one the round already produces: WHERE THE BUILD RANKED in a blind shuffled
# deck against the asset bar, the last Rafe-approved frame, and the plant. It costs nothing extra,
# it is a judgement about the picture rather than about the apparatus, and the seat cannot see it
# coming — it is never told the round number, the history, or that anything is being tracked.
STALL_ROUNDS = 3         # readable rounds with no new best rank, before the line stops
ROUND_CEILING = 15       # absolute backstop, all rounds counted including void ones
JUDGE_MISSES = 2         # consecutive plant misses before the judge is called broken
TWO_STRIKES = 2          # consecutive FAILs carrying the same flip item — ADVISORY, see guards()
FLIP_SAME = 0.60         # Jaccard over content words at which two flip items are "the same item"

# The no-change floor lives with the signature it is measured on — see SIG_GRID below. Two
# captures within it are the same picture, and a FAIL round on the same picture as the last FAIL
# round is a round that was never going to say anything new: §4.2's shape exactly, a fix that
# runs, changes nothing, and says so quietly.

# How far below the asset bar still counts as "near the bar" for a PASS. LOOP-PROCESS §5 asks
# *which of these looks like the shipped game* and requires the answer to be *Yarl or a tie*; the
# deck forbids ties, so one place below the bar is the closest representable tie. Declared before
# any round and not widened after (§8).
NEAR_BAR_SLACK = 1

# Words that carry no content for the purpose of asking whether two flip items are the same one.
_STOP_WORDS = set("""a an the and or but if then than that this these those it its is are was
were be been being to of in on at by for with from as into over under across along up down out
off not no nor so such very more most less least much many few some any each every both either
neither only just also too do does did doing done have has had having make makes made making
should would could can may might will shall must there their them they you your we our i me my
""".split())


# ================================ verdict history, and the guards =============================
def history(where=None):
    """Every verdict this lane has recorded, oldest first.

    ON DISK BY DESIGN. A counter held in a variable resets when a session restarts, and a guard
    that a restart clears is not a guard — it is a suggestion with a number in it.
    """
    HISTORY_ = where or HISTORY
    if not os.path.isdir(HISTORY_):
        return []
    out = []
    for name in sorted(os.listdir(HISTORY_)):
        if not name.endswith(".json"):
            continue
        try:
            out.append(json.load(open(os.path.join(HISTORY_, name))))
        except Exception:
            continue
    out.sort(key=lambda v: v.get("timestamp", ""))
    return _apply_ruling_voids(out)


def _apply_ruling_voids(verdicts):
    """A round RULED VOID reads as VOID — in memory, never on disk.

    RULED (Rafe, 2026-09-13, JUDGE RULING lane art/jamb-211): "Runner seat count is five, never
    one; the two one-seat rounds are recorded as VOID and count for nothing." Those rounds sit
    on disk as INSTALL-LATEST (a single sample, §13.13) and nothing is deleted or rewritten —
    JUDGE-CLEARED.json names them under `rounds_voided` with his words, and every reader of the
    history (the guards, the progress series, the stall report) sees them as VOID from here. A
    marker with no quoted ruling voids nothing."""
    if not os.path.exists(JUDGE_CLEAR):
        return verdicts
    try:
        m = json.load(open(JUDGE_CLEAR))
    except Exception:
        return verdicts
    for entry in (m if isinstance(m, list) else [m]):
        if not isinstance(entry, dict) or not (entry.get("ruling") or "").strip():
            continue
        voided = {r for r in (entry.get("rounds_voided") or []) if isinstance(r, int)}
        for v in verdicts:
            if v.get("lane") == entry.get("lane") and v.get("round") in voided \
                    and v.get("verdict") != "VOID":
                v["verdict_on_disk"] = v.get("verdict")
                v["verdict"] = "VOID"
                v["voided_by_ruling"] = entry["ruling"]
                # "count for nothing": not a VOID in the broken-judge streak either. It keeps
                # its round number (numbering is derived from the count on disk) and drops out
                # of every guard's and series' view via lane_rounds().
                v["counts_for_nothing"] = True
    return verdicts


def lane_rounds(hist, lane):
    """The lane's rounds that COUNT — everything on disk for the lane, less the rounds a ruling
    said count for nothing (still numbered, still in the diff, never read by a guard)."""
    return [v for v in hist if lane_of(v) == lane and not v.get("counts_for_nothing")]


def lane_of(v):
    return v.get("lane")


# ================================ a ruling that clears a guard =================================
#
# SKILL.md §5, LAW (Rafe, 2026-09-03): **a guard clears by an ADDED artifact, never by removing
# evidence.** The counters are derived from the verdict files on disk, so the only way to clear one
# by subtraction is to delete them — and a mechanism whose reset is *destroy the record* teaches
# exactly the wrong reflex, in the lane that most wants the guard gone.
#
# The convention was documented from the day the law was written and NOTHING READ IT (#182). It
# cost a gate on 2026-09-06: a park marker was written, correctly, naming the lane, the guard, the
# rounds and the ruling verbatim — and the guard stopped the line anyway, because no code looked.
# A clear artifact nobody reads is a mechanism that only looks like one, and it is worse than none,
# because the next person writes it and believes the guard is cleared.
#
# WHAT A CLEAR DOES, AND THE LIMIT OF IT:
#
#   it EXCLUDES the named rounds from ONE named guard's evaluation, on ONE named lane.
#   it DELETES nothing — the verdicts stay on disk and in the diff, and every other guard still
#     counts them, because a ruling about why `no-change` fired says nothing about `stall`.
#   it SILENCES nothing — a cleared guard still fires on rounds AFTER the marker. That is the
#     whole distinction between clearing history and disabling a check, and it is the property
#     the positive control exists to prove (§13.5: a check that cannot fail does not count).
#
# ⚠ AND IT CANNOT CLEAR A BROKEN JUDGE. RULED (Rafe, 2026-09-06): *a proven-blind judge is never
# laundered by a park.* `broken-judge` means the picture-plant was missed twice running, so no
# round past it is readable — and a ruling about a LANE cannot make an unreadable verdict readable.
# The refusal is enforced below rather than left to whoever writes the next marker.
PARK = os.path.join(REPO, "PARK-CLEARED.json")
JUDGE_GUARD = "broken-judge"

# ── AND THE ONE THING THAT *CAN* CLEAR A BROKEN JUDGE, BECAUSE IT PROVES THE JUDGE WORKS ──────
#
# The law above stands exactly as written: a PARK can never clear `broken-judge`, because a
# ruling about a LANE cannot make an unreadable verdict readable. What that law is protecting
# against is a builder parking their way past a judge that is actually blind.
#
# It does not reach the case that actually arose. RULED (Rafe, 2026-09-11):
#
#     "STOP cleared by ruling (added artifact, this quoted): the judge is proven working on two
#      tonal plants at 100%; crushed-midband is retired as a device-gate cull that seats are
#      §13.2-blind to — not a plant. Dark-side tonal coverage is an open gap, not a blocker."
#
# The VOIDs were not a blind judge. They were a bad CONTROL: one plant that no seat caught in a
# deck, and that three fresh seats ranked FIRST OF FOUR when it was put in the build slot. The
# judging layer caught every correctly-formed plant it was dealt, 3 of 3.
#
# ⚠ SO THIS CLEARANCE IS NOT TRUSTED, IT IS CHECKED. `JUDGE-CLEARED.json` names the rounds and
# carries Rafe's words, and `judge_cleared()` below RECOMPUTES the evidence from the history
# before honouring it: every plant dealt in those rounds must either have been caught by every
# seat that drew it, or be retired in the morgue. A clearance whose own rounds contain a live
# plant that a seat missed clears nothing. That keeps the 2026-09-06 law's protection intact —
# a genuinely blind judge cannot be laundered by this file either, because the file's premise is
# re-derived rather than asserted.
JUDGE_CLEAR = os.path.join(REPO, "JUDGE-CLEARED.json")


def judge_cleared(lane, tail, morgue=None):
    """Rounds a RULING has excused from `broken-judge`, having re-proved the judge on them.

    Returns the set of round numbers to exclude. Empty unless the marker exists, names this
    lane, and the history agrees with it.
    """
    if not os.path.exists(JUDGE_CLEAR):
        return set()
    try:
        m = json.load(open(JUDGE_CLEAR))
    except Exception:
        return set()                      # a malformed marker clears nothing, by accident or not

    retired = set()
    regimes = {}
    if morgue:
        for e in morgue.get("entries", []):
            if e.get("retired_as_control"):
                retired.add(e["file"])
            if e.get("regime"):
                regimes[e["file"]] = e["regime"]

    out = set()
    for entry in (m if isinstance(m, list) else [m]):
        if not isinstance(entry, dict) or entry.get("lane") != lane:
            continue
        if not (entry.get("ruling") or "").strip():
            continue                      # no quoted ruling, no clearance
        covered = {r for r in (entry.get("rounds_covered") or []) if isinstance(r, int)}
        if not covered:
            continue
        # RE-DERIVE THE PREMISE. Every seat in every covered round either caught its plant, or
        # the plant it drew is retired in the morgue. One live miss and this clears nothing —
        # unless the marker invokes the transitional clause below.
        #
        # ── THE TRANSITIONAL CLAUSE — RULED (Rafe, 2026-09-11) ────────────────────────────────
        #
        # The seat-level re-scope supersedes "a correct plant missed still voids [the round]"
        # (2026-09-08). Rounds already on disk were voided under the OLD rule, and some of them
        # were voided for something that is no longer a round-level fault at all: ONE seat
        # missing ONE live plant. Under the new rule that discards a BALLOT and re-draws the
        # SLOT.
        #
        # So a marker carrying `supersedes` may excuse such a round — but only the exact shape
        # the new rule would have handled: EXACTLY ONE live miss. Two or more is broken-judge
        # under both the old rule and the new one, and no marker excuses it. The re-draw's own
        # outcome then decides: if it misses, the seat-level term raises broken-judge on the
        # spot, which is the check this clause hands the decision to rather than pre-empting.
        supersedes = (entry.get("supersedes") or "").strip()
        # ── A SEAT-BLIND AXIS IS NOT A BROKEN JUDGE — RULED (Rafe, 2026-09-12) ─────────────
        #
        #     "not a broken judge — a seat-blind axis (§13.2). Seats compared a shadowed build
        #      to unshadowed plants and reference and measured exposure, not craft. ... New law:
        #      plants and reference are captured under the deck's lighting regime — scene, rig,
        #      AND shadow state."
        #
        # A marker naming `deck_regime` excuses a missed plant whose MORGUE TAG carries a
        # different regime: the control was captured under other light, so the seat was never
        # asked the question. Re-derived from the morgue, not asserted — a marker cannot excuse
        # a plant that is tagged for the deck's own regime, and an untagged plant is never
        # excused this way.
        deck_regime = (entry.get("deck_regime") or "").strip()
        ok = True
        for v in tail:
            if v.get("round") not in covered:
                continue
            live = 0
            redrawn_ok = {r.get("slot") for r in (v.get("panel") or {}).get("seat_redraws") or []
                          if r.get("redraw_caught")}
            for i, seat in enumerate((v.get("panel") or {}).get("per_seat") or [], 1):
                if seat.get("caught") or i in redrawn_ok:
                    continue
                pf = seat.get("plant") or ""
                if pf in retired:
                    continue
                if deck_regime and regimes.get(pf) and regimes[pf] != deck_regime:
                    continue            # off-regime control: a deck fault, not a seat's
                live += 1
            if live == 0:
                continue
            if live == 1 and supersedes:
                continue            # the transitional clause: one live miss, re-draw pending
            ok = False
        if ok:
            out |= covered
    return out

def redraw_ruled(lane, seat, hist):
    """Has Rafe named THIS seat of the lane's last round for a re-draw, in JUDGE-CLEARED.json?
    Read, not trusted: the marker needs a quoted ruling, this lane, `redraw_round` equal to the
    lane's last round on disk, and the seat in `redraw_seats`."""
    if not os.path.exists(JUDGE_CLEAR):
        return False
    try:
        m = json.load(open(JUDGE_CLEAR))
    except Exception:
        return False
    last = [v for v in hist if lane_of(v) == lane]
    if not last:
        return False
    last_round = last[-1].get("round")
    for entry in (m if isinstance(m, list) else [m]):
        if not isinstance(entry, dict) or entry.get("lane") != lane:
            continue
        if not (entry.get("ruling") or "").strip():
            continue
        if entry.get("redraw_round") != last_round:
            continue
        if seat in {x for x in (entry.get("redraw_seats") or []) if isinstance(x, int)}:
            return True
    return False


# ================================ the human gate's own verdict =================================
#
# RULED (Rafe, 2026-09-07). §13.2 and LOOP-PROCESS §4.3 already put the human gate above the
# instrument bar; this is where that authority is written down in code instead of being available
# only as a bypass. A `PASS-WITH-ROUTED-ITEMS` says: the seat's FAIL was read, every flip in it was
# ROUTED to a named lane, and nothing is left outstanding against THIS build.
#
# It is an ADDED artifact naming its build, exactly as a guard-clearing ruling is (SKILL.md §5),
# and it is scoped as tightly as the verdict it stands beside — same build id, same round.
GATE_RULING = os.path.join(REPO, "GATE-RULING.json")


def gate_rulings(path=None):
    p = path or GATE_RULING
    if not os.path.exists(p):
        return []
    try:
        d = json.load(open(p))
    except Exception:
        return []
    r = d.get("rulings", d if isinstance(d, list) else [])
    return [x for x in r if isinstance(x, dict)]


def ruling_for(lane, rnd, path=None):
    for r in gate_rulings(path):
        if r.get("lane") == lane and r.get("round") == rnd:
            return r
    return None


def _plant_retired(plant_file, morgue):
    """Is this plant retired as a control? A retired plant's miss is a deck fault, not a seat's.

    The distinction is the whole of the 2026-09-11 re-scope. A LIVE plant missed discards that
    seat's ballot and re-draws the slot; a RETIRED plant missed says nothing about the judge at
    all, because the morgue has already recorded that no seat catches it.
    """
    if not plant_file or not morgue:
        return False
    for e in morgue.get("entries", []):
        if e.get("file") == plant_file:
            return bool(e.get("retired_as_control"))
    return False


def _morgue_for_clear():
    """The morgue, for the retirement check. Unreadable -> no retirements, so nothing clears."""
    try:
        return json.load(open(os.path.join(MORGUE, "MORGUE.json")))
    except Exception:
        return {"entries": []}


def park_clears(lane, guard, path=None):
    """Round numbers a ruling has excluded from THIS guard's evaluation on THIS lane.

    Absent, unreadable or irrelevant marker -> empty set, and the guard behaves exactly as it did
    before this existed. A malformed marker must never be able to clear anything by accident.
    """
    if guard == JUDGE_GUARD:
        return set()                      # never, whatever the marker says
    p = path or PARK
    if not os.path.exists(p):
        return set()
    try:
        m = json.load(open(p))
    except Exception:
        return set()
    out = set()
    for e in (m if isinstance(m, list) else [m]):
        if not isinstance(e, dict):
            continue
        if e.get("lane") != lane or e.get("guard") != guard:
            continue
        for r in (e.get("rounds_covered") or []):
            if isinstance(r, int):
                out.add(r)
    return out


# ================================ the progress signal ==========================================
#
# WHY RANK AND NOT A ROUND COUNT. A round count knows nothing about the work. Rank is the seat's
# own answer to the only question that matters — *of these finished pictures, where does ours
# sit* — and it is already produced by every round at no extra cost.
#
# ⚠ AND IT IS A COARSE SIGNAL, WHICH IS WHY IT GUARDS AND DOES NOT JUDGE. This mechanism's own
# evidence (§1.2.1, four rounds) is that a blind seat's ordering does not reproduce Rafe's culls:
# seats put a culled frame above the current build three times, and ranked another culled frame
# best of three. So rank decides when to STOP AND ASK — a question, never a shipping decision —
# while the verdict itself still rests on SHIP.
def rank_score(pos, n):
    """1.0 when the build ranked first, 0.0 when last. Normalised so a 3-frame deck and a
    4-frame deck are comparable — the deck grows by one the day Rafe names an approved capture,
    and a raw position would silently look like a regression that morning."""
    if not pos or n < 2:
        return None
    return (n - pos) / float(n - 1)


def prog(v):
    return v.get("progress") or {}


def readable(lane_hist):
    """The rounds whose findings may be read. A VOID round's rank is not evidence about the
    build — §4 says its findings are not read, and that has to include its rank."""
    return [v for v in lane_hist if v.get("verdict") != "VOID"]


# ── THE CAPTURE SIGNATURE, so "did this build change" is answerable a round later ─────────────
#
# sha256 answers only "byte-identical". The frames themselves are overwritten by the next round,
# so a distance has to be computable from something small enough to live in the verdict file: a
# SIG_GRID x SIG_GRID grid of mean luminance over the DELIVERED, CROPPED frame — the same pixels
# the seat was shown.
#
# ⚠ IT WAS A PERCEPTUAL HASH FIRST, AND THE HASH COULD NOT SEE THE THING IT WAS FOR. A 256-bit
# dHash put `washed-slab-lane.png` and `tile-quantized-wear.png` — two consecutive real builds
# that Rafe culled for two DIFFERENT defects — **2 bits apart, which was the whole declared
# floor.** The guard would have stopped that lane on a round where the art genuinely moved.
# Raising the hash resolution did not help: the gap held at 0.4-0.9% of bits from 256 up to 4096,
# because a gradient-sign hash asks *is this the same scene* and the answer was yes. It is the
# wrong question. Measured before the guard shipped rather than discovered by a false STOP.
#
# A magnitude answers it. Over the same three frames:
#
#     identical                       MAD 0.000   max 0
#     lane -> tile-quantized wear     MAD 1.942   max 36
#     wear -> keyline                 MAD 1.200   max 38
#
# TWO NUMBERS, NOT ONE, AND BOTH MUST BE SMALL TO FIRE. Mean absolute difference alone would miss
# a change confined to a corner of the frame — one tile of ninety moving twenty levels contributes
# about 0.2 to the mean — and a corner is exactly where a seat looks. The max-cell term is what
# stops a small, real, local change being called no change at all.
SIG_GRID = 32            # cells per side; 1024 cells, stable across grid size (measured)
NO_CHANGE_MAD = 0.25     # mean absolute luminance difference, levels. 5x below the smallest
                         # real round-over-round change measured, and determinism produces 0.
NO_CHANGE_MAX = 4        # worst single cell, levels. 9x below the smallest measured.


def signature(path, box, n=SIG_GRID):
    im = Image.open(path).convert("L")
    if box:
        im = im.crop(tuple(box))
    return bytes(im.resize((n, n), Image.BOX).getdata()).hex()


def sig_delta(a, b):
    """(mean absolute difference, worst cell) in luminance levels, or None."""
    if not a or not b or len(a) != len(b):
        return None
    x, y = bytes.fromhex(a), bytes.fromhex(b)
    d = [abs(p - q) for p, q in zip(x, y)]
    return (sum(d) / float(len(d)), max(d))


def unchanged(a, b):
    d = sig_delta(a, b)
    return (d is not None and d[0] <= NO_CHANGE_MAD and d[1] <= NO_CHANGE_MAX), d


def _words(s):
    return {w for w in re.findall(r"[a-z]+", (s or "").lower()) if w not in _STOP_WORDS
            and len(w) > 2}


def same_flip(a, b):
    """Are these two flip-list items the same request, differently worded?

    A seat rephrases. Exact string equality would report two strikes as never happening, which is
    the failure mode a guard cannot have: silently never firing. Content-word overlap is the
    cheapest thing that survives rephrasing, and the threshold is declared above, before any round.
    """
    wa, wb = _words(a), _words(b)
    if not wa or not wb:
        return False
    return len(wa & wb) / float(len(wa | wb)) >= FLIP_SAME


def shared_flip(a, b):
    """The first flip item these two rounds are both asking for, or None."""
    for x in a.get("flip_list", []):
        for y in b.get("flip_list", []):
            if same_flip(x, y):
                return (x, y)
    return None


def two_strikes_advisory(lane_hist):
    """THE BUILDER'S OVERLAY, and it is deliberately NOT a STOP.

    The same flip item surviving two consecutive FAILs used to stop the line on its own. That was
    too eager in a way worth naming: a flip item can legitimately survive a round in which the
    build got materially better on every other axis, and stopping there sends a ruling to Rafe
    about a lane that is working.

    So it is computed, reported and recorded — the builder reads it and decides — while the
    mechanical STOP is `thrash`, which is this AND no movement in rank. Same substance, one extra
    condition, and the condition is exactly the thing that distinguishes a stuck lane from a busy
    one.
    """
    fails = [v for v in lane_hist if v.get("verdict") == "FAIL"]
    if len(fails) < TWO_STRIKES:
        return None
    a, b = fails[-2], fails[-1]
    if lane_hist.index(b) != lane_hist.index(a) + 1:
        return None                      # an intervening PASS or VOID breaks the streak
    pair = shared_flip(a, b)
    if not pair:
        return None
    return dict(rounds=[a.get("round"), b.get("round")], items=list(pair))


# WHICH VERDICTS CLOSE AN ITEM — the ruling's word "PASS" meant the gate-opening states, and
# INSTALL-LATEST is one of them.
#
#     "Progress-guard scope = the item under work, not the lane." — Rafe, 2026-09-08
#     "A PASS state CLOSES an item. Rounds after it are a NEW item's rounds."
#
# The test was `verdict.startswith("PASS")`, written when every gate-opening state began with
# that word. INSTALL-LATEST — ruled into existence the same day, and the state polish rounds
# actually reach — does not, so a lane that PASSED its item still carried that item's rounds
# into the next one's guards. Caught on `polish-198-halo`: the item closed at INSTALL-LATEST on
# 3 of 3 seats not below the reference, and `no-change` was still reasoning about the rounds
# before it.
#
# ⚠ THIS IS A LIST OF GATE-OPENING STATES, NOT A LIST OF STATES I WOULD LIKE TO PASS. FAIL and
# VOID are absent and must stay absent: the guard exists to catch a lane repeating a failure,
# and a lane that has not opened a gate has not closed anything. `prove_stall_ceiling` holds
# both directions — C7 a closing verdict cuts, C8 a lane of FAILs still fires.
CLOSING_VERDICTS = ("PASS", "PASS-INSTALL", "PASS-WITH-ROUTED-ITEMS", "INSTALL-LATEST")


def closes_item(verdict):
    """Does this verdict close the item under work, so later rounds start a new series?"""
    v = str(verdict or "")
    return v.startswith("PASS") or v in CLOSING_VERDICTS


def guards(hist, lane, park=None, gate_path=None):
    """Which guard, if any, has fired. Returns (name, explanation) or (None, None).

    Checked over the lane's verdicts as they sit on disk, BEFORE the run is reported, so a session
    that restarts mid-stall walks into the same wall it walked into before.

    ORDER MATTERS AND IS NOT ARBITRARY:
      broken-judge  first — nothing past it is readable, so every other guard would be reasoning
                    about rounds whose findings §4 forbids reading.
      no-change     next — it is the cheapest true statement available: the same picture twice.
      thrash        then — the same request twice, and no movement.
      stall         then — no new best for STALL_ROUNDS readable rounds.
      ceiling       last — the backstop, which should never be the one that fires. If it is, the
                    three above did not see something they should have, and that is worth
                    knowing.
    """
    lane_hist = lane_rounds(hist, lane)

    # ── THE SERIES IS THE ITEM UNDER WORK — RULED (Rafe, 2026-09-08). ────────────────────────
    #
    #     "Progress-guard scope = the item under work, not the lane; the routed-PASS record
    #      cannot cap future items."
    #
    # A PASS state CLOSES an item. Rounds after it are a NEW item's rounds and do not inherit the
    # closed one's record — which is what stops the saturation this branch reported: a
    # PASS-WITH-ROUTED-ITEMS at rank 1.00 with zero unresolved flips sets
    # `(1.00, shipped, 0)`, the arithmetic maximum a deck can produce, and NOTHING CAN EVER BEAT
    # IT. On lane `combined` that made the stall guard certain to fire three readable rounds
    # later however good the work was. Bible §13.11 a fourth time: a progress metric whose scale
    # can top out stops measuring the work and starts measuring the ceiling.
    #
    # Cutting at the last PASS is the whole fix, and it is deliberately not a counter reset: the
    # verdicts stay on disk, the history is untouched, and the cut point is derived from them.
    # Nothing is deleted, which is the law this mechanism runs on (SKILL.md §5).
    cut = 0
    for i, v in enumerate(lane_hist):
        if closes_item(v.get("verdict")):
            cut = i + 1
    lane_hist = lane_hist[cut:]
    read = readable(lane_hist)

    # A ruling clears ONE guard on ONE lane, so the exclusion is applied per guard rather than to
    # the history as a whole — clearing `no-change` must not quietly clear `stall` as well.
    def less(guard_name, seq):
        ex = park_clears(lane, guard_name, park)
        return [v for v in seq if v.get("round") not in ex] if ex else seq

    # ── broken judge ──────────────────────────────────────────────────────────────────────────
    # A ruling may excuse specific rounds, but only by re-proving the judge on them — see
    # `judge_cleared`. The rounds themselves stay on disk and in the diff; nothing is deleted.
    excused = judge_cleared(lane, lane_hist, morgue=_morgue_for_clear())
    tail = [v for v in lane_hist if v.get("round") not in excused][-JUDGE_MISSES:]
    if len(tail) >= JUDGE_MISSES and all(v.get("verdict") == "VOID" for v in tail):
        return ("broken-judge",
                "the picture-plant was missed %d rounds running. The judging layer is broken; "
                "no round past it is readable and nothing ships past it." % JUDGE_MISSES)

    # ── no change ─────────────────────────────────────────────────────────────────────────────
    # Two consecutive FAILs on the same picture. Not "the fix did not work" — the fix did not
    # reach the frame at all, which is a different problem and needs a different answer.
    read_nc = less("no-change", read)
    if len(read_nc) >= 2:
        a, b = read_nc[-2], read_nc[-1]
        if a.get("verdict") == "FAIL" and b.get("verdict") == "FAIL":
            same, d = unchanged(prog(a).get("capture_signature"),
                                prog(b).get("capture_signature"))
            if same:
                same_bytes = (a.get("build_frame", {}).get("sha256")
                              == b.get("build_frame", {}).get("sha256"))
                return ("no-change",
                        "rounds %s and %s are the same picture — mean %.3f and worst cell %d\n"
                        "    luminance levels apart over the delivered frame%s, against floors of\n"
                        "    %.2f and %d, and both FAIL. Whatever was changed between them did not\n"
                        "    reach the capture."
                        % (a.get("round"), b.get("round"), d[0], d[1],
                           ", byte-identical" if same_bytes else "",
                           NO_CHANGE_MAD, NO_CHANGE_MAX))

    # ── thrash ────────────────────────────────────────────────────────────────────────────────
    adv = two_strikes_advisory(less("thrash", lane_hist))
    if adv:
        a = next(v for v in lane_hist if v.get("round") == adv["rounds"][0])
        b = next(v for v in lane_hist if v.get("round") == adv["rounds"][1])
        sa, sb = prog(a).get("rank_score"), prog(b).get("rank_score")
        if sa is not None and sb is not None and sb <= sa:
            return ("thrash",
                    "the same flip item survived two consecutive FAIL rounds AND the build did\n"
                    "    not move in the deck (rank score %.2f -> %.2f):\n"
                    "    round %s: %s\n    round %s: %s"
                    % (sa, sb, adv["rounds"][0], adv["items"][0],
                       adv["rounds"][1], adv["items"][1]))

    # ── stall ─────────────────────────────────────────────────────────────────────────────────
    # A new best is the only thing that counts as progress. Matching the best is not progress; it
    # is a lane holding still, and holding still for three readable rounds is the signal.
    # ── PROGRESS AT THE CEILING — RULED (Rafe, 2026-09-07). §13.11, SECOND INSTANCE. ──────────
    #
    # `rank_score` is (deck_size - position) / (deck_size - 1), so FIRST PLACE IN A THREE-FRAME
    # DECK IS 1.00 AND THERE IS NOTHING ABOVE IT. This guard demanded a NEW best and treated
    # matching as standing still — so **any lane that ever ranked first was guaranteed to STOP
    # three readable rounds later, however good the work was.** The metric saturated, and a
    # saturated instrument stops measuring the thing and starts measuring the ceiling: the same
    # law the floor-legibility guard cost us on 2026-09-07, in the progress signal this time.
    #
    # So progress is a TUPLE, and rank is only its first term:
    #
    #     (rank_score, shipped, -unresolved_flips)
    #
    #   rank_score        as before — a better place in the deck is progress, and outranks all
    #   shipped           SHIP moving NONE -> yes is progress at any rank. It is the thing the
    #                     whole mechanism is for, and it cannot be reached by ranking harder
    #   unresolved flips  at equal rank and ship, STRICTLY FEWER UNRESOLVED FLIPS is progress.
    #                     UNRESOLVED, not raw: a flip routed to a named lane has been answered,
    #                     and a round whose findings all belong to other lanes has converged even
    #                     though its list is long. The routing is an added artifact (GATE-RULING),
    #                     never the seat's own count, so a lane cannot declare its own progress by
    #                     re-describing what it found.
    #
    # ⚠ A ROUND EXCLUDED FROM THIS GUARD'S EVALUATION CANNOT SET THIS GUARD'S BEST — cleared
    # rounds do not hold records. `less()` removes them from the series entirely rather than
    # merely skipping their count, so a round ruled procedural cannot leave a ceiling behind it.
    # That is the second half of the 2026-09-07 ruling and it is why the exclusion is applied
    # HERE, before `best` is computed, rather than when `since` is incremented.
    def shipped(v):
        sh = (v.get("seat") or {}).get("ship")
        if isinstance(sh, (list, tuple)):
            return 1 if len(sh) else 0
        return 0 if sh in (None, "", "NONE", "none") else 1

    def unresolved(v):
        p = prog(v)
        if p.get("unresolved_flips") is not None:
            return int(p["unresolved_flips"])
        # A DISPOSITIONED FLIP IS AN ANSWERED FLIP. PASS-WITH-ROUTED-ITEMS requires every item to
        # carry a quoted human ruling and, when routed, a destination lane — so the count of
        # undispositioned items is the honest measure of what is still outstanding against this
        # lane. The builder cannot write these (critic_gate refuses a malformed set), which is what
        # stops a lane declaring its own progress.
        d = v.get("dispositions")
        if d is not None:
            return max(len(v.get("flip_list") or []) - len(d), 0)
        r = ruling_for(lane_of(v), v.get("round"), gate_path)
        if r and r.get("unresolved_flips") is not None:
            return int(r["unresolved_flips"])
        return len(v.get("flip_list") or [])

    def key(v):
        return (prog(v)["rank_score"], shipped(v), -unresolved(v))

    scored = [v for v in less("stall", read) if prog(v).get("rank_score") is not None]
    if len(scored) > STALL_ROUNDS:
        best, since, at = None, 0, None
        for v in scored:
            k = key(v)
            if best is None or k > best:
                best, since, at = k, 0, v.get("round")
            else:
                since += 1
        if since >= STALL_ROUNDS:
            return ("stall",
                    "%d readable rounds with no progress. The best is rank %.2f / ship %d /\n"
                    "    %d unresolved flips, set at round %s, and nothing since has beaten it\n"
                    "    on any of the three. The lane is not converging."
                    % (since, best[0], best[1], -best[2], at))

    # ── ceiling ───────────────────────────────────────────────────────────────────────────────
    if len(less("ceiling", lane_hist)) >= ROUND_CEILING:
        return ("ceiling",
                "%d rounds on this lane. This is the backstop and it should never be the guard\n"
                "    that fires — if it did, the progress guards did not see something they\n"
                "    should have, and that is itself worth a ruling."
                % len(less("ceiling", lane_hist)))
    return (None, None)


def write_stall(name, why, hist, lane, cfg, out=None):
    out = out or STALL
    lane_hist = lane_rounds(hist, lane)
    L = []
    L.append("# STALL REPORT — %s\n" % name)
    L.append("**The line has stopped and is not restarting itself.** LOOP-PROCESS §1.1.4 ruling "
             "trigger: this report is the evidence.\n")
    L.append("- **lane** `%s`" % lane)
    L.append("- **surface** `%s`" % cfg.get("surface"))
    L.append("- **guard** `%s`" % name)
    L.append("- **written** %s\n" % datetime.datetime.now().isoformat(timespec="seconds"))
    L.append("## Why it stopped\n")
    # The explanations are indented for the terminal. Markdown would render the continuation
    # lines as part of the paragraph anyway, but a leading run of spaces is one blank line away
    # from becoming a code block, so it is stripped rather than trusted.
    L.append("\n".join(l.strip() for l in why.splitlines()) + "\n")
    L.append("## What was tried, round by round\n")
    L.append("`rank` is where the build placed in that round's blind shuffled deck, and `score` "
             "normalises it so decks of different sizes compare — 1.00 is first, 0.00 is last. "
             "`Δpic` is how far the delivered frame moved from the previous round: mean and worst "
             "cell, in luminance levels. `0.000 / 0` means the picture did not change at all.\n")
    L.append("| round | verdict | rank | score | best? | Δpic | build | the seat's own words |")
    L.append("|---|---|---|---|---|---|---|---|")
    best = None
    prev_sig = None
    for v in lane_hist:
        p = prog(v)
        s = p.get("rank_score")
        isbest = ""
        if s is not None and v.get("verdict") != "VOID":
            if best is None or s > best:
                best, isbest = s, "**new best**"
        d = sig_delta(prev_sig, p.get("capture_signature"))
        if p.get("capture_signature"):
            prev_sig = p["capture_signature"]
        worst = (v.get("seat", {}).get("WORST_WHY") or "").replace("|", "/")
        L.append("| %s | %s | %s | %s | %s | %s | `%s` | %s |"
                 % (v.get("round"), v.get("verdict"),
                    ("%s/%s" % (p.get("rank_position"), p.get("deck_size"))
                     if p.get("rank_position") else "—"),
                    ("%.2f" % s) if s is not None else "—",
                    isbest, "—" if d is None else "%.3f / %d" % d,
                    (v.get("build_id") or "")[:12],
                    " ".join(worst.split())[:140]))
    L.append("")
    L.append("## The flip lists, verbatim\n")
    L.append("Void rounds do not appear here. §4: the plant was missed, so those findings are not "
             "read — they are kept in the verdict under `flip_list_withheld` and are not "
             "evidence.\n")
    for v in lane_hist:
        if not v.get("flip_list"):
            continue
        L.append("**round %s (%s)**\n" % (v.get("round"), v.get("verdict")))
        for f in v["flip_list"]:
            L.append("- %s" % f)
        L.append("")
    L.append("## Where to look\n")
    L.append("Captures and transcripts, per round:\n")
    for v in lane_hist:
        L.append("- round %s — deck `%s`, transcript `%s`"
                 % (v.get("round"), v.get("deck", {}).get("work_dir", "?"),
                    v.get("transcript", "?")))
    L.append("")
    L.append("## What is being asked for\n")
    L.append("A ruling. Not another round — the guard fired precisely because another round is "
             "the wrong move. Nothing installs to the phone while this stands.\n")
    with open(out, "w") as f:
        f.write("\n".join(L))
    return out


# ================================ the deck ====================================================
def crop_to(path, box, dest):
    """Crop one deck frame, and REFUSE a box that runs off the source image.

    ⚠ FOUND BY THE FIRST FINISHED ROUND, in the asset bar of all places. PIL pads an out-of-bounds
    crop with black and says nothing. The bar crop inherited from the floors' seat runner is
    `(336, 240, 720, 528)` against a source that is **720x504** — so every comparative seat this
    project has ever run was shown the commercial bar with **24 rows of black padding along the
    bottom**, and the seat that finally said so culled the bar for it:

        WORST 1 — "The frame is padded. Content ends at row 239. Rows 240-263 are pure ..."

    The bar is the quality reference. A padded bar is a reference the seat rejects for a defect
    that belongs to the crop box, and the comparison it was there to make does not happen.

    Exactly LOOP-PROCESS §4.2's shape — a step that quietly does nothing anyone can see, until it
    surfaces later and somewhere else. So the box is checked against the image and the round
    refuses rather than padding.
    """
    im = Image.open(path).convert("RGB")
    if box:
        x0, y0, x1, y1 = box
        w, h = im.size
        if x0 < 0 or y0 < 0 or x1 > w or y1 > h:
            raise SystemExit(
                "REFUSING: the crop %s runs off %s, which is %dx%d.\n"
                "PIL would pad the difference with black and say nothing, and a seat shown a "
                "padded frame\ncorrectly culls it for the padding. Fix the box in the config."
                % (tuple(box), os.path.relpath(path, REPO) if path.startswith(REPO) else path,
                   w, h))
        im = im.crop(tuple(box))
    im.save(dest)
    return im.size


def pick_plant(surface, morgue, exclude=(), axis=None, subject=None, regime=None):
    """Candidates for this round's plant, narrowed to the AXIS and the SUBJECT the deck asks about.

    LAW (Rafe, 2026-09-12): *"a plant matches the deck's axis AND subject (wall plants for wall
    decks, object plants for object decks)."* Occasioned by round 1 of art/object-projection:
    an object-craft deck was handed cement-cap — a wall-cap MATERIAL cull on a room frame — and
    a seat ranking it above a props build says nothing about the props. A subject named in the
    config is STRICT: an entry must carry it, and a morgue with no plant of that subject refuses
    the round rather than dealing an off-subject one. An entry with no `subject` is legacy and
    serves a round that names none.

    RULED (Rafe, 2026-09-03): *"Per-axis morgue plants — tag entries by axis, assemble the plant
    to match the deck's question; this is why round 5 VOIDed."*

    A plant is only a control if it is wrong on the axis under test. The wall lane's round 5 was
    judged on CONSTRUCTION — does the cap read as stone or as cement — and was handed the `grey
    walls` plant, whose defect is CHROMA. The build had already had its chroma fixed, so the two
    frames differed on an axis the plant was not carrying, the seat had no reason to rank the
    plant last, and the round voided on the judge rather than on the art. **The right image for
    the wrong question is not a control.**

    An entry with no `axis` answers any question, so the morgue stays usable while it is being
    tagged, and a surface whose entries carry no matching axis falls back to all of them rather
    than refusing — a narrower plant is better than no round, but no plant is not an option.

    AN ENTRY MAY SERVE MORE THAN ONE SURFACE, and `surface` is therefore a string OR a list.
    The combined round is what asked for this. It judges a whole room — floor, wall, cap and
    void together — and the morgue was tagged when every round judged one surface at a time.
    Reading the pictures rather than the tags settles which entries can serve it: the three
    FLOOR culls each carry **3904 magenta pixels**, because the wall family did not exist when
    they were taken, and a seat handed one of those in a deck whose build has real walls catches
    it for the debug colour. That is §4.2's failure exactly — the right image flagged for the
    wrong reason — so they are not eligible, and the reason is written in their entries rather
    than left to be rediscovered. The two WALL culls carry no magenta and were captured with the
    real floor laid under them, so they are whole-scene frames already and serve both.
    """
    def serves(e):
        sf = e["surface"]
        return surface == sf if isinstance(sf, str) else surface in sf

    # ── A RETIRED CONTROL IS NEVER DEALT, AND AN EMPTY `axis` DOES NOT RETIRE ONE ─────────────
    #
    # `crushed-midband.png` was seeded on 2026-09-11 and retired the same day, by measurement:
    # both seats that drew it MISSED it, and put in the BUILD slot it was ranked 1st, 1st and
    # 2nd of four by three fresh seats, above the approved reference, flagged by nobody. It is
    # not a weak plant — it is a frame blind seats PREFER. It is the 2026-08-27 DEVICE gate
    # FAIL, and what the phone catches is by construction what a seat does not (§13.2).
    #
    # The first attempt at retiring it set `axis: []`, which does the OPPOSITE: the rule below
    # reads a missing axis as "answers any question", so an entry with no axis is dealt to every
    # round rather than none. An entry is retired by saying so, in a field whose name means it.
    entries = [e for e in morgue["entries"]
               if serves(e) and e["file"] not in exclude and not e.get("retired_as_control")]
    if regime:
        # LAW (Rafe, 2026-09-12): plants are captured under the deck's lighting regime — scene,
        # rig, AND shadow state. A plant captured under other light is not a control for this
        # deck; the round refuses rather than dealing it, until the morgue holds one.
        same = [e for e in entries if (e.get("regime") or "") == regime]
        if not same:
            raise SystemExit(
                "REFUSING: no plant in the morgue is captured under the deck's lighting regime "
                "%r (surface %r). LAW (Rafe, 2026-09-12): plants and reference are captured "
                "under the deck's regime — scene, rig, AND shadow state. Re-capture the "
                "object/wall plants under it before any round runs on this lane.\n  %s"
                % (regime, surface, os.path.join(MORGUE, "MORGUE.json")))
        entries = same
    if subject:
        # a string or a list: a deck that judges walls AND objects in one room draws from both
        wanted = [subject] if isinstance(subject, str) else list(subject)
        entries = [e for e in entries if any(w in (e.get("subject") or []) for w in wanted)]
        if not entries:
            raise SystemExit(
                "REFUSING: the morgue holds no plant for subject %r on surface %r.\n"
                "LAW (Rafe, 2026-09-12): a plant matches the deck's axis AND subject. A wall "
                "plant cannot control an object deck. Seed a Rafe-culled frame tagged "
                "subject=%r in\n  %s" % (subject, surface, subject, os.path.join(MORGUE, "MORGUE.json")))
    if axis:
        # ── A DECK MAY ASK ON MORE THAN ONE AXIS — "plants on both axes" (Rafe, 2026-09-13) ──
        #
        # The jamb round is judged on two things at once: the east face's CONSTRUCTION (is it a
        # face or a flat quad) and the CAST EDGE beside it (is the wedge's boundary a shadow or a
        # mask). One plant cannot be wrong on both without being wrong on everything, so the
        # deck names both axes and the draw takes every entry wrong on EITHER. Dealt without
        # replacement below, a five-seat panel then exercises both controls every round. A
        # string is one axis, as before; an entry with no `axis` still answers any question.
        axes = [axis] if isinstance(axis, str) else [a for a in axis if a]
        on_axis = [e for e in entries
                   if not e.get("axis") or any(a in e["axis"] for a in axes)]
        if on_axis:
            entries = on_axis
    if not entries:
        raise SystemExit(
            "REFUSING: the morgue holds no known-bad frame for surface %r.\n"
            "A round with no plant is a round with no control, and LOOP-PROCESS §4 does not "
            "permit reading its findings. Add a Rafe-culled frame for this surface to\n"
            "  %s\nbefore running another round." % (surface, os.path.join(MORGUE, "MORGUE.json")))
    return entries


def verify_morgue(morgue):
    """A morgue entry whose bytes have changed is not the frame Rafe culled.

    §2.3 in its plainest form. This is the one check that protects the picture-plant's whole
    claim: a plant that can be edited is a plant that can be softened.
    """
    bad = []
    for e in morgue["entries"]:
        p = os.path.join(MORGUE, e["file"])
        if not os.path.exists(p):
            bad.append("%s is missing" % e["file"])
            continue
        got = hashlib.sha256(open(p, "rb").read()).hexdigest()
        if got != e["sha256"]:
            bad.append("%s has changed: recorded %s, on disk %s"
                       % (e["file"], e["sha256"][:16], got[:16]))
    if bad:
        raise SystemExit("REFUSING: the morgue does not match its manifest.\n  "
                         + "\n  ".join(bad)
                         + "\nA plant that can be edited is a plant that can be softened.")


# ================================ the seat ====================================================
# ── THE MEMORY PRECHECK — RULED (Rafe, 2026-09-10) ───────────────────────────────────────────
#
#     "before seating a panel or starting any recompose/capture, check free memory against a
#      floor ... if below, STOP cleanly ... never start a write that an OOM kill can leave
#      corrupt."
#
# Three five-seat rounds on the props build were killed by the system mid-panel, and each one
# looked from the outside exactly like a lane that would not converge. The floors are measured
# (tools/tier0_harness/headroom.py): a seat peaks at 351MB and takes 16 points off the system's
# free percentage for seven and a half minutes; a capture peaks at 507MB for four seconds.
def _headroom(kind):
    import importlib.util
    hp = os.path.join(REPO, "tools", "tier0_harness", "headroom.py")
    if not os.path.exists(hp):
        return True, "headroom module absent; proceeding"
    spec = importlib.util.spec_from_file_location("headroom", hp)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m.require(kind)


def _no_lingering_seats():
    """RULED: free each seat fully before the next starts, and CONFIRM it."""
    import subprocess as sp
    out = sp.run(["pgrep", "-f", "claude -p"], capture_output=True, text=True)
    return [x for x in out.stdout.split() if x.strip()]


def run_seat(work, prompt, timeout):
    p = subprocess.run(["claude", "-p", prompt, "--allowedTools", "Read"],
                       cwd=work, capture_output=True, text=True,
                       timeout=timeout, stdin=subprocess.DEVNULL)
    return p.stdout + p.stderr


LABELS = ["BEST_WHY", "WORST_WHY", "FLAGGED", "RANK", "BEST", "WORST", "SHIP"]
_LABEL_RE = re.compile(r"^\s*#{0,6}\s*\**(" + "|".join(LABELS) + r")\**\s*:\**\s*",
                       re.MULTILINE)
_FLIP_RE = re.compile(r"^\s*#{0,6}\s*\**FLIP\s+(\d+)\**\s*:\**\s*", re.MULTILINE)


def parse(text, n_slots):
    """Split the transcript on its labels. Everything up to the next label is the answer.

    ⚠ AN EMPTY FIELD AND AN UNPARSED FIELD ARE NOT THE SAME THING. The floors' seat runner learned
    this the expensive way — its parser matched the QUESTION line rather than the answer below it,
    every field came back holding the restated question, and it reported a valid round VOID. So a
    label that appears in the transcript but yields nothing is an ERROR here, never an absence.
    """
    out = {}
    marks = [(m.start(), m.end(), m.group(1)) for m in _LABEL_RE.finditer(text)]
    flips = [(m.start(), m.end(), int(m.group(1))) for m in _FLIP_RE.finditer(text)]
    allmarks = sorted([(s, e, ("L", n)) for s, e, n in marks]
                      + [(s, e, ("F", n)) for s, e, n in flips])
    per_flip = {}
    for i, (s0, e0, key) in enumerate(allmarks):
        end = allmarks[i + 1][0] if i + 1 < len(allmarks) else len(text)
        body = text[e0:end].strip()
        lines = body.splitlines()
        if lines and lines[0].rstrip("* ").endswith("?"):
            body = "\n".join(lines[1:]).strip()
        kind, name = key
        if kind == "L":
            if name not in out or len(body) > len(out[name]):
                out[name] = body
        else:
            per_flip.setdefault(name, "")
            if len(body) > len(per_flip[name]):
                per_flip[name] = body
    for k in LABELS:
        out.setdefault(k, "")

    for name in ("RANK", "SHIP", "WORST"):
        if not out[name] and re.search(r"\b%s\b" % name, text):
            raise ValueError(
                "PARSE FAILURE: the transcript mentions %s and nothing was extracted for it. "
                "Treating that as an absent answer is how a valid round gets thrown away." % name)

    out["_flip_blocks"] = {k: [l.strip()[2:].strip() for l in v.splitlines()
                               if l.strip().startswith("- ")]
                           for k, v in per_flip.items()}
    out["_rank"] = [int(x) for x in re.findall(r"\d+", out["RANK"])
                    if 1 <= int(x) <= n_slots]
    out["_ship"] = _slots(out["SHIP"], n_slots)
    out["_flagged"] = _slots(out["FLAGGED"], n_slots)
    out["_worst"] = next((int(x) for x in re.findall(r"\d+", out["WORST"])
                          if 1 <= int(x) <= n_slots), None)
    return out


# ⚠ THE SENTINEL IS AN ANSWER, NOT A WORD THAT HAPPENS TO APPEAR IN ONE.
#
# THE OCCASION (2026-09-07, lane polish-a-184 round 1). The seat answered
#
#     FLAGGED: 1, 2, 3, 4
#     - **1** - the centre of the frame is a smooth cream blur ...
#     - **1, 3, 4** - the mass at (280-375, 645-712) ... no board seams, no lid, no pin heads,
#       no rope ... the biggest object in the room carries NONE of it.
#
# and the parser searched the WHOLE BODY for `\bNONE\b`, found that prose "none", and returned an
# EMPTY flagged list. The plant was slot 1. The seat had flagged it, explicitly, at length, and had
# shipped nothing - a clean catch by the rule as written - and the runner recorded
# `flagged=False` and threw the round away as VOID. Its findings, four substantive flips on the
# build, were not read.
#
# This is bible SS13.11 in the review layer's parser: AN INSTRUMENT'S INPUT MUST BE NO WIDER THAN
# THE THING IT MEASURES. The sentinel measures the ANSWER; it was being fed the answer plus every
# word of the seat's reasoning, and free prose about a dungeon will contain "none" eventually. It
# did on the first round that had a long enough explanation.
#
# THE DIRECTION OF THE OLD FAILURE IS THE UNSAFE ONE, which is why it is fixed rather than
# tolerated: a spurious NONE on FLAGGED makes a caught plant read as missed, and two of those in a
# row fire the broken-judge guard and stop the line over a judge that was working.
#
# SCOPED, and the scope is checked rather than asserted: re-parsing every transcript committed to
# `history/` shows this firing on exactly ONE - the round that found it. No past verdict moves.
#
# AND THE LIST IS DEDUPLICATED, which is a second defect of the same family found in the same
# reading. `every_frame_flagged` asks `len(_flagged) == len(rank)`, and a seat that elaborates
# per frame repeats its numbers - r005-combined parsed to [1,2,3,1,3,1,2] - so the comparison
# could not be true and the signal had never once fired. It is recorded and never scored (SS4), so
# this corrects a REPORT rather than a verdict.
def _slots(body, n_slots):
    """File numbers named in an answer, or [] for the NONE sentinel.

    THE ANSWER LINE IS THE ANSWER. Both the sentinel and the numbers are read from it, and the
    rest of the body — the seat's reasoning — is read only when the answer line names nothing at
    all and does not say NONE, which is the one case where the answer really is below the label.

    ⚠ THE SECOND HALF OF THIS RULE WAS LEARNED ON THE ROUND AFTER THE FIRST HALF, and it is the
    dangerous one. The first fix stopped a prose "none" eating a real list, and read the NUMBERS
    from the whole body — where the seat writes things like *"the slab mortar grid plainly visible
    in 2 and 4 is gone"*, and *"49.5% ... 11.2% ... 16,602 pixels ... 754 in image 2"*. On lane
    polish-a-184 round 2 the seat answered `FLAGGED: 1, 3` and that parser returned [1, 2, 3, 4].
    It happened to be harmless — the plant was in the real answer — but the failure it can produce
    is A PLANT RECORDED AS FLAGGED THAT THE SEAT NEVER FLAGGED, which is a soft critic passing its
    own self-test. That is the one direction this mechanism may never fail in.

    Both halves are the same law (bible §13.11): AN INSTRUMENT'S INPUT MUST BE NO WIDER THAN THE
    THING IT MEASURES. The answer to "which frames are flagged" is the list on the answer line;
    everything under it is why.
    """
    def nums(t):
        return sorted({int(x) for x in re.findall(r"\d+", t) if 1 <= int(x) <= n_slots})
    head = next((l for l in body.strip().splitlines() if l.strip()), "")
    found = nums(head)
    if found:
        return found
    if re.search(r"\bNONE\b", head, re.I):
        return []
    # The answer line named nothing and did not say NONE: the answer is below the label.
    rest = "\n".join(body.strip().splitlines()[1:])
    if re.search(r"\bNONE\b", rest, re.I) and not nums(rest):
        return []
    return nums(rest)


# ── THE PANEL'S ARITHMETIC — RULED (Rafe, 2026-09-08) ─────────────────────────────────────────
#
#     "majority of three independent blind seats rank the build above approved_capture, no
#      unrouted flags from any; each seat its own axis-matched plant."
#
# Extracted so `prove_panel.py` drives THIS function rather than a copy of it. A proof against a
# reimplementation only proves the reimplementation — the same reason `prove_gate.py` shells the
# real gate and the guard fixtures call the real `guards()`.
#
# THE TWO TERMS ARE ASYMMETRIC ON PURPOSE, and the asymmetry IS the ruling:
#
#   rank   is the NOISY term — it flipped on identical bytes — so it takes a MAJORITY.
#   a flag is a FINDING. ONE seat finding it is enough, because a flag outvoted 2-1 is still a
#          defect that two seats missed. Averaging findings would discard the only thing a panel
#          is good at.
#
# EVERY seat must catch its plant. §4 voids a round on one missed plant and a panel does not get
# to dilute that into an average: a soft seat's ballot is exactly what §4 refuses to read.
REF_SLACK = 1
# ⚠ THE DECK FORBIDS TIES, SO ONE PLACE BELOW STANDS IN FOR ONE. The seat is told "no ties", so
# a seat can never say the build and the reference are equal — the only two things it can say are
# "above" and "below", and one of those has to carry the meaning of "level with". This is the
# same device §1.2.1 already uses for the asset bar, where one place below the bar stands in for
# the tie the deck forbids, and it is used here for the same reason and with the same slack.


def score_seat(sd):
    """Parse one seat's ballot and score it: rank, reference rank, flagged, shipped, CAUGHT.

    Called the moment a seat returns — BEFORE any term reads sd["caught"]. The 2026-09-11
    seat-level plant term was first written to read `caught` from seats that had not been
    scored yet, so every fresh seat counted as a live miss, the "slot" it re-drew was a seat
    index, and the re-draw (unscored too) tripped broken-judge on every round it ran. Round 1 of
    lane art/object-projection was voided that way with its plant record saying caught=True.
    Ruled VALID by instrument correction (Rafe, 2026-09-12, LOOP-PROCESS §1.1.5).
    Returns None on success, or the ValueError message if the ballot did not parse."""
    try:
        sd["r"] = parse(sd["text"], len(sd["mapping"]))
    except ValueError as e:
        return str(e)
    rr, sl = sd["r"], sd["slots"]
    sd["rank"] = rr["_rank"].index(sl["build"]) + 1 if sl["build"] in rr["_rank"] else None
    sd["approved_rank"] = (rr["_rank"].index(sl["approved"]) + 1
                           if sl["approved"] and sl["approved"] in rr["_rank"] else None)
    sd["above_approved"] = (sd["approved_rank"] is not None and sd["rank"] is not None
                            and sd["rank"] < sd["approved_rank"])
    # NOT BELOW = above, or one place under (the tie the deck forbids). RULED 2026-09-08.
    sd["not_below"] = (sd["approved_rank"] is not None and sd["rank"] is not None
                       and sd["rank"] <= sd["approved_rank"] + REF_SLACK)
    sd["build_flagged"] = sl["build"] in rr["_flagged"]
    sd["shipped"] = sl["build"] in rr["_ship"]
    sd["caught"], sd["how"] = plant_caught(rr, sl["plant"], sl["build"])
    sd["scored"] = True
    return None


def live_misses(seats, morgue):
    """Indices of seats whose SCORED ballot missed a LIVE plant. A seat that has not been scored
    is an error, never a miss: counting it as one is the defect described in score_seat()."""
    out = []
    for i, sd in enumerate(seats):
        if "caught" not in sd:
            raise RuntimeError("seat %d has not been scored — live_misses() read before "
                               "score_seat(); that is the 2026-09-12 defect, not a miss" % (i + 1))
        if not sd["caught"] and not _plant_retired(sd.get("plant"), morgue):
            out.append(i)
    return out


def panel_tally(seats):
    """(n_above, n_flagged, n_shipped, all_caught, majority_above) over independent seats."""
    n = len(seats)
    n_above = sum(1 for x in seats if x.get("above_approved"))
    n_not_below = sum(1 for x in seats if x.get("not_below"))
    n_flagged = sum(1 for x in seats if x.get("build_flagged"))
    n_shipped = sum(1 for x in seats if x.get("shipped"))
    all_caught = bool(seats) and all(x.get("caught") for x in seats)
    n_below = n - n_not_below
    # ── THE STRONG-MAJORITY REGRESSION BLOCK — RULED (Rafe, 2026-09-09) ───────────────────────
    #
    #     "five seats; block only on strong-majority regression (>=4 of 5 rank below the
    #      reference); else install if exit met and plant caught."
    #
    # Written as a RATIO so it does not silently mean something else on a panel of another size:
    # four of five is four fifths, so the test is `below * 5 >= n * 4`. At n=5 that is >=4, which
    # is the ruling's own arithmetic; at n=3 it is >=3, unanimous, which is the same standard and
    # not a quietly different one.
    strong_regression = (n_below * 5 >= n * 4) if n else False
    return dict(n=n, above=n_above, not_below=n_not_below, below=n_below, flagged=n_flagged,
                shipped=n_shipped, all_caught=all_caught,
                strong_regression=strong_regression,
                majority_above=(n_above * 2 > n), majority_not_below=(n_not_below * 2 > n))


def panel_verdict(seats, approved_in_deck, beats_approved, near_bar, exit_met=False):
    """The verdict the panel yields, decided by vote rather than by sample.

    ── INSTALL-LATEST — RULED (Rafe, 2026-09-08). NON-REGRESSION, NOT VICTORY. ──────────────────

        "PASS-INSTALL for polish rounds = non-regression, not victory. INSTALL-LATEST = majority
         of seats do not rank the build below the seeded reference (above or tied), AND the item's
         own measured exit is met, AND no unrouted flags. Beating the reference is not required to
         install; seeding a new reference is Rafe's walk only — seats never move approved_capture."

    THE CONTRADICTION IT RESOLVES, measured on this lane. Requiring the build to BEAT its reference
    made the gate un-passable for incremental polish: r002 and r003 judged IDENTICAL BYTES and gave
    3-of-3 above and then 1-of-3, because rank carries a measured 40% flip rate (§13.13). A change
    worth shipping — a lane-gain step, a half-cell placement fix, a hero cap — is small next to
    seat-to-seat noise on the same scene, so "better than the frame it came from" was a coin toss
    dressed as a threshold.

    Non-regression is the honest bar for a polish round: the build must not be WORSE than the frame
    already ratified as installable, and it must have DONE THE THING IT SET OUT TO DO — which is
    what the item's measured exit carries, and what stops "not worse" from meaning "not different".

    ── AMENDED (Rafe, 2026-09-09). THE MAJORITY WAS ITSELF THE NOISE. ───────────────────────────

        "five seats; block only on strong-majority regression (>=4 of 5 rank below the
         reference); else install if exit met and plant caught. Rank-in-deck cannot resolve
         polish-sized deltas at a 40% flip rate."

    THE MEASUREMENT THAT MOVED IT, and it is the second time the same frame taught the same
    lesson. Lane `polish-198-halo` ran two panels of three ON BYTE-IDENTICAL BYTES — the round's
    own line reads *picture moved mean 0.000 / worst 0 luminance levels* — and returned:

        r002    NOT BELOW the reference in 3 of 3        install bar met
        r003    NOT BELOW the reference in 1 of 3        no majority, refused

    Six seats, one picture, the majority in both directions. A three-seat majority of a statistic
    with a 40% per-seat flip rate is still, in effect, a single noisy sample: at the seat-level
    rate this frame actually shows (not below in 4 of 6, q_below = 0.333) the OLD rule refuses a
    perfectly good build **25.9% of the time**. The new rule refuses it **4.5%** of the time.

    THE COST IS PAID IN CATCH POWER AND IS NOT HIDDEN. Against a build that six seats in ten
    would rank below, the old rule fired 82% of the time and the new one fires 34%. The trade is
    deliberate: a gate that refuses one good build in four is not measuring the build, and the
    item's own measured exit — which no seat votes on — is what carries "did this do the thing".
    The full false-refuse / true-catch table is published beside `docs/RANK-NOISE-FLOOR.json`.

    ⚠ WHAT DID NOT MOVE. Every plant must still be caught; the exit must still be met and
    measured; and a flag from any seat still blocks the install until it carries a lawful
    disposition — that term simply moved to where it was always enforced, `critic_gate`, instead
    of forcing the round-time verdict to FAIL and then be amended back. `panel.verdict_at_round`
    records what this function returned, so any later divergence is visible as an amendment.
    """
    t = panel_tally(seats)
    if not t["all_caught"]:
        return "VOID"
    if t["shipped"] * 2 > t["n"] and t["flagged"] == 0 and beats_approved and near_bar:
        return "PASS"
    if approved_in_deck and not t["strong_regression"] and exit_met:
        return "INSTALL-LATEST"
    return "FAIL"


# THE PLANT RULE, WRITTEN DOWN BEFORE THE FIRST ROUND AND NOT NEGOTIABLE AFTERWARDS.
#
#     The plant must land WORST or FLAGGED, and must not be in SHIP.
#
# It needs no vocabulary list, and that is the point of moving to a picture. The floors' generated
# plant needed one — a hand-maintained list of ruin words — and it went wrong in every direction a
# list can: it carried "lichen", which no plant ever contained, and it lacked "hole", the plainest
# word for the plant's most prominent feature, for three rounds. A list derived by reading
# transcripts is a test derived from its own outcome. A rank has no vocabulary.
#
# ⚠ WHAT THIS RULE DOES NOT TEST, recorded on the first live round rather than discovered later.
#
# The plant tests for SOFTNESS — a seat that would ship a frame the human gate rejected. It does
# NOT test ordering, and the first real round showed why the distinction has to be written down:
# the seat flagged all three frames, shipped none, and ranked THE PLANT FIRST. The plant was
# caught (it declined to ship a culled frame, which is the claim) and the round stood — but the
# ranking is the more interesting fact in it, and a rule that only emitted CAUGHT would have
# thrown it away.
#
# A plant that outranks the build is a statement ABOUT THE BUILD: it is sitting below a frame
# that was already rejected once. So it is recorded as `outranked_build` and reported, rather
# than folded into the verdict. LOOP-PROCESS §8: a bar found wanting mid-run is held frozen,
# cleared honestly, and impeached in the same report — never re-tuned after the answer is seen.
def plant_caught(r, plant_slot, build_slot):
    ranked_last = bool(r["_rank"]) and r["_rank"][-1] == plant_slot
    worst = r["_worst"] == plant_slot
    flagged = plant_slot in r["_flagged"]
    shipped = plant_slot in r["_ship"]
    rank = r["_rank"]
    outranked = (plant_slot in rank and build_slot in rank
                 and rank.index(plant_slot) < rank.index(build_slot))
    return ((ranked_last or worst or flagged) and not shipped,
            dict(ranked_last=ranked_last, named_worst=worst, flagged=flagged, shipped=shipped,
                 outranked_build=outranked,
                 every_frame_flagged=(len(r["_flagged"]) == len(rank) and bool(rank))))


# ================================ the round ===================================================
def capture(cfg, echo=True):
    ok, msg = _headroom("write")
    if not ok:
        print("\n*** STOP — %s" % msg)
        raise SystemExit(3)
    """Run the configured capture and hand back the frame it produced.

    The command lives in docs/FRAME-CRITIC.json rather than here so this skill stays
    content-agnostic — it judges frames, it does not know how a wall is composed. What it DOES
    enforce is that the command actually produced a new frame: a stale PNG left over from a
    previous build is the exact evidence failure §2.3 exists for, and a capture step that silently
    does nothing is §4.2's.
    """
    frame = os.path.join(REPO, cfg["capture"]["frame"])
    before = None
    if os.path.exists(frame):
        before = (os.path.getmtime(frame), hashlib.sha256(open(frame, "rb").read()).hexdigest())
    cmd = cfg["capture"]["cmd"]
    if echo:
        print("== capture: %s" % " ".join(cmd))
        for k, v in (cfg["capture"].get("env") or {}).items():
            print("   env %s=%s" % (k, v))
    env = dict(os.environ)
    env.update(cfg["capture"].get("env") or {})
    r = subprocess.run(cmd, cwd=REPO, env=env, capture_output=True, text=True)
    if r.returncode != 0:
        sys.stderr.write(r.stdout[-2000:] + r.stderr[-2000:])
        raise SystemExit("REFUSING: the capture command exited %d. There is no frame to judge."
                         % r.returncode)
    if not os.path.exists(frame):
        raise SystemExit("REFUSING: the capture ran and %s does not exist." % frame)
    after = (os.path.getmtime(frame), hashlib.sha256(open(frame, "rb").read()).hexdigest())
    if before is not None and after[0] <= before[0]:
        raise SystemExit(
            "REFUSING: %s was not rewritten by the capture command.\n"
            "A frame left over from an earlier build judged as this one is exactly the evidence\n"
            "failure LOOP-PROCESS §2.3 forbids." % cfg["capture"]["frame"])
    print("   frame: %s  sha256 %s" % (cfg["capture"]["frame"], after[1][:16]))
    # EVERY FLAG THE FRAME WAS TAKEN WITH, in the round's own output. The wrapper command above
    # names a script; the flags are what decide whether the floor is magenta, whether the walls
    # are the tier-0 mocks, and whether the orc layer is present at all — and a capture missing
    # one of those is a plausible-looking wrong picture. capture_corridor.py writes the resolved
    # invocation as the first line of its log; it is echoed here and the log ships with the round.
    log = cfg["capture"].get("log")
    if log and os.path.exists(os.path.join(REPO, log)):
        with open(os.path.join(REPO, log)) as f:
            print("   flags: %s" % f.readline().strip())
        print("   log:   %s" % log)
    return frame, after[1]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--config", default=CONFIG)
    ap.add_argument("--lane", help="which line of rounds this is. Defaults to the git branch, "
                                   "so a session restart lands on the same counters.")
    ap.add_argument("--no-capture", action="store_true",
                    help="judge the frame already on disk instead of taking a new one. For "
                         "replaying a round, never for gating a build.")
    ap.add_argument("--build-frame", help="override the build frame. Used by the plant "
                                          "self-test, which puts a morgue capture in the "
                                          "build's slot and requires the seat to flag it.")
    ap.add_argument("--redraw-seat", type=int, default=None,
                    help="re-draw ONE seat of the lane's last round on the SAME frozen frame, "
                         "because its plant was mis-tagged. RULED (Rafe, 2026-09-08): a seat "
                         "voided by a mis-tagged plant is re-drawn, not the round; a correct "
                         "plant missed still voids.")
    # DEFAULT FIVE, NOT ONE. LAW (bible §13.13): a gate's binding term must never be a single
    # sample; RULED five seats (Rafe, 2026-09-09). The default was 1 and it bit: lane
    # art/jamb-211 r001 ran one seat by omission and wrote an INSTALL-LATEST nobody may install
    # on. A default that contradicts the law is a trap for the next builder, so the law is the
    # default; `--seats 1` is still available for a self-test that says so.
    ap.add_argument("--seats", type=int, default=5,
                    help="how many INDEPENDENT blind seats judge this round. Ruled at 3 for a "
                         "PASS-INSTALL vote (Rafe, 2026-09-08), five since 2026-09-09; higher "
                         "values measure the comparator's own noise floor on unchanged bytes.")
    ap.add_argument("--timeout", type=int, default=2400)
    # ── SHOWING THE GUARDS THEY CAN FIRE, WITHOUT REIMPLEMENTING THEM ────────────────────────
    # LOOP-PROCESS §4 / bible §13.5: no check's pass counts until it has demonstrated it can
    # fail. These three flags exist so the guards can be driven against a fixture history and
    # shown to STOP — running THE SAME `guards()` and `write_stall()` the real path runs.
    # `verify_on_device.sh --check-log` is the precedent and states the reason: a test that
    # reimplements the thing it tests proves the reimplementation.
    ap.add_argument("--history", help="read verdicts from this directory instead of history/")
    ap.add_argument("--gate-ruling", help="read the human gate's rulings from this file "
                                          "instead of GATE-RULING.json. For fixtures (§13.5).")
    ap.add_argument("--park", help="read the guard-clearing ruling from this file instead of "
                                   "PARK-CLEARED.json. Exists so the clear can be driven against "
                                   "a fixture and PROVED still to fire (§13.5).")
    ap.add_argument("--stall-out", help="write the stall report here instead of the repo root")
    ap.add_argument("--check-guards", action="store_true",
                    help="evaluate the loop guards against --history and exit. No round runs, "
                         "no seat is spent, nothing is captured.")
    a = ap.parse_args()

    if not os.path.exists(a.config):
        raise SystemExit("REFUSING: no %s. The critic does not guess what to capture." % a.config)
    cfg = json.load(open(a.config))
    morgue = json.load(open(os.path.join(MORGUE, "MORGUE.json")))
    verify_morgue(morgue)

    lane = a.lane or (BID._git("rev-parse", "--abbrev-ref", "HEAD").strip() or "detached")
    # A SELF-TEST NEVER LANDS IN A REAL LANE'S HISTORY. Its verdict is about the judge, not about
    # a build, and letting one count toward the loop guards would mean a deliberately-failed
    # round pushing a real lane toward a STOP — or, worse, a deliberately-passed one clearing a
    # streak that was earned.
    if a.build_frame and not lane.endswith("-selftest"):
        lane += "-selftest"
    hist = history(a.history)

    # ── THE GUARDS RUN BEFORE THE ROUND, NOT AFTER IT ─────────────────────────────────────────
    # A guard checked only after a fresh round has run is a guard that always pays for one more
    # round. Worse, "never run additional rounds past a broken judge" cannot be honoured by a
    # check that happens at the end of the additional round.
    name, why = guards(hist, lane, a.park, a.gate_ruling)
    if name == "broken-judge" and a.redraw_seat is not None \
            and redraw_ruled(lane, a.redraw_seat, hist):
        # ── A RULED RE-DRAW RUNS UNDER THE GUARD IT IS THE ANSWER TO ─────────────────────────
        #
        # RULED (Rafe, 2026-09-13, lane art/jamb-211): "Judge cleared. ... Re-draw seats 1 and 4,
        # run the five-seat round again on the same frozen bar and the same deck. If a second
        # round trips the guard, stop and bring me the plants, not the seats."
        #
        # `judge_cleared()` excuses a round only once its re-drawn seats have CAUGHT — which is
        # right, and which means the re-draw itself could never start while the guard it repairs
        # is firing. So the marker may also name the seats to re-draw, in his words, and exactly
        # that invocation passes: the same round, the same frozen frame (checked below), one of
        # the named seats. Nothing else does. A re-draw that misses raises the seat-level term
        # on the spot, and the guard stands.
        print("\n== broken-judge is live, and this is the RULED re-draw of seat %d — proceeding "
              "under JUDGE-CLEARED.json" % a.redraw_seat)
        name = None
    if name:
        p = write_stall(name, why, hist, lane, cfg, a.stall_out)
        print("\n*** STOP — %s ***\n%s\n\nwritten: %s\n"
              % (name, why, os.path.relpath(p, REPO)))
        print("This is a LOOP-PROCESS §1.1.4 ruling trigger. The line does not restart itself.")
        return 3
    if a.check_guards:
        print("no guard fired for lane %r over %d verdict(s) in %s"
              % (lane, len([v for v in hist if lane_of(v) == lane]), a.history or HISTORY))
        return 0

    rnd = 1 + len([v for v in hist if lane_of(v) == lane])
    print("=" * 78)
    print("FRAME CRITIC — lane %s, round %d" % (lane, rnd))
    print("  commit   %s" % BID.head())
    print("=" * 78)

    # ── the build's frame ─────────────────────────────────────────────────────────────────────
    if a.build_frame:
        frame = os.path.join(REPO, a.build_frame) if not os.path.isabs(a.build_frame) \
            else a.build_frame
        fsha = hashlib.sha256(open(frame, "rb").read()).hexdigest()
        print("== BUILD FRAME OVERRIDDEN: %s  sha256 %s" % (a.build_frame, fsha[:16]))
        print("   This round is a SELF-TEST of the judge, not a verdict on a build.")
    elif a.no_capture or a.redraw_seat is not None:
        # A RE-DRAW NEVER RE-CAPTURES. Its whole premise is the SAME FROZEN BYTES — the seats that
        # are carried judged this exact frame, and a fresh capture would make their ballots
        # describe a picture they never saw. The sha is checked against the prior round below.
        frame = os.path.join(REPO, cfg["capture"]["frame"])
        fsha = hashlib.sha256(open(frame, "rb").read()).hexdigest()
        print("== capture skipped; judging %s as it sits (sha256 %s)"
              % (cfg["capture"]["frame"], fsha[:16]))
    else:
        frame, fsha = capture(cfg)

    # ── THE BUILD ID IS TAKEN AFTER THE CAPTURE, NOT BEFORE ───────────────────────────────────
    # The capture writes a PNG and a log into the tree, so an id taken beforehand is stale by the
    # time the verdict records it — and the gate, which recomputes it, would then refuse a build
    # that had passed. Same class of self-reference as the one `prove_gate.py` found in
    # build_id.py: the act of producing the evidence moved the thing the evidence names.
    bid, bdetail = BID.build_id()
    print("   build id %s%s" % (bid, "  (+dirty)" if bdetail["dirty"] else ""))

    # ── the deck ──────────────────────────────────────────────────────────────────────────────
    # Shuffled, so the seat cannot learn a slot. Seeded from the build id and the round, so the
    # shuffle is reproducible from the verdict file alone — a deck nobody can reconstruct is a
    # verdict nobody can check.
    # A self-test puts a morgue frame in the BUILD slot. It must not also be drawn as the plant —
    # the seat would be shown the same picture twice and the control would be judging itself.
    exclude = (os.path.basename(a.build_frame),) if a.build_frame else ()
    candidates = pick_plant(cfg["surface"], morgue, exclude=exclude, axis=cfg.get("axis"),
                            subject=cfg.get("subject"), regime=cfg.get("regime"))
    # THE REFERENCE TOO (same law): a reference captured under other light measures exposure,
    # not craft, and the deck refuses rather than compares.
    _ap = cfg.get("approved_capture") or {}
    if cfg.get("regime") and _ap and (_ap.get("regime") or "") != cfg["regime"]:
        raise SystemExit(
            "REFUSING: the approved reference (%s) is captured under regime %r and this deck's "
            "regime is %r. LAW (Rafe, 2026-09-12): the reference is captured under the deck's "
            "lighting regime. Rafe's walk seeds the regime's reference; there is no round before it."
            % (_ap.get("path"), _ap.get("regime") or "untagged", cfg["regime"]))

    # ══════════════════════════════════════════════════════════════════════════════════════════
    # MORE THAN ONE SEAT — RULED (Rafe, 2026-09-08).
    #
    #     "PASS-INSTALL rests on a single rank sample and flipped on identical bytes —
    #      impeachment upheld. Refine: majority of three independent blind seats rank the build
    #      above approved_capture, no unrouted flags from any; each seat its own axis-matched
    #      plant."
    #
    #     LAW: "a gate's binding term must have a measured noise floor and must never be a
    #      single sample."
    #
    # THE OCCASION, and it is one frame: lane polish-c-183 judged build sha 839fb12f twice, with
    # `picture moved mean 0.000 / worst 0` between them, and the build ranked 1 OF 4 then 2 OF 4.
    # It and the reference SWAPPED PLACES with no pixel changing. Under the rule ratified that
    # morning — rank above `approved_capture` — the same bytes were PASS-INSTALL and then FAIL.
    #
    # EVERY SEAT IS INDEPENDENT: its own working directory, its own shuffle, its own plant draw
    # from the axis-matched set. The directory name is a hash of (lane, round, build, seat) for
    # the reason the round number is hashed — a seat that can read "seat 2 of 3" off its own cwd
    # can infer it is one of a panel, and the whole signal depends on it judging the picture
    # rather than the apparatus.
    #
    # THE MORGUE'S TONAL SET IS THREE NOW, AND THE VOID OF 2026-09-10 IS WHY.
    # `combined`/`tonal` held exactly ONE entry, so five seats drew one picture five times, one
    # seat missed it, and the round voided on a judge that four seats had passed. There was
    # nothing to see the miss AGAINST. RULED (Rafe, 2026-09-11): seed two more from his culls —
    # `lamp-clip-figure` (bright: the lamp destroying its subject) and `crushed-midband` (dark:
    # the 2026-08-27 device FAIL, and the morgue's first dark-side plant, since every tonal
    # entry before it failed bright). Dealt without replacement below, three plants across five
    # seats exercises all three every round.
    # ── PLANTS ARE DEALT WITHOUT REPLACEMENT — the ruled property, delivered ─────────────────
    #
    # RULED (Rafe, 2026-09-08): "each seat its own axis-matched plant."
    #
    # The first implementation had each seat draw INDEPENDENTLY, which delivers that only
    # probabilistically. Measured over 900 simulated rounds with a two-member set and three
    # seats: **all three seats draw the SAME plant in 24.8% of rounds** (chance is exactly 25%,
    # so the draw was sound — the DESIGN was what fell short). A quarter of rounds therefore had
    # fully correlated plant evidence, which is the thing the second plant was seeded to end.
    #
    # It cost a VOID immediately: r002 on this lane hit that case, all three seats drew the
    # weaker plant, one missed it, and the round was void on a judge that two seats had passed.
    #
    # Dealing without replacement — shuffle the candidates once per round, then seat i takes
    # i mod n — guarantees that with two plants and three seats BOTH are exercised every round.
    # A seat missing one is then visible against another seat catching the other, which is the
    # discrimination a panel is for. With one candidate it is identical to the old behaviour.
    seat_redraws = []
    seat_plant_stop = None
    deal = list(candidates)
    random.Random(hashlib.sha256(("%s|%d|%s|deal" % (lane, rnd, bid)).encode())
                  .hexdigest()).shuffle(deal)

    def seat_round(seat_idx, redraw=False):
        """One independent seat: its own deck, its own shuffle, its own plant. Returns a dict.

        `redraw` re-seats a slot whose ballot was discarded for missing a live plant (ruled
        2026-09-11). It salts differently, so the fresh seat cannot land in the discarded one's
        working directory or inherit its shuffle, and it STEPS the plant, so wherever the morgue
        holds more than one candidate the re-draw is a different picture. Re-running the same
        seat against the same plant would not be a re-draw, it would be a retry.
        """
        salt = "%s|%d|%s|seat%d%s" % (lane, rnd, bid, seat_idx, "|redraw" if redraw else "")
        srng = random.Random(hashlib.sha256(salt.encode()).hexdigest())
        seat_plant = deal[(seat_idx + (1 if redraw else 0)) % len(deal)]
        work = os.path.join(os.path.expanduser(cfg.get("work_dir", "~/.claude/frame-critic")),
                            "deck-" + hashlib.sha256(salt.encode()).hexdigest()[:16])
        if os.path.commonpath([os.path.realpath(work), os.path.realpath(REPO)]) \
                == os.path.realpath(REPO):
            raise SystemExit("REFUSING: work_dir is inside the repo. §3.1 — the seat's cwd is "
                             "outside it, so the seat cannot read its way to the answer.")
        shutil.rmtree(work, ignore_errors=True)
        os.makedirs(work)

        crop = cfg.get("crop")
        deck = [("build", frame, crop),
                ("plant", os.path.join(MORGUE, seat_plant["file"]), crop)]
        if cfg.get("approved_capture"):
            deck.append(("approved", os.path.join(REPO, cfg["approved_capture"]["path"]), crop))
        bar = cfg.get("asset_bar")
        if bar:
            # §13.3: measurements leave, pixels never do. The bar crop is written into the seat's
            # working directory OUTSIDE the repo and nowhere else.
            deck.append(("bar", bar["image"], bar.get("crop")))
        srng.shuffle(deck)

        mapping = {}
        slots = dict(plant=None, build=None, bar=None, approved=None)
        for i, (what, path, box) in enumerate(deck, start=1):
            size = crop_to(path, box, os.path.join(work, "%d.png" % i))
            mapping[str(i)] = dict(what=what, source=os.path.relpath(path, REPO)
                                   if path.startswith(REPO) else path,
                                   sha256=hashlib.sha256(open(path, "rb").read()).hexdigest(),
                                   crop=box, delivered=list(size))
            slots[what] = i

        print("\n== seat %d of %d — deck (%d frames, shuffled, unlabelled) — cwd %s"
              % (seat_idx + 1, a.seats, len(deck), work))
        for i in sorted(mapping, key=int):
            print("   %s.png  %-9s %s" % (i, mapping[i]["what"], mapping[i]["source"]))
        print("   plant: %s — %s" % (seat_plant["file"], seat_plant["verbatim"]))

        prompt = open(os.path.join(HERE, "seat_prompt.txt")).read().replace(
            "the numbered PNG files in this directory",
            "the files %s in this directory" % ", ".join("%d.png" % i
                                                         for i in range(1, len(deck) + 1)))
        tp = os.path.join(HISTORY, "r%03d-%s-transcript%s.txt"
                          % (rnd, lane.replace("/", "_"),
                             "" if (a.seats == 1 and a.redraw_seat is None)
                             else ("-seat%d-redraw" % (seat_idx + 1)
                                   if a.redraw_seat is not None
                                   else "-seat%d" % (seat_idx + 1))))
        # ⚠ A RE-DRAW WRITES ITS OWN FILE AND NEVER THE ORIGINAL'S. Naming it for the seat alone
        # cost r004's original seat-2 ballot when I renamed files by hand — untracked, gone. A
        # re-drawn ballot is a SECOND ballot on the same bytes, not a replacement for the first.

        # ── A RE-DRAW NEVER RE-ROLLS, AND THIS CHECK RUNS BEFORE THE SEAT IS SPENT ──────────
        #
        # If this seat has already been re-drawn on these frozen bytes, ITS BALLOT STANDS. The
        # first re-draw of seat 2 flagged the build and ranked it below; re-running until a
        # kinder seat turns up is exactly what a plant exists to prevent, arriving through the
        # door marked "the reporting code crashed". A crash in my own summary is not grounds to
        # re-open a verdict already cast.
        #
        # ⚠ IT WAS FIRST WRITTEN AFTER `run_seat` AND SO GUARDED NOTHING — the seat was already
        # spent by the time it was consulted, and a live run had to be killed mid-flight. A
        # no-re-roll guard downstream of the roll is a comment, not a guard.
        if a.redraw_seat is not None and os.path.exists(tp):
            print("   seat %d's re-drawn ballot is already on disk — REUSED, not re-rolled (%s)"
                  % (seat_idx + 1, os.path.relpath(tp, REPO)))
            return dict(seat=seat_idx + 1, work_dir=work, slots=slots, mapping=mapping,
                        plant=seat_plant, text=open(tp).read(),
                        transcript=os.path.relpath(tp, REPO), reused=True)

        print("   running (fresh claude -p, no repo access)...")
        text = run_seat(work, prompt, a.timeout)
        os.makedirs(HISTORY, exist_ok=True)
        with open(tp, "w") as f:
            f.write(text)
        return dict(seat=seat_idx + 1, work_dir=work, slots=slots, mapping=mapping,
                    plant=seat_plant, text=text, transcript=os.path.relpath(tp, REPO))

    crop = cfg.get("crop")          # the deck's crop, used again for the round's perceptual hash

    # ── A MIS-TAGGED PLANT VOIDS A SEAT, NOT A ROUND — RULED (Rafe, 2026-09-08) ──────────────
    #
    #     "a seat voided by a mis-tagged plant is re-drawn, not the round; a correct plant missed
    #      still voids."
    #
    # The distinction is between a fault in the DECK'S CONFIGURATION and a fault in the JUDGE. A
    # seat handed a plant whose axis does not match the round's question was never asked a
    # answerable question — §1.2.1: a plant controls on the axis its cull was made on, and the
    # right image for the wrong question is not a control at all. Throwing away two seats that
    # judged correctly, plus a capture, to repair one mis-configured deck is a cost with no
    # evidentiary return.
    #
    # ⚠ AND THE OTHER HALF IS WHAT KEEPS IT HONEST: a seat that misses a CORRECTLY tagged plant
    # still voids the whole round, exactly as §4 says. This re-draw is available only where the
    # tag was wrong, it names the mis-tagged plant, and it is recorded as an added artifact — so
    # it can never become "re-run the seat that disagreed with me".
    if a.redraw_seat is not None:
        prior_path = os.path.join(HISTORY, "r%03d-%s.json" % (rnd - 1, lane.replace("/", "_")))
        if not os.path.exists(prior_path):
            raise SystemExit("REFUSING: --redraw-seat needs the lane's last round on disk; "
                             "%s is not there." % os.path.relpath(prior_path, REPO))
        prior = json.load(open(prior_path))
        pf = (prior.get("build_frame") or {}).get("sha256")
        now = hashlib.sha256(open(frame, "rb").read()).hexdigest()
        if pf != now:
            raise SystemExit("REFUSING: the frame has moved since that round (%s -> %s). A "
                             "re-draw is only lawful on the SAME FROZEN BYTES." % (pf[:16], now[:16]))
        rnd = prior["round"]
        seats = []
        for old_seat in prior["panel"]["per_seat"]:
            i = old_seat["seat"] - 1
            if old_seat["seat"] == a.redraw_seat:
                print("\n== RE-DRAWING seat %d — its plant (%s) was mis-tagged for axis '%s'"
                      % (old_seat["seat"], old_seat["plant"], cfg.get("axis")))
                seats.append(seat_round(i))
            else:
                tp = os.path.join(REPO, old_seat["transcript"])
                sd = dict(seat=old_seat["seat"], work_dir=old_seat["work_dir"],
                          slots=dict(plant=None, build=None, bar=None, approved=None),
                          mapping={}, plant={"file": old_seat["plant"], "verbatim": "(carried)"},
                          text=open(tp).read(), transcript=old_seat["transcript"],
                          carried=True)
                seats.append(sd)
                print("   seat %d carried unchanged from that round (%s, caught=%s)"
                      % (old_seat["seat"], old_seat["plant"], old_seat["caught"]))
        _prior_plant = prior["panel"]["per_seat"][a.redraw_seat - 1].get("plant")
        _prior_retired = _plant_retired(_prior_plant, morgue)
        redraw_note = dict(round=rnd, seat=a.redraw_seat,
                           discarded_plant=_prior_plant,
                           discarded_plant_retired=_prior_retired,
                           frame_sha256=now,
                           # ⚠ TWO RULINGS REACH THIS PATH AND THEY GIVE DIFFERENT REASONS.
                           # It was built for 2026-09-08's mis-tagged case and would stamp
                           # "mis-tagged" on a plant that was correctly tagged, which is a false
                           # record of why a ballot was discarded. The reason is now derived from
                           # the morgue: a RETIRED plant is the deck-config fault the older
                           # ruling describes; a LIVE one is the seat-level miss ruled on
                           # 2026-09-11, which discards the ballot and re-draws the slot.
                           ruling=(
                               ("a seat voided by a mis-tagged plant is re-drawn, not the round. "
                                "— Rafe, 2026-09-08")
                               if _prior_retired else
                               ("a live-plant miss voids the seat, not the round. The ballot is "
                                "discarded; the slot is re-drawn once (fresh seat, fresh plant); "
                                "the round is valid when it holds five caught ballots. — Rafe, "
                                "2026-09-11, superseding 'a correct plant missed still voids "
                                "[the round]'")))
    else:
        redraw_note = None
        ok, msg = _headroom("seat")
        if not ok:
            print("\n*** STOP — %s" % msg)
            raise SystemExit(3)
        seats = []
        for i in range(a.seats):
            seats.append(seat_round(i))
            err = score_seat(seats[-1])
            if err:
                print("\n%s" % err)
                print("transcript: %s" % seats[-1]["transcript"])
                return 4
            stray = _no_lingering_seats()
            if stray:
                print("   ⚠ %d seat process(es) still alive after seat %d: %s"
                      % (len(stray), i + 1, ", ".join(stray)))
            ok, msg = _headroom("seat")
            if not ok and i + 1 < a.seats:
                print("\n*** STOP after seat %d — %s" % (i + 1, msg))
                raise SystemExit(3)

        # ── A LIVE-PLANT MISS VOIDS THE SEAT, NOT THE ROUND ──────────────────────────────────
        #
        # RULED (Rafe, 2026-09-11), superseding "a correct plant missed still voids [the round]"
        # (2026-09-08):
        #
        #     "a live-plant miss voids the seat, not the round. The blind seat's ballot is
        #      discarded; the slot is re-drawn once (fresh seat, fresh plant); the round is valid
        #      when it holds five caught ballots. Unanimity is preserved where it matters: every
        #      counted ballot caught its plant. Broken-judge = a slot whose re-draw also misses,
        #      or two slots missing in one round."
        #
        # THE ARITHMETIC IS THE REASON, and it is recorded here because the old rule was not
        # wrong in principle — it was wrong at five seats. Measured per-seat catch rate on LIVE
        # plants across this lane: 17 of 19 = 89.5%. Under a unanimous round-level rule:
        #
        #     seats  P(all catch)  rounds VOID
        #       1       89.5%         10.5%
        #       5       57.3%         42.7%      <- the panel ruled for the regression term
        #
        # More seats made a VOID MORE likely, not less: five seats was ruled to cut RANK noise,
        # and the plant gate being unanimous meant the same change multiplied the chance of
        # tripping it. The two ruled terms pulled in opposite directions.
        #
        # ⚠ AND THE RE-SCOPE'S OWN FIGURE IS RECORDED AS MEASURED RATHER THAN AS QUOTED. The
        # ruling cites ~5% VOID. As ruled — BOTH trip conditions live — it is 12.5%:
        #
        #     k=0 misses                        57.3%  valid
        #     k=1, the re-draw catches          30.2%  valid
        #     k=1, the re-draw also misses       3.6%  broken-judge
        #     k>=2 slots miss                    8.9%  broken-judge
        #                                       -----
        #                              VOID     12.5%
        #
        # 5.4% is what the rule gives if the ONLY trip is a failed re-draw. The difference is the
        # two-slot tripwire, which is explicit in the ruling and is carried as written — two
        # independent misses in one round IS evidence about the judge, and dropping it to hit a
        # number would be fitting the law to the arithmetic instead of the other way round.
        # Either way the line moves: 42.7% -> 12.5% is a 3.4x reduction.
        misses = live_misses(seats, morgue)     # every seat is SCORED by now; see score_seat()

        if len(misses) >= 2:
            seat_plant_stop = ("%d seats missed a live plant in one round (seats %s). That is "
                               "not one seat having a bad day." % (
                                   len(misses), ", ".join(str(i + 1) for i in misses)))
        elif len(misses) == 1:
            slot = misses[0]
            # `slot` here is the SEAT index, not the plant's deck slot — the 2026-09-12 record
            # printed "slot 1" for seat 1 while the plant sat in deck slot 4, and the two were
            # read against each other. Both are named now.
            print("\n== seat %d missed a LIVE plant (deck slot %s) — the BALLOT is discarded and "
                  "the seat is re-drawn once"
                  % (slot + 1, seats[slot]["slots"].get("plant")))
            print("   ruled 2026-09-11: a live-plant miss voids the seat, not the round.")
            ok, msg = _headroom("seat")
            if not ok:
                print("\n*** STOP — %s" % msg)
                raise SystemExit(3)
            # A FRESH SEAT AND A FRESH PLANT. The salt carries `redraw` so the new seat cannot
            # land in the discarded one's working directory or draw its deck order, and the
            # plant is stepped so it is a different picture wherever the morgue allows one.
            fresh = seat_round(slot, redraw=True)
            err = score_seat(fresh)
            if err:
                print("\n%s" % err)
                print("transcript: %s" % fresh["transcript"])
                return 4
            discarded = seats[slot]
            discarded["discarded"] = True
            discarded["discarded_why"] = "missed a live plant; ballot not counted (ruled 2026-09-11)"
            seat_redraws.append(dict(seat=slot + 1, slot=slot + 1,
                                     discarded_plant=discarded.get("plant"),
                                     redraw_plant=fresh.get("plant"),
                                     redraw_caught=bool(fresh.get("caught"))))
            if not fresh.get("caught"):
                seat_plant_stop = ("seat %d missed a live plant and its RE-DRAW missed too "
                                   "(%s, then %s). A seat that misses twice is the judge, not "
                                   "the draw." % (slot + 1, discarded.get("plant", {}).get("file"),
                                                  fresh.get("plant", {}).get("file")))
            else:
                print("   re-draw CAUGHT %s — the round holds %d caught ballots."
                      % (fresh.get("plant"), a.seats))
                seats[slot] = fresh
    # The first seat's deck is the one the verdict's top-level fields describe, so a single-seat
    # round records exactly what it always did.
    # THE ROUND'S TOP-LEVEL DESCRIPTORS COME FROM A SEAT THAT ACTUALLY RAN. On a re-draw the
    # first seat may be CARRIED, and a carried seat has no deck of its own — its ballot was cast
    # in the earlier round and only its numbers are kept. Describing the round from it produced
    # "rank ? of 0" and "NO APPROVED FRAME IN THE DECK" on a round whose deck plainly had one.
    desc = next((sd for sd in seats if not sd.get("carried")), seats[0])
    work, mapping = desc["work_dir"], desc["mapping"]
    plant = desc["plant"]
    plant_slot, build_slot = desc["slots"]["plant"], desc["slots"]["build"]
    bar_slot, approved_slot = desc["slots"]["bar"], desc["slots"]["approved"]
    deck = [x for x in (1, 2, 3, 4) if str(x) in mapping]
    text, tpath = desc["text"], os.path.join(REPO, desc["transcript"])

    # ── EVERY SEAT PARSED AND SCORED SEPARATELY, THEN THE VOTE ────────────────────────────────
    prior_by_seat = {}
    if a.redraw_seat is not None:
        prior_by_seat = {p["seat"]: p for p in prior["panel"]["per_seat"]}
    for sd in seats:
        if sd.get("carried"):
            # A CARRIED SEAT IS NOT RE-JUDGED. Its ballot stands exactly as recorded; re-parsing
            # it would risk a different reading of the same words, which is not what "carried"
            # means. Its numbers are taken from the round it was cast in.
            p0 = prior_by_seat[sd["seat"]]
            sd.update(rank=p0["rank"], approved_rank=p0["reference_rank"],
                      above_approved=p0["above_reference"],
                      not_below=p0.get("not_below",
                                       p0["rank"] <= p0["reference_rank"] + REF_SLACK),
                      build_flagged=p0["build_flagged"], shipped=p0["shipped"],
                      caught=p0["caught"],
                      how=dict(ranked_last=None, named_worst=None, flagged=p0["build_flagged"],
                               shipped=p0["shipped"], outranked_build=None,
                               every_frame_flagged=None, carried=True),
                      r={"_rank": [], "_flagged": [], "_ship": [],
                                                      "RANK": "", "SHIP": "", "FLAGGED": "",
                                                      "WORST": "", "WORST_WHY": "", "BEST": "",
                                                      "BEST_WHY": "", "_flip_blocks": {},
                                                      "_worst": None})
            continue
        if sd.get("scored"):
            continue                    # scored the moment it returned — score_seat()
        err = score_seat(sd)
        if err:
            print("\n%s" % err)
            print("transcript: %s" % sd["transcript"])
            return 4

    _t = panel_tally(seats)
    n_above, n_flagged, n_shipped = _t["above"], _t["flagged"], _t["shipped"]
    all_caught, majority_above = _t["all_caught"], _t["majority_above"]
    n_not_below, majority_not_below = _t["not_below"], _t["majority_not_below"]

    # THE ITEM'S OWN MEASURED EXIT — declared in docs/FRAME-CRITIC.json, recorded here, re-checked
    # at the gate. It is what stops "not worse than the reference" from meaning "not different":
    # a build installs because it DID THE THING, and the rank test only says it cost nothing.
    item_exit = cfg.get("item_exit") or {}
    exit_met = bool(item_exit.get("met")) and bool(str(item_exit.get("measured") or "").strip())

    if len(seats) > 1:
        print("\n== the panel (%d independent seats)" % len(seats))
        print("   %-5s %-10s %-12s %-9s %-8s %s"
              % ("seat", "build rank", "reference", "above ref", "flagged", "plant"))
        for sd in seats:
            print("   %-5d %-10s %-12s %-9s %-8s %s"
                  % (sd["seat"], sd["rank"], sd["approved_rank"],
                     "YES" if sd["above_approved"] else "no",
                     "yes" if sd["build_flagged"] else "no",
                     "CAUGHT" if sd["caught"] else "MISSED"))
        print("   ---> above the reference in %d of %d seats; NOT BELOW it in %d of %d — %s"
              % (n_above, len(seats), n_not_below, len(seats),
                 "MAJORITY (install bar)" if majority_not_below else "NO MAJORITY"))
        # THE COMPARATOR'S OWN ERROR BAR, on this round's bytes. A binding term without one is a
        # single sample wearing a threshold (LAW, Rafe 2026-09-08).
        print("   rank's noise floor on THESE bytes: %d/%d seats disagree with the majority"
              % (min(n_above, len(seats) - n_above), len(seats)))
        distinct = len({sd["plant"]["file"] for sd in seats})
        if distinct == 1:
            # ⚠ SAY WHICH IT IS. This line used to assert the CAUSE — "the axis-matched set has
            # one member" — without checking it, and was printed on a round where the set had
            # two. A note that states an unchecked cause is the same defect as an instrument
            # that does (§13.10): report what was observed, and the count that explains it.
            print("   ⚠ every seat drew the SAME plant (%s), so the plant catches are CORRELATED"
                  % seats[0]["plant"]["file"])
            print("     — the panel multiplies rank samples, not the plant's evidence.")
            print("     axis-matched candidates available: %d%s"
                  % (len(candidates),
                     " — seed another cull on this axis" if len(candidates) == 1 else ""))
        else:
            print("   plants dealt without replacement: %d distinct across %d seats"
                  % (distinct, len(seats)))

    try:
        r = parse(text, len(deck))
    except ValueError as e:
        print("\n%s" % e)
        print("transcript: %s" % os.path.relpath(tpath, REPO))
        return 4

    caught, how = desc["caught"], desc["how"]
    flips = r["_flip_blocks"].get(build_slot, [])

    # ── WHERE THE BUILD PLACED, which is this round's contribution to the progress signal ──────
    n = len(deck)
    rk = r["_rank"]
    pos = (rk.index(build_slot) + 1) if build_slot in rk else None
    score = rank_score(pos, n)
    bar_pos = (rk.index(bar_slot) + 1) if bar_slot and bar_slot in rk else None
    app_pos = (rk.index(approved_slot) + 1) if approved_slot and approved_slot in rk else None

    # ── THE COMPARATIVE HALF OF PASS — LOOP-PROCESS §5's visual bar, as a rank ─────────────────
    #
    # §5: *blind side-by-side against shipped commercial games, asking which of these looks like
    # the shipped game. The answer must be Yarl or a tie.* The deck forbids ties, so the closest
    # representable thing to a tie is one place below the bar, and that is what NEAR_BAR_SLACK
    # buys — declared here, before any round, and not widened afterwards (§8).
    #
    # AND THE BUILD MUST NOT SIT BELOW THE LAST FRAME RAFE APPROVED. A frame that ranks under the
    # baseline is a regression however well the seat speaks of it.
    beats_approved = (app_pos is None) or (pos is not None and pos <= app_pos)
    near_bar = (bar_pos is None) or (pos is not None and pos <= bar_pos + NEAR_BAR_SLACK)

    # ⚠ ASSUMPTION, STATED RATHER THAN BURIED. The amendment specifying these guards described
    # PASS as *"still = ranks at/above the last-approved frame and near the bar"*. On main PASS
    # was SHIP-based and said nothing about rank, so "still" cannot be read as *unchanged*. It is
    # taken here as *not loosened*: PASS is the CONJUNCTION of the rule that shipped and the
    # comparative rule above. That can only ever refuse more builds than either reading alone,
    # which is the safe direction for an install gate to be wrong in.
    #
    # If rank alone was meant, delete the two SHIP terms from the line below — it is one edit,
    # and it loosens the gate, so it is Rafe's to make rather than mine.
    # ── PASS-INSTALL — RULED (Rafe, 2026-09-08). THE SEEDED REFERENCE IS THE INSTALL BAR. ─────
    #
    #     "PASS for polish rounds against a seeded reference = rank above approved_capture AND no
    #      unrouted flags -> PASS-INSTALL; SHIP stays recorded as the wowed signal, not the
    #      install gate — the seeded reference is the human-ratified install bar."
    #
    # THE REASON, and it is the whole of why this is not a loosening: **SHIP AND RANK WAS
    # RATIFIED AGAINST A NULL REFERENCE.** Every round the combined lane ever ran before
    # 2026-09-07 recorded *"NO APPROVED FRAME IN THE DECK — that half of the bar is untested this
    # round"*, so SHIP was the only thing standing between a build and the phone and it had to
    # carry the whole gate alone. With a serviceable reference seeded, the deck contains a frame
    # the human gate has ALREADY ratified as installable, and beating it gates the build ABOVE
    # THE BAR IT MEASURES AGAINST. SHIP then answers a different and stricter question — would a
    # stranger ship this as finished work — which is worth recording and is not what an install
    # needs to clear.
    #
    # It REQUIRES the reference. With `approved_capture` null there is nothing to be above, and
    # the rule falls back to the ratified SHIP-and-rank conjunction — the state it was written
    # for. A gate that silently weakens when its comparator goes missing is the failure this
    # whole mechanism exists to avoid.
    #
    # "No unrouted flags" is evaluated HERE as "the seat did not flag the build". A flagged build
    # is a FAIL at round time; only the human gate can route a flag, and it does so by amending
    # the verdict with a quoted ruling per item — which `critic_gate.py` then re-validates.
    # ── THE VOTE, not a sample — RULED (Rafe, 2026-09-08) ─────────────────────────────────────
    #
    # "majority of three independent blind seats rank the build above approved_capture, no
    #  unrouted flags from any". Both halves are asymmetric ON PURPOSE and the asymmetry is the
    #  ruling: rank is the NOISY term so it takes a majority, while a flag is a FINDING and one
    #  seat finding it is enough. A flag outvoted 2-1 is still a defect two seats missed.
    #
    # EVERY SEAT MUST CATCH ITS PLANT. §4 voids a round on one missed plant; a panel does not
    # dilute that into an average, because a soft seat's ballot is exactly what §4 refuses to
    # read. With one seat this is the rule that always applied; with three it is strictly harder.
    approved_in_deck = app_pos is not None
    unflagged_all = n_flagged == 0

    verdict = panel_verdict(seats, approved_in_deck, beats_approved, near_bar, exit_met)

    # ── A SEAT-LEVEL PLANT STOP IS BROKEN-JUDGE AT ONCE, NOT AFTER A SECOND ROUND ────────────
    #
    # The legacy guard fires on two consecutive VOIDs, which was right while a VOID meant "one
    # seat missed" — a thing that happens to a working judge 43% of the time at five seats. Under
    # the 2026-09-11 re-scope a VOID means something much stronger and much rarer: a slot missed
    # a live plant TWICE with a fresh seat and a fresh plant, or two independent slots missed in
    # the same round. Either is the judging layer, not the draw, so waiting for a second round to
    # agree would be collecting evidence nobody needs.
    if seat_plant_stop:
        verdict = "VOID"


    print("\n== the seat said")
    print("   RANK    %s" % (r["RANK"] or "(unparsed)")[:120])
    print("   SHIP    %s" % (r["SHIP"] or "(unparsed)")[:120])
    print("   FLAGGED %s" % (r["FLAGGED"] or "(unparsed)")[:120])
    print("   WORST   %s — %s" % (r["WORST"] or "?", " ".join(r["WORST_WHY"].split())[:110]))
    print("   BEST    %s — %s" % (r["BEST"] or "?", " ".join(r["BEST_WHY"].split())[:110]))
    print("\n   plant was slot %s: ranked_last=%s named_worst=%s flagged=%s shipped=%s -> %s"
          % (plant_slot, how["ranked_last"], how["named_worst"], how["flagged"], how["shipped"],
             "CAUGHT" if caught else "MISSED"))
    print("   build was slot %s" % build_slot)
    if how["outranked_build"]:
        print("\n   ⚠ THE PLANT OUTRANKED THE BUILD. A blind seat put a frame Rafe personally")
        print("     culled — \"%s\" — above this one." % plant["verbatim"])
        print("     That is a statement about the build, not about the judge, and it is not")
        print("     part of the verdict. It is the most important line in this round.")
    if how["every_frame_flagged"]:
        print("   note: the seat flagged EVERY frame, including the commercial bar. The plant")
        print("         check still holds (it declined to ship a culled frame) but the flag")
        print("         carries no discrimination this round.")

    # ── the progress signal, and the whole series it belongs to ───────────────────────────────
    lane_hist = lane_rounds(hist, lane)
    prior = [prog(v).get("rank_score") for v in readable(lane_hist)]
    prior = [s for s in prior if s is not None]
    best_before = max(prior) if prior else None
    new_best = (score is not None and verdict != "VOID"
                and (best_before is None or score > best_before))
    prev = readable(lane_hist)
    prev_sig = prog(prev[-1]).get("capture_signature") if prev else None
    this_sig = signature(frame, crop)
    moved = sig_delta(prev_sig, this_sig)

    # The series carries the signature of every round, because the frames themselves are
    # overwritten by the next capture and a distance you cannot recompute is a distance you
    # cannot check.
    series = [dict(round=v.get("round"), verdict=v.get("verdict"),
                   rank_position=prog(v).get("rank_position"),
                   deck_size=prog(v).get("deck_size"),
                   rank_score=prog(v).get("rank_score"),
                   capture_signature=prog(v).get("capture_signature"))
              for v in lane_hist]
    series.append(dict(round=rnd, verdict=verdict, rank_position=pos, deck_size=n,
                       rank_score=score, capture_signature=this_sig))

    print("\n== progress")
    print("   rank      %s of %s   score %s%s"
          % (pos or "?", n, ("%.2f" % score) if score is not None else "?",
             "   <- NEW BEST" if new_best else ""))
    print("   best so far %s" % (("%.2f" % best_before) if best_before is not None else "(none)"))
    if bar_pos:
        print("   the bar ranked %s; near-bar %s (slack %d)"
              % (bar_pos, "YES" if near_bar else "NO", NEAR_BAR_SLACK))
    if app_pos:
        print("   the approved frame ranked %s; at-or-above %s"
              % (app_pos, "YES" if beats_approved else "NO"))
    else:
        print("   NO APPROVED FRAME IN THE DECK — that half of the bar is untested this round.")
    print("   picture moved %s from the previous readable round"
          % ("(first round)" if moved is None
             else "mean %.3f / worst %d luminance levels (floors %.2f / %d)"
                  % (moved[0], moved[1], NO_CHANGE_MAD, NO_CHANGE_MAX)))

    adv = two_strikes_advisory(lane_hist + [dict(lane=lane, round=rnd, verdict=verdict,
                                                 flip_list=flips)])
    if adv:
        print("\n   two-strikes (ADVISORY, not a stop): the same request has now survived rounds")
        print("     %s and %s — \"%s\"" % (adv["rounds"][0], adv["rounds"][1],
                                          " ".join(adv["items"][1].split())[:100]))
        print("     The line continues because rank moved. Builder's judgement whether it should.")

    out = dict(
        schema="frame-critic/1",
        verdict=verdict,
        lane=lane,
        round=rnd,
        surface=cfg["surface"],
        # THE AXIS THE DECK ASKED ON, recorded because §4's per-axis law makes the verdict
        # unreadable without it. The axis chooses the plant, so a verdict that does not name it
        # cannot be checked later against the rule it was drawn under — and "the deck is
        # reproducible from the verdict file alone" is the property that makes these files worth
        # committing. It was selecting plants correctly and saying nothing about it.
        axis=cfg.get("axis"),
        commit=bdetail["commit"],
        dirty=bdetail["dirty"],
        build_id=bid,
        timestamp=datetime.datetime.now().isoformat(timespec="seconds"),
        build_frame=dict(path=os.path.relpath(frame, REPO) if frame.startswith(REPO) else frame,
                         sha256=fsha,
                         # The log's first line is the resolved engine invocation — every flag the
                         # frame was taken with. §2.3: evidence carries its producer.
                         log=cfg["capture"].get("log") if not a.build_frame else None,
                         flags=(open(os.path.join(REPO, cfg["capture"]["log"])).readline().strip()
                                if (not a.build_frame and cfg["capture"].get("log")
                                    and os.path.exists(os.path.join(REPO,
                                                                    cfg["capture"]["log"])))
                                else None)),
        deck=dict(work_dir=work, slots=mapping, build=os.path.relpath(frame, REPO)
                  if frame.startswith(REPO) else frame),
        plant=dict(slot=plant_slot, file=plant["file"], sha256=plant["sha256"],
                   culled_by=plant["culled_by"], verbatim=plant["verbatim"],
                   caught=caught, how=how),
        seat={k: r[k] for k in LABELS},
        # ── A VOID ROUND CARRIES NO READABLE FLIP LIST ────────────────────────────────────────
        #
        # §4: if the critic does not catch the plant the round is void and its findings are NOT
        # READ — not discounted, void. A soft critic's findings are worse than none because they
        # will be acted on.
        #
        # ⚠ AND THEY WERE BEING PUT IN FRONT OF A READER. `critic_gate.py` printed the flip list
        # for every non-PASS verdict, void included, on the most-read surface the mechanism has.
        # Found the first time a round actually voided. So the withholding lives in the DATA
        # rather than in each reader's manners: every consumer — the gate, the stall report, the
        # guards — is now correct by construction. The findings are kept, under a name that says
        # what they are, because deleting evidence is a different sin.
        flip_list=[] if verdict == "VOID" else flips,
        flip_list_withheld=(flips if verdict == "VOID" else None),
        withheld_because=("LOOP-PROCESS §4: the plant was missed, so this round's findings are "
                          "not read. Nothing here may be cited, quoted, or acted on."
                          if verdict == "VOID" else None),
        # ── THE PROGRESS SIGNAL, AND THE WHOLE SERIES, IN EVERY VERDICT ───────────────────────
        # The series is written into each round's own file rather than only being derivable by
        # walking history/. A counter a restart can clear is a suggestion with a number in it —
        # and so is one that lives only in a directory listing. Here it is in the diff, it is in
        # the verdict the PR carries, and it is in the stall report.
        panel=dict(
            seats=len(seats),
            ruling=("five seats; block only on strong-majority regression (>=4 of 5 rank below "
                    "the reference); else install if exit met and plant caught. Rank-in-deck "
                    "cannot resolve polish-sized deltas at a 40% flip rate. — Rafe, 2026-09-09"),
            law=("a gate's binding term must have a measured noise floor and must never be a "
                 "single sample. — Rafe, 2026-09-08"),
            # WHAT THE PANEL ITSELF RETURNED, before any human or builder touched the file. A
            # verdict that later differs from this is an AMENDMENT and the gate demands the
            # record — which is how "the enforcement is visibility" is made checkable rather
            # than trusted.
            verdict_at_round=verdict,
            above_reference=n_above, not_below_reference=n_not_below,
            below_reference=len(seats) - n_not_below,
            strong_regression=((len(seats) - n_not_below) * 5 >= len(seats) * 4),
            ref_slack=REF_SLACK, flagged_by=n_flagged, shipped_by=n_shipped,
            majority_above=majority_above, majority_not_below=majority_not_below,
            all_caught=all_caught,
            # THE SEAT-LEVEL PLANT TERM'S TRAIL (ruled 2026-09-11). A discarded ballot is not
            # counted and not hidden: the slot, the plant it missed, the plant its re-draw drew
            # and whether that caught are all on the record, so "five caught ballots" can be
            # checked rather than taken on trust.
            seat_redraws=seat_redraws,
            seat_plant_stop=seat_plant_stop,
            dissent=min(n_above, len(seats) - n_above),
            plants_distinct=len({sd["plant"]["file"] for sd in seats}),
            per_seat=[dict(seat=sd["seat"], rank=sd["rank"],
                           reference_rank=sd["approved_rank"],
                           above_reference=sd["above_approved"],
                           not_below=sd["not_below"],
                           build_flagged=sd["build_flagged"], shipped=sd["shipped"],
                           plant=sd["plant"]["file"], caught=sd["caught"],
                           transcript=sd["transcript"], work_dir=sd["work_dir"])
                      for sd in seats],
        ),
        seat_redraw=redraw_note,
        item_exit=item_exit,
        progress=dict(
            rank_position=pos, deck_size=n, rank_score=score,
            bar_position=bar_pos, approved_position=app_pos,
            approved_frame_in_deck=bool(approved_slot),
            beats_approved=beats_approved, near_bar=near_bar,
            near_bar_slack=NEAR_BAR_SLACK,
            new_best=new_best, best_before=best_before,
            capture_signature=this_sig,
            moved=None if moved is None else dict(mad=moved[0], max_cell=moved[1]),
            two_strikes_advisory=adv,
            series=series,
            guards=dict(stall_rounds=STALL_ROUNDS, round_ceiling=ROUND_CEILING,
                        no_change_mad=NO_CHANGE_MAD, no_change_max=NO_CHANGE_MAX,
                        sig_grid=SIG_GRID, judge_misses=JUDGE_MISSES,
                        flip_same=FLIP_SAME),
        ),
        transcript=os.path.relpath(tpath, REPO),
        law=("LOOP-PROCESS: an art round is judged by eyes on delivered frames. The plant must "
             "land worst-or-flagged; a passed plant voids the round and its findings are not "
             "read. The guards measure progress — rank in the deck — never rounds elapsed."),
    )
    if a.build_frame:
        out["self_test"] = True
        out["verdict"] = verdict
    hpath = os.path.join(HISTORY, "r%03d-%s.json" % (rnd, lane.replace("/", "_")))
    with open(hpath, "w") as f:
        json.dump(out, f, indent=1)
    # A SELF-TEST DOES NOT WRITE CRITIC-VERDICT.json. It is a verdict about the JUDGE, taken on a
    # deck whose build slot held a morgue frame — so putting it at the repo root would overwrite a
    # real round's verdict with one that describes a different picture. The gate refuses a
    # self_test verdict as well, and both are wanted: the gate's check is what stops a stale one
    # opening it, and this is what stops a real one being destroyed.
    if not a.build_frame:
        with open(VERDICT, "w") as f:
            json.dump(out, f, indent=1)

    print("\n*** %s ***" % verdict)
    if verdict == "VOID":
        print("The seat would ship, or did not flag, a frame Rafe personally culled:")
        print("   %s — \"%s\"" % (plant["file"], plant["verbatim"]))
        print("LOOP-PROCESS §4: the round is void and its findings are NOT READ.")
    elif verdict == "FAIL":
        for fx in flips:
            print("   flip: %s" % fx)
    if not a.build_frame:
        print("\nwritten: %s" % os.path.relpath(VERDICT, REPO))
        print("         %s" % os.path.relpath(hpath, REPO))
    else:
        print("\nwritten: %s" % os.path.relpath(hpath, REPO))
        print("         (a self-test writes no CRITIC-VERDICT.json — it judges the judge)")

    # ── A SEAT-LEVEL PLANT STOP IS RAISED HERE, AFTER THE RECORD IS ON DISK ────────────────
    #
    # The verdict and the history file are written above, deliberately: a STOP whose evidence was
    # never saved is a STOP nobody can rule on. Under the 2026-09-11 re-scope this does NOT wait
    # for a second round the way the legacy two-VOID guard does — a slot that missed a live plant
    # twice, with a fresh seat and a fresh plant, or two independent slots missing in one round,
    # is the judging layer rather than the draw, and a second round would add nothing.
    if seat_plant_stop:
        why = ("the seat-level plant term tripped: %s\nRuled 2026-09-11: a live-plant miss voids "
               "the SEAT and the slot is re-drawn once. This is the case that outruns the "
               "re-draw." % seat_plant_stop)
        p = write_stall("broken-judge", why, history(a.history), lane, cfg, a.stall_out)
        print("\n*** STOP — broken-judge ***\n%s\n\nwritten: %s"
              % (why, os.path.relpath(p, REPO)))
        print("LOOP-PROCESS §1.1.4 ruling trigger. Ending the turn for Rafe.")
        return 3

    # And check the guards again with this round folded in, so a STOP is written the moment it is
    # earned rather than one round later.
    name, why = guards(history(a.history), lane, a.park, a.gate_ruling)
    if name:
        p = write_stall(name, why, history(a.history), lane, cfg, a.stall_out)
        print("\n*** STOP — %s ***\n%s\n\nwritten: %s"
              % (name, why, os.path.relpath(p, REPO)))
        print("LOOP-PROCESS §1.1.4 ruling trigger. Ending the turn for Rafe.")
        return 3

    # PASS-INSTALL exits 0 with PASS: both open the install gate, and a caller that
    # distinguished them would be a second gate with its own opinion (SKILL.md §6).
    return {"PASS": 0, "PASS-INSTALL": 0, "INSTALL-LATEST": 0,
            "FAIL": 1, "VOID": 2}[verdict]


if __name__ == "__main__":
    raise SystemExit(main())
