"""Geometry toolkit for the aircraft generator.

Internal frame used by all builders ("aircraft frame"):
    s = metres aft of the nose tip, y = metres to the LEFT (port), z = metres above ground.
Conversion to glTF (right-handed, +Y up, +Z = front of the asset, metres) happens at export:
    X = y,  Y = z,  Z = s_origin - s
so the nose points along +Z and the model origin sits at s_origin (the nose-gear contact point).

Every builder returns, alongside positions and triangles, *reference normals* that point
out of the solid. Triangle winding is fixed against them after conversion, so builders
never have to worry about winding order, and mirrored geometry stays correct.
"""
import math

import numpy as np


def normalize(v, eps=1e-12):
    v = np.asarray(v, dtype=float)
    n = np.linalg.norm(v, axis=-1, keepdims=True)
    return v / np.maximum(n, eps)


class MeshBuf:
    """Accumulates geometry for one material of one part."""

    def __init__(self):
        self.P, self.R, self.UV, self.I = [], [], [], []
        self.count = 0

    def add(self, P, I, R, UV=None):
        P = np.asarray(P, dtype=float).reshape(-1, 3)
        R = np.broadcast_to(np.asarray(R, dtype=float), P.shape).reshape(-1, 3)
        I = np.asarray(I, dtype=np.int64).reshape(-1, 3)
        if UV is None:
            UV = np.zeros((len(P), 2))
        self.P.append(P)
        self.R.append(np.array(R))
        self.UV.append(np.asarray(UV, dtype=float).reshape(-1, 2))
        self.I.append(I + self.count)
        self.count += len(P)

    def merge(self, other, offset=(0.0, 0.0, 0.0)):
        if other.count == 0:
            return
        P, R, UV, I = other.arrays()
        self.add(P + np.asarray(offset), I, R, UV)

    def arrays(self):
        if self.count == 0:
            return np.zeros((0, 3)), np.zeros((0, 3)), np.zeros((0, 2)), np.zeros((0, 3), dtype=np.int64)
        return (np.concatenate(self.P), np.concatenate(self.R), np.concatenate(self.UV), np.concatenate(self.I))

    def triangle_count(self):
        return sum(len(i) for i in self.I)

    def finalize(self, convert, origin):
        """Convert to glTF space relative to `origin` (aircraft frame), fix winding, compute normals."""
        P, R, UV, I = self.arrays()
        if len(I) == 0:
            return None
        P = convert(P) - convert(np.asarray(origin, dtype=float).reshape(1, 3))
        # R are directions: convert without the translation part of `convert`.
        R = convert(R) - convert(np.zeros((1, 3)))
        a, b, c = P[I[:, 0]], P[I[:, 1]], P[I[:, 2]]
        face = np.cross(b - a, c - a)
        area2 = np.linalg.norm(face, axis=1)
        keep = area2 > 1e-10
        I, face = I[keep], face[keep]
        ref = R[I[:, 0]] + R[I[:, 1]] + R[I[:, 2]]
        flip = (face * ref).sum(axis=1) < 0
        I[flip] = I[flip][:, [0, 2, 1]]
        face[flip] = -face[flip]
        N = np.zeros_like(P)
        for k in range(3):
            np.add.at(N, I[:, k], face)
        length = np.linalg.norm(N, axis=1)
        fallback = length < 1e-12
        N[fallback] = R[fallback]
        N = normalize(N)
        return P, N, UV, I


class Part:
    """A node in the exported hierarchy: optional geometry (per material) + pivot + children."""

    def __init__(self, name, pivot=(0.0, 0.0, 0.0), extras=None):
        self.name = name
        self.pivot = np.asarray(pivot, dtype=float)
        self.extras = extras or {}
        self.buffers = {}
        self.children = []

    def buf(self, material):
        if material not in self.buffers:
            self.buffers[material] = MeshBuf()
        return self.buffers[material]

    def child(self, part):
        self.children.append(part)
        return part

    def triangle_count(self, recursive=True):
        n = sum(b.triangle_count() for b in self.buffers.values())
        if recursive:
            n += sum(c.triangle_count() for c in self.children)
        return n

    def absorb(self, other):
        """Merge another part's geometry (and its children's) into this part's buffers."""
        for material, buffer in other.buffers.items():
            self.buf(material).merge(buffer)
        for c in other.children:
            self.absorb(c)


# ---------------------------------------------------------------------- builders
# All builders add into a MeshBuf and work in the aircraft frame.

