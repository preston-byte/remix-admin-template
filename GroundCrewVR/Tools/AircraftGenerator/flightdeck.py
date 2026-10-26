"""Flight decks: layout, controls, seats and a painted panel texture atlas.

Everything is placed relative to the pilots' design eye point from the manufacturers'
airport-planning documents (specs.py "flight_deck"). The rest follows typical flight-deck
ergonomics and is APPROX: it is meant to read correctly in VR, not to be a procedures trainer.

Two styles:
  boeing (737NG class): control columns with yokes; six displays with the engine displays
      stacked in the centre; a tall control stand right under the centre displays with long
      thrust levers, speed brake, flap lever and big striped stabiliser trim wheels; the two
      CDUs behind it; grey panels with toggle switches.
  airbus (A320 class): side consoles with sidesticks and steering handwheels, pull-out tables
      and no yokes; six displays with the ECAM pair stacked in the centre; the two MCDUs at the
      front of the pedestal, short thrust levers in a gated housing behind them; blue-grey
      panels with square "lights out" pushbuttons.

No brand names or logos are painted anywhere.
"""
import math

import numpy as np

from meshlib import Part, box, cylinder, grid, normalize
from paint import Canvas, pack

D = math.pi / 180.0
ATLAS_WIDTH = 2048

# ---------------------------------------------------------------------- colours
WHITE = (0.92, 0.92, 0.90)
GREY = (0.55, 0.56, 0.57)
DARK = (0.09, 0.09, 0.10)
BLACK = (0.0, 0.0, 0.0)
GREEN = (0.15, 0.95, 0.30)
CYAN = (0.20, 0.85, 0.95)
MAGENTA = (0.95, 0.30, 0.90)
AMBER = (1.0, 0.65, 0.10)
YELLOW = (1.0, 0.90, 0.10)
RED = (0.90, 0.12, 0.08)
SKY = {"boeing": (0.15, 0.45, 0.85), "airbus": (0.10, 0.50, 0.95)}
GROUND = {"boeing": (0.55, 0.33, 0.13), "airbus": (0.55, 0.30, 0.10)}
PANEL = {"boeing": (0.33, 0.35, 0.37), "airbus": (0.43, 0.50, 0.57)}
SEAT = {"boeing": (0.11, 0.11, 0.12), "airbus": (0.12, 0.15, 0.21)}


def materials(style):
    """Flight-deck materials: name -> dict(rgb, metallic, roughness, emissive, texture, ...)."""
    return {
        "FD_Panel": dict(rgb=PANEL[style], metallic=0.0, roughness=0.65),
        "FD_Dark": dict(rgb=(0.07, 0.07, 0.08), metallic=0.0, roughness=0.8),
        "FD_Faces": dict(rgb=(1.0, 1.0, 1.0), metallic=0.0, roughness=0.6, texture=True),
        "FD_Screens": dict(rgb=(0.25, 0.25, 0.25), metallic=0.0, roughness=0.25, texture=True,
                           emissive=(1.0, 1.0, 1.0), emissive_texture=True),
        "FD_Seat": dict(rgb=SEAT[style], metallic=0.0, roughness=0.75),
        "FD_Grip": dict(rgb=(0.04, 0.04, 0.045), metallic=0.1, roughness=0.5),
        "FD_Handle": dict(rgb=(0.85, 0.86, 0.84), metallic=0.0, roughness=0.45),
        "FD_DomeLight": dict(rgb=(1.0, 0.97, 0.9), metallic=0.0, roughness=0.5, emissive=(1.0, 0.95, 0.85)),
    }


# ====================================================================== layout

class Layout:
    """Key dimensions of one flight deck, shared by the painter and the geometry."""

    def __init__(self, fus, sp):
        fd = sp["flight_deck"]
        self.fus = fus
        self.style = fd["style"]
        self.boeing = self.style == "boeing"
        self.sE, self.yE, self.zE = fd["eye"]
        self.zf = sp["floor_z"]
        sE, zE = self.sE, self.zE
        b = self.boeing

        # Glareshield top 0.17 m below the eye; its lip leaves the documented over-nose view
        # (737 15 deg, A320 20 deg) clear.
        self.z_g = zE - 0.17
        self.s_lip = sE - (0.50 if b else 0.46)
        self.face_h = 0.11                         # MCP / FCU face
        self.du = 0.205 if b else 0.19             # display unit size
        self.tilt = 10 * D
        self.z_top = self.z_g - self.face_h        # main panel top edge
        self.z_d = self.z_top - 0.02 - self.du / 2  # display centre height
        self.s_panel = sE - (0.72 if b else 0.70)  # panel face at display height
        self.Ym = 0.85                             # panel texture half-width
        du = self.du
        self.lower = -(du + (0.015 if b else 0.025))
        self.displays = [("pfd", 0.595 if b else 0.58, 0.0), ("nd", 0.365 if b else 0.37, 0.0),
                         ("eng", 0.0, 0.0), ("sys", 0.0, self.lower),
                         ("nd", -(0.365 if b else 0.37), 0.0), ("pfd", -(0.595 if b else 0.58), 0.0)]
        self.zr_top = du / 2 + 0.02
        self.zr_side = -du / 2 - (0.06 if b else 0.05)
        self.hc = 0.26                              # centre panel half-width (between the knees)
        self.zr_bot = self.lower - du / 2 - (0.04 if b else 0.03)
        self.stby = (0.19, 0.035, 0.085)
        self.gear = (-0.19, -0.11 if b else -0.15)

        # pedestal: (distance aft of the panel foot, height change) profile, half-width
        s_cb, z_cb = self.panel_point(0.0, self.zr_bot)[[0, 2]]
        self.s_cb, self.z_cb = s_cb, z_cb
        if b:
            self.ped_profile = [(0.0, 0.0), (0.42, 0.0), (0.66, -0.06), (1.12, -0.07)]
            self.cdu_seg = 1                        # CDUs sit on segment 1 (behind the control stand)
        else:
            self.ped_profile = [(0.0, 0.03), (0.30, -0.04), (0.44, -0.04), (1.08, -0.06)]
            self.cdu_seg = 0                        # MCDUs at the very front
        self.ped_hw = 0.19
        self.ped_len = sum(math.hypot(b2[0] - a[0], b2[1] - a[1]) for a, b2 in zip(self.ped_profile, self.ped_profile[1:]))

        # side consoles
        self.side_y = self.yE + 0.26
        self.side_z = self.zf + 0.62
        self.side_s = (sE - 0.58, sE + 0.32)

        # overhead panel: from just behind the windscreen's top centre to above the pilots' heads
        top_c = [c for c in sp["windshield"][0] if c[0] == "c"]
        z_top_c = max(c[1] for c in top_c)
        s0 = 0.05
        while self.fus.section(s0)[0] + self.fus.section(s0)[2] < z_top_c and s0 < 5:
            s0 += 0.01
        self.s_o0 = s0 + 0.10
        self.s_o1 = sE + 0.62
        self.o_hw = 0.42

    # MIP plane: (y, slant height zr from the display centre line) -> point
    def panel_point(self, y, zr):
        a = self.tilt
        return np.array([self.s_panel - zr * math.sin(a), y, self.z_d + zr * math.cos(a)])

    @property
    def panel_normal(self):
        return np.array([math.cos(self.tilt), 0.0, math.sin(self.tilt)])

    @property
    def panel_up(self):
        return np.array([-math.sin(self.tilt), 0.0, math.cos(self.tilt)])

    def roof_z(self, s, y):
        fus = self.fus
        zc, a, h = fus.section(s)
        a, h = a - fus.wall, h - fus.wall
        q = 1 - (y / a) ** 2
        return zc + h * math.sqrt(q) if q > 0 else zc

    def wall_y(self, s, z, margin=0.0):
        return max(self.fus.inner_halfwidth(s, z) - margin, 0.0)

    def ped_point(self, v):
        """Pedestal top at arc length v from the panel foot: (s, z, slope angle)."""
        prof = self.ped_profile
        acc = 0.0
        for (d0, h0), (d1, h1) in zip(prof, prof[1:]):
            seg = math.hypot(d1 - d0, h1 - h0)
            if v <= acc + seg or (d1, h1) == prof[-1]:
                t = (v - acc) / seg
                return (self.s_cb + d0 + (d1 - d0) * t, self.z_cb + h0 + (h1 - h0) * t, math.atan2(h1 - h0, d1 - d0))
            acc += seg

    def ped_seg_range(self, k):
        prof = self.ped_profile
        acc = sum(math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(prof[:k], prof[1:k + 1]))
        seg = math.hypot(prof[k + 1][0] - prof[k][0], prof[k + 1][1] - prof[k][1])
        return acc, acc + seg


# ====================================================================== texture atlas

ZONE_SIZES = {
    "pfd": (240, 240), "nd": (240, 240), "eng": (240, 240), "sys": (240, 240), "stby": (96, 96),
    "cdu": (160, 256),
    "mip": (1020, 300),      # main panel, y in [-Ym, Ym], zr in [zr_bot, zr_top]
    "mcp": (1020, 66),       # glareshield face
    "overhead": (420, 520),
    "ped": (190, 560),       # pedestal top along its length
    "quad": (128, 200),      # control stand / thrust housing top
    "side": (150, 300),      # side console top
}
CDU_SCREEN = (0.08, 0.06, 0.92, 0.42)   # screen within the cdu zone (u0, v0, u1, v1)


