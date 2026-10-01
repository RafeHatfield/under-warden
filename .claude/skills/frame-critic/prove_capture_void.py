#!/usr/bin/env python3
"""PROVE THE CAPTURE-FAILURE VOID — Ruling 47 (Rafe, 2026-09-30).

    "Prove both — feed a blank frame and a builder throw, show each voids. File the blank-frame
     round as evidence of the hole, labelled as it is."

Drives the REAL functions (frame_critic.blank_frame, seat_reports_blank, capture, void_capture,
guards; the engine's own capture path through capture_combined.sh). Nothing here reimplements what
it tests. Every positive is paired with a negative, because a check that cannot say no is decorative
(LOOP-PROCESS §4).

    python3 .claude/skills/frame-critic/prove_capture_void.py > /tmp/out.txt 2>&1

⚠ Writes nothing into the tree that it does not remove: the void records go to a temp directory,
and the builder-throw capture's log is deleted after it is read. The engine case runs Godot
HEADLESS (no window, no focus, muted) — the throw happens before anything renders.
"""
import glob
import json
import os
import shutil
import subprocess
import sys
import tempfile
import types

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import frame_critic as fc                                           # noqa: E402

REPO = fc.REPO
CFG = json.load(open(os.path.join(REPO, "docs/FRAME-CRITIC.json")))
BLANK = os.path.join(REPO, "tools/cast_shadows/evidence/lambda_opening/blank_capture.png")
REAL = os.path.join(REPO, CFG["approved_capture"]["path"])
FIX = os.path.join(HERE, "evidence/fixtures")
results = []


def case(name, ok, detail=""):
    results.append((name, ok))
    print("  %s  %s%s" % ("PASS" if ok else "FAIL", name, ("\n        " + detail) if detail else ""))


print("=" * 78)
print("CAPTURE-FAILURE VOID — the proof")
print("=" * 78)

# ── 1. a blank frame is caught on its pixels, before any seat ────────────────────────────────
print("\n== 1. the pixel check")
b, why = fc.blank_frame(BLANK, CFG.get("crop"))
case("the filed blank frame (lane art/lambda-opening-212 r001) is BLANK", b, why)
b, why = fc.blank_frame(REAL, CFG.get("crop"))
case("Rafe's shadowed reference is NOT blank (the check can say no)", not b, why)

# ── 2. the round, end to end, on the blank frame: VOID, zero seats ───────────────────────────
print("\n== 2. the round on a blank frame — VOID (capture failure), no seat spent")
tmp = tempfile.mkdtemp(prefix="prove-capture-void-")
fc.HISTORY, fc.VERDICT = os.path.join(tmp, "history"), os.path.join(tmp, "CRITIC-VERDICT.json")
spent = []
fc.run_seat = lambda *a, **k: spent.append(1) or ""                  # a seat must never run
argv = sys.argv
sys.argv = ["frame_critic.py", "--lane", "proof-capture-void", "--seats", "5",
            "--build-frame", os.path.relpath(BLANK, REPO), "--history", os.path.join(tmp, "history")]
try:
    rc = fc.main()
finally:
    sys.argv = argv
recs = sorted(glob.glob(os.path.join(tmp, "history", "*.json")))
rec = json.load(open(recs[-1])) if recs else {}
case("exit code is VOID's (2)", rc == 2, "rc=%r" % rc)
case("the record says VOID, void_reason capture-failure",
     rec.get("verdict") == "VOID" and rec.get("void_reason") == fc.CAPTURE_FAILURE,
     json.dumps({k: rec.get(k) for k in ("verdict", "void_reason", "capture_failure")}))
case("no seat was spent", not spent, "seats run: %d" % len(spent))

# ── 3. a seat saying 'no picture' about the BUILD voids — and only about the build ───────────
print("\n== 3. the seat check, on the filed round's own five transcripts")
built = {1: 4, 2: 1, 3: 4, 4: 2, 5: 2}     # each seat's build slot, from blank_round_decks.log
hist = os.path.join(REPO, ".claude/skills/frame-critic/history")
for s, slot in built.items():
    t = open(os.path.join(hist, "r001-art_lambda-opening-212-transcript-seat%d.txt" % s)).read()
    case("seat %d (build in slot %d) reports the build blank" % (s, slot),
         fc.seat_reports_blank(t, slot))
    others = [x for x in (1, 2, 3, 4) if x != slot]
    case("seat %d does NOT report the other slots %s blank" % (s, others),
         not any(fc.seat_reports_blank(t, x) for x in others))
for s in range(1, 6):
    t = open(os.path.join(hist, "r001-art_xframe-function-207-transcript-seat%d.txt" % s)).read()
    case("a real round's seat %d reports NO slot blank" % s,
         not any(fc.seat_reports_blank(t, x) for x in (1, 2, 3, 4)))

# ── 4. a builder throw fails the capture hard: no PNG, the marker, a failure exit ────────────
print("\n== 4. the engine: a scene that seals a corridor (headless — no window, no focus)")
out_png = os.path.join(REPO, "tools/tier1_floors/evidence/proof_sealed.png")
out_log = os.path.join(REPO, "tools/tier1_floors/evidence/proof_sealed.log")
for p in (out_png, out_log):
    if os.path.exists(p):
        os.remove(p)
