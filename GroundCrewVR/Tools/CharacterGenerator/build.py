"""Generate the people: pilot, cabin crew and ground crew, as jointed low-poly figures.

    python3 build.py [output_dir]

Default output: GroundCrewVR/Assets/GroundCrew/Art/Characters/{Pilot,CabinCrew,GroundCrew}.glb
plus Characters.json (materials for the Quest material swap).

Each figure is rigid segments on joint nodes (no skinning), posed in code by
GroundCrew.Crew.CrewAnimator. Generic uniforms only - no airline or company liveries.

Frame: Unity (x right, y up, z forward), metres, origin between the feet; converted for glTFast.
Node contract (rest rotations identity; every limb hangs straight down from its pivot):
    Root > Hips > Spine > Neck > Head
                  Spine > UpperArm_L > LowerArm_L > Hand_L > HandAttach_L   (L = -x)
                  Spine > UpperArm_R > ...
           Hips > UpperLeg_L > LowerLeg_L > Foot_L
Joint conventions in Unity: rotating a leg or arm by -angle about x swings it forward;
+angle about x bends a knee; -angle about x bends an elbow forward.
"""
import json
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "AircraftGenerator"))
from meshlib import Part, box, fan, grid, lathe, normalize  # noqa: E402
from gltf_writer import GltfBuilder  # noqa: E402

Y = np.array([0.0, 1.0, 0.0])

MATERIALS = {
    "Skin": ((0.80, 0.62, 0.50), 0.0, 0.75),
    "Hair": ((0.16, 0.11, 0.07), 0.0, 0.70),
    "Eyes": ((0.05, 0.05, 0.06), 0.0, 0.30),
    "Shoes": ((0.05, 0.05, 0.055), 0.0, 0.40),
    "Navy": ((0.08, 0.10, 0.20), 0.0, 0.70),
    "White": ((0.92, 0.93, 0.94), 0.0, 0.65),
    "Gold": ((0.85, 0.66, 0.20), 0.6, 0.35),
    "Teal": ((0.07, 0.30, 0.36), 0.0, 0.65),
    "Scarf": ((0.80, 0.16, 0.18), 0.0, 0.60),
    "Workwear": ((0.14, 0.17, 0.24), 0.0, 0.85),
    "HiVis": ((0.86, 0.93, 0.10), 0.0, 0.65),
    "Reflective": ((0.78, 0.79, 0.80), 0.5, 0.25),
    "EarDefender": ((0.95, 0.40, 0.05), 0.0, 0.55),
    "Glove": ((0.20, 0.20, 0.22), 0.0, 0.85),
    # Passengers: recoloured per person at runtime (CrewAppearance).
    "Top": ((0.55, 0.20, 0.20), 0.0, 0.80),
    "Bottom": ((0.20, 0.25, 0.40), 0.0, 0.85),
    "Bag": ((0.12, 0.14, 0.18), 0.0, 0.70),
}

# Skeleton (metres, for a 1.78 m figure; scaled per character).
HIP_Y, KNEE_Y, ANKLE_Y = 0.93, 0.50, 0.085
HIP_X, SHOULDER_X, SHOULDER_Y = 0.10, 0.205, 1.43
ELBOW_Y, WRIST_Y = 1.14, 0.88
NECK_Y, HEAD_Y = 1.47, 1.55


def convert(P):
    P = np.asarray(P, dtype=float).reshape(-1, 3)
    return np.stack([-P[:, 0], P[:, 1], P[:, 2]], axis=1)


def capsule(buf, p0, p1, r0, r1, flat=0.9, segments=10, rings=3):
    p0, p1 = np.asarray(p0, float), np.asarray(p1, float)
    length = np.linalg.norm(p1 - p0)
    prof, n2 = [], []
    for k in range(rings + 1):
        a = math.pi - (math.pi / 2) * k / rings
        prof.append((r0 * math.cos(a), r0 * math.sin(a)))
        n2.append((math.cos(a), math.sin(a)))
    for k in range(rings + 1):
        a = (math.pi / 2) * (1 - k / rings)
        prof.append((length + r1 * math.cos(a), r1 * math.sin(a)))
        n2.append((math.cos(a), math.sin(a)))

    def squash(local):
        local = local.copy()
        local[:, :, 2] *= flat
        return local

    lathe(buf, prof, p0, p1 - p0, segments, normals2d=n2, deform=squash)


