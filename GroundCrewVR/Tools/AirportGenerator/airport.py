"""Procedural airport around the training stand (numpy only; reuses the aircraft generator's tools).

Frame: Unity world space - x east (right), y up, z north (forward), metres. The training stand
(Stand 24, the sandbox) is at the origin with its nose-gear stop bar at z = 12; the converter in
build.py turns this into glTF for glTFast.

Layout (APPROX; a generic regional airport, no real airport is copied):
    z -290..-225  car park              z -215..-203  landside road + curb
    z -190..-150  terminal (6 contact gates with static jet bridges, gate taxilane at z -75)
    z  -50..-42   airside service road (GSE road) along the whole apron
    z  -42..110   remote apron, Stands 21-26 (x = -144 .. 96, 48 m apart), taxilane at z 80
    z  110..168   apron exits B1-B3 (x = -190, 0, 190)
    z  168..192   parallel taxiway A
    z  192..307   runway connectors A1-A4 with holding positions (x = -1100, -400, 400, 1100)
    z  307..353   runway 09/27, 2400 x 45 m, blast pads and approach lights at both ends
    plus control tower, maintenance hangar, cargo shed, fuel farm, fire station, floodlight masts.

Markings and signs follow the usual ICAO patterns in simplified form: yellow taxi / lead-in
lines, red equipment restraint lines, white runway markings (threshold, designators, centreline,
touchdown zone, aiming point), holding position markings, mandatory / location / direction signs.
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "AircraftGenerator"))
from meshlib import Part, box, cylinder, fan, grid, lathe, normalize  # noqa: E402
from paint import FONT  # noqa: E402

Y = np.array([0.0, 1.0, 0.0])

# name: (rgb, metallic, roughness, emissive, texture, tile metres)
MATERIALS = {
    "Grass": ((0.40, 0.50, 0.25), 0.0, 0.95, None, "grass", 23.0),
    "Concrete": ((0.78, 0.78, 0.76), 0.0, 0.85, None, "concrete", 5.0),
    "Asphalt": ((0.50, 0.50, 0.51), 0.0, 0.90, None, "asphalt", 4.0),
    "TaxiwayAsphalt": ((0.46, 0.46, 0.47), 0.0, 0.90, None, "asphalt", 4.0),
    "RunwayAsphalt": ((0.38, 0.38, 0.39), 0.0, 0.90, None, "asphalt", 4.0),
    "PaintYellow": ((0.96, 0.75, 0.08), 0.0, 0.70, None, None, None),
    "PaintWhite": ((0.92, 0.92, 0.90), 0.0, 0.70, None, None, None),
    "PaintRed": ((0.75, 0.10, 0.08), 0.0, 0.70, None, None, None),
    "Wall": ((0.80, 0.79, 0.76), 0.0, 0.85, None, None, None),
    "WallDark": ((0.42, 0.43, 0.45), 0.0, 0.80, None, None, None),
    "Cladding": ((0.80, 0.82, 0.84), 0.3, 0.55, None, None, None),
    "Metal": ((0.66, 0.68, 0.71), 0.6, 0.40, None, None, None),
    "Glass": ((0.14, 0.24, 0.32), 0.8, 0.12, None, None, None),
    "Mullion": ((0.24, 0.26, 0.29), 0.6, 0.45, None, None, None),
    "Roof": ((0.52, 0.53, 0.54), 0.0, 0.80, None, None, None),
    "Opening": ((0.035, 0.035, 0.04), 0.0, 0.95, None, None, None),
    "RollDoor": ((0.62, 0.64, 0.66), 0.4, 0.45, None, None, None),
    "HangarDoor": ((0.42, 0.50, 0.60), 0.3, 0.50, None, None, None),
    "TankWhite": ((0.90, 0.90, 0.88), 0.2, 0.50, None, None, None),
    "Steel": ((0.52, 0.53, 0.55), 0.8, 0.35, None, None, None),
    "BridgeGrey": ((0.84, 0.85, 0.86), 0.2, 0.45, None, None, None),
    "Bellows": ((0.12, 0.12, 0.13), 0.0, 0.85, None, None, None),
    "Rubber": ((0.05, 0.05, 0.05), 0.0, 0.90, None, None, None),
    "Fixture": ((0.20, 0.21, 0.22), 0.4, 0.60, None, None, None),
    "SignBlack": ((0.04, 0.04, 0.045), 0.0, 0.60, None, None, None),
    "SignYellow": ((0.98, 0.80, 0.06), 0.0, 0.55, (0.20, 0.16, 0.01), None, None),
    "SignRed": ((0.78, 0.07, 0.06), 0.0, 0.55, (0.15, 0.01, 0.01), None, None),
    "SignBlue": ((0.08, 0.22, 0.52), 0.0, 0.50, None, None, None),
    "SignWhite": ((0.95, 0.95, 0.95), 0.0, 0.50, (0.25, 0.25, 0.25), None, None),
    "SockOrange": ((1.00, 0.40, 0.05), 0.0, 0.80, None, None, None),
    "LightWhite": ((1.0, 0.97, 0.88), 0.0, 0.30, (1.0, 0.95, 0.80), None, None),
    "LightGreen": ((0.2, 1.0, 0.4), 0.0, 0.30, (0.1, 1.0, 0.3), None, None),
    "LightRed": ((1.0, 0.15, 0.08), 0.0, 0.30, (1.0, 0.08, 0.04), None, None),
    "LightBlue": ((0.2, 0.4, 1.0), 0.0, 0.30, (0.1, 0.3, 1.0), None, None),
}

# Seen from inside as well as outside (walk-through jet bridges).
DOUBLE_SIDED = {"BridgeGrey", "Bellows", "Glass"}

# Heights above y = 0 for paint (keeps it clear of the surface it lies on, even far away).
H_APRON = 0.015
H_TAXI = 0.025
H_RUNWAY = 0.04

REMOTE_STANDS = {21 + k: -144.0 + 48.0 * k for k in range(6)}
REMOTE_STOP_Z = 12.0
GATES = {1 + k: -150.0 + 60.0 * k for k in range(6)}
GATE_STOP_Z = -139.0
GATE_TAXILANE_Z = -75.0
REMOTE_TAXILANE_Z = 80.0
SIDE_TAXILANE_X = 190.0
EXITS = {"B1": -190.0, "B2": 0.0, "B3": 190.0}
CONNECTORS = {"A1": -1100.0, "A2": -400.0, "A3": 400.0, "A4": 1100.0}
TWY_Z, TWY_HALF = 180.0, 11.5
RWY_Z, RWY_HALF, RWY_X = 330.0, 22.5, 1200.0
HOLD_Z = RWY_Z - RWY_HALF - 60.0
ROAD_Z0, ROAD_Z1 = -50.0, -42.0
TERMINAL_X, TERMINAL_Z0, TERMINAL_Z1 = 195.0, -190.0, -150.0


# ---------------------------------------------------------------------- primitives

def hquad(buf, x0, x1, z0, z1, y=0.0, tile=None):
    """Horizontal rectangle facing up; UVs in tiles of `tile` metres (world-aligned)."""
    P = np.array([[x0, y, z0], [x1, y, z0], [x1, y, z1], [x0, y, z1]])
    uv = P[:, [0, 2]] / tile if tile else None
    buf.add(P, [(0, 1, 2), (0, 2, 3)], Y, uv)


def strip(buf, a, b, width, y):
    """Flat paint line from a to b (x, z points)."""
    a, b = np.array([a[0], y, a[1]], float), np.array([b[0], y, b[1]], float)
    d = b - a
    if np.linalg.norm(d) < 1e-6:
        return
    side = normalize(np.cross(Y, d)) * (width / 2)
    buf.add([a - side, a + side, b + side, b - side], [(0, 1, 2), (0, 2, 3)], Y)


def dashed(buf, a, b, width, y, dash, gap):
    a, b = np.asarray(a, float), np.asarray(b, float)
    length = np.linalg.norm(b - a)
    d = (b - a) / length
    t = 0.0
    while t < length:
        strip(buf, a + d * t, a + d * min(t + dash, length), width, y)
        t += dash + gap


def outline(buf, x0, x1, z0, z1, width, y):
    for a, b in (((x0, z0), (x1, z0)), ((x1, z0), (x1, z1)), ((x1, z1), (x0, z1)), ((x0, z1), (x0, z0))):
        strip(buf, a, b, width, y)


def obox(buf, p0, p1, width, height):
    """Box whose floor centre-line runs from p0 to p1 (3D), `width` across and `height` up."""
    p0, p1 = np.asarray(p0, float), np.asarray(p1, float)
    a = normalize(p1 - p0)
    side = normalize(np.cross(Y, a))
    up = np.cross(a, side)
    centre = (p0 + p1) / 2 + up * (height / 2)
    box(buf, centre, (np.linalg.norm(p1 - p0), height, width), axes=np.stack([a, up, side]))
    return a, side, up


def abox(buf, x0, x1, y0, y1, z0, z1):
    box(buf, ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (abs(x1 - x0), abs(y1 - y0), abs(z1 - z0)))


def text(buf, s, centre, right, up, height, normal, lift=0.0):
    """5x7 pixel lettering (paint stencil / sign style) centred on `centre`, merged row runs."""
    right, up, normal = (np.asarray(v, float) for v in (right, up, normal))
    px = height / 7.0
    width = (len(s) * 6 - 1) * px
    origin = np.asarray(centre, float) - right * (width / 2) + up * (height / 2) + normal * lift
    for i, ch in enumerate(s.upper()):
        rows = FONT.get(ch, FONT[" "])
        for r, bits in enumerate(rows):
            c = 0
            while c < 5:
                if bits & (16 >> c):
                    c0 = c
                    while c < 5 and bits & (16 >> c):
                        c += 1
                    u0, u1 = (i * 6 + c0) * px, (i * 6 + c) * px
                    v0, v1 = -r * px, -(r + 1) * px
                    q = [origin + right * u0 + up * v0, origin + right * u1 + up * v0,
                         origin + right * u1 + up * v1, origin + right * u0 + up * v1]
                    buf.add(q, [(0, 1, 2), (0, 2, 3)], normal)
                else:
                    c += 1
    return width


def ground_text(buf, s, x, z, facing, height, y):
    """Painted on the ground, upright for someone moving along `facing` (x, z)."""
    f = normalize(np.array([facing[0], 0.0, facing[1]]))
    right = np.cross(Y, f)
    text(buf, s, (x, y, z), right, f, height, Y)


def face_text(buf, s, centre, normal, height, lift=0.01):
    """Upright lettering on a vertical face whose outward normal is `normal`."""
    n = normalize(np.asarray(normal, float))
    right = np.cross(Y, -n)
    return text(buf, s, centre, right, Y, height, n, lift)


# ---------------------------------------------------------------------- ground

PAVED = []          # every paved rectangle, so the grass can be cut around them
GRASS_BOUNDS = (-1750.0, 1750.0, -330.0, 600.0)


def pave(buf, x0, x1, z0, z1, y, tile):
    PAVED.append((x0, x1, z0, z1))
    hquad(buf, x0, x1, z0, z1, y, tile)


def build_grass(root):
    """Grass everywhere that isn't paved, as merged rectangles. Nothing lies under the paving:
    two overlapping surfaces would flicker in the distance on Quest's 24-bit depth buffer."""
    gx0, gx1, gz0, gz1 = GRASS_BOUNDS
    xs = sorted({gx0, gx1} | {v for r in PAVED for v in r[:2]})
    zs = sorted({gz0, gz1} | {v for r in PAVED for v in r[2:]})
    buf = root.child(Part("Ground_Grass")).buf("Grass")
    tile = MATERIALS["Grass"][5]
    overlap = 0.1      # tuck the grass edge under the paving so no gap shows
    for j in range(len(zs) - 1):
        z0, z1 = zs[j], zs[j + 1]
        cz = (z0 + z1) / 2
        run = None
        for i in range(len(xs) - 1):
            cx = (xs[i] + xs[i + 1]) / 2
            paved = any(r[0] < cx < r[1] and r[2] < cz < r[3] for r in PAVED)
            if not paved and run is None:
                run = xs[i]
            if (paved or i == len(xs) - 2) and run is not None:
                end = xs[i] if paved else xs[i + 1]
                hquad(buf, run - overlap, end + overlap, z0 - overlap, z1 + overlap, -0.02, tile)
                run = None


