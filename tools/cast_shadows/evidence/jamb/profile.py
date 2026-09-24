import numpy as np, sys, json
from scipy import ndimage
O = '/private/tmp/claude-501/-Users-rafehatfield-development-c-yarl/3ac84765-6905-4a1f-91d3-5255724f9444/scratchpad/'
r = np.load(O + 'ratio.npy')
# each: label, (x, y) centre, (nx, ny) normal (unit, pointing toward the lit side), half-length
probes = json.loads(sys.argv[1])
for label, (x, y), (nx, ny), L in probes:
    n = np.hypot(nx, ny); nx, ny = nx / n, ny / n
    ts = np.arange(-L, L + 0.5, 1.0)
    prof = ndimage.map_coordinates(r, [y + ts * ny, x + ts * nx], order=1)
    prof = np.clip(prof, 0, 1)
    # 3-px running mean to take the floor's own texture out (the joints modulate the ratio)
    sm = np.convolve(prof, np.ones(3) / 3, mode='same')
    lo, hi = sm[:8].mean(), sm[-8:].mean()
    if lo > hi: lo, hi = hi, lo
    p10, p90 = lo + 0.1 * (hi - lo), lo + 0.9 * (hi - lo)
    inside = np.where((sm > p10) & (sm < p90))[0]
    w = (inside.max() - inside.min()) if len(inside) else 0
    print(f"{label:30s} ({x},{y}) n=({nx:+.2f},{ny:+.2f}) ends {lo:.2f}/{hi:.2f} 10-90 width {w:3d} px")
    print('   ', ' '.join(f'{v:.2f}' for v in prof[::2]))
