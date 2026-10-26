"""Generate the Ground Crew VR aircraft as .glb files plus a JSON sidecar.

    python3 build.py [output_dir]

Default output: GroundCrewVR/Assets/GroundCrew/Art/Aircraft/

Each .glb contains:
    <AircraftId>
      LOD0   full detail: openable doors with cabin/hold interior, moving control surfaces,
             spinning fans, steerable nose gear, rolling wheels
      LOD1   same parts, lower resolution, no cabin furniture
      LOD2   single merged static mesh for distance
      ServicePoints   empty nodes (SP_*) where ground equipment connects / is placed

Moving parts carry three empty children: HingeEnd (a point on the hinge axis), TestPoint and
OpenHint (TestPoint after a small *opening* rotation). AircraftRig.cs derives axis and opening
direction from them, so it works whatever axis convention the glTF importer uses.
"Opening" = doors open, flaps/ailerons/elevators trailing edge down, wheels roll forward.
The same information is in <AircraftId>.json together with holds, seat map and doors.
"""
import json
import math
import os
import sys

import numpy as np

import aircraft
import specs
from gltf_writer import GltfBuilder


def make_converter(s_origin, pitch_k, s_pivot):
    """Aircraft frame -> glTF, tilting the level model nose-down about the main gear (small-angle shear)."""
    def convert(P):
        P = np.asarray(P, dtype=float).reshape(-1, 3)
        return np.stack([P[:, 1], P[:, 2] + (P[:, 0] - s_pivot) * pitch_k, s_origin - P[:, 0]], axis=1)
    return convert


PITCH_K = 0.0


def vec_convert(v):
    v = np.asarray(v, dtype=float)
    return np.array([v[1], v[2] + v[0] * PITCH_K, -v[0]])