class Zone:
    """Drawing helpers in zone-local pixels."""

    def __init__(self, canvas, rect):
        self.c = canvas
        self.x, self.y, self.w, self.h = rect
        canvas.clip = (self.x, self.y, self.x + self.w, self.y + self.h)   # painting stays inside the zone

    def rect(self, x, y, w, h, col):
        self.c.rect(self.x + x, self.y + y, w, h, col)

    def frame(self, x, y, w, h, t, col):
        self.c.frame(self.x + x, self.y + y, w, h, t, col)

    def disc(self, x, y, r, col):
        self.c.disc(self.x + x, self.y + y, r, col)

    def arc(self, x, y, r, width, a0, a1, col):
        self.c.arc(self.x + x, self.y + y, r, width, a0, a1, col)

    def ring(self, x, y, r, width, col):
        self.c.ring(self.x + x, self.y + y, r, width, col)

    def line(self, x0, y0, x1, y1, width, col):
        self.c.line(self.x + x0, self.y + y0, self.x + x1, self.y + y1, width, col)

    def polar(self, x, y, angle, r0, r1, width, col):
        self.c.polar_line(self.x + x, self.y + y, angle, r0, r1, width, col)

    def poly(self, pts, col):
        self.c.poly([(self.x + px, self.y + py) for px, py in pts], col)

    def text(self, x, y, s, col, size=7.0, align="l"):
        self.c.text(self.x + x, self.y + y, s, col, size, align)


class MetricZone(Zone):
    """Zone addressed in metres: u_range maps to x (left to right), v_range to y (top to bottom)."""

    def __init__(self, canvas, rect, u_range, v_range):
        super().__init__(canvas, rect)
        self.u0, self.u1 = u_range
        self.v0, self.v1 = v_range

    def px(self, u, v):
        return (u - self.u0) / (self.u1 - self.u0) * self.w, (v - self.v0) / (self.v1 - self.v0) * self.h

    def scale(self):
        return self.w / abs(self.u1 - self.u0)

    def mrect(self, u, v, w, h, col):
        """Rectangle centred on (u, v), size in metres."""
        x, y = self.px(u, v)
        k = self.scale()
        self.rect(x - w * k / 2, y - h * k / 2, w * k, h * k, col)


def uv_of(zones, atlas_h, name, fx, fy):
    """Atlas UV of a fractional position inside a zone (half-texel inset keeps filtering inside)."""
    x, y, w, h = zones[name]
    fx = min(max(fx, 0.5 / w), 1 - 0.5 / w)
    fy = min(max(fy, 0.5 / h), 1 - 0.5 / h)
    return ((x + fx * w) / ATLAS_WIDTH, (y + fy * h) / atlas_h)


# ---------------------------------------------------------------------- painters: displays

def paint_pfd(z, style):
    w, h = z.w, z.h
    z.rect(0, 0, w, h, BLACK)
    cx, cy, half = w * 0.47, h * 0.47, w * 0.27
    z.rect(cx - half, cy - half, 2 * half, half, SKY[style])
    z.rect(cx - half, cy, 2 * half, half, GROUND[style])
    z.rect(cx - half, cy - 1, 2 * half, 2, WHITE)
    for k in (-2, -1, 1, 2):
        yy = cy - k * 22
        ln = 26 if k % 2 == 0 else 14
        z.rect(cx - ln, yy - 0.8, 2 * ln, 1.6, WHITE)
        if k % 2 == 0:
            z.text(cx - ln - 4, yy - 3, str(abs(k) * 5), WHITE, 6, "r")
    z.arc(cx, cy, half * 0.95, 1.5, -60, 60, WHITE)
    for a in (-60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60):
        z.polar(cx, cy, a, half * 0.95, half * 0.95 + (8 if a % 30 == 0 else 5), 1.5, WHITE)
    if style == "boeing":
        for sgn in (-1, 1):
            z.rect(cx + sgn * 34 - 17, cy - 3, 34, 7, WHITE)
            z.rect(cx + sgn * 34 - 15, cy - 1, 30, 3, BLACK)
        z.rect(cx - 4, cy - 4, 8, 8, WHITE)
        z.rect(cx - 2, cy - 2, 4, 4, BLACK)
        fma = [("MCP SPD", GREEN), ("LNAV", GREEN), ("VNAV PTH", GREEN)]
        for k, (t, col) in enumerate(fma):
            z.text(w * (0.25 + 0.25 * k), 8, t, col, 7, "c")
            if k:
                z.rect(w * (0.125 + 0.25 * k), 4, 1.5, 22, WHITE)
        z.text(w * 0.47, 32, "CMD", GREEN, 8, "c")
    else:
        for sgn in (-1, 1):
            z.rect(cx + sgn * 36 - 16, cy - 3, 32, 6, BLACK)
            z.rect(cx + sgn * 36 - 15, cy - 2, 30, 4, YELLOW)
        z.rect(cx - 4, cy - 4, 8, 8, BLACK)
        z.rect(cx - 3, cy - 3, 6, 6, YELLOW)
        cols = [("SPEED", GREEN), ("ALT", GREEN), ("NAV", GREEN), ("CAT3", WHITE), ("AP1", WHITE)]
        for k, (t, col) in enumerate(cols):
            z.text(w * (0.1 + 0.2 * k), 6, t, col, 6.5, "c")
            if k:
                z.rect(w * 0.2 * k, 3, 1.2, 30, WHITE)
        z.text(w * 0.3, 19, "CLB", CYAN, 6.5, "c")
        z.text(w * 0.9, 19, "A/THR", WHITE, 6.5, "c")
    # speed tape (left) and altitude tape (right)
    tape_h = 2 * half
    z.rect(10, cy - half, 34, tape_h, (0.33, 0.33, 0.36))
    for k in range(-3, 4):
        yy = cy - k * 18
        z.rect(36, yy - 0.8, 8, 1.6, WHITE)
        if k % 2 == 0:
            z.text(33, yy - 3, str(140 + k * 10), WHITE, 6, "r")
    z.rect(6, cy - 8, 40, 16, BLACK)
    z.frame(6, cy - 8, 40, 16, 1.5, WHITE if style == "boeing" else YELLOW)
    z.text(42, cy - 4, "142", WHITE if style == "boeing" else GREEN, 8, "r")
    ax = w - 56
    z.rect(ax, cy - half, 40, tape_h, (0.33, 0.33, 0.36))
    for k in range(-3, 4):
        yy = cy - k * 18
        z.rect(ax, yy - 0.8, 6, 1.6, WHITE)
        if k % 2 == 0:
            z.text(ax + 9, yy - 3, str(1200 + k * 100), WHITE, 5.5)
    z.rect(ax - 2, cy - 8, 46, 16, BLACK)
    z.frame(ax - 2, cy - 8, 46, 16, 1.5, WHITE if style == "boeing" else YELLOW)
    z.text(ax + 41, cy - 4, "1200", GREEN if style == "airbus" else WHITE, 8, "r")
    z.rect(w - 13, cy - half * 0.9, 9, half * 1.8, (0.25, 0.25, 0.28))
    # heading at the bottom
    if style == "boeing":
        z.disc(cx, h + 38, 70, (0.30, 0.30, 0.33))
        for a in range(-50, 51, 10):
            z.polar(cx, h + 38, a, 62, 70, 1.5, WHITE)
        z.text(cx, h - 30, "24", WHITE, 7, "c")
        z.poly([(cx - 5, h - 34), (cx + 5, h - 34), (cx, h - 40)], WHITE)
    else:
        z.rect(cx - half, h - 26, 2 * half, 18, (0.30, 0.30, 0.33))
        for k in range(-4, 5):
            z.rect(cx + k * 14 - 0.7, h - 26, 1.4, 6 if k % 2 else 10, WHITE)
        z.text(cx, h - 14, "24", WHITE, 6.5, "c")
        z.rect(cx - 1, h - 32, 2, 10, YELLOW)


def paint_nd(z, style):
    w, h = z.w, z.h
    z.rect(0, 0, w, h, BLACK)
    cx, cy, r = w / 2, h * 0.86, w * 0.62
    z.arc(cx, cy, r, 2, -55, 55, WHITE)
    for a in range(-55, 56, 5):
        z.polar(cx, cy, a, r, r - (10 if a % 10 == 0 else 5), 1.5, WHITE)
    for a, t in ((-30, "21"), (0, "24"), (30, "27")):
        ar = math.radians(a)
        z.text(cx + math.sin(ar) * (r - 22), cy - math.cos(ar) * (r - 22) - 4, t, WHITE, 7, "c")
    for k in range(1, 4):
        z.arc(cx, cy, r * k / 4, 1, -50, 50, (0.4, 0.4, 0.4))
    route = MAGENTA if style == "boeing" else GREEN
    pts = [(cx, cy), (cx + 10, cy - 70), (cx + 46, cy - 120), (cx + 60, cy - 165)]
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        z.line(x0, y0, x1, y1, 2.2, route)
    for (x, y), name in zip(pts[1:], ("ROXY", "DUKE", "LOBO")):
        if style == "boeing":
            z.poly([(x, y - 6), (x + 6, y), (x, y + 6), (x - 6, y)], CYAN)
            z.poly([(x, y - 3), (x + 3, y), (x, y + 3), (x - 3, y)], BLACK)
        else:
            z.line(x - 5, y - 5, x + 5, y + 5, 2, WHITE)
            z.line(x - 5, y + 5, x + 5, y - 5, 2, WHITE)
        z.text(x + 9, y - 3, name, WHITE if style == "airbus" else MAGENTA, 6.5)
    if style == "boeing":
        z.poly([(cx, cy - 16), (cx + 9, cy + 6), (cx - 9, cy + 6)], WHITE)
        z.poly([(cx, cy - 12), (cx + 6, cy + 3), (cx - 6, cy + 3)], BLACK)
        z.rect(cx - 34, 4, 68, 16, BLACK)
        z.frame(cx - 34, 4, 68, 16, 1.5, WHITE)
        z.text(cx, 8, "TRK 240", WHITE, 7, "c")
        z.text(6, 6, "GS  0", WHITE, 6.5)
        z.text(w - 6, 6, "ROXY", MAGENTA, 6.5, "r")
        z.text(w - 6, 18, "12.4 NM", WHITE, 6.5, "r")
    else:
        z.rect(cx - 1.5, cy - 14, 3, 22, YELLOW)
        z.rect(cx - 11, cy - 6, 22, 3, YELLOW)
        z.rect(cx - 5, cy + 5, 10, 2.5, YELLOW)
        z.rect(cx - 1.5, 6, 3, 14, YELLOW)
        z.text(6, 6, "GS 0", WHITE, 6.5)
        z.text(6, 18, "TAS ---", WHITE, 6.5)
        z.text(w - 6, 6, "ROXY", GREEN, 6.5, "r")
        z.text(w - 6, 18, "12 NM", WHITE, 6.5, "r")