def build_ground(root):
    PAVED.clear()
    apron = root.child(Part("Ground_Apron")).buf("Concrete")
    t = MATERIALS["Concrete"][5]
    pave(apron, -TERMINAL_X, TERMINAL_X, TERMINAL_Z1, ROAD_Z0, 0, t)          # terminal apron
    pave(apron, -200, 200, ROAD_Z1, 110, 0, t)                                # remote apron
    pave(apron, -250, -200, ROAD_Z1, 40, 0, t)                                # hangar apron
    pave(apron, 200, 250, ROAD_Z1, 40, 0, t)                                  # cargo apron
    pave(apron, -312, -268, 134, 168.5, 0, t)                                 # fire station forecourt
    pave(apron, -TERMINAL_X, TERMINAL_X, -200, TERMINAL_Z0, 0, t)             # landside pavement
    pave(apron, 226, 254, -190, -162, 0, t)                                   # tower pad

    roads = root.child(Part("Ground_Roads")).buf("Asphalt")
    t = MATERIALS["Asphalt"][5]
    pave(roads, -330, 330, ROAD_Z0, ROAD_Z1, 0, t)                            # GSE road
    pave(roads, -230, 230, -215, -203, 0, t)                                  # landside road
    pave(roads, -230, 230, -203, -200, 0, t)                                  # drop-off lane
    pave(roads, -200, 200, -290, -225, 0, t)                                  # car park
    pave(roads, -12, 12, -225, -215, 0, t)                                    # car park entrance

    twy = root.child(Part("Ground_Taxiways")).buf("TaxiwayAsphalt")
    t = MATERIALS["TaxiwayAsphalt"][5]
    pave(twy, -1112, 1112, TWY_Z - TWY_HALF, TWY_Z + TWY_HALF, 0, t)
    for x in EXITS.values():
        pave(twy, x - 11.5, x + 11.5, 110, TWY_Z - TWY_HALF, 0, t)
    for x in CONNECTORS.values():
        pave(twy, x - 11.5, x + 11.5, TWY_Z + TWY_HALF, RWY_Z - RWY_HALF, 0, t)

    rwy = root.child(Part("Ground_Runway")).buf("RunwayAsphalt")
    t = MATERIALS["RunwayAsphalt"][5]
    pave(rwy, -RWY_X - 60, RWY_X + 60, RWY_Z - RWY_HALF, RWY_Z + RWY_HALF, 0, t)
    build_grass(root)


