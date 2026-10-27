"""Generate the player's gloved hand (right hand; the left is the same model mirrored in Unity).

    python3 build.py [output_dir]

Default output: GroundCrewVR/Assets/GroundCrew/Art/Hands/Hand_R.glb (+ Hand_R.json).

A generic work glove of the kind ground crew wear: dark synthetic-leather palm and fingers,
hi-vis back panel, rubber knuckle guards, orange cuff with a reflective band. Proportions are
an average adult hand (APPROX); nothing is copied from a product.

Frame (generator): x forward (along the fingers), y left, z up, metres; origin at the middle of
the wrist; palm facing down, thumb on the left (+y). glTF: X = left, Y = up, Z = forward.

Node contract (read by GroundCrew.Player.ControllerHands):
    Hand_R                       root, at the wrist
      Palm, Cuff                 static meshes
      Thumb1 > Thumb2 > Thumb3   joint pivots, rest rotation identity
      Index1 > Index2 > Index3   (same for Middle, Ring, Pinky)
        Bend                     helper child of every joint: Unity's Quaternion.AngleAxis(+angle,
                                 Bend.localPosition) curls that joint towards the palm
      Index3 > IndexTip          empty at the fingertip pad (the poke point goes here)
"""
import json
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "AircraftGenerator"))
from meshlib import Part, fan, grid, lathe, normalize, rounded_rect  # noqa: E402
from gltf_writer import GltfBuilder  # noqa: E402

MATERIALS = {
    # name: (rgb, metallic, roughness)
    "GloveDark": ((0.11, 0.11, 0.12), 0.0, 0.80),
    "GlovePalm": ((0.20, 0.19, 0.18), 0.0, 0.90),
    "GloveHiVis": ((0.80, 0.90, 0.08), 0.0, 0.60),
    "GloveRubber": ((0.06, 0.06, 0.065), 0.0, 0.55),
    "GloveCuff": ((0.95, 0.42, 0.05), 0.0, 0.70),
    "Reflective": ((0.78, 0.79, 0.80), 0.5, 0.25),
}

D = math.pi / 180.0


def convert(P):
    """Generator frame (x fwd, y left, z up) -> glTF (X left, Y up, Z fwd)."""
    P = np.asarray(P, dtype=float).reshape(-1, 3)
    return np.stack([P[:, 1], P[:, 2], P[:, 0]], axis=1)


def capsule(buf, p0, direction, length, r0, r1, flat=0.84, segments=12, rings=5):
    """Tapered capsule from p0 along direction: hemispherical ends, flattened top-to-bottom."""
    prof, n2 = [], []
    for k in range(rings + 1):                      # rear hemisphere, axis -> rim
        a = math.pi - (math.pi / 2) * k / rings
        prof.append((r0 * math.cos(a), r0 * math.sin(a)))
        n2.append((math.cos(a), math.sin(a)))
    for k in range(rings + 1):                      # front hemisphere, rim -> axis
        a = (math.pi / 2) * (1 - k / rings)
        prof.append((length + r1 * math.cos(a), r1 * math.sin(a)))
        n2.append((math.cos(a), math.sin(a)))

    def squash(local):
        local = local.copy()
        local[:, :, 2] *= flat
        return local

    lathe(buf, prof, p0, direction, segments, normals2d=n2, deform=squash)


def loft(buf, sections, closed_ends=True):
    """Rounded-rectangle sections along x: [(x, width, thickness, z_centre), ...]."""
    rings, refs = [], []
    for x, w, t, zc in sections:
        rr = rounded_rect(w, t, min(w, t) * 0.48, corner_segments=4)
        ring = np.stack([np.full(len(rr), x), rr[:, 0], rr[:, 1] + zc], axis=1)
        rings.append(ring)
        refs.append(normalize(np.stack([np.zeros(len(rr)), rr[:, 0] / w, rr[:, 1] / t], axis=1)))
    rings, refs = np.array(rings), np.array(refs)
    grid(buf, rings, refs, closed=True)
    if closed_ends:
        fan(buf, rings[0], rings[0].mean(axis=0), np.array([-1.0, 0.0, 0.0]))
        fan(buf, rings[-1], rings[-1].mean(axis=0), np.array([1.0, 0.0, 0.0]))
    return rings


