"""Procedural ground-support vehicles (numpy only; reuses the aircraft generator's mesh tools).

Frame: x forward, y left, z up, metres; origin on the ground under the middle of the wheelbase.
Dimensions are typical of each class of airport ground-support equipment (APPROX); no maker's
shapes, names or logos are copied.

Node contract (read by GroundCrew.Vehicles.VehicleAssembler in Unity):
    Body                  static visual mesh
    Collision_<n>         convex collision hulls (renderer is switched off in Unity)
    Wheel_<FL|FR|RL|RR|..>  wheel visual, pivot at the wheel centre (WheelCollider goes here)
    SteeringWheel         pivot at the hub; child SteerAxis = hub + column axis (towards driver)
    DriverEye / ExitPoint empties: seated eye point, where the player stands after getting out
    Btn_<Name>            dash push buttons, pivot at the cap; child Out = cap + press normal
    Light_<Kind>_<n>      emissive lamp lenses, switched on and off by enabling the node
    Hitch_Front / Hitch_Rear  tow pin or coupling points
    plus per-vehicle moving parts (Drawbar, LiftBox, Scissor*, Platform, HoseReel, TowHead...)
The same data is in <Id>.json.
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "AircraftGenerator"))
from meshlib import Part, box, cylinder, fan, grid, lathe, normalize, polygon  # noqa: E402
from aircraft import tyre  # noqa: E402

D = math.pi / 180.0

MATERIALS = {
    # name: (rgb, metallic, roughness, emissive, alpha, double_sided)
    "GseYellow": ((0.93, 0.70, 0.05), 0.0, 0.45, None, None, False),
    "GseWhite": ((0.90, 0.91, 0.90), 0.0, 0.40, None, None, False),
    "GseBlue": ((0.16, 0.29, 0.52), 0.0, 0.50, None, None, False),
    "GseRed": ((0.72, 0.08, 0.06), 0.0, 0.45, None, None, False),
    "Chassis": ((0.10, 0.10, 0.11), 0.3, 0.60, None, None, False),
    "Steel": ((0.55, 0.56, 0.57), 0.8, 0.35, None, None, False),
    "Rubber": ((0.05, 0.05, 0.05), 0.0, 0.90, None, None, False),
    "Hub": ((0.62, 0.62, 0.60), 0.6, 0.40, None, None, False),
    "Seat": ((0.12, 0.12, 0.13), 0.0, 0.80, None, None, False),
    "Dash": ((0.16, 0.16, 0.17), 0.0, 0.70, None, None, False),
    "Glass": ((0.55, 0.65, 0.72), 0.0, 0.05, None, 0.25, True),
    "Stripe": ((0.95, 0.95, 0.95), 0.0, 0.40, None, None, False),
    "Canvas": ((0.20, 0.25, 0.30), 0.0, 0.90, None, None, False),
    "Lens": ((0.75, 0.75, 0.70), 0.1, 0.20, None, None, False),
    "LensAmber": ((0.60, 0.35, 0.05), 0.1, 0.20, None, None, False),
    "LensRed": ((0.45, 0.05, 0.04), 0.1, 0.20, None, None, False),
    "Light_Head": ((1.0, 1.0, 0.95), 0.0, 0.3, (1.0, 0.98, 0.9), None, False),
    "Light_Beacon": ((1.0, 0.6, 0.05), 0.0, 0.3, (1.0, 0.55, 0.02), None, False),
    "Light_Brake": ((0.9, 0.05, 0.03), 0.0, 0.3, (1.0, 0.05, 0.02), None, False),
    "Light_Reverse": ((1.0, 1.0, 1.0), 0.0, 0.3, (1.0, 1.0, 1.0), None, False),
    "Light_Work": ((1.0, 1.0, 0.95), 0.0, 0.3, (1.0, 0.98, 0.9), None, False),
    "Btn_Green": ((0.10, 0.65, 0.20), 0.0, 0.4, None, None, False),
    "Btn_Red": ((0.75, 0.08, 0.06), 0.0, 0.4, None, None, False),
    "Btn_Grey": ((0.55, 0.56, 0.58), 0.0, 0.4, None, None, False),
    "Btn_Amber": ((0.95, 0.60, 0.05), 0.0, 0.4, None, None, False),
    "Collision": ((1.0, 0.0, 1.0), 0.0, 1.0, None, None, False),
}


# ====================================================================== mesh helpers

def prism(buf, outline, a, b, n, centre, depth):
    """Extrude a convex 2D outline (coordinates along axes a, b) by `depth` along n, centred."""
    a, b, n = (np.asarray(v, dtype=float) for v in (a, b, n))
    o = np.asarray(outline, dtype=float)
    c = np.asarray(centre, dtype=float)
    ring = c + o[:, :1] * a + o[:, 1:2] * b
    front, back = ring + n * depth / 2, ring - n * depth / 2
    polygon(buf, front, n)
    polygon(buf, back, -n)
    m = len(o)
    centroid = ring.mean(axis=0)
    for k in range(m):
        j = (k + 1) % m
        quad = [front[k], front[j], back[j], back[k]]
        mid = 0.5 * (ring[k] + ring[j])
        buf.add(quad, [(0, 1, 2), (0, 2, 3)], normalize(mid - centroid))


def side(buf, pts_xz, y0, y1):
    """Convex outline drawn in side view (x, z), extruded from y0 to y1."""
    prism(buf, pts_xz, [1, 0, 0], [0, 0, 1], [0, 1, 0], [0, 0.5 * (y0 + y1), 0], abs(y1 - y0))


def front(buf, pts_yz, x0, x1):
    """Convex outline drawn in front view (y, z), extruded from x0 to x1."""
    prism(buf, pts_yz, [0, 1, 0], [0, 0, 1], [1, 0, 0], [0.5 * (x0 + x1), 0, 0], abs(x1 - x0))


def bevel_box(buf, centre, size, bevel):
    """Box with its four edges parallel to y chamfered (reads as a rounded panel from the side)."""
    lx, ly, lz = size
    hx, hz, c = lx / 2, lz / 2, min(bevel, lx / 3, lz / 3)
    outline = [(-hx + c, -hz), (hx - c, -hz), (hx, -hz + c), (hx, hz - c), (hx - c, hz), (-hx + c, hz), (-hx, hz - c), (-hx, -hz + c)]
    cx, cy, cz = centre
    prism(buf, [(x + cx, z + cz) for x, z in outline], [1, 0, 0], [0, 0, 1], [0, 1, 0], [0, cy, 0], ly)


def ellipse(rx, rz, n=20):
    t = np.linspace(0, 2 * math.pi, n, endpoint=False)
    return np.stack([rx * np.cos(t), rz * np.sin(t)], axis=1)


def torus(buf, centre, axis, R, r, seg=24, seg2=8):
    t = np.linspace(0, 2 * math.pi, seg2, endpoint=False)
    prof = np.stack([r * np.cos(t), R + r * np.sin(t)], axis=1)
    lathe(buf, prof, centre, axis, seg, closed_profile=True)


def rod(buf, p0, p1, r, seg=8):
    cylinder(buf, p0, p1, r, seg)


def lamp(part, lens_buf, centre, normal, radius, segments=14):
    """A lamp: unlit lens in the body plus an emissive cap in the light node just in front of it."""
    n = normalize(np.asarray(normal, dtype=float))
    c = np.asarray(centre, dtype=float)
    cylinder(lens_buf, c - n * 0.02, c, radius, segments)
    cylinder(part.buf(part.extras["material"]), c, c + n * 0.006, radius * 0.97, segments)


def collision_box(root, k, centre, size):
    p = Part(f"Collision_{k}", centre)
    box(p.buf("Collision"), centre, size)
    root.children.append(p)


def empty(root, name, pos, extras=None):
    p = Part(name, pos, extras=extras or {})
    root.children.append(p)
    return p


# ====================================================================== shared assemblies

def add_wheel(root, name, x, y, r, w, steer, drive, dual=False, rim="Hub"):
    c = np.array([x, y, r])
    p = Part(f"Wheel_{name}", c, extras={"radius": r, "width": w * (2.1 if dual else 1.0), "steer": steer, "drive": drive})
    if dual:
        side_sign = 1 if y > 0 else -1
        for dy in (-w * 0.55, w * 0.55):
            tyre(p.buf("Rubber"), p.buf(rim), c + [0, dy * side_sign, 0], 2 * r, w, 24)
    else:
        tyre(p.buf("Rubber"), p.buf(rim), c, 2 * r, w, 24)
    root.children.append(p)
    return p


def steering_wheel(root, hub, normal, radius):
    n = normalize(np.asarray(normal, dtype=float))
    hub = np.asarray(hub, dtype=float)
    p = Part("SteeringWheel", hub)
    torus(p.buf("Chassis"), hub, n, radius, 0.016, 28, 8)
    cylinder(p.buf("Chassis"), hub - n * 0.02, hub + n * 0.01, 0.05, 14)
    # three spokes
    u = normalize(np.cross(n, [0, 1.0, 0]))
    v = np.cross(n, u)
    for ang in (90, 210, 330):
        a = ang * D
        d = u * math.cos(a) + v * math.sin(a)
        rod(p.buf("Chassis"), hub + d * 0.04, hub + d * (radius - 0.01), 0.011, 6)
    p.children.append(Part("SteerAxis", hub + n * 0.2))
    root.children.append(p)
    return p


DASH_BUTTONS = [("Start", 0.40, "Btn_Green"), ("Lights", 0.32, "Btn_Grey"), ("Reverse", -0.24, "Btn_Grey"),
                ("Neutral", -0.30, "Btn_Grey"), ("Drive", -0.36, "Btn_Grey"), ("Park", -0.42, "Btn_Red")]


def driver_station(root, body, eye, wheel_fwd, wheel_drop, wheel_tilt, wheel_radius, dash, buttons=DASH_BUTTONS, seat_w=0.48):
    """Seat, steering column and wheel (horn button on the hub), dash buttons and the DriverEye empty.
    dash = (x_face, z_top): the driver-facing dash face the buttons sit on, and its top edge.
    buttons = [(name, y offset from the driver, material)]."""
    ex, ey, ez = eye
    seat_z = ez - 0.80
    box(body.buf("Seat"), [ex + 0.02, ey, seat_z], (0.46, seat_w, 0.10))
    back = [[math.cos(-12 * D), 0, -math.sin(-12 * D)], [0, 1, 0], [math.sin(-12 * D), 0, math.cos(-12 * D)]]
    box(body.buf("Seat"), [ex - 0.22, ey, seat_z + 0.33], (0.10, seat_w, 0.62), axes=back)
    box(body.buf("Chassis"), [ex + 0.02, ey, seat_z - 0.12], (0.30, 0.30, 0.16))
    n = np.array([-math.cos(wheel_tilt), 0, math.sin(wheel_tilt)])      # wheel faces the driver
    hub = np.array([ex + wheel_fwd, ey, ez - wheel_drop])
    wheel = steering_wheel(root, hub, n, wheel_radius)
    horn = Part("Btn_Horn", hub + n * 0.012)
    cylinder(horn.buf("Btn_Amber"), hub + n * 0.008, hub + n * 0.02, 0.035, 14)
    horn.children.append(Part("Out", hub + n * 0.06))
    wheel.children.append(horn)
    x_face, z_top = dash
    rod(body.buf("Chassis"), hub - n * 0.02, np.array([x_face + 0.05, ey, z_top - 0.08]), 0.03, 10)
    for name, dy, mat in buttons:
        c = np.array([x_face - 0.012, ey + dy, z_top - 0.06])
        p = Part(f"Btn_{name}", c)
        cylinder(p.buf(mat), c, c + [-0.012, 0, 0], 0.016, 14)
        p.children.append(Part("Out", c + [-0.05, 0, 0]))
        root.children.append(p)
    empty(root, "DriverEye", [ex, ey, ez])


def beacon(root, body, pos):
    p = Part("Light_Beacon_1", pos, extras={"material": "Light_Beacon"})
    cylinder(body.buf("Chassis"), np.array(pos) - [0, 0, 0.03], pos, 0.07, 14)
    lathe(p.buf("Light_Beacon"), [(0.0, 0.065), (0.07, 0.06), (0.11, 0.0)], np.asarray(pos, float), [0, 0, 1.0], 14, closed_profile=False)
    root.children.append(p)


def lamps(root, body, kind, positions, normal, radius, lens):
    for k, pos in enumerate(positions):
        p = Part(f"Light_{kind}_{k + 1}", pos, extras={"material": f"Light_{kind}"})
        lamp(p, body.buf(lens), pos, normal, radius)
        root.children.append(p)


def cab_glass_box(body, x0, x1, y_half, z0, z1, rake=0.0, post=0.05, rear_glass=True, door_side=1):
    """Closed cab: corner posts, roof, transparent windscreen / side / rear glass.
    rake: how far the windscreen top sits behind its bottom (m)."""
    g = body.buf("Glass")
    xs_top = x1 - rake
    for y in (y_half - post / 2, -y_half + post / 2):
        rod(body.buf("Chassis"), [x1, y, z0], [xs_top, y, z1], post / 2, 6)
        rod(body.buf("Chassis"), [x0, y, z0], [x0, y, z1], post / 2, 6)
    box(body.buf("GseWhite"), [(x0 + xs_top) / 2, 0, z1 + 0.04], (xs_top - x0 + 0.08, 2 * y_half + 0.06, 0.08))
    # windscreen
    quad = [[x1, y_half - post, z0], [x1, -y_half + post, z0], [xs_top, -y_half + post, z1], [xs_top, y_half - post, z1]]
    g.add(quad, [(0, 1, 2), (0, 2, 3)], [1.0, 0, 0.3])
    for sgn in (1, -1):
        y = sgn * y_half
        g.add([[x0, y, z0], [x1, y, z0], [xs_top, y, z1], [x0, y, z1]], [(0, 1, 2), (0, 2, 3)], [0, sgn, 0])
    if rear_glass:
        g.add([[x0, y_half, z0], [x0, -y_half, z0], [x0, -y_half, z1], [x0, y_half, z1]], [(0, 1, 2), (0, 2, 3)], [-1.0, 0, 0])


# ====================================================================== vehicles

def baggage_tractor():
    """Diesel baggage tractor, open driver's platform under a ROPS canopy. ~2.9 x 1.3 m."""
    vid = "BaggageTractor"
    root = Part(vid)
    body = Part("Body")
    root.children.append(body)
    wb, track, r, w = 1.65, 1.06, 0.28, 0.17
    xf, xr = wb / 2, -wb / 2
    # chassis and bodywork
    box(body.buf("Chassis"), [0.0, 0, 0.36], (2.75, 1.06, 0.22))
    bevel_box(body.buf("GseYellow"), [0.78, 0, 0.78], (1.25, 1.18, 0.62), 0.12)          # engine hood
    side(body.buf("Chassis"), [(1.35, 0.50), (1.43, 0.55), (1.43, 0.98), (1.35, 1.02)], -0.5, 0.5)  # grille
    bevel_box(body.buf("GseYellow"), [-0.75, 0, 0.62], (1.20, 1.24, 0.30), 0.06)          # rear deck / fenders
    box(body.buf("GseYellow"), [0.06, 0, 0.50], (0.30, 1.20, 0.30))                        # footwell front
    box(body.buf("Chassis"), [-0.10, 0, 0.47], (0.5, 1.0, 0.04))                           # floor plate
    for y in (0.63, -0.63):                                                              # wheel arches
        side(body.buf("GseYellow"), [(xf - 0.36, 0.55), (xf + 0.36, 0.55), (xf + 0.30, 0.68), (xf - 0.30, 0.68)], y - 0.08, y + 0.08)
    box(body.buf("Chassis"), [1.43, 0, 0.32], (0.10, 1.25, 0.18))                          # front bumper
    box(body.buf("Chassis"), [-1.40, 0, 0.36], (0.12, 0.50, 0.22))                         # rear hitch plate
    cylinder(body.buf("Steel"), [-1.47, 0, 0.30], [-1.47, 0, 0.52], 0.022, 10)             # pintle pin
    # ROPS canopy
    for x, y in ((0.30, 0.55), (0.30, -0.55), (-1.25, 0.55), (-1.25, -0.55)):
        rod(body.buf("Chassis"), [x, y, 0.60], [x - (0.10 if x > 0 else 0.0), y, 2.0], 0.03, 8)
    box(body.buf("GseYellow"), [-0.52, 0, 2.02], (1.70, 1.22, 0.06))
    # driver
    driver_station(root, body, eye=(-0.42, 0.0, 1.62), wheel_fwd=0.42, wheel_drop=0.52, wheel_tilt=48 * D, wheel_radius=0.19,
                   dash=(0.14, 1.00))
    box(body.buf("Dash"), [0.20, 0, 0.92], (0.12, 1.0, 0.10))
    # wheels
    add_wheel(root, "FL", xf, track / 2, r, w, steer=True, drive=False)
    add_wheel(root, "FR", xf, -track / 2, r, w, steer=True, drive=False)
    add_wheel(root, "RL", xr, track / 2, r, w, steer=False, drive=True)
    add_wheel(root, "RR", xr, -track / 2, r, w, steer=False, drive=True)
    # lights
    lamps(root, body, "Head", [[1.43, 0.42, 0.78], [1.43, -0.42, 0.78]], [1, 0, 0], 0.06, "Lens")
    lamps(root, body, "Brake", [[-1.36, 0.52, 0.66], [-1.36, -0.52, 0.66]], [-1, 0, 0], 0.035, "LensRed")
    lamps(root, body, "Reverse", [[-1.36, 0.42, 0.66]], [-1, 0, 0], 0.03, "Lens")
    beacon(root, body, [-0.52, 0, 2.05])
    empty(root, "Hitch_Rear", [-1.47, 0, 0.40], {"type": "pintle"})
    empty(root, "ExitPoint", [-0.40, 1.15, 0.0])
    collision_box(root, 0, [0.0, 0, 0.62], (2.95, 1.25, 0.62))
    collision_box(root, 1, [-0.52, 0, 2.02], (1.70, 1.22, 0.08))
    meta = dict(label="Baggage tractor", massKg=2400, maxSpeedKmh=25, maxSteerDeg=38, motorTorque=900, brakeTorque=2200,
                centerOfMass=[0.0, 0.0, 0.45], suspension=0.10, engine="diesel")
    return vid, root, meta