def ellipsoid(buf, centre, rx, ry, rz, segments=12, rings=8, y_from=-1.0, y_to=1.0):
    """Ellipsoid (optionally a band between y_from..y_to in unit-sphere terms)."""
    a0, a1 = math.acos(max(-1, min(1, y_to))), math.acos(max(-1, min(1, y_from)))
    rows = []
    refs = []
    for i in range(rings + 1):
        a = a0 + (a1 - a0) * i / rings
        ring, ref = [], []
        for j in range(segments):
            b = 2 * math.pi * j / segments
            n = np.array([math.sin(a) * math.sin(b), math.cos(a), math.sin(a) * math.cos(b)])
            ring.append(np.asarray(centre) + n * [rx, ry, rz])
            ref.append(n)
        rows.append(ring)
        refs.append(ref)
    rows, refs = np.array(rows), np.array(refs)
    grid(buf, rows, refs, closed=True)
    if y_to < 1.0:
        fan(buf, rows[0], rows[0].mean(axis=0), Y)
    if y_from > -1.0:
        fan(buf, rows[-1], rows[-1].mean(axis=0), -Y)


def loft(buf, sections, segments=14):
    """Horizontal elliptical sections bottom to top: (y, half width, half depth, z offset)."""
    rings, refs = [], []
    for y, hw, hd, zo in sections:
        ang = np.linspace(0, 2 * math.pi, segments, endpoint=False)
        ring = np.stack([hw * np.sin(ang), np.full(segments, y), zo + hd * np.cos(ang)], axis=1)
        rings.append(ring)
        refs.append(normalize(np.stack([np.sin(ang) / hw, np.zeros(segments), np.cos(ang) / hd], axis=1)))
    rings, refs = np.array(rings), np.array(refs)
    grid(buf, rings, refs, closed=True)
    fan(buf, rings[0], rings[0].mean(axis=0), -Y)
    fan(buf, rings[-1], rings[-1].mean(axis=0), Y)