def dial(z, x, y, r, value, colour_arc, needle, label, readout, style):
    a0, a1 = -120, 90
    z.arc(x, y, r, 2.2, a0, a1, colour_arc)
    z.arc(x, y, r, 2.2, a1 - 12, a1, RED if style == "boeing" else AMBER)
    ang = a0 + (a1 - a0) * value
    z.polar(x, y, ang, 0, r - 2, 2.2, needle)
    z.rect(x + 2, y - r * 0.55, r * 1.25, 13, BLACK)
    z.frame(x + 2, y - r * 0.55, r * 1.25, 13, 1.2, WHITE if style == "boeing" else GREY)
    z.text(x + 2 + r * 1.2, y - r * 0.55 + 3, readout, needle, 7, "r")
    if label:
        z.text(x, y + r * 0.5, label, CYAN if style == "airbus" else WHITE, 6.5, "c")


def paint_eng(z, style):
    w, h = z.w, z.h
    z.rect(0, 0, w, h, BLACK)
    if style == "boeing":
        for k, xx in enumerate((w * 0.28, w * 0.72)):
            dial(z, xx, 52, 30, 0.22, WHITE, WHITE, "N1" if k == 0 else "", "21.4", style)
            dial(z, xx, 128, 26, 0.35, WHITE, WHITE, "EGT" if k == 0 else "", "380", style)
        z.text(8, 6, "TAT +12C", WHITE, 6.5)
        z.text(w * 0.5, 168, "FUEL", CYAN, 6.5, "c")
        for k, t in enumerate(("4.21", "5.83", "4.21")):
            x0 = w * (0.18 + 0.32 * k)
            z.frame(x0 - 26, 180, 52, 18, 1.4, WHITE)
            z.text(x0, 185, t, WHITE, 7.5, "c")
        z.text(w * 0.5, 210, "LBS X 1000", WHITE, 6, "c")
    else:
        for k, xx in enumerate((w * 0.28, w * 0.72)):
            dial(z, xx, 50, 30, 0.2, WHITE, GREEN, "N1" if k == 0 else "", "19.5", style)
            dial(z, xx, 120, 24, 0.33, WHITE, GREEN, "EGT" if k == 0 else "", "412", style)
        z.text(w * 0.5, 150, "N2 %", CYAN, 6, "c")
        z.text(w * 0.25, 150, "58.6", GREEN, 7, "c")
        z.text(w * 0.75, 150, "58.6", GREEN, 7, "c")
        z.text(8, 6, "FOB: 6200 KG", WHITE, 6.5)
        z.text(w - 8, 6, "S   F", WHITE, 6.5, "r")
        z.rect(4, 168, w - 8, 1.5, WHITE)
        for k, t in enumerate(("SEAT BELTS", "NO SMOKING", "APU AVAIL", "REFUELG")):
            z.text(10, 176 + k * 14, t, GREEN, 7)


def paint_sys(z, style):
    w, h = z.w, z.h
    z.rect(0, 0, w, h, BLACK)
    if style == "boeing":
        for k, xx in enumerate((w * 0.28, w * 0.72)):
            dial(z, xx, 46, 26, 0.55, WHITE, WHITE, "N2" if k == 0 else "", "58.6", "boeing")
            z.frame(xx - 22, 86, 44, 16, 1.2, WHITE)
            z.text(xx, 90, "0.30", WHITE, 7, "c")
            for j, lab in enumerate(("OIL P", "OIL T")):
                gx = xx - 16 + j * 32
                z.frame(gx - 5, 120, 10, 70, 1.2, WHITE)
                z.rect(gx - 3, 160 - j * 12, 6, 28 + j * 12, WHITE)
                if k == 0:
                    z.text(w * 0.5, 130 + j * 26, lab, CYAN, 6, "c")
        z.text(w * 0.5, 92, "FF", CYAN, 6, "c")
        z.text(w * 0.5, 215, "VIB 0.4  0.4", WHITE, 6.5, "c")
    else:
        # ECAM DOOR / OXY page: the doors a ground crew cares about
        z.text(w * 0.5, 6, "DOOR/OXY", WHITE, 7.5, "c")
        z.rect(w * 0.5 - 30, 15, 60, 1.2, WHITE)
        cx = w * 0.5
        top, bot, hw = 30, 210, 22
        z.arc(cx, top + hw, hw, 1.6, -90, 90, WHITE)
        z.rect(cx - hw - 0.8, top + hw, 1.6, bot - top - hw - 20, WHITE)
        z.rect(cx + hw - 0.8, top + hw, 1.6, bot - top - hw - 20, WHITE)
        z.line(cx - hw, bot - 20, cx - 6, bot, 1.6, WHITE)
        z.line(cx + hw, bot - 20, cx + 6, bot, 1.6, WHITE)
        z.line(cx - hw, 115, cx - hw - 70, 140, 1.6, WHITE)
        z.line(cx + hw, 115, cx + hw + 70, 140, 1.6, WHITE)
        for yy, lab in ((62, "CABIN"), (190, "CABIN")):
            for sgn in (-1, 1):
                z.rect(cx + sgn * (hw + 2) - 4, yy, 8, 12, GREEN)
            z.text(cx - hw - 10, yy + 2, lab, GREEN, 5.5, "r")
        for yy, lab in ((92, "CARGO"), (160, "CARGO")):
            z.rect(cx - hw + 3, yy, 12, 12, GREEN)
            z.text(cx - hw - 10, yy + 2, lab, GREEN, 5.5, "r")
        z.rect(cx - 5, top + hw + 4, 10, 8, GREEN)
        z.text(cx + hw + 8, top + hw + 4, "AVIONIC", GREEN, 5.5)
        z.text(w - 6, 30, "CKPT OXY", WHITE, 5.5, "r")
        z.text(w - 6, 42, "1850 PSI", GREEN, 6.5, "r")
        z.rect(0, h - 26, w, 1.2, WHITE)
        z.text(6, h - 20, "TAT +12 C", WHITE, 6)
        z.text(w * 0.5, h - 20, "12 H 34", GREEN, 6.5, "c")
        z.text(w - 6, h - 20, "GW 62500 KG", WHITE, 6, "r")


def paint_stby(z, style):
    w, h = z.w, z.h
    z.rect(0, 0, w, h, BLACK)
    cx, cy = w / 2, h * 0.52
    z.rect(cx - 26, cy - 26, 52, 26, SKY[style])
    z.rect(cx - 26, cy, 52, 26, GROUND[style])
    z.rect(cx - 26, cy - 0.8, 52, 1.6, WHITE)
    z.rect(cx - 14, cy - 1.5, 10, 3, YELLOW)
    z.rect(cx + 4, cy - 1.5, 10, 3, YELLOW)
    z.rect(4, cy - 20, 14, 40, (0.3, 0.3, 0.32))
    z.rect(w - 18, cy - 20, 14, 40, (0.3, 0.3, 0.32))
    z.text(cx, 4, "STBY", WHITE, 6, "c")


def paint_cdu(z, style):
    w, h = z.w, z.h
    body = PANEL[style] if style == "boeing" else (0.30, 0.34, 0.39)
    z.rect(0, 0, w, h, body)
    u0, v0, u1, v1 = CDU_SCREEN
    sx, sy, sw, sh = u0 * w, v0 * h, (u1 - u0) * w, (v1 - v0) * h
    z.rect(sx - 3, sy - 3, sw + 6, sh + 6, DARK)
    z.rect(sx, sy, sw, sh, BLACK)
    if style == "boeing":
        lines = [("    POS INIT    1/3", WHITE), ("", WHITE), ("REF AIRPORT", WHITE), ("----", WHITE),
                 ("GATE", WHITE), ("24", WHITE), ("UTC     SET IRS POS", WHITE), ("1234Z   [][][][]", WHITE),
                 ("------------------", WHITE), ("<INDEX     ROUTE>", WHITE)]
    else:
        lines = [("      INIT       ", WHITE), (" CO RTE    FROM/TO", WHITE), ("[][][]    [][][]", AMBER),
                 ("ALTN/CO RTE", WHITE), ("----/------", CYAN), ("FLT NBR", WHITE), ("[][][][]", AMBER),
                 ("COST INDEX   TROPO", WHITE), ("---       36090", CYAN), ("CRZ FL/TEMP", WHITE)]
    for k, (t, col) in enumerate(lines):
        z.text(sx + 4, sy + 3 + k * (sh - 6) / len(lines), t, col, (sh - 6) / len(lines) * 0.62)
    for k in range(6):
        yy = sy + 10 + k * (sh - 20) / 5
        z.rect(2, yy - 3, sx - 5, 6, (0.18, 0.18, 0.2))
        z.rect(sx + sw + 3, yy - 3, sx - 5, 6, (0.18, 0.18, 0.2))
    key = (0.16, 0.16, 0.18)
    top = sy + sh + 10
    for r in range(2):
        for c in range(5):
            z.rect(8 + c * 29.5, top + r * 15, 25, 11, key)
            z.rect(9 + c * 29.5, top + r * 15 + 4, 23, 1.2, WHITE)
    letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
    top2 = top + 36
    for k, ch in enumerate(letters):
        r, c = divmod(k, 5)
        x0, y0 = 66 + c * 18, top2 + r * 15
        z.rect(x0, y0, 15, 12, key)
        z.text(x0 + 7.5, y0 + 3, ch, WHITE, 6, "c")
    for k, ch in enumerate("123456789.0-"):
        r, c = divmod(k, 3)
        x0, y0 = 8 + c * 18, top2 + r * 18
        z.disc(x0 + 7, y0 + 7, 7, key)
        z.text(x0 + 7, y0 + 4, ch, WHITE, 6, "c")


# ---------------------------------------------------------------------- painters: panels

