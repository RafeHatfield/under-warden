R = '/Users/rafehatfield/development/c-yarl/.claude/worktrees/queue/'
p = R + 'tools/tier2_props/projection_mesh.py'; s = open(p).read()
old = '''    if name == "fire_ring":'''
new = '''    if name in ("barricade_c1", "barricade_c2"):
        # #207 ROUND 3 (Rafe, 2026-09-13) — the walked X-frame cold-named "crossed planks". The
        # hypothesis under test, not assumed: the X has NO HEIGHT — it lies in the gap instead of
        # standing in it. c1: crossed STAKES with visible feet planted on the floor, the crossing
        # lashed. c2: the same, taller than wide, spanning the gap edge to edge. The lashing is
        # wood-dark, not rope: the binding slot is #208-blocked and the palette lock is fenced —
        # flagged to the lock, not solved here. Chest-high still (§12.2): the stakes rise to 56
        # in a 64 cell, above the walked X's 44, and a stake reads as a stake by its FOOT — each
        # baulk ends in a visible planted end at z=0 rather than a member that could be lying.
        tall = name == "barricade_c2"
        L, W_, T = (72, 9, 9) if tall else (64, 10, 10)
        ang = 72 if tall else 62                      # steeper = taller than wide
        import math as _m
        H = L * _m.sin(_m.radians(ang))               # the stake's standing height
        cz = H / 2
        f = tiltbox(0, cz, L, W_, T, ang, cd=0, part="wood")                    # rises right
        f += tiltbox(0, cz, L, W_, T, 180 - ang, cd=-2, part="wood_dark")       # rises left, in front
        # PLANTED FEET: a short stub at the foot of each stake, square to the floor — the ground
        # contact a lying member never has.
        dx = (L / 2) * _m.cos(_m.radians(ang))
        for fx in (-dx, dx):
            f += box(fx - 6, fx + 6, -W_ / 2 - 1, W_ / 2 + 1, 0, 4, "wood_dark")
        # the crossing, lashed — wood-dark bands wrapped where the stakes meet
        f += box(-6, 6, -W_ / 2 - 3.5, -W_ / 2 - 1.5, cz - 5, cz + 5, "wood_dark")
        f += box(-6, 6, -W_ / 2 - 1.5, W_ / 2 + 8.5, cz + 5, cz + 7, "wood_dark")
        if tall:
            # spanning edge to edge: a low bar between the feet, lashed at both ends
            f += tiltbox(0, 10, 108, W_ - 1, T - 2, 1, cd=7, part="wood")
            for rx in (-48, 48):
                f += box(rx - 5, rx + 5, -W_ / 2 - 3.5, -W_ / 2 - 1.5, 6, 14, "wood_dark")
        return f
    if name == "fire_ring":'''
assert old in s; s = s.replace(old, new, 1); open(p, 'w').write(s); print('ok')
