"""Minimal, dependency-free (numpy only) binary glTF 2.0 (.glb) writer.

Supports what the aircraft generator needs: a node hierarchy with translations and
extras, meshes with several primitives (one per material), PBR metallic-roughness
materials with emission, alpha blending and embedded PNG textures, point lights
(KHR_lights_punctual), positions/normals/UVs and 16- or 32-bit indices.
"""
import json
import struct
import zlib

import numpy as np

FLOAT = 5126
U16 = 5123
U32 = 5125
ARRAY_BUFFER = 34962
ELEMENT_ARRAY_BUFFER = 34963


class GltfBuilder:
    def __init__(self, generator="GroundCrewVR AircraftGenerator"):
        self.asset = {"version": "2.0", "generator": generator}
        self.nodes = []
        self.meshes = []
        self.materials = []
        self.accessors = []
        self.buffer_views = []
        self.bin = bytearray()
        self._material_index = {}
        self.images = []
        self.textures = []
        self.samplers = []
        self.lights = []

    # ------------------------------------------------------------------ materials
    def texture(self, name, rgb, repeat=False):
        """Embed an RGB uint8 image [h, w, 3] as PNG; returns the texture index.
        repeat=False clamps (atlases must not bleed across their edges); True tiles."""
        view = self._view(encode_png(rgb), None)
        self.images.append({"name": name, "bufferView": view, "mimeType": "image/png"})
        wrap = 10497 if repeat else 33071
        sampler = next((i for i, smp in enumerate(self.samplers) if smp["wrapS"] == wrap), None)
        if sampler is None:
            # linear + mipmaps
            self.samplers.append({"magFilter": 9729, "minFilter": 9987, "wrapS": wrap, "wrapT": wrap})
            sampler = len(self.samplers) - 1
        self.textures.append({"name": name, "sampler": sampler, "source": len(self.images) - 1})
        return len(self.textures) - 1

    def material(self, name, color, metallic=0.0, roughness=0.5, emissive=None, double_sided=False,
                 base_texture=None, emissive_texture=None, alpha=None):
        """alpha: None = opaque, otherwise blended with this opacity."""
        if name in self._material_index:
            return self._material_index[name]
        rgba = list(color) + [1.0 if alpha is None else alpha] if len(color) == 3 else list(color)
        mat = {
            "name": name,
            "pbrMetallicRoughness": {
                "baseColorFactor": [float(c) for c in rgba],
                "metallicFactor": float(metallic),
                "roughnessFactor": float(roughness),
            },
        }
        if base_texture is not None:
            mat["pbrMetallicRoughness"]["baseColorTexture"] = {"index": base_texture}
        if emissive is not None:
            mat["emissiveFactor"] = [float(c) for c in emissive]
        if emissive_texture is not None:
            mat["emissiveTexture"] = {"index": emissive_texture}
        if alpha is not None:
            mat["alphaMode"] = "BLEND"
        if double_sided:
            mat["doubleSided"] = True
        self.materials.append(mat)
        index = len(self.materials) - 1
        self._material_index[name] = index
        return index

    # ------------------------------------------------------------------ buffers
    def _view(self, data, target):
        while len(self.bin) % 4:
            self.bin.append(0)
        offset = len(self.bin)
        self.bin += data
        view = {"buffer": 0, "byteOffset": offset, "byteLength": len(data)}
        if target is not None:
            view["target"] = target
        self.buffer_views.append(view)
        return len(self.buffer_views) - 1

    def _accessor(self, array, component_type, accessor_type, target, with_bounds=False):
        view = self._view(array.tobytes(), target)
        acc = {
            "bufferView": view,
            "componentType": component_type,
            "count": int(array.shape[0]),
            "type": accessor_type,
        }
        if with_bounds:
            acc["min"] = [float(v) for v in array.min(axis=0)]
            acc["max"] = [float(v) for v in array.max(axis=0)]
        self.accessors.append(acc)
        return len(self.accessors) - 1

    # ------------------------------------------------------------------ meshes / nodes
    def mesh(self, name, primitives):
        """primitives: list of (material_index, positions, normals, uvs_or_None, triangles)."""
        prims = []
        for material, positions, normals, uvs, triangles in primitives:
            if len(triangles) == 0:
                continue
            attributes = {
                "POSITION": self._accessor(np.ascontiguousarray(positions, dtype="<f4"), FLOAT, "VEC3", ARRAY_BUFFER, True),
                "NORMAL": self._accessor(np.ascontiguousarray(normals, dtype="<f4"), FLOAT, "VEC3", ARRAY_BUFFER),
            }
            if uvs is not None:
                attributes["TEXCOORD_0"] = self._accessor(np.ascontiguousarray(uvs, dtype="<f4"), FLOAT, "VEC2", ARRAY_BUFFER)
            flat = np.asarray(triangles).reshape(-1)
            if len(positions) < 65536:
                indices = self._accessor(np.ascontiguousarray(flat, dtype="<u2"), U16, "SCALAR", ELEMENT_ARRAY_BUFFER)
            else:
                indices = self._accessor(np.ascontiguousarray(flat, dtype="<u4"), U32, "SCALAR", ELEMENT_ARRAY_BUFFER)
            prims.append({"attributes": attributes, "indices": indices, "material": material, "mode": 4})
        if not prims:
            return None
        self.meshes.append({"name": name, "primitives": prims})
        return len(self.meshes) - 1

    def point_light(self, name, color, intensity, range_m):
        """KHR_lights_punctual point light (intensity in candela); attach with node(light=...)."""
        self.lights.append({"name": name, "type": "point", "color": [float(c) for c in color],
                            "intensity": float(intensity), "range": float(range_m)})
        return len(self.lights) - 1

    def node(self, name, mesh=None, translation=None, children=None, extras=None, light=None):
        node = {"name": name}
        if mesh is not None:
            node["mesh"] = mesh
        if light is not None:
            node["extensions"] = {"KHR_lights_punctual": {"light": light}}
        if translation is not None and any(abs(float(t)) > 1e-9 for t in translation):
            node["translation"] = [float(t) for t in translation]
        if children:
            node["children"] = list(children)
        if extras:
            node["extras"] = extras
        self.nodes.append(node)
        return len(self.nodes) - 1

    # ------------------------------------------------------------------ output
    def write(self, path, root_nodes, scene_extras=None):
        while len(self.bin) % 4:
            self.bin.append(0)
        scene = {"nodes": list(root_nodes)}
        if scene_extras:
            scene["extras"] = scene_extras
        doc = {
            "asset": self.asset,
            "scene": 0,
            "scenes": [scene],
            "nodes": self.nodes,
            "meshes": self.meshes,
            "materials": self.materials,
            "accessors": self.accessors,
            "bufferViews": self.buffer_views,
            "buffers": [{"byteLength": len(self.bin)}],
        }
        if self.textures:
            doc.update(images=self.images, textures=self.textures, samplers=self.samplers)
        if self.lights:
            doc["extensionsUsed"] = ["KHR_lights_punctual"]
            doc["extensions"] = {"KHR_lights_punctual": {"lights": self.lights}}
        json_bytes = json.dumps(doc, separators=(",", ":")).encode("utf-8")
        while len(json_bytes) % 4:
            json_bytes += b" "
        total = 12 + 8 + len(json_bytes) + 8 + len(self.bin)
        with open(path, "wb") as f:
            f.write(struct.pack("<III", 0x46546C67, 2, total))
            f.write(struct.pack("<II", len(json_bytes), 0x4E4F534A))
            f.write(json_bytes)
            f.write(struct.pack("<II", len(self.bin), 0x004E4942))
            f.write(self.bin)
        return total


def encode_png(rgb):
    """Minimal PNG encoder (8-bit RGB, filter 'up' on every row) using only zlib."""
    rgb = np.ascontiguousarray(rgb, dtype=np.uint8)
    h, w, _ = rgb.shape
    up = rgb.astype(np.int16)
    up[1:] -= rgb[:-1].astype(np.int16)
    rows = np.concatenate([np.full((h, 1), 2, dtype=np.uint8), (up % 256).astype(np.uint8).reshape(h, w * 3)], axis=1)
    rows[0, 0] = 0
    rows[0, 1:] = rgb[0].reshape(-1)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(rows.tobytes(), 9)) + chunk(b"IEND", b""))