def baggage_cart():
    """Covered baggage cart: turntable front axle steered by the drawbar, roll-up side curtains."""
    vid = "BaggageCart"
    root = Part(vid)
    body = Part("Body")
    root.children.append(body)
    wb, track, r, w = 1.75, 1.20, 0.20, 0.12
    xf, xr = wb / 2, -wb / 2
    deck_z, roof_z = 0.55, 1.95
    L, W = 2.70, 1.50
    box(body.buf("Chassis"), [0, 0, deck_z - 0.10], (L, 1.0, 0.14))
    box(body.buf("Steel"), [0, 0, deck_z - 0.02], (L, W, 0.04))                         # deck
    for x in (L / 2 - 0.03, -L / 2 + 0.03):                                               # end walls (mesh look: frame + bars)
        for z in (deck_z + 0.03, roof_z - 0.03):
            box(body.buf("GseBlue"), [x, 0, z], (0.06, W, 0.06))
        for y in np.linspace(-W / 2 + 0.03, W / 2 - 0.03, 7):
            box(body.buf("GseBlue"), [x, y, (deck_z + roof_z) / 2], (0.04, 0.03, roof_z - deck_z))
    for x, y in ((L / 2 - 0.03, W / 2 - 0.03), (L / 2 - 0.03, -W / 2 + 0.03), (-L / 2 + 0.03, W / 2 - 0.03), (-L / 2 + 0.03, -W / 2 + 0.03)):
        box(body.buf("GseBlue"), [x, y, (deck_z + roof_z) / 2], (0.06, 0.06, roof_z - deck_z))
    side(body.buf("GseWhite"), [(-L / 2 - 0.04, roof_z), (L / 2 + 0.04, roof_z), (L / 2 + 0.04, roof_z + 0.06), (-L / 2 - 0.04, roof_z + 0.06)], -W / 2 - 0.04, W / 2 + 0.04)
    for sgn in (1, -1):                                                                  # rolled-up curtains
        cylinder(body.buf("Canvas"), [-L / 2 + 0.06, sgn * (W / 2 + 0.02), roof_z - 0.08], [L / 2 - 0.06, sgn * (W / 2 + 0.02), roof_z - 0.08], 0.07, 12)
    box(body.buf("Chassis"), [-L / 2 - 0.08, 0, 0.36], (0.16, 0.30, 0.12))               # rear hitch
    cylinder(body.buf("Steel"), [-L / 2 - 0.14, 0, 0.30], [-L / 2 - 0.14, 0, 0.48], 0.02, 10)
    empty(root, "Hitch_Rear", [-L / 2 - 0.14, 0, 0.36], {"type": "pintle"})
    # turntable front axle: wheels steer with the drawbar
    add_wheel(root, "FL", xf, track / 2, r, w, steer=True, drive=False)
    add_wheel(root, "FR", xf, -track / 2, r, w, steer=True, drive=False)
    add_wheel(root, "RL", xr, track / 2, r, w, steer=False, drive=False)
    add_wheel(root, "RR", xr, -track / 2, r, w, steer=False, drive=False)
    pivot = np.array([xf + 0.20, 0, 0.36])
    bar = Part("Drawbar", pivot)
    eye = pivot + [1.30, 0, 0]
    for sgn in (1, -1):
        rod(bar.buf("GseBlue"), pivot + [0, sgn * 0.30, 0], eye - [0.25, 0, 0], 0.025, 8)
    rod(bar.buf("GseBlue"), eye - [0.25, 0, 0], eye - [0.04, 0, 0], 0.03, 8)
    torus(bar.buf("Steel"), eye + [0.02, 0, 0], [0, 0, 1.0], 0.045, 0.014, 16, 6)       # lunette eye
    box(bar.buf("Chassis"), pivot + [0, 0, 0], (0.10, 0.70, 0.06))
    rod(bar.buf("Chassis"), eye - [0.40, 0, 0], eye - [0.40, 0, -0.08], 0.012, 6)         # grab handle post
    rod(bar.buf("Chassis"), eye - [0.48, 0, 0.08], eye - [0.32, 0, 0.08], 0.016, 8)       # grab handle
    bar.children.append(Part("DrawbarEye", eye + [0.02, 0, 0]))
    bar.children.append(Part("DrawbarHandle", eye - [0.40, 0, 0.08]))
    root.children.append(bar)
    lamps(root, body, "Brake", [[-L / 2 - 0.01, 0.62, 0.48], [-L / 2 - 0.01, -0.62, 0.48]], [-1, 0, 0], 0.03, "LensRed")
    collision_box(root, 0, [0, 0, 0.47], (L, W, 0.18))
    collision_box(root, 1, [0, 0, roof_z + 0.03], (L + 0.08, W + 0.08, 0.06))
    for k, x in enumerate((L / 2 - 0.03, -L / 2 + 0.03)):
        collision_box(root, 2 + k, [x, 0, (deck_z + roof_z) / 2], (0.06, W, roof_z - deck_z))
    meta = dict(label="Baggage cart", massKg=450, maxSpeedKmh=25, maxSteerDeg=70, motorTorque=0, brakeTorque=600,
                centerOfMass=[0.0, 0.0, 0.50], suspension=0.05, engine="none",
                cargo={"center": [0, 0, (deck_z + roof_z) / 2], "size": [L - 0.12, W - 0.04, roof_z - deck_z - 0.04], "maxMassKg": 1500},
                drawbar={"length": 1.32, "yawLimit": 80, "pitchUp": 75})
    return vid, root, meta