def build(kind):
    """kind: 'pilot', 'cabin', 'ground', 'passenger'."""
    outfit = {
        "pilot": dict(shirt="White", trousers="Navy", sleeves="White", hands="Skin"),
        "cabin": dict(shirt="Teal", trousers="Teal", sleeves="Teal", hands="Skin"),
        "ground": dict(shirt="Workwear", trousers="Workwear", sleeves="Workwear", hands="Glove"),
        "passenger": dict(shirt="Top", trousers="Bottom", sleeves="Top", hands="Skin"),
    }[kind]
    root = Part("Root")
    hips = root.child(Part("Hips", (0, HIP_Y + 0.02, 0)))
    loft(hips.buf(outfit["trousers"]), [(HIP_Y - 0.08, 0.15, 0.10, 0.0), (HIP_Y + 0.02, 0.165, 0.11, -0.005), (HIP_Y + 0.10, 0.155, 0.10, 0.0)])
    if kind not in ("cabin", "passenger"):
        loft(hips.buf("Shoes"), [(HIP_Y + 0.085, 0.158, 0.104, 0.0), (HIP_Y + 0.115, 0.158, 0.104, 0.0)])   # belt

    spine = hips.child(Part("Spine", (0, HIP_Y + 0.10, 0)))
    torso = [(HIP_Y + 0.09, 0.150, 0.100, 0.0), (1.12, 0.145, 0.098, 0.005), (1.24, 0.165, 0.110, 0.012),
             (1.34, 0.180, 0.112, 0.010), (1.42, 0.175, 0.095, 0.0), (1.465, 0.09, 0.06, 0.0)]
    loft(spine.buf(outfit["shirt"]), torso)
    if kind == "pilot":
        box(spine.buf("Navy"), (0, 1.30, 0.118), (0.045, 0.24, 0.01))                     # tie
        box(spine.buf("Navy"), (0, 1.405, 0.10), (0.06, 0.035, 0.012))
        for sx in (-1, 1):                                                                # epaulettes
            box(spine.buf("Navy"), (sx * 0.135, 1.43, 0.0), (0.10, 0.012, 0.055))
            for k in range(4 if sx < 0 else 3):
                box(spine.buf("Gold"), (sx * (0.10 + k * 0.022), 1.437, 0.0), (0.008, 0.004, 0.056))
        box(spine.buf("Gold"), (-0.10, 1.31, 0.118), (0.05, 0.012, 0.005))               # wings
    if kind == "cabin":
        loft(spine.buf("White"), [(1.40, 0.10, 0.075, 0.0), (1.47, 0.07, 0.055, 0.0)])   # collar
        loft(spine.buf("Scarf"), [(1.44, 0.088, 0.066, 0.004), (1.475, 0.075, 0.058, 0.004)])
        box(spine.buf("Scarf"), (0.03, 1.40, 0.10), (0.05, 0.07, 0.012))
        for k in range(3):
            box(spine.buf("Gold"), (0, 1.20 + k * 0.07, 0.123), (0.014, 0.014, 0.006))     # buttons
        box(spine.buf("Gold"), (-0.10, 1.33, 0.117), (0.05, 0.015, 0.005))               # name badge
    if kind == "ground":
        vest = [(HIP_Y + 0.07, 0.160, 0.110, 0.0), (1.12, 0.156, 0.108, 0.005), (1.24, 0.176, 0.120, 0.012),
                (1.34, 0.190, 0.122, 0.010), (1.415, 0.178, 0.10, 0.0)]
        loft(spine.buf("HiVis"), vest)
        for y in (1.06, 1.16):
            loft(spine.buf("Reflective"), [(y, 0.158, 0.110, 0.006), (y + 0.035, 0.158, 0.110, 0.006)])
        for sx in (-1, 1):                                                                # over-the-shoulder bands
            box(spine.buf("Reflective"), (sx * 0.07, 1.30, 0.124), (0.035, 0.22, 0.008))
            box(spine.buf("Reflective"), (sx * 0.07, 1.30, -0.116), (0.035, 0.22, 0.008))
        box(spine.buf("Workwear"), (0.0, 1.36, 0.125), (0.13, 0.05, 0.006))               # radio patch
    if kind == "passenger":
        pack = spine.child(Part("Backpack", (0, 1.25, -0.12)))                          # hidden on some people
        box(pack.buf("Bag"), (0, 1.22, -0.19), (0.27, 0.36, 0.13))
        box(pack.buf("Bag"), (0, 1.12, -0.26), (0.20, 0.14, 0.04))
        for sx in (-1, 1):
            box(pack.buf("Bag"), (sx * 0.09, 1.33, 0.0), (0.035, 0.012, 0.24))
    neck = spine.child(Part("Neck", (0, NECK_Y, 0)))
    capsule(neck.buf("Skin"), (0, NECK_Y - 0.03, 0.0), (0, HEAD_Y + 0.02, 0.005), 0.05, 0.048)
    head = neck.child(Part("Head", (0, HEAD_Y, 0.0)))
    hc = np.array([0, HEAD_Y + 0.115, 0.012])
    ellipsoid(head.buf("Skin"), hc, 0.085, 0.112, 0.100)
    ellipsoid(head.buf("Skin"), hc + [0, -0.075, 0.035], 0.06, 0.04, 0.06, 10, 5)        # jaw
    for sx in (-1, 1):
        box(head.buf("Eyes"), hc + [sx * 0.033, 0.012, 0.094], (0.016, 0.009, 0.01))
        box(head.buf("Hair"), hc + [sx * 0.033, 0.034, 0.092], (0.03, 0.007, 0.01))       # brows
        ellipsoid(head.buf("Skin"), hc + [sx * 0.086, 0.0, 0.0], 0.012, 0.028, 0.018, 6, 3)  # ears
    box(head.buf("Skin"), hc + [0, -0.012, 0.103], (0.02, 0.04, 0.025))                    # nose
    box(head.buf("Eyes"), hc + [0, -0.056, 0.092], (0.032, 0.006, 0.01))                   # mouth
    if kind == "cabin":
        ellipsoid(head.buf("Hair"), hc + [0, 0.012, -0.004], 0.091, 0.112, 0.105, 12, 6, y_from=-0.15)
        ellipsoid(head.buf("Hair"), hc + [0, 0.02, -0.105], 0.05, 0.045, 0.04, 8, 4)       # bun
    else:
        ellipsoid(head.buf("Hair"), hc + [0, 0.018, -0.006], 0.089, 0.108, 0.104, 12, 6, y_from=0.15)
    if kind == "pilot":
        ellipsoid(head.buf("Navy"), hc + [0, 0.085, 0.0], 0.105, 0.045, 0.115, 12, 4, y_from=0.0)
        lathe(head.buf("Navy"), [(0.0, 0.0), (0.0, 0.095), (0.045, 0.095), (0.045, 0.0)], hc + [0, 0.062, 0.0], Y, 12,
              closed_profile=False, outward_sign=-1.0)
        box(head.buf("Shoes"), hc + [0, 0.065, 0.12], (0.16, 0.012, 0.07))               # peak
        lathe(head.buf("Gold"), [(0.0, 0.097), (0.012, 0.097)], hc + [0, 0.07, 0.0], Y, 12, closed_profile=False, outward_sign=-1.0)
    if kind == "ground":
        for sx in (-1, 1):                                                                # ear defenders
            cup = hc + [sx * 0.098, -0.005, 0.0]
            lathe(head.buf("EarDefender"), [(0.0, 0.0), (0.0, 0.045), (0.03, 0.04), (0.035, 0.0)], cup, (sx, 0, 0), 10,
                  closed_profile=False, outward_sign=-1.0)
        for k in range(9):
            a0, a1 = math.pi * k / 9, math.pi * (k + 1) / 9
            p0 = hc + [-0.11 * math.cos(a0), 0.118 * math.sin(a0), -0.01]
            p1 = hc + [-0.11 * math.cos(a1), 0.118 * math.sin(a1), -0.01]
            capsule(head.buf("Shoes"), p0, p1, 0.008, 0.008, 1.0, 5, 1)

    for side, sx in (("L", -1), ("R", 1)):
        ua = spine.child(Part(f"UpperArm_{side}", (sx * SHOULDER_X, SHOULDER_Y, 0)))
        capsule(ua.buf(outfit["sleeves"]), (sx * SHOULDER_X, SHOULDER_Y, 0), (sx * (SHOULDER_X + 0.01), ELBOW_Y, 0), 0.052, 0.043)
        if kind == "ground":
            capsule(ua.buf("HiVis"), (sx * SHOULDER_X, SHOULDER_Y + 0.005, 0), (sx * (SHOULDER_X + 0.004), SHOULDER_Y - 0.07, 0), 0.058, 0.052, 1.0, 10, 2)
        la = ua.child(Part(f"LowerArm_{side}", (sx * (SHOULDER_X + 0.01), ELBOW_Y, 0)))
        sleeve_end = WRIST_Y + 0.03 if kind != "pilot" else WRIST_Y + 0.02
        capsule(la.buf(outfit["sleeves"]), (sx * (SHOULDER_X + 0.01), ELBOW_Y, 0), (sx * (SHOULDER_X + 0.012), sleeve_end, 0.0), 0.043, 0.036)
        if kind == "pilot":                                                               # cuff stripes
            box(la.buf("Gold"), (sx * (SHOULDER_X + 0.012), sleeve_end + 0.035, 0.0), (0.09, 0.012, 0.075))
        hand = la.child(Part(f"Hand_{side}", (sx * (SHOULDER_X + 0.012), WRIST_Y, 0)))
        hx = sx * (SHOULDER_X + 0.012)
        box(hand.buf(outfit["hands"]), (hx, WRIST_Y - 0.05, 0.005), (0.035, 0.10, 0.085))
        capsule(hand.buf(outfit["hands"]), (hx - sx * 0.0, WRIST_Y - 0.03, 0.05), (hx, WRIST_Y - 0.07, 0.065), 0.014, 0.012, 1.0, 6, 1)  # thumb
        hand.child(Part(f"HandAttach_{side}", (hx, WRIST_Y - 0.08, 0.02)))

        ul = hips.child(Part(f"UpperLeg_{side}", (sx * HIP_X, HIP_Y, 0)))
        capsule(ul.buf(outfit["trousers"]), (sx * HIP_X, HIP_Y, 0), (sx * HIP_X, KNEE_Y, 0.01), 0.078, 0.058)
        ll = ul.child(Part(f"LowerLeg_{side}", (sx * HIP_X, KNEE_Y, 0.01)))
        capsule(ll.buf(outfit["trousers"]), (sx * HIP_X, KNEE_Y, 0.01), (sx * HIP_X, ANKLE_Y + 0.03, 0.0), 0.057, 0.045)
        if kind == "ground":
            for y in (0.20, 0.28):
                lathe(ll.buf("Reflective"), [(0.0, 0.052), (0.035, 0.054)], (sx * HIP_X, y, 0.003), Y, 10, closed_profile=False, outward_sign=-1.0)
        foot = ll.child(Part(f"Foot_{side}", (sx * HIP_X, ANKLE_Y, 0.0)))
        box(foot.buf("Shoes"), (sx * HIP_X, 0.045, 0.055), (0.10, 0.09, 0.27))
        box(foot.buf("Shoes"), (sx * HIP_X, 0.095, -0.01), (0.09, 0.05, 0.12))
    return root