def knob(z, x, y, r, style):
    z.disc(x, y, r * 1.25, (0.12, 0.12, 0.13))
    z.disc(x, y, r, (0.22, 0.22, 0.24) if style == "boeing" else (0.18, 0.2, 0.24))
    z.polar(x, y, 30, 0, r * 0.95, max(1.2, r * 0.18), WHITE)


def toggle(z, x, y, r):
    z.disc(x, y, r, (0.1, 0.1, 0.11))
    z.disc(x, y, r * 0.45, (0.7, 0.7, 0.72))
    z.line(x, y, x, y - r * 1.4, max(1.2, r * 0.4), (0.8, 0.8, 0.82))


def pushbutton(z, x, y, s, top=None, bottom=None, top_col=AMBER, bottom_col=WHITE, lit=False):
    z.rect(x - s / 2, y - s / 2, s, s, (0.06, 0.06, 0.07))
    z.frame(x - s / 2, y - s / 2, s, s, max(1.0, s * 0.06), (0.75, 0.77, 0.80))
    size = max(3.5, s * 0.22)
    if top:
        z.text(x, y - s * 0.32, top, top_col if lit else (0.25, 0.22, 0.18), size, "c")
    if bottom:
        z.text(x, y + s * 0.08, bottom, bottom_col if lit else (0.35, 0.36, 0.38), size, "c")


def annunciator(z, x, y, w, h, col, lit=False):
    dim = tuple(c * 0.25 for c in col)
    z.rect(x - w / 2, y - h / 2, w, h, col if lit else dim)
    z.frame(x - w / 2, y - h / 2, w, h, 1, (0.08, 0.08, 0.09))


def lcd(z, x, y, w, h, text, col):
    z.rect(x - w / 2, y - h / 2, w, h, (0.03, 0.03, 0.03))
    z.text(x, y - h * 0.32, text, col, h * 0.62, "c")


def paint_mip(mz, L):
    st = L.style
    mz.rect(0, 0, mz.w, mz.h, PANEL[st])
    k = mz.scale()
    seam = tuple(c * 0.7 for c in PANEL[st])
    for y in (0.48, 0.26, -0.26, -0.48):
        x, _ = mz.px(y, 0)
        mz.rect(x - 1, 0, 2, mz.h, seam)
    for name, y, zr in L.displays:
        mz.mrect(y, zr, L.du + 0.016, L.du + 0.016, (0.10, 0.10, 0.11))
        mz.mrect(y, zr, L.du - 0.012, L.du - 0.012, BLACK)
    y, zr, size = L.stby
    mz.mrect(y, zr, size + 0.012, size + 0.012, (0.10, 0.10, 0.11))
    for sgn in (1, -1):
        cx, cy = mz.px(sgn * 0.76, 0.03)
        mz.disc(cx, cy, 0.03 * k, (0.08, 0.08, 0.08))
        mz.ring(cx, cy, 0.024 * k, 1.5, WHITE)
        mz.polar(cx, cy, 60, 0, 0.02 * k, 2, WHITE)
        mz.text(cx, cy + 0.035 * k, "CLOCK" if st == "boeing" else "CHR", WHITE, 7, "c")
        for j in range(2):
            kx, ky = mz.px(sgn * (0.70 + j * 0.08), -0.075)
            knob(mz, kx, ky, 0.011 * k, st)
    gy, gz = L.gear
    gx, gzz = mz.px(gy, gz)
    mz.rect(gx - 0.03 * k, gzz - 0.06 * k, 0.06 * k, 0.12 * k, (0.2, 0.2, 0.22))
    mz.rect(gx - 0.004 * k, gzz - 0.05 * k, 0.008 * k, 0.1 * k, BLACK)
    mz.text(gx, gzz - 0.075 * k, "LANDING GEAR" if st == "boeing" else "LDG GEAR", WHITE, 7, "c")
    for j in range(3):
        lx, ly = mz.px(gy - 0.06 + 0.06 * j, gz + 0.085)
        annunciator(mz, lx, ly, 0.035 * k, 0.018 * k, GREEN, lit=True)
    ay, az = -gy, L.gear[1]
    ax, azz = mz.px(ay, az)
    if st == "boeing":
        knob(mz, ax, azz, 0.016 * k, st)
        mz.text(ax, azz - 0.035 * k, "AUTO BRAKE", WHITE, 6.5, "c")
        mz.text(ax, azz + 0.025 * k, "RTO OFF 1 2 3 MAX", WHITE, 5.5, "c")
        for j in range(4):
            lx, ly = mz.px(0.47 - j * 0.03, L.zr_top - 0.012)
            annunciator(mz, lx, ly, 0.026 * k, 0.012 * k, AMBER)
    else:
        for j, t in enumerate(("LO", "MED", "MAX")):
            bx, by = mz.px(ay + 0.045 - j * 0.045, az)
            pushbutton(mz, bx, by, 0.034 * k, "DECEL", t, GREEN, WHITE)
        mz.text(ax, azz - 0.04 * k, "AUTO/BRK", WHITE, 6.5, "c")
        bx, by = mz.px(ay, az - 0.075)
        mz.rect(bx - 0.035 * k, by - 0.02 * k, 0.07 * k, 0.04 * k, BLACK)
        mz.text(bx, by - 0.008 * k, "BRAKES", WHITE, 5, "c")
    label = WHITE
    for name, y, zr in L.displays:
        if name in ("pfd", "nd") and st == "airbus":
            bx, by = mz.px(y, zr - L.du / 2 - 0.02)
            mz.text(bx, by, "PFD/ND XFR" if name == "pfd" else "", label, 5.5, "c")


def paint_mcp(mz, L):
    st = L.style
    k = mz.scale()
    mz.rect(0, 0, mz.w, mz.h, (0.08, 0.08, 0.09))
    face = (0.24, 0.25, 0.27) if st == "boeing" else (0.25, 0.29, 0.34)
    x0, _ = mz.px(0.42, 0)
    x1, _ = mz.px(-0.42, 0)
    mz.rect(x0, 4, x1 - x0, mz.h - 8, face)
    h = mz.h
    if st == "boeing":
        items = [(0.36, "COURSE", "245"), (0.22, "IAS/MACH", "250"), (0.07, "HEADING", "240"),
                 (-0.10, "ALTITUDE", "18000"), (-0.24, "VERT SPEED", ""), (-0.36, "COURSE", "245")]
        for y, lab, val in items:
            x, _ = mz.px(y, 0)
            mz.text(x, 7, lab, WHITE, 6, "c")
            lcd(mz, x, h * 0.42, 0.075 * k, 0.03 * k, val, (1.0, 0.75, 0.35))
            knob(mz, x, h * 0.78, 0.014 * k, st)
        for j, t in enumerate(("N1", "SPEED", "VNAV", "LNAV", "VOR LOC", "APP", "ALT HLD", "V/S", "CMD A", "CMD B")):
            x, _ = mz.px(0.30 - j * 0.062, 0)
            mz.rect(x - 0.022 * k, h * 0.62, 0.044 * k, 0.022 * k, (0.75, 0.76, 0.75))
            mz.text(x, h * 0.62 + 0.007 * k, t, BLACK, 4.6, "c")
    else:
        items = [(0.30, "SPD  MACH", "250"), (0.12, "HDG  TRK", "240"), (-0.10, "ALT", "18000"), (-0.26, "V/S  FPA", "-----")]
        for y, lab, val in items:
            x, _ = mz.px(y, 0)
            mz.text(x, 7, lab, WHITE, 6, "c")
            lcd(mz, x, h * 0.40, 0.08 * k, 0.028 * k, val, (1.0, 0.72, 0.30))
            knob(mz, x, h * 0.76, 0.016 * k, st)
        for j, t in enumerate(("LOC", "AP1", "AP2", "A/THR", "EXPED", "APPR")):
            x, _ = mz.px(0.22 - j * 0.07, 0)
            mz.rect(x - 0.02 * k, h * 0.64, 0.04 * k, 0.026 * k, (0.08, 0.08, 0.09))
            mz.rect(x - 0.012 * k, h * 0.64 + 0.004 * k, 0.024 * k, 0.003 * k, (0.12, 0.3, 0.14))
            mz.text(x, h * 0.64 + 0.012 * k, t, WHITE, 4.6, "c")
    # EFIS control panels and master warning / caution lights, both sides
    for sgn in (1, -1):
        xa, _ = mz.px(sgn * 0.47, 0)
        xb, _ = mz.px(sgn * 0.66, 0)
        lo, hi = min(xa, xb), max(xa, xb)
        mz.rect(lo, 4, hi - lo, mz.h - 8, face)
        for j in range(2):
            x, _ = mz.px(sgn * (0.51 + j * 0.10), 0)
            knob(mz, x, h * 0.6, 0.016 * k, st)
        x, _ = mz.px(sgn * 0.565, 0)
        if st == "boeing":
            mz.text(x, 8, "MINS   BARO", WHITE, 5, "c")
            for j, t in enumerate(("WXR", "STA", "WPT", "ARPT", "DATA", "POS")):
                bx, _ = mz.px(sgn * 0.49 + (j - 2.5) * 0.024 * -sgn + sgn * 0.075, 0)
                mz.rect(bx - 0.009 * k, h * 0.2, 0.018 * k, 0.012 * k, (0.75, 0.76, 0.75))
        else:
            lcd(mz, x, h * 0.25, 0.06 * k, 0.022 * k, "1013", (1.0, 0.72, 0.30))
            for j, t in enumerate(("CSTR", "WPT", "VORD", "NDB", "ARPT")):
                bx, _ = mz.px(sgn * 0.565 + (j - 2) * 0.028, 0)
                mz.rect(bx - 0.011 * k, h * 0.8, 0.022 * k, 0.016 * k, (0.08, 0.08, 0.09))
                mz.text(bx, h * 0.8 + 0.004 * k, t, WHITE, 3.8, "c")
        for j, (t, col) in enumerate((("FIRE" if st == "boeing" else "MASTER WARN", RED), ("MASTER CAUTION" if st == "boeing" else "MASTER CAUT", AMBER))):
            x, _ = mz.px(sgn * (0.71 + j * 0.06), 0)
            mz.rect(x - 0.024 * k, h * 0.25, 0.048 * k, 0.045 * k, tuple(c * 0.35 for c in col))
            mz.frame(x - 0.024 * k, h * 0.25, 0.048 * k, 0.045 * k, 1.2, (0.05, 0.05, 0.05))
            mz.text(x, h * 0.25 + 0.016 * k, t.split()[0], (0.95, 0.9, 0.85), 4.6, "c")