def pushback_tug():
    """Conventional tow-bar tug for narrowbodies: low heavy body, central cab, tow pins both ends."""
    vid = "PushbackTug"
    root = Part(vid)
    body = Part("Body")
    root.children.append(body)
    wb, track, r, w = 2.40, 1.80, 0.43, 0.30
    xf, xr = wb / 2, -wb / 2
    L, W = 4.90, 2.30
    side(body.buf("GseYellow"), [(-L / 2, 0.40), (L / 2, 0.40), (L / 2, 1.00), (L / 2 - 0.25, 1.15), (-L / 2 + 0.25, 1.15), (-L / 2, 1.00)], -W / 2, W / 2)
    box(body.buf("Chassis"), [0, 0, 0.33], (L - 0.2, W - 0.5, 0.16))
    for x in (L / 2 + 0.06, -L / 2 - 0.06):
        box(body.buf("Chassis"), [x, 0, 0.62], (0.12, W - 0.1, 0.28))                    # bumpers
    for k in range(5):                                                                   # black/yellow chevrons
        box(body.buf("Chassis"), [L / 2 + 0.125, -0.9 + k * 0.45, 0.62], (0.012, 0.18, 0.26))
    for sgn in (1, -1):                                                                  # tow pins front / rear
        x = sgn * (L / 2 + 0.22)
        box(body.buf("Chassis"), [sgn * (L / 2 + 0.16), 0, 0.50], (0.16, 0.36, 0.16))
        cylinder(body.buf("Steel"), [x, 0, 0.40], [x, 0, 0.62], 0.03, 10)
    # cab: low, glazed, offset nothing (central) for good visibility both ways
    cx0, cx1, cy, cz0, cz1 = -0.70, 0.75, 0.62, 1.15, 2.05
    box(body.buf("Chassis"), [(cx0 + cx1) / 2, 0, 1.17], (cx1 - cx0, 2 * cy, 0.04))
    cab_glass_box(body, cx0, cx1, cy, cz0, cz1, rake=0.15)
    driver_station(root, body, eye=(-0.15, 0.0, 1.95), wheel_fwd=0.45, wheel_drop=0.55, wheel_tilt=45 * D, wheel_radius=0.19,
                   dash=(0.43, 1.49))
    box(body.buf("Dash"), [0.50, 0, 1.40], (0.14, 1.1, 0.12))
    for name, x, y in (("FL", xf, track / 2), ("FR", xf, -track / 2), ("RL", xr, track / 2), ("RR", xr, -track / 2)):
        add_wheel(root, name, x, y, r, w, steer=x > 0, drive=True)
    lamps(root, body, "Head", [[L / 2 + 0.005, 0.85, 0.95], [L / 2 + 0.005, -0.85, 0.95], [-L / 2 - 0.005, 0.85, 0.95], [-L / 2 - 0.005, -0.85, 0.95]], [1, 0, 0], 0.07, "Lens")
    lamps(root, body, "Brake", [[-L / 2 - 0.005, 0.95, 0.80], [-L / 2 - 0.005, -0.95, 0.80]], [-1, 0, 0], 0.04, "LensRed")
    lamps(root, body, "Reverse", [[-L / 2 - 0.005, 0.70, 0.80]], [-1, 0, 0], 0.035, "Lens")
    beacon(root, body, [0.0, 0, 2.15])
    empty(root, "Hitch_Front", [L / 2 + 0.22, 0, 0.50], {"type": "pin"})
    empty(root, "Hitch_Rear", [-L / 2 - 0.22, 0, 0.50], {"type": "pin"})
    empty(root, "ExitPoint", [-0.1, 1.55, 0.0])
    collision_box(root, 0, [0, 0, 0.78], (L + 0.24, W, 0.76))
    collision_box(root, 1, [0.02, 0, 1.6], (1.5, 1.26, 0.9))
    meta = dict(label="Pushback tug", massKg=12000, maxSpeedKmh=25, maxSteerDeg=35, motorTorque=9000, brakeTorque=20000,
                centerOfMass=[0.0, 0.0, 0.55], suspension=0.06, engine="diesel",
                tow={"maxSpeedKmh": 10, "pushbackSpeedKmh": 5, "torqueScale": 0.6})
    return vid, root, meta


