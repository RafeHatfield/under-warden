"""Per visible east face: geometry vs cap/foot, and light — wedge vs its own front face vs the floor it hangs over.

Masks come from the code's geometry (screen px, 2x native). The wedge's interior excludes the 1-col seam
and a 1-px margin. Luminance is Rec.601 on the delivered frame.
"""
import json, sys
import numpy as np
from PIL import Image

png, faces = sys.argv[1], json.load(open(sys.argv[2]))
im = np.asarray(Image.open(png).convert("RGB")).astype(float)
L = im @ np.array([0.299, 0.587, 0.114])
H, W = L.shape
FIRE = None  # brazier screen centre, read from the frame (brightest blob) below
ys, xs = np.mgrid[0:H, 0:W]


def stats(mask):
    v = L[mask]
    rgb = im[mask]
    if v.size == 0:
        return None
    m = rgb.mean(0)
    return dict(n=int(v.size), mean=round(float(v.mean()), 1), p10=round(float(np.percentile(v, 10)), 1),
                p90=round(float(np.percentile(v, 90)), 1), std=round(float(v.std()), 1),
                rgb=[round(float(c), 1) for c in m],
                rb=round(float(m[0] / max(m[2], 1)), 2))


rows = []
for f in faces:
    sx, sy = f["sx"], f["sy"]
    if sx + 96 > W or sx < 0:
        continue
    u = xs - (sx + 64)                       # screen columns into the wedge, 0..32
    top = sy + 32 - u                        # wedge top edge  (native: 16 - u)
    bot = sy + 64 - u                        # wedge bottom edge (native: 32 - u)
    wedge = (u >= 4) & (u < 31) & (ys >= top + 2) & (ys < bot - 2)
    face = (xs >= sx + 4) & (xs < sx + 60) & (ys >= sy + 34) & (ys < sy + 62)     # its own front face
    # the right half of the face — the columns the wedge samples
    face_r = (xs >= sx + 34) & (xs < sx + 62) & (ys >= sy + 34) & (ys < sy + 62)
    gap = (u >= 4) & (u < 28) & (ys >= sy + 2) & (ys < top - 2)            # the missing top triangle
    under = (u >= 4) & (u < 28) & (ys >= bot + 2) & (ys < sy + 64)          # floor below the rising foot
    floor_e = (xs >= sx + 100) & (xs < sx + 124) & (ys >= sy + 36) & (ys < sy + 60)  # floor further east
    cap = (xs >= sx + 4) & (xs < sx + 60) & (ys >= sy + 2) & (ys < sy + 30)
    r = dict(cell=(f["x"], f["y"]), wedge=stats(wedge), face=stats(face), face_right=stats(face_r),
             gap=stats(gap), under_foot=stats(under), floor_east=stats(floor_e), cap=stats(cap))
    fr = r["face_right"]["mean"]
    r["wedge_over_face_right"] = round(r["wedge"]["mean"] / max(fr, 1e-6), 3)
    r["wedge_over_floor_east"] = round(r["wedge"]["mean"] / max(r["floor_east"]["mean"], 1e-6), 3)
    rows.append(r)

print("%-8s %7s %7s %7s %7s %7s %7s  %6s %6s  %5s %5s" % (
    "cell", "wedge", "faceR", "cap", "gap", "under", "floorE", "w/fR", "w/flE", "wstd", "fstd"))
for r in rows:
    print("%-8s %7.1f %7.1f %7.1f %7.1f %7.1f %7.1f  %6.3f %6.3f  %5.1f %5.1f" % (
        str(r["cell"]), r["wedge"]["mean"], r["face_right"]["mean"], r["cap"]["mean"], r["gap"]["mean"],
        r["under_foot"]["mean"], r["floor_east"]["mean"], r["wedge_over_face_right"],
        r["wedge_over_floor_east"], r["wedge"]["std"], r["face_right"]["std"]))
    print("         hue r/b: wedge %.2f  faceR %.2f  cap %.2f   rgb wedge %s faceR %s" % (
        r["wedge"]["rb"], r["face_right"]["rb"], r["cap"]["rb"], r["wedge"]["rgb"], r["face_right"]["rgb"]))
json.dump(rows, open(sys.argv[3], "w"), indent=1)