def back_patch(buf, sections, lift=0.0012, start=0.12, end=0.88):
    """Panel over the back of the hand: the top part of each section, raised a little."""
    rings, refs = [], []
    for x, w, t, zc in sections:
        rr = rounded_rect(w * 0.92, t, min(w, t) * 0.48, corner_segments=4)
        top = rr[rr[:, 1] > t * 0.18]
        top = top[np.argsort(-top[:, 0])]           # left to right across the back
        n = normalize(np.stack([np.zeros(len(top)), top[:, 0] / w, top[:, 1] / t], axis=1))
        ring = np.stack([np.full(len(top), x), top[:, 0], top[:, 1] + zc], axis=1) + n * lift
        rings.append(ring)
        refs.append(n)
    m = min(len(r) for r in rings)
    rings = np.array([r[np.linspace(0, len(r) - 1, m).astype(int)] for r in rings])
    refs = np.array([r[np.linspace(0, len(r) - 1, m).astype(int)] for r in refs])
    k0, k1 = int(len(rings) * start), int(math.ceil(len(rings) * end))
    grid(buf, rings[k0:k1], refs[k0:k1], closed=False)


def joint(name, pivot, direction, toward):
    """Joint part plus its Bend helper: AngleAxis(+a, Bend.localPosition) in Unity curls it towards `toward`."""
    part = Part(name, pivot)
    d = normalize(direction)
    t = np.asarray(toward, dtype=float)
    t = normalize(t - (t @ d) * d)
    # Unity is left-handed: the axis that turns d towards t there is t x d here (see README).
    part.child(Part("Bend", np.asarray(pivot) + 0.012 * normalize(np.cross(t, d))))
    return part


FINGERS = [
    # name, knuckle (x, y, z), splay deg (+ = towards thumb), lengths (prox, mid, dist), radii (base, tip)
    ("Index", (0.094, 0.027, -0.001), 5.0, (0.044, 0.026, 0.022), (0.0100, 0.0080)),
    ("Middle", (0.098, 0.009, 0.000), 0.0, (0.048, 0.029, 0.023), (0.0104, 0.0083)),
    ("Ring", (0.094, -0.009, -0.001), -4.0, (0.045, 0.028, 0.022), (0.0099, 0.0079)),
    ("Pinky", (0.085, -0.026, -0.004), -10.0, (0.035, 0.020, 0.019), (0.0088, 0.0071)),
]

PALM = [
    # x, width, thickness, z centre
    (-0.004, 0.064, 0.036, 0.000),
    (0.015, 0.074, 0.038, 0.001),
    (0.035, 0.082, 0.037, 0.002),
    (0.058, 0.086, 0.033, 0.002),
    (0.080, 0.087, 0.028, 0.001),
    (0.094, 0.084, 0.024, 0.000),
    (0.101, 0.076, 0.020, -0.001),
]