def tow_bar():
    """Narrowbody tow bar: lunette eye at the tug, tow head (with shear pins) at the nose gear."""
    vid = "TowBar"
    root = Part(vid)
    body = Part("Body")
    root.children.append(body)
    Lb = 4.60
    eye = np.array([0.0, 0, 0.50])
    head = np.array([Lb, 0, 0.40])
    torus(body.buf("Steel"), eye, [0, 0, 1.0], 0.06, 0.018, 16, 6)
    rod(body.buf("GseYellow"), eye + [0.06, 0, 0], eye + [0.8, 0, -0.02], 0.045, 10)
    for sgn in (1, -1):
        rod(body.buf("GseYellow"), eye + [0.8, 0, -0.02], head - [0.45, sgn * 0.22, -0.02], 0.04, 10)
    rod(body.buf("GseYellow"), head - [0.45, 0.24, -0.02], head - [0.45, -0.24, -0.02], 0.04, 10)
    box(body.buf("Chassis"), head - [0.22, 0, 0], (0.42, 0.32, 0.14))                      # tow head / shear-pin housing
    for sgn in (1, -1):
        cylinder(body.buf("Steel"), head + [0.0, sgn * 0.14, 0], head + [0.0, sgn * 0.20, 0], 0.035, 10)   # engagement pins
    for k in range(6):                                                                   # warning stripes
        box(body.buf("GseRed" if k % 2 else "Stripe"), eye + [1.2 + 0.25 * k, 0, 0.0], (0.12, 0.10, 0.10))
    wheel_x = 1.25
    for sgn in (1, -1):
        rod(body.buf("Chassis"), eye + [wheel_x, sgn * 0.08, -0.02], eye + [wheel_x, sgn * 0.20, -0.32], 0.02, 6)
        tyre(body.buf("Rubber"), body.buf("Hub"), np.array([wheel_x, sgn * 0.22, 0.13]), 0.26, 0.08, 16)
    rod(body.buf("Chassis"), head - [0.75, 0, -0.04], head - [0.75, 0, -0.30], 0.015, 6)    # handle post near the head
    rod(body.buf("Chassis"), head - [0.75, 0.12, -0.30], head - [0.75, -0.12, -0.30], 0.018, 8)
    empty(root, "TowHead", head.tolist())
    empty(root, "TowHeadHandle", (head - [0.75, 0, -0.30]).tolist())
    empty(root, "Eye", eye.tolist())
    collision_box(root, 0, (0.5 * (eye + head)).tolist(), (Lb, 0.5, 0.12))
    meta = dict(label="Tow bar (narrowbody)", massKg=180, length=float(np.linalg.norm(head - eye)))
    return vid, root, meta


