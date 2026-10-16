#!/usr/bin/env python3
"""THE ONE-BUILD EXCEPTION MUST BE ABLE TO REFUSE — proved before it is used (LOOP-PROCESS §4).

Drives gate_precheck.main() over a copy of docs/GATE-CONDITIONS.json with the lane-window entry's
exception rewritten case by case, on the tree and frame as they stand. Nothing is written to the
repo. Requires that the precheck:

  - REFUSES with no exception (the condition binds);
  - REFUSES when the exception names another build, another frame, or carries no ruling;
  - PASSES only when build id AND frame both match and Rafe's words are recorded.

Exit 0 only if every case behaves as declared.
"""
import contextlib
import copy
import hashlib
import io
import json
import os
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(REPO, ".claude", "skills", "frame-critic"))
import gate_precheck as GP
import build_id as BID

base = json.load(open(GP.COND))
lane = next(f for f in base["ruled_fixes"] if f["id"] == "lane-window-held")
ok_now, _ = GP.check_ruled_fix(lane["check"])
if ok_now:
    print("lane-window-held PASSES on this frame — nothing for an exception to waive; no proof possible.")
    sys.exit(1)

BID_NOW = BID.build_id()[0]
FRAME = hashlib.sha256(open(os.path.join(GP.EVID, "combined.png"), "rb").read()).hexdigest()
GOOD = {"ruled": "test words", "owner": "#184", "build_id": BID_NOW, "frame_sha256": FRAME}

CASES = [
    ("no exception", None, 1),
    ("another build", dict(GOOD, build_id="0" * 64), 1),
    ("another frame", dict(GOOD, frame_sha256="0" * 64), 1),
    ("no ruling recorded", dict(GOOD, ruled=""), 1),
    ("this build, this frame, ruled", GOOD, 0),
]

bad = 0
for name, ex, want in CASES:
    c = copy.deepcopy(base)
    entry = next(f for f in c["ruled_fixes"] if f["id"] == "lane-window-held")
    entry.pop("exception", None)
    if ex is not None:
        entry["exception"] = ex
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as fh:
        json.dump(c, fh)
    GP.COND = fh.name
    with contextlib.redirect_stdout(io.StringIO()):
        rc = GP.main()
    os.unlink(fh.name)
    ok = rc == want
    bad += not ok
    print("  %-4s %-32s rc=%d (want %d)" % ("ok" if ok else "BAD", name, rc, want))

print()
if bad:
    print("%d CASE(S) DID NOT BEHAVE AS DECLARED." % bad)
    sys.exit(1)
print("EVERY CASE BEHAVED AS DECLARED.  (%d cases)" % len(CASES))