def build_hand():
    root = Part("Hand_R", (0.0, 0.0, 0.0))
    down = np.array([0.0, 0.0, -1.0])

    palm = root.child(Part("Palm", (0.0, 0.0, 0.0)))
    loft(palm.buf("GloveDark"), PALM)
    back_patch(palm.buf("GloveHiVis"), PALM)
    # Padded palm patch (slightly raised under the hand).
    pad = [(x, w * 0.8, t, zc - 0.0012) for x, w, t, zc in PALM[1:-1]]
    rings, refs = [], []
    for x, w, t, zc in pad:
        rr = rounded_rect(w, t, min(w, t) * 0.48, corner_segments=4)
        bottom = rr[rr[:, 1] < -t * 0.25]
        bottom = bottom[np.argsort(bottom[:, 0])]
        n = normalize(np.stack([np.zeros(len(bottom)), bottom[:, 0] / w, bottom[:, 1] / t], axis=1))
        rings.append(np.stack([np.full(len(bottom), x), bottom[:, 0], bottom[:, 1] + zc], axis=1) + n * 0.0012)
        refs.append(n)
    m = min(len(r) for r in rings)
    rings = np.array([r[np.linspace(0, len(r) - 1, m).astype(int)] for r in rings])
    refs = np.array([r[np.linspace(0, len(r) - 1, m).astype(int)] for r in refs])
    grid(palm.buf("GlovePalm"), rings, refs, closed=False)

    # Cuff: a short elliptical tube with a reflective band, open end capped dark inside.
    cuff = root.child(Part("Cuff", (0.0, 0.0, 0.0)))
    cuff_sections = [(-0.080, 0.078, 0.056, 0.0), (-0.060, 0.076, 0.054, 0.0), (-0.030, 0.072, 0.050, 0.0), (-0.002, 0.068, 0.044, 0.0)]
    loft(cuff.buf("GloveCuff"), cuff_sections)
    band = [(-0.056, 0.0772, 0.0552, 0.0), (-0.044, 0.0762, 0.0542, 0.0)]
    loft(cuff.buf("Reflective"), band, closed_ends=False)

    tips = {}
    for name, knuckle, splay, lengths, (rb, rt) in FINGERS:
        ang = splay * D
        d = np.array([math.cos(ang), math.sin(ang), -0.02])
        d = normalize(d)
        radii = np.linspace(rb, rt, 4)
        p = np.asarray(knuckle, dtype=float)
        parent = root
        for k in range(3):
            j = parent.child(joint(f"{name}{k + 1}", p, d, down))
            capsule(j.buf("GloveDark"), p, d, lengths[k], radii[k], radii[k + 1])
            if k == 0:
                # Rubber knuckle guard on top of the proximal segment.
                g = p + d * (lengths[0] * 0.45) + np.array([0.0, 0.0, radii[0] * 0.78])
                capsule(j.buf("GloveRubber"), g - d * 0.011, d, 0.022, 0.0055, 0.005, flat=0.45, segments=10, rings=3)
            p = p + d * lengths[k]
            parent = j
        tip = parent.child(Part(f"{name}Tip", p + d * (radii[3] * 0.6) + np.array([0.0, 0.0, -radii[3] * 0.35])))
        tips[name] = tip.pivot

    # Thumb: from the base of the palm, forward, out to the left and down; curls across the palm.
    thumb_chain = [
        ((0.012, 0.024, -0.006), (0.50, 0.80, -0.24), 0.046, (0.0150, 0.0118)),
        (None, (0.68, 0.66, -0.26), 0.033, (0.0114, 0.0102)),
        (None, (0.82, 0.50, -0.24), 0.028, (0.0102, 0.0087)),
    ]
    toward = np.array([0.15, -0.75, -0.55])
    p = np.asarray(thumb_chain[0][0], dtype=float)
    parent = root
    for k, (_, direction, length, (r0, r1)) in enumerate(thumb_chain):
        d = normalize(direction)
        j = parent.child(joint(f"Thumb{k + 1}", p, d, toward))
        capsule(j.buf("GloveDark"), p, d, length, r0, r1, flat=0.88)
        p = p + d * length
        parent = j
    tip = parent.child(Part("ThumbTip", p + d * 0.005))
    tips["Thumb"] = tip.pivot
    return root, tips


def export(out_dir):
    root, tips = build_hand()
    b = GltfBuilder("GroundCrewVR HandGenerator")
    mats = {n: b.material(n, rgb, metallic, rough) for n, (rgb, metallic, rough) in MATERIALS.items()}
    tris = 0

    def emit(part, parent_pivot):
        nonlocal tris
        prims = []
        for material, buf in part.buffers.items():
            if buf.count == 0:
                continue
            P, N, UV, I = buf.finalize(convert, part.pivot)
            prims.append((mats[material], P, N, UV, I))
            tris += len(I)
        mesh = b.mesh(part.name, prims) if prims else None
        children = [emit(c, part.pivot) for c in part.children]
        translation = convert(part.pivot)[0] - (convert(parent_pivot)[0] if parent_pivot is not None else 0.0)
        return b.node(part.name, mesh, translation, children)

    os.makedirs(out_dir, exist_ok=True)
    size = b.write(os.path.join(out_dir, "Hand_R.glb"), [emit(root, None)])
    sidecar = {"id": "Hand_R", "units": "metres",
               "coordinateSystem": "glTF: +Y up, +Z along the fingers, +X = thumb side (left); origin at the wrist, palm down",
               "triangles": tris,
               "tips": {k: [round(float(x), 4) for x in convert(v)[0]] for k, v in tips.items()}}
    with open(os.path.join(out_dir, "Hand_R.json"), "w") as f:
        json.dump(sidecar, f, indent=1)
    return size, tris


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(here, "..", "..", "Assets", "GroundCrew", "Art", "Hands"))
    size, tris = export(out_dir)
    print(f"Hand_R.glb: {size / 1e3:.0f} kB | {tris:,} tris")


if __name__ == "__main__":
    main()