# ---------------------------------------------------------------------- markings

def build_markings(root):
    m = root.child(Part("Markings_Apron"))
    yel, wht, red = m.buf("PaintYellow"), m.buf("PaintWhite"), m.buf("PaintRed")
    y = H_APRON

    # Remote stands: lead-in, stop bar, stand number, equipment restraint area, GSE parking.
    for number, x in REMOTE_STANDS.items():
        strip(yel, (x, REMOTE_STOP_Z), (x, REMOTE_TAXILANE_Z), 0.15, y)
        strip(yel, (x - 1.5, REMOTE_STOP_Z), (x + 1.5, REMOTE_STOP_Z), 0.3, y)
        strip(yel, (x - 1.0, REMOTE_STOP_Z - 2.0), (x + 1.0, REMOTE_STOP_Z - 2.0), 0.15, y)   # second type stop
        ground_text(yel, str(number), x, REMOTE_STOP_Z + 14.0, (0, -1), 2.6, y)
        outline(red, x - 20, x + 20, -4, 58, 0.15, y)
        for x0, x1 in ((x - 17, x - 5), (x + 5, x + 17)):
            outline(wht, x0, x1, -38, -26, 0.12, y)
            ground_text(wht, "GSE", (x0 + x1) / 2, -32, (0, 1), 1.2, y)
    # Remote taxilane, side taxilanes and the apron ends of the exits.
    strip(yel, (-SIDE_TAXILANE_X, REMOTE_TAXILANE_Z), (SIDE_TAXILANE_X, REMOTE_TAXILANE_Z), 0.15, y)
    for x in (-SIDE_TAXILANE_X, SIDE_TAXILANE_X):
        strip(yel, (x, GATE_TAXILANE_Z), (x, ROAD_Z0), 0.15, y)
        strip(yel, (x, ROAD_Z1), (x, 110), 0.15, y)
    strip(yel, (0, REMOTE_TAXILANE_Z), (0, 110), 0.15, y)

    # Contact gates: lead-in, stop bar, gate number, restraint area.
    for number, x in GATES.items():
        strip(yel, (x, GATE_STOP_Z), (x, GATE_TAXILANE_Z), 0.15, y)
        strip(yel, (x - 1.5, GATE_STOP_Z), (x + 1.5, GATE_STOP_Z), 0.3, y)
        ground_text(yel, str(number), x, GATE_STOP_Z + 12.0, (0, -1), 2.6, y)
        outline(red, x - 20, x + 20, -148, -84, 0.15, y)
        # Jet-bridge drive area: a white circle of dashes around the drive column's reach.
        sx, sz = bridge_stow_column()
        cx, cz, r = x + sx, sz, 4.0
        for k in range(24):
            a0, a1 = 2 * math.pi * k / 24, 2 * math.pi * (k + 0.55) / 24
            strip(wht, (cx + r * math.cos(a0), cz + r * math.sin(a0)), (cx + r * math.cos(a1), cz + r * math.sin(a1)), 0.12, y)
    strip(yel, (-SIDE_TAXILANE_X, GATE_TAXILANE_Z), (SIDE_TAXILANE_X, GATE_TAXILANE_Z), 0.15, y)
    # Apron edge / service road boundary (white "do not cross" line along the head of the stands).
    for z in (ROAD_Z0 - 0.3, ROAD_Z1 + 0.3):
        strip(wht, (-TERMINAL_X, z), (TERMINAL_X, z), 0.12, y)

    # Roads: edge lines, dashed centre lines, taxilane crossings, zebra crossing, car park bays.
    r = root.child(Part("Markings_Roads"))
    rw, ry = r.buf("PaintWhite"), r.buf("PaintYellow")
    zc = (ROAD_Z0 + ROAD_Z1) / 2
    for z in (ROAD_Z0 + 0.3, ROAD_Z1 - 0.3):
        strip(rw, (-330, z), (330, z), 0.15, y)
    for x0, x1 in ((-330, -SIDE_TAXILANE_X - 26), (-SIDE_TAXILANE_X + 26, SIDE_TAXILANE_X - 26), (SIDE_TAXILANE_X + 26, 330)):
        dashed(rw, (x0, zc), (x1, zc), 0.12, y, 3.0, 3.0)
    for x in (-SIDE_TAXILANE_X, SIDE_TAXILANE_X):
        # Aircraft have priority: stop lines and STOP on both lanes, yellow taxilane across the road.
        # Drive on the right: eastbound traffic uses the south lane, westbound the north lane.
        strip(rw, (x - 25, ROAD_Z0 + 0.3), (x - 25, zc), 0.4, y)
        strip(rw, (x + 25, zc), (x + 25, ROAD_Z1 - 0.3), 0.4, y)
        ground_text(rw, "STOP", x - 28.5, (zc + ROAD_Z0) / 2, (1, 0), 1.6, y)
        ground_text(rw, "STOP", x + 28.5, (zc + ROAD_Z1) / 2, (-1, 0), 1.6, y)
        strip(ry, (x, ROAD_Z0), (x, ROAD_Z1), 0.15, y)
    for k in range(7):                                   # zebra crossing at the head of Stand 24
        zz = ROAD_Z0 + 0.6 + k * 1.1
        strip(rw, (-8.0, zz + 0.25), (-4.0, zz + 0.25), 0.5, y)
    for z in (-214.7, -203.3):
        strip(rw, (-230, z), (230, z), 0.15, y)
    dashed(rw, (-230, -209), (230, -209), 0.12, y, 3.0, 3.0)
    # Car park: rows of 2.5 x 5 m bays either side of 7 m aisles.
    for z0, z1 in ((-289, -284), (-277, -272), (-271, -266), (-259, -254), (-253, -248), (-241, -236)):
        for x in np.arange(-196, 196.1, 2.5):
            strip(rw, (x, z0), (x, z1), 0.12, y)
    for z in (-289, -271, -253):
        strip(rw, (-196, z), (196, z), 0.12, y)

    # Taxiway: centre lines, edge lines with gaps at junctions, holding positions.
    t = root.child(Part("Markings_Taxiways"))
    ty = t.buf("PaintYellow")
    y = H_TAXI
    strip(ty, (-1100, TWY_Z), (1100, TWY_Z), 0.15, y)
    for x in EXITS.values():
        strip(ty, (x, 110), (x, TWY_Z), 0.15, y)
    for x in CONNECTORS.values():
        strip(ty, (x, TWY_Z), (x, RWY_Z - RWY_HALF), 0.15, y)
    gaps_s = sorted(EXITS.values())
    gaps_n = sorted(CONNECTORS.values())
    for z, gaps in ((TWY_Z - TWY_HALF + 0.3, gaps_s), (TWY_Z + TWY_HALF - 0.3, gaps_n)):
        x0 = -1112.0
        for g in gaps + [1112.0 + 11.5]:
            x1 = g - 11.5
            if x1 > x0:
                strip(ty, (x0, z), (x1, z), 0.15, y)
                strip(ty, (x0, z + (0.3 if z < TWY_Z else -0.3)), (x1, z + (0.3 if z < TWY_Z else -0.3)), 0.15, y)
            x0 = g + 11.5
    for x in CONNECTORS.values():
        for k, solid in ((0, True), (1, True), (2, False), (3, False)):
            z = HOLD_Z - 1.05 + k * 0.6
            if solid:
                strip(ty, (x - 11.2, z), (x + 11.2, z), 0.3, y)
            else:
                dashed(ty, (x - 11.2, z), (x + 11.2, z), 0.3, y, 1.0, 1.0)
        for x0, x1 in ((x - 11.2, x - 11.2), (x + 11.2, x + 11.2)):
            strip(ty, (x0, TWY_Z + TWY_HALF), (x1, RWY_Z - RWY_HALF), 0.15, y)
        ground_text(ty, "A" + str(list(CONNECTORS.values()).index(x) + 1), x, HOLD_Z - 12, (0, 1), 2.5, y)

    # Runway 09/27.
    rm = root.child(Part("Markings_Runway"))
    w, yy = rm.buf("PaintWhite"), rm.buf("PaintYellow")
    y = H_RUNWAY
    for z in (RWY_Z - RWY_HALF + 0.9, RWY_Z + RWY_HALF - 0.9):
        strip(w, (-RWY_X, z), (RWY_X, z), 0.9, y)
    dashed(w, (-RWY_X + 90, RWY_Z), (RWY_X - 90, RWY_Z), 0.9, y, 30.0, 20.0)
    for sign, label in ((-1, "09"), (1, "27")):
        end = sign * RWY_X
        inward = -sign
        for k in range(6):
            off = 2.7 + k * 3.6
            for s in (-1, 1):
                strip(w, (end + inward * 6, RWY_Z + s * off), (end + inward * 36, RWY_Z + s * off), 1.8, y)
        ground_text(w, label, end + inward * 54, RWY_Z, (inward, 0), 9.0, y)
        # Touchdown zone (stripes 22.5 m long, 1.8 wide) and aiming point (45 x 6 m).
        for dist, count in ((150, 3), (300, 3), (550, 2), (700, 2), (850, 1), (1000, 1)):
            for s in (-1, 1):
                for k in range(count):
                    off = 9 + 0.9 + k * 3.3
                    strip(w, (end + inward * dist, RWY_Z + s * off), (end + inward * (dist + 22.5), RWY_Z + s * off), 1.8, y)
        for s in (-1, 1):
            strip(w, (end + inward * 400, RWY_Z + s * 12), (end + inward * 445, RWY_Z + s * 12), 6.0, y)
        # Blast pad chevrons.
        for k in range(3):
            tip = end + sign * (8 + k * 14)
            for s in (-1, 1):
                strip(yy, (tip, RWY_Z), (tip + sign * 20, RWY_Z + s * 20), 0.9, y)
        strip(w, (end, RWY_Z - RWY_HALF), (end, RWY_Z + RWY_HALF), 1.2, y)
    # Lead-on lines from the connectors to the centre line.
    for x in CONNECTORS.values():
        strip(yy, (x, RWY_Z - RWY_HALF), (x, RWY_Z), 0.15, y)