def grid(buf, rings, refs, closed=True, mask=None, uv=None):
    """Quad grid from rings[nu, nv, 3]. closed: wrap around v. mask[nu-1, nv(-1)] True = skip quad."""
    rings = np.asarray(rings, dtype=float)
    nu, nv = rings.shape[:2]
    P = rings.reshape(-1, 3)
    R = np.asarray(refs, dtype=float).reshape(-1, 3)
    idx = np.arange(nu * nv).reshape(nu, nv)
    nq = nv if closed else nv - 1
    tris = []
    for i in range(nu - 1):
        for j in range(nq):
            if mask is not None and mask[i, j]:
                continue
            j2 = (j + 1) % nv
            a, b, c, d = idx[i, j], idx[i + 1, j], idx[i + 1, j2], idx[i, j2]
            tris.append((a, b, c))
            tris.append((a, c, d))
    if tris:
        buf.add(P, np.array(tris), R, None if uv is None else np.asarray(uv).reshape(-1, 2))


def grid_normals(rings, closed=True, outward_hint=None):
    """Numerical surface normals of a grid; oriented to agree with outward_hint[nu, nv, 3]."""
    rings = np.asarray(rings, dtype=float)
    du = np.gradient(rings, axis=0)
    if closed:
        dv = (np.roll(rings, -1, axis=1) - np.roll(rings, 1, axis=1)) * 0.5
    else:
        dv = np.gradient(rings, axis=1)
    n = np.cross(du, dv)
    if outward_hint is not None:
        sign = np.sign((n * outward_hint).sum(axis=-1, keepdims=True))
        sign[sign == 0] = 1
        n = n * sign
    return normalize(n)


def fan(buf, ring, center, ref):
    """Cap a closed ring of points with a triangle fan to `center`."""
    ring = np.asarray(ring, dtype=float)
    P = np.vstack([ring, np.asarray(center, dtype=float).reshape(1, 3)])
    c = len(ring)
    tris = [(c, k, (k + 1) % c) for k in range(c)]
    buf.add(P, tris, ref)


def polygon(buf, pts, ref):
    """Convex planar polygon, fan triangulated from its centroid."""
    pts = np.asarray(pts, dtype=float)
    if len(pts) < 3:
        return
    fan(buf, pts, pts.mean(axis=0), ref)


def box(buf, center, size, axes=None):
    """Box with flat-shaded faces. axes: 3x3 rows = local x, y, z unit axes (default aircraft frame)."""
    center = np.asarray(center, dtype=float)
    hx, hy, hz = np.asarray(size, dtype=float) / 2.0
    ax = np.eye(3) if axes is None else np.asarray(axes, dtype=float)
    for k in range(3):
        for sgn in (-1.0, 1.0):
            n = ax[k] * sgn
            u, v = ax[(k + 1) % 3], ax[(k + 2) % 3]
            h = (hx, hy, hz)
            hu, hv, hn = h[(k + 1) % 3], h[(k + 2) % 3], h[k]
            c = center + n * hn
            quad = [c - u * hu - v * hv, c + u * hu - v * hv, c + u * hu + v * hv, c - u * hu + v * hv]
            buf.add(quad, [(0, 1, 2), (0, 2, 3)], n)


def frame_from_axis(axis):
    axis = normalize(axis)
    helper = np.array([0.0, 0.0, 1.0]) if abs(axis[2]) < 0.9 else np.array([1.0, 0.0, 0.0])
    u = normalize(np.cross(axis, helper))
    v = np.cross(axis, u)
    return axis, u, v


def profile_normals(profile, closed=True, outward_sign=-1.0):
    """Outward 2D normals of a polyline. Closed loops are oriented automatically (away from the
    enclosed area); open polylines use outward_sign: with points ordered front->back (or
    centre->rim), -1 gives normals on the +radius / forward side."""
    prof = np.asarray(profile, dtype=float)
    if closed:
        nxt, prv = np.roll(prof, -1, axis=0), np.roll(prof, 1, axis=0)
        area = 0.5 * np.sum(prof[:, 0] * nxt[:, 1] - nxt[:, 0] * prof[:, 1])
        orient = 1.0 if area > 0 else -1.0
    else:
        nxt = np.vstack([prof[1:], prof[-1:] + (prof[-1:] - prof[-2:-1])])
        prv = np.vstack([prof[:1] - (prof[1:2] - prof[:1]), prof[:-1]])
        orient = outward_sign
    t = nxt - prv
    return normalize(np.stack([t[:, 1], -t[:, 0]], axis=1)) * orient