# Sitting pose, identical to CrewAnimator's Sit (Unity Euler degrees per joint; hips offset in metres).
SIT_POSE = {
    "Spine": (-4, 0, 0),
    "UpperLeg_L": (-88, 0, -4), "UpperLeg_R": (-88, 0, 4), "LowerLeg_L": (85, 0, 0), "LowerLeg_R": (85, 0, 0),
    "UpperArm_L": (-22, 0, -4), "UpperArm_R": (-22, 0, 4), "LowerArm_L": (-55, 0, 0), "LowerArm_R": (-55, 0, 0),
}
SIT_HIPS = (0.0, -0.44, -0.04)


def euler(ex, ey, ez):
    """Unity Quaternion.Euler as a matrix: Ry * Rx * Rz."""
    x, y, z = math.radians(ex), math.radians(ey), math.radians(ez)
    rx = np.array([[1, 0, 0], [0, math.cos(x), -math.sin(x)], [0, math.sin(x), math.cos(x)]])
    ry = np.array([[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]])
    rz = np.array([[math.cos(z), -math.sin(z), 0], [math.sin(z), math.cos(z), 0], [0, 0, 1]])
    return ry @ rx @ rz


def pose_in_place(part, parent_m=np.eye(3), parent_t=np.zeros(3)):
    """Forward kinematics: bake the sitting pose into the vertices (each part rotates about its pivot)."""
    r = euler(*SIT_POSE.get(part.name, (0, 0, 0)))
    offset = np.array(SIT_HIPS) if part.name == "Hips" else np.zeros(3)
    # world(v) = parent_m @ (pivot + offset + r @ (v - pivot)) + parent_t
    m = parent_m @ r
    t = parent_m @ (part.pivot + offset - r @ part.pivot) + parent_t
    for buf in part.buffers.values():
        buf.P = [(m @ np.asarray(P).T).T + t for P in buf.P]
        buf.R = [(m @ np.asarray(R).T).T for R in buf.R]
    for c in part.children:
        pose_in_place(c, m, t)