OVERHEAD_TITLES = {
    "boeing": ["FLT CONTROL", "NAV", "FUEL", "ELECTRICAL", "APU", "IRS", "WIPER", "LIGHTS", "ENGINE", "ANTI-ICE",
               "HYDRAULIC", "AIR COND", "BLEED", "PRESS", "DOORS", "PASS SIGNS", "EXT LIGHTS", "IGNITION"],
    "airbus": ["ADIRS", "FLT CTL", "EVAC", "EMER ELEC", "GPWS", "RCDR", "OXYGEN", "CALLS", "FIRE", "HYD", "FUEL",
               "ELEC", "AIR COND", "ANTI ICE", "CABIN PRESS", "EXT LT", "APU", "SIGNS", "INT LT", "WIPER"],
}


def paint_overhead(mz, L):
    st = L.style
    k = mz.scale()
    mz.rect(0, 0, mz.w, mz.h, PANEL[st])
    rng = np.random.default_rng(7 if st == "boeing" else 11)
    seam = tuple(c * 0.65 for c in PANEL[st])
    cols = 4 if st == "boeing" else 5
    rows = [0.0, 0.17, 0.36, 0.55, 0.74, 0.95]
    titles = iter(OVERHEAD_TITLES[st] * 3)
    width = mz.w / cols
    for r0, r1 in zip(rows, rows[1:]):
        _, y0 = mz.px(0, r0)
        _, y1 = mz.px(0, r1)
        mz.rect(0, y0, mz.w, 2, seam)
        for c in range(cols):
            x0 = c * width
            mz.rect(x0, y0, 2, y1 - y0, seam)
            mz.text(x0 + width / 2, y0 + 5, next(titles), WHITE, 6, "c")
            n = 3 if st == "boeing" else 3
            for j in range(n):
                for i in range(2):
                    cx = x0 + width * (j + 0.5) / n
                    cy = y0 + 20 + (y1 - y0 - 26) * (i + 0.5) / 2
                    pick = rng.random()
                    if st == "boeing":
                        if pick < 0.45:
                            toggle(mz, cx, cy, 0.008 * k)
                        elif pick < 0.62:
                            knob(mz, cx, cy, 0.011 * k, st)
                        elif pick < 0.82:
                            annunciator(mz, cx, cy, 0.035 * k, 0.014 * k, AMBER if rng.random() < 0.5 else CYAN)
                        else:
                            mz.disc(cx, cy, 0.017 * k, BLACK)
                            mz.arc(cx, cy, 0.013 * k, 1.2, -120, 120, WHITE)
                            mz.polar(cx, cy, rng.uniform(-90, 60), 0, 0.012 * k, 1.4, WHITE)
                    else:
                        if pick < 0.75:
                            lit = rng.random() < 0.12
                            pushbutton(mz, cx, cy, 0.03 * k, "FAULT", "ON" if lit else "OFF", AMBER,
                                       CYAN if lit else WHITE, lit=lit)
                        elif pick < 0.9:
                            knob(mz, cx, cy, 0.011 * k, st)
                        else:
                            toggle(mz, cx, cy, 0.008 * k)
    if st == "airbus":   # engine and APU fire pushbutton-handles live on the A320 overhead
        for j, t in enumerate(("ENG 1", "APU", "ENG 2")):
            cx, cy = mz.px(0.18 - j * 0.18, 0.465)
            mz.rect(cx - 0.035 * k, cy - 0.018 * k, 0.07 * k, 0.036 * k, (0.45, 0.06, 0.05))
            mz.text(cx, cy - 0.006 * k, t, WHITE, 5.5, "c")


def paint_ped(mz, L):
    st = L.style
    k = mz.scale()
    mz.rect(0, 0, mz.w, mz.h, PANEL[st])

    def panel_box(v0, v1, title=None):
        _, y0 = mz.px(0, v0)
        _, y1 = mz.px(0, v1)
        mz.rect(0, y0, mz.w, 2, tuple(c * 0.65 for c in PANEL[st]))
        if title:
            mz.text(mz.w / 2, y0 + 4, title, WHITE, 5.5, "c")
        return y0, y1

    def radio(v, title, a, b, col):
        y0, y1 = panel_box(v, v + 0.085, title)
        for j, t in enumerate((a, b)):
            x = mz.w * (0.3 + 0.4 * j)
            lcd(mz, x, y0 + 0.045 * k, 0.06 * k, 0.018 * k, t, col)
        knob(mz, mz.w * 0.5, y0 + 0.068 * k, 0.009 * k, st)

    if st == "boeing":
        v = L.ped_seg_range(2)[0] + 0.01
        y0, _ = panel_box(v, v + 0.11, "FIRE")
        for j, t in enumerate(("1", "APU", "2")):
            x = mz.w * (0.2 + 0.3 * j)
            mz.rect(x - 0.025 * k, y0 + 0.035 * k, 0.05 * k, 0.045 * k, (0.12, 0.12, 0.12))
            mz.rect(x - 0.02 * k, y0 + 0.04 * k, 0.04 * k, 0.016 * k, (0.5, 0.08, 0.06))
            mz.text(x, y0 + 0.088 * k, t, WHITE, 5, "c")
        radio(v + 0.11, "VHF COMM 1", "118.30", "121.90", (1.0, 0.75, 0.35))
        radio(v + 0.195, "VHF NAV 1", "110.30", "113.70", (1.0, 0.75, 0.35))
        y0, _ = panel_box(v + 0.28, v + 0.36, "AUDIO")
        for j in range(7):
            for i in range(2):
                mz.rect(mz.w * 0.1 + j * mz.w * 0.115, y0 + 0.02 * k + i * 0.022 * k, mz.w * 0.08, 0.014 * k, (0.18, 0.18, 0.2))
        radio(v + 0.36, "ATC", "2000", "STBY", (1.0, 0.75, 0.35))
    else:
        v = L.ped_seg_range(1)[0]
        y0, _ = panel_box(v, v + 0.13, "ECAM")
        for j, t in enumerate(("ENG", "BLEED", "PRESS", "ELEC", "HYD", "FUEL", "APU", "COND", "DOOR", "WHEEL", "F/CTL", "ALL")):
            r, c = divmod(j, 6)
            x = mz.w * (0.1 + 0.16 * c)
            yy = y0 + 0.04 * k + r * 0.03 * k
            mz.rect(x - 0.012 * k, yy, 0.024 * k, 0.018 * k, (0.08, 0.08, 0.09))
            mz.text(x, yy + 0.005 * k, t, WHITE, 3.6, "c")
        v2 = L.ped_seg_range(2)[0] + 0.32
        y0, _ = panel_box(v2, v2 + 0.09, "ENG")
        for j, t in enumerate(("1", "2")):
            x = mz.w * (0.3 + 0.4 * j)
            mz.rect(x - 0.008 * k, y0 + 0.02 * k, 0.016 * k, 0.05 * k, BLACK)
            mz.text(x, y0 + 0.075 * k, "MASTER " + t, WHITE, 4.2, "c")
        knob(mz, mz.w * 0.5, y0 + 0.045 * k, 0.014 * k, st)
        radio(v2 + 0.09, "RMP 1", "118.300", "121.900", GREEN)
        y0, _ = panel_box(v2 + 0.175, v2 + 0.24, "ACP")
        for j in range(6):
            mz.rect(mz.w * 0.08 + j * mz.w * 0.15, y0 + 0.02 * k, mz.w * 0.1, 0.03 * k, (0.08, 0.08, 0.09))
        y0, _ = panel_box(v2 + 0.24, v2 + 0.30, "PARK BRK")
        mz.text(mz.w * 0.5, y0 + 0.03 * k, "ON  OFF", WHITE, 4.5, "c")


def paint_quad(mz, L):
    st = L.style
    k = mz.scale()
    mz.rect(0, 0, mz.w, mz.h, (0.2, 0.2, 0.22) if st == "boeing" else (0.26, 0.29, 0.33))
    for y in (0.035, -0.035):
        x, _ = mz.px(y, 0)
        mz.rect(x - 0.006 * k, mz.h * 0.08, 0.012 * k, mz.h * 0.84, BLACK)
    if st == "boeing":
        for y, labels in ((0.09, ("DOWN", "ARMED", "", "FLT", "", "UP")),
                          (-0.09, ("UP", "1", "2", "5", "10", "15", "25", "30", "40"))):
            x, _ = mz.px(y, 0)
            mz.rect(x - 0.004 * k, mz.h * 0.1, 0.008 * k, mz.h * 0.8, BLACK)
            for j, t in enumerate(labels):
                yy = mz.h * (0.12 + 0.76 * j / (len(labels) - 1))
                mz.text(x + (0.012 if y < 0 else -0.012) * k, yy - 2, t, WHITE, 4.0, "l" if y < 0 else "r")
    else:
        x, _ = mz.px(0, 0)
        for j, t in enumerate(("TOGA", "FLX MCT", "CL", "", "0", "REV")):
            yy = mz.h * (0.12 + 0.15 * j)
            mz.rect(mz.w * 0.12, yy, mz.w * 0.76, 1.2, WHITE)
            mz.text(mz.w * 0.06, yy - 6, t, WHITE, 4.0, "l")