def export(sp, out_dir):
    global PITCH_K
    s_origin = sp["nose_gear"]["s"]
    PITCH_K = math.tan(math.radians(sp.get("pitch_deg", 0.0)))
    convert = make_converter(s_origin, PITCH_K, sp["main_gear"]["s"])
    b = GltfBuilder()
    mats = {name: b.material(name, rgb, metallic, rough, emissive) for name, (rgb, metallic, rough, emissive) in aircraft.MATERIALS.items()}
    moving_parts = []
    stats = {}

    def emit(part, parent_pivot, path):
        prims = []
        for material, buf in part.buffers.items():
            if buf.count == 0:
                continue
            P, N, UV, I = buf.finalize(convert, part.pivot)
            prims.append((mats[material], P, N, UV, I))
        mesh = b.mesh(part.name, prims) if prims else None
        children = []
        extras = None
        hinge = part.extras.get("hinge")
        if hinge is not None:
            axis = vec_convert(hinge["axis"])
            axis = axis / np.linalg.norm(axis)
            r = vec_convert(np.asarray(hinge["test"]) - part.pivot)
            if np.dot(np.cross(axis, r), vec_convert(hinge["desired"])) < 0:
                axis = -axis
            # Importer-independent hinge description (an importer may mirror an axis, which flips
            # rotation direction): HingeEnd gives the axis; TestPoint rotated by +10 degrees (glTF,
            # right-hand rule) lands on OpenHint, so Unity code can find the opening direction itself.
            children.append(b.node("HingeEnd", translation=axis * 0.5))
            test = r / max(np.linalg.norm(r), 1e-6) * min(max(np.linalg.norm(r), 0.3), 1.0)
            k = np.radians(10.0)
            rotated = test * np.cos(k) + np.cross(axis, test) * np.sin(k) + axis * np.dot(axis, test) * (1 - np.cos(k))
            children.append(b.node("TestPoint", translation=test))
            children.append(b.node("OpenHint", translation=rotated))
            extras = {k: v for k, v in part.extras.items() if k != "hinge"}
            extras.update({"hingeAxis": [round(float(x), 5) for x in axis], "minAngle": hinge["min"], "maxAngle": hinge["max"]})
            moving_parts.append({"path": f"{path}/{part.name}", **extras})
        elif part.extras:
            extras = {k: (v.tolist() if isinstance(v, np.ndarray) else v) for k, v in part.extras.items()}
        for c in part.children:
            children.append(emit(c, part.pivot, f"{path}/{part.name}"))
        # Top-level groups are placed relative to the glTF origin (the nose-gear contact point).
        translation = convert(part.pivot)[0] - (convert(parent_pivot)[0] if parent_pivot is not None else 0.0)
        return b.node(part.name, mesh, translation, children, extras)

    lod_nodes, info = [], None
    for lod in (0, 1, 2):
        group, meta = aircraft.build(sp, lod)
        stats[f"LOD{lod}"] = group.triangle_count()
        lod_nodes.append(emit(group, None, sp["id"]))
        if lod == 0:
            info = meta

    sp_nodes = []
    service_points = {}
    for name, d in sorted(info["service_points"].items()):
        pos = convert(d["position"])[0]
        if d.get("ground"):
            pos[1] = 0.0
        normal = vec_convert(d["normal"])
        service_points[name] = {"position": [round(float(x), 4) for x in pos], "normal": [round(float(x), 4) for x in normal]}
        if "sillHeight" in d:
            service_points[name]["sillHeight"] = round(float(pos[1]), 3)
        sp_nodes.append(b.node(name, translation=pos))
    sp_root = b.node("ServicePoints", children=sp_nodes)
    root = b.node(sp["id"], children=lod_nodes + [sp_root], extras={"label": sp["label"], "units": "metres",
                                                                     "origin": "nose-gear ground contact, nose towards +Z"})
    os.makedirs(out_dir, exist_ok=True)
    glb_path = os.path.join(out_dir, sp["id"] + ".glb")
    size = b.write(glb_path, [root])

    fus = info["fuselage"]
    sidecar = {
        "id": sp["id"],
        "label": sp["label"],
        "units": "metres",
        "coordinateSystem": "glTF: +Y up, +Z forward (nose), +X = aircraft left; origin = nose-gear ground contact",
        "dimensions": {"length": sp["length"], "fuselageWidth": sp["width"], "fuselageHeight": sp["height"],
                       "wingspan": round(2 * (aircraft.wing_tip_y(sp["wing"]) + (0.73 if sp["wing"]["tip"]["type"] == "blended" else 0.03)), 2),
                       "floorHeight": sp["floor_z"]},
        "triangles": stats,
        "lodGroups": ["LOD0", "LOD1", "LOD2"],
        "movingParts": moving_parts,
        "doors": [{"name": d["name"], "side": d["side"], "type": d["kind"], "opens": d["opens"],
                   "width": round(d["s"][1] - d["s"][0], 3), "height": round(d["z"][1] - d["z"][0], 3),
                   "sillHeight": round(d["z"][0] - aircraft.pitch_drop(sp, 0.5 * (d["s"][0] + d["s"][1])), 3), "maxAngle": d["max"], "removable": bool(d.get("removable", False))} for d in sp["doors"]],
        "holds": [{"name": h["name"], "center": [round(float(x), 3) for x in convert(h["center"])[0]],
                   "size": [round(h["size"][1], 3), round(h["size"][2], 3), round(h["size"][0], 3)],
                   "maxMassKg": h["maxMassKg"]} for h in info["holds"]],
        "seats": [{"seat": s["seat"], "position": [round(float(x), 3) for x in convert(s["position"])[0]]} for s in info["seats"]],
        "servicePoints": service_points,
        "notes": "Generated by GroundCrewVR/Tools/AircraftGenerator. Positions marked APPROX in specs.py should be "
                 "checked against the manufacturer's airport-planning document before they drive gameplay.",
    }
    with open(os.path.join(out_dir, sp["id"] + ".json"), "w") as f:
        json.dump(sidecar, f, indent=1)
    return glb_path, size, stats, len(info["seats"])


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    default_out = os.path.normpath(os.path.join(here, "..", "..", "Assets", "GroundCrew", "Art", "Aircraft"))
    out_dir = sys.argv[1] if len(sys.argv) > 1 else default_out
    for sp in specs.ALL:
        path, size, stats, seats = export(sp, out_dir)
        tri = ", ".join(f"{k} {v:,} tris" for k, v in stats.items())
        print(f"{os.path.basename(path)}: {size / 1e6:.1f} MB | {tri} | {seats} seats")


if __name__ == "__main__":
    main()
