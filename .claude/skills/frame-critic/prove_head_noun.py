#!/usr/bin/env python3
"""THE HEAD NOUN DECIDES — proved on every saved naming run before it is used (§13.5).

RULED (Rafe, R1b-r005, 2026-10-13): the bar is what the seat says the object IS (its head noun),
not what it says the object looks like. This drives the real `cold_naming.head_verdict` over the
seats' own words, saved on disk, and requires:

  - Rafe's five r005 calls, seat by seat (seats 1, 2, 3, 5 named; seat 4 a miss) -> PASS 4/5;
  - the X-frame's two controls still FAIL — the walked frame Rafe culled as "crossed planks",
    and c2 on the blocking subject ("five head nouns of sawhorse/trestle/planks");
  - the Λ hidden at the corridor mouth (r004) still FAILS, and c3 on the blocking subject still
    PASSES — the rule must be able to say both;
  - the ruling's own examples, phrase by phrase.

No seat is run. Exit 0 only if every case behaves as declared.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
REPO = os.path.dirname(REPO) if not os.path.isdir(os.path.join(REPO, "tools")) else REPO
sys.path.insert(0, HERE)
import cold_naming as cn

EV = os.path.join(REPO, "tools/cast_shadows/evidence")
SUBJ = {s["key"]: s for s in cn.SUBJECTS}

# (file, subject, expected verdict, expected per-seat NAMED pattern or None)
RUNS = [
    ("lambda_opening/cold_name_depth_fixed.json", "barricade", "PASS", [1, 1, 1, 0, 1]),
    ("xframe/judge_control_walked.json", "barricade", "FAIL", None),
    ("xframe/judge_control_c2_blocking.json", "barricade_blocking", "FAIL", None),
    ("xframe/judge_c3_blocking.json", "barricade_blocking", "PASS", None),
    ("lambda_opening/cold_name_r004.json", "barricade", "FAIL", None),
    ("lambda_opening/cold_name_baseline_old_position.json", "barricade", "FAIL", None),
]
PHRASES = [
    ("an x-shaped barricade (sawhorse-style)", "barricade", "NAMED"),
    ("a sawhorse or crossed-plank barricade", "barricade", "NAMED"),
    ("two crossed wooden beams leaning against the wall, like an a-frame brace or sawhorse",
     "barricade", "MISS"),
    ("a wooden sawhorse", "barricade", "REFUSED"),
    ("wooden x-shaped trestle", "barricade", "REFUSED"),
    ("crossed wooden planks", "barricade", "REFUSED"),
    ("a wooden a-frame", "barricade", "REFUSED"),
    ("row of wooden x-shaped spike barricades", "barricade_blocking", "NAMED"),
    ("a pile of sticks", "barricade", "REFUSED"),
]

bad = 0
print("== the seats' own words, re-scored by head noun")
for f, subj, want, pattern in RUNS:
    d = json.load(open(os.path.join(EV, f)))
    got = [1 if cn.head_verdict(s.get("phrase") or "", SUBJ[subj])[0] == "NAMED" else 0
           for s in d["per_seat"]]
    n = sum(got)
    verdict = "PASS" if n * 5 >= len(got) * 4 else "FAIL"
    ok = verdict == want and (pattern is None or got == pattern)
    bad += not ok
    print("  %-4s %-48s %-18s %d/%d %s (was %s %s)%s" % (
        "ok" if ok else "BAD", f, subj, n, len(got), verdict, d.get("verdict"), d.get("named"),
        "" if pattern is None else "  seats %s" % got))
print("== the ruling's examples")
for p, subj, want in PHRASES:
    v = cn.head_verdict(p, SUBJ[subj])[0]
    ok = v == want
    bad += not ok
    print("  %-4s %-8s %s" % ("ok" if ok else "BAD", v, p))
print()
if bad:
    print("%d CASE(S) DID NOT BEHAVE AS DECLARED." % bad)
    sys.exit(1)
print("EVERY CASE BEHAVED AS DECLARED.  (%d cases)" % (len(RUNS) + len(PHRASES)))