# ---------------------------------------------------------------------- lights

def light_unit(fixture, lamp, x, z, height=0.36, lamp_size=0.16):
    abox(fixture, x - 0.09, x + 0.09, -0.05, height, z - 0.09, z + 0.09)
    abox(lamp, x - lamp_size / 2, x + lamp_size / 2, height, height + 0.1, z - lamp_size / 2, z + lamp_size / 2)


def build_lights(root):
    l = root.child(Part("Lights_Airfield"))
    fx = l.buf("Fixture")
    white, green, red, blue = l.buf("LightWhite"), l.buf("LightGreen"), l.buf("LightRed"), l.buf("LightBlue")
    for x in np.arange(-RWY_X, RWY_X + 0.1, 60):
        for z in (RWY_Z - RWY_HALF - 1.5, RWY_Z + RWY_HALF + 1.5):
            light_unit(fx, white, x, z)
    for sign in (-1, 1):
        for z in np.linspace(RWY_Z - RWY_HALF + 1, RWY_Z + RWY_HALF - 1, 12):
            light_unit(fx, green, sign * (RWY_X + 1.5), z, 0.2)
            light_unit(fx, red, sign * (RWY_X - 1.5), z, 0.2)
        # Approach lights: centre-line bars every 30 m and a cross bar at 300 m.
        for k in range(1, 11):
            x = sign * (RWY_X + 60 + k * 30)
            for dz in (-2, -1, 0, 1, 2):
                light_unit(fx, white, x, RWY_Z + dz * 1.2, 0.8 + k * 0.08, 0.22)
        for dz in range(-7, 8):
            light_unit(fx, white, sign * (RWY_X + 360), RWY_Z + dz * 2.0, 1.7, 0.22)
    gaps = list(EXITS.values()) + list(CONNECTORS.values())
    for x in np.arange(-1110, 1110.1, 40):
        for z, junctions in ((TWY_Z - TWY_HALF - 1.2, EXITS.values()), (TWY_Z + TWY_HALF + 1.2, CONNECTORS.values())):
            if all(abs(x - g) > 14 for g in junctions):
                light_unit(fx, blue, x, z, 0.3)
    for x in CONNECTORS.values():
        for dx in np.linspace(-9, 9, 7):                # stop bar lights at the holding position
            light_unit(fx, red, x + dx, HOLD_Z + 1.5, 0.12, 0.14)
        for s in (-1, 1):
            for z in np.arange(TWY_Z + TWY_HALF + 20, RWY_Z - RWY_HALF - 5, 30):
                light_unit(fx, blue, x + s * 12.7, z, 0.3)
    _ = gaps


# ---------------------------------------------------------------------- signs

def sign(part, x, z, normal, panels, height=1.0):
    """Taxiway sign: frangible legs and a box with lettering on the face towards `normal`.
    panels: [(text, panel material, text material)] laid out left to right as seen by the reader."""
    n = normalize(np.array([normal[0], 0.0, normal[1]]))
    right = np.cross(Y, -n)
    widths = [len(t) * 0.75 + 0.8 for t, _, _ in panels]
    total = sum(widths)
    base = np.array([x, 0.0, z])
    legs = part.buf("Fixture")
    for s in (-1, 1):
        p = base + right * s * (total / 2 - 0.3)
        abox(legs, p[0] - 0.05, p[0] + 0.05, 0, 0.35, p[2] - 0.05, p[2] + 0.05)
    cursor = -total / 2
    for (label, panel_mat, text_mat), w in zip(panels, widths):
        c = base + right * (cursor + w / 2) + Y * (0.35 + height / 2)
        box(part.buf(panel_mat), c, (w, height, 0.3), axes=np.stack([right, Y, n]))
        face_text(part.buf(text_mat), label, c + n * 0.15, n, height * 0.6, 0.005)
        cursor += w


