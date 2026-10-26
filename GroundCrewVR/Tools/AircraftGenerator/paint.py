"""Tiny numpy-only 2D painter for the flight-deck texture atlas.

Canvas coordinates are final-image pixels (x right, y down); drawing happens on a 2x
supersampled buffer that `result()` box-filters down, which gives cheap antialiasing.
Colours are RGB tuples in 0..1.
"""
import math

import numpy as np

# 5x7 bitmap font: 7 rows per glyph, bit 4 = leftmost column.
FONT = {
    "A": (14, 17, 17, 31, 17, 17, 17), "B": (30, 17, 17, 30, 17, 17, 30), "C": (14, 17, 16, 16, 16, 17, 14),
    "D": (30, 17, 17, 17, 17, 17, 30), "E": (31, 16, 16, 30, 16, 16, 31), "F": (31, 16, 16, 30, 16, 16, 16),
    "G": (14, 17, 16, 23, 17, 17, 15), "H": (17, 17, 17, 31, 17, 17, 17), "I": (14, 4, 4, 4, 4, 4, 14),
    "J": (7, 2, 2, 2, 2, 18, 12), "K": (17, 18, 20, 24, 20, 18, 17), "L": (16, 16, 16, 16, 16, 16, 31),
    "M": (17, 27, 21, 21, 17, 17, 17), "N": (17, 17, 25, 21, 19, 17, 17), "O": (14, 17, 17, 17, 17, 17, 14),
    "P": (30, 17, 17, 30, 16, 16, 16), "Q": (14, 17, 17, 17, 21, 18, 13), "R": (30, 17, 17, 30, 20, 18, 17),
    "S": (15, 16, 16, 14, 1, 1, 30), "T": (31, 4, 4, 4, 4, 4, 4), "U": (17, 17, 17, 17, 17, 17, 14),
    "V": (17, 17, 17, 17, 17, 10, 4), "W": (17, 17, 17, 21, 21, 21, 10), "X": (17, 17, 10, 4, 10, 17, 17),
    "Y": (17, 17, 17, 10, 4, 4, 4), "Z": (31, 1, 2, 4, 8, 16, 31),
    "0": (14, 17, 19, 21, 25, 17, 14), "1": (4, 12, 4, 4, 4, 4, 14), "2": (14, 17, 1, 2, 4, 8, 31),
    "3": (31, 2, 4, 2, 1, 17, 14), "4": (2, 6, 10, 18, 31, 2, 2), "5": (31, 16, 30, 1, 1, 17, 14),
    "6": (6, 8, 16, 30, 17, 17, 14), "7": (31, 1, 2, 4, 8, 8, 8), "8": (14, 17, 17, 14, 17, 17, 14),
    "9": (14, 17, 17, 15, 1, 2, 12), "-": (0, 0, 0, 31, 0, 0, 0), "/": (0, 1, 2, 4, 8, 16, 0),
    ".": (0, 0, 0, 0, 0, 12, 12), "+": (0, 4, 4, 31, 4, 4, 0), ":": (0, 12, 12, 0, 12, 12, 0),
    "%": (24, 25, 2, 4, 8, 19, 3), "<": (2, 4, 8, 16, 8, 4, 2), ">": (8, 4, 2, 1, 2, 4, 8),
    "[": (14, 8, 8, 8, 8, 8, 14), "]": (14, 2, 2, 2, 2, 2, 14), "*": (0, 21, 14, 31, 14, 21, 0),
    " ": (0, 0, 0, 0, 0, 0, 0),
}