def paint_side(mz, L):
    st = L.style
    k = mz.scale()
    mz.rect(0, 0, mz.w, mz.h, PANEL[st])
    x, y = mz.px(L.side_y + 0.12, L.sE - 0.40)
    mz.rect(x - 0.06 * k, y - 0.05 * k, 0.12 * k, 0.10 * k, (0.12, 0.12, 0.13))
    mz.text(x, y + 0.055 * k, "OXYGEN", WHITE, 5, "c")
    if st == "airbus":
        x, y = mz.px(L.side_y + 0.12, L.sE - 0.20)
        mz.disc(x, y, 0.045 * k, (0.12, 0.13, 0.15))
        mz.text(x, y - 0.065 * k, "PRIORITY", WHITE, 4.5, "c")
        annunciator(mz, x - 0.03 * k, y + 0.065 * k, 0.025 * k, 0.012 * k, GREEN)
        annunciator(mz, x + 0.03 * k, y + 0.065 * k, 0.025 * k, 0.012 * k, RED)
    x, y = mz.px(L.side_y + 0.12, L.sE + 0.12)
    for j in range(3):
        knob(mz, x + (j - 1) * 0.04 * k, y, 0.01 * k, st)
    mz.text(x, y + 0.025 * k, "LIGHTS", WHITE, 4.5, "c")


def atlas(fus, sp):
    """Paint the flight-deck atlas. Returns (rgb image [h, w, 3] uint8, zones, height, layout)."""
    L = Layout(fus, sp)
    zones, height = pack(ZONE_SIZES, ATLAS_WIDTH)
    c = Canvas(ATLAS_WIDTH, height, background=(0.5, 0.5, 0.5))
    st = L.style
    paint_pfd(Zone(c, zones["pfd"]), st)
    paint_nd(Zone(c, zones["nd"]), st)
    paint_eng(Zone(c, zones["eng"]), st)
    paint_sys(Zone(c, zones["sys"]), st)
    paint_stby(Zone(c, zones["stby"]), st)
    paint_cdu(Zone(c, zones["cdu"]), st)
    paint_mip(MetricZone(c, zones["mip"], (L.Ym, -L.Ym), (L.zr_top, L.zr_bot)), L)
    paint_mcp(MetricZone(c, zones["mcp"], (L.Ym, -L.Ym), (L.face_h, 0.0)), L)
    paint_overhead(MetricZone(c, zones["overhead"], (L.o_hw, -L.o_hw), (0.0, 1.0)), L)
    paint_ped(MetricZone(c, zones["ped"], (L.ped_hw, -L.ped_hw), (0.0, L.ped_len)), L)
    paint_quad(MetricZone(c, zones["quad"], (0.13, -0.13), (0.0, 1.0)), L)
    paint_side(MetricZone(c, zones["side"], (L.side_y + 0.32, L.side_y), L.side_s), L)
    return c.result(), zones, height, L


# ====================================================================== geometry helpers

def quad(buf, corners, ref, uvs=None):
    buf.add(np.asarray(corners, dtype=float), [(0, 1, 2), (0, 2, 3)], ref, uvs)


def face_quad(buf, centre, normal, up, w, h, uvs=None, lift=0.0):
    """Rectangle facing `normal`; corners TL, TR, BR, BL as seen by the viewer."""
    normal, up = normalize(normal), normalize(up)
    left = np.cross(up, normal)
    c = np.asarray(centre, dtype=float) + normal * lift
    pts = [c + left * w / 2 + up * h / 2, c - left * w / 2 + up * h / 2,
           c - left * w / 2 - up * h / 2, c + left * w / 2 - up * h / 2]
    quad(buf, pts, normal, uvs)


def zone_uvs(T, name, rect=(0.0, 0.0, 1.0, 1.0)):
    """UVs for TL, TR, BR, BL of a sub-rectangle (fractions) of an atlas zone."""
    zones, h = T
    u0, v0, u1, v1 = rect
    return [uv_of(zones, h, name, u0, v0), uv_of(zones, h, name, u1, v0),
            uv_of(zones, h, name, u1, v1), uv_of(zones, h, name, u0, v1)]


def mapped_surface(buf, point, us, vs, uv=None, ref=None, keep=None):
    """Grid surface: point(u, v) -> xyz over us x vs; uv(u, v) -> atlas uv; keep(u, v) cell filter."""
    P = np.array([[point(u, v) for u in us] for v in vs])
    UV = None if uv is None else np.array([[uv(u, v) for u in us] for v in vs])
    mask = None
    if keep is not None:
        mask = np.array([[not keep(0.5 * (us[j] + us[j + 1]), 0.5 * (vs[i] + vs[i + 1]))
                          for j in range(len(us) - 1)] for i in range(len(vs) - 1)])
    R = np.broadcast_to(np.asarray(ref, dtype=float), P.shape)
    grid(buf, P, R, closed=False, mask=mask, uv=UV)
    return P


def tube(buf, pts, radius, segments=10):
    """Circular tube along a polyline (end caps included)."""
    pts = np.asarray(pts, dtype=float)
    for a, b in zip(pts, pts[1:]):
        cylinder(buf, a, b, radius, segments, caps=True)


def rot_x(a):
    """Axes for box(): rotated about the lateral (y) axis by `a` (positive = top leans aft)."""
    c, s = math.cos(a), math.sin(a)
    return [[c, 0, -s], [0, 1, 0], [s, 0, c]]


def lever(buf_arm, buf_knob, pivot, length, angle, knob_size, arm_r=0.007):
    """A lever from `pivot` leaning aft by `angle` (rad), with a box knob at the end."""
    pivot = np.asarray(pivot, dtype=float)
    tip = pivot + length * np.array([math.sin(angle), 0, math.cos(angle)])
    cylinder(buf_arm, pivot, tip, arm_r, 8)
    box(buf_knob, tip, knob_size, axes=rot_x(angle))
    return tip


# ====================================================================== geometry

def build(fus, sp, interior):
    """Add the flight deck (part 'FlightDeck', with a 'FlightDeckLight' point light) to the interior."""
    L = Layout(fus, sp)
    T = pack(ZONE_SIZES, ATLAS_WIDTH)
    fd = Part("FlightDeck")
    interior.children.append(fd)
    b = L.boeing
    sE, yE, zE, zf = L.sE, L.yE, L.zE, L.zf

    build_main_panel(fd, L, T)
    build_glareshield(fd, L, T)
    build_pedestal(fd, L, T)
    build_side_consoles(fd, L, T)
    build_overhead(fd, L, T)
    for side in (1, -1):
        build_pilot_seat(fd.buf("FD_Seat"), fd.buf("FD_Grip"), np.array([sE, side * yE, zf]))
        build_rudder_pedals(fd, np.array([sE - 0.95, side * yE, zf]))
        if b:
            build_yoke(fd, np.array([sE, side * yE, zf]), L)
        else:
            build_sidestick(fd, L, side)
            build_table(fd, L, side)
    # observer seat behind the captain, facing forward
    sfd = sp["flight_deck_bulkhead"]
    build_pilot_seat(fd.buf("FD_Seat"), fd.buf("FD_Grip"), np.array([sfd - 0.62, 0.72, zf]), scale=0.92)
    # dome light
    s_l = sE + 0.80   # behind the overhead panel
    zr = L.roof_z(s_l, 0.0)
    cylinder(fd.buf("FD_DomeLight"), [s_l, 0, zr - 0.005], [s_l, 0, zr - 0.02], 0.09, 16)
    fd.children.append(Part("FlightDeckLight", [s_l, 0.0, zr - 0.25], extras={
        "light": {"color": (1.0, 0.94, 0.85), "intensity": 0.6, "range": 3.5}}))


def build_main_panel(fd, L, T):
    panel_buf, faces, screens = fd.buf("FD_Panel"), fd.buf("FD_Faces"), fd.buf("FD_Screens")
    n, up = L.panel_normal, L.panel_up
    zones, h = T

    def keep(y, zr):
        return zr >= L.zr_side or abs(y) <= L.hc

    def point(y, zr):
        p = L.panel_point(y, zr)
        lim = L.wall_y(p[0], p[2]) + 0.04
        p[1] = max(-lim, min(lim, y))
        return p

    def uv(y, zr):
        return uv_of(zones, h, "mip", (L.Ym - y) / (2 * L.Ym), (L.zr_top - zr) / (L.zr_top - L.zr_bot))

    ys = np.concatenate([np.linspace(L.Ym, L.hc, 9), np.linspace(L.hc, -L.hc, 6)[1:-1], np.linspace(-L.hc, -L.Ym, 9)])
    zs = np.array(sorted({L.zr_top, L.zr_side, L.zr_bot, 0.5 * (L.zr_top + L.zr_side), 0.5 * (L.zr_side + L.zr_bot)}, reverse=True))
    mapped_surface(faces, point, ys, zs, uv, n, keep)

    for name, y, zr in L.displays:
        s = L.du - 0.012
        face_quad(screens, L.panel_point(y, zr), n, up, s, s, zone_uvs(T, name), lift=0.003)
    y, zr, size = L.stby
    face_quad(screens, L.panel_point(y, zr), n, up, size, size, zone_uvs(T, "stby"), lift=0.003)

    # bezels stand proud of the panel a little
    for name, y, zr in L.displays:
        c = L.panel_point(y, zr) - n * 0.006
        box(fd.buf("FD_Dark"), c, (0.014, L.du + 0.016, L.du + 0.016), axes=rot_x(-L.tilt))

    # footwell under the side panels and the knee panels' undersides
    for sgn in (1, -1):
        y_in, y_out = sgn * L.hc, sgn * L.Ym
        p_in, p_out = L.panel_point(y_in, L.zr_side), L.panel_point(y_out, L.zr_side)
        depth = 0.32

        def under(u, v, sgn=sgn):
            p = L.panel_point(u, L.zr_side) - np.array([v, 0, 0])
            lim = L.wall_y(p[0], p[2]) + 0.04
            p[1] = max(-lim, min(lim, p[1]))
            return p
        mapped_surface(fd.buf("FD_Dark"), under, np.linspace(y_in, y_out, 6), [0.0, depth], None, [0, 0, -1.0])
        s_kick = L.panel_point(0, L.zr_side)[0] - depth

        def kick(u, zz):
            p = np.array([s_kick, u, zz])
            lim = L.wall_y(p[0], p[2]) + 0.04
            p[1] = max(-lim, min(lim, p[1]))
            return p
        mapped_surface(fd.buf("FD_Dark"), kick, np.linspace(y_in, y_out, 6), np.linspace(p_in[2], L.zf, 4), None, [1.0, 0, 0])
    # centre panel sides (between the knee recesses)
    for sgn in (1, -1):
        a = L.panel_point(sgn * L.hc, L.zr_side)
        c = L.panel_point(sgn * L.hc, L.zr_bot)
        quad(panel_buf, [a, a - [0.32, 0, 0], c - [0.32, 0, 0], c], [0, sgn, 0])

    # gear lever (and Boeing's sits in the same place)
    gy, gz = L.gear
    base = L.panel_point(gy, gz)
    tip = base + n * 0.07 + up * 0.03
    cylinder(fd.buf("Metal"), base, tip, 0.006, 8)
    cylinder(fd.buf("FD_Handle"), tip - [0, 0.018, 0], tip + [0, 0.018, 0], 0.022, 14)


