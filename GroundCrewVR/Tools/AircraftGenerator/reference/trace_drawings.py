import numpy as np
from PIL import Image

def segments(col, max_gap):
    idx = np.nonzero(col)[0]
    if len(idx) == 0: return []
    segs, start, prev = [], idx[0], idx[0]
    for i in idx[1:]:
        if i - prev > max_gap:
            segs.append((start, prev)); start = i
        prev = i
    segs.append((start, prev))
    return segs

def trace(mask, x0, x1, ground_row, length, ref_z, step=4, max_gap=8):
    mpp = length / (x1 - x0)
    ref_row = ground_row - ref_z / mpp
    out = []
    for x in range(x0, x1 + 1, step):
        segs = segments(mask[:, x], max_gap)
        seg = [s for s in segs if s[0] <= ref_row <= s[1]]
        if not seg:
            # nose / tail tip: nearest segment to the reference row
            if not segs: continue
            seg = [min(segs, key=lambda s: min(abs(s[0]-ref_row), abs(s[1]-ref_row)))]
        a, b = seg[0]
        out.append(((x - x0) * mpp, (ground_row - b) * mpp, (ground_row - a) * mpp))
    return np.array(out)