def truck_cab(root, body, x_front, width, height, colour):
    """Cab-over truck cab: flat front, big windscreen, doors; returns the cab's rear x."""
    depth = 1.75
    x0 = x_front - depth
    hw = width / 2
    side(body.buf(colour), [(x0, 0.95), (x_front, 0.95), (x_front, 1.65), (x0, 1.65)], -hw, hw)   # lower cab
    box(body.buf("Chassis"), [x_front + 0.03, 0, 1.20], (0.06, width - 0.2, 0.40))                 # grille
    box(body.buf("Chassis"), [x_front + 0.10, 0, 0.75], (0.16, width, 0.30))                       # bumper
    cab_glass_box(body, x0, x_front, hw, 1.65, height - 0.08, rake=0.10, rear_glass=False)
    box(body.buf(colour), [x0 + 0.02, 0, (1.65 + height) / 2], (0.04, width, height - 1.65))
    for sgn in (1, -1):                                                                          # steps
        box(body.buf("Chassis"), [x_front - 0.55, sgn * (hw - 0.05), 0.60], (0.45, 0.14, 0.04))
        box(body.buf("Chassis"), [x_front - 0.55, sgn * (hw - 0.05), 0.95], (0.45, 0.14, 0.04))
        rod(body.buf("Steel"), [x_front - 0.92, sgn * (hw + 0.03), 1.0], [x_front - 0.92, sgn * (hw + 0.03), 1.9], 0.015, 6)  # grab rail
    box(body.buf("Dash"), [x_front - 0.22, 0, 1.78], (0.30, width - 0.2, 0.22))
    return x0