def build_glareshield(fd, L, T):
    zones, h = T
    dark, faces = fd.buf("FD_Dark"), fd.buf("FD_Faces")
    zg, zb = L.z_g, L.z_g - L.face_h
    s_lip = L.s_lip

    def front_s(y, z):
        """Most forward station at which (y, z) is still inside the flight deck."""
        lo, hi = L.fus.sp["lining_start"] + 0.01, s_lip
        if L.wall_y(lo, z) >= abs(y):
            return lo
        for _ in range(30):
            mid = 0.5 * (lo + hi)
            if L.wall_y(mid, z) >= abs(y):
                hi = mid
            else:
                lo = mid
        return hi

    y_lim = L.wall_y(s_lip, zg) - 0.003
    ys = np.linspace(y_lim, -y_lim, 25)
    for z, ref in ((zg, [0, 0, 1.0]), (zb, [0, 0, -1.0])):
        rows = []
        for y in ys:
            s_front = front_s(y, z) if z == zg else max(front_s(y, z), L.panel_point(0, L.zr_top)[0] - 0.02)
            rows.append([np.array([s_front, y, z]), np.array([s_lip, y, z])])
        rows = np.array(rows)
        grid(dark, rows, np.broadcast_to(np.asarray(ref), rows.shape), closed=False)

    def point(y, z):
        lim = L.wall_y(s_lip, z) - 0.003
        return np.array([s_lip, max(-lim, min(lim, y)), z])

    def uv(y, z):
        return uv_of(zones, h, "mcp", (L.Ym - y) / (2 * L.Ym), (zg - z) / L.face_h)
    mapped_surface(faces, point, np.linspace(L.Ym, -L.Ym, 30), [zg, zb], uv, [1.0, 0, 0])
    # rounded lip
    cylinder(dark, [s_lip, y_lim, zg - 0.01], [s_lip, -y_lim, zg - 0.01], 0.012, 8)


def build_pedestal(fd, L, T):
    zones, h = T
    panel_buf, faces, dark = fd.buf("FD_Panel"), fd.buf("FD_Faces"), fd.buf("FD_Dark")
    hw = L.ped_hw
    n_v = 40
    vs = np.linspace(0, L.ped_len, n_v)

    def top(y, v):
        s, z, _ = L.ped_point(v)
        return np.array([s, y, z])

    def uv(y, v):
        return uv_of(zones, h, "ped", (hw - y) / (2 * hw), v / L.ped_len)
    ys = np.linspace(hw, -hw, 4)
    mapped_surface(faces, top, ys, vs, uv, [0, 0, 1.0])
    for sgn in (1, -1):
        rows = np.array([[top(sgn * hw, v), top(sgn * hw, v) * [1, 1, 0] + [0, 0, L.zf]] for v in vs])
        grid(panel_buf, rows, np.broadcast_to(np.array([0, sgn, 0.0]), rows.shape), closed=False)
    end = top(0, L.ped_len)
    quad(panel_buf, [[end[0], hw, end[2]], [end[0], -hw, end[2]], [end[0], -hw, L.zf], [end[0], hw, L.zf]], [1.0, 0, 0])
    s0 = L.s_cb
    quad(panel_buf, [[s0, hw, L.z_cb], [s0, -hw, L.z_cb], [s0, -hw, L.zf], [s0, hw, L.zf]], [-1.0, 0, 0])

    # CDUs / MCDUs
    v0, v1 = L.ped_seg_range(L.cdu_seg)
    vm = 0.5 * (v0 + v1)
    s, z, slope = L.ped_point(vm)
    normal = np.array([-math.sin(slope), 0, math.cos(slope)])
    up = np.array([-math.cos(slope), 0, -math.sin(slope)])     # 'up' on the face = forward
    length = min(0.235, v1 - v0 - 0.005)
    for sgn in (1, -1):
        c = np.array([s, sgn * 0.083, z])
        box(dark, c - normal * 0.01, (length + 0.01, 0.155, 0.024), axes=rot_x(-slope))
        face_quad(faces, c, normal, up, 0.145, length, zone_uvs(T, "cdu"), lift=0.0035)
        screen = c + up * (length * (0.5 - (CDU_SCREEN[1] + CDU_SCREEN[3]) / 2))
        face_quad(fd.buf("FD_Screens"), screen, normal, up, 0.145 * (CDU_SCREEN[2] - CDU_SCREEN[0]),
                  length * (CDU_SCREEN[3] - CDU_SCREEN[1]), zone_uvs(T, "cdu", CDU_SCREEN), lift=0.005)

    if L.boeing:
        build_control_stand(fd, L, T)
    else:
        build_thrust_housing(fd, L, T)


def housing(fd, L, T, v0, v1, half_w, height_fn):
    """Raised housing on the pedestal top: textured top (zone 'quad') and plain sides."""
    zones, h = T
    vs = np.linspace(v0, v1, 14)

    def top(y, v):
        s, z, _ = L.ped_point(v)
        return np.array([s, y, z + height_fn((v - v0) / (v1 - v0))])

    def uv(y, v):
        return uv_of(zones, h, "quad", (0.13 - y) / 0.26, (v - v0) / (v1 - v0))
    mapped_surface(fd.buf("FD_Faces"), top, np.linspace(half_w, -half_w, 3), vs, uv, [0, 0, 1.0])
    for sgn in (1, -1):
        rows = np.array([[top(sgn * half_w, v), top(sgn * half_w, v) - [0, 0, height_fn((v - v0) / (v1 - v0)) + 0.01]] for v in vs])
        grid(fd.buf("FD_Panel"), rows, np.broadcast_to(np.array([0, sgn, 0.0]), rows.shape), closed=False)
    for v, ref in ((v0, -1.0), (v1, 1.0)):
        a, bb = top(half_w, v), top(-half_w, v)
        drop = height_fn(0.0 if v == v0 else 1.0) + 0.01
        quad(fd.buf("FD_Panel"), [a, bb, bb - [0, 0, drop], a - [0, 0, drop]], [ref, 0, 0])
    return top


def trim_wheel(fd, centre, radius, width, stripes=12):
    axis = np.array([0, width / 2, 0])
    cylinder(fd.buf("FD_Grip"), centre - axis, centre + axis, radius, 32)
    for k in range(stripes):
        a = 2 * math.pi * k / stripes
        p = centre + radius * np.array([math.sin(a), 0, math.cos(a)])
        box(fd.buf("FD_Handle"), p, (0.012, width + 0.002, 0.012), axes=rot_x(a))


def build_control_stand(fd, L, T):
    """Boeing control stand: arched housing, thrust levers, speed brake, flap lever, trim wheels."""
    v0, v1 = 0.02, 0.40
    half_w = 0.125
    top = housing(fd, L, T, v0, v1, half_w, lambda t: 0.07 + 0.08 * math.sin(math.pi * min(max(t, 0), 1) * 0.85 + 0.25))
    arm, grip, handle = fd.buf("Metal"), fd.buf("FD_Grip"), fd.buf("FD_Handle")
    piv = top(0, 0.24) - [0, 0, 0.10]
    for y in (0.035, -0.035):   # thrust levers at idle
        tip = lever(arm, grip, piv + [0, y, 0], 0.33, 12 * D, (0.03, 0.03, 0.03))
        cylinder(grip, tip - [0, 0.03, 0], tip + [0, 0.03, 0], 0.017, 12)
    lever(arm, handle, top(0.09, 0.08) - [0, 0, 0.08], 0.17, 40 * D, (0.05, 0.03, 0.025))       # speed brake (down)
    lever(arm, handle, top(-0.09, 0.10) - [0, 0, 0.08], 0.16, 35 * D, (0.035, 0.05, 0.035))     # flap lever (up)
    lever(arm, fd.buf("FD_Grip"), top(0.05, 0.36) - [0, 0, 0.02], 0.06, 0.0, (0.015, 0.035, 0.02))  # start levers
    lever(arm, fd.buf("FD_Grip"), top(-0.05, 0.36) - [0, 0, 0.02], 0.06, 0.0, (0.015, 0.035, 0.02))
    s, z, _ = L.ped_point(0.24)
    for sgn in (1, -1):
        trim_wheel(fd, np.array([s, sgn * (half_w + 0.025), z + 0.03]), 0.15, 0.035)
    # parking brake lever, captain's side of the stand
    lever(arm, fd.buf("FD_Handle"), top(half_w - 0.01, 0.03) + [0, 0.02, -0.04], 0.09, -20 * D, (0.02, 0.05, 0.02))