env = dict(os.environ, GODOT=os.path.join(FIX, "godot_headless.sh"), PYTHONDONTWRITEBYTECODE="1")
cmd = ["tools/tier1_floors/capture_combined.sh", "proof_sealed", "0",
       "--scene-spec", os.path.join(FIX, "sealed_corridor.json"),
       "--occluders", "all", "--shadow-softness", "12.0", "--shadow-darkness", "0.8", "--fire-flicker", "1"]
r = subprocess.run(cmd, cwd=REPO, env=env, capture_output=True, text=True, timeout=600)
log = (open(out_log).read() if os.path.exists(out_log) else "") + r.stdout + r.stderr
marker = [l.strip() for l in log.splitlines() if fc.CAPTURE_FAILED_MARKER in l]
# The marker comes first and gates the rest: a non-zero exit WITHOUT it means the engine never ran
# (the first run of this proof exited 3 on a memory-headroom refusal and would have "passed").
case("the engine ran and printed the CAPTURE FAILED marker", bool(marker),
     (marker or ["(none) — rc=%d; did Godot run at all?" % r.returncode])[0][:220])
case("…and the capture exits non-zero", bool(marker) and r.returncode != 0, "rc=%d" % r.returncode)
case("no PNG was written", not os.path.exists(out_png))

# ── 5. …and the critic reads that as a capture failure, not a refusal ─────────────────────────
# Case 4 proved the ENGINE's side on the real thing. This case proves the CRITIC's side: that
# capture() turns each engine outcome into a CaptureFailure. It drives capture() with stub commands
# that reproduce those outcomes exactly — a second real Godot run only re-collides with the memory
# guard (the first run of this proof took free memory from 41% to 34%), and the guard is not what is
# under test here. So, INSIDE THIS CASE ONLY, _headroom is stubbed to "ok"; it is restored after.
print("\n== 5. frame_critic.capture reads each engine outcome as a CaptureFailure (stubbed commands)")
for p in (out_png, out_log):
    if os.path.exists(p):
        os.remove(p)
stub_frame = os.path.join(tmp, "stub_frame.png")
shutil.copy(REAL, stub_frame)
old_head = fc._headroom
fc._headroom = lambda kind="seat": (True, "stubbed for case 5")
marker_line = fc.CAPTURE_FAILED_MARKER + " — the scene did not build: (stub)"
stubs = [
    ("the marker and a non-zero exit", [sys.executable, "-c", "print(%r); raise SystemExit(1)" % marker_line]),
    ("the marker with the exit code LOST (exit 0)", [sys.executable, "-c", "print(%r)" % marker_line]),
    ("exit 0, no marker, the frame NOT rewritten", [sys.executable, "-c", "pass"]),
]
try:
    for label, scmd in stubs:
        scfg = dict(CFG, capture=dict(cmd=scmd, frame=stub_frame, log=None, env={}))
        try:
            fc.capture(scfg, echo=False)
            case("capture() raises CaptureFailure on %s" % label, False, "it returned a frame")
        except fc.CaptureFailure as e:
            case("capture() raises CaptureFailure on %s" % label, True, str(e)[:200])
        except SystemExit as e:
            case("capture() raises CaptureFailure on %s" % label, False,
                 "SystemExit %s — a refusal, not a recorded failure" % e.code)
    # and the negative: a command that really rewrites the frame is NOT a failure
    ok_cmd = [sys.executable, "-c", "import shutil; shutil.copy(%r, %r)" % (REAL, stub_frame)]
    import time
    time.sleep(1.1)                                                 # mtime must move
    try:
        fc.capture(dict(CFG, capture=dict(cmd=ok_cmd, frame=stub_frame, log=None, env={})), echo=False)
        case("a command that rewrites a real frame is NOT a CaptureFailure (it can say no)", True)
    except (fc.CaptureFailure, SystemExit) as e:
        case("a command that rewrites a real frame is NOT a CaptureFailure (it can say no)", False, str(e)[:200])
finally:
    fc._headroom = old_head

# ── 6. the guards: a capture-failure VOID is not a missed plant ──────────────────────────────
print("\n== 6. broken-judge ignores capture-failure VOIDs, and still fires on real ones")
def v(rnd, reason=None):
    d = dict(lane="proof-guard", round=rnd, verdict="VOID", schema=1)
    if reason:
        d["void_reason"] = reason
    return d
name, _ = fc.guards([v(1, fc.CAPTURE_FAILURE), v(2, fc.CAPTURE_FAILURE)], "proof-guard")
case("two capture-failure VOIDs: broken-judge does NOT fire", name != "broken-judge", "guard=%r" % name)
name, _ = fc.guards([v(1), v(2)], "proof-guard")
case("two plant-miss VOIDs: broken-judge DOES fire (the guard still bites)", name == "broken-judge",
     "guard=%r" % name)

shutil.rmtree(tmp, ignore_errors=True)
bad = [n for n, ok in results if not ok]
print("\n" + "=" * 78)
print("ALL %d CASES HOLD." % len(results) if not bad else "%d OF %d CASES FAILED: %s"
      % (len(bad), len(results), "; ".join(bad)))
sys.exit(1 if bad else 0)
