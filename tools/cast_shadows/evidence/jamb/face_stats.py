"""Structure and hue of the two east faces, from the rendered frame, each normalised by its own
cell's FRONT face (same lamp, same distance to within a cell): does the jamb's east face carry the
pillar's slab structure and quarry hue, or is it a flat quad?"""
from PIL import Image
import numpy as np, sys
E = '/Users/rafehatfield/development/c-yarl/.claude/worktrees/queue/tools/cast_shadows/evidence/jamb/'
im = np.asarray(Image.open(E + (sys.argv[1] if len(sys.argv) > 1 else 'jamb_r0.png')).convert('RGB')).astype(float)
OX, OY = 7, 221
def para(cx, cy):
    """pixels of the east-face parallelogram hanging off cell (cx, cy): screen x tw..tw+32."""
    x0, y0 = cx * 64 - OX + 64, cy * 64 - OY
    pts = []
    for u in range(32):
        lo, hi = 16 - u // 2, 32 - u // 2
        for v in range(2 * lo, 2 * hi):
            pts.append((y0 + v, x0 + u))
    ys, xs = zip(*pts); return im[list(ys), list(xs)]
def front(cx, cy):
    x0, y0 = cx * 64 - OX, cy * 64 - OY + 32
    return im[y0:y0 + 32, x0:x0 + 64].reshape(-1, 3)
def stats(px):
    L = px.mean(axis=1); return L.mean(), L.std(), (px[:, 0].mean() + 1) / (px[:, 2].mean() + 1)
for name, c in (('jamb (7,11)', (7, 11)), ('pillar (5,15)', (5, 15))):
    eL, eS, eH = stats(para(*c)); fL, fS, fH = stats(front(*c))
    print(f"{name:14s} east: L={eL:6.1f} std={eS:5.1f} cv={eS/eL:.2f} r/b={eH:.2f} | front: L={fL:6.1f} std={fS:5.1f} cv={fS/fL:.2f} r/b={fH:.2f} | east/front L={eL/fL:.2f}")