def build_signs(root):
    s = root.child(Part("Signs"))
    for k, (name, x) in enumerate(CONNECTORS.items()):
        for side in (-1, 1):
            sign(s, x + side * 16, HOLD_Z - 1.5, (0, -1), [("09-27", "SignRed", "SignWhite"), (name, "SignBlack", "SignYellow")])
    for name, x in EXITS.items():
        sign(s, x + 15.5, 150, (0, -1), [(name, "SignBlack", "SignYellow"), ("<A A>", "SignYellow", "SignBlack")])
        sign(s, x - 15.5, TWY_Z - TWY_HALF - 6, (1, 0), [("A", "SignBlack", "SignYellow"), (name + ">", "SignYellow", "SignBlack")])
    for name, x in CONNECTORS.items():
        sign(s, x - 18, TWY_Z + TWY_HALF + 6, (1, 0), [("A", "SignBlack", "SignYellow"), (name, "SignYellow", "SignBlack")])
    # Stand ID boards at the head of each stand, and gate boards on poles by the terminal.
    for number, x in REMOTE_STANDS.items():
        pole = s.buf("Steel")
        cylinder(pole, (x - 9, 0, -3), (x - 9, 5.2, -3), 0.08, 8)
        box(s.buf("SignBlack"), (x - 9, 5.8, -3), (2.6, 1.6, 0.15))
        face_text(s.buf("SignYellow"), str(number), (x - 9, 5.8, -2.925), (0, 0, 1), 1.0, 0.01)
    sign(s, -SIDE_TAXILANE_X + 14, -30, (0, 1), [("21-26", "SignYellow", "SignBlack"), ("GATES 1-6", "SignYellow", "SignBlack")])


# ---------------------------------------------------------------------- buildings

def build_terminal(root):
    t = root.child(Part("Building_Terminal"))
    wall, dark, glass, mull = t.buf("Wall"), t.buf("WallDark"), t.buf("Glass"), t.buf("Mullion")
    metal, roof, opening = t.buf("Metal"), t.buf("Roof"), t.buf("Opening")
    X0, X1, Z0, Z1 = -TERMINAL_X, TERMINAL_X, TERMINAL_Z0, TERMINAL_Z1
    abox(wall, X0, X1, 0, 16, Z0 + 1, Z1 - 1)                     # main body
    # Airside: concrete ground floor with baggage hall openings and doors, glass above.
    abox(wall, X0, X1, 0, 5.5, Z1 - 1, Z1)
    for x in (-165, -105, -45, 15, 75, 135):
        abox(opening, x - 3, x + 3, 0, 4.2, Z1 - 0.02, Z1 + 0.02)
        abox(metal, x - 3.3, x + 3.3, 4.2, 4.5, Z1, Z1 + 0.25)
        for k in range(12):                                       # strip curtain
            xx = x - 2.75 + k * 0.5
            abox(t.buf("Bellows"), xx - 0.2, xx + 0.2, 0.05, 4.1, Z1 + 0.03, Z1 + 0.05)
    for x in (-135, -75, -15, 45, 105, 165):
        abox(t.buf("RollDoor"), x - 0.6, x + 0.6, 0, 2.2, Z1, Z1 + 0.05)
    abox(metal, X0, X1, 5.5, 6.1, Z1 - 0.2, Z1 + 0.5)              # floor edge band
    abox(glass, X0, X1, 6.1, 15.6, Z1 - 1.0, Z1 - 0.4)
    for x in np.arange(X0, X1 + 0.1, 3.0):
        abox(mull, x - 0.06, x + 0.06, 6.1, 15.6, Z1 - 0.45, Z1 - 0.15)
    for yy in (9.0, 12.2):
        abox(mull, X0, X1, yy - 0.05, yy + 0.05, Z1 - 0.45, Z1 - 0.2)
    # Roof with a 4 m airside overhang, fascia, rooftop plant.
    abox(roof, X0 - 2, X1 + 2, 15.6, 16.4, Z0 - 2, Z1 + 4)
    abox(metal, X0 - 2, X1 + 2, 15.4, 17.4, Z1 + 3.7, Z1 + 4.0)
    abox(metal, X0 - 2, X1 + 2, 15.4, 17.4, Z0 - 2.3, Z0 - 2.0)
    for x in (-150, -60, 40, 130):
        abox(metal, x - 7, x + 7, 16.4, 19.2, -178, -166)
        for k in range(8):
            abox(mull, x - 6.5 + k * 1.8, x - 6.0 + k * 1.8, 16.6, 19.0, -166.05, -165.9)
    # Gate numbers on the fascia.
    for number, x in GATES.items():
        c = (x + 9.0, 16.4, Z1 + 4.05)
        box(t.buf("SignBlue"), c, (6.2, 1.6, 0.1))
        face_text(t.buf("SignWhite"), f"GATE {number}", (c[0], c[1], c[2] + 0.05), (0, 0, 1), 1.0, 0.01)
    # Landside: glazed entrances, canopy on columns, name on the fascia.
    abox(glass, X0, X1, 0.3, 15.0, Z0 - 0.4, Z0 + 0.6)
    for x in np.arange(X0, X1 + 0.1, 3.0):
        abox(mull, x - 0.06, x + 0.06, 0.3, 15.0, Z0 - 0.5, Z0 - 0.3)
    for yy in (5.5, 10.5):
        abox(metal, X0, X1, yy - 0.3, yy + 0.3, Z0 - 0.7, Z0)
    abox(metal, -120, 120, 6.2, 6.7, -201, Z0)
    for x in np.arange(-114, 115, 12):
        cylinder(metal, (x, 0, -200.4), (x, 6.2, -200.4), 0.22, 10)
    face_text(t.buf("SignWhite"), "DEPARTURES", (0, 13.0, Z0 - 0.75), (0, 0, -1), 2.2, 0.01)
    for x in (-60, 0, 60):                                       # entrance doors
        abox(opening, x - 4, x + 4, 0, 3.2, Z0 - 0.55, Z0 - 0.45)
    abox(dark, X0 - 0.5, X0, 0, 16, Z0, Z1)                      # end walls
    abox(dark, X1, X1 + 0.5, 0, 16, Z0, Z1)


