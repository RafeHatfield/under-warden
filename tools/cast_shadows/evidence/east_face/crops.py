"""Crop each east face (the wall cell and the cell to its east) at 3x, with the code's wedge outline and the cap-top / foot lines drawn in."""
import json, sys
from PIL import Image, ImageDraw

png, faces, out = sys.argv[1], json.load(open(sys.argv[2])), sys.argv[3]
im = Image.open(png).convert("RGB")
S = 3
tiles = []
for f in faces:
    sx, sy = f["sx"], f["sy"]
    box = (int(sx - 32), int(sy - 32), int(sx + 160), int(sy + 96))
    if box[2] > im.width or box[0] < 0:
        continue
    c = im.crop(box).resize(((box[2] - box[0]) * S, (box[3] - box[1]) * S), Image.NEAREST)
    dr = ImageDraw.Draw(c)
    ox, oy = box[0], box[1]
    T = lambda x, y: ((x - ox) * S, (y - oy) * S)
    # the code's wedge: (64,64)-(64,32)-(96,2)-(96,34) in screen px from the cell's top-left
    poly = [T(sx + 64, sy + 64), T(sx + 64, sy + 32), T(sx + 96, sy + 2), T(sx + 96, sy + 34)]
    dr.line(poly + [poly[0]], fill=(0, 255, 255), width=1)
    dr.line([T(sx - 32, sy), T(sx + 160, sy)], fill=(255, 0, 255), width=1)        # cap top
    dr.line([T(sx - 32, sy + 64), T(sx + 160, sy + 64)], fill=(255, 255, 0), width=1)  # foot
    dr.text((4, 4), "(%d,%d)" % (f["x"], f["y"]), fill=(255, 255, 255))
    tiles.append(c)
w = max(t.width for t in tiles)
sheet = Image.new("RGB", (w, sum(t.height + 6 for t in tiles)), (40, 0, 40))
yy = 0
for t in tiles:
    sheet.paste(t, (0, yy)); yy += t.height + 6
sheet.save(out)
print("tiles", len(tiles), sheet.size)
