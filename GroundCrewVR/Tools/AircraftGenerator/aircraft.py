"""Procedural narrowbody airliner builder (737-class and A320-class from specs.py).

Produces a Part tree per level of detail. See meshlib.py for the coordinate frame
(s aft, y left, z up; metres).
"""
import math

import numpy as np

from meshlib import (Part, box, clip_polygon, cylinder, fan, grid, grid_normals, lathe, normalize,
                     polygon, profile_normals, rounded_rect)

D = math.pi / 180.0

LODS = {
    0: dict(ntheta=56, cyl_step=0.55, nose_n=26, tail_n=24, foil=18, lathe=40, small=16, tyre=28,
            windows=True, openings=True, interior=True, corner=3),
    1: dict(ntheta=30, cyl_step=1.2, nose_n=12, tail_n=11, foil=9, lathe=20, small=10, tyre=14,
            windows=True, openings=True, interior=False, corner=1),
    2: dict(ntheta=14, cyl_step=3.5, nose_n=6, tail_n=6, foil=5, lathe=10, small=6, tyre=8,
            windows=False, openings=False, interior=False, corner=1),
}

# Materials (name -> (rgb, metallic, roughness, emissive)). Generic white livery: no airline marks.
MATERIALS = {
    "Paint": ((0.93, 0.94, 0.95), 0.0, 0.32, None),
    "WingGrey": ((0.66, 0.68, 0.70), 0.15, 0.45, None),
    "Metal": ((0.78, 0.79, 0.80), 0.9, 0.28, None),
    "DarkMetal": ((0.20, 0.21, 0.23), 0.6, 0.45, None),
    "Fan": ((0.10, 0.10, 0.11), 0.7, 0.4, None),
    "Exhaust": ((0.16, 0.15, 0.14), 0.6, 0.6, None),
    "Rubber": ((0.05, 0.05, 0.05), 0.0, 0.9, None),
    "Window": ((0.04, 0.05, 0.07), 0.2, 0.08, None),
    "Seal": ((0.22, 0.23, 0.25), 0.0, 0.7, None),
    "Lining": ((0.88, 0.87, 0.84), 0.0, 0.6, None),
    "WindowInner": ((0.72, 0.84, 0.97), 0.0, 0.3, (0.45, 0.55, 0.65)),
    "Carpet": ((0.20, 0.24, 0.33), 0.0, 0.95, None),
    "Seat": ((0.12, 0.16, 0.30), 0.0, 0.85, None),
    "SeatHead": ((0.85, 0.86, 0.88), 0.0, 0.8, None),
    "Plastic": ((0.90, 0.90, 0.88), 0.0, 0.5, None),
    "Galley": ((0.62, 0.64, 0.66), 0.8, 0.35, None),
    "CabinLight": ((1.0, 0.97, 0.9), 0.0, 0.5, (1.0, 0.95, 0.85)),
    "Cockpit": ((0.17, 0.18, 0.20), 0.1, 0.6, None),
    "Display": ((0.05, 0.10, 0.18), 0.0, 0.2, (0.10, 0.22, 0.40)),
    "HoldLining": ((0.36, 0.37, 0.38), 0.0, 0.9, None),
    # Exterior lights: separate materials so the game can switch emission per light type.
    "Light_Beacon": ((0.9, 0.05, 0.03), 0.0, 0.3, (1.0, 0.05, 0.02)),
    "Light_NavRed": ((0.9, 0.05, 0.05), 0.0, 0.3, (1.0, 0.08, 0.05)),
    "Light_NavGreen": ((0.05, 0.85, 0.2), 0.0, 0.3, (0.08, 1.0, 0.25)),
    "Light_White": ((0.95, 0.95, 0.95), 0.0, 0.3, (1.0, 1.0, 1.0)),
}


# ====================================================================== fuselage

def pchip(xs, ys):
    """Shape-preserving (monotone) cubic interpolation, sampled densely; returns (x, y) arrays."""
    x = np.asarray(xs, dtype=float)
    y = np.asarray(ys, dtype=float)
    if len(x) < 3:
        return x, y
    h = np.diff(x)
    d = np.diff(y) / h
    m = np.zeros_like(y)
    for k in range(1, len(x) - 1):
        if d[k - 1] * d[k] > 0:
            w1, w2 = 2 * h[k] + h[k - 1], h[k] + 2 * h[k - 1]
            m[k] = (w1 + w2) / (w1 / d[k - 1] + w2 / d[k])
    m[0], m[-1] = d[0], d[-1]
    xd, yd = [], []
    for k in range(len(x) - 1):
        t = np.linspace(0, 1, 12, endpoint=False)
        h00, h10, h01, h11 = 2 * t ** 3 - 3 * t ** 2 + 1, t ** 3 - 2 * t ** 2 + t, -2 * t ** 3 + 3 * t ** 2, t ** 3 - t ** 2
        xd.append(x[k] + t * h[k])
        yd.append(h00 * y[k] + h10 * h[k] * m[k] + h01 * y[k + 1] + h11 * h[k] * m[k + 1])
    xd.append([x[-1]])
    yd.append([y[-1]])
    return np.concatenate(xd), np.concatenate(yd)


class Fuselage:
    """Fuselage outline from the traced profile tables in specs.py (level aircraft)."""

    def __init__(self, sp):
        self.sp = sp
        self.L = sp["length"]
        self.W2 = sp["width"] / 2.0
        self.H = sp["height"]
        self.zb = sp["keel_z"]
        self.wall = sp["wall"]
        pr = sp["profile"]
        nose = np.asarray(pr["nose"], dtype=float)
        self.Ln = float(nose[-1, 0])
        self._nose = [pchip(nose[:, 0], nose[:, k]) for k in (1, 2, 3)]
        tb = np.asarray(pr["tail_bottom"], dtype=float)
        tt = np.asarray(pr["tail_top"], dtype=float)
        th = np.asarray(pr["tail_half"], dtype=float)
        self.Lt = float(min(tb[0, 0], tt[0, 0], th[0, 0]))
        self._tail = [pchip(tt[:, 0], tt[:, 1]), pchip(tb[:, 0], tb[:, 1]), pchip(th[:, 0], th[:, 1])]
        top0, bot0, _ = (np.interp(0.0, x, y) for x, y in self._nose)
        self.tip_z = self.zb + 0.5 * (top0 + bot0)

    def section(self, s):
        """(zc, half-width a, half-height h) at station s."""
        if s <= self.Ln:
            top, bot, a = (float(np.interp(s, x, y)) for x, y in self._nose)
        else:
            top, bot, a = self.H, 0.0, self.W2
        if s >= self.Lt:
            tops = [(float(np.interp(s, x, y)) if s >= x[0] else d) for (x, y), d in zip(self._tail, (self.H, 0.0, self.W2))]
            top, bot, a = tops
        top, bot = self.zb + top, self.zb + bot
        return (top + bot) / 2.0, max(a, 0.0), max((top - bot) / 2.0, 0.0)

    def point(self, s, theta):
        zc, a, h = self.section(s)
        return np.array([s, a * math.sin(theta), zc + h * math.cos(theta)])

    def normal(self, s, theta):
        p = self.point(s, theta)
        ds = self.point(s + 0.01, theta) - self.point(s - 0.01, theta)
        dt = self.point(s, theta + 0.002) - self.point(s, theta - 0.002)
        n = np.cross(ds, dt)
        zc = self.section(s)[0]
        if np.dot(n, p - np.array([s, 0.0, zc])) < 0:
            n = -n
        return normalize(n)

    def theta_for_z(self, s, z, side):
        zc, a, h = self.section(s)
        th = math.acos(float(np.clip((z - zc) / h, -1.0, 1.0)))
        return th if side == "L" else 2 * math.pi - th

    def inner_halfwidth(self, s, z):
        zc, a, h = self.section(s)
        a, h = a - self.wall, h - self.wall
        if h <= 0 or a <= 0:
            return 0.0
        q = 1.0 - ((z - zc) / h) ** 2
        return a * math.sqrt(q) if q > 0 else 0.0

    def inner_ring(self, s, n=64):
        zc, a, h = self.section(s)
        t = np.linspace(0, 2 * math.pi, n, endpoint=False)
        return np.stack([np.full(n, s), (a - self.wall) * np.sin(t), zc + (h - self.wall) * np.cos(t)], axis=1)