class Canvas:
    def __init__(self, width, height, background=(0, 0, 0), ss=2):
        self.w, self.h, self.ss = width, height, ss
        self.img = np.empty((height * ss, width * ss, 3), dtype=np.float32)
        self.img[:] = background
        self.clip = (0, 0, width, height)

    # ------------------------------------------------------------------ helpers
    def _box(self, x0, y0, x1, y1):
        s = self.ss
        cx0, cy0, cx1, cy1 = self.clip
        x0, y0, x1, y1 = max(x0, cx0), max(y0, cy0), min(x1, cx1), min(y1, cy1)
        a, b = max(int(math.floor(x0 * s)), 0), min(int(math.ceil(x1 * s)), self.w * s)
        c, d = max(int(math.floor(y0 * s)), 0), min(int(math.ceil(y1 * s)), self.h * s)
        a, b = max(a, int(round(cx0 * s))), min(b, int(round(cx1 * s)))
        c, d = max(c, int(round(cy0 * s))), min(d, int(round(cy1 * s)))
        if a >= b or c >= d:
            return None
        X, Y = np.meshgrid((np.arange(a, b) + 0.5) / s, (np.arange(c, d) + 0.5) / s)
        return (slice(c, d), slice(a, b)), X, Y

    def _paint(self, region, mask, col):
        sl = region
        self.img[sl][mask] = col

    # ------------------------------------------------------------------ shapes
    def rect(self, x, y, w, h, col):
        s = self.ss
        cx0, cy0, cx1, cy1 = self.clip
        x, y, w, h = max(x, cx0), max(y, cy0), min(x + w, cx1) - max(x, cx0), min(y + h, cy1) - max(y, cy0)
        a, b = max(int(round(x * s)), 0), min(int(round((x + w) * s)), self.w * s)
        c, d = max(int(round(y * s)), 0), min(int(round((y + h) * s)), self.h * s)
        if a < b and c < d:
            self.img[c:d, a:b] = col

    def frame(self, x, y, w, h, t, col):
        self.rect(x, y, w, t, col)
        self.rect(x, y + h - t, w, t, col)
        self.rect(x, y, t, h, col)
        self.rect(x + w - t, y, t, h, col)

    def disc(self, cx, cy, r, col):
        box = self._box(cx - r, cy - r, cx + r, cy + r)
        if box:
            sl, X, Y = box
            self._paint(sl, (X - cx) ** 2 + (Y - cy) ** 2 <= r * r, col)

    def arc(self, cx, cy, r, width, a0, a1, col):
        """Ring segment; angles in degrees, 0 = up, clockwise (gauge convention)."""
        ro = r + width / 2
        box = self._box(cx - ro, cy - ro, cx + ro, cy + ro)
        if box:
            sl, X, Y = box
            d = np.hypot(X - cx, Y - cy)
            ang = (np.degrees(np.arctan2(X - cx, -(Y - cy))) - a0) % 360
            self._paint(sl, (np.abs(d - r) <= width / 2) & (ang <= (a1 - a0) % 360 + 1e-6), col)

    def ring(self, cx, cy, r, width, col):
        self.arc(cx, cy, r, width, 0, 359.999, col)

    def line(self, x0, y0, x1, y1, width, col):
        hw = width / 2
        box = self._box(min(x0, x1) - hw, min(y0, y1) - hw, max(x0, x1) + hw, max(y0, y1) + hw)
        if box:
            sl, X, Y = box
            dx, dy = x1 - x0, y1 - y0
            L2 = max(dx * dx + dy * dy, 1e-9)
            t = np.clip(((X - x0) * dx + (Y - y0) * dy) / L2, 0, 1)
            self._paint(sl, np.hypot(X - (x0 + t * dx), Y - (y0 + t * dy)) <= hw, col)

    def polar_line(self, cx, cy, angle, r0, r1, width, col):
        a = math.radians(angle)
        sx, sy = math.sin(a), -math.cos(a)
        self.line(cx + sx * r0, cy + sy * r0, cx + sx * r1, cy + sy * r1, width, col)

    def poly(self, pts, col):
        """Filled convex polygon."""
        pts = np.asarray(pts, dtype=float)
        box = self._box(pts[:, 0].min(), pts[:, 1].min(), pts[:, 0].max(), pts[:, 1].max())
        if not box:
            return
        sl, X, Y = box
        area = np.sum(pts[:, 0] * np.roll(pts[:, 1], -1) - np.roll(pts[:, 0], -1) * pts[:, 1])
        inside = np.ones(X.shape, dtype=bool)
        for k in range(len(pts)):
            (xa, ya), (xb, yb) = pts[k], pts[(k + 1) % len(pts)]
            c = (xb - xa) * (Y - ya) - (yb - ya) * (X - xa)
            inside &= (c >= 0) if area > 0 else (c <= 0)
        self._paint(sl, inside, col)

    def text(self, x, y, s, col, size=7.0, align="l"):
        """Bitmap text; size = glyph height in pixels; (x, y) = top-left (or top-centre / top-right)."""
        px = size / 7.0
        adv = 6 * px
        width = len(s) * adv - px
        if align == "c":
            x -= width / 2
        elif align == "r":
            x -= width
        for k, ch in enumerate(s.upper()):
            rows = FONT.get(ch, FONT[" "])
            gx = x + k * adv
            for r, bits in enumerate(rows):
                for c in range(5):
                    if bits & (16 >> c):
                        self.rect(gx + c * px, y + r * px, px, px, col)

    # ------------------------------------------------------------------ output
    def result(self):
        s = self.ss
        img = self.img.reshape(self.h, s, self.w, s, 3).mean(axis=(1, 3))
        return (np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8)


def pack(sizes, width):
    """Shelf-pack named rectangles {name: (w, h)} into an atlas `width` wide.
    Returns ({name: (x, y, w, h)}, height rounded up to a power of two)."""
    zones, x, y, shelf = {}, 0, 0, 0
    for name, (w, h) in sorted(sizes.items(), key=lambda kv: -kv[1][1]):
        if x + w > width:
            x, y, shelf = 0, y + shelf, 0
        zones[name] = (x, y, w, h)
        x += w
        shelf = max(shelf, h)
    height = 1
    while height < y + shelf:
        height *= 2
    return zones, height