# Apron-drive jet bridge (moving parts in JetBridge.glb; the fixed link and rotunda are static here).
BRIDGE_DX, BRIDGE_Z, BRIDGE_FLOOR = 16.0, -146.0, 5.0      # rotunda centre relative to the gate line
BRIDGE_STOW_YAW = -48.0                                       # stowed tunnel heading (deg, 0 = +z)
BRIDGE_COLUMN_Z = 9.1                                         # drive column, along the tunnel at e = 0
# Tunnel sections (local, rest pose = fully retracted, tunnel along +z, floor at y 0 through the pivot):
#   name, start z, length, width, height, floor y
BRIDGE_SECTIONS = [("Tunnel", 2.0, 9.0, 2.9, 3.0, 0.0), ("TunnelB", 3.0, 8.0, 2.75, 2.85, 0.07), ("TunnelC", 3.6, 7.5, 2.6, 2.7, 0.14)]
BRIDGE_CAB_MOUNT = 11.1                                       # tunnel end (cab mount), floor y 0.14
BRIDGE_CAB_LENGTH = 2.6


def bridge_stow_column():
    a = math.radians(BRIDGE_STOW_YAW)
    return BRIDGE_DX + BRIDGE_COLUMN_Z * math.sin(a), BRIDGE_Z + BRIDGE_COLUMN_Z * math.cos(a)


def build_jet_bridges(root):
    for number, gx in GATES.items():
        b = root.child(Part(f"JetBridge_{number}_Fixed"))
        grey, steel = b.buf("BridgeGrey"), b.buf("Steel")
        rot = np.array([gx + BRIDGE_DX, BRIDGE_FLOOR, BRIDGE_Z])
        # Fixed link into the terminal's upper floor; rotunda drum on its column.
        obox(grey, (rot[0], BRIDGE_FLOOR - 0.15, TERMINAL_Z1 - 0.5), (rot[0], BRIDGE_FLOOR - 0.15, BRIDGE_Z - 1.9), 2.6, 3.0)
        # Rotunda drum: open wall + roof + floor, so you can walk through it (the tunnel turns inside).
        lathe(grey, [(0.0, 2.2), (3.15, 2.2)], rot - Y * 0.15, Y, 20, closed_profile=False, outward_sign=-1.0)
        lathe(grey, [(3.15, 2.2), (3.15, 0.0)], rot - Y * 0.15, Y, 20, closed_profile=False, outward_sign=-1.0)
        lathe(grey, [(0.0, 0.0), (0.0, 2.2)], rot - Y * 0.15, Y, 20, closed_profile=False, outward_sign=-1.0)
        cylinder(steel, (rot[0], 0, rot[2]), (rot[0], BRIDGE_FLOOR - 0.15, rot[2]), 0.55, 12)


def build_jet_bridge_model():
    """The moving parts of one bridge, root at the rotunda floor centre, rest pose retracted
    along +z. Node chain: Slew (yaw) > Tunnel (pitch) > TunnelB > TunnelC (telescopic, slide
    along z) > CabMount (counter-pitch) > Cab (yaw) > Canopy; TunnelC > ColumnMount
    (counter-pitch) > ColumnLegs (scaled in y) + Bogie (yaw) > Wheel_L / Wheel_R."""
    root = Part("JetBridge")
    slew = root.child(Part("Slew"))
    parent = slew
    for name, z0, length, w, h, fy in BRIDGE_SECTIONS:
        sec = parent.child(Part(name, (0.0, 0.0, 0.0) if name == "Tunnel" else (0.0, fy, z0)))
        grey, glass = sec.buf("BridgeGrey"), sec.buf("Glass")
        p0, p1 = np.array([0.0, fy, z0]), np.array([0.0, fy, z0 + length])
        a, side, up = obox(grey, p0, p1, w, h)
        for sgn in (-1, 1):
            c0 = p0 + side * sgn * (w / 2 + 0.01) + up * (h * 0.55)
            c1 = p1 + side * sgn * (w / 2 + 0.01) + up * (h * 0.55)
            obox(glass, c0 - up * 0.3, c1 - up * 0.3, 0.02, 0.6)
        for f in np.linspace(0.06, 0.94, 5):
            p = p0 + (p1 - p0) * f
            box(grey, p + up * (h / 2), (0.12, h + 0.08, w + 0.08), axes=np.stack([a, up, side]))
        box(sec.buf("Bellows"), p0 + up * 0.03 + a * (length / 2), (length - 0.1, 0.02, w - 0.2), axes=np.stack([a, up, side]))   # floor mat
        parent = sec
    tunnel_c = parent
    # Cab: rotates about the tunnel end; canopy (bellows) slides out to the fuselage.
    mount = tunnel_c.child(Part("CabMount", (0.0, 0.14, BRIDGE_CAB_MOUNT)))
    cab = mount.child(Part("Cab", (0.0, 0.14, BRIDGE_CAB_MOUNT)))
    z0, z1 = BRIDGE_CAB_MOUNT - 0.3, BRIDGE_CAB_MOUNT + BRIDGE_CAB_LENGTH
    grey, glass, steel = cab.buf("BridgeGrey"), cab.buf("Glass"), cab.buf("Steel")
    abox(grey, -1.7, 1.7, 0.04, 0.14, z0, z1)                        # floor
    abox(grey, -1.7, 1.7, 2.84, 3.0, z0, z1)                         # roof
    for x0, x1 in ((-1.7, -1.6), (1.6, 1.7)):
        abox(grey, x0, x1, 0.14, 1.2, z0, z1)
        abox(glass, x0, x1, 1.2, 2.5, z0 + 0.2, z1 - 0.1)
        abox(grey, x0, x1, 2.5, 2.84, z0, z1)
    abox(grey, -1.7, -1.3, 0.14, 2.84, z1 - 0.1, z1)                 # front frame around the door opening
    abox(grey, 1.3, 1.7, 0.14, 2.84, z1 - 0.1, z1)
    abox(grey, -1.3, 1.3, 2.3, 2.84, z1 - 0.1, z1)
    abox(cab.buf("Rubber"), -1.2, 1.2, 0.0, 0.14, z1, z1 + 0.1)       # floor bumper (the docking face)
    # Operator console (left of the door) with two buttons; the editor makes them pressable.
    abox(steel, 0.95, 1.55, 0.14, 1.05, z1 - 0.9, z1 - 0.35)
    for name, x in (("Btn_BridgeDock", 1.08), ("Btn_BridgeRetract", 1.40)):
        btn = cab.child(Part(name, (x, 1.07, z1 - 0.62)))
        cylinder(btn.buf("SignYellow" if "Dock" in name else "SignRed"), (x, 1.05, z1 - 0.62), (x, 1.07, z1 - 0.62), 0.03, 12)
        btn.child(Part("Out", (x, 1.17, z1 - 0.62)))
    cab.child(Part("CabFront", (0.0, 0.14, z1 + 0.1)))
    canopy = cab.child(Part("Canopy", (0.0, 0.14, z1 - 0.25)))
    cb = canopy.buf("Bellows")
    for x0, x1, y0, y1 in ((-1.35, -1.2, 0.14, 2.35), (1.2, 1.35, 0.14, 2.35), (-1.35, 1.35, 2.2, 2.35)):
        abox(cb, x0, x1, y0, y1, z1 - 0.25, z1 + 0.05)
    # Drive column: legs (scaled to length at runtime), bogie with two wheels.
    col = tunnel_c.child(Part("ColumnMount", (0.0, 0.04, BRIDGE_COLUMN_Z)))
    legs = col.child(Part("ColumnLegs", (0.0, 0.04, BRIDGE_COLUMN_Z)))
    axle_y = -BRIDGE_FLOOR + 0.45
    leg_len = 0.04 - (axle_y + 0.6)
    for sgn in (-1, 1):
        cylinder(legs.buf("Steel"), (sgn * 0.9, 0.04, BRIDGE_COLUMN_Z), (sgn * 0.9, 0.04 - leg_len, BRIDGE_COLUMN_Z), 0.16, 10)
    box(col.buf("Steel"), (0, -0.12, BRIDGE_COLUMN_Z), (2.3, 0.3, 0.5))
    bogie = col.child(Part("Bogie", (0.0, axle_y, BRIDGE_COLUMN_Z)))
    box(bogie.buf("Steel"), (0, axle_y + 0.45, BRIDGE_COLUMN_Z), (2.6, 0.3, 0.7))
    for sgn in (-1, 1):
        cylinder(bogie.buf("Steel"), (sgn * 0.9, axle_y + 0.6, BRIDGE_COLUMN_Z), (sgn * 0.9, axle_y + 0.3, BRIDGE_COLUMN_Z), 0.2, 8)
        wheel = bogie.child(Part("Wheel_L" if sgn < 0 else "Wheel_R", (sgn * 1.0, axle_y, BRIDGE_COLUMN_Z)))
        cylinder(wheel.buf("Rubber"), (sgn * 0.82, axle_y, BRIDGE_COLUMN_Z), (sgn * 1.18, axle_y, BRIDGE_COLUMN_Z), 0.45, 14)
        cylinder(wheel.buf("Steel"), (sgn * 0.8, axle_y, BRIDGE_COLUMN_Z), (sgn * 1.2, axle_y, BRIDGE_COLUMN_Z), 0.22, 8)
    return root


