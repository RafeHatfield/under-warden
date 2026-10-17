"""List the review scene's east-face cells exactly as Tier1BoundaryWall decides them, with screen rects."""
import json, sys
from PIL import Image

REPO = "/Users/rafehatfield/development/ff/under-warden-wt/r2"
d = json.load(open(REPO + "/src/Presentation/assets/tier0_harness/scenes/tier1_props_review.json"))
W, H = d["width"], d["height"]
floor = [[False] * W for _ in range(H)]
for c in d["carve"]:
    for y in range(c["y0"], c["y1"] + 1):
        for x in range(c["x0"], c["x1"] + 1):
            floor[y][x] = True
wall = lambda x, y: 0 <= x < W and 0 <= y < H and not floor[y][x]
inb = lambda x, y: 0 <= x < W and 0 <= y < H

for y in range(H):
    print("".join("." if floor[y][x] else "#" for x in range(W)))

# grid map: centre00=(23,-190.5) pitch=(64,64); a cell's screen top-left = centre - 32
CX, CY, P = 23, -190.5, 64
faces = []
for y in range(H):
    for x in range(W):
        if not wall(x, y):
            continue
        east_open = inb(x + 1, y) and not wall(x + 1, y)
        ns_run = wall(x, y - 1) and wall(x, y + 1)
        south_open = inb(x, y + 1) and not wall(x, y + 1)
        if east_open and not ns_run:
            sx, sy = CX - 32 + P * x, CY - 32 + P * y
            faces.append((x, y, south_open, sx, sy))
print("\neast faces (code's rule): %d" % len(faces))
for f in faces:
    x, y, so, sx, sy = f
    print("  (%2d,%2d) south_open=%s  cell screen x %.0f..%.0f  cap top y %.1f  face top %.1f  foot %.1f"
          % (x, y, so, sx, sx + 64, sy, sy + 32, sy + 64))
json.dump([dict(x=f[0], y=f[1], south_open=f[2], sx=f[3], sy=f[4]) for f in faces],
          open(sys.argv[1], "w"), indent=1)