def fuel_truck():
    """4x2 refueller, ~20 000 l: cab-over chassis, elliptical tank, rear cabinet with hose reel."""
    vid = "FuelTruck"
    root = Part(vid)
    body = Part("Body")
    root.children.append(body)
    wb, track, rf = 4.60, 2.00, 0.53
    xf, xr = 2.6, 2.6 - wb
    x_front = xf + 1.45
    W = 2.50
    box(body.buf("Chassis"), [0.3, 0, 0.95], (7.6, 0.95, 0.25))
    x_cab = truck_cab(root, body, x_front, W, 3.0, "GseWhite")
    driver_station(root, body, eye=(x_front - 1.05, 0.55, 2.42), wheel_fwd=0.50, wheel_drop=0.50, wheel_tilt=30 * D, wheel_radius=0.23,
                   dash=(x_front - 0.37, 1.92), seat_w=0.5)
    # tank: elliptical section, dished ends
    t0, t1 = x_cab - 0.15, -3.20
    tz, trx, trz = 2.05, 1.22, 0.92
    prism(body.buf("GseWhite"), ellipse(trx, trz, 28), [0, 1, 0], [0, 0, 1], [1, 0, 0], [(t0 + t1) / 2, 0, tz], t0 - t1)
    for x in (t0, t1):
        prism(body.buf("GseWhite"), ellipse(trx * 0.92, trz * 0.92, 28), [0, 1, 0], [0, 0, 1], [1, 0, 0], [x + (0.06 if x == t0 else -0.06), 0, tz], 0.12)
    for sgn in (1, -1):                                                                  # red band
        side(body.buf("GseRed"), [(t1 + 0.1, tz - 0.30), (t0 - 0.1, tz - 0.30), (t0 - 0.1, tz - 0.12), (t1 + 0.1, tz - 0.12)], sgn * (trx + 0.005) - 0.005, sgn * (trx + 0.005) + 0.005)
    for k in range(3):                                                                   # manlids and top rail
        cylinder(body.buf("Steel"), [t0 - 1.0 - k * 1.5, 0, tz + trz - 0.02], [t0 - 1.0 - k * 1.5, 0, tz + trz + 0.08], 0.25, 16)
    for sgn in (1, -1):                                                                  # fold-down top rail on posts
        rod(body.buf("GseYellow"), [t0 - 0.3, sgn * 0.55, tz + trz + 0.35], [t1 + 0.3, sgn * 0.55, tz + trz + 0.35], 0.02, 6)
        for x in np.linspace(t1 + 0.3, t0 - 0.3, 4):
            rod(body.buf("GseYellow"), [x, sgn * 0.55, tz + trz - 0.06], [x, sgn * 0.55, tz + trz + 0.35], 0.018, 6)
    # rear cabinet with hose reel (moving part), bonding reel, deadman box
    box(body.buf("GseWhite"), [t1 - 0.45, 0, 1.75], (0.80, W - 0.1, 1.55))
    box(body.buf("Chassis"), [t1 - 0.86, 0, 1.75], (0.02, W - 0.3, 1.35))
    reel_c = np.array([t1 - 0.45, 0, 1.55])
    reel = Part("HoseReel", reel_c)
    for y in (0.75, -0.75):
        cylinder(reel.buf("Steel"), reel_c + [0, y - 0.02, 0], reel_c + [0, y + 0.02, 0], 0.42, 24)
    cylinder(reel.buf("Rubber"), reel_c + [0, -0.73, 0], reel_c + [0, 0.73, 0], 0.30, 24)
    root.children.append(reel)
    rod(body.buf("Rubber"), reel_c + [-0.20, 0.6, -0.20], reel_c + [-0.47, 0.6, -0.65], 0.04, 8)
    box(body.buf("Chassis"), reel_c + [-0.47, 0.6, -0.70], (0.08, 0.12, 0.12))           # nozzle stowed
    empty(root, "HoseNozzle", (reel_c + [-0.47, 0.6, -0.70]).tolist())
    cylinder(body.buf("GseRed"), [t1 - 0.80, -1.0, 1.0], [t1 - 0.80, -0.8, 1.0], 0.14, 14)  # bonding cable reel
    empty(root, "BondingReel", [t1 - 0.80, -0.9, 1.0])
    box(body.buf("Chassis"), [-1.0, 0, 1.08], (2.4, W - 0.2, 0.06))
    for sgn in (1, -1):                                                                  # side skirts / fenders
        side(body.buf("GseRed"), [(xr - 0.65, 0.95), (xr + 0.65, 0.95), (xr + 0.55, 1.25), (xr - 0.55, 1.25)], sgn * (W / 2 - 0.12), sgn * (W / 2 - 0.02))
        box(body.buf("Steel"), [0.0, sgn * (W / 2 - 0.10), 0.75], (2.2, 0.04, 0.30))
    add_wheel(root, "FL", xf, track / 2, rf, 0.30, steer=True, drive=False)
    add_wheel(root, "FR", xf, -track / 2, rf, 0.30, steer=True, drive=False)
    add_wheel(root, "RL", xr, track / 2 - 0.08, rf, 0.28, steer=False, drive=True, dual=True)
    add_wheel(root, "RR", xr, -track / 2 + 0.08, rf, 0.28, steer=False, drive=True, dual=True)
    lamps(root, body, "Head", [[x_front + 0.19, 0.95, 0.80], [x_front + 0.19, -0.95, 0.80]], [1, 0, 0], 0.08, "Lens")
    lamps(root, body, "Brake", [[t1 - 0.87, 1.05, 1.05], [t1 - 0.87, -1.05, 1.05]], [-1, 0, 0], 0.05, "LensRed")
    lamps(root, body, "Reverse", [[t1 - 0.87, 0.85, 1.05]], [-1, 0, 0], 0.04, "Lens")
    beacon(root, body, [x_cab + 0.35, 0, 3.08])
    empty(root, "Hitch_Rear", [t1 - 0.95, 0, 0.75], {"type": "none"})
    empty(root, "ExitPoint", [x_front - 1.05, 1.75, 0.0])
    collision_box(root, 0, [x_front - 0.88, 0, 1.95], (1.80, W, 2.1))
    collision_box(root, 1, [(t0 + t1) / 2, 0, tz], (t0 - t1, 2 * trx, 2 * trz))
    collision_box(root, 2, [0.3, 0, 0.95], (7.6, W - 0.2, 0.5))
    collision_box(root, 3, [t1 - 0.45, 0, 1.75], (0.8, W - 0.1, 1.55))
    meta = dict(label="Fuel truck (refueller)", massKg=26000, maxSpeedKmh=25, maxSteerDeg=40, motorTorque=16000, brakeTorque=40000,
                centerOfMass=[0.0, 0.0, 1.4], suspension=0.12, engine="diesel_big", capacityLitres=20000)
    return vid, root, meta