def build_tower(root, x=240.0, z=-176.0):
    t = root.child(Part("Building_Tower"))
    wall, metal, glass, roof = t.buf("Wall"), t.buf("Metal"), t.buf("Glass"), t.buf("Roof")
    abox(wall, x - 9, x + 9, 0, 6, z - 7, z + 7)
    abox(t.buf("Glass"), x - 9.05, x + 9.05, 1.5, 4.5, z - 3, z + 3)
    lathe(wall, [(0, 0), (0, 3.4), (38, 3.0), (38, 0)], (x, 0, z), Y, 20, closed_profile=False, outward_sign=-1.0)
    for k in range(6):                                            # window slits up the shaft
        a = 2 * math.pi * k / 6
        cx, cz = x + 3.05 * math.cos(a), z + 3.05 * math.sin(a)
        box(glass, (cx, 20, cz), (0.25, 30, 0.6), axes=np.array([[math.cos(a), 0, math.sin(a)], [0, 1, 0], [-math.sin(a), 0, math.cos(a)]]))
    lathe(metal, [(38, 0), (38, 6.2), (41, 6.2), (41, 0)], (x, 0, z), Y, 16, closed_profile=False, outward_sign=-1.0)
    prof = [(41, 0), (41, 6.0), (45.8, 7.0), (45.8, 0)]
    lathe(glass, prof, (x, 0, z), Y, 16, closed_profile=False, outward_sign=-1.0)
    for k in range(16):
        a = 2 * math.pi * (k + 0.5) / 16
        p0 = np.array([x + 6.05 * math.cos(a), 41, z + 6.05 * math.sin(a)])
        p1 = np.array([x + 7.05 * math.cos(a), 45.8, z + 7.05 * math.sin(a)])
        cylinder(t.buf("Mullion"), p0, p1, 0.07, 4, caps=False)
    lathe(roof, [(45.8, 0), (45.8, 7.5), (46.6, 7.5), (47.2, 3.0), (47.2, 0)], (x, 0, z), Y, 16, closed_profile=False, outward_sign=-1.0)
    cylinder(metal, (x, 47.2, z), (x, 54, z), 0.12, 6)
    cylinder(metal, (x + 2, 47.2, z), (x + 2, 50, z), 0.08, 6)
    lathe(metal, [(0, 0), (0.4, 0.9), (0.42, 0)], (x - 2.5, 47.6, z), (1, 0, 0), 10, closed_profile=False, outward_sign=-1.0)
    box(t.buf("LightRed"), (x, 54.15, z), (0.3, 0.3, 0.3))


def build_hangar(root):
    h = root.child(Part("Building_Hangar"))
    clad, door, metal = h.buf("Cladding"), h.buf("HangarDoor"), h.buf("Metal")
    x0, x1, z0, z1, eave, rise = -320.0, -250.0, -36.0, 36.0, 12.0, 8.0
    abox(clad, x0, x1, 0, eave, z0, z1)
    # Arched roof along x.
    zs = np.linspace(z0, z1, 25)
    ys = eave + rise * (1 - (zs / z1) ** 2)
    rings = np.array([[[x, yy, zz] for zz, yy in zip(zs, ys)] for x in (x0, x1)])
    refs = np.array([[normalize(np.array([0.0, 1.0, -2 * rise * zz / z1 ** 2])) for zz in zs] for _ in (x0, x1)])
    grid(h.buf("Roof"), rings, refs, closed=False)
    for x, n in ((x0, -1.0), (x1, 1.0)):                         # gable ends
        pts = [np.array([x, eave, zz]) for zz in zs] + []
        for k in range(len(zs) - 1):
            h.buf("Cladding").add([[x, eave, zs[k]], [x, ys[k], zs[k]], [x, ys[k + 1], zs[k + 1]], [x, eave, zs[k + 1]]],
                                  [(0, 1, 2), (0, 2, 3)], (n, 0, 0))
    # Door: six leaves with vertical ribs, header beam, name.
    for k in range(6):
        zc = z0 + 6 + k * 12
        abox(door, x1, x1 + 0.4, 0, 11.0, zc - 5.9, zc + 5.9)
        for r in range(5):
            zz = zc - 4.8 + r * 2.4
            abox(metal, x1 + 0.4, x1 + 0.55, 0.2, 10.8, zz - 0.08, zz + 0.08)
    abox(metal, x1, x1 + 0.7, 11.0, 12.0, z0, z1)
    face_text(h.buf("PaintWhite"), "MAINTENANCE", (x1 + 0.4, 15.0, 0), (1, 0, 0), 2.4, 0.05)
    # Office lean-to on the north side.
    abox(h.buf("Wall"), x0, x0 + 30, 0, 7, z1, z1 + 8)
    for k in range(8):
        xx = x0 + 2 + k * 3.5
        abox(h.buf("Glass"), xx, xx + 2.2, 3.8, 5.6, z1 + 7.98, z1 + 8.02)
        abox(h.buf("Glass"), xx, xx + 2.2, 0.9, 2.4, z1 + 7.98, z1 + 8.02)