def build_seated(kind):
    """One mesh, already sitting: one renderer per seated passenger instead of ~16 jointed parts."""
    root = build(kind)
    for part in [root] + [p for p in root.children]:
        for c in list(part.children):
            for g in list(c.children):
                if g.name == "Backpack":
                    c.children.remove(g)          # stowed in the overhead bin
    pose_in_place(root)
    seated = Part("Seated")
    seated.absorb(root)
    return seated


def export(kind, name, scale, out_dir, b_meta, seated=False):
    root = build_seated(kind) if seated else build(kind)
    b = GltfBuilder("GroundCrewVR CharacterGenerator")
    mats = {}
    tris = 0

    def emit(part, parent_pivot):
        nonlocal tris
        prims = []
        for material, buf in part.buffers.items():
            if buf.count == 0:
                continue
            if material not in mats:
                rgb, metallic, rough = MATERIALS[material]
                mats[material] = b.material(material, rgb, metallic, rough)
            P, N, UV, I = buf.finalize(lambda q: convert(q) * scale, part.pivot)
            prims.append((mats[material], P, N, None, I))
            tris += len(I)
        mesh = b.mesh(part.name, prims) if prims else None
        children = [emit(c, part.pivot) for c in part.children]
        translation = (convert(part.pivot)[0] - (convert(parent_pivot)[0] if parent_pivot is not None else 0.0)) * scale
        return b.node(part.name, mesh, translation, children)

    size = b.write(os.path.join(out_dir, name + ".glb"), [emit(root, None)])
    b_meta[name] = {"triangles": tris, "height": round(1.78 * scale, 3)}
    return size, tris


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(here, "..", "..", "Assets", "GroundCrew", "Art", "Characters"))
    os.makedirs(out_dir, exist_ok=True)
    meta = {}
    for kind, name, scale in (("pilot", "Pilot", 1.0), ("cabin", "CabinCrew", 0.96), ("ground", "GroundCrew", 1.01), ("passenger", "Passenger", 0.98)):
        size, tris = export(kind, name, scale, out_dir, meta)
        print(f"{name}.glb: {size / 1e3:.0f} kB | {tris:,} tris")
    size, tris = export("passenger", "PassengerSeated", 0.98, out_dir, meta, seated=True)
    print(f"PassengerSeated.glb: {size / 1e3:.0f} kB | {tris:,} tris")
    sidecar = {"characters": meta,
               "materials": [{"name": n, "color": list(rgb), "metallic": m, "smoothness": round(1 - r, 3), "emission": [], "texture": "",
                              "tileMetres": 0.0, "shader": ""} for n, (rgb, m, r) in MATERIALS.items()]}
    with open(os.path.join(out_dir, "Characters.json"), "w") as f:
        json.dump(sidecar, f, indent=1)


if __name__ == "__main__":
    main()
