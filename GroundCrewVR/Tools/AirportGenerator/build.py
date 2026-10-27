"""Generate the airport as Airport.glb + Airport.json + tiling ground textures.

    python3 build.py [output_dir]

Default output: GroundCrewVR/Assets/GroundCrew/Art/Airport/ (Airport.glb, Airport.json,
Textures/*.png). Unity: Ground Crew > Airport > Build Airport Around Sandbox places it, swaps the
glTF materials for cheaper URP Simple Lit ones (using the PNGs and the JSON), adds colliders and
teleport areas and parks aircraft at some gates.
"""
import json
import os
import sys

import numpy as np

import airport
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "AircraftGenerator"))
from gltf_writer import GltfBuilder, encode_png  # noqa: E402


def convert(P):
    """Unity world (x east, y up, z north) -> glTF (glTFast mirrors X on import)."""
    P = np.asarray(P, dtype=float).reshape(-1, 3)
    return np.stack([-P[:, 0], P[:, 1], P[:, 2]], axis=1)


# ---------------------------------------------------------------------- textures

def tile_noise(rng, size, cells):
    """Tileable value noise in 0..1 (bilinear over a wrapped random grid)."""
    g = rng.random((cells, cells))
    t = np.arange(size) * cells / size
    i0 = np.floor(t).astype(int)
    f = t - i0
    f = f * f * (3 - 2 * f)
    i1 = (i0 + 1) % cells
    a = g[i0][:, i0] * (1 - f)[None, :] + g[i0][:, i1] * f[None, :]
    b = g[i1][:, i0] * (1 - f)[None, :] + g[i1][:, i1] * f[None, :]
    return a * (1 - f)[:, None] + b * f[:, None]


def fbm(rng, size, octaves):
    out, amp, total = np.zeros((size, size)), 1.0, 0.0
    for cells in octaves:
        out += amp * tile_noise(rng, size, cells)
        total += amp
        amp *= 0.55
    return out / total


def to_rgb(base, shade):
    img = np.clip(np.asarray(base)[None, None, :] * shade[:, :, None], 0, 1)
    return (img * 255 + 0.5).astype(np.uint8)


def make_textures():
    rng = np.random.default_rng(24)
    n = 256
    tex = {}
    # Concrete slab (one 5 m panel per tile): mottling, aggregate speckle, saw-cut joints, a few stains.
    shade = 0.86 + 0.16 * fbm(rng, n, (4, 8, 16, 32, 64)) + 0.05 * (rng.random((n, n)) - 0.5)
    for k in range(3):
        cx, cy, r = rng.integers(0, n, 2).tolist() + [int(rng.integers(12, 34))]
        yy, xx = np.ogrid[:n, :n]
        d = np.sqrt(((xx - cx + n / 2) % n - n / 2) ** 2 + ((yy - cy + n / 2) % n - n / 2) ** 2)
        shade *= 1 - 0.07 * np.clip(1 - d / r, 0, 1) * fbm(rng, n, (16, 32))
    shade[:2, :] *= 0.55
    shade[:, :2] *= 0.55
    tex["concrete"] = to_rgb((1.0, 1.0, 0.99), shade)
    # Asphalt: fine dark aggregate with lighter stones.
    shade = 0.88 + 0.1 * fbm(rng, n, (8, 32, 64, 128)) + 0.16 * (rng.random((n, n)) - 0.5)
    stones = rng.random((n, n)) > 0.985
    shade[stones] += 0.35
    tex["asphalt"] = to_rgb((1.0, 1.0, 1.0), shade)
    # Grass: patchy greens with dry spots.
    g = fbm(rng, n, (6, 12, 32, 96))
    blades = 0.12 * (rng.random((n, n)) - 0.5)
    img = np.empty((n, n, 3))
    green = np.array([0.80, 1.0, 0.72])
    dry = np.array([1.05, 1.0, 0.70])
    mix = np.clip((g - 0.45) * 3, 0, 1)[:, :, None]
    img[:] = (green * (1 - mix) + dry * mix) * (0.85 + 0.25 * g[:, :, None] + blades[:, :, None])
    tex["grass"] = (np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8)
    return tex


# ---------------------------------------------------------------------- export

