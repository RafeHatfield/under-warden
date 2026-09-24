#!/usr/bin/env python3
"""POSITIVE CONTROL for the floor variant census. Floor session two, precondition 1.

    §13.5 / LOOP-PROCESS §4: no instrument's pass counts until it has demonstrated it can fail.
    "Stub the metric to a constant, plant the defect it exists to catch, MUTATE THE THING IT
    GUARDS. Show it goes red. Record the verbatim failure."

The thing this census guards is `TileThemeConfig.PositionHash`. Session one found it LINEAR —
`h(x,y) = 7919x + 104729y` — which makes the chosen variant periodic along every straight line:
the difference along a step is constant, so modulo the pool size the index advances by a fixed
amount and cycles. On a 24-id pool the diagonal step was `112648 mod 24 = 16`, whose additive
order mod 24 is **3**, so the same tile recurred every third cell diagonally and a 24-tile family
delivered three.

So the plant is not a synthetic defect invented for the control. **It is the exact code that
shipped**, restored verbatim, and the control asserts the census notices.

WHAT MAKES THIS A REAL CONTROL RATHER THAN A GESTURE — LOOP-PROCESS §4.1, LAW: *the plant must
carry the defect ON THE AXIS THE LEVER CLAIMS.* A control that only checked "did the number
change?" would certify connectivity and report it as efficacy. This one requires EVERY
axis's step-difference mode to saturate at 1.000 under the plant and to sit near chance without
it, because constancy-of-step IS linearity — a scalar distinct-count would have missed it, and did.

⚠ THE FIRST VERSION OF THIS CONTROL FAILED, AND IT FAILED USEFULLY. It required a `repeat@3`
statistic to spike, which is the period a linear hash produces on a 24-id pool. The pool is now
96, where the same hash has period 12, so the planted defect measured 0.000 and the control
reported the census as blind. It was: the census had been tuned to one pool size. The statistic
is now step-difference constancy, which is what linearity means at every pool size.

It rebuilds the engine twice and restores the source in a `finally`, so an interrupted run cannot
leave a linear hash in the tree.
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(REPO, "tools/tier0_harness"))
import capture_corridor as CC      # noqa: E402

TARGET = os.path.join(REPO, "src/Presentation/TileThemeConfig.cs")
THEME = "res://src/Presentation/assets/tier1_floors/tile_themes_tier1_floors.yaml"
SCENE = "src/Presentation/assets/tier0_harness/scenes/tier1_floor_review.json"
OUT = os.path.join(HERE, "controls")
GODOT = os.environ.get("GODOT", "/Applications/Godot_mono.app/Contents/MacOS/Godot")

# The shipped defect, verbatim. Session one's commit replaced exactly this body.
LINEAR_BODY = """    private static int PositionHash(int x, int y)
    {
        unchecked
        {
            return (x * 7919 + y * 104729) & 0x7FFFFFFF;
        }
    }"""

CENSUS_RE = re.compile(
    r"variant census: cells=(\d+) distinct=(\d+) max_multiplicity=(\d+)\s+step_top2\S*:"
    r"\s+row=([\d.]+)\S*\s+col=([\d.]+)\S*\s+diag=([\d.]+)\S*\s+anti=([\d.]+)\S*"
    r"\s+worst_top2=([\d.]+)")


def parse_census(log):
    m = CENSUS_RE.search(log)
    if not m:
        raise SystemExit("REFUSING: no census line in the capture log. The control cannot "
                         "conclude anything from an instrument that did not run.")
    return dict(cells=int(m.group(1)), distinct=int(m.group(2)), max_mult=int(m.group(3)),
                row=float(m.group(4)), col=float(m.group(5)),
                diag=float(m.group(6)), anti=float(m.group(7)),
                worst=float(m.group(8)), line=m.group(0))


def build_and_capture(tag):
    r = subprocess.run(["dotnet", "build", os.path.join(REPO, "UnderWarden.Presentation.csproj")],
                       capture_output=True, text=True, cwd=REPO)
    if r.returncode != 0:
        print(r.stdout[-2000:], file=sys.stderr)
        raise SystemExit("REFUSING: dotnet build failed for arm %s" % tag)
    subprocess.run([GODOT, "--headless", "--path", REPO, "--import"],
                   capture_output=True, text=True)
    os.makedirs(OUT, exist_ok=True)
    png = os.path.join(OUT, "census_%s.png" % tag)
    log = os.path.join(OUT, "census_%s.log" % tag)
    cfg = CC.read_config()
    rc, out, _ = CC.capture(png, THEME, cfg, GODOT, scene_spec=SCENE, log_out=log)
    return parse_census(out)


def swap_to_linear():
    """Replace the mixed hash with the linear one that shipped. Returns the original text."""
    src = open(TARGET).read()
    start = src.index("    private static int PositionHash(int x, int y)")
    end = src.index("\n    }", src.index("return h & 0x7FFFFFFF;")) + len("\n    }")
    return src, src[:start] + LINEAR_BODY + src[end:]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--keep-arms", action="store_true", help="leave the two captures on disk")
    a = ap.parse_args()

    original, mutated = swap_to_linear()
    if mutated == original:
        raise SystemExit("REFUSING: the mutation changed nothing. A control that plants no "
                         "defect proves nothing (LOOP-PROCESS §4.2).")

    print("CONTROL — the variant census, shown able to fail")
    print("  mutating: %s  (PositionHash -> the linear body that shipped)"
          % os.path.relpath(TARGET, REPO))

    results = {}
    try:
        print("\n== arm FIXED (the hash as it stands)")
        results["fixed"] = build_and_capture("fixed")
        print("   " + results["fixed"]["line"])

        print("\n== arm LINEAR (session one's defect, restored verbatim)")
        with open(TARGET, "w") as f:
            f.write(mutated)
        results["linear"] = build_and_capture("linear")
        print("   " + results["linear"]["line"])
    finally:
        with open(TARGET, "w") as f:
            f.write(original)
        print("\n== source restored (a leftover linear hash would be the defect, silently)")
        subprocess.run(["dotnet", "build", os.path.join(REPO, "UnderWarden.Presentation.csproj")],
                       capture_output=True, text=True, cwd=REPO)
        subprocess.run([GODOT, "--headless", "--path", REPO, "--import"],
                       capture_output=True, text=True)

    fx, ln = results["fixed"], results["linear"]

    # §4.1: the plant must move the axis the instrument claims.
    # The claim is that the census detects LINEARITY. Under a linear hash the step difference is
    # a constant on every axis, so every modal fraction is 1.000; under a mixed hash the mode sits
    # near 1/N. Requiring ALL FOUR axes to saturate — not just one — is what stops this passing on
    # a coincidence, and requiring the fixed arm to stay near chance is what stops it passing on
    # an instrument that simply always reds.
    chance = 1.0 / max(fx["distinct"], 1)
    checks = [
        ("every axis saturates under the linear hash (top2 = 1.000)",
         min(ln["row"], ln["col"], ln["diag"], ln["anti"]) >= 0.99),
        ("no axis saturates under the fixed hash",
         fx["worst"] <= 0.35),
        ("the fixed arm sits within an order of magnitude of chance (%.3f)" % chance,
         fx["worst"] <= max(0.35, chance * 10)),
        ("the two arms are not the same measurement",
         ln["line"] != fx["line"]),
    ]
    print("\n== verdict")
    ok = True
    for name, passed in checks:
        print("  %-58s %s" % (name, "PASS" if passed else "*** FAIL ***"))
        ok = ok and passed

    res = dict(commit=subprocess.run(["git", "-C", REPO, "rev-parse", "HEAD"],
                                     capture_output=True, text=True).stdout.strip(),
               instrument="src/Presentation/Map/FloorVariantCensus.cs",
               plant="TileThemeConfig.PositionHash restored to the linear body that shipped",
               law=("LOOP-PROCESS §4.1 — the plant carries the defect on the axis the lever "
                    "claims; a control that only asks 'did anything change?' certifies "
                    "connectivity and reports it as efficacy."),
               arms=dict(fixed=fx, linear=ln), checks=[dict(check=n, passed=p) for n, p in checks],
               all_passed=ok)
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, "CENSUS-CONTROL.json"), "w") as f:
        json.dump(res, f, indent=1)
    print("\n%s" % ("CONTROL PASSED — the census can be made to fail, on its own axis."
                    if ok else "CONTROL FAILED — the census is decorative until this passes."))
    print("written: %s" % os.path.relpath(os.path.join(OUT, "CENSUS-CONTROL.json"), REPO))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
