"""Generate the ground-support vehicles as .glb files plus a JSON sidecar each.

    python3 build.py [output_dir]

Default output: GroundCrewVR/Assets/GroundCrew/Art/Vehicles/
glTF frame: +Y up, +Z forward, +X = vehicle left; origin on the ground under the middle of the
wheelbase (the tow bar's origin is its lunette eye).
"""
import json
import os
import sys

import numpy as np

import vehicles
from gltf_writer import GltfBuilder


def convert(P):
    """Vehicle frame (x fwd, y left, z up) -> glTF (X left, Y up, Z fwd)."""
    P = np.asarray(P, dtype=float).reshape(-1, 3)
    return np.stack([P[:, 1], P[:, 2], P[:, 0]], axis=1)


def r3(v):
    return [round(float(x), 4) for x in v]


def export(make, out_dir):
    vid, root, meta = make()
    b = GltfBuilder("GroundCrewVR VehicleGenerator")
    mats = {}
    for name, (rgb, metallic, rough, emissive, alpha, double) in vehicles.MATERIALS.items():
        mats[name] = b.material(name, rgb, metallic, rough, emissive, double_sided=double, alpha=alpha)
    nodes, tris = {}, 0

    def emit(part, parent_pivot):
        nonlocal tris
        prims = []
        for material, buf in part.buffers.items():
            if buf.count == 0:
                continue
            P, N, UV, I = buf.finalize(convert, part.pivot)
            prims.append((mats[material], P, N, UV, I))
            if material != "Collision":
                tris += len(I)
        mesh = b.mesh(part.name, prims) if prims else None
        children = [emit(c, part.pivot) for c in part.children]
        nodes.setdefault(part.name, convert(part.pivot)[0])
        translation = convert(part.pivot)[0] - (convert(parent_pivot)[0] if parent_pivot is not None else 0.0)
        return b.node(part.name, mesh, translation, children)

    root_node = emit(root, None)
    os.makedirs(out_dir, exist_ok=True)
    size = b.write(os.path.join(out_dir, vid + ".glb"), [root_node])

    def find(name):
        stack = [root]
        while stack:
            p = stack.pop()
            if p.name == name:
                return p
            stack += p.children
        return None

    wheels = []
    for name, pos in nodes.items():
        if name.startswith("Wheel_"):
            ex = find(name).extras
            wheels.append({"name": name, "position": r3(pos), "radius": ex["radius"], "width": round(ex["width"], 3),
                           "steer": ex["steer"], "drive": ex["drive"]})
    points = [{"name": n, "position": r3(p)} for n, p in nodes.items()
              if n in ("DriverEye", "ExitPoint", "Hitch_Front", "Hitch_Rear", "TowHead", "Eye", "HoseNozzle", "BondingReel")]
    sidecar = {"id": vid, "units": "metres",
               "coordinateSystem": "glTF: +Y up, +Z forward, +X = vehicle left; origin on the ground under the middle of the wheelbase",
               "triangles": tris, "wheels": sorted(wheels, key=lambda w: w["name"]), "points": points}
    for k, v in meta.items():
        if k == "centerOfMass":
            v = r3(convert(v)[0])
        elif isinstance(v, dict):
            v = {kk: (r3(convert(vv)[0]) if kk in ("center", "pivot") else vv) for kk, vv in v.items()}
            if "size" in v:
                sx, sy, sz = v["size"]
                v["size"] = [sy, sz, sx]
        sidecar[k] = v
    with open(os.path.join(out_dir, vid + ".json"), "w") as f:
        json.dump(sidecar, f, indent=1)
    return vid, size, tris


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(here, "..", "..", "Assets", "GroundCrew", "Art", "Vehicles"))
    for make in vehicles.ALL:
        vid, size, tris = export(make, out_dir)
        print(f"{vid}.glb: {size / 1e3:.0f} kB | {tris:,} tris")


if __name__ == "__main__":
    main()