def merged_params(base, keep, tol):
    """Sorted union of base and keep values; base values within tol of a keep value are dropped."""
    keep = np.unique(np.round(np.asarray(keep, dtype=float), 4))
    base = np.asarray(base, dtype=float)
    if len(keep):
        base = base[np.min(np.abs(base[:, None] - keep[None, :]), axis=1) > tol]
    return np.unique(np.concatenate([base, keep]))


def door_theta_range(fus, d):
    smid = 0.5 * (d["s"][0] + d["s"][1])
    t_top = fus.theta_for_z(smid, d["z"][1], d["side"])
    t_sill = fus.theta_for_z(smid, d["z"][0], d["side"])
    return min(t_top, t_sill), max(t_top, t_sill), t_top


def build_fuselage(fus, sp, lod, airframe, parts, interior):
    L, Ln, Lt = fus.L, fus.Ln, fus.Lt
    doors = sp["doors"] if lod["openings"] else []
    wall = fus.wall

    # --- stations (s) and angles (theta), with door edges inserted exactly
    nose = Ln * np.linspace(0, 1, lod["nose_n"] + 1)[1:] ** 1.7
    cyl = np.arange(Ln, Lt, lod["cyl_step"])
    tail = Lt + (L - Lt) * np.linspace(0, 1, lod["tail_n"] + 1) ** 0.9
    keep_s = [d["s"][0] for d in doors] + [d["s"][1] for d in doors]
    if lod["openings"]:
        keep_s += [sp["lining_start"], sp["aft_bulkhead"]]
    S = merged_params(np.concatenate([[0.02], nose, cyl, tail]), keep_s, 0.06)
    S = S[(S >= 0.02) & (S <= L)]

    T_base = np.linspace(0, 2 * math.pi, lod["ntheta"], endpoint=False)
    keep_t, ranges = [], []
    for d in doors:
        t0, t1, _ = door_theta_range(fus, d)
        keep_t += [t0, t1]
        ranges.append((t0, t1))
    T = merged_params(T_base, keep_t, 0.6 * D)
    T = T[T < 2 * math.pi - 1e-6]

    nu, nv = len(S), len(T)
    rings = np.zeros((nu, nv, 3))
    hint = np.zeros((nu, nv, 3))
    for i, s in enumerate(S):
        zc, a, h = fus.section(s)
        rings[i, :, 0] = s
        rings[i, :, 1] = a * np.sin(T)
        rings[i, :, 2] = zc + h * np.cos(T)
        hint[i, :, 1] = rings[i, :, 1]
        hint[i, :, 2] = rings[i, :, 2] - zc
    N = grid_normals(rings, closed=True, outward_hint=hint)

    # --- door openings
    mask = np.zeros((nu - 1, nv), dtype=bool)
    holes = []
    for d, (t0, t1) in zip(doors, ranges):
        i0, i1 = int(np.argmin(np.abs(S - d["s"][0]))), int(np.argmin(np.abs(S - d["s"][1])))
        j0, j1 = int(np.argmin(np.abs(T - t0))), int(np.argmin(np.abs(T - t1)))
        mask[i0:i1, j0:j1] = True
        holes.append((d, i0, i1, j0, j1))

    uv = np.stack(np.meshgrid(S / L, np.arange(nv) / nv, indexing="ij"), axis=-1)
    grid(airframe.buf("Paint"), rings, N, closed=True, mask=mask, uv=uv)
    fan(airframe.buf("Paint"), rings[0], [0.0, 0.0, fus.tip_z], [-1.0, 0.0, 0.0])
    zc_end = fus.section(L)[0]
    fan(airframe.buf("Exhaust"), rings[-1], [L + 0.01, 0.0, zc_end], [1.0, 0.0, 0.0])

    if not lod["openings"]:
        return {"S": S, "T": T, "rings": rings, "N": N}

    # --- inner lining (so open doors show a cabin / hold, not the sky)
    inner = rings - wall * N
    il0 = int(np.argmin(np.abs(S - sp["lining_start"])))
    il1 = int(np.argmin(np.abs(S - sp["aft_bulkhead"])))
    grid(airframe.buf("Lining"), inner[il0:il1 + 1], -N[il0:il1 + 1], closed=True, mask=mask[il0:il1])
    polygon(airframe.buf("Lining"), inner[il0], [1.0, 0.0, 0.0])
    polygon(airframe.buf("Lining"), inner[il1], [-1.0, 0.0, 0.0])

    # --- door jambs: close the gap between skin and lining around each opening
    jamb = airframe.buf("Seal")
    for d, i0, i1, j0, j1 in holes:
        centre = rings[(i0 + i1) // 2, (j0 + j1) // 2]
        edges = []
        for i in range(i0, i1):
            edges += [((i, j0), (i + 1, j0)), ((i, j1), (i + 1, j1))]
        for j in range(j0, j1):
            edges += [((i0, j), (i0, j + 1)), ((i1, j), (i1, j + 1))]
        for (ia, ja), (ib, jb) in edges:
            p, q = rings[ia, ja], rings[ib, jb]
            quad = [p, q, inner[ib, jb], inner[ia, ja]]
            ref = normalize(centre - 0.5 * (p + q))
            jamb.add(quad, [(0, 1, 2), (0, 2, 3)], ref)

    for d in doors:
        parts.append(build_door(fus, sp, d, lod))
    return {"S": S, "T": T, "rings": rings, "N": N, "inner": inner}


def surface_patch(fus, s0, s1, t0, t1, ns, nt, offset=0.0):
    S = np.linspace(s0, s1, ns)
    T = np.linspace(t0, t1, nt)
    P = np.zeros((ns, nt, 3))
    N = np.zeros((ns, nt, 3))
    for i, s in enumerate(S):
        for j, t in enumerate(T):
            n = fus.normal(s, t)
            P[i, j] = fus.point(s, t) + n * offset
            N[i, j] = n
    return P, N


def tangent_frame(fus, s, theta):
    p = fus.point(s, theta)
    n = fus.normal(s, theta)
    ts = normalize(fus.point(s + 0.02, theta) - fus.point(s - 0.02, theta))
    tu = normalize(np.cross(n, ts))
    if tu[2] < 0:
        tu = -tu
    return p, n, ts, tu


def window_patch(buf, centre, n, ts, tu, w, h, offset, ref_sign, corner_segments):
    outline = rounded_rect(w, h, min(w, h) * 0.38, corner_segments)
    pts = centre + n * offset + outline[:, 0:1] * ts + outline[:, 1:2] * tu
    polygon(buf, pts, n * ref_sign)


def build_door(fus, sp, d, lod):
    """A door panel that exactly fills its opening (with a 12 mm seam), pivoted at its hinge."""
    wall = fus.wall
    t0, t1, t_top = door_theta_range(fus, d)
    s0, s1 = d["s"]
    gap = 0.012
    r_local = fus.section(0.5 * (s0 + s1))[2]
    dt = gap / max(r_local, 0.5)
    ns = max(3, int((s1 - s0) / 0.25) + 2)
    nt = max(3, int((t1 - t0) * r_local / 0.25) + 2)
    P, N = surface_patch(fus, s0 + gap, s1 - gap, t0 + dt, t1 - dt, ns, nt)
    Pin = P - N * wall

    smid = 0.5 * (s0 + s1)
    tmid = 0.5 * (t0 + t1)
    kind = d["kind"]
    outward = fus.normal(smid, tmid)
    if kind == "pax":
        pivot = fus.point(s0, tmid) + fus.normal(s0, tmid) * 0.10
        axis = np.array([0.0, 0.0, 1.0])
        test = fus.point(s1, tmid)               # aft edge must swing outward first
        desired = outward
    else:
        pivot = fus.point(smid, t_top)
        pivot = pivot + fus.normal(smid, t_top) * (0.04 if d["opens"] == "out" else -wall)
        axis = np.array([1.0, 0.0, 0.0])
        test = fus.point(smid, tmid)
        desired = outward if d["opens"] == "out" else -outward

    part = Part(d["name"], pivot, extras={
        "kind": "door", "doorType": kind, "opens": d["opens"], "side": d["side"],
        "removable": bool(d.get("removable", False)),
        "hinge": {"axis": axis, "test": test, "desired": desired, "min": 0.0, "max": float(d["max"])},
    })
    grid(part.buf("Paint"), P, N, closed=False)
    grid(part.buf("Lining"), Pin, -N, closed=False)
    seal = part.buf("Seal")
    centre = P[ns // 2, nt // 2]
    border = [(i, 0) for i in range(ns)] + [(ns - 1, j) for j in range(1, nt)] + \
             [(i, nt - 1) for i in range(ns - 2, -1, -1)] + [(0, j) for j in range(nt - 2, 0, -1)]
    for k in range(len(border)):
        a, b = border[k], border[(k + 1) % len(border)]
        quad = [P[a], P[b], Pin[b], Pin[a]]
        seal.add(quad, [(0, 1, 2), (0, 2, 3)], normalize(0.5 * (P[a] + P[b]) - centre))

    if lod["windows"] and kind in ("pax", "overwing"):
        wz = sp["window_z"]
        if d["z"][0] < wz < d["z"][1]:
            th = fus.theta_for_z(smid, wz, d["side"])
            c, n, ts, tu = tangent_frame(fus, smid, th)
            size = sp["window_size"] if kind == "overwing" else (0.2, 0.25)
            window_patch(part.buf("Window"), c, n, ts, tu, size[0], size[1], 0.004, 1.0, lod["corner"])
            window_patch(part.buf("WindowInner"), c - n * wall, n, ts, tu, size[0] * 1.2, size[1] * 1.15, -0.004, -1.0, lod["corner"])
    return part


POST_THETA = 3.5 * D   # half-width of the windscreen centre post, as an angle from the crown


def resolve_pane_corner(fus, corner, side):
    """("c", z): point on the centre post at height z; ("p", s, z): side point at station s, height z.
    Returns (s, theta)."""
    if corner[0] == "c":
        z = corner[1]
        lo, hi = 0.05, fus.Ln
        for _ in range(40):     # crest height rises monotonically along the nose
            mid = 0.5 * (lo + hi)
            zc, a, h = fus.section(mid)
            if zc + h * math.cos(POST_THETA) < z:
                lo = mid
            else:
                hi = mid
        s, theta = 0.5 * (lo + hi), POST_THETA
    else:
        s, z = corner[1], corner[2]
        theta = fus.theta_for_z(s, z, "L")
    return s, theta if side == "L" else 2 * math.pi - theta


def build_windows(fus, sp, lod, airframe):
    if not lod["windows"]:
        return []
    w, h = sp["window_size"]
    s_a, s_b = sp["window_range"]
    centres = []
    for side in ("L", "R"):
        blocked = [d for d in sp["doors"] if d["side"] == side and d["kind"] in ("pax", "overwing")]
        s = s_a
        while s <= s_b + 1e-6:
            if all(not (d["s"][0] - 0.22 < s < d["s"][1] + 0.22) for d in blocked):
                th = fus.theta_for_z(s, sp["window_z"], side)
                c, n, ts, tu = tangent_frame(fus, s, th)
                window_patch(airframe.buf("Window"), c, n, ts, tu, w, h, 0.004, 1.0, lod["corner"])
                if lod["openings"]:
                    window_patch(airframe.buf("WindowInner"), c - n * fus.wall, n, ts, tu, w * 1.25, h * 1.15, -0.005, -1.0, lod["corner"])
                centres.append((side, s))
            s += sp["window_pitch"]
    # flight-deck windows
    for pane in sp["windshield"]:
        for side in ("L", "R"):
            corners = [resolve_pane_corner(fus, c, side) for c in pane]
            n_s, n_t = (9, 9) if lod["corner"] > 1 else (4, 4)
            P = np.zeros((n_s, n_t, 3))
            N = np.zeros((n_s, n_t, 3))
            (sa, ta), (sb, tb), (sc, tc), (sd, td) = corners  # front-top, aft-top, aft-bottom, front-bottom
            for i, u in enumerate(np.linspace(0, 1, n_s)):
                for j, v in enumerate(np.linspace(0, 1, n_t)):
                    s = (1 - u) * ((1 - v) * sa + v * sd) + u * ((1 - v) * sb + v * sc)
                    t = (1 - u) * ((1 - v) * ta + v * td) + u * ((1 - v) * tb + v * tc)
                    n = fus.normal(s, t)
                    P[i, j] = fus.point(s, t) + n * 0.010
                    N[i, j] = n
            grid(airframe.buf("Window"), P, N, closed=False)
            if lod["openings"]:
                inner_pane = P - N * (fus.wall + 0.016)
                grid(airframe.buf("WindowInner"), inner_pane, -N, closed=False)
    return centres


def build_belly_fairing(fus, sp, lod, airframe):
    bf = sp["belly_fairing"]
    s0, s1 = bf["s"]
    n_s = max(6, lod["nose_n"])
    n_t = max(12, lod["ntheta"] // 2)
    S = np.linspace(s0, s1, n_s)
    T = np.linspace(0, 2 * math.pi, n_t, endpoint=False)
    rings = np.zeros((n_s, n_t, 3))
    hint = np.zeros_like(rings)
    for i, s in enumerate(S):
        u = (s - s0) / (s1 - s0)
        bump = max(math.sin(math.pi * u), 0.0) ** 0.45
        a = (fus.W2 + bf["extra_width"]) * bump
        h = bf["half_height"] * bump
        rings[i, :, 0] = s
        rings[i, :, 1] = a * np.sin(T)
        rings[i, :, 2] = bf["center_z"] + h * np.cos(T)
        hint[i, :, 1] = np.sin(T)
        hint[i, :, 2] = np.cos(T)
    grid(airframe.buf("Paint"), rings, grid_normals(rings, True, hint), closed=True)


# ====================================================================== lifting surfaces

def foil_loop(n, tc, x0=0.0, x1=1.0, camber=0.0):
    beta = np.linspace(0, math.pi, 2 * n + 1)
    xs = 0.5 * (1 - np.cos(beta))
    xs = xs[(xs > x0 + 1e-4) & (xs < x1 - 1e-4)]
    xs = np.concatenate([[x0], xs, [x1]])
    yt = 5 * tc * (0.2969 * np.sqrt(xs) - 0.1260 * xs - 0.3516 * xs ** 2 + 0.2843 * xs ** 3 - 0.1036 * xs ** 4)
    yt = np.where(xs > 0.5, np.maximum(yt, 0.0025), yt)
    yc = 4 * camber * xs * (1 - xs)
    upper = np.stack([xs, yc + yt], axis=1)
    lower = np.stack([xs, yc - yt], axis=1)
    if x0 <= 1e-9:
        return np.vstack([upper[::-1], lower[1:]])
    return np.vstack([upper[::-1], lower])


class Planform:
    """Piecewise-linear lifting-surface definition along a span coordinate (y for wings, z for fins)."""

    def __init__(self, stations):
        # stations: list of dicts with span, le(3), chord, tc, twist, roll
        self.st = sorted(stations, key=lambda d: d["span"])

    def at(self, span):
        st = self.st
        if span <= st[0]["span"]:
            return dict(st[0], span=span)
        for a, b in zip(st, st[1:]):
            if span <= b["span"]:
                u = (span - a["span"]) / (b["span"] - a["span"])
                return {
                    "span": span,
                    "le": (1 - u) * np.asarray(a["le"]) + u * np.asarray(b["le"]),
                    "chord": (1 - u) * a["chord"] + u * b["chord"],
                    "tc": (1 - u) * a["tc"] + u * b["tc"],
                    "twist": (1 - u) * a["twist"] + u * b["twist"],
                    "roll": (1 - u) * a["roll"] + u * b["roll"],
                    "camber": (1 - u) * a.get("camber", 0) + u * b.get("camber", 0),
                }
        return dict(st[-1], span=span)


def section_frame(st):
    phi, tw = st["roll"] * D, st["twist"] * D
    cd0 = np.array([1.0, 0.0, 0.0])
    up0 = np.array([0.0, -math.sin(phi), math.cos(phi)])
    cd = math.cos(tw) * cd0 + math.sin(tw) * up0
    up = -math.sin(tw) * cd0 + math.cos(tw) * up0
    span_dir = np.array([0.0, math.cos(phi), math.sin(phi)])
    return cd, up, span_dir


def place_section(loop, st):
    cd, up, _ = section_frame(st)
    c = st["chord"]
    P = np.asarray(st["le"]) + loop[:, 0:1] * c * cd + loop[:, 1:2] * c * up
    n2 = profile_normals(loop, closed=True)
    R = n2[:, 0:1] * cd + n2[:, 1:2] * up
    return P, R


def loft_sections(buf, stations, n, x0, x1, cap_start=False, cap_end=True):
    rings, refs = [], []
    for st in stations:
        loop = foil_loop(n, st["tc"], x0, x1, st.get("camber", 0.0))
        P, R = place_section(loop, st)
        rings.append(P)
        refs.append(R)
    rings, refs = np.array(rings), np.array(refs)
    grid(buf, rings, refs, closed=True)
    if cap_start:
        _, _, sd = section_frame(stations[0])
        fan(buf, rings[0], rings[0].mean(axis=0), -sd)
    if cap_end:
        _, _, sd = section_frame(stations[-1])
        fan(buf, rings[-1], rings[-1].mean(axis=0), sd)
    return rings


def hinge_point(st, h):
    cd, up, _ = section_frame(st)
    return np.asarray(st["le"]) + h * st["chord"] * cd + 4 * st.get("camber", 0) * h * (1 - h) * st["chord"] * up


def build_surface(pf, spans, controls, hinge, lod, body_buf, fixed_buf, parent_parts, prefix, side_sign,
                  ranges, cap_tip=True):
    """Main box [0, hinge] + trailing-edge pieces. controls: (name, span0, span1, kind)."""
    n = lod["foil"]
    cuts = sorted(set([spans[0], spans[-1]] + [c[1] for c in controls] + [c[2] for c in controls] + list(spans)))
    stations = [pf.at(s) for s in cuts]
    loft_sections(body_buf, stations, n, 0.0, hinge, cap_start=False, cap_end=cap_tip)

    covered = sorted([(c[1], c[2]) for c in controls])
    fixed, cursor = [], spans[0]
    for a, b in covered:
        if a > cursor + 1e-6:
            fixed.append((cursor, a))
        cursor = max(cursor, b)
    if spans[-1] > cursor + 1e-6:
        fixed.append((cursor, spans[-1]))
    for a, b in fixed:
        seg = [pf.at(s) for s in cuts if a - 1e-6 <= s <= b + 1e-6]
        if len(seg) < 2:
            continue
        loft_sections(fixed_buf, seg, max(3, n // 2), hinge, 1.0, cap_start=False, cap_end=cap_tip and b >= spans[-1] - 1e-6)

    made = []
    for name, a, b, kind in controls:
        seg = [pf.at(s) for s in cuts if a - 1e-6 <= s <= b + 1e-6]
        h0, h1 = hinge_point(seg[0], hinge), hinge_point(seg[-1], hinge)
        cd, up, _ = section_frame(seg[len(seg) // 2])
        te = 0.5 * (np.asarray(seg[0]["le"]) + np.asarray(seg[-1]["le"])) + 0.5 * (seg[0]["chord"] + seg[-1]["chord"]) * cd
        lo, hi = ranges[kind]
        part = Part(f"{prefix}{name}", h0, extras={
            "kind": "control", "surface": kind,
            "hinge": {"axis": h1 - h0, "test": te, "desired": -up if kind != "rudder" else np.array([0.0, side_sign, 0.0]),
                      "min": lo, "max": hi},
        })
        loft_sections(part.buf(fixed_buf_material(kind)), seg, max(3, n // 2), hinge + 0.004, 1.0, cap_start=True, cap_end=True)
        made.append(part)
    parent_parts.extend(made)


def fixed_buf_material(kind):
    return "Paint" if kind == "rudder" else "WingGrey"


CONTROL_RANGES = {"flap": (0.0, 40.0), "aileron": (-20.0, 20.0), "elevator": (-25.0, 20.0), "rudder": (-27.0, 27.0)}


def wing_tip_y(w):
    return w["stations"][-1][0]


def wing_planform(w):
    """Planform from the traced (y, LE, TE, t/c, twist) stations; positive twist = washout (nose down)."""
    tand = math.tan(w["dihedral"] * D)
    st = [{"span": y, "le": np.array([le_s, y, w["root_z"] + y * tand]), "chord": te_s - le_s, "tc": tc,
           "twist": -tw, "roll": w["dihedral"], "camber": 0.02}
          for y, le_s, te_s, tc, tw in w["stations"]]
    return Planform(st)


def winglet_stations(w, pf, lod):
    """Blended winglet (737): the tip section curves up through an arc, then a straight swept blade."""
    wl = w["tip"]
    tip = pf.at(wing_tip_y(w))
    phi0, phi1 = w["dihedral"] * D, wl["cant"] * D
    R = wl["radius"]
    p0 = np.asarray(tip["le"])
    tan_sw = math.tan(wl["le_sweep"] * D)
    n_arc = {0: 5, 1: 3, 2: 1}[lod["key"]]
    out = []
    arc_len_total = R * (phi1 - phi0)
    for k in range(1, n_arc + 1):
        phi = phi0 + (phi1 - phi0) * k / n_arc
        dy = R * (math.sin(phi) - math.sin(phi0))
        dz = -R * (math.cos(phi) - math.cos(phi0))
        arc = R * (phi - phi0)
        u = arc / arc_len_total
        out.append({"le": p0 + np.array([arc * tan_sw * 0.6, dy, dz]),
                    "chord": tip["chord"] + (wl["root_chord"] - tip["chord"]) * u,
                    "tc": tip["tc"] + (wl["tc"] - tip["tc"]) * u, "twist": tip["twist"], "roll": math.degrees(phi), "camber": 0.01})
    end = out[-1]
    rise_left = wl["height"] - (end["le"][2] - p0[2])
    length = max(rise_left, 0.2) / math.sin(phi1)
    e = np.array([0.0, math.cos(phi1), math.sin(phi1)])
    for f in (0.5, 1.0):
        out.append({"le": end["le"] + e * length * f + np.array([length * f * tan_sw, 0.0, 0.0]),
                    "chord": end["chord"] + (wl["tip_chord"] - end["chord"]) * f,
                    "tc": wl["tc"], "twist": tip["twist"], "roll": math.degrees(phi1), "camber": 0.0})
    return [dict(tip, camber=tip.get("camber", 0.02))] + out


def wingtip_fence(buf, w, pf, lod):
    """Classic A320 wing-tip fence: a thin arrowhead plate above (swept back) and below (pointing forward)."""
    f = w["tip"]
    tip = pf.at(wing_tip_y(w))
    le = np.asarray(tip["le"]) + np.array([0.0, 0.04, 0.0])
    n = max(4, lod["foil"] // 2)

    def st(s_le, dz, chord):
        return {"span": 0.0, "le": le + np.array([s_le, 0.0, dz]), "chord": chord, "tc": 0.07, "twist": 0.0, "roll": 90.0}

    upper = [st(-0.10, -0.05, f["chord"]), st(0.55 * f["chord"], f["up"] * 0.6, 0.55 * f["chord"]),
             st(0.80 * f["chord"], f["up"], 0.18 * f["chord"])]
    loft_sections(buf, upper, n, 0.0, 1.0, cap_start=False, cap_end=True)
    lower = [st(-f["ahead"], -f["down"], 0.20 * f["chord"]), st(-0.55 * f["ahead"], -f["down"] * 0.55, 0.45 * f["chord"]),
             st(-0.10, 0.05, 0.62 * f["chord"])]
    loft_sections(buf, lower, n, 0.0, 1.0, cap_start=True, cap_end=False)


def mirror(part_or_buf_parts):
    """Mirror a list of Parts left->right (y -> -y) in place and return them."""
    for part in part_or_buf_parts:
        _mirror_part(part)
    return part_or_buf_parts


def _mirror_part(part):
    part.pivot = part.pivot * np.array([1.0, -1.0, 1.0])
    for buf in part.buffers.values():
        for k in range(len(buf.P)):
            buf.P[k] = buf.P[k] * np.array([1.0, -1.0, 1.0])
            buf.R[k] = buf.R[k] * np.array([1.0, -1.0, 1.0])
    h = part.extras.get("hinge")
    if h:
        for key in ("axis", "test", "desired"):
            h[key] = np.asarray(h[key]) * np.array([1.0, -1.0, 1.0])
    for c in part.children:
        _mirror_part(c)


def flap_fairings(buf, w, pf, lod):
    """Flap-track fairings ("canoes") under the trailing edge."""
    if lod["key"] == 2:
        return
    for y in w.get("flap_fairings", []):
        st = pf.at(y)
        cd, up, _ = section_frame(st)
        c = st["chord"]
        lower = st["tc"] * c * 0.5
        origin = np.asarray(st["le"]) + cd * 0.48 * c - up * (lower * 0.6)
        length = 0.75 * c + 0.6
        prof = [(0.0, 0.02), (0.12 * length, 0.13), (0.45 * length, 0.19), (0.8 * length, 0.13), (length, 0.02)]
        lathe(buf, prof, origin, cd, max(8, lod["small"]), closed_profile=False)


def build_wings(sp, lod, lod_key):
    w = sp["wing"]
    pf = wing_planform(w)
    tip_y = wing_tip_y(w)
    blended = w["tip"]["type"] == "blended"
    lod = dict(lod, key=lod_key)
    n_extra = {0: 6, 1: 3, 2: 0}[lod_key]
    spans = sorted(set([0.6, tip_y] + [st[0] for st in w["stations"] if st[0] > 0.6] +
                       list(np.linspace(0.6, tip_y, n_extra + 2))))
    sides = []
    for suffix in ("_L", "_R"):
        part = Part("Wing" + suffix)
        controls = []
        build_surface(pf, spans, w["controls"], w["hinge"], lod, part.buf("WingGrey"), part.buf("WingGrey"),
                      controls, "", 1.0, CONTROL_RANGES, cap_tip=not blended)
        for c in controls:
            c.name = c.name + suffix
        if blended:
            loft_sections(part.buf("Paint"), winglet_stations(w, pf, lod), lod["foil"], 0.0, 1.0, cap_start=False, cap_end=True)
        else:
            wingtip_fence(part.buf("Paint"), w, pf, lod)
        flap_fairings(part.buf("WingGrey"), w, pf, lod)
        part.children.extend(controls)
        if suffix == "_R":
            mirror([part])
        sides.append(part)
    return sides[0], sides[1], pf


def build_htail(sp, lod, lod_key):
    t = sp["htail"]
    tanL, tand = (t["tip_le_s"] - t["root_le_s"]) / t["semi_span"], math.tan(t["dihedral"] * D)

    def st(y, chord):
        return {"span": y, "le": np.array([t["root_le_s"] + y * tanL, y, t["root_z"] + y * tand]),
                "chord": chord, "tc": t["tc"], "twist": 0.0, "roll": t["dihedral"], "camber": 0.0}

    pf = Planform([st(0.0, t["root_chord"]), st(t["semi_span"], t["tip_chord"])])
    sides = []
    for suffix in ("_L", "_R"):
        part = Part("Stabilizer" + suffix)
        ctrls = []
        spans = sorted(set([0.3, t["semi_span"]] + list(np.linspace(0.3, t["semi_span"], {0: 3, 1: 2, 2: 0}[lod_key] + 2))))
        build_surface(pf, spans, t["controls"], t["hinge"], lod, part.buf("Paint"), part.buf("Paint"), ctrls, "", 1.0, CONTROL_RANGES)
        for c in ctrls:
            c.name = c.name + suffix
        part.children.extend(ctrls)
        if suffix == "_R":
            mirror([part])
        sides.append(part)
    return sides


def build_vtail(sp, lod, lod_key):
    v = sp["vtail"]
    sts = [{"span": z, "le": np.array([s, 0.0, z]), "chord": c, "tc": v["tc"], "twist": 0.0, "roll": 90.0, "camber": 0.0}
           for s, z, c in v["stations"]]
    pf = Planform(sts)
    part = Part("Fin")
    ctrls = []
    z0, z1 = v["stations"][0][1], v["stations"][-1][1]
    spans = sorted(set([z0, z1] + [s[1] for s in v["stations"]] + list(np.linspace(z0, z1, {0: 3, 1: 2, 2: 0}[lod_key] + 2))))
    build_surface(pf, spans, v["controls"], v["hinge"], lod, part.buf("Paint"), part.buf("Paint"), ctrls, "", 1.0, CONTROL_RANGES)
    part.children.extend(ctrls)
    return part


# ====================================================================== engines

ENGINE_PROFILES = {
    # (outer cowl front->aft, inner duct aft->front, extra parts), in units of nacelle max radius
    "short": {  # CFM56-7 class: short fan cowl, separate-flow exhaust with exposed core
        "outer": [(0.00, 0.88), (0.04, 0.93), (0.12, 0.97), (0.35, 1.0), (1.2, 1.0), (2.0, 0.95), (2.6, 0.86), (3.0, 0.78)],
        "inner": [(3.0, 0.74), (2.4, 0.79), (1.4, 0.80), (0.6, 0.80), (0.15, 0.80), (0.03, 0.84)],
        "core": [(2.4, 0.66), (3.2, 0.60), (3.9, 0.48), (4.3, 0.40)],
        "plug": [(4.3, 0.34), (4.6, 0.22), (5.0, 0.0)],
        "fan_x": 0.55, "fan_r": 0.78, "spinner": [(0.18, 0.0), (0.40, 0.17), (0.55, 0.26)],
    },
    "long": {  # CFM56-5B class: long-duct mixed-flow nacelle
        "outer": [(0.00, 0.90), (0.05, 0.96), (0.15, 1.0), (0.45, 1.04), (1.6, 1.05), (2.8, 1.0), (3.7, 0.86), (4.3, 0.72)],
        "inner": [(4.3, 0.68), (3.6, 0.70), (2.6, 0.78), (1.2, 0.84), (0.5, 0.85), (0.12, 0.85), (0.03, 0.88)],
        "core": [(0.9, 0.46), (3.9, 0.46)],
        "plug": [(3.9, 0.42), (4.3, 0.34), (4.85, 0.0)],
        "fan_x": 0.6, "fan_r": 0.83, "spinner": [(0.2, 0.0), (0.45, 0.18), (0.6, 0.28)],
    },
}


def build_engine(sp, lod, side, wing_pf):
    e = sp["engine"]
    prof = ENGINE_PROFILES[e["type"]]
    R = e["radius"]
    y = e["y"] * (1 if side == "L" else -1)
    origin = np.array([e["front_s"], y, e["center_z"]])
    axis = np.array([1.0, 0.0, 0.0])
    seg = lod["lathe"]
    flatten = e["flatten"]

    def scaled(points):
        return np.asarray(points, dtype=float) * R

    # 737 "hamster pouch": the inlet lip is an oval with a flat bottom, and the cowl stays flatter
    # underneath back to the fan case. The fan duct itself stays round, so the cowl never cuts into it.
    lip_flat = flatten - 0.06

    def squash(local, f):
        y, z = local[..., 1], local[..., 2]
        r = np.maximum(np.hypot(y, z), 1e-6)
        t = np.clip(-z / (0.7 * r), 0.0, 1.0)            # 0 at the equator -> 1 towards the bottom
        t = t * t * (3 - 2 * t)
        local[..., 1] = y * (1 + (1 - f) * 0.35 * t)
        local[..., 2] = np.where(z < 0, z * (1 - (1 - f) * np.clip(-z / r, 0.0, 1.0) ** 0.5), z)
        return local

    def deform_outer(local):
        if flatten >= 0.999:
            return local
        x = local[..., 0] / R
        f = np.where(x < 0.3, lip_flat + (flatten - lip_flat) * x / 0.3,
                     np.where(x < 1.6, flatten, np.where(x > 2.7, 1.0, flatten + (1 - flatten) * (x - 1.6) / 1.1)))
        return squash(local, f)

    def deform_inner(local):
        if flatten >= 0.999:
            return local
        x = local[..., 0] / R
        f = np.where(x < 0.3, lip_flat + (1 - lip_flat) * x / 0.3, 1.0)
        return squash(local, f)

    part = Part(f"Engine_{side}", origin, extras={"kind": "engine"})
    outer, inner = scaled(prof["outer"]), scaled(prof["inner"])
    loop = np.vstack([outer, inner])
    n2 = profile_normals(loop, closed=True)
    no = len(outer)
    lathe(part.buf("Paint"), outer, origin, axis, seg, normals2d=n2[:no], deform=deform_outer)
    lip = np.vstack([inner, outer[:1]])
    lip_n = np.vstack([n2[no:], n2[:1]])
    lathe(part.buf("Metal"), lip, origin, axis, seg, normals2d=lip_n, deform=deform_inner)
    lathe(part.buf("WingGrey" if e["type"] == "short" else "Fan"), scaled(prof["core"]), origin, axis, seg, closed_profile=False)
    lathe(part.buf("Exhaust"), scaled(prof["plug"]), origin, axis, seg, closed_profile=False)

    fan_pivot = origin + axis * prof["fan_x"] * R
    fan_part = Part(f"Fan_{side}", fan_pivot, extras={"kind": "fan", "hinge": {
        "axis": axis, "test": fan_pivot + np.array([0.0, 0.0, 0.5]), "desired": np.array([0.0, 1.0 if side == "L" else -1.0, 0.0]),
        "min": -1e9, "max": 1e9}})
    lathe(fan_part.buf("Fan"), [(prof["fan_x"] * R, 0.02), (prof["fan_x"] * R, prof["fan_r"] * R)], origin, axis, seg, closed_profile=False)
    lathe(fan_part.buf("Metal"), scaled(prof["spinner"]), origin, axis, seg, closed_profile=False)
    # Fan blade hint: radial dark strips so spinning is visible.
    nb = {40: 12, 20: 8}.get(seg, 0)
    for k in range(nb):
        a = 2 * math.pi * k / nb
        d = np.array([0.0, math.cos(a), math.sin(a)])
        c = fan_pivot + np.array([-0.012, 0, 0]) + d * (0.55 * prof["fan_r"] * R)
        tang = np.cross(axis, d)
        box(fan_part.buf("DarkMetal"), c, (0.01, 0.07, 0.42 * prof["fan_r"] * R),
            axes=[axis, tang, d])
    part.children.append(fan_part)

    # Pylon: two thick symmetric sections from inside the nacelle up into the wing.
    py = e["pylon"]
    wing_st = wing_pf.at(abs(y))
    z_top = wing_st["le"][2]
    z_bot = e["center_z"] + 0.75 * R
    sts = [
        {"span": z_bot, "le": np.array([py["bottom_le_s"], y, z_bot]), "chord": py["bottom_chord"], "tc": 0.22, "twist": 0, "roll": 90.0},
        {"span": z_top, "le": np.array([py["top_le_s"], y, z_top]), "chord": py["top_chord"], "tc": 0.16, "twist": 0, "roll": 90.0},
    ]
    loft_sections(part.buf("Paint"), sts, max(4, lod["foil"] // 2), 0.0, 1.0, cap_start=False, cap_end=False)
    return part


# ====================================================================== landing gear

def tyre(buf_rubber, buf_hub, centre, diameter, width, segments):
    R = diameter / 2
    r_in = R * 0.58
    rc = R - r_in
    outline = rounded_rect(width, rc, min(width, rc) * 0.42, 3)
    prof = np.stack([outline[:, 0], outline[:, 1] + (R + r_in) / 2], axis=1)
    axis = np.array([0.0, 1.0, 0.0])
    lathe(buf_rubber, prof, centre - axis * 0.0, axis, segments, closed_profile=True)
    hw = width * 0.42
    cylinder(buf_hub, centre - axis * hw, centre + axis * hw, r_in * 1.01, max(8, segments // 2))


def pitch_drop(sp, s):
    """How much a point at station s is lowered by the nose-down pitch (rotation about the main gear)."""
    return (sp["main_gear"]["s"] - s) * math.tan(sp.get("pitch_deg", 0.0) * D)


def build_nose_gear(fus, sp, lod):
    g = sp["nose_gear"]
    s = g["s"]
    R = g["tyre_d"] / 2
    axle_z = R + pitch_drop(sp, s)   # the model is tilted nose-down about the main gear afterwards
    top_z = fus.section(s)[0] - fus.section(s)[2] + 0.35
    seg = lod["small"]
    gear = Part("NoseGear", np.array([s, 0.0, top_z]), extras={"kind": "gear"})
    cylinder(gear.buf("Metal"), [s, 0, axle_z + 0.55], [s, 0, top_z], 0.085, seg)
    # gear-well doors, hanging open beside the well
    for side in (1, -1):
        box(gear.buf("Paint"), [s - 0.6, side * 0.36, top_z - 0.55], (0.9, 0.025, 0.42))
    steer = Part("NoseGear_Steer", np.array([s, 0.0, axle_z]), extras={
        "kind": "steering", "hinge": {"axis": np.array([0.0, 0.0, 1.0]), "test": np.array([s - 1.0, 0.0, axle_z]),
                                      "desired": np.array([0.0, 1.0, 0.0]), "min": -78.0, "max": 78.0}})
    cylinder(steer.buf("Metal"), [s, 0, axle_z], [s, 0, axle_z + 0.62], 0.06, seg)
    cylinder(steer.buf("DarkMetal"), [s, -g["spacing"] / 2, axle_z], [s, g["spacing"] / 2, axle_z], 0.045, seg)
    box(steer.buf("DarkMetal"), [s - 0.12, 0, axle_z + 0.32], (0.05, 0.06, 0.36), axes=None)   # torque links
    box(steer.buf("DarkMetal"), [s - 0.23, 0, axle_z + 0.02], (0.18, 0.14, 0.08))               # tow fitting
    for side, name in ((1, "L"), (-1, "R")):
        c = np.array([s, side * g["spacing"] / 2, axle_z])
        wheel = Part(f"NoseGear_Wheel_{name}", c, extras=wheel_extras(c))
        tyre(wheel.buf("Rubber"), wheel.buf("Metal"), c, g["tyre_d"], g["tyre_w"], lod["tyre"])
        steer.children.append(wheel)
    gear.children.append(steer)
    return gear


def wheel_extras(c):
    return {"kind": "wheel", "hinge": {"axis": np.array([0.0, 1.0, 0.0]), "test": c + np.array([0.0, 0.0, 0.5]),
                                       "desired": np.array([-1.0, 0.0, 0.0]), "min": -1e9, "max": 1e9}}


def build_main_gear(sp, lod, side, wing_pf):
    g = sp["main_gear"]
    sign = 1 if side == "L" else -1
    s, y = g["s"], g["y"] * sign
    R = g["tyre_d"] / 2
    axle_z = R
    seg = lod["small"]
    wing_z = wing_pf.at(g["y"])["le"][2] - 0.1
    gear = Part(f"MainGear_{side}", np.array([s, y, wing_z]), extras={"kind": "gear"})
    cylinder(gear.buf("Metal"), [s, y, axle_z + 0.15], [s, y, wing_z], 0.12, seg)
    cylinder(gear.buf("Metal"), [s, y, axle_z], [s, y, axle_z + 0.55], 0.09, seg)
    cylinder(gear.buf("DarkMetal"), [s, y - g["spacing"] / 2, axle_z], [s, y + g["spacing"] / 2, axle_z], 0.07, seg)
    # side brace towards the fuselage
    cylinder(gear.buf("DarkMetal"), [s + 0.15, y, axle_z + 1.0], [s + 0.3, y - sign * 1.3, wing_z - 0.05], 0.05, max(6, seg // 2))
    # strut door (outboard)
    box(gear.buf("Paint"), [s, y + sign * 0.2, (axle_z + 0.7 + wing_z) / 2], (0.7, 0.025, wing_z - axle_z - 0.6))
    for k, name in ((1, "Out"), (-1, "In")):
        c = np.array([s, y + sign * k * g["spacing"] / 2, axle_z])
        wheel = Part(f"MainGear_{side}_Wheel_{name}", c, extras=wheel_extras(c))
        tyre(wheel.buf("Rubber"), wheel.buf("Metal"), c, g["tyre_d"], g["tyre_w"], lod["tyre"])
        gear.children.append(wheel)
    return gear


# ====================================================================== interior

def strip(buf, S, y_fn, z, ref):
    rings = np.array([[[s, y_fn(s), z], [s, -y_fn(s), z]] for s in S])
    grid(buf, rings, np.broadcast_to(np.asarray(ref, dtype=float), rings.shape), closed=False)


def build_interior(fus, sp, holds_out, seats_out):
    interior = Part("Interior", extras={"kind": "interior"})
    zf = sp["floor_z"]
    s_a, s_b = sp["lining_start"] + 0.03, sp["aft_bulkhead"] - 0.03
    S = np.linspace(s_a, s_b, 60)
    strip(interior.buf("Carpet"), S, lambda s: max(fus.inner_halfwidth(s, zf) - 0.01, 0.05), zf, [0, 0, 1])
    strip(interior.buf("HoldLining"), S, lambda s: max(fus.inner_halfwidth(s, zf - 0.14) - 0.01, 0.05), zf - 0.14, [0, 0, -1])

    # cargo holds: floor + end bulkheads (the lining forms the walls)
    for hd in sp["holds"]:
        h0, h1 = hd["s"]
        Sh = np.linspace(h0, h1, 16)
        strip(interior.buf("HoldLining"), Sh, lambda s: max(fus.inner_halfwidth(s, hd["floor_z"]) - 0.01, 0.05), hd["floor_z"], [0, 0, 1])
        for s_end, ref in ((h0, [1.0, 0, 0]), (h1, [-1.0, 0, 0])):
            pts = clip_polygon(fus.inner_ring(s_end), [0, 0, -1.0], -(zf - 0.14))
            polygon(interior.buf("HoldLining"), pts, ref)
        mid = 0.5 * (h0 + h1)
        top = zf - 0.14
        width = 2 * fus.inner_halfwidth(mid, 0.5 * (hd["floor_z"] + top))
        holds_out.append({"name": hd["name"], "center": [mid, 0.0, 0.5 * (hd["floor_z"] + top)],
                          "size": [h1 - h0, width, top - hd["floor_z"]], "maxMassKg": hd["max_kg"]})

    # seats
    sr = sp["seat_rows"]
    w = sr["seat_width"]
    rows, s = [], sr["first"]
    extras = sorted(sr["exit_rows_extra"])
    for r in range(sr["count"]):
        for es, add in extras:
            if abs(s - es) < sr["pitch"] / 2:
                s += add
        rows.append(s)
        s += sr["pitch"]
    tilt = 12 * D
    back_axes = [[math.cos(tilt), 0, -math.sin(tilt)], [0, 1, 0], [math.sin(tilt), 0, math.cos(tilt)]]
    letters = "ABCDEF"
    for r, s in enumerate(rows):
        for side in (1, -1):
            limit = fus.inner_halfwidth(s, zf + 0.5) - 0.05
            seats = [k for k in range(sr["abreast"]) if sr["aisle"] / 2 + w * (k + 1) <= limit]
            if not seats:
                continue
            for k in seats:
                y = side * (sr["aisle"] / 2 + w * (k + 0.5))
                box(interior.buf("Seat"), [s, y, zf + 0.43], (0.46, w - 0.04, 0.12))
                box(interior.buf("Seat"), [s + 0.26, y, zf + 0.80], (0.09, w - 0.04, 0.66), axes=back_axes)
                box(interior.buf("SeatHead"), [s + 0.33, y, zf + 1.07], (0.04, w - 0.08, 0.16), axes=back_axes)
                idx = (2 - k) if side > 0 else (3 + k)
                seats_out.append({"seat": f"{r + 1}{letters[idx]}", "position": [s, y, zf + 0.5]})
            y_mid = side * (sr["aisle"] / 2 + w * len(seats) / 2)
            box(interior.buf("DarkMetal"), [s, y_mid, zf + 0.18], (0.35, w * len(seats) - 0.1, 0.3))

    # overhead bins and cabin lighting
    zb, zt = zf + 1.55, zf + 1.98
    Sb = np.linspace(rows[0] - 0.5, rows[-1] + 0.6, 24)
    for side in (1, -1):
        def prof(s):
            yo_b = fus.inner_halfwidth(s, zb) - 0.01
            yo_t = fus.inner_halfwidth(s, zt) - 0.01
            return [np.array([s, side * yo_b, zb]), np.array([s, side * (yo_b - 0.55), zb]),
                    np.array([s, side * (yo_b - 0.60), zb + 0.30]), np.array([s, side * yo_t, zt])]
        profs = np.array([prof(s) for s in Sb])
        faces = [((0, 1), [0, 0, -1]), ((1, 2), [0, -side, -0.2]), ((2, 3), [0, -side * 0.4, 1])]
        for (a, b), ref in faces:
            grid(interior.buf("Plastic"), profs[:, [a, b]], np.broadcast_to(np.asarray(ref, float), profs[:, [a, b]].shape), closed=False)
        for k, ref in ((0, [-1, 0, 0]), (-1, [1, 0, 0])):
            polygon(interior.buf("Plastic"), profs[k], ref)
        light = np.array([[p[2] + np.array([0, -side * 0.05, 0.06]), p[2] + np.array([0, -side * 0.12, 0.10])] for p in profs])
        grid(interior.buf("CabinLight"), light, np.broadcast_to(np.array([0, -side * 0.5, -1.0]), light.shape), closed=False)

    # galleys and lavatories (boxes against the sidewall); lav doors are separate pivoted parts
    def wall_box(entry, material, depth, height):
        side = 1 if entry["side"] == "L" else -1
        s0, s1 = entry["s"]
        sm = 0.5 * (s0 + s1)
        y_out = fus.inner_halfwidth(sm, zf + height) - 0.02
        y_in = max(y_out - depth, 0.35)
        if y_out - y_in < 0.25:
            raise ValueError(f"{material} at s={sm:.2f} does not fit the fuselage section; move it forward in specs.py")
        box(interior.buf(material), [sm, side * 0.5 * (y_out + y_in), zf + height / 2], (s1 - s0, y_out - y_in, height))
        return side, s0, s1, y_in
    for g in sp["galleys"]:
        wall_box(g, "Galley", 0.8, 1.9)
    for n, lv in enumerate(sp["lavatories"]):
        side, s0, s1, y_in = wall_box(lv, "Plastic", 0.95, 1.95)
        pivot = np.array([s0 + 0.08, side * (y_in - 0.015), zf + 0.02])
        door = Part(f"LavDoor_{n + 1}", pivot, extras={"kind": "door", "doorType": "lavatory", "hinge": {
            "axis": np.array([0, 0, 1.0]), "test": pivot + np.array([0.5, 0, 0.9]),
            "desired": np.array([0, -side, 0.0]), "min": 0.0, "max": 95.0}})
        box(door.buf("Lining"), [s0 + 0.08 + (s1 - s0 - 0.16) / 2, side * (y_in - 0.03), zf + 0.98], (s1 - s0 - 0.16, 0.03, 1.9))
        interior.children.append(door)

    # flight deck: bulkhead with door, seats, panels
    sfd = sp["flight_deck_bulkhead"]
    ring = clip_polygon(fus.inner_ring(sfd), [0, 0, 1.0], zf)
    half_door, door_h = 0.40, 1.95
    pieces = [clip_polygon(ring, [0, 1.0, 0], half_door), clip_polygon(ring, [0, -1.0, 0], half_door),
              clip_polygon(clip_polygon(clip_polygon(ring, [0, -1.0, 0], -half_door), [0, 1.0, 0], -half_door), [0, 0, 1.0], zf + door_h)]
    for pc in pieces:
        polygon(interior.buf("Lining"), pc, [1.0, 0, 0])
        polygon(interior.buf("Lining"), pc + np.array([-0.04, 0, 0]), [-1.0, 0, 0])
    fdd_pivot = np.array([sfd - 0.02, half_door, zf])
    fdd = Part("FlightDeckDoor", fdd_pivot, extras={"kind": "door", "doorType": "flightdeck", "hinge": {
        "axis": np.array([0, 0, 1.0]), "test": np.array([sfd, 0.0, zf + 1.0]), "desired": np.array([-1.0, 0, 0]),
        "min": 0.0, "max": 100.0}})
    box(fdd.buf("Cockpit"), [sfd - 0.02, 0.0, zf + door_h / 2], (0.04, 2 * half_door - 0.02, door_h - 0.02))
    interior.children.append(fdd)

    # Main panel top ~0.9 m above the floor, below the windshield sill; pilots' eye ~1.1 m.
    s_panel = sp["windshield"][0][3][1] + 0.45   # behind the front windscreen's lower edge
    pw = min(1.0, fus.inner_halfwidth(s_panel, zf + 0.7) - 0.08)
    tilt_p = [[math.cos(-15 * D), 0, -math.sin(-15 * D)], [0, 1, 0], [math.sin(-15 * D), 0, math.cos(-15 * D)]]
    box(interior.buf("Cockpit"), [s_panel, 0, zf + 0.62], (0.08, 2 * pw, 0.5), axes=tilt_p)
    box(interior.buf("Cockpit"), [s_panel + 0.02, 0, zf + 0.9], (0.3, 2 * pw * 0.9, 0.07))
    for k in range(6):
        y = (k - 2.5) * (2 * pw / 6.4)
        box(interior.buf("Display"), [s_panel - 0.05, y, zf + 0.64], (0.012, 0.25, 0.22), axes=tilt_p)
    box(interior.buf("Cockpit"), [s_panel + 0.65, 0, zf + 0.34], (0.85, 0.34, 0.6))
    ztop = fus.section(s_panel + 0.6)[0] + fus.section(s_panel + 0.6)[2] - fus.wall - 0.12
    box(interior.buf("Cockpit"), [s_panel + 0.75, 0, ztop], (0.9, 0.7, 0.08))
    for side in (1, -1):
        y = side * min(0.52, fus.inner_halfwidth(sfd - 1.1, zf + 0.45) - 0.35)
        box(interior.buf("Seat"), [sfd - 1.1, y, zf + 0.48], (0.5, 0.5, 0.12))
        box(interior.buf("Seat"), [sfd - 0.83, y, zf + 0.9], (0.1, 0.5, 0.75), axes=back_axes)
    return interior


# ====================================================================== service points

def service_points(fus, sp, wing_pf):
    pts = {}
    for name, (where, s, z) in sp["service_points"].items():
        if where == "C":
            pts[name] = {"position": [s, 0.0, z + pitch_drop(sp, s)], "normal": [0, 0, -1.0]}
        elif where in ("L", "R"):
            th = fus.theta_for_z(s, z, where)
            p, n = fus.point(s, th), fus.normal(s, th)
            pts[name] = {"position": (p + n * 0.01).tolist(), "normal": n.tolist()}
        elif where == "RW":
            st = wing_pf.at(s)
            le = np.asarray(st["le"])
            p = le + np.array([0.6, 0.0, -0.14 * st["chord"] * 0.5])
            p[1] = -p[1]
            pts[name] = {"position": p.tolist(), "normal": [0, 0, -1.0]}
    ng, mg = sp["nose_gear"], sp["main_gear"]
    rn, rm = ng["tyre_d"] / 2, mg["tyre_d"] / 2
    pts["SP_Chock_NoseGear_Fwd"] = {"position": [ng["s"] - rn - 0.2, 0.0, 0.0], "normal": [0, 0, 1.0], "ground": True}
    pts["SP_Chock_NoseGear_Aft"] = {"position": [ng["s"] + rn + 0.2, 0.0, 0.0], "normal": [0, 0, 1.0], "ground": True}
    for side, sign in (("L", 1), ("R", -1)):
        for k, wn in ((1, "Out"), (-1, "In")):
            y = sign * (mg["y"] + k * mg["spacing"] / 2)
            pts[f"SP_Chock_MainGear_{side}_{wn}_Fwd"] = {"position": [mg["s"] - rm - 0.22, y, 0.0], "normal": [0, 0, 1.0], "ground": True}
            pts[f"SP_Chock_MainGear_{side}_{wn}_Aft"] = {"position": [mg["s"] + rm + 0.22, y, 0.0], "normal": [0, 0, 1.0], "ground": True}
    w = sp["wing"]
    tip_y = wing_tip_y(w)
    tip = wing_pf.at(tip_y)
    e = sp["engine"]
    pts["SP_Cone_Nose"] = {"position": [-1.5, 0.0, 0.0], "normal": [0, 0, 1.0], "ground": True}
    pts["SP_Cone_Tail"] = {"position": [fus.L + 1.5, 0.0, 0.0], "normal": [0, 0, 1.0], "ground": True}
    for side, sign in (("L", 1), ("R", -1)):
        pts[f"SP_Cone_WingTip_{side}"] = {"position": [float(tip["le"][0]) + tip["chord"] / 2, sign * (tip_y + 1.0), 0.0], "normal": [0, 0, 1.0], "ground": True}
        pts[f"SP_Cone_Engine_{side}"] = {"position": [e["front_s"] - 1.5, sign * e["y"], 0.0], "normal": [0, 0, 1.0], "ground": True}
    for d in sp["doors"]:
        if d["kind"] in ("pax", "cargo"):
            smid = 0.5 * (d["s"][0] + d["s"][1])
            th = fus.theta_for_z(smid, d["z"][0], d["side"])
            p, n = fus.point(smid, th), fus.normal(smid, th)
            pts[f"SP_Sill_{d['name']}"] = {"position": (p + n * 0.05).tolist(), "normal": n.tolist(), "sillHeight": d["z"][0]}
    return pts


# ====================================================================== lights and antennas

def light_dome(buf, centre, normal, radius, segments):
    """Low dome (beacon / strobe lens) facing `normal`."""
    prof = [(0.0, radius), (radius * 0.35, radius * 0.8), (radius * 0.55, 0.0)]
    lathe(buf, prof, centre, normal, segments, closed_profile=False)


def build_lights(fus, sp, wing_pf, lod):
    """Exterior lights as separate nodes so the game can switch them (beacon on = engines running)."""
    seg = max(8, lod["small"])
    parts = []
    w = sp["wing"]
    s_top = float(wing_pf.at(w["root_y"])["le"][0]) + 2.5
    zc, a, h = fus.section(s_top)
    beacon = Part("Beacon_Top", np.array([s_top, 0.0, zc + h]), extras={"kind": "light", "lightType": "beacon"})
    light_dome(beacon.buf("Light_Beacon"), np.array([s_top, 0.0, zc + h - 0.02]), [0, 0, 1.0], 0.1, seg)
    parts.append(beacon)
    bf = sp["belly_fairing"]
    s_bot = 0.5 * (bf["s"][0] + bf["s"][1]) - 1.0
    z_bot = bf["center_z"] - bf["half_height"] * 0.98
    beacon_b = Part("Beacon_Bottom", np.array([s_bot, 0.0, z_bot]), extras={"kind": "light", "lightType": "beacon"})
    light_dome(beacon_b.buf("Light_Beacon"), np.array([s_bot, 0.0, z_bot + 0.02]), [0, 0, -1.0], 0.1, seg)
    parts.append(beacon_b)
    tip = wing_pf.at(wing_tip_y(w))
    cd, up, _ = section_frame(tip)
    for side, sign, mat in (("L", 1, "Light_NavRed"), ("R", -1, "Light_NavGreen")):
        le = np.asarray(tip["le"]) * np.array([1, sign, 1])
        nav = Part(f"NavLight_{side}", le, extras={"kind": "light", "lightType": "navigation"})
        box(nav.buf(mat), le + np.array([0.12, sign * 0.06, 0.0]), (0.18, 0.08, 0.07))
        parts.append(nav)
        te = le + np.array([tip["chord"] * 0.85, 0.0, 0.03])
        strobe = Part(f"Strobe_{side}", te, extras={"kind": "light", "lightType": "strobe"})
        box(strobe.buf("Light_White"), te + np.array([0, sign * 0.05, 0.0]), (0.12, 0.06, 0.05))
        parts.append(strobe)
    zc_t, a_t, h_t = fus.section(fus.L - 0.4)
    tail = Part("NavLight_Tail", np.array([fus.L - 0.4, 0.0, zc_t + h_t]), extras={"kind": "light", "lightType": "navigation"})
    box(tail.buf("Light_White"), [fus.L - 0.4, 0.0, zc_t + h_t + 0.02], (0.12, 0.08, 0.05))
    parts.append(tail)
    return parts


def build_antennas(fus, sp, airframe):
    root_le = sp["wing"]["stations"][0][1]
    for s, top in ((9.0, True), (root_le + 6.5, True), (7.0, False), (sp["aft_bulkhead"] - 6.0, False)):
        zc, a, h = fus.section(s)
        z = zc + h if top else zc - h
        sign = 1 if top else -1
        box(airframe.buf("DarkMetal"), [s, 0.0, z + sign * 0.12], (0.32, 0.025, 0.26),
            axes=[[math.cos(0.35), 0, sign * math.sin(0.35)], [0, 1, 0], [-sign * math.sin(0.35), 0, math.cos(0.35)]])


# ====================================================================== assembly

def build(sp, lod_key):
    lod = dict(LODS[lod_key], key=lod_key)
    fus = Fuselage(sp)
    group = Part(f"LOD{lod_key}")
    airframe = Part("Airframe")
    group.children.append(airframe)
    moving = []
    build_fuselage(fus, sp, lod, airframe, moving, None)
    build_windows(fus, sp, lod, airframe)
    build_belly_fairing(fus, sp, lod, airframe)
    wing_l, wing_r, wing_pf = build_wings(sp, lod, lod_key)
    group.children += [wing_l, wing_r]
    group.children += build_htail(sp, lod, lod_key)
    group.children.append(build_vtail(sp, lod, lod_key))
    group.children += [build_engine(sp, lod, "L", wing_pf), build_engine(sp, lod, "R", wing_pf)]
    group.children += [build_nose_gear(fus, sp, lod), build_main_gear(sp, lod, "L", wing_pf), build_main_gear(sp, lod, "R", wing_pf)]
    group.children += build_lights(fus, sp, wing_pf, lod)
    build_antennas(fus, sp, airframe)
    doors = Part("Doors")
    doors.children = moving
    if moving:
        group.children.append(doors)
    holds, seats = [], []
    if lod["interior"]:
        group.children.append(build_interior(fus, sp, holds, seats))
    if lod_key == 2:
        merged = Part("Airframe")
        merged.absorb(group)
        group.children = [merged]
        group.buffers = {}
    return group, {"holds": holds, "seats": seats, "service_points": service_points(fus, sp, wing_pf),
                   "fuselage": fus}
