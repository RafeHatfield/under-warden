#!/usr/bin/env python3
"""COLD NAMING, ONE OBJECT, STRONG MAJORITY — #207 round 3's judge (Rafe, 2026-09-13).

    python3 .claude/skills/frame-critic/cold_name_object.py --frame <png> --subject barricade \\
        --box x0,y0,x1,y1 [--seats 5] [--out verdict.json]

cold_naming.py asks the naming question of the whole frame and calls a prop NAMED if ANY seat
names it — generous by design, because it exists to catch objects nobody can name. That bar
cannot judge a round whose question is "does THIS barricade, the X-frame at (3,15), read as a
barricade", because the scene holds two barricades and a seat naming the Λ-frame satisfies the
family. The queue's PASS x4 was exactly that: the family passed on B while Rafe's eye read A as
"crossed planks".

So this scores ONE object, located by the seat's own WHERE line falling inside a screen box, and
asks for a STRONG MAJORITY of seats (>= 4 of 5, the panel's own ratio) to name it with an accept
word and no refuse word. Same prompt as cold_naming.py — no object, material or count is named
to the seat — same accept/refuse lists (the identity card's role_reject), same blind seats.

§13.5: --prove runs it on the walked frame, where Rafe's cull says the X-frame is not named, and
requires a FAIL there before any PASS counts.
"""
import argparse, json, os, re, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import cold_naming as cn

_BLOCK = re.compile(r"OBJECT\**\s*:\**\s*(?P<obj>.+?)\s*\n\s*\**WHERE\**\s*:\**\s*(?P<where>.+?)\s*\n",
                    re.IGNORECASE)
_NUM = re.compile(r"-?\d+(?:\.\d+)?")


def objects_with_where(text):
    out = []
    for m in _BLOCK.finditer(text):
        nums = [float(n) for n in _NUM.findall(m.group("where"))]
        if len(nums) >= 2:
            out.append((m.group("obj").strip().lower(), nums[0], nums[1]))
    return out


def seat_names(text, subject, box):
    """(verdict, phrase): NAMED if an object located in the box carries an accept word and no
    refuse word; REFUSED if one carries a refuse word; MISS if nothing of the seat's lands there."""
    x0, y0, x1, y1 = box
    inside = [o for o, x, y in objects_with_where(text) if x0 <= x <= x1 and y0 <= y <= y1]
    if not inside:
        return "MISS", None
    for phrase in inside:
        if any(bad in phrase for bad in subject["refuse"]):
            return "REFUSED", phrase
    for phrase in inside:
        if any(good in phrase for good in subject["accept"]):
            return "NAMED", phrase
    return "MISS", inside[0]


def judge(frame, subject_key, box, seats, work_root, timeout=300):
    subject = next(s for s in cn.SUBJECTS if s["key"] == subject_key)
    work = os.path.join(work_root, "cold-name-object")
    os.makedirs(work, exist_ok=True)
    for f in os.listdir(work):
        os.remove(os.path.join(work, f))
    import shutil
    shutil.copy(frame, os.path.join(work, "frame.png"))
    per_seat = []
    for i in range(seats):
        t = cn.run_seat(work, timeout)
        v, phrase = seat_names(t, subject, box)
        per_seat.append(dict(seat=i + 1, verdict=v, phrase=phrase, transcript=t))
        print("  seat %d: %-8s %s" % (i + 1, v, phrase or "(nothing located in the box)"))
    named = sum(1 for s in per_seat if s["verdict"] == "NAMED")
    strong = named * 5 >= seats * 4
    return dict(subject=subject_key, box=box, seats=seats, named=named,
                verdict="PASS" if strong else "FAIL",
                rule=">= 4 of 5 seats name it with an accept word and no refuse word",
                per_seat=[{k: v for k, v in s.items() if k != "transcript"} for s in per_seat],
                transcripts=[s["transcript"] for s in per_seat])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--frame", required=True)
    ap.add_argument("--subject", default="barricade")
    ap.add_argument("--box", required=True, help="x0,y0,x1,y1 in frame px")
    ap.add_argument("--seats", type=int, default=5)
    ap.add_argument("--work", default=os.path.expanduser("~/.claude/frame-critic"))
    ap.add_argument("--out")
    ap.add_argument("--expect", choices=["PASS", "FAIL"],
                    help="§13.5 — assert the outcome (a control run)")
    a = ap.parse_args()
    box = tuple(float(v) for v in a.box.split(","))
    r = judge(a.frame, a.subject, box, a.seats, a.work)
    print("\n  %s — %d of %d seats named the %s in the box" % (r["verdict"], r["named"], r["seats"], a.subject))
    if a.out:
        json.dump(r, open(a.out, "w"), indent=1, ensure_ascii=False)
    if a.expect and a.expect != r["verdict"]:
        print("  *** CONTROL FAILED: expected %s ***" % a.expect)
        sys.exit(2)
    sys.exit(0 if r["verdict"] == "PASS" else 1)


if __name__ == "__main__":
    main()
