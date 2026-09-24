#!/usr/bin/env python3
"""THE COLD-NAMING TEST — bible §12.2's instrument, and a miss is a FAIL on §12.

    python3 .claude/skills/frame-critic/cold_naming.py --frame <png> [--seats 3]
    python3 .claude/skills/frame-critic/cold_naming.py --prove        # §13.5

WHY IT EXISTS. Rafe walked the props build and failed it: *"small, unrecognizable except the
fire; colouring quite good."* That build had passed every instrument and a FIVE-SEAT PANEL, which
ranked it first of four. Not one of its six flip items said *I cannot tell what that is*.

**A seat asked to rank craft will rank craft.** Nobody had asked the naming question, so nobody
answered it. §12's first clause — *names itself at 1×* — had been carried at the human gate alone
(the audit's own instrument row says so: "Eye-side by design"), and the human gate is the one
place a round only reaches after everything else has passed.

So this asks the naming question FIRST, before any ranking question, and it asks it cold: the
seat is shown the delivered frame, told nothing about what is in it, and asked to name what it
sees. It is not shown the sprite on a white card, because §4.1 and §12's last clause require the
read to survive a busy screen — and the whole failure was that these objects do not.

── WHAT MAKES IT A FAIL RATHER THAN A NOTE ───────────────────────────────────────────────────
A style flip is advice. This is not: an object nobody can name has failed the section that says
identifiability is required *regardless of style conformance*. §12.2 states it outright — "a miss
is a FAIL on §12, not a style note."

── HOW IT IS SCORED ──────────────────────────────────────────────────────────────────────────
Each prop declares words that COUNT as naming it and words that REFUSE it. The refusals are the
identity cards' own `role_reject` values: the barricade family spent sixty-odd generations not
being a fence, so a seat calling it a fence is a miss even though it named an object. Naming is
judged on the seat's own unprompted prose, not on a question that supplies the answer.

⚠ NEVER ASK A LEADING QUESTION. "Is there a standing stone at (343,641)?" is not a naming test,
it is a recognition test with the answer in it, and it will pass art that cannot be named. The
prompt below names no object, no material, and no count.
"""
import argparse
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))

# ── THE SUBJECTS ──────────────────────────────────────────────────────────────────────────────
# `accept` is what naming this object looks like in a seat's own words. `refuse` is the identity
# card's role_reject: naming it as one of these is a MISS, not a partial credit, because the card
# rejects that object by name.
SUBJECTS = [
    dict(key="marker", card="B-PROP-001 the boundary marker stone",
         accept=["standing stone", "stone post", "boundary stone", "marker stone", "menhir",
                 "stone pillar", "stone marker", "monolith", "upright stone", "stone column",
                 "cairn", "post", "pillar", "obelisk", "stele"],
         refuse=["gravestone", "headstone", "tombstone", "statue", "signpost"]),
    # THE BAR IS THE WORD — RULED at the §3.2 props walk (Rafe, 2026-09-13): the family "reads
    # as wood but not as a barricade"; A read as "a fallen jumble of wood roped together", B as
    # "neatly stacked bricks". So "timber", "logs", "planks", "beams", "woodpile" no longer
    # count: a barricade that a seat can only call wood has not been named. Accept is the
    # object; refuse is what the walk and the card reject by name.
    dict(key="barricade", card="B-PROP-002 the barricade",
         accept=["barricade", "barrier", "blockade", "roadblock", "wooden barrier",
                 "cheval de frise", "chevaux de frise", "obstacle"],
         refuse=["fence", "fencing", "palisade", "railing", "gate", "ladder", "sticks",
                 "debris", "rubble", "bricks", "brick", "bench", "bed", "jumble", "pile"]),
    dict(key="fire", card="B-PROP-003 the orc fire",
         accept=["fire", "campfire", "firepit", "fire pit", "brazier", "hearth", "bonfire",
                 "fire ring", "cooking fire", "embers", "flame"],
         refuse=["well", "hole", "pit trap", "drain", "manhole"]),
]

PROMPT = """You are looking at one screenshot from a top-down video game. It is
`frame.png` in this directory. Read it with the Read tool.

Describe every DISTINCT MAN-MADE OR PLACED OBJECT you can see sitting on the floor of this
scene — things somebody put there, as opposed to the floor, the walls, or the character.

For each one, answer in this exact shape, one block per object:

OBJECT: <what it is, in a few plain words — the noun you would use telling someone what is there>
WHERE: <approximate x,y in pixels>
SURE: <one of CERTAIN / PROBABLY / GUESSING>

If a shape is clearly a deliberate object but you cannot tell what it is meant to be, say so in
the OBJECT line in those words — "an object I cannot identify" — and mark SURE: GUESSING. That is
a useful answer and you will not be marked down for it. Do not invent an identity to fill the
line.

Then finish with one line:

COUNT: <how many distinct placed objects you found>

Say nothing else."""


def run_seat(work, timeout=300):
    p = subprocess.run(["claude", "-p", PROMPT, "--allowedTools", "Read"],
                       cwd=work, capture_output=True, text=True,
                       timeout=timeout, stdin=subprocess.DEVNULL)
    return p.stdout + p.stderr


