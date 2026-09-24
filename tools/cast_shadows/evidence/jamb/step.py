"""Bar 2 (edge): the step in the shadow factor across the jamb's east-face outline.
Shadow factor r = lit(occluders all) / lit(occluders none), per pixel, so the MATERIAL cancels and
only the light's shadowing remains. The outline is the exempt region measured on the PRE-FIX pair
(r == 1.00 exactly, because an exempt sprite receives no shadow) — an outline nobody drew by hand.
Inside = that region eroded 2 px; outside = a 2..5 px ring around it on the corridor floor, east
of the jamb's own exempt front sprite."""
from PIL import Image
import numpy as np
from scipy import ndimage
E = '/Users/rafehatfield/development/c-yarl/.claude/worktrees/queue/tools/cast_shadows/evidence/jamb/'
def lum(p): return np.asarray(Image.open(E + p).convert('RGB')).astype(float).mean(axis=2)
def ratio(a, b):
    A, B = lum(a), lum(b); return np.clip(np.where(B > 8, A / np.maximum(B, 1), 1.0), 0, 1.2)
pre = ratio('pre_shadow.png', 'pre_noshadow.png')
post = ratio('jamb_m_shadow.png', 'jamb_m_noshadow.png')
# the exempt parallelogram: r>=0.995 inside the corridor mouth cell, east of the jamb's own sprite
win = np.zeros_like(pre, bool); win[470:560, 503:545] = True
ex = (pre >= 0.995) & win
ex = ndimage.binary_opening(ex, iterations=1)
lab, n = ndimage.label(ex); sizes = ndimage.sum(ex, lab, range(1, n + 1)); ex = lab == (1 + int(np.argmax(sizes)))
ys, xs = np.where(ex); print('exempt outline: x %d..%d y %d..%d, %d px' % (xs.min(), xs.max(), ys.min(), ys.max(), ex.sum()))
inner = ndimage.binary_erosion(ex, iterations=2)
outer = ndimage.binary_dilation(ex, iterations=5) & ~ndimage.binary_dilation(ex, iterations=2)
outer &= ~(pre >= 0.995)          # never the neighbouring exempt wall sprites, only floor
for name, r in (('PRE-FIX ', pre), ('POST-FIX', post)):
    i, o = r[inner].mean(), r[outer].mean()
    print('%s  shadow factor inside %.2f  ring outside %.2f  step %.2f' % (name, i, o, abs(i - o)))
# the pillar's east face must stay lit: the lamp is on its side
px = np.zeros_like(pre, bool); px[745:800, 380:405] = True
print('pillar east face shadow factor post-fix: %.2f (pre %.2f)' % (post[px].mean(), pre[px].mean()))
print()
for f in ('pre_shadow.png','pre_noshadow.png','jamb_m_shadow.png','jamb_m_noshadow.png','m_nofire_s0.png','m_nofire_none.png','m_masktest.png'):
    im=np.asarray(Image.open(E+f).convert('RGB')).astype(float)
    L=im.mean(axis=2); rb=(im[...,0]+1)/(im[...,2]+1)
    print('%-22s face L %5.1f r/b %.2f | ring L %5.1f r/b %.2f' % (f, L[inner].mean(), rb[inner].mean(), L[outer].mean(), rb[outer].mean()))