def catering_truck():
    """High-loader catering truck: cab-over chassis, scissor lift, insulated box with front platform."""
    vid = "CateringTruck"
    root = Part(vid)
    body = Part("Body")
    root.children.append(body)
    wb, track, rf = 4.30, 2.00, 0.50
    xf, xr = 2.5, 2.5 - wb
    x_front = xf + 1.40
    W = 2.50
    box(body.buf("Chassis"), [0.0, 0, 0.92], (8.4, 0.95, 0.25))
    x_cab = truck_cab(root, body, x_front, W, 2.95, "GseWhite")
    driver_station(root, body, eye=(x_front - 1.05, 0.55, 2.40), wheel_fwd=0.50, wheel_drop=0.50, wheel_tilt=30 * D, wheel_radius=0.23,
                   dash=(x_front - 0.37, 1.92), seat_w=0.5)
    # subframe that carries the scissor
    sub_z = 1.18
    box(body.buf("Steel"), [-1.45, 0, sub_z], (5.6, 2.2, 0.12))
    # box (moves up), with its front platform
    bx0, bx1 = -4.25, x_cab - 0.25        # box rear / front
    stow = 1.38                           # box floor height when lowered
    box_c = np.array([0.5 * (bx0 + bx1), 0, stow])
    lift = Part("LiftBox", box_c)
    bl = bx1 - bx0
    side(lift.buf("GseWhite"), [(bx0, stow + 0.08), (bx1, stow + 0.08), (bx1, stow + 2.45), (bx0, stow + 2.45)], -W / 2, W / 2)
    box(lift.buf("Steel"), [box_c[0], 0, stow + 0.04], (bl, W, 0.08))
    box(lift.buf("GseBlue"), [box_c[0], 0, stow + 1.25], (bl + 0.01, W + 0.01, 0.25))       # stripe
    box(lift.buf("Chassis"), [bx1 + 0.01, 0, stow + 1.25], (0.02, 1.0, 2.0))               # front door (to the aircraft)
    plat = Part("Platform", [bx1, 0, stow])
    box(plat.buf("Steel"), [bx1 + 0.45, 0, stow + 0.04], (0.90, W - 0.1, 0.06))
    for sgn in (1, -1):
        rod(plat.buf("GseYellow"), [bx1 + 0.05, sgn * (W / 2 - 0.1), stow + 1.1], [bx1 + 0.85, sgn * (W / 2 - 0.1), stow + 1.1], 0.025, 6)
        for x in (bx1 + 0.1, bx1 + 0.85):
            rod(plat.buf("GseYellow"), [x, sgn * (W / 2 - 0.1), stow + 0.06], [x, sgn * (W / 2 - 0.1), stow + 1.1], 0.025, 6)
    plat.children.append(Part("PlatformEdge", [bx1 + 0.90, 0, stow + 0.07]))
    lift.children.append(plat)
    for sgn in (1, -1):
        lamps(lift, lift, "Work", [[bx1 + 0.02, sgn * 1.0, stow + 2.30]], [1, 0, 0], 0.05, "Lens")
    root.children.append(lift)
    # scissor arms: A from the lower front pivot to the upper rear; B crosses it at the middle
    arm_len = 4.6
    pivot_x = 0.25
    alpha0 = math.asin(min(0.99, (stow - sub_z - 0.12) / arm_len))
    for sgn, tag in ((1, "L"), (-1, "R")):
        y = sgn * 0.85
        pa = np.array([pivot_x, y, sub_z + 0.06])
        da = np.array([-math.cos(alpha0), 0, math.sin(alpha0)])
        arm_a = Part(f"ScissorA_{tag}", pa)
        box(arm_a.buf("GseYellow"), pa + da * arm_len / 2, (arm_len, 0.10, 0.14),
            axes=[da, [0, 1, 0], np.cross(da, [0, 1, 0])])
        arm_a.children.append(Part("End", pa + da * arm_len))
        root.children.append(arm_a)
        mid = pa + da * arm_len / 2 + [0, -sgn * 0.12, 0]
        db = np.array([math.cos(alpha0), 0, math.sin(alpha0)])
        arm_b = Part(f"ScissorB_{tag}", mid)
        box(arm_b.buf("GseYellow"), mid, (arm_len, 0.10, 0.14), axes=[db, [0, 1, 0], np.cross(db, [0, 1, 0])])
        arm_b.children.append(Part("End", mid + db * arm_len / 2))
        root.children.append(arm_b)
    add_wheel(root, "FL", xf, track / 2, rf, 0.30, steer=True, drive=False)
    add_wheel(root, "FR", xf, -track / 2, rf, 0.30, steer=True, drive=False)
    add_wheel(root, "RL", xr, track / 2 - 0.08, rf, 0.28, steer=False, drive=True, dual=True)
    add_wheel(root, "RR", xr, -track / 2 + 0.08, rf, 0.28, steer=False, drive=True, dual=True)
    lamps(root, body, "Head", [[x_front + 0.19, 0.95, 0.80], [x_front + 0.19, -0.95, 0.80]], [1, 0, 0], 0.08, "Lens")
    lamps(root, body, "Brake", [[bx0 - 0.01, 1.05, 1.05], [bx0 - 0.01, -1.05, 1.05]], [-1, 0, 0], 0.05, "LensRed")
    lamps(root, body, "Reverse", [[bx0 - 0.01, 0.85, 1.05]], [-1, 0, 0], 0.04, "Lens")
    box(body.buf("Chassis"), [bx0 + 0.05, 0, 0.98], (0.12, W - 0.2, 0.12))
    beacon(root, body, [x_cab + 0.35, 0, 3.03])
    # lift controls on the platform (front of the box), used standing on the platform
    for name, dy, mat in (("LiftUp", 0.30, "Btn_Green"), ("LiftDown", 0.18, "Btn_Grey"), ("PlatformOut", -0.18, "Btn_Grey"), ("PlatformIn", -0.30, "Btn_Grey")):
        c = np.array([bx1 + 0.02, -0.75 + dy, stow + 1.05])
        p = Part(f"Btn_{name}", c)
        cylinder(p.buf(mat), c, c + [0.012, 0, 0], 0.018, 14)
        p.children.append(Part("Out", c + [0.05, 0, 0]))
        lift.children.append(p)
    empty(root, "ExitPoint", [x_front - 1.05, 1.75, 0.0])
    collision_box(root, 0, [x_front - 0.88, 0, 1.95], (1.80, W, 2.0))
    collision_box(root, 1, [0.0, 0, 0.95], (8.4, W - 0.2, 0.5))
    meta = dict(label="Catering truck (high-loader)", massKg=14000, maxSpeedKmh=25, maxSteerDeg=40, motorTorque=10000, brakeTorque=30000,
                centerOfMass=[0.0, 0.0, 1.3], suspension=0.12, engine="diesel_big",
                lift={"armLength": arm_len, "pivot": [pivot_x, 0, sub_z + 0.06], "stowedFloor": stow, "maxFloor": 5.60,
                      "speed": 0.12, "platformTravel": 0.90, "maxSpeedKmhRaised": 0})
    return vid, root, meta


ALL = [baggage_tractor, baggage_cart, pushback_tug, tow_bar, fuel_truck, catering_truck]