_OBJ = re.compile(r"^\s*\**OBJECT\**\s*:\**\s*(.+?)\s*$", re.MULTILINE | re.IGNORECASE)


def objects_named(text):
    return [m.group(1).strip().lower() for m in _OBJ.finditer(text)]


def score(named, subject):
    """Did any named object name THIS subject? Returns (verdict, the phrase, why)."""
    for phrase in named:
        for bad in subject["refuse"]:
            if bad in phrase:
                return "MISS", phrase, "named as '%s' — the card rejects that by name" % bad
    for phrase in named:
        for good in subject["accept"]:
            if good in phrase:
                return "NAMED", phrase, "'%s'" % good
    return "MISS", None, "no seat named it at all"


def judge(frame, seats, work_root, timeout=300):
    work = os.path.join(work_root, "cold-naming")
    if os.path.isdir(work):
        for f in os.listdir(work):
            os.remove(os.path.join(work, f))
    os.makedirs(work, exist_ok=True)
    import shutil
    shutil.copy(frame, os.path.join(work, "frame.png"))

    transcripts, all_named = [], []
    for i in range(seats):
        t = run_seat(work, timeout)
        transcripts.append(t)
        named = objects_named(t)
        all_named.append(named)
        print("  seat %d named %d object(s): %s"
              % (i + 1, len(named), "; ".join(named[:6]) or "(nothing parsed)"))

    results = []
    for sub in SUBJECTS:
        # A prop is NAMED if ANY seat named it. The bar is deliberately generous: this test
        # exists to catch objects nobody can name, not to demand unanimity about a noun.
        best = ("MISS", None, "no seat named it at all")
        for named in all_named:
            v, phrase, why = score(named, sub)
            if v == "NAMED":
                best = (v, phrase, why)
                break
            if phrase is not None:          # a refusal is more informative than silence
                best = (v, phrase, why)
        results.append(dict(key=sub["key"], card=sub["card"],
                            verdict=best[0], phrase=best[1], why=best[2]))
    return results, transcripts


def report(results):
    print("\n  %-11s %-8s %s" % ("prop", "verdict", "why"))
    for r in results:
        print("  %-11s %-8s %s" % (r["key"], r["verdict"], r["why"]))
    missed = [r["key"] for r in results if r["verdict"] == "MISS"]
    if missed:
        print("\n  *** FAIL on §12 — not a style note. Unnamed: %s ***" % ", ".join(missed))
    else:
        print("\n  PASS — every prop was named cold, in scene, unprompted.")
    return 1 if missed else 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--frame")
    ap.add_argument("--seats", type=int, default=3)
    ap.add_argument("--work", default=os.path.expanduser("~/.claude/frame-critic"))
    ap.add_argument("--out", help="write the verdict as JSON here")
    ap.add_argument("--prove", action="store_true",
                    help="§13.5 — show the test can fail, against the frame Rafe failed")
    a = ap.parse_args()

    if a.prove:
        return prove(a)

    if not a.frame:
        print("--frame is required (or --prove)", file=sys.stderr)
        return 2
    print("COLD NAMING — bible §12.2. %s, %d seat(s)\n" % (a.frame, a.seats))
    results, _ = judge(a.frame, a.seats, a.work)
    rc = report(results)
    if a.out:
        json.dump(dict(frame=a.frame, seats=a.seats, results=results),
                  open(a.out, "w"), indent=2)
    return rc


def prove(a):
    """§13.5 — no instrument's pass counts until it has demonstrated it can fail.

    THE CALIBRATION FRAME IS THE ONE THE HUMAN GATE REJECTED, and its verdict is unusually
    specific: *"small, unrecognizable except the fire."* So this does not merely require a FAIL —
    it requires the RIGHT fail. The test must miss the marker and the barricade and NAME THE
    FIRE, because that is what Rafe saw. An instrument that fails all three is not agreeing with
    him, it is broken in a way that happens to look like agreement.
    """
    frame = os.path.join(REPO, "tools/tier1_floors/evidence/props_pre_1212.png")
    if not os.path.exists(frame):
        frame = os.path.join(REPO, "tools/tier1_floors/evidence/combined.png")
    print("PROVE — cold naming, against the frame the human gate FAILED")
    print("  frame:   %s" % frame)
    print("  ruling:  \"small, unrecognizable except the fire; colouring quite good.\"")
    print("  so the test must MISS marker and barricade and NAME fire.\n")
    results, _ = judge(frame, a.seats, a.work)
    report(results)

    want = dict(marker="MISS", barricade="MISS", fire="NAMED")
    bad = 0
    print("\n  %-11s %-8s %-8s %s" % ("prop", "got", "wanted", ""))
    for r in results:
        w = want[r["key"]]
        ok = (r["verdict"] == w)
        bad += 0 if ok else 1
        print("  %-11s %-8s %-8s %s" % (r["key"], r["verdict"], w,
                                        "ok" if ok else "*** DISAGREES WITH THE GATE ***"))
    if bad:
        print("\n  %d of 3 disagree with Rafe's walk. The instrument is NOT calibrated and its\n"
              "  pass does not count (§13.5)." % bad)
        return 1
    print("\n  CALIBRATED — it fails the two he could not name and names the one he could.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