def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(here, "..", "..", "Assets", "GroundCrew", "Art", "Airport"))
    os.makedirs(os.path.join(out_dir, "Textures"), exist_ok=True)
    textures = make_textures()
    for name, rgb in textures.items():
        with open(os.path.join(out_dir, "Textures", name.capitalize() + ".png"), "wb") as f:
            f.write(encode_png(rgb))

    root, spawns = airport.build()
    b = GltfBuilder("GroundCrewVR AirportGenerator")
    tex_index = {name: b.texture(name, rgb, repeat=True) for name, rgb in textures.items()}
    mats = {}
    for name, (rgb, metallic, rough, emissive, texture, _tile) in airport.MATERIALS.items():
        mats[name] = b.material(name, rgb, metallic, rough, emissive, double_sided=name in airport.DOUBLE_SIDED,
                                base_texture=tex_index[texture] if texture else None)
    tris = {}

    def emit(part, parent_pivot):
        prims = []
        for material, buf in part.buffers.items():
            if buf.count == 0:
                continue
            P, N, UV, I = buf.finalize(convert, part.pivot)
            has_uv = airport.MATERIALS[material][4] is not None
            prims.append((mats[material], P, N, UV if has_uv else None, I))
            tris[part.name] = tris.get(part.name, 0) + len(I)
        mesh = b.mesh(part.name, prims) if prims else None
        children = [emit(c, part.pivot) for c in part.children]
        translation = convert(part.pivot)[0] - (convert(parent_pivot)[0] if parent_pivot is not None else 0.0)
        return b.node(part.name, mesh, translation, children)

    size = b.write(os.path.join(out_dir, "Airport.glb"), [emit(root, None)])

    # The moving jet bridge, one model instanced at every gate.
    bb = GltfBuilder("GroundCrewVR AirportGenerator")
    bmats = {}

    def emit_bridge(part, parent_pivot):
        prims = []
        for material, buf in part.buffers.items():
            if buf.count == 0:
                continue
            if material not in bmats:
                rgb, metallic, rough, emissive, _t, _tile = airport.MATERIALS[material]
                bmats[material] = bb.material(material, rgb, metallic, rough, emissive, double_sided=material in airport.DOUBLE_SIDED)
            P, N, UV, I = buf.finalize(convert, part.pivot)
            prims.append((bmats[material], P, N, None, I))
        mesh = bb.mesh(part.name, prims) if prims else None
        children = [emit_bridge(c, part.pivot) for c in part.children]
        translation = convert(part.pivot)[0] - (convert(parent_pivot)[0] if parent_pivot is not None else 0.0)
        return bb.node(part.name, mesh, translation, children)

    bridge = airport.build_jet_bridge_model()
    bsize = bb.write(os.path.join(out_dir, "JetBridge.glb"), [emit_bridge(bridge, None)])
    print(f"JetBridge.glb: {bsize / 1e3:.0f} kB | {bridge.triangle_count():,} tris")
    sidecar = {
        "id": "Airport", "units": "metres",
        "coordinateSystem": "Unity world after glTFast import: +X east, +Y up, +Z north; origin = Stand 24 training area",
        "triangles": sum(tris.values()),
        # Lists (not maps) so Unity's JsonUtility can read them. Colours are sRGB.
        "materials": [{"name": name, "color": list(rgb), "metallic": metallic, "smoothness": round(1 - rough, 3),
                       "emission": list(emissive) if emissive else [],
                       "texture": f"Textures/{texture.capitalize()}.png" if texture else "", "tileMetres": tile or 0.0,
                       # Paint lies on the ground: drawn with a depth offset (GroundCrew/Ground Paint) so it can't flicker.
                       "shader": "paint" if name.startswith("Paint") else "", "doubleSided": name in airport.DOUBLE_SIDED}
                      for name, (rgb, metallic, rough, emissive, texture, tile) in airport.MATERIALS.items()],
        "spawnPoints": spawns,
        "groundNodes": ["Ground_Grass", "Ground_Apron", "Ground_Roads", "Ground_Taxiways", "Ground_Runway"],
        "solidNodes": ["Building_Terminal", "Building_Tower", "Building_Hangar", "Building_Cargo", "Building_FuelFarm",
                       "Building_FireStation", "Floodlight_Masts", "Signs"],
        "jetBridge": {"floorHeight": airport.BRIDGE_FLOOR, "stowYaw": airport.BRIDGE_STOW_YAW, "cabMount": airport.BRIDGE_CAB_MOUNT,
                      "cabLength": airport.BRIDGE_CAB_LENGTH, "columnZ": airport.BRIDGE_COLUMN_Z},
    }
    with open(os.path.join(out_dir, "Airport.json"), "w") as f:
        json.dump(sidecar, f, indent=1)
    print(f"Airport.glb: {size / 1e3:.0f} kB | {sum(tris.values()):,} tris")
    for k, v in sorted(tris.items(), key=lambda kv: -kv[1])[:12]:
        print(f"  {k}: {v:,}")


if __name__ == "__main__":
    main()