def build_cargo(root):
    c = root.child(Part("Building_Cargo"))
    x0, x1, z0, z1 = 250.0, 312.0, -34.0, 36.0
    abox(c.buf("Cladding"), x0, x1, 0, 11, z0, z1)
    abox(c.buf("Roof"), x0 - 0.5, x1 + 0.5, 11, 11.6, z0 - 0.5, z1 + 0.5)
    for k in range(8):
        zc = z0 + 6 + k * 8.2
        abox(c.buf("RollDoor"), x0 - 0.05, x0, 0.0, 4.8, zc - 2.0, zc + 2.0)
        for z0b, z1b in ((zc - 2.3, zc - 2.05), (zc + 2.05, zc + 2.3)):
            abox(c.buf("Rubber"), x0 - 0.35, x0, 0.9, 1.5, z0b, z1b)
        abox(c.buf("PaintYellow"), x0 - 0.6, x0 - 0.05, 0.0, 1.0, zc - 2.3, zc - 2.1)
    abox(c.buf("Metal"), x0 - 4, x0, 6.0, 6.4, z0, z1)
    face_text(c.buf("PaintWhite"), "CARGO", (x0 - 0.05, 8.6, 0), (-1, 0, 0), 2.6, 0.02)


def build_fuel_farm(root):
    f = root.child(Part("Building_FuelFarm"))
    for x, z in ((315, -120), (345, -120), (330, -94)):
        lathe(f.buf("TankWhite"), [(0, 0), (0, 8.0), (11, 8.0), (12.2, 0)], (x, 0, z), Y, 24, closed_profile=False, outward_sign=-1.0)
        lathe(f.buf("PaintRed"), [(9.5, 8.03), (10.2, 8.03)], (x, 0, z), Y, 24, closed_profile=False, outward_sign=-1.0)
        for k in range(12):                                       # ladder up the side
            box(f.buf("Steel"), (x + 8.15, 0.5 + k * 0.9, z), (0.1, 0.05, 0.6))
    wall = f.buf("Wall")
    for x0, x1, z0, z1 in ((298, 362, -140, -139.6), (298, 362, -78.4, -78), (298, 298.4, -140, -78), (361.6, 362, -140, -78)):
        abox(wall, x0, x1, 0, 1.2, z0, z1)
    abox(f.buf("Metal"), 300, 312, 0, 3.2, -76, -70)
    box(f.buf("TankWhite"), (330, 1.6, -140.4), (7.0, 1.4, 0.12))
    face_text(f.buf("PaintRed"), "FUEL FARM", (330, 1.6, -140.46), (0, 0, -1), 0.8, 0.01)
    for k in range(3):
        cylinder(f.buf("Steel"), (312, 1.0 + k * 0.5, -73), (330, 1.0 + k * 0.5, -86), 0.15, 8)


def build_fire_station(root):
    f = root.child(Part("Building_FireStation"))
    x0, x1, z0, z1 = -308.0, -272.0, 116.0, 134.0
    abox(f.buf("Wall"), x0, x1, 0, 8.5, z0, z1)
    abox(f.buf("Roof"), x0 - 0.4, x1 + 0.4, 8.5, 9.0, z0 - 0.4, z1 + 0.4)
    for k in range(4):
        xc = x0 + 4.5 + k * 9
        abox(f.buf("SignRed"), xc - 3.4, xc + 3.4, 0, 5.4, z1, z1 + 0.06)
        for r in range(9):
            abox(f.buf("Metal"), xc - 3.4, xc + 3.4, 0.3 + r * 0.58, 0.34 + r * 0.58, z1 + 0.06, z1 + 0.08)
    face_text(f.buf("PaintRed"), "AIRPORT FIRE", ((x0 + x1) / 2, 7.0, z1), (0, 0, 1), 1.3, 0.02)
    abox(f.buf("Metal"), x1 - 4, x1, 0, 14, z0, z0 + 4)          # hose drying tower


def build_masts(root):
    m = root.child(Part("Floodlight_Masts"))
    steel, lamp, fixture = m.buf("Steel"), m.buf("LightWhite"), m.buf("Fixture")
    spots = [(x, -38.0, 1.0) for x in (-168, -120, -72, -24, 24, 72, 120, 168)]
    spots += [(x, -143.0, 1.0) for x in (-180, -120, -60, 0, 60, 120, 180)]
    for x, z, facing in spots:
        abox(m.buf("Concrete"), x - 0.9, x + 0.9, 0, 0.8, z - 0.9, z + 0.9)
        for k in range(4):                                        # yellow / black base bands
            abox(m.buf("PaintYellow" if k % 2 == 0 else "SignBlack"), x - 0.92, x + 0.92, 0.1 + k * 0.17, 0.27 + k * 0.17, z - 0.92, z + 0.92)
        cylinder(steel, (x, 0.8, z), (x, 28, z), 0.38, 10, radius1=0.24)
        head = np.array([x, 28.6, z + 0.35 * facing])
        abox(fixture, x - 2.2, x + 2.2, 27.6, 29.6, z - 0.25, z + 0.25)
        tilt = math.radians(28)
        n = np.array([0.0, -math.sin(tilt), math.cos(tilt) * facing])
        u = np.array([0.0, math.cos(tilt), math.sin(tilt) * facing])
        for r in range(2):
            for c in range(4):
                p = head + np.array([-1.5 + c * 1.0, -0.45 + r * 0.9, 0.0])
                box(fixture, p, (0.9, 0.08, 0.8), axes=np.stack([[1, 0, 0], n, u]))
                box(lamp, p + n * 0.05, (0.8, 0.04, 0.7), axes=np.stack([[1, 0, 0], n, u]))


def build_windsocks(root):
    w = root.child(Part("Windsocks"))
    for x in (-900.0, 900.0):
        base = np.array([x, 0.0, RWY_Z + 75.0])
        cylinder(w.buf("Steel"), base, base + Y * 6.5, 0.08, 8)
        ring = base + Y * 6.4
        # Cone streaming east in alternating orange / white bands.
        for k in range(5):
            mat = "SockOrange" if k % 2 == 0 else "PaintWhite"
            r0, r1 = 0.45 - k * 0.06, 0.45 - (k + 1) * 0.06
            lathe(w.buf(mat), [(k * 0.72, r0), ((k + 1) * 0.72, r1)], ring, (1, -0.15, 0), 12, closed_profile=False, outward_sign=-1.0)


def build():
    root = Part("Airport")
    build_ground(root)
    build_markings(root)
    build_lights(root)
    build_signs(root)
    build_terminal(root)
    build_jet_bridges(root)
    build_tower(root)
    build_hangar(root)
    build_cargo(root)
    build_fuel_farm(root)
    build_fire_station(root)
    build_masts(root)
    build_windsocks(root)
    spawns = []
    for number, x in REMOTE_STANDS.items():
        spawns.append({"name": f"Stand_{number}", "position": [x, 0.0, REMOTE_STOP_Z], "yaw": 180.0})
    for number, x in GATES.items():
        spawns.append({"name": f"Gate_{number}", "position": [x, 0.0, GATE_STOP_Z], "yaw": 180.0})
    for number, x in GATES.items():
        spawns.append({"name": f"Bridge_{number}", "position": [x + BRIDGE_DX, BRIDGE_FLOOR, BRIDGE_Z], "yaw": BRIDGE_STOW_YAW})
    for s in spawns:
        root.child(Part(s["name"], s["position"]))
    return root, spawns