def build_thrust_housing(fd, L, T):
    """Airbus pedestal: gated thrust-lever housing, short levers, trim wheels, speed brake, flaps."""
    v0 = L.ped_seg_range(2)[0] + 0.0
    v1 = v0 + 0.30
    half_w = 0.11
    top = housing(fd, L, T, v0, v1, half_w, lambda t: 0.05 + 0.02 * math.sin(math.pi * min(max(t, 0), 1)))
    arm, grip = fd.buf("Metal"), fd.buf("FD_Grip")
    piv = top(0, v0 + 0.17) - [0, 0, 0.06]
    for y in (0.04, -0.04):     # thrust levers at idle ('0')
        tip = lever(arm, grip, piv + [0, y, 0], 0.17, 8 * D, (0.06, 0.05, 0.05), arm_r=0.01)
        box(fd.buf("FD_Handle"), tip + [0, 0, 0.027], (0.05, 0.051, 0.006), axes=rot_x(8 * D))
    s, z, _ = L.ped_point(v0 + 0.15)
    for sgn in (1, -1):
        trim_wheel(fd, np.array([s, sgn * (half_w + 0.03), z + 0.0]), 0.10, 0.03, stripes=10)
    lever(arm, fd.buf("FD_Handle"), top(0.15, v0 + 0.03) - [0, 0, 0.04], 0.10, 30 * D, (0.04, 0.025, 0.02))    # speed brake
    lever(arm, fd.buf("FD_Handle"), top(-0.15, v0 + 0.05) - [0, 0, 0.04], 0.10, 25 * D, (0.03, 0.045, 0.03))   # flaps
    s, z, _ = L.ped_point(v1 + 0.04)
    for y in (0.055, -0.055):   # engine master switches
        box(grip, [s, y, z + 0.02], (0.015, 0.012, 0.04))
    s, z, _ = L.ped_point(v1 + 0.33)
    cylinder(grip, [s, 0, z], [s, 0, z + 0.025], 0.022, 14)   # PARK BRK knob


def build_side_consoles(fd, L, T):
    zones, h = T
    s0, s1 = L.side_s
    z = L.side_z
    for sgn in (1, -1):
        def top(u, v, sgn=sgn):
            lim = L.wall_y(v, z) + 0.04
            return np.array([v, sgn * min(u, lim), z])

        def uv(u, v):
            return uv_of(zones, h, "side", (L.side_y + 0.32 - u) / 0.32, (v - s0) / (s1 - s0))
        us = np.linspace(L.side_y + 0.32, L.side_y, 4) if sgn > 0 else np.linspace(L.side_y, L.side_y + 0.32, 4)
        vs = np.linspace(s0, s1, 10)
        keep = (lambda u, v: L.wall_y(v, z) + 0.04 > L.side_y + 0.005)
        mapped_surface(fd.buf("FD_Faces"), top, us, vs, uv, [0, 0, 1.0], keep)
        y_in = sgn * L.side_y
        quad(fd.buf("FD_Panel"), [[s0, y_in, z], [s1, y_in, z], [s1, y_in, L.zf], [s0, y_in, L.zf]], [0, -sgn, 0])
        lim0 = L.wall_y(s0, z) + 0.04
        if lim0 > L.side_y:
            quad(fd.buf("FD_Panel"), [[s0, y_in, z], [s0, sgn * lim0, z], [s0, sgn * lim0, L.zf], [s0, y_in, L.zf]], [-1.0, 0, 0])
        lim1 = L.wall_y(s1, z) + 0.04
        quad(fd.buf("FD_Panel"), [[s1, y_in, z], [s1, sgn * lim1, z], [s1, sgn * lim1, L.zf], [s1, y_in, L.zf]], [1.0, 0, 0])
        # steering tiller: Boeing on the captain's side only, Airbus on both
        if not L.boeing or sgn > 0:
            if L.boeing:
                c = np.array([L.sE - 0.30, sgn * (L.side_y + 0.10), z + 0.02])
                cylinder(fd.buf("FD_Grip"), c - [0, 0, 0.02], c, 0.06, 18)
                cylinder(fd.buf("FD_Grip"), c + [0.04, 0, 0], c + [0.04, 0, 0.05], 0.011, 8)
            else:
                c = np.array([L.sE - 0.47, sgn * (L.side_y + 0.12), z + 0.015])
                cylinder(fd.buf("FD_Grip"), c - [0, 0, 0.015], c, 0.075, 20)
                cylinder(fd.buf("FD_Grip"), c + [0.05, 0, 0], c + [0.05, 0, 0.04], 0.01, 8)


def build_overhead(fd, L, T):
    zones, h = T
    s0, s1 = L.s_o0, L.s_o1
    z0, z1 = L.roof_z(s0, 0) - 0.14, L.roof_z(s1, 0) - 0.18
    length = math.hypot(s1 - s0, z1 - z0)

    def face(y, v):
        t = v / length
        return np.array([s0 + (s1 - s0) * t, y, z0 + (z1 - z0) * t])

    def width(v):
        p = face(0, v)
        lo, hi = 0.0, L.o_hw
        for _ in range(25):
            mid = 0.5 * (lo + hi)
            if L.roof_z(p[0], mid) > p[2] + 0.015:
                lo = mid
            else:
                hi = mid
        return lo

    def point(y, v):
        w = width(v)
        p = face(0, v)
        p[1] = max(-w, min(w, y))
        return p

    def uv(y, v):
        return uv_of(zones, h, "overhead", (L.o_hw - y) / (2 * L.o_hw), v / length)
    vs = np.linspace(0, length, 12)
    normal = normalize(np.array([z1 - z0, 0, -(s1 - s0)]))
    if normal[2] > 0:
        normal = -normal
    mapped_surface(fd.buf("FD_Faces"), point, np.linspace(L.o_hw, -L.o_hw, 9), vs, uv, normal)
    for sgn in (1, -1):   # side skirts up to the roof
        rows = []
        for v in vs:
            p = point(sgn * L.o_hw, v)
            rows.append([p, np.array([p[0], p[1], L.roof_z(p[0], p[1]) + 0.02])])
        rows = np.array(rows)
        grid(fd.buf("FD_Panel"), rows, np.broadcast_to(np.array([0, sgn, 0.0]), rows.shape), closed=False)
    for v, ref in ((0.0, -1.0), (length, 1.0)):
        w = width(v)
        p = face(0, v)
        zr = L.roof_z(p[0], 0) + 0.02
        quad(fd.buf("FD_Panel"), [[p[0], w, p[2]], [p[0], -w, p[2]], [p[0], -w, zr], [p[0], w, zr]], [ref, 0, 0])


def build_pilot_seat(seat, dark, base, scale=1.0):
    """Pilot seat with its eye reference at base[0]; base = (eye station, y, floor height)."""
    s, y, zf = base
    k = scale
    box(dark, [s - 0.02, y, zf + 0.12], (0.34 * k, 0.30 * k, 0.24))
    box(seat, [s - 0.12, y, zf + 0.42], (0.50 * k, 0.50 * k, 0.11), axes=rot_x(4 * D))
    back = rot_x(-14 * D)
    box(seat, [s + 0.20 * k, y, zf + 0.82], (0.13, 0.50 * k, 0.70), axes=back)
    box(seat, [s + 0.28 * k, y, zf + 1.24], (0.10, 0.30 * k, 0.17), axes=back)
    for sgn in (1, -1):
        box(seat, [s - 0.02, y + sgn * 0.27 * k, zf + 0.66], (0.34 * k, 0.06, 0.05))
        box(dark, [s + 0.08, y + sgn * 0.27 * k, zf + 0.55], (0.05, 0.04, 0.22))


def build_rudder_pedals(fd, centre):
    for dy in (0.11, -0.11):
        c = centre + [0, dy, 0.17]
        box(fd.buf("FD_Grip"), c, (0.025, 0.08, 0.22), axes=rot_x(-25 * D))
        cylinder(fd.buf("Metal"), c + [-0.02, 0, 0.10], c + [-0.15, 0, 0.12], 0.012, 8)


def build_yoke(fd, base, L):
    """Boeing control column with a ram's-horn yoke facing the pilot."""
    s, y, zf = base
    foot = np.array([s - 0.60, y, zf])
    hub = np.array([s - 0.48, y, zf + 0.64])
    box(fd.buf("FD_Dark"), foot + [0, 0, 0.04], (0.16, 0.14, 0.08))
    cylinder(fd.buf("FD_Dark"), foot + [0, 0, 0.06], hub - [0.03, 0, 0], 0.032, 12)
    box(fd.buf("FD_Grip"), hub, (0.06, 0.12, 0.08))
    box(fd.buf("FD_Handle"), hub + [0.032, 0, 0.0], (0.004, 0.08, 0.055))       # checklist clip
    for sgn in (1, -1):
        path = [hub + [0, sgn * 0.05, -0.01], hub + [0.01, sgn * 0.14, -0.035], hub + [0.02, sgn * 0.185, 0.0],
                hub + [0.025, sgn * 0.19, 0.06], hub + [0.02, sgn * 0.17, 0.10]]
        tube(fd.buf("FD_Grip"), path, 0.017, 10)


def build_sidestick(fd, L, side):
    c = np.array([L.sE - 0.24, side * (L.side_y + 0.12), L.side_z])
    cylinder(fd.buf("FD_Grip"), c, c + [0, 0, 0.03], 0.045, 16, radius1=0.03)
    top = c + [-0.03, -side * 0.008, 0.15]
    tube(fd.buf("FD_Grip"), [c + [0, 0, 0.025], c + [-0.005, 0, 0.06], top], 0.021, 12)
    box(fd.buf("FD_Grip"), top + [0.0, 0, 0.012], (0.045, 0.035, 0.035), axes=rot_x(-12 * D))
    box(fd.buf("FD_Handle"), top + [-0.012, 0, 0.032], (0.018, 0.018, 0.008))     # takeover button (lit red in reality)


def build_table(fd, L, side):
    p = L.panel_point(side * L.yE, L.zr_side)
    s0 = p[0] - 0.05
    s1 = s0 + 0.36
    z = p[2] - 0.035
    y = side * L.yE
    box(fd.buf("FD_Panel"), [(s0 + s1) / 2, y, z], (s1 - s0, 0.44, 0.016))
    box(fd.buf("FD_Grip"), [s1 + 0.005, y, z], (0.012, 0.18, 0.02))