def lathe(buf, profile, origin, axis, segments, closed_profile=True, outward_sign=-1.0,
          deform=None, start_angle=0.0, sweep=2 * math.pi, normals2d=None):
    """Surface of revolution. profile: [(axial, radius), ...] in metres.
    closed_profile: profile is a closed loop (thick cowl, tyre); otherwise open (spinner, strut).
    normals2d: precomputed outward profile normals (for a segment of a larger closed loop).
    deform(local_points[n,3]) -> local_points lets callers flatten nacelles etc. Local frame:
    x = axial, y/z = radial (z = 'up' of the u/v frame)."""
    prof = np.asarray(profile, dtype=float)
    a, u, v = frame_from_axis(axis)
    # Ensure v points as close to world up as possible so 'deform' can talk about up/down.
    if v[2] < 0:
        u, v = -u, -v
    m = len(prof)
    if normals2d is not None:
        n2 = np.asarray(normals2d, dtype=float)
        closed_profile = False
    else:
        n2 = profile_normals(prof, closed_profile, outward_sign)
    full = abs(sweep - 2 * math.pi) < 1e-9
    nseg = segments
    angles = start_angle + np.arange(nseg + (0 if full else 1)) * (sweep / nseg)
    cos, sin = np.cos(angles), np.sin(angles)
    local = np.zeros((m, len(angles), 3))
    local[:, :, 0] = prof[:, 0:1]
    local[:, :, 1] = prof[:, 1:2] * cos[None, :]
    local[:, :, 2] = prof[:, 1:2] * sin[None, :]
    lref = np.zeros_like(local)
    lref[:, :, 0] = n2[:, 0:1]
    lref[:, :, 1] = n2[:, 1:2] * cos[None, :]
    lref[:, :, 2] = n2[:, 1:2] * sin[None, :]
    if deform is not None:
        local = deform(local)
    basis = np.stack([a, u, v])  # rows
    world = np.asarray(origin, dtype=float) + local @ basis
    wref = lref @ basis
    rings = world  # [m, nang, 3]; m = 'u' direction, angles = 'v'
    if closed_profile:
        # wrap in u too: append the first profile point at the end
        rings = np.concatenate([rings, rings[:1]], axis=0)
        wref = np.concatenate([wref, wref[:1]], axis=0)
    grid(buf, rings, wref, closed=full)
    return world


def cylinder(buf, p0, p1, radius, segments=12, caps=True, radius1=None):
    p0, p1 = np.asarray(p0, dtype=float), np.asarray(p1, dtype=float)
    r1 = radius if radius1 is None else radius1
    length = np.linalg.norm(p1 - p0)
    profile = [(0.0, radius), (length, r1)]
    lathe(buf, profile, p0, p1 - p0, segments, closed_profile=False, outward_sign=-1.0)
    if caps:
        a, u, v = frame_from_axis(p1 - p0)
        if v[2] < 0:
            u, v = -u, -v
        ang = np.arange(segments) * (2 * math.pi / segments)
        for center, r, n in ((p0, radius, -a), (p1, r1, a)):
            ring = center + r * (np.cos(ang)[:, None] * u + np.sin(ang)[:, None] * v)
            fan(buf, ring, center, n)


def clip_polygon(pts, plane_normal, plane_offset):
    """Keep the part of a convex polygon where dot(p, n) >= offset (Sutherland-Hodgman)."""
    out = []
    n = np.asarray(plane_normal, dtype=float)
    k = len(pts)
    for i in range(k):
        a, b = pts[i], pts[(i + 1) % k]
        da, db = a @ n - plane_offset, b @ n - plane_offset
        if da >= 0:
            out.append(a)
        if (da >= 0) != (db >= 0):
            t = da / (da - db)
            out.append(a + (b - a) * t)
    return np.array(out) if out else np.zeros((0, 3))


def rounded_rect(width, height, radius, corner_segments=3):
    """2D rounded rectangle outline centred on the origin (counter-clockwise)."""
    r = min(radius, width / 2, height / 2)
    hx, hy = width / 2 - r, height / 2 - r
    pts = []
    for cx, cy, a0 in ((hx, hy, 0), (-hx, hy, 90), (-hx, -hy, 180), (hx, -hy, 270)):
        for k in range(corner_segments + 1):
            a = math.radians(a0 + 90 * k / corner_segments)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return np.array(pts)
